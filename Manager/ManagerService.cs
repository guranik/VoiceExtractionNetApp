using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Tcp.Messages;
using Common.Tcp.Networking;
using Manager.Core;
using Manager.Networking;
using Manager.Scheduling;
using Manager.Processing;
using Common.Tcp.Models;
using System.Collections.Concurrent;
using System.Text;

namespace Manager;

public class ManagerService
{
    private readonly ManagerConfig _config;
    private readonly WorkerListener _listener;
    private readonly ISessionHub _sessionHub;

    private readonly List<WorkerSession> _workers = new();
    private readonly object _workersLock = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    private readonly ConcurrentQueue<(TcpClient client, MessageBase msg)> _incoming = new();

    public ManagerService(ManagerConfig config, ISessionHub sessionHub)
    {
        _config = config;
        _sessionHub = sessionHub;
        _listener = new WorkerListener(config.Network.ManagerPort);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        CleanDirectories(
            _config.Directories.Input,
            _config.Directories.ExtractSegments,
            _config.Directories.TranscribeSegments,
            _config.Directories.Transcriptions,
            _config.Directories.Output
        );

        _listener.Start();

        var tasks = new[]
        {
            AcceptLoop(ct),
            MessageDispatcher(ct),
            HeartbeatMonitor(ct),
            new TaskDispatcher(_workers, _workersLock).RunAsync(ct)
        };

        await Task.WhenAll(tasks);
    }

    // ========== Метод для вызова из HTTP-слоя ==========

    public async Task ProcessSessionAsync(SessionState session, CancellationToken ct)
    {
        var retryCount = 0;

        if (string.IsNullOrEmpty(session.InputFilePath) || !File.Exists(session.InputFilePath))
            return;

        try
        {
            // 1. Сплиттинг
            AudioSplitter.Split(
                session.InputFilePath,
                _config.Directories.ExtractSegments,
                _config.AudioSplitter.MaxExtractSegmentDurationSec,
                _config.AudioSplitter.ExtractTranscribeEfficiency,
                session.SessionId);

            UpdateSessionProgress(session);

            // 2. Постановка задач в очередь Extract
            foreach (var seg in Directory.GetFiles(_config.Directories.ExtractSegments, $"{session.SessionId}_*"))
            {
                var fileName = Path.GetFileName(seg);
                var base64Content = Convert.ToBase64String(File.ReadAllBytes(seg));

                TaskQueues.ExtractQueue.Enqueue(new TaskMessage
                {
                    TaskType = TaskType.Extract,
                    SourceFileName = fileName,
                    SessionId = session.SessionId,
                    Files = { new FilePayload { FileName = fileName, Base64Content = base64Content } }
                });
            }

            // 3. Ожидание завершения
            while (!CanFinalize(session.SessionId) && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                UpdateSessionProgress(session);
            }

            // 4. Финализация
            if (!ct.IsCancellationRequested)
                await FinalizeSessionAsync(session);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Retry {retryCount} for session {session.SessionId}: {ex.Message}");
            retryCount++;
            await Task.Delay(1000 * retryCount, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Обработка сессии {session.SessionId}: {ex.Message}");
            _sessionHub.RemoveSession(session.SessionId);
        }
    }

    public void UpdateSessionProgress(SessionState session)
    {
        var inputFile = session.InputFilePath ??
            Directory.GetFiles(_config.Directories.Input, $"{session.SessionId}.*").FirstOrDefault();

        var latestExtract = inputFile != null
            ? AudioSplitter.GetLatestExtractSegmentStartSec(
                _config.Directories.ExtractSegments, inputFile, session.SessionId)
            : 0;

        var duration = inputFile != null
            ? AudioSplitter.GetInputDurationSec(inputFile)
            : 0;

        var latestTranscriptionEnd =
            AudioSplitter.GetLatestTranscriptionEndSec(
                _config.Directories.Transcriptions, session.SessionId);

        _sessionHub.UpdateProgress(session.SessionId, latestExtract, duration, latestTranscriptionEnd);
    }

    // ========== TCP-логика для воркеров (без изменений) ==========

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptAsync(ct);
            _ = Task.Run(() => WorkerReadLoop(client, ct), ct);
        }
    }

    private async Task WorkerReadLoop(TcpClient client, CancellationToken ct)
    {
        var reader = new TcpMessageReader(client);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var readTask = reader.ReadAsync(ct);

                var completed = await Task.WhenAny(
                    readTask,
                    Task.Delay(TimeSpan.FromSeconds(30), ct));

                if (completed != readTask)
                    throw new TimeoutException("Worker read timeout");

                var msg = await readTask;

                if (msg == null)
                    throw new IOException("Worker disconnected");

                _incoming.Enqueue((client, msg));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Worker read loop terminated: {ex.Message}");

            var worker = GetWorker(client);

            if (worker != null)
                worker.Disconnect();

            try
            {
                client.Close();
            }
            catch { }
        }
    }

    private async Task MessageDispatcher(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            while (_incoming.TryDequeue(out var item))
                await HandleIncoming(item.client, item.msg, ct);
            await Task.Delay(5, ct);
        }
    }

    private async Task HandleIncoming(TcpClient client, MessageBase msg, CancellationToken ct)
    {
        switch (msg)
        {
            case WorkerReadyMessage hello:
                await RegisterWorker(client, hello, ct);
                break;
            case HeartBeatMessage:
                GetWorker(client)?.Info.UpdateHeartbeat();
                break;
            case TaskMessage task:
                await HandleWorkerTask(task, client, ct);
                break;
        }
    }

    private async Task HandleWorkerTask(TaskMessage task, TcpClient client, CancellationToken ct)
    {
        var worker = GetWorker(client);
        if (worker == null) return;

        var sessionId = task.SessionId;
        if (!_sessionHub.TryGetSession(sessionId, out var session)) return;

        if (task.TaskType == TaskType.Extract)
        {
            worker.Info.IncExtract();
            worker.Info.ActiveExtract.TryRemove(
                task.SourceFileName,
                out _);
            foreach (var f in task.Files)
            {
                var targetPath = Path.Combine(_config.Directories.TranscribeSegments, f.FileName);
                File.WriteAllBytes(targetPath, Convert.FromBase64String(f.Base64Content));

                TaskQueues.TranscribeQueue.Enqueue(new TaskMessage
                {
                    TaskType = TaskType.Transcribe,
                    SourceFileName = f.FileName,
                    SessionId = sessionId,
                    Files = { f }
                });
            }

            File.Delete(Path.Combine(_config.Directories.ExtractSegments, task.SourceFileName));
            UpdateSessionProgress(session);
        }
        else // Transcribe
        {
            worker.Info.IncTranscribe();
            worker.Info.ActiveTranscribe.TryRemove(
                task.SourceFileName,
                out _);

            foreach (var f in task.Files)
            {
                var targetPath = Path.Combine(_config.Directories.Transcriptions, f.FileName);
                if (Path.GetExtension(f.FileName).ToLower() == ".wav")
                    targetPath = Path.ChangeExtension(targetPath, ".txt");

                File.WriteAllBytes(targetPath, Convert.FromBase64String(f.Base64Content));
            }

            var sourceWave = Path.Combine(_config.Directories.TranscribeSegments, task.SourceFileName);
            if (File.Exists(sourceWave)) File.Delete(sourceWave);

            UpdateSessionProgress(session);

            if (!session.IsFinalized && CanFinalize(sessionId))
            {
                await FinalizeSessionAsync(session);
            }
        }
    }

    private bool CanFinalize(string sessionId)
    {
        return
            !Directory.GetFiles(_config.Directories.ExtractSegments, $"{sessionId}_*").Any() &&
            !Directory.GetFiles(_config.Directories.TranscribeSegments, $"{sessionId}_*").Any() &&
            TaskQueues.IsEmpty(sessionId);
    }

    private async Task FinalizeSessionAsync(SessionState session)
    {
        var semaphore = _sessionLocks.GetOrAdd(session.SessionId, _ => new SemaphoreSlim(1, 1));
        bool acquired = false;

        try
        {
            acquired = await semaphore.WaitAsync(TimeSpan.FromSeconds(5));
            if (!acquired)
            {
                Console.WriteLine($"[WARN] Finalization for session {session.SessionId} is already running or timed out.");
                return;
            }

            var outputPath = Path.Combine(_config.Directories.Output, $"{session.SessionId}.txt");

            var transcriptionFiles = Directory.GetFiles(_config.Directories.Transcriptions, $"{session.SessionId}_*")
                                              .OrderBy(f => f)
                                              .ToList();

            if (transcriptionFiles.Count == 0)
            {
                Console.WriteLine($"[WARN] No transcription files found for session {session.SessionId}");
                return;
            }

            Console.WriteLine($"[FINALIZE] Processing {transcriptionFiles.Count} files for session {session.SessionId}");

            bool writeSuccess = false;
            const int maxRetries = 3;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                if (attempt > 0) await Task.Delay(500 * attempt);

                try
                {
                    await using var sw = new StreamWriter(outputPath, false, Encoding.UTF8);

                    foreach (var file in transcriptionFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var timeCodePart = fileName.Length > session.SessionId.Length + 1
                            ? fileName.Substring(session.SessionId.Length + 1)
                            : "unknown";

                        var content = await File.ReadAllTextAsync(file);
                        await sw.WriteLineAsync($"[{timeCodePart}]: {content}");
                    }

                    await sw.FlushAsync();
                    writeSuccess = true;
                    break;
                }
                catch (IOException ex) when (ex.Message.Contains("being used by another process"))
                {
                    Console.WriteLine($"[RETRY] File locked. Attempt {attempt + 1}/{maxRetries}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Critical write error: {ex.Message}");
                    throw;
                }
            }

            if (!writeSuccess || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new IOException($"Output file for session {session.SessionId} was not created or is empty after {maxRetries} attempts.");
            }

            Console.WriteLine($"[SUCCESS] Output file created: {outputPath} ({new FileInfo(outputPath).Length} bytes)");

            session.ResultFilePath = outputPath;
            session.ResultFileName = Path.GetFileNameWithoutExtension(session.ClientFileName) + ".txt";
            _sessionHub.MarkFinalized(session.SessionId, outputPath);

            CleanSessionDirectories(session.SessionId);
        }
        finally
        {
            if (acquired)
            {
                semaphore.Release();
                if (_sessionLocks.TryGetValue(session.SessionId, out var s) && s.CurrentCount == 1)
                    _sessionLocks.TryRemove(session.SessionId, out _);
            }
        }
    }

    private async Task RegisterWorker(
    TcpClient client,
    WorkerReadyMessage hello,
    CancellationToken ct)
    {
        if (GetWorker(client) != null)
            return;

        var session = new WorkerSession(
            client,
            new WorkerInfo(
                client,
                hello.ExtractThreads,
                hello.TranscribeThreads));

        lock (_workersLock)
        {
            _workers.Add(session);
        }

        try
        {
            await session.SendAsync(new AckMessage(), ct);
        }
        catch
        {
            session.Disconnect();

            lock (_workersLock)
            {
                _workers.Remove(session);
            }
        }
    }

    private WorkerSession? GetWorker(TcpClient client)
    {
        lock (_workersLock)
            return _workers.FirstOrDefault(w => w.Info.Client == client);
    }

    private async Task HeartbeatMonitor(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                List<WorkerSession> dead;

                lock (_workersLock)
                {
                    dead = _workers
                        .Where(w =>
                            w.IsDead ||
                            (now - w.Info.LastHeartbeatUtc).TotalSeconds >
                            _config.Heartbeat.WorkerTimeoutSec)
                        .ToList();
                }

                foreach (var w in dead)
                {
                    Console.WriteLine("[WARN] Worker considered dead.");

                    w.Disconnect();

                    foreach (var t in w.Info.ActiveExtract.Values)
                        TaskQueues.EnqueueExtract(t);

                    foreach (var t in w.Info.ActiveTranscribe.Values)
                        TaskQueues.EnqueueTranscribe(t);

                    w.Info.ResetState();

                    lock (_workersLock)
                    {
                        _workers.Remove(w);
                    }

                    w.Info.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Heartbeat monitor: {ex.Message}");
            }

            await Task.Delay(
                _config.Heartbeat.MonitorIntervalMs,
                ct);
        }
    }

    private void CleanDirectories(params string[] dirs)
    {
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir))
                try { File.Delete(file); } catch { }
        }
    }

    private void CleanSessionDirectories(string sessionId)
    {
        var dirs = new[]
        {
            _config.Directories.Input,
            _config.Directories.ExtractSegments,
            _config.Directories.TranscribeSegments,
            _config.Directories.Transcriptions
        };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, $"{sessionId}_*"))
                try { File.Delete(file); } catch { }
        }
    }
}
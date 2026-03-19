using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Messages;
using Common.Models;
using Common.Utils;
using Common.Networking;
using Manager.Networking;
using Manager.Scheduling;
using Manager.Processing;

namespace Manager;

public class ManagerService
{
    private readonly ManagerConfig _config;
    private readonly WorkerListener _listener;
    
    private readonly ConcurrentDictionary<string, ClientSession> _clients = new();
    private readonly List<WorkerSession> _workers = new();
    private readonly object _workersLock = new();
    
    private readonly ConcurrentQueue<(TcpClient client, MessageBase msg)> _incoming = new();

    public ManagerService(ManagerConfig config)
    {
        _config = config;
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
            new TaskDispatcher(_workers).RunAsync(ct)
        };

        await Task.WhenAll(tasks);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptAsync(ct);
            _ = Task.Run(() => ClientReadLoop(client, ct), ct);
        }
    }

    private async Task ClientReadLoop(TcpClient client, CancellationToken ct)
    {
        var reader = new TcpMessageReader(client);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await reader.ReadAsync(ct);
                if (msg != null)
                    _incoming.Enqueue((client, msg));
            }
        }
        catch
        {
            client.Close();
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

    private ClientSession? GetClientSession(TcpClient client)
    {
        return _clients.Values.FirstOrDefault(c => c.Client == client);
    }

    private async Task HandleIncoming(
        TcpClient client,
        MessageBase msg,
        CancellationToken ct)
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

            case ClientFileMessage input:
                await HandleClientInput(input, client, ct);
                break;
        }
    }

    private async Task HandleClientInput(ClientFileMessage msg, TcpClient client, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..16];
        var session = new ClientSession(client, sessionId, msg.File.FileName);
        _clients[sessionId] = session;

        var inputPath = Path.Combine(
            _config.Directories.Input,
            $"{sessionId}.wav");

        Base64FileHelper.WriteBase64ToFile(inputPath, msg.File.Base64Content);

        AudioSplitter.Split(
            inputPath,
            _config.Directories.ExtractSegments,
            _config.AudioSplitter.MaxExtractSegmentDurationSec,
            _config.AudioSplitter.ExtractTranscribeEfficiency,
            sessionId);

        foreach (var seg in Directory.GetFiles(_config.Directories.ExtractSegments, $"{sessionId}_*"))
        {
            var fileName = Path.GetFileName(seg);

            TaskQueues.ExtractQueue.Enqueue(new TaskMessage
            {
                TaskType = TaskType.Extract,
                SourceFileName = fileName,
                SessionId = sessionId,
                Files =
                {
                    new FilePayload
                    {
                        FileName = fileName,
                        Base64Content = Base64FileHelper.ReadFileAsBase64(seg)
                    }
                }
            });
        }

        await SendClientProgressAsync(session, ct);
    }

    private async Task HandleWorkerTask(TaskMessage task, TcpClient client, CancellationToken ct)
    {
        var worker = GetWorker(client);
        if (worker == null) return;

        var sessionId = task.SessionId;
        if (!_clients.ContainsKey(sessionId)) return;

        if (task.TaskType == TaskType.Extract)
        {
            worker.Info.IncExtract();
            worker.Info.ActiveExtract.RemoveAll(t =>
                t.SourceFileName == task.SourceFileName);

            foreach (var f in task.Files)
            {
                Base64FileHelper.WriteBase64ToFile(
                    Path.Combine(_config.Directories.TranscribeSegments, f.FileName),
                    f.Base64Content);

                TaskQueues.TranscribeQueue.Enqueue(new TaskMessage
                {
                    TaskType = TaskType.Transcribe,
                    SourceFileName = f.FileName,
                    SessionId = sessionId,
                    Files = { f }
                });
            }

            File.Delete(Path.Combine(
                _config.Directories.ExtractSegments,
                task.SourceFileName));

            var session = _clients[sessionId];
            await SendClientProgressAsync(session, ct);
        }
        else // Transcribe
        {
            worker.Info.IncTranscribe();
            worker.Info.ActiveTranscribe.RemoveAll(t =>
                t.SourceFileName == task.SourceFileName);

            foreach (var f in task.Files)
            {
                Base64FileHelper.WriteBase64ToFile(
                    Path.Combine(_config.Directories.Transcriptions, f.FileName),
                    f.Base64Content);
            }

            File.Delete(Path.Combine(
                _config.Directories.TranscribeSegments,
                Path.ChangeExtension(task.SourceFileName, ".wav")));

            var session = _clients[sessionId];
            await SendClientProgressAsync(session, ct);

            if (!session.IsFinalized && CanFinalize(sessionId))
            {
                session.IsFinalized = await FinalizeAsync(session, ct);
            }
        }
    }

    private async Task SendClientProgressAsync(ClientSession session, CancellationToken ct)
    {
        if (session.Client == null || !session.Client.Connected)
            return;

        var writer = new TcpMessageWriter(session.Client);

        var inputFile = Directory
            .GetFiles(_config.Directories.Input, $"{session.SessionId}.*")
            .FirstOrDefault();

        var latestExtract = inputFile != null
            ? AudioSplitter.GetLatestExtractSegmentStartSec(
                _config.Directories.ExtractSegments,
                inputFile,
                session.SessionId)
            : 0;

        var duration = inputFile != null
            ? AudioSplitter.GetInputDurationSec(inputFile)
            : 0;

        var latestTranscriptionEnd =
            AudioSplitter.GetLatestTranscriptionEndSec(
                _config.Directories.Transcriptions,
                session.SessionId);

        var msg = new ClientProgressMessage
        {
            EarliestExtractSegmentStart = latestExtract,
            InputFileDuration = duration,
            LatestTranscriptionEnd = latestTranscriptionEnd
        };

        await writer.SendAsync(msg, ct);
    }

    private bool CanFinalize(string sessionId)
    {
        return
            !Directory.GetFiles(_config.Directories.ExtractSegments, $"{sessionId}_*").Any() &&
            !Directory.GetFiles(_config.Directories.TranscribeSegments, $"{sessionId}_*").Any() &&
            TaskQueues.IsEmpty(sessionId);
    }

    private async Task<bool> FinalizeAsync(ClientSession session, CancellationToken ct)
    {
        if (session.Client == null || !session.Client.Connected)
            return false;

        await SendClientProgressAsync(session, ct);

        var outputPath = Path.Combine(
            _config.Directories.Output,
            $"{session.SessionId}.txt");

        using (var sw = new StreamWriter(outputPath))
        {
            foreach (var file in Directory
                .GetFiles(_config.Directories.Transcriptions, $"{session.SessionId}_*")
                .OrderBy(f => f))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                var timeCodePart = fileName.Substring(session.SessionId.Length + 1);
                
                await sw.WriteLineAsync(
                    $"[{timeCodePart}]: {File.ReadAllText(file)}");
            }
        }

        var msg = new ClientFileMessage
        {
            File = new FilePayload
            {
                FileName = session.ClientFileName,
                Base64Content = Base64FileHelper.ReadFileAsBase64(outputPath)
            }
        };

        await new TcpMessageWriter(session.Client)
            .SendAsync(msg, ct);

        try
        {
            CleanSessionDirectories(session.SessionId);
            File.Delete(outputPath);
        }
        catch { }

        Console.WriteLine($"Finalization completed for session {session.SessionId}");

        _clients.TryRemove(session.SessionId, out _);
        return true;
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
            _workers.Add(session);

        await new TcpMessageWriter(client)
            .SendAsync(new AckMessage(), ct);
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
            var now = DateTime.UtcNow;

            lock (_workersLock)
            {
                var dead = _workers
                    .Where(w => (now - w.Info.LastHeartbeatUtc)
                        .TotalSeconds > _config.Heartbeat.WorkerTimeoutSec)
                    .ToList();

                foreach (var w in dead)
                {
                    foreach (var t in w.Info.ActiveExtract)
                        TaskQueues.ExtractQueue.Enqueue(t);

                    foreach (var t in w.Info.ActiveTranscribe)
                        TaskQueues.TranscribeQueue.Enqueue(t);

                    w.Info.Dispose();
                    _workers.Remove(w);
                }
            }

            await Task.Delay(_config.Heartbeat.MonitorIntervalMs, ct);
        }
    }

    private void CleanDirectories(params string[] dirs)
    {
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.GetFiles(dir))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private void CleanSessionDirectories(string sessionId)
    {
        CleanDirectories(
            _config.Directories.Input,
            _config.Directories.ExtractSegments,
            _config.Directories.TranscribeSegments,
            _config.Directories.Transcriptions
        );
    }
}
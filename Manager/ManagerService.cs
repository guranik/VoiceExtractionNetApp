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
    private readonly object _clientLock = new();
    private TcpClient? _currentClient;

    private readonly List<WorkerSession> _workers = new();
    private readonly ConcurrentQueue<(TcpClient client, MessageBase msg)> _incoming = new();

    private bool _finalized = true;

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
            _config.Directories.Transcriptions
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

    private TcpClient? GetCurrentClient()
    {
        lock (_clientLock)
            return _currentClient;
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
                await HandleWorkerTask(task, client);
                break;

            case ClientFileMessage input:
                _currentClient = client;
                HandleClientInput(input);
                break;
        }
    }

    private void HandleClientInput(ClientFileMessage msg)
    {
        var inputPath = Path.Combine(
            _config.Directories.Input,
            msg.File.FileName);

        Base64FileHelper.WriteBase64ToFile(
            inputPath,
            msg.File.Base64Content);

        AudioSplitter.Split(
            inputPath,
            _config.Directories.ExtractSegments,
            _config.AudioSplitter.MaxExtractSegmentDurationSec,
            _config.AudioSplitter.ExtractTranscribeEfficiency);

        foreach (var seg in Directory.GetFiles(_config.Directories.ExtractSegments))
        {
            var fileName = Path.GetFileName(seg);

            TaskQueues.ExtractQueue.Enqueue(new TaskMessage
            {
                TaskType = TaskType.Extract,
                SourceFileName = fileName,
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

        _finalized = false;
        SendClientProgress();
    }

    private async Task HandleWorkerTask(TaskMessage task, TcpClient client)
    {
        var worker = _workers.First(w => w.Info.Client == client);

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
                    Files = { f }
                });
            }

            File.Delete(Path.Combine(
                _config.Directories.ExtractSegments,
                task.SourceFileName));

            SendClientProgress();
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

            SendClientProgress();

            if (!_finalized && CanFinalize())
            {
                _finalized = await FinalizeAsync();
            }
        }
    }

    private void SendClientProgress()
    {
        if (_currentClient == null || !_currentClient.Connected)
            return;

        var writer = new TcpMessageWriter(_currentClient);

        var inputFile = Directory
            .GetFiles(_config.Directories.Input)
            .FirstOrDefault();

        var latestExtract = inputFile != null
            ? AudioSplitter.GetLatestExtractSegmentStartSec(
                _config.Directories.ExtractSegments,
                inputFile)
            : 0;

        var duration = inputFile != null
            ? AudioSplitter.GetInputDurationSec(inputFile)
            : 0;

        var latestTranscriptionEnd =
            AudioSplitter.GetLatestTranscriptionEndSec(
                _config.Directories.Transcriptions);

        var msg = new ClientProgressMessage
        {
            EarliestExtractSegmentStart = latestExtract,
            InputFileDuration = duration,
            LatestTranscriptionEnd = latestTranscriptionEnd
        };

        _ = writer.SendAsync(msg, CancellationToken.None);
    }

    private bool CanFinalize()
    {
        return
            !Directory.GetFiles(_config.Directories.ExtractSegments).Any() &&
            !Directory.GetFiles(_config.Directories.TranscribeSegments).Any() &&
            TaskQueues.ExtractQueue.IsEmpty &&
            TaskQueues.TranscribeQueue.IsEmpty;
    }

    private async Task<bool> FinalizeAsync()
    {
        var client = GetCurrentClient();
        if (client == null || !client.Connected)
            return false;
        var inputFile = Directory.GetFiles(_config.Directories.Input).FirstOrDefault();
        if (inputFile == null)
            return false;

        SendClientProgress();

        var inputName = Path.GetFileNameWithoutExtension(inputFile);
        var outputPath = Path.Combine(
            _config.Directories.Output,
            inputName + ".txt");

        using (var sw = new StreamWriter(outputPath))
        {
            foreach (var file in Directory
                .GetFiles(_config.Directories.Transcriptions)
                .OrderBy(f => f))
            {
                await sw.WriteLineAsync(
                    $"[{Path.GetFileName(file)}]: {File.ReadAllText(file)}");
            }
        }

        var msg = new ClientFileMessage
        {
            File = new FilePayload
            {
                FileName = Path.GetFileName(outputPath),
                Base64Content = Base64FileHelper.ReadFileAsBase64(outputPath)
            }
        };

        await new TcpMessageWriter(client)
            .SendAsync(msg, CancellationToken.None);

        try
        {
            File.Delete(inputFile);
            File.Delete(outputPath);
        }
        catch { }

        Console.WriteLine("Finalization completed and sent to client");

        ResetManagerState();
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

        lock (_workers)
            _workers.Add(session);

        await new TcpMessageWriter(client)
            .SendAsync(new AckMessage(), ct);
    }

    private WorkerSession? GetWorker(TcpClient client)
    {
        lock (_workers)
            return _workers.FirstOrDefault(w => w.Info.Client == client);
    }

    private async Task HeartbeatMonitor(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            lock (_workers)
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

    private static void CleanDirectories(params string[] dirs)
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

    private void ResetManagerState()
    {
        CleanDirectories(
            _config.Directories.Input,
            _config.Directories.ExtractSegments,
            _config.Directories.TranscribeSegments,
            _config.Directories.Transcriptions,
            _config.Directories.Output
        );

        TaskQueues.ClearAll();

        lock (_workers)
        {
            foreach (var w in _workers)
            {
                w.Info.ResetState();
            }
        }

        _currentClient = null;
        _finalized = true;

        Console.WriteLine("Manager state fully reset and ready for next file");
    }
}

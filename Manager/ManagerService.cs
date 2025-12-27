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

class ManagerService
{
    private readonly ManagerConfig _config;
    private readonly WorkerListener _listener;

    private readonly List<WorkerSession> _workers = new();
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

    private async Task HandleIncoming(
        TcpClient client,
        MessageBase msg,
        CancellationToken ct)
    {
        switch (msg)
        {
            case WorkerHelloMessage hello:
                await RegisterWorker(client, hello, ct);
                break;

            case HeartBeatMessage:
                GetWorker(client)?.Info.UpdateHeartbeat();
                break;

            case TaskMessage task:
                await HandleWorkerTask(task, client);
                break;

            case ClientInputMessage input:
                await HandleClientInputAsync(input);
                break;
        }
    }

    private async Task HandleClientInputAsync(ClientInputMessage msg)
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
    }

    private async Task HandleWorkerTask(TaskMessage task, TcpClient client)
    {
        var worker = _workers.First(w => w.Info.Client == client);

        if (task.TaskType == TaskType.Extract)
        {
            worker.Info.IncExtract();

            foreach (var f in task.Files)
            {
                var segmentPath = Path.Combine(
                    _config.Directories.TranscribeSegments,
                    f.FileName);

                Base64FileHelper.WriteBase64ToFile(segmentPath, f.Base64Content);

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
        }
        else
        {
            worker.Info.IncTranscribe();

            foreach (var f in task.Files)
            {
                Base64FileHelper.WriteBase64ToFile(
                    Path.Combine(_config.Directories.Transcriptions, f.FileName),
                    f.Base64Content);
            }

            File.Delete(Path.Combine(
                _config.Directories.TranscribeSegments,
                Path.ChangeExtension(task.SourceFileName, ".wav")));
        }
    }

    private async Task RegisterWorker(
        TcpClient client,
        WorkerHelloMessage hello,
        CancellationToken ct)
    {
        if (GetWorker(client) != null)
            return;

        var info = new WorkerInfo(
            client,
            hello.ExtractThreads,
            hello.TranscribeThreads);

        var session = new WorkerSession(client, info);

        lock (_workers)
            _workers.Add(session);

        var writer = new TcpMessageWriter(client);
        await writer.SendAsync(new AckMessage
        {
            AckedMessageId = hello.MessageId
        }, ct);
    }

    private WorkerSession GetWorker(TcpClient client)
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
                    w.Info.Dispose();
                    _workers.Remove(w);
                }
            }

            await Task.Delay(
                _config.Heartbeat.MonitorIntervalMs,
                ct);
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
}

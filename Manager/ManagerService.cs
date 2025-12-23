using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Messages;
using Common.Models;
using Common.Utils;

class ManagerService
{
    private const int Port = 5000;

    private readonly List<WorkerSession> _workers = new();
    private readonly WorkerListener _listener = new(Port);

    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();

        _ = Task.Run(() => AcceptLoop(ct), ct);
        _ = Task.Run(() => HeartbeatMonitor(ct), ct);

        var dispatcher = new TaskDispatcher(_workers);
        await dispatcher.RunAsync(ct);

        await InputLoop(ct);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptAsync(ct);
            _ = Task.Run(() => HandleWorker(client, ct), ct);
        }
    }

    private async Task HandleWorker(TcpClient client, CancellationToken ct)
    {
        var reader = new Common.Networking.TcpMessageReader(client);
        var writer = new Common.Networking.TcpMessageWriter(client);

        var hello = (WorkerHelloMessage)await reader.ReadAsync(ct);

        var info = new WorkerInfo(client, hello.ExtractThreads, hello.TranscribeThreads);
        var session = new WorkerSession(client, info);

        lock (_workers)
            _workers.Add(session);

        await writer.SendAsync(new AckMessage
        {
            AckedMessageId = hello.MessageId
        }, ct);

        while (!ct.IsCancellationRequested)
        {
            var msg = await session.ReadAsync(ct);
            await HandleWorkerMessage(session, msg);
        }
    }

    private async Task HandleWorkerMessage(WorkerSession session, MessageBase msg)
    {
        switch (msg)
        {
            case HeartBeatMessage:
                session.Info.UpdateHeartbeat();
                break;

            case TaskMessage task:
                if (task.TaskType == TaskType.Extract)
                {
                    session.Info.IncExtract();

                    foreach (var f in task.Files)
                        Base64FileHelper.WriteBase64ToFile(
                            Path.Combine("transcribe_segments", f.FileName),
                            f.Base64Content);

                    TaskQueues.TranscribeQueue.Enqueue(task);
                }
                else
                {
                    session.Info.IncTranscribe();

                    foreach (var f in task.Files)
                        Base64FileHelper.WriteBase64ToFile(
                            Path.Combine("transcriptions", f.FileName),
                            f.Base64Content);

                    File.Delete(Path.Combine("transcribe_segments",
                        Path.ChangeExtension(task.SourceFileName, ".wav")));
                }
                break;
        }
    }

    private async Task HeartbeatMonitor(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            lock (_workers)
            {
                var dead = _workers
                    .Where(w => (now - w.Info.LastHeartbeatUtc).TotalSeconds > 15)
                    .ToList();

                foreach (var d in dead)
                {
                    d.Info.Dispose();
                    _workers.Remove(d);
                }
            }

            await Task.Delay(3000, ct);
        }
    }

    private async Task InputLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var file in Directory.GetFiles("input", "*.wav"))
            {
                var name = Path.GetFileName(file);

                AudioSplitter.Split(
                    file,
                    "extract_segments",
                    30,
                    0.8);

                foreach (var seg in Directory.GetFiles("extract_segments"))
                {
                    TaskQueues.ExtractQueue.Enqueue(new TaskMessage
                    {
                        TaskType = TaskType.Extract,
                        SourceFileName = Path.GetFileName(seg),
                        Files = new List<FilePayload>
                        {
                            new()
                            {
                                FileName = Path.GetFileName(seg),
                                Base64Content = Base64FileHelper.ReadFileAsBase64(seg)
                            }
                        }
                    });
                }

                File.Delete(file);
            }

            await Task.Delay(1000, ct);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Messages;
using Common.Models;
using Common.Utils;

class WorkerService
{
    private const string ManagerIp = "127.0.0.1";
    private const int ManagerPort = 5000;

    private readonly WorkerState _state = new();
    private ManagerConnection _connection;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await InitializePythonWorkersAsync(ct);
                await ConnectAndServeAsync(ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Worker error: {ex}");
                Cleanup();
                await Task.Delay(3000, ct);
            }
        }
    }

    private async Task InitializePythonWorkersAsync(CancellationToken ct)
    {
        int extract = 1;
        int transcribe = 3;

        string root = Directory.GetCurrentDirectory();

        for (int i = 0; i < extract; i++)
        {
            _state.ExtractWorkers.Add(new PythonWorker(
                @"C:\Projects\VoiceExtraction\speech_extractor.py",
                Path.Combine(root, "extract_segments"),
                Path.Combine(root, "transcribe_segments"),
                i));
        }

        for (int i = 0; i < transcribe; i++)
        {
            _state.TranscribeWorkers.Add(new PythonWorker(
                @"C:\Projects\VoiceExtraction\speech_transcriptor.py",
                Path.Combine(root, "transcribe_segments"),
                Path.Combine(root, "transcriptions"),
                i));
        }

        await Task.WhenAll(
            _state.ExtractWorkers.Select(w => w.Ready)
                .Concat(_state.TranscribeWorkers.Select(w => w.Ready))
        );
    }

    private async Task ConnectAndServeAsync(CancellationToken ct)
    {
        _connection = new ManagerConnection();
        await _connection.ConnectAsync(ManagerIp, ManagerPort, ct);

        await _connection.SendAsync(new WorkerHelloMessage
        {
            ExtractThreads = _state.ExtractWorkers.Count,
            TranscribeThreads = _state.TranscribeWorkers.Count
        }, ct);

        _ = Task.Run(() => HeartbeatLoop(ct), ct);

        while (!ct.IsCancellationRequested)
        {
            var msg = await _connection.ReadAsync(ct);
            if (msg != null)
                await HandleMessageAsync(msg, ct);
        }
    }

    private async Task HandleMessageAsync(MessageBase msg, CancellationToken ct)
    {
        switch (msg)
        {
            case TaskMessage task:
                if (task.TaskType == TaskType.Extract)
                    await HandleExtractTaskAsync(task, ct);
                else
                    await HandleTranscribeTaskAsync(task, ct);
                break;
        }
    }

    private async Task HandleExtractTaskAsync(TaskMessage task, CancellationToken ct)
    {
        var worker = _state.ExtractWorkers.First(w => w.IsFree);
        worker.IsFree = false;

        foreach (var file in task.Files)
            Base64FileHelper.WriteBase64ToFile(
                Path.Combine("extract_segments", file.FileName),
                file.Base64Content);

        var produced = await worker.SendTask(task.SourceFileName);

        var payload = produced.Select(f =>
        {
            var path = Path.Combine("transcribe_segments", $"{worker.ThreadIndex}_{f}");
            return new FilePayload
            {
                FileName = f,
                Base64Content = Base64FileHelper.ReadFileAsBase64(path)
            };
        }).ToList();

        await _connection.SendAsync(new TaskMessage
        {
            TaskType = TaskType.Extract,
            SourceFileName = task.SourceFileName,
            Files = payload
        }, ct);
    }

    private async Task HandleTranscribeTaskAsync(TaskMessage task, CancellationToken ct)
    {
        var worker = _state.TranscribeWorkers.First(w => w.IsFree);
        worker.IsFree = false;

        foreach (var file in task.Files)
            Base64FileHelper.WriteBase64ToFile(
                Path.Combine("transcribe_segments", file.FileName),
                file.Base64Content);

        var produced = await worker.SendTask(task.SourceFileName);

        var payload = produced.Select(f =>
        {
            var path = Path.Combine("transcriptions", $"{worker.ThreadIndex}_{f}");
            return new FilePayload
            {
                FileName = f,
                Base64Content = Base64FileHelper.ReadFileAsBase64(path)
            };
        }).ToList();

        await _connection.SendAsync(new TaskMessage
        {
            TaskType = TaskType.Transcribe,
            SourceFileName = task.SourceFileName,
            Files = payload
        }, ct);
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            await _connection.SendAsync(new HeartBeatMessage(), ct);
        }
    }

    private void Cleanup()
    {
        try
        {
            Directory.Delete("extract_segments", true);
            Directory.Delete("transcribe_segments", true);
            Directory.Delete("transcriptions", true);
        }
        catch { }

        DirectoryValidator.ValidateWorkerEnvironment();
        _connection?.Dispose();
        _state.DisposeAll();
    }
}

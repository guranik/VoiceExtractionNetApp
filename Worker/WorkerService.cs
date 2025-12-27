using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Common.Messages;
using Common.Models;
using Common.Utils;

class WorkerService
{
    private const string ManagerIp = "127.0.0.1";
    private const int ManagerPort = 5000;

    private readonly WorkerState _state = new();
    private ManagerConnection _connection = null!;
    private readonly Channel<TaskMessage> _taskQueue =
        Channel.CreateUnbounded<TaskMessage>();

    public async Task RunAsync(CancellationToken ct)
    {
        CleanWorkingDirectories();
        await InitializeWorkersAsync(ct);
        await ConnectAsync(ct);

        _ = Task.Run(() => HeartbeatLoop(ct), ct);
        _ = Task.Run(() => DispatcherLoop(ct), ct);

        while (!ct.IsCancellationRequested)
        {
            var msg = await _connection.ReadAsync(ct);
            if (msg is TaskMessage task)
                await _taskQueue.Writer.WriteAsync(task, ct);
        }
    }

    private async Task InitializeWorkersAsync(CancellationToken ct)
    {
        const int extract = 1;
        const int transcribe = 3;

        var extractPool = Channel.CreateBounded<PythonWorker>(extract);
        var transcribePool = Channel.CreateBounded<PythonWorker>(transcribe);

        string root = Directory.GetCurrentDirectory();

        for (int i = 0; i < extract; i++)
        {
            var w = new PythonWorker(
                @"C:\Projects\VoiceExtraction\speech_extractor.py",
                "extract_segments",
                "transcribe_segments",
                i);

            _state.AllWorkers.Add(w);
            await w.Ready;
            await extractPool.Writer.WriteAsync(w, ct);
        }

        for (int i = 0; i < transcribe; i++)
        {
            var w = new PythonWorker(
                @"C:\Projects\VoiceExtraction\speech_transcriptor.py",
                "transcribe_segments",
                "transcriptions",
                i);

            _state.AllWorkers.Add(w);
            await w.Ready;
            await transcribePool.Writer.WriteAsync(w, ct);
        }

        _state.ExtractPool = extractPool;
        _state.TranscribePool = transcribePool;

        Console.WriteLine("Все Python-воркеры готовы.");
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        _connection = new ManagerConnection();
        await _connection.ConnectAsync(ManagerIp, ManagerPort, ct);

        await _connection.SendAsync(new WorkerHelloMessage
        {
            ExtractThreads = _state.ExtractPool.Reader.Count,
            TranscribeThreads = _state.TranscribePool.Reader.Count
        }, ct);
    }

    private async Task DispatcherLoop(CancellationToken ct)
    {
        await foreach (var task in _taskQueue.Reader.ReadAllAsync(ct))
        {
            _ = Task.Run(() => HandleTask(task, ct), ct);
        }
    }

    private async Task HandleTask(TaskMessage task, CancellationToken ct)
    {
        if (task.TaskType == TaskType.Extract)
            await Process(task, _state.ExtractPool, "extract_segments", ct);
        else
            await Process(task, _state.TranscribePool, "transcribe_segments", ct);
    }

    private async Task Process(
        TaskMessage task,
        Channel<PythonWorker> pool,
        string inputDir,
        CancellationToken ct)
    {
        var worker = await pool.Reader.ReadAsync(ct);

        try
        {
            foreach (var f in task.Files)
                Base64FileHelper.WriteBase64ToFile(
                    Path.Combine(inputDir, f.FileName),
                    f.Base64Content);

            var produced = await worker.SendTask(task.SourceFileName);

            var payload = produced.Select(f =>
            {
                var path = Path.Combine(
                    task.TaskType == TaskType.Extract
                        ? "transcribe_segments"
                        : "transcriptions",
                    $"{worker.ThreadIndex}_{f}");

                return new FilePayload
                {
                    FileName = f,
                    Base64Content = Base64FileHelper.ReadFileAsBase64(path)
                };
            }).ToList();

            await _connection.SendAsync(new TaskMessage
            {
                TaskType = task.TaskType,
                SourceFileName = task.SourceFileName,
                Files = payload
            }, ct);

            foreach (var f in produced)
            {
                var path = Path.Combine(
                    task.TaskType == TaskType.Extract
                        ? "transcribe_segments"
                        : "transcriptions",
                    $"{worker.ThreadIndex}_{f}");

                TryDelete(path);
            }
        }
        finally
        {
            await pool.Writer.WriteAsync(worker, ct);
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            await _connection.SendAsync(new HeartBeatMessage(), ct);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to delete {path}: {ex.Message}");
        }
    }

    private static void CleanWorkingDirectories()
    {
        foreach (var dir in new[]
        {
            "extract_segments",
            "transcribe_segments",
            "transcriptions"
        })
        {
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir))
                File.Delete(f);
        }
    }
}

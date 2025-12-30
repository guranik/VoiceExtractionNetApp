using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Common.Messages;
using Common.Models;
using Common.Utils;
using Worker.Network;

namespace Worker;
public class WorkerService
{
    private readonly WorkerConfiguration _cfg;
    private readonly WorkerState _state = new();
    private ManagerConnection _connection = null!;
    private readonly Channel<TaskMessage> _taskQueue =
        Channel.CreateUnbounded<TaskMessage>();

    public WorkerService(WorkerConfiguration cfg)
    {
        _cfg = cfg;
    }

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
        var extractPool = Channel.CreateBounded<PythonWorker>(_cfg.Workers.ExtractCount);
        var transcribePool = Channel.CreateBounded<PythonWorker>(_cfg.Workers.TranscribeCount);

        for (int i = 0; i < _cfg.Workers.ExtractCount; i++)
        {
            var w = new PythonWorker(
                _cfg.PythonScripts.Extractor,
                _cfg.Directories.Extract,
                _cfg.Directories.Transcribe,
                i);

            _state.AllWorkers.Add(w);
            await w.Ready;
            await extractPool.Writer.WriteAsync(w, ct);
        }

        for (int i = 0; i < _cfg.Workers.TranscribeCount; i++)
        {
            var w = new PythonWorker(
                _cfg.PythonScripts.Transcriptor,
                _cfg.Directories.Transcribe,
                _cfg.Directories.Results,
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
        await _connection.ConnectAsync(_cfg.Manager.Ip, _cfg.Manager.Port, ct);

        await _connection.SendAsync(new WorkerHelloMessage
        {
            ExtractThreads = _cfg.Workers.ExtractCount,
            TranscribeThreads = _cfg.Workers.TranscribeCount
        }, ct);
    }

    private async Task DispatcherLoop(CancellationToken ct)
    {
        await foreach (var task in _taskQueue.Reader.ReadAllAsync(ct))
            _ = Task.Run(() => HandleTask(task, ct), ct);
    }

    private async Task HandleTask(TaskMessage task, CancellationToken ct)
    {
        if (task.TaskType == TaskType.Extract)
            await Process(task, _state.ExtractPool, _cfg.Directories.Extract, ct);
        else
            await Process(task, _state.TranscribePool, _cfg.Directories.Transcribe, ct);
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

            var outputDir = task.TaskType == TaskType.Extract
                ? _cfg.Directories.Transcribe
                : _cfg.Directories.Results;

            var payload = produced.Select(f =>
            {
                var path = Path.Combine(outputDir, $"{worker.ThreadIndex}_{f}");
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
                TryDelete(Path.Combine(outputDir, $"{worker.ThreadIndex}_{f}"));
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
            await Task.Delay(TimeSpan.FromSeconds(_cfg.HeartbeatSeconds), ct);
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

    private void CleanWorkingDirectories()
    {
        foreach (var dir in new[]
        {
            _cfg.Directories.Extract,
            _cfg.Directories.Transcribe,
            _cfg.Directories.Results
        })
        {
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir))
                File.Delete(f);
        }
    }
}

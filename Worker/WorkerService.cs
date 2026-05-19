using System.Threading.Channels;
using Common.Tcp.Models;
using Worker.Network;
using Common.Tcp.Messages;
using Common.Tcp.Utils;

namespace Worker;

public class WorkerService : IAsyncDisposable
{
    private readonly WorkerConfiguration _cfg;
    private readonly WorkerState _state = new();
    private ManagerConnection _connection = null!;
    private readonly Channel<TaskMessage> _taskQueue = Channel.CreateUnbounded<TaskMessage>();
    private CancellationTokenSource _globalCts = new();

    public WorkerService(WorkerConfiguration cfg) => _cfg = cfg;

    public async Task RunAsync(CancellationToken ct)
    {
        _globalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = _globalCts.Token;

        CleanWorkingDirectories();
        await InitializeWorkersAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                await ConnectAndHandshakeAsync(sessionCts.Token);
                var heartbeatTask = Task.Run(() => HeartbeatLoop(sessionCts.Token), sessionCts.Token);
                var dispatcherTask = Task.Run(() => DispatcherLoop(sessionCts.Token), sessionCts.Token);

                while (_connection.IsAlive && !sessionCts.IsCancellationRequested)
                {
                    var msg = await _connection.ReadAsync(sessionCts.Token);
                    if (msg is TaskMessage task)
                        await _taskQueue.Writer.WriteAsync(task, sessionCts.Token);
                }
                throw new IOException("Manager disconnected or session cancelled");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Console.WriteLine("[INFO] Graceful shutdown requested.");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Connection lost: {ex.Message}. Waiting workers...");
                sessionCts.Cancel();
                await WaitAllWorkersIdleAsync(ct);
                CleanWorkingDirectories();
                await Task.Delay(2000, ct);
            }
            finally
            {
                _connection?.Dispose();
            }
        }
    }

    private async Task InitializeWorkersAsync(CancellationToken ct)
    {
        var extractPool = Channel.CreateBounded<PythonWorker>(_cfg.Workers.ExtractCount);
        var transcribePool = Channel.CreateBounded<PythonWorker>(_cfg.Workers.TranscribeCount);

        for (int i = 0; i < _cfg.Workers.ExtractCount; i++)
        {
            var w = new PythonWorker(_cfg.PythonScripts.Extractor, _cfg.Directories.Extract, _cfg.Directories.Transcribe, i);
            _state.AllWorkers.Add(w);
            await w.Ready;
            await extractPool.Writer.WriteAsync(w, ct);
        }

        for (int i = 0; i < _cfg.Workers.TranscribeCount; i++)
        {
            var w = new PythonWorker(_cfg.PythonScripts.Transcriptor, _cfg.Directories.Transcribe, _cfg.Directories.Results, i);
            _state.AllWorkers.Add(w);
            await w.Ready;
            await transcribePool.Writer.WriteAsync(w, ct);
        }

        _state.ExtractPool = extractPool;
        _state.TranscribePool = transcribePool;
        Console.WriteLine("Все Python-воркеры готовы.");
    }

    private async Task ConnectAndHandshakeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = new ManagerConnection();
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                await _connection.ConnectAsync(_cfg.Manager.Ip, _cfg.Manager.Port, connectCts.Token);

                await _connection.SendAsync(new WorkerReadyMessage
                {
                    ExtractThreads = _cfg.Workers.ExtractCount,
                    TranscribeThreads = _cfg.Workers.TranscribeCount
                }, ct);

                var msg = await _connection.ReadAsync(ct);
                if (msg is AckMessage)
                {
                    Console.WriteLine("Handshake success");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Connection attempt: {ex.GetType().Name} - {ex.Message}");
                _connection?.Dispose();
                await Task.Delay(2000, ct);
            }
        }
    }

    private async Task DispatcherLoop(CancellationToken ct)
    {
        await foreach (var task in _taskQueue.Reader.ReadAllAsync(ct))
        {
            // Fire-and-forget с гарантированной обработкой ошибок
            _ = Task.Run(async () =>
            {
                try { await HandleTask(task, ct); }
                catch (Exception ex) { Console.Error.WriteLine($"[TASK FAILED] {ex.Message}"); }
            }, ct);
        }
    }

    private async Task HandleTask(TaskMessage task, CancellationToken ct)
    {
        var pool = task.TaskType == TaskType.Extract ? _state.ExtractPool : _state.TranscribePool;
        var inputDir = task.TaskType == TaskType.Extract ? _cfg.Directories.Extract : _cfg.Directories.Transcribe;
        await Process(task, pool, inputDir, ct);
    }

    private async Task Process(TaskMessage task, Channel<PythonWorker> pool, string inputDir, CancellationToken ct)
    {
        var worker = await pool.Reader.ReadAsync(ct);
        try
        {
            foreach (var f in task.Files)
                Base64FileHelper.WriteBase64ToFile(Path.Combine(inputDir, f.FileName), f.Base64Content);

            // Запуск с таймаутом 10 минут + поддержка отмены
            var produced = await worker.SendTaskAsync(task.SourceFileName, ct, TimeSpan.FromMinutes(10));

            var outputDir = task.TaskType == TaskType.Extract ? _cfg.Directories.Transcribe : _cfg.Directories.Results;
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
                SessionId = task.SessionId,
                Files = payload
            }, ct);

            foreach (var f in produced)
                TryDelete(Path.Combine(outputDir, $"{worker.ThreadIndex}_{f}"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PROCESS ERROR] Thread {worker.ThreadIndex}: {ex.Message}");
            // При ошибке всё равно возвращаем воркер в пул
        }
        finally
        {
            await pool.Writer.WriteAsync(worker, ct);
        }
    }

    private async Task WaitAllWorkersIdleAsync(CancellationToken ct)
    {
        foreach (var w in _state.AllWorkers.OfType<PythonWorker>())
        {
            try { await w.WaitIdleAsync(ct); }
            catch (OperationCanceledException) { /* игнорируем при отмене */ }
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _connection.IsAlive)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_cfg.HeartbeatSeconds), ct);
                await _connection.SendAsync(new HeartBeatMessage(), ct);
            }
            catch { return; }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void CleanWorkingDirectories()
    {
        foreach (var dir in new[] { _cfg.Directories.Extract, _cfg.Directories.Transcribe, _cfg.Directories.Results })
        {
            Directory.CreateDirectory(dir);
            try { foreach (var f in Directory.GetFiles(dir)) File.Delete(f); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _globalCts.Cancel();
        _state.DisposeAll();
    }
}
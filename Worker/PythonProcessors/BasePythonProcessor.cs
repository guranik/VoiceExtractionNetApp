using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Worker.Interfaces;

namespace Worker.PythonProcessors;
public abstract class BasePythonProcessor : IWorker, IAsyncDisposable
{
    public int ThreadIndex { get; }
    protected readonly string _outputDir;
    private readonly Process _process;
    private TaskCompletionSource<List<string>>? _currentTask;
    private CancellationTokenSource? _taskCts;
    private readonly TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => _readyTcs.Task;

    private readonly Channel<string> _logQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _logConsumerTask;

    protected static readonly string AppDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    protected BasePythonProcessor(string script, string inputDir, string outputDir, int index, string specificArgs)
    {
        ThreadIndex = index;
        _outputDir = outputDir;

        var startInfo = new ProcessStartInfo
        {
            // 1. Use bundled Python
            FileName = Path.Combine(AppDir, "python", "python.exe"),
            // Base arguments: script, inputDir, outputDir, index
            Arguments = $"\"{script}\" \"{inputDir}\" \"{outputDir}\" {index}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppDir
        };

        // Даем наследникам добавить свои переменные окружения
        ConfigureEnvironment(startInfo);

        // Даем наследникам добавить свои аргументы командной строки
        if (!string.IsNullOrWhiteSpace(specificArgs))
        {
            startInfo.Arguments += " " + specificArgs;
        }

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data, false);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data, true);

        // Важно: подписываемся на событие выхода процесса
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => HandleProcessExit();

        _logConsumerTask = ConsumeLogsAsync();

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>
    /// Метод для добавления специфичных переменных окружения (например, PyTorch, CUDA, Cache).
    /// </summary>
    protected abstract void ConfigureEnvironment(ProcessStartInfo startInfo);

    /// <summary>
    /// Метод для получения специфичных аргументов командной строки.
    /// </summary>

    public async Task<List<string>> SendTaskAsync(string fileName, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (_currentTask != null) throw new InvalidOperationException("Worker already busy");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        _taskCts = linkedCts;

        _currentTask = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        await _process.StandardInput.WriteAsync((fileName + "\n").AsMemory(), linkedCts.Token);
        await _process.StandardInput.FlushAsync(linkedCts.Token);

        try
        {
            return await _currentTask.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (linkedCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Python worker {ThreadIndex} did not respond within timeout.");
        }
    }

    public Task WaitIdleAsync(CancellationToken ct = default)
    {
        var tcs = _currentTask;
        if (tcs == null) return Task.CompletedTask;
        return tcs.Task.WaitAsync(ct);
    }

    private void OnLine(string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        line = line.Trim();

        _logQueue.Writer.TryWrite(isError ? $"[PY-{ThreadIndex}] [ERR] {line}" : $"[PY-{ThreadIndex}] {line}");

        if (line == "READY")
        {
            _readyTcs.TrySetResult(true);
            return;
        }

        if (!isError && line.StartsWith("DONE"))
        {
            CompleteTask();
        }
    }

    private void HandleProcessExit()
    {
        if (!_process.HasExited) return;

        var code = _process.ExitCode;
        _logQueue.Writer.TryWrite($"[PY-{ThreadIndex}] Process exited with code {code}");
        if (_currentTask != null && !_currentTask.Task.IsCompleted)
        {
            _currentTask.TrySetException(new InvalidOperationException($"Python process {ThreadIndex} exited unexpectedly (code {code})"));
        }
    }

    /// <summary>
    /// Сделан virtual, чтобы наследники могли переопределить логику сбора файлов, если она будет отличаться.
    /// </summary>
    protected virtual void CompleteTask()
    {
        var tcs = _currentTask;
        if (tcs == null || tcs.Task.IsCompleted) return;

        try
        {
            var files = Directory.GetFiles(_outputDir)
                .Select(Path.GetFileName)
                .Where(f => f!.StartsWith($"{ThreadIndex}_"))
                .Select(f => f![$"{ThreadIndex}_".Length..])
                .ToList();
            tcs.TrySetResult(files);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        finally
        {
            _currentTask = null;
            _taskCts?.Dispose();
            _taskCts = null;
        }
    }

    private async Task ConsumeLogsAsync()
    {
        await foreach (var log in _logQueue.Reader.ReadAllAsync())
        {
            try { Console.Error.WriteLine(log); } catch { /* игнорируем падение консоли */ }
        }
    }

    public void Dispose() => ((IAsyncDisposable)this).DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        _logQueue.Writer.Complete();
        try
        {
            if (!_process.HasExited)
                _process.Kill(true);
            await _process.WaitForExitAsync();
        }
        catch { }
        _process.Dispose();
        if (_logConsumerTask != null) await _logConsumerTask;
    }
}

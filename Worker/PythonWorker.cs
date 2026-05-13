using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Worker.Interfaces;

namespace Worker;

public class PythonWorker : IWorker, IAsyncDisposable
{
    public int ThreadIndex { get; }
    private readonly string _outputDir;
    private readonly Process _process;
    private TaskCompletionSource<List<string>>? _currentTask;
    private CancellationTokenSource? _taskCts;
    private readonly TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => _readyTcs.Task;

    // Неблокирующий логгер: очередь + фоновый потребитель
    private readonly Channel<string> _logQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _logConsumerTask;

    private static readonly string AppDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public PythonWorker(string script, string inputDir, string outputDir, int index)
    {
        ThreadIndex = index;
        _outputDir = outputDir;

        var pythonExe = Path.Combine(AppDir, "python", "python.exe");
        var pythonLib = Path.Combine(AppDir, "python", "Lib");
        var sitePackages = Path.Combine(AppDir, "python", "Lib", "site-packages");
        var torchLib = Path.Combine(sitePackages, "torch", "lib");
        var torchaudioLib = Path.Combine(sitePackages, "torchaudio", "lib");
        var ffmpegBin = Path.Combine(AppDir, "ffmpeg", "bin");

        var startInfo = new ProcessStartInfo
        {
            // 1. Use bundled Python, NOT system "python"
            FileName = Path.Combine(AppDir, "python", "python.exe"),
            Arguments = $"\"{script}\" \"{inputDir}\" \"{outputDir}\" {index}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppDir // Ensures relative imports in script work
        };

        // === Environment Variables for Offline Operation ===
        var env = startInfo.EnvironmentVariables;

        // 1. Python runtime isolation (critical for embeddable Python)
        env["PYTHONHOME"] = Path.Combine(AppDir, "python");
        env["PYTHONPATH"] = $"{sitePackages};{pythonLib}";

        // 2. I/O protocol reliability
        env["PYTHONUNBUFFERED"] = "1";       
        env["PYTHONIOENCODING"] = "utf-8";  
        env["PYTHONDONTWRITEBYTECODE"] = "1"; 

        env["PATH"] = $"{torchLib};{torchaudioLib};{ffmpegBin};{env["PATH"]}";

        string cacheDir = Path.Combine(AppDir, "cache");
        env["TORCH_HOME"] = Path.Combine(cacheDir, "torch");
        env["TORCH_HUB_DIR"] = Path.Combine(cacheDir, "torch_hub");
        env["XDG_CACHE_HOME"] = cacheDir;          
        env["HF_HOME"] = Path.Combine(cacheDir, "huggingface"); 

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data, false);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data, true);
        _logConsumerTask = ConsumeLogsAsync();

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task<List<string>> SendTaskAsync(string fileName, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (_currentTask != null) throw new InvalidOperationException("Worker already busy");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        _taskCts = linkedCts;

        _currentTask = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Асинхронная запись в stdin + таймаут
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

        // Неблокирующая отправка в очередь логов
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
        var code = _process.ExitCode;
        _logQueue.Writer.TryWrite($"[PY-{ThreadIndex}] Process exited with code {code}");
        if (_currentTask != null && !_currentTask.Task.IsCompleted)
        {
            _currentTask.TrySetException(new InvalidOperationException($"Python process {ThreadIndex} exited unexpectedly (code {code})"));
        }
    }

    private void CompleteTask()
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

    public void Dispose() => ((IAsyncDisposable)this).DisposeAsync().GetAwaiter().GetResult();

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
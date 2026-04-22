using System.Diagnostics;
using System.Text;
using Worker.Interfaces;

namespace Worker;
public class PythonWorker : IWorker
{
    public int ThreadIndex { get; }
    private readonly string _outputDir;
    private readonly Process _process;
    private TaskCompletionSource<List<string>>? _currentTask;
    private readonly TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => _readyTcs.Task;

    // Resolve deployment folder once at startup
    private static readonly string AppDir = Path.GetDirectoryName(Environment.ProcessPath)
                                         ?? AppContext.BaseDirectory;

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
        env["PYTHONUNBUFFERED"] = "1";        // Instant stdout/stderr delivery for READY/DONE
        env["PYTHONIOENCODING"] = "utf-8";    // Clean Unicode handling
        env["PYTHONDONTWRITEBYTECODE"] = "1"; // No __pycache__ clutter

        // 3. Native DLL search path (fixes libtorchaudio.pyd / torch.dll loading)
        // Prepend our DLL folders so Windows finds them BEFORE system paths
        env["PATH"] = $"{torchLib};{torchaudioLib};{ffmpegBin};{env["PATH"]}";

        // 4. Redirect framework caches to local deployment folder (zero internet calls)
        string cacheDir = Path.Combine(AppDir, "cache");
        env["TORCH_HOME"] = Path.Combine(cacheDir, "torch");
        env["TORCH_HUB_DIR"] = Path.Combine(cacheDir, "torch_hub");
        env["XDG_CACHE_HOME"] = cacheDir;           // Fallback for pip, requests, etc.
        env["HF_HOME"] = Path.Combine(cacheDir, "huggingface"); // Future-proofing

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data, false);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data, true);

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public Task WaitIdleAsync()
    {
        var tcs = _currentTask;
        return tcs != null ? tcs.Task : Task.CompletedTask;
    }

    private void OnLine(string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        line = line.Trim();

        if (line == "READY")
        {
            Console.WriteLine($"[PY-{ThreadIndex}] READY");
            _readyTcs.TrySetResult(true);
            return;
        }

        if (isError)
        {
            Console.WriteLine($"[PY-{ThreadIndex}] [ERROR] {line}");
            return;
        }

        Console.WriteLine($"[PY-{ThreadIndex}] {line}");

        if (line.StartsWith("DONE"))
            CompleteTask();
    }

    private void CompleteTask()
    {
        var files = Directory.GetFiles(_outputDir)
            .Select(Path.GetFileName)
            .Where(f => f!.StartsWith($"{ThreadIndex}_"))
            .Select(f => f!.Substring($"{ThreadIndex}_".Length))
            .ToList();

        _currentTask?.TrySetResult(files);
        _currentTask = null;
    }

    public Task<List<string>> SendTask(string fileName)
    {
        if (_currentTask != null)
            throw new InvalidOperationException("Worker already busy");

        _currentTask = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _process.StandardInput.WriteLine(fileName);
        _process.StandardInput.Flush();

        return _currentTask.Task;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(true);
        }
        catch { }
    }
}

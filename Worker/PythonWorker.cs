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
    private readonly TaskCompletionSource<bool> _readyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => _readyTcs.Task;

    public PythonWorker(string script, string inputDir, string outputDir, int index)
    {
        var absoluteScriptPath = Path.GetFullPath(script);
        Console.WriteLine($"[PY-{index}] Script path: {absoluteScriptPath}");
        Console.WriteLine($"[PY-{index}] Script exists: {File.Exists(absoluteScriptPath)}");

        ThreadIndex = index;
        _outputDir = outputDir;

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{script}\" \"{inputDir}\" \"{outputDir}\" {index}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

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

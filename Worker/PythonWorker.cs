using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;

class PythonWorker
{
    public bool IsFree { get; set; } = true;
    public int ThreadIndex { get; }

    private readonly string _outputDir;
    private readonly Process _process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<List<string>>> _pending;
    private readonly TaskCompletionSource<bool> _readyTcs;

    public Task Ready => _readyTcs.Task;

    public PythonWorker(string script, string inputDir, string outputDir, int index)
    {
        ThreadIndex = index;
        _outputDir = outputDir;
        _pending = new ConcurrentDictionary<string, TaskCompletionSource<List<string>>>();
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
            },
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (_, e) => ProcessLine(e.Data, false);
        _process.ErrorDataReceived += (_, e) => ProcessLine(e.Data, true);

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private void ProcessLine(string line, bool isError)
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
            Console.WriteLine($"[PY-{ThreadIndex}] [ERROR] {line}");
        else
            Console.WriteLine($"[PY-{ThreadIndex}] {line}");

        if (line.StartsWith("DONE"))
        {
            if (_pending.Any())
            {
                var key = _pending.Keys.First();
                if (_pending.TryRemove(key, out var tcs))
                {
                    var files = Directory.Exists(_outputDir)
                        ? Directory.GetFiles(_outputDir)
                            .Where(f => Path.GetFileName(f).StartsWith($"{ThreadIndex}_"))
                            .Select(f => Path.GetFileName(f).Substring(f.IndexOf('_') + 1))
                            .ToList()
                        : new List<string>();

                    tcs.TrySetResult(files);
                    IsFree = true;
                }
            }
        }
    }

    public Task<List<string>> SendTask(string fileName)
    {
        var tcs = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(fileName, tcs))
            throw new InvalidOperationException("Duplicate task");

        try
        {
            _process.StandardInput.WriteLine(fileName);
            _process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            _pending.TryRemove(fileName, out _);
            Console.WriteLine($"[PY-{ThreadIndex}] [ERROR] Failed to write to stdin: {ex}");
            tcs.TrySetResult(new List<string>());
            IsFree = true;
        }

        return tcs.Task;
    }
}

using System.IO;
using System.Text.Json;

namespace Worker;
public sealed class WorkerConfiguration
{
    public ManagerConfig Manager { get; set; } = new();
    public DirectoryConfig Directories { get; set; } = new();
    public PythonScriptConfig PythonScripts { get; set; } = new();
    public WorkerCountConfig Workers { get; set; } = new();
    public int HeartbeatSeconds { get; set; }

    public static WorkerConfiguration Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Configuration file not found: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkerConfiguration>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}

public sealed class ManagerConfig
{
    public string Ip { get; set; } = "";
    public int Port { get; set; }
}

public sealed class DirectoryConfig
{
    public string Extract { get; set; } = "";
    public string Transcribe { get; set; } = "";
    public string Results { get; set; } = "";
}

public sealed class PythonScriptConfig
{
    public string Extractor { get; set; } = "";
    public string Transcriptor { get; set; } = "";
}

public sealed class WorkerCountConfig
{
    public int ExtractCount { get; set; }
    public int TranscribeCount { get; set; }
}

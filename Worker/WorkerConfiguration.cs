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
        var config = JsonSerializer.Deserialize<WorkerConfiguration>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        config.PythonScripts.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path));

        return config;
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
    public string? BaseDirectory { get; set; }

    private string _extractor = "";
    private string _transcriptor = "";

    public string Extractor
    {
        get => string.IsNullOrEmpty(BaseDirectory)
            ? _extractor
            : Path.GetFullPath(Path.Combine(BaseDirectory, _extractor));
        set => _extractor = value;
    }

    public string Transcriptor
    {
        get => string.IsNullOrEmpty(BaseDirectory)
            ? _transcriptor
            : Path.GetFullPath(Path.Combine(BaseDirectory, _transcriptor));
        set => _transcriptor = value;
    }
}

public sealed class WorkerCountConfig
{
    public int ExtractCount { get; set; }
    public int TranscribeCount { get; set; }
}

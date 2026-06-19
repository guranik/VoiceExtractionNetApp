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
        {
            const string launchSettingsFile = "launchSettings.json";
            if (File.Exists(launchSettingsFile))
            {
                try
                {
                    var launchJson = File.ReadAllText(launchSettingsFile);
                    using var doc = JsonDocument.Parse(launchJson);

                    if (doc.RootElement.TryGetProperty("workingDirectory", out var workDirProp))
                    {
                        string workDir = workDirProp.GetString();
                        if (!string.IsNullOrWhiteSpace(workDir))
                        {
                            if (Directory.Exists(workDir))
                            {
                                Directory.SetCurrentDirectory(workDir);
                                Console.WriteLine($"[Info] Config not found. Switched to working directory: {Path.GetFullPath(workDir)}");
                            }
                            else
                            {
                                Console.WriteLine($"[Error] Specified workingDirectory does not exist: {workDir}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Failed to parse launchSettings.json: {ex.Message}");
                }
            }

            if (!File.Exists(path))
            {
                Console.WriteLine($"[Error] Configuration file not found: {Path.GetFullPath(path)}");
                throw new FileNotFoundException($"Configuration file not found: {Path.GetFullPath(path)}");
            }
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<WorkerConfiguration>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        config.PythonScripts.ScriptSourceDirectory = Path.GetDirectoryName(Path.GetFullPath(path));

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
    public string? ScriptSourceDirectory { get; set; }

    private string _extractor = "";
    private string _transcriptor = "";
   
    public string Extractor
    {
        get => string.IsNullOrEmpty(ScriptSourceDirectory)
            ? _extractor
            : Path.GetFullPath(Path.Combine(ScriptSourceDirectory, _extractor));
        set => _extractor = value;
    }

    public string Transcriptor
    {
        get => string.IsNullOrEmpty(ScriptSourceDirectory)
            ? _transcriptor
            : Path.GetFullPath(Path.Combine(ScriptSourceDirectory, _transcriptor));
        set => _transcriptor = value;
    }

    public string WhisperModel { get; set; } = null!;
    public int MaxSegmentDuration { get; set; } = 30;
}

public sealed class WorkerCountConfig
{
    public int ExtractCount { get; set; }
    public int TranscribeCount { get; set; }
}

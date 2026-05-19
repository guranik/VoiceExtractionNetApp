using System.Text.Json;

namespace Manager;

public class ManagerConfig
{
    public NetworkConfig Network { get; set; }
    public SessionConfig Session { get; set; }
    public DirectoryConfig Directories { get; set; }
    public AudioSplitterConfig AudioSplitter { get; set; }
    public HeartbeatConfig Heartbeat { get; set; }

    public static ManagerConfig Load(string path)
    {
        // 1. Проверяем наличие файла конфигурации в текущей директории
        if (!File.Exists(path))
        {
            // 2. Если нет, ищем launchSettings.json
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
                            // 3. Проверяем и переходим в указанную директорию
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

            // 4. Повторная проверка файла конфигурации после возможной смены директории
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Error] Configuration file not found: {Path.GetFullPath(path)}");
                throw new FileNotFoundException($"Configuration file not found: {Path.GetFullPath(path)}");
            }
        }

        // 5. Стандартная логика чтения и десериализации
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ManagerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
public class NetworkConfig
{
    public int ManagerPort { get; set; }
    public int HttpPort { get; set; }
}

public class SessionConfig
{
    public int FinalizeIdleTimeoutSeconds { get; set; }
}

public class DirectoryConfig
{
    public string Input { get; set; }
    public string ExtractSegments { get; set; }
    public string TranscribeSegments { get; set; }
    public string Transcriptions { get; set; }
    public string Output { get; set; }
}

public class AudioSplitterConfig
{
    public double MaxExtractSegmentDurationSec { get; set; }
    public double ExtractTranscribeEfficiency { get; set; }
}

public class HeartbeatConfig
{
    public int WorkerTimeoutSec { get; set; }
    public int MonitorIntervalMs { get; set; }
}

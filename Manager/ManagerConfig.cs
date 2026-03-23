using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;

namespace Manager;
public class ManagerConfig
{
    public NetworkConfig Network { get; set; }
    public DirectoryConfig Directories { get; set; }
    public AudioSplitterConfig AudioSplitter { get; set; }
    public HeartbeatConfig Heartbeat { get; set; }

    public static ManagerConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ManagerConfig>(json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }
}

public class NetworkConfig
{
    public int ManagerPort { get; set; }
    public int HttpPort { get; set; } = 8080;
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

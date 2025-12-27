using System.IO;
using System.Text.Json;

class ManagerConfig
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

class NetworkConfig
{
    public int ManagerPort { get; set; }
}

class DirectoryConfig
{
    public string Input { get; set; }
    public string ExtractSegments { get; set; }
    public string TranscribeSegments { get; set; }
    public string Transcriptions { get; set; }
}

class AudioSplitterConfig
{
    public double MaxExtractSegmentDurationSec { get; set; }
    public double ExtractTranscribeEfficiency { get; set; }
}

class HeartbeatConfig
{
    public int WorkerTimeoutSec { get; set; }
    public int MonitorIntervalMs { get; set; }
}

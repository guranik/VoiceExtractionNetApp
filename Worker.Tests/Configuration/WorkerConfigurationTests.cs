using System.IO;
using Worker;
using Xunit;

public class WorkerConfigurationTests
{
    [Fact]
    public void Load_Throws_When_File_Not_Exists()
    {
        Assert.Throws<FileNotFoundException>(() =>
            WorkerConfiguration.Load("missing.json"));
    }

    [Fact]
    public void Load_Reads_Config_Correctly()
    {
        using var dir = new TempDir();
        var path = dir.File("config.json", """
        {
          "Manager": { "Ip": "127.0.0.1", "Port": 1234 },
          "Directories": {
            "Extract": "a",
            "Transcribe": "b",
            "Results": "c"
          },
          "PythonScripts": {
            "Extractor": "ex.py",
            "Transcriptor": "tr.py"
          },
          "Workers": {
            "ExtractCount": 2,
            "TranscribeCount": 3
          },
          "HeartbeatSeconds": 10
        }
        """);

        var cfg = WorkerConfiguration.Load(path);

        Assert.Equal("127.0.0.1", cfg.Manager.Ip);
        Assert.Equal(1234, cfg.Manager.Port);
        Assert.Equal(2, cfg.Workers.ExtractCount);
        Assert.Equal(3, cfg.Workers.TranscribeCount);
        Assert.Equal(10, cfg.HeartbeatSeconds);
    }
}

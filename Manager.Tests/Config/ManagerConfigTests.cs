using System.IO;
using Manager;
using Xunit;
using Manager.Tests.TestHelpers;

namespace Manager.Tests.Config;

public class ManagerConfigTests
{
    [Fact]
    public void Load_ReadsJsonCorrectly()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "config.json");

        File.WriteAllText(path, """
        {
          "Network": { "ManagerPort": 5000 },
          "Directories": {
            "Input": "in",
            "ExtractSegments": "ex",
            "TranscribeSegments": "tr",
            "Transcriptions": "out"
          }
        }
        """);

        var cfg = ManagerConfig.Load(path);

        Assert.Equal(5000, cfg.Network.ManagerPort);
        Assert.Equal("in", cfg.Directories.Input);
    }
}

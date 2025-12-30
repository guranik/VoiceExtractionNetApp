using Manager.Utils;
using Xunit;
using Manager;
using Manager.Tests.TestHelpers;

namespace Manager.Tests.Utils;

public class DirectoryValidatorTests
{
    [Fact]
    public void ValidateManagerEnvironment_CreatesDirectories()
    {
        using var dir = new TempDirectory();

        var config = new ManagerConfig
        {
            Directories = new DirectoryConfig
            {
                Input = System.IO.Path.Combine(dir.Path, "in"),
                ExtractSegments = System.IO.Path.Combine(dir.Path, "ex"),
                TranscribeSegments = System.IO.Path.Combine(dir.Path, "tr"),
                Transcriptions = System.IO.Path.Combine(dir.Path, "out")
            }
        };

        DirectoryValidator.ValidateManagerEnvironment(config);

        Assert.True(System.IO.Directory.Exists(config.Directories.Input));
        Assert.True(System.IO.Directory.Exists(config.Directories.ExtractSegments));
        Assert.True(System.IO.Directory.Exists(config.Directories.TranscribeSegments));
        Assert.True(System.IO.Directory.Exists(config.Directories.Transcriptions));
    }
}

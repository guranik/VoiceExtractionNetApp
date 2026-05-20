using System.IO;
using Worker;
using Worker.Utils;
using Xunit;

public class DirectoryValidatorTests
{
    [Fact]
    public void Validate_Creates_Missing_Directories()
    {
        using var dir = new TempDir();

        var cfg = new WorkerConfiguration
        {
            Directories = new DirectoryConfig
            {
                Extract = Path.Combine(dir.Path, "ex"),
                Transcribe = Path.Combine(dir.Path, "tr"),
                Results = Path.Combine(dir.Path, "res")
            },
            PythonScripts = new PythonScriptConfig
            {
                Extractor = dir.File("a.py"),
                Transcriptor = dir.File("b.py")
            }
        };

        DirectoryValidator.Validate(cfg);

        Assert.True(Directory.Exists(cfg.Directories.Extract));
        Assert.True(Directory.Exists(cfg.Directories.Transcribe));
        Assert.True(Directory.Exists(cfg.Directories.Results));
    }

    [Fact]
    public void Validate_Throws_When_Script_Missing()
    {
        using var dir = new TempDir();

        var cfg = new WorkerConfiguration
        {
            Directories = new DirectoryConfig
            {
                Extract = dir.Path,
                Transcribe = dir.Path,
                Results = dir.Path
            },
            PythonScripts = new PythonScriptConfig
            {
                Extractor = "missing.py",
                Transcriptor = "missing2.py"
            }
        };

        Assert.Throws<FileNotFoundException>(() =>
            DirectoryValidator.Validate(cfg));
    }
}

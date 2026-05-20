using System.IO;
using Worker;
using Xunit;

public class WorkerServiceTests
{
    [Fact]
    public void CleanWorkingDirectories_Removes_All_Files()
    {
        using var dir = new TempDir();

        var extract = Path.Combine(dir.Path, "ex");
        var trans = Path.Combine(dir.Path, "tr");
        var res = Path.Combine(dir.Path, "res");

        Directory.CreateDirectory(extract);
        Directory.CreateDirectory(trans);
        Directory.CreateDirectory(res);

        File.WriteAllText(Path.Combine(extract, "a.txt"), "x");
        File.WriteAllText(Path.Combine(trans, "b.txt"), "x");
        File.WriteAllText(Path.Combine(res, "c.txt"), "x");

        var cfg = new WorkerConfiguration
        {
            Directories = new DirectoryConfig
            {
                Extract = extract,
                Transcribe = trans,
                Results = res
            }
        };

        var svc = new WorkerService(cfg);

        // вызываем private метод через reflection
        var method = typeof(WorkerService)
            .GetMethod("CleanWorkingDirectories",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!;

        method.Invoke(svc, null);

        Assert.Empty(Directory.GetFiles(extract));
        Assert.Empty(Directory.GetFiles(trans));
        Assert.Empty(Directory.GetFiles(res));
    }
}

using System;

namespace Manager.Tests.TestHelpers;

public class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Path))
            System.IO.Directory.Delete(Path, true);
    }
}
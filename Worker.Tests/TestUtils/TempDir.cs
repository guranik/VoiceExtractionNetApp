using System;
using System.IO;

public sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());

    public TempDir()
    {
        Directory.CreateDirectory(Path);
    }

    public string File(string name, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, true);
    }
}

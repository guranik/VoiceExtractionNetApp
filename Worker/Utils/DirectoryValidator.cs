namespace Worker.Utils;
public static class DirectoryValidator
{
    public static void Validate(WorkerConfiguration cfg)
    {
        EnsureDir(cfg.Directories.Extract);
        EnsureDir(cfg.Directories.Transcribe);
        EnsureDir(cfg.Directories.Results);

        EnsureFile(cfg.PythonScripts.Extractor);
        EnsureFile(cfg.PythonScripts.Transcriptor);
    }

    private static void EnsureDir(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static void EnsureFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required file missing: {path}");
    }
}

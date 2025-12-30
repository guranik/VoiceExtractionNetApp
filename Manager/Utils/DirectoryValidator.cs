using System.IO;

namespace Manager.Utils;
public static class DirectoryValidator
{
    public static void ValidateManagerEnvironment(ManagerConfig config)
    {
        Ensure(config.Directories.Input);
        Ensure(config.Directories.ExtractSegments);
        Ensure(config.Directories.TranscribeSegments);
        Ensure(config.Directories.Transcriptions);
    }

    private static void Ensure(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}

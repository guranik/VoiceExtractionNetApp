using System;
using System.IO;

static class DirectoryValidator
{
    public static void ValidateWorkerEnvironment()
    {
        string root = Directory.GetCurrentDirectory();

        EnsureDir(Path.Combine(root, "extract_segments"));
        EnsureDir(Path.Combine(root, "transcribe_segments"));
        EnsureDir(Path.Combine(root, "transcriptions"));

        EnsureFile(@"C:\Projects\VoiceExtraction\speech_extractor.py");
        EnsureFile(@"C:\Projects\VoiceExtraction\speech_transcriptor.py");
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

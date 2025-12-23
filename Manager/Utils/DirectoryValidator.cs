// Utils/DirectoryValidator.cs
using System.IO;

static class DirectoryValidator
{
    public static void ValidateManagerEnvironment()
    {
        Ensure("input");
        Ensure("extract_segments");
        Ensure("transcribe_segments");
        Ensure("transcriptions");
    }

    private static void Ensure(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}

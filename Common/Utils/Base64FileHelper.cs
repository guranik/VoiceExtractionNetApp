// Utils/Base64FileHelper.cs
namespace Common.Utils;

public static class Base64FileHelper
{
    public static string ReadFileAsBase64(string path)
        => Convert.ToBase64String(File.ReadAllBytes(path));

    public static void WriteBase64ToFile(string path, string base64)
        => File.WriteAllBytes(path, Convert.FromBase64String(base64));
}

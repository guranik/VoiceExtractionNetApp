// Models/FilePayload.cs
namespace Common.Models;

public sealed class FilePayload
{
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Base64 содержимое файла
    /// </summary>
    public string Base64Content { get; init; } = string.Empty;
}

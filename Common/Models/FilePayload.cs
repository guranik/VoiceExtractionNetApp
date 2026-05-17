namespace Common.Tcp.Models;

public sealed class FilePayload
{
    public string FileName { get; init; } = string.Empty;

    public string Base64Content { get; init; } = string.Empty;
}

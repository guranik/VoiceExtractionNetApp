// Networking/ChunkConstants.cs
namespace Common.Networking;

public static class ChunkConstants
{
    // 256 KB – безопасно для TCP + JSON
    public const int MaxChunkSizeBytes = 256 * 1024;
}

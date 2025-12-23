// Messages/FileChunkMessage.cs
namespace Common.Messages;

public sealed class FileChunkMessage : MessageBase
{
    public Guid OriginalMessageId { get; init; }

    public int ChunkIndex { get; init; }
    public int TotalChunks { get; init; }

    /// <summary>
    /// Часть JSON-сообщения (UTF8)
    /// </summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public FileChunkMessage()
    {
        Type = MessageType.FileChunk;
    }
}

// Networking/MessageFramer.cs
using Common.Messages;
using Common.Serialization;

namespace Common.Networking;

public static class MessageFramer
{
    public static IEnumerable<FileChunkMessage> Frame(MessageBase message)
    {
        var payload = JsonMessageSerializer.Serialize(message);

        int totalChunks =
            (int)Math.Ceiling(payload.Length / (double)ChunkConstants.MaxChunkSizeBytes);

        for (int i = 0; i < totalChunks; i++)
        {
            var offset = i * ChunkConstants.MaxChunkSizeBytes;
            var size = Math.Min(
                ChunkConstants.MaxChunkSizeBytes,
                payload.Length - offset);

            var chunk = new byte[size];
            Buffer.BlockCopy(payload, offset, chunk, 0, size);

            yield return new FileChunkMessage
            {
                OriginalMessageId = message.MessageId,
                ChunkIndex = i,
                TotalChunks = totalChunks,
                Data = chunk
            };
        }
    }
}

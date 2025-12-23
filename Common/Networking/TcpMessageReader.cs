// Networking/TcpMessageReader.cs
using System.Net.Sockets;
using Common.Messages;
using Common.Serialization;

namespace Common.Networking;

public sealed class TcpMessageReader
{
    private readonly NetworkStream _stream;

    private readonly Dictionary<Guid, List<FileChunkMessage>> _chunks = new();

    public TcpMessageReader(TcpClient client)
    {
        _stream = client.GetStream();
    }

    public async Task<MessageBase> ReadAsync(CancellationToken ct)
    {
        var lengthBuffer = new byte[4];
        await _stream.ReadExactlyAsync(lengthBuffer, ct);

        int length = BitConverter.ToInt32(lengthBuffer);
        var data = new byte[length];

        await _stream.ReadExactlyAsync(data, ct);

        var chunk = (FileChunkMessage)JsonMessageSerializer.Deserialize(data);

        if (!_chunks.TryGetValue(chunk.OriginalMessageId, out var list))
        {
            list = new List<FileChunkMessage>(chunk.TotalChunks);
            _chunks[chunk.OriginalMessageId] = list;
        }

        list.Add(chunk);

        if (list.Count < chunk.TotalChunks)
            return null!;

        list.Sort((a, b) => a.ChunkIndex.CompareTo(b.ChunkIndex));

        var fullData = list.SelectMany(c => c.Data).ToArray();
        _chunks.Remove(chunk.OriginalMessageId);

        return JsonMessageSerializer.Deserialize(fullData);
    }
}

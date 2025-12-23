// Networking/TcpMessageWriter.cs
using System.Net.Sockets;
using Common.Messages;
using Common.Serialization;

namespace Common.Networking;

public sealed class TcpMessageWriter
{
    private readonly NetworkStream _stream;

    public TcpMessageWriter(TcpClient client)
    {
        _stream = client.GetStream();
    }

    public async Task SendAsync(MessageBase message, CancellationToken ct)
    {
        foreach (var chunk in MessageFramer.Frame(message))
        {
            var data = JsonMessageSerializer.Serialize(chunk);

            var lengthPrefix = BitConverter.GetBytes(data.Length);

            await _stream.WriteAsync(lengthPrefix, ct);
            await _stream.WriteAsync(data, ct);
        }
    }
}

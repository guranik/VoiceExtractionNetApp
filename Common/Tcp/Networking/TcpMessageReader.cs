using System.Net.Sockets;
using Common.Tcp.Messages;
using Common.Tcp.Serialization;

namespace Common.Tcp.Networking;

public sealed class TcpMessageReader
{
    private readonly NetworkStream _stream;

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

        var message = JsonMessageSerializer.Deserialize(data);
        return message;
    }
}

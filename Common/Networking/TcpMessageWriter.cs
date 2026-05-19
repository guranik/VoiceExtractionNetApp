using System.Net.Sockets;
using Common.Tcp.Messages;
using Common.Tcp.Serialization;

namespace Common.Tcp.Networking;

public sealed class TcpMessageWriter
{
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public TcpMessageWriter(TcpClient client)
    {
        _stream = client.GetStream();
    }

    public async Task SendAsync(MessageBase message, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            var data = JsonMessageSerializer.Serialize(message);
            var lengthPrefix = BitConverter.GetBytes(data.Length);

            await _stream.WriteAsync(lengthPrefix, ct);
            await _stream.WriteAsync(data, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}

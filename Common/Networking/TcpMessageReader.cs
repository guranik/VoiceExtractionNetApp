using System.Net.Sockets;
using Common.Messages;
using Common.Serialization;

namespace Common.Networking;

public sealed class TcpMessageReader
{
    private readonly NetworkStream _stream;

    public TcpMessageReader(TcpClient client)
    {
        _stream = client.GetStream();
    }

    public async Task<MessageBase> ReadAsync(CancellationToken ct)
    {
        // Сначала читаем 4 байта длины
        var lengthBuffer = new byte[4];
        await _stream.ReadExactlyAsync(lengthBuffer, ct);

        int length = BitConverter.ToInt32(lengthBuffer);

        var data = new byte[length];
        await _stream.ReadExactlyAsync(data, ct);

        // Десериализация целого сообщения сразу
        return JsonMessageSerializer.Deserialize(data);
    }
}

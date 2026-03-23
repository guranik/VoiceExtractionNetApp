using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Tcp.Messages;
using Common.Tcp.Networking;

namespace Worker.Network;
class ManagerConnection : IDisposable
{
    private TcpClient _client;
    private TcpMessageReader _reader;
    private TcpMessageWriter _writer;

    public bool IsConnected => _client?.Connected == true;

    public bool IsAlive =>
        _client != null &&
        _client.Connected &&
        !(_client.Client.Poll(1, SelectMode.SelectRead) && _client.Client.Available == 0);

    public async Task ConnectAsync(string ip, int port, CancellationToken ct)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(ip, port, ct);

        _reader = new TcpMessageReader(_client);
        _writer = new TcpMessageWriter(_client);
    }

    public Task SendAsync(MessageBase msg, CancellationToken ct)
        => _writer.SendAsync(msg, ct);

    public async Task<MessageBase> ReadAsync(CancellationToken ct)
        => await _reader.ReadAsync(ct);

    public void Dispose()
    {
        _client?.Close();
        _client?.Dispose();
    }
}

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Messages;
using Common.Networking;

namespace Client.Networking;

public class ManagerClient : IDisposable
{
    private TcpClient _client;
    private TcpMessageReader _reader;
    private TcpMessageWriter _writer;
    private readonly CancellationTokenSource _cts = new();

    public event Action<string> OnLog;
    public event Action<ClientProgressMessage> OnProgress;

    public async Task ConnectAsync()
    {
        if (_client?.Connected == true)
            return;

        _client = new TcpClient();
        await _client.ConnectAsync("127.0.0.1", 5000);

        _reader = new TcpMessageReader(_client);
        _writer = new TcpMessageWriter(_client);

        _ = Task.Run(ReadLoop);
        OnLog?.Invoke("Connected to manager");
    }

    public Task SendAsync(MessageBase msg)
        => _writer.SendAsync(msg, _cts.Token);

    private async Task ReadLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var msg = await _reader.ReadAsync(_cts.Token);
                if (msg == null)
                    break;

                if (msg is ClientProgressMessage progress)
                {
                    OnProgress?.Invoke(progress);
                }
            }
        }
        catch
        {
            // соединение умерло — молча выходим
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _client?.Close();
    }
}

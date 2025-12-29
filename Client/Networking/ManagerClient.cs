using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Messages;
using Common.Networking;

namespace Client.Networking;

class ManagerClient : IDisposable
{
    private TcpClient _client;
    private TcpMessageReader _reader;
    private TcpMessageWriter _writer;
    private readonly CancellationTokenSource _cts = new();

    public event Action<string> OnLog;
    public event Action<string, string> OnTranscription;
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

                switch (msg)
                {
                    case TaskMessage task when task.TaskType == TaskType.Transcribe:
                        foreach (var f in task.Files)
                        {
                            var text = Encoding.UTF8.GetString(
                                Convert.FromBase64String(f.Base64Content));

                            OnTranscription?.Invoke(f.FileName, text);
                        }
                        break;

                    case ClientProgressMessage progress:
                        OnProgress?.Invoke(progress);
                        break;
                }
            }
        }
        catch
        {
            // соединение сдохло — и хуй с ним
        }
    }


    public void Dispose()
    {
        _cts.Cancel();
        _client?.Close();
    }
}

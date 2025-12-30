using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Messages;
using Common.Networking;

namespace Manager.Networking;
public class WorkerSession
{
    public WorkerInfo Info { get; }
    private readonly TcpMessageReader _reader;
    private readonly TcpMessageWriter _writer;

    public WorkerSession(TcpClient client, WorkerInfo info)
    {
        Info = info;
        _reader = new TcpMessageReader(client);
        _writer = new TcpMessageWriter(client);
    }

    public Task SendAsync(MessageBase msg, CancellationToken ct)
        => _writer.SendAsync(msg, ct);

    public async Task<MessageBase> ReadAsync(CancellationToken ct)
        => await _reader.ReadAsync(ct);
}

using System.Net;
using System.Net.Sockets;

namespace Manager.Networking;
public class WorkerListener
{
    private readonly TcpListener _listener;

    public WorkerListener(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start() => _listener.Start();

    public async Task<TcpClient> AcceptAsync(CancellationToken ct)
        => await _listener.AcceptTcpClientAsync(ct);
}

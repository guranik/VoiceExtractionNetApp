using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Tcp.Messages;
using Common.Tcp.Networking;

namespace Manager.Networking;

public class WorkerSession
{
    public WorkerInfo Info { get; }

    private readonly TcpMessageReader _reader;
    private readonly TcpMessageWriter _writer;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsDead { get; private set; }

    public WorkerSession(TcpClient client, WorkerInfo info)
    {
        Info = info;

        _reader = new TcpMessageReader(client);
        _writer = new TcpMessageWriter(client);
    }

    public async Task SendAsync(
        MessageBase msg,
        CancellationToken ct,
        int timeoutMs = 10000)
    {
        if (IsDead)
            throw new IOException("Worker is dead");

        await _sendLock.WaitAsync(ct);

        try
        {
            var sendTask = _writer.SendAsync(msg, ct);

            var completed = await Task.WhenAny(
                sendTask,
                Task.Delay(timeoutMs, ct));

            if (completed != sendTask)
                throw new TimeoutException("Worker send timeout");

            await sendTask;
        }
        catch
        {
            Disconnect();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<MessageBase> ReadAsync(
        CancellationToken ct,
        int timeoutMs = 60000)
    {
        if (IsDead)
            throw new IOException("Worker is dead");

        try
        {
            var readTask = _reader.ReadAsync(ct);

            var completed = await Task.WhenAny(
                readTask,
                Task.Delay(timeoutMs, ct));

            if (completed != readTask)
                throw new TimeoutException("Worker read timeout");

            var result = await readTask;

            if (result == null)
                throw new IOException("Worker disconnected");

            return result;
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    public void Disconnect()
    {
        if (IsDead)
            return;

        IsDead = true;

        try
        {
            Info.Client.Client.Shutdown(SocketShutdown.Both);
        }
        catch { }

        try
        {
            Info.Client.Close();
        }
        catch { }
    }
}
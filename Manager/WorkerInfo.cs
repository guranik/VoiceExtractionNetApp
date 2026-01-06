using System;
using System.Net.Sockets;

namespace Manager;
public class WorkerInfo : IDisposable
{
    public TcpClient Client { get; }

    public int TotalExtract { get; }
    public int TotalTranscribe { get; }

    public int FreeExtract { get; private set; }
    public int FreeTranscribe { get; private set; }

    private readonly object _lock = new();

    public DateTime LastHeartbeatUtc { get; private set; } = DateTime.UtcNow;

    public List<TaskMessage> ActiveExtract { get; } = new();
    public List<TaskMessage> ActiveTranscribe { get; } = new();

    public WorkerInfo(TcpClient client, int extract, int transcribe)
    {
        Client = client;
        TotalExtract = extract;
        TotalTranscribe = transcribe;
        FreeExtract = extract;
        FreeTranscribe = transcribe;
    }

    public void UpdateHeartbeat()
        => LastHeartbeatUtc = DateTime.UtcNow;

    public void DecExtract()
    {
        lock (_lock)
        {
            if (FreeExtract <= 0)
                throw new InvalidOperationException("No free extract threads");
            FreeExtract--;
        }
    }

    public void IncExtract()
    {
        lock (_lock)
        {
            if (FreeExtract >= TotalExtract)
                throw new InvalidOperationException("Extract overflow");
            FreeExtract++;
        }
    }

    public void DecTranscribe()
    {
        lock (_lock)
        {
            if (FreeTranscribe <= 0)
                throw new InvalidOperationException("No free transcribe threads");
            FreeTranscribe--;
        }
    }

    public void IncTranscribe()
    {
        lock (_lock)
        {
            if (FreeTranscribe >= TotalTranscribe)
                throw new InvalidOperationException("Transcribe overflow");
            FreeTranscribe++;
        }
    }

    public void Dispose()
    {
        try { Client.Close(); } catch { }
    }

    public void ResetState()
    {
        ActiveExtract.Clear();
        ActiveTranscribe.Clear();
    }
}

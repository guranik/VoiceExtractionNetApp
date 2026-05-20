using System.Net.Sockets;
using Manager;
using Xunit;

namespace Manager.Tests.Networking;

public class WorkerInfoTests
{
    [Fact]
    public void ExtractCounters_WorkCorrectly()
    {
        var info = new WorkerInfo(new TcpClient(), 2, 1);

        info.DecExtract();
        Assert.Equal(1, info.FreeExtract);

        info.IncExtract();
        Assert.Equal(2, info.FreeExtract);
    }

    [Fact]
    public void TranscribeOverflow_Throws()
    {
        var info = new WorkerInfo(new TcpClient(), 1, 1);

        Assert.Throws<System.InvalidOperationException>(() =>
        {
            info.IncTranscribe();
        });
    }

    [Fact]
    public void Heartbeat_UpdatesTime()
    {
        var info = new WorkerInfo(new TcpClient(), 1, 1);
        var before = info.LastHeartbeatUtc;

        info.UpdateHeartbeat();

        Assert.True(info.LastHeartbeatUtc >= before);
    }
}

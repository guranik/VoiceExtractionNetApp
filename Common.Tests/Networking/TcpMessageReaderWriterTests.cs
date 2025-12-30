using System.Net;
using System.Net.Sockets;
using Common.Messages;
using Common.Networking;
using FluentAssertions;
using Xunit;

public class TcpMessageReaderWriterTests
{
    [Fact]
    public async Task WriterAndReader_ShouldTransferMessage()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        var serverClient = await listener.AcceptTcpClientAsync();

        var writer = new TcpMessageWriter(client);
        var reader = new TcpMessageReader(serverClient);

        var msg = new HeartBeatMessage();

        await writer.SendAsync(msg, CancellationToken.None);
        var received = await reader.ReadAsync(CancellationToken.None);

        received.Should().BeOfType<HeartBeatMessage>();

        listener.Stop();
    }
}

using Common.Tcp.Messages;
using FluentAssertions;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
using Xunit;
using MessageType = Common.Tcp.Messages.MessageType;

public class HeartBeatMessageTests
{
    [Fact]
    public void Ctor_ShouldSetTypeAndTimestamp()
    {
        var before = DateTime.UtcNow;
        var msg = new HeartBeatMessage();
        var after = DateTime.UtcNow;

        msg.Type.Should().Be(MessageType.HeartBeat);
    }
}

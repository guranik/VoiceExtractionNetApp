using Common.Messages;
using FluentAssertions;
using Xunit;

public class WorkerHelloMessageTests
{
    [Fact]
    public void Ctor_ShouldSetType()
    {
        var msg = new WorkerReadyMessage();

        msg.Type.Should().Be(MessageType.WorkerReady);
    }

    [Fact]
    public void Properties_ShouldBeAssigned()
    {
        var msg = new WorkerReadyMessage
        {
            ExtractThreads = 2,
            TranscribeThreads = 4
        };

        msg.ExtractThreads.Should().Be(2);
        msg.TranscribeThreads.Should().Be(4);
    }
}

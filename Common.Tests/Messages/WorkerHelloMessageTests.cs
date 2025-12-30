using Common.Messages;
using FluentAssertions;
using Xunit;

public class WorkerHelloMessageTests
{
    [Fact]
    public void Ctor_ShouldSetType()
    {
        var msg = new WorkerHelloMessage();

        msg.Type.Should().Be(MessageType.WorkerHello);
    }

    [Fact]
    public void Properties_ShouldBeAssigned()
    {
        var msg = new WorkerHelloMessage
        {
            ExtractThreads = 2,
            TranscribeThreads = 4
        };

        msg.ExtractThreads.Should().Be(2);
        msg.TranscribeThreads.Should().Be(4);
    }
}

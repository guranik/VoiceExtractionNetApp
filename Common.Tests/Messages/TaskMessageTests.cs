using Common.Tcp.Messages;
using FluentAssertions;
using Xunit;

public class TaskMessageTests
{
    [Theory]
    [InlineData(TaskType.Extract, MessageType.ExtractTask)]
    [InlineData(TaskType.Transcribe, MessageType.TranscribeTask)]
    public void Type_ShouldDependOnTaskType(TaskType taskType, MessageType expected)
    {
        var msg = new TaskMessage { TaskType = taskType };

        msg.Type.Should().Be(expected);
    }

    [Fact]
    public void Files_ShouldBeInitialized()
    {
        var msg = new TaskMessage();

        msg.Files.Should().NotBeNull().And.BeEmpty();
    }
}

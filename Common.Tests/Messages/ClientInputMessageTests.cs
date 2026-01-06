using Common.Messages;
using Common.Models;
using FluentAssertions;
using Xunit;

public class ClientInputMessageTests
{
    [Fact]
    public void Type_ShouldAlwaysBeClientInput()
    {
        var msg = new ClientFileMessage();

        msg.Type.Should().Be(MessageType.ClientFile);
    }

    [Fact]
    public void File_CanBeAssigned()
    {
        var file = new FilePayload { FileName = "test.txt" };
        var msg = new ClientFileMessage { File = file };

        msg.File.Should().Be(file);
    }
}

using Common.Messages;
using FluentAssertions;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.ObjectModel;
using Xunit;
using MessageType = Common.Messages.MessageType;

public class ClientProgressMessageTests
{
    [Fact]
    public void Type_ShouldBeClientProgress()
    {
        var msg = new ClientProgressMessage();

        msg.Type.Should().Be(MessageType.ClientProgress);
    }

    [Fact]
    public void Properties_ShouldStoreValues()
    {
        var msg = new ClientProgressMessage
        {
            LatestExtractSegmentStart = 10,
            InputFileDuration = 100,
            TotalTranscribeSegments = 5,
            TotalTranscriptions = 3
        };

        msg.LatestExtractSegmentStart.Should().Be(10);
        msg.InputFileDuration.Should().Be(100);
        msg.TotalTranscribeSegments.Should().Be(5);
        msg.TotalTranscriptions.Should().Be(3);
    }
}

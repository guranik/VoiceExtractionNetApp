using Common.Messages;
using Common.Serialization;
using FluentAssertions;
using Xunit;

public class JsonMessageSerializerTests
{
    [Fact]
    public void SerializeAndDeserialize_ShouldPreserveMessage()
    {
        var original = new WorkerHelloMessage
        {
            ExtractThreads = 1,
            TranscribeThreads = 2
        };

        var bytes = JsonMessageSerializer.Serialize(original);
        var result = JsonMessageSerializer.Deserialize(bytes);

        result.Should().BeOfType<WorkerHelloMessage>();
        var msg = (WorkerHelloMessage)result;
        msg.ExtractThreads.Should().Be(1);
        msg.TranscribeThreads.Should().Be(2);
    }

    [Fact]
    public void Deserialize_UnknownType_ShouldThrow()
    {
        var json = @"{ ""type"": 999 }";
        var data = System.Text.Encoding.UTF8.GetBytes(json);

        Assert.Throws<InvalidOperationException>(
            () => JsonMessageSerializer.Deserialize(data));
    }
}

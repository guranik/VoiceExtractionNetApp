using Common.Tcp.Models;
using FluentAssertions;
using Xunit;

public class FilePayloadTests
{
    [Fact]
    public void Defaults_ShouldBeEmptyStrings()
    {
        var payload = new FilePayload();

        payload.FileName.Should().BeEmpty();
        payload.Base64Content.Should().BeEmpty();
    }
}

using Client.Networking;
using Xunit;

namespace Client.Tests.Networking;

public class ManagerClientTests
{
    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var client = new ManagerClient();

        var ex = Record.Exception(() => client.Dispose());

        Assert.Null(ex);
    }
}

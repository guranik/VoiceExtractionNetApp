using Manager.Core;
using Xunit;

namespace Manager.Tests.Core;

public class SessionStateTests
{

    [Fact]
    public void LastActivityUtc_InitializedToUtcNow()
    {
        var before = DateTime.UtcNow;
        var session = new SessionState("s1", "f.wav");
        var after = DateTime.UtcNow;

        Assert.True(session.LastActivityUtc >= before);
        Assert.True(session.LastActivityUtc <= after);
    }
}
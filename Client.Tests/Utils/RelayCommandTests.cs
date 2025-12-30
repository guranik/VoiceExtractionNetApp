using Client.Utils;
using Xunit;

namespace Client.Tests.Utils;

public class RelayCommandTests
{
    [Fact]
    public void CanExecute_AlwaysReturnsTrue()
    {
        var cmd = new RelayCommand(() => { });

        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public void Execute_ShouldInvokeAction()
    {
        bool executed = false;
        var cmd = new RelayCommand(() => executed = true);

        cmd.Execute(null);

        Assert.True(executed);
    }
}

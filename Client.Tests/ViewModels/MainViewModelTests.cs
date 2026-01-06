using Client.ViewModels;
using Common.Messages;
using Xunit;

namespace Client.Tests.ViewModels;

public class MainViewModelTests
{
    [StaFact]
    public void AppendLog_ShouldAddTimestampedMessage()
    {
        var vm = new MainViewModel(new TestDispatcher());

        vm.GetType()
          .GetMethod("AppendLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
          .Invoke(vm, new object[] { "test" });

        Assert.Contains("test", vm.Log);
    }

    [StaFact]
    public void CanSend_ShouldBeFalse_WhenNoFileSelected()
    {
        var vm = new MainViewModel(new TestDispatcher());

        Assert.False(vm.CanSend);
    }

    [StaFact]
    public void OnProgressReceived_ShouldUpdateProgressValues()
    {
        var vm = new MainViewModel(new TestDispatcher());

        var msg = new ClientProgressMessage
        {
            InputFileDuration = 100,
            EarliestExtractSegmentStart = 50,
            LatestTranscriptionEnd = 10,
        };

        vm.GetType()
          .GetMethod("OnProgressReceived", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
          .Invoke(vm, new object[] { msg });

        Assert.Equal(0.5, vm.ExtractProgress, 2);
        Assert.Equal(0.1, vm.TranscribeProgress, 2);
    }
}

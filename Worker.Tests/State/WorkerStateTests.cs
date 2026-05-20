using Worker;
using Worker.Interfaces;
using Xunit;

public class WorkerStateTests
{
    [Fact]
    public void DisposeAll_Disposes_All_Workers()
    {
        var state = new WorkerState();

        var w1 = new FakeWorker();
        var w2 = new FakeWorker();

        state.AllWorkers.Add(w1);
        state.AllWorkers.Add(w2);

        state.DisposeAll();

        Assert.True(w1.Disposed);
        Assert.True(w2.Disposed);
        Assert.Empty(state.AllWorkers);
    }

    private sealed class FakeWorker : IWorker
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}

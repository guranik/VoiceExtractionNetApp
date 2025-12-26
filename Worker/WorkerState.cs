using System.Collections.Generic;
using System.Threading.Channels;

class WorkerState
{
    public Channel<PythonWorker> ExtractPool { get; set; } = null!;
    public Channel<PythonWorker> TranscribePool { get; set; } = null!;

    public List<PythonWorker> AllWorkers { get; } = new();

    public void DisposeAll()
    {
        foreach (var w in AllWorkers)
            w.Dispose();

        AllWorkers.Clear();
    }
}

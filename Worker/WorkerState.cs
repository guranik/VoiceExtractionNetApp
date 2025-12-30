using System.Collections.Generic;
using System.Threading.Channels;
using Worker.Interfaces;

namespace Worker;
public class WorkerState
{
    public Channel<PythonWorker> ExtractPool { get; set; } = null!;
    public Channel<PythonWorker> TranscribePool { get; set; } = null!;

    public List<IWorker> AllWorkers { get; } = new();

    public void DisposeAll()
    {
        foreach (var w in AllWorkers)
            w.Dispose();

        AllWorkers.Clear();
    }
}

using System.Threading.Channels;
using Worker.Interfaces;
using Worker.PythonProcessors;

namespace Worker;
public class WorkerState
{
    public Channel<BasePythonProcessor> ExtractPool { get; set; } = null!;
    public Channel<BasePythonProcessor> TranscribePool { get; set; } = null!;

    public List<IWorker> AllWorkers { get; } = new();

    public void DisposeAll()
    {
        foreach (var w in AllWorkers)
            w.Dispose();

        AllWorkers.Clear();
    }
}

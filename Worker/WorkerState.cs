// WorkerState.cs
using System.Collections.Generic;

class WorkerState
{
    public List<PythonWorker> ExtractWorkers { get; } = new();
    public List<PythonWorker> TranscribeWorkers { get; } = new();

    public void DisposeAll()
    {
        ExtractWorkers.Clear();
        TranscribeWorkers.Clear();
    }
}

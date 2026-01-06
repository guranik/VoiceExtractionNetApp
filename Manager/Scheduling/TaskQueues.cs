using System.Collections.Concurrent;
using System.Collections.Generic;
using Common.Messages;

namespace Manager.Scheduling;
public static class TaskQueues
{
    public static readonly ConcurrentQueue<TaskMessage> ExtractQueue = new();
    public static readonly ConcurrentQueue<TaskMessage> TranscribeQueue = new();


    private static readonly object _lock = new();


    public static void ClearAll()
    {
        while (ExtractQueue.TryDequeue(out _)) { }
        while (TranscribeQueue.TryDequeue(out _)) { }
    }
}

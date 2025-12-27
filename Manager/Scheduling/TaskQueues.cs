using System.Collections.Concurrent;
using Common.Messages;

static class TaskQueues
{
    public static readonly ConcurrentQueue<TaskMessage> ExtractQueue = new();
    public static readonly ConcurrentQueue<TaskMessage> TranscribeQueue = new();
}

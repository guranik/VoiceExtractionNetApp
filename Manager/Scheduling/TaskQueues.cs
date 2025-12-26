using System.Collections.Concurrent;
using Common.Messages;

static class TaskQueues
{
    public static readonly ConcurrentQueue<TaskMessage> ExtractQueue = new();
    public static readonly ConcurrentQueue<TaskMessage> TranscribeQueue = new();

    public static readonly ConcurrentDictionary<string, byte> ExtractFiles = new();
    public static readonly ConcurrentDictionary<string, byte> TranscribeFiles = new();
}

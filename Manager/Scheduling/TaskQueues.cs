using System.Collections.Concurrent;
using System.Collections.Generic;
using Common.Messages;

static class TaskQueues
{
    public static readonly ConcurrentQueue<TaskMessage> ExtractQueue = new();
    public static readonly ConcurrentQueue<TaskMessage> TranscribeQueue = new();

    public static readonly List<string> ExtractFiles = new();
    public static readonly List<string> TranscribeFiles = new();

    private static readonly object _lock = new();

    public static void AddExtract(string file)
    {
        lock (_lock)
            ExtractFiles.Add(file);
    }

    public static void AddTranscribe(string file)
    {
        lock (_lock)
            TranscribeFiles.Add(file);
    }
}

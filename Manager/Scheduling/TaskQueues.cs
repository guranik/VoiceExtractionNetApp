using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Common.Tcp.Messages;

namespace Manager.Scheduling;

public static class TaskQueues
{
    public static readonly ConcurrentQueue<TaskMessage> ExtractQueue = new();
    public static readonly ConcurrentQueue<TaskMessage> TranscribeQueue = new();

    private static readonly ConcurrentDictionary<string, int> _extractSessionCounts = new();
    private static readonly ConcurrentDictionary<string, int> _transcribeSessionCounts = new();

    private static readonly object _lock = new();

    public static void EnqueueExtract(TaskMessage task)
    {
        ExtractQueue.Enqueue(task);

        if (!string.IsNullOrEmpty(task.SessionId))
        {
            _extractSessionCounts.AddOrUpdate(
                task.SessionId,
                1,
                (_, count) => count + 1);
        }
    }

    public static void EnqueueTranscribe(TaskMessage task)
    {
        TranscribeQueue.Enqueue(task);

        if (!string.IsNullOrEmpty(task.SessionId))
        {
            _transcribeSessionCounts.AddOrUpdate(
                task.SessionId,
                1,
                (_, count) => count + 1);
        }
    }

    public static bool TryDequeueExtract(out TaskMessage task)
    {
        if (ExtractQueue.TryDequeue(out task))
        {
            if (!string.IsNullOrEmpty(task.SessionId))
            {
                _extractSessionCounts.AddOrUpdate(
                    task.SessionId,
                    0,
                    (_, count) => count > 0 ? count - 1 : 0);
            }
            return true;
        }
        return false;
    }

    public static bool TryDequeueTranscribe(out TaskMessage task)
    {
        if (TranscribeQueue.TryDequeue(out task))
        {
            if (!string.IsNullOrEmpty(task.SessionId))
            {
                _transcribeSessionCounts.AddOrUpdate(
                    task.SessionId,
                    0,
                    (_, count) => count > 0 ? count - 1 : 0);
            }
            return true;
        }
        return false;
    }

    public static bool IsEmpty(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return ExtractQueue.IsEmpty && TranscribeQueue.IsEmpty;

        _extractSessionCounts.TryGetValue(sessionId, out var extractCount);
        _transcribeSessionCounts.TryGetValue(sessionId, out var transcribeCount);

        return extractCount == 0 && transcribeCount == 0;
    }

    public static bool IsEmpty()
    {
        return ExtractQueue.IsEmpty && TranscribeQueue.IsEmpty;
    }

    public static void ClearAll()
    {
        while (ExtractQueue.TryDequeue(out _)) { }
        while (TranscribeQueue.TryDequeue(out _)) { }

        _extractSessionCounts.Clear();
        _transcribeSessionCounts.Clear();
    }

    public static void ClearSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;

        var extractItems = new List<TaskMessage>();
        while (ExtractQueue.TryDequeue(out var task))
        {
            if (task.SessionId != sessionId)
                extractItems.Add(task);
        }
        foreach (var item in extractItems)
            ExtractQueue.Enqueue(item);

        var transcribeItems = new List<TaskMessage>();
        while (TranscribeQueue.TryDequeue(out var task))
        {
            if (task.SessionId != sessionId)
                transcribeItems.Add(task);
        }
        foreach (var item in transcribeItems)
            TranscribeQueue.Enqueue(item);

        _extractSessionCounts.TryRemove(sessionId, out _);
        _transcribeSessionCounts.TryRemove(sessionId, out _);
    }

    public static int GetExtractCount(string sessionId)
    {
        _extractSessionCounts.TryGetValue(sessionId, out var count);
        return count;
    }

    public static int GetTranscribeCount(string sessionId)
    {
        _transcribeSessionCounts.TryGetValue(sessionId, out var count);
        return count;
    }

    public static int GetTotalExtractCount() => ExtractQueue.Count;
    public static int GetTotalTranscribeCount() => TranscribeQueue.Count;
}
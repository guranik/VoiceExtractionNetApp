using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Tcp.Messages;
using Manager.Networking;

namespace Manager.Scheduling;

public class TaskDispatcher
{
    private readonly List<WorkerSession> _workers;
    private readonly object _workersLock;

    public TaskDispatcher(
        List<WorkerSession> workers,
        object workersLock)
    {
        _workers = workers;
        _workersLock = workersLock;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _ = Task.Run(() => DispatchExtract(ct), ct);
        _ = Task.Run(() => DispatchTranscribe(ct), ct);

        Console.WriteLine("Диспетчеры задач запущены.");

        await Task.CompletedTask;
    }

    private async Task DispatchExtract(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<WorkerSession> snapshot;

            lock (_workersLock)
            {
                snapshot = _workers
                    .Where(w => !w.IsDead && w.Info.FreeExtract > 0)
                    .ToList();
            }

            foreach (var w in snapshot)
            {
                if (!TaskQueues.ExtractQueue.TryDequeue(out var task))
                    break;

                try
                {
                    w.Info.DecExtract();

                    w.Info.ActiveExtract[task.SourceFileName] = task;

                    await w.SendAsync(task, ct);

                    Console.WriteLine($"Extract task sent.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Extract dispatch failed: {ex.Message}");

                    w.Disconnect();

                    TaskQueues.EnqueueExtract(task);

                    try
                    {
                        w.Info.IncExtract();
                    }
                    catch { }
                }
            }

            await Task.Delay(50, ct);
        }
    }

    private async Task DispatchTranscribe(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<WorkerSession> snapshot;

            lock (_workersLock)
            {
                snapshot = _workers
                    .Where(w => !w.IsDead && w.Info.FreeTranscribe > 0)
                    .ToList();
            }

            foreach (var w in snapshot)
            {
                if (!TaskQueues.TranscribeQueue.TryDequeue(out var task))
                    break;

                try
                {
                    w.Info.DecTranscribe();

                    w.Info.ActiveTranscribe[task.SourceFileName] = task;

                    await w.SendAsync(task, ct);

                    Console.WriteLine($"Transcribe task sent.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Transcribe dispatch failed: {ex.Message}");

                    w.Disconnect();

                    TaskQueues.EnqueueTranscribe(task);

                    try
                    {
                        w.Info.IncTranscribe();
                    }
                    catch { }
                }
            }

            await Task.Delay(50, ct);
        }
    }
}
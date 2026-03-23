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

    public TaskDispatcher(List<WorkerSession> workers)
    {
        _workers = workers;
    }

    public async Task RunAsync(CancellationToken ct)
    {

        _ = Task.Run(() => DispatchExtract(ct), ct);
        _ = Task.Run(() => DispatchTranscribe(ct), ct);

        Console.WriteLine("Диспетчеры задач запущены.");
    }

    private async Task DispatchExtract(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var w in _workers.Where(w => w.Info.FreeExtract > 0))
            {
                if (!TaskQueues.ExtractQueue.TryDequeue(out var task))
                    break;
                w.Info.DecExtract();
                w.Info.ActiveExtract.Add(task);
                await w.SendAsync(task, ct);


                Console.WriteLine($"Задача Extract отправлена воркеркеру.");
            }

            await Task.Delay(50, ct);
        }
    }

    private async Task DispatchTranscribe(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var w in _workers.Where(w => w.Info.FreeTranscribe > 0))
            {
                if (!TaskQueues.TranscribeQueue.TryDequeue(out var task))
                    break;

                w.Info.DecTranscribe();
                w.Info.ActiveTranscribe.Add(task);
                await w.SendAsync(task, ct);

                Console.WriteLine($"Задача Transcribe отправлена воркеру. Осталось свободных потоков: {w.Info.FreeTranscribe}.");
            }

            await Task.Delay(50, ct);
        }
    }
}

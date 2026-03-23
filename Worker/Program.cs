using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Worker.Utils;

namespace Worker;
class Program
{
    static async Task Main()
    {
        Console.Title = "Worker";

        try
        {
            var config = WorkerConfiguration.Load("configuration.json");
            DirectoryValidator.Validate(config);

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var worker = new WorkerService(config);
            await worker.RunAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex}");
        }
    }
}

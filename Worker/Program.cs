using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "Worker";

        try
        {
            DirectoryValidator.ValidateWorkerEnvironment();

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var worker = new WorkerService();
            await worker.RunAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR]{ex.ToString()}");
        }
    }
}

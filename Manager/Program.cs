using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        Console.Title = "Manager";

        try
        {
            var config = ManagerConfig.Load("configuration.json");

            DirectoryValidator.ValidateManagerEnvironment(config);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var manager = new ManagerService(config);
            await manager.RunAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex}");
        }
    }
}

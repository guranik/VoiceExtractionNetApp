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
            DirectoryValidator.ValidateManagerEnvironment();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var manager = new ManagerService();
            await manager.RunAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.ToString()}");
        }
    }
}

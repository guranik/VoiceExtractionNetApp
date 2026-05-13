using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Worker.Utils;
using Worker;

class Program
{
    static async Task Main()
    {
        Console.Title = "Worker";

        var config = WorkerConfiguration.Load("configuration.json");
        DirectoryValidator.Validate(config);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddSingleton<WorkerService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);

                // logging.ClearProviders();
            })
            .Build();

        var worker = host.Services.GetRequiredService<WorkerService>();
        await worker.RunAsync(cts.Token);
    }
}
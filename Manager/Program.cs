using Manager.Core;
using Manager.HttpApi;
using Manager.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Manager;

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
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton(config);
                    services.AddSingleton<ISessionHub, SessionHub>();
                    services.AddSingleton<ManagerService>();
                    services.AddSingleton<HttpApiHost>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .Build();

            var sessionHub = host.Services.GetRequiredService<ISessionHub>();
            var httpApi = host.Services.GetRequiredService<HttpApiHost>();
            var managerService = host.Services.GetRequiredService<ManagerService>();

            var tasks = new[]
            {
                managerService.RunAsync(cts.Token),
                httpApi.StartAsync(cts.Token),
                RunSessionCleanupAsync(sessionHub, cts.Token)
            };

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex}");
        }
    }

    private static async Task RunSessionCleanupAsync(ISessionHub sessionHub, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            // Опционально: удалять старые сессии
        }
    }
}
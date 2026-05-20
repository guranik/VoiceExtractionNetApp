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
                    services.AddSingleton<ISessionHub>(sp =>
                    {
                        var config = sp.GetRequiredService<ManagerConfig>();
                        var logger = sp.GetRequiredService<ILogger<SessionHub>>();

                        return new SessionHub(
                            config,
                            logger: logger,
                            retentionAfterFinalize: TimeSpan.FromSeconds(config.Session.FinalizeIdleTimeoutSeconds),
                            cleanupInterval: TimeSpan.FromMinutes(5)
                        );
                    }); services.AddSingleton<ManagerService>();
                    services.AddSingleton<HttpApiHost>(sp =>
                    {
                        return new HttpApiHost(
                            sp.GetRequiredService<ISessionHub>(),
                            sp.GetRequiredService<ManagerService>(),
                            sp.GetRequiredService<ManagerConfig>(),
                            sp.GetRequiredService<ILogger<HttpApiHost>>()
                        );
                    });
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .Build();

            var httpApi = host.Services.GetRequiredService<HttpApiHost>();
            var managerService = host.Services.GetRequiredService<ManagerService>();

            var tasks = new[]
            {
                managerService.RunAsync(cts.Token),
                httpApi.StartAsync(cts.Token),
            };

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] {ex}");
        }
    }
}
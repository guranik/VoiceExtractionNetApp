using Microsoft.AspNetCore.Http.Features;
using System.Net;
using System.Net.Http.Headers;
using WebClient.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("Configuration.json", optional: false, reloadOnChange: true);

var managerIp = builder.Configuration["Manager:IpAddress"] ?? "127.0.0.1";
var managerPort = builder.Configuration["Manager:Port"] ?? "5000";
var managerBaseUrl = $"http://{managerIp}:{managerPort}";

builder.Services.AddRazorPages();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddSingleton<NetworkStateService>();

builder.Services
    .AddHttpClient("ManagerClient", client =>
    {
        client.BaseAddress = new Uri(managerBaseUrl);

        client.Timeout = Timeout.InfiniteTimeSpan;

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = false
        };
    });

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024;
    options.ValueLengthLimit = 256;
    options.MultipartHeadersLengthLimit = 1024;
    options.BufferBody = false;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 3L * 1024 * 1024 * 1024;
    options.ListenAnyIP(8080);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.MapRazorPages();

app.Run();
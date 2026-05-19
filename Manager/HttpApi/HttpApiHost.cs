using Common.Http.Dtos;
using Manager.Core;
using Manager.Processing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Manager.HttpApi;

public class HttpApiHost
{
    private readonly WebApplication _app;
    private readonly ISessionHub _sessionHub;
    private readonly ManagerService _managerService;
    private readonly ManagerConfig _config;
    private readonly ILogger<HttpApiHost> _logger;

    public HttpApiHost(
        ISessionHub sessionHub,
        ManagerService managerService,
        ManagerConfig config,
        ILogger<HttpApiHost> logger)
    {
        _sessionHub = sessionHub;
        _managerService = managerService;
        _config = config;
        _logger = logger;

        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 3L * 1024 * 1024 * 1024;
        });
        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.ListenAnyIP(config.Network.HttpPort);
            opts.Limits.MaxRequestBodySize = 1024 * 1024 * 1024;
        });
        _app = builder.Build();

        RegisterEndpoints();
    }

    private void RegisterEndpoints()
    {
        // POST /upload
        _app.MapPost("/upload", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data" });

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault(f => f.Name == "file");

            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "Файл не предоставлен" });

            using var stream = file.OpenReadStream();
            var (valid, error) = await WavValidator.ValidateAsync(stream, file.FileName);
            if (!valid)
                return Results.BadRequest(new { error = error });

            var session = _sessionHub.CreateSession(Path.GetFileNameWithoutExtension(file.FileName));

            var inputPath = Path.Combine(_config.Directories.Input, $"{session.SessionId}.wav");
            using (var fs = new FileStream(inputPath, FileMode.Create, FileAccess.Write))
                await file.CopyToAsync(fs);

            session.InputFilePath = inputPath;

            _ = Task.Run(() => _managerService.ProcessSessionAsync(session, ct), ct);

            _logger.LogInformation("Сессия {SessionId} создана", session.SessionId);

            return Results.Accepted($"/progress/{session.SessionId}", new { SessionId = session.SessionId });
        }).DisableAntiforgery();

        // GET /progress/{sessionId}
        _app.MapGet("/progress/{sessionId}", (string sessionId) =>
        {
            if (!_sessionHub.TryGetSession(sessionId, out var session))
                return Results.NotFound(new { error = "Сессия не найдена" });

            _managerService.UpdateSessionProgress(session);

            if (session.IsFinalized && !string.IsNullOrEmpty(session.ResultFilePath) && File.Exists(session.ResultFilePath))
            {
                return Results.File(
                    new FileStream(session.ResultFilePath, FileMode.Open, FileAccess.Read, FileShare.Read),
                    "text/plain",
                    session.ResultFileName);
            }

            return Results.Json(new ProgressResponseDto
            {
                EarliestExtractSegmentStart = session.LatestExtractStart,
                InputFileDuration = session.InputDuration,
                LatestTranscriptionEnd = session.LatestTranscriptionEnd
            });
        });

        _app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _app.StartAsync(ct);
        _logger.LogInformation("HTTP API запущен на порту {Port}", _config.Network.HttpPort);
    }
}
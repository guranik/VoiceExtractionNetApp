using Common.Http.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using WebClient.Services;
using Common.Http.Utils;

namespace WebClient.Pages;

public class IndexModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IndexModel> _logger;
    private readonly NetworkStateService _networkState;

    public IndexModel(
        IHttpClientFactory httpClientFactory,
        ILogger<IndexModel> logger,
        NetworkStateService networkState)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _networkState = networkState;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostUploadAsync(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Файл не выбран" });

        if (file.Length > 1024L * 1024 * 1024)
            return BadRequest(new { error = "Файл слишком большой" });

        var attempt = 0;
        var baseDelay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(30);

        while (true)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var client = _httpClientFactory.CreateClient("ManagerClient");

                using var content = new MultipartFormDataContent();

                using var stream = file.OpenReadStream();

                using var streamContent = new StreamContent(stream);

                streamContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);

                content.Add(streamContent, "file", file.FileName);
                content.Add(new StringContent(file.FileName), "clientFileName");

                _logger.LogInformation("Попытка отправки файла в Manager (attempt {Attempt})...", attempt + 1);

                var response = await client.PostAsync(
                    "/upload",
                    content,
                    timeoutCts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    var sessionData = JsonSerializer.Deserialize<SessionResponse>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    if (sessionData?.SessionId is not null)
                    {
                        HttpContext.Session.SetString("SessionId", sessionData.SessionId);
                        HttpContext.Session.SetString("OriginalFileName", file.FileName);

                        _logger.LogInformation("Upload успешен");

                        return new OkObjectResult(new
                        {
                            sessionId = sessionData.SessionId
                        });
                    }
                }

                var errorContent = await response.Content.ReadAsStringAsync();

                return StatusCode((int)response.StatusCode, new
                {
                    error = errorContent
                });
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Timeout upload");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Manager недоступен");
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Сетевой IO exception");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка upload");
            }

            _logger.LogWarning("Ожидание переподключения к Manager (attempt {Attempt})...", attempt + 1);

            while (!_networkState.IsNetworkAvailable)
            {
                await Task.Delay(1000);
            }

            await ExponentialBackoff.WaitAsync(attempt, baseDelay, maxDelay, HttpContext.RequestAborted);
            attempt++;
        }
    }

    [HttpGet]
    public async Task<IActionResult> OnGetProgressAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "SessionId не указан" });

        var attempt = 0;
        var baseDelay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(30);

        while (true)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                var client = _httpClientFactory.CreateClient("ManagerClient");

                var response = await client.GetAsync(
                    $"/progress/{sessionId}",
                    timeoutCts.Token);

                if (IsBinaryResponse(response))
                    return await HandleFileDownloadAsync(response);

                var json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var progress = JsonSerializer.Deserialize<ProgressResponseDto>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    if (progress is not null)
                    {
                        return new OkObjectResult(new
                        {
                            type = "progress",
                            data = progress
                        });
                    }
                }

                return Content(json, "application/json");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Progress timeout");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Manager недоступен during polling");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка polling");
            }

            while (!_networkState.IsNetworkAvailable)
            {
                await Task.Delay(1000);
            }

            await ExponentialBackoff.WaitAsync(attempt, baseDelay, maxDelay, HttpContext.RequestAborted);
            attempt++;
        }
    }

    private async Task<IActionResult> HandleFileDownloadAsync(HttpResponseMessage response)
    {
        var fileName =
            response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName ??
            HttpContext.Session.GetString("OriginalFileName")?.Replace(".wav", "_processed.wav") ??
            "result.wav";

        var fileBytes = await response.Content.ReadAsByteArrayAsync();

        return File(fileBytes, "application/octet-stream", fileName);
    }

    private bool IsBinaryResponse(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        var disposition = response.Content.Headers.ContentDisposition;

        return contentType is "application/octet-stream" or "binary/octet-stream"
               || disposition?.FileName is not null
               || disposition?.FileNameStar is not null;
    }
}

public class SessionResponse
{
    public string? SessionId { get; set; }
}
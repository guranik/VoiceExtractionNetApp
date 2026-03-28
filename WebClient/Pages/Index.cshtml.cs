using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Common.Http.Dto;
using System.Text.Json;

namespace WebClient.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            IHttpClientFactory httpClientFactory,
            ILogger<IndexModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostUploadAsync(IFormFile file)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "Файл не выбран или пуст." });

            if (file.Length > 500 * 1024 * 1024) // 500 MB лимит
                return BadRequest(new { error = "Превышен максимальный размер файла." });

            var client = _httpClientFactory.CreateClient("ManagerClient");
            using var content = new MultipartFormDataContent();

            using var stream = file.OpenReadStream();
            using var streamContent = new StreamContent(stream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);

            content.Add(streamContent, "file", file.FileName);
            content.Add(new StringContent(file.FileName), "clientFileName");

            try
            {
                var response = await client.PostAsync("/upload", content);

                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var sessionData = JsonSerializer.Deserialize<SessionResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (sessionData?.SessionId is not null)
                    {
                        HttpContext.Session.SetString("SessionId", sessionData.SessionId);
                        HttpContext.Session.SetString("OriginalFileName", file.FileName);
                        return new OkObjectResult(new { sessionId = sessionData.SessionId });
                    }
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { error = errorContent });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ошибка подключения к Manager");
                return StatusCode(503, new { error = "Сервис обработки временно недоступен." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> OnGetProgressAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return BadRequest(new { error = "SessionId не указан" });

            var client = _httpClientFactory.CreateClient("ManagerClient");

            try
            {
                var response = await client.GetAsync($"/progress/{sessionId}");

                if (IsBinaryResponse(response))
                {
                    return await HandleFileDownloadAsync(response);
                }

                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    var progress = JsonSerializer.Deserialize<ProgressResponseDto>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (progress is not null)
                        return new OkObjectResult(new { type = "progress", data = progress });
                }
                return Content(json, "application/json");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ошибка опроса прогресса для сессии {SessionId}", sessionId);
                return StatusCode(503, new { error = "Не удалось получить статус обработки" });
            }
        }

        private async Task<IActionResult> HandleFileDownloadAsync(HttpResponseMessage response)
        {
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
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

            return contentType is "application/octet-stream" or "binary/octet-stream" ||
                   disposition?.FileName is not null ||
                   disposition?.FileNameStar is not null;
        }
    }

    public class SessionResponse
    {
        public string? SessionId { get; set; }
    }
}
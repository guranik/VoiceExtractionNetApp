using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Manager.Core;

public class SessionState
{
    public string SessionId { get; }
    public string ClientFileName { get; set; }
    public string? ResultFileName { get; set; }   
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public string? InputFilePath { get; set; }
    public string? ResultFilePath { get; set; }
    public int LatestExtractStart { get; set; }
    public int InputDuration { get; set; }
    public int LatestTranscriptionEnd { get; set; }
    public int? PollingExtract { get; set; }
    public int? PollingTranscribe { get; set; }

    public SessionState(string sessionId, string clientFileName)
    {
        SessionId = sessionId;
        ClientFileName = clientFileName;
        ResultFileName = Path.GetFileNameWithoutExtension(clientFileName) + ".txt";
    }
}

public interface ISessionHub
{
    SessionState CreateSession(string clientFileName);
    bool TryGetSession(string sessionId, out SessionState? session);
    void UpdateProgress(string sessionId, int extractStart, int duration, int transcribeEnd);
    void MarkFinalized(string sessionId, string? resultFilePath = null);
    void RemoveSession(string sessionId);
    void InitializeExtractCounter(string sessionId, int extractCount);
    void DecrementExtractCounter(string sessionId);
    void IncrementTranscribeCounter(string sessionId);
    void DecrementTranscribeCounter(string sessionId);
    bool CanFinalize(string sessionId);
}

public class SessionHub : ISessionHub
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    private readonly TimeSpan _retentionAfterFinalize;
    private readonly TimeSpan _cleanupInterval;
    private readonly Timer? _cleanupTimer;
    private readonly ILogger<SessionHub>? _logger;
    private readonly ManagerConfig _config;

    public SessionHub(
        ManagerConfig config,
        ILogger<SessionHub>? logger = null,
        TimeSpan? retentionAfterFinalize = null,
        TimeSpan? cleanupInterval = null)
    {
        _logger = logger;
        _retentionAfterFinalize = retentionAfterFinalize ?? TimeSpan.FromHours(1);
        _cleanupInterval = cleanupInterval ?? TimeSpan.FromMinutes(5);

        // Запускаем фоновую очистку
        _cleanupTimer = new Timer(DoCleanup, null, _cleanupInterval, _cleanupInterval);
        _config = config;
    }

    public SessionState CreateSession(string clientFileName)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..16];
        var session = new SessionState(sessionId, clientFileName);
        _sessions[sessionId] = session;
        return session;
    }

    public bool TryGetSession(string sessionId, out SessionState? session)
        => _sessions.TryGetValue(sessionId, out session);

    public void UpdateProgress(string sessionId, int extractStart, int duration, int transcribeEnd)
    {
        if (TryGetSession(sessionId, out var session))
        {
            session.LatestExtractStart = extractStart;
            session.InputDuration = duration;
            session.LatestTranscriptionEnd = transcribeEnd;
            session.LastActivityUtc = DateTime.UtcNow;
        }
    }

    public void MarkFinalized(string sessionId, string? resultFilePath = null)
    {
        if (TryGetSession(sessionId, out var session))
        {
            session.IsFinalized = true;
            session.ResultFilePath = resultFilePath;
            session.FinalizedAtUtc = DateTime.UtcNow;
            session.LastActivityUtc = DateTime.UtcNow;
        }
    }

    public IEnumerable<string> GetStaleFinalizedSessionIds(DateTime threshold)
    {
        var now = DateTime.UtcNow;
        return _sessions
            .Where(kv => kv.Value.IsFinalized &&
                        kv.Value.FinalizedAtUtc.HasValue &&
                        kv.Value.FinalizedAtUtc.Value < threshold)
            .Select(kv => kv.Key)
            .ToList(); // ToList для снятия блокировки на итерацию
    }

    private void DoCleanup(object? state)
    {
        try
        {
            var threshold = DateTime.UtcNow - _retentionAfterFinalize;
            var staleIds = GetStaleFinalizedSessionIds(threshold);

            foreach (var sessionId in staleIds)
            {
                // 1. Удаляем файлы сессии
                CleanSessionFiles(sessionId);

                // 2. Удаляем из хранилища
                RemoveSession(sessionId);

                _logger?.LogInformation("Очищена финализированная сессия {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при очистке сессий");
        }
    }

    private void CleanSessionFiles(string sessionId)
    {
        try
        {
            if (!string.IsNullOrEmpty(_config.Directories.Input))
            {
                var inputFiles = Directory.GetFiles(_config.Directories.Input, $"{sessionId}.*");
                foreach (var file in inputFiles)
                {
                    if (Path.GetFileNameWithoutExtension(file).Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(file);
                            _logger?.LogDebug("Удалён входной файл: {FilePath}", file);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Не удалось удалить входной файл: {FilePath}", file);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(_config.Directories.Output))
            {
                var outputFiles = Directory.GetFiles(_config.Directories.Output, $"{sessionId}.*");
                foreach (var file in outputFiles)
                {
                    if (Path.GetFileNameWithoutExtension(file).Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(file);
                            _logger?.LogDebug("Удалён выходной файл: {FilePath}", file);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Не удалось удалить выходной файл: {FilePath}", file);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при очистке файлов сессии {SessionId}", sessionId);
        }
    }

    public void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Инициализирует только счётчик Extract (первый этап пайплайна).
    /// Lock не требуется — вызывается однопоточно на старте обработки сессии.
    /// </summary>
    public void InitializeExtractCounter(string sessionId, int extractCount)
    {
        if (TryGetSession(sessionId, out var session))
        {
            session.PollingExtract = extractCount;
            // PollingTranscribe остаётся null — транскрипции ещё не созданы
            session.LastActivityUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Уменьшает счётчик Extract. Вызывается при успешном завершении Extract-задачи.
    /// </summary>
    public void DecrementExtractCounter(string sessionId)
    {
        if (TryGetSession(sessionId, out var session))
        {
            lock (session)
            {
                if (session.PollingExtract.HasValue && session.PollingExtract > 0)
                    session.PollingExtract--;
            }
            session.LastActivityUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Увеличивает счётчик Transcribe. 
    /// Если счётчик ещё null (первый переход из Extract), инициализирует его значением 1.
    /// </summary>
    public void IncrementTranscribeCounter(string sessionId)
    {
        if (TryGetSession(sessionId, out var session))
        {
            lock (session)
            {
                session.PollingTranscribe = session.PollingTranscribe.HasValue
                    ? session.PollingTranscribe.Value + 1
                    : 1;
            }
            session.LastActivityUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Уменьшает счётчик Transcribe. Вызывается при успешном завершении Transcribe-задачи.
    /// </summary>
    public void DecrementTranscribeCounter(string sessionId)
    {
        if (TryGetSession(sessionId, out var session))
        {
            lock (session)
            {
                if (session.PollingTranscribe.HasValue && session.PollingTranscribe > 0)
                    session.PollingTranscribe--;
            }
            session.LastActivityUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Возвращает true, когда оба счётчика инициализированы (не null) и равны 0.
    /// </summary>
    public bool CanFinalize(string sessionId)
    {
        if (!TryGetSession(sessionId, out var session))
            return false;

        return session.PollingExtract.HasValue &&
               session.PollingTranscribe.HasValue &&
               session.PollingExtract == 0 &&
               session.PollingTranscribe == 0;
    }

    public void Dispose()
    {
        _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer?.Dispose();
    }
}
using System.Collections.Concurrent;

namespace Manager.Core;

public class SessionState
{
    public string SessionId { get; }
    public string ClientFileName { get; set; }
    public string? ResultFileName { get; set; }   

    public bool IsFinalized { get; set; }
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public string? InputFilePath { get; set; }
    public string? ResultFilePath { get; set; }
    public int LatestExtractStart { get; set; }
    public int InputDuration { get; set; }
    public int LatestTranscriptionEnd { get; set; }

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
    SessionState? GetSession(string sessionId);
    bool TryGetSession(string sessionId, out SessionState? session);
    void UpdateProgress(string sessionId, int extractStart, int duration, int transcribeEnd);
    void MarkFinalized(string sessionId, string? resultFilePath = null);
    void RemoveSession(string sessionId);
}

public class SessionHub : ISessionHub
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    public SessionState CreateSession(string clientFileName)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..16];
        var session = new SessionState(sessionId, clientFileName);
        _sessions[sessionId] = session;
        return session;
    }

    public SessionState? GetSession(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s : null;

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
            session.LastActivityUtc = DateTime.UtcNow;
        }
    }

    public void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }
}
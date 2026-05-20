using Manager.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Manager;

namespace Manager.Tests.Core;

public class SessionHubTests : IDisposable
{
    private readonly SessionHub _hub;
    private readonly ManagerConfig _config;

    public SessionHubTests()
    {
        _config = new ManagerConfig
        {
            Directories = new DirectoryConfig
            {
                Input = Path.Combine(Path.GetTempPath(), "input_test"),
                Output = Path.Combine(Path.GetTempPath(), "output_test")
            },
            Network = new NetworkConfig
            {
                ManagerPort = 5001,
                HttpPort = 5000
            }
        };

        Directory.CreateDirectory(_config.Directories.Input);
        Directory.CreateDirectory(_config.Directories.Output);

        // Отключаем таймер очистки для детерминированности тестов
        _hub = new SessionHub(_config, NullLogger<SessionHub>.Instance,
            retentionAfterFinalize: TimeSpan.FromMilliseconds(100),
            cleanupInterval: TimeSpan.FromMilliseconds(500));
    }

    public void Dispose()
    {
        _hub?.Dispose();
        try { Directory.Delete(_config.Directories.Input, true); } catch { }
        try { Directory.Delete(_config.Directories.Output, true); } catch { }
    }

    [Fact]
    public void CreateSession_GeneratesIdAndStoresSession()
    {
        var session = _hub.CreateSession("audio.wav");

        Assert.NotNull(session);
        Assert.Equal(16, session.SessionId.Length);
        Assert.Equal("audio.wav", session.ClientFileName);
        Assert.True(_hub.TryGetSession(session.SessionId, out var retrieved));
        Assert.Same(session, retrieved);
    }

    [Fact]
    public void TryGetSession_ReturnsFalse_ForUnknownId()
    {
        var found = _hub.TryGetSession("unknown", out var session);

        Assert.False(found);
        Assert.Null(session);
    }

    [Fact]
    public void UpdateProgress_UpdatesSessionFields()
    {
        var session = _hub.CreateSession("f.wav");
        var before = session.LastActivityUtc;

        _hub.UpdateProgress(session.SessionId, 10, 100, 50);

        Assert.Equal(10, session.LatestExtractStart);
        Assert.Equal(100, session.InputDuration);
        Assert.Equal(50, session.LatestTranscriptionEnd);
        Assert.True(session.LastActivityUtc >= before);
    }

    [Fact]
    public void MarkFinalized_SetsFinalizationProperties()
    {
        var session = _hub.CreateSession("f.wav");
        var before = session.LastActivityUtc;

        _hub.MarkFinalized(session.SessionId, "/path/result.txt");

        Assert.True(session.IsFinalized);
        Assert.Equal("/path/result.txt", session.ResultFilePath);
        Assert.NotNull(session.FinalizedAtUtc);
        Assert.True(session.FinalizedAtUtc.Value >= before);
        Assert.True(session.LastActivityUtc >= before);
    }

    [Fact]
    public void RemoveSession_DeletesFromStorage()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.RemoveSession(session.SessionId);

        var found = _hub.TryGetSession(session.SessionId, out _);
        Assert.False(found);
    }

    [Fact]
    public void InitializeExtractCounter_SetsExtractCount()
    {
        var session = _hub.CreateSession("f.wav");

        _hub.InitializeExtractCounter(session.SessionId, 5);

        Assert.Equal(5, session.PollingExtract);
        Assert.Null(session.PollingTranscribe);
    }

    [Fact]
    public void DecrementExtractCounter_DecreasesValue()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.InitializeExtractCounter(session.SessionId, 3);

        _hub.DecrementExtractCounter(session.SessionId);
        Assert.Equal(2, session.PollingExtract);

        _hub.DecrementExtractCounter(session.SessionId);
        _hub.DecrementExtractCounter(session.SessionId);
        Assert.Equal(0, session.PollingExtract);
    }

    [Fact]
    public void DecrementExtractCounter_DoesNotGoBelowZero()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.InitializeExtractCounter(session.SessionId, 1);

        _hub.DecrementExtractCounter(session.SessionId);

        Assert.Equal(0, session.PollingExtract);
    }

    [Fact]
    public void IncrementTranscribeCounter_InitializesAndIncrements()
    {
        var session = _hub.CreateSession("f.wav");

        _hub.IncrementTranscribeCounter(session.SessionId);
        Assert.Equal(1, session.PollingTranscribe);

        _hub.IncrementTranscribeCounter(session.SessionId);
        _hub.IncrementTranscribeCounter(session.SessionId);
        Assert.Equal(3, session.PollingTranscribe);
    }

    [Fact]
    public void DecrementTranscribeCounter_DecreasesValue()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.IncrementTranscribeCounter(session.SessionId);
        _hub.IncrementTranscribeCounter(session.SessionId);

        _hub.DecrementTranscribeCounter(session.SessionId);
        Assert.Equal(1, session.PollingTranscribe);

        _hub.DecrementTranscribeCounter(session.SessionId);
        Assert.Equal(0, session.PollingTranscribe);
    }

    [Fact]
    public void DecrementTranscribeCounter_DoesNotGoBelowZero()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.IncrementTranscribeCounter(session.SessionId);

        _hub.DecrementTranscribeCounter(session.SessionId);

        Assert.Equal(0, session.PollingTranscribe);
    }

    [Fact]
    public void CanFinalize_ReturnsTrue_WhenBothCountersZero()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.InitializeExtractCounter(session.SessionId, 2);

        _hub.DecrementExtractCounter(session.SessionId);
        _hub.DecrementExtractCounter(session.SessionId);
        _hub.IncrementTranscribeCounter(session.SessionId);
        _hub.DecrementTranscribeCounter(session.SessionId);

        Assert.True(_hub.CanFinalize(session.SessionId));
    }

    [Fact]
    public void CanFinalize_ReturnsFalse_WhenExtractNotZero()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.InitializeExtractCounter(session.SessionId, 1);
        _hub.IncrementTranscribeCounter(session.SessionId);
        _hub.DecrementTranscribeCounter(session.SessionId);

        Assert.False(_hub.CanFinalize(session.SessionId));
    }

    [Fact]
    public void CanFinalize_ReturnsFalse_WhenTranscribeNotZero()
    {
        var session = _hub.CreateSession("f.wav");
        _hub.InitializeExtractCounter(session.SessionId, 1);
        _hub.DecrementExtractCounter(session.SessionId);
        _hub.IncrementTranscribeCounter(session.SessionId);
        _hub.IncrementTranscribeCounter(session.SessionId);

        Assert.False(_hub.CanFinalize(session.SessionId));
    }

    [Fact]
    public void CanFinalize_ReturnsFalse_WhenCountersNotInitialized()
    {
        var session = _hub.CreateSession("f.wav");

        Assert.False(_hub.CanFinalize(session.SessionId));
    }
}
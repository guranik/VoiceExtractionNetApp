using System.IO;
using Manager.Processing;
using Manager.Tests.TestHelpers;
using NAudio.Wave;
using Xunit;

namespace Manager.Tests.Processing;

public class AudioSplitterTests
{
    private const string TestSessionId = "test_session_01";

    [Fact]
    public void GetInputDurationSec_ReturnsCorrectDuration()
    {
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "test.wav");

        CreateSilentWav(file, seconds: 3);

        var duration = AudioSplitter.GetInputDurationSec(file);

        Assert.InRange(duration, 2, 3);
    }

    [Fact]
    public void Split_CreatesSegments()
    {
        using var dir = new TempDirectory();
        var input = Path.Combine(dir.Path, "input.wav");
        var output = Path.Combine(dir.Path, "out");

        CreateSilentWav(input, seconds: 5);

        AudioSplitter.Split(input, output, maxSegSec: 2, ratio: 1, sessionId: TestSessionId);

        var files = Directory.GetFiles(output);

        Assert.True(files.Length >= 2);
        // Проверяем, что имена файлов содержат префикс SessionId
        Assert.All(files, f => Assert.Contains(TestSessionId, Path.GetFileName(f)));
    }

    [Fact]
    public void GetLatestExtractSegmentStartSec_ReturnsZero_WhenNoDir()
    {
        var sec = AudioSplitter.GetLatestExtractSegmentStartSec(
            "not_exists",
            null,
            TestSessionId);

        Assert.Equal(0, sec);
    }

    [Fact]
    public void GetLatestExtractSegmentStartSec_ReturnsCorrectStart_WhenSegmentsExist()
    {
        using var dir = new TempDirectory();
        var extractDir = Path.Combine(dir.Path, "extract");
        Directory.CreateDirectory(extractDir);

        // Создаём тестовый сегмент с именем SessionId_00-00-05.000.wav
        var segmentFile = Path.Combine(extractDir, $"{TestSessionId}_00-00-05.000.wav");
        CreateSilentWav(segmentFile, seconds: 1);

        var sec = AudioSplitter.GetLatestExtractSegmentStartSec(
            extractDir,
            null,
            TestSessionId);

        Assert.Equal(5, sec);
    }

    [Fact]
    public void GetLatestTranscriptionEndSec_ReturnsZero_WhenNoDir()
    {
        var sec = AudioSplitter.GetLatestTranscriptionEndSec(
            "not_exists",
            TestSessionId);

        Assert.Equal(0, sec);
    }

    [Fact]
    public void GetLatestTranscriptionEndSec_ReturnsCorrectEnd_WhenTranscriptionsExist()
    {
        using var dir = new TempDirectory();
        var transcriptionsDir = Path.Combine(dir.Path, "transcriptions");
        Directory.CreateDirectory(transcriptionsDir);

        // Создаём тестовый файл транскрипции с именем SessionId_00-00-10.000_30.000.txt
        var transcriptionFile = Path.Combine(transcriptionsDir, $"{TestSessionId}_00-00-10.000_30.000.txt");
        File.WriteAllText(transcriptionFile, "Test transcription");

        var sec = AudioSplitter.GetLatestTranscriptionEndSec(
            transcriptionsDir,
            TestSessionId);

        Assert.Equal(40, sec); // 10 + 30 = 40 секунд
    }

    [Fact]
    public void Split_CreatesSegmentsWithCorrectPrefix()
    {
        using var dir = new TempDirectory();
        var input = Path.Combine(dir.Path, "input.wav");
        var output = Path.Combine(dir.Path, "out");
        var sessionId = "abc123";

        CreateSilentWav(input, seconds: 5);

        AudioSplitter.Split(input, output, maxSegSec: 2, ratio: 1, sessionId: sessionId);

        var files = Directory.GetFiles(output);

        Assert.All(files, f => Assert.StartsWith($"{sessionId}_", Path.GetFileName(f)));
    }

    [Fact]
    public void GetLatestExtractSegmentStartSec_IgnoresOtherSessions()
    {
        using var dir = new TempDirectory();
        var extractDir = Path.Combine(dir.Path, "extract");
        Directory.CreateDirectory(extractDir);

        // Создаём сегменты для разных сессий
        var segmentFile1 = Path.Combine(extractDir, $"session1_00-00-05.000.wav");
        var segmentFile2 = Path.Combine(extractDir, $"session2_00-00-10.000.wav");
        CreateSilentWav(segmentFile1, seconds: 1);
        CreateSilentWav(segmentFile2, seconds: 1);

        // Проверяем, что для session1 возвращается 5, а не 10
        var sec = AudioSplitter.GetLatestExtractSegmentStartSec(
            extractDir,
            null,
            "session1");

        Assert.Equal(5, sec);
    }

    private static void CreateSilentWav(string path, int seconds)
    {
        using var writer = new WaveFileWriter(
            path,
            WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));

        var buffer = new float[16000];
        for (int i = 0; i < seconds; i++)
            writer.WriteSamples(buffer, 0, buffer.Length);
    }
}
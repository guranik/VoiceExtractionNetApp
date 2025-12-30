using System.IO;
using Manager.Processing;
using Manager.Tests.TestHelpers;
using Microsoft.VisualBasic;
using NAudio.Wave;
using Xunit;

namespace Manager.Tests.Processing;

public class AudioSplitterTests
{
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

        AudioSplitter.Split(input, output, maxSegSec: 2, ratio: 1);

        var files = Directory.GetFiles(output);

        Assert.True(files.Length >= 2);
    }

    [Fact]
    public void GetLatestExtractSegmentStartSec_ReturnsZero_WhenNoDir()
    {
        var sec = AudioSplitter.GetLatestExtractSegmentStartSec(
            "not_exists",
            null);

        Assert.Equal(0, sec);
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

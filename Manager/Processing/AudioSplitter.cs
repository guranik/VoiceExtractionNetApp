using System;
using System.IO;
using NAudio.Wave;

static class AudioSplitter
{
    public static void Split(
        string inputFile,
        string outputDir,
        double maxSegSec,
        double ratio)
    {
        using var reader = new AudioFileReader(inputFile);
        double pos = 0;

        while (reader.CurrentTime.TotalSeconds <
               reader.TotalTime.TotalSeconds)
        {
            double remaining =
                reader.TotalTime.TotalSeconds - reader.CurrentTime.TotalSeconds;

            double cut =
                remaining < maxSegSec * ratio * 2
                    ? remaining
                    : maxSegSec * ratio;

            var start = TimeSpan.FromSeconds(pos);
            var name = $"{start:hh\\-mm\\-ss}.wav";

            var outPath = Path.Combine(outputDir, name);
            WriteSegment(reader, outPath, cut);

            pos += cut;
        }
    }

    private static void WriteSegment(AudioFileReader reader, string path, double seconds)
    {
        int samples = (int)(seconds * reader.WaveFormat.SampleRate);
        var buffer = new float[samples * reader.WaveFormat.Channels];

        reader.Read(buffer, 0, buffer.Length);

        WaveFileWriter.CreateWaveFile(path,
            new RawSourceWaveStream(
                new MemoryStream(buffer.SelectMany(BitConverter.GetBytes).ToArray()),
                reader.WaveFormat));
    }
}

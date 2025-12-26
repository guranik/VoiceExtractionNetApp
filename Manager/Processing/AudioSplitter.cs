using System;
using System.IO;
using System.Linq;
using NAudio.Wave;

static class AudioSplitter
{
    public static void Split(
        string inputFile,
        string outputDir,
        double maxSegSec,
        double ratio)
    {
        Directory.CreateDirectory(outputDir);

        using var reader = new AudioFileReader(inputFile);
        double pos = 0;

        Console.WriteLine($"Начало дробления файла: {inputFile}");

        while (reader.CurrentTime < reader.TotalTime)
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

            if (!WriteSegment(reader, outPath, cut))
                break;

            pos += cut;
        }
    }

    private static bool WriteSegment(
        AudioFileReader reader,
        string path,
        double seconds)
    {
        int samples = (int)(seconds * reader.WaveFormat.SampleRate);
        var buffer = new float[samples * reader.WaveFormat.Channels];

        int read = reader.Read(buffer, 0, buffer.Length);
        if (read == 0)
            return false;

        var bytes = buffer
            .Take(read)
            .SelectMany(BitConverter.GetBytes)
            .ToArray();

        using var ms = new MemoryStream(bytes);
        using var raw = new RawSourceWaveStream(ms, reader.WaveFormat);

        WaveFileWriter.CreateWaveFile(path, raw);

        return true;
    }
}

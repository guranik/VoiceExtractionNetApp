using System;
using System.IO;
using System.Linq;
using NAudio.Wave;

namespace Manager.Processing;
public static class AudioSplitter
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

    public static int GetLatestExtractSegmentStartSec(string extractDir, string inputFile)
    {
        if (!Directory.Exists(extractDir))
            return 0;

        var files = Directory.GetFiles(extractDir);
        if (files.Length == 0)
            return inputFile != null
                ? GetInputDurationSec(inputFile)
                : 0;

        var file = Directory.GetFiles(extractDir)
            .OrderBy(f => f)
            .FirstOrDefault();

        if (file == null)
            return 0;

        var name = Path.GetFileNameWithoutExtension(file);
        var parts = name.Split('-').Select(int.Parse).ToArray();

        return parts[0] * 3600 + parts[1] * 60 + parts[2];
    }

    public static int GetInputDurationSec(string inputFile)
    {
        using var reader = new AudioFileReader(inputFile);
        return (int)reader.TotalTime.TotalSeconds;
    }

    public static int GetLatestTranscriptionEndSec(string transcriptionsDir)
    {
        if (!Directory.Exists(transcriptionsDir))
            return 0;

        var file = Directory.GetFiles(transcriptionsDir, "*.txt")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (file == null)
            return 0;

        var name = Path.GetFileNameWithoutExtension(file);
        var parts = name.Split('_');

        if (parts.Length != 2)
            return 0;

        double start = ParseStartTime(parts[0]); 
        double duration = ParseDuration(parts[1]);  

        return (int)Math.Round(start + duration);
    }


    private static double ParseStartTime(string value)
    {
        // 00-02-00.000
        var hms = value.Split('-');
        if (hms.Length != 3)
            return 0;

        int h = int.Parse(hms[0]);
        int m = int.Parse(hms[1]);

        var secParts = hms[2].Split('.');
        int s = int.Parse(secParts[0]);
        int ms = secParts.Length > 1 ? int.Parse(secParts[1]) : 0;

        return h * 3600 + m * 60 + s + ms / 1000.0;
    }

    private static double ParseDuration(string value)
    {
        // 30.000
        return double.Parse(
            value,
            System.Globalization.CultureInfo.InvariantCulture
        );
    }
}

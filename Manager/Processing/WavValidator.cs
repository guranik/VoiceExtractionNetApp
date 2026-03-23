using System.Text;

namespace Manager.Processing;

public static class WavValidator
{
    private const string CHUNK_FMT = "fmt ";
    private const short FORMAT_PCM = 1;
    private static readonly int[] SUPPORTED_SAMPLE_RATES = { 8000, 16000 };

    public static async Task<(bool IsValid, string? ErrorMessage)> ValidateAsync(Stream stream, string fileName)
    {
        if (stream.CanSeek) stream.Position = 0;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".wav")
            return (false, $"Ожидался .wav, получено: {ext}");

        if (stream.Length < 44)
            return (false, "Файл слишком мал для корректного WAV");

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var riff = reader.ReadBytes(4);
        if (Encoding.ASCII.GetString(riff) != "RIFF")
            return (false, "Отсутствует RIFF заголовок");

        stream.Seek(4, SeekOrigin.Current);
        var wave = reader.ReadBytes(4);
        if (Encoding.ASCII.GetString(wave) != "WAVE")
            return (false, "Отсутствует WAVE метка");

        while (stream.Position < stream.Length)
        {
            if (stream.Position + 8 > stream.Length) break;

            var chunkId = reader.ReadBytes(4);
            var chunkSize = reader.ReadInt32();

            if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                return (false, "Повреждённая структура WAV");

            if (Encoding.ASCII.GetString(chunkId) == CHUNK_FMT)
            {
                if (chunkSize < 16) return (false, "Некорректный fmt чанк");

                var formatTag = reader.ReadInt16();
                var channels = reader.ReadInt16();
                var sampleRate = reader.ReadInt32();

                if (formatTag != FORMAT_PCM)
                    return (false, "Поддерживается только PCM-формат");
                if (channels != 1)
                    return (false, "Поддерживается только моно");
                if (!SUPPORTED_SAMPLE_RATES.Contains(sampleRate))
                    return (false, $"Поддерживается 8/16 кГц, получено: {sampleRate} Гц");

                return (true, null);
            }
            else
            {
                stream.Seek(chunkSize, SeekOrigin.Current);
            }
        }

        return (false, "Не найден блок 'fmt '");
    }
}
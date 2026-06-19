using System.Diagnostics;
using Worker;

namespace Worker.PythonProcessors;
public class TranscribeProcessor : BasePythonProcessor
{

    public TranscribeProcessor(string script, string inputDir, string outputDir, int index, string modelName)
        : base(script, inputDir, outputDir, index, $"--model \"{modelName}\"")
    {
    }

    protected override void ConfigureEnvironment(ProcessStartInfo startInfo)
    {
        // Транскрайберу (например, Whisper) не нужны специфичные переменные окружения для PyTorch.
        // Если в будущем понадобятся оффлайн-кэши специфично для Whisper, их можно будет добавить сюда.
    }
}
using System.Diagnostics;
using Worker;

namespace Worker.PythonProcessors;
public class ExtractProcessor : BasePythonProcessor
{
    public ExtractProcessor(string script, string inputDir, string outputDir, int index, int maxDuration)
        : base(script, inputDir, outputDir, index, $"--max-duration {maxDuration}")
    {
    }

    protected override void ConfigureEnvironment(ProcessStartInfo startInfo)
    {
        var env = startInfo.EnvironmentVariables;
        var sitePackages = Path.Combine(AppDir, "python", "Lib", "site-packages");
        var torchLib = Path.Combine(sitePackages, "torch", "lib");
        var torchaudioLib = Path.Combine(sitePackages, "torchaudio", "lib");
        var ffmpegBin = Path.Combine(AppDir, "ffmpeg", "bin");
        var pythonLib = Path.Combine(AppDir, "python", "Lib");

        // 1. Python runtime isolation (critical for embeddable Python)
        env["PYTHONHOME"] = Path.Combine(AppDir, "python");
        env["PYTHONPATH"] = $"{sitePackages};{pythonLib}";

        // 2. I/O protocol reliability
        env["PYTHONUNBUFFERED"] = "1";
        env["PYTHONIOENCODING"] = "utf-8";
        env["PYTHONDONTWRITEBYTECODE"] = "1";

        // Добавляем специфичные библиотеки в PATH
        env["PATH"] = $"{torchLib};{torchaudioLib};{ffmpegBin};{env["PATH"]}";

        // Настраиваем оффлайн кэши для PyTorch и HuggingFace
        string cacheDir = Path.Combine(AppDir, "cache");
        env["TORCH_HOME"] = Path.Combine(cacheDir, "torch");
        env["TORCH_HUB_DIR"] = Path.Combine(cacheDir, "torch_hub");
        env["XDG_CACHE_HOME"] = cacheDir;
        env["HF_HOME"] = Path.Combine(cacheDir, "huggingface");
    }
}


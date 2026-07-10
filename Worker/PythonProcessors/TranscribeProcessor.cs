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
        var ffmpegDir = Path.Combine(AppDir, "ffmpeg");

        var env = startInfo.EnvironmentVariables;

        env["PATH"] = $"{ffmpegDir};{env["PATH"]}";
    }
}
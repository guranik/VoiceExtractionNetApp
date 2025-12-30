using Manager.Scheduling;
using Xunit;

namespace Manager.Tests.Scheduling;

public class TaskQueuesTests
{
    [Fact]
    public void AddExtract_AddsFile()
    {
        TaskQueues.AddExtract("file1");

        Assert.Contains("file1", TaskQueues.ExtractFiles);
    }

    [Fact]
    public void AddTranscribe_AddsFile()
    {
        TaskQueues.AddTranscribe("file2");

        Assert.Contains("file2", TaskQueues.TranscribeFiles);
    }
}

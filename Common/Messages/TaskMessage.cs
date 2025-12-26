using Common.Messages;
using Common.Models;

public sealed class TaskMessage : MessageBase
{
    public override MessageType Type =>
        TaskType == TaskType.Extract
            ? MessageType.ExtractTask
            : MessageType.TranscribeTask;

    public TaskType TaskType { get; set; }
    public string SourceFileName { get; set; }
    public List<FilePayload> Files { get; set; } = new();
}

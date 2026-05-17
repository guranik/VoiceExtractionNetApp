using Common.Tcp.Models;

namespace Common.Tcp.Messages;
public sealed class TaskMessage : MessageBase
{
    public override MessageType Type =>
        TaskType == TaskType.Extract
            ? MessageType.ExtractTask
            : MessageType.TranscribeTask;

    public TaskType TaskType { get; set; }
    public string SourceFileName { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public List<FilePayload> Files { get; set; } = new();
}

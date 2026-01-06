namespace Common.Messages;

public sealed class WorkerReadyMessage : MessageBase
{
    public int ExtractThreads { get; init; }
    public int TranscribeThreads { get; init; }
    public override MessageType Type => MessageType.WorkerReady;
}

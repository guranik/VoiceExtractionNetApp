namespace Common.Messages;

public sealed class WorkerHelloMessage : MessageBase
{
    public int ExtractThreads { get; init; }
    public int TranscribeThreads { get; init; }

    public WorkerHelloMessage()
    {
        Type = MessageType.WorkerHello;
    }
}

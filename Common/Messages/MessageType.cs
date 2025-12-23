// Messages/MessageType.cs
namespace Common.Messages;

public enum MessageType
{
    Ack,
    WorkerHello,
    HeartBeat,

    ExtractTask,
    TranscribeTask,

    FileChunk,
    CancelTask
}

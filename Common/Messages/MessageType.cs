namespace Common.Messages;

public enum MessageType
{
    WorkerHello,
    Ack,
    HeartBeat,

    ExtractTask,
    TranscribeTask,

    ClientInput,
    ClientProgress
}

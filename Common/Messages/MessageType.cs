namespace Common.Messages;

public enum MessageType
{
    WorkerHello,
    HeartBeat,

    ExtractTask,
    TranscribeTask,

    ClientInput,
    ClientProgress
}

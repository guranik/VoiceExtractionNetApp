namespace Common.Tcp.Messages;

public enum MessageType
{
    WorkerReady,
    Ack,
    HeartBeat,

    ExtractTask,
    TranscribeTask,

    ClientFile,
    ClientProgress
}

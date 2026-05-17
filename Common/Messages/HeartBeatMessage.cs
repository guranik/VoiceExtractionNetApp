namespace Common.Tcp.Messages;

public sealed class HeartBeatMessage : MessageBase
{
    public override MessageType Type => MessageType.HeartBeat;
}

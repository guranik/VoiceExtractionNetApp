namespace Common.Tcp.Messages;

public sealed class HeartBeatMessage : MessageBase
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public override MessageType Type => MessageType.HeartBeat;
}

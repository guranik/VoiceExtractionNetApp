namespace Common.Messages;

public sealed class HeartBeatMessage : MessageBase
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public HeartBeatMessage()
    {
        Type = MessageType.HeartBeat;
    }
}

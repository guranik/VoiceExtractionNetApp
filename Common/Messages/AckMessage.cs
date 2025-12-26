namespace Common.Messages;

public sealed class AckMessage : MessageBase
{
    public Guid AckedMessageId { get; init; }

    public AckMessage()
    {
        Type = MessageType.Ack;
    }
}

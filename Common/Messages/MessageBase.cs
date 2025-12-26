namespace Common.Messages;

public abstract class MessageBase
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public virtual MessageType Type { get; init; }
}

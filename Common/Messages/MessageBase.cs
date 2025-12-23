// Messages/MessageBase.cs
namespace Common.Messages;

public abstract class MessageBase
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public MessageType Type { get; init; }
}

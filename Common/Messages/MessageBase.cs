namespace Common.Tcp.Messages;

public abstract class MessageBase
{
    public virtual MessageType Type { get; init; }
}

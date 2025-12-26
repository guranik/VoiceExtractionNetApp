using Common.Models;

namespace Common.Messages;

public sealed class ClientInputMessage : MessageBase
{
    public override MessageType Type => MessageType.ClientInput;

    public FilePayload File { get; set; }
}

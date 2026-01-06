using Common.Models;

namespace Common.Messages;

public sealed class ClientFileMessage : MessageBase
{
    public override MessageType Type => MessageType.ClientFile;

    public FilePayload File { get; set; }
}

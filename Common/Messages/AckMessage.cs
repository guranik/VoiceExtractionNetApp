namespace Common.Tcp.Messages
{
    public class AckMessage : MessageBase
    {
        public override MessageType Type => MessageType.Ack;
    }
}

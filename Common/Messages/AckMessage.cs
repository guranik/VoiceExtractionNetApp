using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Messages
{
    public class AckMessage : MessageBase
    {
        public override MessageType Type => MessageType.Ack;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Messages
{
    public class ClientProgressMessage : MessageBase
    {
        public override MessageType Type => MessageType.ClientProgress;
        public int EarliestExtractSegmentStart { get; set; } 
        public int InputFileDuration { get; set; }
        public int LatestTranscriptionEnd { get; set; }
    }
}

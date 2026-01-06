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
        public int LatestExtractSegmentStart { get; set; } 
        public int InputFileDuration { get; set; }
        public int TotalTranscribeSegments { get; set; }
        public int TotalTranscriptions { get; set; }
    }
}

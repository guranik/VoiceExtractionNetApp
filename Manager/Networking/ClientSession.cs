using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Manager.Networking
{
    public class ClientSession
    {
        public string SessionId { get; }
        public TcpClient Client { get; }
        public string ClientFileName { get; set; }
        public bool IsFinalized { get; set; }

        public ClientSession(TcpClient client, string sessionId, string clientFileName)
        {
            Client = client;
            SessionId = sessionId;
            ClientFileName = clientFileName;
            IsFinalized = false;
        }
    }
}

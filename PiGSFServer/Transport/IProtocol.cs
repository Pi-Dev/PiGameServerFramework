using PiGSF.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Transport
{
    internal interface IProtocol
    {
        // this function takes data and if a message has been created, returns the message
        public List<byte[]> AddData(Span<byte> bytes);
        public byte[] CreateMessage(byte[] source);
    }
}

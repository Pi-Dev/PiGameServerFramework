using PiGSF.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Server
{
    internal class GameServerProtocol : IProtocol
    {
        private const int HeaderSize = 2;
        private List<byte> buffer = new();

        public List<byte[]> AddData(Span<byte> bytes)
        {
            buffer.AddRange(bytes.ToArray());
            var result = new List<byte[]>();
            while (buffer.Count >= HeaderSize)
            {
                int messageLength = BitConverter.ToUInt16(buffer.ToArray(), 0);
                if (buffer.Count < HeaderSize + messageLength) break; // Not enough data for the full message
                var message = buffer.GetRange(HeaderSize, messageLength).ToArray();
                result.Add(message);
                buffer.RemoveRange(0, HeaderSize + messageLength);
            }
            return result;
        }

        public byte[] CreateMessage(byte[] source)
        {
            var ms = new MemoryStream(source.Length + 2);
            var bw = new BinaryWriter(ms);
            bw.Write((ushort)source.Length);
            bw.Write(source, 0, source.Length);
            return ms.ToArray();
        }
    }
}

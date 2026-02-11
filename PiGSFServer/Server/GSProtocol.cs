using System;
using System.Collections.Generic;
using System.IO;

namespace PiGSF.Server
{
    internal class GameServerProtocol : IProtocol
    {
        private const int HeaderSize = sizeof(ushort);
        private const int ExtraHeaderSize = sizeof(uint);
        private const ushort ExtendedLengthMarker = 0xFFFF;
        private List<byte> buffer = new();

        public List<byte[]> AddData(Span<byte> bytes)
        {
            buffer.AddRange(bytes.ToArray());
            var result = new List<byte[]>();
            bool processing = true;

            while (processing)
            {
                if (buffer.Count < HeaderSize)
                {
                    processing = false;
                    continue;
                }

                ushort header = BitConverter.ToUInt16(buffer.ToArray(), 0);
                int totalHeaderSize = HeaderSize;
                uint messageLength;

                if (header == ExtendedLengthMarker)
                {
                    if (buffer.Count < HeaderSize + ExtraHeaderSize)
                    {
                        processing = false;
                        continue;
                    }

                    messageLength = BitConverter.ToUInt32(buffer.ToArray(), HeaderSize);
                    totalHeaderSize = HeaderSize + ExtraHeaderSize;
                }
                else
                {
                    messageLength = header;
                }

                if (buffer.Count < totalHeaderSize + messageLength)
                {
                    processing = false;
                    continue;
                }

                var message = buffer.GetRange(totalHeaderSize, (int)messageLength).ToArray();
                result.Add(message);
                buffer.RemoveRange(0, totalHeaderSize + (int)messageLength);
            }

            return result;
        }

        public byte[] CreateMessage(byte[] source)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            if (source.Length < ExtendedLengthMarker)
            {
                bw.Write((ushort)source.Length);
            }
            else
            {
                bw.Write(ExtendedLengthMarker);
                bw.Write((uint)source.Length);
            }

            bw.Write(source);
            return ms.ToArray();
        }
    }
}

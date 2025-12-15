using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PiGSF.Server
{
    internal class WebSocketProtocol : IProtocol
    {
        internal bool compressed = false;
        private const int HeaderSize = 2;
        private readonly List<byte> buffer = new();

        public List<byte[]> AddData(Span<byte> bytes)
        {
            buffer.AddRange(bytes.ToArray());
            var messages = new List<byte[]>();

            while (buffer.Count >= HeaderSize)
            {
                int payloadOffset = HeaderSize;
                byte finAndOpcode = buffer[0];
                byte maskAndLength = buffer[1];

                int opcode = finAndOpcode & 0b0000_1111;
                bool isMasked = (maskAndLength & 0b1000_0000) != 0;
                ulong payloadLenU = (ulong)(maskAndLength & 0b0111_1111);

                if (payloadLenU == 126)
                {
                    if (buffer.Count < payloadOffset + 2) break;
                    payloadLenU = (ulong)((buffer[payloadOffset] << 8) | buffer[payloadOffset + 1]);
                    payloadOffset += 2;
                }
                else if (payloadLenU == 127)
                {
                    if (buffer.Count < payloadOffset + 8) break;

                    Span<byte> lenBytes = stackalloc byte[8];
                    for (int i = 0; i < 8; i++) lenBytes[i] = buffer[payloadOffset + i];
                    payloadLenU = BinaryPrimitives.ReadUInt64BigEndian(lenBytes);
                    payloadOffset += 8;
                }

                if (payloadLenU > int.MaxValue) throw new InvalidOperationException("Frame payload too large.");
                int payloadLength = (int)payloadLenU;

                byte[] maskingKey = Array.Empty<byte>();
                if (isMasked)
                {
                    if (buffer.Count < payloadOffset + 4) break;
                    maskingKey = buffer.Skip(payloadOffset).Take(4).ToArray();
                    payloadOffset += 4;
                }

                if (buffer.Count < payloadOffset + payloadLength) break;

                var payload = buffer.Skip(payloadOffset).Take(payloadLength).ToArray();
                if (isMasked)
                    for (int i = 0; i < payload.Length; i++)
                        payload[i] ^= maskingKey[i & 3];

                if (opcode == 0x01) payload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload));
                else if (opcode == 0x08) { messages.Add(null); return messages; }
                else if (opcode == 0x09 || opcode == 0x0A)
                {
                    buffer.RemoveRange(0, payloadOffset + payloadLength);
                    continue; // don't surface ping/pong as app messages
                }

                messages.Add(payload);
                buffer.RemoveRange(0, payloadOffset + payloadLength);
            }

            return messages;
        }

        public static byte[] CreateFrame(byte[] payload, bool isText = true, bool isFinal = true)
        {
            var frame = new List<byte>(2 + payload.Length + 10);

            byte finAndOpcode = (byte)((isFinal ? 0b1000_0000 : 0x00) | (isText ? 0x01 : 0x02));
            frame.Add(finAndOpcode);

            int len = payload.Length;
            if (len <= 125)
                frame.Add((byte)len);
            else if (len <= ushort.MaxValue)
            {
                frame.Add(126);
                Span<byte> tmp = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(tmp, (ushort)len);
                frame.AddRange(tmp.ToArray());
            }
            else
            {
                frame.Add(127);
                Span<byte> tmp = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(tmp, (ulong)len);
                frame.AddRange(tmp.ToArray());
            }

            frame.AddRange(payload);
            return frame.ToArray();
        }

        public byte[] CreateMessage(byte[] source) => CreateFrame(source, false, true);
    }
}

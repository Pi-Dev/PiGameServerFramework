using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Server
{

    internal class WebSocketProtocol : IProtocol
    {
        internal bool compressed = false;
        private const int HeaderSize = 2;
        private List<byte> buffer = new();

        public List<byte[]> AddData(Span<byte> bytes)
        {
            buffer.AddRange(bytes.ToArray());
            var messages = new List<byte[]>();

            while (buffer.Count >= HeaderSize)
            {
                int payloadOffset = HeaderSize;
                byte finAndOpcode = buffer[0];
                byte maskAndLength = buffer[1];

                bool isFinalFrame = (finAndOpcode & 0b10000000) != 0;
                int opcode = finAndOpcode & 0b00001111;
                bool isMasked = (maskAndLength & 0b10000000) != 0;
                int payloadLength = maskAndLength & 0b01111111;

                // Handle extended payload lengths
                if (payloadLength == 126)
                {
                    if (buffer.Count < payloadOffset + 2) break;
                    payloadLength = (buffer[payloadOffset] << 8) | buffer[payloadOffset + 1];
                    payloadOffset += 2;
                }
                else if (payloadLength == 127)
                {
                    if (buffer.Count < payloadOffset + 8) break;
                    payloadLength = (int)BitConverter.ToUInt64(buffer.GetRange(payloadOffset, 8).ToArray(), 0);
                    payloadOffset += 8;
                }

                // Handle masking key
                byte[] maskingKey = Array.Empty<byte>();
                if (isMasked)
                {
                    if (buffer.Count < payloadOffset + 4) break;
                    maskingKey = buffer.Skip(payloadOffset).Take(4).ToArray();
                    payloadOffset += 4;
                }

                // Check for full payload
                if (buffer.Count < payloadOffset + payloadLength) break;

                // Extract and unmask payload
                var payload = buffer.Skip(payloadOffset).Take(payloadLength).ToArray();
                if (isMasked)
                {
                    for (int i = 0; i < payload.Length; i++)
                    {
                        payload[i] ^= maskingKey[i % 4];
                    }
                }

                if (opcode == 0x01) // Text frame, reconvert to normalize
                {
                    payload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload));
                }
                else if (opcode == 0x08) // Close frame
                {
                    messages.Add(null);
                    return messages; // trigger a disconnect
                }
                else if (opcode == 0x09) // Ping frame
                {
                }
                else if (opcode == 0x0A) // Pong frame
                {
                }

                messages.Add(payload);
                buffer.RemoveRange(0, payloadOffset + payloadLength);

                // Stop if this is the final frame
                if (isFinalFrame) break;
            }

            return messages;
        }

        public static byte[] CreateFrame(byte[] payload, bool isText = true, bool isFinal = true)
        {
            List<byte> frame = new();

            // FIN bit and Opcode
            byte finAndOpcode = (byte)((isFinal ? 0b10000000 : 0x00) | (isText ? 0x01 : 0x02));
            frame.Add(finAndOpcode);

            // Mask bit and Payload length
            if (payload.Length <= 125)
            {
                frame.Add((byte)payload.Length);
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                frame.Add(126);
                frame.AddRange(BitConverter.GetBytes((ushort)payload.Length));
            }
            else
            {
                frame.Add(127);
                frame.AddRange(BitConverter.GetBytes((ulong)payload.Length));
            }

            // Add payload (unmasked for server)
            frame.AddRange(payload);

            return frame.ToArray();
        }

        public byte[] CreateMessage(byte[] source)
        {
            return CreateFrame(source, false, true);
        }
    }
}

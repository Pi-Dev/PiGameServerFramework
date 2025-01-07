using PiGSF.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Server.Utils
{
    public class MessageBuilder : BinaryWriter
    {
        // Builder API
        readonly int sz = ServerConfig.HeaderSize;
        MemoryStream? m_Stream;
        public MessageBuilder() : base(new MemoryStream())
        {
            m_Stream = BaseStream as MemoryStream;
            switch (sz)
            {
                case 1: Write((byte)0); break;
                case 2: Write((ushort)0); break;
                case 4: Write((uint)0); break;
            }
        }
        public byte[] ToArray() // Patches the header
        {
            var len = m_Stream!.Length - sz;
            m_Stream.Position = 0;
            switch (sz)
            {
                case 1: Write((byte)len); break;
                case 2: Write((ushort)len); break;
                case 4: Write((uint)len); break;
            }
            m_Stream.Position = m_Stream.Length;
            return m_Stream.ToArray();
        }
    }

    static public class Message
    {
        public static byte[] Create(byte[] source)
        {
            var ms = new MemoryStream((int)(source.Length + ServerConfig.HeaderSize));
            var bw = new BinaryWriter(ms);
            switch (ServerConfig.HeaderSize)
            {
                case 1: bw.Write((byte)source.Length); break;
                case 2: bw.Write((ushort)source.Length); break;
                case 4: bw.Write((uint)source.Length); break;
            }
            bw.Write(source, 0, source.Length);
            return ms.ToArray();
        }
        public static byte[] Create(string str)
        {
            var strBytes = Encoding.UTF8.GetBytes(str);
            var ms = new MemoryStream((int)(strBytes.Length + ServerConfig.HeaderSize));
            var bw = new BinaryWriter(ms);
            switch (ServerConfig.HeaderSize)
            {
                case 1: bw.Write((byte)strBytes.Length); break;
                case 2: bw.Write((ushort)strBytes.Length); break;
                case 4: bw.Write((uint)strBytes.Length); break;
            }
            bw.Write(strBytes, 0, strBytes.Length);
            return ms.ToArray();
        }
    }
}

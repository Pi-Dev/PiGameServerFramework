using PiGSF.Client.Transport;
using PiGSFClient.Transport;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Client
{
    public class Client
    {
        // Networking
        ITransport transport;
        public void SendBytes(byte[] data) => transport.SendBytes(data);
        public void SendString(string data) => transport.SendString(data);
        public void Close() { transport.Close(); transport.Dispose(); }
        public bool isConnected => transport == null ? false : transport.IsConnected();

        // Message queue
        internal ConcurrentQueue<byte[]> messages = new();
        public List<byte[]> GetMessages()
        {
            List<byte[]> res = new();
            while (messages.TryDequeue(out var result)) res.Add(result);
            return res;
        }
        public byte[]? GetMessage()
        {
            if (messages.TryDequeue(out var result)) return result;
            return null;
        }

        // Connect
        public void Connect(string address, int port)
        {
            Exception? ex = null;
            try
            {
                var tcp = new TCPTransport(this);
                tcp.Connect(address, port);
                transport = tcp;
            }
            catch (Exception e)
            {
                ex = e;
                var tcp = new TCPTransport(this);
                try
                {
                    var ws = new WSTransport(this);
                    ws.Connect(address, port); ex = null;
                }
                catch (Exception) { ex = e; }
            }
            if (ex != null) throw ex;
        }
    }
}

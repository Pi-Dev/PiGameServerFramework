using PiGSF.Client;
using PiGSF.Client.Transport;
using System.Buffers;
using System.Net.Sockets;
using System.Net;
using System.Text;
using PiGSF.Client.Utils;

namespace PiGSFClient.Transport
{
    internal class TCPTransport : ITransport
    {
        Client client;
        public TCPTransport(Client c)
        {
            client = c;
        }
        private bool disposedValue;

        // TCP or Websocket connection
        public Stream tcpStream { get; private set; }
        bool isConnected;
        TcpClient tcpClient = null;

        public async Task Connect(string address)
        {
            IPEndPoint addr = IPEndPoint.Parse(address);

            // unguarded, client code must handle exception
            tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            await tcpClient.ConnectAsync(addr);
            var stream = tcpClient.GetStream();
            tcpStream = stream;

            // Set the delegates
            var abs = ArrayPool<byte>.Shared;
            int sz = ClientConfig.HeaderSize;

            // Thread to receive messages into the message queue
            var recvThread = new Thread(() =>
            {
                try
                {
                    while (tcpClient.Connected)
                    {
                        var hdrb = abs.Rent(sz);
                        stream.ReadExactly(hdrb, 0, sz);
                        int size = ClientConfig.HeaderSize switch
                        {
                            1 => hdrb[0],
                            2 => BitConverter.ToInt16(hdrb),
                            4 => BitConverter.ToInt32(hdrb),
                            _ => 0
                        };
                        abs.Return(hdrb);
                        if (size > 0)
                        {
                            byte[] buffer = new byte[size];
                            stream.ReadExactly(buffer, 0, size);
                            client.messages.Enqueue(buffer);
                        }
                    }
                    Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
                finally
                {
                    isConnected = false;
                    tcpClient.Dispose();
                }
                isConnected = false;
                tcpClient.Dispose();
            });
            recvThread.Start();
            isConnected = true;
        }

        void ITransport.SendBytes(byte[] data)
        {
            try { tcpStream.Write(Message.Create(data)); } catch { };
        }
        void ITransport.SendString(string data)
        {
            try { tcpStream.Write(Message.Create(Encoding.UTF8.GetBytes(data))); } catch { };
        }
        bool ITransport.IsConnected() => isConnected;
        void ITransport.Close()
        {
            try
            {
                tcpClient.Close(); tcpClient.Dispose(); isConnected = false;
            }
            catch (Exception) { }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    tcpStream.Dispose();
                }
                disposedValue = true;
            }
        }

        void IDisposable.Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

    }
}


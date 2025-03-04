﻿using PiGSF.Client;
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

        public void Connect(string address, int port)
        {
            // unguarded, client code must handle exception
            tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            tcpClient.Connect(address, port);
            var stream = tcpClient.GetStream();
            tcpStream = stream;
            tcpStream.Write(Encoding.UTF8.GetBytes("GS"), 0, 2); // GS Protocol


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
                        stream.ReadExactly(hdrb, 0, sz); // This is the usual rest place of this thread
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
                            lock (client.messages) Monitor.Pulse(client.messages);
                        }
                    }
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
            recvThread.Name = "TCP Receiver";
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


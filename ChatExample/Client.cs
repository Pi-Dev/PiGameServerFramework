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
    public class Client()
    {
        string playerData;
        public Client(string playerData) : this()
        {
            this.playerData = playerData;
        }

        // API for control
        public delegate void SenderBytes(byte[] data);
        public SenderBytes SendBytes;
        public delegate void SenderString(string data);
        public SenderString SendString;

        // Message queue
        ConcurrentQueue<byte[]> messages = new();
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

        public Action Close;
        public bool isConnected { get; private set; }

        // TCP or Websocket connection

        public Stream tcpStream { get; private set; }
        public ClientWebSocket webSocket { get; private set; }

        // Smart connect for usage with Unity games that may get WebGL port
        public async Task Connect(IPAddress address, int port)
        {
            Exception? ex = null;
            // Attempt Socket connection first
            try { await ConnectTCP(address, port); }
            catch (Exception e)
            {
                ex = e;
                try { await ConnectWS(address, port); ex = null; }
                catch (Exception) { ex = e; }
            }
            if (ex != null) throw ex;
        }

        public async Task ConnectTCP(IPAddress address, int port)
        {
            // unguarded, client code must handle exception
            TcpClient tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(address, port);
            var stream = tcpClient.GetStream();
            tcpStream = stream;

            // Set the delegates
            var abs = ArrayPool<byte>.Shared;
            int sz = ClientConfig.HeaderSize;
            SendBytes = (data) => stream.Write(ClientAPI.CreateMessage(data));
            SendString = (str) => SendBytes(Encoding.UTF8.GetBytes(str));

            // Thread to receive messages into the message queue
            var recvThread = new Thread(() =>
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
                        messages.Enqueue(buffer);
                    }
                }
                isConnected = false;
                tcpClient.Dispose();
            });
            recvThread.Start();

            // Close
            Close = () => { tcpClient.Close(); tcpClient.Dispose(); isConnected = false; };
            isConnected = true;
        }

        public async Task ConnectWS(IPAddress address, int port)
        {
            ClientWebSocket ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"http://{address}:{port}"), CancellationToken.None);
            webSocket = ws;

            // Set the delegates
            var abs = ArrayPool<byte>.Shared;
            int sz = ClientConfig.HeaderSize;
            SendBytes = (data) => ws.SendAsync(
                new ArraySegment<byte>(ClientAPI.CreateMessage(data)),
                WebSocketMessageType.Binary,
                true, CancellationToken.None);
            SendString = (str) => SendBytes(Encoding.UTF8.GetBytes(str));
            Close = () => ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

            var recvThread = new Thread(() =>
            {
                var bufferStream = new MemoryStream(); // Buffer stream for incoming data
                var receiveBuffer = new byte[1024]; // Temporary buffer for WebSocket data
                while (webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        // Read data from WebSocket
                        var result = webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None).GetAwaiter().GetResult();

                        // Check for WebSocket close
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine("WebSocket connection closed by the server.");
                            break;
                        }
                        bufferStream.Write(receiveBuffer, 0, result.Count);
                        ProcessPacketsFromStream(bufferStream);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in WebSocket recvThread: {ex.Message}");
                        break;
                    }
                }
                isConnected = false;
                ws.Dispose();
            });
            recvThread.Start();
        }

        void ProcessPacketsFromStream(MemoryStream bufferStream)
        {
            bufferStream.Position = 0; // Reset position to start reading

            while (true)
            {
                // Check if enough data exists for the header
                if (bufferStream.Length - bufferStream.Position < ClientConfig.HeaderSize) break;

                // Read the size header
                byte[] hdrb = new byte[ClientConfig.HeaderSize];
                bufferStream.Read(hdrb, 0, ClientConfig.HeaderSize);
                int size = ClientConfig.HeaderSize switch
                {
                    1 => hdrb[0],
                    2 => BitConverter.ToInt16(hdrb, 0),
                    4 => BitConverter.ToInt32(hdrb, 0),
                    _ => 0
                };

                // Check if enough data exists for the complete message
                if (bufferStream.Length - bufferStream.Position < size)
                {
                    bufferStream.Position -= ClientConfig.HeaderSize;
                    break;
                }
                byte[] buffer = new byte[size];
                bufferStream.Read(buffer, 0, size);
                messages.Enqueue(buffer);
            }

            // Preserve unprocessed data
            long remainingBytes = bufferStream.Length - bufferStream.Position;
            if (remainingBytes > 0)
            {
                // Copy remaining data to a new byte array
                byte[] remainingData = bufferStream.ToArray()[(int)bufferStream.Position..];

                // Clear the buffer stream and rewrite only unprocessed data
                bufferStream.SetLength(0);
                bufferStream.Write(remainingData, 0, remainingData.Length);
            }
            else
            {
                // No remaining data, clear the stream
                bufferStream.SetLength(0);
            }

            // Move the position to the end for appending new data
            bufferStream.Position = bufferStream.Length;
        }
    }

    static partial class ClientAPI
    {
        // Create Message
        public static byte[] CreateMessage(byte[] source)
        {
            var ms = new MemoryStream((int)(source.Length + ClientConfig.HeaderSize));
            var bw = new BinaryWriter(ms);
            switch (ClientConfig.HeaderSize)
            {
                case 1: bw.Write((byte)source.Length); break;
                case 2: bw.Write((ushort)source.Length); break;
                case 4: bw.Write((uint)source.Length); break;
            }
            bw.Write(source, 0, source.Length);
            return ms.ToArray();
        }
        public static byte[] CreateMessage(string str)
        {
            var strBytes = Encoding.UTF8.GetBytes(str);
            var ms = new MemoryStream((int)(strBytes.Length + ClientConfig.HeaderSize));
            var bw = new BinaryWriter(ms);
            switch (ClientConfig.HeaderSize)
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

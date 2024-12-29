using PiGSF.Client;
using PiGSF.Client.Transport;
using PiGSF.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace PiGSFClient.Transport
{
    internal class WSTransport : ITransport
    {
        Client client;
        public WSTransport(Client c)
        {
            client = c;
        }
        private bool disposedValue;
        ClientWebSocket webSocket;
        bool isConnected;
        bool ITransport.IsConnected() => isConnected;

        public async Task Connect(string address)
        {
            webSocket = new ClientWebSocket();
            await webSocket.ConnectAsync(new Uri($"http://{address}"), CancellationToken.None);

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
                webSocket.Dispose();
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
                client.messages.Enqueue(buffer);
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

        public void SendBytes(byte[] data)
        {
            webSocket.SendAsync(
                new ArraySegment<byte>(Message.Create(data)),
                WebSocketMessageType.Binary,
                true, CancellationToken.None);
        }

        public void SendString(string str) => SendBytes(Encoding.UTF8.GetBytes(str));

        public void Close()
        {
            webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            isConnected = false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    webSocket.Dispose();
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

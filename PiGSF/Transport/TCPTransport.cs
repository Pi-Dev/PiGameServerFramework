using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using PiGSF.Server;
using PiGSF.Utils;

namespace Transport
{
    public class TcpTransport
    {
        public TcpTransport(Server server)
        {
            this.server = server;
        }
        Server server;

        // Static buffer for header parsing and pool of buffers for message data
        ObjectPooler<byte[]> headerBuffers = new(() => new byte[ServerConfig.HeaderSize]);
        ObjectPooler<byte[]> messageBuffers = new(() => new byte[1024]);
        TcpListener listener;

        public void Listen(int port)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(100);
            Console.WriteLine($"Server started on {port}");
            listener.BeginAcceptTcpClient(OnAccept, null);
        }

        (byte[] bytes, Action Dispose) GetPacketBuffer(int size)
        {
            if (size <= ServerConfig.PolledBuffersSize)
            {
                var buffer = messageBuffers.Buy();
                return (buffer, () => messageBuffers.Recycle(buffer));
            }
            else
            {
                var buffer = new byte[size];
                return (buffer, () => { });
            }
        }

        // Handles client connection and authentication
        async void OnClientConnected(TcpClient client)
        {
            var stream = client.GetStream();
            var hdrb = headerBuffers.Buy();
            await stream.ReadExactlyAsync(hdrb, 0, (int)ServerConfig.HeaderSize);
            int size = BitConverter.ToInt16(hdrb, 0);

            // Avoid DDOS attackers, limit first pack to small size
            if (size < 0 || size > ServerConfig.MaxInitialPacketSize)
            {
                Console.WriteLine("ERROR: Client sending negative or too big header. Disconnecting");
                client.Close(); return;
            }

            var packet = GetPacketBuffer(size);
            await stream.ReadExactlyAsync(packet.bytes, 0, size);

            string data = Encoding.UTF8.GetString(packet.bytes);
            packet.Dispose();

            var player = await server.AuthenticatePlayer(data);
            if (player == null)
            {
                Console.WriteLine("ERROR: Client Unauthorized");
                client.Close(); return;
            }

            Console.WriteLine($"New client connected: {data}");
        }

        void OnAccept(IAsyncResult ar)
        {
            try
            {
                var client = listener!.EndAcceptTcpClient(ar);
                OnClientConnected(client);
                client.NoDelay = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
            listener.BeginAcceptTcpClient(OnAccept, null);
        }

        public async Task<Stream> AcceptConnectionAsync()
        {
            if (listener == null)
            {
                throw new InvalidOperationException("Listener is not started.");
            }

            TcpClient client = await listener.AcceptTcpClientAsync();
            return client.GetStream();
        }

        public static async Task SendAsync(Stream connection, byte[] data)
        {
            try
            {
                await connection.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending data: {ex.Message}");
            }
        }

        public static async Task<byte[]> ReceiveAsync(Stream connection)
        {
            var buffer = new byte[1024];
            try
            {
                int bytesRead = await connection.ReadAsync(buffer, 0, buffer.Length);
                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);
                return data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving data: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            listener?.Stop();
            Console.WriteLine("TCP Transport stopped.");
        }
    }
}

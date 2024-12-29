using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using PiGSF.Server;
using PiGSF.Server.Utils;
using PiGSF.Utils;

namespace Transport
{
    public class TcpTransport: ITransport
    {
        Server server;

        // Static buffer for header parsing and pool of buffers for message data
        ObjectPooler<byte[]> headerBuffers = new(() => new byte[ServerConfig.HeaderSize]);
        ObjectPooler<byte[]> messageBuffers = new(() => new byte[1024]);
        TcpListener listener;

        public void Init(int port, Server serverRef)
        {
            this.server = serverRef;
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
            var hdrb = headerBuffers.Buy();
            (byte[] bytes, Action Dispose)? packet = null;
            string data = string.Empty;
            try
            {
                var stream = client.GetStream();
                client.NoDelay = true;
                await stream.ReadExactlyAsync(hdrb, 0, (int)ServerConfig.HeaderSize);
                int size = BitConverter.ToInt16(hdrb, 0);

                // Avoid DDOS attackers, limit first pack to small size
                if (size < 0 || size > ServerConfig.MaxInitialPacketSize)
                {
                    Console.WriteLine("ERROR: Client sending negative or too big header. Disconnecting");
                    client.Close(); return;
                }

                packet = GetPacketBuffer(size);
                await stream.ReadExactlyAsync(packet.Value.bytes, 0, size);

                data = Encoding.UTF8.GetString(packet.Value.bytes, 0, size);
                packet.Value.Dispose();
                packet = null;

                Console.WriteLine($"New client connected: {data}");
                var player = await server.AuthenticatePlayer(data);
                if (player == null)
                {
                    Console.WriteLine("ERROR: Client Unauthorized");
                    client.Close(); return;
                }

                // Player is connected, create the API and give response
                player._SendData = (data) => { var m = Message.Create(data); stream.WriteAsync(m, 0, m.Length); };
                player._CloseConnection = stream.Close;

                // Send a message to the player to tell him the room details and room id
                var m = new MessageBuilder();
                m.Write(player.activeRoom.Id);
                m.Write(player.activeRoom.GetType().Name);
                player.Send(m.ToArray());

                // Start the receiving routine
                _ = Task.Run(async()=>await ReceiveLoop(client, player));
            }
            catch (Exception ex)
            {
                client.Close();
            }
            finally
            {
                headerBuffers.Recycle(hdrb);
                if (packet.HasValue) packet.Value.Dispose();
            }
        }

        async Task ReceiveLoop(TcpClient client, Player player)
        {
            await Task.Yield();
            var abs = ArrayPool<byte>.Shared;
            int sz = (int)ServerConfig.HeaderSize;
            var stream = client.GetStream();
            while (client.Connected)
            {
                var hdrb = abs.Rent(sz);
                await stream.ReadExactlyAsync(hdrb, 0, sz);
                int size = sz switch
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
                    await stream.ReadExactlyAsync(buffer, 0, size);
                    
                    // Deliver the message to all rooms where player is connected
                    var pm = new Room.PlayerMessage { msg = buffer, pl = player };
                    var rooms = player.rooms;
                    foreach(var r in rooms) r.messageQueue.Enqueue(pm);
                }

            }
            Console.WriteLine("TCPTransport: Task.Run(async Lambda): Client for player " + player.uid + " disconnected");
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

        public void Stop()
        {
            listener?.Stop();
            Console.WriteLine("TCP Transport stopped.");
        }
    }
}

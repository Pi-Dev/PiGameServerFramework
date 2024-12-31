using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
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
    public class TcpTransport : ITransport
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
            listener.Start(5000);
            ServerLogger.Log($"Server started on {port}");
            listener.BeginAcceptTcpClient(OnAcceptConcurrent, null);
        }

        void OnAcceptConcurrent(IAsyncResult ar)
        {
            TcpClient client = null;
            try
            {
                client = listener!.EndAcceptTcpClient(ar);
            }
            catch { };
            if (!stopAccepting) listener.BeginAcceptTcpClient(OnAcceptConcurrent, null);
            else return;

            if (client != null)
                Task.Run(() =>
                {
                    try
                    {
                        OnClientConnected(client);
                    }
                    catch (Exception ex)
                    {
                        ServerLogger.Log($"OnAcceptConcurrent: Error accepting client: {ex.Message}");
                    }
                });
        }

        // Thread: Started from OnAcceptConcurrent
        int call = 0;
        void OnClientConnected(TcpClient client)
        {
            call++;
            if (call % 10 == 0) ServerLogger.Log($"[{call}] OnClientConnected");

            client.NoDelay = true;
            var hdrb = headerBuffers.Buy();
            (byte[] bytes, Action Dispose)? packet = null;
            string data = string.Empty;
            try
            {
                var stream = client.GetStream();
                stream.ReadExactly(hdrb, 0, (int)ServerConfig.HeaderSize);
                int size = BitConverter.ToInt16(hdrb, 0);

                // Avoid DDOS attackers, limit first pack to small size
                if (size < 0 || size > ServerConfig.MaxInitialPacketSize)
                {
                    if (call % 10 == 0) ServerLogger.Log($"[{call}] ERROR: Client sending negative or too big header. Disconnecting");
                    client.Close(); return;
                }

                packet = GetPacketBuffer(size);
                stream.ReadExactly(packet.Value.bytes, 0, size);

                data = Encoding.UTF8.GetString(packet.Value.bytes, 0, size);
                packet.Value.Dispose();
                packet = null;
                if (call % 10 == 0) ServerLogger.Log($"[{call}] Client Data: {data}");
                var player = server.AuthenticatePlayer(data).GetAwaiter().GetResult();
                if (player == null)
                {
                    if (call % 10 == 0) ServerLogger.Log($"[{call}] ERROR: Client Unauthorized");
                    client.Close(); return;
                }

                // Player is connected, create the API and give response
                var messageQueue = new ConcurrentQueue<byte[]>();
                player._SendData = (data) => messageQueue.Enqueue(Message.Create(data));
                player._CloseConnection = stream.Close;

                // Send a message to the player to tell him the room details and room id
                var m = new MessageBuilder();
                m.Write(player.activeRoom.Id);
                m.Write(player.activeRoom.GetType().Name);
                player.Send(m.ToArray());

                // Experiment 1: 
                if (call % 10 == 0) ServerLogger.Log($"[{call}] EXPERIMENT 1 START");
                var sender = SendLoop(client, player, messageQueue);
                var receiver = ReceiveLoop(client, player);
                if (call % 10 == 0) ServerLogger.Log($"[{call}] EXPERIMENT 1 ENDED");

                // Start the I/O routines as 2 separate threads
                //if (call % 10 == 0) ServerLogger.Log($"[{call}] Starting I/O threads");
                //var t = new Thread(()=>SendLoop(client, player, messageQueue));
                //t.Name = $"Client {call}: SendLoop";
                //Thread.CurrentThread.Name = $"Client {call}: ReceiveLoop [repurposed]";
                //ReceiveLoop(client, player);
            }
            catch (IOException ex) { }
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
            try
            {
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
                        foreach (var r in rooms)
                            r.messageQueue.Enqueue(pm);
                    }

                }
            }
            catch (IOException ex) { client.Close(); }
            catch (Exception e)
            {
                ServerLogger.Log(e.Message);
            }
            //ServerLogger.Write("TCPTransport: ReceiveLoop: Client for player " + player.uid + " disconnected");
            player.Disconnect();
        }

        async Task SendLoop(TcpClient client, Player player, ConcurrentQueue<byte[]> queue)
        {
            await Task.Yield();
            var stream = client.GetStream();
            try
            {
                while (client.Connected)
                    if (queue.TryDequeue(out var msg))
                        await stream.WriteAsync(msg);
            }
            catch (Exception e)
            {
                ServerLogger.Log(e.Message);
            }
            //ServerLogger.Write("TCPTransport: SendLoop: Client for player " + player.uid + " disconnected");
            player.Disconnect();
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


        bool stopAccepting = false;
        void OnAccept(IAsyncResult ar)
        {
            try
            {
                var client = listener!.EndAcceptTcpClient(ar);
                if (stopAccepting) return; // just kill it
                OnClientConnected(client);
                client.NoDelay = true;
            }
            catch (Exception ex)
            {
                ServerLogger.Log($"Error accepting client: {ex.Message}");
            }
            if (!stopAccepting) listener.BeginAcceptTcpClient(OnAccept, null);
        }

        public static async Task SendAsync(Stream connection, byte[] data)
        {
            try
            {
                await connection.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                ServerLogger.Log($"Error sending data: {ex.Message}");
            }
        }

        public void Stop()
        {
            listener?.Stop();
            ServerLogger.Log("TCP Transport stopped.");
        }

        public void StopAccepting()
        {
            stopAccepting = true;
            listener.Stop();
        }
    }
}

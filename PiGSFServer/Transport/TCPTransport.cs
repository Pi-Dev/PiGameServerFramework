using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Quic;
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

        // Running on Server Thread once
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
            else return; //stopAccepting

            if (client != null)
            {
                var ClientConnected = new Thread(() => OnClientConnected(client));
                ClientConnected.Name = "TCP OnClientConnected";
                ClientConnected.Start();
            }
        }

        static int CC = 0;
        // Thread: Started from OnAcceptConcurrent
        void OnClientConnected(TcpClient client)
        {
            int cid = CC++;
            ServerLogger.Log($"[{cid}] Connected");
            bool error = true;
            var sd = new Stopwatch();
            sd.Start();

            client.NoDelay = true;
            var hdrb = headerBuffers.Buy();
            (byte[] bytes, Action Dispose)? packet = null;
            string data = string.Empty;
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 2000; // set 2s read timeout for initial packet
                stream.ReadExactly(hdrb, 0, (int)ServerConfig.HeaderSize);
                ServerLogger.Log($"[{cid}] Header T={sd.ElapsedMilliseconds}ms");

                int size = BitConverter.ToInt16(hdrb, 0);

                // Avoid DDOS attackers, limit first pack to small size
                if (size < 0 || size > ServerConfig.MaxInitialPacketSize)
                {
                    ServerLogger.Log($"[{cid}] ERROR: Client sending negative or too big header. T={sd.ElapsedMilliseconds}ms");
                    client.Close(); client.Dispose(); return;
                }

                packet = GetPacketBuffer(size);
                stream.ReadTimeout = 150; // set 150 ms for message payload timeout 
                stream.ReadExactly(packet.Value.bytes, 0, size);

                data = Encoding.UTF8.GetString(packet.Value.bytes, 0, size);
                packet.Value.Dispose();
                packet = null;
                var player = server.AuthenticatePlayer(data).GetAwaiter().GetResult();
                if (player == null)
                {
                    ServerLogger.Log($"ERROR: Client Unauthorized");
                    client.Close(); client.Dispose(); return;
                }

                // Player is connected, create the API and give response
                var messageQueue = new Queue<byte[]>();
                player._SendData = (data) =>
                {
                    lock (messageQueue)
                    {
                        messageQueue.Enqueue(Message.Create(data));
                        Monitor.Pulse(messageQueue);
                    }
                };
                player._CloseConnection = stream.Close;

                // Send a message to the player to tell him the room details and room id
                var m = new MessageBuilder();
                m.Write(player.activeRoom.Id);
                m.Write(player.activeRoom.GetType().Name);
                player.Send(m.ToArray());

                // Experiment 1: 
                //var sender = SendLoop(client, player, messageQueue);
                //var receiver = ReceiveLoop(client, player);

                // Start the sender thread...
                var sender = new Thread(() => SendLoop(client, player, messageQueue));
                sender.Name = $"{player.id} ({player.username}): TCP Send";
                sender.Start();

                // Start the Receiver thread
                var receiver = new Thread(()=>ReceiveLoop(client, player));
                receiver.Name = $"{player.id} ({player.username}): TCP Recv";
                receiver.Start();

                // Repurpose current thread as Receiver Thread
                // Thread.CurrentThread.Name = $"{player.id} ({player.username}): TCP Recv";
                // ReceiveLoop(client, player);
                error = false;
                ServerLogger.Log($"[{cid}] Done, T={sd.ElapsedMilliseconds}ms");

                sender.Join();
                receiver.Join();
                ServerLogger.Log($"[{cid}] Client Cleanup, T={sd.ElapsedMilliseconds}ms");
            }
            catch (IOException ex) { }
            catch (Exception ex)
            {
                client.Close();
                client.Dispose();
            }
            finally
            {
                sd.Stop();
                ServerLogger.Log($"[{cid}] {(error?"ERROR":"Done")} [at finally], T={sd.ElapsedMilliseconds}ms");
                headerBuffers.Recycle(hdrb);
                if (packet.HasValue) packet.Value.Dispose();
            }
        }

        void ReceiveLoop(TcpClient client, Player player)
        {
            var abs = ArrayPool<byte>.Shared;
            int sz = (int)ServerConfig.HeaderSize;
            var stream = client.GetStream();
            try
            {
                while (client.Connected)
                {
                    var hdrb = abs.Rent(sz);
                    stream.ReadTimeout = 120 * 1000; // orphan players that do nothing for 2m?
                    stream.ReadExactly(hdrb, 0, sz); // good wait place!
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
                        stream.ReadTimeout = 3000; // set for message payload timeout 
                        stream.ReadExactly(buffer, 0, size);

                        // Deliver the message to all rooms where player is connected
                        var pm = new Room.PlayerMessage { msg = buffer, pl = player };
                        var rooms = player.rooms;
                        foreach (var r in rooms)
                        {
                            r.messageQueue.EnqueueAndNotify(pm);
                        }
                    }

                }
            }
            catch (IOException ex) { client.Close(); client.Dispose(); }
            catch (Exception e)
            {
                ServerLogger.Log(e.Message);
            }
            //ServerLogger.Write("TCPTransport: ReceiveLoop: Client for player " + player.uid + " disconnected");
            player.Disconnect();
        }

        void SendLoop(TcpClient client, Player player, Queue<byte[]> queue)
        {
            var stream = client.GetStream();
            try
            {
                byte[][] msgs;
                while (client.Connected)
                {
                    lock (queue)
                    {
                        if (queue.Count == 0) Monitor.Wait(queue, 1000);
                        msgs = queue.ToArray();
                        queue.Clear();
                    }
                    foreach (var msg in msgs) stream.Write(msg);
                }
            }
            catch (IOException e) { client.Close(); client.Dispose(); }
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

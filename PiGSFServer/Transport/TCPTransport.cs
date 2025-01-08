using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using PiGSF.Server;
using PiGSF.Server.Utils;
using PiGSF.Utils;

namespace Transport
{
    public class TcpTransport : ITransport
    {
        TcpListener listener;
        List<TCPSocketWorker> workers = new();

        class ClientState
        {
            internal ClientState(TcpClient c, TCPSocketWorker w)
            {
                client = c;
                worker = w;
                stream = c.GetStream();
                socket = stream.Socket;
                ReadMessageState = 0;
            }
            internal TcpClient client;
            internal NetworkStream stream;
            internal Socket socket;
            internal Player player;
            internal TCPSocketWorker worker;
            internal int ReadMessageState;
            internal byte[]? CurrentMessage;
            internal bool IsAuthenticated => player != null;
            internal volatile bool IsAuthenticating = false;
            internal volatile bool disconnectRequested = false;
            internal volatile bool disconnectRecvHandled = false;
            internal volatile bool disconnectSendHandled = false;
            internal Queue<byte[]> pendingReceivedMessages = new();
            internal void AddReceivedMessage(byte[] message)
            {
                if (!IsAuthenticated && !IsAuthenticating)
                {
                    IsAuthenticating = true;
                    Task.Run(async () =>
                    {
                        var p = await Server.AuthenticatePlayer(Encoding.UTF8.GetString(message));
                        if (p == null) socket.Close();
                        else
                        {
                            player = p; // Authenticated
                            player._SendData = (data) => worker.SendMessageQueue.EnqueueAndNotify(new SendPacket { messageWithHeader = data, state = this });
                            player._CloseConnection = () => { socket.Disconnect(false); disconnectRequested = true; };
                            IsAuthenticating = false;
                        }
                    });
                    // begin auth
                }
                else if (!IsAuthenticated && IsAuthenticating)
                {
                    // During auth, only push messages
                    pendingReceivedMessages.Enqueue(message);
                }
                else // When authenticated, process messages
                {
                    pendingReceivedMessages.Enqueue(message);
                    ProcessReceivedMessages();
                }
            }
            void ProcessReceivedMessages()
            {
                while (pendingReceivedMessages.TryDequeue(out var m))
                {
                    // Send to all rooms referenced by the player
                    var rooms = Room.FindAllWithPlayer(player);
                    foreach (Room room in rooms)
                        room.messageQueue.EnqueueAndNotify(
                            new Room.PlayerMessage { pl = player, msg = m });
                }
            }
        }

        // Running on Server Thread once
        public void Init(int port)
        {
            maxClientsPerWorker = ServerConfig.GetInt("TCPClientsPerWorker", 10);
            var t = new Thread(() => TCPAcceptorThread(port));
            t.Name = "TcpTransport Listener";
            t.Start();
        }

        int maxClientsPerWorker = 10;

        // Accepts and registers clients with workers
        void TCPAcceptorThread(int port)
        {
            var sw = new Stopwatch();
            sw.Start();
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(5000);
            while (!Server.ServerStopRequested)
            {
                var client = listener.AcceptTcpClient();
                client.NoDelay = true;
                //ServerLogger.Log($"CONN. T={sw.ElapsedMilliseconds}");
                AddClient(client);
            }
        }
        // Finds a suitable thread worker and registers the client with it
        void AddClient(TcpClient client)
        {
            TCPSocketWorker firstCapableWorker = null, activeWorker = null;
            lock (workers)
            {
                foreach (var t in workers)
                {
                    // attempt to place in avtive thread first
                    if (t.workerCount < maxClientsPerWorker)
                    {
                        if (firstCapableWorker == null) firstCapableWorker = t;
                        if (t.active)
                        {
                            activeWorker = t;
                            break;
                        }
                    }
                }
            }

            if (activeWorker != null) { activeWorker.AddClient(client); return; }
            // Not placed in active worker? place in first capable worker
            if (firstCapableWorker != null) firstCapableWorker.AddClient(client);
            else
            {
                // Hire a new worker & add client to it
                var worker = new TCPSocketWorker(this);
                worker.AddClient(client);
                worker.StartThreads();
            }
        }

        // class with send request for a sender worker
        class SendPacket()
        {
            internal ClientState state;
            internal byte[] messageWithHeader;
        }

        // TCP Worker processes the messages
        // Sender workers do not select on a socket, they process all requests
        // Receiver workers do select on their client list where to receive from
        class TCPSocketWorker
        {
            List<ClientState> clients = new();
            internal volatile bool active = false; // flag if the thread isnt waiting on a Select
            internal ConcurrentQueue<SendPacket> SendMessageQueue = new();
            internal int workerCount => clients.Count;
            Thread sender, receiver;
            internal TcpTransport transport;
            volatile bool requestStop = false;
            ConditionalWeakTable<Socket, ClientState> socketState = new();

            internal TCPSocketWorker(TcpTransport transport)
            {
                this.transport = transport;
                int workerId = transport.workers.Count() + 1;
                ServerLogger.Log($"New TCPSocketWorker ({workerId})");
                lock (transport.workers) transport.workers.Add(this);
                sender = new Thread(TCPSenderWorkerThread);
                receiver = new Thread(TCPReceiverWorkerThread);
                sender.Name = $"TCP Sender[{workerId}]";
                receiver.Name = $"TCP Receiver[{workerId}]";
            }

            internal void StartThreads()
            {
                sender.Start();
                receiver.Start();
            }

            internal void TCPSenderWorkerThread()
            {
                List<Socket> socketsToWrite = new();
                List<ClientState> disposableClients = new();
                List<ClientState> writableClients;
                var requeueBuffer = new List<SendPacket>();
                while (!requestStop)
                {
                    // 1. check, prepare select list & check for disconnects before selecting
                    lock (clients) writableClients = clients.Where(c => !c.disconnectSendHandled).ToList();
                    disposableClients.Clear();
                    foreach (var wc in writableClients)
                    {
                        if (wc.disconnectRequested)
                        {
                            //ServerLogger.Log($"TCP Sender handled disc for {wc.player?.name}");
                            wc.disconnectSendHandled = true;
                            if (wc.disconnectRecvHandled)
                            {
                                //ServerLogger.Log($"TCP Sender DISPOSED {wc.player?.name}");
                                disposableClients.Add(wc);
                            }
                        }
                    }
                    lock (clients) foreach (var d in disposableClients) clients.Remove(d);
                    foreach (var d in disposableClients)
                    {
                        d.player?.Disconnect();
                        d.client.Dispose();
                        writableClients.Remove(d);
                    }
                    socketsToWrite.Clear();
                    foreach (var s in writableClients) socketsToWrite.Add(s.socket);

                    if (socketsToWrite.Count == 0)
                    {
                        lock (SendMessageQueue) Monitor.Wait(SendMessageQueue, 1000);
                        continue;
                    }

                    try
                    {
                        Socket.Select(null, socketsToWrite, null, 5000);

                        while (SendMessageQueue.TryDequeue(out var sd))
                        {
                            if (socketsToWrite.Contains(sd.state.socket))
                            {
                                if (sd.state.socket.Connected) // else it's disconnected, drop the message
                                    sd.state.stream.Write(sd.messageWithHeader, 0, sd.messageWithHeader.Length);
                            }
                            else requeueBuffer.Add(sd);
                        }
                    }
                    catch (Exception) { } // The receiver will handle disconnection

                    lock (SendMessageQueue) Monitor.Wait(SendMessageQueue, 1000);

                    foreach (var packet in requeueBuffer)
                        SendMessageQueue.Enqueue(packet);
                    requeueBuffer.Clear();
                }
            }

            internal void TCPReceiverWorkerThread()
            {
                List<Socket> socketsToRead = new();
                List<ClientState> disposableClients = new();
                List<ClientState> readableClients;

                while (!requestStop)
                {
                    if (clients.Count == 0)
                    {
                        // End redundant workers, keep only one on waiting
                        if (transport.workers.Count > 0)
                        {
                            //Thread.Sleep(1000 * 60 * 10);
                            requestStop = true;
                            lock (transport.workers) transport.workers.Remove(this);
                            //ServerLogger.Log("Worker DIED");
                        }
                        else // Wait for clients
                        {
                            active = false;
                            lock (clients) Monitor.Wait(clients, 1000);
                            active = true;
                        }
                    }
                    else
                    {
                        // 1. check, prepare select list & check for disconnects before selecting
                        lock (clients) readableClients = clients.Where(c => !c.disconnectRecvHandled).ToList();
                        disposableClients.Clear();
                        foreach (var wc in readableClients)
                        {
                            if(!wc.client.Connected) wc.disconnectRequested = true; 
                            if (wc.disconnectRequested)
                            {
                                //ServerLogger.Log($"TCP Receiver handled disc for {wc.player?.name}");
                                wc.player?.Disconnect(); // Must call Disconnect on
                                wc.disconnectRecvHandled = true;
                                if (wc.disconnectSendHandled)
                                {
                                    //ServerLogger.Log($"TCP Receiver DISPOSED {wc.player?.name}");
                                    disposableClients.Add(wc);
                                }
                            }
                        }
                        lock (clients) foreach (var d in disposableClients) clients.Remove(d);
                        foreach (var d in disposableClients)
                        {
                            d.player?.Disconnect();
                            d.client.Dispose();
                            readableClients.Remove(d);
                        }
                        socketsToRead.Clear();
                        foreach (var s in readableClients) socketsToRead.Add(s.socket);    
                        if (socketsToRead.Count == 0) continue;

                        try
                        {
                            active = false;
                            Socket.Select(socketsToRead, null, null, 5000); // usual parking place
                            active = true; // if true the worker will be preferred for new clients
                        }
                        catch (SocketException ex) { }
                        catch (ObjectDisposedException ex) { }
                        catch (Exception ex) { ServerLogger.Log(ex.ToString()); }


                        foreach (var s in socketsToRead)
                        {
                            if (socketState.TryGetValue(s, out ClientState state))
                            {
                                try
                                {
                                    byte[] buffer = new byte[1024];
                                    int bytesRead = state.stream.Read(buffer, 0, buffer.Length);
                                    int offset = 0;
                                    while (bytesRead > 0)
                                    {
                                        switch (state.ReadMessageState)
                                        {
                                            case 0: // Reading the 2-byte header
                                                if (bytesRead - offset >= sizeof(ushort))
                                                {
                                                    int messageLength = BitConverter.ToUInt16(buffer, offset);
                                                    offset += sizeof(ushort);
                                                    state.CurrentMessage = new byte[messageLength];
                                                    state.ReadMessageState = 1; // Move to reading the message
                                                }
                                                else goto BreakBytesRead; // Not enough data for the header
                                                break;

                                            case 1: // Reading the message body
                                                int bytesToCopy = Math.Min(state.CurrentMessage!.Length, bytesRead - offset);
                                                Array.Copy(buffer, offset, state.CurrentMessage, 0, bytesToCopy);
                                                offset += bytesToCopy;

                                                if (offset >= state.CurrentMessage.Length)
                                                {
                                                    state.AddReceivedMessage(state.CurrentMessage);
                                                    state.ReadMessageState = 0; // Reset to reading the next header
                                                    state.CurrentMessage = null;
                                                }
                                                break;
                                        }
                                        if (offset >= bytesRead) break;
                                    }
                                BreakBytesRead: { }
                                }
                                catch (IOException) { state.player?.Disconnect(); }
                                catch (ObjectDisposedException) { state.player?.Disconnect(); }
                                catch (Exception ex) { state.player?.Disconnect(); ServerLogger.Log(ex.ToString()); }
                            }
                        }
                    }
                }
            }

            public void AddClient(TcpClient client)
            {
                var state = new ClientState(client, this);
                socketState.Add(state.socket, state);
                lock (clients)
                {
                    clients.Add(state);
                    Monitor.Pulse(clients);
                }
            }
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
            listener.Stop();
        }
    }
}

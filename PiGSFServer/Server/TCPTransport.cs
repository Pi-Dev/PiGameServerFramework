﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PiGSF.Utils;

namespace PiGSF.Server
{
    public class TcpTransport : ITransport
    {
        TcpListener listener;
        List<TCPSocketWorker> workers = new();
        bool enableInsecureHTTP;
        class ClientState
        {
            internal ClientState(TcpClient c, TCPSocketWorker w)
            {
                client = c;
                worker = w;
                stream = c.GetStream();
                socket = c.Client;
                socket.NoDelay = true;
                socket.Blocking = false;
                socket.ReceiveTimeout = 10000;
                socket.SendTimeout = 10000;
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 3);         // First probe after 3 sec
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);     // Subsequent probes every 2 sec
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);   // 3 failed probes before closing
                ReadMessageState = 0;
            }
            internal TcpClient client;
            internal Socket socket;
            internal System.IO.Stream stream;
            internal IProtocol protocol;
            internal Player player;
            internal TCPSocketWorker worker;
            internal int ReadMessageState;
            internal byte[]? CurrentMessage;
            internal bool IsAuthenticated => player != null;
            internal volatile bool IsAuthenticating = false;
            internal volatile bool disconnectRequested = false;
            internal volatile bool disconnectRecvHandled = false;
            internal volatile bool disconnectSendHandled = false;
            internal volatile bool IsProtocolInitializing = false;
            internal Queue<byte[]> pendingReceivedMessages = new();
            internal void AddReceivedMessage(byte[] message)
            {
                // null message means disconnect, generated by WS/WSS protocol
                if (message == null)
                {
                    disconnectRequested = true;
                    player?.Disconnect();
                    return;
                }
                if (!IsAuthenticated && !IsAuthenticating)
                {
                    IsAuthenticating = true;
                    Task.Run(async () =>
                    {
                        var p = await Server.AuthenticatePlayer(Encoding.UTF8.GetString(message));
                        if (p == null) { socket.Close(); disconnectRequested = true; /* so it gets removed from the Worker */ }
                        else
                        {
                            // Disconnect player if it has disconnect request, otherwise this player will join as connected phantom
                            if (disconnectRequested) p.Disconnect();
                            else
                            {
                                player = p; // Authenticated
                                player._SendData = (data) => worker.SendMessageQueue.EnqueueAndNotify(new SendPacket { message = data, state = this });
                                player._CloseConnection = () => { socket.Disconnect(false); disconnectRequested = true; };
                                IsAuthenticating = false;
                            }
                        }
                    });
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

            void HandleHTTPProtocol(string httpRequest, bool secure) // Runs on Task.Run thread
            {
                var lines = httpRequest.Split("\r\n");

                // Verify HTTP structure and ensure headers end with \r\n\r\n
                if (!(lines[0].Contains("HTTP/") && httpRequest.EndsWith("\r\n\r\n")))
                {
                    client.Close();
                    disconnectRequested = true;
                    return;
                }

                // Parse the request line (e.g., "GET /path HTTP/1.1")
                string[] requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (requestLine.Length < 3)
                {
                    client.Close();
                    disconnectRequested = true;
                    return;
                }

                string method = requestLine[0];
                string path = requestLine[1];

                // Collect headers
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) break;

                    var headerParts = lines[i].Split(':', 2).Select(s => s.Trim()).ToArray();
                    if (headerParts.Length == 2)
                    {
                        headers[headerParts[0]] = headerParts[1];
                    }
                }

                // Check for WebSocket upgrade
                bool isUpgradeToWS = headers.ContainsKey("Upgrade") && headers["Upgrade"].Equals("websocket", StringComparison.OrdinalIgnoreCase);
                bool supportPerMessageDeflate = headers.ContainsKey("Sec-WebSocket-Extensions") && headers["Sec-WebSocket-Extensions"].Contains("permessage-deflate", StringComparison.OrdinalIgnoreCase);
                string? clientKey = headers.ContainsKey("Sec-WebSocket-Key") ? headers["Sec-WebSocket-Key"] : null;

                if (isUpgradeToWS && clientKey != null)
                {
                    // Handle WebSocket upgrade handshake
                    const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                    string acceptKey;
                    using (SHA1 sha1 = SHA1.Create())  // Create SHA1 instance
                    {
                        byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(clientKey + WebSocketGuid));
                        acceptKey = Convert.ToBase64String(hash);
                    }

                    string switchResp = $"HTTP/1.1 101 Switching Protocols\r\n" +
                                        $"Upgrade: websocket\r\n" +
                                        $"Connection: Upgrade\r\n" +
                                        $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
                    byte[] resp = Encoding.UTF8.GetBytes(switchResp);
                    stream.Write(resp, 0, resp.Length);
                    stream.Flush();
                    //socket.Blocking = false;

                    var wsp = new WebSocketProtocol();
                    wsp.compressed = supportPerMessageDeflate;
                    protocol = wsp;
                    IsProtocolInitializing = false;
                    return;
                }

                // Not a WebSocket upgrade; handle as a normal HTTP request
                //socket.Blocking = false;
                var bodyIndex = httpRequest.IndexOf("\r\n\r\n") + 4;
                string body = bodyIndex < httpRequest.Length ? httpRequest.Substring(bodyIndex) : string.Empty;
                var request = new Request(method, path, body)
                {
                    Headers = headers
                };

                Response response;
                try
                {
                    response = RESTManager.HandleRequest(request);
                    if (response.Body == null && response.BinaryData == null) throw new Exception("Null HTTP Response");
                }
                catch (Exception ex)
                {
                    response = new Response(500, "text/plain", $"Internal Server Error: {ex.Message}");
                }

                // Build and send HTTP response
                int byteCount = response.BinaryData != null ? response.BinaryData.Length : Encoding.UTF8.GetByteCount(response!.Body);

                string httpResponse = $"HTTP/1.1 {response.StatusCode} {GetStatusMessage(response.StatusCode)}\r\n" +
                                      $"Content-Type: {byteCount}\r\n" +
                                      $"Content-Length: {byteCount}\r\n" +
                                      $"Connection: Close\r\n" +
                                      string.Join("", response.ExtraHeaders.Select(header => $"{header.Key}: {header.Value}\r\n")) +
                                      "\r\n";

                byte[] httpRespBytes;
                if (response.BinaryData != null)
                {
                    httpRespBytes = Enumerable.Concat(Encoding.UTF8.GetBytes(httpResponse), response.BinaryData).ToArray();
                }
                else
                {
                    httpRespBytes = Encoding.UTF8.GetBytes(httpResponse + response.Body);
                }
                stream.Write(httpRespBytes, 0, httpRespBytes.Length);
                stream.Flush();
                client.Close();
                disconnectRequested = true;
            }

            string GetStatusMessage(int statusCode)
            {
                return statusCode switch
                {
                    200 => "OK",
                    404 => "Not Found",
                    403 => "Forbidden",
                    500 => "Internal Server Error",
                    _ => "Unknown"
                };
            }

            internal void InitProtocol(Span<byte> buffer)
            {
                IsProtocolInitializing = true;
                if (buffer[0] == 'G' && buffer[1] == 'S')
                {
                    var buf = new byte[2];
                    var hs = stream.Read(buf, 0, 2);
                    protocol = new GameServerProtocol();
                    IsProtocolInitializing = false;
                    //socket.Blocking = false;
                    return;
                }
                if (buffer[0] == 0x16)
                {
                    if (Server.serverCertificate == null)
                    {
                        client.Close();
                        disconnectRequested = true;
                        return;
                    }
                    Task.Run(() => // TLS Handshake
                    {
                        socket.Blocking = true;
                        var sslStream = new SslStream(stream, false);
                        try
                        {
                            sslStream.AuthenticateAsServer(Server.serverCertificate);
                            this.stream = sslStream;
                            var buffer = new byte[4196];
                            int bytesRead = sslStream.Read(buffer, 0, buffer.Length);
                            HandleHTTPProtocol(Encoding.UTF8.GetString(buffer, 0, bytesRead), true);
                            socket.Blocking = false;
                        }
                        catch (AuthenticationException e)
                        {
                            if (e.InnerException != null)
                            {
                                //Console.WriteLine("Inner exception: {0}", e.InnerException.Message);
                            }
                            ServerLogger.Log("Authentication failed - " + e.ToString());
                            sslStream.Close();
                            client.Close();
                            disconnectRequested = true;
                            return;
                        }
                    });
                }
                else if (worker.transport.enableInsecureHTTP)
                {
                    Task.Run(() =>
                    {
                        var buffer = new byte[4196];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        HandleHTTPProtocol(Encoding.UTF8.GetString(buffer, 0, bytesRead), false);
                    });
                }
                else
                {
                    client.Close();
                    disconnectRequested = true;
                }
            }
        }

        // Running on Server Thread once
        public void Init(int port)
        {
            maxClientsPerWorker = ServerConfig.GetInt("TCPClientsPerWorker", 10);
            enableInsecureHTTP = ServerConfig.Get("EnableInsecureHTTP", "true") == "true";
            var t = new Thread(() => TCPAcceptorThread(port));
            t.Name = "TcpTransport Listener";
            t.Start();
        }

        int maxClientsPerWorker = 10;

        // Accepts and registers clients with workers
        void TCPAcceptorThread(int port)
        {
            //var sw = new Stopwatch();
            //sw.Start();
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
        class SendPacket
        {
            internal ClientState state;
            internal byte[] message;
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
                                {
                                    var framed = sd.state.protocol.CreateMessage(sd.message);
                                    sd.state.stream.Write(framed, 0, framed.Length);
                                }
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
                const int sz = 1024 * 4;
                byte[] buffer = new byte[sz];

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
                            if (!wc.client.Connected) { wc.disconnectRequested = true; }
                            if (wc.disconnectRequested)
                            {
                                //ServerLogger.Log($"TCP Receiver handled disc for {wc.player?.name}");
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
                        bool cleanup = false;
                        try
                        {
                            active = false;
                            Socket.Select(socketsToRead, null, null, 5000); // usual parking place
                            active = true; // if true the worker will be preferred for new clients
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted) { cleanup = true; }
                        catch (SocketException ex) { cleanup = true; }
                        catch (ObjectDisposedException ex) { cleanup = true; }
                        catch (Exception ex) { ServerLogger.Log(ex.ToString()); }
                        if (cleanup) socketsToRead.RemoveAll(socket => socket == null || !socket.Connected);

                        foreach (var s in socketsToRead)
                        {
                            if (socketState.TryGetValue(s, out ClientState state))
                            {
                                try
                                {
                                    int offset = 0;
                                    // If protocol is unknown, determine the protocol - peek at the socket
                                    if (state.protocol == null && !state.IsProtocolInitializing)
                                    {
                                        int bytesRead = s.Receive(buffer, sz, SocketFlags.Peek);
                                        if (bytesRead > 4) state.InitProtocol(buffer.AsSpan(0, bytesRead));
                                    }
                                    else if (state.protocol != null)
                                    {
                                        int bytesRead = state.stream.Read(buffer, 0, buffer.Length);
                                        if (bytesRead > 0)
                                        {
                                            var messages = state.protocol.AddData(buffer.AsSpan(0, bytesRead));
                                            foreach (var m in messages) state.AddReceivedMessage(m);
                                        }
                                    }
                                }
                                catch (IOException ex) when (
                                    ex.InnerException is SocketException socketEx
                                    && socketEx.SocketErrorCode == SocketError.ConnectionReset)
                                { }
                                catch (IOException ex) when (
                                    ex.InnerException is SocketException socketEx
                                    && socketEx.SocketErrorCode == SocketError.WouldBlock)
                                { }
                                catch (IOException e) { state.player?.Disconnect(); }
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

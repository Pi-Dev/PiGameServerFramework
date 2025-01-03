using Auth;
using PiGSF.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.Serialization;
using Terminal.Gui;
using Transport;

namespace PiGSF.Server
{
    // Main server logic
    public class Server
    {
        private readonly int port;
        private int NextPlayerId = 1;
        public IAuthProvider authenticator;

        // Player Database
        ConcurrentDictionary<string, Player> knownPlayersByUid = new();
        internal ConcurrentList<Player> knownPlayers = new();
        public Player? GetPlayerByUid(string uid) => knownPlayersByUid!.GetValueOrDefault(uid, null) ?? null;

        ConcurrentList<ITransport> transports;
        public static Room? defaultRoom;
        TcpTransport TCP;

        List<Type> InitRoomTypes()
        {
            ServerLogger.Log("[===== Room Types =====]");
            var ts = TypeLoader.GetSubclassesOf<Room>();
            foreach (var r in ts)
            {
                ServerLogger.Log($"|- {r.Name} [{r.FullName}]");
            }
            ServerLogger.Log("|");
            return ts;
        }

        List<Type> InitTransports()
        {
            ServerLogger.Log("[===== Transports =====]");
            var ts = TypeLoader.GetTypesImplementing<ITransport>();
            foreach (var r in ts)
            {
                ServerLogger.Log($"|- {r.Name} [{r.FullName}]");
            }
            ServerLogger.Log("|");
            return ts;
        }

        List<Type> InitAuthenticators()
        {
            ServerLogger.Log("[=== Authenticators ===]");
            var ts = TypeLoader.GetTypesImplementing<IAuthProvider>();
            foreach (var r in ts)
            {
                ServerLogger.Log($"|- {r.Name} [{r.FullName}]");
            }
            ServerLogger.Log("|");
            return ts;
        }

        internal void HandleCommand(string s)
        {
            if (s == "stop")
            {
                Stop();
            }
            else if (s == "keys")
            {
                var keys = RSAEncryption.GenerateRSAKeyPairs(512);
                ServerLogger.Log("");
                ServerLogger.Log(keys.PrivateKey);
                ServerLogger.Log("");
                ServerLogger.Log(keys.PublicKey);
                ServerLogger.Log("");
            }
        }

        public Server(int port)
        {
            this.port = port;
            authenticator = new JWTAuth();

            var transports = InitTransports();
            this.transports = new();
            foreach (var t in transports)
            {
                var transport = Activator.CreateInstance(t) as ITransport;
                if (transport != null) this.transports.Add(transport);
            }
            InitAuthenticators();
            InitRoomTypes();

            defaultRoom = ServerConfig.defaultRoom;
        }

        // Running on Server Thread
        public void Start()
        {
            transports.ForEach(x => x.Init(port, this));
        }

        public async void Stop()
        {
            // Terminate listeners
            try
            {
                transports.ForEach(x => x.StopAccepting());
            }
            catch { }

            // Send ShutdownRequest to all rooms
            Room.rooms.ForEach(r => r.messageQueue.EnqueueAndNotify(new Room.RoomEvent(() =>
            {
                r.AllowPlayers = false;
                r.AllowSpectators = false;
                r.messageQueue.EnqueueAndNotify(new Room.ShutdownRequest());
            })));

            ServerLogger.Log("Waiting for rooms to complete.");

            // Depending of hosted games, some rooms may need a ton of time to complete, especially if ranked
            // While I don't believe that this server will be used in a E-Sport someday, 
            // I better prepare this server for such important use case too
            // So I will just wait indefinitely until all rooms are stopped, or eligible for stop
            while (true)
            {
                await Task.Delay(500);
                bool canShutDown = true;
                Room.rooms.ForEach((r) =>
                {
                    if (!r.eligibleForDeletion) canShutDown = false;
                });

                if (canShutDown)
                {
                    Room.rooms.ForEach(r => r.Stop());
                    while (Room.rooms.Count != 0) await Task.Yield();
                    break;
                }
            }
            ServerLogger.Log("All rooms stopped. SHUTTING DOWN...");
            ServerLogger.Stop();
            Application.RequestStop();
            ServerLogger.Log("Application should be shutdown now!");
        }

        public async Task<Player?> AuthenticatePlayer(string connectionPayload)
        {
            PlayerData? pd = null;
            foreach (var a in ServerConfig.authProviders) // concurrent reading!
            {
                pd = await a.Authenticate(connectionPayload);
                if (pd != null) break;
            }
            if (pd == null) return null; // Failed authentication should disconnect player

            var player = GetPlayerByUid(pd.uid);
            if (player == null)
            {
                player = new Player(NextPlayerId++);
                player.uid = pd.uid;
                player.username = pd.username;
                player.name = pd.name;
                knownPlayers.Add(player);
                knownPlayersByUid[pd.uid] = player;
                player.JoinRoom(defaultRoom);
            }
            return player;
        }

    }
}

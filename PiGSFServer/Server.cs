using Auth;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.Serialization;
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
        ConcurrentBag<Player> knownPlayers = new();
        public Player? GetPlayerByUid(string uid) => knownPlayersByUid!.GetValueOrDefault(uid, null) ?? null;

        List<ITransport> transports;
        public static Room? defaultRoom;
        TcpTransport TCP;

        List<Type> InitRoomTypes()
        {
            Console.WriteLine("[===== Room Types =====]");
            var ts = TypeLoader.GetSubclassesOf<Room>();
            foreach (var r in ts)
            {
                Console.WriteLine($"|- {r.Name} [{r.FullName}]");
            }
            Console.WriteLine("|");
            return ts;
        }

        List<Type> InitTransports()
        {
            Console.WriteLine("[===== Transports =====]");
            var ts = TypeLoader.GetTypesImplementing<ITransport>();
            foreach (var r in ts)
            {
                Console.WriteLine($"|- {r.Name} [{r.FullName}]");
            }
            Console.WriteLine("|");
            return ts;
        }

        List<Type> InitAuthenticators()
        {
            Console.WriteLine("[=== Authenticators ===]");
            var ts = TypeLoader.GetTypesImplementing<IAuthProvider>();
            foreach (var r in ts)
            {
                Console.WriteLine($"|- {r.Name} [{r.FullName}]");
            }
            Console.WriteLine("|");
            return ts;
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
                if(transport != null) this.transports.Add(transport);
            }
            InitAuthenticators();
            InitRoomTypes();

            defaultRoom = ServerConfig.defaultRoom;
        }

        public void Start()
        {
            foreach (var t in transports)
            {
                t!.Init(port, this);
            }
        }

        public void Stop()
        {
            TCP?.Stop();
            Console.WriteLine("Server stopped.");
        }

        public async Task<Player?> AuthenticatePlayer(string connectionPayload)
        {
            PlayerData? pd = null;
            foreach (var a in ServerConfig.authProviders)
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


        //private async void HandleClient(Stream connection)
        //{
        //    Player? player = null;
        //    try
        //    {
        //        using var reader = new StreamReader(connection, System.Text.Encoding.UTF8);
        //        string token = await reader.ReadLineAsync() ?? string.Empty;
        //
        //        var playerData = await authenticator.Authenticate(token);
        //        player = GetPlayerByUid(playerData.uid);
        //
        //        if (player == null)
        //        {
        //            player = new Player(NextPlayerId++);
        //            player.uid = playerData.uid;
        //            player.username = playerData.username;
        //            player.name = playerData.name;
        //            knownPlayers.Add(player);
        //            knownPlayersByUid[playerData.uid] = player;
        //            player.activeRoom = defaultRoom;
        //        }
        //
        //        //player.connection = connection;
        //        if (player.activeRoom.AddPlayer(player))
        //        {
        //            Console.WriteLine($"Player {player.name} (UID: {player.uid}) connected to room {player.activeRoom.Name}.");
        //        }
        //        else
        //        {
        //            Console.WriteLine($"Player {player.name} (UID: {player.uid}) was REJECTED from {player.activeRoom.Name}.");
        //            player.activeRoom = defaultRoom;
        //        }
        //
        //        while (true)
        //        {
        //            var message = await reader.ReadLineAsync();
        //            if (message == null)
        //            {
        //                break;
        //            }
        //            player.activeRoom.OnMessageReceived(message, player);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error handling client: {ex.Message}");
        //    }
        //    finally
        //    {
        //        if (connection != null)
        //        {
        //            connection.Close();
        //        }
        //
        //        if (player != null)
        //        {
        //            player.activeRoom?.RemovePlayer(player);
        //            Console.WriteLine($"Player {player.name} (UID: {player.uid}) disconnected.");
        //        }
        //    }
        //}

    }
}

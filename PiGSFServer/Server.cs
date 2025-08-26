using Auth;
using PiGSF.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PiGSF.Server
{
    // Main server logic
    public static class Server
    {
        static int port;
        static int NextPlayerId = 1;
        //public static IAuthProvider authenticator;
        static ConcurrentList<ITransport> transports;

        // Player Database
        static ConcurrentDictionary<string, Player> knownPlayersByUid = new();
        static internal ConcurrentList<Player> knownPlayers = new();
        public static Player? GetPlayerByUid(string uid) => knownPlayersByUid!.GetValueOrDefault(uid, null) ?? null;
        public static Player? GetPlayerById(int id) => knownPlayers.FirstOrDefault(p => p.id == id);
        public static Player? GetPlayerById(string id)
        {
            if (int.TryParse(id, out int pid)) return GetPlayerById(pid);
            return null;
        }

        // This creates a bot player, useful for testing purposes.
        // Player is registered, connected, and you can assign _SendData to implement bot logic
        public static Player CreateBotPlayer(string uid)
        {
            var player = new Player(NextPlayerId++);
            player.isBot = true;
            player.uid = uid;
            player.username = "[Bot]";
            player.name = "[Bot]";
            knownPlayers.Add(player);
            knownPlayersByUid[uid] = player;
            return player;
        }

        static volatile bool _isActive = false;
        public static bool IsActive() => _isActive;

        static List<Type> InitTransports()
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

        static List<Type> InitAuthenticators()
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

        static string RoomLogEntry(Room r)
        {
            var p = r.GetPlayersData();
            return $" #{r.Id,-5} | {r.Name,-10} | ({p.connected}/{p.total}) | {r.GetType().Name}";
        }
        public static void HandleCommand(string command)
        {
            string s = command.ToLower();
            if (s == "h" || s == "?" || s == "help")
            {
                var helpText = @"
    List of commands
    help, h, ?  => Displays this
    f [text]    => Sets log message filter
                        
    stop        => Stops the server
    keys        => Generates a RSA key pair

    players, p  => Lists all connected players
    p [id]      => Searches player by id
    ps [user]   => Searches player by username/name/uid
                        
    rooms, r    => Displays list of all active rooms
    l [id/name] => Opens log for chosen room
    r [id]      => Shows info for given room by id/name
    rs [name]   => Searches rooms by name (or shows named rooms)
    q, b, back  => Exits back to main log
    ";
                ServerLogger.WriteMessageToScreen(helpText);
            }
            else if (s == "")
            {
                var room = ServerLogger.currentRoomChannel;
                if (room != null)
                {
                    var pd = room.GetPlayersData();

                    var sb = new StringBuilder();
                    sb.AppendLine($"Status: {room.Status}");
                    sb.AppendLine($"Players (Current/Max/Seen): ({pd.connected}/{room.MaxPlayers}/{pd.total})");
                    sb.AppendLine($"Vars: minP={room.MinPlayers} maxP={room.MaxPlayers} stared={room.IsStarted} TI={room.TickInterval}");
                    ServerLogger.WriteMessageToScreen(sb.ToString());

                }
                else
                {
                    int c = 0; knownPlayers.ForEach(p => { if (p.IsConnected()) c++; });

                    ServerLogger.WriteMessageToScreen($"Players: {c}/{knownPlayers.Count}; Rooms: {Room.rooms.Count}");
                }
            }
            else if (s == "f") ServerLogger.SetFilter("");
            else if (s.StartsWith("f "))
            {
                string filter = s.Substring(2);
                ServerLogger.SetFilter(filter);
            }
            else if (s == "stop")
            {
                Server.Stop();
            }
            else if (s == "keys")
            {
                var buf = new StringBuilder();
                var keys = RSAEncryption.GenerateRSAKeyPairs(512);
                ServerLogger.WriteMessageToScreen($"\n{keys.PrivateKey}\n\n{keys.PublicKey}\n\n");
            }
            else if (s == "rooms" || s == "r")
            {
                string str = "";
                Room.rooms.ForEach(r =>
            {
                str += RoomLogEntry(r) + "\n";
            });
                ServerLogger.WriteMessageToScreen(str);
            }
            else if (s == "rs")
            {
                var tokens = s.Split(" ").Select(s=>s.Trim()).ToArray();
                string str = "";
                string what = "";
                if (tokens.Length > 0) what = tokens[1];
                str += RoomLogEntry(Room.defaultRoom) + "\n";
                Room.rooms.ForEach(r =>
                {
                    if (r.Name != "" && r.Name.Contains(what))
                        str += RoomLogEntry(r) + "\n";
                });
                ServerLogger.WriteMessageToScreen(str);
            }
            else if (s.StartsWith("l "))
            {
                var tokens = s.Split(" ").Select(s => s.Trim()).ToArray();
                if (tokens.Length > 1)
                {
                    var r = Room.GetByName(tokens[1]);
                    if (r == null && int.TryParse(tokens[1], out int roomID)) r = Room.GetById(roomID);
                    if (r == null) ServerLogger.WriteMessageToScreen($"No room with id/name {tokens[1]} exists");
                    else { ServerLogger.SetOutputToRoom(r); UpdatePrompt(true); }
                }
            }
            else if (s.StartsWith("r "))
            {
                var tokens = s.Split(" ").Select(s => s.Trim()).ToArray();
                if (tokens.Length > 1)
                {
                    Room r = null;
                    if (int.TryParse(tokens[1], out int roomID)) r = Room.GetById(roomID);
                    if (r == null) ServerLogger.WriteMessageToScreen($"No room with id/name {tokens[1]} exists");
                    else ShowRoomInfo(r);
                }
            }
            else if (s == "players" || s == "p")
            {
                string str = "";
                knownPlayers.ForEach(p => { if (p.IsConnected()) str += p.ToTableString() + "\n"; });
                ServerLogger.WriteMessageToScreen(str.Length > 0 ? str : "No players connected");
            }
            else if (s.StartsWith("p "))
            {
                string str = "";
                var tokens = s.Split(" ");
                if (tokens.Length == 1) { HandleCommand("p"); return; }
                if (int.TryParse(tokens[1], out int pid))
                {
                    var pl = GetPlayerById(pid);
                    if (pl == null) ServerLogger.WriteMessageToScreen($"No player {pid} was seen on the server");
                    else ShowPlayerInfo(pl);
                }
            }
            else if (s == "q" || s == "back" || s == "b")
            {
                ServerLogger.SetOutputToServer();
                UpdatePrompt(true);
            }
            // Add other SERVER commands here
            else
            {
                // ROOM commands
                Room r = ServerLogger.currentRoomChannel;
                if (r != null)
                {
                    if (s.StartsWith("kick "))
                    {
                        var tokens = s.Split(' ');
                        if (tokens.Length == 1) return;
                        var p = GetPlayerById(tokens[1]);
                        r.KickPlayer(p);
                    }
                    else if (s.StartsWith("ban "))
                    {
                        var tokens = s.Split(' ');
                        if (tokens.Length == 1) return;
                        var p = GetPlayerById(tokens[1]);
                        r.BanPlayer(p);
                    }
                    r.messageQueue.Enqueue(new Room.ServerCommand { command = command });
                }
                else HandleCommand("?");
            }
            UpdatePrompt(true); // Finally
        }
        internal static void UpdatePrompt(bool print)
        {
            int current = 0, total = 0;
            string prefix;
            var r = ServerLogger.currentRoomChannel;
            if (r == null)
            {
                prefix = "[Server]";
                Server.knownPlayers.ForEach(p => { if (p.IsConnected()) current++; total++; });
            }
            else
            {
                prefix = $"[{r.GetType().Name}:{r.Id}]";
                prefix += $"[{r.Name}]";
                r.players.ForEach(p => { if (p.IsConnected()) current++; total++; });
            }
            ServerLogger.prompt = $"{prefix}({current}/{total})> ";
            if (print) ServerLogger.WritePrompt();
        }
        static void ShowRoomInfo(Room r)
        {
            List<Player> Connected = new(), Tracked = new();
            r.players.ForEach(x => (x.IsConnected() ? Connected : Tracked).Add(x));
            string s =
                 $"+================= ROOM ID = {r.Id,-5} ======================+\n";
            s += $"│  {$"ID: {r.Id} - NAME: {r.Name} - ROOM TYPE: {r.GetType().Name}",-53} │\n";
            s += $"│  {$"Players: ({Connected.Count} connected / {Tracked.Count} tracked)",-53} │\n";
            s += $"│  {$"Room Status: {r.Status}",-53} │\n";
            s += $"│  {$"",-53} │\n";

            s += $"│  {"CONNECTED PLAYERS:",-53} │\n";
            for (int i = 0; i < Connected.Count; i += 2)
            {
                var p1 = Connected[i];
                var p2 = (i + 1 < Connected.Count) ? Connected[i + 1] : null;
                s += $"│  {$" #{p1.id} {p1.username}  ({p1.name})",-26}" +
                     (p2 != null ? $" {$" #{p2.id} {p2.username} ({p2.name})",-26}" : " ".PadLeft(27)) + " │\n";
            }

            s += $"│  {$"",-53} │\n";

            s += $"│  {$"TRACKED PLAYERS (Not in this room):",-53} │\n";
            for (int i = 0; i < Tracked.Count; i += 2)
            {
                var p1 = Tracked[i];
                var p2 = (i + 1 < Tracked.Count) ? Tracked[i + 1] : null;
                s += $"│  {$" #{p1.id} {p1.username}  ({p1.name})",-26}" +
                     (p2 != null ? $" {$" #{p2.id} {p2.username} ({p2.name})",-26}" : " ".PadLeft(27)) + " │\n";
            }

            s += $"│  {$"",-53} │\n";

            s += $"│  {$"Type `r {r.Id} to see room's log`",-53} │\n";
            s += $"+========================================================+\n";
            s += $"Log file: {r.Log._logFilePath}\n";

            ServerLogger.WriteMessageToScreen(s);
        }
        static void ShowPlayerInfo(Player p)
        {
            string s =
                 $"+================ PLAYER ID = {p.id,-5} =====================+\n";
            s += $"│  UID:       {p.uid.PadRight(42)} │\n";
            s += $"│  Username:  {p.username.PadRight(42)} │\n";
            s += $"│  Name:      {p.name.PadRight(42)} │\n";
            s += $"│  {$"",-53} │\n";
            if (p.IsConnected())
            {
                var r = p.activeRoom;
                if (r != null)
                    s += $"│  {$"Active in {r.Name} {r.GetType().Name} [id={r.Id}] ",-53} │\n";
                foreach (var rr in p.rooms) if (rr != p.activeRoom)
                        s += $"│  {$" |- also in {rr.Name} {rr.GetType().Name} [id={rr.Id}] ",-53} │\n";
            }
            else
                s += $"│  {"Not currently connected",-53} │\n";
            s += $"+========================================================+\n";
            ServerLogger.WriteMessageToScreen(s);
        }

        static Thread mainThread;

        // Must run on separate thread
        static public void Start(int port)
        {
            mainThread = Thread.CurrentThread;
            Server.port = port;

            List<Type> transports = InitTransports();
            Server.transports = new();
            foreach (var t in transports)
            {
                var transport = Activator.CreateInstance(t) as ITransport;
                if (transport != null) Server.transports.Add(transport);
            }

            InitAuthenticators();

            // Init default room
            Room.defaultRoom = Room.CreateDefaultRoom();

            // Transports
            Server.transports.ForEach(x => x.Init(port));
            _isActive = true;

            // SSL Files - PFX:
            string fnCert = ServerConfig.Get("SSLServerCertPfx"); 
            string fnCertPass = ServerConfig.Get("SSLServerCertPfxPassword"); 
			if(fnCert != "")
			{
				if(fnCert.StartsWith("~")) fnCert = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + fnCert.Substring(1);
				serverCertificate = new X509Certificate2(fnCert, fnCertPass);
			}
			
			// PEM 
			string certPath = ServerConfig.Get("SSLServerCertPem"); 
			string keyPath = ServerConfig.Get("SSLServerKeyPemPassword");
			if (certPath.StartsWith("~")) certPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + certPath.Substring(1);
			if (keyPath.StartsWith("~")) keyPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + keyPath.Substring(1);
			if (certPath != "" && keyPath != "")
			{
				string certPem = File.ReadAllText(certPath);
				string keyPem = File.ReadAllText(keyPath);

                // Loading from PEM
                serverCertificate = X509Certificate2.CreateFromPem(certPem, keyPem);

                // if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(keyPath))
                // {
                //     byte[] certBytes = File.ReadAllBytes(certPath);
                //     serverCertificate = new X509Certificate2(certBytes, (string)null, X509KeyStorageFlags.PersistKeySet);
                // }
            }

        }

        internal static volatile bool ServerStopRequested = false;
        public static async void Stop()
        {
            ServerStopRequested = true;
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
            _isActive = false;

            Environment.Exit(0);
        }

        public static async Task<Player?> AuthenticatePlayer(string connectionPayload)
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
                player.playerData = pd;
                knownPlayers.Add(player);
                knownPlayersByUid[pd.uid] = player;
                player.JoinRoom(Room.defaultRoom);
            }
            return player;
        }

        // HTTPS sockets
        internal static X509Certificate2 serverCertificate = null;
        public static X509Certificate2 LoadCertificateWithPrivateKey(string certPath, string keyPath)
        {
            // Load the certificate
            var certPem = File.ReadAllText(certPath);
            var certBytes = DecodePem(certPem, "CERTIFICATE");

            // Load the private key
            var keyPem = File.ReadAllText(keyPath);
            var keyBytes = DecodePem(keyPem, "PRIVATE KEY");

            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(keyBytes, out _);

            var cert = new X509Certificate2(certBytes);
            return cert.CopyWithPrivateKey(rsa);
        }
        private static byte[] DecodePem(string pem, string label)
        {
            string header = $"-----BEGIN {label}-----";
            string footer = $"-----END {label}-----";

            int start = pem.IndexOf(header, StringComparison.Ordinal) + header.Length;
            int end = pem.IndexOf(footer, StringComparison.Ordinal);

            string base64 = pem.Substring(start, end - start).Replace("\n", "").Replace("\r", "").Trim();
            return Convert.FromBase64String(base64);
        }
    }
}

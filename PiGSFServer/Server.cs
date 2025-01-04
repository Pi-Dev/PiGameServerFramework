using Auth;
using PiGSF.Rooms;
using PiGSF.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;

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
        public Player? GetPlayerById(int id) => knownPlayers.FirstOrDefault(p => p.id == id, null);
        public Player? GetPlayerById(string id)
        {
            if(int.TryParse(id, out int pid)) return GetPlayerById(pid);
            return null;
        }

        ConcurrentList<ITransport> transports;
        public static Room? defaultRoom;
        static List<Type> roomTypes;

        volatile bool _isActive;
        public bool IsActive() => _isActive;

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

        string RoomLogEntry(Room r)
        {
            var p = r.GetPlayersData();
            return $" #{r.Id,-5} | {r.Name,-10} | ({p.connected}/{p.total}) | {r.GetType().Name}";
        }
        internal void HandleCommand(string command)
        {
            string s = command.ToLower();
            if (s == "h" || s == "?" || s == "help")
            {
                lock (Console.Out)
                {
                    Console.WriteLine("""
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
                        """);
                }
            }
            else if (s == "")
            {
                var room = ServerLogger.currentRoomChannel;
                if (room != null)
                {
                    var pd = room.GetPlayersData();
                    lock (Console.Out)
                    {
                        Console.WriteLine($"Status: {room.Status}");
                        Console.WriteLine($"Players (Current/Max/Seen): ({pd.connected}/{room.MaxPlayers}/{pd.total})");
                        Console.WriteLine($"Vars: minP={room.MinPlayers} maxP={room.MaxPlayers} stared={room.IsStarted} TR={room.TickRate}");
                    }
                }
                else
                {
                    int c = 0; knownPlayers.ForEach(p => { if (p.IsConnected()) c++; });
                    lock (Console.Out)
                        Console.WriteLine($"Players: {c}/{knownPlayers.Count}; Rooms: {Room.rooms.Count}");
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
                Stop();
            }
            else if (s == "keys")
            {
                lock (Console.Out)
                {
                    var keys = RSAEncryption.GenerateRSAKeyPairs(512);
                    Console.WriteLine("");
                    Console.WriteLine(keys.PrivateKey);
                    Console.WriteLine("");
                    Console.WriteLine(keys.PublicKey);
                    Console.WriteLine("");
                }
            }
            else if (s == "rooms" || s == "r")
            {
                string str = "";
                Room.rooms.ForEach(r =>
            {
                str += RoomLogEntry(r) + "\n";
            });
                lock (Console.Out) Console.WriteLine(str);
            }
            else if (s == "rs")
            {
                var tokens = s.Split(" ", StringSplitOptions.TrimEntries);
                string str = "";
                string what = "";
                if (tokens.Length > 0) what = tokens[1];
                str += RoomLogEntry(defaultRoom) + "\n";
                Room.rooms.ForEach(r =>
                {
                    if (r.Name != "" && r.Name.Contains(what))
                        str += RoomLogEntry(r) + "\n";
                });
                lock (Console.Out) Console.WriteLine(str);
            }
            else if (s.StartsWith("l "))
            {
                var tokens = s.Split(" ", StringSplitOptions.TrimEntries);
                if (tokens.Length > 1)
                {
                    var r = Room.GetByName(tokens[1]);
                    if (r == null && int.TryParse(tokens[1], out int roomID)) r = Room.GetById(roomID);
                    if (r == null) Console.WriteLine($"No room with id/name {tokens[1]} exists");
                    else ServerLogger.SetOutputToRoom(r);
                }
            }
            else if (s.StartsWith("r "))
            {
                var tokens = s.Split(" ", StringSplitOptions.TrimEntries);
                if (tokens.Length > 1)
                {
                    Room r = null;
                    if (int.TryParse(tokens[1], out int roomID)) r = Room.GetById(roomID);
                    if (r == null) Console.WriteLine($"No room with id/name {tokens[1]} exists");
                    else ShowRoomInfo(r);
                }
            }
            else if (s == "players" || s == "p")
            {
                string str = "";
                knownPlayers.ForEach(p => { if (p.IsConnected()) str += p.ToTableString() + "\n"; });
                Console.WriteLine(str.Length > 0 ? str : "No players connected");
            }
            else if (s.StartsWith("p "))
            {
                string str = "";
                var tokens = s.Split(" ");
                if (tokens.Length == 1) { HandleCommand("p"); return; }
                if (int.TryParse(tokens[1], out int pid))
                {
                    var pl = GetPlayerById(pid);
                    if (pl == null) Console.Write($"No player {pid} was seen on the server");
                    else ShowPlayerInfo(pl);
                }
            }
            else if (s == "q" || s == "back" || s == "b")
            {
                ServerLogger.SetOutputToServer();
            }
            // Add other SERVER commands here
            else
            {
                // ROOM commands
                Room r = ServerLogger.currentRoomChannel;
                if (r != null)
                {
                    if(s.StartsWith("kick "))
                    {
                        var tokens = s.Split(' ');
                        if (tokens.Length == 1) return;
                        var p = GetPlayerById(tokens[1]);
                        r.KickPlayer(p);
                    }
                    else if(s.StartsWith("ban "))
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
        }

        void ShowRoomInfo(Room r)
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

            lock (Console.Out) Console.WriteLine(s);
        }
        void ShowPlayerInfo(Player p)
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
            lock (Console.Out) Console.WriteLine(s);
        }

        Thread mainThread;
        public Server(int port)
        {
            mainThread = Thread.CurrentThread;
            this.port = port;
            authenticator = new JWTAuth();

            List<Type> transports = InitTransports();
            this.transports = new();
            foreach (var t in transports)
            {
                var transport = Activator.CreateInstance(t) as ITransport;
                if (transport != null) this.transports.Add(transport);
            }
            InitAuthenticators();
            roomTypes = InitRoomTypes();
        }

        internal static void CreateDefaultRoom()
        {
            if (defaultRoom != null) return;
            var tokens = ServerConfig.Get("defaultRoom").Split(",", StringSplitOptions.TrimEntries);
            if (tokens.Length > 1)
            {
                List<Type> t = roomTypes.Where(x => x.Name.ToLower() == tokens[0].ToLower()).ToList();
                if (t.Count > 0)
                    defaultRoom = Activator.CreateInstance(t[0], tokens[1]) as Room;
            }
            if (defaultRoom == null) defaultRoom = new ChatRoom("Lobby");

        }

        // Running on Server Thread
        public void Start()
        {
            // Init default room
            CreateDefaultRoom();

            // Transports
            transports.ForEach(x => x.Init(port, this));
            _isActive = true;
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
            _isActive = false;

            Environment.Exit(0);
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

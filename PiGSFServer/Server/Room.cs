using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PiGSF.Rooms;
using PiGSF.Utils;

namespace PiGSF.Server
{
    public abstract class Room : IDisposable
    {
        // Room identification
        public readonly int Id;
        public readonly string Name;

        // Room properties
        public double TickInterval = 1.0 / 60.0; // Default to 60 ticks per second
        public int MinPlayers = 1;
        public int MaxPlayers = 16;
        public int MaxClients = 32;
        public int WaitTime = 60;
        public bool WaitForMinPlayers = true;
        public bool AllowPlayers = true;
        public bool AllowSpectators = true;
        public string Status = ""; // Set to anything, e.g. Turn X, or Matchmaking, to be used in clients

        public int ConnectionTimeout = ServerConfig.DefaultRoomConnectionTimeout;
        public int PlayerDisbandTimeout = -1;

        // Connected players and banned players
        internal protected ConcurrentList<Player> players = new();
        protected ConcurrentBag<string> BannedPlayerUids = new();
        public (int connected, int total) GetPlayersData()
        {
            lock (players)
            {
                int connected = 0;
                foreach (Player player in players) if (player.IsConnected()) connected++;
                return (connected, players.Count);
            }
        }

        // Player Messages & Room Message Queue
        internal class IRoomEvent { };
        internal class PlayerMessage : IRoomEvent { public Player pl; public byte[] msg; }
        internal class PlayerDisconnect : IRoomEvent { public Player pl; public bool disband; }
        internal class RoomEvent : IRoomEvent { public Action func; public RoomEvent(Action f) { func = f; } }
        internal class RoomStopEvent : IRoomEvent { }
        internal class RoomStartEvent : IRoomEvent { }
        internal class ShutdownRequest : IRoomEvent { }
        internal class ServerCommand : IRoomEvent { public string command=""; }
        internal ConcurrentQueue<IRoomEvent> messageQueue = new();

        private volatile bool _isStarted = false;
        private volatile int _firstPlayerConnected = 0;
        private CancellationTokenSource? startTimerCts;

        public bool IsStarted => _isStarted;

        volatile int roomThreadId = 0;

        // returns false to stop the thread
        bool RoomThreadProcessEvent(IRoomEvent item)
        {
            if (item is PlayerMessage pm)
                OnMessageReceived(pm.msg, pm.pl);
            else if (item is RoomEvent re)
            {
                try { re.func(); }
                catch (Exception e) { ServerLogger.Log(e.ToString()); Log.Write(e.ToString()); }
            }
            else if (item is PlayerDisconnect pd)
                OnPlayerDisconnected(pd.pl, pd.disband);
            else if (item is RoomStartEvent)
            {
                if (_isStarted)
                {
                    _isStarted = true;
                    Start();
                }
            }
            else if (item is ServerCommand sc)
                OnServerCommand(sc.command);
            else if (item is ShutdownRequest)
                OnShutdownRequested();
            else if (item is RoomStopEvent)
                return false;
            return true;
        }

        void RoomThread()
        {
            roomThreadId = Thread.CurrentThread.ManagedThreadId;

            ServerLogger.Log("Thread for room " + GetType().Name + " started");

            // 0. Initialize the room
            Setup();

            // 1. Determine when first Update must be called
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            long tickIntervalTicks = (long)(Stopwatch.Frequency * TickInterval);
            long nextUpdateTick = stopwatch.ElapsedTicks + tickIntervalTicks;
            try
            {
                while (true) // breaks when RoomStopEvent received
                {
                    Thread.Yield(); // Yield hint for others to run...

                    // 1. Process messages until close to next update time or queue is empty
                    while (stopwatch.ElapsedTicks < nextUpdateTick - Stopwatch.Frequency / 1000 * 1
                        && messageQueue.TryDequeue(out var item))
                        if (!RoomThreadProcessEvent(item)) goto EndOfThread;

                    // 2. WAIT! Sleep the thread for a short time to avoid busy-waiting
                    long currentTicks = stopwatch.ElapsedTicks;
                    int timeout = (int)Math.Max(1, (nextUpdateTick - currentTicks) * 1000 / Stopwatch.Frequency);
                    if (timeout > 0) lock (messageQueue) Monitor.Wait(messageQueue, timeout);

                    // 3. Finally, call Update
                    if (stopwatch.ElapsedTicks >= nextUpdateTick)
                    {
                        Update((float)TickInterval);
                        nextUpdateTick = stopwatch.ElapsedTicks + tickIntervalTicks;

                        if (stopwatch.ElapsedTicks > nextUpdateTick)
                        {
                            nextUpdateTick = stopwatch.ElapsedTicks + tickIntervalTicks;
                        }
                    }
                }
            EndOfThread: // Yeah, this, prefer to keep it here for readability
                ServerLogger.Log("Thread for room " + GetType().Name + " ended");
                Dispose();
            }
            catch (Exception e)
            {
                string message = $"ROOM {Id}: {GetType().Name} ENCOUNTERED ERROR:\n" + e.ToString();
                ServerLogger.Log(message);
                Log.Write(message);
                if (Room.defaultRoom == this)
                {
                    message = "CRITICAL! THE DEFAULT ROOM CRASHED!";
                    ServerLogger.Log(message);
                    Log.Write(message);
                    Room.defaultRoom = null;
                    Room.defaultRoom = CreateDefaultRoom?.Invoke();
                }
                Dispose();
            }
        }

        public RoomLogger Log;

        // Constructor
        protected Room(string name = "")
        {
            // Init room, create unique id
            Id = NextRoomId++;
            Name = name;
            Log = new RoomLogger(this);

            // Register rooms with the server
            rooms.Add(this);
            roomsById[Id] = this;
            if (!string.IsNullOrEmpty(name)) namedRooms[name] = this;

            var t = new Thread(RoomThread);
            t.Name = $"Room {Id}: {GetType().Name}";
            t.Start();
        }

        // Lifecycle methods, called from the Room Thread, in the Room Thread
        // Must be reimplemented by the room gamelogic for server-authoritative games
        
        protected virtual void Setup()
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }

        protected virtual void Start()
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }

        protected virtual void Update(float dt)
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }

        protected virtual void OnPlayerConnected(Player player, bool isNew)
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }

        protected virtual void OnPlayerDisconnected(Player player, bool disband)
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }

        protected virtual void OnMessageReceived(byte[] message, Player sender)
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }

        protected virtual void OnShutdownRequested()
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }

        protected virtual void OnServerCommand(string s)
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
        }


        // Default Room Management
        static List<Type> InitRoomTypes()
        {
            ServerLogger.Log("[===== Room Types =====]");
            var ts = TypeLoader.GetSubclassesOf<Room>();
            foreach (var r in ts)
            {
                // this line IS important as it will call the static ctors
                ServerLogger.Log($"|- {r.Name} [{r.FullName}]"); 
            }
            ServerLogger.Log("|");
            return ts;
        }

        static List<Type> roomTypes;
        public static Room? defaultRoom;
        public static Func<Room> CreateDefaultRoom = () =>
        {
            roomTypes = InitRoomTypes();
            var tokens = ServerConfig.Get("defaultRoom").Split(",").Select(s=>s.Trim()).ToArray();
            if (tokens.Length > 1)
            {
                List<Type> t = roomTypes.Where(x => x.Name.ToLower() == tokens[0].ToLower()).ToList();
                if (t.Count > 0)
                    return Activator.CreateInstance(t[0], tokens[1]) as Room;
            }
            return new ChatRoom("Lobby");
        };

        // Room API

        /// Thread-safe
        public void Stop()
        {
            messageQueue.EnqueueAndNotify(new RoomStopEvent());
        }
        public volatile bool eligibleForDeletion = false;

        public void BroadcastMessage(byte[] data, Player? sender = null)
        {
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == roomThreadId);
            players.ForEach(p => { if (sender == null || p != sender) p.Send(data); });
        }

        /// Thread-safe
        public void DisconnectPlayer(Player player, bool disband = false)
        {
            if (player == null) return;
            player.Disconnect(disband);
        }

        // Thread safe!!
        public void KickPlayer(Player player)
        {
            if (player == null) return;
            if (this == Room.defaultRoom)
                DisconnectPlayer(player, true);
            else
            {
                player.TransferToDefaultLobby();
            }
        }

        // Thread safe!!
        public void BanPlayer(Player player)
        {
            if (player == null) return;
            KickPlayer(player);
            if (!BannedPlayerUids.Contains(player.uid)) BannedPlayerUids.Add(player.uid);
        }

        // Thread safe!!
        public bool IsPlayerConnected(Player player)
        {
            return player.IsConnected();
        }

        // Thread safe!!
        public bool AddPlayer(Player player)
        {
            if (BannedPlayerUids.Contains(player.uid))
            {
                Log.Write($"Player {player.name} is banned from Room {Name}.");
                return false;
            }
            if (!AllowPlayers && !players.Contains(player))
            {
                Log.Write($"Room {Name} is not accepting new players.");
                return false;
            }
            if (players.Count >= MaxPlayers)
            {
                Log.Write($"Room {Name} is full. Player {player.name} cannot join.");
                return false;
            }
            if (!AllowSpectators && player.isSpectator)
            {
                Log.Write($"Player {player.name} is a spectator, but spectators are not allowed in Room {Name}.");
                return false;
            }

            bool isReconnect = false;

            if (!players.AddIfNotExists(player)) isReconnect = true;

            // This must be interlocked
            if (Interlocked.CompareExchange(ref _firstPlayerConnected, 1, 0) == 0)
                HandleFirstPlayerConnection();

            player.activeRoom = this;
            player.rooms = null;
            messageQueue.EnqueueAndNotify(new RoomEvent(() => { OnPlayerConnected(player, isNew: !isReconnect); }));

            // Check if game should start
            if (!_isStarted && (!WaitForMinPlayers || players.Count >= MinPlayers))
                messageQueue.EnqueueAndNotify(new RoomStartEvent());

            return true;
        }

        /// <summary>
        /// Removes player and disbands it from the room data
        /// Thread-safe
        /// </summary>
        /// <param name="player"></param>
        public void RemovePlayer(Player player)
        {
            if (!players.Contains(player)) return; // Deadlock

            players.Remove(player);
            messageQueue.EnqueueAndNotify(new RoomEvent(() =>
            {
                OnPlayerDisconnected(player, disband: true);
            }));

            // unset the active room if set to this room,
            // the player may still be referenced by other rooms
            if (player.activeRoom == this) player.activeRoom = null;
            player.rooms = null;
        }

        private void HandleFirstPlayerConnection()
        {
            if (WaitForMinPlayers && WaitTime > 0)
            {
                ScheduleStartAfterWaitTime();
            }
            else if (!WaitForMinPlayers && WaitTime <= 0)
            {
                messageQueue.EnqueueAndNotify(new RoomEvent(Start));
            }
        }

        private void ScheduleStartAfterWaitTime()
        {
            startTimerCts?.Cancel();
            startTimerCts = new CancellationTokenSource();
            var token = startTimerCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(WaitTime * 1000, token);
                    if (!_isStarted)
                    {
                        messageQueue.EnqueueAndNotify(new RoomEvent(Start));
                    }
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        // Rooms Database
        private static int NextRoomId = 1;
        public static ConcurrentList<Room> rooms = new();
        internal static ConcurrentDictionary<string, Room> namedRooms = new();
        static ConcurrentDictionary<int, Room> roomsById = new();

        public static Room? GetById(int rid) => roomsById!.GetValueOrDefault(rid, null);
        public static Room? GetByName(string roomName) => namedRooms!.GetValueOrDefault(roomName, null);
        public static Room GetOrCreate(string roomName, Func<int, Room> roomFactory)
        {
            return namedRooms.GetOrAdd(roomName, _ => roomFactory(NextRoomId++));
        }

        public static List<Room> FindAllWithPlayer(Player p)
        {
            var res = new List<Room>();
            rooms.ForEach(r => { if (r.players.Contains(p)) res.Add(r); });
            return res;
        }

        // Disposable
        private bool disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                // defaultRoom is a special case - first zero it down, causing players to be disconnected
                if(this == defaultRoom) defaultRoom = null;

                // Remove this room from all referenced player
                players.ForEach(p =>
                {
                    p.rooms.Remove(this);
                    if (p.activeRoom == this) p.activeRoom = null;
                    if (p.rooms.Count == 0 && p.activeRoom == null)
                    {
                        if (defaultRoom == null)
                            p.Disconnect();
                        else p.JoinRoom(defaultRoom, true);
                    }
                });
                if (disposing)
                {
                    rooms.Remove(this);
                    roomsById.TryRemove(Id, out _);
                    namedRooms.TryRemove(Name, out _);
                }
                disposedValue = true;
            }
        }
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}

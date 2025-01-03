using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PiGSF.Server.TUI;
using PiGSF.Utils;

namespace PiGSF.Server
{
    public abstract class Room : IDisposable
    {
        // Room identification
        public readonly int Id;
        public readonly string Name;

        // Room properties
        public int TickRate = 60;
        public int MinPlayers = 1;
        public int MaxPlayers = 16;
        public int MaxClients = 32;
        public int WaitTime = 60;
        public bool WaitForMinPlayers = true;
        public bool AllowPlayers = true;
        public bool AllowSpectators = true;

        public int RoomTimeout = ServerConfig.DefaultRoomTimeout;
        public int PlayerDisbandTimeout = -1;

        // Connected players and banned players
        protected ConcurrentList<Player> ConnectedPlayers = new();
        protected ConcurrentBag<string> BannedPlayerUids = new();
        public (int connected, int total) GetPlayersData()
        {
            lock (ConnectedPlayers)
            {
                int connected = 0;
                foreach (Player player in ConnectedPlayers) if (player.IsConnected()) connected++;
                return (connected, ConnectedPlayers.Count);
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

            // 1. Determine when first Update must be called
            var t = new Stopwatch(); t.Start();
            long tickInterval = Stopwatch.Frequency / TickRate; // Ticks for 1 / TickRate seconds
            long NextUpdateTick = t.ElapsedTicks + tickInterval;

            try
            {
                while (true) // breaks when RoomStopEvent received
                {
                    Thread.Yield(); // Yield hint for others to run...

                    // 1. Process messages until close to next update time or queue is empty
                    while (t.ElapsedTicks < NextUpdateTick - Stopwatch.Frequency / 1000 * 1
                        && messageQueue.TryDequeue(out var item))
                        if (!RoomThreadProcessEvent(item)) goto EndOfThread;

                    // 2. WAIT! Sleep the thread for a short time to avoid busy-waiting
                    long currentTicks = t.ElapsedTicks;
                    int timeout = (int)Math.Max(1, (NextUpdateTick - currentTicks) * 1000 / Stopwatch.Frequency); // Convert ticks to ms
                    if (timeout > 0) lock (messageQueue) Monitor.Wait(messageQueue, timeout);

                    // 3. Finally, call Update
                    if (t.ElapsedTicks >= NextUpdateTick)
                    {
                        Update((float)tickInterval / Stopwatch.Frequency);

                        // Schedule the next update
                        NextUpdateTick += tickInterval;

                        // Adjust in case of excessive delay
                        if (t.ElapsedTicks > NextUpdateTick)
                        {
                            NextUpdateTick = t.ElapsedTicks + tickInterval;
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
                if (Server.defaultRoom == this)
                {
                    message = "CRITICAL! THE DEFAULT ROOM CRASHED!";
                    ServerLogger.Log(message);
                    Log.Write(message);
                    Server.defaultRoom = ServerConfig.defaultRoom;
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
            ConnectedPlayers.ForEach(p => { if (sender == null || p != sender) p.Send(data); });
        }

        /// Thread-safe
        public void DisconnectPlayer(Player player, bool disband = false)
        {
            player.Disconnect(disband);
        }

        // Thread safe!!
        public void KickPlayer(Player player)
        {
            DisconnectPlayer(player, true);
        }

        // Thread safe!!
        public void BanPlayer(Player player)
        {
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
            if (!AllowPlayers && !ConnectedPlayers.Contains(player))
            {
                Log.Write($"Room {Name} is not accepting new players.");
                return false;
            }
            if (ConnectedPlayers.Count >= MaxPlayers)
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

            if (!ConnectedPlayers.AddIfNotExists(player)) isReconnect = true;

            // This must be interlocked
            if (Interlocked.CompareExchange(ref _firstPlayerConnected, 1, 0) == 0)
                HandleFirstPlayerConnection();

            player.activeRoom = this;
            player.rooms = null;
            messageQueue.EnqueueAndNotify(new RoomEvent(() => { OnPlayerConnected(player, isNew: !isReconnect); }));

            // Check if game should start
            if (!_isStarted && (!WaitForMinPlayers || ConnectedPlayers.Count >= MinPlayers))
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
            if (!ConnectedPlayers.Contains(player)) return; // Deadlock

            ConnectedPlayers.Remove(player);
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
        static ConcurrentDictionary<string, Room> namedRooms = new();
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
            rooms.ForEach(r => { if (r.ConnectedPlayers.Contains(p)) res.Add(r); });
            return res;
        }

        // Disposable
        private bool disposedValue;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                // defaultRoom is a special case - first zero it down, causing players to be disconnected
                if(this == Server.defaultRoom) Server.defaultRoom = null;

                // Remove this room from all referenced player
                ConnectedPlayers.ForEach(p =>
                {
                    p.rooms.Remove(this);
                    if (p.activeRoom == this) p.activeRoom = null;
                    if (p.rooms.Count == 0 && p.activeRoom == null)
                    {
                        if (Server.defaultRoom == null)
                            p.Disconnect();
                        else p.JoinRoom(Server.defaultRoom, true);
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

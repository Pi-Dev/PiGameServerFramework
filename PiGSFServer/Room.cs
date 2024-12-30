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
        CancellationTokenSource? cts; // For managing cancellation of the loop
        int _tickRate = 0;
        public int TickRate
        {
            get => _tickRate;
            set
            {
                if (_tickRate == value) return;
                cts?.Cancel();
                Console.WriteLine("Tickrate change for room " + Id + " requested");
                if (_tickRate > 0)
                {
                    messageQueue.Enqueue(new RoomEvent(() =>
                    {
                        Console.WriteLine($"Tickrate for room {Id}: set to {value}");
                        cts = new CancellationTokenSource();
                        _ = UpdateLoop(cts.Token);
                    }));
                }
                _tickRate = value;
            }
        }
        public int MinPlayers { get; set; } = 1;
        public int MaxPlayers { get; set; } = 16;
        public int MaxClients { get; set; } = 32;
        public int WaitTime { get; set; } = 60;
        public bool WaitForMinPlayers { get; set; } = true;
        public bool AllowPlayers { get; set; } = true;
        public bool AllowSpectators { get; set; } = true;

        public int RoomTimeout { get; set; } = ServerConfig.DefaultRoomTimeout;
        public int PlayerDisbandTimeout { get; set; } = -1;

        // Connected players and banned players
        protected ConcurrentList<Player> ConnectedPlayers = new();
        protected ConcurrentBag<string> BannedPlayerUids = new();

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

        void RoomThread()
        {
            roomThreadId = Thread.CurrentThread.ManagedThreadId;

            Console.WriteLine("Thread for room " + GetType().Name + " started");
            try
            {

                cts = new CancellationTokenSource();
                _ = UpdateLoop(cts.Token);

                while (true)
                {
                    Thread.Yield();

                    if (messageQueue.TryDequeue(out var item))
                    {
                        if (item is PlayerMessage pm)
                            OnMessageReceived(pm.msg, pm.pl);
                        else if (item is RoomEvent re)
                        {
                            try { re.func(); }
                            catch (Exception e) { Console.WriteLine(e); }
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
                            break;
                    }
                }
                Console.WriteLine("Thread for room " + GetType().Name + " ended");
                Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine($"ROOM {Id}: {GetType().Name} ENCOUNTERED ERROR:\n"+e.ToString());
                if(Server.defaultRoom == this)
                {
                    Console.WriteLine("CRITICAL! DEFAULT LOBBY CRASHED!");
                    Server.defaultRoom = ServerConfig.defaultRoom;
                }
                Dispose();
            }
        }
        async Task UpdateLoop(CancellationToken ct)
        {
            while (true)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var dt = 1 / TickRate;
                    await Task.Delay(dt * 1000, ct);
                    Update(dt);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    //task is cancelled, return or do something else
                    return;
                }
                catch (DivideByZeroException)
                {
                    return; // expected
                }
            }
        }

        public RoomLogger Log = new();

        // Constructor
        protected Room(string name = "")
        {
            // Init room, create unique id
            Id = NextRoomId++;
            Name = name;

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
            messageQueue.Enqueue(new RoomStopEvent());
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
                Log.WriteLine($"Player {player.name} is banned from Room {Name}.");
                return false;
            }
            if (!AllowPlayers && !ConnectedPlayers.Contains(player))
            {
                Log.WriteLine($"Room {Name} is not accepting new players.");
                return false;
            }
            if (ConnectedPlayers.Count >= MaxPlayers)
            {
                Log.WriteLine($"Room {Name} is full. Player {player.name} cannot join.");
                return false;
            }
            if (!AllowSpectators && player.isSpectator)
            {
                Log.WriteLine($"Player {player.name} is a spectator, but spectators are not allowed in Room {Name}.");
                return false;
            }

            bool isReconnect = false;

            if (!ConnectedPlayers.AddIfNotExists(player)) isReconnect = true;

            // This must be interlocked
            if (Interlocked.CompareExchange(ref _firstPlayerConnected, 1, 0) == 0)
                HandleFirstPlayerConnection();

            player.activeRoom = this;
            player.rooms = null;
            messageQueue.Enqueue(new RoomEvent(() => { OnPlayerConnected(player, isNew: !isReconnect); }));

            // Check if game should start
            if (!_isStarted && (!WaitForMinPlayers || ConnectedPlayers.Count >= MinPlayers))
                messageQueue.Enqueue(new RoomStartEvent());

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
            messageQueue.Enqueue(new RoomEvent(() =>
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
                messageQueue.Enqueue(new RoomEvent(Start));
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
                        messageQueue.Enqueue(new RoomEvent(Start));
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
                ConnectedPlayers.ForEach(p => { 
                    p.rooms.Remove(this);
                    if (p.activeRoom == this) p.activeRoom = null;
                    if (p.rooms.Count == 0 && p.activeRoom == null) p.Disconnect(); 
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

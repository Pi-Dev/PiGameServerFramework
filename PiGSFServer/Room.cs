using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PiGSF.Utils;

namespace PiGSF.Server
{
    public abstract class Room : IDisposable
    {
        // Room identification
        public readonly int Id;
        public string Name { get; set; }

        // Room properties
        CancellationTokenSource? cts; // For managing cancellation of the loop
        int _tickRate = 0;
        public int TickRate
        {
            get => _tickRate;
            set {
                if (_tickRate == value) return; 
                cts?.Cancel();
                Console.WriteLine("Tickrate change for room " + Id + " requested");
                if (_tickRate > 0)
                {
                    messageQueue.Enqueue(new RoomEvent(() => {
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
        protected HashSet<string> BannedPlayerUids = new();

        // Player Messages & Room Message Queue
        internal class IRoomEvent { };
        internal class PlayerMessage : IRoomEvent { public Player pl; public byte[] msg; }
        internal class PlayerJoin : IRoomEvent { public Player pl; }
        internal class PlayerDisconnect : IRoomEvent { public Player pl; }
        internal class PlayerReconnect : IRoomEvent { public Player pl; }
        internal class RoomEvent : IRoomEvent { public Action func; public RoomEvent(Action f) { func = f; } }
        internal ConcurrentQueue<IRoomEvent> messageQueue = new();

        private bool _isStarted = false;
        private bool _firstPlayerConnected = false;
        private CancellationTokenSource? startTimerCts;

        public bool IsStarted => _isStarted;

        void RoomThread()
        {
            Console.WriteLine("Thread for room " + GetType().Name + " started");

            cts = new CancellationTokenSource();
            _ = UpdateLoop(cts.Token);

            while (true)
            {
                Thread.Yield();

                if (messageQueue.TryDequeue(out var item))
                {
                    if (item is PlayerMessage pm)
                    {
                        OnMessageReceived(pm.msg, pm.pl);
                    }
                    else if (item is PlayerJoin pj)
                    {
                    }
                    else if (item is PlayerReconnect pr)
                    {
                    }
                    else if (item is PlayerDisconnect pd)
                    {
                    }
                    else if (item is RoomEvent re)
                    {

                    }
                }
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
            }
        }

        // Constructor
        protected Room(string name = "")
        {
            // Init room, create unique id
            Id = NextRoomId++;
            Name = name;

            // Register rooms with the server
            rooms.Add(this);
            RoomsById[Id] = this;
            if (!string.IsNullOrEmpty(name)) namedRooms[name] = this;

            var t = new Thread(RoomThread);
            t.Start();
        }

        // Lifecycle methods, called from the Room Thread, in the Room Thread

        public virtual void Start()
        {
        }

        public virtual void Update(float dt)
        {
        }

        public virtual void OnPlayerConnected(Player player, bool isNew)
        {
        }

        public virtual void OnPlayerDisconnected(Player player, bool disband)
        {
        }

        public virtual void OnMessageReceived(byte[] message, Player sender)
        {
        }

        // Room API
        public void BroadcastMessage(byte[] data, Player? sender = null)
        {
            ConnectedPlayers.ForEach(p => { if (sender == null || p != sender) p.Send(data); });
        }

        public void DisconnectPlayer(Player player, bool disband = false)
        {
            player.Disconnect(disband);
        }

        // Thread safety!!
        public void KickPlayer(Player player)
        {
            DisconnectPlayer(player, true);
        }

        // Thread safety!!
        public void BanPlayer(Player player)
        {
            KickPlayer(player);
            BannedPlayerUids.Add(player.uid);
        }

        // Thread safety!!
        public bool IsPlayerConnected(Player player)
        {
            return player.IsConnected();
        }

        public bool AddPlayer(Player player)
        {
            if (BannedPlayerUids.Contains(player.uid))
            {
                Console.WriteLine($"Player {player.name} is banned from Room {Name}.");
                return false;
            }

            if (!AllowPlayers && !ConnectedPlayers.Contains(player))
            {
                Console.WriteLine($"Room {Name} is not accepting new players.");
                return false;
            }

            if (ConnectedPlayers.Count >= MaxPlayers)
            {
                Console.WriteLine($"Room {Name} is full. Player {player.name} cannot join.");
                return false;
            }

            if (!AllowSpectators && player.isSpectator)
            {
                Console.WriteLine($"Player {player.name} is a spectator, but spectators are not allowed in Room {Name}.");
                return false;
            }

            bool isReconnect = false;
            if (!ConnectedPlayers.AddIfNotExists(player)) isReconnect = true;

            if (!_firstPlayerConnected)
            {
                _firstPlayerConnected = true;
                HandleFirstPlayerConnection();
            }
            player.activeRoom = this;
            player.rooms = null;
            messageQueue.Enqueue(new RoomEvent(() => { OnPlayerConnected(player, isNew: !isReconnect); }));

            // Check if game should start
            if (!_isStarted && (!WaitForMinPlayers || ConnectedPlayers.Count >= MinPlayers))
                messageQueue.Enqueue(new RoomEvent(Start));

            return true;
        }

        public void RemovePlayer(Player player)
        {
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
        static ConcurrentList<Room> rooms = new();
        static ConcurrentDictionary<string, Room> namedRooms = new();
        static ConcurrentDictionary<int, Room> RoomsById = new();

        public static Room GetByName(string roomName) => namedRooms.GetValueOrDefault(roomName, null);
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
                ConnectedPlayers.ForEach(RemovePlayer);
                if (disposing) rooms.Remove(this);
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

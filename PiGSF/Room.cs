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
        public int TickRate { get; set; } = 0;
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

        private bool _isStarted = false;
        private bool _firstPlayerConnected = false;
        private CancellationTokenSource? startTimerCts;

        public bool IsStarted => _isStarted;

        // Constructor
        protected Room(string name = "")
        {
            // Init room, create unique id
            Id = NextRoomId++;
            Name = name;

            // Register rooms with the server
            rooms.Add(this);
            RoomsById[Id] = this;
            if(!string.IsNullOrEmpty(name)) namedRooms[name] = this;

        }

        // Lifecycle methods

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

        public virtual void OnMessageReceived(object message, Player sender)
        {
        }

        // Room API
        public void BroadcastMessage(object data, Player? sender = null)
        {
            byte[] d = data is string s ? Encoding.UTF8.GetBytes(s) : JsonSerializer.SerializeToUtf8Bytes(data);
            ConnectedPlayers.ForEach(p => { if (sender == null || p != sender) p.Send(d); });
        }

        public void DisconnectPlayer(Player player, bool disband = false)
        {
            player.Disconnect(disband);
        }

        public void KickPlayer(Player player)
        {
            DisconnectPlayer(player, true);
        }

        public void BanPlayer(Player player)
        {
            KickPlayer(player);
            BannedPlayerUids.Add(player.uid);
        }

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

            ConnectedPlayers.Add(player);

            if (!_firstPlayerConnected)
            {
                _firstPlayerConnected = true;
                HandleFirstPlayerConnection();
            }
            player.activeRoom = this;
            OnPlayerConnected(player, isNew: true);

            // Check if game should start
            if (!_isStarted && (!WaitForMinPlayers || ConnectedPlayers.Count >= MinPlayers)) Start();

            return true;

        }

        public void RemovePlayer(Player player)
        {
            ConnectedPlayers.Remove(player);
            OnPlayerDisconnected(player, disband: true);

            // unset the active room if set to this room,
            // the player may still be referenced by other rooms
            if (player.activeRoom == this) player.activeRoom = null;
        }

        private void HandleFirstPlayerConnection()
        {
            if (WaitForMinPlayers && WaitTime > 0)
            {
                ScheduleStartAfterWaitTime();
            }
            else if (!WaitForMinPlayers && WaitTime <= 0)
            {
                Start();
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
                        Start();
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

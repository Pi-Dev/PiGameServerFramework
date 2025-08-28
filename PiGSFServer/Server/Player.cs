using Auth;
using PiGSF.Utils;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace PiGSF.Server
{
    public partial class Player
    {
        public readonly int id; // Unique player ID (server-global)
        public string uid = "anon:guest";
        public PlayerData playerData;

        public bool isSpectator;
        public bool isBot;

        public Action _CloseConnection;
        public Action<byte[]> _SendData;

        public object UserData { get; set; } // Game-specific user data

        // Careful with changing this
        public Room activeRoom;
        internal TcpTransport.ClientState tcpTransportState;

        List<Room>? _rooms = null;
        public List<Room> rooms
        {
            get
            {
                if (_rooms == null) _rooms = Room.FindAllWithPlayer(this);
                return _rooms;
            }
            set { _rooms = null; } // any write will invalidate the rooms cache
        }

        // This disbands the player from current active room and joins him to the new room
        public void TransferToRoom(Room? destination, bool disband = false)
        {
            if (activeRoom != destination)
            {
                _rooms = null;
                if (disband) activeRoom?.RemovePlayer(this);
                if (destination != null)
                {
                    if (!destination.AddPlayer(this))
                        if (activeRoom == Room.defaultRoom) Disconnect();
                        else TransferToDefaultLobby();
                }
                else Disconnect();
            }
        }
        public void TransferToDefaultLobby()
        {
            TransferToRoom(Room.defaultRoom);
        }

        // This joins the player to destination additively, 
        // and by default sets the active room to destination
        // A chat room and matchmaker room can send messages to game room,
        // with the player connected to all of them
        public void JoinRoom(Room? destination, bool setActive = true)
        {
            _rooms = null;
            if (destination != null)
            {
                destination.AddPlayer(this);
            }
            if (setActive) activeRoom = destination;
        }

        internal volatile int isConnected = 1;
        public bool IsConnected() => isBot ? true : isConnected != 0 && _SendData != null;

        public Player(int id)
        {
            this.id = id;
        }

        public void Send(byte[] data) => _SendData?.Invoke(data);

        // Automation / BOT APIs
        public void InjectMessageInActiveRoom(byte[] data) 
            => activeRoom?.messageQueue.EnqueueAndNotify(new Room.PlayerMessage { pl = this, msg = data });
        public void InjectMessageInConnectedRooms(byte[] data)
            => rooms.ForEach( r=> r.messageQueue.EnqueueAndNotify(new Room.PlayerMessage { pl = this, msg = data }));

        /// Thread-safe
        public void Disconnect(bool disband = false)
        {
            if (Interlocked.Exchange(ref isConnected, 0) == 0)
                return; // Another thread also called into Disconnect

            _CloseConnection?.Invoke();
            _SendData = null;
            _CloseConnection = null;
            var rooms = Room.FindAllWithPlayer(this);
            foreach (var r in rooms)
            {
                if (disband) r.RemovePlayer(this);
                else r.messageQueue.EnqueueAndNotify(new Room.PlayerDisconnect { pl = this, disband = false });
            }
            _rooms = null;
        }

        /// Misc / Debug
        public string ToTableString()
        {
            return
                /* Id  */ id.ToString().PadRight(5) + "| " +
                /* username */ username.PadRight(16) + " | " +
                // /* name */ name.PadRight(32) + " | " +
                /* uid */ uid.PadRight(48) + " |";
        }
}
}

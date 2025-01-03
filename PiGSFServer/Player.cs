using Transport;
using System;
using PiGSF.Utils;

namespace PiGSF.Server
{
    public partial class Player
    {
        public readonly int id; // Unique player ID (server-global)
        public string uid = "anon:guest";

        public bool isSpectator;

        public Action? _CloseConnection;
        public Action<byte[]> _SendData;

        public object? UserData { get; set; } // Game-specific user data

        // Caeful with changing this
        public Room activeRoom;

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
        public void TransferToRoom(Room? destination)
        {
            if (activeRoom != destination)
            {
                _rooms = null;
                activeRoom?.RemovePlayer(this);
                if (destination != null)
                    destination.AddPlayer(this);
                else Disconnect();
            }
        }
        public void TransferToDefaultLobby() => TransferToRoom(Server.defaultRoom);

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
        public bool IsConnected() => isConnected != 0 && _SendData != null;

        public Player(int id)
        {
            this.id = id;
        }

        public void Send(byte[] data) => _SendData?.Invoke(data);

        /// Thread-safe
        public void Disconnect(bool disband = false)
        {
            if (Interlocked.Exchange(ref isConnected, 0) == 0)
                return; // Another also called into Disconnect

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
    }
}

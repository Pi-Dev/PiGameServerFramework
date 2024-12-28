using Transport;
using System;

namespace PiGSF.Server
{
    public partial class Player
    {
        public readonly int id; // Unique player ID (server-global)
        public string uid = "anon:guest";

        public bool isSpectator;

        public Action? _CloseConnection;
        public Action<byte[]>? _SendData;

        public object? UserData { get; set; } // Game-specific user data

        // Caeful with changing this
        public Room activeRoom;

        // This disbands the player from current active room and joins him to the new room
        public void TransferToRoom(Room? destination)
        {
            if (activeRoom != destination)
            {
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
            if (destination != null)
                destination.AddPlayer(this);
            if (setActive) activeRoom = destination;
        }

        internal bool isConnected = true;
        public bool IsConnected() => isConnected && _SendData != null;

        public Player(int id)
        {
            this.id = id;
        }

        public void Send(byte[] data) => _SendData?.Invoke(data);

        public void Disconnect(bool disband = false)
        {
            _CloseConnection?.Invoke();
            _SendData = null;
            _CloseConnection = null;
            isConnected = false;
            foreach (var r in Room.FindAllWithPlayer(this))
            {
                r.OnPlayerDisconnected(this, disband: false);
                if (disband) r.RemovePlayer(this);
            }
        }
    }
}

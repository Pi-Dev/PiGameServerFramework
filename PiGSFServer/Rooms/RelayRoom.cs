using PiGSF.Server;

namespace PiGSF.Rooms
{
    public class RelayRoom : Room
    {
        public RelayRoom(string name = "") : base(name)
        {
            MaxPlayers = int.MaxValue;
            MinPlayers = 0;
            WaitTime = 0;
            Log.Write($"RelayRoom {Name} created.");
        }

        protected override void OnPlayerConnected(Player player, bool isNew)
        {
            Log.Write($"[ == {player.name} joined == ]");
        }

        protected override void OnPlayerDisconnected(Player player, bool disband)
        {
            string end = disband ? "left the room" : "lost connection";
            Log.Write($"[ == {player.name} {end} == ]");
            RemovePlayer(player);
        }

        protected override void OnMessageReceived(byte[] message, Player sender)
        {
            Log.Write($"[{Name}] relayed {message.Length} bytes from {sender.name}");
            BroadcastMessage(message);
        }

        protected override void OnShutdownRequested()
        {
            base.OnShutdownRequested();
            Log.Write("RelayRoom.OnShutdownRequested()");
            Log.Write("Marking as eligibleForDeletion");
            eligibleForDeletion = true;
        }
    }
}

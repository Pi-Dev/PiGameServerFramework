using System;
using System.Collections.Generic;
using System.Text;
using PiGSF.Server;
using PiGSF.Server.Utils;
using static System.Net.Mime.MediaTypeNames;

namespace PiGSF.Rooms
{
    public class ChatRoom : Room
    {
        public ChatRoom(string name = "") : base(name)
        {
            Log.Write("BEGIN");
            for (int i = 0; i < 100; i++)
                Log.Write("_textView.MoveEnd(); // Scroll to the end");
            Log.Write("END");
        }

        protected override void Start()
        {
            Log.Write($"ChatRoom {Name} started.");
        }

        protected override void OnPlayerConnected(Player player, bool isNew)
        {
            BroadcastMessage(Message.Create($"{player.name} has joined the chat."), null);
        }

        protected override void OnPlayerDisconnected(Player player, bool disband)
        {
            string end = disband ? "left the room": "lost connection";
            Log.Write($"[{player.name} {end}]");
            if(disband) BroadcastMessage(Message.Create($"{player.name} has left the chat."), null);
            RemovePlayer(player);
        }

        protected override void OnMessageReceived(byte[] message, Player sender)
        {
            var text = Encoding.UTF8.GetString(message);
            if (text.StartsWith("crash")) throw new Exception("Debugging error");
            Log.Write($"[{Name}] {sender.name}: {text}");
            BroadcastMessage(Message.Create($"{sender.name}: {text}"), sender);
        }

        protected override void OnShutdownRequested()
        {
            base.OnShutdownRequested();
            Log.Write("ChatRoom.OnShutdownRequested()");
            Log.Write("Marking as eligibleForDeletion");
            eligibleForDeletion = true; // room will continue to run, but server is allowed to terminate it
        }
    }
}

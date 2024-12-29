using System;
using System.Collections.Generic;
using System.Text;
using PiGSF.Server;
using PiGSF.Server.Utils;

namespace PiGSF.Rooms
{
    public class ChatRoom : Room
    {
        public ChatRoom(string name = "") : base(name)
        {
        }

        public override void Start()
        {
            Console.WriteLine($"ChatRoom {Name} started.");
            AllowPlayers = false;
        }

        public override void OnPlayerConnected(Player player, bool isNew)
        {
            BroadcastMessage(Message.Create($"{player.name} has joined the chat."), null);
        }

        public override void OnPlayerDisconnected(Player player, bool disband)
        {
            BroadcastMessage(Message.Create($"{player.name} has left the chat."), null);
        }

        public override void OnMessageReceived(byte[] message, Player sender)
        {
            var text = Encoding.UTF8.GetString(message);
            Console.WriteLine($"[{Name}] {sender.name}: {text}");
            BroadcastMessage(Message.Create($"{sender.name}: {text}"), sender);
        }
    }
}

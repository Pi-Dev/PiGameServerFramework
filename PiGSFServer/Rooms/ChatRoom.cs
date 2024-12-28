using System;
using System.Collections.Generic;
using PiGSF.Server;

namespace PiGSF.Rooms
{
    public class ChatRoom : Room
    {
        public ChatRoom(string name = ""): base(name)
        {
        }

        public override void Start()
        {
            Console.WriteLine($"ChatRoom {Name} started.");
            AllowPlayers = false;
        }

        public override void OnPlayerConnected(Player player, bool isNew)
        {
            BroadcastMessage($"{player.name} has joined the chat.", null);
        }

        public override void OnPlayerDisconnected(Player player, bool disband)
        {
            BroadcastMessage($"{player.name} has left the chat.", null);
        }

        public override void OnMessageReceived(object message, Player sender)
        {
            if (message is string textMessage)
            {
                Console.WriteLine($"[{Name}] {sender.name}: {textMessage}");
                BroadcastMessage($"{sender.name}: {textMessage}", sender);
            }
        }
    }
}

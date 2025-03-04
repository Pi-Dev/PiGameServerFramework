﻿using System;
using System.Collections.Generic;
using System.Text;
using PiGSF.Server;
using static System.Net.Mime.MediaTypeNames;

namespace PiGSF.Rooms
{
    public class ChatRoom : Room
    {
        public ChatRoom(string name = "") : base(name)
        {
            MaxPlayers = int.MaxValue;
            MinPlayers = 0;
            WaitTime = 0;
            Log.Write($"ChatRoom {Name} created.");
            Server.RESTManager.Register($"/chats/{this.Id}", (r) => {
                var sb = new StringBuilder();
                sb.Append("<html><head><title>Chat</title></head><body><h1>Chat</h1><ul>");
                foreach (var m in this.Log.roomBuffer)
                {
                    sb.Append($"<li>{m}</li>");
                }
                sb.Append("</ul></body></html>");
                return Response.Html(sb.ToString());
            });
        }

        protected override void OnPlayerConnected(Player player, bool isNew)
        {
            Log.Write($"[ == {player.name} joined == ]");
            //BroadcastMessage(Message.Create($"{player.name} has joined."), null);
        }

        protected override void OnPlayerDisconnected(Player player, bool disband)
        {
            string end = disband ? "left the room": "lost connection";
            Log.Write($"[ == {player.name} {end} == ]");
            //if(disband) BroadcastMessage(Message.Create($"{player.name} has left."), null);
            RemovePlayer(player); // Chatrooms just forget players
        }

        protected override void OnMessageReceived(byte[] message, Player sender)
        {
            var text = Encoding.UTF8.GetString(message);
            if (text.StartsWith("crash")) throw new Exception("Debugging error");
            Log.Write($"[{Name}] {sender.name}: {text}");
            BroadcastMessage(Encoding.UTF8.GetBytes($"{sender.name}: {text}"), sender);
        }

        protected override void OnServerCommand(string s)
        {
            BroadcastMessage(Encoding.UTF8.GetBytes(s));
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

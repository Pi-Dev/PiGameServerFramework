Pi GameServer Framework is a developer-oriented authoritative or relayed, MMO optimized, multi-room/multi-game server framework.
Intended use is C# / Unity games using System.Net.Sockets or WebSockets.
The server is intended to be run on Linux via dotnet, no AOT support.

The framework is based on concept of Rooms that implement game logic, and connectivity is based on sending / processing messages.
This framework does not support high-level things such a sync variables, you have to implement them yourself. 

Upon connection, players join a designated default room. It can be a Lobby management room, a global chat or a matchmaker, it's up to you what this room does.
The server handles player disconnections, IP Address changes and similar reability measures, and once a player reconnects, it will get a response from the server what to do.

The server tracks which rooms a player is connected, and each player is connected to single, active room, but can be referenced by multiple other rooms, 
e.g. the a global LobbyManager and a global ChatRoom can keep a reference, and send messages to, this player. 

Room messages are just byte arrays, it's up to you to implement proper serialization for example, using protobuf. 
Most examples here use binary stream messages, or strings.

Included examples should be modified to match your requirements. 

**This project is under development, do not use it for now!**

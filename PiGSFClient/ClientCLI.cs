using System.Diagnostics;
using System.Text;

namespace PiGSF.Client
{
    public static class Log
    {
        static volatile bool runLogger = false;
        public static Thread Thread;

        // Lockables
        static List<(string msg, ConsoleColor c)> logEntries = new();
        static Queue<(string msg, ConsoleColor c)> incomingEntries = new();
        static readonly object renderLocker = new object(); // Only Console.WriteLine

        public static void Write(string s, ConsoleColor c)
        {
            lock (incomingEntries)
            {
                incomingEntries.Enqueue((s, c));
                Monitor.Pulse(incomingEntries);
            }
        }

        static void WriteConsoleColored((string msg, ConsoleColor c) x)
        {
            if (filtered && !x.msg.StartsWith(filter)) return;
            lock (renderLocker)
            {
                Console.ForegroundColor = x.c;
                Console.WriteLine(x.msg);
            }
        }

        static Log()
        {
            var t = new Thread(LoggerThread);
            t.Name = "Logger";
            t.Start();
        }
        static void LoggerThread()
        {
            while (true)
            {
                (string msg, ConsoleColor c)[] entries;
                lock (incomingEntries)
                {
                    while (incomingEntries.Count == 0) Monitor.Wait(incomingEntries);
                    entries = incomingEntries.ToArray();
                    incomingEntries.Clear();
                }
                lock (logEntries)
                {
                    foreach (var x in entries)
                    {
                        WriteConsoleColored(x);
                        logEntries.Add(x);
                    }
                }
            }
        }

        // Intended to collect logs and write them to the console

        // Logger API
        static volatile bool filtered;
        static volatile string filter;
        public static void SetFilter(string startsWith)
        {
            filter = startsWith;  // volatile ref, order guaranteed
            filtered = true;      // volatile val, order guaranteed
            RenderFullLog();
        }

        static int rendering;
        public static void RenderFullLog()
        {
            if (Interlocked.CompareExchange(ref rendering, 1, 0) != 0) return;
            try
            {
                // Rendering operations
                lock (renderLocker)
                {
                    Console.Clear();
                    foreach (var x in logEntries)
                        WriteConsoleColored(x);
                }
            }
            finally
            {
                // Reset the flag
                rendering = 0;
            }
        }
    }

    public class ClientCLI
    {
        public class ClientState
        {
            public Task task;
        }

        public static int Main(string[] args)
        {
            int ntests = ClientConfig.numberOfTests;
            for (int i = 0; i < ntests; i++)
            {
                TCPTest(i.ToString(), (ConsoleColor)((i + 6) % Enum.GetValues<ConsoleColor>().Length));
            }

            // filter
            if (ntests > 1)
            {
                while (true)
                {
                    var key = Console.ReadLine();
                    Log.SetFilter("[" + key);
                }
            }
            return 0;
        }

        static void TCPTest(string username, ConsoleColor color)
        {
            var t = new Thread(()=>TCPTestThread(username, color));
            t.Name = $"TCP TEST {username}";
            t.Start();
        }

        static void TCPTestThread(string username, ConsoleColor color)
        {
            string playerData = $"anon:DDoS-Shard1-User{username}&User{username}&0p0";
            Log.Write($"[{username}] Connecting to {ClientConfig.serverAddress}:{ClientConfig.serverPort}...", color);
            var client = new Client();
            try
            {
                client.Connect(ClientConfig.serverAddress, ClientConfig.serverPort);
                Log.Write($"[{username}] Connected", color);
            }
            catch (Exception ex)
            {
                Log.Write(ex.ToString(), color);
                return;
            }
            if (!client.isConnected)
            {
                Log.Write($"[{username}] => Connection cannot be established", color);
                return;
            }

            client.SendString(playerData);

            string[] messages = {
                "Bot liked a message: Can we fix this?",
                "Admin uploaded a file: Hello!",
                "User shared a link: Why not?",
                "Admin mentioned you: OMG!",
                "Bot left the chat: Testing...",
                "Developer shared a link: How are you?",
                "Bot deleted a message: I'm confused.",
                "Developer asked a question: See you later.",
                "User asked a question: How are you?",
                "Developer joined the chat: I'm confused.",
                "Developer disliked a message: How are you?",
                "Bot disliked a message: Hello!",
                "Support joined the chat: Goodbye!",
                "User shared a link: Nice try.",
                "User sent a message: How are you?",
                "User disliked a message: What do you think?",
                "Developer uploaded a file: Need help here.",
                "User joined the chat: See you later.",
                "User mentioned you: Hello!",
                "Bot edited a message: What do you think?",
                "Bot deleted a message: Goodbye!",
                "Admin left the chat: Need help here.",
                "Support left the chat: See you later.",
                "User asked a question: See you later.",
                "Admin asked a question: Sure, go ahead.",
                "Support is typing...: Why not?",
                "User deleted a message: I'll be back.",
                "Developer uploaded a file: OMG!",
                "Developer disliked a message: Nice try.",
                "Support shared a link: Error 404.",
                "Admin liked a message: Testing...",
                "Admin sent a message: This is awesome!",
                "User is typing...: Sure, go ahead.",
                "Support sent a message: OMG!",
                "Admin mentioned you: Error 404.",
                "User is typing...: How are you?",
                "Support mentioned you: What's up?",
                "Admin disliked a message: I'll be back.",
                "Bot uploaded a file: Need help here.",
                "Developer disliked a message: Why not?",
                "Bot deleted a message: What do you think?",
                "Developer asked a question: Nice try.",
                "User mentioned you: OMG!",
                "Admin deleted a message: Sure, go ahead.",
                "Support mentioned you: Goodbye!",
                "Admin is typing...: Help me!",
                "Developer asked a question: Nice try.",
                "Bot asked a question: This is awesome!",
                "User joined the chat: Error 404.",
                "Developer joined the chat: Testing...",
                "Support asked a question: Sure, go ahead.",
                "Bot is typing...: Error 404.",
                "Admin mentioned you: How are you?",
                "Bot is typing...: Hello!",
                "Admin mentioned you: Testing...",
                "Developer joined the chat: Why not?",
                "Admin edited a message: OMG!",
                "User asked a question: Error 404.",
                "Bot shared a link: OMG!",
                "User disliked a message: What's up?",
                "Support is typing...: I'll be back.",
                "Bot asked a question: Need help here.",
                "Support left the chat: Hello!",
                "User mentioned you: Help me!",
                "Support is typing...: Goodbye!",
                "Support mentioned you: Why not?",
                "Support liked a message: Why not?",
                "Admin is typing...: Why not?",
                "Admin asked a question: What's up?",
                "User uploaded a file: Why not?",
                "Developer left the chat: See you later.",
                "Bot edited a message: Why not?",
                "Developer is typing...: This is awesome!",
                "User edited a message: This is awesome!",
                "Support mentioned you: I'll be back.",
                "User sent a message: Need help here.",
                "Bot left the chat: Help me!",
                "Bot uploaded a file: Can we fix this?",
                "User disliked a message: Why not?",
                "Admin left the chat: Nice try.",
                "Developer mentioned you: I'll be back.",
                "Bot liked a message: LOL",
                "Bot edited a message: Error 404.",
                "Support asked a question: OMG!",
                "Support mentioned you: This is awesome!",
                "Support edited a message: LOL",
                "User sent a message: Why not?",
                "Bot deleted a message: Error 404.",
                "Admin left the chat: Sure, go ahead.",
                "Bot disliked a message: Can we fix this?",
                "Developer asked a question: OMG!",
                "Admin left the chat: Hello!",
                "Developer uploaded a file: Hello!",
                "Bot edited a message: See you later.",
                "Bot disliked a message: LOL",
                "Developer left the chat: OMG!",
                "Developer shared a link: How are you?",
                "Support edited a message: Goodbye!",
                "Support liked a message: See you later.",
                "Developer disliked a message: Error 404."
            };

            var t = new Stopwatch();
            t.Start();
            long nt = t.ElapsedMilliseconds + 1 + 500 * new Random().Next(0, 10);
            //Thread.Sleep(new TimeSpan(0, 3, 0));
            client.SendString($"Hello fellows! [nt={nt}]");

            if(ClientConfig.numberOfTests == 1)
            {
                Task.Run(() => {
                    while (true)
                    {
                        string? input = Console.ReadLine();
                        client.SendString(input);
                    }
                });
            }

            while (true)
            {
                lock (client.messages) {
                    while (client.messages.TryDequeue(out var m)) Log.Write($"RECV: {Encoding.UTF8.GetString(m)}", color);
                    Monitor.Wait(client.messages, 16);
                }
                if(t.ElapsedMilliseconds > nt)
                {
                    var nextTime = 1 + 1000 * new Random().Next(0, 10);
                    nt = t.ElapsedMilliseconds + nextTime;
                    string randomMessage = messages[new Random().Next(messages.Length)];
                    client.SendString(randomMessage + $" [nt={nextTime}]");
                }
            }

            //while (true)
            //{
            //    int st = 1 + 500 * new Random().Next(0, 5);
            //    Thread.Sleep(st);
            //    string randomMessage = messages[new Random().Next(messages.Length)];
            //    client.SendString(randomMessage);
            //}

            //Start main client loop
            while (true)
            {
                string? input = Console.ReadLine();
                client.SendString(input);
            }
        }
    }
}

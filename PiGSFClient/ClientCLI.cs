using Microsoft.VisualBasic;
using PiGSF.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

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
 
        public static int Main(string[] args)
        {
            for (int i = 0; i < 1000; i++)
            {
                TCPTest(i.ToString(), (ConsoleColor)( (i + 6) % Enum.GetValues<ConsoleColor>().Length));
            }

            while (true)
            {
                var key = Console.ReadLine();
                Log.SetFilter("[" + key);
            }
        }

        static async Task TCPTest(string username, ConsoleColor color)
        {
            string playerData = username;
            Log.Write($"[{username}] Connecting to {ClientConfig.serverAddress}:{ClientConfig.serverPort}...", color);
            var client = new Client();
            try
            {
                await client.Connect(ClientConfig.serverAddress + ":" + ClientConfig.serverPort);
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

            // Message Pump Thread
            var mpt = new Thread(() =>
            {
                List<byte[]> messages;
                lock (client.messages)
                {
                    if(client.messages.Count == 0) Monitor.Wait(client.messages);
                    messages = client.GetMessages();
                }
                foreach(var m in messages)
                    Log.Write($"[{username}] RECV: {Encoding.UTF8.GetString(m).Substring(1)}", color);
            });
            mpt.Name = $"MPT for [{username}]";
            mpt.Start();

            // Start main client loop
            // while (true)
            // {
            //     string? input = Console.ReadLine();
            //     client.SendString(input);
            // }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Client
{


    public class ClientCLI
    {
        static void LogColored(string s, ConsoleColor c)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = c;
                Console.WriteLine(s);
            }
        }
        public static async Task<int> Main(string[] args)
        {
            for (int i = 0; i < 5; i++)
            {
                TCPTest("User " + i, (ConsoleColor)i + 6);
            }

            await Task.Delay(60 * 1000 * 10);
            return 0;
        }

        static async Task TCPTest(string username, ConsoleColor color)
        {
            string playerData = username;
            LogColored($"[{username}] Connecting to {ClientConfig.serverAddress}:{ClientConfig.serverPort}...", color);
            var client = new Client();
            try
            {
                await client.Connect(ClientConfig.serverAddress + ":" + ClientConfig.serverPort);
                LogColored($"[{username}] Connected", color);

            }
            catch (Exception ex)
            {
                LogColored(ex.ToString(), color);
                return;
            }
            if (!client.isConnected)
            {
                LogColored($"[{username}] => Connection cannot be established", color);
                return;
            }

            client.SendString(playerData);

            // Message Pump Thread
            var mpt = new Thread(() =>
            {
                while (true)
                {
                    var m = client.GetMessage();
                    if (m != null)
                    {
                        LogColored($"[{username}] RECV: {Encoding.UTF8.GetString(m)}", color);
                    };
                    if (m == null) Thread.Sleep(16);
                }
            });
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

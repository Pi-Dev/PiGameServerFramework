using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Client
{
    public class ClientCLI
    {
        public static async Task<int> Main(string[] args)
        {
            for(int i = 0; i < 10; i++)
            {
                TCPTest("User "+i);
            }

            await Task.Delay(60 * 1000 * 10);
            return 0;
        }

        static async Task TCPTest(string username)
        {
            string playerData = username;

            Console.WriteLine($"[{username}] Connecting to {ClientConfig.serverAddress}:{ClientConfig.serverPort}...");
            var client = new Client();
            try
            {
                await client.Connect(ClientConfig.serverAddress + ":" + ClientConfig.serverPort);
                Console.WriteLine($"[{username}] Connected");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return;
            }
            if (!client.isConnected)
            {
                Console.WriteLine("Connection cannot be established");
                return;
            }

            client.SendString(playerData);

            // Message Pump Thread
            var mpt = new Thread(() => {
                while (true)
                {
                    var m = client.GetMessage();
                    if (m != null) Console.WriteLine("Message: " + Encoding.UTF8.GetString(m));
                    if(m==null) Thread.Sleep(16);
                }
            });
            mpt.Start();

            // Start main client loop
            while (true)
            {
                Thread.Sleep(5000);
                string? input = username + " is calling?";
                client.SendString(input);
            }
        }
    }
}

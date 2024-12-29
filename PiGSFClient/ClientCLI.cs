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
            await TCPTest();
            return 0;
        }

        static async Task TCPTest()
        {
            string playerData = "Username";

            var client = new Client();
            Console.WriteLine($"Connecting to {ClientConfig.serverAddress}:{ClientConfig.serverPort}...");
            try
            {
                await client.Connect(ClientConfig.serverAddress + ":" + ClientConfig.serverPort);
                Console.WriteLine($"Connected");

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
                }
            });
            mpt.Start();

            // Start main client loop
            while (true)
            {
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                else
                {
                    client.SendString(input);
                }

            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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

            var client = new Client(playerData);
            Console.WriteLine($"Connecting to {ClientConfig.serverAddress}:{ClientConfig.serverPort}...");
            try
            {
                await client.Connect(ClientConfig.serverAddress, ClientConfig.serverPort);
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

            // Start main client loop
            while (true)
            {
                var m = client.GetMessage();
                if (m!=null) Console.WriteLine(m);

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

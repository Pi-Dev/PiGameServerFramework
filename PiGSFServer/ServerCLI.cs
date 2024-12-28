using Auth;
using System;

namespace PiGSF.Server
{
    class ServerCLI
    {

        static void Main(string[] args)
        {
            Console.WriteLine("Pi Game Server Framework by Pi-Dev");
            int port = int.Parse(ServerConfig.Get("bindPort"));
            var server = new Server(port);
            server.Start();

            Console.WriteLine("Type 'help' for commands.");
            while (true)
            {
                var input = Console.ReadLine();
                if (input == "exit")
                {
                    server.Stop();
                    break;
                }
                else if(input == "keys")
                {
                    var keys = RSAEncryption.GenerateRSAKeyPairs(512);
                    Console.WriteLine();
                    Console.WriteLine(keys.PrivateKey);
                    Console.WriteLine();
                    Console.WriteLine(keys.PublicKey);
                    Console.WriteLine();
                }
                else if (input == "rooms")
                {
                }
                else if (input == "help")
                {
                    Console.WriteLine("""
                        exit - Stops the server
                        keys - Generates and RSA key pair and PRINTS TO THE CONSOLE the private and public key
                        rooms - Displays room info
                        """);
                }
                else
                {
                    Console.WriteLine("Unknown command. Type 'help' for a list of commands.");
                }
            }
        }
    }
}

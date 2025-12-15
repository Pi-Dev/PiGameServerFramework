using System;
using System.Threading;
using System.Threading.Tasks;

namespace PiGSF.Server
{
    public class ServerCLI
    {
        static async void UpdatePromptLoop()
        {
            while (true)
            {
                await Task.Delay(500);
                Server.UpdatePrompt(true);
            }
        }

        public static Thread StartServer()
        {
            ServerLogger.Log("Pi Game Server Framework by Pi-Dev");
            int port = int.Parse(ServerConfig.Get("bindPort"));
            var t = new Thread(() => Server.Start(port));
            t.Name = "Server Thread (Start)";
            t.Start();
            return t;
        }

        public static void ConsoleInterfaceLoop()
        {
            UpdatePromptLoop();
            while (!Server.IsActive()) Thread.Sleep(16);
            while (Server.IsActive())
            {                
                // INPUT 
                {
                    var key = Console.ReadKey(false);
                    if (key.Key == ConsoleKey.Enter)
                    {
                        var ibuf = ServerLogger.inputBuffer;
                        Console.WriteLine();// peserve what we typed
                        ServerLogger.inputBuffer = "";
                        Server.HandleCommand(ibuf);
                    }
                    else if (key.Key == ConsoleKey.Backspace && ServerLogger.inputBuffer.Length > 0)
                    {
                        // Handle backspace
                        var ibuf = ServerLogger.inputBuffer;
                        ServerLogger.inputBuffer = ibuf.Substring(0, ibuf.Length - 1);
                        ServerLogger.WritePrompt();
                    }
                    else if (key.Key != ConsoleKey.Backspace)
                    {
                        // Append typed character to input buffer
                        ServerLogger.inputBuffer += key.KeyChar;
                    }
                }
                /**/
            }
        }

        public static void Exec()
        {
            var t = StartServer();
            ConsoleInterfaceLoop();
			
            t.Join();
        }
    }
}

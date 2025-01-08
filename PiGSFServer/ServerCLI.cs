using System.Diagnostics;

namespace PiGSF.Server
{
    class ServerCLI
    {
        static async void UpdatePromptLoop()
        {
            while (true)
            {
                await Task.Delay(500);
                Server.UpdatePrompt(true);
            }
        }

        static int Main(string[] args)
        {
            ServerLogger.Log("Pi Game Server Framework by Pi-Dev");
            int port = int.Parse(ServerConfig.Get("bindPort"));

            var t = new Thread(() => Server.Start(port));
            t.Name = "Server Thread";
            t.Start();
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
            }
            t.Join();
            return 0;
        }
    }
}

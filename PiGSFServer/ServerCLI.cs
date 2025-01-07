using System.Diagnostics;

namespace PiGSF.Server
{
    class ServerCLI
    {
        static StreamReader cin = new StreamReader(Console.OpenStandardInput());
        static string ReadLine()
        {
            return cin!.ReadLineAsync()!.Result;
        }

        static int Main(string[] args)
        {
            ServerLogger.Log("Pi Game Server Framework by Pi-Dev");
            int port = int.Parse(ServerConfig.Get("bindPort"));

            var t = new Thread(() => Server.Start(port));
            t.Name = "Server Thread";
            t.Start();

            while (!Server.IsActive()) Thread.Sleep(16);
            while (Server.IsActive())
            {
                int current = 0, total = 0; 
                string prefix;
                var r = ServerLogger.currentRoomChannel;
                if (r == null)
                {
                    prefix = "[Server]";
                    Server.knownPlayers.ForEach(p => { if (p.IsConnected()) current++; total++; });
                }
                else
                {
                    prefix = $"[{r.GetType().Name}:{r.Id}]";
                    prefix += $"[{r.Name}]";
                    r.players.ForEach(p => { if (p.IsConnected()) current++; total++; });
                }
                Console.Write($"{prefix}({current}/{total})> ");
                var input = ReadLine();
                Server.HandleCommand(input);
            }

            cin.Dispose();
            t.Join();
            return 0;
        }
    }
}

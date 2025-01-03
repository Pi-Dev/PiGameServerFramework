namespace PiGSF.Server
{
    class ServerCLI
    {
        static void Main(string[] args)
        {
            Server server = null;
            ServerLogger.Log("Pi Game Server Framework by Pi-Dev");
            int port = int.Parse(ServerConfig.Get("bindPort"));
            server = new Server(port);

            var t = new Thread(() => server.Start());
            t.Name = "Server Thread";
            t.Start();

            while (!server.IsActive()) Thread.Yield();
            while (server!.IsActive())
            {
                Console.Write("> ");
                var input = Console.ReadLine(); // the wait place of main thread
                server.HandleCommand(input);
            }

            t.Join();
        }
    }
}

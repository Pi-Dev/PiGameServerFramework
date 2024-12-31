using Auth;
using PiGSF.Server.TUI;
using System;
using Terminal.Gui;

namespace PiGSF.Server
{
    class ServerCLI
    {
        static void Main(string[] args)
        {
            //Console.SetOut(new ConsoleWriteHandler());

            ConfigurationManager.RuntimeConfig = """{ "Theme": "Dark" }""";
            //Application.Init(null, "NetDriver");
            
            // Setup GUI overrides
            MessageBox.DefaultBorderStyle = LineStyle.Double;
            Dialog.DefaultBorderStyle = LineStyle.Single;
            Dialog.DefaultShadow = ShadowStyle.Transparent;

            Application.KeyBindings.Clear(Command.Quit);
            //var ui = new ServerMainUI();

            var t = new Thread(() => {
                ServerLogger.Log("Pi Game Server Framework by Pi-Dev");
                int port = int.Parse(ServerConfig.Get("bindPort"));
                var server = new Server(port);
                //ui.server = server;
                server.Start();
            });
            t.Name = "ServerThread";
            t.Start();
  
            //Application.Run(ui);
            //ui.Dispose();
        }
    }
}

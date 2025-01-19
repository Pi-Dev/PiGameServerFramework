using PiGSF.Rooms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // this will override the one set in config
            // Server.Room.CreateDefaultRoom = () => new ChatRoom("TESTRoom");

            // Create a Web Server
            var wwwpath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "/wwwroot";
            Server.RESTManager.Register("/*", new Server.DirectoryFileServer(wwwpath));

            Server.ServerCLI.Exec();
        }
    }
}

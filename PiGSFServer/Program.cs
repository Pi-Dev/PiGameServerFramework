using PiGSF.Ratings;
using PiGSF.Rooms;
using PiGSF.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF
{
    public static class Program
    {
        static void TESTS()
        {
            GDSkill.Params p1 = GDSkill.Params.Default;
            GDSkill.Params p2 = GDSkill.Params.Default;
            Dictionary<int, int> Wins = new Dictionary<int, int>();
            Wins[0]=0;
            Wins[1]=0;
            Action<int> print = (int i) =>
            {
                Console.ForegroundColor = i==1?ConsoleColor.Red:ConsoleColor.Green;
                Console.WriteLine($"{p1,-30} {(i == 0 ? "<<    " : "  >>  ")} {p2}");
                Wins[i]++;
            };
            print(0);

            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player2Won); print(1);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player2Won); print(1);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);
            (p1, p2) = GDSkill.UpdateRatings(p1, p2, GDSkill.GameOutcome.Player1Won); print(0);

            Console.WriteLine("== PLACED ==");
            for(int i = 0; i < 1000; i++)
            {
                var outcome = (GDSkill.GameOutcome) Utils.Utils.Choose(0,0,0,0,1);
                (p1, p2) = GDSkill.UpdateRatings(p1, p2, outcome); print(outcome==GDSkill.GameOutcome.Player2Won?1:0);
            }

            Console.WriteLine("== FINAL ==");
            Console.WriteLine($"LEFT  {Wins[0]}  ====  {Wins[1]}  RIGHT");
            Console.WriteLine($"Ratio = { (float) Wins[1] / Wins[0]}");
        }

        public static void Main(string[] args)
        {

            TESTS();
            return;


            // this will override the one set in config
            int nextMatch = 0;
            Matchmaker.MatchFound Commence = (players) => {
                ServerLogger.Log(string.Join(", ", players.Select(x => x.name)) + " matched.");
                return new ChatRoom($"Match #{nextMatch++}");
            };
            Room.CreateDefaultRoom = () => new Matchmaker(
                2,5, // min/max per match
                OnMatchFound: Commence, 
                SkillFunc: p => p.username[0], // alphabetical ? :D LMAO
                SkillMinDistance: 2, SkillMaxDistance: 6);

            // Create a Web Server
            // var wwwpath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "/wwwroot";
            // Server.RESTManager.Register("/", new Server.StaticFileServer(wwwpath + "/index.html"));
            // Server.RESTManager.Register("/*", new Server.StaticFileServer(wwwpath));

            Server.ServerCLI.Exec();
        }
    }
}

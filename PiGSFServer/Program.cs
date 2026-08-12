using PiGSF.Ratings;
using PiGSF.Rooms;
using PiGSF.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PiGSF
{
    public static class Program
    {
        static int countEndpointHits;

        static void RegisterHttpHarnessEndpoints()
        {
            RESTManager.Register("count", r =>
            {
                var n = Interlocked.Increment(ref countEndpointHits);
                //Console.WriteLine("/count -> " + n);
                return Response.Text(n.ToString());
            });
            RESTManager.Register("POST", "randompost", r =>
            {
                int count = 0;
                var bytes = r.BodyBytes;
                for (int i = 0; i < bytes.Length; i++)
                    if (bytes[i] == 0x7F) count++;
                //Console.WriteLine($"/randompost bytes={bytes.Length} 0x7F={count}");
                return Response.Text($"{bytes.Length}:{count}");
            });
        }

        static void TestHTTPPArser(int blocksize, int ntests)
        {
            RegisterHttpHarnessEndpoints();

            var requestbatch = new MemoryStream();
            var rng = new Random(1234);
            for (int i = 0; i < ntests; i++)
            {
                var data = new byte[1024 * 1024];
                rng.NextBytes(data);
                var header = Encoding.ASCII.GetBytes(
                    "POST /randompost HTTP/1.1\r\n" +
                    "Host: parser\r\n" +
                    "Connection: keep-alive\r\n" +
                    "Content-Type: application/octet-stream\r\n" +
                    $"Content-Length: {data.Length}\r\n\r\n");
                requestbatch.Write(header, 0, header.Length);
                requestbatch.Write(data, 0, data.Length);
            }

            var state = new TcpTransport.ClientState();
            var http = new HTTPProtocol(state);

            requestbatch.Position = 0;
            using var ms = requestbatch;
            var buf = new byte[blocksize];
            while (ms.Position < ms.Length)
            {
                int n = ms.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                var chunk = new byte[n];
                Buffer.BlockCopy(buf, 0, chunk, 0, n);
                var requests = http.AddRequestData(chunk);
                if (requests.Count > 0)
                {
                    foreach (var parsed in requests)
                    {
                    int httpRequestId = Server.Server.AllocateHttpRequestId();
                        Console.WriteLine($"REST[{httpRequestId}] #{state.id} {parsed.Method} {parsed.Path}");
                        if (http.TryUpgradeToWebSocket(httpRequestId, parsed)) continue;
                        Response response;
                        try
                        {
                            response = RESTManager.HandleRequest(parsed);
                            if (response.Body == null && response.BinaryData == null)
                                throw new Exception("Null HTTP Response");
                        }
                        catch (Exception ex)
                        {
                            response = new Response(500, "text/plain", $"Internal Server Error: {ex.Message}");
                        }
                        bool keepAlive = HTTPProtocol.WantsKeepAlive(parsed);
                        http.PublishResponse(httpRequestId, HTTPProtocol.BuildPayload(response, keepAlive), !keepAlive, null);
                    }
                }
            }

            Console.ReadKey();
        }

        static void TESTS()
        {
            Random seeded = new Random(1234); // forced seed
            GDSkill.GDRating p1 = GDSkill.GDRating.Default(10);
            GDSkill.GDRating p2 = GDSkill.GDRating.Default(10);

            Dictionary<int, int> Wins = new Dictionary<int, int>();
            Wins[0]=0;
            Wins[1]=0;
            Action<int> print = (int i) =>
            {
                Console.ForegroundColor = i==1?ConsoleColor.Red:ConsoleColor.Green;
                Console.Write($"{p1,-30}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{(i == 0 ? "<<    " : "  >>  ")}");
                Console.ForegroundColor = i==1?ConsoleColor.Green:ConsoleColor.Red;
                Console.WriteLine($"{p2}");
                Wins[i]++;
            };
            print(0);

            for(int i = 0; i < 10000; i++)
            {
                var outcome = (GDSkill.GameOutcome) Utils.Utils.Choose(seeded, 0,0,0,0,0,1);
                GDSkill.UpdateRatings(p1, p2, outcome); print(outcome==GDSkill.GameOutcome.Side2Won?1:0);

                if (i == 9)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("== PLACED ==");
                }
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("== FINAL ==");
            Console.WriteLine($"LEFT  {Wins[0]}  ====  {Wins[1]}  RIGHT");
            Console.WriteLine($"Ratio = { (float) Wins[0] / Wins[1]}");
        }

        public static void Main(string[] args)
        {
            //TestHTTPPArser(1, 10);
            //return;

            // this will override the one set in config
            int nextMatch = 0;
            Matchmaker.MatchFound Commence = (players) => {
                var room = new ChatRoom($"Match #{nextMatch++}");
                ServerLogger.Log($"MATCH #{nextMatch-1} => " + string.Join(", ", players.Select(x => x.name)));
                return room;
            };
            Room.CreateDefaultRoom = () => new Matchmaker(
                6, 10, // min/max per match
                MaxWaitTime: 60,
                TickInterval: 5,
                OnMatchFound: Commence,
                SkillFunc: p => p.MMR,
                SkillWideningDistance: 80, 
                SkillDistIncreasePerTick: 50
                );
            //{
            //    TickRate = 1
            //};



            // Create a Web Server
            RegisterHttpHarnessEndpoints();
            var wwwpath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "/wwwroot";
            RESTManager.Register("/*", new StaticFileServer(wwwpath, "/", false));

            // Start the server
            ServerCLI.StartServer();

            while (Room.defaultRoom == null) Thread.Sleep(16); // Wait for matchmaker

            // Make some bots
            var r = new Random(1234);
            for (int i = 0; i < 4; i++)
            {
                var p = Server.Server.CreateBotPlayer($"bot:{i}");
                p.MMR = r.Next(60, 400);
                p.username = $"#{p.id};MMR={p.MMR}";
                p.name = p.username;
                
                // Queue the player
                p.JoinRoom(Room.defaultRoom); 
            }

            ServerCLI.ConsoleInterfaceLoop();
        }
    }
}

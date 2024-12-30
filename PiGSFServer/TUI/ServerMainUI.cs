using PiGSF.Server;
using PiGSF.Server.TUI;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace PiGSFServer.TUI
{
    internal class ServerMainUI : Toplevel
    {
        MenuBar _menuBar;
        TextView _consoleTextView;
        TextField _commandTextField;
        StatusBar _statusBar;
        Shortcut rooms, players, status;
        internal Server server;
        CancellationTokenSource statusRoutine = new CancellationTokenSource();

        public ServerMainUI()
        {
            // Initialize the UI components
            InitUI();
        }

        private void InitUI()
        {

            // Create a MenuBar
            _menuBar = new MenuBar();
            _menuBar.Menus = new MenuBarItem[]
            {
                new MenuBarItem("_Server", new MenuItem[]
                {
                    new MenuItem("_Stop", "", StopServer),
                }),
                new MenuBarItem("_Rooms", new MenuItem[]
                {
                    new MenuItem("_List", "", () => {
                        string str = "";
                        Room.rooms.ForEach(r => str += r.Id.ToString().PadRight(6) + " | " + r.GetType().Name + "  [" + r.Name + "]\n");
                        var tx = new TextWindow($" Rooms list ", str);
                        Add(tx);
                    })
                }),
                new MenuBarItem("_Players", new MenuItem[]
                {
                    new MenuItem("_Show", "", () => {
                        string str = "";
                        server.knownPlayers.ForEach(p => str += $"{p.id.ToString().PadRight(6)} | {p.name} ({p.username}) => {p.uid}\n");
                        var tx = new TextWindow($" Rooms list ", str);
                        Add(tx);
                    })
                }),
                new MenuBarItem("_Help", new MenuItem[]
                {
                    new MenuItem("_About", "", () => MessageBox.Query("About", "Pi GameServer Framework :: Server\nCopyright (C) Pi-Dev Bulgaria\nLicensed under MIT\n\nCheck out ColorBlend FX: Desaturation on Steam\n", "OK"))
                })
            };

            // Create a TextView for logs/console output
            _consoleTextView = new TextView
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill() - 2,
                ReadOnly = true,
                WordWrap = true,
                CanFocus = false,
                ColorScheme = new ColorScheme(new Attribute(0xdddddd, 0))
            };
            var writer = new TextViewWriter(_consoleTextView);
            Console.SetOut(writer);


            // Create a TextField for command input
            _commandTextField = new TextField
            {
                X = 2,
                Y = Pos.Bottom(this) - 2,
                Width = Dim.Fill(),
                Height = 1,
                CursorVisibility = CursorVisibility.VerticalFix,
                ColorScheme = new ColorScheme(new Attribute(Color.White, Color.DarkGray)),
            };

            var _ctprefix = new View
            {
                X = 0,
                Y = Pos.Bottom(this) - 2,
                Width = 2,
                Height = 1,
                Text = "> ",
                CanFocus = false,
                ColorScheme = new ColorScheme(new Attribute(Color.White, 0x888888)),
            };

            // Handle Enter key
            _commandTextField.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == KeyCode.Enter)
                {
                    var command = _commandTextField.Text;
                    Console.WriteLine(command);
                    server.HandleCommand(command);
                    _commandTextField.Text = ""; // Clear the field
                    e.Handled = true; // Suppress default Enter behavior

                    if (command.StartsWith("room"))
                    {
                        var parts = command.Split(' ');
                        var rid = parts[1];
                        try
                        {
                            var room = Room.GetById(int.Parse(rid));
                            if (room.Log.logWindow != null) room.Log.logWindow.SetFocus();
                            else
                            {
                                var lw = new LogWindow($"Room {rid} = {room.Name} :: |{room.GetType().Name}", room);
                                Add(lw);
                            }
                        }
                        catch { }
                    }
                }
            };


            // Create a StatusBar
            rooms = new Shortcut(null, $"Rooms:  0", ShowRooms)
            {
                ColorScheme = new ColorScheme(new Attribute(Color.Yellow, Color.Red))
            };
            players = new Shortcut(null, $"Players:  0/0", ShowPlayers);
            status = new Shortcut(null, "", null);
            _statusBar = new StatusBar(new[]
            {
                rooms, players, status
            });

            // Add things to the window
            Add(_menuBar);
            Add(_consoleTextView);
            Add(_ctprefix);
            Add(_commandTextField);
            Add(_statusBar);

            // Status bar routine
            _ = Task.Run(async () =>
            {
                while (!statusRoutine.IsCancellationRequested)
                {
                    //Console.WriteLine("statusRoutine");
                    var rc = Room.rooms.Count;
                    int pc = 0, ac = server != null ? server.knownPlayers.Count() : 0;
                    if (server != null) server.knownPlayers.ForEach(p => { if (p.IsConnected()) pc++; });
                    SetRoomCount(rc);
                    SetPlayerCount(pc, ac);

                    await Task.Delay(1);
                    //await Task.Delay(500); CAUSE RACE CONDITIONS
                }
            });
        }

        public void ShowPlayers() { }
        public void ShowRooms() { }
        public void StopServer()
        {
            Console.WriteLine("Stopping server...");
            server.Stop();
            status.Text = "Waiting for existing games to complete.";
        }

        public void SetPlayerCount(int playerCount, int knownPlayerCount) => Application.Invoke(() => { players.Text = "Players:"; players.Title = $"{playerCount}/{knownPlayerCount}"; });
        public void SetRoomCount(int roomCount) => Application.Invoke(() => { rooms.Text = "Rooms:"; rooms.Title = roomCount.ToString(); });
    }
}

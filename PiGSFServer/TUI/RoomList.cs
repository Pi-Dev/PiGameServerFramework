using PiGSF.Server;
using PiGSF.Server.TUI;
using System.Linq;
using Terminal.Gui;
using Size = System.Drawing.Size;
using Attribute = Terminal.Gui.Attribute;

public class RoomList : Window
{
    static int numTextWindows = 0;
    ServerMainUI main;
    public RoomList(Server server)
    {
        main = Application.Top as ServerMainUI;
        ++numTextWindows;
        // Window
        Title = "> Room List ";
        ColorScheme = new ColorScheme(new Attribute(Color.White, 0x333333));
        X = 15 + numTextWindows;
        Y = numTextWindows;
        Width = Dim.Fill() - 10;
        Height = Dim.Fill() - 6;
        BorderStyle = LineStyle.Double;
        //ShadowStyle = ShadowStyle.Transparent;

        VerticalScrollBar.Visible = true;

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                --numTextWindows;
                Application.Top.Remove(this);
                Dispose();
            };
        };
        Disposing += (s, e) =>
        {
            foreach (var b in buttons) b.Value.Dispose();
        };
        UpdateItems();
    }

    Dictionary<int, Button> buttons = new Dictionary<int, Button>();

    void _updateRooms()
    {
        RemoveAll();
        var l = new List<Room>();
        Room.rooms.ForEach(l.Add);

        for(int i=0;i<l.Count; i++)
        {
            // Get, or create the button
            if (!buttons.TryGetValue(i, out Button button))
            {
                button = new Button()
                {
                    X = -1,
                    Y = i,
                    Width = Dim.Fill(),
                    Height = 1,
                    Text = "- Unloaded",
                    ColorScheme = new ColorScheme(new Attribute(Color.White, 0x444444)),
                    BorderStyle = LineStyle.None,
                    ShadowStyle = ShadowStyle.None,
                    CanFocus = true,
                    TextAlignment = Alignment.Start,
                };
                button.Accepting += (s, e) =>
                {
                    main.HandleUICommand("room " + button.Text.Substring(1).Split("|")[0].Trim());
                };
                buttons[i] = button;
            }

            // Technically we may get raced, so very carefully what we access here!
            var r = l[i];
            var pd = r.GetPlayersData(); // thread-safe call
            bool isStarted = r.IsStarted;
            button.ColorScheme = new ColorScheme(new Attribute(isStarted ? 0x00ff00 : Color.White, 0x444444));
            button.Text = "> " +
                /* Status, Id  */ r.Id.ToString().PadRight(5) + "| " +
                /*current/total*/ (pd.connected + "/" + pd.total).PadRight(7) + " | " +
                r.GetType().Name.PadRight(16) + " | "+
                r.Name;
            Add(button);
        }
        var cs = GetContentSize();
        cs.Height = l.Count;
        SetContentSize(cs);
    }

    public async void UpdateItems()
    {
        await Task.Yield();
        try { _updateRooms(); } catch { Exception ex; }
        this.ScrollVertical(0);
    }
}

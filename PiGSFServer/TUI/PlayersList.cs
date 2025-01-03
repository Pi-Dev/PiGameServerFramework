using PiGSF.Server;
using PiGSF.Server.TUI;
using System.Linq;
using Terminal.Gui;
using Size = System.Drawing.Size;
using Attribute = Terminal.Gui.Attribute;

public class PlayersList : Window
{
    static int numTextWindows = 0;
    ServerMainUI main;
    Server server;
    MenuBar menuBar;
    View view;

    // Sort vars 
    bool showAll = false;
    bool asc = true;
    Func<List<Player>, List<Player>> sorter;
    List<Player> SorterId(List<Player> p) => p.OrderBy(p => p.id, Comparer<int>.Create((a, b) => a + (asc? b:-b))).ToList();
    List<Player> SorterUsername(List<Player> p) => p.OrderBy(p => p.username, Comparer<string>.Create((a, b) => asc? a.CompareTo(b) : b.CompareTo(a))).ToList();
    List<Player> SorterName(List<Player> p) => p.OrderBy(p => p.name, Comparer<string>.Create((a, b) => asc ? a.CompareTo(b) : b.CompareTo(a))).ToList();
    void SortById()
    {
        if (sorter == SorterId) { asc = !asc; }
        else sorter = SorterId;
    }
    void SortByName()
    {
        if (sorter == SorterName) { asc = !asc; }
        else sorter = SorterName;
    }
    void SortByUsername()
    {
        if (sorter == SorterUsername) { asc = !asc; }
        else sorter = SorterUsername;
    }

    public PlayersList(Server server)
    {
        this.server = server;
        main = Application.Top as ServerMainUI;
        sorter = SorterId;

        // Window
        Title = "> Players List ";
        ColorScheme = new ColorScheme(new Attribute(Color.White, 0x333333));
        X = 0;
        Y = 1;
        Width = Dim.Fill();
        Height = Dim.Fill()-1;
        BorderStyle = LineStyle.Double;

        // Menu
        menuBar = new MenuBar();
        menuBar.Menus = new MenuBarItem[]
        {
            new MenuBarItem("[X]", "", ()=>{ Application.Top.Remove(this); }),
            new MenuBarItem("All", "", ()=>{ showAll = true; UpdateItems(); }),
            new MenuBarItem("Connected", "", ()=>{ showAll = false;UpdateItems(); }),
            new MenuBarItem("|", "", null),
            new MenuBarItem("Id", "", SortById),
            new MenuBarItem("Username", "", SortByUsername),
            new MenuBarItem("Name", "", SortByName),
        };
        Add(menuBar);

        view = new View() {
            X = 1,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        view.VerticalScrollBar.Visible = true;
        Add(view);

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

    void _updateItems()
    {
        view.RemoveAll();
        //Add(menuBar);
        var l = new List<Player>();
        server.knownPlayers.ForEach(l.Add);
        if (showAll == false) l = l.Where(x=>x.IsConnected()).ToList();
        l = sorter(l);

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
                    main.HandleUICommand("player " + button.Text.Substring(1).Split("|")[0].Trim());
                };
                buttons[i] = button;
            }

            // Technically we may get raced, so very carefully what we access here!
            var p = l[i];
            button.ColorScheme = new ColorScheme(new Attribute(!p.IsConnected() ? 0x777777 : Color.White, 0x444444));
            button.Text = "> " + p.ToTableString();
            view.Add(button);
        }
        var cs = view.GetContentSize();
        cs.Width = view.Viewport.Width;
        cs.Height = l.Count;
        view.SetContentSize(cs);
    }

    public async void UpdateItems()
    {
        await Task.Yield();
        try { _updateItems(); } catch { Exception ex; }
        //this.ScrollVertical(0);
    }
}

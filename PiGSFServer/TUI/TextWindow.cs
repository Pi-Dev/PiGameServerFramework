using System.Linq;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

public class TextWindow : Window
{
    private readonly TextView _textView;

    static int numTextWindows = 0;
    public TextWindow(string title, string text)
    {
        ++numTextWindows;
        // Window
        Title = title;
        ColorScheme = new ColorScheme(new Attribute(Color.White, Color.DarkGray));
        X = 15+numTextWindows;
        Y = numTextWindows;
        Width = Dim.Fill() - 6;
        Height = Dim.Fill() - 6;
        BorderStyle = LineStyle.Double;
        ShadowStyle = ShadowStyle.Transparent;

        // Textview
        _textView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
            ColorScheme = new ColorScheme(new Attribute(Color.White, 0x444444))
        };


        Add(_textView);

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == KeyCode.Esc)
            {
                --numTextWindows;
                Application.Top.Remove(this);
                Dispose();
            };
        };

        postInit(text);
    }

    async void postInit(string text)
    {
        await Task.Yield();
        _textView.Text = text;
        _textView.MoveHome();
    }
}

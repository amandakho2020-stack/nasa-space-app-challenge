using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// Keeps the words on the 3D outpost's wall boards and desk screens INSIDE their screen.
///
/// The 3D builder guesses the text size ("a letter is 0.6 wide"), but capital letters are wider,
/// so long words (HYDROPONICS, OBJ: PLANT BIO-DOME...) stuck out past the edge. Live boards also
/// change their words while you play. This measures the real text with its real font and shrinks it
/// to fit its own screen (a CSGBox3D named "Panel" next to the label), then centres it.
/// It checks again a few times per second, so changing text keeps fitting too.
/// Words that would have to shrink a lot are only decoration, so they are hidden instead (the screen still glows).
/// In the Command Center ALL board words are removed (the team asked for clean screens there).
/// Created by World3DBridge.
/// </summary>
public partial class BoardTextFitter : Node
{
    private const float Margin = 0.88f;        // use 88% of the screen, leave a small border
    private const float RecheckSeconds = 0.4f;
    private const float MinShrink = 0.9f;      // needs to shrink below 90% of its size? -> hide the words
    private const string CommandCenterPath = "Interiors/CommandCenter"; // under Base: all its words are removed

    private sealed class Board
    {
        public Label3D Label;
        public Vector2 Screen;      // the screen size in metres
        public float MaxPixel;      // never bigger than the builder made it
        public string FittedText;
    }

    private readonly List<Board> _boards = new();
    private float _timer;

    public override void _Ready() => Callable.From(Collect).CallDeferred(); // after the 3D scene is ready

    private void Collect()
    {
        var root = GetTree().CurrentScene?.GetNodeOrNull("Base");
        if (root == null) return;

        // The Command Center: NO words on any of its boards at all (only the glowing screens stay).
        var commandCenter = root.GetNodeOrNull(CommandCenterPath);
        if (commandCenter != null)
            foreach (Node n in commandCenter.FindChildren("*", "Label3D", true, false))
                if (n is Label3D l)
                {
                    l.Visible = false;
                    l.ProcessMode = ProcessModeEnum.Disabled; // live boards stop rewriting their text too
                }
        foreach (Node n in root.FindChildren("*", "Label3D", true, false))
        {
            if (n is not Label3D label || label.Billboard != BaseMaterial3D.BillboardModeEnum.Disabled) continue;
            if (!label.Visible) continue; // already removed (Command Center)
            if (label.GetParent()?.GetNodeOrNull<CsgBox3D>("Panel") is not { } panel) continue;
            if (label.VerticalAlignment == VerticalAlignment.Top) continue; // title-at-the-top screens (e.g. the base map)
            _boards.Add(new Board
            {
                Label = label,
                Screen = new Vector2(panel.Size.X, panel.Size.Y),
                MaxPixel = label.PixelSize,
            });
        }
        GD.Print($"[BoardTextFitter] fitting {_boards.Count} board labels");
        FitAll();
    }

    public override void _Process(double delta)
    {
        _timer -= (float)delta;
        if (_timer > 0f) return;
        _timer = RecheckSeconds;
        FitAll();
    }

    private void FitAll()
    {
        foreach (var b in _boards)
        {
            if (!IsInstanceValid(b.Label) || b.Label.Text == b.FittedText) continue;
            Fit(b);
        }
    }

    private static void Fit(Board b)
    {
        var label = b.Label;
        Font font = label.Font ?? ThemeDB.FallbackFont;
        string text = label.Text;
        if (font == null || string.IsNullOrEmpty(text)) { b.FittedText = text; return; }

        // real size of the text in font pixels (all lines)
        string[] lines = text.Split('\n');
        float widest = 1f;
        foreach (string line in lines)
            widest = Mathf.Max(widest, font.GetStringSize(line, HorizontalAlignment.Left, -1, label.FontSize).X);
        float lineHeight = font.GetHeight(label.FontSize) + label.LineSpacing;
        float tall = Mathf.Max(1f, lineHeight * lines.Length);

        float fit = Mathf.Min(b.Screen.X * Margin / widest, b.Screen.Y * Margin / tall);

        // Doesn't fit without becoming tiny? It's only decoration: hide the words, keep the glowing screen.
        label.Visible = fit >= b.MaxPixel * MinShrink;
        label.PixelSize = Mathf.Min(b.MaxPixel, fit);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.Width = widest + 4f;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Position = new Vector3(0f, 0f, label.Position.Z); // centred on its screen
        b.FittedText = text;
    }
}

using Godot;

namespace AeroKidz;

/// <summary>
/// POWER ROOM - SOLAR PANEL PICTURE PUZZLE.
/// One picture of a blue solar panel is cut into 9 squares (3 x 3) and shuffled.
/// Drag each piece onto the board where it belongs (it snaps in if it's right, bounces back if not).
/// When the solar cell lines all connect, sunlight hits the panel and makes electricity.
/// Success: +PowerReward Power.
///
/// Have a real solar panel picture? Load it instead of BuildPicture():  _picture = GD.Load&lt;Texture2D&gt;("res://...png");
/// </summary>
public partial class SolarPuzzleTask : RoomTask
{
    public const float PowerReward = 45f;

    private const int Pic = 270;               // picture size in pixels
    private const float Tile = 90f;
    private static readonly Vector2 BoardOrigin = new(232, 300);
    private static readonly Vector2 TrayOrigin = new(575, 306);
    private const float TrayGap = 102f;

    private Texture2D _picture;
    private readonly int[] _traySlotOf = new int[9];   // piece -> tray slot
    private readonly bool[] _placed = new bool[9];
    private int _placedCount;

    private int _dragging = -1;
    private Vector2 _dragPos, _grabOffset;
    private float _winTimer = -1f;
    private const float WinAnimTime = 1.8f;

    protected override string Title => "POWER ROOM: FIX THE SOLAR PANEL";
    protected override Color Accent => Art.Orange;
    protected override string HelpText => "Click a piece. Then click where it goes.";

    protected override void Setup()
    {
        _picture = BuildPicture();

        // shuffle pieces into the tray
        var slots = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        for (int i = slots.Length - 1; i > 0; i--)
        {
            int j = Rng.RandiRange(0, i);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }
        for (int p = 0; p < 9; p++) _traySlotOf[p] = slots[p];
    }

    /// <summary>Draws one complete solar-panel picture pixel by pixel (6 x 6 shiny blue cells in a silver frame).</summary>
    private static Texture2D BuildPicture()
    {
        var img = Image.CreateEmpty(Pic, Pic, false, Image.Format.Rgb8);
        const int frame = 10;
        const float cell = (Pic - 2 * frame) / 6f;
        for (int y = 0; y < Pic; y++)
            for (int x = 0; x < Pic; x++)
            {
                Color c;
                if (x < frame || y < frame || x >= Pic - frame || y >= Pic - frame)
                {
                    // silver frame with a darker outer edge and a light inner edge
                    int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(Pic - 1 - x, Pic - 1 - y));
                    c = edge < 2 ? new Color(0.35f, 0.38f, 0.48f) : edge > frame - 3 ? new Color(0.93f, 0.95f, 1f) : new Color(0.75f, 0.79f, 0.87f);
                }
                else
                {
                    float gx = (x - frame) % cell, gy = (y - frame) % cell;
                    if (gx < 2.5f || gy < 2.5f)
                    {
                        c = new Color(0.82f, 0.87f, 0.95f);                              // silver lines between cells
                    }
                    else
                    {
                        // each cell: lighter at the top-left, deeper blue at the bottom-right, with a bevel
                        float u = (gx - 2.5f) / (cell - 2.5f), v = (gy - 2.5f) / (cell - 2.5f);
                        c = new Color(0.22f, 0.5f, 0.98f).Lerp(new Color(0.06f, 0.2f, 0.62f), (u + v) * 0.5f);
                        if (u < 0.08f || v < 0.08f) c = c.Lightened(0.25f);
                        if (u > 0.94f || v > 0.94f) c = c.Darkened(0.25f);
                        if (Mathf.Abs(u - 0.5f) < 0.02f) c = c.Lerp(new Color(0.75f, 0.82f, 0.95f), 0.5f); // thin busbar
                    }
                    // soft diagonal shine across the whole panel
                    float d = Mathf.Abs((x + y) - Pic * 0.62f);
                    if (d < 40f) c = c.Lerp(Colors.White, 0.28f * (1f - d / 40f));
                }
                img.SetPixel(x, y, c);
            }
        // little label + junction box so the corners look different
        img.FillRect(new Rect2I(18, 18, 34, 10), new Color(1f, 0.82f, 0.2f));
        img.FillRect(new Rect2I(Pic - 50, Pic - 30, 32, 16), new Color(0.15f, 0.15f, 0.2f));
        return ImageTexture.CreateFromImage(img);
    }
    private static Rect2 BoardSlot(int i) => new(BoardOrigin + new Vector2(i % 3, i / 3) * Tile, new Vector2(Tile, Tile));
    private static Rect2 TraySlot(int s) => new(TrayOrigin + new Vector2(s % 3, s / 3) * TrayGap, new Vector2(Tile, Tile));
    private static Rect2 Source(int piece) => new(new Vector2(piece % 3, piece / 3) * Tile, new Vector2(Tile, Tile));

    protected override void Tick(float dt)
    {
        if (_dragging >= 0)
            _dragPos = GetLocalMousePosition() - _grabOffset; // piece follows the mouse

        if (_winTimer >= 0f)
        {
            _winTimer -= dt;
            if (_winTimer < 0f)
            {
                Reward(0f, PowerReward, 0f);
                Finish(true);
            }
        }
    }

    // Works with DRAG and with CLICK-CLICK (click a piece, it follows the mouse, click where it goes).
    private Vector2 _pressPos;

    protected override void OnInput(InputEvent e)
    {
        if (_winTimer >= 0f) return;

        if (e is InputEventMouseMotion mm && _dragging >= 0)
            _dragPos = mm.Position - _grabOffset;

        if (e is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left) return;

        if (mb.Pressed)
        {
            if (_dragging >= 0)
            {
                _dragPos = mb.Position - _grabOffset;
                Drop(); // second click: put the piece down
                return;
            }
            for (int p = 0; p < 9; p++)
            {
                if (_placed[p]) continue;
                var r = TraySlot(_traySlotOf[p]);
                if (r.HasPoint(mb.Position))
                {
                    _dragging = p;
                    _grabOffset = new Vector2(Tile / 2, Tile / 2);
                    _dragPos = mb.Position - _grabOffset;
                    _pressPos = mb.Position;
                    Sound(Sfx.Pop, -14f);
                    return;
                }
            }
        }
        else if (_dragging >= 0 && mb.Position.DistanceTo(_pressPos) > 20f)
        {
            _dragPos = mb.Position - _grabOffset;
            Drop(); // released after a real drag
        }
    }

    private void Drop()
    {
        {
            int p = _dragging;
            _dragging = -1;
            var center = _dragPos + new Vector2(Tile / 2, Tile / 2);
            if (BoardSlot(p).Grow(20f).HasPoint(center))
            {
                _placed[p] = true;
                _placedCount++;
                Sound(Sfx.Ding, -10f);
                if (_placedCount == 9)
                {
                    _winTimer = WinAnimTime;
                    Sound(Sfx.Ding);
                    Pop(new Vector2(BoardOrigin.X + Tile * 1.5f, BoardOrigin.Y - 10), "SUNLIGHT = POWER!", Art.Yellow);
                }
            }
            else
            {
                for (int i = 0; i < 9; i++)
                    if (BoardSlot(i).HasPoint(center))
                    {
                        Sound(Sfx.Bad, -12f);
                        Pop(center, "NOT HERE!", Art.Red);
                        break;
                    }
            }
        }
    }

    // ==================================================================
    // DRAWING - power room wall, framed panel on a stand, piece tray, POWER OUTPUT screen
    // ==================================================================
    private static readonly Vector2 SunPos = new(204, 282);

    protected override void DrawTask()
    {
        float done = _placedCount / 9f;
        float winT = _winTimer >= 0f ? 1f - _winTimer / WinAnimTime : 0f;

        // light steel wall with seams and glowing strips
        var wall = new Rect2(176, 256, 928, 430);
        Art.RoundRect(this, wall, 14, new Color(0.6f, 0.65f, 0.78f));
        for (float x = wall.Position.X + 116; x < wall.End.X; x += 116)
            DrawLine(new Vector2(x, wall.Position.Y), new Vector2(x, wall.End.Y), new Color(0.48f, 0.53f, 0.66f), 3f);
        DrawLine(new Vector2(wall.Position.X, 640), new Vector2(wall.End.X, 640), new Color(0.48f, 0.53f, 0.66f), 3f);
        for (int k = 0; k < 4; k++)
            Art.RoundRect(this, new Rect2(230 + k * 230, 656, 110, 10), 5, new Color(1f, 0.75f, 0.25f, 0.55f + 0.3f * Mathf.Sin(Elapsed * 3f + k)));

        // the panel on its stand (legs + frame)
        var board = new Rect2(BoardOrigin, new Vector2(Tile * 3, Tile * 3));
        DrawLine(new Vector2(board.Position.X + 60, board.End.Y), new Vector2(board.Position.X + 40, 640), new Color(0.35f, 0.4f, 0.52f), 10f);
        DrawLine(new Vector2(board.End.X - 60, board.End.Y), new Vector2(board.End.X - 40, 640), new Color(0.35f, 0.4f, 0.52f), 10f);
        Art.RoundRect(this, board.Grow(16), 12, new Color(0.3f, 0.34f, 0.46f));
        Art.RoundRect(this, board.Grow(11), 10, new Color(0.78f, 0.82f, 0.9f));
        Art.RoundRect(this, board.Grow(4), 6, new Color(0.08f, 0.12f, 0.24f));
        foreach (var p in new[] { board.Position - new Vector2(8, 8), new Vector2(board.End.X + 8, board.Position.Y - 8), board.End + new Vector2(8, 8), new Vector2(board.Position.X - 8, board.End.Y + 8) })
            DrawCircle(p, 3.5f, new Color(0.4f, 0.45f, 0.58f));

        // where the held piece would go: green = the right slot
        int target = -1;
        if (_dragging >= 0)
            for (int i = 0; i < 9; i++)
                if (BoardSlot(i).HasPoint(_dragPos + new Vector2(Tile / 2, Tile / 2))) target = i;

        for (int i = 0; i < 9; i++)
        {
            var r = BoardSlot(i);
            if (_placed[i])
            {
                DrawTextureRectRegion(_picture, r, Source(i), winT > 0 ? new Color(1f + 0.4f * winT, 1f + 0.4f * winT, 1f) : Colors.White);
                if (_placedCount < 9) DrawRect(r, new Color(0.05f, 0.08f, 0.18f, 0.6f), false, 1.5f); // seams until it's complete
            }
            else
            {
                Art.RoundRect(this, r.Grow(-4), 6, new Color(0.13f, 0.18f, 0.32f));
                if (i == target)
                    Art.RoundRect(this, r.Grow(-2), 6, i == _dragging ? new Color(0.3f, 1f, 0.45f, 0.35f) : new Color(1f, 0.85f, 0.3f, 0.25f));
            }
        }

        // a small sun in the corner - it shines on the panel at the end
        float sunGlow = 0.6f + 0.4f * Mathf.Sin(Elapsed * 3f);
        DrawCircle(SunPos, 26f + 4f * sunGlow, new Color(1f, 0.75f, 0.2f, 0.25f));
        for (int k = 0; k < 10; k++)
        {
            float a = Elapsed * 0.5f + k * Mathf.Tau / 10f;
            DrawLine(SunPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 22f, SunPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 32f, new Color(1f, 0.8f, 0.25f), 3f);
        }
        DrawCircle(SunPos, 18f, new Color(1f, 0.82f, 0.25f));
        DrawCircle(SunPos, 11f, new Color(1f, 0.95f, 0.65f));
        if (winT > 0f)
            for (int k = 0; k < 5; k++)
            {
                var hit = board.Position + new Vector2(50 + k * 45, 40 + k * 40);
                DrawLine(SunPos, SunPos.Lerp(hit, Mathf.Min(winT * 2.5f, 1f)), new Color(1f, 0.9f, 0.35f, 0.7f), 5f);
            }

        // piece tray (recessed slots)
        var tray = new Rect2(TrayOrigin - new Vector2(14, 14), new Vector2(TrayGap * 2 + Tile + 28, TrayGap * 2 + Tile + 28));
        Art.RoundRect(this, tray.Grow(4), 14, new Color(0.3f, 0.34f, 0.46f));
        Art.RoundRect(this, tray, 12, new Color(0.14f, 0.17f, 0.28f));
        Art.TextCentered(this, "PIECES", tray.GetCenter().X, tray.Position.Y - 8, 15, new Color(0.15f, 0.2f, 0.35f));
        for (int s = 0; s < 9; s++)
            Art.RoundRect(this, TraySlot(s).Grow(2), 6, new Color(0.1f, 0.12f, 0.2f));
        for (int p = 0; p < 9; p++)
        {
            if (_placed[p] || p == _dragging) continue;
            var r = TraySlot(_traySlotOf[p]);
            DrawTextureRectRegion(_picture, r, Source(p));
            DrawRect(r, new Color(0.85f, 0.9f, 1f, 0.6f), false, 1.5f);
        }

        // cable from the panel to the screen (power flows at the end)
        var plug = new Vector2(board.End.X + 16, board.End.Y - 20);
        var screenIn = new Vector2(905, 600);
        var cable = new[] { plug, new Vector2(plug.X + 10, 624), new Vector2(screenIn.X - 20, 624), screenIn };
        DrawPolyline(cable, new Color(0.15f, 0.17f, 0.25f), 9f, true);
        DrawPolyline(cable, new Color(0.35f, 0.38f, 0.5f), 5f, true);
        if (winT > 0f)
            for (int k = 0; k < 6; k++)
            {
                float u = Mathf.PosMod(winT * 2f + k / 6f, 1f);
                var a = u < 0.5f ? cable[1].Lerp(cable[2], u * 2f) : cable[2].Lerp(cable[3], (u - 0.5f) * 2f);
                DrawCircle(a, 5f, new Color(1f, 0.85f, 0.25f));
            }

        DrawOutputScreen(new Rect2(905, 300, 185, 320), done, winT);

        // the piece in your hand: a shadow + a slightly bigger piece
        if (_dragging >= 0)
        {
            var r = new Rect2(_dragPos, new Vector2(Tile, Tile));
            Art.RoundRect(this, new Rect2(r.Position + new Vector2(8, 10), r.Size), 6, new Color(0, 0, 0, 0.3f));
            var big = r.Grow(5);
            DrawRect(big.Grow(3), new Color(1f, 0.9f, 0.35f));
            DrawTextureRectRegion(_picture, big, Source(_dragging));
        }
    }

    /// <summary>POWER OUTPUT screen: % of the panel rebuilt, a bar, and a battery (energy icon) that charges.</summary>
    private void DrawOutputScreen(Rect2 r, float done, float winT)
    {
        Art.RoundRect(this, r.Grow(6), 20, new Color(0.45f, 0.5f, 0.64f));
        Art.RoundRect(this, r, 16, new Color(0.1f, 0.15f, 0.3f));
        foreach (var p in new[] { r.Position + new Vector2(12, 12), new Vector2(r.End.X - 12, r.Position.Y + 12), r.End - new Vector2(12, 12), new Vector2(r.Position.X + 12, r.End.Y - 12) })
            DrawCircle(p, 4f, new Color(0.6f, 0.65f, 0.78f));

        Art.RoundRect(this, new Rect2(r.Position.X + 14, r.Position.Y + 22, r.Size.X - 28, 34), 10, new Color(0.45f, 0.3f, 0.08f));
        Art.TextCentered(this, "POWER OUTPUT", r.GetCenter().X, r.Position.Y + 46, 16, Colors.White, 3);
        float pct = winT > 0f ? 100f : done * 100f;
        Art.TextCentered(this, $"{pct:0}%", r.GetCenter().X, r.Position.Y + 118, 46, Colors.White, 5);
        var bar = new Rect2(r.Position.X + 18, r.Position.Y + 136, r.Size.X - 36, 18);
        Art.RoundRect(this, bar, 9, new Color(0.04f, 0.07f, 0.16f));
        if (pct > 1) Art.RoundRect(this, new Rect2(bar.Position, new Vector2(bar.Size.X * pct / 100f, bar.Size.Y)), 9, new Color(1f, 0.72f, 0.15f));
        Art.TextCentered(this, $"PIECES {_placedCount} / 9", r.GetCenter().X, r.Position.Y + 178, 15, new Color(0.8f, 0.85f, 0.95f));

        // battery that charges
        var bat = new Vector2(r.GetCenter().X, r.End.Y - 70);
        float charge = winT > 0f ? Mathf.Min(1f, done * 0.5f + winT) : done * 0.5f;
        DrawCircle(bat, 44f, new Color(1f, 0.75f, 0.2f, 0.12f + 0.2f * charge));
        Art.Picture(this, "res://AeroKidz/Assets/energy/energy_04.png", bat, 96f, new Color(1f, 1f, 1f, 0.55f + 0.45f * charge));
        Art.TextCentered(this, winT > 0f ? "CHARGING!" : "NO POWER", r.GetCenter().X, r.End.Y - 14, 14, winT > 0f ? Art.Green : Art.Orange, 3);
    }
}

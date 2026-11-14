using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// OXYGEN ROOM - FIX THE OXYGEN PIPES.
/// The pipes are disconnected. Click a pipe piece to rotate it. Guide the oxygen from the TANK (left)
/// to the OXYGEN LEVEL screen (right). Connected steel pipes fill with blue oxygen.
/// Success: +OxygenReward Oxygen.
/// </summary>
public partial class PipeTask : RoomTask
{
    public const float OxygenReward = 45f;

    private const int Cols = 6, Rows = 4;
    private const float Cell = 80f;
    private static readonly Vector2 Origin = new(370, 300);

    // Openings as bits: N=1, E=2, S=4, W=8
    private const int N = 1, E = 2, S = 4, W = 8;

    private readonly int[,] _mask = new int[Cols, Rows];
    private readonly bool[,] _flow = new bool[Cols, Rows];
    private int _startRow, _endRow;
    private float _winTimer = -1f;

    protected override string Title => "OXYGEN ROOM: ELECTROLYSIS PIPES";
    protected override Color Accent => Art.Cyan;
    protected override string HelpText => "ELECTROLYSIS splits water into oxygen. Click pipes to turn them!";

    protected override void Setup()
    {
        _startRow = Rng.RandiRange(0, Rows - 1);
        _endRow = Rng.RandiRange(0, Rows - 1);

        // ---- Build a guaranteed path: in each column go up/down to a row, then step right ----
        var path = new List<Vector2I>();
        int row = _startRow;
        for (int c = 0; c < Cols; c++)
        {
            int target = c == Cols - 1 ? _endRow : Rng.RandiRange(0, Rows - 1);
            path.Add(new Vector2I(c, row));
            while (row != target)
            {
                row += target > row ? 1 : -1;
                path.Add(new Vector2I(c, row));
            }
        }

        var onPath = new bool[Cols, Rows];
        foreach (var p in path) onPath[p.X, p.Y] = true;
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            if (i == 0) _mask[p.X, p.Y] |= W;
            if (i == path.Count - 1) _mask[p.X, p.Y] |= E;
            if (i > 0) _mask[p.X, p.Y] |= DirBit(path[i - 1] - p);
            if (i < path.Count - 1) _mask[p.X, p.Y] |= DirBit(path[i + 1] - p);
        }

        // ---- Filler pieces + scramble ----
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                if (!onPath[c, r])
                    _mask[c, r] = Rng.Randf() < 0.5f ? (N | S) : (N | E);
                int turns = Rng.RandiRange(0, 3);
                for (int t = 0; t < turns; t++) _mask[c, r] = RotateCw(_mask[c, r]);
            }

        UpdateFlow();
        if (IsSolved())
        {
            var first = path[0];
            _mask[first.X, first.Y] = RotateCw(_mask[first.X, first.Y]);
            UpdateFlow();
        }
    }

    private static int DirBit(Vector2I d) =>
        d == new Vector2I(0, -1) ? N : d == new Vector2I(1, 0) ? E : d == new Vector2I(0, 1) ? S : W;

    private static int RotateCw(int m) => ((m << 1) | (m >> 3)) & 15;

    private static int Opposite(int bit) => bit switch { N => S, S => N, E => W, _ => E };

    private void UpdateFlow()
    {
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                _flow[c, r] = false;

        if ((_mask[0, _startRow] & W) == 0)
            return;

        var queue = new Queue<Vector2I>();
        queue.Enqueue(new Vector2I(0, _startRow));
        _flow[0, _startRow] = true;
        var dirs = new[] { (N, new Vector2I(0, -1)), (E, new Vector2I(1, 0)), (S, new Vector2I(0, 1)), (W, new Vector2I(-1, 0)) };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var (bit, d) in dirs)
            {
                if ((_mask[p.X, p.Y] & bit) == 0) continue;
                var q = p + d;
                if (q.X < 0 || q.X >= Cols || q.Y < 0 || q.Y >= Rows || _flow[q.X, q.Y]) continue;
                if ((_mask[q.X, q.Y] & Opposite(bit)) == 0) continue;
                _flow[q.X, q.Y] = true;
                queue.Enqueue(q);
            }
        }
    }

    private bool IsSolved() => _flow[Cols - 1, _endRow] && (_mask[Cols - 1, _endRow] & E) != 0;

    protected override void Tick(float dt)
    {
        if (_winTimer >= 0f)
        {
            _winTimer -= dt;
            if (_winTimer < 0f)
            {
                Reward(OxygenReward, 0f, 0f);
                Finish(true);
            }
        }
    }

    protected override void OnInput(InputEvent e)
    {
        if (_winTimer >= 0f) return;
        if (e is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left) return;

        var local = (mb.Position - Origin) / Cell;
        int c = Mathf.FloorToInt(local.X), r = Mathf.FloorToInt(local.Y);
        if (c < 0 || c >= Cols || r < 0 || r >= Rows) return;

        _mask[c, r] = RotateCw(_mask[c, r]);
        Sound(Sfx.Pop, -12f);
        UpdateFlow();
        if (IsSolved())
        {
            _winTimer = 1.0f;
            Sound(Sfx.Ding);
            Pop(new Vector2(640, 640), "AIR IS FLOWING!", Art.Cyan);
        }
    }

    // ==================================================================
    // DRAWING - spaceship room, steel pipes, O2 tank with gauge, OXYGEN LEVEL screen
    // ==================================================================
    private static readonly Color PipeDark = new(0.16f, 0.18f, 0.25f);
    private static readonly Color PipeSteel = new(0.62f, 0.66f, 0.75f);
    private static readonly Color PipeShine = new(0.92f, 0.95f, 1f);
    private static readonly Color Oxygen = new(0.3f, 0.8f, 1f);

    protected override void DrawTask()
    {
        // light steel wall with panel seams and glowing strips (like the reference room)
        var wall = new Rect2(176, 256, 928, 430);
        Art.RoundRect(this, wall, 14, new Color(0.6f, 0.65f, 0.78f));
        for (float x = wall.Position.X + 116; x < wall.End.X; x += 116)
            DrawLine(new Vector2(x, wall.Position.Y), new Vector2(x, wall.End.Y), new Color(0.48f, 0.53f, 0.66f), 3f);
        DrawLine(new Vector2(wall.Position.X, 640), new Vector2(wall.End.X, 640), new Color(0.48f, 0.53f, 0.66f), 3f);
        for (int k = 0; k < 4; k++)
            Art.RoundRect(this, new Rect2(230 + k * 230, 656, 110, 10), 5, new Color(0.35f, 0.8f, 1f, 0.6f + 0.3f * Mathf.Sin(Elapsed * 3f + k)));

        // dark pipe board with rivets
        var grid = new Rect2(Origin, new Vector2(Cols * Cell, Rows * Cell));
        Art.RoundRect(this, grid.Grow(12), 12, new Color(0.25f, 0.28f, 0.38f));
        Art.RoundRect(this, grid.Grow(6), 10, new Color(0.12f, 0.15f, 0.24f));
        foreach (var corner in new[] { grid.Position, new Vector2(grid.End.X, grid.Position.Y), grid.End, new Vector2(grid.Position.X, grid.End.Y) })
            DrawCircle(corner, 4f, new Color(0.6f, 0.64f, 0.74f));

        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                var cell = new Rect2(Origin + new Vector2(c, r) * Cell, new Vector2(Cell, Cell));
                Art.RoundRect(this, cell.Grow(-3), 6, new Color(0.17f, 0.2f, 0.3f));
                DrawPipePiece(cell.GetCenter(), _mask[c, r], _flow[c, r]);
            }

        // feed pipe: tank -> first column, and last column -> screen
        float inY = Origin.Y + (_startRow + 0.5f) * Cell;
        float outY = Origin.Y + (_endRow + 0.5f) * Cell;
        DrawSteelSegment(new Vector2(300, inY), new Vector2(Origin.X, inY), true);
        DrawFlange(new Vector2(Origin.X - 4, inY), true);
        bool solved = IsSolved();
        DrawSteelSegment(new Vector2(grid.End.X, outY), new Vector2(910, outY), solved);
        DrawFlange(new Vector2(grid.End.X + 4, outY), true);

        DrawTank(new Vector2(250, 470));
        DrawLevelScreen(new Rect2(905, 300, 185, 320), solved);
    }

    /// <summary>One pipe piece: steel arms from the centre to each open side, a joint, and flanges at the edges.</summary>
    private void DrawPipePiece(Vector2 center, int m, bool flowing)
    {
        var arms = new System.Collections.Generic.List<Vector2>();
        if ((m & N) != 0) arms.Add(new Vector2(0, -1));
        if ((m & E) != 0) arms.Add(new Vector2(1, 0));
        if ((m & S) != 0) arms.Add(new Vector2(0, 1));
        if ((m & W) != 0) arms.Add(new Vector2(-1, 0));

        foreach (var d in arms) DrawLine(center, center + d * Cell / 2, PipeDark, 28f);
        foreach (var d in arms) DrawLine(center, center + d * Cell / 2, PipeSteel, 22f);
        foreach (var d in arms)
        {
            var shine = d.X != 0 ? new Vector2(0, -6) : new Vector2(-6, 0);
            DrawLine(center + shine, center + d * Cell / 2 + shine, PipeShine, 4f);
            if (flowing)
            {
                DrawLine(center, center + d * Cell / 2, new Color(Oxygen, 0.85f), 8f);
                // bubbles moving through the pipe
                for (int k = 0; k < 2; k++)
                {
                    float u = Mathf.PosMod(Elapsed * 1.4f + k * 0.5f + center.X * 0.01f, 1f);
                    DrawCircle(center + d * Cell / 2 * u, 3f, new Color(1, 1, 1, 0.9f));
                }
            }
        }
        foreach (var d in arms) DrawFlange(center + d * (Cell / 2 - 5), d.X != 0);

        // joint in the middle with bolts
        DrawCircle(center, 17f, PipeDark);
        DrawCircle(center, 14f, flowing ? new Color(0.55f, 0.8f, 0.95f) : PipeSteel);
        DrawCircle(center + new Vector2(-4, -4), 5f, new Color(1, 1, 1, 0.5f));
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Pi / 4f + k * Mathf.Pi / 2f;
            DrawCircle(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9f, 2f, PipeDark);
        }
    }

    private void DrawFlange(Vector2 p, bool horizontalPipe)
    {
        var size = horizontalPipe ? new Vector2(9, 32) : new Vector2(32, 9);
        Art.RoundRect(this, new Rect2(p - size / 2f - new Vector2(1, 1), size + new Vector2(2, 2)), 3, PipeDark);
        Art.RoundRect(this, new Rect2(p - size / 2f, size), 3, new Color(0.75f, 0.78f, 0.86f));
        var off = horizontalPipe ? new Vector2(0, 11) : new Vector2(11, 0);
        DrawCircle(p + off, 1.8f, PipeDark);
        DrawCircle(p - off, 1.8f, PipeDark);
    }

    private void DrawSteelSegment(Vector2 a, Vector2 b, bool flowing)
    {
        DrawLine(a, b, PipeDark, 28f);
        DrawLine(a, b, PipeSteel, 22f);
        DrawLine(a + new Vector2(0, -6), b + new Vector2(0, -6), PipeShine, 4f);
        if (flowing)
        {
            DrawLine(a, b, new Color(Oxygen, 0.85f), 8f);
            for (int k = 0; k < 3; k++)
                DrawCircle(a.Lerp(b, Mathf.PosMod(Elapsed * 0.9f + k / 3f, 1f)), 3f, Colors.White);
        }
    }

    /// <summary>Blue O2 tank with silver bands, a valve with a yellow light and a pressure gauge (like the reference).</summary>
    private void DrawTank(Vector2 c)
    {
        var body = new Rect2(c.X - 58, c.Y - 120, 116, 250);
        Art.RoundRect(this, new Rect2(c.X - 70, body.End.Y - 6, 140, 34), 10, new Color(0.3f, 0.34f, 0.46f));     // base
        Art.RoundRect(this, body.Grow(4), 40, PipeDark);
        Art.RoundRect(this, body, 38, new Color(0.18f, 0.48f, 0.92f));
        DrawRect(new Rect2(body.Position.X + 14, body.Position.Y + 30, 14, body.Size.Y - 60), new Color(1, 1, 1, 0.25f)); // shine
        // silver label band with O2
        var band = new Rect2(body.Position.X - 4, c.Y - 46, body.Size.X + 8, 96);
        Art.RoundRect(this, band, 8, new Color(0.88f, 0.9f, 0.95f));
        Art.Text(this, "O", new Vector2(c.X - 34, c.Y + 20), 54, new Color(0.12f, 0.35f, 0.8f));
        Art.Text(this, "2", new Vector2(c.X + 8, c.Y + 32), 28, new Color(0.12f, 0.35f, 0.8f));
        Art.TextCentered(this, "OXYGEN TANK", c.X, c.Y + 44, 12, new Color(0.12f, 0.35f, 0.8f));
        // valve + yellow light
        Art.RoundRect(this, new Rect2(c.X - 18, body.Position.Y - 26, 36, 30), 6, new Color(0.7f, 0.74f, 0.82f));
        DrawCircle(new Vector2(c.X, body.Position.Y - 32), 7f, new Color(1f, 0.8f, 0.2f, 0.7f + 0.3f * Mathf.Sin(Elapsed * 5f)));
        // pressure gauge on a short pipe
        var g = new Vector2(c.X + 52, body.Position.Y - 40);
        DrawLine(new Vector2(c.X + 18, body.Position.Y - 12), g + new Vector2(0, 30), PipeSteel, 8f);
        DrawCircle(g, 34f, PipeDark);
        DrawCircle(g, 31f, new Color(0.7f, 0.74f, 0.82f));
        DrawCircle(g, 26f, Colors.White);
        DrawArc(g, 20f, Mathf.Pi * 0.9f, Mathf.Pi * 1.4f, 10, new Color(1f, 0.8f, 0.2f), 6f);
        DrawArc(g, 20f, Mathf.Pi * 1.4f, Mathf.Pi * 1.85f, 10, new Color(0.3f, 0.8f, 0.35f), 6f);
        DrawArc(g, 20f, Mathf.Pi * 1.85f, Mathf.Pi * 2.1f, 6, Art.Red, 6f);
        float a = Mathf.Pi * 0.9f + Mathf.Pi * 1.2f * 0.82f + 0.05f * Mathf.Sin(Elapsed * 6f);
        DrawLine(g, g + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 20f, PipeDark, 3f);
        DrawCircle(g, 4f, PipeDark);
        Art.TextCentered(this, "O2", g.X, g.Y + 18, 10, new Color(0.12f, 0.35f, 0.8f));
    }

    /// <summary>The OXYGEN LEVEL screen: big % number, a bar, and a status line.</summary>
    private void DrawLevelScreen(Rect2 r, bool solved)
    {
        Art.RoundRect(this, r.Grow(6), 20, new Color(0.45f, 0.5f, 0.64f));
        Art.RoundRect(this, r, 16, new Color(0.1f, 0.15f, 0.3f));
        foreach (var p in new[] { r.Position + new Vector2(12, 12), new Vector2(r.End.X - 12, r.Position.Y + 12), r.End - new Vector2(12, 12), new Vector2(r.Position.X + 12, r.End.Y - 12) })
            DrawCircle(p, 4f, new Color(0.6f, 0.65f, 0.78f));

        float o2 = Gm.Oxygen;
        Art.RoundRect(this, new Rect2(r.Position.X + 14, r.Position.Y + 22, r.Size.X - 28, 34), 10, new Color(0.15f, 0.3f, 0.55f));
        Art.TextCentered(this, "OXYGEN LEVEL", r.GetCenter().X, r.Position.Y + 46, 17, Colors.White, 3);
        Art.TextCentered(this, $"{o2:0}%", r.GetCenter().X, r.Position.Y + 128, 52, Colors.White, 5);
        var bar = new Rect2(r.Position.X + 18, r.Position.Y + 150, r.Size.X - 36, 20);
        Art.RoundRect(this, bar, 10, new Color(0.04f, 0.07f, 0.16f));
        var fillCol = o2 < 30 ? Art.Red : o2 < 60 ? Art.Yellow : new Color(0.55f, 0.95f, 0.2f);
        if (o2 > 1) Art.RoundRect(this, new Rect2(bar.Position, new Vector2(bar.Size.X * o2 / 100f, bar.Size.Y)), 10, fillCol);

        if (solved)
        {
            Art.IconCheck(this, new Vector2(r.Position.X + 34, r.Position.Y + 214), 16, Colors.White);
            Art.Text(this, "Oxygen is", new Vector2(r.Position.X + 58, r.Position.Y + 208), 15, new Color(0.55f, 0.95f, 0.3f));
            Art.Text(this, "flowing!", new Vector2(r.Position.X + 58, r.Position.Y + 228), 15, new Color(0.55f, 0.95f, 0.3f));
        }
        else
        {
            bool blink = Mathf.Sin(Elapsed * 6f) > 0;
            Art.TextCentered(this, "PIPES BROKEN!", r.GetCenter().X, r.Position.Y + 214, 16, blink ? Art.Red : Art.Orange, 3);
            Art.TextCentered(this, "Turn the pipes", r.GetCenter().X, r.Position.Y + 238, 13, Colors.White);
        }
        // little O2 tank icon at the bottom
        Art.RoundRect(this, new Rect2(r.GetCenter().X - 50, r.End.Y - 62, 100, 40), 12, solved ? new Color(0.15f, 0.5f, 1f) : new Color(0.25f, 0.3f, 0.45f));
        Art.TextCentered(this, solved ? "REFILLING" : "WAITING", r.GetCenter().X, r.End.Y - 36, 14, Colors.White);
    }
}

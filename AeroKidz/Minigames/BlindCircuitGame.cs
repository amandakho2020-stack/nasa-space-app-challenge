using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// MISSION 2 - DAY 6: THE DARKOUT (Blind Circuit Puzzle)
/// A fuse box with 3 plugs on the left and 3 on the right. Drag a wire from a LEFT plug to a RIGHT plug.
///   WRONG: "bzzt" + sparks, the wire snaps back.
///   RIGHT: "ding", the wire lights up in its real colour and the LED next to the plug turns GREEN.
/// Blackout (used the HMR heater): every wire is BLACK - you have to guess. 60 seconds.
/// Lights on (saved power): you can see the colours. 45 seconds.
/// WIN: all 3 wires connected. LOSE: time runs out or Oxygen hits 0%.
/// </summary>
public partial class BlindCircuitGame : MinigameBase
{
    // ---- Tuning ----
    private const float DarkTime = 60f, LitTime = 45f;
    private const float PowerPerWireInBlackout = 10f;
    private const float PowerOnFinishInBlackout = 30f;
    private const float PortRadius = 22f;

    private static readonly Color[] WireColors = { Art.Red, Art.Blue, Art.Yellow };
    private static readonly string[] WireNames = { "RED", "BLUE", "YELLOW" };
    private static readonly float[] PortY = { 330f, 450f, 570f };
    private const float LeftX = 450f, RightX = 830f;

    private bool _dark;
    private readonly int[] _match = new int[3];          // left i belongs to right _match[i]
    private readonly bool[] _leftDone = new bool[3];
    private readonly bool[] _rightDone = new bool[3];
    private int _connected;
    private int _wrongTries;

    private int _dragging = -1;
    private Vector2 _mouse;
    private int _snapWire = -1;          // wire snapping back after a wrong try
    private Vector2 _snapFrom;
    private float _snapT;
    private float _finishTimer = -1f;

    private sealed class Spark { public Vector2 Pos, Vel; public float Life; }
    private readonly List<Spark> _sparks = new();

    protected override void Setup()
    {
        _dark = Gm.Blackout;
        TimeLeft = _dark ? DarkTime : LitTime;
        DrainPerSecond = new Vector3(0.5f, 0f, 0f); // "Hurry before the Oxygen runs out!"

        // random matching (a shuffled 0,1,2)
        var order = new List<int> { 0, 1, 2 };
        for (int i = 0; i < 3; i++)
        {
            int k = Rng.RandiRange(0, order.Count - 1);
            _match[i] = order[k];
            order.RemoveAt(k);
        }
    }

    private static Vector2 LeftPort(int i) => new(LeftX, PortY[i]);
    private Vector2 RightPort(int r) => new(RightX, PortY[r]);

    /// <summary>Right plug r shows the colour of the wire that belongs there.</summary>
    private int ColorOfRight(int r)
    {
        for (int i = 0; i < 3; i++)
            if (_match[i] == r) return i;
        return 0;
    }

    protected override void Tick(float dt)
    {
        _mouse = GetLocalMousePosition(); // the wire follows the mouse even without motion events

        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            s.Life -= dt;
            s.Vel.Y += 600f * dt;
            s.Pos += s.Vel * dt;
            if (s.Life <= 0) _sparks.RemoveAt(i);
        }

        if (_snapWire >= 0)
        {
            _snapT += dt * 4f;
            if (_snapT >= 1f) _snapWire = -1;
        }

        if (_finishTimer >= 0f)
        {
            _finishTimer -= dt;
            if (_finishTimer < 0f)
                Win();
        }
    }

    // ---- Input: works with DRAG (press on a left plug, release on a right plug)
    //      AND with CLICK-CLICK (click a left plug, the wire follows the mouse, click a right plug).
    //      Click-click matters on laptop touchpads, where dragging is hard. ----
    private const float HitRadius = PortRadius + 26f;
    private Vector2 _pressPos;

    private int LeftPortAt(Vector2 p)
    {
        for (int i = 0; i < 3; i++)
            if (!_leftDone[i] && p.DistanceTo(LeftPort(i)) < HitRadius) return i;
        return -1;
    }

    private int RightPortAt(Vector2 p)
    {
        for (int r = 0; r < 3; r++)
            if (!_rightDone[r] && p.DistanceTo(RightPort(r)) < HitRadius) return r;
        return -1;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (IsDone || _finishTimer >= 0f)
            return;

        if (e is InputEventMouseMotion mm)
            _mouse = mm.Position;

        if (e is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left)
            return;

        _mouse = mb.Position;
        AcceptEvent();

        if (mb.Pressed)
        {
            if (_dragging >= 0)
            {
                // a wire is already in hand (click-click mode)
                int r = RightPortAt(_mouse);
                if (r >= 0) { TryConnect(_dragging, r); return; }

                int other = LeftPortAt(_mouse);
                if (other >= 0) { _dragging = other; _pressPos = _mouse; Sound(Sfx.Pop, -12f); return; }

                StartSnapBack(_dragging); // clicked empty space: drop the wire
                _dragging = -1;
                return;
            }

            int l = LeftPortAt(_mouse);
            if (l >= 0)
            {
                _dragging = l;
                _pressPos = _mouse;
                _snapWire = -1;
                Sound(Sfx.Pop, -12f);
            }
        }
        else if (_dragging >= 0)
        {
            int r = RightPortAt(_mouse);
            if (r >= 0) { TryConnect(_dragging, r); return; }

            // released close to where we pressed = it was a CLICK: keep holding the wire
            if (_mouse.DistanceTo(_pressPos) < 20f)
                return;

            StartSnapBack(_dragging); // dragged into empty space
            _dragging = -1;
        }
    }

    private void TryConnect(int wire, int r)
    {
        _dragging = -1;
        if (_match[wire] == r) Connect(wire, r);
        else WrongConnection(wire, r);
    }

    private void Connect(int wire, int r)
    {
        _leftDone[wire] = true;
        _rightDone[r] = true;
        _connected++;
        Sound(Sfx.Ding);
        Gm.AddScience(30);
        Pop(RightPort(r) + new Vector2(0, -40), "MATCH!", Art.Green);
        if (_dark)
            Gm.ChangeResources(0f, PowerPerWireInBlackout, 0f);

        if (_connected == 3)
        {
            if (_dark)
            {
                Gm.ChangeResources(0f, PowerOnFinishInBlackout, 0f);
                Say("Lights are back on! Great job!");
            }
            _finishTimer = 1.4f;
        }
    }

    private void WrongConnection(int wire, int r)
    {
        _wrongTries++;
        Sound(Sfx.Zap);
        Pop(RightPort(r) + new Vector2(0, -40), "BZZT!", Art.Orange);
        for (int k = 0; k < 18; k++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            _sparks.Add(new Spark
            {
                Pos = RightPort(r),
                Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rng.RandfRange(120f, 320f),
                Life = Rng.RandfRange(0.25f, 0.6f),
            });
        }
        if (_wrongTries == 2 && _dark)
            Say("Keep trying! Wrong plugs help you learn.");
        StartSnapBack(wire);
    }

    private void StartSnapBack(int wire)
    {
        _snapWire = wire;
        _snapFrom = _mouse;
        _snapT = 0f;
    }

    protected override void OnTimeUp() =>
        Fail(_dark ? "The base stayed dark and the Oxygen ran low." : "The wires weren't fixed in time.");

    // ==================================================================
    public override void _Draw()
    {
        // Room
        DrawRect(new Rect2(Vector2.Zero, Size), _dark ? new Color(0.02f, 0.02f, 0.04f) : new Color(0.12f, 0.15f, 0.22f));
        bool restored = _finishTimer >= 0f;
        if (_dark && !restored)
        {
            // red emergency light that sweeps around
            float a = Elapsed * 2f;
            var c = new Vector2(640, 175);
            DrawColoredPolygon(new[] { c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.4f + 1f) * 900f,
                c + new Vector2(Mathf.Cos(a + 0.4f), Mathf.Sin(a + 0.4f) * 0.4f + 1f) * 900f }, new Color(1f, 0.1f, 0.05f, 0.08f));
            DrawCircle(c, 10f, new Color(1f, 0.2f, 0.1f, 0.6f + 0.4f * Mathf.Sin(Elapsed * 8f)));
        }
        if (restored)
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 0.95f, 0.7f, 0.12f));

        // Fuse box
        var box = new Rect2(340, 230, 600, 420);
        DrawRect(box, _dark && !restored ? new Color(0.1f, 0.1f, 0.12f) : new Color(0.3f, 0.34f, 0.42f));
        DrawRect(box, _dark ? new Color(0.25f, 0.25f, 0.3f) : Art.Steel, false, 4f);
        Art.TextCentered(this, _dark && !restored ? "MAIN POWER - NO LIGHT!" : "MAIN POWER", 640, 262, 20,
            _dark && !restored ? Art.Red : Art.Yellow);

        // Ports + LEDs
        for (int i = 0; i < 3; i++)
        {
            var lp = LeftPort(i);
            var col = _dark && !_leftDone[i] ? new Color(0.13f, 0.13f, 0.15f) : WireColors[i];
            DrawCircle(lp, PortRadius, new Color(0.05f, 0.05f, 0.06f));
            DrawCircle(lp, PortRadius - 6, col);
            DrawLine(lp - new Vector2(80, 0), lp, col, 10f); // wire coming in from the left
            if (!_dark) Art.Text(this, WireNames[i], lp + new Vector2(-150, -18), 13, WireColors[i]);

            var rp = RightPort(i);
            int rc = ColorOfRight(i);
            var rcol = _dark && !_rightDone[i] ? new Color(0.13f, 0.13f, 0.15f) : WireColors[rc];
            DrawCircle(rp, PortRadius, new Color(0.05f, 0.05f, 0.06f));
            DrawCircle(rp, PortRadius - 6, rcol);
            DrawLine(rp, rp + new Vector2(80, 0), rcol, 10f);

            // LED
            var led = rp + new Vector2(50, -34);
            bool on = _rightDone[i];
            DrawCircle(led, 9f, on ? Art.Green : new Color(0.25f, 0.05f, 0.05f));
            if (on) DrawCircle(led, 16f, new Color(Art.Green, 0.25f));
        }

        // Connected wires (glowing in their real colour)
        for (int i = 0; i < 3; i++)
            if (_leftDone[i])
                DrawWire(LeftPort(i), RightPort(_match[i]), WireColors[i], true);

        // The wire being dragged / snapping back
        if (_dragging >= 0)
            DrawWire(LeftPort(_dragging), _mouse, _dark ? new Color(0.16f, 0.16f, 0.18f) : WireColors[_dragging], false);
        else if (_snapWire >= 0)
        {
            var end = _snapFrom.Lerp(LeftPort(_snapWire), Mathf.Clamp(_snapT, 0f, 1f));
            DrawWire(LeftPort(_snapWire), end, _dark ? new Color(0.16f, 0.16f, 0.18f) : WireColors[_snapWire], false);
        }

        // Sparks
        foreach (var s in _sparks)
            DrawLine(s.Pos, s.Pos - s.Vel * 0.03f, new Color(1f, 0.85f, 0.3f, Mathf.Clamp(s.Life * 3f, 0, 1)), 2.5f);

        // Status
        Art.Panel(this, new Rect2(1000, 170, 260, 70), Art.Green);
        Art.Text(this, $"WIRES FIXED: {_connected} / 3", new Vector2(1016, 198), 20, Art.Green);
        Art.Text(this, $"WRONG TRIES: {_wrongTries}", new Vector2(1016, 226), 15, Art.White);

        if (restored)
            Art.TextCentered(this, "POWER IS BACK!", 640, 214, 30, Art.Green, 6);

        // highlight the plugs you can use right now
        int hl = _dragging < 0 ? LeftPortAt(_mouse) : RightPortAt(_mouse);
        if (hl >= 0)
            DrawArc(_dragging < 0 ? LeftPort(hl) : RightPort(hl), PortRadius + 6, 0, Mathf.Tau, 24, Art.White, 3f);
        if (_dragging >= 0)
            Art.TextCentered(this, "NOW CLICK A RIGHT PLUG", 640, 212, 22, Art.Yellow, 5);

        Art.Hint(this, _dark
            ? "CLICK LEFT, THEN RIGHT - GREEN = CORRECT!"
            : "CLICK LEFT, THEN SAME COLOR - GREEN = CORRECT!");
        DrawFloaters();
    }

    private void DrawWire(Vector2 from, Vector2 to, Color color, bool glow)
    {
        var mid = (from + to) / 2f + new Vector2(0, 40);
        var pts = new Vector2[17];
        for (int k = 0; k < pts.Length; k++)
        {
            float t = k / (float)(pts.Length - 1);
            pts[k] = from.Lerp(mid, t).Lerp(mid.Lerp(to, t), t); // quadratic curve (sags a little)
        }
        if (glow) DrawPolyline(pts, new Color(color, 0.3f), 18f, true);
        DrawPolyline(pts, color, 8f, true);
    }
}

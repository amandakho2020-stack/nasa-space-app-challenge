using Godot;

namespace AeroKidz;

/// <summary>
/// MISSION 3 - DAY 7: THE SUN WATCHER (telescope calibration)
/// The Sun drifts around the sky. The telescope crosshair follows the mouse, but it is heavy, so it lags.
/// CLICK (or press Space) when the Sun is inside the crosshair to catch its light.
/// WIN: catch the sunlight 5 times. LOSE: time runs out, or Power hits 0% (the telescope uses a lot).
/// </summary>
public partial class SunWatcherGame : MinigameBase
{
    // ---- Tuning ----
    private const int Goal = 5;
    private const float CrosshairFollow = 4.5f;   // higher = lighter telescope
    private const float CatchRadius = 46f;
    private const float SunRadius = 34f;
    private const float StartSpeed = 150f, SpeedPerCatch = 40f;
    private static readonly Rect2 Sky = new(80, 190, 1120, 330);

    private Vector2 _sun = new(400, 300);
    private Vector2 _sunVel;
    private Vector2 _crosshair = new(640, 400);
    private Vector2 _mouse = new(640, 400);
    private float _turnTimer;
    private int _caught;
    private float _flash;
    private bool _spaceWasDown;
    private Vector2 _beamFrom;   // where the Sun was when its light was caught
    private float _beamT;        // 1 -> 0: the light beam fades

    protected override void Setup()
    {
        TimeLeft = 60f;
        DrainPerSecond = new Vector3(0.25f, 0.6f, 0f); // telescope = lots of power
        _sunVel = RandomDir() * StartSpeed;
    }

    private Vector2 RandomDir()
    {
        float a = Rng.RandfRange(0, Mathf.Tau);
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    protected override void Tick(float dt)
    {
        // Sun drifts and changes direction now and then
        _turnTimer -= dt;
        if (_turnTimer <= 0f)
        {
            _turnTimer = Rng.RandfRange(1.0f, 2.2f);
            _sunVel = RandomDir() * (StartSpeed + SpeedPerCatch * _caught);
        }
        _sun += _sunVel * dt;
        if (_sun.X < Sky.Position.X || _sun.X > Sky.End.X) _sunVel.X = -_sunVel.X;
        if (_sun.Y < Sky.Position.Y || _sun.Y > Sky.End.Y) _sunVel.Y = -_sunVel.Y;
        _sun = _sun.Clamp(Sky.Position, Sky.End);

        // Heavy telescope: crosshair eases toward the mouse
        _crosshair = _crosshair.Lerp(_mouse, 1f - Mathf.Exp(-CrosshairFollow * dt));
        _flash = Mathf.Max(0f, _flash - dt * 2f);
        _beamT = Mathf.Max(0f, _beamT - dt * 1.5f);

        bool space = Input.IsKeyPressed(Key.Space);
        if (space && !_spaceWasDown) TryCatch();
        _spaceWasDown = space;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm) _mouse = mm.Position;
        if (IsLeftClick(e, out var p))
        {
            _mouse = p;
            TryCatch();
        }
    }

    private void TryCatch()
    {
        if (IsDone) return;
        if (_crosshair.DistanceTo(_sun) <= CatchRadius)
        {
            _caught++;
            _flash = 1f;
            _beamFrom = _sun;
            _beamT = 1f;
            Gm.AddScience(40);
            Sound(Sfx.Ding);
            Pop(_sun + new Vector2(0, -50), "SUNLIGHT CAUGHT!", Art.Yellow);
            _sun = new Vector2(Rng.RandfRange(Sky.Position.X, Sky.End.X), Rng.RandfRange(Sky.Position.Y, Sky.End.Y));
            _sunVel = RandomDir() * (StartSpeed + SpeedPerCatch * _caught);
            if (_caught == 3) Say("Great! The Sun looks very busy today...");
            if (_caught >= Goal) Win();
        }
        else
        {
            Sound(Sfx.Bad, -14f);
            Pop(_crosshair + new Vector2(0, -50), "MISSED!", Art.Red);
        }
    }

    protected override void OnTimeUp() => Fail("The Sun set behind the horizon before the telescope got enough data.");

    // ==================================================================
    // DRAWING - space sky, glowing Sun, sci-fi reticle, observatory with a steel telescope
    // ==================================================================
    private static readonly Vector2 Lens = new(540, 560);   // telescope pivot (top of the dome)

    public override void _Draw()
    {
        // night sky gradient + nebula clouds + stars
        for (int i = 0; i < 12; i++)
            DrawRect(new Rect2(0, i * 60f, Size.X, 61), new Color(0.03f, 0.04f, 0.14f).Lerp(new Color(0.12f, 0.07f, 0.25f), i / 11f));
        var rng = new RandomNumberGenerator { Seed = 5 };
        for (int i = 0; i < 9; i++)
        {
            var p = new Vector2(rng.RandfRange(0, 1280), rng.RandfRange(180, 520));
            var col = i % 2 == 0 ? new Color(0.55f, 0.3f, 0.95f, 0.025f) : new Color(0.25f, 0.5f, 1f, 0.022f);
            float rad = rng.RandfRange(90, 150);
            for (int j = 0; j < 8; j++) DrawCircle(p, rad * (1f - j / 8f), col); // soft: brighter towards the middle
        }
        Art.Stars(this, new Rect2(0, 0, Size.X, Size.Y), 160, Elapsed);

        DrawSun();

        // light beam into the telescope after a catch
        if (_beamT > 0f)
        {
            var lensTip = Lens + (_crosshair - Lens).Normalized() * 130f;
            DrawLine(_beamFrom, lensTip, new Color(1f, 0.9f, 0.4f, 0.5f * _beamT), 26f * _beamT);
            DrawLine(_beamFrom, lensTip, new Color(1f, 1f, 0.85f, 0.9f * _beamT), 7f);
        }

        DrawObservatory();
        DrawReticle();

        if (_flash > 0f)
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 0.95f, 0.6f, 0.3f * _flash));

        // SUNLIGHT DATA panel: 5 sun icons that light up
        Art.Panel(this, new Rect2(980, 172, 280, 76), Art.Yellow);
        Art.Text(this, "SUNLIGHT DATA", new Vector2(996, 196), 16, Art.Yellow, outline: 3);
        for (int i = 0; i < Goal; i++)
        {
            var p = new Vector2(1012 + i * 50, 226);
            bool got = i < _caught;
            if (got) DrawCircle(p, 17f, new Color(1f, 0.75f, 0.2f, 0.3f));
            DrawCircle(p, 12f, got ? new Color(1f, 0.82f, 0.2f) : new Color(0.25f, 0.27f, 0.38f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.Tau / 8f;
                DrawLine(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 14f, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 18f,
                    got ? new Color(1f, 0.82f, 0.2f) : new Color(0.25f, 0.27f, 0.38f), 2f);
            }
        }

        Art.Hint(this, "AIM WITH THE MOUSE - CLICK WHEN THE SUN IS IN THE CIRCLE!");
        DrawFloaters();
    }

    private void DrawSun()
    {
        var c = _sun;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Elapsed * 3f);
        DrawCircle(c, SunRadius * 2.6f, new Color(1f, 0.55f, 0.1f, 0.07f));
        DrawCircle(c, SunRadius * 1.9f, new Color(1f, 0.6f, 0.15f, 0.14f + 0.04f * pulse));
        // rotating corona rays
        for (int k = 0; k < 12; k++)
        {
            float a = Elapsed * 0.4f + k * Mathf.Tau / 12f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var side = new Vector2(-d.Y, d.X);
            float len = SunRadius * (k % 2 == 0 ? 1.75f : 1.45f) + 4f * pulse;
            DrawColoredPolygon(new[] { c + d * SunRadius * 0.9f + side * 7f, c + d * len, c + d * SunRadius * 0.9f - side * 7f },
                new Color(1f, 0.7f, 0.15f, 0.55f));
        }
        DrawCircle(c, SunRadius + 3f, new Color(1f, 0.45f, 0.1f));
        DrawCircle(c, SunRadius, new Color(1f, 0.68f, 0.15f));
        DrawCircle(c, SunRadius * 0.72f, new Color(1f, 0.85f, 0.3f));
        DrawCircle(c, SunRadius * 0.4f, new Color(1f, 0.97f, 0.75f));
        DrawCircle(c + new Vector2(-12, 8), 4f, new Color(0.85f, 0.4f, 0.1f, 0.7f));   // sunspots
        DrawCircle(c + new Vector2(10, -12), 3f, new Color(0.85f, 0.4f, 0.1f, 0.7f));
        float fa = Elapsed * 0.7f;
        DrawArc(c + new Vector2(Mathf.Cos(fa), Mathf.Sin(fa)) * SunRadius, 11f + 4f * pulse, fa - 1.3f, fa + 1.3f, 12,
            new Color(1f, 0.5f, 0.15f), 3f);                                                  // a little solar flare loop
    }

    private void DrawObservatory()
    {
        // moon ground
        var ground = new System.Collections.Generic.List<Vector2>();
        for (float x = 0; x <= 1280; x += 40) ground.Add(new Vector2(x, 600 - 18f * Mathf.Sin(x * 0.004f + 0.6f)));
        ground.Add(new Vector2(1280, 720));
        ground.Add(new Vector2(0, 720));
        DrawColoredPolygon(ground.ToArray(), new Color(0.42f, 0.42f, 0.52f));
        Art.Ellipse(this, new Vector2(220, 650), new Vector2(80, 14), new Color(0.34f, 0.34f, 0.44f));
        Art.Ellipse(this, new Vector2(1050, 668), new Vector2(110, 16), new Color(0.34f, 0.34f, 0.44f));

        // telescope tube (behind the dome), pointing at the reticle
        var dir = (_crosshair - Lens).Normalized();
        if (dir.Y > -0.15f) dir = new Vector2(dir.X, -0.15f).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        var tip = Lens + dir * 130f;
        DrawColoredPolygon(new[] { Lens + side * 22, tip + side * 26, tip - side * 26, Lens - side * 22 }, new Color(0.16f, 0.18f, 0.28f));
        DrawColoredPolygon(new[] { Lens + side * 18, tip + side * 22, tip - side * 22, Lens - side * 18 }, new Color(0.7f, 0.75f, 0.86f));
        DrawLine(Lens + side * 10, tip + side * 12, new Color(1, 1, 1, 0.6f), 4f);                       // shine
        DrawLine(Lens + dir * 70 + side * 22, Lens + dir * 70 - side * 22, new Color(0.3f, 0.6f, 1f), 7f); // blue band
        DrawLine(tip + side * 28, tip - side * 28, new Color(1f, 0.78f, 0.3f), 10f);                      // gold rim
        DrawCircle(tip, 14f, new Color(0.4f, 0.8f, 1f, 0.6f + 0.3f * Mathf.Sin(Elapsed * 4f)));           // lens glow

        // white observatory dome with a slit, on a base
        Art.RoundRect(this, new Rect2(Lens.X - 120, Lens.Y + 30, 240, 80), 10, new Color(0.62f, 0.66f, 0.78f));
        for (int k = 0; k < 3; k++)
            Art.RoundRect(this, new Rect2(Lens.X - 90 + k * 70, Lens.Y + 60, 40, 10), 5, new Color(0.35f, 0.8f, 1f, 0.7f));
        Art.HalfDisc(this, Lens + new Vector2(0, 34), 104f, new Color(0.18f, 0.2f, 0.3f));
        Art.HalfDisc(this, Lens + new Vector2(0, 34), 100f, new Color(0.9f, 0.92f, 0.98f));
        DrawArc(Lens + new Vector2(0, 34), 70f, Mathf.Pi, Mathf.Tau, 24, new Color(0.75f, 0.8f, 0.9f), 3f);
        DrawCircle(Lens, 24f, new Color(0.18f, 0.2f, 0.3f));
        DrawCircle(Lens, 18f, new Color(0.55f, 0.6f, 0.72f));
        Art.TextCentered(this, "SOLAR TELESCOPE", Lens.X, Lens.Y + 100, 14, Art.Cyan, 3);
    }

    private void DrawReticle()
    {
        var c = _crosshair;
        bool onSun = c.DistanceTo(_sun) <= CatchRadius;
        var col = onSun ? new Color(0.35f, 1f, 0.45f) : new Color(0.35f, 0.85f, 1f);
        float spin = Elapsed * (onSun ? 3f : 1f);

        DrawCircle(c, CatchRadius, new Color(col, 0.08f));
        for (int k = 0; k < 4; k++)                       // outer segmented ring that spins
        {
            float a = spin + k * Mathf.Pi / 2f;
            DrawArc(c, CatchRadius + 10, a, a + 1.0f, 12, col, 4f);
        }
        DrawArc(c, CatchRadius, 0, Mathf.Tau, 40, new Color(col, 0.7f), 2f);
        for (int k = 0; k < 4; k++)                       // ticks
        {
            var d = new Vector2(Mathf.Cos(k * Mathf.Pi / 2f), Mathf.Sin(k * Mathf.Pi / 2f));
            DrawLine(c + d * (CatchRadius - 12), c + d * (CatchRadius + 20), col, 3f);
        }
        float b = CatchRadius + 26;                       // corner brackets
        foreach (var s in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var corner = c + s * b * 0.72f;
            DrawLine(corner, corner - new Vector2(s.X * 14, 0), col, 3f);
            DrawLine(corner, corner - new Vector2(0, s.Y * 14), col, 3f);
        }
        DrawCircle(c, 3f, col);
        Art.TextCentered(this, onSun ? "LOCKED! CLICK!" : "AIMING...", c.X, c.Y + CatchRadius + 44, onSun ? 22 : 15, col, 4);
    }
}

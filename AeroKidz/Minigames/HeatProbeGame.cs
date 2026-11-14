using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// MISSION 2: CLEAN THE WATER (HMR heat probe)
/// The melted lunar ice is in a water tank, and Earth germs are floating in it.
/// Move the HMR HEAT PROBE with the mouse (or WASD). Germs close to the hot tip heat up and pop.
/// Clean ALL the germs before the timer runs out! The heater uses POWER while it moves.
///
/// Used the big HMR heater (Option A): most germs were already killed -> fewer germs, but power is only 10%.
/// Saved power (Option B): lots of germs left -> more to clean, but plenty of power.
///
/// SCIENCE: HMR = Heat Microbial Reduction. NASA heats spacecraft parts to kill Earth germs (BIOBURDEN).
/// This is PLANETARY PROTECTION: keeping Earth germs off other worlds.
/// </summary>
public partial class HeatProbeGame : MinigameBase
{
    // ---- Tuning ----
    private const int GermsAfterHmr = 6, GermsNoHmr = 14;
    private const float TimeLimit = 40f;
    private const float ProbeSpeed = 420f;
    private const float HeatRadius = 50f;
    private const float HeatToPop = 0.45f;          // seconds a germ must stay in the heat
    private const float PowerPerSecondMoving = 0.5f;
    private static readonly Rect2 Tank = new(190, 200, 940, 450);
    private const string HeaterPic = "res://AeroKidz/Assets/energy/energy_18.png";

    private sealed class Germ { public Vector2 Pos, Vel; public float Wobble; public float Heat; }
    private readonly List<Germ> _germs = new();

    private sealed class Puff { public Vector2 Pos; public float Life; }
    private readonly List<Puff> _puffs = new();

    private Vector2 _probe = new(640, 430);
    private bool _moving;
    private int _total;
    private int _cleaned;
    private float _winDelay = -1f;
    private bool _hmrUsed;

    protected override void Setup()
    {
        TimeLeft = TimeLimit;
        TimerIsGoal = true;           // this game HAS a timer (it keeps running even when other timers are off)
        DrainPerSecond = new Vector3(0.2f, 0f, 0f);
        _hmrUsed = Gm.Blackout;       // Option A used the big HMR heater
        _total = _hmrUsed ? GermsAfterHmr : GermsNoHmr;
        for (int i = 0; i < _total; i++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            _germs.Add(new Germ
            {
                Pos = new Vector2(Rng.RandfRange(Tank.Position.X + 40, Tank.End.X - 40), Rng.RandfRange(Tank.Position.Y + 40, Tank.End.Y - 40)),
                Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rng.RandfRange(30f, 70f),
                Wobble = Rng.RandfRange(0, Mathf.Tau),
            });
        }
    }

    protected override void Tick(float dt)
    {
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            _puffs[i].Life -= dt;
            _puffs[i].Pos += new Vector2(0, -40f * dt);
            if (_puffs[i].Life <= 0) _puffs.RemoveAt(i);
        }

        if (_winDelay >= 0f)
        {
            _winDelay -= dt;
            if (_winDelay < 0f) Win();
            return;
        }

        // ---- Move the heat probe (follows the mouse, or WASD) ----
        var dir = WalkDirection(_probe, false, followMouse: true);
        _moving = dir != Vector2.Zero;
        if (_moving)
        {
            _probe += dir * ProbeSpeed * dt;
            _probe = _probe.Clamp(Tank.Position + new Vector2(20, 20), Tank.End - new Vector2(20, 20));
        }
        DrainPerSecond = new Vector3(0.2f, _moving ? PowerPerSecondMoving : 0f, 0f); // the heater uses power

        // ---- Germs swim around; the ones near the hot tip heat up and pop ----
        for (int i = _germs.Count - 1; i >= 0; i--)
        {
            var g = _germs[i];
            g.Wobble += dt;
            bool inHeat = g.Pos.DistanceTo(_probe) < HeatRadius;
            if (inHeat)
            {
                g.Heat += dt;
                g.Pos += (_probe - g.Pos) * 2f * dt; // the heat pulls them in a little
            }
            else
            {
                g.Heat = Mathf.Max(0f, g.Heat - dt * 0.5f);
                g.Pos += g.Vel * dt;
                var away = g.Pos - _probe;
                if (away.Length() < 150f) g.Pos += away.Normalized() * 25f * dt; // germs try to swim away
            }
            if (g.Pos.X < Tank.Position.X + 20 || g.Pos.X > Tank.End.X - 20) g.Vel.X = -g.Vel.X;
            if (g.Pos.Y < Tank.Position.Y + 20 || g.Pos.Y > Tank.End.Y - 20) g.Vel.Y = -g.Vel.Y;
            g.Pos = g.Pos.Clamp(Tank.Position + new Vector2(15, 15), Tank.End - new Vector2(15, 15));

            if (g.Heat >= HeatToPop)
            {
                _germs.RemoveAt(i);
                _cleaned++;
                Gm.AddScience(15);
                Sound(Sfx.Pop);
                Pop(g.Pos + new Vector2(0, -40), "+1 CLEAN", Art.Cyan);
                for (int k = 0; k < 3; k++)
                    _puffs.Add(new Puff { Pos = g.Pos + new Vector2(Rng.RandfRange(-12, 12), Rng.RandfRange(-8, 8)), Life = 0.8f });
            }
        }

        if (_cleaned >= _total)
        {
            Sound(Sfx.Ding);
            Pop(new Vector2(640, 420), "WATER IS CLEAN!", Art.Green);
            _winDelay = 1.2f;
        }
    }

    protected override void OnTimeUp() => Fail("Time ran out - germs are still in the water!");

    // ==================================================================
    public override void _Draw()
    {
        // Room + water tank (seen from above)
        Art.TechBackground(this, Size, new Color(0.15f, 0.2f, 0.3f));
        Art.RoundRect(this, Tank.Grow(14), 26, Art.Steel);
        Art.RoundRect(this, Tank, 20, new Color(0.15f, 0.45f, 0.75f));
        for (int k = 0; k < 7; k++)
        {
            float y = Tank.Position.Y + 40 + k * 60 + Mathf.Sin(Elapsed * 1.5f + k) * 6f;
            DrawLine(new Vector2(Tank.Position.X + 30, y), new Vector2(Tank.End.X - 30, y + 8), new Color(1, 1, 1, 0.1f), 3f);
        }

        // Germs (turn red as they heat up)
        foreach (var g in _germs)
        {
            float h = Mathf.Clamp(g.Heat / HeatToPop, 0f, 1f);
            float s = 1f + 0.25f * h * Mathf.Sin(Elapsed * 40f);
            var body = new Color(0.35f, 0.85f, 0.25f).Lerp(new Color(1f, 0.35f, 0.15f), h);
            var pts = new Vector2[10];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = Mathf.Tau * i / pts.Length + g.Wobble;
                pts[i] = g.Pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? 20f : 13f) * s;
            }
            DrawColoredPolygon(pts, body);
            DrawCircle(g.Pos + new Vector2(-5, -3), 4f, Colors.White);
            DrawCircle(g.Pos + new Vector2(5, -3), 4f, Colors.White);
            DrawCircle(g.Pos + new Vector2(-5, -3), 2f, Colors.Black);
            DrawCircle(g.Pos + new Vector2(5, -3), 2f, Colors.Black);
            if (h > 0.2f) DrawCircle(g.Pos + new Vector2(0, 7), 3f, new Color(0.2f, 0.05f, 0.05f)); // "oh!" mouth
            else DrawArc(g.Pos + new Vector2(0, 6), 5f, Mathf.Pi + 0.4f, Mathf.Tau - 0.4f, 8, new Color(0.1f, 0.3f, 0.1f), 2f);
        }
        foreach (var p in _puffs)
            DrawCircle(p.Pos, 10f + (0.8f - p.Life) * 16f, new Color(0.9f, 0.9f, 0.95f, p.Life * 0.6f));

        // The HMR heater on the left, a cable, and the hot probe at the mouse
        var heater = new Vector2(Tank.Position.X - 95, Tank.Position.Y + 110);
        DrawPolyline(new[] { heater + new Vector2(30, 20), heater.Lerp(_probe, 0.5f) + new Vector2(0, 40), _probe }, new Color(0.25f, 0.28f, 0.35f), 10f, true);
        Art.Picture(this, HeaterPic, heater, 150f);
        Art.TextCentered(this, "HMR HEATER", heater.X, heater.Y + 90, 15, Art.Orange, 4);

        float glow = 0.6f + 0.4f * Mathf.Sin(Elapsed * 10f);
        DrawCircle(_probe, HeatRadius + 6, new Color(1f, 0.45f, 0.1f, 0.12f + 0.08f * glow));
        DrawArc(_probe, HeatRadius, 0, Mathf.Tau, 36, new Color(1f, 0.6f, 0.2f, 0.7f), 3f);
        DrawCircle(_probe, 22f, new Color(0.35f, 0.36f, 0.42f));
        DrawCircle(_probe, 14f, new Color(1f, 0.4f + 0.3f * glow, 0.1f));
        if (_moving)
            for (int k = 0; k < 5; k++)
            {
                float a = Elapsed * 3f + k * Mathf.Tau / 5f;
                var w = _probe + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (HeatRadius - 8);
                DrawLine(w, w + new Vector2(0, -10 - 4 * Mathf.Sin(Elapsed * 12f + k)), new Color(1f, 0.7f, 0.3f, 0.6f), 2f); // heat shimmer
            }

        // Status (top corners, away from Nova and the hint)
        Art.Panel(this, new Rect2(980, 170, 280, 66), Art.Green);
        Art.Text(this, $"GERMS LEFT: {_total - _cleaned}", new Vector2(996, 198), 20, Art.Green);
        Art.Text(this, $"TIME: {Mathf.CeilToInt(TimeLeft)} s", new Vector2(996, 224), 17, TimeLeft < 10 ? Art.Red : Art.White);
        if (_hmrUsed)
        {
            Art.Panel(this, new Rect2(20, 170, 330, 34), Art.Orange);
            Art.Text(this, "LOW POWER - MOVE CAREFULLY!", new Vector2(34, 193), 15, Art.Orange);
        }

        Art.Hint(this, "MOVE THE HMR HEAT PROBE - HEAT UP ALL THE GERMS!");
        DrawFloaters();
    }
}

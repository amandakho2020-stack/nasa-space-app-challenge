using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// MISSION 2: DRILL FOR ICE (temperature dial) - about 15-20 seconds.
/// HOLD the mouse (or Space) to drill. Drilling heats the drill; letting go cools it.
///   BLUE  "SAFE"    : normal speed
///   GREEN "BEST"    : 50% faster - the sweet spot!
///   RED   "TOO HOT" : at the top the drill JAMS for 2 seconds and costs Power
/// The hard rock in the middle heats up faster. Reach the ice at 60 m to win.
///
/// SCIENCE: real drills heat up from friction. Engineers drill in bursts so the bit doesn't overheat.
/// Moon soil is called REGOLITH.
/// </summary>
public partial class DrillGame : MinigameBase
{
    // ---- Tuning ----
    private const float GoalDepth = 60f;
    private const float SafeMax = 45f, BestMax = 75f;   // dial zones (0-100)
    private const float CoolPerSec = 45f;
    private const float JamSeconds = 2f;
    private const float JamPowerCost = 6f;
    private const float DrillPowerPerSec = 0.6f;
    private const float BestZoneBonus = 1.5f;

    // Layers: name, bottom depth (m), speed (m/s), heat (per s), colour
    private static readonly (string Name, float Bottom, float Speed, float Heat, Color Color)[] Layers =
    {
        ("REGOLITH (MOON DUST)", 20f, 4.5f, 22f, new Color(0.88f, 0.62f, 0.32f)),
        ("HARD ROCK (BASALT)", 40f, 3.0f, 38f, new Color(0.3f, 0.29f, 0.38f)),
        ("ICE LAYER", 60f, 4.0f, 26f, new Color(0.55f, 0.82f, 1f)),
    };

    private const float SurfaceY = 262f, BottomY = 610f;
    private const float DrillX = 330f;
    private static readonly Vector2 DialCenter = new(930, 430);
    private const float DialRadius = 175f;

    private float _depth;
    private float _temp;
    private float _jam;
    private bool _drilling;
    private float _winDelay = -1f;
    private int _lastLayer;
    private bool _warnedHot;
    private float _needle; // smoothed dial angle 0..100

    private sealed class Chip { public Vector2 Pos, Vel; public float Life; public Color Color; }
    private readonly List<Chip> _chips = new();

    protected override void Setup()
    {
        TimeLeft = 90f;
        DrainPerSecond = new Vector3(0.3f, 0f, 0f);
    }

    private int LayerIndex => _depth < Layers[0].Bottom ? 0 : _depth < Layers[1].Bottom ? 1 : 2;
    private float DepthToY(float d) => SurfaceY + (BottomY - SurfaceY) * d / GoalDepth;

    protected override void Tick(float dt)
    {
        UpdateChips(dt);
        _needle = Mathf.Lerp(_needle, _temp, 1f - Mathf.Exp(-12f * dt));

        if (_winDelay >= 0f)
        {
            _winDelay -= dt;
            if (_winDelay < 0f) Win();
            return;
        }

        bool hold = Input.IsKeyPressed(Key.Space) || MouseHeld; // MouseHeld also catches quick touchpad taps

        if (_jam > 0f)
        {
            _jam -= dt;
            _drilling = false;
            _temp = Mathf.Max(40f, _temp - CoolPerSec * dt);
            DrainPerSecond = new Vector3(0.3f, 0f, 0f);
            return;
        }

        var layer = Layers[LayerIndex];
        _drilling = hold;
        if (hold)
        {
            float speed = layer.Speed * (_temp >= SafeMax ? BestZoneBonus : 1f);
            _depth = Mathf.Min(GoalDepth, _depth + speed * dt);
            _temp += layer.Heat * dt;
            DrainPerSecond = new Vector3(0.3f, DrillPowerPerSec, 0f);
            if (Rng.Randf() < 0.7f) SpawnChip(layer.Color);
        }
        else
        {
            _temp = Mathf.Max(0f, _temp - CoolPerSec * dt);
            DrainPerSecond = new Vector3(0.3f, 0f, 0f);
        }

        if (LayerIndex != _lastLayer)
        {
            _lastLayer = LayerIndex;
            Say(LayerIndex == 1 ? "Hard rock! It gets hot fast!" : "I see ice! Almost there!");
        }

        if (!_warnedHot && _temp >= BestMax)
        {
            _warnedHot = true;
            Say("Too hot! Let go!");
        }

        if (_temp >= 100f)
        {
            _temp = 100f;
            _jam = JamSeconds;
            Gm.ChangeResources(0f, -JamPowerCost, 0f);
            Sound(Sfx.Bad);
            Pop(new Vector2(DrillX, DepthToY(_depth) - 30), "JAMMED!", Art.Red);
        }

        if (_depth >= GoalDepth)
        {
            Gm.AddScience(100);
            Sound(Sfx.Ding);
            Pop(new Vector2(DrillX, BottomY - 40), "ICE! +100", Art.Cyan);
            _winDelay = 1.2f;
        }
    }

    private void SpawnChip(Color c)
    {
        _chips.Add(new Chip
        {
            Pos = new Vector2(DrillX, SurfaceY),
            Vel = new Vector2(Rng.RandfRange(-130, 130), Rng.RandfRange(-240, -100)),
            Life = 0.8f,
            Color = c,
        });
    }

    private void UpdateChips(float dt)
    {
        for (int i = _chips.Count - 1; i >= 0; i--)
        {
            var c = _chips[i];
            c.Life -= dt;
            c.Vel.Y += 420f * dt;
            c.Pos += c.Vel * dt;
            if (c.Life <= 0) _chips.RemoveAt(i);
        }
    }

    protected override void OnTimeUp() => Fail("The drill didn't reach the ice in time.");

    // ==================================================================
    // DRAWING (style follows the team's reference picture)
    // ==================================================================
    private static readonly Color[] DialStops =
    {
        new(0.15f, 0.4f, 1f), new(0.2f, 0.8f, 1f), new(0.3f, 0.9f, 0.35f), new(1f, 0.9f, 0.2f), new(1f, 0.5f, 0.1f), new(0.95f, 0.15f, 0.1f),
    };
    private static readonly float[] DialStopAt = { 0f, 28f, 55f, 70f, 85f, 100f };

    private static Color DialColor(float v)
    {
        for (int i = 1; i < DialStopAt.Length; i++)
            if (v <= DialStopAt[i])
                return DialStops[i - 1].Lerp(DialStops[i], (v - DialStopAt[i - 1]) / (DialStopAt[i] - DialStopAt[i - 1]));
        return DialStops[^1];
    }

    public override void _Draw()
    {
        DrawSpace();
        DrawGround();
        DrawDrill();

        // Depth panel with an arrow icon
        Art.Panel(this, new Rect2(20, 172, 240, 64), Art.Cyan);
        Art.Text(this, $"DEPTH   {_depth:0} / {GoalDepth:0} m", new Vector2(36, 198), 18, Art.White, outline: 3);
        DrawColoredPolygon(new[] { new Vector2(36, 210), new Vector2(52, 210), new Vector2(44, 224) }, Art.Cyan);
        Art.RoundRect(this, new Rect2(60, 210, 184, 14), 7, new Color(0.05f, 0.08f, 0.18f));
        if (_depth > 0.5f) Art.RoundRect(this, new Rect2(60, 210, 184 * _depth / GoalDepth, 14), 7, Art.Cyan);

        DrawDial();

        if (_jam > 0f)
            Art.TextCentered(this, "JAMMED! COOLING DOWN...", DialCenter.X, DialCenter.Y - 70, 26, Mathf.Sin(Elapsed * 10f) > 0 ? Art.Red : Art.White, 5);

        Art.Hint(this, "HOLD TO DRILL - LET GO TO COOL - STAY IN THE GREEN!");
        DrawFloaters();
    }

    private void DrawSpace()
    {
        // night-blue gradient sky with stars and two planets
        for (int i = 0; i < 12; i++)
        {
            float y = i * 60f;
            DrawRect(new Rect2(0, y, Size.X, 61), new Color(0.06f, 0.08f, 0.24f).Lerp(new Color(0.16f, 0.12f, 0.36f), i / 11f));
        }
        Art.Stars(this, new Rect2(0, 0, Size.X, Size.Y), 120, Elapsed);
        DrawCircle(new Vector2(1250, 215), 78, new Color(0.55f, 0.6f, 0.85f));
        DrawCircle(new Vector2(1235, 200), 64, new Color(0.65f, 0.7f, 0.95f));
        DrawCircle(new Vector2(1215, 230), 12, new Color(0.5f, 0.55f, 0.8f));
        DrawCircle(new Vector2(1262, 190), 8, new Color(0.5f, 0.55f, 0.8f));
        DrawCircle(new Vector2(18, 210), 52, new Color(0.4f, 0.25f, 0.6f));

        // rocks along the bottom + a sci-fi platform on the right
        var rng = new RandomNumberGenerator { Seed = 11 };
        for (int i = 0; i < 16; i++)
        {
            float x = rng.RandfRange(0, 1280), r = rng.RandfRange(28, 60);
            var p = new Vector2(x, 700 + rng.RandfRange(-10, 20));
            DrawColoredPolygon(new[] { p + new Vector2(-r, r * 0.5f), p + new Vector2(-r * 0.7f, -r * 0.4f), p + new Vector2(0, -r * 0.7f),
                p + new Vector2(r * 0.8f, -r * 0.3f), p + new Vector2(r, r * 0.5f) }, new Color(0.3f, 0.3f, 0.48f).Lerp(new Color(0.42f, 0.4f, 0.6f), rng.Randf()));
        }
        Art.RoundRect(this, new Rect2(700, 640, 600, 90), 14, new Color(0.18f, 0.2f, 0.32f));
        for (int k = 0; k < 3; k++)
        {
            float glow = 0.6f + 0.4f * Mathf.Sin(Elapsed * 3f + k);
            Art.RoundRect(this, new Rect2(780 + k * 170, 668, 120, 12), 6, new Color(0.3f, 0.75f, 1f, glow));
        }
    }

    private float Wave(float x, float seed) => 6f * Mathf.Sin(x * 0.03f + seed) + 3f * Mathf.Sin(x * 0.07f + seed * 2f);

    private void DrawGround()
    {
        const float left = 12f, right = 660f;
        var rng = new RandomNumberGenerator { Seed = 7 };
        float top = SurfaceY;
        for (int li = 0; li < Layers.Length; li++)
        {
            var l = Layers[li];
            float bottom = DepthToY(l.Bottom);
            // layer with wavy bottom edge
            var pts = new System.Collections.Generic.List<Vector2> { new(left, top), new(right, top) };
            for (float x = right; x >= left; x -= 20) pts.Add(new Vector2(x, bottom + (li < 2 ? Wave(x, li * 3f) : 0)));
            DrawColoredPolygon(pts.ToArray(), l.Color);
            // texture: pebbles / rocks / crystals
            for (int k = 0; k < 14; k++)
            {
                var p = new Vector2(rng.RandfRange(left + 15, right - 15), rng.RandfRange(top + 14, bottom - 10));
                float r = rng.RandfRange(6, 16);
                if (li == 2)
                    Art.IconIce(this, p, r);
                else
                    Art.IconRock(this, p, r, l.Color.Darkened(0.25f).Lerp(l.Color.Lightened(0.15f), rng.Randf()));
            }
            top = bottom;
        }
        // deep ice below
        DrawRect(new Rect2(left, BottomY, right - left, Size.Y - BottomY), new Color(0.75f, 0.9f, 1f));

        // snowy top crust
        var snow = new System.Collections.Generic.List<Vector2>();
        for (float x = left; x <= right; x += 16) snow.Add(new Vector2(x, SurfaceY - 8 + 3 * Mathf.Sin(x * 0.11f)));
        for (float x = right; x >= left; x -= 16) snow.Add(new Vector2(x, SurfaceY + 10 + 5 * Mathf.Sin(x * 0.07f + 1f)));
        DrawColoredPolygon(snow.ToArray(), new Color(0.95f, 0.97f, 1f));

        // label pills with icons
        float y0 = SurfaceY;
        for (int li = 0; li < Layers.Length; li++)
        {
            float y1 = DepthToY(Layers[li].Bottom);
            int idx = li;
            Art.IconPill(this, new Vector2(400, (y0 + y1) / 2f + 4), Layers[li].Name, p =>
            {
                if (idx == 2) Art.IconIce(this, p, 12);
                else Art.IconRock(this, p, 13, idx == 0 ? new Color(0.75f, 0.55f, 0.35f) : new Color(0.45f, 0.43f, 0.55f));
            }, 15);
            y0 = y1;
        }
    }

    private void DrawDrill()
    {
        var shake = _drilling ? new Vector2(Rng.RandfRange(-2, 2), 0) : Vector2.Zero;
        float tipY = DepthToY(_depth) + 6f;
        var top = new Vector2(DrillX, SurfaceY - 95) + shake;

        // orange tripod legs with joints, on two feet
        foreach (var s in new[] { -1f, 1f })
        {
            var hip = top + new Vector2(s * 26, 30);
            var knee = top + new Vector2(s * 62, 52);
            var foot = new Vector2(DrillX + s * 92, SurfaceY - 14);
            DrawLine(hip, knee, Art.Orange, 11f);
            DrawLine(knee, foot, Art.Orange, 11f);
            DrawCircle(knee, 9, new Color(0.95f, 0.75f, 0.3f));
            DrawCircle(knee, 4, Art.Steel);
            Art.RoundRect(this, new Rect2(foot + new Vector2(-30, -8), new Vector2(60, 18)), 6, new Color(0.35f, 0.4f, 0.6f));
        }

        // screw bit (silver with spiral stripes), glows with heat
        float shaftTop = top.Y + 40;
        var hot = Mathf.Clamp((_temp - 40f) / 60f, 0f, 1f);
        DrawRect(new Rect2(DrillX - 12 + shake.X, shaftTop, 24, tipY - shaftTop), new Color(0.78f, 0.8f, 0.88f));
        float spin = _drilling ? Elapsed * 30f : 0f;
        for (float y = shaftTop + Mathf.PosMod(spin, 14f); y < tipY - 6; y += 14f)
            DrawLine(new Vector2(DrillX - 13, y + 7) + shake, new Vector2(DrillX + 13, y) + shake, new Color(0.45f, 0.48f, 0.6f), 4f);
        DrawLine(new Vector2(DrillX - 4, shaftTop) + shake, new Vector2(DrillX - 4, tipY - 4) + shake, new Color(1, 1, 1, 0.5f), 3f);
        var tipCol = new Color(0.8f, 0.82f, 0.9f).Lerp(Art.Red, hot);
        DrawColoredPolygon(new[] { new Vector2(DrillX - 13, tipY - 4) + shake, new Vector2(DrillX + 13, tipY - 4) + shake, new Vector2(DrillX, tipY + 24) + shake }, tipCol);
        if (hot > 0.05f) DrawCircle(new Vector2(DrillX, tipY + 8), 30f, new Color(1f, 0.4f, 0.1f, 0.3f * hot));

        // robot head on top (blue light)
        Art.RoundRect(this, new Rect2(top + new Vector2(-34, -6), new Vector2(68, 52)), 14, new Color(0.25f, 0.3f, 0.5f));
        Art.RoundRect(this, new Rect2(top + new Vector2(-30, -2), new Vector2(60, 44)), 12, new Color(0.75f, 0.8f, 0.92f));
        Art.RoundRect(this, new Rect2(top + new Vector2(-20, 6), new Vector2(40, 14)), 7, new Color(0.25f, 0.75f, 1f, 0.7f + 0.3f * Mathf.Sin(Elapsed * 4f)));
        DrawCircle(top + new Vector2(0, -14), 7, Art.Yellow);
        DrawLine(top + new Vector2(0, -8), top + new Vector2(0, -2), Art.Steel, 3f);

        // golden sparks at the tip
        foreach (var c in _chips)
            DrawCircle(c.Pos, 4f, c.Color.Lightened(0.3f));
        if (_drilling)
            for (int k = 0; k < 6; k++)
            {
                float a = Rng.RandfRange(-2.6f, -0.5f);
                var p0 = new Vector2(DrillX, tipY + 10);
                DrawLine(p0, p0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rng.RandfRange(12, 30), new Color(1f, 0.8f, 0.3f, 0.8f), 2f);
            }
    }

    /// <summary>The big rainbow temperature dial with a metal rim and a "DRILL TEMPERATURE" plate.</summary>
    private void DrawDial()
    {
        var c = DialCenter;
        float A(float v) => Mathf.Pi + Mathf.Pi * v / 100f; // 0 = left, 100 = right

        // metal housing + dark face
        Art.HalfDisc(this, c + new Vector2(0, 8), DialRadius + 30, new Color(0.45f, 0.5f, 0.66f));
        Art.HalfDisc(this, c + new Vector2(0, 8), DialRadius + 20, new Color(0.62f, 0.67f, 0.8f));
        Art.HalfDisc(this, c + new Vector2(0, 8), DialRadius + 6, new Color(0.08f, 0.1f, 0.22f));
        for (int k = 0; k <= 6; k++)
        {
            float a = Mathf.Pi + Mathf.Pi * k / 6f;
            DrawCircle(c + new Vector2(0, 8) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (DialRadius + 25), 4f, new Color(0.3f, 0.34f, 0.48f));
        }

        // rainbow band (many thin slices = smooth gradient)
        const int slices = 60;
        for (int i = 0; i < slices; i++)
        {
            float v0 = 100f * i / slices, v1 = 100f * (i + 1) / slices + 0.4f;
            DrawArc(c, DialRadius - 30, A(v0), A(Mathf.Min(v1, 100f)), 3, DialColor((v0 + v1) / 2f), 46f);
        }
        // zone ticks
        foreach (var v in new[] { SafeMax, BestMax })
        {
            float a = A(v);
            DrawLine(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (DialRadius - 56), c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (DialRadius - 4), Colors.White, 3f);
        }

        // labels with icons
        Art.IconCheck(this, c + new Vector2(-128, -112), 13, Colors.White);
        Art.TextCentered(this, "SAFE", c.X - 112, c.Y - 62, 22, Colors.White, 5);
        Art.IconSnowflake(this, c + new Vector2(-112, -30), 15, Colors.White);
        Art.IconCheck(this, c + new Vector2(0, -DialRadius + 6), 14, Colors.White);
        Art.TextCentered(this, "BEST", c.X, c.Y - DialRadius + 52, 24, Colors.White, 5);
        Art.TextCentered(this, "TOO", c.X + 118, c.Y - 82, 20, Colors.White, 5);
        Art.TextCentered(this, "HOT", c.X + 120, c.Y - 58, 20, Colors.White, 5);
        Art.IconFlame(this, c + new Vector2(120, -26), 15);

        // needle (tapered) + glossy knob
        float an = A(Mathf.Clamp(_needle, 0f, 100f));
        var dir = new Vector2(Mathf.Cos(an), Mathf.Sin(an));
        var side = new Vector2(-dir.Y, dir.X);
        var tip = c + dir * (DialRadius - 14);
        DrawColoredPolygon(new[] { c + side * 10, tip, c - side * 10 }, new Color(0.08f, 0.1f, 0.2f));
        DrawColoredPolygon(new[] { c + side * 7, tip - dir * 4, c - side * 7 }, new Color(0.75f, 0.92f, 1f));
        DrawCircle(c, 24, new Color(0.3f, 0.35f, 0.5f));
        DrawCircle(c, 19, new Color(0.25f, 0.65f, 1f));
        DrawCircle(c + new Vector2(-6, -6), 6, new Color(1, 1, 1, 0.6f));

        // plate
        var plate = new Rect2(c.X - DialRadius - 20, c.Y + 22, (DialRadius + 20) * 2, 62);
        Art.RoundRect(this, plate.Grow(4), 16, new Color(0.45f, 0.5f, 0.66f));
        Art.RoundRect(this, plate, 14, new Color(0.1f, 0.12f, 0.22f));
        DrawCircle(plate.Position + new Vector2(16, 31), 4, Art.Steel);
        DrawCircle(new Vector2(plate.End.X - 16, plate.Position.Y + 31), 4, Art.Steel);
        Art.IconThermometer(this, new Vector2(plate.Position.X + 62, plate.Position.Y + 30), 18);
        Art.TextCentered(this, "DRILL TEMPERATURE", c.X + 22, plate.Position.Y + 41, 24, Art.Yellow, 4);

        if (_temp >= SafeMax && _temp < BestMax && _drilling)
            Art.TextCentered(this, "FAST!", c.X, c.Y - 40, 28, Art.Green, 6);
    }
}

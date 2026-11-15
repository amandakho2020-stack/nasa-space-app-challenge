using Godot;

namespace AeroKidz;

/// <summary>
/// MISSION 1 - DAY 3: HARVEST DAY
/// The garden grew from the WATER you caught on Day 1 (GameManager.WaterCollected out of 15):
///   lots of water -> big HEALTHY carrots (most science)
///   little water  -> SMALL ones, brown WITHERED ones (little science) and UNRIPE sprouts you can't pick at all
/// Kept collecting during the oxygen leak? Then you walk in SLOW MOTION (muscle atrophy).
///
/// Move: CLICK where to walk (or WASD / arrows). Walk over a carrot to pick it (carry max 3),
/// then walk into the DEPOSIT box. Stand at the O2 tank to refill Oxygen (costs Power).
/// WIN: every carrot that can be picked is deposited.
/// </summary>
public partial class HarvestGame : MinigameBase
{
    // ---- Tuning ----
    private const int MaxCarry = 3;
    private const float BaseSpeed = 340f;
    private const float RefillO2PerSec = 22f;
    private const float RefillPowerCostPerSec = 3f;

    private enum Plant { Healthy, Small, Withered, Unripe, Picked }
    private static readonly int[] PlantScience = { 25, 10, 2, 0, 0 };

    private static readonly Rect2 Floor = new(70, 250, 1140, 420);
    private static readonly Rect2[] Beds =
    {
        new(270, 300, 300, 70), new(700, 300, 300, 70),
        new(270, 510, 300, 70), new(700, 510, 300, 70),
    };
    private static readonly Rect2 DepositBox = new(1095, 405, 100, 95);
    private static readonly Rect2 OxygenStation = new(85, 385, 90, 125);

    private Vector2[] _slots;
    private Plant[] _plants;
    private int[] _carried = new int[MaxCarry];   // science value of each carried carrot

    private Vector2 _player = new(640, 460);
    private float _walk;
    private bool _faceLeft;
    private int _carrying;
    private int _deposited;
    private int _goal;
    private bool _refilling;
    private float _water; // 0..1

    protected override void Setup()
    {
        TimeLeft = 100f;
        DrainPerSecond = new Vector3(0.6f, 0.25f, 0f);
        _water = Mathf.Clamp(Gm.WaterCollected / (float)GameManager.WaterGoal, 0f, 1f);

        // 4 carrots per bed. How they grew depends on the water.
        _slots = new Vector2[Beds.Length * 4];
        _plants = new Plant[_slots.Length];
        for (int b = 0; b < Beds.Length; b++)
            for (int k = 0; k < 4; k++)
            {
                int i = b * 4 + k;
                _slots[i] = new Vector2(Beds[b].Position.X + 45 + k * 70, Beds[b].Position.Y + 38);
                float r = Rng.Randf();
                _plants[i] =
                    r < _water * 0.75f ? Plant.Healthy :
                    r < _water ? Plant.Small :
                    r < _water + (1f - _water) * 0.55f ? Plant.Withered :
                    Plant.Unripe;
            }

        // always leave at least 3 to pick
        for (int i = 0; CountPickable() < 3 && i < _plants.Length; i++)
            if (_plants[i] == Plant.Unripe) _plants[i] = Plant.Withered;

        _goal = CountPickable();
    }

    private int CountPickable()
    {
        int n = 0;
        foreach (var p in _plants)
            if (p is Plant.Healthy or Plant.Small or Plant.Withered) n++;
        return n;
    }

    protected override void Tick(float dt)
    {
        // ---- Move (click to walk, or keys) ----
        var dir = WalkDirection(_player + new Vector2(0, -40), false);
        if (dir != Vector2.Zero)
        {
            _player += dir * BaseSpeed * Gm.PlayerSpeedMultiplier * dt;
            _player = _player.Clamp(Floor.Position + new Vector2(20, 90), Floor.End - new Vector2(20, 0));
            _walk += dt * 12f;
            if (dir.X != 0) _faceLeft = dir.X < 0;
        }

        // ---- Pick ----
        for (int i = 0; i < _slots.Length; i++)
        {
            var p = _plants[i];
            if (p is Plant.Picked or Plant.Unripe || _carrying >= MaxCarry) continue;
            if (_player.DistanceTo(_slots[i] + new Vector2(0, 20)) < 50f)
            {
                _carried[_carrying++] = PlantScience[(int)p];
                _plants[i] = Plant.Picked;
                Pop(_slots[i], p == Plant.Withered ? "WITHERED..." : "+1 CARROT", p == Plant.Withered ? Art.Orange : Art.Magenta);
                Sound(Sfx.Pop);
            }
        }

        // ---- Deposit ----
        if (_carrying > 0 && DepositBox.Grow(25f).HasPoint(_player))
        {
            int sci = 0;
            for (int c = 0; c < _carrying; c++) sci += _carried[c];
            _deposited += _carrying;
            Gm.AddScience(sci);
            Pop(DepositBox.GetCenter(), $"+{sci} SCIENCE", Art.Green);
            Sound(Sfx.Ding, -10f);
            _carrying = 0;
        }

        // ---- Oxygen refill (costs power) ----
        _refilling = OxygenStation.Grow(30f).HasPoint(_player) && Gm.Oxygen < GameManager.MaxResource;
        if (_refilling)
            Gm.ChangeResources(RefillO2PerSec * dt, -RefillPowerCostPerSec * dt, 0f);

        if (_deposited >= _goal)
            Win();
    }

    protected override void OnTimeUp() => Fail("The day ended before the harvest was finished.");

    // ==================================================================
    // DRAWING - a hydroponic farm: glass water tanks, grow lights, carrots with their roots in the water
    // ==================================================================
    private static readonly Color CarrotOrange = new(1f, 0.52f, 0.08f);
    private static readonly Color LeafGreen = new(0.35f, 0.85f, 0.3f);
    private static readonly Color DryBrown = new(0.55f, 0.4f, 0.22f);
    private const string O2Pic = "res://AeroKidz/Assets/energy/energy_10.png"; // oxygen cells

    public override void _Draw()
    {
        Art.DomeBackground(this, Size, Elapsed);
        DrawFarmFloor();

        foreach (var bed in Beds)
            DrawGrowLight(bed);
        for (int b = 0; b < Beds.Length; b++)
            DrawTankBack(Beds[b]);
        for (int i = 0; i < _slots.Length; i++)
            DrawPlant(_slots[i], _plants[i], i);
        for (int b = 0; b < Beds.Length; b++)
            DrawTankFront(Beds[b], b);

        DrawO2Station();
        DrawCrate();

        // walk target marker
        if (WalkTarget is Vector2 wt)
        {
            DrawArc(wt, 14f, 0, Mathf.Tau, 24, new Color(Art.Yellow, 0.8f), 3f);
            DrawArc(wt, 6f, 0, Mathf.Tau, 16, new Color(Art.Yellow, 0.5f), 2f);
        }

        // the Commander, with the harvest basket above the head
        Art.Astronaut(this, _player, 0.9f, _walk, _faceLeft);
        if (_carrying > 0) DrawBasket(_player + new Vector2(0, -118));
        if (_refilling)
            Art.TextCentered(this, "REFILLING O2...", _player.X, _player.Y - 150, 16, Art.Cyan, 4);

        DrawStatus();

        if (Gm.PlayerSpeedMultiplier < 1f)
        {
            Art.Panel(this, new Rect2(20, 170, 330, 36), Art.Red);
            Art.Text(this, "SLOW - LOW OXYGEN!", new Vector2(36, 195), 18, Art.Red);
        }
        else if (_water < 0.7f)
        {
            Art.Panel(this, new Rect2(20, 170, 360, 36), Art.Orange);
            Art.Text(this, "LITTLE WATER = SMALL CARROTS", new Vector2(36, 195), 16, Art.Orange);
        }
        if (Gm.Oxygen < 30f && Mathf.Sin(Elapsed * 8f) > 0f)
            Art.TextCentered(this, "LOW OXYGEN! GO TO THE O2 TANK!", 640, 235, 24, Art.Red, 5);

        Art.Hint(this, "CLICK TO WALK - PICK CARROTS - DROP THEM IN THE CRATE");
        DrawFloaters();
    }

    private void DrawFarmFloor()
    {
        DrawRect(Floor, new Color(0.16f, 0.19f, 0.25f, 0.95f));
        // metal deck tiles
        for (float x = Floor.Position.X; x < Floor.End.X; x += 60f)
            DrawLine(new Vector2(x, Floor.Position.Y), new Vector2(x, Floor.End.Y), new Color(1, 1, 1, 0.05f), 2f);
        for (float y = Floor.Position.Y; y < Floor.End.Y; y += 60f)
            DrawLine(new Vector2(Floor.Position.X, y), new Vector2(Floor.End.X, y), new Color(1, 1, 1, 0.05f), 2f);
        // green walkway between the tanks
        DrawRect(new Rect2(Floor.Position.X, 420, Floor.Size.X, 48), new Color(0.25f, 0.6f, 0.35f, 0.18f));
        DrawRect(Floor, new Color(Art.Cyan, 0.45f), false, 3f);
    }

    /// <summary>LED grow light above a tank: pink-purple light that plants love.</summary>
    private void DrawGrowLight(Rect2 bed)
    {
        float y = bed.Position.Y - 52f;
        float glow = 0.75f + 0.25f * Mathf.Sin(Elapsed * 2f + bed.Position.X);
        var cone = new[]
        {
            new Vector2(bed.Position.X + 14, y + 8), new Vector2(bed.End.X - 14, y + 8),
            new Vector2(bed.End.X + 10, bed.Position.Y + 16), new Vector2(bed.Position.X - 10, bed.Position.Y + 16),
        };
        DrawColoredPolygon(cone, new Color(1f, 0.35f, 0.9f, 0.10f * glow));
        DrawRect(new Rect2(bed.Position.X + 6, y - 4, bed.Size.X - 12, 14), new Color(0.2f, 0.22f, 0.28f));
        DrawRect(new Rect2(bed.Position.X + 12, y + 6, bed.Size.X - 24, 5), new Color(1f, 0.45f, 0.95f, glow));
        // hanging wires
        DrawLine(new Vector2(bed.Position.X + 30, y - 4), new Vector2(bed.Position.X + 30, y - 30), Art.Steel, 2f);
        DrawLine(new Vector2(bed.End.X - 30, y - 4), new Vector2(bed.End.X - 30, y - 30), Art.Steel, 2f);
    }

    /// <summary>The glass water tank (drawn BEFORE the carrots, so their roots hang in the water).</summary>
    private void DrawTankBack(Rect2 bed)
    {
        var water = new Rect2(bed.Position.X, bed.Position.Y + 20, bed.Size.X, bed.Size.Y - 20);
        DrawRect(water, new Color(0.15f, 0.55f, 0.9f, 0.45f));
        // gentle waves on top of the water
        var pts = new Vector2[16];
        for (int k = 0; k < pts.Length; k++)
        {
            float x = bed.Position.X + k * bed.Size.X / (pts.Length - 1);
            pts[k] = new Vector2(x, water.Position.Y + 3 + 2f * Mathf.Sin(Elapsed * 3f + x * 0.08f));
        }
        DrawPolyline(pts, new Color(0.7f, 0.9f, 1f, 0.6f), 2f);
        // bubbles (oxygen for the roots)
        for (int k = 0; k < 5; k++)
        {
            float t = (Elapsed * 0.5f + k * 0.21f + bed.Position.X * 0.001f) % 1f;
            var bp = new Vector2(bed.Position.X + 25 + k * 62, water.End.Y - 6 - t * (water.Size.Y - 12));
            DrawArc(bp, 3f, 0, Mathf.Tau, 10, new Color(1, 1, 1, 0.5f * (1f - t)), 1.5f);
        }
    }

    /// <summary>Net-pot plate + steel frame + glass shine (drawn AFTER the carrots).</summary>
    private void DrawTankFront(Rect2 bed, int b)
    {
        // the plate the carrots sit in
        var plate = new Rect2(bed.Position.X - 4, bed.Position.Y + 12, bed.Size.X + 8, 10);
        DrawRect(plate, new Color(0.85f, 0.88f, 0.92f));
        DrawRect(new Rect2(plate.Position.X, plate.End.Y - 3, plate.Size.X, 3), new Color(0.55f, 0.6f, 0.68f));
        // steel frame + glass
        DrawRect(new Rect2(bed.Position.X, bed.Position.Y + 20, bed.Size.X, bed.Size.Y - 20), new Color(0.75f, 0.85f, 0.95f), false, 4f);
        DrawLine(bed.Position + new Vector2(14, 28), bed.Position + new Vector2(30, bed.Size.Y - 6), new Color(1, 1, 1, 0.25f), 3f);
        DrawRect(new Rect2(bed.Position.X - 8, bed.End.Y, bed.Size.X + 16, 8), new Color(0.35f, 0.38f, 0.45f));
        // little label
        Art.TextCentered(this, $"TANK {b + 1}", bed.GetCenter().X, bed.End.Y + 24, 12, new Color(Art.Cyan, 0.8f));
    }

    private void DrawPlant(Vector2 slot, Plant plant, int i)
    {
        var top = new Vector2(slot.X, slot.Y - 22); // where the carrot meets the plate
        switch (plant)
        {
            case Plant.Healthy:
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Elapsed * 5f + i);
                DrawCircle(top + new Vector2(0, -6), 30f + pulse * 4f, new Color(1f, 0.9f, 0.3f, 0.18f));
                DrawCarrot(top, 34f, 20f, CarrotOrange, LeafGreen, 0f, 1f);
                if (pulse > 0.85f) DrawStar(top + new Vector2(16, -34), 5f);
                break;
            }
            case Plant.Small:
                DrawCarrot(top, 18f, 11f, CarrotOrange.Lightened(0.15f), LeafGreen.Darkened(0.1f), 0f, 0.6f);
                break;
            case Plant.Withered:
                DrawCarrot(top, 22f, 13f, new Color(0.7f, 0.45f, 0.25f), DryBrown, 1f, 0.75f);
                break;
            case Plant.Unripe:
                DrawLine(top, top - new Vector2(0, 12), LeafGreen.Darkened(0.3f), 3f);
                DrawCircle(top - new Vector2(5, 14), 4f, LeafGreen.Darkened(0.2f));
                DrawCircle(top - new Vector2(-5, 13), 4f, LeafGreen.Darkened(0.2f));
                Art.TextCentered(this, "not ready", top.X, top.Y - 22, 12, new Color(1, 1, 1, 0.7f));
                break;
            case Plant.Picked:
                DrawCircle(top + new Vector2(0, 2), 7f, new Color(0.3f, 0.32f, 0.38f)); // empty hole
                break;
        }
    }

    /// <summary>
    /// A carrot: orange root pointing DOWN (into the water) with ridges, and feathery green leaves on top.
    /// wilt 0 = fresh and upright, 1 = dry and drooping.
    /// </summary>
    private void DrawCarrot(Vector2 top, float len, float width, Color root, Color leaf, float wilt, float leafScale)
    {
        // leaves first (behind the root's shoulder)
        int fronds = 4;
        for (int f = 0; f < fronds; f++)
        {
            float spread = (f - (fronds - 1) / 2f) * 0.38f;
            float sway = 0.06f * Mathf.Sin(Elapsed * 2.5f + f + top.X * 0.05f) * (1f - wilt);
            float angle = -Mathf.Pi / 2f + spread * (1f + wilt * 1.6f) + sway;
            float frondLen = (26f + (f % 2) * 6f) * leafScale * (1f - wilt * 0.25f);
            Vector2 baseP = top + new Vector2(0, -2);
            Vector2 tip = baseP + Vector2.FromAngle(angle) * frondLen + new Vector2(0, wilt * 14f);
            DrawLine(baseP, tip, leaf.Darkened(0.15f), 2.5f);
            for (int s = 1; s <= 4; s++)
            {
                Vector2 q = baseP.Lerp(tip, s / 4.4f);
                DrawCircle(q + Vector2.FromAngle(angle + 1.3f) * 4f * leafScale, 2.4f * leafScale, leaf);
                DrawCircle(q + Vector2.FromAngle(angle - 1.3f) * 4f * leafScale, 2.4f * leafScale, leaf);
            }
            DrawCircle(tip, 2.6f * leafScale, leaf);
        }

        // the root (a tapered shape with a slightly rounded shoulder)
        float h = width / 2f;
        var body = new[]
        {
            top + new Vector2(-h, 0), top + new Vector2(-h * 0.85f, -3), top + new Vector2(h * 0.85f, -3), top + new Vector2(h, 0),
            top + new Vector2(h * 0.55f, len * 0.55f), top + new Vector2(h * 0.15f, len * 0.92f), top + new Vector2(0, len),
            top + new Vector2(-h * 0.15f, len * 0.92f), top + new Vector2(-h * 0.55f, len * 0.55f),
        };
        DrawColoredPolygon(body, root);
        DrawPolyline(new[] { body[0], body[1], body[2], body[3], body[4], body[5], body[6], body[7], body[8], body[0] },
            root.Darkened(0.35f), 1.5f);
        // ridges + shine
        for (int r = 1; r <= 3; r++)
        {
            float y = len * r / 4.5f;
            float w = h * (1f - y / len) * 0.8f;
            DrawLine(top + new Vector2(-w, y), top + new Vector2(-w * 0.2f, y + 2), root.Darkened(0.25f), 1.5f);
            DrawLine(top + new Vector2(w * 0.3f, y + 5), top + new Vector2(w, y + 3), root.Darkened(0.25f), 1.5f);
        }
        DrawLine(top + new Vector2(-h * 0.45f, 3), top + new Vector2(-h * 0.2f, len * 0.5f), new Color(1, 1, 1, 0.35f), 2f);
        // a thin hair root at the tip
        DrawLine(top + new Vector2(0, len), top + new Vector2(2, len + 7), root.Darkened(0.2f), 1f);
    }

    private void DrawStar(Vector2 c, float r)
    {
        DrawLine(c - new Vector2(r, 0), c + new Vector2(r, 0), Art.White, 2f);
        DrawLine(c - new Vector2(0, r), c + new Vector2(0, r), Art.White, 2f);
    }

    /// <summary>The basket the Commander carries, with the picked carrots poking out.</summary>
    private void DrawBasket(Vector2 c)
    {
        for (int k = 0; k < _carrying; k++)
        {
            var t = c + new Vector2(-14 + k * 14, -4);
            bool dry = _carried[k] <= PlantScience[(int)Plant.Withered];
            DrawCarrot(t + new Vector2(0, -6), 18f, 10f, dry ? new Color(0.7f, 0.45f, 0.25f) : CarrotOrange,
                dry ? DryBrown : LeafGreen, dry ? 1f : 0f, 0.55f);
        }
        var basket = new[]
        {
            c + new Vector2(-26, 0), c + new Vector2(26, 0), c + new Vector2(20, 22), c + new Vector2(-20, 22),
        };
        DrawColoredPolygon(basket, new Color(0.72f, 0.5f, 0.25f));
        for (int k = 1; k < 3; k++)
            DrawLine(c + new Vector2(-24 + k * 1.5f, k * 7), c + new Vector2(24 - k * 1.5f, k * 7), new Color(0.5f, 0.32f, 0.15f), 2f);
        DrawPolyline(new[] { basket[0], basket[1], basket[2], basket[3], basket[0] }, new Color(0.45f, 0.28f, 0.12f), 2f);
    }

    private void DrawO2Station()
    {
        var c = OxygenStation.GetCenter();
        bool on = _refilling;
        DrawCircle(c + new Vector2(0, 10), 62f, new Color(Art.Cyan, on ? 0.25f : 0.10f));
        Art.Picture(this, O2Pic, c + new Vector2(0, 4), 120f);
        Art.TextCentered(this, "O2 REFILL", c.X, OxygenStation.End.Y + 26, 16, Art.Cyan, 4);
        Art.TextCentered(this, "(uses Power)", c.X, OxygenStation.End.Y + 44, 12, Art.Orange, 3);
    }

    /// <summary>The carrot crate: walk into it to drop your carrots.</summary>
    private void DrawCrate()
    {
        var r = DepositBox;
        bool near = _carrying > 0 && r.Grow(60f).HasPoint(_player);
        if (near || _carrying > 0)
            DrawRect(r.Grow(8f), new Color(Art.Yellow, near ? 0.35f : 0.12f + 0.08f * Mathf.Sin(Elapsed * 5f)));
        DrawRect(r, new Color(0.62f, 0.42f, 0.22f));
        for (int k = 1; k < 4; k++)
            DrawLine(new Vector2(r.Position.X, r.Position.Y + k * r.Size.Y / 4f), new Vector2(r.End.X, r.Position.Y + k * r.Size.Y / 4f),
                new Color(0.42f, 0.27f, 0.12f), 3f);
        DrawLine(r.Position, r.End, new Color(0.45f, 0.3f, 0.14f), 4f);
        DrawRect(r, new Color(0.38f, 0.24f, 0.1f), false, 4f);
        // carrots already inside, poking out of the top
        int shown = Mathf.Min(_deposited, 7);
        for (int k = 0; k < shown; k++)
            DrawCarrot(new Vector2(r.Position.X + 14 + k * 12, r.Position.Y + 2), 10f, 8f, CarrotOrange, LeafGreen, 0f, 0.45f);
        Art.TextCentered(this, "CARROT CRATE", r.GetCenter().X, r.Position.Y - 34, 15, Art.Yellow, 4);
        Art.TextCentered(this, "DROP HERE", r.GetCenter().X, r.End.Y + 22, 13, Art.White, 3);
    }

    private void DrawStatus()
    {
        var panel = new Rect2(960, 84, 300, 96);
        Art.Panel(this, panel, CarrotOrange);
        DrawCarrot(panel.Position + new Vector2(30, 34), 26f, 15f, CarrotOrange, LeafGreen, 0f, 0.7f);
        Art.Text(this, $"CARROTS: {_deposited} / {_goal}", panel.Position + new Vector2(58, 30), 20, CarrotOrange);
        // progress bar
        var bar = new Rect2(panel.Position + new Vector2(58, 40), new Vector2(222, 10));
        DrawRect(bar, new Color(0, 0, 0, 0.5f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * (_goal > 0 ? _deposited / (float)_goal : 0f), bar.Size.Y)), CarrotOrange);
        Art.Text(this, $"WATER GIVEN: {Gm.WaterCollected} / {GameManager.WaterGoal}", panel.Position + new Vector2(58, 66), 13, Art.Cyan);
        Art.Text(this, $"BASKET: {_carrying} / {MaxCarry}", panel.Position + new Vector2(58, 86), 13, Art.White);
    }
}

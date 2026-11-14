using Godot;

namespace AeroKidz;

/// <summary>
/// MISSION 1 - DAY 3: HARVEST DAY
/// The garden grew from the WATER you caught on Day 1 (GameManager.WaterCollected out of 15):
///   lots of water -> big HEALTHY radishes (most science)
///   little water  -> SMALL ones, brown WITHERED ones (little science) and UNRIPE sprouts you can't pick at all
/// Kept collecting during the oxygen leak? Then you walk in SLOW MOTION (muscle atrophy).
///
/// Move: CLICK where to walk (or WASD / arrows). Walk over a radish to pick it (carry max 3),
/// then walk into the DEPOSIT box. Stand at the O2 tank to refill Oxygen (costs Power).
/// WIN: every radish that can be picked is deposited.
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
    private int[] _carried = new int[MaxCarry];   // science value of each carried radish

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

        // 4 radishes per bed. How they grew depends on the water.
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
                Pop(_slots[i], p == Plant.Withered ? "WITHERED..." : "+1 RADISH", p == Plant.Withered ? Art.Orange : Art.Magenta);
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
    public override void _Draw()
    {
        Art.DomeBackground(this, Size, Elapsed);

        DrawRect(Floor, new Color(0.2f, 0.23f, 0.3f, 0.9f));
        DrawRect(Floor, new Color(Art.Cyan, 0.4f), false, 2f);

        foreach (var bed in Beds)
        {
            DrawRect(bed, new Color(0.15f, 0.4f, 0.65f, 0.85f)); // hydroponic water tank
            DrawRect(bed, Art.Steel, false, 4f);
        }
        for (int i = 0; i < _slots.Length; i++)
            DrawPlant(_slots[i], _plants[i], i);

        // O2 refill station
        for (int t = 0; t < 2; t++)
        {
            var tank = new Rect2(OxygenStation.Position.X + 5 + t * 42, OxygenStation.Position.Y + 20, 36, 100);
            DrawRect(tank, Art.White);
            DrawCircle(new Vector2(tank.GetCenter().X, tank.Position.Y), 18f, Art.White);
            DrawRect(new Rect2(tank.Position.X, tank.Position.Y + 40, 36, 10), Art.Cyan);
        }
        Art.TextCentered(this, "O2 REFILL", OxygenStation.GetCenter().X, OxygenStation.End.Y + 22, 15, Art.Cyan);
        Art.TextCentered(this, "(uses Power)", OxygenStation.GetCenter().X, OxygenStation.End.Y + 40, 12, Art.Orange);

        // Deposit box
        DrawRect(DepositBox, new Color(0.5f, 0.54f, 0.6f));
        DrawRect(new Rect2(DepositBox.Position, new Vector2(DepositBox.Size.X, 18)), new Color(0.3f, 0.33f, 0.4f));
        Art.TextCentered(this, "DEPOSIT", DepositBox.GetCenter().X, DepositBox.Position.Y - 10, 16, Art.Yellow);

        // Walk target marker
        if (WalkTarget is Vector2 wt)
            DrawArc(wt, 12f, 0, Mathf.Tau, 20, new Color(Art.Yellow, 0.7f), 2f);

        // Commander + carried radishes
        Art.Astronaut(this, _player, 0.9f, _walk, _faceLeft);
        for (int c = 0; c < _carrying; c++)
            DrawCircle(_player + new Vector2(-14 + c * 14, -112), 8f, new Color(0.85f, 0.15f, 0.35f));
        if (_refilling)
            Art.TextCentered(this, "REFILLING O2...", _player.X, _player.Y - 130, 15, Art.Cyan);

        // Status
        Art.Panel(this, new Rect2(980, 170, 280, 72), Art.Magenta);
        Art.Text(this, $"HARVESTED: {_deposited} / {_goal}", new Vector2(996, 196), 20, Art.Magenta);
        Art.Text(this, $"WATER GIVEN: {Gm.WaterCollected} / {GameManager.WaterGoal}", new Vector2(996, 220), 14, Art.Cyan);
        Art.Text(this, $"CARRYING: {_carrying} / {MaxCarry}", new Vector2(996, 238), 13, Art.White);

        if (Gm.PlayerSpeedMultiplier < 1f)
        {
            Art.Panel(this, new Rect2(20, 170, 360, 34), Art.Red);
            Art.Text(this, "SLOW - LOW OXYGEN!", new Vector2(34, 193), 17, Art.Red);
        }
        else if (_water < 0.7f)
        {
            Art.Panel(this, new Rect2(20, 170, 380, 34), Art.Orange);
            Art.Text(this, "LITTLE WATER = SMALL PLANTS", new Vector2(34, 193), 16, Art.Orange);
        }
        if (Gm.Oxygen < 30f && Mathf.Sin(Elapsed * 8f) > 0f)
            Art.TextCentered(this, "LOW OXYGEN! GO TO THE O2 TANK!", 640, 235, 24, Art.Red, 5);

        Art.Hint(this, "CLICK TO WALK - PICK RADISHES - DROP IN THE BOX");
        DrawFloaters();
    }

    private void DrawPlant(Vector2 p, Plant plant, int i)
    {
        switch (plant)
        {
            case Plant.Healthy:
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Elapsed * 5f + i);
                DrawCircle(p, 26f + pulse * 3f, new Color(Art.Yellow, 0.25f));
                DrawCircle(p, 17f, new Color(0.85f, 0.15f, 0.35f));
                DrawCircle(p + new Vector2(-9, -21), 9f, Art.Green);
                DrawCircle(p + new Vector2(9, -21), 9f, Art.Green);
                break;
            }
            case Plant.Small:
                DrawCircle(p, 18f, new Color(Art.Yellow, 0.15f));
                DrawCircle(p, 9f, new Color(0.8f, 0.3f, 0.4f));
                DrawCircle(p + new Vector2(-5, -12), 5f, Art.Green.Darkened(0.2f));
                DrawCircle(p + new Vector2(5, -12), 5f, Art.Green.Darkened(0.2f));
                break;
            case Plant.Withered:
                DrawCircle(p, 10f, new Color(0.5f, 0.35f, 0.25f));
                DrawLine(p + new Vector2(0, -8), p + new Vector2(-10, -18), new Color(0.55f, 0.45f, 0.2f), 3f);
                DrawLine(p + new Vector2(0, -8), p + new Vector2(9, -16), new Color(0.55f, 0.45f, 0.2f), 3f);
                break;
            case Plant.Unripe:
                DrawLine(p, p - new Vector2(0, 12), Art.Green.Darkened(0.3f), 3f);
                DrawCircle(p - new Vector2(0, 12), 3f, Art.Green.Darkened(0.3f));
                Art.TextCentered(this, "not ready", p.X, p.Y + 22, 12, new Color(1, 1, 1, 0.6f));
                break;
        }
    }
}

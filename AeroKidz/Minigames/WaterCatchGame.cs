using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// MISSION 1 - DAY 1: THE SPACE GARDEN (hydroponics)
/// Move with A/D or the arrow keys. Catch the falling vitamin-water drops in your basket to feed the plants.
/// The grow lights use POWER all the time: catch the yellow BATTERIES to keep them on.
/// WIN: catch 15 drops before the timer ends. LOSE: time runs out, or Power hits 0%.
/// </summary>
public partial class WaterCatchGame : MinigameBase
{
    // ---- Tuning ----
    private const int Goal = GameManager.WaterGoal;
    private const int HazardAtDrops = 6;      // the oxygen leak alert pops up after this many drops
    private bool _hazardAsked;
    private const float BaseSpeed = 520f;      // px/sec (multiplied by PlayerSpeedMultiplier)
    private const float FeetY = 660f;
    private const float BatteryChance = 0.18f;
    private const float BatteryPower = 12f;
    private const float LightsPowerPerSec = 0.9f;

    private sealed class Faller { public Vector2 Pos; public float Speed; public bool Battery; public float Wobble; }
    private readonly List<Faller> _fallers = new();

    private float _playerX = 640f;
    private float _spawnTimer;
    private float _walk;
    private bool _faceLeft;
    private int _caught;
    private bool _warnedLowPower;

    protected override void Setup()
    {
        TimeLeft = 60f;
        DrainPerSecond = new Vector3(0.3f, LightsPowerPerSec, 0f); // the grow lights eat power
    }

    protected override void Tick(float dt)
    {
        // ---- Move (keys, or just move the mouse) ----
        float dir = WalkDirection(new Vector2(_playerX, FeetY), true, followMouse: true).X;
        _playerX = Mathf.Clamp(_playerX + dir * BaseSpeed * Gm.PlayerSpeedMultiplier * dt, 60f, 1220f);
        if (dir != 0f)
        {
            _walk += dt * 12f;
            _faceLeft = dir < 0f;
        }

        // ---- Spawn ----
        _spawnTimer -= dt;
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = Rng.RandfRange(0.45f, 0.75f);
            _fallers.Add(new Faller
            {
                Pos = new Vector2(Rng.RandfRange(60f, 1220f), 175f),
                Speed = Rng.RandfRange(190f, 300f),
                Battery = Rng.Randf() < BatteryChance,
                Wobble = Rng.RandfRange(0f, 6f),
            });
        }

        // ---- Fall + catch ----
        var basket = BasketRect();
        for (int i = _fallers.Count - 1; i >= 0; i--)
        {
            var f = _fallers[i];
            f.Pos.Y += f.Speed * dt;

            if (basket.Grow(6f).HasPoint(f.Pos))
            {
                if (f.Battery)
                {
                    Gm.ChangeResources(0f, BatteryPower, 0f);
                    Pop(f.Pos, $"+{BatteryPower:0} POWER", Art.Yellow);
                    Sound(Sfx.Ding, -10f);
                }
                else
                {
                    _caught++;
                    Gm.WaterCollected = _caught; // the harvest grows from this
                    Gm.AddScience(10);
                    Pop(f.Pos, "+1 WATER", Art.Cyan);
                    Sound(Sfx.Pop);
                }
                _fallers.RemoveAt(i);
            }
            else if (f.Pos.Y > 700f)
            {
                if (!f.Battery) Pop(new Vector2(f.Pos.X, 660f), "MISS!", Art.Red);
                _fallers.RemoveAt(i);
            }
        }

        if (!_warnedLowPower && Gm.Power < 30f)
        {
            _warnedLowPower = true;
            Say("Lights are weak! Catch a battery!");
        }
        if (Gm.Power > 45f) _warnedLowPower = false;

        // The oxygen leak hits in the MIDDLE of the water game (Mission 1 hazard).
        if (!_hazardAsked && _caught >= HazardAtDrops)
        {
            _hazardAsked = true;
            _fallers.Clear();
            RequestHazard();
            return;
        }

        if (_caught >= Goal)
            Win();
    }

    protected override void OnTimeUp() => Fail("The plants didn't get enough water in time.");

    private Rect2 BasketRect() => new(_playerX - 52f, FeetY - 140f, 104f, 34f);

    // ==================================================================
    public override void _Draw()
    {
        Art.DomeBackground(this, Size, Elapsed);

        float light = Mathf.Clamp(Gm.Power / 60f, 0.15f, 1f);         // lights dim when power is low
        bool flicker = Gm.Power < 25f && Mathf.Sin(Elapsed * 20f) > 0.3f;
        if (flicker) light *= 0.4f;

        // Grow lights + hydroponic tanks (no soil!) - the plants grow as you catch water
        float growth = (float)_caught / Goal;
        for (int b = 0; b < 3; b++)
        {
            float x = 110 + b * 380;
            // lamp
            DrawRect(new Rect2(x + 40, 470, 220, 14), Art.Steel);
            var beam = new[] { new Vector2(x + 50, 484), new Vector2(x + 250, 484), new Vector2(x + 300, 610), new Vector2(x, 610) };
            DrawColoredPolygon(beam, new Color(1f, 0.55f, 0.95f, 0.22f * light));
            DrawRect(new Rect2(x + 50, 482, 200, 5), new Color(1f, 0.6f, 1f, light));

            // water tank with roots
            var tank = new Rect2(x, 610, 300, 45);
            DrawRect(tank, new Color(0.15f, 0.45f, 0.75f, 0.85f));
            DrawRect(tank, Art.Cyan, false, 2f);
            DrawRect(new Rect2(x - 6, tank.End.Y, 312, 14), Art.Steel);
            for (int k = 0; k < 5; k++)
            {
                var root = new Vector2(x + 30 + k * 60, 612);
                float h = 6f + 30f * growth;
                var leaf = light > 0.3f ? Art.Green : new Color(0.4f, 0.5f, 0.25f);
                DrawLine(root, root + new Vector2(0, 26), new Color(0.9f, 0.9f, 0.8f, 0.7f), 2f); // roots in water
                DrawLine(root, root - new Vector2(0, h), leaf, 4f);
                DrawCircle(root - new Vector2(7, h), 3f + 6f * growth, leaf);
                DrawCircle(root - new Vector2(-7, h - 3), 3f + 6f * growth, leaf);
            }
        }

        // Sprinkler pipe across the top
        DrawRect(new Rect2(0, 166, Size.X, 10), Art.Steel);

        // Falling objects
        foreach (var f in _fallers)
        {
            if (f.Battery)
            {
                // battery picture (craftpix energy icon pack)
                DrawCircle(f.Pos, 22f, new Color(0.3f, 0.85f, 1f, 0.18f));
                Art.Picture(this, "res://AeroKidz/Assets/energy/energy_04.png", f.Pos, 52f);
            }
            else
            {
                float w = Mathf.Sin(Elapsed * 6f + f.Wobble) * 1.5f;
                DrawColoredPolygon(new[] { f.Pos + new Vector2(w, -22), f.Pos + new Vector2(-9, -4), f.Pos + new Vector2(9, -4) }, Art.Cyan);
                DrawCircle(f.Pos, 10f, Art.Cyan);
                DrawCircle(f.Pos + new Vector2(-3, -3), 3f, new Color(1, 1, 1, 0.7f));
            }
        }

        // Commander + basket held overhead
        Art.Astronaut(this, new Vector2(_playerX, FeetY), 1f, _walk, _faceLeft);
        var basket = BasketRect();
        DrawLine(new Vector2(_playerX - 16, FeetY - 62), new Vector2(basket.Position.X + 12, basket.End.Y), Art.White, 6f);
        DrawLine(new Vector2(_playerX + 16, FeetY - 62), new Vector2(basket.End.X - 12, basket.End.Y), Art.White, 6f);
        DrawColoredPolygon(new[]
        {
            basket.Position, new Vector2(basket.End.X, basket.Position.Y),
            new Vector2(basket.End.X - 12, basket.End.Y), new Vector2(basket.Position.X + 12, basket.End.Y),
        }, new Color(0.75f, 0.8f, 0.88f));
        for (int i = 1; i < 5; i++)
            DrawLine(basket.Position + new Vector2(i * 20.8f, 0), new Vector2(basket.Position.X + 12 + i * 16f, basket.End.Y), Art.Steel, 2f);

        // Score + lights status
        Art.Panel(this, new Rect2(1030, 170, 230, 70), Art.Cyan);
        Art.Text(this, $"WATER: {_caught} / {Goal}", new Vector2(1048, 198), 22, Art.Cyan);
        Art.Text(this, Gm.Power < 25f ? "LIGHTS: FLICKERING!" : "LIGHTS: ON", new Vector2(1048, 226), 15,
            Gm.Power < 25f ? Art.Red : Art.Yellow);

        if (Gm.PlayerSpeedMultiplier < 1f)
            Art.TextCentered(this, "SLOW - LOW OXYGEN!", _playerX, FeetY - 160, 18, Art.Red, 4);

        Art.Hint(this, "MOVE THE MOUSE - CATCH WATER AND BATTERIES!");
        DrawFloaters();
    }
}

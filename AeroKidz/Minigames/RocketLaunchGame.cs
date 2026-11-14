using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// END OF EVERY MISSION: RUN TO THE ROCKET!
/// Walk (A/D or arrows) across the launch pad to the Earth rocket before the rocket clock hits 0:00.
/// Reach the door -> 3, 2, 1 countdown -> LIFTOFF -> mission complete.
/// There is no own timer: the rocket clock at the top of the screen is the limit (LevelController).
/// </summary>
public partial class RocketLaunchGame : MinigameBase
{
    // ---- Tuning ----
    private const float WalkSpeed = 300f;
    private const float GroundY = 650f;
    private const float RocketX = 1060f;
    private const float DoorX = 1000f;
    private const float CountdownSeconds = 3f;
    private const float LiftoffSeconds = 2.4f;

    private float _playerX = 140f;
    private float _walk;
    private bool _boarded;
    private float _countdown = CountdownSeconds;
    private int _lastBeep = -1;
    private float _liftoff = -1f;
    private float _rocketY;

    private sealed class Smoke { public Vector2 Pos, Vel; public float Life, Size; }
    private readonly List<Smoke> _smoke = new();

    protected override void Setup()
    {
        ShowTimer = false;
        AllowRoomShortcuts = false;
        DrainPerSecond = new Vector3(0.2f, 0f, 0f);
    }

    protected override void Tick(float dt)
    {
        for (int i = _smoke.Count - 1; i >= 0; i--)
        {
            var s = _smoke[i];
            s.Life -= dt;
            s.Pos += s.Vel * dt;
            s.Size += 30f * dt;
            if (s.Life <= 0f) _smoke.RemoveAt(i);
        }

        if (!_boarded)
        {
            float dir = WalkDirection(new Vector2(_playerX, GroundY), true).X; // click the rocket, or A / D
            _playerX = Mathf.Clamp(_playerX + dir * WalkSpeed * Gm.PlayerSpeedMultiplier * dt, 80f, DoorX);
            if (dir != 0f) _walk += dt * 12f;
            if (_playerX >= DoorX - 1f)
            {
                _boarded = true;
                Sound(Sfx.Ding);
                Say("Everyone on board! 3, 2, 1...");
            }
            return;
        }

        if (_liftoff < 0f)
        {
            _countdown -= dt;
            int n = Mathf.CeilToInt(_countdown);
            if (n != _lastBeep && n > 0)
            {
                _lastBeep = n;
                Sound(Sfx.Beep);
            }
            if (_countdown <= 0f)
            {
                _liftoff = 0f;
                Sound(Sfx.Launch, -3f);
            }
            return;
        }

        _liftoff += dt;
        _rocketY -= (60f + 500f * _liftoff) * dt;
        for (int k = 0; k < 3; k++)
            _smoke.Add(new Smoke
            {
                Pos = new Vector2(RocketX + Rng.RandfRange(-20, 20), GroundY + _rocketY),
                Vel = new Vector2(Rng.RandfRange(-140, 140), Rng.RandfRange(-20, 40)),
                Life = Rng.RandfRange(0.8f, 1.5f),
                Size = Rng.RandfRange(10, 20),
            });
        if (_liftoff >= LiftoffSeconds)
            Win();
    }

    // ==================================================================
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.06f));
        Art.Stars(this, new Rect2(0, 160, Size.X, 420), 120, Elapsed);
        // Earth waiting for you
        DrawCircle(new Vector2(260, 290), 60, new Color(0.25f, 0.5f, 0.95f));
        DrawCircle(new Vector2(240, 275), 20, new Color(0.3f, 0.75f, 0.4f));
        DrawCircle(new Vector2(285, 310), 15, new Color(0.3f, 0.75f, 0.4f));
        Art.TextCentered(this, "HOME", 260, 375, 16, Art.Cyan);

        // Ground + launch pad
        DrawRect(new Rect2(0, GroundY, Size.X, Size.Y - GroundY), new Color(0.3f, 0.3f, 0.34f));
        DrawRect(new Rect2(940, GroundY - 12, 240, 12), Art.Steel);
        for (float x = 120; x < 1000; x += 90)
            DrawRect(new Rect2(x, GroundY + 14, 50, 6), new Color(Art.Yellow, 0.6f)); // runway arrows

        // Launch tower with the launch-window clock
        DrawRect(new Rect2(1150, 300, 20, GroundY - 300), Art.Orange);
        for (float y = 320; y < GroundY; y += 40)
            DrawLine(new Vector2(1150, y), new Vector2(1170, y + 30), Art.Orange, 3f);
        if (LevelController.RocketClockOn)
        {
            int t = Mathf.CeilToInt(Gm.RocketTimeLeft);
            Art.Panel(this, new Rect2(860, 190, 380, 74), Gm.RocketTimeLeft < 30f ? Art.Red : Art.Yellow);
            Art.TextCentered(this, "ROCKET LAUNCH WINDOW", 1050, 214, 15, Art.White);
            Art.TextCentered(this, $"{t / 60:00}:{t % 60:00}", 1050, 252, 34, Gm.RocketTimeLeft < 30f ? Art.Red : Art.Yellow);
        }
        else
        {
            Art.Panel(this, new Rect2(860, 190, 380, 60), Art.Green);
            Art.TextCentered(this, "READY TO GO HOME!", 1050, 226, 14, Art.Green);
        }

        // Smoke behind the rocket
        foreach (var s in _smoke)
            DrawCircle(s.Pos, s.Size, new Color(0.75f, 0.75f, 0.8f, Mathf.Clamp(s.Life * 0.5f, 0, 0.5f)));

        DrawRocket(new Vector2(RocketX, GroundY - 12 + _rocketY));

        if (!_boarded)
            Art.Astronaut(this, new Vector2(_playerX, GroundY), 1f, _walk);

        // Countdown
        if (_boarded && _liftoff < 0f)
            Art.TextCentered(this, $"{Mathf.CeilToInt(_countdown)}", 640, 420, 120, Art.Yellow);
        else if (_liftoff >= 0f)
            Art.TextCentered(this, "LIFTOFF!", 640, 420, 80, Art.Green);

        Art.Hint(this, _boarded ? "GOING HOME TO EARTH!" : "CLICK THE ROCKET TO RUN TO IT!");
        DrawFloaters();
    }

    private void DrawRocket(Vector2 b)
    {
        // flames
        if (_liftoff >= 0f)
        {
            float f = 1f + 0.3f * Mathf.Sin(Elapsed * 40f);
            DrawColoredPolygon(new[] { b + new Vector2(-22, 0), b + new Vector2(22, 0), b + new Vector2(0, 90 * f) }, Art.Orange);
            DrawColoredPolygon(new[] { b + new Vector2(-12, 0), b + new Vector2(12, 0), b + new Vector2(0, 55 * f) }, Art.Yellow);
        }
        // fins
        DrawColoredPolygon(new[] { b + new Vector2(-34, -10), b + new Vector2(-34, -80), b + new Vector2(-70, 0) }, Art.Red);
        DrawColoredPolygon(new[] { b + new Vector2(34, -10), b + new Vector2(34, -80), b + new Vector2(70, 0) }, Art.Red);
        // body
        DrawRect(new Rect2(b + new Vector2(-34, -260), new Vector2(68, 260)), Art.White);
        DrawColoredPolygon(new[] { b + new Vector2(-34, -260), b + new Vector2(34, -260), b + new Vector2(0, -340) }, Art.Red);
        DrawCircle(b + new Vector2(0, -190), 16f, Art.Steel);
        DrawCircle(b + new Vector2(0, -190), 11f, _boarded ? Art.Yellow : Art.Cyan);
        // door
        DrawRect(new Rect2(b + new Vector2(-16, -70), new Vector2(32, 58)), _boarded ? Art.Steel : new Color(0.15f, 0.2f, 0.3f));
        if (!_boarded)
            Art.TextCentered(this, "DOOR", b.X, b.Y - 80, 13, Art.Yellow);
        Art.TextCentered(this, "EARTH", b.X, b.Y - 120, 14, Art.Blue);
    }
}

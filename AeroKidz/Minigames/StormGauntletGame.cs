using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// MISSION 3 - DAY 9: THE 3-MINUTE STORM (final survival)
/// The base shakes. Oxygen, Power and Shield drain at speeds taken from REAL NASA DONKI data (NasaData.cs):
///   Power  <- Kp index (geomagnetic storm)     Shield <- CME speed (km/s)     Oxygen <- proton flux
/// Every ~25 s a data "event" knocks one bar down hard and Nova reads the NASA line.
/// Run (WASD / arrows) into a room and stand on its glowing pad to start that room's repair:
///   OXYGEN ROOM -> PipeTask      POWER ROOM -> SolarPuzzleTask      SHIELD ROOM -> ShieldOrbitTask
/// "Keep Studying the Sun": the storm knocks the Shield to 0% - fix it within ShieldGraceSeconds!
/// WIN: survive until the timer hits 0:00.
/// </summary>
public partial class StormGauntletGame : MinigameBase
{
    // ---- Tuning ----
    public const float ShieldGraceSeconds = 25f;
    /// <summary>The storm lasts 2 minutes (shorter, but the bars drop faster - see NasaData.StormSpeedUp).</summary>
    public const float DurationSeconds = 120f;
    private const float MoveSpeed = 330f;
    private const float PadRadius = 36f;
    private const float PadHoldTime = 0.35f;
    private const float EventMin = 14f, EventMax = 20f;
    private const float EventHitMin = 10f, EventHitMax = 16f;

    private static readonly Rect2 Floor = new(60, 175, 1160, 525);

    private enum Room { Oxygen, Power, Shield }
    // Same layout as the outpost sketch: Oxygen top-left, Power far left, Shield bottom-left, Hub in the middle.
    private static readonly Rect2[] Rooms =
    {
        new(260, 195, 290, 195),  // Oxygen
        new(70, 400, 260, 180),   // Power
        new(370, 495, 280, 195),  // Shield
    };
    private static readonly Vector2 Hub = new(860, 420);
    private static readonly string[] RoomNames = { "OXYGEN ROOM", "POWER ROOM", "SHIELD ROOM" };
    private static readonly Color[] RoomColors = { Art.Cyan, Art.Orange, Art.Magenta };

    private NasaData Nasa => NasaData.Instance;

    private Vector2 _player = new(860, 460);
    private float _walk;
    private bool _faceLeft;
    private float _padTimer;
    private int _padLock = -1;           // must step off this pad before it opens again
    private RoomTask _task;

    public override bool HasOverlay => _task != null;

    private float _eventTimer;
    private float _flickerTimer;
    private float _shake;
    private string _alert = "";
    private float _alertTime;
    private float _graceLeft = -1f;

    private sealed class Debris { public Vector2 Pos, Vel; public float Life; }
    private readonly List<Debris> _debris = new();

    protected override void Setup()
    {
        TimeLeft = DurationSeconds;
        TimerIsGoal = true; // survive until 0:00
        AllowRoomShortcuts = false; // here you RUN to the rooms yourself
        DrainPerSecond = new Vector3(Nasa.OxygenDrain, Nasa.PowerDrain, Nasa.ShieldDrain);
        _eventTimer = Rng.RandfRange(12f, 16f);
        _flickerTimer = 6f;

        if (Gm.StormBreaksShield)
        {
            Gm.KnockOutShield();
            _graceLeft = ShieldGraceSeconds;
            ShowAlert("SHIELD AT 0%! GO TO THE SHIELD ROOM!");
        }
    }

    protected override void Tick(float dt)
    {
        _shake = Mathf.Max(0.25f, _shake - dt * 2f); // the ground never stops shaking a little
        _alertTime = Mathf.Max(0f, _alertTime - dt);
        UpdateDebris(dt);

        // ---- Shield grace period ----
        if (_graceLeft >= 0f)
        {
            if (!Gm.ShieldOffline)
            {
                _graceLeft = -1f;
                SayAloud("Shield is back on! Nice!");
            }
            else
            {
                _graceLeft -= dt;
                if (_graceLeft <= 0f)
                {
                    Fail("The Shields stayed at 0% and the solar storm hit the base.");
                    return;
                }
            }
        }

        // ---- NASA data events ----
        _eventTimer -= dt;
        if (_eventTimer <= 0f)
        {
            _eventTimer = Rng.RandfRange(EventMin, EventMax);
            DataEvent();
        }

        // X-class flare: the power flickers
        if (Nasa.IsXClass)
        {
            _flickerTimer -= dt;
            if (_flickerTimer <= 0f)
            {
                _flickerTimer = Rng.RandfRange(6f, 10f);
                Gm.ChangeResources(0f, -3f, 0f);
                Pop(Rooms[(int)Room.Power].GetCenter(), "FLICKER -3", Art.Orange);
            }
        }

        // ---- Moving (not while repairing) ----
        if (_task != null)
            return;

        var dir = WalkDirection(_player, false); // click where to run, or WASD
        if (dir != Vector2.Zero)
        {
            _player += dir * MoveSpeed * Gm.PlayerSpeedMultiplier * dt;
            _player = _player.Clamp(Floor.Position + new Vector2(25, 90), Floor.End - new Vector2(25, 5));
            _walk += dt * 12f;
            if (dir.X != 0) _faceLeft = dir.X < 0;
        }

        // ---- Pads ----
        int onPad = -1;
        for (int r = 0; r < Rooms.Length; r++)
            if (_player.DistanceTo(Rooms[r].GetCenter() + new Vector2(0, 40)) < PadRadius)
                onPad = r;

        if (onPad != _padLock) _padLock = -1;
        if (onPad >= 0 && onPad != _padLock)
        {
            _padTimer += dt;
            if (_padTimer >= PadHoldTime)
                OpenTask((Room)onPad);
        }
        else
        {
            _padTimer = 0f;
        }
    }

    private void DataEvent()
    {
        var room = (Room)Rng.RandiRange(0, 2);
        float hit = Rng.RandfRange(EventHitMin, EventHitMax);
        _shake = 1.2f;
        Sound(Sfx.Alarm, -10f);
        for (int k = 0; k < 25; k++)
            _debris.Add(new Debris
            {
                Pos = new Vector2(Rng.RandfRange(80, 1200), 180),
                Vel = new Vector2(Rng.RandfRange(-40, 40), Rng.RandfRange(150, 400)),
                Life = Rng.RandfRange(0.8f, 1.6f),
            });

        switch (room)
        {
            case Room.Oxygen:
                Gm.ChangeResources(-hit, 0f, 0f);
                ShowAlert($"PROTON FLUX! OXYGEN -{hit:0}");
                SayAloud(Nasa.OxygenLine);
                break;
            case Room.Power:
                Gm.ChangeResources(0f, -hit, 0f);
                ShowAlert($"Kp STORM! POWER -{hit:0}");
                SayAloud(Nasa.PowerLine);
                break;
            default:
                Gm.ChangeResources(0f, 0f, -hit);
                ShowAlert($"CME HIT! SHIELD -{hit:0}");
                SayAloud(Nasa.ShieldLine);
                break;
        }
    }

    private void ShowAlert(string text)
    {
        _alert = text;
        _alertTime = 3f;
    }

    private void OpenTask(Room room)
    {
        _padTimer = 0f;
        _task = room switch
        {
            Room.Oxygen => new PipeTask(),
            Room.Power => new SolarPuzzleTask(),
            _ => new ShieldOrbitTask(),
        };
        _padLock = (int)room;
        _task.Done += OnTaskDone;
        AddChild(_task);
    }

    private void OnTaskDone(bool success)
    {
        if (_task != null)
        {
            _task.Done -= OnTaskDone;
            _task.QueueFree();
            _task = null;
        }
        if (success)
        {
            Gm.AddScience(50);
            Sound(Sfx.Ding, -8f);
        }
    }

    protected override void OnTimeUp() => Win(); // survived the storm!

    private void UpdateDebris(float dt)
    {
        for (int i = _debris.Count - 1; i >= 0; i--)
        {
            var d = _debris[i];
            d.Life -= dt;
            d.Pos += d.Vel * dt;
            if (d.Life <= 0f) _debris.RemoveAt(i);
        }
    }

    private float RoomValue(int r) => r switch { 0 => Gm.Oxygen, 1 => Gm.Power, _ => Gm.Shield };

    // ==================================================================
    public override void _Draw()
    {
        var shake = new Vector2(Rng.RandfRange(-1f, 1f), Rng.RandfRange(-1f, 1f)) * 5f * _shake;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.06f, 0.02f, 0.04f));
        DrawSetTransform(shake);

        // Floor + corridors
        DrawRect(Floor, new Color(0.17f, 0.18f, 0.24f));
        for (float x = Floor.Position.X; x < Floor.End.X; x += 58)
            DrawLine(new Vector2(x, Floor.Position.Y), new Vector2(x, Floor.End.Y), new Color(0.22f, 0.23f, 0.3f), 1f);
        float red = 0.5f + 0.5f * Mathf.Sin(Elapsed * 4f);
        DrawRect(Floor, new Color(1f, 0.1f, 0.05f, 0.05f + 0.07f * red));

        // Corridors from the hub to each room + the hub itself
        foreach (var room in Rooms)
        {
            DrawLine(Hub, room.GetCenter(), new Color(0.3f, 0.32f, 0.4f), 60f);
            DrawLine(Hub, room.GetCenter(), new Color(0.4f, 0.43f, 0.52f), 44f);
        }
        DrawCircle(Hub, 95f, new Color(0.3f, 0.32f, 0.4f));
        DrawCircle(Hub, 85f, new Color(0.22f, 0.24f, 0.32f));
        Art.TextCentered(this, "MAIN HUB", Hub.X, Hub.Y - 50, 16, Art.Steel);

        // Rooms
        for (int r = 0; r < Rooms.Length; r++)
        {
            var rect = Rooms[r];
            var col = RoomColors[r];
            float v = RoomValue(r);
            bool low = v < 30f || (r == 2 && Gm.ShieldOffline);

            DrawRect(rect, new Color(0.15f, 0.16f, 0.22f));
            DrawRect(rect, new Color(col, 0.12f));
            DrawRect(rect, low && Mathf.Sin(Elapsed * 10f) > 0 ? Art.Red : col, false, 4f);
            Art.Text(this, RoomNames[r], rect.Position + new Vector2(14, 28), 18, col);

            // machine
            DrawMachine(r, rect.GetCenter() + new Vector2(0, -25), col);

            // pad
            var pad = rect.GetCenter() + new Vector2(0, 40);
            float glow = 0.5f + 0.5f * Mathf.Sin(Elapsed * 5f + r);
            DrawCircle(pad, PadRadius, new Color(col, 0.2f + 0.25f * glow));
            DrawArc(pad, PadRadius, 0, Mathf.Tau, 32, col, 3f);
            if (_padLock == r)
                Art.TextCentered(this, "STEP OFF, THEN BACK ON", pad.X, pad.Y + 58, 12, Art.Steel);

            // room bar
            var bar = new Rect2(rect.Position.X + 14, rect.End.Y - 26, rect.Size.X - 28, 14);
            DrawRect(bar, new Color(0.02f, 0.03f, 0.06f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * v / 100f, bar.Size.Y)), col);
            if (low)
                Art.TextCentered(this, r == 2 && Gm.ShieldOffline ? "!! OFFLINE !!" : "!! LOW !!", rect.GetCenter().X, rect.Position.Y - 6, 16, Art.Red);
        }

        // Player
        Art.Astronaut(this, _player, 0.7f, _walk, _faceLeft);
        if (_padTimer > 0f)
            DrawArc(_player + new Vector2(0, -80), 14f, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (_padTimer / PadHoldTime), 20, Art.White, 3f);

        // Debris
        foreach (var d in _debris)
            DrawLine(d.Pos, d.Pos - d.Vel * 0.04f, new Color(1f, 0.6f, 0.2f, Mathf.Clamp(d.Life, 0, 1)), 3f);

        DrawSetTransform(Vector2.Zero);

        // Data panel (what the NASA numbers are doing right now)
        Art.Panel(this, new Rect2(960, 178, 300, 92), Art.Yellow, 0.85f); // top-right: never under Nova's bubble
        Art.Text(this, "NASA STORM DATA", new Vector2(974, 200), 15, Art.Yellow);
        Art.Text(this, $"Kp INDEX {Nasa.KpIndex:0.#}  -> POWER -{Nasa.PowerDrain:0.00}%/s", new Vector2(974, 220), 13, Art.Orange);
        Art.Text(this, $"CME {Nasa.CmeSpeedKms} km/s -> SHIELD -{Nasa.ShieldDrain:0.00}%/s", new Vector2(974, 238), 13, Art.Magenta);
        Art.Text(this, $"PROTON FLUX {(Nasa.ProtonFluxHigh ? "HIGH" : "LOW")} -> O2 -{Nasa.OxygenDrain:0.00}%/s", new Vector2(974, 256), 13, Art.Cyan);

        if (_graceLeft >= 0f)
        {
            Art.Panel(this, new Rect2(390, 170, 500, 44), Art.Red);
            Art.TextCentered(this, $"SHIELD OFFLINE! FIX IT IN {Mathf.CeilToInt(_graceLeft)}s!", 640, 200, 22,
                Mathf.Sin(Elapsed * 10f) > 0 ? Art.Red : Art.White);
        }
        else if (_alertTime > 0f)
        {
            Art.Panel(this, new Rect2(340, 170, 600, 44), Art.Red);
            Art.TextCentered(this, _alert, 640, 200, 22, Mathf.Sin(Elapsed * 10f) > 0 ? Art.Red : Art.Yellow);
        }

        Art.Hint(this, "CLICK TO RUN - STAND ON A GLOWING PAD TO FIX IT!");
        DrawFloaters();
    }

    private void DrawMachine(int r, Vector2 c, Color col)
    {
        // machine pictures from the craftpix energy icon pack
        float pulse = 1f + 0.04f * Mathf.Sin(Elapsed * 4f + r);
        DrawCircle(c, 44f, new Color(col, 0.15f));
        string pic = r switch
        {
            0 => "res://AeroKidz/Assets/energy/energy_10.png",   // oxygen cells
            1 => "res://AeroKidz/Assets/energy/energy_08.png",   // solar panel
            _ => "res://AeroKidz/Assets/energy/energy_36.png",   // shield generator
        };
        Art.Picture(this, pic, c, 84f * pulse);
    }
}

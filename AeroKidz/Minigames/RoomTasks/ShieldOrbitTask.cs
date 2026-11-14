using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// SHIELD ROOM - SPIN THE SHIELD (pictures: craftpix.net "Free Space Shooter Game Objects").
/// A shield moves around the moon base in a circle. Meteors fly in from every side.
/// Point the shield at them (mouse, or A/D) to block them. A meteor that gets past hits the base: -1 life.
///
/// CHALLENGE:
///   * it gets FASTER after every block, and meteors come more often
///   * BIG meteors need 2 blocks (the first block cracks them and knocks them back)
///   * FAST comets are small and quick
///   * after a few blocks, meteors come from 2 sides at once
///   * HEARTS: do NOT block them - let them reach the base for +1 life!
/// Block BlocksToWin meteors to finish (+ bonus). 3 lives.
/// </summary>
public partial class ShieldOrbitTask : RoomTask
{
    public const float ShieldPerBlock = 4f;
    public const float FinishBonus = 15f;
    private const int BlocksToWin = 10;
    private const int Lives = 3, MaxLives = 5;

    private static readonly Vector2 Ship = new(640, 470);
    private const float OrbitRadius = 105f;
    private const float ShieldHalfArc = 0.68f;   // radians each side (smaller = harder)
    private const float SpawnRadius = 230f;
    private const float KeyTurnSpeed = 4.5f;
    private const float BaseSpeed = 80f, SpeedPerBlock = 3f;
    private const float StartInterval = 1.6f, MinInterval = 0.95f;
    private const int DoubleAttackAfter = 7;

    private const string Dir = "res://AeroKidz/Assets/space/";
    private const string BasePic = "res://AeroKidz/Assets/energy/energy_36.png";

    private enum Kind { Normal, Big, Comet, Heart }
    private sealed class Meteor
    {
        public Kind Kind; public float Angle, Radius, Speed, Spin, SpinSpeed; public int Hits; public int Sprite;
    }
    private readonly List<Meteor> _meteors = new();

    private sealed class Boom { public Vector2 Pos; public float T; public bool Ring; }
    private readonly List<Boom> _booms = new();

    private float _shieldAngle = -Mathf.Pi / 2f;
    private float _spawnTimer = 1.0f;
    private int _blocked;
    private int _lives = Lives;
    private float _hitFlash;
    private float _endTimer = -1f;
    private bool _won;

    protected override string Title => "SHIELD ROOM: BLOCK THE METEORS";
    protected override Color Accent => Art.Magenta;
    protected override string HelpText => $"Turn the shield with the mouse. Block {BlocksToWin}! Let HEARTS through!";

    private float Speed => BaseSpeed + SpeedPerBlock * _blocked;
    private float Interval => Mathf.Max(MinInterval, StartInterval - 0.05f * _blocked);

    protected override void Tick(float dt)
    {
        _hitFlash = Mathf.Max(0f, _hitFlash - dt * 2f);
        for (int i = _booms.Count - 1; i >= 0; i--)
        {
            _booms[i].T += dt;
            if (_booms[i].T > 0.6f) _booms.RemoveAt(i);
        }

        if (_endTimer >= 0f)
        {
            _endTimer -= dt;
            if (_endTimer < 0f)
            {
                if (_won) Reward(0f, 0f, FinishBonus);
                Finish(_won);
            }
            return;
        }

        float dir = (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1f : 0f)
                  - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1f : 0f);
        _shieldAngle += dir * KeyTurnSpeed * dt;

        // ---- Spawning (gets harder) ----
        _spawnTimer -= dt;
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = Interval * Rng.RandfRange(0.8f, 1.2f);
            float a = Rng.RandfRange(0, Mathf.Tau);
            Spawn(a);
            if (_blocked >= DoubleAttackAfter && Rng.Randf() < 0.25f)
                Spawn(a + Mathf.Pi + Rng.RandfRange(-0.4f, 0.4f)); // a second one from the other side!
        }

        for (int i = _meteors.Count - 1; i >= 0; i--)
        {
            var m = _meteors[i];
            float before = m.Radius;
            m.Radius -= m.Speed * dt;
            m.Spin += m.SpinSpeed * dt;

            // crossing the shield ring this frame?
            if (before > OrbitRadius && m.Radius <= OrbitRadius &&
                Mathf.Abs(Mathf.AngleDifference(_shieldAngle, m.Angle)) <= ShieldHalfArc)
            {
                if (m.Kind == Kind.Heart)
                {
                    Pop(Pos(m.Angle, OrbitRadius + 30), "OOPS! LET HEARTS IN!", Art.Red);
                    Sound(Sfx.Bad, -10f);
                    _meteors.RemoveAt(i);
                    continue;
                }
                _booms.Add(new Boom { Pos = Pos(m.Angle, OrbitRadius), Ring = true });
                if (m.Kind == Kind.Big && m.Hits == 0)
                {
                    m.Hits = 1;                       // cracked! knocked back, comes again faster
                    m.Radius = OrbitRadius + 70f;
                    m.Speed *= 1.25f;
                    Sound(Sfx.Pop);
                    Pop(Pos(m.Angle, OrbitRadius + 40), "CRACK! AGAIN!", Art.Yellow);
                    continue;
                }
                _blocked++;
                Gm.ChangeResources(0f, 0f, ShieldPerBlock);
                Sound(Sfx.Pop);
                Pop(Pos(m.Angle, OrbitRadius + 34), $"+{ShieldPerBlock:0} SHIELD", Art.Magenta);
                _meteors.RemoveAt(i);
                if (_blocked >= BlocksToWin)
                {
                    _won = true;
                    _endTimer = 0.9f;
                    _meteors.Clear();
                    Sound(Sfx.Ding);
                    Pop(Ship + new Vector2(0, -150), "SHIELD STRONG!", Art.Green);
                }
                continue;
            }

            if (m.Radius <= 38f)
            {
                _meteors.RemoveAt(i);
                if (m.Kind == Kind.Heart)
                {
                    _lives = Mathf.Min(MaxLives, _lives + 1);
                    Sound(Sfx.Ding, -8f);
                    Pop(Ship + new Vector2(0, -70), "+1 LIFE!", Art.Green);
                    continue;
                }
                _lives -= 1;
                _hitFlash = 1f;
                _booms.Add(new Boom { Pos = Pos(m.Angle, 44f) });
                Sound(Sfx.Boom);
                Pop(Ship + new Vector2(0, -70), "OUCH! -1 LIFE", Art.Red);
                if (_lives <= 0)
                {
                    _won = false;
                    _endTimer = 0.9f;
                }
            }
        }
    }

    private void Spawn(float angle)
    {
        float r = Rng.Randf();
        var kind = r < 0.12f ? Kind.Heart
                 : _blocked >= 4 && r < 0.24f ? Kind.Big
                 : _blocked >= 3 && r < 0.36f ? Kind.Comet
                 : Kind.Normal;
        float speed = Speed * kind switch { Kind.Comet => 1.5f, Kind.Big => 0.75f, Kind.Heart => 0.8f, _ => 1f };
        _meteors.Add(new Meteor
        {
            Kind = kind,
            Angle = angle,
            Radius = SpawnRadius,
            Speed = speed * Rng.RandfRange(0.9f, 1.1f),
            SpinSpeed = Rng.RandfRange(-3f, 3f),
            Sprite = Rng.RandiRange(1, 10),
        });
    }

    protected override void OnInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && mm.Position.DistanceTo(Ship) > 10f)
            _shieldAngle = (mm.Position - Ship).Angle();
    }

    private static Vector2 Pos(float angle, float radius) => Ship + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

    protected override void DrawTask()
    {
        // space inside the panel
        var sky = new Rect2(Ship.X - 300, 252, 600, 438); // starts below the help line
        Art.RoundRect(this, sky, 18, new Color(0.03f, 0.04f, 0.12f));
        Art.Stars(this, sky.Grow(-10), 60, Elapsed, 21);
        DrawArc(Ship, OrbitRadius, 0, Mathf.Tau, 64, new Color(Art.Magenta, 0.18f), 2f);
        DrawArc(Ship, SpawnRadius, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.05f), 1f);

        // the moon base (energy icon) + red flash when hit
        Art.Picture(this, BasePic, Ship, 96f, _hitFlash > 0 ? new Color(1f, 0.5f, 0.5f) : Colors.White);

        // meteors / comets / hearts
        foreach (var m in _meteors)
        {
            var p = Pos(m.Angle, m.Radius);
            switch (m.Kind)
            {
                case Kind.Heart:
                    Art.Picture(this, Dir + "HP_Bonus.png", p, 44f + 4f * Mathf.Sin(Elapsed * 8f));
                    break;
                case Kind.Comet:
                    var back = Pos(m.Angle, m.Radius + 46);
                    DrawLine(p, back, new Color(1f, 0.6f, 0.15f, 0.8f), 12f);
                    DrawLine(p, back, new Color(1f, 0.9f, 0.4f, 0.9f), 5f);
                    Art.PictureRotated(this, Dir + $"Meteor_{m.Sprite:D2}.png", p, 30f, m.Spin);
                    break;
                default:
                    float size = m.Kind == Kind.Big ? 70f : 44f;
                    Art.PictureRotated(this, Dir + $"Meteor_{m.Sprite:D2}.png", p, size, m.Spin,
                        m.Hits > 0 ? new Color(1f, 0.6f, 0.5f) : Colors.White);
                    break;
            }
        }

        // the shield: a blue energy arc (picture) that faces outward
        var sp = Pos(_shieldAngle, OrbitRadius + 6);
        DrawArc(Ship, OrbitRadius, _shieldAngle - ShieldHalfArc, _shieldAngle + ShieldHalfArc, 20, new Color(0.4f, 0.75f, 1f, 0.35f), 18f);
        Art.PictureRotated(this, Dir + "Rocket_Effect_02.png", sp, 2f * OrbitRadius * Mathf.Sin(ShieldHalfArc) * 1.35f, _shieldAngle + Mathf.Pi / 2f);

        // explosions: animated frames for hits, a bright ring for blocks
        foreach (var b in _booms)
        {
            if (b.Ring)
                Art.Picture(this, Dir + "Explosion_02.png", b.Pos, 30f + 80f * b.T, new Color(1, 1, 1, 1f - b.T / 0.6f));
            else
            {
                int frame = Mathf.Clamp((int)(b.T / 0.6f * 9f), 0, 8);
                Art.Picture(this, Dir + $"Missile_1_Explosion_{frame:D3}.png", b.Pos, 120f);
            }
        }

        // score + lives (hearts)
        Art.Text(this, $"BLOCKED: {_blocked} / {BlocksToWin}", Box.Position + new Vector2(24, 122), 22, Art.Magenta, outline: 4);
        Art.Text(this, "LIVES:", Box.Position + new Vector2(24, 156), 18, Art.White);
        for (int i = 0; i < MaxLives; i++)
            Art.Picture(this, Dir + "HP_Bonus.png", Box.Position + new Vector2(112 + i * 32, 150), 28f,
                i < _lives ? Colors.White : new Color(0.25f, 0.25f, 0.3f, 0.6f));
        Art.Text(this, $"SPEED x{Speed / BaseSpeed:0.0}", Box.Position + new Vector2(24, 186), 15, Art.Orange);

        if (_hitFlash > 0f)
            DrawRect(Box, new Color(1f, 0.1f, 0.05f, 0.15f * _hitFlash));
    }
}

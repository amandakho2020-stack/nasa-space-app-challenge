using Godot;

namespace AeroKidz;

/// <summary>
/// MISSION 2: WALKING OUTSIDE THE OUTPOST (spacesuit walk)
/// The Core Drill is outside, so the Commander walks there across the Moon (A/D or arrows), with low-gravity hops.
///   Route.ToDrill     : from the outpost airlock (left) to the drill (right).
///   Route.BackToBase  : from the drill back toward the airlock - halfway you find the sparking CIRCUIT BOX.
/// The spacesuit uses Oxygen, and space radiation nibbles the Shield while you are outside.
/// </summary>
public partial class MoonWalkGame : MinigameBase
{
    public enum Route { ToDrill, BackToBase }

    // ---- Tuning ----
    private const float WalkSpeed = 260f;
    private const float GroundY = 610f;
    private const float AirlockX = 150f, DrillX = 1110f, CircuitBoxX = 600f;

    private readonly Route _route;
    private float _x;
    private float _walk;
    private bool _faceLeft;
    private float _arrived = -1f;

    public MoonWalkGame() : this(Route.ToDrill) { }
    public MoonWalkGame(Route route) { _route = route; }

    protected override void Setup()
    {
        TimeLeft = 60f;
        DrainPerSecond = new Vector3(0.45f, 0f, 0.1f); // suit oxygen + a little radiation
        _x = _route == Route.ToDrill ? AirlockX + 60f : DrillX - 70f;
        _faceLeft = _route == Route.BackToBase;
    }

    private float TargetX => _route == Route.ToDrill ? DrillX - 70f : CircuitBoxX + 70f;

    protected override void Tick(float dt)
    {
        if (_arrived >= 0f)
        {
            _arrived -= dt;
            if (_arrived < 0f) Win();
            return;
        }

        float dir = WalkDirection(new Vector2(_x, GroundY), true).X; // click / hold where to walk, or A / D
        _x = Mathf.Clamp(_x + dir * WalkSpeed * Gm.PlayerSpeedMultiplier * dt, AirlockX + 40f, DrillX - 40f);
        if (dir != 0f)
        {
            _walk += dt * 7f;
            _faceLeft = dir < 0f;
        }

        bool reached = _route == Route.ToDrill ? _x >= TargetX : _x <= TargetX;
        if (reached)
        {
            if (_route == Route.ToDrill)
            {
                Sound(Sfx.Ding, -8f);
                Pop(new Vector2(_x, GroundY - 150), "AT THE DRILL!", Art.Green);
            }
            else
            {
                Sound(Sfx.Zap);
                Pop(new Vector2(CircuitBoxX, GroundY - 150), "UH OH! BROKEN CIRCUIT!", Art.Orange);
                Say("Oh no! This circuit box is broken!");
            }
            _arrived = 1.4f;
        }
    }

    protected override void OnTimeUp() => Fail("Your spacesuit ran low on air out on the surface.");

    // ==================================================================
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.06f));
        Art.Stars(this, new Rect2(0, 160, Size.X, 330), 110, Elapsed);
        DrawCircle(new Vector2(980, 260), 38, new Color(0.25f, 0.5f, 0.95f)); // Earth in the sky
        DrawCircle(new Vector2(968, 250), 13, new Color(0.3f, 0.75f, 0.4f));

        // Rolling grey ground
        var ground = new Vector2[18];
        for (int i = 0; i <= 15; i++)
        {
            float x = i * 1280f / 15f;
            ground[i] = new Vector2(x, GroundY - 10f * Mathf.Sin(x * 0.01f));
        }
        ground[16] = new Vector2(1280, 720);
        ground[17] = new Vector2(0, 720);
        DrawColoredPolygon(ground, new Color(0.42f, 0.42f, 0.45f));
        Art.Ellipse(this, new Vector2(380, 650), new Vector2(70, 12), new Color(0.33f, 0.33f, 0.36f));
        Art.Ellipse(this, new Vector2(860, 670), new Vector2(110, 16), new Color(0.33f, 0.33f, 0.36f));

        // Outpost dome with the airlock (left)
        Art.HalfDisc(this, new Vector2(AirlockX - 40, GroundY), 140, new Color(0.75f, 0.8f, 0.88f));
        Art.HalfDisc(this, new Vector2(AirlockX - 40, GroundY), 140, new Color(0.6f, 0.9f, 1f, 0.15f));
        DrawRect(new Rect2(AirlockX + 50, GroundY - 80, 40, 80), Art.Orange);
        Art.TextCentered(this, "AIRLOCK", AirlockX + 70, GroundY - 92, 13, Art.Orange);
        Art.TextCentered(this, "OUTPOST", AirlockX - 40, GroundY - 40, 18, Art.Navy);

        // Core drill (right)
        DrawLine(new Vector2(DrillX - 50, GroundY), new Vector2(DrillX, GroundY - 150), Art.Orange, 7f);
        DrawLine(new Vector2(DrillX + 50, GroundY), new Vector2(DrillX, GroundY - 150), Art.Orange, 7f);
        DrawRect(new Rect2(DrillX - 8, GroundY - 140, 16, 150), Art.Steel);
        Art.TextCentered(this, "CORE DRILL", DrillX, GroundY - 165, 14, Art.Orange);

        // Power cable from the drill to the outpost, through the circuit box
        DrawLine(new Vector2(AirlockX + 90, GroundY - 4), new Vector2(DrillX, GroundY - 4), new Color(0.15f, 0.15f, 0.18f), 5f);
        var box = new Rect2(CircuitBoxX - 26, GroundY - 50, 52, 46);
        DrawRect(box, new Color(0.55f, 0.58f, 0.65f));
        DrawRect(box, Art.Yellow, false, 2f);
        if (_route == Route.BackToBase)
        {
            for (int k = 0; k < 5; k++)
            {
                float a = Elapsed * 13f + k * 1.3f;
                var s = box.GetCenter() + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (20 + 10 * Mathf.Sin(Elapsed * 20f + k));
                DrawLine(box.GetCenter(), s, Art.Yellow, 2f);
            }
            Art.TextCentered(this, "!", box.GetCenter().X, box.Position.Y - 12, 28, Art.Red);
        }

        // Footprints behind you
        float from = _route == Route.ToDrill ? AirlockX + 60f : _x;
        float to = _route == Route.ToDrill ? _x : DrillX - 70f;
        for (float fx = from; fx < to; fx += 34f)
            Art.Ellipse(this, new Vector2(fx, GroundY + 8 + ((int)(fx / 34f) % 2) * 6), new Vector2(7, 3), new Color(0.3f, 0.3f, 0.33f));

        // Commander, hopping in low gravity
        float hop = Mathf.Abs(Mathf.Sin(_walk)) * 18f;
        Art.Astronaut(this, new Vector2(_x, GroundY - hop), 1f, _walk * 0.6f, _faceLeft);

        // Distance marker
        float left = Mathf.Abs(TargetX - _x);
        Art.Panel(this, new Rect2(1010, 170, 250, 44), Art.Cyan);
        Art.Text(this, _route == Route.ToDrill ? $"TO THE DRILL: {left / 10f:0} m" : $"TO THE OUTPOST: {left / 10f:0} m",
            new Vector2(1024, 199), 17, Art.Cyan);

        Art.Hint(this, _route == Route.ToDrill
            ? "CLICK THE DRILL TO WALK THERE"
            : "CLICK THE BASE TO WALK BACK");
        DrawFloaters();
    }
}

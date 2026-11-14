using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// BASE CLASS FOR EVERY MINIGAME.
///
/// A minigame is a full-screen Control that LevelController creates in code (no .tscn needed)
/// and adds to %MinigameContainer. It:
///   * sets TimeLeft (shown in the HUD) and DrainPerSecond (global drains, applied by LevelController)
///   * calls Win() or Fail("reason") when done -> emits Finished(won, reason)
///   * draws itself in _Draw() using the Art helpers
///
/// To make a new minigame: subclass this, override Setup(), Tick(dt), _Draw(), and optionally
/// OnTimeUp() and _GuiInput(). Then return it from LevelController.CreateMinigame().
/// </summary>
public partial class MinigameBase : Control
{
    [Signal] public delegate void FinishedEventHandler(bool won, string reason);

    /// <summary>Ask Nova to say something (LevelController forwards it to the pop-up robot).</summary>
    [Signal] public delegate void NovaLineEventHandler(string message, bool speak);

    /// <summary>true while this game shows its own pop-up panel (e.g. a repair in the storm) - Nova hides then.</summary>
    public virtual bool HasOverlay => false;

    /// <summary>The mission's hazard should pop up NOW, in the middle of this game (Mission 1 water game).</summary>
    [Signal] public delegate void HazardReadyEventHandler();

    /// <summary>false = hide the O2 / POWER / SHIELD room buttons during this game.</summary>
    public bool AllowRoomShortcuts { get; protected set; } = true;

    /// <summary>Seconds left. When it reaches 0, OnTimeUp() is called.</summary>
    public float TimeLeft { get; protected set; } = 60f;

    /// <summary>False = this minigame has no own timer (the HUD shows the rocket clock only).</summary>
    public bool ShowTimer { get; protected set; } = true;

    /// <summary>
    /// true = the timer is HOW you win (Day 9: survive 3:00), so it keeps running even when
    /// GameManager.TimersEnabled is false. Every other countdown is switched off then.
    /// </summary>
    protected bool TimerIsGoal { get; set; }

    /// <summary>Global drain in % per second: X = Oxygen, Y = Power, Z = Shield. Applied by LevelController.</summary>
    public Vector3 DrainPerSecond { get; protected set; } = Vector3.Zero;

    public bool IsDone { get; private set; }

    protected GameManager Gm => GameManager.Instance;

    /// <summary>Seconds since the minigame started (use it for animation).</summary>
    protected float Elapsed;

    protected readonly RandomNumberGenerator Rng = new();

    // Little "+1" / "MISS!" texts that float up and fade.
    private sealed class FloatText { public Vector2 Pos; public string Text; public Color Color; public float Life; }
    private readonly List<FloatText> _floaters = new();

    // ==================================================================
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // anchors AND size: fill the whole screen
        MouseFilter = MouseFilterEnum.Stop;
        Rng.Randomize();
        Setup();
        if (!GameManager.TimersEnabled && !TimerIsGoal)
            ShowTimer = false; // no time limit
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Elapsed += dt;
        _clickPulse = Mathf.Max(0f, _clickPulse - dt);

        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            _floaters[i].Life -= dt;
            _floaters[i].Pos += new Vector2(0, -45f * dt);
            if (_floaters[i].Life <= 0f) _floaters.RemoveAt(i);
        }

        if (!IsDone)
        {
            Tick(dt);
            if (!IsDone && ShowTimer)
            {
                TimeLeft -= dt;
                if (TimeLeft <= 0f)
                {
                    TimeLeft = 0f;
                    OnTimeUp();
                }
            }
        }

        QueueRedraw();
    }

    // ==================================================================
    // OVERRIDE THESE
    // ==================================================================
    protected virtual void Setup() { }
    protected virtual void Tick(float dt) { }
    protected virtual void OnTimeUp() => Fail("Time ran out before the job was finished.");

    // ==================================================================
    // HELPERS
    // ==================================================================
    protected void Win()
    {
        if (IsDone) return;
        IsDone = true;
        EmitSignal(SignalName.Finished, true, "");
    }

    protected void Fail(string reason)
    {
        if (IsDone) return;
        IsDone = true;
        EmitSignal(SignalName.Finished, false, reason);
    }

    /// <summary>Make Nova pop up and say this line.</summary>
    protected void Say(string message) => EmitSignal(SignalName.NovaLine, message, false);

    /// <summary>Nova shows this line AND reads it out loud (only the Mission 3 storm uses this).</summary>
    protected void SayAloud(string message) => EmitSignal(SignalName.NovaLine, message, true);

    /// <summary>Play a sound effect (see Sfx.cs).</summary>
    protected void Sound(string name, float volumeDb = -6f) => Sfx.Play(this, name, volumeDb);

    /// <summary>Spawn a floating text, e.g. Pop(pos, "+1", Art.Cyan).</summary>
    protected void Pop(Vector2 pos, string text, Color color)
    {
        _floaters.Add(new FloatText { Pos = pos, Text = text, Color = color, Life = 1.1f });
    }

    /// <summary>Call at the END of your _Draw() so floating texts appear on top.</summary>
    protected void DrawFloaters()
    {
        foreach (var f in _floaters)
            Art.TextCentered(this, f.Text, f.Pos.X, f.Pos.Y, 28, new Color(f.Color, Mathf.Clamp(f.Life * 1.5f, 0f, 1f)), 7);
    }

    protected static bool KeyLeft() => Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left);
    protected static bool KeyRight() => Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right);
    protected static bool KeyUp() => Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up);
    protected static bool KeyDown() => Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down);

    /// <summary>Ask LevelController to show the hazard alert now (pauses this game until the choice is made).</summary>
    protected void RequestHazard() => EmitSignal(SignalName.HazardReady);

    // ---- MOUSE + KEYBOARD MOVEMENT ----
    // The mouse is read from INPUT EVENTS (not by asking "is the button down?" each frame):
    // a touchpad TAP is so short that a per-frame check misses it completely.
    private Vector2? _walkTarget;
    private Vector2 _mousePos;
    private bool _mouseMoved;
    private bool _mouseHeld;
    private float _clickPulse;

    /// <summary>True while the mouse button is held - or for a moment after a quick tap (touchpads).</summary>
    protected bool MouseHeld => _mouseHeld || _clickPulse > 0f;

    /// <summary>Mouse clicks / moves on this game. Games that need their own clicks override _GuiInput instead.</summary>
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _mousePos = mb.Position;
            if (mb.Pressed)
            {
                _mouseHeld = true;
                _clickPulse = 0.3f;
                _walkTarget = mb.Position;
            }
            else
            {
                _mouseHeld = false;
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            _mousePos = mm.Position;
            _mouseMoved = true;
            if (_mouseHeld) _walkTarget = mm.Position;
        }
    }

    /// <summary>
    /// Direction the player should walk this frame (length 0 or 1).
    ///   Keyboard (WASD / arrows) always wins.
    ///   Mouse: CLICK (or tap, or hold) somewhere and the astronaut walks there.
    ///   followMouse = true (catch games): the astronaut also follows the mouse when you just move it.
    /// </summary>
    protected Vector2 WalkDirection(Vector2 from, bool horizontalOnly, bool followMouse = false)
    {
        var key = new Vector2((KeyRight() ? 1 : 0) - (KeyLeft() ? 1 : 0),
                              horizontalOnly ? 0 : (KeyDown() ? 1 : 0) - (KeyUp() ? 1 : 0));

        if (key != Vector2.Zero)
        {
            _walkTarget = null;
            _mouseMoved = false;
            return key.Normalized();
        }

        if (followMouse && _mouseMoved)
            _walkTarget = _mousePos;
        _mouseMoved = false;

        if (_walkTarget is Vector2 t)
        {
            var d = t - from;
            if (horizontalOnly) d.Y = 0;
            if (d.Length() < 10f)
            {
                _walkTarget = null;
                return Vector2.Zero;
            }
            return d.Normalized();
        }
        return Vector2.Zero;
    }

    /// <summary>Where the astronaut is walking to with the mouse (to draw a marker), or null.</summary>
    protected Vector2? WalkTarget => _walkTarget;

    protected static bool IsLeftClick(InputEvent e, out Vector2 position)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            position = mb.Position;
            return true;
        }
        position = Vector2.Zero;
        return false;
    }
}

using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// BASE CLASS FOR A ROOM REPAIR (Oxygen Room / Power Room / Shield Room).
/// A pop-up panel over the base. It emits Done(success) when finished, or Done(false) if the player
/// presses LEAVE / Esc. The storm keeps draining the bars while you repair - be quick!
///
/// Used by StormGauntletGame on Day 9. They don't depend on the gauntlet, so the 3D base can
/// open them too: AddChild(new PipeTask()) and listen to Done.
/// </summary>
public enum RoomKind { Oxygen, Power, Shield }

public partial class RoomTask : Control
{
    [Signal] public delegate void DoneEventHandler(bool success);

    /// <summary>Make the repair task for a room: Oxygen -> pipes, Power -> solar puzzle, Shield -> meteors.</summary>
    public static RoomTask Create(RoomKind kind) => kind switch
    {
        RoomKind.Oxygen => new PipeTask(),
        RoomKind.Power => new SolarPuzzleTask(),
        _ => new ShieldOrbitTask(),
    };

    /// <summary>The panel the task draws in.</summary>
    protected static readonly Rect2 Box = new(160, 165, 960, 535);
    private static readonly Rect2 LeaveButton = new(Box.End.X - 170, Box.Position.Y + 14, 150, 40);

    protected virtual string Title => "REPAIR";
    protected virtual Color Accent => Art.Cyan;
    protected virtual string HelpText => "";

    /// <summary>false = no LEAVE button / Esc (the safe-choice visit you must finish).</summary>
    public bool CanLeave { get; set; } = true;

    /// <summary>true = finishing fills the bar to 100% (safe-choice visit). false = the normal top-up.</summary>
    public bool FillToFull { get; set; }

    /// <summary>Gives the room's reward: the normal amount, or a full bar when FillToFull is on.</summary>
    protected void Reward(float oxygen, float power, float shield)
    {
        if (FillToFull)
            Gm.ChangeResources(oxygen > 0 ? 100f : 0f, power > 0 ? 100f : 0f, shield > 0 ? 100f : 0f);
        else
            Gm.ChangeResources(oxygen, power, shield);
    }

    protected float Elapsed;
    protected readonly RandomNumberGenerator Rng = new();
    protected GameManager Gm => GameManager.Instance;
    public bool IsFinished { get; private set; }

    private sealed class FloatText { public Vector2 Pos; public string Text; public Color Color; public float Life; }
    private readonly List<FloatText> _floaters = new();

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // anchors AND size: fill the whole screen
        MouseFilter = MouseFilterEnum.Stop;
        Rng.Randomize();
        Setup();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Elapsed += dt;
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            _floaters[i].Life -= dt;
            _floaters[i].Pos += new Vector2(0, -45f * dt);
            if (_floaters[i].Life <= 0f) _floaters.RemoveAt(i);
        }
        if (!IsFinished)
        {
            if (CanLeave && Input.IsKeyPressed(Key.Escape))
                Finish(false);
            else
                Tick(dt);
        }
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (IsFinished) return;
        if (CanLeave && e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && LeaveButton.HasPoint(mb.Position))
        {
            Finish(false);
            AcceptEvent();
            return;
        }
        OnInput(e);
    }

    protected virtual void Setup() { }
    protected virtual void Tick(float dt) { }
    protected virtual void OnInput(InputEvent e) { }
    protected virtual void DrawTask() { }

    protected void Finish(bool success)
    {
        if (IsFinished) return;
        IsFinished = true;
        EmitSignal(SignalName.Done, success);
    }

    protected void Pop(Vector2 pos, string text, Color color) =>
        _floaters.Add(new FloatText { Pos = pos, Text = text, Color = color, Life = 1.1f });

    protected void Sound(string name, float volumeDb = -6f) => Sfx.Play(this, name, volumeDb);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0.01f, 0.04f, 0.55f));
        Art.Panel(this, Box, Accent, 0.97f);
        Art.Text(this, Title, Box.Position + new Vector2(24, 46), 28, Accent, outline: 5);
        Art.Text(this, HelpText, Box.Position + new Vector2(24, 76), 17, Art.White);

        if (CanLeave)
        {
            Art.Panel(this, LeaveButton, Art.Red, 0.95f);
            Art.TextCentered(this, "LEAVE", LeaveButton.GetCenter().X, LeaveButton.GetCenter().Y + 7, 17, Art.White);
        }

        DrawTask();

        foreach (var f in _floaters)
            Art.TextCentered(this, f.Text, f.Pos.X, f.Pos.Y, 28, new Color(f.Color, Mathf.Clamp(f.Life * 1.5f, 0f, 1f)), 7);
    }
}

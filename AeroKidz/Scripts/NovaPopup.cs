using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// NOVA - the robot helper in the bottom-right corner.
/// Attach to: Main/UI/%Nova (a full-screen Control with Mouse Filter = Ignore, so it never blocks the game).
///
/// ONE message at a time. Nothing disappears by itself: the player reads at their own speed and
/// clicks the NEXT arrow (or presses Enter) to go on. Made for young readers (6+).
///
///   Dialog(text) : part of the story. The game WAITS until the player clicks NEXT (DialogClosed is emitted).
///   Tip(text)    : a hint during play. Only ONE tip at a time - a newer tip replaces an old one.
///                  speak = true reads it out loud (only used in the final storm of Mission 3).
///
/// Blocked = true while a pop-up / alert / repair is on screen: Nova hides so nothing overlaps.
/// Dialogs wait and come back afterwards; tips that arrive while blocked are dropped (spoken ones are still heard).
/// </summary>
public partial class NovaPopup : Control
{
    [Signal] public delegate void DialogClosedEventHandler();

    // ---- Tuning ----
    private const float PopInTime = 0.3f;
    private const float CharsPerSecond = 45f;   // typing effect
    private const int FontSize = 20;
    private const float BubbleWidth = 420f;
    private const float VoicePitch = 1.25f;
    private const int VoiceVolume = 90;

    private static readonly Vector2 RobotHome = new(1190, 606);
    private const float RobotScale = 1.4f;

    private sealed class Message { public string Text; public bool IsDialog; public bool Speak; }

    private readonly Queue<Message> _dialogs = new();
    private Message _current;
    private Message _pendingTip;
    private float _shownTime;      // seconds the current message has been visible
    private float _time;
    private Rect2 _nextButton;
    private static string _voiceId;

    /// <summary>True while Nova shows a message (the map hides its small Nova then).</summary>
    public static bool IsTalking { get; private set; }

    /// <summary>Set by UIManager: true while any pop-up, alert or repair panel is on screen.</summary>
    public bool Blocked { get; set; }

    /// <summary>True while a story dialog is waiting to be read.</summary>
    public bool HasDialog => (_current != null && _current.IsDialog) || _dialogs.Count > 0;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // anchors AND size: fill the whole screen
        MouseFilter = MouseFilterEnum.Ignore;
    }

    // ==================================================================
    // API
    // ==================================================================

    /// <summary>A story line. The flow waits until the player clicks NEXT.</summary>
    public void Dialog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { EmitSignal(SignalName.DialogClosed); return; }
        _dialogs.Enqueue(new Message { Text = text, IsDialog = true });
        if (_current != null && !_current.IsDialog) _current = null; // a story line wins over a tip
        ShowNextIfIdle();
    }

    /// <summary>A short hint during play. Replaces an older tip. speak = read it out loud.</summary>
    public void Tip(string text, bool speak = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (speak) Speak(text);
        if (Blocked) return; // something else is on screen - don't pile up
        var tip = new Message { Text = text, Speak = speak };
        if (_current == null || !_current.IsDialog)
        {
            _current = tip;
            _shownTime = 0f;
        }
        else
        {
            _pendingTip = tip; // show after the story line
        }
    }

    /// <summary>Remove everything (e.g. when going back to the map).</summary>
    public void ClearTips()
    {
        _pendingTip = null;
        if (_current != null && !_current.IsDialog) _current = null;
    }

    private void ShowNextIfIdle()
    {
        if (_current != null) return;
        if (_dialogs.Count > 0)
        {
            _current = _dialogs.Dequeue();
            _shownTime = 0f;
        }
        else if (_pendingTip != null)
        {
            _current = _pendingTip;
            _pendingTip = null;
            _shownTime = 0f;
        }
    }

    private void Next()
    {
        if (_current == null) return;
        int fullTime = (int)(_current.Text.Length / CharsPerSecond * 1000);
        if (_shownTime * 1000 < fullTime)
        {
            _shownTime = 999f; // first click: finish typing the text
            return;
        }
        bool wasDialog = _current.IsDialog;
        _current = null;
        Sfx.Play(this, Sfx.Pop, -14f);
        ShowNextIfIdle();
        if (wasDialog && !HasDialog)
            EmitSignal(SignalName.DialogClosed);
    }

    // ---- Voice (only for tips with speak = true) ----
    private static void Speak(string text)
    {
        try
        {
            if (_voiceId == null)
            {
                var voices = DisplayServer.TtsGetVoicesForLanguage("en");
                _voiceId = voices.Length > 0 ? voices[0] : "";
            }
            if (_voiceId == "") return;
            DisplayServer.TtsSpeak(text, _voiceId, VoiceVolume, VoicePitch, 1.0f, 0, true);
        }
        catch (System.Exception e)
        {
            GD.Print($"[Nova] text-to-speech not available: {e.Message}");
            _voiceId = "";
        }
    }

    // ==================================================================
    // INPUT: the NEXT arrow (clicks anywhere else go through to the game)
    // ==================================================================
    public override void _Input(InputEvent e)
    {
        if (_current == null || Blocked) return;

        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            var p = GetGlobalTransformWithCanvas().AffineInverse() * mb.Position;
            if (_nextButton.Grow(8).HasPoint(p))
            {
                Next();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (e is InputEventKey k && k.Pressed && !k.Echo && (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter))
        {
            Next();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (_current != null && !Blocked) _shownTime += dt;
        IsTalking = _current != null && !Blocked;
        QueueRedraw();
    }

    // ==================================================================
    // DRAWING
    // ==================================================================
    public override void _Draw()
    {
        if (_current == null || Blocked)
            return;

        float up = EaseOutBack(Mathf.Clamp(_shownTime / PopInTime, 0f, 1f));
        var head = RobotHome + new Vector2(0, (1f - up) * 260f);
        int shown = Mathf.Min(_current.Text.Length, (int)(_shownTime * CharsPerSecond));
        bool typing = shown < _current.Text.Length;
        Art.NovaRobot(this, head, RobotScale, _time, typing, typing);
        if (_shownTime >= PopInTime * 0.6f)
            DrawBubble(head, _current.Text.Substring(0, shown), typing);
    }

    private void DrawBubble(Vector2 robot, string shownText, bool typing)
    {
        var font = Art.Font;
        const float pad = 18f;
        float textW = BubbleWidth - pad * 2;
        var full = font.GetMultilineStringSize(_current.Text, HorizontalAlignment.Left, textW, FontSize);
        float h = full.Y + pad * 2 + 26f + 44f; // name + text + NEXT button row

        float right = robot.X - 58f;
        float bottom = robot.Y + 46f;
        var r = new Rect2(right - BubbleWidth, bottom - h, BubbleWidth, h);

        var outline = new Color(0.08f, 0.1f, 0.22f);
        var tail = new[] { new Vector2(r.End.X - 4, r.End.Y - 60), new Vector2(r.End.X - 4, r.End.Y - 30), new Vector2(robot.X - 38, robot.Y + 4) };
        DrawColoredPolygon(new[] { tail[0] + new Vector2(0, -3), tail[1] + new Vector2(0, 3), tail[2] + new Vector2(4, 0) }, outline);
        Art.RoundRect(this, r.Grow(3), 18, outline);
        Art.RoundRect(this, r, 16, Colors.White);
        DrawColoredPolygon(tail, Colors.White);

        Art.Text(this, "NOVA", r.Position + new Vector2(pad, pad + 14), 17, new Color(0.15f, 0.45f, 0.85f));
        var textPos = r.Position + new Vector2(pad, pad + 26 + font.GetAscent(FontSize));
        DrawMultilineString(font, textPos, shownText, HorizontalAlignment.Left, textW, FontSize, -1, new Color(0.06f, 0.08f, 0.18f));

        // NEXT arrow button (bottom-right of the bubble) - pulses so kids notice it
        bool last = !_current.IsDialog || _dialogs.Count == 0;
        string label = typing ? "..." : (last ? "OK  ✓" : "NEXT  ▶");
        _nextButton = new Rect2(r.End.X - 132, r.End.Y - 50, 116, 38);
        float pulse = typing ? 0f : 0.5f + 0.5f * Mathf.Sin(_time * 5f);
        Art.RoundRect(this, _nextButton.Grow(2 + 2 * pulse), 12, new Color(0.1f, 0.35f, 0.8f, 0.5f));
        Art.RoundRect(this, _nextButton, 11, typing ? new Color(0.6f, 0.65f, 0.75f) : new Color(0.15f, 0.5f, 1f));
        Art.TextCentered(this, label, _nextButton.GetCenter().X, _nextButton.GetCenter().Y + 7, 18, Colors.White);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}

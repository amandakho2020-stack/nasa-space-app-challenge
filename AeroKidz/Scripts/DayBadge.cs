using Godot;

namespace AeroKidz;

/// <summary>
/// The day badge in the top-right corner of the HUD:
///   [mission icon]  DAY 9          ● ● ●
///                   The Storm      (timer)
/// Icon: plant (Mission 1, days 1-3), drill (Mission 2, days 4-6), sun (Mission 3, days 7-9).
/// The 3 dots show which day of the mission it is.
/// UIManager puts it inside %DayPanel and calls Set(...).
/// </summary>
public partial class DayBadge : Control
{
    private int _day = 1;
    private string _title = "";
    private string _timer = "";
    private float _time;

    public void Set(int day, string title)
    {
        _day = day;
        _title = title ?? "";
        QueueRedraw();
    }

    public void SetTimer(string text) => _timer = text ?? "";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(240, 64);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    private Color MissionColor => _day <= 3 ? Art.Green : _day <= 6 ? Art.Orange : Art.Yellow;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        Color mc = MissionColor;

        // panel with a soft mission-coloured glow and a shine line on top
        Art.Panel(this, r, new Color(mc, 0.95f), 0.92f);
        DrawLine(new Vector2(16, 5), new Vector2(r.Size.X - 16, 5), new Color(1, 1, 1, 0.12f), 2f);

        // mission icon in a glowing circle
        var c = new Vector2(34, r.Size.Y / 2f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 2.5f);
        DrawCircle(c, 25f + pulse * 2f, new Color(mc, 0.18f));
        DrawCircle(c, 21f, new Color(0.03f, 0.06f, 0.14f));
        DrawArc(c, 21f, 0, Mathf.Tau, 32, mc, 2.5f);
        DrawIcon(c, mc);

        // DAY 9 + the day's name
        Art.Text(this, $"DAY {_day}", new Vector2(66, 30), 22, Art.Yellow, outline: 5);
        string sub = string.IsNullOrEmpty(_timer) ? _title : _timer;
        Color subColor = string.IsNullOrEmpty(_timer) ? Art.White : (Mathf.Sin(_time * 6f) > 0f ? Art.Red : Art.Orange);
        Art.Text(this, sub, new Vector2(66, 52), 15, subColor, HorizontalAlignment.Left, r.Size.X - 76, 3);

        // which day of this mission (1, 2 or 3)
        int dayInMission = (_day - 1) % 3 + 1;
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector2(r.Size.X - 52 + i * 16, 22);
            bool done = i < dayInMission;
            if (done) DrawCircle(p, 7f, new Color(mc, 0.25f));
            DrawCircle(p, 5f, done ? mc : new Color(1, 1, 1, 0.15f));
        }
    }

    private void DrawIcon(Vector2 c, Color mc)
    {
        if (_day <= 3)
        {
            // a sprout: stem + two leaves
            DrawLine(c + new Vector2(0, 11), c + new Vector2(0, -4), Art.Green, 3f);
            DrawColoredPolygon(new[] { c + new Vector2(0, -2), c + new Vector2(-12, -10), c + new Vector2(-4, 2) }, Art.Green);
            DrawColoredPolygon(new[] { c + new Vector2(0, -4), c + new Vector2(12, -13), c + new Vector2(4, 0) }, Art.Green.Lightened(0.2f));
            DrawLine(c + new Vector2(-8, 12), c + new Vector2(8, 12), new Color(0.6f, 0.4f, 0.2f), 3f);
        }
        else if (_day <= 6)
        {
            // a drill bit pointing down
            DrawRect(new Rect2(c + new Vector2(-6, -13), new Vector2(12, 8)), Art.Steel);
            DrawColoredPolygon(new[] { c + new Vector2(-8, -5), c + new Vector2(8, -5), c + new Vector2(0, 13) }, mc);
            DrawLine(c + new Vector2(-5, 0), c + new Vector2(4, 3), new Color(0.5f, 0.25f, 0f), 2f);
            DrawLine(c + new Vector2(-3, 5), c + new Vector2(3, 7), new Color(0.5f, 0.25f, 0f), 2f);
        }
        else
        {
            // the Sun with turning rays
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.Tau / 8f + _time * 0.6f;
                DrawLine(c + Vector2.FromAngle(a) * 10f, c + Vector2.FromAngle(a) * 15f, mc, 2.5f);
            }
            DrawCircle(c, 8f, mc);
            DrawCircle(c + new Vector2(-2, -2), 3f, new Color(1, 1, 1, 0.6f));
        }
    }
}

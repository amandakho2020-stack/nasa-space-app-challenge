using Godot;

namespace AeroKidz;

/// <summary>
/// A small round icon for the top bar (drawn in code, no image files).
/// Attach to a Control (e.g. 30x30) inside the HUD and pick the Kind in the Inspector.
///   0 = Science (flask)   1 = Oxygen (O2)   2 = Power (lightning)   3 = Shield
/// </summary>
public partial class HudIcon : Control
{
    [Export(PropertyHint.Enum, "Science,Oxygen,Power,Shield")] public int Kind { get; set; }

    private static readonly Color[] Rings =
    {
        new(0.45f, 1f, 0.55f), new(0.3f, 0.85f, 1f), new(1f, 0.72f, 0.2f), new(0.85f, 0.4f, 1f),
    };

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var c = Size / 2f;
        float r = Mathf.Min(Size.X, Size.Y) / 2f;
        var ring = Rings[Mathf.Clamp(Kind, 0, 3)];
        DrawCircle(c, r, new Color(0.05f, 0.08f, 0.2f));
        DrawArc(c, r - 1.5f, 0, Mathf.Tau, 32, ring, 2.5f, true);

        switch (Kind)
        {
            case 0: // flask
                DrawColoredPolygon(new[] { c + new Vector2(-3, -r * 0.6f), c + new Vector2(3, -r * 0.6f), c + new Vector2(3, -r * 0.15f),
                    c + new Vector2(r * 0.5f, r * 0.5f), c + new Vector2(-r * 0.5f, r * 0.5f), c + new Vector2(-3, -r * 0.15f) }, new Color(0.85f, 0.95f, 1f));
                DrawColoredPolygon(new[] { c + new Vector2(-r * 0.3f, r * 0.15f), c + new Vector2(r * 0.3f, r * 0.15f),
                    c + new Vector2(r * 0.46f, r * 0.46f), c + new Vector2(-r * 0.46f, r * 0.46f) }, ring);
                break;
            case 1: // O2
                Art.TextCentered(this, "O2", c.X, c.Y + r * 0.35f, Mathf.RoundToInt(r * 0.95f), Colors.White);
                break;
            case 2: // lightning bolt
                DrawColoredPolygon(new[] { c + new Vector2(r * 0.15f, -r * 0.65f), c + new Vector2(-r * 0.35f, r * 0.08f), c + new Vector2(-r * 0.02f, r * 0.08f),
                    c + new Vector2(-r * 0.15f, r * 0.65f), c + new Vector2(r * 0.38f, -r * 0.1f), c + new Vector2(r * 0.04f, -r * 0.1f) }, ring);
                break;
            default: // shield
                DrawColoredPolygon(new[] { c + new Vector2(0, -r * 0.6f), c + new Vector2(r * 0.5f, -r * 0.38f), c + new Vector2(r * 0.42f, r * 0.15f),
                    c + new Vector2(0, r * 0.62f), c + new Vector2(-r * 0.42f, r * 0.15f), c + new Vector2(-r * 0.5f, -r * 0.38f) }, ring);
                DrawLine(c + new Vector2(0, -r * 0.5f), c + new Vector2(0, r * 0.5f), new Color(1, 1, 1, 0.6f), 2f);
                break;
        }
    }
}

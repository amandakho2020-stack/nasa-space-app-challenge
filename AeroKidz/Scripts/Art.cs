using Godot;

namespace AeroKidz;

/// <summary>
/// SHARED DRAWING HELPERS - every visual in the game is drawn with code (no image files needed).
/// Call these from any node's _Draw(), passing "this" as the canvas.
///
/// UI designer: when you have real sprites, replace a helper's body (e.g. Astronaut) with
/// c.DrawTexture(...) and every minigame picks up the new art automatically.
/// </summary>
public static class Art
{
    // ---- Palette (matches the HUD theme) ----
    public static readonly Color Cyan = new(0.3f, 0.85f, 1f);
    public static readonly Color Orange = new(1f, 0.68f, 0.12f);
    public static readonly Color Magenta = new(0.85f, 0.35f, 1f);
    public static readonly Color Yellow = new(1f, 0.85f, 0.2f);
    public static readonly Color Red = new(1f, 0.3f, 0.25f);
    public static readonly Color Green = new(0.4f, 1f, 0.5f);
    public static readonly Color Blue = new(0.3f, 0.55f, 1f);
    public static readonly Color Navy = new(0.035f, 0.07f, 0.15f);
    public static readonly Color White = new(0.95f, 0.97f, 1f);
    public static readonly Color Steel = new(0.6f, 0.65f, 0.72f);

    public static Font Font => ThemeDB.FallbackFont;

    // ------------------------------------------------------------------
    // PICTURES (PNG files in res://Assets - craftpix.net free packs, see the LICENSE files there)
    // ------------------------------------------------------------------
    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> Textures = new();

    /// <summary>Load a picture once and keep it. Works even before Godot has imported the file.</summary>
    public static Texture2D Tex(string resPath)
    {
        if (Textures.TryGetValue(resPath, out var t)) return t;
        if (ResourceLoader.Exists(resPath))
            t = GD.Load<Texture2D>(resPath);
        if (t == null)
        {
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(resPath));
            if (img != null && !img.IsEmpty()) t = ImageTexture.CreateFromImage(img);
        }
        Textures[resPath] = t;
        return t;
    }

    /// <summary>Draw a picture so it fits in a square of 'size' pixels around 'center' (no rotation).</summary>
    public static void Picture(CanvasItem c, string resPath, Vector2 center, float size, Color? modulate = null)
    {
        var t = Tex(resPath);
        if (t == null) return;
        var ts = t.GetSize();
        float k = size / Mathf.Max(ts.X, ts.Y);
        c.DrawTextureRect(t, new Rect2(center - ts * k / 2f, ts * k), false, modulate ?? Colors.White);
    }

    /// <summary>
    /// Draw a picture rotated. NOTE: this resets the canvas transform afterwards,
    /// so don't use it in the middle of your own DrawSetTransform (e.g. screen shake).
    /// </summary>
    public static void PictureRotated(CanvasItem c, string resPath, Vector2 center, float size, float angle, Color? modulate = null)
    {
        var t = Tex(resPath);
        if (t == null) return;
        var ts = t.GetSize();
        float k = size / Mathf.Max(ts.X, ts.Y);
        c.DrawSetTransform(center, angle, new Vector2(k, k));
        c.DrawTexture(t, -ts / 2f, modulate ?? Colors.White);
        c.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// A chunky, rounded, kid-friendly font from the computer (no font file needed).
    /// Tries each name in order; Windows always has Segoe UI. Called once at start-up by GameManager.
    /// To use your own font: put a .ttf in the project and set ThemeDB.FallbackFont = GD.Load&lt;Font&gt;("res://...ttf").
    /// </summary>
    public static void UseKidFont()
    {
        ThemeDB.FallbackFont = new SystemFont
        {
            FontNames = new[] { "Fredoka", "Baloo 2", "Nunito", "Arial Rounded MT Bold", "Segoe UI Black", "Segoe UI", "Arial" },
            FontWeight = 800,
            Antialiasing = TextServer.FontAntialiasing.Gray,
        };
    }

    // ------------------------------------------------------------------
    // TEXT (outline > 0 = dark outline around the letters, like a cartoon title)
    // ------------------------------------------------------------------
    public static void Text(CanvasItem c, string text, Vector2 pos, int size, Color color,
                            HorizontalAlignment align = HorizontalAlignment.Left, float width = -1, int outline = 0)
    {
        if (outline > 0)
            c.DrawStringOutline(Font, pos, text, align, width, size, outline, new Color(0.02f, 0.03f, 0.1f, color.A));
        c.DrawString(Font, pos, text, align, width, size, color);
    }

    /// <summary>Text centred horizontally on x (y is the baseline).</summary>
    public static void TextCentered(CanvasItem c, string text, float x, float y, int size, Color color, int outline = 0)
    {
        Text(c, text, new Vector2(x - 600f, y), size, color, HorizontalAlignment.Center, 1200f, outline);
    }

    /// <summary>The instruction banner under the HUD, shared by every minigame (short words!).</summary>
    public static void Hint(CanvasItem c, string text)
    {
        float w = Font.GetStringSize(text, HorizontalAlignment.Left, -1, 17).X + 48f;
        var r = new Rect2(640 - w / 2, 120, w, 38);
        Panel(c, r, Cyan, 0.88f);
        TextCentered(c, text, 640, 145, 17, White, 4);
    }

    // ------------------------------------------------------------------
    // SHAPES
    // ------------------------------------------------------------------
    private static readonly System.Collections.Generic.Dictionary<(Color, float), StyleBoxFlat> PanelStyles = new();

    /// <summary>A rounded, glowing sci-fi panel (like the reference art).</summary>
    public static void Panel(CanvasItem c, Rect2 r, Color border, float alpha = 0.9f)
    {
        if (!PanelStyles.TryGetValue((border, alpha), out var sb))
        {
            sb = new StyleBoxFlat
            {
                BgColor = new Color(0.04f, 0.08f, 0.18f, alpha),
                BorderColor = border,
                ShadowColor = new Color(border, 0.35f * alpha),
                ShadowSize = 6,
                AntiAliasing = true,
            };
            sb.SetBorderWidthAll(3);
            sb.SetCornerRadiusAll(12);
            PanelStyles[(border, alpha)] = sb;
        }
        c.DrawStyleBox(sb, r);
    }

    public static void Ellipse(CanvasItem c, Vector2 center, Vector2 radii, Color color)
    {
        var pts = new Vector2[28];
        for (int i = 0; i < pts.Length; i++)
        {
            float a = Mathf.Tau * i / pts.Length;
            pts[i] = center + new Vector2(Mathf.Cos(a) * radii.X, Mathf.Sin(a) * radii.Y);
        }
        c.DrawColoredPolygon(pts, color);
    }

    /// <summary>Upper half of a circle, filled (domes).</summary>
    public static void HalfDisc(CanvasItem c, Vector2 baseCenter, float radius, Color color)
    {
        var pts = new Vector2[25];
        for (int i = 0; i < pts.Length; i++)
        {
            float a = Mathf.Pi + Mathf.Pi * i / (pts.Length - 1);
            pts[i] = baseCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        c.DrawColoredPolygon(pts, color);
    }

    public static void Stars(CanvasItem c, Rect2 area, int count, float time, ulong seed = 7)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        for (int i = 0; i < count; i++)
        {
            var p = new Vector2(rng.RandfRange(area.Position.X, area.End.X), rng.RandfRange(area.Position.Y, area.End.Y));
            float twinkle = 0.5f + 0.5f * Mathf.Sin(time * rng.RandfRange(1f, 3f) + i);
            c.DrawCircle(p, rng.RandfRange(0.8f, 1.8f), new Color(1, 1, 1, 0.25f + 0.6f * twinkle));
        }
    }

    /// <summary>A whole bar 0..1 with a label, used inside minigames.</summary>
    public static void Meter(CanvasItem c, Rect2 r, float fill, Color color, string label)
    {
        c.DrawRect(r, new Color(0.02f, 0.05f, 0.1f));
        c.DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Mathf.Clamp(fill, 0, 1), r.Size.Y)), color);
        c.DrawRect(r, color, false, 2f);
        TextCentered(c, label, r.GetCenter().X, r.Position.Y - 6, 14, White);
    }

    // ------------------------------------------------------------------
    // CHARACTERS
    // ------------------------------------------------------------------

    private static readonly System.Collections.Generic.Dictionary<(Color, int), StyleBoxFlat> RoundStyles = new();

    /// <summary>A filled rectangle with rounded corners.</summary>
    public static void RoundRect(CanvasItem c, Rect2 r, float radius, Color color)
    {
        int rad = Mathf.RoundToInt(radius);
        if (!RoundStyles.TryGetValue((color, rad), out var sb))
        {
            sb = new StyleBoxFlat { BgColor = color, AntiAliasing = true };
            sb.SetCornerRadiusAll(rad);
            RoundStyles[(color, rad)] = sb;
        }
        c.DrawStyleBox(sb, r);
    }

    private static readonly Color Outline = new(0.08f, 0.1f, 0.22f);
    private static readonly Color VisorDark = new(0.16f, 0.13f, 0.45f);
    private static readonly Color VisorLight = new(0.33f, 0.3f, 0.75f);
    private static readonly Color Gold = new(1f, 0.78f, 0.3f);

    /// <summary>The Commander - a cute cartoon astronaut. feet = point between the boots. walk = animation phase.</summary>
    public static void Astronaut(CanvasItem c, Vector2 feet, float s = 1f, float walk = 0f, bool faceLeft = false)
    {
        float f = faceLeft ? -1f : 1f;
        float leg = Mathf.Sin(walk) * 6f;
        float bounce = Mathf.Abs(Mathf.Sin(walk)) * 2f;
        Vector2 P(float x, float y) => feet + new Vector2(x * f, y - bounce) * s;
        var suit = White;
        var shade = new Color(0.78f, 0.82f, 0.92f);

        // backpack
        RoundRect(c, RectAt(P(-20, -62), new Vector2(14, 34) * s), 5 * s, shade);
        // boots + legs
        c.DrawCircle(P(-7 + leg, -6), 9 * s, Outline);
        c.DrawCircle(P(8 - leg, -6), 9 * s, Outline);
        c.DrawCircle(P(-7 + leg, -7), 7.5f * s, Red);
        c.DrawCircle(P(8 - leg, -7), 7.5f * s, Red);
        c.DrawLine(P(-6 + leg, -12), P(-4, -30), shade, 10 * s);
        c.DrawLine(P(7 - leg, -12), P(5, -30), shade, 10 * s);
        // body
        RoundRect(c, RectAt(P(0, -44), new Vector2(34, 36) * s), 12 * s, suit);
        RoundRect(c, RectAt(P(2, -44), new Vector2(16, 10) * s), 3 * s, new Color(0.25f, 0.3f, 0.45f));
        c.DrawCircle(P(-2, -44), 2.2f * s, Red);
        c.DrawCircle(P(4, -44), 2.2f * s, Green);
        // arm (swings a little)
        c.DrawLine(P(12, -54), P(20, -38 + leg * 0.5f), suit, 9 * s);
        c.DrawCircle(P(20, -37 + leg * 0.5f), 5 * s, shade);
        // BIG round helmet with a purple visor (like the reference art)
        c.DrawCircle(P(0, -82), 25 * s, Outline);
        c.DrawCircle(P(0, -82), 23 * s, suit);
        c.DrawCircle(P(3, -81), 17 * s, Gold);
        c.DrawCircle(P(3, -81), 15 * s, VisorDark);
        c.DrawCircle(P(5, -84), 10 * s, VisorLight);
        c.DrawCircle(P(10, -89), 4 * s, new Color(1, 1, 1, 0.85f));
        c.DrawCircle(P(4, -91), 2 * s, new Color(1, 1, 1, 0.7f));
    }

    private static Rect2 RectAt(Vector2 center, Vector2 size) => new(center - size / 2f, size);

    // ------------------------------------------------------------------
    // SMALL ICONS (drawn in code; r = radius / half-size)
    // ------------------------------------------------------------------
    public static void IconSnowflake(CanvasItem c, Vector2 p, float r, Color col)
    {
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.Pi / 3f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            c.DrawLine(p - d, p + d, col, r * 0.22f);
            foreach (var s in new[] { 1f, -1f })
            {
                var tip = p + d * s * 0.62f;
                var n = d.Rotated(0.9f).Normalized() * r * 0.32f;
                var m = d.Rotated(-0.9f).Normalized() * r * 0.32f;
                c.DrawLine(tip, tip + n * s, col, r * 0.16f);
                c.DrawLine(tip, tip + m * s, col, r * 0.16f);
            }
        }
    }

    public static void IconFlame(CanvasItem c, Vector2 p, float r)
    {
        c.DrawColoredPolygon(new[] { p + new Vector2(0, -r * 1.3f), p + new Vector2(r * 0.8f, 0), p + new Vector2(r * 0.55f, r * 0.75f),
            p + new Vector2(-r * 0.55f, r * 0.75f), p + new Vector2(-r * 0.8f, 0) }, new Color(1f, 0.45f, 0.1f));
        c.DrawColoredPolygon(new[] { p + new Vector2(0, -r * 0.5f), p + new Vector2(r * 0.4f, r * 0.2f), p + new Vector2(0, r * 0.7f),
            p + new Vector2(-r * 0.4f, r * 0.2f) }, new Color(1f, 0.85f, 0.2f));
    }

    public static void IconCheck(CanvasItem c, Vector2 p, float r, Color ring)
    {
        c.DrawCircle(p, r, ring);
        c.DrawCircle(p, r * 0.78f, new Color(0.1f, 0.6f, 0.25f));
        c.DrawPolyline(new[] { p + new Vector2(-r * 0.4f, 0), p + new Vector2(-r * 0.1f, r * 0.32f), p + new Vector2(r * 0.42f, -r * 0.3f) },
            Colors.White, r * 0.22f, true);
    }

    public static void IconThermometer(CanvasItem c, Vector2 p, float r)
    {
        RoundRect(c, RectAt(p + new Vector2(0, -r * 0.35f), new Vector2(r * 0.6f, r * 1.6f)), r * 0.3f, White);
        c.DrawCircle(p + new Vector2(0, r * 0.6f), r * 0.5f, White);
        c.DrawCircle(p + new Vector2(0, r * 0.6f), r * 0.36f, Red);
        c.DrawLine(p + new Vector2(0, r * 0.5f), p + new Vector2(0, -r * 0.6f), Red, r * 0.24f);
    }

    public static void IconRock(CanvasItem c, Vector2 p, float r, Color col)
    {
        c.DrawColoredPolygon(new[] { p + new Vector2(-r, r * 0.6f), p + new Vector2(-r * 0.7f, -r * 0.3f), p + new Vector2(-r * 0.1f, -r * 0.8f),
            p + new Vector2(r * 0.7f, -r * 0.4f), p + new Vector2(r, r * 0.6f) }, col);
        c.DrawLine(p + new Vector2(-r * 0.1f, -r * 0.8f), p + new Vector2(0, r * 0.6f), col.Darkened(0.35f), r * 0.12f);
    }

    public static void IconIce(CanvasItem c, Vector2 p, float r)
    {
        c.DrawColoredPolygon(new[] { p + new Vector2(0, -r), p + new Vector2(r * 0.75f, -r * 0.2f), p + new Vector2(r * 0.4f, r),
            p + new Vector2(-r * 0.4f, r), p + new Vector2(-r * 0.75f, -r * 0.2f) }, new Color(0.6f, 0.88f, 1f));
        c.DrawLine(p + new Vector2(0, -r), p + new Vector2(0, r), new Color(1, 1, 1, 0.8f), r * 0.12f);
    }

    /// <summary>A label "pill" with an icon on the left (like the reference art's layer labels).</summary>
    public static void IconPill(CanvasItem c, Vector2 leftCenter, string text, System.Action<Vector2> drawIcon, int fontSize = 18)
    {
        float w = Font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X + 66f;
        var r = new Rect2(leftCenter - new Vector2(0, 20), new Vector2(w, 40));
        RoundRect(c, r.Grow(3), 22, new Color(0.08f, 0.1f, 0.22f, 0.9f));
        RoundRect(c, r, 20, new Color(0.98f, 0.95f, 0.88f));
        drawIcon(leftCenter + new Vector2(26, 0));
        Text(c, text, leftCenter + new Vector2(50, fontSize * 0.36f), fontSize, new Color(0.12f, 0.12f, 0.22f));
    }

    /// <summary>Nova, the robot helper (small, e.g. on maps).</summary>
    public static void Nova(CanvasItem c, Vector2 center, float s, float time) =>
        NovaRobot(c, center, s * 0.75f, time, false, false);

    /// <summary>
    /// Nova the robot: rounded white head with a dark screen face (happy cyan eyes),
    /// ear lights, antenna, little body with a heart light and a thruster flame.
    /// center = middle of the head.
    /// </summary>
    public static void NovaRobot(CanvasItem c, Vector2 center, float s, float time, bool talking, bool waving)
    {
        float bob = Mathf.Sin(time * 3f) * 3f;
        var p = center + new Vector2(0, bob);
        Vector2 Q(float x, float y) => p + new Vector2(x, y) * s;

        // thruster flame
        float flame = 0.75f + 0.25f * Mathf.Sin(time * 25f);
        Ellipse(c, Q(0, 62), new Vector2(9, 13 * flame) * s, new Color(Orange, 0.85f));
        Ellipse(c, Q(0, 59), new Vector2(5, 8 * flame) * s, new Color(Yellow, 0.95f));

        // body
        RoundRect(c, RectAt(Q(0, 42), new Vector2(40, 34) * s), 14 * s, Outline);
        RoundRect(c, RectAt(Q(0, 41), new Vector2(36, 30) * s), 12 * s, White);
        float heart = 0.6f + 0.4f * Mathf.Sin(time * 4f);
        c.DrawCircle(Q(0, 41), 6 * s, new Color(Cyan, heart));

        // arms (one waves while talking)
        float wave = waving ? Mathf.Sin(time * 10f) * 0.6f : 0f;
        c.DrawLine(Q(-18, 36), Q(-28, 50), Steel, 5 * s);
        c.DrawCircle(Q(-28, 50), 4 * s, White);
        var hand = Q(18, 36) + new Vector2(Mathf.Cos(-0.9f + wave), Mathf.Sin(-0.9f + wave)) * 16 * s;
        c.DrawLine(Q(18, 36), hand, Steel, 5 * s);
        c.DrawCircle(hand, 4 * s, White);

        // antenna
        c.DrawLine(Q(0, -26), Q(0, -38), Steel, 3 * s);
        bool lightOn = Mathf.PosMod(time, 1f) < 0.6f;
        c.DrawCircle(Q(0, -40), 5 * s, lightOn ? Red : new Color(0.45f, 0.1f, 0.1f));

        // head
        RoundRect(c, RectAt(Q(0, 0), new Vector2(64, 52) * s), 20 * s, Outline);
        RoundRect(c, RectAt(Q(0, 0), new Vector2(60, 48) * s), 18 * s, White);
        c.DrawCircle(Q(-31, 0), 6 * s, Cyan);   // ear lights
        c.DrawCircle(Q(31, 0), 6 * s, Cyan);
        // screen face
        RoundRect(c, RectAt(Q(0, 1), new Vector2(46, 32) * s), 12 * s, new Color(0.06f, 0.1f, 0.22f));
        bool blink = Mathf.PosMod(time, 3.2f) < 0.12f;
        if (blink)
        {
            c.DrawLine(Q(-14, -2), Q(-6, -2), Cyan, 2.5f * s);
            c.DrawLine(Q(6, -2), Q(14, -2), Cyan, 2.5f * s);
        }
        else
        {
            // happy "^ ^" eyes
            c.DrawArc(Q(-10, 1), 5 * s, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 8, Cyan, 3f * s);
            c.DrawArc(Q(10, 1), 5 * s, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 8, Cyan, 3f * s);
        }
        if (talking)
        {
            float open = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(time * 16f));
            Ellipse(c, Q(0, 9), new Vector2(5, 1.5f + 3.5f * open) * s, Cyan);
        }
        else
        {
            c.DrawArc(Q(0, 6), 5 * s, 0.3f, Mathf.Pi - 0.3f, 8, Cyan, 2.5f * s);
        }
        // cheeks
        c.DrawCircle(Q(-16, 9), 2.5f * s, new Color(1f, 0.5f, 0.7f, 0.6f));
        c.DrawCircle(Q(16, 9), 2.5f * s, new Color(1f, 0.5f, 0.7f, 0.6f));
    }

    // ------------------------------------------------------------------
    // BACKGROUNDS
    // ------------------------------------------------------------------

    /// <summary>Inside the Bio-Dome: starry glass dome, moon outside, metal floor.</summary>
    public static void DomeBackground(CanvasItem c, Vector2 size, float time)
    {
        c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.03f, 0.04f, 0.1f));
        Stars(c, new Rect2(0, 0, size.X, size.Y * 0.55f), 80, time);
        // Earth through the glass
        c.DrawCircle(new Vector2(1120, 230), 34, new Color(0.25f, 0.5f, 0.95f));
        c.DrawCircle(new Vector2(1110, 222), 12, new Color(0.3f, 0.75f, 0.4f));
        c.DrawCircle(new Vector2(1132, 240), 9, new Color(0.3f, 0.75f, 0.4f));
        // lunar surface outside
        c.DrawRect(new Rect2(0, size.Y * 0.5f, size.X, size.Y * 0.5f), new Color(0.3f, 0.3f, 0.34f));
        Ellipse(c, new Vector2(300, size.Y * 0.56f), new Vector2(70, 12), new Color(0.24f, 0.24f, 0.28f));
        Ellipse(c, new Vector2(900, size.Y * 0.58f), new Vector2(110, 16), new Color(0.24f, 0.24f, 0.28f));
        // glass dome ribs
        var hub = new Vector2(size.X / 2, size.Y + 200);
        var glass = new Color(0.5f, 0.85f, 1f, 0.25f);
        for (int i = 0; i < 3; i++)
            c.DrawArc(hub, 820 + i * 60, Mathf.Pi, Mathf.Tau, 64, glass, 3f);
        for (int i = 1; i < 8; i++)
        {
            float a = Mathf.Pi + Mathf.Pi * i / 8f;
            c.DrawLine(hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 300, hub + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 940, glass, 3f);
        }
        // floor
        c.DrawRect(new Rect2(0, size.Y - 40, size.X, 40), new Color(0.38f, 0.42f, 0.5f));
        for (int x = 0; x < size.X; x += 80)
            c.DrawLine(new Vector2(x, size.Y - 40), new Vector2(x, size.Y), new Color(0.25f, 0.28f, 0.35f), 2f);
    }

    /// <summary>Dark metal interior wall (maintenance rooms).</summary>
    public static void TechBackground(CanvasItem c, Vector2 size, Color tint)
    {
        c.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0.07f, 0.12f));
        var line = new Color(tint, 0.08f);
        for (int x = 0; x < size.X; x += 64) c.DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), line, 1f);
        for (int y = 0; y < size.Y; y += 64) c.DrawLine(new Vector2(0, y), new Vector2(size.X, y), line, 1f);
    }
}

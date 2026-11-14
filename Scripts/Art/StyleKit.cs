using Godot;
using System.Collections.Generic;

/// <summary>Material slots every generated model uses (one ArrayMesh surface per slot).</summary>
public enum Surf
{
	/// <summary>Painted panels: vertex-colour albedo, matte. Gets the ink outline.</summary>
	Paint = 0,
	/// <summary>Bare metal: vertex colour, slightly metallic and glossier. Gets the ink outline.</summary>
	Metal = 1,
	/// <summary>Transparent glass/visors: vertex colour + alpha, double-sided, no outline.</summary>
	Glass = 2,
	/// <summary>Emissive screens/LEDs/lamps: vertex colour drives emission, no outline.</summary>
	Glow = 3,
}

/// <summary>
/// The single source of truth for the Lunar Outpost art style.
///
/// Consistency rules every model in <see cref="ModelLibrary"/> follows:
///   1. Colours only come from this palette (sampled from the reference sheets: weathered cream
///      panels, sea-green/teal accents, rust-orange, gunmetal, brass, hazard yellow).
///   2. Flat-shaded low-poly facets (8-16 sided cylinders, 4-7 ring spheres) - "chunky" proportions.
///   3. Every face gets a small random brightness jitter (MeshKit.ColorJitter) = hand-painted look.
///   4. Four shared materials (Paint, Metal, Glass, Glow) - nothing gets a bespoke material.
///   5. A uniform screen-space ink outline (style_outline.gdshader) on Paint and Metal surfaces.
/// </summary>
public static class StyleKit
{
	// ---------------- Palette (sRGB) ----------------
	public static readonly Color Cream = new(0.80f, 0.77f, 0.66f);
	public static readonly Color CreamDark = new(0.64f, 0.61f, 0.52f);
	public static readonly Color Teal = new(0.38f, 0.58f, 0.54f);
	public static readonly Color TealPale = new(0.58f, 0.72f, 0.68f);
	public static readonly Color TealDark = new(0.22f, 0.36f, 0.37f);
	public static readonly Color Rust = new(0.70f, 0.39f, 0.20f);
	public static readonly Color Orange = new(0.90f, 0.56f, 0.21f);
	public static readonly Color Steel = new(0.58f, 0.60f, 0.62f);
	public static readonly Color Gunmetal = new(0.32f, 0.34f, 0.38f);
	public static readonly Color DarkSteel = new(0.17f, 0.18f, 0.21f);
	public static readonly Color Brass = new(0.74f, 0.58f, 0.29f);
	public static readonly Color Hazard = new(0.92f, 0.74f, 0.15f);
	public static readonly Color Red = new(0.72f, 0.19f, 0.16f);
	public static readonly Color Leather = new(0.44f, 0.31f, 0.20f);
	public static readonly Color SolarBlue = new(0.11f, 0.19f, 0.48f);
	public static readonly Color CellLine = new(0.42f, 0.57f, 0.86f);
	public static readonly Color Soil = new(0.29f, 0.19f, 0.12f);
	public static readonly Color Leaf = new(0.33f, 0.62f, 0.24f);
	public static readonly Color LeafDark = new(0.21f, 0.44f, 0.17f);
	public static readonly Color Radish = new(0.58f, 0.12f, 0.32f);
	public static readonly Color RootWhite = new(0.88f, 0.86f, 0.82f);
	public static readonly Color SuitWhite = new(0.86f, 0.86f, 0.84f);
	public static readonly Color VisorGold = new(0.88f, 0.62f, 0.17f);
	public static readonly Color Rubber = new(0.22f, 0.21f, 0.20f);
	public static readonly Color Rock = new(0.34f, 0.35f, 0.39f);
	public static readonly Color Crystal = new(0.55f, 0.58f, 0.66f);
	public static readonly Color EmblemBlue = new(0.16f, 0.27f, 0.62f);

	public static readonly Color GlowScreen = new(0.45f, 1f, 0.78f);
	public static readonly Color GlowAmber = new(1f, 0.64f, 0.2f);
	public static readonly Color GlowRed = new(1f, 0.22f, 0.16f);
	public static readonly Color GlowGreen = new(0.3f, 1f, 0.38f);
	public static readonly Color GlowBlue = new(0.32f, 0.62f, 1f);
	public static readonly Color GlowYellow = new(1f, 0.9f, 0.3f);
	public static readonly Color GlowWhite = new(1f, 0.95f, 0.84f);
	public static readonly Color GlowPurple = new(0.75f, 0.4f, 1f);

	public static readonly Color GlassClear = new(0.70f, 0.88f, 0.92f, 0.2f);
	public static readonly Color GlassDark = new(0.13f, 0.20f, 0.27f, 0.82f);

	/// <summary>Colours used for button grids, stack lights etc.</summary>
	public static readonly Color[] ButtonColors = { GlowRed, GlowYellow, GlowGreen, GlowBlue, GlowPurple, GlowAmber };

	// ---------------- Shared materials ----------------
	public static bool Outlines = true;
	public static float OutlinePixels = 2.2f;

	private static Dictionary<Surf, Material> _materials;
	private static ShaderMaterial _outline;
	private static StandardMaterial3D _hullExterior, _hullInterior;

	/// <summary>Drops cached materials so the next access rebuilds them (call before a fresh replace).</summary>
	public static void Reset()
	{
		_materials = null;
		_outline = null;
		_hullExterior = null;
		_hullInterior = null;
	}

	public static Material Get(Surf slot)
	{
		Build();
		return _materials[slot];
	}

	public static ShaderMaterial OutlineMaterial
	{
		get { Build(); return _outline; }
	}

	/// <summary>Plain materials for re-skinning the CSG base shell in the same palette (CSG has no vertex colours).</summary>
	public static StandardMaterial3D HullExterior => _hullExterior ??= new StandardMaterial3D
	{
		AlbedoColor = Cream,
		Roughness = 0.8f,
		MetallicSpecular = 0.35f,
	};

	public static StandardMaterial3D HullInterior => _hullInterior ??= new StandardMaterial3D
	{
		AlbedoColor = new Color(0.66f, 0.72f, 0.69f),
		Roughness = 0.75f,
		MetallicSpecular = 0.3f,
	};

	/// <summary>All shared resources with suggested file names, for saving next to the generated meshes.</summary>
	public static IEnumerable<(string file, Resource resource)> AllResources()
	{
		Build();
		yield return ("style_outline.tres", _outline);
		yield return ("style_paint.tres", _materials[Surf.Paint]);
		yield return ("style_metal.tres", _materials[Surf.Metal]);
		yield return ("style_glass.tres", _materials[Surf.Glass]);
		yield return ("style_glow.tres", _materials[Surf.Glow]);
		yield return ("style_hull_exterior.tres", HullExterior);
		yield return ("style_hull_interior.tres", HullInterior);
	}

	private static void Build()
	{
		if (_materials != null)
			return;

		_outline = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/style_outline.gdshader") };
		_outline.SetShaderParameter("outline_px", OutlinePixels);

		var paint = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			VertexColorIsSrgb = true,
			Roughness = 0.85f,
			MetallicSpecular = 0.35f,
			NextPass = Outlines ? _outline : null,
		};
		var metal = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			VertexColorIsSrgb = true,
			Metallic = 0.25f, // the airless environment has no reflections, so keep metals mostly diffuse
			Roughness = 0.45f,
			MetallicSpecular = 0.6f,
			NextPass = Outlines ? _outline : null,
		};
		var glass = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			VertexColorUseAsAlbedo = true,
			VertexColorIsSrgb = true,
			Roughness = 0.05f,
			MetallicSpecular = 0.9f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var glow = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/style_glow.gdshader") };

		_materials = new Dictionary<Surf, Material>
		{
			[Surf.Paint] = paint,
			[Surf.Metal] = metal,
			[Surf.Glass] = glass,
			[Surf.Glow] = glow,
		};
	}
}

using Godot;

/// <summary>
/// One imported model placed by <see cref="ModelBaseBuilder"/>. Every field is editable in the
/// Inspector (expand the entry in the builder's Placements list). Position / Rotation / Scale are
/// applied live in the editor, so you can nudge models apart without rebuilding.
/// </summary>
[Tool]
[GlobalClass]
public partial class ModelPlacement : Resource
{
	public enum FitMode
	{
		/// <summary>Use the model's native size (times UnitScale and Scale).</summary>
		None,
		/// <summary>Uniformly scale so the model's height = FitSize.</summary>
		Height,
		/// <summary>Uniformly scale so the model's width (local X) = FitSize.</summary>
		Width,
		/// <summary>Uniformly scale so the larger horizontal side (X or Z) = FitSize.</summary>
		Footprint,
	}

	public enum CollisionMode
	{
		None,
		/// <summary>One BoxShape3D around the fitted model (cheap, good for props).</summary>
		Box,
		/// <summary>One convex hull per mesh (tighter, still cheap).</summary>
		Convex,
	}

	[Export] public string Label = "Model";
	[Export(PropertyHint.File, "*.glb,*.gltf,*.tscn,*.scn")] public string ScenePath = "";
	[Export] public bool Enabled = true;

	[ExportGroup("Transform (live in editor)")]
	/// <summary>Position, relative to <see cref="ParentPath"/> (scene root when empty).</summary>
	[Export] public Vector3 Position;
	/// <summary>Rotation in degrees (Y = turn left/right).</summary>
	[Export] public Vector3 RotationDegrees;
	/// <summary>Extra uniform scale on top of the fit.</summary>
	[Export(PropertyHint.Range, "0.01,20,0.01,or_greater")] public float Scale = 1f;

	[ExportGroup("Normalize")]
	[Export] public FitMode Fit = FitMode.None;
	[Export] public float FitSize = 1f;
	/// <summary>Multiplier applied before fitting (e.g. 0.01 for a model exported in centimetres).</summary>
	[Export] public float UnitScale = 1f;
	/// <summary>Move the model so its X/Z bounding-box centre sits on the origin (fixes off-centre pivots).</summary>
	[Export] public bool CenterXZ = true;
	/// <summary>Move the model so its lowest point sits on the origin (fixes sunken / floating pivots).</summary>
	[Export] public bool GroundY = true;
	/// <summary>Mesh nodes whose name contains any of these are removed (e.g. "Door module" to open a corridor end).</summary>
	[Export] public string[] ExcludeMeshNames = System.Array.Empty<string>();

	[ExportGroup("Scene integration")]
	/// <summary>Node to attach to, relative to the scene root (e.g. "Player/Visual", "LunarRover"). Empty = scene root.</summary>
	[Export] public string ParentPath = "";
	/// <summary>Graybox nodes to hide while this model is present (scene-root relative). Their collision is kept.</summary>
	[Export] public string[] HideNodes = System.Array.Empty<string>();
	/// <summary>Hide every other visible mesh/sprite under the parent (e.g. the old rover CSG, the player sprite).</summary>
	[Export] public bool HideOtherGeometryUnderParent;
	[Export] public CollisionMode Collision = CollisionMode.None;
	/// <summary>Physics layers for the generated collision (bit 1 = world, bit 4 = camera_blocker).</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint CollisionLayer = 1 | 8;
	/// <summary>AnimationPlayer animation to autoplay on loop (empty = first one, "-" = none).</summary>
	[Export] public string Animation = "";
	/// <summary>Cast shadows from both faces (walls/corridors: stops the sun leaking through).</summary>
	[Export] public bool DoubleSidedShadows;
	/// <summary>Node group added to the instance (e.g. "player_model").</summary>
	[Export] public string Group = "";

	/// <summary>
	/// Old placeholder nodes this model REPLACES (scene-root relative): hidden AND their collision is
	/// switched off, so the player doesn't bump into an invisible old rocket. Restored by "Remove Models".
	/// </summary>
	[Export] public string[] RetireNodes = System.Array.Empty<string>();

	[ExportGroup("Minigame trigger")]
	/// <summary>
	/// AeroKidz room this model opens when the player presses E at it (e.g. "Telescope", "Rocket").
	/// Empty = no trigger. Creates an Area3D + MinigameTrigger3D child; see Scripts/Models/MinigameTrigger3D.cs.
	/// </summary>
	[Export] public string TriggerRoom = "";
	/// <summary>Where the player stands to use it, relative to the model's position (rotated with it).</summary>
	[Export] public Vector3 TriggerOffset;
	/// <summary>Radius of the trigger Area3D (body_entered / body_exited fire inside it).</summary>
	[Export] public float TriggerRadius = 2.5f;
	/// <summary>Text shown when the player is close (empty = automatic).</summary>
	[Export] public string TriggerPrompt = "";

	[ExportGroup("Corridor tiling")]
	/// <summary>If &gt; 0: copies are laid end to end along local Z and stretched to exactly fill this length (m).</summary>
	[Export] public float FillLength;
	/// <summary>Adds invisible camera-blocker boxes (ceiling + walls) matching the model's interior.</summary>
	[Export] public bool InteriorCameraBlockers;
	/// <summary>Interior half width, height and floor height in the model's native units (before scaling).</summary>
	[Export] public Vector3 InteriorHalfWidthHeightFloor = new(2.3f, 2.48f, 0f);

	public ModelPlacement() { }

	public ModelPlacement(string label, string path, Vector3 pos, Vector3 rot, FitMode fit, float fitSize,
		CollisionMode collision = CollisionMode.Box, string parent = "", params string[] hide)
	{
		Label = label;
		ScenePath = path;
		Position = pos;
		RotationDegrees = rot;
		Fit = fit;
		FitSize = fitSize;
		Collision = collision;
		ParentPath = parent;
		HideNodes = hide;
	}
}

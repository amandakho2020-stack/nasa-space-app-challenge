using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One-click editor tool that generates the whole Lunar Outpost graybox:
///   - LunarSurface : 600 m regolith CSGBox3D (FastNoiseLite StandardMaterial3D), carved craters,
///                    distant hills, and MultiMesh rock fields (noise-distorted low-poly rocks)
///   - Base         : connected CSG tubes and rooms (Lobby, Oxygen/Power/Shield wing,
///                    Drill/Plant/Telescope wing, Rover Bay airlock) with interiors and lights
///   - Lobby        : command-center interior (slanted desks, emissive screens with Label3D text,
///                    overhead monitor bank, windows facing the Rover Bay)
///   - WorldEnvironment + Sun : airless lighting (ACES, black ambient, hard high-res shadows)
///   - LunarRover   : drivable VehicleBody3D (or a static CSG scale reference)
///
/// Usage: select the EnvironmentBuilder node, click "Build Lunar Environment" in the Inspector,
/// then save the scene (Ctrl+S). Re-running replaces everything it generated.
///
/// The implementation is split across partial files:
///   EnvironmentBuilder.Surface.cs, .Base.cs, .Lobby.cs, .Rover.cs
/// </summary>
[Tool]
public partial class EnvironmentBuilder : Node
{
	public enum RoverBuildMode
	{
		Drivable,
		StationaryScaleReference,
		None,
	}

	public enum RegolithMaterialStyle
	{
		/// <summary>StandardMaterial3D with NoiseTexture2D albedo/normal/detail maps (world triplanar).</summary>
		StandardMaterial,
		/// <summary>Custom spatial shader (Shaders/regolith.gdshader) with multi-scale blending.</summary>
		CustomShader,
	}

	private const string RegolithShaderPath = "res://Shaders/regolith.gdshader";
	private const string SkyShaderPath = "res://Shaders/space_sky.gdshader";
	private const string RoverScriptPath = "res://Scripts/RoverController.cs";
	private const string ConsoleScriptPath = "res://Scripts/MissionConsole.cs";
	private const string AirlockScriptPath = "res://Scripts/AirlockDoor.cs";
	private const string HubZoneScriptPath = "res://Scripts/HubZone.cs";
	private const string LiveScreenScriptPath = "res://Scripts/LiveScreen.cs";

	/// <summary>Top-level nodes from the previous outdoor-module layout, removed on build.</summary>
	private static readonly string[] LegacyNodes = { "OxygenRoom", "Paths", "BioDome", "CoreDrill", "CommTower" };

	[ExportToolButton("Build Lunar Environment", Icon = "WorldEnvironment")]
	public Callable BuildButton => Callable.From(Build);

	[ExportGroup("Surface")]
	[Export] public string MaterialsDir = "res://Materials";
	[Export] public RegolithMaterialStyle RegolithStyle = RegolithMaterialStyle.StandardMaterial;
	[Export] public Color RegolithBase = new Color("5a5a5a");
	[Export] public float GroundSize = 600f;
	[Export] public int CraterCount = 14;
	[Export] public int HillCount = 20;
	[Export] public int NoiseSeed = 1969;

	[ExportGroup("Rock Scattering")]
	[Export] public int SmallRockCount = 900;
	[Export] public int MediumRockCount = 180;
	[Export] public int BoulderCount = 30;
	/// <summary>Rocks are scattered inside this radius (m) around the base, outside its footprint.</summary>
	[Export] public float ScatterRadius = 225f;
	/// <summary>Extra rock-free margin (m) around every room, corridor and pad.</summary>
	[Export] public float BaseClearMargin = 5f;

	[ExportGroup("Sky & Ambient")]
	/// <summary>On: procedural starfield sky. Off: pure black Custom Color background.</summary>
	[Export] public bool UseStarfield = true;
	/// <summary>Faint dark-gray fill so moon areas in shadow stay dimly visible (was near-black 0.02).</summary>
	[Export] public Color AmbientColor = new Color(0.11f, 0.11f, 0.125f);
	[Export] public float AmbientEnergy = 1f;
	[Export] public float Exposure = 1f;

	[ExportGroup("Sun")]
	[Export] public float SunEnergy = 5f;
	/// <summary>Low sun = long Apollo-style shadows.</summary>
	[Export] public float SunElevationDegrees = 20f;
	[Export] public float SunAzimuthDegrees = 150f;
	/// <summary>Sun disc size in degrees: 0 = razor-sharp shadows, 0.53 = the real sun (slight soft penumbra).</summary>
	[Export(PropertyHint.Range, "0,2,0.01")] public float SunAngularDiameter = 0.53f;
	/// <summary>Shadow darkness (1 = pitch black). Below 1 lets the ambient read through Apollo-style shadows.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float SunShadowOpacity = 0.82f;
	/// <summary>Writes an 8192 shadow atlas + "Soft Low" shadow filter to Project Settings.</summary>
	[Export] public bool ApplyHardShadowProjectSettings = true;

	[ExportGroup("Base")]
	[Export] public bool BuildBase = true;
	/// <summary>Tubes that use corridor.glb (placed by ModelBaseBuilder) instead of a procedural shell. EastCorridor and RoverBayTube form a T-junction a straight module cannot represent, so they stay procedural.</summary>
	[Export] public string[] ModelCorridorTubes = { "WestCorridor", "OxygenTube", "PowerTube", "ShieldTube", "PlantTube", "TelescopeTube" };
	/// <summary>Physics layers for walls and corridor shells: 1 = world, 4 = camera_blocker (spring arms collide with both).</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint WallCollisionLayers = 1u | 8u;

	private bool IsModelCorridor(string tube) => ModelCorridorTubes != null && System.Array.IndexOf(ModelCorridorTubes, tube) >= 0;
	/// <summary>Deletes the old outdoor modules (OxygenRoom, Paths, BioDome, CoreDrill, CommTower).</summary>
	[Export] public bool RemoveLegacyModules = true;
	/// <summary>Moves the Player into the Command Center lobby, facing the monitor bank.</summary>
	[Export] public bool MovePlayerToLobby = true;

	/// <summary>Remote drill rig on the open surface. Keep it 150-200 m from the base so the rover is needed.</summary>
	[Export] public Vector3 DrillSitePosition = new Vector3(13f, 0f, 200f);

	[ExportGroup("Rover")]
	[Export] public RoverBuildMode Rover = RoverBuildMode.Drivable;
	/// <summary>Chassis centre. Y is adjusted automatically so the wheels sit on y = 0.</summary>
	[Export] public Vector3 RoverPosition = new Vector3(13f, 0f, 21f);
	/// <summary>0 = nose pointing south (+Z), out along the road to the Drill Site.</summary>
	[Export] public float RoverYawDegrees = 0f;

	// ----- Build-time state -----
	/// <summary>Footprints (XZ) that must stay free of rocks and craters.</summary>
	private readonly List<Rect2> _clearZones = new();
	/// <summary>Scripts to attach after the node tree is complete (SetScript replaces the C# wrapper).</summary>
	private readonly List<(Node node, string script, Dictionary<string, Variant> props)> _pendingScripts = new();
	private readonly Dictionary<Color, StandardMaterial3D> _screenMaterials = new();
	private Material _regolith;

	public void Build()
	{
		Node root = Owner ?? GetParent();
		if (root == null)
		{
			GD.PushError("EnvironmentBuilder: add this node to a scene first.");
			return;
		}

		_clearZones.Clear();
		_pendingScripts.Clear();
		_screenMaterials.Clear();
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(MaterialsDir));

		if (RemoveLegacyModules)
			foreach (string legacy in LegacyNodes)
				RemoveIfExists(root, legacy);

		// Regolith material first (the base uses it for the shelter berms), then the base
		// (it registers the clear zones the rock scatter must avoid), then the surface.
		_regolith = RegolithStyle == RegolithMaterialStyle.StandardMaterial
			? BuildRegolithStandardMaterial()
			: BuildRegolithShaderMaterial();
		if (BuildBase)
			BuildBaseLayout(root);
		else
			RemoveIfExists(root, "Base");

		BuildSurface(root);

		ApplyEnvironment(root, BuildEnvironment());
		ConfigureSun(root);
		if (ApplyHardShadowProjectSettings)
			ApplyShadowSettings();

		BuildRover(root);

		if (MovePlayerToLobby && BuildBase)
			PlacePlayer(root);

		AttachPendingScripts();

		GD.Print($"EnvironmentBuilder: done ({SmallRockCount + MediumRockCount + BoulderCount} rocks, {CraterCount} craters, base = {BuildBase}, rover = {Rover}). " +
			"Save the scene (Ctrl+S) to keep the changes.");
	}

	// ======================================================================
	//  Environment (vacuum lighting)
	// ======================================================================

	private Environment BuildEnvironment()
	{
		var env = new Environment();

		if (UseStarfield)
		{
			var skyMat = new ShaderMaterial { Shader = GD.Load<Shader>(SkyShaderPath) };
			skyMat.SetShaderParameter("band_noise", Noise(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.01f, 5, 512, 7));
			env.BackgroundMode = Environment.BGMode.Sky;
			env.Sky = new Sky
			{
				SkyMaterial = skyMat,
				RadianceSize = Sky.RadianceSizeEnum.Size32, // the sky never lights the scene; keep it cheap
			};
		}
		else
		{
			env.BackgroundMode = Environment.BGMode.Color;
			env.BackgroundColor = Colors.Black;
		}

		// No atmosphere = no scattered skylight: shadows are pitch black.
		env.AmbientLightSource = Environment.AmbientSource.Color;
		env.AmbientLightColor = AmbientColor;
		env.AmbientLightEnergy = AmbientEnergy;
		env.ReflectedLightSource = Environment.ReflectionSource.Sky; // black sky; indoor ReflectionProbes supply reflections

		// Indoor depth: ambient occlusion in corners + screen-space bounce light off the metal walls.
		env.SsaoEnabled = true;
		env.SsaoRadius = 1.2f;
		env.SsaoIntensity = 1.6f;
		env.SsilEnabled = true;
		env.SsilRadius = 4f;
		env.SsilIntensity = 1.1f;

		// ACES (Godot 4's fitted ACES curve): sunlit regolith clips to white like Apollo film.
		env.TonemapMode = Environment.ToneMapper.Aces;
		env.TonemapExposure = Exposure;

		// Glow only on very bright pixels (sun disk, screens, emissive lights), not on lit dust.
		env.GlowEnabled = true;
		env.GlowHdrThreshold = 2.5f;
		env.GlowIntensity = 0.5f;

		env.FogEnabled = false;
		env.VolumetricFogEnabled = false;

		return SaveResource(env, "lunar_environment.tres");
	}

	private static void ApplyEnvironment(Node root, Environment env)
	{
		WorldEnvironment we = FindChildOfType<WorldEnvironment>(root);
		if (we == null)
		{
			we = new WorldEnvironment { Name = "WorldEnvironment" };
			root.AddChild(we);
			root.MoveChild(we, 0);
			we.Owner = root;
		}
		we.Environment = env;
	}

	// ======================================================================
	//  Sun
	// ======================================================================

	private void ConfigureSun(Node root)
	{
		DirectionalLight3D sun = root.GetNodeOrNull<DirectionalLight3D>("Sun") ?? FindChildOfType<DirectionalLight3D>(root);
		if (sun == null)
		{
			sun = new DirectionalLight3D { Name = "Sun" };
			root.AddChild(sun);
			sun.Owner = root;
		}

		sun.Position = new Vector3(0, 30, 0);
		sun.RotationDegrees = new Vector3(-SunElevationDegrees, SunAzimuthDegrees, 0);

		sun.LightColor = new Color(1f, 0.98f, 0.95f); // unfiltered sunlight, almost pure white
		sun.LightEnergy = SunEnergy;                  // "blinding": washes lit dust out under ACES
		sun.LightSpecular = 0.3f;
		sun.LightAngularDistance = SunAngularDiameter; // 0 disables PCSS soft shadows

		sun.ShadowEnabled = true;
		sun.ShadowOpacity = SunShadowOpacity; // softened: shadows are dark but not pitch black
		sun.ShadowBlur = 1.2f;    // gentle edge softening
		sun.ShadowBias = 0.04f;
		sun.ShadowNormalBias = 1.0f;
		sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
		sun.DirectionalShadowMaxDistance = 150f;
		sun.DirectionalShadowBlendSplits = true;
		sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky;
	}

	private static void ApplyShadowSettings()
	{
		const string sizeKey = "rendering/lights_and_shadows/directional_shadow/size";
		const string filterKey = "rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality";
		ProjectSettings.SetSetting(sizeKey, 8192);
		ProjectSettings.SetSetting(filterKey, 2); // 2 = Soft Low

		RenderingServer.DirectionalShadowAtlasSetSize(8192, true);
		RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);

		if (Engine.IsEditorHint())
			ProjectSettings.Save();
	}

	// ======================================================================
	//  Player
	// ======================================================================

	private static void PlacePlayer(Node root)
	{
		var player = root.GetNodeOrNull<Node3D>("Player");
		if (player == null)
			return;
		player.Position = new Vector3(0f, BaseFloor + 0.05f, 5f);
		if (player.GetNodeOrNull<Node3D>("Visual") is { } visual)
			visual.Rotation = Vector3.Zero;          // face north, toward the monitor bank
		if (player.GetNodeOrNull<Node3D>("CameraPivot") is { } pivot)
			pivot.Rotation = Vector3.Zero;           // camera behind the player, looking north
	}

	// ======================================================================
	//  Shared helpers
	// ======================================================================

	private T SaveResource<T>(T resource, string fileName) where T : Resource
	{
		string path = MaterialsDir.PathJoin(fileName);
		Error err = ResourceSaver.Save(resource, path);
		if (err != Error.Ok)
		{
			GD.PushError($"EnvironmentBuilder: could not save {path} ({err}).");
			return resource;
		}
		resource.TakeOverPath(path); // the scene references the file instead of embedding a copy
		return resource;
	}

	private void QueueScript(Node node, string scriptPath, Dictionary<string, Variant> props = null)
	{
		_pendingScripts.Add((node, scriptPath, props ?? new Dictionary<string, Variant>()));
	}

	/// <summary>
	/// Attaches scripts last. SetScript swaps the managed C# wrapper, so the node is re-fetched by
	/// instance id before exported properties are assigned (works in the editor for non-tool scripts too).
	/// </summary>
	private void AttachPendingScripts()
	{
		foreach (var (node, scriptPath, props) in _pendingScripts)
		{
			ulong id = node.GetInstanceId();
			node.SetScript(GD.Load<Script>(scriptPath));
			GodotObject fresh = GodotObject.InstanceFromId(id);
			foreach (var (key, value) in props)
				fresh.Set(key, value);
		}
		_pendingScripts.Clear();
	}

	private static void RemoveIfExists(Node root, string name)
	{
		Node old = root.GetNodeOrNull(name);
		if (old == null)
			return;
		root.RemoveChild(old);
		old.Free();
	}

	private static StandardMaterial3D Mat(Color color, float metallic = 0f, float roughness = 0.8f) => new()
	{
		AlbedoColor = color,
		Metallic = metallic,
		Roughness = roughness,
	};

	private static StandardMaterial3D Emissive(Color color, float energy = 2f) => new()
	{
		AlbedoColor = color,
		EmissionEnabled = true,
		Emission = color,
		EmissionEnergyMultiplier = energy,
	};

	private static StandardMaterial3D Glass(Color tint, float alpha) => new()
	{
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		AlbedoColor = new Color(tint, alpha),
		Roughness = 0.05f,
		MetallicSpecular = 0.8f,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};

	private static CsgBox3D Box(Node parent, string name, Vector3 size, Vector3 position, Material material,
		bool collision = false, Vector3? rotationDegrees = null)
	{
		var box = new CsgBox3D
		{
			Name = name,
			Size = size,
			Position = position,
			Material = material,
			UseCollision = collision,
			RotationDegrees = rotationDegrees ?? Vector3.Zero,
		};
		parent.AddChild(box);
		return box;
	}

	private static CsgCylinder3D Cyl(Node parent, string name, float radius, float height, Vector3 position,
		Vector3 rotationDegrees, Material material, bool cone = false, int sides = 20, bool collision = false)
	{
		var cyl = new CsgCylinder3D
		{
			Name = name,
			Radius = radius,
			Height = height,
			Position = position,
			RotationDegrees = rotationDegrees,
			Material = material,
			Cone = cone,
			Sides = sides,
			UseCollision = collision,
		};
		parent.AddChild(cyl);
		return cyl;
	}

	private static Label3D Label(Node parent, string name, string text, Vector3 position, Vector3 rotationDegrees,
		float pixelSize, int fontSize, Color color, bool billboard = false, uint layers = 1, int outline = 12)
	{
		var label = new Label3D
		{
			Name = name,
			Text = text,
			Position = position,
			RotationDegrees = rotationDegrees,
			PixelSize = pixelSize,
			FontSize = fontSize,
			Modulate = color,
			OutlineSize = outline,
			Billboard = billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled,
			DoubleSided = billboard, // flat signs are readable from the front only (no mirrored text from behind)
			Layers = layers,
		};
		parent.AddChild(label);
		return label;
	}

	private static OmniLight3D Omni(Node parent, string name, Vector3 position, Color color, float energy, float range)
	{
		var light = new OmniLight3D
		{
			Name = name,
			Position = position,
			LightColor = color,
			LightEnergy = energy,
			OmniRange = range,
			ShadowEnabled = false,
		};
		parent.AddChild(light);
		return light;
	}

	private static void SetOwnerRecursive(Node node, Node owner)
	{
		foreach (Node child in node.GetChildren())
		{
			child.Owner = owner;
			SetOwnerRecursive(child, owner);
		}
		if (node != owner)
			node.Owner = owner;
	}

	private static T FindChildOfType<T>(Node root) where T : Node
	{
		foreach (Node child in root.GetChildren())
			if (child is T match)
				return match;
		return null;
	}

	/// <summary>Yaw (degrees) that turns a node's +Z axis to face along <paramref name="dir"/> (XZ).</summary>
	private static float YawFacing(Vector3 dir) => Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));

	private bool InClearZone(Vector2 p, float extra)
	{
		foreach (Rect2 r in _clearZones)
			if (r.Grow(extra).HasPoint(p))
				return true;
		return false;
	}

	private void AddClearZone(Vector3 center, Vector2 size)
	{
		_clearZones.Add(new Rect2(center.X - size.X / 2f, center.Z - size.Y / 2f, size.X, size.Y).Grow(BaseClearMargin));
	}
}

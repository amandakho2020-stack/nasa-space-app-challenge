using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Places the final imported models (res://Art/models/*.glb) into the scene and hides the graybox
/// they replace. Attach to a Node3D at the scene root, then use the Inspector buttons:
///
///   "Reset Placements To Defaults" - fills <see cref="Placements"/> with one entry per model
///                                     (plus one corridor entry per model-corridor tube)
///   "Build Models"                 - instantiates every enabled entry (rebuilds from scratch)
///   "Remove Models"                - deletes the instances and un-hides the graybox
///
/// Each entry exposes Position / RotationDegrees / Scale. With <see cref="LiveUpdate"/> on, editing
/// them moves the model immediately in the editor - no rebuild needed. Fit, pivot, collision and
/// hide settings need a rebuild.
///
/// Node layout per entry:
///   Model_&lt;n&gt;_&lt;Label&gt; (Node3D, Position/Rotation/Scale from the entry)  [under ParentPath]
///   ├── Fit (Node3D, auto scale + pivot correction)          one per tiled copy
///   │   ├── &lt;glb instance&gt;
///   │   └── Collision (StaticBody3D)                          if Collision != None
///   ├── CameraBlockers (StaticBody3D, layer camera_blocker)   corridors only
///   └── AnimLoop (ModelAnimLoop)                              if the model has an animation
/// </summary>
[Tool]
public partial class ModelBaseBuilder : Node3D
{
	public const string Models = "res://Art/models/";
	public const string InstanceGroup = "model_builder_instance";
	private const string HiddenGroup = "model_builder_hidden";
	private const string MetaLayers = "mb_layers";
	private const string MetaVisible = "mb_visible";
	private const string MetaCollisionLayer = "mb_collision_layer";
	private const string MetaCsgCollision = "mb_csg_collision";
	private const string MetaIndex = "mb_index";

	/// <summary>Physics layer 4 ("camera_blocker"): only the camera spring arms collide with it.</summary>
	public const uint CameraBlockerLayer = 1u << 3;

	[ExportToolButton("Build Models", Icon = "PackedScene")]
	public Callable BuildButton => Callable.From(Build);

	[ExportToolButton("Remove Models", Icon = "Remove")]
	public Callable RemoveButton => Callable.From(RemoveAll);

	[ExportToolButton("Reset Placements To Defaults", Icon = "Reload")]
	public Callable ResetButton => Callable.From(ResetDefaults);

	[Export] public Godot.Collections.Array<ModelPlacement> Placements = new();

	/// <summary>Apply Position / Rotation / Scale edits to the placed models live in the editor.</summary>
	[Export] public bool LiveUpdate = true;

	private Node _root;
	private float _liveTimer;

	// ======================================================================
	//  Defaults (positions match the EnvironmentBuilder base layout)
	// ======================================================================

	public void ResetDefaults()
	{
		_root = SceneRoot();
		const float f = 0.25f; // interior floor height
		var list = new List<ModelPlacement>
		{
			new("Astronaut (player)", Models + "astronaut(character).glb", Vector3.Zero, new Vector3(0, 180, 0),
				ModelPlacement.FitMode.Height, 1.75f, ModelPlacement.CollisionMode.None, "Player/Visual")
			{ HideOtherGeometryUnderParent = true, Group = "player_model" },

			new("Lunar Rover", Models + "Lunar_rover (Transport Outdoor).glb", new Vector3(0, -0.76f, 0), new Vector3(0, -90, 0),
				ModelPlacement.FitMode.None, 1f, ModelPlacement.CollisionMode.None, "LunarRover")
			{ HideOtherGeometryUnderParent = true },

			new("Communication Tower", Models + "Communication tower.glb", new Vector3(20f, 0f, -17f), Vector3.Zero,
				ModelPlacement.FitMode.None, 1f, ModelPlacement.CollisionMode.Box, "", "Base/CommTower"),

			new("Control Room Monitor (lobby)", Models + "control_room_monitor(lobby, indoor).glb", new Vector3(0f, f + 2.35f, -9.35f), new Vector3(0, -90, 0),
				ModelPlacement.FitMode.Footprint, 6.0f, ModelPlacement.CollisionMode.None, "", "Base/Interiors/CommandCenter/MonitorBank"),

			new("Holo Table (lobby)", Models + "holo-table.glb", new Vector3(0f, f, 0f), Vector3.Zero,
				ModelPlacement.FitMode.Footprint, 2.6f, ModelPlacement.CollisionMode.Box, "", "Base/Interiors/CommandCenter/HoloProjector"),

			new("Computer Desk West (lobby)", Models + "computer desk (lobby).glb", new Vector3(-5.6f, f, 5.4f), new Vector3(0, 20, 0),
				ModelPlacement.FitMode.Footprint, 3.4f, ModelPlacement.CollisionMode.Box),

			new("Computer Desk East (lobby)", Models + "computer desk (lobby).glb", new Vector3(5.6f, f, 5.4f), new Vector3(0, -20, 0),
				ModelPlacement.FitMode.Footprint, 3.4f, ModelPlacement.CollisionMode.Box),

			new("Oxygen Station (Oxygen room)", Models + "oxygen_station (Indoor, Oxygen room).glb", new Vector3(-31.6f, f, -24.5f), new Vector3(0, -90, 0),
				ModelPlacement.FitMode.Height, 3.0f, ModelPlacement.CollisionMode.Box),

			new("Monitor (Power room)", Models + "monitor (power room).glb", new Vector3(-58.6f, f, -6.35f), Vector3.Zero,
				ModelPlacement.FitMode.None, 1f, ModelPlacement.CollisionMode.None) { GroundY = false },

			new("Shield Generator (Shield room)", Models + "shield_generator.glb", new Vector3(-36f, f, 26.6f), Vector3.Zero,
				ModelPlacement.FitMode.Height, 3.2f, ModelPlacement.CollisionMode.Box),

			new("Solar Panel Field 5x10", Models + "solar_panel_5x10.glb", new Vector3(-86f, 0f, 0f), Vector3.Zero,
				ModelPlacement.FitMode.Footprint, 20f, ModelPlacement.CollisionMode.Box, "", "Base/SolarField"),

			new("Solar Station", Models + "Solar station .glb", new Vector3(-86f, 0f, -24f), Vector3.Zero,
				ModelPlacement.FitMode.None, 1f, ModelPlacement.CollisionMode.Box),

			// Swapped in 2026-11-15: replaces the old rocket (Base/LaunchPad). Press E at its ladder to fly home.
			LunarModulePlacement(),
			// Swapped in 2026-11-15: replaces the old telescope (ring scanner) in the Telescope room. Starts Mission 3.
			SpitzerTelescopePlacement(),

			// Removed 2026-11-15 (outdoor hallway next to the Rover Bay was not used):
			// new("Hallway Kit (Rover Bay annex)", Models + "hallway.glb", new Vector3(26f, 0f, 21f), new Vector3(0, 90, 0),
			//	ModelPlacement.FitMode.None, 1f, ModelPlacement.CollisionMode.Box) { UnitScale = 0.01f, Scale = 1.5f },
		};

		// One corridor.glb run per model-corridor tube (EnvironmentBuilder tags them with span metadata).
		var corridors = _root?.GetNodeOrNull("Base/Interiors/Corridors");
		if (corridors != null)
		{
			foreach (Node n in corridors.GetChildren())
			{
				if (!n.HasMeta("span_a") || !n.GetMeta("model_corridor", false).AsBool())
					continue;
				string tube = n.GetMeta("tube_name").AsString();
				Vector3 a = n.GetMeta("span_a").AsVector3(), b = n.GetMeta("span_b").AsVector3();
				Vector3 d = b - a;
				var mid = (a + b) / 2f;
				list.Add(new ModelPlacement($"Corridor: {tube}", Models + "corridor.glb", new Vector3(mid.X, 0.27f, mid.Z),
					new Vector3(0, Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)), 0), ModelPlacement.FitMode.Width, 4.33f,
					ModelPlacement.CollisionMode.None, "",
					$"Base/Interiors/Corridors/{tube}Structure", $"Base/Interiors/Corridors/{tube}Trim")
				{
					GroundY = false,
					ExcludeMeshNames = new[] { "Door module" },
					FillLength = d.Length(),
					DoubleSidedShadows = true,
					InteriorCameraBlockers = true,
				});
			}
		}

		Placements = new Godot.Collections.Array<ModelPlacement>(list);
		NotifyPropertyListChanged();
		GD.Print($"ModelBaseBuilder: {Placements.Count} default placements. Click 'Build Models'.");
	}

	// ======================================================================
	//  Build / remove
	// ======================================================================

	public void Build()
	{
		_root = SceneRoot();
		if (_root == null)
		{
			GD.PushError("ModelBaseBuilder: add this node to a scene first.");
			return;
		}
		if (Placements.Count == 0)
			ResetDefaults();

		RemoveAll();
		int built = 0;
		for (int i = 0; i < Placements.Count; i++)
		{
			ModelPlacement p = Placements[i];
			if (p == null || !p.Enabled || string.IsNullOrEmpty(p.ScenePath))
				continue;
			if (BuildOne(p, i))
				built++;
		}
		GD.Print($"ModelBaseBuilder: placed {built} model entries. Save the scene (Ctrl+S).");
	}

	public void RemoveAll()
	{
		_root = SceneRoot();
		if (_root == null)
			return;
		var all = new List<Node>();
		Collect(_root, all);
		foreach (Node n in all.Where(n => n.IsInGroup(InstanceGroup)).ToList())
		{
			if (!IsInstanceValid(n))
				continue;
			n.GetParent()?.RemoveChild(n);
			n.Free();
		}
		foreach (Node n in all.Where(n => IsInstanceValid(n) && n.IsInGroup(HiddenGroup)))
			Unhide(n);
	}

	private bool BuildOne(ModelPlacement p, int index)
	{
		if (!ResourceLoader.Exists(p.ScenePath))
		{
			GD.PushWarning($"ModelBaseBuilder: {p.Label}: '{p.ScenePath}' is missing or not imported yet - skipped (the old placeholder stays).");
			return false;
		}
		var scene = GD.Load<PackedScene>(p.ScenePath);
		if (scene == null)
		{
			GD.PushError($"ModelBaseBuilder: cannot load '{p.ScenePath}' ({p.Label}).");
			return false;
		}
		Node parent = string.IsNullOrEmpty(p.ParentPath) ? _root : _root.GetNodeOrNull(p.ParentPath);
		if (parent == null)
		{
			GD.PushWarning($"ModelBaseBuilder: parent '{p.ParentPath}' not found for {p.Label}; skipped.");
			return false;
		}

		var holder = new Node3D { Name = $"Model_{index:00}_{Sanitize(p.Label)}" };
		holder.SetMeta(MetaIndex, index);
		parent.AddChild(holder);
		holder.Owner = _root;
		holder.AddToGroup(InstanceGroup, true);
		ApplyTransform(holder, p);

		// Measure one copy (after excluding meshes) to compute fit + pivot correction.
		Node3D probe = scene.Instantiate<Node3D>();
		RemoveExcluded(probe, p.ExcludeMeshNames);
		Aabb box = MeasureAabb(probe);
		probe.Free();
		box = new Aabb(box.Position * p.UnitScale, box.Size * p.UnitScale);

		float fit = p.Fit switch
		{
			ModelPlacement.FitMode.Height => p.FitSize / Mathf.Max(box.Size.Y, 1e-4f),
			ModelPlacement.FitMode.Width => p.FitSize / Mathf.Max(box.Size.X, 1e-4f),
			ModelPlacement.FitMode.Footprint => p.FitSize / Mathf.Max(Mathf.Max(box.Size.X, box.Size.Z), 1e-4f),
			_ => 1f,
		};
		float s = p.UnitScale * fit;
		Vector3 center = box.GetCenter() * fit;
		var pivot = new Vector3(p.CenterXZ ? -center.X : 0f, p.GroundY ? -box.Position.Y * fit : 0f, p.CenterXZ ? -center.Z : 0f);
		float length = box.Size.Z * fit;

		int copies = 1;
		float stretch = 1f;
		if (p.FillLength > 0f && length > 0.01f)
		{
			copies = Mathf.Max(1, Mathf.RoundToInt(p.FillLength / length));
			stretch = p.FillLength / (copies * length);
		}

		AnimationPlayer anim = null;
		for (int c = 0; c < copies; c++)
		{
			float z = copies == 1 ? 0f : -p.FillLength / 2f + (c + 0.5f) * p.FillLength / copies;
			var fitNode = new Node3D
			{
				Name = copies == 1 ? "Fit" : $"Fit{c}",
				Position = new Vector3(pivot.X, pivot.Y, pivot.Z * stretch + z),
				Scale = new Vector3(s, s, s * stretch),
			};
			holder.AddChild(fitNode);
			fitNode.Owner = _root;

			Node3D inst = scene.Instantiate<Node3D>(PackedScene.GenEditState.Instance);
			inst.Name = "Model";
			fitNode.AddChild(inst);
			inst.Owner = _root;
			_root.SetEditableInstance(inst, true); // lets per-node overrides (excluded meshes, shadows) be saved
			RemoveExcluded(inst, p.ExcludeMeshNames, hideInstead: true);
			if (p.DoubleSidedShadows)
				foreach (var g in Descendants<GeometryInstance3D>(inst))
					g.CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
			if (!string.IsNullOrEmpty(p.Group))
				inst.AddToGroup(p.Group, true);
			anim ??= Descendants<AnimationPlayer>(inst).FirstOrDefault();

			if (p.Collision != ModelPlacement.CollisionMode.None)
				AddCollision(fitNode, inst, p, box, fit);
		}

		if (p.InteriorCameraBlockers)
			AddCameraBlockers(holder, p, s, pivot.Y, p.FillLength > 0f ? p.FillLength : length);

		if (anim != null && p.Animation != "-")
		{
			string name = string.IsNullOrEmpty(p.Animation) ? anim.GetAnimationList().FirstOrDefault() : p.Animation;
			if (!string.IsNullOrEmpty(name))
			{
				var loop = new Node { Name = "AnimLoop" };
				holder.AddChild(loop);
				loop.Owner = _root;
				ulong id = loop.GetInstanceId(); // SetScript disposes this C# wrapper, so grab the id first
				loop.SetScript(GD.Load<Script>("res://Scripts/Models/ModelAnimLoop.cs"));
				GodotObject.InstanceFromId(id).Set("AnimationName", name);
			}
		}

		// Retire the old placeholders this model replaces (hidden + collision off, restorable).
		foreach (string path in p.RetireNodes ?? Array.Empty<string>())
			if (_root.GetNodeOrNull(path) is { } old)
				Retire(old);
		if (!string.IsNullOrEmpty(p.TriggerRoom))
			AddTrigger(holder, p);

		// Hide the graybox this model replaces (rendering only; collision stays active).
		foreach (string path in p.HideNodes ?? Array.Empty<string>())
			if (_root.GetNodeOrNull(path) is { } target)
				Hide(target);
		if (p.HideOtherGeometryUnderParent)
			foreach (var g in Descendants<GeometryInstance3D>(parent))
				if (!IsUnder(g, holder))
					Hide(g);
		return true;
	}

	private void AddCollision(Node3D fitNode, Node3D inst, ModelPlacement p, Aabb box, float fit)
	{
		var body = new StaticBody3D { Name = "Collision", CollisionLayer = p.CollisionLayer, CollisionMask = 0 };
		fitNode.AddChild(body);
		body.Owner = _root;
		if (p.Collision == ModelPlacement.CollisionMode.Box)
		{
			// box is in UnitScale-scaled model space; the fit node scale is UnitScale*fit, so divide UnitScale back out.
			var native = new Aabb(box.Position / p.UnitScale, box.Size / p.UnitScale);
			var shape = new CollisionShape3D { Name = "Box", Shape = new BoxShape3D { Size = native.Size }, Position = native.GetCenter() };
			body.AddChild(shape);
			shape.Owner = _root;
		}
		else
		{
			int i = 0;
			foreach (var mi in Descendants<MeshInstance3D>(inst))
			{
				if (!mi.Visible || mi.Mesh == null)
					continue;
				var shape = new CollisionShape3D { Name = $"Hull{i++}", Shape = mi.Mesh.CreateConvexShape(true, true), Transform = RelativeTransform(mi, inst) };
				body.AddChild(shape);
				shape.Owner = _root;
			}
		}
	}

	/// <summary>Ceiling + wall boxes on the camera_blocker layer, matching the model's interior.</summary>
	private void AddCameraBlockers(Node3D holder, ModelPlacement p, float s, float pivotY, float length)
	{
		Vector3 i = p.InteriorHalfWidthHeightFloor;
		float halfW = i.X * s, height = i.Y * s, floor = i.Z * s + pivotY;
		var body = new StaticBody3D { Name = "CameraBlockers", CollisionLayer = CameraBlockerLayer, CollisionMask = 0 };
		holder.AddChild(body);
		body.Owner = _root;
		void Box(string name, Vector3 size, Vector3 pos)
		{
			var cs = new CollisionShape3D { Name = name, Shape = new BoxShape3D { Size = size }, Position = pos };
			body.AddChild(cs);
			cs.Owner = _root;
		}
		Box("Ceiling", new Vector3(halfW * 2f + 1f, 0.4f, length), new Vector3(0, floor + height + 0.2f - 0.1f, 0));
		Box("WallLeft", new Vector3(0.4f, height + 1f, length), new Vector3(-halfW - 0.2f + 0.1f, floor + height / 2f, 0));
		Box("WallRight", new Vector3(0.4f, height + 1f, length), new Vector3(halfW + 0.2f - 0.1f, floor + height / 2f, 0));
	}

	// ======================================================================
	//  Live editing
	// ======================================================================

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint() || !LiveUpdate)
			return;
		_liveTimer -= (float)delta;
		if (_liveTimer > 0f)
			return;
		_liveTimer = 0.1f;
		_root ??= SceneRoot();
		if (_root == null)
			return;
		foreach (Node n in GetTree().GetNodesInGroup(InstanceGroup))
		{
			if (n is not Node3D holder || !holder.HasMeta(MetaIndex))
				continue;
			int i = holder.GetMeta(MetaIndex).AsInt32();
			if (i >= 0 && i < Placements.Count && Placements[i] != null)
				ApplyTransform(holder, Placements[i]);
		}
	}

	private static void ApplyTransform(Node3D holder, ModelPlacement p)
	{
		holder.Position = p.Position;
		holder.RotationDegrees = p.RotationDegrees;
		holder.Scale = Vector3.One * p.Scale;
	}

	// ======================================================================
	//  Helpers
	// ======================================================================

	private Node SceneRoot() => Owner ?? GetParent();

	/// <summary>Hides rendering only (VisualInstance layers = 0, lights off); physics and scripts are untouched.</summary>
	private static void Hide(Node node)
	{
		foreach (Node n in Self(node).Concat(Descendants<Node>(node)))
		{
			if (n.IsInGroup(HiddenGroup))
				continue;
			if (n is VisualInstance3D vi and not Light3D)
			{
				n.SetMeta(MetaLayers, vi.Layers);
				vi.Layers = 0;
				n.AddToGroup(HiddenGroup, true);
			}
			else if (n is Light3D light)
			{
				n.SetMeta(MetaVisible, light.Visible);
				light.Visible = false;
				n.AddToGroup(HiddenGroup, true);
			}
		}
	}

	/// <summary>Hides a replaced placeholder AND switches its collision off (both restored by "Remove Models").</summary>
	private static void Retire(Node node)
	{
		Hide(node);
		foreach (Node n in Self(node).Concat(Descendants<Node>(node)))
		{
			if (n is CollisionObject3D body && !n.HasMeta(MetaCollisionLayer))
			{
				n.SetMeta(MetaCollisionLayer, body.CollisionLayer);
				body.CollisionLayer = 0;
				n.AddToGroup(HiddenGroup, true);
			}
			else if (n is CsgShape3D csg && csg.IsRootShape() && csg.UseCollision)
			{
				n.SetMeta(MetaCsgCollision, true);
				csg.UseCollision = false;
				n.AddToGroup(HiddenGroup, true);
			}
		}
	}

	private static void Unhide(Node n)
	{
		if (n is VisualInstance3D vi && n.HasMeta(MetaLayers))
		{
			vi.Layers = n.GetMeta(MetaLayers).AsUInt32();
			n.RemoveMeta(MetaLayers);
		}
		if (n is Light3D light && n.HasMeta(MetaVisible))
		{
			light.Visible = n.GetMeta(MetaVisible).AsBool();
			n.RemoveMeta(MetaVisible);
		}
		if (n is CollisionObject3D body && n.HasMeta(MetaCollisionLayer))
		{
			body.CollisionLayer = n.GetMeta(MetaCollisionLayer).AsUInt32();
			n.RemoveMeta(MetaCollisionLayer);
		}
		if (n is CsgShape3D csg && n.HasMeta(MetaCsgCollision))
		{
			csg.UseCollision = true;
			n.RemoveMeta(MetaCsgCollision);
		}
		n.RemoveFromGroup(HiddenGroup);
	}

	/// <summary>
	/// Adds the interaction point that starts an AeroKidz minigame: an Area3D (MinigameTrigger3D) with a
	/// sphere CollisionShape3D, at TriggerOffset from the model. Press E there, or walk in when
	/// TriggerOnEnter is set on the node.
	/// </summary>
	private void AddTrigger(Node3D holder, ModelPlacement p)
	{
		if (!Enum.TryParse(p.TriggerRoom, true, out AeroKidz.OutpostRoom room))
		{
			GD.PushWarning($"ModelBaseBuilder: {p.Label}: unknown TriggerRoom '{p.TriggerRoom}' (use Telescope, Rocket, Plant, Drill, Oxygen, Power or Shield).");
			return;
		}
		var trigger = new AeroKidz.MinigameTrigger3D
		{
			Name = "MinigameTrigger",
			Room = room,
			Prompt = p.TriggerPrompt ?? "",
			Position = p.TriggerOffset / Mathf.Max(p.Scale, 0.01f), // the holder is scaled by p.Scale
			CollisionLayer = 0,  // detects the player; never blocks movement or the camera
			CollisionMask = 1,
		};
		holder.AddChild(trigger);
		trigger.Owner = _root;
		var shape = new CollisionShape3D
		{
			Name = "TriggerShape",
			Shape = new SphereShape3D { Radius = p.TriggerRadius },
			Position = new Vector3(0f, 1f, 0f),
		};
		trigger.AddChild(shape);
		shape.Owner = _root;
	}

	/// <summary>Apollo 11 Lunar Module on the launch pad: replaces the old rocket; E at its ladder = fly home.</summary>
	public static ModelPlacement LunarModulePlacement() =>
		new("Apollo 11 Lunar Module (fly home)", Models + "apollo_11_lunar_module.glb", new Vector3(58f, 0.2f, 32f), Vector3.Zero,
			ModelPlacement.FitMode.Height, 7.0f, ModelPlacement.CollisionMode.Box)
		{
			RetireNodes = new[] { "Base/LaunchPad/Lander", "Base/LaunchPad/RocketLander", "Base/LaunchPad/LanderCollision" },
			TriggerRoom = "Rocket",
			TriggerOffset = new Vector3(-5.2f, 0f, 0f), // west side, facing the path from the Rover Bay
			TriggerRadius = 2.5f,
		};

	/// <summary>Spitzer Space Telescope in the Telescope room: replaces the old ring scanner; E at it = Mission 3.</summary>
	public static ModelPlacement SpitzerTelescopePlacement() =>
		new("Spitzer Space Telescope (Mission 3)", Models + "spitzer_space_telescope.glb", new Vector3(36f, 0.25f, 27.5f), Vector3.Zero,
			ModelPlacement.FitMode.Height, 4.2f, ModelPlacement.CollisionMode.Box)
		{
			RetireNodes = new[]
			{
				"Base/Interiors/Telescope/RingScanner", "Base/Interiors/Telescope/PierCollision",
				"Base/Interiors/Telescope/Pier", "Base/Interiors/Telescope/Mount",
				"Base/Interiors/Telescope/OpticalTube", "Base/Interiors/Telescope/Aperture",
			},
			TriggerRoom = "Telescope",
			TriggerOffset = new Vector3(0f, 0f, -2.6f), // in front of it, toward the door
			TriggerRadius = 2.0f,
		};

	private static void RemoveExcluded(Node3D inst, string[] names, bool hideInstead = false)
	{
		if (names == null || names.Length == 0)
			return;
		foreach (var mi in Descendants<MeshInstance3D>(inst).ToList())
		{
			if (!names.Any(x => mi.Name.ToString().Contains(x, StringComparison.OrdinalIgnoreCase)))
				continue;
			if (hideInstead)
				mi.Visible = false;
			else
			{
				mi.GetParent().RemoveChild(mi);
				mi.Free();
			}
		}
	}

	/// <summary>Bounding box of all visible meshes, in the instance root's local space.</summary>
	private static Aabb MeasureAabb(Node3D inst)
	{
		Aabb box = default;
		bool first = true;
		foreach (var mi in Descendants<MeshInstance3D>(inst))
		{
			if (mi.Mesh == null || !mi.Visible)
				continue;
			Aabb a = RelativeTransform(mi, inst) * mi.GetAabb();
			box = first ? a : box.Merge(a);
			first = false;
		}
		return box;
	}

	private static Transform3D RelativeTransform(Node3D node, Node3D root)
	{
		Transform3D t = Transform3D.Identity;
		for (Node n = node; n != null && n != root; n = n.GetParent())
			if (n is Node3D n3)
				t = n3.Transform * t;
		return t;
	}

	private static IEnumerable<Node> Self(Node n) { yield return n; }

	private static IEnumerable<T> Descendants<T>(Node n) where T : class
	{
		foreach (Node c in n.GetChildren())
		{
			if (c is T t)
				yield return t;
			foreach (T d in Descendants<T>(c))
				yield return d;
		}
	}

	private static bool IsUnder(Node n, Node ancestor)
	{
		for (Node p = n; p != null; p = p.GetParent())
			if (p == ancestor)
				return true;
		return false;
	}

	private static void Collect(Node n, List<Node> into)
	{
		into.Add(n);
		foreach (Node c in n.GetChildren())
			Collect(c, into);
	}

	private static string Sanitize(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}

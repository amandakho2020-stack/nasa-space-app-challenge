using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Editor utility that swaps the graybox placeholders for the procedural models in
/// <see cref="ModelLibrary"/>, and can swap them back.
///
/// "Replace Graybox With Models":
///   - finds each known placeholder (CSG / primitive meshes built by EnvironmentBuilder, the
///     player's capsule, the rover parts),
///   - adds a MeshInstance3D with the generated model as a sibling using the placeholder's exact
///     local transform (position, rotation and scale preserved),
///   - preserves collision: every placeholder CSG with Use Collision gets its collision baked
///     (CSGShape3D.BakeCollisionShape) into a StaticBody3D at the same transform, so physics is
///     unchanged even where the new model's silhouette differs,
///   - hides the placeholder and disables its CSG collision (nothing is deleted).
/// "Restore Graybox" removes everything generated and re-enables the placeholders.
///
/// Generated meshes, baked shapes and the shared style materials are saved under ModelsDir so the
/// scene references small resource files. Re-running EnvironmentBuilder rebuilds the graybox,
/// so run the replacer again afterwards.
/// </summary>
[Tool]
public partial class ArtReplacer : Node
{
	private const string GeneratedGroup = "art_generated";
	private const string PlaceholderGroup = "art_placeholder";
	private const string SwappedGroup = "art_swapped";
	private const string MetaHadCollision = "art_had_collision";
	private const string MetaMaterial = "art_original_material";
	private const string MetaMultiMesh = "art_original_multimesh";
	private const string MetaOverride = "art_original_override";

	[ExportToolButton("Replace Graybox With Models", Icon = "MeshInstance3D")]
	public Callable ReplaceButton => Callable.From(ReplaceAll);

	[ExportToolButton("Restore Graybox", Icon = "CSGBox3D")]
	public Callable RestoreButton => Callable.From(RestoreAll);

	[Export] public string ModelsDir = "res://Materials/Models";
	[Export] public bool Outlines = true;
	[Export(PropertyHint.Range, "0,6,0.1")] public float OutlinePixels = 2.2f;
	/// <summary>Re-skin the CSG base shell in the art palette (cream hull, sage interior).</summary>
	[Export] public bool RestyleBaseShell = true;
	/// <summary>Swap the rock-field MultiMesh meshes for crystal rocks.</summary>
	[Export] public bool ReplaceRocks = true;

	private Node _root;
	private Node3D _primeHost;
	private readonly Dictionary<string, ArrayMesh> _meshes = new();
	private int _models, _placeholders, _bakedShapes, _collisionKeptOnPlaceholder;

	// ======================================================================
	//  Entry points
	// ======================================================================

	public void ReplaceAll()
	{
		_root = Owner ?? GetParent();
		if (_root == null)
		{
			GD.PushError("ArtReplacer: add this node to a scene first.");
			return;
		}

		RestoreAll(); // idempotent: start from a clean graybox
		_meshes.Clear();
		_models = _placeholders = _bakedShapes = _collisionKeptOnPlaceholder = 0;

		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(ModelsDir.PathJoin("collision")));
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(ModelsDir.PathJoin("rocks")));
		StyleKit.Reset();
		StyleKit.Outlines = Outlines;
		StyleKit.OutlinePixels = OutlinePixels;
		foreach (var (file, resource) in StyleKit.AllResources())
			Save(resource, file);

		try
		{
			ReplacePlayer();
			ReplaceRover();
			ReplacePlantDome();
			ReplacePlanters();
			ReplaceOxygenRoom();
			ReplaceSolarField();
			ReplaceCommTower();
			ReplaceDrillSite();
			ReplaceDesks();
			ReplaceTelescope();
			ReplaceLander();
			if (ReplaceRocks)
				ReplaceRockFields();
			if (RestyleBaseShell)
				RestyleShell();
		}
		finally
		{
			ReleasePrimeHost();
		}

		GD.Print($"ArtReplacer: {_models} models placed, {_placeholders} placeholders hidden, {_bakedShapes} collision shapes baked" + (_collisionKeptOnPlaceholder > 0 ? $", {_collisionKeptOnPlaceholder} kept on hidden placeholders" : "") + ". " +
			"Save the scene (Ctrl+S) to keep the changes.");
	}

	public void RestoreAll()
	{
		_root = Owner ?? GetParent();
		if (_root == null)
			return;

		var all = new List<Node>();
		Collect(_root, all);
		int removed = 0, restored = 0;

		// Collect first, then free: freeing a generated node also frees its children, so the list
		// must be checked for validity before every access.
		var generated = all.Where(n => n.IsInGroup(GeneratedGroup)).ToList();
		foreach (Node n in generated)
		{
			if (!GodotObject.IsInstanceValid(n))
				continue;
			n.GetParent()?.RemoveChild(n);
			n.Free();
			removed++;
		}
		foreach (Node n in all)
		{
			if (!GodotObject.IsInstanceValid(n))
				continue;
			if (n.IsInGroup(PlaceholderGroup))
			{
				if (n is Node3D n3)
					n3.Visible = true;
				if (n is CsgShape3D csg && csg.HasMeta(MetaHadCollision))
				{
					csg.UseCollision = true;
					csg.RemoveMeta(MetaHadCollision);
				}
				n.RemoveFromGroup(PlaceholderGroup);
				restored++;
			}
			if (n.IsInGroup(SwappedGroup))
			{
				if (n is CsgShape3D shell && shell.HasMeta(MetaMaterial))
				{
					shell.Set("material", shell.GetMeta(MetaMaterial));
					shell.RemoveMeta(MetaMaterial);
				}
				if (n is MultiMeshInstance3D mmi && mmi.HasMeta(MetaMultiMesh))
				{
					mmi.Multimesh = mmi.GetMeta(MetaMultiMesh).As<MultiMesh>();
					mmi.MaterialOverride = mmi.HasMeta(MetaOverride) ? mmi.GetMeta(MetaOverride).As<Material>() : null;
					mmi.RemoveMeta(MetaMultiMesh);
					mmi.RemoveMeta(MetaOverride);
				}
				n.RemoveFromGroup(SwappedGroup);
				restored++;
			}
		}
		if (removed + restored > 0)
			GD.Print($"ArtReplacer: restored graybox ({removed} generated nodes removed, {restored} placeholders re-enabled).");
	}

	// ======================================================================
	//  Replacement table
	// ======================================================================

	private void ReplacePlayer()
	{
		var visual = Find<Node3D>("Player/Visual");
		if (visual == null)
			return;
		// With the 2.5D sprite installed (SpriteSheetSlicer), keep it and only retire the old 3D body.
		bool hasSprite = visual.GetNodeOrNull<AnimatedSprite3D>("Sprite") != null;
		foreach (Node child in visual.GetChildren().ToList())
			if (child is GeometryInstance3D and not SpriteBase3D)
				Retire(child);
		if (!hasSprite)
			AddModel(visual, "AstronautModel", Transform3D.Identity, Mesh("astronaut", ModelLibrary.Astronaut));
	}

	private void ReplaceRover()
	{
		var rover = Find<Node3D>("LunarRover");
		if (rover == null)
			return;
		foreach (Node child in rover.GetChildren().ToList())
		{
			if (child is CsgShape3D)
				Retire(child);
			else if (child is Node3D wheel && child.Name.ToString().StartsWith("Wheel"))
			{
				foreach (Node part in wheel.GetChildren().ToList())
					if (part is CsgShape3D)
						Retire(part);
				// Hub cap faces outward: +X on the left (x > 0), turned around on the right.
				var xf = wheel.Position.X < 0f ? new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero) : Transform3D.Identity;
				AddModel(wheel, "WheelModel", xf, Mesh("rover_wheel", () => ModelLibrary.RoverWheel()));
			}
		}
		AddModel(rover, "RoverModel", Transform3D.Identity, Mesh("rover_body", ModelLibrary.RoverBody));
	}

	private void ReplacePlantDome()
	{
		var dome = Find<CsgSphere3D>("Base/PlantDomeGlass");
		if (dome == null)
			return;
		var door = dome.GetNodeOrNull<Node3D>("Door");
		Vector3 doorDir = door != null ? new Vector3(door.Position.X, 0f, door.Position.Z).Normalized() : Vector3.Left;
		float radius = dome.Radius;
		Retire(dome);
		RetireTree(Find<Node3D>("Base/PlantDomeRibs"));
		AddModel(dome.GetParent(), "GeodesicDome", dome.Transform,
			Mesh($"geodesic_dome_r{radius:0.#}", () => ModelLibrary.GeodesicDome(radius, doorDir)));
	}

	private void ReplacePlanters()
	{
		var plant = Find<Node3D>("Base/Interiors/Plant");
		if (plant == null)
			return;
		var children = plant.GetChildren().ToList();
		foreach (Node child in children)
		{
			string name = child.Name;
			if (child is CsgBox3D planter && name.StartsWith("Planter"))
			{
				Retire(planter);
				Vector3 size = planter.Size;
				AddModel(plant, $"Hydro{name}", planter.Transform, Mesh($"hydro_planter_{size.X:0.#}", () => ModelLibrary.HydroPlanter(size)));
			}
			else if (child is CsgSphere3D && name.StartsWith("Plant"))
			{
				Retire(child);
			}
		}
	}

	private void ReplaceOxygenRoom()
	{
		var room = Find<Node3D>("Base/Interiors/Oxygen");
		if (room == null)
			return;
		var tanks = Enumerable.Range(0, 6).Select(i => room.GetNodeOrNull<CsgCylinder3D>($"O2Tank{i}")).Where(t => t != null).ToList();
		if (tanks.Count > 0)
		{
			float floorY = tanks[0].Position.Y - tanks[0].Height / 2f;
			int half = (tanks.Count + 1) / 2;
			foreach (var group in new[] { tanks.Take(half).ToList(), tanks.Skip(half).ToList() })
			{
				if (group.Count == 0)
					continue;
				Vector3 c = group.Aggregate(Vector3.Zero, (acc, t) => acc + t.Position) / group.Count;
				AddModel(room, $"O2Cluster{_models}", new Transform3D(Basis.Identity, new Vector3(c.X, floorY, c.Z)), Mesh("o2_cluster", ModelLibrary.O2Cluster));
			}
			foreach (var t in tanks)
				Retire(t);
		}
		Retire(room.GetNodeOrNull("Manifold"));

		if (room.GetNodeOrNull<CsgBox3D>("Electrolyzer") is { } station)
		{
			Retire(station);
			// The station model faces +Z; the electrolyzer stands on the west wall facing into the room (+X).
			var xf = new Transform3D(station.Basis * new Basis(Vector3.Up, Mathf.Pi / 2f), station.Position - new Vector3(0f, station.Size.Y / 2f, 0f));
			AddModel(room, "ControlStation", xf, Mesh("control_station", ModelLibrary.ControlStation));
		}
	}

	private void ReplaceSolarField()
	{
		var field = Find<Node3D>("Base/SolarField");
		if (field == null)
			return;
		foreach (Node child in field.GetChildren().ToList())
		{
			if (child is not CsgCylinder3D post || !child.Name.ToString().StartsWith("Post"))
				continue;
			string id = child.Name.ToString().Substring(4);
			var panel = field.GetNodeOrNull<CsgBox3D>($"Panel{id}");
			float w = panel?.Size.X ?? 5f, d = panel?.Size.Z ?? 2.8f, tilt = panel?.RotationDegrees.X ?? 30f;
			bool broken = id == "22"; // one wrecked array for visual storytelling
			Retire(post);
			Retire(panel);
			var xf = new Transform3D(post.Basis, post.Position - new Vector3(0f, post.Height / 2f, 0f));
			AddModel(field, $"SolarTracker{id}", xf, Mesh($"solar_tracker_{w:0.#}x{d:0.#}{(broken ? "_broken" : "")}", () => ModelLibrary.SolarTracker(w, d, tilt, broken)));
		}
	}

	private void ReplaceCommTower()
	{
		var mast = Find<CsgCylinder3D>("Base/LobbyDishMast");
		if (mast == null)
			return;
		Retire(mast);
		Retire(Find<Node>("Base/LobbyDish"));
		var xf = new Transform3D(mast.Basis, mast.Position - new Vector3(0f, mast.Height / 2f, 0f));
		AddModel(mast.GetParent(), "CommTower", xf, Mesh("comm_tower", () => ModelLibrary.CommTower()));
	}

	private void ReplaceDrillSite()
	{
		var site = Find<Node3D>("Base/DrillSite");
		if (site == null)
			return;

		if (site.GetNodeOrNull<Node3D>("Rig") is { } rig)
		{
			RetireTree(rig);
			AddModel(site, "DrillRigModel", rig.Transform, Mesh("drill_rig", ModelLibrary.DrillRig));
		}
		if (site.GetNodeOrNull<CsgBox3D>("SampleRack") is { } rack)
		{
			foreach (Node child in site.GetChildren().ToList())
				if (child.Name.ToString().StartsWith("CoreSample"))
					Retire(child);
			Retire(rack);
			Vector3 size = rack.Size;
			AddModel(site, "SampleBasket", rack.Transform, Mesh("sample_basket", () => ModelLibrary.SampleBasket(size)));
		}
		foreach (string name in new[] { "CoolantTankA", "CoolantTankB" })
		{
			if (site.GetNodeOrNull<CsgCylinder3D>(name) is not { } tank)
				continue;
			Retire(tank);
			float r = tank.Radius, h = tank.Height;
			AddModel(site, $"{name}Model", tank.Transform, Mesh($"pressure_tank_{r:0.##}x{h:0.#}", () => ModelLibrary.PressureTank(r, h)));
		}
		if (site.GetNodeOrNull<CsgBox3D>("FieldHab") is { } hab)
		{
			Retire(hab);
			var xf = new Transform3D(hab.Basis, hab.Position - new Vector3(0f, hab.Size.Y / 2f, 0f));
			AddModel(site, "HabPod", xf, Mesh("hab_pod", ModelLibrary.HabPod));
		}
	}

	private void ReplaceDesks()
	{
		var cc = Find<Node3D>("Base/Interiors/CommandCenter");
		if (cc == null)
			return;
		foreach (Node child in cc.GetChildren().ToList())
		{
			if (child is not Node3D desk || !child.Name.ToString().StartsWith("Desk"))
				continue;
			float width = (desk.GetNodeOrNull<CsgPolygon3D>("Body")?.Depth) ?? 2.4f;
			Retire(desk.GetNodeOrNull("Body"));
			Retire(desk.GetNodeOrNull("Kickplate"));
			foreach (Node part in desk.GetChildren().ToList())
				if (part.Name.ToString().StartsWith("Keyboard"))
					Retire(part);
			AddModel(desk, "ConsoleModel", Transform3D.Identity, Mesh($"desk_console_{width:0.#}", () => ModelLibrary.DeskConsole(width)));
			if (desk.GetNodeOrNull<Node3D>("Chair") is { } chair)
			{
				RetireTree(chair);
				AddModel(desk, "ChairModel", chair.Transform, Mesh("chair", ModelLibrary.Chair));
			}
			// The Screen nodes (bezel + panel + live Label3D) are kept: they sit on the console's slanted face.
		}
	}

	private void ReplaceTelescope()
	{
		var room = Find<Node3D>("Base/Interiors/Telescope");
		var pier = room?.GetNodeOrNull<CsgCylinder3D>("Pier");
		if (pier == null)
			return;
		foreach (string name in new[] { "Pier", "Mount", "OpticalTube", "Aperture" })
			Retire(room.GetNodeOrNull(name));
		var xf = new Transform3D(Basis.Identity, pier.Position - new Vector3(0f, pier.Height / 2f, 0f));
		AddModel(room, "RingScanner", xf, Mesh("ring_scanner", ModelLibrary.RingScanner));
	}

	private void ReplaceLander()
	{
		var lander = Find<CsgCylinder3D>("Base/LaunchPad/Lander");
		if (lander == null)
			return;
		Retire(lander);
		var xf = new Transform3D(lander.Basis, lander.Position - new Vector3(0f, lander.Height / 2f, 0f));
		AddModel(lander.GetParent(), "RocketLander", xf, Mesh("rocket_lander", ModelLibrary.RocketLander));
	}

	private void ReplaceRockFields()
	{
		var rocks = Find<Node>("LunarSurface/Rocks");
		if (rocks == null)
			return;
		var all = new List<Node>();
		Collect(rocks, all);
		foreach (Node n in all)
		{
			if (n is not MultiMeshInstance3D mmi || mmi.Multimesh == null)
				continue;
			string name = mmi.Name;
			int variant = char.IsDigit(name[^1]) ? name[^1] - '0' : 0;
			ArrayMesh rock = Mesh($"crystal_rock_{variant}", () => ModelLibrary.CrystalRock(301 + variant));

			mmi.SetMeta(MetaMultiMesh, mmi.Multimesh);
			mmi.SetMeta(MetaOverride, mmi.MaterialOverride);
			var copy = (MultiMesh)mmi.Multimesh.Duplicate();
			copy.Mesh = rock;
			mmi.Multimesh = Save(copy, $"rocks/{name}.res");
			mmi.MaterialOverride = null; // use the mesh's style materials
			mmi.AddToGroup(SwappedGroup, true);
			_models++;
		}
	}

	private void RestyleShell()
	{
		var shell = Find<Node>("Base/Shell");
		if (shell == null)
			return;
		foreach (Node child in shell.GetChildren())
		{
			if (child is not CsgShape3D csg)
				continue;
			string name = csg.Name;
			Material target = name.EndsWith("Hull") || name.EndsWith("Dome") || name.EndsWith("Ring") ? StyleKit.HullExterior
				// Interior faces ("Void") keep the EnvironmentBuilder interior steel material.
				: null;
			if (target == null)
				continue;
			csg.SetMeta(MetaMaterial, csg.Get("material")); // "material" lives on each CSG subclass
			csg.Set("material", target);
			csg.AddToGroup(SwappedGroup, true);
		}
	}

	// ======================================================================
	//  Core operations
	// ======================================================================

	/// <summary>Hides a placeholder; bakes and keeps its collision if it was a colliding root CSG shape.</summary>
	private void Retire(Node node)
	{
		if (node == null || node.IsInGroup(PlaceholderGroup))
			return;

		if (node is CsgShape3D csg && csg.IsRootShape() && csg.UseCollision)
		{
			ConcavePolygonShape3D shape = BakeCollision(csg);
			if (shape != null && shape.GetFaces().Length > 0)
			{
				var body = new StaticBody3D
				{
					Name = $"{csg.Name}Collision",
					Transform = csg.Transform,
					CollisionLayer = csg.CollisionLayer,
					CollisionMask = csg.CollisionMask,
				};
				body.AddChild(new CollisionShape3D { Name = "Shape", Shape = Save(shape, $"collision/{SafeName(csg)}.res") });
				csg.GetParent().AddChild(body);
				Own(body);
				_bakedShapes++;
				csg.SetMeta(MetaHadCollision, true);
				csg.UseCollision = false;
			}
			else
			{
				// Could not bake: leave the (now invisible) CSG colliding so physics never changes.
				_collisionKeptOnPlaceholder++;
			}
		}

		if (node is Node3D n3)
			n3.Visible = false;
		node.AddToGroup(PlaceholderGroup, true);
		_placeholders++;
	}

	/// <summary>Retires every CSG / geometry node under <paramref name="container"/>, then hides the container.</summary>
	private void RetireTree(Node container)
	{
		if (container == null)
			return;
		var all = new List<Node>();
		Collect(container, all);
		foreach (Node n in all)
			if (n != container && (n is CsgShape3D { } c && c.IsRootShape() || n is GeometryInstance3D and not CsgShape3D))
				Retire(n);
		Retire(container);
	}

	/// <summary>
	/// Bakes a CSG shape's collision faces (local space). CSG builds its geometry one frame after it
	/// enters the scene tree: in the editor the placeholders are already live, so this is immediate.
	/// For a scene that is not in the tree (batch / command-line use) call
	/// <see cref="PrimeCollisionBakes"/> at least one frame before <see cref="ReplaceAll"/>.
	/// </summary>
	private ConcavePolygonShape3D BakeCollision(CsgShape3D csg)
	{
		if (csg.IsInsideTree())
			return csg.BakeCollisionShape();
		if (_primeHost == null)
			return null;
		string key = _root.GetPathTo(csg).ToString();
		foreach (Node n in _primeHost.GetChildren())
			if (n is CsgShape3D primed && primed.GetMeta("art_source", "").AsString() == key)
				return primed.BakeCollisionShape();
		return null;
	}

	/// <summary>
	/// Batch-mode helper: copies every colliding placeholder-sized CSG into a temporary node inside the
	/// live tree so its collision can be baked on a later frame. Not needed when used from the editor.
	/// </summary>
	public void PrimeCollisionBakes()
	{
		_root = Owner ?? GetParent();
		ReleasePrimeHost();
		if (Engine.GetMainLoop() is not SceneTree tree)
			return;
		_primeHost = new Node3D { Name = "__ArtPrimeHost" };
		tree.Root.AddChild(_primeHost);

		var all = new List<Node>();
		Collect(_root, all);
		foreach (Node n in all)
		{
			if (n is not CsgShape3D csg || !csg.IsRootShape() || !csg.UseCollision || csg.IsInsideTree())
				continue;
			if (csg is CsgCombiner3D || csg.Name == "Regolith") // the base shell and ground stay as they are
				continue;
			var dup = (CsgShape3D)csg.Duplicate();
			dup.Transform = Transform3D.Identity;
			dup.SetMeta("art_source", _root.GetPathTo(csg).ToString());
			_primeHost.AddChild(dup);
		}
	}

	private void ReleasePrimeHost()
	{
		if (_primeHost == null || !GodotObject.IsInstanceValid(_primeHost))
		{
			_primeHost = null;
			return;
		}
		_primeHost.GetParent()?.RemoveChild(_primeHost);
		_primeHost.Free();
		_primeHost = null;
	}

	private MeshInstance3D AddModel(Node parent, string name, Transform3D xf, ArrayMesh mesh)
	{
		var mi = new MeshInstance3D { Name = name, Mesh = mesh, Transform = xf };
		parent.AddChild(mi);
		Own(mi);
		_models++;
		return mi;
	}

	private void Own(Node n)
	{
		n.Owner = _root;
		foreach (Node c in n.GetChildren())
		{
			c.Owner = _root;
		}
		n.AddToGroup(GeneratedGroup, true);
	}

	private ArrayMesh Mesh(string key, Func<ArrayMesh> build)
	{
		if (_meshes.TryGetValue(key, out ArrayMesh mesh))
			return mesh;
		mesh = Save(build(), $"{key}.res");
		_meshes[key] = mesh;
		return mesh;
	}

	private T Save<T>(T resource, string relativePath) where T : Resource
	{
		string path = ModelsDir.PathJoin(relativePath);
		Error err = ResourceSaver.Save(resource, path);
		if (err != Error.Ok)
		{
			GD.PushError($"ArtReplacer: could not save {path} ({err}).");
			return resource;
		}
		resource.TakeOverPath(path);
		return resource;
	}

	private T Find<T>(string path) where T : class => _root.GetNodeOrNull(path) as T;

	private string SafeName(Node n) => _root.GetPathTo(n).ToString().Replace("/", "_").Replace(":", "_");

	private static void Collect(Node n, List<Node> into)
	{
		into.Add(n);
		foreach (Node c in n.GetChildren())
			Collect(c, into);
	}
}

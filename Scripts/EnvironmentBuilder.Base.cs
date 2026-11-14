using Godot;
using System.Collections.Generic;

// Modular base layout (from the design sketch):
//
//              OXYGEN
//                 |
//   POWER --- WEST HUB === LOBBY === EAST HUB --- PLANT (Bio-Dome)
//                 |          (spawn)  |  |
//              SHIELD         ROVER BAY  TELESCOPE
//                                 |
//                         rover road, ~165 m south
//                                 |
//                            DRILL SITE (remote, outdoors)
//
// All pressurized hulls live in ONE CSGCombiner3D ("Shell") so tubes cut clean door openings:
//   1. unions    : outer hulls of rooms and tubes
//   2. voids     : interior volumes (subtraction) -> openings appear wherever a tube void crosses a wall
//   3. fills     : floors, tube walkways, light strips (union after the voids, so they are not carved)
public partial class EnvironmentBuilder
{
	private const float BaseFloor = 0.25f;     // interior floor height everywhere
	private const float TubeOuter = 1.95f;
	private const float TubeInner = 1.6f;
	private const float TubeCenterY = 1.65f;   // inner bottom at 0.05; walkway top at BaseFloor
	private const float RoomHeight = 4.6f;     // clear interior height of standard rooms
	private const float WallThickness = 0.4f;

	/// <summary>Centre of the Rover Bay parking pad (south of the East Corridor spur).</summary>
	private static readonly Vector3 RoverPadCenter = new(13f, 0f, 20.3f);

	private readonly List<CsgShape3D> _unions = new();
	private readonly List<CsgShape3D> _voids = new();
	private readonly List<CsgShape3D> _fills = new();
	private readonly List<(Vector3 pos, Shape3D shape)> _pressurized = new();
	private readonly List<(string name, Vector3 center, Vector2 size, Color color)> _rooms = new();
	private readonly List<(Vector3 a, Vector3 b)> _tubes = new();
	/// <summary>Per corridor: start point, unit direction, and the exposed span (distance along the tube between the two room walls).</summary>
	private readonly List<(string name, Vector3 a, Vector3 dir, float start, float end)> _tubeSpans = new();

	private Node3D _base;
	private Node3D _lights;
	private Node3D _interiors;
	private StandardMaterial3D _hullExt, _hullInt, _trim, _walkway, _strip, _amber, _bezel, _dark;

	private void BuildBaseLayout(Node root)
	{
		RemoveIfExists(root, "Base");
		_unions.Clear();
		_voids.Clear();
		_fills.Clear();
		_pressurized.Clear();
		_rooms.Clear();
		_tubes.Clear();
		_tubeSpans.Clear();

		_base = new Node3D { Name = "Base" };
		root.AddChild(_base);
		_lights = new Node3D { Name = "Lights" };
		_interiors = new Node3D { Name = "Interiors" };

		_hullExt = Mat(new Color(0.86f, 0.87f, 0.88f), 0.1f, 0.55f);
		BuildInteriorMaterials();
		_hullInt = _ikMetalGray;   // bare shell interior = gray steel plate
		_trim = Mat(new Color(0.3f, 0.31f, 0.34f), 0.4f, 0.5f);
		_walkway = _ikGrate;
		_dark = Mat(new Color(0.08f, 0.085f, 0.1f), 0.3f, 0.5f);
		_bezel = Mat(new Color(0.05f, 0.05f, 0.06f), 0.5f, 0.4f);
		_strip = _ikGlowWhite;
		_amber = Emissive(new Color(1f, 0.62f, 0.15f), 3f);

		// ---- Rooms --------------------------------------------------------------
		AddOctRoom("Lobby", Vector3.Zero, 11f, 10.5f, 6.35f, new Color(0.08f, 0.1f, 0.16f));
		AddOctRoom("WestHub", new Vector3(-36, 0, 0), 4.8f, 4.3f, RoomHeight, new Color(0.3f, 0.31f, 0.33f));
		AddOctRoom("EastHub", new Vector3(36, 0, 0), 4.8f, 4.3f, RoomHeight, new Color(0.3f, 0.31f, 0.33f));
		AddBoxRoom("Oxygen", new Vector3(-36, 0, -26), new Vector2(14, 12), new Color(0.1f, 0.55f, 0.65f));
		AddBoxRoom("Power", new Vector3(-62, 0, 0), new Vector2(12, 14), new Color(0.75f, 0.6f, 0.1f));
		AddBoxRoom("Shield", new Vector3(-36, 0, 26), new Vector2(12, 12), new Color(0.6f, 0.2f, 0.15f), wall: 1.0f, height: 3.8f);
		AddTelescopeRoom(new Vector3(36, 0, 26));
		AddPlantDome(new Vector3(62, 0, 0));

		// ---- Corridors (center to center; voids cut the doorways) ------------------
		float lobbyHalf = 11f * Mathf.Cos(Mathf.Pi / 8f);
		float hubHalf = 4.8f * Mathf.Cos(Mathf.Pi / 8f);
		AddTube("WestCorridor", Vector3.Zero, new Vector3(-36, 0, 0), lobbyHalf, hubHalf, "COMMAND CENTER", "OXYGEN  |  POWER  |  SHIELD");
		AddTube("OxygenTube", new Vector3(-36, 0, 0), new Vector3(-36, 0, -26), hubHalf, 6.4f, "WEST HUB", "OXYGEN");
		AddTube("PowerTube", new Vector3(-36, 0, 0), new Vector3(-62, 0, 0), hubHalf, 6.4f, "WEST HUB", "POWER");
		AddTube("ShieldTube", new Vector3(-36, 0, 0), new Vector3(-36, 0, 26), hubHalf, 7.0f, "WEST HUB", "SHIELD");
		AddTube("EastCorridor", Vector3.Zero, new Vector3(36, 0, 0), lobbyHalf, hubHalf, "COMMAND CENTER", "PLANT  |  TELESCOPE  |  ROVER BAY");
		AddTube("PlantTube", new Vector3(36, 0, 0), new Vector3(62, 0, 0), hubHalf, 10f, "EAST HUB", "PLANT BIO-DOME");
		AddTube("TelescopeTube", new Vector3(36, 0, 0), new Vector3(36, 0, 26), hubHalf, 8f, "EAST HUB", "TELESCOPE");

		BuildRoverBay(new Vector3(13, 0, 0));
		BuildLaunchPad(new Vector3(58, 0, 32));
		BuildLobbyShellCuts();
		BuildRoomInteriors();

		// ---- Assemble the combiner in the required order --------------------------
		var shell = new CsgCombiner3D { Name = "Shell", UseCollision = true, CollisionLayer = WallCollisionLayers, CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided }; // double-sided: no sunlight leaking through thin walls
		_base.AddChild(shell);
		foreach (CsgShape3D s in _unions) { s.Operation = CsgShape3D.OperationEnum.Union; shell.AddChild(s); }
		foreach (CsgShape3D s in _voids) { s.Operation = CsgShape3D.OperationEnum.Subtraction; shell.AddChild(s); }
		foreach (CsgShape3D s in _fills) { s.Operation = CsgShape3D.OperationEnum.Union; shell.AddChild(s); }

		BuildLobbyInterior();
		BuildInteriorDetail(); // EnvironmentBuilder.Interior.cs: corridor liners/ribs, wall panels, grates, lights

		_base.AddChild(_interiors);
		_base.AddChild(_lights);
		BuildPressurizedZone();

		SetOwnerRecursive(_base, root);
	}

	// ======================================================================
	//  Room primitives
	// ======================================================================

	private void AddBoxRoom(string name, Vector3 c, Vector2 size, Color floorColor, float wall = WallThickness, float height = RoomHeight)
	{
		float outerH = BaseFloor + height + 0.4f;
		_unions.Add(new CsgBox3D
		{
			Name = $"{name}Hull",
			Size = new Vector3(size.X + 2 * wall, outerH, size.Y + 2 * wall),
			Position = new Vector3(c.X, outerH / 2f, c.Z),
			Material = _hullExt,
		});
		_voids.Add(new CsgBox3D
		{
			Name = $"{name}Void",
			Size = new Vector3(size.X, height, size.Y),
			Position = new Vector3(c.X, BaseFloor + height / 2f, c.Z),
			Material = _hullInt,
		});
		_fills.Add(new CsgBox3D
		{
			Name = $"{name}Floor",
			Size = new Vector3(size.X, 0.2f, size.Y),
			Position = new Vector3(c.X, BaseFloor - 0.1f, c.Z),
			Material = DeckMat(floorColor),
		});
		_fills.Add(new CsgBox3D
		{
			Name = $"{name}CeilingLight",
			Size = new Vector3(size.X * 0.5f, 0.06f, 0.5f),
			Position = new Vector3(c.X, BaseFloor + height - 0.03f, c.Z),
			Material = _strip,
		});

		_pressurized.Add((new Vector3(c.X, BaseFloor + height / 2f, c.Z), new BoxShape3D { Size = new Vector3(size.X, height, size.Y) }));
		AddClearZone(c, size + Vector2.One * 2 * wall);
		_rooms.Add((name, c, size, floorColor));

		Omni(_lights, $"{name}Light", new Vector3(c.X, BaseFloor + height - 0.6f, c.Z), new Color(0.92f, 0.96f, 1f), 1.6f,
			Mathf.Max(size.X, size.Y) * 0.95f);
		AddExteriorDetails(name, c, new Vector2(size.X + 2 * wall, size.Y + 2 * wall), outerH);
	}

	/// <summary>Octagonal room (flat faces on the X/Z axes so corridors meet a face square-on).</summary>
	private void AddOctRoom(string name, Vector3 c, float outerR, float innerR, float height, Color floorColor)
	{
		float outerH = BaseFloor + height + 0.4f;
		var rot = new Vector3(0, 22.5f, 0);
		_unions.Add(new CsgCylinder3D { Name = $"{name}Hull", Radius = outerR, Height = outerH, Sides = 8, RotationDegrees = rot, Position = new Vector3(c.X, outerH / 2f, c.Z), Material = _hullExt });
		_voids.Add(new CsgCylinder3D { Name = $"{name}Void", Radius = innerR, Height = height, Sides = 8, RotationDegrees = rot, Position = new Vector3(c.X, BaseFloor + height / 2f, c.Z), Material = _hullInt });
		_fills.Add(new CsgCylinder3D { Name = $"{name}Floor", Radius = innerR, Height = 0.2f, Sides = 8, RotationDegrees = rot, Position = new Vector3(c.X, BaseFloor - 0.1f, c.Z), Material = DeckMat(floorColor) });

		_pressurized.Add((new Vector3(c.X, BaseFloor + height / 2f, c.Z), OctagonPrism(innerR, height))); // matches the walls exactly, corners included
		AddClearZone(c, Vector2.One * outerR * 2f);
		_rooms.Add((name, c, Vector2.One * innerR * 2f, floorColor));

		if (name != "Lobby") // the lobby gets its own lighting rig
			Omni(_lights, $"{name}Light", new Vector3(c.X, BaseFloor + height - 0.6f, c.Z), new Color(0.92f, 0.96f, 1f), 1.4f, innerR * 2.2f);
	}

	private void AddTelescopeRoom(Vector3 c)
	{
		const float outerR = 8f, innerR = 7.6f;
		float wallTop = BaseFloor + RoomHeight + 0.4f;
		var floorColor = new Color(0.12f, 0.2f, 0.45f);

		_unions.Add(new CsgCylinder3D { Name = "TelescopeHull", Radius = outerR, Height = wallTop, Sides = 32, Position = new Vector3(c.X, wallTop / 2f, c.Z), Material = _hullExt });
		_unions.Add(new CsgSphere3D { Name = "TelescopeDome", Radius = outerR, RadialSegments = 32, Rings = 16, Position = new Vector3(c.X, wallTop, c.Z), Material = _hullExt });
		_voids.Add(new CsgCylinder3D { Name = "TelescopeVoid", Radius = innerR, Height = RoomHeight + 0.4f, Sides = 32, Position = new Vector3(c.X, BaseFloor + (RoomHeight + 0.4f) / 2f, c.Z), Material = _hullInt });
		_voids.Add(new CsgSphere3D { Name = "TelescopeDomeVoid", Radius = innerR, RadialSegments = 32, Rings = 16, Position = new Vector3(c.X, wallTop, c.Z), Material = _hullInt });
		// Observation slit through the dome, facing north toward the corridor side.
		_voids.Add(new CsgBox3D { Name = "TelescopeSlit", Size = new Vector3(1.8f, 10f, 9f), Position = new Vector3(c.X, wallTop + 5f, c.Z - 4.5f), Material = _hullInt });
		_fills.Add(new CsgCylinder3D { Name = "TelescopeFloor", Radius = innerR, Height = 0.2f, Sides = 32, Position = new Vector3(c.X, BaseFloor - 0.1f, c.Z), Material = DeckMat(floorColor) });

		_pressurized.Add((new Vector3(c.X, BaseFloor + RoomHeight / 2f, c.Z), new CylinderShape3D { Radius = innerR, Height = RoomHeight }));
		AddClearZone(c, Vector2.One * outerR * 2f);
		_rooms.Add(("Telescope", c, Vector2.One * innerR * 2f, floorColor));
		Omni(_lights, "TelescopeLight", new Vector3(c.X, BaseFloor + 3.8f, c.Z), new Color(0.6f, 0.7f, 1f), 1.2f, 12f);
		Label(_base, "TelescopeSign", "TELESCOPE", new Vector3(c.X, wallTop + outerR + 2.5f, c.Z), Vector3.Zero, 0.03f, 96, new Color(0.6f, 0.75f, 1f), billboard: true, layers: 2);
	}

	/// <summary>Glass greenhouse dome, kept as a separate CSG so it can use the transparent glass material.</summary>
	private void AddPlantDome(Vector3 c)
	{
		const float r = 10f, inner = 9.7f;
		var floorColor = new Color(0.15f, 0.45f, 0.18f);

		// In the shell: clear the tube hull inside the dome, add a low hull ring and the floor.
		_unions.Add(new CsgCylinder3D { Name = "PlantRing", Radius = r + 0.6f, Height = 0.9f, Sides = 40, Position = new Vector3(c.X, 0.45f, c.Z), Material = _hullExt });
		_voids.Add(new CsgSphere3D { Name = "PlantVoid", Radius = inner, RadialSegments = 40, Rings = 20, Position = c, Material = _hullInt });
		_fills.Add(new CsgCylinder3D { Name = "PlantFloor", Radius = inner, Height = 0.2f, Sides = 40, Position = new Vector3(c.X, BaseFloor - 0.1f, c.Z), Material = DeckMat(floorColor) });

		var dome = new CsgSphere3D
		{
			Name = "PlantDomeGlass",
			Radius = r,
			RadialSegments = 40,
			Rings = 20,
			Position = c,
			Material = Glass(new Color(0.75f, 1f, 0.8f), 0.18f),
			UseCollision = true,
			// Alpha-blended glass never casts shadows in Godot, so sunlight reaches the plants.
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_base.AddChild(dome);
		dome.AddChild(new CsgSphere3D { Name = "Hollow", Operation = CsgShape3D.OperationEnum.Subtraction, Radius = inner, RadialSegments = 40, Rings = 20 });
		dome.AddChild(new CsgBox3D { Name = "BelowGround", Operation = CsgShape3D.OperationEnum.Subtraction, Size = new Vector3(2 * r + 2, r, 2 * r + 2), Position = new Vector3(0, -r / 2f + 0.9f, 0) });
		// Door: same profile as the PlantTube void, which enters from the west.
		dome.AddChild(new CsgCylinder3D { Name = "Door", Operation = CsgShape3D.OperationEnum.Subtraction, Radius = TubeInner, Height = 4f, Sides = 16, Position = new Vector3(-r + 0.5f, TubeCenterY, 0), RotationDegrees = new Vector3(0, 0, 90) });
		AddDomeRibs(c, r);

		_pressurized.Add((new Vector3(c.X, BaseFloor + 2.2f, c.Z), new CylinderShape3D { Radius = inner - 0.5f, Height = 4.4f }));
		AddClearZone(c, Vector2.One * (r + 0.6f) * 2f);
		_rooms.Add(("Plant", c, Vector2.One * inner * 2f, floorColor));
		Label(_base, "PlantSign", "PLANT BIO-DOME", new Vector3(c.X, r + 3f, c.Z), Vector3.Zero, 0.03f, 96, new Color(0.55f, 1f, 0.6f), billboard: true, layers: 2);
	}

	private void AddDomeRibs(Vector3 c, float r)
	{
		var ribs = new Node3D { Name = "PlantDomeRibs", Position = c };
		_base.AddChild(ribs);
		for (int i = 0; i < 4; i++)
		{
			// Vertical half-rings (torus standing up), rotated around Y.
			var ring = new CsgTorus3D
			{
				Name = $"Rib{i}",
				InnerRadius = r - 0.06f,
				OuterRadius = r + 0.1f,
				Sides = 48,
				RingSides = 6,
				RotationDegrees = new Vector3(90, i * 45f, 0),
				Material = _trim,
			};
			ribs.AddChild(ring);
			ring.AddChild(new CsgBox3D { Name = "CutBelow", Operation = CsgShape3D.OperationEnum.Subtraction, Size = new Vector3(2 * r + 2, 2 * r + 2, r + 1), Position = new Vector3(0, 0, (r + 1) / 2f - 0.9f) });
		}
	}

	/// <summary>Walkable tube between two room centres, with walkway, light strip, lamps and direction signs.</summary>
	private void AddTube(string name, Vector3 a, Vector3 b, float halfA, float halfB, string signTowardA, string signTowardB,
		float voidTrimAtB = 0f)
	{
		Vector3 d = b - a;
		float len = d.Length();
		Vector3 dir = d / len;
		bool alongX = Mathf.Abs(dir.X) > 0.5f;
		var rot = alongX ? new Vector3(0, 0, 90) : new Vector3(90, 0, 0);
		Vector3 mid = (a + b) / 2f;

		float voidLen = len - voidTrimAtB;
		Vector3 voidMid = a + dir * (voidLen / 2f);

		bool modelCorridor = IsModelCorridor(name);
		if (!modelCorridor) // model corridors (corridor.glb, placed by ModelBaseBuilder) provide their own shell
			_unions.Add(new CsgCylinder3D { Name = $"{name}Hull", Radius = TubeOuter, Height = len, Sides = 16, RotationDegrees = rot, Position = new Vector3(mid.X, TubeCenterY, mid.Z), Material = _hullExt });
		_voids.Add(new CsgCylinder3D { Name = $"{name}Void", Radius = TubeInner, Height = voidLen, Sides = 16, RotationDegrees = rot, Position = new Vector3(voidMid.X, TubeCenterY, voidMid.Z), Material = _hullInt });

		Vector3 along(float l, float h, float w) => alongX ? new Vector3(l, h, w) : new Vector3(w, h, l);
		// Walkway and light strip only along the exposed part between the two rooms (not through them).
		// Walkway overlaps ~1 m into each room (flush with the room floor) so the doorway has no groove;
		// the light strip stays inside the exposed tube.
		float exposedStart = halfA, exposedEnd = voidLen - halfB;
		float segStart = Mathf.Max(exposedStart - 1.1f, 0f), segEnd = Mathf.Min(exposedEnd + 1.1f, voidLen);
		float segLen = segEnd - segStart;
		Vector3 segMid = a + dir * ((segStart + segEnd) / 2f);
		Vector3 stripMid = a + dir * ((exposedStart + exposedEnd) / 2f);
		_fills.Add(new CsgBox3D { Name = $"{name}Walkway", Size = along(segLen, 0.1f, 1.5f), Position = new Vector3(segMid.X, BaseFloor - 0.05f, segMid.Z), Material = _walkway });
		_fills.Add(new CsgBox3D { Name = $"{name}LightStrip", Size = along(exposedEnd - exposedStart - 0.6f, 0.05f, 0.22f), Position = new Vector3(stripMid.X, TubeCenterY + TubeInner - 0.06f, stripMid.Z), Material = _strip });

		// Lamps every ~5 m inside the exposed part of the tube.
		float start = halfA + 1.5f, end = len - halfB - 1.5f;
		int lamps = Mathf.Max(1, Mathf.RoundToInt((end - start) / 5f) + 1);
		for (int i = 0; i < lamps; i++)
		{
			float t = lamps == 1 ? (start + end) / 2f : Mathf.Lerp(start, end, i / (float)(lamps - 1));
			Vector3 p = a + dir * t;
			Omni(_lights, $"{name}Lamp{i}", new Vector3(p.X, TubeCenterY + 1.1f, p.Z), new Color(0.9f, 0.95f, 1f), 1.1f, 6f);
		}

		// Direction signs near each end, readable while walking toward that end.
		if (!string.IsNullOrEmpty(signTowardB))
		{
			Vector3 p = b - dir * (halfB + 0.8f);
			Label(_interiors, $"{name}SignB", signTowardB, new Vector3(p.X, modelCorridor ? 1.85f : TubeCenterY + 1.05f, p.Z), new Vector3(0, YawFacing(-dir), 0), 0.0045f, 64, new Color(1f, 0.85f, 0.4f), outline: 8);
		}
		if (!string.IsNullOrEmpty(signTowardA))
		{
			Vector3 p = a + dir * (halfA + 0.8f);
			Label(_interiors, $"{name}SignA", signTowardA, new Vector3(p.X, modelCorridor ? 1.85f : TubeCenterY + 1.05f, p.Z), new Vector3(0, YawFacing(dir), 0), 0.0045f, 64, new Color(1f, 0.85f, 0.4f), outline: 8);
		}

		_pressurized.Add((new Vector3(voidMid.X, TubeCenterY, voidMid.Z), new BoxShape3D { Size = along(voidLen, 3.1f, 3.0f) }));
		_clearZones.Add(alongX
			? new Rect2(Mathf.Min(a.X, b.X), a.Z - 2.5f, len, 5f).Grow(BaseClearMargin)
			: new Rect2(a.X - 2.5f, Mathf.Min(a.Z, b.Z), 5f, len).Grow(BaseClearMargin));
		_tubes.Add((a, b));
		_tubeSpans.Add((name, a, dir, exposedStart, exposedEnd));
	}

	// ======================================================================
	//  Rover Bay: airlock spur off the East Corridor + parking pad
	// ======================================================================

	private void BuildRoverBay(Vector3 junction)
	{
		const float spurLen = 10.3f, capThickness = 0.3f;
		Vector3 end = junction + new Vector3(0, 0, spurLen);
		AddTube("RoverBayTube", junction, end, 0f, 0.6f, null, "ROVER BAY  -  AIRLOCK\nDRILL SITE: TAKE THE ROVER SOUTH", voidTrimAtB: capThickness);

		// Door opening in the end cap + the sliding airlock door itself.
		float doorZ = end.Z - capThickness / 2f;
		_voids.Add(new CsgBox3D { Name = "AirlockOpening", Size = new Vector3(1.4f, 2.4f, 1.0f), Position = new Vector3(end.X, BaseFloor + 1.2f, doorZ), Material = _trim });

		var door = new CsgBox3D { Name = "AirlockDoor", Size = new Vector3(1.4f, 2.4f, 0.12f), Position = new Vector3(end.X, BaseFloor + 1.2f, doorZ) };
		_base.AddChild(door);
		Omni(door, "Indicator", new Vector3(0, 1.5f, 0.45f), new Color(1f, 0.25f, 0.2f), 2f, 3.5f);
		QueueScript(door, AirlockScriptPath, new Dictionary<string, Variant>
		{
			["OpenOffset"] = new Vector3(1.45f, 0f, 0f),
			["DoorColor"] = new Color(1f, 0.55f, 0.1f),
		});

		// Ramp from the door threshold (y = BaseFloor) down to the regolith.
		var ramp = new CsgPolygon3D
		{
			Name = "AirlockRamp",
			Polygon = new[] { new Vector2(0, 0), new Vector2(0, BaseFloor + 0.02f), new Vector2(2.2f, 0) },
			Mode = CsgPolygon3D.ModeEnum.Depth,
			Depth = 2.0f,
			Position = new Vector3(end.X - 1f, 0f, end.Z),
			RotationDegrees = new Vector3(0, -90, 0),
			UseCollision = true,
			Material = _walkway,
		};
		_base.AddChild(ramp);

		// Parking pad with painted bay lines and floodlight poles.
		Vector3 pad = RoverPadCenter;
		var bay = new Node3D { Name = "RoverBay", Position = pad };
		_base.AddChild(bay);
		Box(bay, "Pad", new Vector3(14f, 0.1f, 14f), Vector3.Zero, Mat(new Color(0.3f, 0.3f, 0.32f), 0f, 0.9f), collision: true);
		var paint = Mat(new Color(0.95f, 0.8f, 0.15f));
		Box(bay, "LineLeft", new Vector3(0.2f, 0.02f, 10f), new Vector3(-2.6f, 0.06f, 0), paint);
		Box(bay, "LineRight", new Vector3(0.2f, 0.02f, 10f), new Vector3(2.6f, 0.06f, 0), paint);
		Box(bay, "LineFront", new Vector3(5.4f, 0.02f, 0.2f), new Vector3(0, 0.06f, -5f), paint);
		foreach (float x in new[] { -6.5f, 6.5f })
		{
			Cyl(bay, $"FloodPole{(x < 0 ? "L" : "R")}", 0.08f, 5f, new Vector3(x, 2.5f, -6.5f), Vector3.Zero, _trim);
			Box(bay, $"FloodHead{(x < 0 ? "L" : "R")}", new Vector3(0.6f, 0.25f, 0.4f), new Vector3(x, 5f, -6.3f), _amber, rotationDegrees: new Vector3(-30, 0, 0));
		}
		Label(_base, "RoverBaySign", "ROVER BAY", new Vector3(end.X, 3.6f, end.Z + 0.1f), Vector3.Zero, 0.008f, 96, new Color(1f, 0.8f, 0.3f), outline: 16);

		AddClearZone(pad, new Vector2(16f, 16f));
	}

	// ======================================================================
	//  Exterior details, pressurized zone
	// ======================================================================

	private void AddExteriorDetails(string name, Vector3 c, Vector2 outer, float outerH)
	{
		// Amber marker lights on each outer wall, offset from the centre so they miss tube doorways.
		var dirs = new[] { Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward };
		foreach (Vector3 n in dirs)
		{
			float half = Mathf.Abs(n.X) > 0 ? outer.X / 2f : outer.Y / 2f;
			Vector3 side = new(n.Z, 0, -n.X);
			float sideHalf = Mathf.Abs(n.X) > 0 ? outer.Y / 2f : outer.X / 2f;
			Vector3 p = c + n * (half + 0.03f) + side * (sideHalf * 0.6f);
			Box(_base, $"{name}Marker{n.X:0}{n.Z:0}", new Vector3(0.9f, 0.12f, 0.9f) * new Vector3(Mathf.Abs(n.Z) + 0.07f, 1, Mathf.Abs(n.X) + 0.07f),
				new Vector3(p.X, outerH - 1.2f, p.Z), _amber);
		}
		Label(_base, $"{name}Sign", name.ToUpper(), new Vector3(c.X, outerH + 2.5f, c.Z), Vector3.Zero, 0.03f, 96, new Color(0.85f, 0.92f, 1f), billboard: true, layers: 2);
	}

	/// <summary>Convex octagonal prism matching a Sides=8 CSGCylinder3D rotated 22.5 degrees.</summary>
	private static ConvexPolygonShape3D OctagonPrism(float circumRadius, float height)
	{
		var points = new Vector3[16];
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.DegToRad(22.5f + i * 45f);
			var p = new Vector3(Mathf.Cos(a) * circumRadius, 0, Mathf.Sin(a) * circumRadius);
			points[i] = p + new Vector3(0, -height / 2f, 0);
			points[i + 8] = p + new Vector3(0, height / 2f, 0);
		}
		return new ConvexPolygonShape3D { Points = points };
	}

	private void BuildPressurizedZone()
	{
		var zone = new Area3D { Name = "PressurizedZone" };
		_base.AddChild(zone);
		int i = 0;
		foreach (var (pos, shape) in _pressurized)
			zone.AddChild(new CollisionShape3D { Name = $"Volume{i++}", Position = pos, Shape = shape });
		QueueScript(zone, HubZoneScriptPath);
	}

	// ======================================================================
	//  Room interiors (props, screens, mission consoles)
	// ======================================================================

	private void BuildRoomInteriors()
	{
		float f = BaseFloor;

		// ---------------- OXYGEN (-36, -26), 14 x 12, door south ----------------
		{
			var room = Group("Oxygen");
			var tank = Mat(new Color(0.3f, 0.8f, 1f), 0.6f, 0.3f);
			for (int i = 0; i < 6; i++)
				Cyl(room, $"O2Tank{i}", 0.55f, 2.8f, new Vector3(-41f + i * 2f, f + 1.4f, -30.6f), Vector3.Zero, tank, collision: true);
			Box(room, "Electrolyzer", new Vector3(1.4f, 1.8f, 3f), new Vector3(-41.8f, f + 0.9f, -24.5f), Mat(new Color(0.9f, 0.9f, 0.92f)), collision: true);
			Cyl(room, "Manifold", 0.12f, 11f, new Vector3(-36f, f + 3.2f, -31.4f), new Vector3(0, 0, 90), _trim);
			Screen(room, "O2Screen", WallScreen(new Vector3(-29f, 2.6f, -26f), Vector3.Left), new Vector2(2.4f, 1.3f),
				"OXYGEN GENERATION\nSUIT O2: {O2}%\nELECTROLYSIS: ONLINE\nSTORAGE: 6 x 400 L", PhosphorCyan);
		}

		// ---------------- POWER (-62, 0), 12 x 14, door east ----------------
		{
			var room = Group("Power");
			var battery = Mat(new Color(0.2f, 0.2f, 0.22f), 0.3f, 0.5f);
			for (int i = 0; i < 4; i++)
			{
				float z = -5.1f + i * 3.4f;
				Box(room, $"Battery{i}", new Vector3(1.2f, 2.4f, 2.6f), new Vector3(-67.2f, f + 1.2f, z), battery, collision: true);
				Box(room, $"BatteryStripe{i}", new Vector3(0.02f, 0.25f, 2.4f), new Vector3(-66.58f, f + 2.1f, z), Mat(new Color(0.95f, 0.75f, 0.1f)));
				Box(room, $"BatteryLed{i}", new Vector3(0.02f, 0.1f, 0.5f), new Vector3(-66.58f, f + 1.6f, z), Emissive(new Color(0.3f, 1f, 0.4f), 3f));
			}
			Screen(room, "PowerScreen", WallScreen(new Vector3(-62f, 2.6f, -7f), Vector3.Back), new Vector2(2.6f, 1.3f),
				"POWER GRID\nCHARGE: {PWR} / {MAXPWR} %\nSOLAR ARRAY: TRACKING\nLOAD: 4.2 kW", PhosphorAmber);
			Console(room, "PowerConsole", new Vector3(-60f, f + 0.6f, 5.5f), Zone.PowerRoom, "Power Grid Repair",
				"Connect the pipes from start to finish to restore the coolant loop.", Tool.Wrench,
				oxygen: 0f, power: 50f, science: 10, canisters: 0, new Color(0.3f, 0.85f, 1f));
			BuildSolarField(new Vector3(-86f, 0f, 0f));
		}

		// ---------------- SHIELD (-36, 26), 12 x 12, door north, thick walls ----------------
		{
			var room = Group("Shield");
			var bunk = Mat(new Color(0.35f, 0.15f, 0.12f));
			Box(room, "BunkWest", new Vector3(1.0f, 0.5f, 5f), new Vector3(-41.3f, f + 0.25f, 27f), bunk, collision: true);
			Box(room, "BunkEast", new Vector3(1.0f, 0.5f, 5f), new Vector3(-30.7f, f + 0.25f, 27f), bunk, collision: true);
			for (int i = 0; i < 5; i++)
				Box(room, $"Locker{i}", new Vector3(0.9f, 2.2f, 0.6f), new Vector3(-39.6f + i * 1.0f, f + 1.1f, 31.6f), Mat(new Color(0.5f, 0.52f, 0.55f), 0.4f, 0.5f), collision: true);
			Box(room, "AlertStrip", new Vector3(10f, 0.08f, 0.08f), new Vector3(-36f, f + 3.5f, 31.9f), Emissive(new Color(1f, 0.15f, 0.1f), 2f));
			Screen(room, "ShieldScreen", WallScreen(new Vector3(-31.5f, 2.3f, 32f), Vector3.Forward), new Vector2(2.2f, 1.2f),
				"RADIATION SHELTER\nSTATUS: READY\nDOSE TODAY: 0.31 mSv\nSPE ALERT: NONE", PhosphorAmber);
			// Regolith bag berm around the outside: extra radiation shielding.
			var bags = _regolith ?? (Material)Mat(RegolithBase);
			Box(_base, "BermWest", new Vector3(1.4f, 2.6f, 15f), new Vector3(-43.8f, 1.3f, 26f), bags, collision: true);
			Box(_base, "BermEast", new Vector3(1.4f, 2.6f, 15f), new Vector3(-28.2f, 1.3f, 26f), bags, collision: true);
			Box(_base, "BermSouth", new Vector3(17f, 2.6f, 1.4f), new Vector3(-36f, 1.3f, 33.8f), bags, collision: true);
		}

		// ---------------- DRILL SITE: remote, out on the surface (see EnvironmentBuilder.DrillSite.cs) ----
		BuildDrillSite(RoverPadCenter);

		// ---------------- PLANT BIO-DOME (62, 0), r 9.7, door west ----------------
		{
			var room = Group("Plant");
			var soil = Mat(new Color(0.25f, 0.17f, 0.1f));
			var leaf = Mat(new Color(0.2f, 0.65f, 0.22f), 0f, 0.8f);
			var rng = new RandomNumberGenerator { Seed = (ulong)(NoiseSeed + 505) };
			for (int row = 0; row < 3; row++)
			{
				float z = -4f + row * 4f;
				Box(room, $"Planter{row}", new Vector3(11f, 0.8f, 1.2f), new Vector3(63.5f, f + 0.4f, z), soil, collision: true);
				for (int k = 0; k < 9; k++)
				{
					float s = rng.RandfRange(0.28f, 0.5f);
					room.AddChild(new CsgSphere3D { Name = $"Plant{row}_{k}", Radius = s, RadialSegments = 10, Rings = 6, Position = new Vector3(58.8f + k * 1.15f, f + 0.8f + s * 0.6f, z), Material = leaf });
				}
			}
			Omni(_lights, "GrowLightA", new Vector3(60f, 4.2f, 0f), new Color(1f, 0.45f, 0.9f), 1.4f, 9f);
			Omni(_lights, "GrowLightB", new Vector3(66f, 4.2f, 0f), new Color(1f, 0.45f, 0.9f), 1.4f, 9f);
			Omni(_lights, "PlantLight", new Vector3(62f, 5.5f, 0f), new Color(0.9f, 1f, 0.9f), 1.0f, 12f);
			Console(room, "BioConsole", new Vector3(56.5f, f + 0.6f, 6.2f), Zone.BioDome, "Hydroponics Harvest",
				"Reconnect the nutrient feed lines to restore hydroponic flow.", Tool.Scanner,
				oxygen: 30f, power: 0f, science: 5, canisters: 1, new Color(0.4f, 1f, 0.4f));
			var stand = new Node3D { Name = "ScreenStand", Position = new Vector3(56f, 0, -6.4f) };
			room.AddChild(stand);
			Cyl(stand, "Post", 0.06f, 1.4f, new Vector3(0, f + 0.7f, 0), Vector3.Zero, _trim);
			Screen(stand, "PlantScreen", new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(60), 0)), new Vector3(0, f + 1.9f, 0)),
				new Vector2(1.4f, 0.8f), "HYDROPONICS\nGROWTH: 87%\nO2 OUTPUT: NOMINAL", PhosphorGreen);
		}

		// ---------------- TELESCOPE (36, 26), r 7.6, door north ----------------
		{
			var room = Group("Telescope");
			var white = Mat(new Color(0.9f, 0.9f, 0.92f), 0.2f, 0.4f);
			Cyl(room, "Pier", 0.6f, 1.8f, new Vector3(36f, f + 0.9f, 27.5f), Vector3.Zero, _trim, collision: true);
			Box(room, "Mount", new Vector3(1.0f, 0.6f, 1.0f), new Vector3(36f, f + 2.0f, 27.5f), _trim);
			Cyl(room, "OpticalTube", 0.65f, 5.5f, new Vector3(36f, f + 3.6f, 26.3f), new Vector3(-40, 0, 0), white, sides: 24);
			Cyl(room, "Aperture", 0.7f, 0.2f, new Vector3(36f, f + 5.7f, 24.5f), new Vector3(-40, 0, 0), _dark, sides: 24);
			Screen(room, "TelescopeScreen", WallScreen(new Vector3(36f, 2.4f, 33.6f), Vector3.Forward), new Vector2(2.6f, 1.3f),
				"DEEP SPACE TRACKING\nTARGET: NOVA V1405 CAS\nMAG 5.1   EXP 30 S\nGUIDING: LOCKED", PhosphorCyan);
		}
	}

	private Node3D Group(string name)
	{
		var g = new Node3D { Name = name };
		_interiors.AddChild(g);
		return g;
	}

	/// <summary>Screen transform for a wall-mounted display: <paramref name="facing"/> = direction into the room.</summary>
	private static Transform3D WallScreen(Vector3 wallPoint, Vector3 facing)
	{
		Vector3 pos = wallPoint + facing * 0.12f;
		return new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(YawFacing(facing)), 0)), new Vector3(pos.X, wallPoint.Y + BaseFloor, pos.Z));
	}

	private void Console(Node parent, string name, Vector3 pos, Zone zone, string mission, string title, Tool tool,
		float oxygen, float power, int science, int canisters, Color color)
	{
		var console = new CsgBox3D { Name = name, Size = new Vector3(1.2f, 1.2f, 0.6f), Position = pos };
		parent.AddChild(console);
		QueueScript(console, ConsoleScriptPath, new Dictionary<string, Variant>
		{
			["Zone"] = (int)zone,
			["MissionName"] = mission,
			["MinigameTitle"] = title,
			["RequiredTool"] = (int)tool,
			["OxygenReward"] = oxygen,
			["PowerReward"] = power,
			["ScienceReward"] = science,
			["CanisterReward"] = canisters,
			["BaseColor"] = color,
			["ObjectiveGlow"] = color.Lightened(0.15f),
		});
	}

	private void BuildSolarField(Vector3 center)
	{
		var field = new Node3D { Name = "SolarField", Position = center };
		_base.AddChild(field);
		var panel = Mat(new Color(0.05f, 0.08f, 0.3f), 0.7f, 0.15f);
		for (int row = 0; row < 3; row++)
		for (int col = 0; col < 3; col++)
		{
			var p = new Vector3(-6f + col * 6f, 0f, -8f + row * 8f);
			Cyl(field, $"Post{row}{col}", 0.1f, 1.6f, p + new Vector3(0, 0.8f, 0), Vector3.Zero, _trim);
			Box(field, $"Panel{row}{col}", new Vector3(5f, 0.1f, 2.8f), p + new Vector3(0, 1.7f, 0), panel, collision: true, rotationDegrees: new Vector3(30, 0, 0));
		}
		AddClearZone(center, new Vector2(20f, 22f));
	}

	// ======================================================================
	//  Launch pad with an ascent-vehicle placeholder (ArtReplacer swaps it for the RocketLander model)
	// ======================================================================

	private void BuildLaunchPad(Vector3 c)
	{
		var pad = new Node3D { Name = "LaunchPad", Position = c };
		_base.AddChild(pad);
		Cyl(pad, "Pad", 7f, 0.2f, new Vector3(0, 0.1f, 0), Vector3.Zero, Mat(new Color(0.42f, 0.42f, 0.44f), 0f, 0.9f), sides: 32, collision: true);
		Cyl(pad, "Lander", 1.5f, 10f, new Vector3(0, 0.2f + 5f, 0), Vector3.Zero, Mat(new Color(0.6f, 0.6f, 0.62f)), sides: 16, collision: true);
		Label(pad, "Sign", "ASCENT VEHICLE", new Vector3(0, 13.5f, 0), Vector3.Zero, 0.03f, 96, new Color(1f, 0.75f, 0.4f), billboard: true, layers: 2);
		AddClearZone(c, new Vector2(16f, 16f));
	}
}

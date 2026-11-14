using Godot;
using System.Collections.Generic;
using System.Linq;

// Central Lobby = Command Center interior: slanted desks with screens, overhead monitor bank,
// equipment racks, a live base map, and windows looking out at the Rover Bay.
//
// Octagon facing (angle measured from +Z toward +X): S = 0 (window), SE = 45 (window),
// E = 90 (east corridor), NE = 135 (orbit map), N = 180 (monitor bank), NW = -135 (life support),
// W = -90 (west corridor), SW = -45 (base map).
public partial class EnvironmentBuilder
{
	private static readonly Color PhosphorGreen = new(0.25f, 1f, 0.45f);
	private static readonly Color PhosphorCyan = new(0.25f, 0.9f, 1f);
	private static readonly Color PhosphorAmber = new(1f, 0.7f, 0.2f);

	private const float LobbyInnerR = 10.5f;
	private const float LobbyOuterR = 11f;
	private static float LobbyInnerApothem => LobbyInnerR * Mathf.Cos(Mathf.Pi / 8f);
	private static float LobbyWallMid => (LobbyInnerR + LobbyOuterR) / 2f * Mathf.Cos(Mathf.Pi / 8f);

	private static readonly float[] WindowFacets = { 0f, 45f };

	private static Vector3 FacetDir(float angleDeg) =>
		new(Mathf.Sin(Mathf.DegToRad(angleDeg)), 0f, Mathf.Cos(Mathf.DegToRad(angleDeg)));

	/// <summary>Window openings in the lobby hull (added to the shell's void list).</summary>
	private void BuildLobbyShellCuts()
	{
		foreach (float a in WindowFacets)
		{
			Vector3 p = FacetDir(a) * LobbyWallMid;
			_voids.Add(new CsgBox3D
			{
				Name = $"LobbyWindowCut{a:0}",
				Size = new Vector3(6.4f, 2.8f, 2.0f),
				Position = new Vector3(p.X, BaseFloor + 2.4f, p.Z),
				RotationDegrees = new Vector3(0, a, 0),
				Material = _trim,
			});
		}

		// Roof hardware: high-gain dish and the exterior sign.
		float roof = BaseFloor + 6.35f + 0.4f;
		Cyl(_base, "LobbyDishMast", 0.12f, 3f, new Vector3(5f, roof + 1.5f, -5f), Vector3.Zero, _trim);
		Cyl(_base, "LobbyDish", 1.6f, 0.4f, new Vector3(5f, roof + 3.1f, -5f), new Vector3(-35, 30, 0), Mat(new Color(0.92f, 0.92f, 0.94f)), cone: true, sides: 28);
		Label(_base, "LobbySign", "COMMAND CENTER", new Vector3(0, roof + 4.5f, 0), Vector3.Zero, 0.03f, 96, new Color(0.85f, 0.92f, 1f), billboard: true, layers: 2);
	}

	private void BuildLobbyInterior()
	{
		var cc = new Node3D { Name = "CommandCenter" };
		_interiors.AddChild(cc);
		float f = BaseFloor;

		// ---------------- Windows (glass separate from the shell so it can skip shadow casting) ---
		var glass = Glass(new Color(0.7f, 0.85f, 1f), 0.12f);
		foreach (float a in WindowFacets)
		{
			Vector3 p = FacetDir(a) * LobbyWallMid;
			var win = new Node3D { Name = $"Window{a:0}", Position = new Vector3(p.X, f + 2.4f, p.Z), RotationDegrees = new Vector3(0, a, 0) };
			cc.AddChild(win);
			var pane = Box(win, "Glass", new Vector3(6.4f, 2.8f, 0.06f), Vector3.Zero, glass, collision: true);
			pane.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			Box(win, "MullionL", new Vector3(0.1f, 2.8f, 0.3f), new Vector3(-2.13f, 0, 0), _trim);
			Box(win, "MullionR", new Vector3(0.1f, 2.8f, 0.3f), new Vector3(2.13f, 0, 0), _trim);
			Box(win, "Sill", new Vector3(6.6f, 0.12f, 0.6f), new Vector3(0, -1.46f, -0.2f), _trim);
		}

		// ---------------- Overhead monitor bank (north facet) ----------------
		{
			Vector3 n = FacetDir(180f);
			var bank = new Node3D
			{
				Name = "MonitorBank",
				Position = n * (LobbyInnerApothem - 0.35f) + new Vector3(0, f + 4.1f, 0),
				RotationDegrees = new Vector3(10, 0, 0), // tilted down toward the desks
			};
			cc.AddChild(bank);
			Box(bank, "Frame", new Vector3(7.6f, 3.3f, 0.25f), new Vector3(0, 0, -0.16f), _dark);
			Screen(bank, "FlightStatus", Xf(0, 0.1f), new Vector2(3.6f, 2.2f),
				"FLIGHT STATUS: {STATUS}\nSOL {SOL}     MET {MET}\nOBJECTIVE: {OBJ}\nCOMMS: DSN LOCKED", PhosphorGreen);
			Screen(bank, "OrbitalData", Xf(-2.8f, 0.6f), new Vector2(1.8f, 1.0f), "ORBITAL DATA\nPERILUNE 15 KM\nINC 90.0 DEG", PhosphorCyan);
			Screen(bank, "LifeSupport", Xf(-2.8f, -0.55f), new Vector2(1.8f, 1.0f), "LIFE SUPPORT\nO2 {O2}%  HP {HP}%\nCO2 SCRUB: OK", PhosphorCyan);
			Screen(bank, "PowerData", Xf(2.8f, 0.6f), new Vector2(1.8f, 1.0f), "POWER\n{PWR} / {MAXPWR} %\nSOLAR: TRACKING", PhosphorAmber);
			Screen(bank, "ScienceData", Xf(2.8f, -0.55f), new Vector2(1.8f, 1.0f), "SCIENCE\n{SCI} PTS\nSOL TIME {TIME}", PhosphorGreen);
			Label(bank, "Banner", "LUNAR OUTPOST  -  MISSION CONTROL", new Vector3(0, 1.85f, 0.05f), Vector3.Zero, 0.006f, 64, new Color(0.85f, 0.92f, 1f), outline: 8);
		}

		// ---------------- Wing displays (NE orbit map, NW systems) ----------------
		{
			Node3D ne = WallNode(cc, "OrbitMapWall", 135f, f + 3.4f);
			Node3D orbit = Screen(ne, "OrbitMap", Xf(0, 0), new Vector2(3.2f, 1.9f), "ORBIT MAP\n\n\n\nLRO PASS IN 00:42", PhosphorGreen);
			var ring = new CsgTorus3D { Name = "OrbitRing", InnerRadius = 0.55f, OuterRadius = 0.6f, Sides = 48, RingSides = 4, Position = new Vector3(0.6f, -0.05f, 0.04f), RotationDegrees = new Vector3(90, 0, 0), Material = Emissive(PhosphorGreen, 3f) };
			orbit.AddChild(ring);
			orbit.AddChild(new CsgSphere3D { Name = "Moon", Radius = 0.12f, Position = new Vector3(0.6f, -0.05f, 0.04f), Material = Emissive(PhosphorGreen, 3f) });
			orbit.AddChild(new CsgSphere3D { Name = "Satellite", Radius = 0.04f, Position = new Vector3(1.17f, 0.1f, 0.05f), Material = Emissive(PhosphorAmber, 4f) });

			Node3D nw = WallNode(cc, "SystemsWall", -135f, f + 3.4f);
			Screen(nw, "Systems", Xf(0, 0), new Vector2(3.2f, 1.9f),
				"BASE SYSTEMS\nSRB: READY     CORE: READY\nESM: NOT READY\nFARS: ATTACHED  LAS: ATTACHED\nVESSEL STATUS: OPERATIONAL", PhosphorGreen);
		}

		// ---------------- Base map (SW facet): live schematic of the layout ----------------
		BuildBaseMapScreen(WallNode(cc, "BaseMapWall", -45f, f + 2.5f));

		// ---------------- Two rows of slanted control desks facing the monitor bank -----------
		string[][] deskTexts =
		{
			new[] { "SRB: READY\nCORE: READY\nESM: NOT READY", "FLIGHT STATUS:\nAWAITING LAUNCH", "MET: {MET}\nTIME: {TIME}" },
			new[] { "COMMS LINK\nDSN: LOCKED\nSNR 18.2 DB", "NAV\nLAT 0.67 N\nLON 23.47 E", "O2 {O2}%\nPWR {PWR}%" },
			new[] { "RAD: 0.31 mSv\nSHIELD: READY", "ORBIT MAP\nLRO +00:42", "DRILL\nRPM 0  TEMP OK" },
			new[] { "HYDROPONICS\nGROWTH 87%", "SOL {SOL}\nOBJ: {OBJ}", "TELESCOPE\nTARGET: NOVA" },
			new[] { "THERMAL\nRAD LOOP: OK", "EVA SUITS\n2 / 2 READY", "ROVER\nBAY 1: PARKED" },
			new[] { "SCIENCE\n{SCI} PTS", "FDO\nGO", "GUIDANCE\nGO" },
		};
		Color[] palette = { PhosphorGreen, PhosphorCyan, PhosphorGreen, PhosphorAmber, PhosphorCyan, PhosphorGreen };
		// Curved command console around a holographic projector (EnvironmentBuilder.Interior.cs).
		BuildCommandConsole(cc, deskTexts, palette);
		// ---------------- Equipment racks flanking the corridor doors ----------------
		int rackIndex = 0;
		foreach (float a in new[] { 90f, -90f })
		{
			Vector3 n = FacetDir(a);
			Vector3 side = new(n.Z, 0, -n.X);
			foreach (float s in new[] { -3.0f, 3.0f })
			{
				var rack = new Node3D
				{
					Name = $"Rack{rackIndex++}",
					Position = n * (LobbyInnerApothem - 0.45f) + side * s + new Vector3(0, f, 0),
					RotationDegrees = new Vector3(0, YawFacing(-n), 0),
				};
				cc.AddChild(rack);
				BuildRack(rack, rackIndex);
			}
		}

		// ---------------- Floor emblem, ceiling light panels, lighting rig ----------------
		cc.AddChild(new CsgTorus3D
		{
			Name = "FloorEmblem",
			InnerRadius = 1.6f,
			OuterRadius = 1.8f,
			Sides = 48,
			RingSides = 4,
			Position = new Vector3(0, f + 0.005f, 4.5f),
			Scale = new Vector3(1, 0.05f, 1),
			Material = Emissive(PhosphorCyan, 1.5f),
		});
		float ceiling = f + 6.35f;
		for (int i = 0; i < 8; i++)
		{
			float ang = i * 45f + 22.5f;
			Vector3 p = FacetDir(ang) * 6.2f;
			Box(cc, $"CeilingPanel{i}", new Vector3(2.2f, 0.06f, 0.6f), new Vector3(p.X, ceiling - 0.05f, p.Z), _strip, rotationDegrees: new Vector3(0, ang, 0));
		}
		Omni(_lights, "LobbyCenter", new Vector3(0, ceiling - 0.8f, 0), new Color(0.9f, 0.95f, 1f), 1.4f, 14f);
		for (int i = 0; i < 4; i++)
		{
			Vector3 p = FacetDir(i * 90f + 45f) * 6f;
			Omni(_lights, $"LobbyRing{i}", new Vector3(p.X, ceiling - 0.8f, p.Z), new Color(0.85f, 0.92f, 1f), 0.8f, 9f);
		}
		Omni(_lights, "MonitorGlow", FacetDir(180f) * 6.5f + new Vector3(0, f + 3.5f, 0), new Color(0.4f, 1f, 0.6f), 0.5f, 7f);
	}

	private static Transform3D Xf(float x, float y) => new(Basis.Identity, new Vector3(x, y, 0));

	/// <summary>A node on the inside of a lobby facet, facing the room centre.</summary>
	private static Node3D WallNode(Node3D parent, string name, float facetAngle, float height)
	{
		Vector3 n = FacetDir(facetAngle);
		var node = new Node3D
		{
			Name = name,
			Position = n * (LobbyInnerApothem - 0.15f) + new Vector3(0, height, 0),
			RotationDegrees = new Vector3(0, YawFacing(-n), 0),
		};
		parent.AddChild(node);
		return node;
	}

	/// <summary>
	/// Slanted control desk. Built in a local frame where +X runs from the operator toward the back
	/// of the desk and +Z along the desk's width; the node is then turned to face north.
	/// </summary>
	private void Desk(Node3D parent, string name, Vector3 frontCenter, float width, float yawDeg, string[] texts, Color color)
	{
		var desk = new Node3D { Name = name, Position = frontCenter, RotationDegrees = new Vector3(0, 90f + yawDeg, 0) };
		parent.AddChild(desk);

		// Side profile: flat writing lip at 0.8 m, panel slanting up to 1.2 m, flat top, vertical back.
		desk.AddChild(new CsgPolygon3D
		{
			Name = "Body",
			Polygon = new[]
			{
				new Vector2(0f, 0f), new Vector2(0f, 0.8f), new Vector2(0.25f, 0.8f),
				new Vector2(0.55f, 1.2f), new Vector2(0.9f, 1.2f), new Vector2(0.9f, 0f),
			},
			Mode = CsgPolygon3D.ModeEnum.Depth,
			Depth = width,
			Position = new Vector3(0, 0, width / 2f), // extrusion runs along -Z, so this centres it
			UseCollision = true,
			Material = Mat(new Color(0.42f, 0.44f, 0.48f), 0.3f, 0.5f),
		});
		Box(desk, "Kickplate", new Vector3(0.05f, 0.7f, width - 0.1f), new Vector3(-0.02f, 0.35f, 0), _dark);

		// Screens on the slanted panel: slope direction (0.6, 0.8), outward normal (-0.8, 0.6).
		var slanted = new Basis(new Vector3(0, 0, 1), new Vector3(0.6f, 0.8f, 0), new Vector3(-0.8f, 0.6f, 0));
		for (int i = 0; i < 3; i++)
		{
			float z = (i - 1) * width / 3f;
			var origin = new Vector3(0.4f - 0.8f * 0.03f, 1.0f + 0.6f * 0.03f, z);
			Screen(desk, $"Screen{i}", new Transform3D(slanted, origin), new Vector2(width / 3f - 0.12f, 0.36f), texts[i], color);
			Box(desk, $"Keyboard{i}", new Vector3(0.18f, 0.025f, 0.5f), new Vector3(0.12f, 0.812f, z), _dark);
		}

		// Chair on the operator side (-X), no collision so it never snags the player.
		var chair = new Node3D { Name = "Chair", Position = new Vector3(-0.7f, 0, 0) };
		desk.AddChild(chair);
		var fabric = Mat(new Color(0.12f, 0.16f, 0.3f));
		Cyl(chair, "Stem", 0.05f, 0.42f, new Vector3(0, 0.21f, 0), Vector3.Zero, _trim);
		Box(chair, "Seat", new Vector3(0.5f, 0.08f, 0.5f), new Vector3(0, 0.46f, 0), fabric);
		Box(chair, "Back", new Vector3(0.07f, 0.6f, 0.48f), new Vector3(-0.24f, 0.8f, 0), fabric, rotationDegrees: new Vector3(0, 0, -8));
	}

	private void BuildRack(Node3D rack, int seed)
	{
		Box(rack, "Cabinet", new Vector3(1.2f, 3.2f, 0.7f), new Vector3(0, 1.6f, 0), _dark, collision: true);
		var rng = new RandomNumberGenerator { Seed = (ulong)(NoiseSeed + 900 + seed) };
		Color[] leds = { new(0.3f, 1f, 0.4f), new(1f, 0.25f, 0.2f), new(1f, 0.75f, 0.2f), new(0.3f, 0.8f, 1f) };
		var mats = leds.Select(c => Emissive(c, 3f)).ToArray();
		for (int row = 0; row < 7; row++)
		for (int col = 0; col < 4; col++)
		{
			if (rng.Randf() < 0.35f)
				continue;
			Box(rack, $"Led{row}_{col}", new Vector3(0.06f, 0.03f, 0.02f), new Vector3(-0.42f + col * 0.12f, 0.6f + row * 0.18f, 0.36f), mats[rng.RandiRange(0, mats.Length - 1)]);
		}
		Screen(rack, "RackScreen", new Transform3D(Basis.Identity, new Vector3(0.12f, 2.6f, 0.37f)), new Vector2(0.7f, 0.42f),
			seed % 2 == 0 ? "NODE {SOL}\nLINK OK" : "CPU 23%\nMEM OK", seed % 2 == 0 ? PhosphorCyan : PhosphorGreen);
	}

	/// <summary>Schematic of the base drawn with emissive blocks (north up).</summary>
	private void BuildBaseMapScreen(Node3D wall)
	{
		Node3D map = Screen(wall, "BaseMap", Xf(0, 0), new Vector2(4.4f, 2.5f), "BASE MAP\n\n\n\n\n\n", PhosphorCyan, center: true);
		const float s = 0.027f; // metres of screen per metre of base
		var roomMat = Emissive(PhosphorCyan.Darkened(0.2f), 0.9f);
		var tubeMat = Emissive(PhosphorGreen.Darkened(0.2f), 0.8f);
		var youMat = Emissive(PhosphorAmber, 4f);
		Vector3 offset = new(-0.05f, -0.15f, 0.035f);
		foreach (var (a, b) in _tubes)
		{
			Vector3 mid = (a + b) / 2f, d = b - a;
			bool alongX = Mathf.Abs(d.X) > Mathf.Abs(d.Z);
			var size = alongX ? new Vector3(Mathf.Abs(d.X) * s, 0.03f, 0.005f) : new Vector3(0.03f, Mathf.Abs(d.Z) * s, 0.005f);
			Box(map, $"Tube{a.X:0}_{a.Z:0}_{b.X:0}_{b.Z:0}", size, offset + new Vector3(mid.X * s, -mid.Z * s, 0), tubeMat);
		}
		foreach (var (name, c, size, _) in _rooms)
			Box(map, $"Room{name}", new Vector3(size.X * s, size.Y * s, 0.006f), offset + new Vector3(c.X * s, -c.Z * s, 0.002f), roomMat);
		Box(map, "YouAreHere", new Vector3(0.08f, 0.08f, 0.01f), offset + new Vector3(0, 0, 0.008f), youMat);
	}

	// ======================================================================
	//  Emissive screen = bezel + glowing panel + Label3D text (optionally live)
	// ======================================================================

	private Node3D Screen(Node3D parent, string name, Transform3D xf, Vector2 size, string text, Color color, bool center = false)
	{
		var holder = new Node3D { Name = name, Transform = xf };
		parent.AddChild(holder);
		Box(holder, "Bezel", new Vector3(size.X + 0.06f, size.Y + 0.06f, 0.03f), new Vector3(0, 0, -0.02f), _bezel);
		Box(holder, "Panel", new Vector3(size.X, size.Y, 0.02f), Vector3.Zero, ScreenMaterial(color));

		string preview = PreviewText(text);
		string[] lines = preview.Split('\n');
		int maxChars = Mathf.Max(8, lines.Max(l => l.Length));
		const int font = 64;
		float px = Mathf.Min(size.Y * 0.85f / (lines.Length * font * 1.2f), size.X * 0.9f / (maxChars * font * 0.6f));

		var label = new Label3D
		{
			Name = "Text",
			Text = preview,
			FontSize = font,
			PixelSize = px,
			Modulate = color.Lightened(0.35f),
			OutlineSize = 0,
			Position = new Vector3(0, 0, 0.03f),
			HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
			VerticalAlignment = center ? VerticalAlignment.Top : VerticalAlignment.Center,
			Width = size.X * 0.92f / px,
			DoubleSided = false,
		};
		if (center)
			label.Position = new Vector3(0, size.Y * 0.45f, 0.03f);
		holder.AddChild(label);

		if (text.Contains('{'))
			QueueScript(label, LiveScreenScriptPath, new Dictionary<string, Variant> { ["Template"] = text });
		return holder;
	}

	/// <summary>Phosphor screen: near-black albedo with a dim emissive glow in the text colour.</summary>
	private StandardMaterial3D ScreenMaterial(Color color)
	{
		if (_screenMaterials.TryGetValue(color, out var mat))
			return mat;
		mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.01f, 0.02f, 0.02f),
			EmissionEnabled = true,
			Emission = color.Darkened(0.55f),
			EmissionEnergyMultiplier = 0.6f,
			Roughness = 0.15f,
		};
		_screenMaterials[color] = mat;
		return mat;
	}

	/// <summary>Editor preview values for live tokens (LiveScreen replaces them at runtime).</summary>
	private static string PreviewText(string template) => template
		.Replace("{SOL}", "1/9").Replace("{MET}", "0:00:00:00").Replace("{TIME}", "06:00")
		.Replace("{O2}", "100").Replace("{PWR}", "100").Replace("{MAXPWR}", "100").Replace("{HP}", "100")
		.Replace("{SCI}", "0").Replace("{OBJ}", "PLANT BIO-DOME").Replace("{STATUS}", "NOMINAL");
}

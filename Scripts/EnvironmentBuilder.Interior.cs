using Godot;
using System.Collections.Generic;
using System.Linq;

// "Starship" interior pass (runs after the base shell and lobby are built):
//   - Interior material kit: white / gray / dark steel plate with procedural normal maps, floor deck,
//     floor grate, glowing hazard stripes, cyan / blue / white emissives, additive hologram.
//   - Corridors: opaque octagonal liner + structural ribs + bulkhead door frames (CSGPolygon3D, Spin
//     mode), floor grate, floor-level glow strips, ceiling light panel, side accent strips, conduits.
//   - Rooms: wall panels between structural columns, kick plates, cyan accent band, ceiling beams,
//     floor grates, hazard thresholds at every doorway.
//   - Lobby: curved command console around a holographic projector pad.
//   - Lighting: shadowed SpotLight3Ds, tinted OmniLight3Ds, ReflectionProbes per room.
public partial class EnvironmentBuilder
{
	/// <summary>
	/// Direction a CSGPolygon3D Spin sweeps from its profile (local +X): +1 = toward local +Z.
	/// Measured in Godot 4.7.2: a 90 degree spin of a profile at +X covers +X .. -Z, so the sign is -1.
	/// </summary>
	private const float SpinSweepSign = -1f;

	private const float LinerInner = 1.5f;   // octagon circumradius of the corridor liner
	private const float LinerOuter = 1.56f;
	private const float RibSpacing = 2.4f;

	// ----- Interior kit materials -----
	private StandardMaterial3D _ikWallWhite, _ikMetalGray, _ikMetalDark, _ikDeck, _ikGrate, _ikHazard;
	private StandardMaterial3D _ikGlowCyan, _ikGlowBlue, _ikGlowWhite, _ikHolo;
	private readonly Dictionary<Color, StandardMaterial3D> _deckMats = new();

	// ======================================================================
	//  Material kit (procedural textures -> StandardMaterial3D)
	// ======================================================================

	private void BuildInteriorMaterials()
	{
		string dir = MaterialsDir.PathJoin("Interior");
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
		_deckMats.Clear();

		// Heavy steel plates, bevelled seams, corner rivets. Tiled at 0.28/m: each plate ~1.8 m x 0.9 m.
		Image plateHeight = PlateHeight(256, 128, 64, seed: 3);
		ImageTexture plateNormal = NormalFromHeight(plateHeight, 6f);
		ImageTexture plateAlbedo = AlbedoFromHeight(plateHeight, 0.62f);
		// Deck: larger 1 m square plates with a diamond tread.
		Image deckHeight = PlateHeight(256, 128, 128, seed: 5, tread: true);
		ImageTexture deckNormal = NormalFromHeight(deckHeight, 5f);
		ImageTexture deckAlbedo = AlbedoFromHeight(deckHeight, 0.7f);
		// Grate: bar grid over a dark void.
		Image grateHeight = GrateHeight(256);
		ImageTexture grateNormal = NormalFromHeight(grateHeight, 8f);
		ImageTexture grateAlbedo = AlbedoFromHeight(grateHeight, 0.15f);
		ImageTexture hazard = HazardStripes(256);

		Save(plateNormal, dir, "plate_normal.res");
		Save(plateAlbedo, dir, "plate_albedo.res");
		Save(deckNormal, dir, "deck_normal.res");
		Save(deckAlbedo, dir, "deck_albedo.res");
		Save(grateNormal, dir, "grate_normal.res");
		Save(grateAlbedo, dir, "grate_albedo.res");
		Save(hazard, dir, "hazard.res");

		StandardMaterial3D Plate(Color c, float metal, float rough, Texture2D albedo, Texture2D normal, float scale) => new()
		{
			AlbedoColor = c,
			AlbedoTexture = albedo,
			Metallic = metal,
			Roughness = rough,
			MetallicSpecular = 0.6f,
			NormalEnabled = true,
			NormalTexture = normal,
			NormalScale = 1f,
			Uv1Triplanar = true,
			Uv1WorldTriplanar = true,
			Uv1Scale = Vector3.One * scale,
			Uv1TriplanarSharpness = 6f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};

		_ikWallWhite = Save(Plate(new Color(0.9f, 0.91f, 0.93f), 0.35f, 0.28f, plateAlbedo, plateNormal, 0.28f), dir, "wall_white.tres");
		_ikMetalGray = Save(Plate(new Color(0.5f, 0.52f, 0.56f), 0.75f, 0.32f, plateAlbedo, plateNormal, 0.28f), dir, "metal_gray.tres");
		_ikMetalDark = Save(Plate(new Color(0.13f, 0.14f, 0.16f), 0.8f, 0.38f, plateAlbedo, plateNormal, 0.28f), dir, "metal_dark.tres");
		_ikDeck = Save(Plate(new Color(0.34f, 0.35f, 0.38f), 0.7f, 0.42f, deckAlbedo, deckNormal, 0.3f), dir, "deck.tres");
		_ikGrate = Save(Plate(new Color(0.42f, 0.44f, 0.47f), 0.8f, 0.45f, grateAlbedo, grateNormal, 1f), dir, "grate.tres");

		// Interior plates render both faces: if the camera is ever pressed into a wall it sees the
		// wall from behind (opaque steel) instead of looking straight through to the lunar surface.
		foreach (var m in new[] { _ikWallWhite, _ikMetalGray, _ikMetalDark, _ikDeck })
			m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

		_ikHazard = Save(new StandardMaterial3D
		{
			AlbedoTexture = hazard,
			Roughness = 0.5f,
			EmissionEnabled = true,          // "glowing" hazard stripes
			EmissionTexture = hazard,
			Emission = Colors.White,
			EmissionEnergyMultiplier = 0.45f,
			Uv1Triplanar = true,
			Uv1WorldTriplanar = true,
			Uv1Scale = Vector3.One * 2f,
		}, dir, "hazard_stripes.tres");

		_ikGlowCyan = Save(Emissive(new Color(0.25f, 0.85f, 1f), 3f), dir, "glow_cyan.tres");
		_ikGlowBlue = Save(Emissive(new Color(0.22f, 0.42f, 1f), 2.6f), dir, "glow_blue.tres");
		_ikGlowWhite = Save(Emissive(new Color(0.95f, 0.97f, 1f), 2f), dir, "glow_white.tres");
		_ikHolo = Save(new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			AlbedoColor = new Color(0.25f, 0.8f, 1f, 0.35f),
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = false,
		}, dir, "hologram.tres");
	}

	/// <summary>Floor deck plate tinted toward a room's identity colour.</summary>
	private StandardMaterial3D DeckMat(Color tint)
	{
		if (_deckMats.TryGetValue(tint, out var m))
			return m;
		m = (StandardMaterial3D)_ikDeck.Duplicate();
		m.AlbedoColor = new Color(0.34f, 0.35f, 0.38f).Lerp(tint, 0.35f);
		_deckMats[tint] = m;
		return m;
	}

	private T Save<T>(T res, string dir, string file) where T : Resource
	{
		string path = dir.PathJoin(file);
		if (ResourceSaver.Save(res, path) == Error.Ok)
			res.TakeOverPath(path);
		return res;
	}

	private static Image PlateHeight(int size, int plateW, int plateH, int seed, bool tread = false)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
		var noise = new FastNoiseLite { Seed = seed, Frequency = 0.05f, FractalOctaves = 3 };
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			int row = y / plateH;
			int ox = (row % 2) * plateW / 2;                // staggered plates
			int px = (x + ox) % plateW, py = y % plateH;
			int edge = Mathf.Min(Mathf.Min(px, plateW - 1 - px), Mathf.Min(py, plateH - 1 - py));
			float h = edge < 2 ? 0f : edge < 6 ? (edge - 2) / 4f * 0.85f : 0.85f;   // groove + bevel
			// rivets 6 px in from each corner
			foreach (int cx in new[] { 7, plateW - 8 })
			foreach (int cy in new[] { 7, plateH - 8 })
			{
				float d = new Vector2(px - cx, py - cy).Length();
				if (d < 3.2f)
					h = Mathf.Max(h, 1f - d / 6f);
			}
			if (tread && edge >= 6)
			{
				int tx = (px + py) % 16, ty = (px - py + 1024) % 16;
				if (tx < 2 || ty < 2)
					h += 0.08f;
			}
			h += noise.GetNoise2D(x, y) * 0.03f;
			img.SetPixel(x, y, new Color(h, h, h));
		}
		return img;
	}

	private static Image GrateHeight(int size)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			bool barX = x % 16 < 4, barY = y % 32 < 3;
			float h = barX || barY ? (barX && barY ? 1f : 0.85f) : 0.05f;
			img.SetPixel(x, y, new Color(h, h, h));
		}
		return img;
	}

	private static ImageTexture NormalFromHeight(Image height, float strength)
	{
		var n = (Image)height.Duplicate();
		n.Convert(Image.Format.Rgba8);
		n.BumpMapToNormalMap(strength);
		n.GenerateMipmaps();
		return ImageTexture.CreateFromImage(n);
	}

	/// <summary>Light albedo that darkens seams/grooves (grime) - tinted by the material's AlbedoColor.</summary>
	private static ImageTexture AlbedoFromHeight(Image height, float seamDarkness)
	{
		int s = height.GetWidth();
		var img = Image.CreateEmpty(s, s, false, Image.Format.Rgb8);
		for (int y = 0; y < s; y++)
		for (int x = 0; x < s; x++)
		{
			float h = height.GetPixel(x, y).R;
			float v = Mathf.Lerp(seamDarkness, 1f, Mathf.Clamp(h / 0.85f, 0f, 1f));
			img.SetPixel(x, y, new Color(v, v, v));
		}
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	private static ImageTexture HazardStripes(int size)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
		var yellow = new Color(0.95f, 0.72f, 0.1f);
		var black = new Color(0.06f, 0.06f, 0.07f);
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
			img.SetPixel(x, y, ((x + y) / 32) % 2 == 0 ? yellow : black);
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	// ======================================================================
	//  Interior pass
	// ======================================================================

	private void BuildInteriorDetail()
	{
		var corridors = new Node3D { Name = "Corridors" };
		_interiors.AddChild(corridors);
		foreach (var span in _tubeSpans)
			BuildCorridor(corridors, span);

		var rooms = new Node3D { Name = "RoomDetail" };
		_interiors.AddChild(rooms);
		foreach (var (name, c, size, _) in _rooms)
		{
			List<Vector3> doors = DoorDirections(c);
			switch (name)
			{
				case "Oxygen":
				case "Power":
					DetailBoxRoom(rooms, name, c, size, RoomHeight, doors);
					break;
				case "Shield":
					DetailBoxRoom(rooms, name, c, size, 3.8f, doors);
					break;
				case "Lobby":
					DetailOctRoom(rooms, name, c, LobbyInnerR, 6.35f, doors, WindowFacets);
					break;
				case "WestHub":
				case "EastHub":
					DetailOctRoom(rooms, name, c, 4.3f, RoomHeight, doors, System.Array.Empty<float>());
					break;
				case "Telescope":
					DetailTelescope(rooms, c, size.X / 2f);
					break;
			}
		}

		LightInterior();
	}

	private List<Vector3> DoorDirections(Vector3 roomCenter)
	{
		var doors = new List<Vector3>();
		foreach (var (a, b) in _tubes)
		{
			if (a.DistanceTo(roomCenter) < 0.5f)
				doors.Add((b - a).Normalized());
			else if (b.DistanceTo(roomCenter) < 0.5f)
				doors.Add((a - b).Normalized());
		}
		return doors;
	}

	// ----------------------------------------------------------------------
	//  Corridors
	// ----------------------------------------------------------------------

	/// <summary>Frame for an octagonal spin ring around a tube: local Y = tube axis, flat floor/ceiling faces.</summary>
	private static Basis TubeRingBasis(Vector3 dir)
	{
		Vector3 y = dir.Normalized();
		Vector3 x = Vector3.Up.Rotated(y, Mathf.DegToRad(22.5f)).Normalized();
		Vector3 z = x.Cross(y).Normalized();
		return new Basis(x, y, z);
	}

	/// <summary>Octagonal ring (or sleeve) of the given radii and length, centred at <paramref name="center"/>.</summary>
	private static CsgPolygon3D OctRing(string name, Vector3 dir, Vector3 center, float rIn, float rOut, float length, Material mat)
	{
		float h = length / 2f;
		return new CsgPolygon3D
		{
			Name = name,
			Mode = CsgPolygon3D.ModeEnum.Spin,
			SpinDegrees = 360f,
			SpinSides = 8,
			Polygon = new[] { new Vector2(rIn, -h), new Vector2(rOut, -h), new Vector2(rOut, h), new Vector2(rIn, h) },
			Transform = new Transform3D(TubeRingBasis(dir), center),
			Material = mat,
		};
	}

	/// <summary>Removes the part of a ring below <paramref name="cutY"/> (so ribs/frames stand on the floor instead of crossing it).</summary>
	private static void CutBelow(CsgShape3D ring, float cutY)
	{
		Transform3D inv = ring.Transform.AffineInverse();
		var worldCenter = new Vector3(ring.Position.X, cutY - 1f, ring.Position.Z);
		ring.AddChild(new CsgBox3D
		{
			Name = "FloorCut",
			Operation = CsgShape3D.OperationEnum.Subtraction,
			Size = new Vector3(4f, 2f, 4f),
			Transform = new Transform3D(inv.Basis, inv * worldCenter),
		});
	}

	private void BuildCorridor(Node3D parent, (string name, Vector3 a, Vector3 dir, float start, float end) span)
	{
		var (name, a, dir, s0, s1) = span;
		bool fromJunction = s0 < 0.1f;                    // spur leaving another corridor (Rover Bay)
		if (fromJunction)
			s0 = LinerOuter + 0.06f;
		float len = s1 - s0;
		if (len < 1f)
			return;

		Vector3 P(float s, float y = TubeCenterY) { Vector3 p = a + dir * s; return new Vector3(p.X, y, p.Z); }
		Vector3 side = new(-dir.Z, 0f, dir.X);
		bool alongX = Mathf.Abs(dir.X) > 0.5f;
		Vector3 Along(float l, float h, float w) => alongX ? new Vector3(l, h, w) : new Vector3(w, h, l);
		float mid = (s0 + s1) / 2f;

		// Structure (collides): opaque liner, ribs, bulkhead frames.
		var hull = new CsgCombiner3D { Name = $"{name}Structure", UseCollision = true, CollisionLayer = WallCollisionLayers, CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided };
		parent.AddChild(hull);
		hull.AddChild(OctRing("Liner", dir, P(mid), LinerInner, LinerOuter, len, _ikWallWhite));
		// Span metadata: ModelBaseBuilder lays corridor.glb along it (and hides this procedural liner).
		hull.SetMeta("tube_name", name);
		hull.SetMeta("span_a", P(s0, 0f));
		hull.SetMeta("span_b", P(s1, 0f));
		hull.SetMeta("model_corridor", IsModelCorridor(name));

		int ribs = Mathf.Max(1, Mathf.FloorToInt(len / RibSpacing));
		float step = len / (ribs + 1);
		for (int i = 1; i <= ribs; i++)
		{
			CsgPolygon3D rib = OctRing($"Rib{i}", dir, P(s0 + i * step), 1.36f, 1.6f, 0.18f, _ikMetalDark);
			CutBelow(rib, 0.42f);
			hull.AddChild(rib);
		}
		foreach (float s in fromJunction ? new[] { s1 - 0.16f } : new[] { s0 + 0.16f, s1 - 0.16f })
		{
			CsgPolygon3D frame = OctRing($"Bulkhead{s:0}", dir, P(s), 1.24f, 1.6f, 0.32f, _ikMetalGray);
			CutBelow(frame, 0.62f); // removes the whole bottom bar so the doorway floor is clear
			hull.AddChild(frame);
		}

		// Trim (visual only): accents, floor grate, glow strips, ceiling light, conduits.
		var trim = new CsgCombiner3D { Name = $"{name}Trim", UseCollision = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild(trim);
		for (int i = 1; i <= ribs; i += 2)
		{
			CsgPolygon3D accent = OctRing($"RibGlow{i}", dir, P(s0 + i * step), 1.335f, 1.37f, 0.05f, _ikGlowCyan);
			CutBelow(accent, 0.6f);
			trim.AddChild(accent);
		}
		foreach (float s in fromJunction ? new[] { s1 - 0.16f } : new[] { s0 + 0.16f, s1 - 0.16f })
		{
			CsgPolygon3D stripe = OctRing($"HazardRing{s:0}", dir, P(s), 1.21f, 1.25f, 0.3f, _ikHazard);
			CutBelow(stripe, 0.62f);
			trim.AddChild(stripe);
		}
		trim.AddChild(new CsgBox3D { Name = "FloorGrate", Size = Along(len, 0.02f, 0.9f), Position = P(mid, 0.275f), Material = _ikGrate });
		foreach (float o in new[] { -0.5f, 0.5f })
		{
			trim.AddChild(new CsgBox3D { Name = $"FloorGlow{o:+0;-0}", Size = Along(len - 0.7f, 0.015f, 0.05f), Position = P(mid, 0.272f) + side * o, Material = _ikGlowCyan });
			trim.AddChild(new CsgBox3D { Name = $"WallGlow{o:+0;-0}", Size = Along(len - 0.7f, 0.05f, 0.03f), Position = P(mid, 2.05f) + side * (o * 2.72f), Material = _ikGlowBlue });
			var conduit = new CsgCylinder3D
			{
				Name = $"Conduit{o:+0;-0}",
				Radius = 0.06f,
				Height = len,
				Sides = 8,
				Position = P(mid, 2.85f) + side * (o * 1.9f),
				RotationDegrees = alongX ? new Vector3(0, 0, 90) : new Vector3(90, 0, 0),
				Material = _ikMetalGray,
			};
			trim.AddChild(conduit);
		}
		trim.AddChild(new CsgBox3D { Name = "CeilingLight", Size = Along(len - 1f, 0.03f, 0.45f), Position = P(mid, 3.02f), Material = _ikGlowWhite });

		// Openings where a spur joins this corridor (e.g. the Rover Bay airlock spur).
		foreach (var other in _tubeSpans)
		{
			if (other.name == name || other.start > 0.1f)
				continue;
			Vector3 rel = other.a - a;
			float along = rel.Dot(dir);
			if (along < s0 || along > s1 || (rel - dir * along).Length() > 0.5f)
				continue;
			Vector3 cut = new Vector3(other.a.X, 1.55f, other.a.Z) + other.dir * 1.15f; // y 0.2..2.9, from 0.45 m off-axis: no rib stub or lip left in the walking path
			bool otherAlongX = Mathf.Abs(other.dir.X) > 0.5f;
			var size = otherAlongX ? new Vector3(1.4f, 2.7f, 2.9f) : new Vector3(2.9f, 2.7f, 1.4f);
			hull.AddChild(new CsgBox3D { Name = $"Opening_{other.name}", Operation = CsgShape3D.OperationEnum.Subtraction, Size = size, Position = cut });
			trim.AddChild(new CsgBox3D { Name = $"Opening_{other.name}", Operation = CsgShape3D.OperationEnum.Subtraction, Size = size, Position = cut });
		}

		// Reflection probe so the glossy liner reflects the corridor itself, not black space.
		_lights.AddChild(new ReflectionProbe
		{
			Name = $"{name}Probe",
			Position = P(mid),
			Size = Along(len + 1f, 3.4f, 3.4f),
			BoxProjection = true,
			Interior = true,
			UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
			AmbientMode = ReflectionProbe.AmbientModeEnum.Color,
			AmbientColor = new Color(0.1f, 0.12f, 0.15f),
			AmbientColorEnergy = 0.5f,
		});
	}

	// ----------------------------------------------------------------------
	//  Rooms
	// ----------------------------------------------------------------------

	private void DetailBoxRoom(Node3D parent, string name, Vector3 c, Vector2 size, float height, List<Vector3> doors)
	{
		var detail = new CsgCombiner3D { Name = $"{name}Detail", UseCollision = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
		parent.AddChild(detail);
		float f = BaseFloor;
		float panelH = Mathf.Min(3.1f, height - 1.2f);

		var walls = new[] { Vector3.Forward, Vector3.Back, Vector3.Left, Vector3.Right };
		foreach (Vector3 n in walls)
		{
			// n = outward wall normal; t = direction along the wall.
			bool xWall = Mathf.Abs(n.X) > 0.5f;
			float half = xWall ? size.X / 2f : size.Y / 2f;
			float wallLen = xWall ? size.Y : size.X;
			Vector3 t = xWall ? Vector3.Back : Vector3.Right;
			Vector3 face = c + n * half;
			bool doorWall = doors.Any(d => d.Dot(n) > 0.9f);
			float yaw = YawFacing(-n);

			int bays = Mathf.Max(1, Mathf.RoundToInt(wallLen / 2.4f));
			float bay = wallLen / bays;
			for (int k = 0; k <= bays; k++)
			{
				float s = -wallLen / 2f + k * bay;
				if (doorWall && Mathf.Abs(s) < 1.75f)
					continue;
				Vector3 colPos = face - n * 0.11f + t * Mathf.Clamp(s, -wallLen / 2f + 0.14f, wallLen / 2f - 0.14f);
				detail.AddChild(new CsgBox3D { Name = $"Column{n.X:0}{n.Z:0}_{k}", Size = new Vector3(0.28f, height, 0.22f), Position = new Vector3(colPos.X, f + height / 2f, colPos.Z), RotationDegrees = new Vector3(0, yaw, 0), Material = _ikMetalDark });
				if (k % 2 == 1)
					detail.AddChild(new CsgBox3D { Name = $"ColumnGlow{n.X:0}{n.Z:0}_{k}", Size = new Vector3(0.05f, 2.2f, 0.02f), Position = new Vector3(colPos.X, f + 1.9f, colPos.Z) - n * 0.115f, RotationDegrees = new Vector3(0, yaw, 0), Material = _ikGlowBlue });
			}
			for (int k = 0; k < bays; k++)
			{
				float s = -wallLen / 2f + (k + 0.5f) * bay;
				if (doorWall && Mathf.Abs(s) < 1.75f + bay / 2f)
					continue;
				Vector3 p = face - n * 0.03f + t * s;
				var rot = new Vector3(0, yaw, 0);
				detail.AddChild(new CsgBox3D { Name = $"Panel{n.X:0}{n.Z:0}_{k}", Size = new Vector3(bay - 0.34f, panelH, 0.05f), Position = new Vector3(p.X, f + 0.45f + panelH / 2f, p.Z), RotationDegrees = rot, Material = _ikWallWhite });
				detail.AddChild(new CsgBox3D { Name = $"Kick{n.X:0}{n.Z:0}_{k}", Size = new Vector3(bay - 0.34f, 0.4f, 0.07f), Position = new Vector3(p.X, f + 0.2f, p.Z), RotationDegrees = rot, Material = _ikMetalDark });
				detail.AddChild(new CsgBox3D { Name = $"Upper{n.X:0}{n.Z:0}_{k}", Size = new Vector3(bay - 0.34f, height - panelH - 0.55f, 0.04f), Position = new Vector3(p.X, f + 0.5f + panelH + (height - panelH - 0.55f) / 2f, p.Z), RotationDegrees = rot, Material = _ikMetalGray });
			}
			// continuous cyan accent band above the panels
			Vector3 band = face - n * 0.07f;
			float bandLen = doorWall ? wallLen / 2f - 1.75f : wallLen;
			foreach (float sgn in doorWall ? new[] { -1f, 1f } : new[] { 0f })
			{
				Vector3 bp = band + t * (sgn * (1.75f + bandLen / 2f));
				detail.AddChild(new CsgBox3D { Name = $"Accent{n.X:0}{n.Z:0}{sgn:0}", Size = new Vector3(bandLen, 0.05f, 0.03f), Position = new Vector3(bp.X, f + 0.5f + panelH + 0.06f, bp.Z), RotationDegrees = new Vector3(0, yaw, 0), Material = _ikGlowCyan });
			}
		}

		// Ceiling beams across the short axis + floor grate + hazard thresholds at the doors.
		bool beamsAlongX = size.X <= size.Y;
		float span = beamsAlongX ? size.Y : size.X;
		for (float s = -span / 2f + 1.2f; s < span / 2f - 0.6f; s += 2.4f)
		{
			Vector3 p = c + (beamsAlongX ? new Vector3(0, 0, s) : new Vector3(s, 0, 0));
			detail.AddChild(new CsgBox3D { Name = $"Beam{s:0}", Size = beamsAlongX ? new Vector3(size.X, 0.3f, 0.24f) : new Vector3(0.24f, 0.3f, size.Y), Position = new Vector3(p.X, f + height - 0.15f, p.Z), Material = _ikMetalDark });
		}
		detail.AddChild(new CsgBox3D { Name = "FloorGrate", Size = new Vector3(size.X * 0.5f, 0.02f, size.Y * 0.32f), Position = new Vector3(c.X, f + 0.01f, c.Z), Material = _ikGrate });
		foreach (Vector3 d in doors)
		{
			float half = Mathf.Abs(d.X) > 0.5f ? size.X / 2f : size.Y / 2f;
			Vector3 p = c + d * (half - 0.4f);
			bool alongX = Mathf.Abs(d.X) > 0.5f;
			detail.AddChild(new CsgBox3D { Name = $"Threshold{d.X:0}{d.Z:0}", Size = alongX ? new Vector3(0.6f, 0.012f, 2.6f) : new Vector3(2.6f, 0.012f, 0.6f), Position = new Vector3(p.X, f + 0.006f, p.Z), Material = _ikHazard });
		}

		AddRoomLights(name, new Vector3(c.X, f, c.Z), height, Mathf.Max(size.X, size.Y));
		AddProbe(name, new Vector3(c.X, f + height / 2f, c.Z), new Vector3(size.X, height, size.Y));
	}

	private void DetailOctRoom(Node3D parent, string name, Vector3 c, float innerR, float height, List<Vector3> doors, float[] skipFacets)
	{
		var detail = new CsgCombiner3D { Name = $"{name}Detail", UseCollision = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
		parent.AddChild(detail);
		float f = BaseFloor;
		float ap = innerR * Mathf.Cos(Mathf.Pi / 8f);
		float facet = 2f * innerR * Mathf.Sin(Mathf.Pi / 8f);
		float panelH = name == "Lobby" ? 4.0f : Mathf.Min(3.1f, height - 1.2f);

		for (int i = 0; i < 8; i++)
		{
			float ang = i * 45f;
			Vector3 n = FacetDir(ang);
			bool isDoor = doors.Any(d => d.Dot(n) > 0.95f);
			bool skip = isDoor || skipFacets.Any(s => Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(s), Mathf.DegToRad(ang))) < 0.01f);

			// Columns at the facet corners (always, also beside doors and windows).
			Vector3 v = FacetDir(ang + 22.5f) * (innerR - 0.16f);
			detail.AddChild(new CsgBox3D { Name = $"Column{i}", Size = new Vector3(0.34f, height, 0.3f), Position = c + new Vector3(v.X, f + height / 2f, v.Z), RotationDegrees = new Vector3(0, ang + 22.5f, 0), Material = _ikMetalDark });
			detail.AddChild(new CsgBox3D { Name = $"ColumnGlow{i}", Size = new Vector3(0.06f, 2.6f, 0.02f), Position = c + new Vector3(v.X, f + 2.1f, v.Z) - FacetDir(ang + 22.5f) * 0.16f, RotationDegrees = new Vector3(0, ang + 22.5f, 0), Material = i % 2 == 0 ? _ikGlowCyan : _ikGlowBlue });
			// Radial ceiling beam toward the corner.
			Vector3 beam = FacetDir(ang + 22.5f) * (innerR / 2f);
			detail.AddChild(new CsgBox3D { Name = $"Beam{i}", Size = new Vector3(0.26f, 0.32f, innerR), Position = c + new Vector3(beam.X, f + height - 0.16f, beam.Z), RotationDegrees = new Vector3(0, ang + 22.5f, 0), Material = _ikMetalDark });

			if (skip)
				continue;
			Vector3 p = n * (ap - 0.03f);
			var rot = new Vector3(0, ang, 0);
			detail.AddChild(new CsgBox3D { Name = $"Panel{i}", Size = new Vector3(facet - 0.5f, panelH, 0.05f), Position = c + new Vector3(p.X, f + 0.45f + panelH / 2f, p.Z), RotationDegrees = rot, Material = _ikWallWhite });
			detail.AddChild(new CsgBox3D { Name = $"Kick{i}", Size = new Vector3(facet - 0.5f, 0.4f, 0.07f), Position = c + new Vector3(p.X, f + 0.2f, p.Z), RotationDegrees = rot, Material = _ikMetalDark });
			detail.AddChild(new CsgBox3D { Name = $"Upper{i}", Size = new Vector3(facet - 0.5f, height - panelH - 0.55f, 0.04f), Position = c + new Vector3(p.X, f + 0.5f + panelH + (height - panelH - 0.55f) / 2f, p.Z), RotationDegrees = rot, Material = _ikMetalGray });
			detail.AddChild(new CsgBox3D { Name = $"Accent{i}", Size = new Vector3(facet - 0.5f, 0.05f, 0.03f), Position = c + new Vector3(p.X, f + 0.5f + panelH + 0.06f, p.Z) - n * 0.04f, RotationDegrees = rot, Material = _ikGlowCyan });
		}

		// Ceiling halo ring, floor grate around the centre, hazard thresholds at the doors.
		detail.AddChild(new CsgTorus3D { Name = "CeilingHalo", InnerRadius = innerR * 0.42f, OuterRadius = innerR * 0.42f + 0.12f, Sides = 32, RingSides = 4, Position = c + new Vector3(0, f + height - 0.35f, 0), Material = _ikGlowCyan });
		detail.AddChild(new CsgCylinder3D { Name = "FloorGrate", Radius = name == "Lobby" ? 2.7f : innerR * 0.55f, Height = 0.02f, Sides = 8, RotationDegrees = new Vector3(0, 22.5f, 0), Position = c + new Vector3(0, f + 0.01f, 0), Material = _ikGrate });
		foreach (Vector3 d in doors)
		{
			Vector3 p = c + d * (ap - 0.4f);
			bool alongX = Mathf.Abs(d.X) > 0.5f;
			detail.AddChild(new CsgBox3D { Name = $"Threshold{d.X:0}{d.Z:0}", Size = alongX ? new Vector3(0.6f, 0.012f, 2.6f) : new Vector3(2.6f, 0.012f, 0.6f), Position = new Vector3(p.X, f + 0.006f, p.Z), Material = _ikHazard });
		}

		if (name == "Lobby")
			LightLobby(c, height);
		else
			AddRoomLights(name, c + new Vector3(0, f, 0), height, innerR * 2f);
		AddProbe(name, c + new Vector3(0, f + height / 2f, 0), new Vector3(innerR * 2f, height, innerR * 2f));
	}

	private void DetailTelescope(Node3D parent, Vector3 c, float innerR)
	{
		var detail = new CsgCombiner3D { Name = "TelescopeDetail", UseCollision = false };
		parent.AddChild(detail);
		float f = BaseFloor;
		for (int i = 0; i < 12; i++)
		{
			float ang = i * 30f + 15f;
			if (Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(ang), Mathf.Pi)) < 0.3f)
				continue; // doorway (north)
			Vector3 v = FacetDir(ang) * (innerR - 0.15f);
			detail.AddChild(new CsgBox3D { Name = $"Column{i}", Size = new Vector3(0.3f, RoomHeight, 0.26f), Position = c + new Vector3(v.X, f + RoomHeight / 2f, v.Z), RotationDegrees = new Vector3(0, ang, 0), Material = _ikMetalDark });
		}
		detail.AddChild(new CsgTorus3D { Name = "AccentRing", InnerRadius = innerR - 0.1f, OuterRadius = innerR - 0.04f, Sides = 48, RingSides = 4, Position = c + new Vector3(0, f + 3.6f, 0), Material = _ikGlowBlue });
		detail.AddChild(new CsgCylinder3D { Name = "FloorGrate", Radius = 3.2f, Height = 0.02f, Sides = 24, Position = c + new Vector3(0, f + 0.01f, 0), Material = _ikGrate });
		AddRoomLights("Telescope", c + new Vector3(0, f, 0), RoomHeight, innerR * 2f);
		AddProbe("Telescope", c + new Vector3(0, f + RoomHeight / 2f, 0), new Vector3(innerR * 2f, RoomHeight + 3f, innerR * 2f));
	}

	// ----------------------------------------------------------------------
	//  Curved command console + holographic projector (Lobby)
	// ----------------------------------------------------------------------

	private void BuildCommandConsole(Node3D cc, string[][] texts, Color[] palette)
	{
		float f = BaseFloor;
		var center = new Vector3(0f, f, 0f);
		const float rIn = 3.4f;           // operator-side edge
		const float sweep = 124f;         // degrees of arc, centred on north (facing the monitor bank)
		const int segments = 7;

		var console = new Node3D { Name = "CommandConsole", Position = center };
		cc.AddChild(console);

		// Yaw that centres a Spin sweep on north: the sweep's middle direction must map to -Z.
		float midLocal = Mathf.Atan2(SpinSweepSign * Mathf.Sin(Mathf.DegToRad(sweep / 2f)), Mathf.Cos(Mathf.DegToRad(sweep / 2f)));
		// local mid direction = (cos m, 0, sign*sin m) -> its angle from +Z toward +X:
		Vector3 localMid = new(Mathf.Cos(Mathf.DegToRad(sweep / 2f)), 0f, SpinSweepSign * Mathf.Sin(Mathf.DegToRad(sweep / 2f)));
		float spinYaw = Mathf.Pi - Mathf.Atan2(localMid.X, localMid.Z); // north = angle PI
		_ = midLocal;

		CsgPolygon3D Arc(string name, Vector2[] profile, Material mat, bool collide = false) => new()
		{
			Name = name,
			Mode = CsgPolygon3D.ModeEnum.Spin,
			SpinDegrees = sweep,
			SpinSides = 28,
			Polygon = profile,
			Rotation = new Vector3(0f, spinYaw, 0f),
			Material = mat,
			UseCollision = collide,
		};

		// Body: writing lip at 0.8 m, slanted control face up to 1.2 m, top deck, vertical back.
		console.AddChild(Arc("Body", new[]
		{
			new Vector2(rIn, 0f), new Vector2(rIn, 0.8f), new Vector2(rIn + 0.35f, 0.8f),
			new Vector2(rIn + 0.65f, 1.2f), new Vector2(rIn + 1.0f, 1.2f), new Vector2(rIn + 1.0f, 0f),
		}, _ikMetalGray, collide: true));
		console.AddChild(Arc("Kickplate", new[] { new Vector2(rIn - 0.02f, 0.05f), new Vector2(rIn + 0.05f, 0.05f), new Vector2(rIn + 0.05f, 0.65f), new Vector2(rIn - 0.02f, 0.65f) }, _ikMetalDark));
		console.AddChild(Arc("LipGlow", new[] { new Vector2(rIn - 0.015f, 0.78f), new Vector2(rIn + 0.01f, 0.78f), new Vector2(rIn + 0.01f, 0.81f), new Vector2(rIn - 0.015f, 0.81f) }, _ikGlowCyan));
		console.AddChild(Arc("FloorGlow", new[] { new Vector2(rIn - 0.05f, 0f), new Vector2(rIn - 0.02f, 0f), new Vector2(rIn - 0.02f, 0.04f), new Vector2(rIn - 0.05f, 0.04f) }, _ikGlowBlue));
		console.AddChild(Arc("BackGlow", new[] { new Vector2(rIn + 0.99f, 1.18f), new Vector2(rIn + 1.02f, 1.18f), new Vector2(rIn + 1.02f, 1.21f), new Vector2(rIn + 0.99f, 1.21f) }, _ikGlowCyan));

		// Small hardware for all segments goes into one combiner (one mesh, few draw calls).
		var hardware = new CsgCombiner3D { Name = "Hardware", UseCollision = false };
		console.AddChild(hardware);
		var dark = _ikMetalDark;
		var leds = new[] { _ikGlowCyan, _ikGlowBlue, Emissive(new Color(0.3f, 1f, 0.4f), 3f), Emissive(new Color(1f, 0.65f, 0.15f), 3f), Emissive(new Color(1f, 0.25f, 0.2f), 3f) };

		for (int i = 0; i < segments; i++)
		{
			// Segment angle measured from +Z toward +X; north = 180.
			float theta = 180f - sweep / 2f + sweep * (i + 0.5f) / segments;
			// Segment frame: local +X = outward (operator -> back of console), local Z = along the arc.
			var seg = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(theta - 90f)), Vector3.Zero);
			float chord = 2f * rIn * Mathf.Sin(Mathf.DegToRad(sweep / segments / 2f));
			string[] t = texts[i % texts.Length];
			Color col = palette[i % palette.Length];

			// Screen in the slanted control face (+Z of the screen = panel normal (-0.8, 0.6)).
			var slanted = new Basis(new Vector3(0, 0, 1), new Vector3(0.6f, 0.8f, 0), new Vector3(-0.8f, 0.6f, 0));
			var segNode = new Node3D { Name = $"Segment{i}", Transform = seg };
			console.AddChild(segNode);
			Screen(segNode, "PanelScreen", new Transform3D(slanted, new Vector3(rIn + 0.5f - 0.024f, 1.0f + 0.018f, 0f)), new Vector2(chord * 0.78f, 0.34f), t[i % 3], col);

			// Angled monitor on the top deck, tilted 14 degrees back, facing the operator.
			Vector3 normal = new Vector3(-Mathf.Cos(Mathf.DegToRad(14f)), Mathf.Sin(Mathf.DegToRad(14f)), 0f);
			var monBasis = Basis.LookingAt(-normal, Vector3.Up);
			var monPos = new Vector3(rIn + 0.85f, 1.62f, 0f);
			Screen(segNode, "Monitor", new Transform3D(monBasis, monPos), new Vector2(chord * 0.82f, 0.48f), t[(i + 1) % 3], i == segments / 2 ? PhosphorCyan : col);
			AddPart(hardware, seg, new CsgBox3D { Name = $"MonHousing{i}", Size = new Vector3(chord * 0.82f + 0.1f, 0.58f, 0.07f), Material = dark }, new Transform3D(monBasis, monPos - normal * 0.05f));
			AddPart(hardware, seg, new CsgBox3D { Name = $"MonStand{i}", Size = new Vector3(0.08f, 0.3f, 0.08f), Material = dark }, new Transform3D(Basis.Identity, new Vector3(rIn + 0.9f, 1.32f, 0f)));

			// Keyboard on the writing lip (key rows), toggle switches + LEDs, button grid.
			AddPart(hardware, seg, new CsgBox3D { Name = $"Keyboard{i}", Size = new Vector3(0.16f, 0.025f, 0.5f), Material = dark }, Local(rIn + 0.12f, 0.815f, 0f));
			for (int r = 0; r < 3; r++)
				AddPart(hardware, seg, new CsgBox3D { Name = $"Keys{i}_{r}", Size = new Vector3(0.03f, 0.008f, 0.45f), Material = _ikMetalGray }, Local(rIn + 0.07f + r * 0.05f, 0.83f, 0f));
			for (int k = 0; k < 4; k++)
			{
				float z = 0.3f + k * 0.065f;
				AddPart(hardware, seg, new CsgBox3D { Name = $"ToggleBase{i}_{k}", Size = new Vector3(0.05f, 0.02f, 0.04f), Material = dark }, Local(rIn + 0.27f, 0.81f, z));
				AddPart(hardware, seg, new CsgCylinder3D { Name = $"Toggle{i}_{k}", Radius = 0.008f, Height = 0.07f, Sides = 6, Material = _ikMetalGray },
					new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad(k % 2 == 0 ? 25f : -25f)), new Vector3(rIn + 0.27f, 0.85f, z)));
				AddPart(hardware, seg, new CsgBox3D { Name = $"ToggleLed{i}_{k}", Size = new Vector3(0.02f, 0.012f, 0.02f), Material = leds[(i + k) % leds.Length] }, Local(rIn + 0.32f, 0.81f, z));
			}
			for (int r = 0; r < 2; r++)
			for (int k = 0; k < 3; k++)
				AddPart(hardware, seg, new CsgBox3D { Name = $"Button{i}_{r}_{k}", Size = new Vector3(0.045f, 0.015f, 0.045f), Material = leds[(i + r * 3 + k) % leds.Length] }, Local(rIn + 0.18f + r * 0.08f, 0.81f, -0.33f - k * 0.065f));
		}

		BuildHoloProjector(cc, center);
	}

	private static Transform3D Local(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));

	/// <summary>Adds a CSG part to a combiner at segmentFrame * local.</summary>
	private static void AddPart(CsgCombiner3D combiner, Transform3D segment, CsgShape3D part, Transform3D local)
	{
		part.Transform = segment * local;
		combiner.AddChild(part);
	}

	private void BuildHoloProjector(Node3D cc, Vector3 center)
	{
		var holo = new Node3D { Name = "HoloProjector", Position = center };
		cc.AddChild(holo);
		Cyl(holo, "Base", 1.1f, 0.22f, new Vector3(0, 0.11f, 0), Vector3.Zero, _ikMetalDark, sides: 32, collision: true);
		Cyl(holo, "Step", 1.25f, 0.06f, new Vector3(0, 0.03f, 0), Vector3.Zero, _ikMetalGray, sides: 32);
		holo.AddChild(new CsgTorus3D { Name = "EmitterRing", InnerRadius = 0.86f, OuterRadius = 0.98f, Sides = 40, RingSides = 6, Position = new Vector3(0, 0.23f, 0), Material = _ikGlowCyan });
		Cyl(holo, "EmitterDisc", 0.7f, 0.02f, new Vector3(0, 0.225f, 0), Vector3.Zero, _ikGlowBlue, sides: 32);
		for (int i = 0; i < 8; i++)
		{
			float a = i * Mathf.Tau / 8f;
			Box(holo, $"Vent{i}", new Vector3(0.18f, 0.05f, 0.04f), new Vector3(Mathf.Sin(a) * 1.11f, 0.12f, Mathf.Cos(a) * 1.11f), _ikGlowCyan, rotationDegrees: new Vector3(0, Mathf.RadToDeg(a), 0));
		}

		// Light cone (tip at the emitter) and a slowly spinning Moon hologram.
		var cone = Cyl(holo, "LightCone", 0.95f, 1.6f, new Vector3(0, 0.24f + 0.8f, 0), new Vector3(180, 0, 0), _ikHolo, cone: true, sides: 32);
		cone.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		var spinner = new Node3D { Name = "Hologram", Position = new Vector3(0, 1.55f, 0) };
		holo.AddChild(spinner);
		spinner.AddChild(new CsgSphere3D { Name = "Moon", Radius = 0.42f, RadialSegments = 20, Rings = 10, Material = _ikHolo, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		spinner.AddChild(new CsgTorus3D { Name = "Orbit", InnerRadius = 0.68f, OuterRadius = 0.71f, Sides = 40, RingSides = 4, RotationDegrees = new Vector3(18, 0, 0), Material = _ikGlowCyan, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		spinner.AddChild(new CsgSphere3D { Name = "Satellite", Radius = 0.05f, Position = new Vector3(0.7f, 0.2f, 0f), Material = _ikGlowWhite, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		QueueScript(spinner, "res://Scripts/HoloSpin.cs", new Dictionary<string, Variant> { ["DegreesPerSecond"] = 18f });
		Label(holo, "HoloLabel", "LUNAR OUTPOST\nSURFACE OPS", new Vector3(0, 2.35f, 0), Vector3.Zero, 0.006f, 64, new Color(0.45f, 0.9f, 1f), billboard: true, outline: 0);
		Omni(_lights, "HoloGlow", center + new Vector3(0, 1.3f, 0), new Color(0.3f, 0.85f, 1f), 1.2f, 5.5f);
	}

	// ----------------------------------------------------------------------
	//  Lighting
	// ----------------------------------------------------------------------

	private void LightLobby(Vector3 c, float height)
	{
		float ceiling = BaseFloor + height;
		// Four shadowed key lights converging on the console ring = soft cinematic pools.
		for (int i = 0; i < 4; i++)
		{
			Vector3 p = FacetDir(45f + i * 90f) * 5.2f + new Vector3(0, ceiling - 0.4f, 0);
			var spot = new SpotLight3D
			{
				Name = $"LobbyKey{i}",
				LightColor = i % 2 == 0 ? new Color(0.92f, 0.97f, 1f) : new Color(0.7f, 0.92f, 1f),
				LightEnergy = 3.2f,
				SpotRange = 11f,
				SpotAngle = 42f,
				SpotAngleAttenuation = 0.8f,
				ShadowEnabled = true,
				ShadowBlur = 2.5f,
				Position = c + p,
			};
			_lights.AddChild(spot);
			spot.LookAtFromPosition(c + p, c + new Vector3(p.X * 0.35f, BaseFloor, p.Z * 0.35f), Vector3.Up);
		}
		// Cyan wash from the ceiling halo and the monitor wall.
		Omni(_lights, "LobbyHaloWash", c + new Vector3(0, ceiling - 0.6f, 0), new Color(0.45f, 0.85f, 1f), 0.9f, 12f);
	}

	private void AddRoomLights(string name, Vector3 floorCenter, float height, float extent)
	{
		var spot = new SpotLight3D
		{
			Name = $"{name}Key",
			LightColor = name == "Shield" ? new Color(1f, 0.85f, 0.8f) : new Color(0.93f, 0.97f, 1f),
			LightEnergy = 2.6f,
			SpotRange = height + 4f,
			SpotAngle = 58f,
			SpotAngleAttenuation = 0.9f,
			ShadowEnabled = true,
			ShadowBlur = 2f,
			Position = floorCenter + new Vector3(0, height - 0.35f, 0),
			RotationDegrees = new Vector3(-90, 0, 0),
		};
		_lights.AddChild(spot);
		Omni(_lights, $"{name}Fill", floorCenter + new Vector3(0, height * 0.55f, 0), new Color(0.55f, 0.8f, 1f), 0.5f, extent * 0.8f);
	}

	private void AddProbe(string name, Vector3 center, Vector3 size)
	{
		_lights.AddChild(new ReflectionProbe
		{
			Name = $"{name}Probe",
			Position = center,
			Size = size + Vector3.One * 0.5f,
			BoxProjection = true,
			Interior = true,
			UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
			AmbientMode = ReflectionProbe.AmbientModeEnum.Color,
			AmbientColor = new Color(0.1f, 0.12f, 0.15f),
			AmbientColorEnergy = 0.5f,
		});
	}

	/// <summary>Re-tunes lights created by earlier passes (grow lights, tube lamps, old lobby lights).</summary>
	private void LightInterior()
	{
		void Tune(string name, Color color, float energy, float? range = null)
		{
			if (_lights.GetNodeOrNull<OmniLight3D>(name) is not { } l)
				return;
			l.LightColor = color;
			l.LightEnergy = energy;
			if (range.HasValue)
				l.OmniRange = range.Value;
		}
		// Bio-Dome: soft, faint green/white grow lights.
		Tune("GrowLightA", new Color(0.78f, 1f, 0.8f), 0.8f, 9f);
		Tune("GrowLightB", new Color(0.78f, 1f, 0.8f), 0.8f, 9f);
		Tune("PlantLight", new Color(0.92f, 1f, 0.94f), 0.6f, 12f);
		// Lobby: bright white / cyan.
		Tune("LobbyCenter", new Color(0.9f, 0.97f, 1f), 1.2f, 14f);
		for (int i = 0; i < 4; i++)
			Tune($"LobbyRing{i}", new Color(0.6f, 0.9f, 1f), 0.7f, 9f);
		// Corridor lamps: cool white.
		foreach (Node n in _lights.GetChildren())
			if (n is OmniLight3D o && n.Name.ToString().Contains("Lamp"))
			{
				o.LightColor = new Color(0.82f, 0.93f, 1f);
				o.LightEnergy = 0.9f;
			}
	}
}

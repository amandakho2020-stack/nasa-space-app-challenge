using Godot;

// Lunar surface: regolith material, carved craters, distant hills, MultiMesh rock scattering.
public partial class EnvironmentBuilder
{
	private NoiseTexture2D _regolithNormal; // shared with the rock material

	private void BuildSurface(Node root)
	{
		RemoveIfExists(root, "LunarSurface");
		var surface = new Node3D { Name = "LunarSurface" };
		root.AddChild(surface);
		root.MoveChild(surface, Mathf.Min(3, root.GetChildCount() - 1));

		// Massive ground slab: top face at y = 0. Craters are carved into it with CSG subtraction.
		var ground = new CsgBox3D
		{
			Name = "Regolith",
			Size = new Vector3(GroundSize, 2f, GroundSize),
			Position = new Vector3(0, -1f, 0),
			UseCollision = true,
			Material = _regolith,
		};
		surface.AddChild(ground);
		CarveCraters(ground);

		BuildHills(surface);
		ScatterRocks(surface);

		SetOwnerRecursive(surface, root);
	}

	// ======================================================================
	//  Regolith materials
	// ======================================================================

	/// <summary>
	/// StandardMaterial3D regolith: FastNoiseLite albedo (gray color ramp), fractal FBM normal map,
	/// detail layer = fine grain (multiply) + cellular micro-crater normals. World triplanar mapping,
	/// so the 600 m CSG box and the crater bowls need no UVs.
	/// </summary>
	private Material BuildRegolithStandardMaterial()
	{
		var ramp = new Gradient
		{
			Offsets = new[] { 0f, 0.5f, 1f },
			Colors = new[] { RegolithBase.Darkened(0.32f), RegolithBase, RegolithBase.Lightened(0.22f) },
		};
		NoiseTexture2D albedo = Noise(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.006f, 5, 1024, 0);
		albedo.ColorRamp = ramp;

		_regolithNormal = Noise(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.02f, 7, 1024, 2, asNormalMap: true, bump: 10f);

		var grainRamp = new Gradient
		{
			Offsets = new[] { 0f, 1f },
			Colors = new[] { new Color(0.72f, 0.72f, 0.72f), Colors.White },
		};
		NoiseTexture2D grain = Noise(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.09f, 3, 512, 1);
		grain.ColorRamp = grainRamp;

		NoiseTexture2D craters = CellularNoise(1024, 3, asNormalMap: true, invert: false, bump: 3.5f, frequency: 0.012f);

		var mat = new StandardMaterial3D
		{
			AlbedoColor = Colors.White,
			AlbedoTexture = albedo,
			NormalEnabled = true,
			NormalTexture = _regolithNormal,
			NormalScale = 1.0f,
			Roughness = 1.0f,          // moon dust is not shiny
			Metallic = 0f,
			MetallicSpecular = 0.0f,   // "Specular" in the Inspector
			DetailEnabled = true,
			DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul,
			DetailUVLayer = BaseMaterial3D.DetailUV.UV1,
			DetailAlbedo = grain,
			DetailNormal = craters,
			Uv1Triplanar = true,
			Uv1WorldTriplanar = true,
			Uv1Scale = new Vector3(0.03f, 0.03f, 0.03f), // one texture repeat every ~33 m
			Uv1TriplanarSharpness = 4f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};
		return SaveResource(mat, "regolith.tres");
	}

	/// <summary>Alternative: the custom multi-scale shader from Shaders/regolith.gdshader.</summary>
	private Material BuildRegolithShaderMaterial()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>(RegolithShaderPath) };
		mat.SetShaderParameter("macro_noise", Noise(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.008f, 4, 512, 0));
		mat.SetShaderParameter("grain_noise", Noise(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.12f, 3, 256, 1));
		_regolithNormal = Noise(FastNoiseLite.NoiseTypeEnum.SimplexSmooth, 0.03f, 6, 512, 2, asNormalMap: true, bump: 6f);
		mat.SetShaderParameter("rock_normal", _regolithNormal);
		mat.SetShaderParameter("crater_normal", CellularNoise(512, 3, asNormalMap: true, invert: false, bump: 6f));
		mat.SetShaderParameter("crater_mask", CellularNoise(512, 3, asNormalMap: false, invert: true, bump: 0f));
		mat.SetShaderParameter("base_color", RegolithBase);
		mat.SetShaderParameter("roughness_value", 1.0f);
		mat.SetShaderParameter("specular_value", 0.0f);
		return SaveResource(mat, "regolith.tres");
	}

	private NoiseTexture2D Noise(FastNoiseLite.NoiseTypeEnum type, float frequency, int octaves, int size, int seedOffset,
		bool asNormalMap = false, float bump = 8f)
	{
		var noise = new FastNoiseLite
		{
			NoiseType = type,
			Frequency = frequency,
			Seed = NoiseSeed + seedOffset,
			FractalType = octaves > 1 ? FastNoiseLite.FractalTypeEnum.Fbm : FastNoiseLite.FractalTypeEnum.None,
			FractalOctaves = octaves,
			FractalLacunarity = 2f,
			FractalGain = 0.5f,
		};
		return new NoiseTexture2D
		{
			Width = size,
			Height = size,
			Seamless = true,
			AsNormalMap = asNormalMap,
			BumpStrength = bump,
			GenerateMipmaps = true,
			Noise = noise,
		};
	}

	private NoiseTexture2D CellularNoise(int size, int seedOffset, bool asNormalMap, bool invert, float bump, float frequency = 0.02f)
	{
		var noise = new FastNoiseLite
		{
			NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
			Frequency = frequency,
			Seed = NoiseSeed + seedOffset,
			FractalType = FastNoiseLite.FractalTypeEnum.None,
			CellularDistanceFunction = FastNoiseLite.CellularDistanceFunctionEnum.EuclideanSquared, // rounded bowls
			CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance,
			CellularJitter = 0.9f,
		};
		return new NoiseTexture2D
		{
			Width = size,
			Height = size,
			Seamless = true,
			AsNormalMap = asNormalMap,
			BumpStrength = bump,
			Invert = invert,
			GenerateMipmaps = true,
			Noise = noise,
		};
	}

	// ======================================================================
	//  Craters (CSG subtraction into the ground) and distant hills
	// ======================================================================

	private void CarveCraters(CsgBox3D ground)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(NoiseSeed + 101) };
		var bowls = new System.Collections.Generic.List<(Vector3 pos, float radius)>();

		int attempts = 0;
		while (bowls.Count < CraterCount && attempts++ < CraterCount * 40)
		{
			float radius = Mathf.Lerp(4f, 14f, Mathf.Pow(rng.Randf(), 1.6f));
			float angle = rng.Randf() * Mathf.Tau;
			float dist = rng.RandfRange(40f, ScatterRadius + 30f);
			var pos = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
			if (InClearZone(new Vector2(pos.X, pos.Z), radius * 1.4f))
				continue;
			bool overlaps = false;
			foreach (var (p, r) in bowls)
				overlaps |= p.DistanceTo(pos) < (r + radius) * 1.3f;
			if (!overlaps)
				bowls.Add((pos, radius));
		}

		// Ground slab is centred at y = -1, so convert world positions to its local space.
		Vector3 toLocal = new(0, 1f, 0);

		// Subtractions first, then rims (union), so rims are not cut away by neighbouring bowls.
		int i = 0;
		foreach (var (pos, radius) in bowls)
		{
			float depth = radius * 0.28f;
			ground.AddChild(new CsgSphere3D
			{
				Name = $"CraterBowl{i++}",
				Operation = CsgShape3D.OperationEnum.Subtraction,
				Radius = radius,
				RadialSegments = 28,
				Rings = 14,
				Position = pos + toLocal,
				Scale = new Vector3(1f, depth / radius, 1f),
				Material = _regolith,
			});
		}
		i = 0;
		foreach (var (pos, radius) in bowls)
		{
			ground.AddChild(new CsgTorus3D
			{
				Name = $"CraterRim{i++}",
				InnerRadius = radius * 0.92f,
				OuterRadius = radius * 1.35f,
				Sides = 32,
				RingSides = 8,
				Position = pos + toLocal,
				Scale = new Vector3(1f, 0.55f, 1f),
				Material = _regolith,
			});
		}
	}

	private void BuildHills(Node3D surface)
	{
		var hills = new Node3D { Name = "Hills" };
		surface.AddChild(hills);
		var rng = new RandomNumberGenerator { Seed = (ulong)(NoiseSeed + 202) };
		var mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 };

		for (int i = 0; i < HillCount; i++)
		{
			float angle = (i + rng.Randf() * 0.7f) / HillCount * Mathf.Tau;
			float dist = rng.RandfRange(245f, 295f);
			var scale = new Vector3(rng.RandfRange(25f, 60f), rng.RandfRange(7f, 20f), rng.RandfRange(25f, 60f));
			// Push hills outward if they would swallow the Drill Site, its road, or any base footprint.
			for (int tries = 0; tries < 6; tries++)
			{
				var p2 = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
				if (!InClearZone(p2, Mathf.Max(scale.X, scale.Z)))
					break;
				dist += 25f;
			}
			hills.AddChild(new MeshInstance3D
			{
				Name = $"Hill{i}",
				Mesh = mesh,
				MaterialOverride = _regolith,
				Position = new Vector3(Mathf.Cos(angle) * dist, -scale.Y * 0.3f, Mathf.Sin(angle) * dist),
				Scale = scale,
				RotationDegrees = new Vector3(0, rng.RandfRange(0, 360), 0),
			});
		}
	}

	// ======================================================================
	//  Rock scattering (MultiMeshInstance3D)
	// ======================================================================

	private void ScatterRocks(Node3D surface)
	{
		var rocks = new Node3D { Name = "Rocks" };
		surface.AddChild(rocks);

		var rockMat = new StandardMaterial3D
		{
			AlbedoColor = RegolithBase.Lightened(0.05f),
			VertexColorUseAsAlbedo = true,  // per-instance tint from MultiMesh colors
			Roughness = 1f,
			MetallicSpecular = 0f,
			NormalEnabled = true,
			NormalTexture = _regolithNormal,
			NormalScale = 1.5f,
			Uv1Triplanar = true,            // object-space triplanar: detail rotates with each rock
			Uv1Scale = new Vector3(0.6f, 0.6f, 0.6f),
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
		};
		SaveResource(rockMat, "rock.tres");

		var meshes = new ArrayMesh[3];
		for (int v = 0; v < meshes.Length; v++)
			meshes[v] = SaveResource(BuildRockMesh(NoiseSeed + 300 + v), $"rock_mesh_{v}.res");

		var rng = new RandomNumberGenerator { Seed = (ulong)(NoiseSeed + 303) };
		var cluster = new FastNoiseLite { Seed = NoiseSeed + 304, Frequency = 0.02f, FractalOctaves = 2 };

		ScatterLayer(rocks, "SmallRocks", meshes, rockMat, SmallRockCount, 0.12f, 0.6f, rng, cluster, colliders: false);
		ScatterLayer(rocks, "MediumRocks", meshes, rockMat, MediumRockCount, 0.6f, 1.4f, rng, cluster, colliders: false);
		ScatterLayer(rocks, "Boulders", meshes, rockMat, BoulderCount, 1.6f, 4.0f, rng, cluster, colliders: true);
	}

	/// <summary>One MultiMeshInstance3D per rock shape variant; optional sphere colliders for big rocks.</summary>
	private void ScatterLayer(Node3D parent, string name, ArrayMesh[] meshes, Material material, int count,
		float minSize, float maxSize, RandomNumberGenerator rng, FastNoiseLite cluster, bool colliders)
	{
		var layer = new Node3D { Name = name };
		parent.AddChild(layer);

		var perVariant = new System.Collections.Generic.List<(Transform3D xf, Color tint)>[meshes.Length];
		for (int v = 0; v < meshes.Length; v++)
			perVariant[v] = new();

		StaticBody3D bodies = null;
		if (colliders)
		{
			bodies = new StaticBody3D { Name = "Colliders" };
			layer.AddChild(bodies);
		}

		int placed = 0, attempts = 0;
		while (placed < count && attempts++ < count * 30)
		{
			// Uniform in a disc, then thinned by a noise field so rocks gather in fields and streaks.
			float r = Mathf.Sqrt(rng.Randf()) * ScatterRadius;
			float a = rng.Randf() * Mathf.Tau;
			var p = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
			float density = cluster.GetNoise2D(p.X, p.Y) * 0.5f + 0.5f;
			if (rng.Randf() > Mathf.Pow(density, 1.5f) * 1.3f)
				continue;

			// Size distribution skewed toward the small end.
			float size = Mathf.Lerp(minSize, maxSize, Mathf.Pow(rng.Randf(), 2.2f));
			if (InClearZone(p, size))
				continue;

			var scale = new Vector3(rng.RandfRange(0.8f, 1.3f), rng.RandfRange(0.45f, 0.85f), rng.RandfRange(0.8f, 1.3f)) * size;
			var rot = new Quaternion(Basis.FromEuler(new Vector3(rng.RandfRange(-0.25f, 0.25f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.25f, 0.25f))));
			var basis = new Basis(rot) * Basis.FromScale(scale);
			var origin = new Vector3(p.X, -scale.Y * 0.25f, p.Y); // sink slightly into the dust
			float shade = rng.RandfRange(0.72f, 1.08f);
			perVariant[rng.RandiRange(0, meshes.Length - 1)].Add((new Transform3D(basis, origin), new Color(shade, shade, shade * 0.98f)));

			if (bodies != null)
			{
				float radius = Mathf.Min(scale.X, scale.Z) * 0.85f;
				bodies.AddChild(new CollisionShape3D
				{
					Name = $"Shape{placed}",
					Position = origin + new Vector3(0, scale.Y * 0.2f, 0),
					Shape = new SphereShape3D { Radius = radius },
				});
			}
			placed++;
		}

		for (int v = 0; v < meshes.Length; v++)
		{
			var list = perVariant[v];
			var mm = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = true,
				Mesh = meshes[v],
				InstanceCount = list.Count,
			};
			for (int k = 0; k < list.Count; k++)
			{
				mm.SetInstanceTransform(k, list[k].xf);
				mm.SetInstanceColor(k, list[k].tint);
			}
			layer.AddChild(new MultiMeshInstance3D
			{
				Name = $"{name}_Shape{v}",
				Multimesh = SaveResource(mm, $"{name.ToLower()}_{v}.res"),
				MaterialOverride = material,
			});
		}
	}

	/// <summary>
	/// Low-poly rock: a coarse SphereMesh whose vertices are pushed in/out by 3D fractal noise,
	/// with a flattened base, rebuilt with flat (faceted) normals.
	/// </summary>
	private static ArrayMesh BuildRockMesh(int seed)
	{
		var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 7 };
		Godot.Collections.Array arrays = sphere.GetMeshArrays();
		Vector3[] verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

		var noise = new FastNoiseLite
		{
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
			Seed = seed,
			Frequency = 0.8f,
			FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 3,
		};

		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		st.SetSmoothGroup(uint.MaxValue); // flat shading = faceted low-poly look
		foreach (int index in indices)
		{
			Vector3 v = verts[index];
			// Displacement depends only on position, so duplicated seam vertices stay welded.
			Vector3 d = v * (1f + noise.GetNoise3Dv(v * 1.7f) * 0.5f);
			if (d.Y < -0.15f)
				d.Y = -0.15f + (d.Y + 0.15f) * 0.3f; // flat-ish underside sits on the ground
			st.AddVertex(d);
		}
		st.GenerateNormals();
		return st.Commit();
	}
}

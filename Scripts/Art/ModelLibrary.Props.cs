using Godot;
using static StyleKit;

public static partial class ModelLibrary
{
	// ======================================================================
	//  Shared parts
	// ======================================================================

	/// <summary>Upright capsule pressure tank standing on <paramref name="basePos"/>: domed ends, two bands, valve with red handwheel.</summary>
	private static void AddCapsuleTank(MeshKit k, Vector3 basePos, float r, float h, Color body, Color band)
	{
		float capH = r * 0.6f;
		float bodyH = h - 2f * capH;
		Vector3 c = basePos + new Vector3(0f, h / 2f, 0f);
		k.Cylinder(P, c, r, r, bodyH, 12, body, caps: false);
		k.Sphere(P, c + new Vector3(0f, bodyH / 2f, 0f), new Vector3(r, capH, r), 3, 12, body, 0f, 90f);
		k.Sphere(P, c - new Vector3(0f, bodyH / 2f, 0f), new Vector3(r, capH, r), 3, 12, body, -90f, 0f);
		foreach (float f in new[] { -0.28f, 0.28f })
			k.Cylinder(M, c + new Vector3(0f, bodyH * f, 0f), r * 1.06f, r * 1.06f, Mathf.Min(0.1f, h * 0.05f), 12, band);
		k.Box(P, c + new Vector3(0f, 0f, r * 0.99f), new Vector3(r * 0.6f, bodyH * 0.25f, 0.02f), CreamDark);
		k.Box(P, c + new Vector3(0f, bodyH * 0.13f, r * 1.0f), new Vector3(r * 0.6f, 0.05f, 0.02f), Hazard);
		Vector3 top = basePos + new Vector3(0f, h, 0f);
		k.Cylinder(M, top + new Vector3(0f, 0.06f, 0f), 0.06f, 0.06f, 0.12f, 6, Steel);
		k.Torus(P, top + new Vector3(0f, 0.15f, 0f), 0.09f, 0.018f, 10, 4, Red);
	}

	/// <summary>Small gauge dial facing +Z.</summary>
	private static void AddGauge(MeshKit k, Vector3 c, float r)
	{
		k.Push(c, new Vector3(90f, 0f, 0f));
		k.Cylinder(M, Vector3.Zero, r * 1.15f, r * 1.15f, 0.03f, 10, Gunmetal);
		k.Cylinder(P, new Vector3(0f, 0.02f, 0f), r, r, 0.015f, 10, Cream);
		k.Box(P, new Vector3(r * 0.3f, 0.03f, 0f), new Vector3(r * 0.6f, 0.005f, 0.012f), Red, new Vector3(0f, 35f, 0f));
		k.Pop();
	}

	// ======================================================================
	//  Radish + hydroponic planter
	// ======================================================================

	private static void AddRadish(MeshKit k, Vector3 soil, float s)
	{
		k.Sphere(P, soil + new Vector3(0f, 0.03f * s, 0f), new Vector3(0.075f, 0.08f, 0.075f) * s, 3, 7, Radish);
		k.Cylinder(P, soil - new Vector3(0f, 0.065f * s, 0f), 0f, 0.03f * s, 0.07f * s, 5, RootWhite);
		int leaves = 4;
		float spin = k.Rng.Randf() * 360f;
		for (int i = 0; i < leaves; i++)
		{
			k.Push(soil + new Vector3(0f, 0.09f * s, 0f), new Vector3(-38f + k.Rng.RandfRange(-8f, 8f), spin + i * 90f, 0f));
			k.Box(P, new Vector3(0f, 0.09f * s, 0f), new Vector3(0.075f, 0.18f, 0.012f) * s, i % 2 == 0 ? Leaf : LeafDark);
			k.Pop();
		}
	}

	/// <summary>Standalone radish (for pickups / UI props), sitting on its soil line at the origin.</summary>
	public static ArrayMesh RadishPlant(float scale = 1f)
	{
		var k = new MeshKit(101);
		AddRadish(k, Vector3.Zero, scale);
		return k.Commit();
	}

	/// <summary>
	/// Hydroponic planter sized to a placeholder box (<paramref name="size"/>, origin = box centre):
	/// tray of radishes, arch frame with grow-light bars, water tank with sight glass, control box.
	/// </summary>
	public static ArrayMesh HydroPlanter(Vector3 size)
	{
		var k = new MeshKit(111);
		float L = size.X, H = size.Y, W = size.Z;
		float y0 = -H / 2f;
		float trayTop = y0 + H * 0.72f;

		k.Box(P, new Vector3(0f, y0 + H * 0.36f, 0f), new Vector3(L, H * 0.72f, W), TealPale);
		k.Box(P, new Vector3(0f, trayTop, 0f), new Vector3(L + 0.05f, 0.06f, W + 0.05f), Cream);
		k.Box(P, new Vector3(0f, trayTop + 0.035f, 0f), new Vector3(L - 0.12f, 0.05f, W - 0.12f), Soil);
		foreach (float sx in new[] { -1f, 1f })
		foreach (float sz in new[] { -1f, 1f })
			k.Box(M, new Vector3(sx * (L / 2f - 0.08f), y0 + 0.05f, sz * (W / 2f - 0.06f)), new Vector3(0.12f, 0.1f, 0.12f), DarkSteel);
		for (float x = -L / 2f + 1.5f; x < L / 2f - 1f; x += 3f)
			k.Box(G, new Vector3(x, y0 + H * 0.45f, W / 2f + 0.006f), new Vector3(0.22f, 0.12f, 0.01f), GlowScreen);

		// radishes in two rows
		int n = Mathf.Max(2, Mathf.FloorToInt((L - 0.4f) / 0.42f));
		float step = (L - 0.4f) / (n - 1);
		foreach (float z in new[] { -W / 4f, W / 4f })
			for (int i = 0; i < n; i++)
				AddRadish(k, new Vector3(-L / 2f + 0.2f + i * step + k.Rng.RandfRange(-0.05f, 0.05f), trayTop + 0.06f, z), k.Rng.RandfRange(0.9f, 1.25f));

		// arch frame + grow-light bars
		float archTop = y0 + H + 1.15f;
		foreach (float x in new[] { -L / 2f + 0.15f, L / 2f - 0.15f })
		foreach (float z in new[] { -W / 2f, W / 2f })
			k.Beam(M, new Vector3(x, trayTop, z), new Vector3(x, archTop, z), 0.07f, Gunmetal);
		foreach (float z in new[] { -W / 2f, W / 2f })
			k.Beam(M, new Vector3(-L / 2f + 0.15f, archTop, z), new Vector3(L / 2f - 0.15f, archTop, z), 0.07f, Gunmetal);
		for (float x = -L / 2f + 1.2f; x < L / 2f - 0.6f; x += 2.2f)
		{
			k.Beam(M, new Vector3(x, archTop, -W / 2f), new Vector3(x, archTop, W / 2f), 0.06f, Gunmetal);
			k.Box(M, new Vector3(x, archTop - 0.08f, 0f), new Vector3(1.5f, 0.07f, 0.26f), Steel);
			k.Box(G, new Vector3(x, archTop - 0.125f, 0f), new Vector3(1.4f, 0.02f, 0.2f), GlowWhite);
		}

		// water tank at +X with sight glass and feed pipe
		var tankBase = new Vector3(L / 2f + 0.45f, y0, 0f);
		AddCapsuleTank(k, tankBase, 0.32f, 1.5f, Cream, Teal);
		k.Box(G, tankBase + new Vector3(0f, 0.75f, 0.33f), new Vector3(0.08f, 0.5f, 0.01f), GlowBlue);
		k.Pipe(M, new[] { tankBase + new Vector3(0f, 1.45f, 0f), tankBase + new Vector3(0f, 1.65f, 0f), new Vector3(L / 2f - 0.1f, y0 + 1.65f, 0f), new Vector3(L / 2f - 0.1f, trayTop, 0f) }, 0.05f, 6, Steel);
		k.Torus(P, new Vector3(L / 2f + 0.15f, y0 + 1.65f, 0f), 0.07f, 0.015f, 8, 4, Red, new Vector3(0f, 0f, 90f));

		// control box at -X
		k.Box(P, new Vector3(-L / 2f - 0.22f, y0 + 0.6f, 0f), new Vector3(0.3f, 0.5f, 0.45f), Gunmetal);
		k.Box(G, new Vector3(-L / 2f - 0.375f, y0 + 0.68f, 0f), new Vector3(0.01f, 0.18f, 0.3f), GlowScreen);
		return k.Commit();
	}

	// ======================================================================
	//  Life support: O2 tank cluster, pressure tank, control station
	// ======================================================================

	/// <summary>Four O2 tanks on a skid with manifold pipes, valves and gauges (front = +Z).</summary>
	public static ArrayMesh O2Cluster()
	{
		var k = new MeshKit(121);
		var tankColor = new Color(0.62f, 0.68f, 0.63f);
		k.Box(M, new Vector3(0f, 0.06f, 0f), new Vector3(2.3f, 0.12f, 1.7f), Gunmetal);
		var tanks = new[] { new Vector3(-0.55f, 0.12f, -0.42f), new Vector3(0.55f, 0.12f, -0.42f), new Vector3(-0.55f, 0.12f, 0.42f), new Vector3(0.55f, 0.12f, 0.42f) };
		foreach (Vector3 t in tanks)
			AddCapsuleTank(k, t, 0.36f, 2.0f, tankColor, Gunmetal);

		// manifold loop over the tank tops + drop to the front
		float y = 2.42f;
		k.Pipe(M, new[] { new Vector3(-0.55f, 2.25f, -0.42f), new Vector3(-0.55f, y, -0.42f), new Vector3(0.55f, y, -0.42f), new Vector3(0.55f, 2.25f, -0.42f) }, 0.05f, 6, Steel);
		k.Pipe(M, new[] { new Vector3(-0.55f, 2.25f, 0.42f), new Vector3(-0.55f, y, 0.42f), new Vector3(0.55f, y, 0.42f), new Vector3(0.55f, 2.25f, 0.42f) }, 0.05f, 6, Steel);
		k.Pipe(M, new[] { new Vector3(0f, y, -0.42f), new Vector3(0f, y + 0.12f, 0f), new Vector3(0f, y, 0.42f) }, 0.05f, 6, Steel);
		k.Pipe(M, new[] { new Vector3(0f, y + 0.12f, 0f), new Vector3(0f, y + 0.12f, 0.95f), new Vector3(0f, 1.2f, 0.95f), new Vector3(0f, 0.15f, 0.95f) }, 0.06f, 6, Steel);
		foreach (float x in new[] { -0.3f, 0.3f })
			k.Torus(P, new Vector3(x, y, 0.42f), 0.07f, 0.016f, 8, 4, Red, new Vector3(0f, 0f, 90f));
		AddGauge(k, new Vector3(0f, 1.6f, 1.0f), 0.1f);
		AddGauge(k, new Vector3(0f, 1.95f, 1.0f), 0.08f);
		return k.Commit();
	}

	/// <summary>Capsule pressure tank centred on the origin along Y (matches a CSGCylinder3D placeholder).</summary>
	public static ArrayMesh PressureTank(float radius, float height)
	{
		var k = new MeshKit(131);
		AddCapsuleTank(k, new Vector3(0f, -height / 2f, 0f), radius, height, new Color(0.84f, 0.85f, 0.86f), Teal);
		return k.Commit();
	}

	/// <summary>Control cabinet with screens, gauges, button grid, stack light and floor cables (front = +Z).</summary>
	public static ArrayMesh ControlStation()
	{
		var k = new MeshKit(141);
		k.Box(P, new Vector3(0f, 0.75f, 0f), new Vector3(1.3f, 1.5f, 0.6f), Gunmetal);
		k.Box(P, new Vector3(0f, 0.95f, 0.305f), new Vector3(1.12f, 0.95f, 0.01f), CreamDark);
		k.Box(G, new Vector3(-0.27f, 1.18f, 0.315f), new Vector3(0.45f, 0.3f, 0.01f), GlowScreen);
		k.Box(G, new Vector3(0.27f, 1.18f, 0.315f), new Vector3(0.45f, 0.3f, 0.01f), GlowScreen);
		AddGauge(k, new Vector3(-0.32f, 0.78f, 0.32f), 0.1f);
		AddGauge(k, new Vector3(0.32f, 0.78f, 0.32f), 0.1f);
		for (int r = 0; r < 2; r++)
		for (int c = 0; c < 4; c++)
			k.Box(G, new Vector3(-0.12f + c * 0.08f, 0.82f - r * 0.08f, 0.315f), new Vector3(0.05f, 0.05f, 0.01f), ButtonColors[(r * 4 + c) % ButtonColors.Length]);
		k.Box(P, new Vector3(0.655f, 0.5f, 0f), new Vector3(0.01f, 0.4f, 0.4f), DarkSteel);
		for (int i = 0; i < 4; i++)
			k.Box(G, new Vector3(0.5f, 1.58f + i * 0.08f, -0.15f), new Vector3(0.1f, 0.07f, 0.1f), new[] { GlowBlue, GlowGreen, GlowYellow, GlowRed }[i]);
		k.Pipe(P, new[] { new Vector3(0.4f, 0.1f, -0.31f), new Vector3(0.6f, 0.03f, -0.6f), new Vector3(1.2f, 0.03f, -0.85f) }, 0.035f, 5, DarkSteel);
		k.Pipe(P, new[] { new Vector3(0.2f, 0.1f, -0.31f), new Vector3(0.3f, 0.03f, -0.7f), new Vector3(0.9f, 0.03f, -1.1f) }, 0.03f, 5, Red);
		k.Pipe(P, new[] { new Vector3(0f, 0.1f, -0.31f), new Vector3(0f, 0.03f, -0.8f), new Vector3(0.5f, 0.03f, -1.3f) }, 0.03f, 5, Hazard);
		return k.Commit();
	}

	// ======================================================================
	//  Solar tracker (+ broken variant)
	// ======================================================================

	/// <summary>Sun-tracking array: A-frames, axle with gear hub, 2x2 panels tilted <paramref name="tiltDeg"/> about X at 1.7 m.</summary>
	public static ArrayMesh SolarTracker(float panelW, float panelD, float tiltDeg, bool broken = false)
	{
		var k = new MeshKit(broken ? 161 : 151);
		float axleY = 1.7f;
		float ax = panelW / 2f - 0.3f;

		// skids, A-frames, axle, gear hub, bearings
		foreach (float x in new[] { -ax, ax })
		{
			k.Beam(M, new Vector3(x, 0.05f, -0.65f), new Vector3(x, 0.05f, 0.65f), 0.1f, Gunmetal);
			k.Beam(M, new Vector3(x, 0.05f, -0.6f), new Vector3(x, axleY, 0f), 0.1f, Gunmetal);
			k.Beam(M, new Vector3(x, 0.05f, 0.6f), broken && x > 0 ? new Vector3(x + 0.25f, axleY - 0.6f, 0.15f) : new Vector3(x, axleY, 0f), 0.1f, Gunmetal);
			k.Beam(M, new Vector3(x, 0.6f, -0.4f), new Vector3(x, 0.6f, 0.4f), 0.06f, Gunmetal);
			k.CylinderBetween(P, new Vector3(x - 0.1f, axleY, 0f), new Vector3(x + 0.1f, axleY, 0f), 0.15f, 10, Brass);
		}
		k.Beam(M, new Vector3(-ax, 0.05f, 0f), new Vector3(ax, 0.05f, 0f), 0.08f, Gunmetal);
		k.CylinderBetween(M, new Vector3(-panelW / 2f + 0.1f, axleY, 0f), new Vector3(panelW / 2f - 0.1f, axleY, 0f), 0.08f, 8, Steel);
		k.CylinderBetween(M, new Vector3(-0.15f, axleY, 0f), new Vector3(0.15f, axleY, 0f), 0.32f, 12, Gunmetal);
		for (int i = 0; i < 12; i++)
		{
			float a = i * Mathf.Tau / 12f;
			k.Box(M, new Vector3(0f, axleY + Mathf.Sin(a) * 0.34f, Mathf.Cos(a) * 0.34f), new Vector3(0.26f, 0.06f, 0.06f), DarkSteel, new Vector3(-Mathf.RadToDeg(a), 0f, 0f));
		}

		// panel group
		float tilt = broken ? tiltDeg + 28f : tiltDeg;
		k.Push(new Vector3(0f, axleY + 0.1f, 0f), new Vector3(tilt, 0f, broken ? 9f : 0f));
		float w = panelW / 2f - 0.08f, d = panelD / 2f - 0.06f;
		int panelIndex = 0;
		foreach (float cx in new[] { -panelW / 4f, panelW / 4f })
		foreach (float cz in new[] { -panelD / 4f, panelD / 4f })
		{
			panelIndex++;
			bool shattered = broken && panelIndex is 2 or 3;
			k.Box(M, new Vector3(cx, 0f, cz), new Vector3(w, 0.06f, d), Gunmetal);
			if (shattered)
				AddShards(k, new Vector3(cx, 0.035f, cz), w - 0.08f, d - 0.08f);
			else
				AddCells(k, new Vector3(cx, 0.035f, cz), w - 0.08f, d - 0.08f);
		}
		k.Beam(M, new Vector3(-panelW / 2f + 0.2f, -0.06f, 0f), new Vector3(panelW / 2f - 0.2f, -0.06f, 0f), 0.08f, Gunmetal);
		k.Pop();

		// junction boxes and cables
		k.Box(P, new Vector3(-0.6f, 0.35f, 0.5f), new Vector3(0.4f, 0.5f, 0.25f), Cream);
		k.Box(G, new Vector3(-0.68f, 0.45f, 0.626f), new Vector3(0.05f, 0.05f, 0.01f), broken ? GlowRed : GlowGreen);
		k.Box(G, new Vector3(-0.56f, 0.45f, 0.626f), new Vector3(0.05f, 0.05f, 0.01f), GlowAmber);
		k.Box(P, new Vector3(0.7f, 0.3f, 0.55f), new Vector3(0.35f, 0.4f, 0.2f), TealPale);
		k.Pipe(P, new[] { new Vector3(-0.6f, 0.6f, 0.5f), new Vector3(-0.3f, 1.2f, 0.25f), new Vector3(0f, 1.55f, 0.1f) }, 0.025f, 5, DarkSteel);

		if (broken)
		{
			// dangling wires and a fallen shard field on the ground
			Color[] wire = { Red, Hazard, EmblemBlue, Rust };
			for (int i = 0; i < 4; i++)
			{
				float x = 0.4f + i * 0.18f;
				k.Pipe(P, new[] { new Vector3(x, axleY, 0.3f), new Vector3(x + 0.1f, axleY - 0.5f, 0.55f + i * 0.05f), new Vector3(x + 0.05f, 0.5f, 0.8f) }, 0.018f, 4, wire[i]);
			}
			for (int i = 0; i < 6; i++)
				k.Box(P, new Vector3(k.Rng.RandfRange(0.5f, 2f), 0.02f, k.Rng.RandfRange(0.8f, 1.6f)), new Vector3(k.Rng.RandfRange(0.15f, 0.4f), 0.015f, k.Rng.RandfRange(0.1f, 0.3f)), SolarBlue,
					new Vector3(0f, k.Rng.RandfRange(0f, 180f), k.Rng.RandfRange(-6f, 6f)));
		}
		return k.Commit();
	}

	private static void AddCells(MeshKit k, Vector3 c, float w, float d)
	{
		k.Box(P, c, new Vector3(w, 0.012f, d), SolarBlue);
		for (float x = -w / 2f + 0.3f; x < w / 2f - 0.1f; x += 0.3f)
			k.Box(P, c + new Vector3(x, 0.008f, 0f), new Vector3(0.014f, 0.005f, d - 0.04f), CellLine);
		for (float z = -d / 2f + 0.3f; z < d / 2f - 0.1f; z += 0.3f)
			k.Box(P, c + new Vector3(0f, 0.008f, z), new Vector3(w - 0.04f, 0.005f, 0.014f), CellLine);
	}

	private static void AddShards(MeshKit k, Vector3 c, float w, float d)
	{
		const int nx = 4, nz = 3;
		for (int i = 0; i < nx; i++)
		for (int j = 0; j < nz; j++)
		{
			var a = c + new Vector3(-w / 2f + w * i / nx, 0f, -d / 2f + d * j / nz);
			var b = a + new Vector3(w / nx, 0f, 0f);
			var e = a + new Vector3(0f, 0f, d / nz);
			var f = a + new Vector3(w / nx, 0f, d / nz);
			Vector3 lift() => new(k.Rng.RandfRange(-0.03f, 0.03f), k.Rng.RandfRange(0f, 0.06f), k.Rng.RandfRange(-0.03f, 0.03f));
			Vector3 g1 = lift(), g2 = lift();
			k.Face(P, (i + j) % 2 == 0 ? SolarBlue : new Color(0.16f, 0.25f, 0.55f), Vector3.Up, a + g1, b + g1, e + g1);
			k.Face(P, (i + j) % 2 == 1 ? SolarBlue : new Color(0.16f, 0.25f, 0.55f), Vector3.Up, b + g2, f + g2, e + g2);
			k.Face(P, SolarBlue, Vector3.Down, a + g1 - new Vector3(0, 0.01f, 0), b + g1 - new Vector3(0, 0.01f, 0), e + g1 - new Vector3(0, 0.01f, 0));
			k.Face(P, SolarBlue, Vector3.Down, b + g2 - new Vector3(0, 0.01f, 0), f + g2 - new Vector3(0, 0.01f, 0), e + g2 - new Vector3(0, 0.01f, 0));
		}
	}

	// ======================================================================
	//  Sample basket (wire crate with handles and a logger box)
	// ======================================================================

	/// <summary>Wire-mesh sample crate sized to a placeholder box (origin = box centre), with core samples inside.</summary>
	public static ArrayMesh SampleBasket(Vector3 size)
	{
		var k = new MeshKit(171);
		Vector3 h = size / 2f;
		var frame = new[]
		{
			new Vector3(-h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, -h.Z), new Vector3(h.X, -h.Y, h.Z), new Vector3(-h.X, -h.Y, h.Z),
			new Vector3(-h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, -h.Z), new Vector3(h.X, h.Y, h.Z), new Vector3(-h.X, h.Y, h.Z),
		};
		int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
		for (int i = 0; i < edges.Length; i += 2)
			k.Beam(M, frame[edges[i]], frame[edges[i + 1]], 0.05f, Steel);
		k.Box(M, new Vector3(0f, -h.Y + 0.02f, 0f), new Vector3(size.X - 0.04f, 0.03f, size.Z - 0.04f), Gunmetal);

		// wire mesh on the four sides
		const float pitch = 0.14f;
		foreach (float sz in new[] { -h.Z, h.Z })
		{
			for (float x = -h.X + pitch; x < h.X - 0.02f; x += pitch)
				k.Beam(M, new Vector3(x, -h.Y, sz), new Vector3(x, h.Y, sz), 0.012f, Gunmetal);
			for (float y = -h.Y + pitch; y < h.Y - 0.02f; y += pitch)
				k.Beam(M, new Vector3(-h.X, y, sz), new Vector3(h.X, y, sz), 0.012f, Gunmetal);
		}
		foreach (float sx in new[] { -h.X, h.X })
		{
			for (float z = -h.Z + pitch; z < h.Z - 0.02f; z += pitch)
				k.Beam(M, new Vector3(sx, -h.Y, z), new Vector3(sx, h.Y, z), 0.012f, Gunmetal);
			for (float y = -h.Y + pitch; y < h.Y - 0.02f; y += pitch)
				k.Beam(M, new Vector3(sx, y, -h.Z), new Vector3(sx, y, h.Z), 0.012f, Gunmetal);
		}

		// handles at both ends
		foreach (float sx in new[] { -1f, 1f })
		{
			Vector3 a = new(sx * h.X, h.Y, -0.25f), b = new(sx * h.X, h.Y, 0.25f);
			Vector3 up = new(sx * 0.05f, 0.18f, 0f);
			k.Beam(M, a, a + up, 0.035f, Steel);
			k.Beam(M, b, b + up, 0.035f, Steel);
			k.CylinderBetween(P, a + up, b + up, 0.035f, 8, DarkSteel);
		}

		// core samples inside
		for (int i = 0; i < 4; i++)
			k.CylinderBetween(P, new Vector3(-h.X + 0.25f, -h.Y + 0.15f + (i % 2) * 0.14f, -h.Z + 0.25f + i * 0.2f), new Vector3(h.X - 0.25f, -h.Y + 0.15f + (i % 2) * 0.14f, -h.Z + 0.25f + i * 0.2f), 0.06f, 8,
				i % 2 == 0 ? Rock : new Color(0.45f, 0.42f, 0.38f));

		// data logger clamped to the front
		k.Box(P, new Vector3(h.X * 0.45f, h.Y * 0.3f, h.Z + 0.12f), new Vector3(0.5f, 0.35f, 0.18f), Gunmetal);
		k.Box(G, new Vector3(h.X * 0.45f - 0.08f, h.Y * 0.3f + 0.05f, h.Z + 0.215f), new Vector3(0.22f, 0.12f, 0.01f), GlowScreen);
		for (int i = 0; i < 3; i++)
			k.Box(G, new Vector3(h.X * 0.45f + 0.12f, h.Y * 0.3f + 0.08f - i * 0.07f, h.Z + 0.215f), new Vector3(0.05f, 0.04f, 0.01f), ButtonColors[i]);
		k.Pipe(P, new[] { new Vector3(h.X * 0.45f + 0.2f, h.Y * 0.3f - 0.15f, h.Z + 0.15f), new Vector3(h.X + 0.1f, -h.Y + 0.1f, h.Z + 0.2f), new Vector3(h.X + 0.4f, -h.Y + 0.02f, h.Z + 0.1f) }, 0.025f, 5, Red);
		return k.Commit();
	}

	// ======================================================================
	//  Command-center desk console + chair
	// ======================================================================

	/// <summary>
	/// Slanted control desk in the lobby desks' local frame: +X runs from the operator to the back,
	/// width along Z (centred). The slanted face matches the CSG desk so the live Label3D screens
	/// still sit on it. Trestle legs, button banks, keyboards.
	/// </summary>
	public static ArrayMesh DeskConsole(float width)
	{
		var k = new MeshKit(181);
		float hw = width / 2f;
		var upper = new[]
		{
			new Vector2(0f, 0.62f), new Vector2(0f, 0.8f), new Vector2(0.25f, 0.8f),
			new Vector2(0.55f, 1.2f), new Vector2(0.9f, 1.2f), new Vector2(0.9f, 0.62f),
		};
		k.Extrude(P, upper, -hw, hw, Gunmetal);
		k.Extrude(P, upper, hw, hw + 0.04f, CreamDark);
		k.Extrude(P, upper, -hw - 0.04f, -hw, CreamDark);
		k.Box(M, new Vector3(0.12f, 0.805f, 0f), new Vector3(0.26f, 0.012f, width), DarkSteel); // writing lip
		k.Box(P, new Vector3(0.725f, 1.205f, 0f), new Vector3(0.35f, 0.012f, width), DarkSteel);   // top deck

		// trestle legs, back panel, side cabinet with vents
		foreach (float z in new[] { -hw + 0.12f, hw - 0.12f })
		{
			k.Beam(M, new Vector3(0.12f, 0f, z), new Vector3(0.38f, 0.62f, z), 0.08f, Gunmetal);
			k.Beam(M, new Vector3(0.82f, 0f, z), new Vector3(0.56f, 0.62f, z), 0.08f, Gunmetal);
			k.Beam(M, new Vector3(0.05f, 0.03f, z), new Vector3(0.88f, 0.03f, z), 0.07f, DarkSteel);
		}
		k.Box(P, new Vector3(0.86f, 0.31f, 0f), new Vector3(0.04f, 0.62f, width - 0.35f), Gunmetal);
		k.Box(P, new Vector3(0.62f, 0.3f, hw - 0.35f), new Vector3(0.45f, 0.58f, 0.36f), TealDark);
		for (int i = 0; i < 4; i++)
			k.Box(P, new Vector3(0.39f, 0.18f + i * 0.09f, hw - 0.35f), new Vector3(0.01f, 0.03f, 0.26f), DarkSteel);

		// button banks on the top deck
		for (float z = -hw + 0.12f; z < hw - 0.08f; z += 0.11f)
			for (int r = 0; r < 2; r++)
				k.Box(G, new Vector3(0.64f + r * 0.12f, 1.218f, z), new Vector3(0.06f, 0.02f, 0.06f), ButtonColors[k.Rng.RandiRange(0, ButtonColors.Length - 1)]);

		// keyboards on the lip
		for (int i = -1; i <= 1; i++)
		{
			float z = i * width / 3f;
			k.Box(P, new Vector3(0.12f, 0.82f, z), new Vector3(0.18f, 0.025f, 0.5f), DarkSteel);
			for (int r = 0; r < 3; r++)
				k.Box(P, new Vector3(0.07f + r * 0.05f, 0.835f, z), new Vector3(0.03f, 0.008f, 0.44f), Gunmetal);
		}
		return k.Commit();
	}

	/// <summary>Swivel chair facing +X (the desk), origin on the floor under the seat.</summary>
	public static ArrayMesh Chair()
	{
		var k = new MeshKit(191);
		k.Cylinder(M, new Vector3(0f, 0.25f, 0f), 0.04f, 0.04f, 0.42f, 6, Steel);
		for (int i = 0; i < 5; i++)
		{
			float a = i * Mathf.Tau / 5f;
			var tip = new Vector3(Mathf.Cos(a) * 0.28f, 0.04f, Mathf.Sin(a) * 0.28f);
			k.Beam(M, new Vector3(0f, 0.07f, 0f), tip, 0.04f, Gunmetal);
			k.Sphere(P, tip - new Vector3(0f, 0.015f, 0f), Vector3.One * 0.03f, 2, 6, DarkSteel);
		}
		k.Box(P, new Vector3(0f, 0.47f, 0f), new Vector3(0.5f, 0.08f, 0.5f), TealDark);
		k.Box(P, new Vector3(-0.24f, 0.82f, 0f), new Vector3(0.07f, 0.56f, 0.46f), TealDark, new Vector3(0f, 0f, -8f));
		k.Beam(M, new Vector3(-0.2f, 0.45f, 0f), new Vector3(-0.25f, 0.6f, 0f), 0.04f, Steel);
		return k.Commit();
	}

	// ======================================================================
	//  Crystal-studded rock (for the MultiMesh rock fields; ~1 m radius like the old rocks)
	// ======================================================================

	public static ArrayMesh CrystalRock(int seed)
	{
		var k = new MeshKit(seed) { ColorJitter = 0.09f };
		var noise = new FastNoiseLite
		{
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
			Seed = seed,
			Frequency = 0.9f,
			FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 3,
		};
		Vector3 Shape(Vector3 u)
		{
			Vector3 d = u * (1f + noise.GetNoise3Dv(u * 1.7f) * 0.45f);
			if (d.Y < -0.15f)
				d.Y = -0.15f + (d.Y + 0.15f) * 0.3f;
			return d;
		}
		k.Sphere(P, Vector3.Zero, Vector3.One, 6, 9, Rock, shape: Shape);

		// crystal prisms poking out of the upper half
		int crystals = 3 + (seed % 4);
		for (int i = 0; i < crystals; i++)
		{
			float az = k.Rng.Randf() * Mathf.Tau;
			float el = k.Rng.RandfRange(0.15f, 1.2f);
			var dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
			Vector3 root = Shape(dir) * 0.7f;
			float len = k.Rng.RandfRange(0.35f, 0.75f);
			float r = k.Rng.RandfRange(0.1f, 0.2f);
			Vector3 tip = root + dir * len;
			k.CylinderBetween(P, root, tip, r, 5, Crystal);
			k.CylinderBetween(P, tip, tip + dir * r * 1.6f, r, 5, Crystal.Lightened(0.15f), r1: 0f);
		}
		return k.Commit();
	}
}

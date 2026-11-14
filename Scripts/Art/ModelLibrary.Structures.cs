using Godot;
using System.Collections.Generic;
using static StyleKit;

public static partial class ModelLibrary
{
	// ======================================================================
	//  Geodesic greenhouse dome (from the dome reference images)
	// ======================================================================

	/// <summary>
	/// Geodesic hemisphere (frequency-4 icosphere) of glass panes on dark struts, sphere centre at the
	/// origin (sits on the CSG hull ring). A doorway gap is left toward <paramref name="doorDir"/>.
	/// Includes the interior kit from the reference: overhead pipe ring, hanging grow-light bars, ECLSS tank.
	/// </summary>
	public static ArrayMesh GeodesicDome(float radius, Vector3 doorDir, float doorHeight = 1.65f, int frequency = 4)
	{
		var k = new MeshKit(41) { ColorJitter = 0.03f };

		// Icosahedron with a vertex pointing straight up.
		float t = (1f + Mathf.Sqrt(5f)) / 2f;
		var ico = new[]
		{
			new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
			new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
			new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
		};
		int[] faces =
		{
			0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
			3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
		};
		var tilt = new Basis(Vector3.Right, Mathf.Atan2(-t, 1f)); // rotates (0,1,t) onto +Y
		for (int i = 0; i < ico.Length; i++)
			ico[i] = (tilt * ico[i]).Normalized();

		Vector3 door = new Vector3(doorDir.X, 0, doorDir.Z).Normalized() * radius + new Vector3(0, doorHeight, 0);
		var edges = new HashSet<(Vector3I, Vector3I)>();
		var edgeEnds = new List<(Vector3 a, Vector3 b)>();
		static Vector3I Q(Vector3 v) => new(Mathf.RoundToInt(v.X * 100f), Mathf.RoundToInt(v.Y * 100f), Mathf.RoundToInt(v.Z * 100f));

		void AddEdge(Vector3 a, Vector3 b)
		{
			var qa = Q(a);
			var qb = Q(b);
			var key = string.CompareOrdinal(qa.ToString(), qb.ToString()) < 0 ? (qa, qb) : (qb, qa);
			if (edges.Add(key) && ((a + b) / 2f - door).Length() > 1.9f)
				edgeEnds.Add((a, b));
		}

		void Pane(Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 centroid = (a + b + c) / 3f;
			if (centroid.Y < 0.03f * radius)
				return; // keep the upper hemisphere
			if ((centroid - door).Length() < 2.1f)
				return; // doorway
			const float inset = 0.94f;
			k.Face(Gl, GlassClear, centroid,
				centroid + (a - centroid) * inset, centroid + (b - centroid) * inset, centroid + (c - centroid) * inset);
			AddEdge(a, b);
			AddEdge(b, c);
			AddEdge(c, a);
		}

		int f = frequency;
		for (int fi = 0; fi < faces.Length; fi += 3)
		{
			Vector3 A = ico[faces[fi]], B = ico[faces[fi + 1]], C = ico[faces[fi + 2]];
			Vector3 Pt(int i, int j) => (A + (B - A) * (i / (float)f) + (C - A) * (j / (float)f)).Normalized() * radius;
			for (int i = 0; i < f; i++)
			for (int j = 0; j < f - i; j++)
			{
				Pane(Pt(i, j), Pt(i + 1, j), Pt(i, j + 1));
				if (i + j < f - 1)
					Pane(Pt(i + 1, j), Pt(i + 1, j + 1), Pt(i, j + 1));
			}
		}
		foreach (var (a, b) in edgeEnds)
			k.Beam(M, a, b, 0.12f, DarkSteel);

		// base band and door frame
		k.Torus(M, new Vector3(0f, 0.95f, 0f), radius, 0.08f, 40, 4, Gunmetal);
		Vector3 dside = new Vector3(-doorDir.Z, 0, doorDir.X).Normalized();
		Vector3 dbase = new Vector3(doorDir.X, 0, doorDir.Z).Normalized() * (radius - 0.3f);
		foreach (float s in new[] { -1.65f, 1.65f })
			k.Beam(M, dbase + dside * s + new Vector3(0, 0.25f, 0), dbase + dside * s + new Vector3(0, 3.4f, 0), 0.18f, Gunmetal);
		k.Beam(M, dbase - dside * 1.65f + new Vector3(0, 3.4f, 0), dbase + dside * 1.65f + new Vector3(0, 3.4f, 0), 0.18f, Gunmetal);
		k.Box(G, dbase + new Vector3(0, 3.6f, 0) - new Vector3(doorDir.X, 0, doorDir.Z).Normalized() * 0.1f, new Vector3(0.8f, 0.08f, 0.8f), GlowWhite);

		// interior: overhead pipe ring with hangers and grow-light bars
		float ringY = radius * 0.62f;
		k.Torus(M, new Vector3(0f, ringY, 0f), radius * 0.45f, 0.11f, 24, 6, Steel);
		for (int i = 0; i < 4; i++)
		{
			float a = i * Mathf.Tau / 4f + 0.4f;
			var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius * 0.45f;
			k.Beam(M, p + new Vector3(0, ringY, 0), p + new Vector3(0, ringY + radius * 0.25f, 0), 0.08f, Gunmetal);
		}
		for (int i = -1; i <= 1; i++)
		{
			var c = new Vector3(i * radius * 0.3f, ringY - 1.2f, 0f);
			k.Box(M, c + new Vector3(0, 0.06f, 0), new Vector3(0.3f, 0.1f, radius * 0.75f), Gunmetal);
			k.Box(G, c, new Vector3(0.22f, 0.04f, radius * 0.72f), GlowWhite);
			k.Beam(M, c + new Vector3(0, 0.1f, radius * 0.3f), c + new Vector3(0, 1.2f, radius * 0.3f), 0.03f, Steel);
			k.Beam(M, c + new Vector3(0, 0.1f, -radius * 0.3f), c + new Vector3(0, 1.2f, -radius * 0.3f), 0.03f, Steel);
		}

		// ECLSS tank against the wall + feed pipe up to the ring
		var tank = new Vector3(radius * 0.62f, 0.25f, -radius * 0.55f);
		AddCapsuleTank(k, tank, 0.55f, 2.2f, Cream, Teal);
		k.Box(G, tank + new Vector3(-0.56f, 1.1f, 0f), new Vector3(0.02f, 0.7f, 0.12f), GlowBlue);
		k.Pipe(M, new[] { tank + new Vector3(0, 2.3f, 0), tank + new Vector3(0, ringY - 0.25f, 0), new Vector3(radius * 0.45f * 0.8f, ringY, -radius * 0.45f * 0.6f) }, 0.08f, 6, Steel);
		return k.Commit();
	}

	// ======================================================================
	//  Comm tower (lattice mast + dish + equipment cabinet)
	// ======================================================================

	public static ArrayMesh CommTower(float height = 6.5f)
	{
		var k = new MeshKit(51);
		const float b0 = 0.6f, b1 = 0.28f;
		const int levels = 5;
		float Half(float y) => Mathf.Lerp(b0, b1, y / height);
		Vector3 Corner(int c, float y) => new((c is 0 or 3 ? -1 : 1) * Half(y), y, (c < 2 ? -1 : 1) * Half(y));

		for (int c = 0; c < 4; c++)
			k.Beam(M, Corner(c, 0f), Corner(c, height), 0.08f, Steel);
		for (int l = 0; l <= levels; l++)
		{
			float y = height * l / levels;
			for (int c = 0; c < 4; c++)
				k.Beam(M, Corner(c, y), Corner((c + 1) % 4, y), 0.05f, Steel);
			if (l == levels)
				continue;
			float y2 = height * (l + 1) / levels;
			for (int c = 0; c < 4; c++)
			{
				int n = (c + 1) % 4;
				k.Beam(M, Corner(c, y), Corner(n, y2), 0.035f, Steel);
				k.Beam(M, Corner(n, y), Corner(c, y2), 0.035f, Steel);
			}
		}

		// platform and dish
		k.Box(M, new Vector3(0f, height + 0.04f, 0f), new Vector3(b1 * 2f + 0.7f, 0.07f, b1 * 2f + 0.7f), Gunmetal);
		k.Push(new Vector3(0f, height + 0.6f, 0.35f), new Vector3(50f, 0f, 0f));
		k.Cylinder(P, new Vector3(0f, 0.22f, 0f), 0.12f, 1.1f, 0.45f, 16, Cream, caps: false);
		k.Cylinder(P, new Vector3(0f, 0.22f, 0f), 0.12f, 1.1f, 0.45f, 16, CreamDark, caps: false, inward: true);
		k.Torus(M, new Vector3(0f, 0.45f, 0f), 1.1f, 0.04f, 20, 4, Gunmetal);
		for (int i = 0; i < 3; i++)
		{
			float a = i * Mathf.Tau / 3f;
			k.Beam(M, new Vector3(Mathf.Cos(a) * 1.05f, 0.45f, Mathf.Sin(a) * 1.05f), new Vector3(0f, 1.2f, 0f), 0.03f, Gunmetal);
		}
		k.Cylinder(M, new Vector3(0f, 1.25f, 0f), 0.06f, 0.14f, 0.18f, 8, Brass);
		k.Box(M, new Vector3(0f, -0.1f, 0f), new Vector3(0.4f, 0.3f, 0.4f), Gunmetal);
		k.Pop();

		// whip antenna + red beacon, warning lights
		k.Beam(M, Corner(1, height), Corner(1, height) + new Vector3(0f, 1.6f, 0f), 0.03f, Steel);
		k.Box(G, Corner(1, height) + new Vector3(0f, 1.65f, 0f), Vector3.One * 0.09f, GlowRed);
		k.Box(G, Corner(3, height * 0.5f) + new Vector3(-0.05f, 0f, 0.05f), Vector3.One * 0.08f, GlowRed);

		// equipment cabinet with status lights, cable up the leg
		k.Box(P, new Vector3(0f, 0.6f, b0 + 0.5f), new Vector3(0.9f, 1.2f, 0.6f), Cream);
		k.Box(P, new Vector3(0f, 0.6f, b0 + 0.805f), new Vector3(0.75f, 1.0f, 0.01f), CreamDark);
		for (int i = 0; i < 3; i++)
			k.Box(G, new Vector3(-0.25f, 0.95f - i * 0.15f, b0 + 0.815f), new Vector3(0.07f, 0.07f, 0.01f), ButtonColors[i]);
		k.Box(G, new Vector3(0.1f, 0.85f, b0 + 0.815f), new Vector3(0.3f, 0.18f, 0.01f), GlowScreen);
		k.Pipe(P, new[] { new Vector3(0.3f, 1.2f, b0 + 0.3f), Corner(2, 1.6f), Corner(2, height - 0.2f) }, 0.035f, 5, DarkSteel);
		return k.Commit();
	}

	// ======================================================================
	//  Drill rig (column + lattice tower + gearbox + hoses + control boxes)
	// ======================================================================

	public static ArrayMesh DrillRig()
	{
		var k = new MeshKit(61);

		// base plate with hazard ring and bolts
		k.Cylinder(M, new Vector3(0f, 0.5f, 0f), 2.6f, 2.6f, 1f, 16, Gunmetal);
		for (int i = 0; i < 16; i++)
		{
			float a = (i + 0.5f) * Mathf.Tau / 16f;
			k.Box(P, new Vector3(Mathf.Cos(a) * 2.3f, 1.02f, Mathf.Sin(a) * 2.3f), new Vector3(0.45f, 0.04f, 0.45f), i % 2 == 0 ? Hazard : DarkSteel,
				new Vector3(0f, -Mathf.RadToDeg(a), 0f));
		}

		// drill bit with teeth, chuck
		k.Cylinder(M, new Vector3(0f, 1.6f, 0f), 0.45f, 0.9f, 1.2f, 8, DarkSteel);
		for (int i = 0; i < 8; i++)
		{
			float a = i * Mathf.Tau / 8f;
			var p = new Vector3(Mathf.Cos(a) * 0.55f, 1.1f, Mathf.Sin(a) * 0.55f);
			k.CylinderBetween(M, p + new Vector3(0f, 0.25f, 0f), p - new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a)) * 0.15f, 0.1f, 4, Steel, r1: 0f);
		}
		k.Cylinder(P, new Vector3(0f, 2.6f, 0f), 1.05f, 1.05f, 0.8f, 12, Rust);
		k.Cylinder(M, new Vector3(0f, 2.25f, 0f), 1.12f, 1.12f, 0.12f, 12, Brass);

		// main column with brass bands and orange collar
		k.Cylinder(P, new Vector3(0f, 5.5f, 0f), 0.95f, 0.95f, 5f, 12, Steel);
		k.Cylinder(P, new Vector3(0f, 4.2f, 0f), 1.05f, 1.05f, 0.5f, 12, Orange);
		foreach (float y in new[] { 3.3f, 5.6f, 7.6f })
			k.Cylinder(M, new Vector3(0f, y, 0f), 1.03f, 1.03f, 0.25f, 12, Brass);

		// gearbox, motor, mast + beacon
		k.Box(P, new Vector3(0f, 8.9f, 0f), new Vector3(2.6f, 1.6f, 2.6f), Gunmetal);
		k.Box(P, new Vector3(0f, 8.9f, 1.31f), new Vector3(1.6f, 0.9f, 0.02f), Teal);
		k.Box(G, new Vector3(-0.5f, 9.15f, 1.33f), new Vector3(0.3f, 0.12f, 0.01f), GlowGreen);
		k.Box(G, new Vector3(0.45f, 9.15f, 1.33f), new Vector3(0.12f, 0.12f, 0.01f), GlowRed);
		k.Cylinder(P, new Vector3(0f, 10.5f, 0f), 1.25f, 1.25f, 1.6f, 12, Rust);
		k.Cylinder(M, new Vector3(0f, 11.45f, 0f), 0.9f, 0.7f, 0.3f, 12, Gunmetal);
		k.Cylinder(M, new Vector3(0f, 13.9f, 0f), 0.28f, 0.2f, 4.8f, 8, Steel);
		k.Box(G, new Vector3(0f, 16.4f, 0f), Vector3.One * 0.3f, GlowAmber);

		// lattice tower beside the column
		const float x0 = 2.4f, x1 = 3.8f, z0 = -0.7f, z1 = 0.7f, top = 12f;
		var corners = new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };
		Vector3 C(int i, float y) => new(corners[i].X, y, corners[i].Y);
		for (int i = 0; i < 4; i++)
			k.Beam(M, C(i, 0f), C(i, top), 0.12f, Steel);
		for (float y = 0f; y < top; y += 2f)
		{
			for (int i = 0; i < 4; i++)
			{
				int n = (i + 1) % 4;
				k.Beam(M, C(i, y + 2f), C(n, y + 2f), 0.07f, Steel);
				k.Beam(M, (y / 2f) % 2 == 0 ? C(i, y) : C(n, y), (y / 2f) % 2 == 0 ? C(n, y + 2f) : C(i, y + 2f), 0.05f, Steel);
			}
		}
		k.Beam(M, C(0, top), new Vector3(1.3f, 9.6f, -0.5f), 0.18f, Gunmetal);
		k.Beam(M, C(3, top), new Vector3(1.3f, 9.6f, 0.5f), 0.18f, Gunmetal);

		// hoses to the ground control boxes
		k.Pipe(P, new[] { new Vector3(1.3f, 9.0f, 0.8f), new Vector3(2.3f, 8.0f, 1.4f), new Vector3(3.2f, 4.0f, 1.8f), new Vector3(4.6f, 0.6f, 2.5f), new Vector3(5.4f, 0.35f, 2.9f) }, 0.12f, 6, DarkSteel);
		k.Pipe(P, new[] { new Vector3(1.3f, 8.4f, 1.0f), new Vector3(2.0f, 6.5f, 1.7f), new Vector3(3.0f, 2.0f, 2.3f), new Vector3(4.0f, 0.4f, 2.7f), new Vector3(6.6f, 0.35f, 2.9f) }, 0.09f, 6, Rust);

		// control boxes
		k.Box(P, new Vector3(5.8f, 0.7f, 3.3f), new Vector3(1.2f, 1.4f, 0.8f), Cream);
		k.Box(G, new Vector3(5.8f, 0.95f, 2.89f), new Vector3(0.7f, 0.4f, 0.01f), GlowScreen);
		for (int i = 0; i < 5; i++)
			k.Box(G, new Vector3(5.45f + i * 0.17f, 0.55f, 2.89f), new Vector3(0.09f, 0.09f, 0.01f), ButtonColors[i]);
		k.Box(P, new Vector3(7.2f, 0.45f, 3.2f), new Vector3(0.9f, 0.9f, 0.8f), TealPale);
		k.Box(P, new Vector3(7.2f, 0.6f, 2.79f), new Vector3(0.4f, 0.3f, 0.01f), Hazard);
		return k.Commit();
	}

	// ======================================================================
	//  Rocket lander (ascent vehicle on its pad)
	// ======================================================================

	public static ArrayMesh RocketLander()
	{
		var k = new MeshKit(71);

		// landing platform disc
		k.Cylinder(P, new Vector3(0f, 0.08f, 0f), 4.2f, 4.3f, 0.16f, 24, new Color(0.55f, 0.55f, 0.56f));

		// legs with knee struts and foot pads
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Pi / 4f + i * Mathf.Pi / 2f;
			var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			Vector3 hip = d * 1.25f + new Vector3(0f, 3.4f, 0f);
			Vector3 foot = d * 3.0f + new Vector3(0f, 0.35f, 0f);
			k.Beam(P, hip, foot, 0.24f, Teal);
			k.Beam(M, d * 1.3f + new Vector3(0f, 1.6f, 0f), (hip + foot) / 2f, 0.12f, Gunmetal);
			k.Cylinder(P, foot - new Vector3(0f, 0.12f, 0f), 0.55f, 0.45f, 0.18f, 8, Gunmetal);
		}

		// engine bells
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Pi / 2f + i * Mathf.Tau / 3f;
			k.Cylinder(M, new Vector3(Mathf.Cos(a) * 0.6f, 0.95f, Mathf.Sin(a) * 0.6f), 0.45f, 0.25f, 0.9f, 10, DarkSteel);
		}

		// body sections
		k.Cylinder(P, new Vector3(0f, 2.5f, 0f), 1.35f, 1.35f, 2.2f, 16, Cream);
		k.Cylinder(P, new Vector3(0f, 3.75f, 0f), 1.42f, 1.42f, 0.35f, 16, Teal);
		k.Cylinder(P, new Vector3(0f, 5.45f, 0f), 1.4f, 1.4f, 3.0f, 16, Cream);
		for (int i = 0; i < 8; i++)
		{
			float a = i * Mathf.Tau / 8f;
			k.Box(M, new Vector3(Mathf.Cos(a) * 1.405f, 5.45f, Mathf.Sin(a) * 1.405f), new Vector3(0.04f, 2.9f, 0.04f), CreamDark, new Vector3(0f, -Mathf.RadToDeg(a), 0f));
		}
		k.Cylinder(P, new Vector3(0f, 7.1f, 0f), 1.45f, 1.45f, 0.3f, 16, Teal);
		k.Cylinder(P, new Vector3(0f, 7.85f, 0f), 1.38f, 1.28f, 1.2f, 16, TealPale);

		// nose cone in stacked frustums
		float[] ys = { 8.45f, 9.25f, 9.95f, 10.45f, 10.75f };
		float[] rs = { 1.28f, 1.12f, 0.86f, 0.5f, 0f };
		for (int i = 0; i < ys.Length - 1; i++)
			k.Cylinder(P, new Vector3(0f, (ys[i] + ys[i + 1]) / 2f, 0f), rs[i], rs[i + 1], ys[i + 1] - ys[i], 16, Orange);
		k.Box(P, new Vector3(0f, 9.2f, 1.13f), new Vector3(0.5f, 0.6f, 0.06f), new Color(0.78f, 0.45f, 0.17f), new Vector3(-12f, 0f, 0f));

		// portholes
		foreach (float x in new[] { -0.5f, 0.5f })
		{
			k.Torus(M, new Vector3(x, 7.85f, 1.3f), 0.22f, 0.05f, 12, 4, Gunmetal, new Vector3(90f, 0f, 0f));
			k.Cylinder(Gl, new Vector3(x, 7.85f, 1.31f), 0.2f, 0.2f, 0.03f, 12, GlassDark);
		}

		// generic mission emblem (not an agency logo)
		k.Push(new Vector3(0f, 5.0f, 1.41f), new Vector3(90f, 0f, 0f));
		k.Cylinder(P, Vector3.Zero, 0.42f, 0.42f, 0.03f, 16, EmblemBlue);
		k.Torus(P, new Vector3(0f, 0.02f, 0f), 0.34f, 0.025f, 16, 4, Cream);
		k.Box(P, new Vector3(0f, 0.025f, 0f), new Vector3(0.5f, 0.01f, 0.06f), Red, new Vector3(0f, 30f, 0f));
		k.Pop();

		// side plumbing, hatch, ladder down a leg
		k.Pipe(M, new[] { new Vector3(1.38f, 1.5f, 0.35f), new Vector3(1.48f, 3.0f, 0.35f), new Vector3(1.48f, 6.6f, 0.35f), new Vector3(1.33f, 7.9f, 0.35f) }, 0.08f, 6, Steel);
		k.Box(P, new Vector3(1.47f, 4.6f, -0.3f), new Vector3(0.12f, 0.6f, 0.4f), Gunmetal);
		k.Box(G, new Vector3(1.535f, 4.7f, -0.3f), new Vector3(0.01f, 0.12f, 0.2f), GlowGreen);
		k.Box(P, new Vector3(0f, 4.5f, 1.38f), new Vector3(0.7f, 1.1f, 0.06f), CreamDark);
		var ladderDir = new Vector3(Mathf.Cos(Mathf.Pi / 4f), 0f, Mathf.Sin(Mathf.Pi / 4f));
		Vector3 side = new(-ladderDir.Z, 0f, ladderDir.X);
		Vector3 l0 = ladderDir * 2.75f + new Vector3(0f, 0.3f, 0f), l1 = ladderDir * 1.45f + new Vector3(0f, 3.4f, 0f);
		k.Beam(M, l0 + side * 0.22f, l1 + side * 0.22f, 0.05f, Steel);
		k.Beam(M, l0 - side * 0.22f, l1 - side * 0.22f, 0.05f, Steel);
		for (int i = 1; i < 8; i++)
		{
			Vector3 p = l0.Lerp(l1, i / 8f);
			k.Beam(M, p - side * 0.22f, p + side * 0.22f, 0.035f, Steel);
		}
		return k.Commit();
	}

	// ======================================================================
	//  Hab pod (crew module with windshield, door, skids)
	// ======================================================================

	public static ArrayMesh HabPod()
	{
		var k = new MeshKit(81);
		Vector2[] Octagon(float hw, float y0, float y1, float ch) => new[]
		{
			new Vector2(-hw + ch, y0), new Vector2(hw - ch, y0), new Vector2(hw, y0 + ch), new Vector2(hw, y1 - ch),
			new Vector2(hw - ch, y1), new Vector2(-hw + ch, y1), new Vector2(-hw, y1 - ch), new Vector2(-hw, y0 + ch),
		};

		// body and cab (chamfered prisms along Z)
		k.Extrude(P, Octagon(1.2f, 0.6f, 3.0f, 0.4f), -2.1f, 1.5f, Cream);
		k.Extrude(P, Octagon(1.1f, 0.7f, 2.8f, 0.45f), 1.5f, 2.2f, CreamDark);
		k.Extrude(P, Octagon(1.23f, 0.58f, 3.02f, 0.4f), 1.35f, 1.5f, Gunmetal); // seam band

		// windshield and side windows
		k.Box(Gl, new Vector3(0f, 2.15f, 2.21f), new Vector3(1.5f, 0.75f, 0.02f), GlassDark);
		foreach (float sx in new[] { -1f, 1f })
			k.Box(Gl, new Vector3(sx * 1.105f, 2.15f, 1.85f), new Vector3(0.02f, 0.6f, 0.5f), GlassDark);

		// door on +X with porthole, handle and status lights
		k.Box(P, new Vector3(1.215f, 1.75f, -0.2f), new Vector3(0.03f, 1.6f, 0.95f), CreamDark);
		k.Box(Gl, new Vector3(1.235f, 2.2f, -0.2f), new Vector3(0.01f, 0.32f, 0.32f), GlassDark);
		k.Beam(M, new Vector3(1.25f, 1.5f, 0.15f), new Vector3(1.25f, 1.9f, 0.15f), 0.04f, Steel);
		k.Box(P, new Vector3(1.215f, 1.9f, -1.3f), new Vector3(0.03f, 0.5f, 0.4f), Gunmetal);
		k.Box(G, new Vector3(1.235f, 2.05f, -1.4f), new Vector3(0.01f, 0.06f, 0.08f), GlowRed);
		k.Box(G, new Vector3(1.235f, 1.9f, -1.4f), new Vector3(0.01f, 0.06f, 0.08f), GlowGreen);
		k.Box(G, new Vector3(1.235f, 1.75f, -1.25f), new Vector3(0.01f, 0.25f, 0.06f), GlowBlue);

		// hazard-striped bumper and roof vents
		for (int i = 0; i < 8; i++)
			k.Box(P, new Vector3(-0.95f + i * 0.27f, 0.88f, 2.22f), new Vector3(0.26f, 0.16f, 0.03f), i % 2 == 0 ? Hazard : DarkSteel);
		k.Box(P, new Vector3(0f, 3.07f, -0.4f), new Vector3(1.0f, 0.12f, 1.2f), Gunmetal);
		k.Box(P, new Vector3(0f, 3.14f, -0.4f), new Vector3(0.8f, 0.02f, 1.0f), Hazard);

		// rear thruster pods
		foreach (float sx in new[] { -0.55f, 0.55f })
		{
			k.CylinderBetween(M, new Vector3(sx, 1.9f, -2.05f), new Vector3(sx, 1.9f, -2.6f), 0.42f, 10, Gunmetal);
			k.CylinderBetween(M, new Vector3(sx, 1.9f, -2.6f), new Vector3(sx, 1.9f, -2.9f), 0.28f, 10, DarkSteel, r1: 0.36f);
		}

		// skids and cabling
		foreach (float sx in new[] { -1f, 1f })
		foreach (float sz in new[] { -1f, 1f })
		{
			k.Beam(M, new Vector3(sx * 0.9f, 0.62f, sz * 1.3f), new Vector3(sx * 1.1f, 0.15f, sz * 1.45f), 0.12f, Gunmetal);
			k.Box(P, new Vector3(sx * 1.1f, 0.08f, sz * 1.45f), new Vector3(0.5f, 0.12f, 0.7f), DarkSteel);
		}
		k.Pipe(P, new[] { new Vector3(-1.21f, 2.5f, -1.8f), new Vector3(-1.3f, 2.0f, -1.2f), new Vector3(-1.25f, 1.0f, -0.6f) }, 0.04f, 5, Red);
		k.Pipe(P, new[] { new Vector3(-1.21f, 2.4f, -1.9f), new Vector3(-1.32f, 1.8f, -1.5f), new Vector3(-1.25f, 0.9f, -1.0f) }, 0.035f, 5, Hazard);
		return k.Commit();
	}

	// ======================================================================
	//  Ring scanner (deep-space instrument on a tiered base)
	// ======================================================================

	public static ArrayMesh RingScanner()
	{
		var k = new MeshKit(91);
		k.Cylinder(M, new Vector3(0f, 0.18f, 0f), 2.3f, 2.4f, 0.36f, 20, Gunmetal);
		for (int i = 0; i < 20; i++)
		{
			float a = (i + 0.5f) * Mathf.Tau / 20f;
			k.Box(P, new Vector3(Mathf.Cos(a) * 2.36f, 0.16f, Mathf.Sin(a) * 2.36f), new Vector3(0.1f, 0.2f, 0.32f), DarkSteel, new Vector3(0f, -Mathf.RadToDeg(a), 0f));
			if (i % 4 == 0)
				k.Box(G, new Vector3(Mathf.Cos(a) * 2.1f, 0.37f, Mathf.Sin(a) * 2.1f), new Vector3(0.12f, 0.02f, 0.12f), GlowAmber);
		}
		k.Cylinder(P, new Vector3(0f, 0.46f, 0f), 1.9f, 2.0f, 0.2f, 20, Steel);
		k.Cylinder(M, new Vector3(0f, 0.63f, 0f), 1.3f, 1.4f, 0.16f, 16, Gunmetal);
		k.Cylinder(P, new Vector3(0f, 0.95f, 0f), 0.5f, 0.6f, 0.5f, 10, Gunmetal);

		// ring assembly (vertical, facing +Z)
		var ringC = new Vector3(0f, 2.1f, 0f);
		var vertical = new Vector3(90f, 0f, 0f);
		k.Torus(M, ringC, 1.05f, 0.22f, 24, 8, Gunmetal, vertical);
		k.Torus(P, ringC, 0.78f, 0.07f, 24, 6, Steel, vertical);
		k.Torus(G, ringC, 0.9f, 0.035f, 24, 4, GlowAmber, vertical);
		k.Push(ringC, vertical);
		k.Cylinder(M, Vector3.Zero, 0.42f, 0.42f, 0.22f, 12, DarkSteel);
		k.Cylinder(G, Vector3.Zero, 0.2f, 0.2f, 0.24f, 12, GlowAmber);
		k.Pop();
		foreach (float sx in new[] { -1f, 1f })
		{
			k.Box(P, new Vector3(sx * 1.25f, 1.25f, 0f), new Vector3(0.35f, 1.4f, 0.45f), Gunmetal);
			k.Box(M, new Vector3(sx * 1.2f, 2.1f, 0f), new Vector3(0.3f, 0.5f, 0.55f), Steel);
		}

		// control box + cables
		k.Box(P, new Vector3(1.8f, 0.78f, 1.05f), new Vector3(0.55f, 0.42f, 0.3f), Gunmetal);
		k.Box(G, new Vector3(1.72f, 0.82f, 1.205f), new Vector3(0.3f, 0.16f, 0.01f), GlowScreen);
		for (int i = 0; i < 3; i++)
			k.Box(G, new Vector3(1.66f + i * 0.09f, 0.66f, 1.205f), new Vector3(0.05f, 0.05f, 0.01f), ButtonColors[i + 1]);
		k.Pipe(P, new[] { new Vector3(1.8f, 0.6f, 0.9f), new Vector3(1.5f, 0.5f, 0.5f), new Vector3(1.1f, 1.0f, 0.2f) }, 0.035f, 5, Red);
		k.Pipe(P, new[] { new Vector3(1.9f, 0.6f, 0.9f), new Vector3(1.6f, 0.45f, 0.4f), new Vector3(1.2f, 1.2f, -0.1f) }, 0.03f, 5, DarkSteel);
		return k.Commit();
	}
}

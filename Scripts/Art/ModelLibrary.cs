using Godot;
using static StyleKit;

/// <summary>
/// Procedural low-poly models for Lunar Outpost, built with <see cref="MeshKit"/> in the shared
/// <see cref="StyleKit"/> style. Units are metres; unless noted a model's origin is at ground
/// level under its centre and its front faces +Z.
///
/// Split across files:
///   ModelLibrary.cs            - astronaut, lunar rover, rover wheel
///   ModelLibrary.Structures.cs - geodesic dome, comm tower, drill rig, rocket lander, hab pod, ring scanner
///   ModelLibrary.Props.cs      - hydroponic planter, radish, O2 tank cluster, pressure tank, control station,
///                                solar tracker (+ broken), sample basket, desk console, chair, crystal rock
/// </summary>
public static partial class ModelLibrary
{
	private const Surf P = Surf.Paint;
	private const Surf M = Surf.Metal;
	private const Surf G = Surf.Glow;
	private const Surf Gl = Surf.Glass;

	// ======================================================================
	//  Astronaut (chibi proportions, 1.8 m tall, faces -Z to match PlayerController's Visual)
	// ======================================================================

	public static ArrayMesh Astronaut()
	{
		var k = new MeshKit(11);
		foreach (float sx in new[] { -1f, 1f })
		{
			// boots + soles
			k.Box(P, new Vector3(sx * 0.12f, 0.08f, -0.03f), new Vector3(0.19f, 0.14f, 0.3f), Gunmetal);
			k.Box(P, new Vector3(sx * 0.12f, 0.015f, -0.03f), new Vector3(0.2f, 0.03f, 0.31f), DarkSteel);
			// legs with red knee stripe
			k.Cylinder(P, new Vector3(sx * 0.12f, 0.38f, 0f), 0.1f, 0.11f, 0.46f, 8, SuitWhite);
			k.Cylinder(P, new Vector3(sx * 0.12f, 0.44f, 0f), 0.113f, 0.115f, 0.05f, 8, Red);
			// shoulder, arm, cuff, glove
			k.Sphere(P, new Vector3(sx * 0.3f, 1.1f, 0f), Vector3.One * 0.11f, 4, 8, SuitWhite);
			k.CylinderBetween(P, new Vector3(sx * 0.31f, 1.08f, 0f), new Vector3(sx * 0.35f, 0.76f, -0.03f), 0.085f, 8, SuitWhite);
			k.CylinderBetween(P, new Vector3(sx * 0.347f, 0.79f, -0.027f), new Vector3(sx * 0.352f, 0.74f, -0.03f), 0.093f, 8, Red);
			k.Sphere(P, new Vector3(sx * 0.355f, 0.68f, -0.04f), new Vector3(0.085f, 0.095f, 0.085f), 4, 8, Gunmetal);
			// helmet ear lights
			k.Box(P, new Vector3(sx * 0.33f, 1.5f, 0.02f), new Vector3(0.07f, 0.11f, 0.13f), Gunmetal);
			k.Box(G, new Vector3(sx * 0.368f, 1.52f, 0.02f), new Vector3(0.01f, 0.045f, 0.07f), GlowAmber);
		}

		// belt and pouch
		k.Box(P, new Vector3(0f, 0.64f, 0f), new Vector3(0.46f, 0.11f, 0.31f), Leather);
		k.Box(M, new Vector3(0f, 0.64f, -0.16f), new Vector3(0.09f, 0.07f, 0.02f), Steel);
		k.Box(P, new Vector3(0.17f, 0.61f, -0.165f), new Vector3(0.09f, 0.1f, 0.04f), Leather);

		// torso, chest control panel, stripe, flag patch (astronaut's right = +X)
		k.Box(P, new Vector3(0f, 0.94f, 0f), new Vector3(0.5f, 0.5f, 0.34f), SuitWhite);
		k.Box(P, new Vector3(0f, 1.0f, -0.177f), new Vector3(0.22f, 0.14f, 0.02f), CreamDark);
		k.Box(G, new Vector3(-0.05f, 1.02f, -0.189f), new Vector3(0.035f, 0.035f, 0.01f), GlowRed);
		k.Box(G, new Vector3(0f, 1.02f, -0.189f), new Vector3(0.035f, 0.035f, 0.01f), GlowBlue);
		k.Box(G, new Vector3(0.05f, 1.02f, -0.189f), new Vector3(0.035f, 0.035f, 0.01f), GlowGreen);
		k.Box(P, new Vector3(0f, 0.86f, -0.172f), new Vector3(0.5f, 0.04f, 0.01f), Red);
		k.Box(P, new Vector3(0.405f, 1.02f, 0f), new Vector3(0.01f, 0.07f, 0.1f), EmblemBlue);

		// collar ring, helmet, gold visor
		k.Torus(P, new Vector3(0f, 1.2f, 0f), 0.17f, 0.045f, 12, 6, Red);
		k.Sphere(P, new Vector3(0f, 1.48f, 0.01f), Vector3.One * 0.33f, 6, 12, SuitWhite);
		k.Sphere(M, new Vector3(0f, 1.46f, -0.1f), new Vector3(0.26f, 0.22f, 0.245f), 5, 12, VisorGold);

		// life-support backpack
		k.Box(P, new Vector3(0f, 0.99f, 0.27f), new Vector3(0.44f, 0.56f, 0.2f), new Color(0.76f, 0.76f, 0.74f));
		k.Box(P, new Vector3(0f, 1.28f, 0.27f), new Vector3(0.36f, 0.06f, 0.16f), Gunmetal);
		k.CylinderBetween(M, new Vector3(-0.15f, 0.76f, 0.385f), new Vector3(-0.15f, 1.2f, 0.385f), 0.045f, 8, Steel);
		k.Box(G, new Vector3(0.12f, 1.1f, 0.375f), new Vector3(0.08f, 0.05f, 0.01f), GlowGreen);
		k.Box(G, new Vector3(0.12f, 1.02f, 0.375f), new Vector3(0.08f, 0.03f, 0.01f), GlowAmber);
		return k.Commit();
	}

	// ======================================================================
	//  Lunar rover body (front +Z, origin = chassis centre; wheels are separate)
	// ======================================================================

	public static ArrayMesh RoverBody()
	{
		var k = new MeshKit(21);

		// frame rails and cross members
		foreach (float sx in new[] { -1f, 1f })
			k.Beam(M, new Vector3(sx * 0.62f, -0.03f, -1.45f), new Vector3(sx * 0.62f, -0.03f, 1.45f), 0.08f, Gunmetal);
		foreach (float z in new[] { -1.4f, -0.6f, 0.3f, 1.4f })
			k.Beam(M, new Vector3(-0.62f, -0.03f, z), new Vector3(0.62f, -0.03f, z), 0.07f, Gunmetal);

		// deck with grating
		k.Box(P, new Vector3(0f, 0.03f, 0f), new Vector3(1.18f, 0.04f, 2.7f), TealPale);
		for (int i = 0; i < 10; i++)
			k.Box(M, new Vector3(0f, 0.055f, -0.9f + i * 0.2f), new Vector3(1.0f, 0.012f, 0.03f), Gunmetal);

		// forward chassis (batteries / nav) and aft pallet
		k.Box(P, new Vector3(0f, 0.2f, 1.17f), new Vector3(1.0f, 0.3f, 0.56f), Teal);
		k.Box(P, new Vector3(0f, 0.36f, 1.17f), new Vector3(1.02f, 0.03f, 0.58f), Rust);
		k.Box(G, new Vector3(0.3f, 0.25f, 1.455f), new Vector3(0.12f, 0.05f, 0.01f), GlowGreen);
		k.Box(G, new Vector3(0.15f, 0.25f, 1.455f), new Vector3(0.05f, 0.05f, 0.01f), GlowRed);
		k.Box(P, new Vector3(0f, 0.22f, -1.15f), new Vector3(1.1f, 0.34f, 0.56f), Cream);
		k.Box(P, new Vector3(0f, 0.405f, -1.15f), new Vector3(1.12f, 0.03f, 0.58f), Rust);
		k.Box(P, new Vector3(0.28f, 0.47f, -1.15f), new Vector3(0.32f, 0.1f, 0.3f), Gunmetal);
		k.Box(P, new Vector3(-0.25f, 0.5f, -1.2f), new Vector3(0.28f, 0.16f, 0.26f), Hazard);

		// fenders over the four wheels (wheel centres at x +-0.9, y -0.35, z +-1.15)
		foreach (float sx in new[] { -1f, 1f })
		foreach (float sz in new[] { -1f, 1f })
		{
			var wc = new Vector3(sx * 0.9f, -0.35f, sz * 1.15f);
			k.ArcPlate(P, wc, 0.5f, 0.34f, 0.03f, 15f, 165f, 7, TealPale);
			k.Beam(M, new Vector3(sx * 0.6f, 0.0f, sz * 1.15f), new Vector3(sx * 0.82f, 0.15f, sz * 1.15f), 0.04f, Gunmetal);
		}

		// two seats: pan, frame, slanted back with straps
		foreach (float sx in new[] { -0.32f, 0.32f })
		{
			k.Box(P, new Vector3(sx, 0.21f, -0.15f), new Vector3(0.46f, 0.05f, 0.46f), TealDark);
			k.Beam(M, new Vector3(sx - 0.22f, 0.06f, 0.06f), new Vector3(sx - 0.22f, 0.2f, 0.06f), 0.03f, Steel);
			k.Beam(M, new Vector3(sx + 0.22f, 0.06f, 0.06f), new Vector3(sx + 0.22f, 0.2f, 0.06f), 0.03f, Steel);
			k.Push(new Vector3(sx, 0.47f, -0.41f), new Vector3(-12f, 0f, 0f));
			k.Box(P, Vector3.Zero, new Vector3(0.44f, 0.5f, 0.03f), TealDark);
			for (int i = 0; i < 4; i++)
				k.Box(P, new Vector3(0f, -0.18f + i * 0.12f, 0.02f), new Vector3(0.4f, 0.03f, 0.012f), CreamDark);
			k.Beam(M, new Vector3(-0.23f, -0.26f, 0f), new Vector3(-0.23f, 0.26f, 0f), 0.03f, Steel);
			k.Beam(M, new Vector3(0.23f, -0.26f, 0f), new Vector3(0.23f, 0.26f, 0f), 0.03f, Steel);
			k.Beam(M, new Vector3(-0.23f, 0.26f, 0f), new Vector3(0.23f, 0.26f, 0f), 0.03f, Steel);
			k.Pop();
		}

		// control console on its post, hand controller between the seats
		k.Beam(M, new Vector3(0f, 0.05f, 0.3f), new Vector3(0f, 0.55f, 0.42f), 0.05f, Steel);
		k.Box(P, new Vector3(0f, 0.62f, 0.45f), new Vector3(0.3f, 0.22f, 0.14f), Cream);
		k.Box(G, new Vector3(0f, 0.64f, 0.377f), new Vector3(0.2f, 0.1f, 0.01f), GlowScreen);
		k.Beam(M, new Vector3(0f, 0.22f, 0.12f), new Vector3(0f, 0.42f, 0.12f), 0.03f, DarkSteel);
		k.Box(P, new Vector3(0f, 0.45f, 0.12f), new Vector3(0.06f, 0.07f, 0.06f), DarkSteel);

		// high-gain umbrella antenna (brass dish, rust ribs)
		var mastBase = new Vector3(-0.45f, 0.05f, 1.05f);
		var mastTop = new Vector3(-0.45f, 1.2f, 1.05f);
		k.Beam(M, mastBase, mastTop, 0.04f, Gunmetal);
		k.Push(mastTop + new Vector3(0f, 0.05f, 0f), new Vector3(40f, 0f, 0f));
		k.Cylinder(M, new Vector3(0f, 0.09f, 0f), 0.05f, 0.48f, 0.18f, 12, Brass, caps: false);
		k.Cylinder(M, new Vector3(0f, 0.09f, 0f), 0.05f, 0.48f, 0.18f, 12, Brass, caps: false, inward: true);
		for (int i = 0; i < 8; i++)
		{
			float a = i * Mathf.Tau / 8f;
			k.Beam(M, Vector3.Zero, new Vector3(Mathf.Cos(a) * 0.48f, 0.18f, Mathf.Sin(a) * 0.48f), 0.015f, Rust);
		}
		k.Beam(M, Vector3.Zero, new Vector3(0f, 0.42f, 0f), 0.02f, Gunmetal);
		k.Sphere(P, new Vector3(0f, 0.44f, 0f), Vector3.One * 0.04f, 3, 6, Brass);
		k.Pop();

		// TV camera and low-gain antenna
		k.Beam(M, new Vector3(0.3f, 0.38f, 1.25f), new Vector3(0.3f, 0.75f, 1.25f), 0.04f, Gunmetal);
		k.Box(P, new Vector3(0.3f, 0.82f, 1.25f), new Vector3(0.14f, 0.14f, 0.22f), Brass);
		k.CylinderBetween(P, new Vector3(0.3f, 0.82f, 1.36f), new Vector3(0.3f, 0.82f, 1.43f), 0.05f, 10, DarkSteel);
		k.Beam(M, new Vector3(0.45f, 0.38f, 1.0f), new Vector3(0.45f, 1.2f, 1.0f), 0.02f, Steel);
		k.Cylinder(P, new Vector3(0.45f, 1.24f, 1.0f), 0.06f, 0f, 0.1f, 8, Brass);

		// cable run
		k.Pipe(P, new[] { new Vector3(0f, 0.55f, 0.42f), new Vector3(-0.2f, 0.42f, 0.62f), new Vector3(-0.42f, 0.38f, 0.95f) }, 0.015f, 5, DarkSteel);
		return k.Commit();
	}

	/// <summary>Rover wheel with chunky tread; axle along X, hub cap on the +X side (rotate 180° about Y for right-side wheels).</summary>
	public static ArrayMesh RoverWheel(float radius = 0.41f, float width = 0.23f)
	{
		var k = new MeshKit(31);
		k.Push(Vector3.Zero, new Vector3(0f, 0f, 90f)); // local Y (cylinder axis) -> world X; local -Y -> world +X
		k.Cylinder(P, Vector3.Zero, radius * 0.9f, radius * 0.9f, width, 16, Rubber);
		for (int i = 0; i < 16; i++)
		{
			float a = i * Mathf.Tau / 16f;
			k.Push(new Vector3(Mathf.Cos(a) * radius * 0.93f, 0f, Mathf.Sin(a) * radius * 0.93f), new Vector3(0f, -Mathf.RadToDeg(a), 0f));
			k.Box(P, Vector3.Zero, new Vector3(0.09f, width * 1.04f, 0.11f), new Color(0.17f, 0.16f, 0.15f));
			k.Pop();
		}
		k.Cylinder(M, Vector3.Zero, 0.22f, 0.22f, width + 0.03f, 10, Gunmetal);
		float capY = -(width / 2f + 0.035f);
		k.Cylinder(P, new Vector3(0f, capY, 0f), 0.16f, 0.13f, 0.04f, 10, TealPale);
		for (int i = 0; i < 5; i++)
		{
			float a = i * Mathf.Tau / 5f;
			k.Box(M, new Vector3(Mathf.Cos(a) * 0.09f, capY - 0.025f, Mathf.Sin(a) * 0.09f), new Vector3(0.025f, 0.02f, 0.025f), Steel);
		}
		k.Pop();
		return k.Commit();
	}
}

using Godot;
using System.Collections.Generic;

// Remote Drill Site: an outdoor core-drilling rig far out on the surface (default ~200 m south of
// the base), linked to the Rover Bay by a marked road. Walking there costs a lot of suit O2 (no
// pressurized shelter at the site), while the rover is fast and feeds the suit, so the intended
// route is: Rover Bay airlock -> rover -> drive south -> drill console -> drive back.
public partial class EnvironmentBuilder
{
	private void BuildDrillSite(Vector3 roverPad)
	{
		Vector3 site = new(DrillSitePosition.X, 0f, DrillSitePosition.Z);
		const float padHalf = 13f;

		var root = new Node3D { Name = "DrillSite", Position = site };
		_base.AddChild(root);

		var steel = Mat(new Color(0.6f, 0.6f, 0.64f), 0.7f, 0.35f);
		var orange = Mat(new Color(0.95f, 0.5f, 0.12f), 0.3f, 0.5f);
		var padMat = Mat(new Color(0.33f, 0.33f, 0.35f), 0f, 0.9f);
		var paint = Mat(new Color(0.95f, 0.8f, 0.15f));

		// ---- Work pad and rover parking spot (north edge, where the road arrives) ----
		Box(root, "Pad", new Vector3(padHalf * 2f, 0.1f, padHalf * 2f), Vector3.Zero, padMat, collision: true);
		Box(root, "ParkLineL", new Vector3(0.2f, 0.02f, 6f), new Vector3(-6f, 0.06f, -8.5f), paint);
		Box(root, "ParkLineR", new Vector3(0.2f, 0.02f, 6f), new Vector3(-1f, 0.06f, -8.5f), paint);

		// ---- Drill rig: stacked cylinders inside a four-leg derrick ----
		var rig = new Node3D { Name = "Rig", Position = new Vector3(0, 0, 4f) };
		root.AddChild(rig);
		Cyl(rig, "Base", 2.6f, 1.0f, new Vector3(0, 0.5f, 0), Vector3.Zero, steel, sides: 24, collision: true);
		Cyl(rig, "Housing", 1.2f, 7f, new Vector3(0, 4.5f, 0), Vector3.Zero, orange, sides: 20, collision: true);
		Cyl(rig, "Motor", 1.7f, 2f, new Vector3(0, 9f, 0), Vector3.Zero, steel, sides: 20, collision: true);
		Cyl(rig, "Mast", 0.3f, 6f, new Vector3(0, 13f, 0), Vector3.Zero, steel);
		Cyl(rig, "Borehole", 0.5f, 0.05f, new Vector3(0, 1.03f, 0), Vector3.Zero, _dark);
		foreach (float sx in new[] { -1f, 1f })
		foreach (float sz in new[] { -1f, 1f })
			Box(rig, $"Leg{(sx > 0 ? "E" : "W")}{(sz > 0 ? "S" : "N")}", new Vector3(0.25f, 10.5f, 0.25f), new Vector3(sx * 2.6f, 5.1f, sz * 2.6f), steel,
				collision: true, rotationDegrees: new Vector3(sz * -12f, 0, sx * 12f));
		rig.AddChild(new CsgTorus3D { Name = "WorkPlatform", InnerRadius = 1.6f, OuterRadius = 2.6f, Sides = 24, RingSides = 4, Position = new Vector3(0, 7.5f, 0), Scale = new Vector3(1, 0.15f, 1), Material = steel });

		// ---- Equipment: coolant tanks, core sample rack ----
		Cyl(root, "CoolantTankA", 0.8f, 3.2f, new Vector3(-8f, 0.85f, 4f), new Vector3(90, 0, 0), Mat(new Color(0.85f, 0.87f, 0.9f), 0.4f, 0.4f), collision: true);
		Cyl(root, "CoolantTankB", 0.8f, 3.2f, new Vector3(-8f, 0.85f, 6.2f), new Vector3(90, 0, 0), Mat(new Color(0.85f, 0.87f, 0.9f), 0.4f, 0.4f), collision: true);
		Box(root, "SampleRack", new Vector3(2.2f, 1.0f, 1.2f), new Vector3(7f, 0.5f, 5f), _trim, collision: true);
		for (int i = 0; i < 5; i++)
			Cyl(root, $"CoreSample{i}", 0.12f, 1.8f, new Vector3(7f, 1.12f, 4.6f + i * 0.2f), new Vector3(0, 0, 90), Mat(new Color(0.45f, 0.43f, 0.4f)));

		// ---- Mission console (outdoors, facing the parking spot) + status screen ----
		Console(root, "DrillConsole", new Vector3(3f, 0.6f, -3f), Zone.DrillArea, "Core Sample Drill",
			"Route coolant through the drill head before it overheats.", Tool.Drill,
			oxygen: 0f, power: -10f, science: 25, canisters: 0, new Color(1f, 0.55f, 0.15f));
		var stand = new Node3D { Name = "ScreenStand", Position = new Vector3(5.6f, 0, -3f) };
		root.AddChild(stand);
		Cyl(stand, "Post", 0.06f, 1.5f, new Vector3(0, 0.75f, 0), Vector3.Zero, _trim);
		Screen(stand, "DrillScreen", new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.Pi, 0)), new Vector3(0, 2.0f, 0)), new Vector2(1.6f, 0.9f),
			"CORE DRILL\nDEPTH: 12.4 M\nBIT TEMP: NOMINAL\nSCIENCE: {SCI} PTS", PhosphorGreen);

		// ---- Navigation: tall beacon visible from the base, floodlights, big sign ----
		Cyl(root, "BeaconMast", 0.15f, 16f, new Vector3(-9f, 8f, -9f), Vector3.Zero, _trim);
		root.AddChild(new CsgSphere3D { Name = "BeaconLamp", Radius = 0.45f, Position = new Vector3(-9f, 16.3f, -9f), Material = Emissive(new Color(1f, 0.5f, 0.1f), 6f) });
		Omni(root, "BeaconLight", new Vector3(-9f, 16f, -9f), new Color(1f, 0.55f, 0.15f), 2.5f, 30f);
		foreach (float x in new[] { -11f, 11f })
		{
			Cyl(root, $"FloodPole{(x < 0 ? "W" : "E")}", 0.08f, 5f, new Vector3(x, 2.5f, -11f), Vector3.Zero, _trim);
			Box(root, $"FloodHead{(x < 0 ? "W" : "E")}", new Vector3(0.6f, 0.25f, 0.4f), new Vector3(x, 5f, -10.8f), _amber, rotationDegrees: new Vector3(-30, 0, 0));
		}
		Label(root, "Sign", "DRILL SITE", new Vector3(0, 19f, 4f), Vector3.Zero, 0.05f, 96, new Color(1f, 0.7f, 0.35f), billboard: true, layers: 2);

		// Field hab placeholder (ArtReplacer swaps it for the HabPod model).
		Box(root, "FieldHab", new Vector3(2.4f, 2.6f, 4.2f), new Vector3(9.5f, 1.3f, -6f), Mat(new Color(0.6f, 0.6f, 0.62f)), collision: true);

		AddClearZone(site, Vector2.One * padHalf * 2f);

		BuildRoverRoad(new Vector3(roverPad.X, 0f, roverPad.Z + 7f), new Vector3(site.X, 0f, site.Z - padHalf));
	}

	/// <summary>Marked road between the Rover Bay and the Drill Site: dashed centre line, lit posts, distance signs.</summary>
	private void BuildRoverRoad(Vector3 from, Vector3 to)
	{
		var road = new Node3D { Name = "RoverRoad" };
		_base.AddChild(road);

		Vector3 d = to - from;
		d.Y = 0;
		float len = d.Length();
		Vector3 dir = d / len;
		Vector3 side = new(-dir.Z, 0, dir.X);
		float yaw = YawFacing(dir);
		var paint = Mat(new Color(0.95f, 0.8f, 0.15f));
		var postMat = Mat(new Color(0.85f, 0.85f, 0.88f));
		var lamp = Emissive(new Color(1f, 0.6f, 0.15f), 4f);

		// Dashed centre line (3 m dashes every 8 m)
		int dash = 0;
		for (float t = 2f; t < len - 1.5f; t += 8f)
		{
			Vector3 p = from + dir * t;
			Box(road, $"Dash{dash++}", new Vector3(0.3f, 0.02f, 3f), new Vector3(p.X, 0.01f, p.Z), paint, rotationDegrees: new Vector3(0, yaw, 0));
		}

		// Reflector posts on both edges every 15 m, with glowing caps for night-side driving.
		int post = 0;
		for (float t = 5f; t < len; t += 15f)
		{
			foreach (float s in new[] { -4.5f, 4.5f })
			{
				Vector3 p = from + dir * t + side * s;
				Cyl(road, $"Post{post}", 0.08f, 1.4f, new Vector3(p.X, 0.7f, p.Z), Vector3.Zero, postMat);
				Box(road, $"PostLamp{post++}", new Vector3(0.2f, 0.12f, 0.2f), new Vector3(p.X, 1.45f, p.Z), lamp);
			}
		}

		// Distance signs every 50 m, facing drivers heading out toward the site.
		for (float t = 25f; t < len - 10f; t += 50f)
		{
			Vector3 p = from + dir * t + side * 5.5f;
			Label(road, $"Distance{t:0}", $"DRILL SITE  {len - t:0} M", new Vector3(p.X, 2.2f, p.Z), new Vector3(0, YawFacing(-dir), 0), 0.012f, 96, new Color(1f, 0.85f, 0.4f), outline: 16);
		}

		// Keep rocks and craters off the road (axis-aligned bounds of the road corridor).
		var a2 = new Vector2(from.X, from.Z);
		var b2 = new Vector2(to.X, to.Z);
		var min = new Vector2(Mathf.Min(a2.X, b2.X), Mathf.Min(a2.Y, b2.Y)) - Vector2.One * 6f;
		var max = new Vector2(Mathf.Max(a2.X, b2.X), Mathf.Max(a2.Y, b2.Y)) + Vector2.One * 6f;
		_clearZones.Add(new Rect2(min, max - min));
	}
}

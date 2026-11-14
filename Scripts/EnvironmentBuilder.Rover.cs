using Godot;

// Lunar rover (Apollo LRV scale): drivable VehicleBody3D or a static CSG scale reference.
public partial class EnvironmentBuilder
{
	private const string RoverName = "LunarRover";
	private const float WheelRadius = 0.41f;
	private const float WheelWidth = 0.23f;
	private const float TrackHalf = 0.9f;      // 1.8 m track
	private const float WheelbaseHalf = 1.15f; // 2.3 m wheelbase
	private const float WheelDrop = 0.35f;     // chassis centre -> wheel centre at rest

	private void BuildRover(Node root)
	{
		RemoveIfExists(root, RoverName);
		if (Rover == RoverBuildMode.None)
			return;

		bool drivable = Rover == RoverBuildMode.Drivable;
		float chassisY = WheelRadius + WheelDrop; // wheels resting on y = 0

		Node3D rover;
		if (drivable)
		{
			rover = new VehicleBody3D
			{
				Mass = 300f,
				GravityScale = 1.62f / 9.8f, // lunar gravity (project gravity is Earth's 9.8)
				CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom,
				CenterOfMass = new Vector3(0, -0.6f, 0), // very low CoM: hard to flip in 1/6 g
				CanSleep = false,
			};
		}
		else
		{
			rover = new Node3D();
		}
		rover.Name = RoverName;
		rover.Position = new Vector3(RoverPosition.X, chassisY + (drivable ? 0.05f : 0f), RoverPosition.Z);
		rover.RotationDegrees = new Vector3(0, RoverYawDegrees, 0);
		root.AddChild(rover);

		BuildRoverBody(rover);
		BuildWheels(rover, drivable);

		if (drivable)
		{
			rover.AddChild(new CollisionShape3D
			{
				Name = "CollisionShape3D",
				Position = new Vector3(0, 0.1f, 0),
				Shape = new BoxShape3D { Size = new Vector3(1.5f, 0.45f, 3.0f) },
			});
			// Driver seat: the astronaut's Visual is parented here while driving. Turned 180 degrees so the
			// astronaut (who faces -Z) looks down the rover's nose (+Z). Move it in the editor to fit a new rover model.
			rover.AddChild(new Marker3D { Name = "Seat", Position = new Vector3(0.33f, 0.25f, -0.15f), RotationDegrees = new Vector3(0f, 180f, 0f) });
			rover.AddChild(new Marker3D { Name = "ExitPoint", Position = new Vector3(2.0f, -0.6f, 0f) });

			// Chase camera behind the rover (rover front is +Z, so the pivot is turned 180 deg).
			var pivot = new Node3D { Name = "CameraPivot", Position = new Vector3(0, 1.2f, 0), RotationDegrees = new Vector3(0, 180, 0) };
			var arm = new SpringArm3D
			{
				Name = "SpringArm3D",
				RotationDegrees = new Vector3(-15, 0, 0),
				SpringLength = 7f,
				Margin = 0.2f,
				Shape = new SphereShape3D { Radius = 0.25f },
			};
			arm.AddChild(new Camera3D { Name = "Camera3D", Fov = 70f });
			pivot.AddChild(arm);
			rover.AddChild(pivot);
		}

		SetOwnerRecursive(rover, root);
		if (drivable)
			QueueScript(rover, RoverScriptPath);
	}

	private static void BuildRoverBody(Node3D rover)
	{
		var frame = Mat(new Color(0.7f, 0.72f, 0.75f), metallic: 0.7f, roughness: 0.35f);
		var seat = Mat(new Color(0.15f, 0.3f, 0.7f));
		var foil = Mat(new Color(0.85f, 0.65f, 0.25f), metallic: 0.9f, roughness: 0.25f);
		var dark = Mat(new Color(0.12f, 0.12f, 0.14f));
		var white = Mat(new Color(0.92f, 0.92f, 0.92f));

		// +Z = front
		Box(rover, "Chassis", new Vector3(1.3f, 0.12f, 2.9f), Vector3.Zero, frame);
		Box(rover, "SeatLeft", new Vector3(0.5f, 0.08f, 0.5f), new Vector3(0.33f, 0.2f, -0.15f), seat);
		Box(rover, "SeatRight", new Vector3(0.5f, 0.08f, 0.5f), new Vector3(-0.33f, 0.2f, -0.15f), seat);
		Box(rover, "SeatbackLeft", new Vector3(0.5f, 0.5f, 0.06f), new Vector3(0.33f, 0.45f, -0.42f), seat);
		Box(rover, "SeatbackRight", new Vector3(0.5f, 0.5f, 0.06f), new Vector3(-0.33f, 0.45f, -0.42f), seat);
		Cyl(rover, "ControlPost", 0.03f, 0.55f, new Vector3(0, 0.33f, 0.3f), Vector3.Zero, frame);
		Box(rover, "DisplayConsole", new Vector3(0.45f, 0.28f, 0.1f), new Vector3(0, 0.66f, 0.42f), dark);
		Box(rover, "ForwardChassisBox", new Vector3(1.0f, 0.25f, 0.5f), new Vector3(0, 0.18f, 1.15f), foil);
		Box(rover, "AftCargoPallet", new Vector3(1.1f, 0.35f, 0.6f), new Vector3(0, 0.24f, -1.1f), frame);

		foreach (float x in new[] { TrackHalf, -TrackHalf })
		foreach (float z in new[] { WheelbaseHalf, -WheelbaseHalf })
			Box(rover, $"Fender{(x > 0 ? "L" : "R")}{(z > 0 ? "F" : "R")}", new Vector3(WheelWidth + 0.1f, 0.04f, 0.95f), new Vector3(x, 0.14f, z), foil);

		// High-gain antenna (umbrella dish) and low-gain whip
		Cyl(rover, "HighGainMast", 0.025f, 1.1f, new Vector3(-0.45f, 0.7f, 1.1f), Vector3.Zero, frame);
		// Mid-gray, not white: under the 6x sun a white dish blooms into a blob in the chase-cam view.
		Cyl(rover, "HighGainDish", 0.45f, 0.12f, new Vector3(-0.45f, 1.3f, 1.1f), new Vector3(-55, 0, 0), Mat(new Color(0.5f, 0.5f, 0.52f), 0.3f, 0.6f), cone: true);
		Cyl(rover, "LowGainAntenna", 0.015f, 0.9f, new Vector3(0.45f, 0.6f, 1.1f), Vector3.Zero, frame);
		Box(rover, "TvCamera", new Vector3(0.15f, 0.15f, 0.25f), new Vector3(0.25f, 0.85f, 1.2f), white);
	}

	private static void BuildWheels(Node3D rover, bool drivable)
	{
		var tire = Mat(new Color(0.28f, 0.28f, 0.3f), metallic: 0.5f, roughness: 0.7f);
		var hub = Mat(new Color(0.75f, 0.75f, 0.78f), metallic: 0.8f, roughness: 0.3f);

		foreach (float x in new[] { TrackHalf, -TrackHalf })
		foreach (float z in new[] { WheelbaseHalf, -WheelbaseHalf })
		{
			string name = $"Wheel{(z > 0 ? "Front" : "Rear")}{(x > 0 ? "Left" : "Right")}";
			Node3D holder;
			if (drivable)
			{
				// Suspension attaches 0.1 m below the chassis centre; rest length drops the wheel further.
				holder = new VehicleWheel3D
				{
					Name = name,
					Position = new Vector3(x, -0.1f, z),
					UseAsTraction = true,          // the LRV had a motor in every hub
					UseAsSteering = z > 0,         // front-wheel steering
					WheelRadius = WheelRadius,
					WheelRestLength = 0.25f,
					SuspensionTravel = 0.25f,
					SuspensionStiffness = 12f,
					SuspensionMaxForce = 6000f,
					DampingCompression = 0.9f,
					DampingRelaxation = 1.2f,
					WheelFrictionSlip = 3f,        // loose regolith: modest grip
					WheelRollInfluence = 0.1f,
				};
			}
			else
			{
				holder = new Node3D { Name = name, Position = new Vector3(x, -WheelDrop, z) };
			}
			rover.AddChild(holder);

			// Cylinders stand on Y by default: rotate 90 degrees around Z so the axle points along X.
			Cyl(holder, "Tire", WheelRadius, WheelWidth, Vector3.Zero, new Vector3(0, 0, 90), tire);
			Cyl(holder, "Hub", 0.16f, WheelWidth + 0.04f, Vector3.Zero, new Vector3(0, 0, 90), hub);
		}
	}
}

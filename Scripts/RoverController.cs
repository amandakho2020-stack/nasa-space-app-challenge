using Godot;

/// <summary>
/// Drivable lunar rover (Apollo LRV-style graybox). Attach to the VehicleBody3D root.
/// Built automatically by EnvironmentBuilder; expected children:
///   CollisionShape3D, 4x VehicleWheel3D (each with a CSGCylinder3D wheel mesh),
///   SeatPoint (Marker3D), ExitPoint (Marker3D),
///   CameraPivot (Node3D) / SpringArm3D / Camera3D
///
/// Controls (all relative to the rover's own facing):
///   W      = drive forward
///   S      = brake while rolling forward, then reverse once (almost) stopped
///   A / D  = steer left / right (reversed travel steers like a real car)
///   Space  = handbrake
///   Mouse  = look around; the camera swings back behind the rover after a moment
///   E      = get out (only when nearly stopped)
///
/// VehicleBody3D conventions used here (Godot 4):
///   - The vehicle's FORWARD axis is local +Z: a positive EngineForce pushes the body toward +Z.
///   - A positive Steering angle turns the front wheels toward local +X, which (facing +Z, Y up)
///     is the driver's LEFT. So "A = left" means Steering > 0.
/// The rover's visual front (dash, antenna, forward box) is built on +Z to match.
/// While driving, the rover's life support feeds the suit: O2 does not drain (see GameManager).
/// </summary>
public partial class RoverController : VehicleBody3D, IInteractable
{
	[ExportGroup("Drive")]
	/// <summary>Engine force per traction wheel when accelerating forward (N). x4 wheels / 300 kg = ~3 m/s².
	/// Much more than this pops wheelies and rolls the rover in 1/6 g.</summary>
	[Export] public float ForwardEngineForce = 220f;
	[Export] public float ReverseEngineForce = 140f;
	/// <summary>Top speed (m/s). The real LRV managed 3.6 m/s; the game rover is tuned faster so long hauls are quick.</summary>
	[Export] public float MaxForwardSpeed = 12f;
	[Export] public float MaxReverseSpeed = 4f;
	[Export] public float BrakeForce = 8f;
	[Export] public float HandbrakeForce = 12f;
	/// <summary>Light drag when coasting (no throttle), so the rover doesn't roll forever.</summary>
	[Export] public float CoastBrakeForce = 0.4f;
	/// <summary>Below this speed (m/s) pressing S starts reversing instead of braking.</summary>
	[Export] public float ReverseThreshold = 0.6f;

	[ExportGroup("Steering")]
	[Export] public float MaxSteerRadians = 0.5f;
	/// <summary>Fraction of MaxSteerRadians available at top speed (prevents rollovers in 1/6 g).</summary>
	[Export(PropertyHint.Range, "0.1,1,0.05")] public float HighSpeedSteerFactor = 0.35f;
	[Export] public float SteerSpeed = 2.5f;

	[ExportGroup("Camera")]
	[Export] public float MouseSensitivity = 0.003f;
	/// <summary>Seconds without mouse input before the chase camera swings back behind the rover.</summary>
	[Export] public float CameraRecenterDelay = 1.2f;
	[Export] public float CameraRecenterSpeed = 2.5f;
	[Export] public float DefaultCameraPitchDegrees = -15f;

	[ExportGroup("Stability (arcade assist)")]
	/// <summary>Torque that pulls the chassis back upright (per kg). 0 = pure simulation.</summary>
	[Export] public float UprightStiffness = 6f;
	/// <summary>Damps roll/pitch wobble (per kg).</summary>
	[Export] public float UprightDamping = 2.5f;
	/// <summary>If the rover stays tipped past this (up.Y below it) for RecoverDelay seconds, it is set back on its wheels.</summary>
	[Export] public float TippedThreshold = 0.35f;
	[Export] public float RecoverDelay = 1.5f;

	[ExportGroup("Seat (astronaut stays visible)")]
	/// <summary>Seated pose of the astronaut relative to the Seat marker (lower it so the hips sit on the seat).</summary>
	[Export] public Vector3 SeatedOffset = new(0f, -1.0f, 0.05f);
	/// <summary>Seated tilt (e.g. lean back a few degrees on X).</summary>
	[Export] public Vector3 SeatedRotationDegrees = new(-10f, 0f, 0f);

	[ExportGroup("Camera transitions")]
	/// <summary>Seconds for the player-camera to rover-camera blend when boarding.</summary>
	[Export] public float BoardBlendSeconds = 1.0f;
	/// <summary>Seconds for the rover-camera to player-camera blend when getting out.</summary>
	[Export] public float ExitBlendSeconds = 0.8f;
	/// <summary>How tightly the chase camera follows the rover position (higher = stiffer).</summary>
	[Export] public float ChasePositionStiffness = 10f;
	/// <summary>How quickly the chase camera swings round when the rover turns (lower = lazier, more cinematic).</summary>
	[Export] public float ChaseYawStiffness = 4f;

	[ExportGroup("Exit")]
	/// <summary>The driver can only get out below this speed (m/s).</summary>
	[Export] public float MaxExitSpeed = 2f;

	private PlayerController _driver;
	private Node3D _seat;
	private Node3D _exit;
	private Node3D _cameraPivot;
	private SpringArm3D _springArm;
	private Camera3D _camera;

	// Camera orbit relative to the rover. Yaw = PI puts the camera behind the rover (rover front is +Z).
	private const float BehindYaw = Mathf.Pi;
	private float _orbitYaw = BehindYaw;
	private float _orbitPitch;
	private float _timeSinceMouse;
	private float _tippedTime;
	private Vector3 _pivotOffset = new(0f, 1.2f, 0f);
	private Vector3 _followPos;
	private float _followYaw;

	public bool IsOccupied => _driver != null;

	/// <summary>Signed speed along the rover's forward axis (+Z), in m/s. Positive = moving forward.</summary>
	public float ForwardSpeed => LinearVelocity.Dot(GlobalBasis.Z);

	public Vector3 InteractPosition => GlobalPosition;

	public override void _Ready()
	{
		AddToGroup(PlayerController.InteractableGroup);
		_seat = GetNodeOrNull<Node3D>("Seat") ?? GetNodeOrNull<Node3D>("SeatPoint");
		_exit = GetNodeOrNull<Node3D>("ExitPoint");
		_cameraPivot = GetNodeOrNull<Node3D>("CameraPivot");
		_springArm = GetNodeOrNull<SpringArm3D>("CameraPivot/SpringArm3D");
		_camera = GetNodeOrNull<Camera3D>("CameraPivot/SpringArm3D/Camera3D");
		_springArm?.AddExcludedObject(GetRid());
		if (_springArm != null)
		{
			_springArm.CollisionMask = 1u | 8u; // world + camera_blocker
			_springArm.Margin = 0.3f;
		}
		if (_camera != null)
			_camera.Near = 0.05f;
		if (_cameraPivot != null)
		{
			// Detach the chase pivot from the rover transform so it can follow with lag (see UpdateChaseFollow).
			_pivotOffset = _cameraPivot.Position;
			_cameraPivot.TopLevel = true;
			SnapChaseCamera();
		}

		ResetCamera();
		CanSleep = false;
		Brake = HandbrakeForce; // parked
	}

	public string GetPrompt() => IsOccupied ? "" : "[E] Drive Lunar Rover";

	public void Interact(PlayerController player)
	{
		if (!IsOccupied && player != null)
			Board(player);
	}

	// ======================================================================
	//  Boarding
	// ======================================================================

	private void Board(PlayerController player)
	{
		_driver = player;
		var seatedPose = new Transform3D(Basis.FromEuler(SeatedRotationDegrees * (Mathf.Pi / 180f)), SeatedOffset);
		if (_seat != null && _seat.Name == "SeatPoint")
			seatedPose = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero) * seatedPose; // old marker had no 180 turn
		Camera3D fromCamera = player.PlayerCamera;
		player.EnterVehicle(_seat, seatedPose);
		ResetCamera(); // always start looking down the rover's nose
		SnapChaseCamera();
		CameraBlender.Blend(fromCamera, _camera, BoardBlendSeconds);
		Brake = 0f;
		GameManager.Instance?.SetActiveVehicle(this);
		GameManager.Instance?.PostMessage("Rover engaged - life support connected. [W] forward  [S] brake/reverse  [A/D] steer  [Space] handbrake  [E] exit");
	}

	private void Exit()
	{
		if (Mathf.Abs(ForwardSpeed) > MaxExitSpeed)
		{
			GameManager.Instance?.PostMessage("Slow down to get out of the rover.");
			return;
		}

		PlayerController driver = _driver;
		_driver = null;
		EngineForce = 0f;
		Steering = 0f;
		Brake = HandbrakeForce;
		GameManager.Instance?.SetActiveVehicle(null);

		Vector3 exitPos = _exit != null ? _exit.GlobalPosition : GlobalPosition + GlobalBasis.X * 2f;
		exitPos.Y = Mathf.Max(exitPos.Y, 0.2f);
		// Hand the camera back with the orbit camera looking the way the rover faces (yaw + PI: the
		// player camera looks along its pivot's -Z), so the blend is a short, natural move.
		driver.ExitVehicle(exitPos, _camera, GlobalRotation.Y + Mathf.Pi, ExitBlendSeconds);
	}

	public override void _ExitTree()
	{
		if (IsOccupied)
			GameManager.Instance?.SetActiveVehicle(null);
	}

	// ======================================================================
	//  Input
	// ======================================================================

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsOccupied)
			return;
		var gm = GameManager.Instance;
		if (gm != null && gm.InputLocked)
			return;

		if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_orbitYaw -= motion.Relative.X * MouseSensitivity;
			_orbitPitch = Mathf.Clamp(_orbitPitch - motion.Relative.Y * MouseSensitivity, Mathf.DegToRad(-60f), Mathf.DegToRad(10f));
			_timeSinceMouse = 0f;
			ApplyCamera();
		}
		else if (@event.IsActionPressed("interact"))
		{
			Exit();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
			GetViewport().SetInputAsHandled();
		}
	}

	// ======================================================================
	//  Driving
	// ======================================================================

	public override void _PhysicsProcess(double delta)
	{
		if (!IsOccupied)
		{
			EngineForce = 0f;
			Steering = 0f;
			Brake = HandbrakeForce;
			SnapChaseCamera(); // keep the detached chase pivot on the parked rover
			return;
		}

		float dt = (float)delta;
		var gm = GameManager.Instance;
		bool locked = gm != null && gm.InputLocked;

		// Raw inputs: +1 = W (forward), -1 = S; +1 = A (left), -1 = D (right).
		float throttle = locked ? 0f : Input.GetAxis("move_back", "move_forward");
		float steerInput = locked ? 0f : Input.GetAxis("move_right", "move_left");
		bool handbrake = !locked && Input.IsActionPressed("jump");

		float speed = ForwardSpeed;
		float engine = 0f;
		float brake = 0f;

		if (handbrake)
		{
			brake = HandbrakeForce;
		}
		else if (throttle > 0f)
		{
			if (speed < -ReverseThreshold)
				brake = BrakeForce;                                // rolling backwards: stop first
			else if (speed < MaxForwardSpeed)
				engine = ForwardEngineForce * throttle;            // push toward +Z (forward)
		}
		else if (throttle < 0f)
		{
			if (speed > ReverseThreshold)
				brake = BrakeForce;                                // rolling forwards: S brakes
			else if (speed > -MaxReverseSpeed)
				engine = ReverseEngineForce * throttle;            // throttle < 0 -> push toward -Z (reverse)
		}
		else
		{
			brake = CoastBrakeForce;
		}

		EngineForce = engine;
		Brake = brake;

		// Positive Steering = left. Reduce the lock at speed so the rover doesn't roll in low gravity.
		float speedFactor = Mathf.Lerp(1f, HighSpeedSteerFactor, Mathf.Clamp(Mathf.Abs(speed) / MaxForwardSpeed, 0f, 1f));
		float targetSteer = steerInput * MaxSteerRadians * speedFactor;
		Steering = Mathf.MoveToward(Steering, targetSteer, SteerSpeed * dt);

		ApplyStabilityAssist(dt);

		// Carry the (collision-less) player body with the seat so the minimap, pressurized zone and O2
		// logic track the rover. The visible astronaut is parented to the Seat marker itself.
		_driver.GlobalPosition = _seat != null ? _seat.GlobalPosition : GlobalPosition;

		UpdateChaseCamera(dt, speed);
		UpdateChaseFollow(dt);
	}

	// ======================================================================
	//  Stability assist
	// ======================================================================

	/// <summary>
	/// Low gravity means very little weight on the tyres, so a real-physics rover rolls easily.
	/// A spring-damper torque keeps the chassis upright (roll/pitch only, yaw untouched), and a
	/// rover left on its side is set back on its wheels after a moment.
	/// </summary>
	private void ApplyStabilityAssist(float dt)
	{
		Vector3 up = GlobalBasis.Y;
		Vector3 axis = up.Cross(Vector3.Up);                       // rotation that brings 'up' back to world up
		Vector3 tiltSpin = AngularVelocity - Vector3.Up * AngularVelocity.Dot(Vector3.Up); // ignore yaw rate
		ApplyTorque((axis * UprightStiffness - tiltSpin * UprightDamping) * Mass);

		_tippedTime = up.Y < TippedThreshold ? _tippedTime + dt : 0f;
		if (_tippedTime > RecoverDelay)
		{
			_tippedTime = 0f;
			float yaw = Mathf.Atan2(GlobalBasis.Z.X, GlobalBasis.Z.Z);
			GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), GlobalPosition + Vector3.Up * 1.2f);
			LinearVelocity = Vector3.Zero;
			AngularVelocity = Vector3.Zero;
			GameManager.Instance?.PostMessage("Rover righted. Ease off the throttle in tight turns - 1/6 g means very little grip.");
		}
	}

	// ======================================================================
	//  Chase camera
	// ======================================================================

	private void ResetCamera()
	{
		_orbitYaw = BehindYaw;
		_orbitPitch = Mathf.DegToRad(DefaultCameraPitchDegrees);
		_timeSinceMouse = 0f;
		ApplyCamera();
	}

	/// <summary>After a short pause in mouse input, swing the camera back behind the rover.</summary>
	private void UpdateChaseCamera(float dt, float speed)
	{
		_timeSinceMouse += dt;
		if (_timeSinceMouse < CameraRecenterDelay)
			return;

		float weight = Mathf.Clamp(CameraRecenterSpeed * dt, 0f, 1f);
		_orbitYaw = Mathf.LerpAngle(_orbitYaw, BehindYaw, weight);
		_orbitPitch = Mathf.Lerp(_orbitPitch, Mathf.DegToRad(DefaultCameraPitchDegrees), weight);
		ApplyCamera();
	}

	/// <summary>Lagged follow: the top-level chase pivot eases toward the rover position and heading.</summary>
	private void UpdateChaseFollow(float dt)
	{
		if (_cameraPivot == null)
			return;
		Vector3 target = GlobalTransform * _pivotOffset;
		_followPos = _followPos.Lerp(target, 1f - Mathf.Exp(-ChasePositionStiffness * dt));
		_followYaw = Mathf.LerpAngle(_followYaw, GlobalRotation.Y, 1f - Mathf.Exp(-ChaseYawStiffness * dt));
		_cameraPivot.GlobalPosition = _followPos;
		ApplyCamera();
	}

	/// <summary>Puts the chase pivot exactly on the rover (no lag) - used when parked and on boarding.</summary>
	private void SnapChaseCamera()
	{
		if (_cameraPivot == null)
			return;
		_followPos = GlobalTransform * _pivotOffset;
		_followYaw = GlobalRotation.Y;
		_cameraPivot.GlobalPosition = _followPos;
		ApplyCamera();
		_springArm?.ForceUpdateTransform();
	}

	private void ApplyCamera()
	{
		if (_cameraPivot != null)
			_cameraPivot.Rotation = new Vector3(0, _followYaw + _orbitYaw, 0); // top-level: global yaw = lagged rover yaw + orbit
		if (_springArm != null)
			_springArm.Rotation = new Vector3(_orbitPitch, 0, 0);
	}
}

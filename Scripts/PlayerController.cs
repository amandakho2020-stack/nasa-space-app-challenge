using Godot;

/// <summary>
/// Third-person lunar astronaut. Expected children:
///   CollisionShape3D            (CapsuleShape3D)
///   Visual (Node3D)             meshes; rotated to face the movement direction
///   CameraPivot (Node3D)        at head height; yaw (mouse X)
///     SpringArm3D               pitch (mouse Y); spring length = camera distance
///       Camera3D
///
/// The CharacterBody3D itself never rotates, so the camera can orbit freely.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	public const string InteractableGroup = "Interactable";

	/// <summary>Horizontal speed in m/s. Public so the GameManager can change it at runtime.</summary>
	[Export] public float MoveSpeed = 5f;
	[Export] public float Acceleration = 10f;
	/// <summary>Fraction of Acceleration available while airborne (low = floaty).</summary>
	[Export(PropertyHint.Range, "0,1,0.05")] public float AirControl = 0.25f;
	/// <summary>Lunar surface gravity is 1.62 m/s^2 (~1/6 of Earth).</summary>
	[Export] public float Gravity = 1.62f;
	/// <summary>With 1.62 gravity, 3.2 m/s gives a ~3 m high, ~4 s jump.</summary>
	[Export] public float JumpVelocity = 3.2f;
	/// <summary>How fast the astronaut turns toward the movement direction.</summary>
	[Export] public float TurnSpeed = 10f;

	[ExportGroup("Camera")]
	[Export] public float MouseSensitivity = 0.003f;
	[Export] public float MinPitchDegrees = -65f;
	[Export] public float MaxPitchDegrees = 25f;
	[Export] public float MinZoom = 2.5f;
	[Export] public float MaxZoom = 9f;
	[Export] public float ZoomStep = 0.5f;

	[ExportGroup("2.5D Sprite")]
	/// <summary>True if the side-walk frames in the sheet face screen-right (FlipH is applied when moving left).</summary>
	[Export] public bool SpriteFacesRight = true;
	/// <summary>Horizontal speed (m/s) above which the walk animation plays.</summary>
	[Export] public float WalkThreshold = 0.4f;
	/// <summary>Seconds standing still before the astronaut waves at the camera.</summary>
	[Export] public float WaveAfterIdleSeconds = 8f;
	/// <summary>Height (m) of the low-gravity bounce added while walking.</summary>
	[Export] public float WalkBounce = 0.07f;

	[ExportGroup("Camera (clipping)")]
	/// <summary>Near clip plane. 0.05 keeps walls from being sliced open when the camera is pressed against them; much lower causes z-fighting at distance.</summary>
	[Export(PropertyHint.Range, "0.005,0.5,0.005")] public float CameraNear = 0.05f;
	[Export] public float CameraFar = 2000f;
	/// <summary>Spring arm collision layers: 1 = world, 4 = camera_blocker (corridor interior shells).</summary>
	[Export(PropertyHint.Layers3DPhysics)] public uint CameraCollisionMask = 1u | 8u;
	/// <summary>Radius of the sphere the spring arm sweeps (bigger = camera stays further off walls).</summary>
	[Export] public float CameraProbeRadius = 0.3f;
	/// <summary>Gap kept between the camera and whatever the spring arm hit.</summary>
	[Export] public float CameraMargin = 0.25f;

	[ExportGroup("Interaction")]
	/// <summary>Max horizontal distance to an interactable's origin.</summary>
	[Export] public float InteractRange = 3f;

	private Node3D _visual;
	private AnimatedSprite3D _sprite;
	private float _spriteBaseY;
	private bool _faceRight = true;
	private float _idleTime;
	private float _bobTime;
	private DirectionalLight3D _sun;
	private float _shadeTimer;
	private float _targetShade = 1f;
	private Node3D _cameraPivot;
	private SpringArm3D _springArm;
	private Camera3D _camera;
	private Transform3D _visualRestPose = Transform3D.Identity;
	private Node3D _model;          // 3D astronaut (ModelBaseBuilder, group "player_model"), if present
	private Vector3 _modelBasePos;
	private float _modelBob;

	private float _yaw;
	private float _pitch;
	private IInteractable _currentTarget;
	private string _currentPrompt = "";

	/// <summary>Y rotation of the astronaut mesh (used by the minimap arrow).</summary>
	public float FacingYaw => _visual.GlobalRotation.Y;

	/// <summary>Y rotation of the orbit camera.</summary>
	public float CameraYaw => _yaw;

	public override void _Ready()
	{
		EnsureInputActions();

		_visual = GetNode<Node3D>("Visual");
		_sprite = GetNodeOrNull<AnimatedSprite3D>("Visual/Sprite");
		if (_sprite != null)
			_spriteBaseY = _sprite.Position.Y;
		_sun = FindSun(GetTree().CurrentScene ?? GetTree().Root);
		_cameraPivot = GetNode<Node3D>("CameraPivot");
		_springArm = GetNode<SpringArm3D>("CameraPivot/SpringArm3D");
		_camera = _springArm.GetNode<Camera3D>("Camera3D");
		ConfigureCamera();
		_model = FindModel(_visual);
		if (_model != null)
			_modelBasePos = _model.Position;
		_springArm.AddExcludedObject(GetRid()); // don't let the arm collide with our own capsule

		_yaw = _cameraPivot.Rotation.Y;
		_pitch = _springArm.Rotation.X;
		ApplyCameraRotation();

		Input.MouseMode = Input.MouseModeEnum.Captured;
		GameManager.Instance?.RegisterPlayer(this);
	}

	public override void _ExitTree()
	{
		GameManager.Instance?.UnregisterPlayer(this);
	}

	// ======================================================================
	//  Vehicles (see RoverController)
	// ======================================================================

	/// <summary>True while the astronaut is seated in a vehicle.</summary>
	public bool InVehicle { get; private set; }

	private int _collisionRestoreFrames;

	/// <summary>
	/// Seats the astronaut in a vehicle. The CharacterBody3D stops simulating (no physics, no input,
	/// collision off), but the astronaut stays VISIBLE: the Visual node (sprite or 3D model) is
	/// reparented onto the vehicle's Seat marker, so it rides along sitting in the rover.
	/// </summary>
	public void EnterVehicle(Node3D seat, Transform3D seatedPose)
	{
		InVehicle = true;
		Velocity = Vector3.Zero;
		// Disable immediately (not deferred): the vehicle carries the body to the seat on the next
		// physics step, and an enabled capsule would be swept into the vehicle and shove it.
		GetNode<CollisionShape3D>("CollisionShape3D").Disabled = true;
		_collisionRestoreFrames = 0;
		SetPhysicsProcess(false);
		SetProcessUnhandledInput(false);
		_currentTarget = null;
		_currentPrompt = "";
		GameManager.Instance?.SetInteractPrompt("");

		if (seat != null)
		{
			_visualRestPose = _visual.Transform;
			_visual.Reparent(seat, keepGlobalTransform: false);
			_visual.Transform = seatedPose;
		}
		if (_sprite != null && _sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation("idle"))
		{
			_sprite.Play("idle");
			_sprite.Position = new Vector3(0f, _spriteBaseY, 0f);
		}
		if (_model != null)
			_model.Position = _modelBasePos;
	}

	/// <summary>
	/// Puts the astronaut back on foot at <paramref name="position"/>: the Visual node returns to the
	/// player, physics/input resume, and the camera blends back from <paramref name="fromCamera"/>.
	/// The orbit camera is aimed along <paramref name="cameraYaw"/> so the blend is short and natural.
	/// </summary>
	public void ExitVehicle(Vector3 position, Camera3D fromCamera = null, float? cameraYaw = null, float blendSeconds = 0.8f)
	{
		if (_visual.GetParent() != this)
		{
			_visual.Reparent(this, keepGlobalTransform: false);
			_visual.Transform = _visualRestPose;
		}

		GlobalPosition = position;
		Velocity = Vector3.Zero;
		// Jolt moves a teleported kinematic body by sweeping it from the old transform (the seat)
		// to the new one. If the capsule collided during that step it would plough through the
		// vehicle and flip it, so collision is restored only after the teleport has settled.
		_collisionRestoreFrames = 2;
		SetPhysicsProcess(true);
		SetProcessUnhandledInput(true);

		if (cameraYaw.HasValue)
		{
			_yaw = cameraYaw.Value;
			ApplyCameraRotation();
		}
		// Make sure the spring arm has collided at the new position before the blend samples it.
		ForceUpdateTransform();
		_springArm.ForceUpdateTransform();
		CameraBlender.Blend(fromCamera, _camera, blendSeconds);
		InVehicle = false;
	}

	/// <summary>The player's third-person camera (end of the spring arm).</summary>
	public Camera3D PlayerCamera => _camera;

	/// <summary>Current orbit-camera yaw (radians).</summary>
	public float OrbitYaw => _yaw;

	/// <summary>Kept for compatibility; the minimap now reads the Visual's global rotation.</summary>
	public void SetFacingYaw(float yaw) => _visual.Rotation = new Vector3(0, yaw, 0);

	public override void _UnhandledInput(InputEvent @event)
	{
		var gm = GameManager.Instance;
		bool locked = gm != null && gm.InputLocked;
		bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;

		if (locked)
			return;

		if (@event is InputEventMouseMotion motion && captured)
		{
			_yaw -= motion.Relative.X * MouseSensitivity;
			_pitch -= motion.Relative.Y * MouseSensitivity;
			ApplyCameraRotation();
			return;
		}

		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp)
				_springArm.SpringLength = Mathf.Clamp(_springArm.SpringLength - ZoomStep, MinZoom, MaxZoom);
			else if (mb.ButtonIndex == MouseButton.WheelDown)
				_springArm.SpringLength = Mathf.Clamp(_springArm.SpringLength + ZoomStep, MinZoom, MaxZoom);
			else if (mb.ButtonIndex == MouseButton.Left && !captured)
				Input.MouseMode = Input.MouseModeEnum.Captured;
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = captured ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed("interact"))
		{
			_currentTarget?.Interact(this);
			GetViewport().SetInputAsHandled();
		}
		else if (gm != null)
		{
			if (@event.IsActionPressed("tool_1")) gm.EquipTool(Tool.Scanner);
			else if (@event.IsActionPressed("tool_2")) gm.EquipTool(Tool.Wrench);
			else if (@event.IsActionPressed("tool_3")) gm.EquipTool(Tool.Drill);
			else if (@event.IsActionPressed("tool_4")) gm.UseO2Canister();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_collisionRestoreFrames > 0)
		{
			// Hold still until the post-vehicle teleport has been applied by the physics server.
			if (--_collisionRestoreFrames == 0)
				GetNode<CollisionShape3D>("CollisionShape3D").Disabled = false;
			return;
		}

		float dt = (float)delta;
		var gm = GameManager.Instance;
		bool locked = gm != null && gm.InputLocked;

		Vector3 velocity = Velocity;
		bool onFloor = IsOnFloor();

		if (!onFloor)
			velocity.Y -= Gravity * dt;

		if (!locked && onFloor && Input.IsActionJustPressed("jump"))
			velocity.Y = JumpVelocity;

		// Movement is relative to the camera's yaw, not the body.
		Vector2 input = locked ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		Vector3 direction = (new Basis(Vector3.Up, _yaw) * new Vector3(input.X, 0, input.Y)).Normalized();
		Vector3 target = direction * MoveSpeed;

		float accel = onFloor ? Acceleration : Acceleration * AirControl;
		float weight = Mathf.Clamp(accel * dt, 0f, 1f);
		velocity.X = Mathf.Lerp(velocity.X, target.X, weight);
		velocity.Z = Mathf.Lerp(velocity.Z, target.Z, weight);

		Velocity = velocity;
		MoveAndSlide();

		// Turn the astronaut mesh to face where it's walking (mesh forward is -Z).
		if (direction.LengthSquared() > 0.01f)
		{
			float targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
			float yaw = Mathf.LerpAngle(_visual.Rotation.Y, targetYaw, Mathf.Clamp(TurnSpeed * dt, 0f, 1f));
			_visual.Rotation = new Vector3(0, yaw, 0);
		}

		UpdateSprite(dt);
		UpdateModel(dt);
		UpdateInteractTarget(locked);
	}

	// ======================================================================
	//  2.5D sprite animation (Visual/Sprite = Y-billboarded AnimatedSprite3D)
	// ======================================================================

	/// <summary>
	/// Picks idle / walk / walk_front / walk_back / jump / wave from the velocity measured in the
	/// CAMERA's frame, flips the sprite when moving screen-left, and adds a low-gravity bounce.
	/// </summary>
	private void UpdateSprite(float dt)
	{
		if (_sprite == null || _sprite.SpriteFrames == null)
			return;

		var camBasis = new Basis(Vector3.Up, _yaw);
		Vector3 camRight = camBasis * Vector3.Right;
		Vector3 camForward = camBasis * Vector3.Forward;   // away from the camera
		Vector3 hv = new(Velocity.X, 0f, Velocity.Z);
		float speed = hv.Length();
		float side = hv.Dot(camRight);
		float depth = hv.Dot(camForward);

		string anim;
		if (!IsOnFloor() && Mathf.Abs(Velocity.Y) > 0.6f)
		{
			anim = "jump";
			_idleTime = 0f;
		}
		else if (speed > WalkThreshold)
		{
			_idleTime = 0f;
			if (depth > Mathf.Abs(side) * 1.3f)
				anim = "walk_back";        // walking into the screen: show the backpack
			else if (-depth > Mathf.Abs(side) * 1.3f)
				anim = "walk_front";       // walking toward the camera
			else
				anim = "walk";             // mostly sideways
		}
		else
		{
			_idleTime += dt;
			anim = _idleTime > WaveAfterIdleSeconds && _idleTime < WaveAfterIdleSeconds + 3.2f ? "wave" : "idle";
			if (_idleTime > WaveAfterIdleSeconds + 3.2f)
				_idleTime = 0f;
		}

		// Only change facing on clear sideways movement so diagonal jitter doesn't flicker.
		if (Mathf.Abs(side) > 0.25f)
			_faceRight = side > 0f;
		_sprite.FlipH = SpriteFacesRight ? !_faceRight : _faceRight;

		if (!_sprite.SpriteFrames.HasAnimation(anim))
			anim = anim.StartsWith("walk") && _sprite.SpriteFrames.HasAnimation("walk") ? "walk" : "idle";
		if (_sprite.Animation != anim || !_sprite.IsPlaying())
			_sprite.Play(anim);

		// Animation speed follows actual speed (slower after the Sol 2 "muscle atrophy" penalty).
		_sprite.SpeedScale = anim.StartsWith("walk") ? Mathf.Clamp(speed / 5f, 0.4f, 1.6f) : 1f;

		// Low-gravity bounce while walking; gentle breathing while idle.
		float y = 0f;
		if (anim.StartsWith("walk"))
		{
			_bobTime += dt * Mathf.Max(speed, 1f) * 1.4f;
			y = Mathf.Abs(Mathf.Sin(_bobTime)) * WalkBounce;
		}
		else
		{
			_bobTime += dt;
			y = Mathf.Sin(_bobTime * 1.6f) * 0.01f;
		}
		_sprite.Position = new Vector3(0f, _spriteBaseY + y, 0f);

		UpdateSpriteShade(dt);
	}

	/// <summary>
	/// The sprite is unshaded (so it never turns into a black back-lit cut-out), so it is dimmed
	/// here instead: full brightness indoors and in sunlight, darker when standing in a shadow
	/// outdoors (a ray toward the sun hits something).
	/// </summary>
	private void UpdateSpriteShade(float dt)
	{
		_shadeTimer -= dt;
		if (_shadeTimer <= 0f)
		{
			_shadeTimer = 0.15f;
			var gm = GameManager.Instance;
			if (gm != null && gm.IsInsideHub)
				_targetShade = 0.95f;
			else if (_sun != null)
			{
				Vector3 from = _sprite.GlobalPosition;
				Vector3 toSun = _sun.GlobalBasis.Z.Normalized(); // light travels along -Z
				var query = PhysicsRayQueryParameters3D.Create(from, from + toSun * 80f);
				query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
				bool shadowed = GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
				_targetShade = shadowed ? 0.42f : 1f;
			}
		}
		float current = _sprite.Modulate.R;
		float shade = Mathf.Lerp(current, _targetShade, Mathf.Clamp(dt * 6f, 0f, 1f));
		_sprite.Modulate = new Color(shade, shade, shade * 1.03f);
	}

	private static DirectionalLight3D FindSun(Node root)
	{
		if (root is DirectionalLight3D d)
			return d;
		foreach (Node c in root.GetChildren())
			if (FindSun(c) is { } found)
				return found;
		return null;
	}

	/// <summary>Applies the clipping-safe camera settings to Camera3D + SpringArm3D.</summary>
	private void ConfigureCamera()
	{
		_camera.Near = CameraNear;
		_camera.Far = CameraFar;
		_springArm.CollisionMask = CameraCollisionMask;
		_springArm.Margin = CameraMargin;
		_springArm.Shape = new SphereShape3D { Radius = CameraProbeRadius };
	}

	/// <summary>Finds the ModelBaseBuilder holder (direct child of Visual) that contains the "player_model".</summary>
	private static Node3D FindModel(Node3D visual)
	{
		foreach (Node child in visual.GetChildren())
			if (child is Node3D holder && ContainsGroup(holder, "player_model"))
				return holder;
		return null;
	}

	private static bool ContainsGroup(Node n, string group)
	{
		if (n.IsInGroup(group))
			return true;
		foreach (Node c in n.GetChildren())
			if (ContainsGroup(c, group))
				return true;
		return false;
	}

	/// <summary>
	/// The imported astronaut has no skeleton/animations, so walking is suggested procedurally:
	/// a low-gravity hop plus a slight forward lean while moving, a gentle breathing bob at rest.
	/// </summary>
	private void UpdateModel(float dt)
	{
		if (_model == null)
			return;
		float speed = new Vector2(Velocity.X, Velocity.Z).Length();
		bool airborne = !IsOnFloor();
		float y, lean;
		if (speed > WalkThreshold && !airborne)
		{
			_modelBob += dt * Mathf.Max(speed, 1f) * 1.4f;
			y = Mathf.Abs(Mathf.Sin(_modelBob)) * WalkBounce * 1.4f;
			lean = Mathf.Clamp(speed / MoveSpeed, 0f, 1f) * 7f;
		}
		else
		{
			_modelBob += dt;
			y = Mathf.Sin(_modelBob * 1.6f) * 0.008f;
			lean = 0f;
		}
		_model.Position = _modelBasePos + new Vector3(0f, y, 0f);
		Vector3 r = _model.RotationDegrees;
		// The model holder faces -Z under Visual after its 180 degree turn; leaning forward = +X rotation.
		_model.RotationDegrees = new Vector3(Mathf.Lerp(r.X, -lean, Mathf.Clamp(dt * 8f, 0f, 1f)), r.Y, r.Z);
	}

	private void ApplyCameraRotation()
	{
		_pitch = Mathf.Clamp(_pitch, Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
		_cameraPivot.Rotation = new Vector3(0, _yaw, 0);
		_springArm.Rotation = new Vector3(_pitch, 0, 0);
	}

	/// <summary>Picks the nearest Interactable node within range (horizontal distance).</summary>
	private void UpdateInteractTarget(bool locked)
	{
		IInteractable best = null;
		if (!locked)
		{
			float bestDist = InteractRange;
			Vector3 me = GlobalPosition;
			foreach (Node node in GetTree().GetNodesInGroup(InteractableGroup))
			{
				if (node is not IInteractable interactable)
					continue;
				Vector3 offset = interactable.InteractPosition - me;
				offset.Y = 0;
				float dist = offset.Length();
				if (dist < bestDist)
				{
					bestDist = dist;
					best = interactable;
				}
			}
		}

		_currentTarget = best;
		string prompt = best?.GetPrompt() ?? "";
		if (prompt != _currentPrompt)
		{
			_currentPrompt = prompt;
			GameManager.Instance?.SetInteractPrompt(prompt);
		}
	}

	/// <summary>Forces the HUD prompt to refresh next frame (e.g. after a door toggles).</summary>
	public void RefreshPrompt() => _currentPrompt = null;

	/// <summary>
	/// Creates the input actions at runtime if they are not defined in Project Settings > Input Map,
	/// so the prototype works out of the box.
	/// </summary>
	private static void EnsureInputActions()
	{
		AddKeyAction("move_forward", Key.W, Key.Up);
		AddKeyAction("move_back", Key.S, Key.Down);
		AddKeyAction("move_left", Key.A, Key.Left);
		AddKeyAction("move_right", Key.D, Key.Right);
		AddKeyAction("jump", Key.Space);
		AddKeyAction("interact", Key.E);
		AddKeyAction("tool_1", Key.Key1);
		AddKeyAction("tool_2", Key.Key2);
		AddKeyAction("tool_3", Key.Key3);
		AddKeyAction("tool_4", Key.Key4);
	}

	private static void AddKeyAction(string action, params Key[] keys)
	{
		if (InputMap.HasAction(action))
			return;
		InputMap.AddAction(action);
		foreach (Key key in keys)
			InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
	}
}

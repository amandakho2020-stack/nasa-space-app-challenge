using Godot;

/// <summary>
/// An airlock door (the Rover Bay exit). Attach to a CSGBox3D that fills the door opening.
/// [E] slides it open (sideways into the wall) so the player can step out onto the
/// surface; it closes itself after a delay once the doorway is clear.
/// An optional OmniLight3D child named "Indicator" shows red (closed) / green (open).
/// </summary>
public partial class AirlockDoor : CsgBox3D, IInteractable
{
	/// <summary>Local offset applied when fully open.</summary>
	[Export] public Vector3 OpenOffset = new Vector3(2.6f, 0f, 0f);
	[Export] public float SlideTime = 0.8f;
	[Export] public float AutoCloseDelay = 4f;
	/// <summary>The door will not auto-close while the player is closer than this.</summary>
	[Export] public float DoorwayClearRadius = 1.8f;
	[Export] public Color DoorColor = new Color(1f, 0.55f, 0.1f);

	private Vector3 _closedPosition;
	private bool _isOpen;
	private float _closeTimer;
	private Tween _tween;
	private OmniLight3D _indicator;

	public bool IsOpen => _isOpen;

	/// <summary>The doorway itself, so the prompt stays put while the door slides.</summary>
	public Vector3 InteractPosition => GetParent<Node3D>().ToGlobal(_closedPosition);

	public override void _Ready()
	{
		UseCollision = true;
		AddToGroup(PlayerController.InteractableGroup);
		_closedPosition = Position;
		Material = new StandardMaterial3D
		{
			AlbedoColor = DoorColor,
			EmissionEnabled = true,
			Emission = DoorColor,
			EmissionEnergyMultiplier = 0.25f,
		};
		_indicator = GetNodeOrNull<OmniLight3D>("Indicator");
		UpdateIndicator();
	}

	public override void _Process(double delta)
	{
		if (!_isOpen)
			return;

		_closeTimer -= (float)delta;
		if (_closeTimer > 0f)
			return;

		var player = GameManager.Instance?.Player;
		bool playerInDoorway = player != null &&
			player.GlobalPosition.DistanceTo(InteractPosition) < DoorwayClearRadius;
		if (playerInDoorway)
			_closeTimer = 0.5f; // check again shortly
		else
			SetOpen(false);
	}

	public string GetPrompt()
	{
		bool inside = GameManager.Instance?.IsInsideHub ?? true;
		if (_isOpen)
			return "[E] Close Airlock";
		return inside ? "[E] Cycle Airlock - step outside (EVA)" : "[E] Cycle Airlock - enter the base";
	}

	public void Interact(PlayerController player)
	{
		SetOpen(!_isOpen);
		player?.RefreshPrompt();
	}

	public void SetOpen(bool open)
	{
		if (_isOpen == open)
			return;
		_isOpen = open;
		_closeTimer = AutoCloseDelay;

		_tween?.Kill();
		_tween = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_tween.TweenProperty(this, "position", open ? _closedPosition + OpenOffset : _closedPosition, SlideTime);

		UpdateIndicator();
		GameManager.Instance?.Player?.RefreshPrompt();
	}

	private void UpdateIndicator()
	{
		if (_indicator != null)
			_indicator.LightColor = _isOpen ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.25f, 0.2f);
	}
}

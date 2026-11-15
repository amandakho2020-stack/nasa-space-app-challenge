using Godot;
using System;

namespace AeroKidz;

/// <summary>
/// Makes a 3D model start one of the AeroKidz minigames.
///
/// Attach to an <see cref="Area3D"/> placed where the player should stand (e.g. at the Lunar
/// Module's ladder, beside the telescope) with a CollisionShape3D child. It works two ways:
///   - Press E: it is an IInteractable, so the team's PlayerController shows the prompt when you
///     are near and calls Interact() on E (the same way the RoomScreen3D terminals work).
///   - Walk in: with TriggerOnEnter on, the Area3D's body_entered signal starts it automatically.
///
/// Either way it calls the SAME entry point as the room screens - BaseMap.EnterRoom(Room) via
/// World3DBridge - so the minigame code (LevelController, SunWatcherGame, RocketLaunchGame, ...)
/// runs completely unchanged. LevelController decides what happens (start the mission, finish a
/// "walk to ..." step, or nothing if it is not time yet).
///
/// When it is the current goal it shows a floating "GO HERE" marker (visible through walls).
/// </summary>
[GlobalClass]
public partial class MinigameTrigger3D : Area3D, IInteractable
{
	public const string Group = "minigame_trigger";

	/// <summary>Which room / game this trigger stands for (Telescope = Mission 3, Rocket = fly home, ...).</summary>
	[Export] public OutpostRoom Room = OutpostRoom.Telescope;
	/// <summary>Prompt shown near it ("" = automatic, e.g. "[E] TELESCOPE").</summary>
	[Export] public string Prompt = "";
	/// <summary>Also start when the player simply walks into the area (body_entered).</summary>
	[Export] public bool TriggerOnEnter;
	/// <summary>Height of the GO HERE marker above this node.</summary>
	[Export] public float MarkerHeight = 4f;

	/// <summary>Raised when the player uses it. World3DBridge connects this to BaseMap.EnterRoom.</summary>
	public event Action<OutpostRoom> Used;

	private Label3D _marker;
	private bool _isGoal;
	private bool _playerInside;
	private float _time;

	public override void _Ready()
	{
		AddToGroup(Group);
		AddToGroup(PlayerController.InteractableGroup);
		Monitoring = true;
		Monitorable = false;

		// Signals wired in code so nothing has to be connected by hand in the Node dock.
		// (Equivalent to Node dock -> body_entered -> _on_body_entered.)
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;

		_marker = new Label3D
		{
			Text = "▼ GO HERE ▼\n" + BaseMap.RoomName(Room),
			FontSize = 56,
			PixelSize = 0.0015f,
			FixedSize = true,
			NoDepthTest = true,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			OutlineSize = 14,
			Modulate = new Color(1f, 0.85f, 0.25f),
			OutlineModulate = new Color(0.25f, 0.12f, 0f),
			Position = new Vector3(0, MarkerHeight, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
			RenderPriority = 10,
			Layers = 2, // not drawn on the minimap
			Visible = false,
		};
		AddChild(_marker);
	}

	/// <summary>Called by World3DBridge whenever the goal room changes.</summary>
	public void SetGoal(bool isGoal) => _isGoal = isGoal;

	public override void _Process(double delta)
	{
		_time += (float)delta;
		_marker.Visible = _isGoal && RoomScreen3D.MarkersAllowed && !_playerInside;
		if (_marker.Visible)
			_marker.Position = new Vector3(0, MarkerHeight + 0.15f * Mathf.Sin(_time * 3f), 0);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is not PlayerController)
			return;
		_playerInside = true;
		if (TriggerOnEnter)
			Used?.Invoke(Room);
	}

	private void OnBodyExited(Node3D body)
	{
		if (body is PlayerController)
			_playerInside = false;
	}

	// ---- IInteractable: the team's PlayerController picks the nearest one within 3 m ----
	public Vector3 InteractPosition => GlobalPosition;

	public string GetPrompt()
	{
		if (!string.IsNullOrEmpty(Prompt))
			return Prompt;
		return Room == OutpostRoom.Rocket
			? (_isGoal ? "[E] BOARD THE LUNAR MODULE - FLY HOME!" : "LUNAR MODULE - finish all 3 missions to fly home")
			: $"[E] {BaseMap.RoomName(Room)}";
	}

	public void Interact(PlayerController player) => Used?.Invoke(Room);
}

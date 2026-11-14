using Godot;

/// <summary>
/// Area3D covering the pressurized interior of the base (one CollisionShape3D per room/corridor). While the player is inside (and the
/// outpost has power), suit Oxygen does not drain. Needs a CollisionShape3D child.
/// </summary>
public partial class HubZone : Area3D
{
	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is PlayerController)
			GameManager.Instance?.SetInsideHub(true);
	}

	private void OnBodyExited(Node3D body)
	{
		if (body is PlayerController)
			GameManager.Instance?.SetInsideHub(false);
	}
}

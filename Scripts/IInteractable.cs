using Godot;

/// <summary>
/// Anything the player can use with [E]. Implementations must be Node3D-derived and add
/// themselves to the "Interactable" group; the player picks the nearest one in range.
/// </summary>
public interface IInteractable
{
	/// <summary>World position used for the range check (e.g. a door's closed position).</summary>
	Vector3 InteractPosition { get; }

	/// <summary>Text shown on the HUD while this is the nearest interactable.</summary>
	string GetPrompt();

	/// <summary>Called when the player presses the interact key.</summary>
	void Interact(PlayerController player);
}

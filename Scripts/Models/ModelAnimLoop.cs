using Godot;
using System.Linq;

/// <summary>
/// Plays an imported model's animation on a loop at runtime (glTF animations import as
/// play-once by default). Added automatically by ModelBaseBuilder next to animated models.
/// </summary>
public partial class ModelAnimLoop : Node
{
	[Export] public string AnimationName = "";

	private AnimationPlayer _player;

	public override void _Ready()
	{
		_player = FindPlayer(GetParent());
		if (_player == null || string.IsNullOrEmpty(AnimationName) || !_player.HasAnimation(AnimationName))
			return;
		_player.AnimationFinished += name => _player.Play(name);
		_player.Play(AnimationName);
	}

	private static AnimationPlayer FindPlayer(Node n)
	{
		if (n is AnimationPlayer p)
			return p;
		return n.GetChildren().Select(FindPlayer).FirstOrDefault(x => x != null);
	}
}

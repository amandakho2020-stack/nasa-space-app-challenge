using Godot;

/// <summary>Slowly spins a hologram (or any Node3D) around its Y axis, with a gentle bob.</summary>
public partial class HoloSpin : Node3D
{
	[Export] public float DegreesPerSecond = 18f;
	[Export] public float BobHeight = 0.04f;

	private float _t;
	private float _baseY;

	public override void _Ready() => _baseY = Position.Y;

	public override void _Process(double delta)
	{
		_t += (float)delta;
		RotateY(Mathf.DegToRad(DegreesPerSecond) * (float)delta);
		Position = new Vector3(Position.X, _baseY + Mathf.Sin(_t * 1.3f) * BobHeight, Position.Z);
	}
}

using Godot;

/// <summary>
/// Smooth "AAA" hand-off between two cameras. A temporary blend camera starts at the source
/// camera's exact transform and FOV, then eases (smoothstep on a sine curve) toward the target
/// camera. The target is sampled every frame, so the blend follows a moving vehicle without a pop.
/// When the blend finishes the target camera becomes current and the blend camera frees itself.
///
/// Usage: CameraBlender.Blend(fromCamera, toCamera, 0.9f);
/// </summary>
public partial class CameraBlender : Camera3D
{
	private Camera3D _to;
	private Transform3D _start;
	private float _startFov;
	private float _duration;
	private float _t;

	/// <summary>Starts a blend. Any blend already running is replaced (it continues from where it was).</summary>
	public static void Blend(Camera3D from, Camera3D to, float duration = 0.9f)
	{
		if (to == null)
			return;
		if (from == null || duration <= 0f || !from.IsInsideTree())
		{
			to.MakeCurrent();
			return;
		}

		Node host = to.GetTree().CurrentScene ?? to.GetTree().Root;
		var existing = host.GetNodeOrNull<CameraBlender>("CameraBlender");
		Camera3D source = existing ?? from;

		var blend = new CameraBlender
		{
			Name = existing != null ? "CameraBlenderNext" : "CameraBlender",
			_to = to,
			_start = source.GlobalTransform,
			_startFov = source.Fov,
			_duration = duration,
			Near = Mathf.Min(from.Near, to.Near),
			Far = Mathf.Max(from.Far, to.Far),
			Fov = source.Fov,
			Environment = from.Environment,
			Attributes = from.Attributes,
			CullMask = from.CullMask,
		};
		existing?.QueueFree();
		host.AddChild(blend);
		blend.Name = "CameraBlender";
		blend.GlobalTransform = blend._start;
		blend.MakeCurrent();
	}

	public override void _Process(double delta)
	{
		if (_to == null || !IsInstanceValid(_to))
		{
			QueueFree();
			return;
		}

		_t = Mathf.Min(_t + (float)delta / _duration, 1f);
		// Ease in/out: slow start, fast middle, gentle settle.
		float e = _t * _t * _t * (_t * (_t * 6f - 15f) + 10f); // smootherstep

		Transform3D target = _to.GlobalTransform;
		var rot = new Quaternion(_start.Basis.Orthonormalized()).Slerp(new Quaternion(target.Basis.Orthonormalized()), e);
		// Arc the path slightly upward so the move reads as a crane shot rather than a straight slide.
		Vector3 pos = _start.Origin.Lerp(target.Origin, e) + Vector3.Up * Mathf.Sin(e * Mathf.Pi) * 0.6f;
		GlobalTransform = new Transform3D(new Basis(rot), pos);
		Fov = Mathf.Lerp(_startFov, _to.Fov, e);

		if (_t >= 1f)
		{
			_to.MakeCurrent();
			QueueFree();
		}
	}
}

using Godot;

/// <summary>
/// Makes a Label3D "screen" show live mission data. Write the text with tokens in
/// <see cref="Template"/>; they are replaced a few times per second at runtime:
///   {SOL} {MET} {TIME} {O2} {PWR} {MAXPWR} {HP} {SCI} {OBJ} {STATUS}
/// In the editor the label keeps the static preview text the builder gave it.
/// </summary>
public partial class LiveScreen : Label3D
{
	[Export(PropertyHint.MultilineText)] public string Template = "";
	[Export] public float RefreshSeconds = 0.25f;

	private float _timer;

	public override void _Process(double delta)
	{
		_timer -= (float)delta;
		if (_timer > 0f || string.IsNullOrEmpty(Template))
			return;
		_timer = RefreshSeconds;

		var gm = GameManager.Instance;
		if (gm == null)
			return;

		Text = Template
			.Replace("{SOL}", $"{gm.CurrentDay}/{GameManager.FinalDay}")
			.Replace("{MET}", gm.MetText())
			.Replace("{TIME}", gm.ClockText())
			.Replace("{O2}", $"{gm.Oxygen:0}")
			.Replace("{PWR}", $"{gm.Power:0}")
			.Replace("{MAXPWR}", $"{gm.MaxPower:0}")
			.Replace("{HP}", $"{gm.Hp:0}")
			.Replace("{SCI}", $"{gm.SciencePoints}")
			.Replace("{OBJ}", GameManager.ZoneName(gm.ObjectiveZone).ToUpper())
			.Replace("{STATUS}", !gm.IsRunning ? "MISSION ENDED" : gm.IsMinigameOpen ? "REPAIR IN PROGRESS" : "NOMINAL");
	}
}

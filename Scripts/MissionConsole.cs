using Godot;

/// <summary>
/// The work panel of an exterior module. Attach to a CSGBox3D next to the module.
/// Pressing [E] (with the right tool equipped, on the right Sol) opens the module's
/// minigame overlay; solving it completes the Sol, applies the rewards below and
/// advances the day (hazard days 2, 5, 8 then show the Trade-Off popup).
/// </summary>
public partial class MissionConsole : CsgBox3D, IInteractable
{
	[Export] public Zone Zone = Zone.BioDome;
	[Export] public string MissionName = "Hydroponics Harvest";
	/// <summary>Instruction text shown at the top of the minigame overlay.</summary>
	[Export(PropertyHint.MultilineText)] public string MinigameTitle = "Connect the pipes from start to finish to restore the system flow.";
	[Export] public Tool RequiredTool = Tool.None;

	[ExportGroup("Rewards (negative = cost)")]
	[Export] public float OxygenReward;
	[Export] public float PowerReward;
	[Export] public int ScienceReward;
	[Export] public int CanisterReward;

	[ExportGroup("Look")]
	[Export] public Color BaseColor = new Color(0.9f, 0.75f, 0.2f);
	[Export] public Color ObjectiveGlow = new Color(1f, 0.85f, 0.3f);

	private StandardMaterial3D _material;
	private float _pulseTime;

	public override void _Ready()
	{
		UseCollision = true;
		AddToGroup(PlayerController.InteractableGroup);

		_material = new StandardMaterial3D
		{
			AlbedoColor = BaseColor,
			EmissionEnabled = true,
			Emission = ObjectiveGlow,
			EmissionEnergyMultiplier = 0f,
		};
		Material = _material;
	}

	public override void _Process(double delta)
	{
		// Pulse the console that is today's objective.
		var gm = GameManager.Instance;
		bool isObjective = gm != null && gm.IsRunning && gm.ObjectiveZone == Zone;
		_pulseTime += (float)delta;
		_material.EmissionEnergyMultiplier = isObjective ? 0.8f + 0.8f * Mathf.Sin(_pulseTime * 4f) : 0f;
	}

	public Vector3 InteractPosition => GlobalPosition;

	public string GetPrompt()
	{
		var gm = GameManager.Instance;
		if (gm == null)
			return MissionName;

		if (gm.RequireObjectiveZone && gm.ObjectiveZone != Zone)
			return $"{MissionName}  (not scheduled today)";

		if (RequiredTool != Tool.None && gm.EquippedTool != RequiredTool)
			return $"{MissionName} - equip {GameManager.ToolName(RequiredTool)} [{GameManager.ToolKey(RequiredTool)}]";

		return $"[E] {MissionName}";
	}

	public void Interact(PlayerController player)
	{
		GameManager.Instance?.RequestMission(this);
	}
}

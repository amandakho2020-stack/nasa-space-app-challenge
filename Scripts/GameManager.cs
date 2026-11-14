using Godot;
using System;

/// <summary>The three exterior mission modules.</summary>
public enum Zone
{
	BioDome,
	DrillArea,
	PowerRoom, // was CommTower (same value, 2)
}

/// <summary>Tools on the Quick Access wheel (slot 4 of the wheel is the O2 canister, a consumable).</summary>
public enum Tool
{
	None = -1,
	Scanner = 0,
	Wrench = 1,
	Drill = 2,
}

/// <summary>
/// Global game state for Lunar Outpost. Registered as the Autoload "GameManager"
/// (Project Settings > Globals > Autoload > res://Scripts/GameManager.cs).
///
/// Owns the 9-Sol loop, HP / Oxygen / Power / Science, the variables Trade-Off choices modify,
/// the "inside the pressurized base" state, the equipped tool and the active module minigame.
/// </summary>
public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; }

	// ----- Signals (UI listens to these) -----
	[Signal] public delegate void StatsChangedEventHandler();
	[Signal] public delegate void DayChangedEventHandler(int newDay);
	[Signal] public delegate void HazardTriggeredEventHandler(int day);
	[Signal] public delegate void MessagePostedEventHandler(string text);
	[Signal] public delegate void InteractPromptChangedEventHandler(string prompt);
	[Signal] public delegate void GameEndedEventHandler(bool victory, string summary);
	[Signal] public delegate void MinigameRequestedEventHandler(MissionConsole console);
	[Signal] public delegate void HubStateChangedEventHandler(bool insideHub);
	[Signal] public delegate void ToolChangedEventHandler(int tool);

	// ----- Constants -----
	public const int FinalDay = 9;
	public const float MaxOxygen = 100f;
	public const float MaxHp = 100f;
	public static readonly int[] HazardDays = { 2, 5, 8 };

	// ----- Tuning defaults (applied on every new run) -----
	private const float DefaultOxygenDrainRate = 1.0f; // % per second while on EVA
	private const float DefaultPowerDrainRate = 0.4f;  // % per second, always
	private const float DefaultMaxPower = 100f;
	private const int DefaultO2Canisters = 1;

	/// <summary>When true, only the module that is today's objective can be worked on.</summary>
	public bool RequireObjectiveZone = true;

	/// <summary>Oxygen drains this many times faster while Power is at 0.</summary>
	public float NoPowerOxygenMultiplier = 2f;

	/// <summary>HP lost per second once Oxygen hits 0.</summary>
	public float SuffocationHpRate = 5f;

	/// <summary>O2 regained per second inside the powered base. Set to 0 for "no drain" only.</summary>
	public float HubOxygenRefillRate = 2f;

	/// <summary>O2 restored by one canister from the tool wheel.</summary>
	public float CanisterOxygen = 35f;

	/// <summary>While driving, the rover's life support feeds the suit and O2 does not drain.</summary>
	public bool RoverSuppliesOxygen = true;

	// ----- Core state -----
	public int CurrentDay { get; private set; } = 1;
	public int SciencePoints { get; private set; }
	public bool IsRunning { get; private set; } = true;
	public bool IsInsideHub { get; private set; }
	public Tool EquippedTool { get; private set; } = Tool.None;
	public int O2Canisters { get; private set; } = DefaultO2Canisters;

	/// <summary>Seconds elapsed in the current Sol (drives the HUD mission clock).</summary>
	public float DayClock { get; private set; }

	/// <summary>Real seconds since the run started (drives the control-room MET displays).</summary>
	public float MissionElapsed { get; private set; }

	/// <summary>The module whose minigame is open, or null.</summary>
	public MissionConsole ActiveMission { get; private set; }
	public bool IsMinigameOpen => ActiveMission != null;

	/// <summary>True when the player should not move or interact (minigame, popup, end screen).</summary>
	public bool InputLocked => !IsRunning || IsMinigameOpen || GetTree().Paused || ExternalInputLock;

	/// <summary>Set by the AeroKidz 2D games (World3DBridge): true while a minigame, pop-up or Nova message is on screen.</summary>
	public bool ExternalInputLock { get; set; }

	/// <summary>
	/// Set by the AeroKidz games (World3DBridge): they run the missions and the Oxygen/Power/Shield bars,
	/// so this survival loop (suit O2 drain, Sols, mission consoles) is switched off.
	/// </summary>
	public bool ExternalControl { get; set; }

	private float _hp = MaxHp;
	public float Hp
	{
		get => _hp;
		set => _hp = Mathf.Clamp(value, 0f, MaxHp);
	}

	private float _oxygen = MaxOxygen;
	public float Oxygen
	{
		get => _oxygen;
		set => _oxygen = Mathf.Clamp(value, 0f, MaxOxygen);
	}

	private float _power = DefaultMaxPower;
	public float Power
	{
		get => _power;
		set => _power = Mathf.Clamp(value, 0f, MaxPower);
	}

	// ----- Variables changed by Trade-Off choices -----
	public float OxygenDrainRate = DefaultOxygenDrainRate;
	public float PowerDrainRate = DefaultPowerDrainRate;

	private float _maxPower = DefaultMaxPower;
	public float MaxPower
	{
		get => _maxPower;
		set
		{
			_maxPower = Mathf.Max(0f, value);
			Power = _power; // re-clamp to the new cap
		}
	}

	/// <summary>The player in the scene (registers itself in _Ready).</summary>
	public PlayerController Player { get; private set; }

	/// <summary>The rover the player is currently driving, or null.</summary>
	public RoverController ActiveVehicle { get; private set; }

	/// <summary>True while a rover is supplying the suit with O2.</summary>
	public bool RoverProtecting => ActiveVehicle != null && RoverSuppliesOxygen;

	/// <summary>Objectives rotate Plant (Bio-Dome) -> Drill Site (remote, reach it by rover) -> Power Room each Sol.</summary>
	public Zone ObjectiveZone => (Zone)((CurrentDay - 1) % 3);

	/// <summary>Is the pressurized base currently protecting the player?</summary>
	public bool HubProtecting => IsInsideHub && Power > 0f;

	public bool IsHazardDay(int day) => Array.IndexOf(HazardDays, day) >= 0;

	private bool _noPowerWarned;
	private bool _lowOxygenWarned;
	private bool _suffocatingWarned;

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this)
		{
			GD.PushWarning("GameManager: a second instance was created; freeing it.");
			QueueFree();
			return;
		}
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	public override void _Process(double delta)
	{
		// Default "Inherit" process mode: this freezes while the tree is paused (Trade-Off popup).
		// The module minigame does NOT pause, so O2 keeps draining while you work outside.
		if (!IsRunning || ExternalControl)
			return;

		float dt = (float)delta;
		DayClock += dt;
		MissionElapsed += dt;

		Power -= PowerDrainRate * dt;
		if (Power <= 0f && !_noPowerWarned)
		{
			_noPowerWarned = true;
			PostMessage("POWER DEPLETED - base life support offline, O2 draining faster! Restore power in the Power Room.");
		}
		else if (Power > 0f)
		{
			_noPowerWarned = false;
		}

		if (HubProtecting)
		{
			Oxygen += HubOxygenRefillRate * dt;
		}
		else if (RoverProtecting)
		{
			// Rover life support: suit O2 holds steady while driving.
		}
		else
		{
			float rate = OxygenDrainRate * (Power <= 0f ? NoPowerOxygenMultiplier : 1f);
			Oxygen -= rate * dt;
		}

		if (Oxygen <= 25f && !_lowOxygenWarned)
		{
			_lowOxygenWarned = true;
			PostMessage("WARNING: Oxygen below 25%. Return to the base or use an O2 canister [4].");
		}
		else if (Oxygen > 25f)
		{
			_lowOxygenWarned = false;
		}

		if (Oxygen <= 0f)
		{
			Hp -= SuffocationHpRate * dt;
			if (!_suffocatingWarned)
			{
				_suffocatingWarned = true;
				PostMessage("OXYGEN EMPTY - SUFFOCATING! HP is dropping.");
			}
		}
		else
		{
			_suffocatingWarned = false;
		}

		EmitSignal(SignalName.StatsChanged);

		if (Hp <= 0f)
			EndGame(false, $"Vital signs lost on Sol {CurrentDay}.\nThe outpost could not be sustained.");
	}

	// ======================================================================
	//  Mission flow: console -> minigame -> complete -> next Sol -> hazard
	// ======================================================================

	/// <summary>
	/// Called by a MissionConsole. Validates the request and, if allowed, opens the module minigame.
	/// </summary>
	public void RequestMission(MissionConsole console)
	{
		if (!IsRunning || GetTree().Paused || IsMinigameOpen || console == null)
			return;

		if (RequireObjectiveZone && console.Zone != ObjectiveZone)
		{
			PostMessage($"{console.MissionName} is not on today's schedule. Sol {CurrentDay} objective: {ZoneName(ObjectiveZone)}.");
			return;
		}

		if (console.RequiredTool != Tool.None && EquippedTool != console.RequiredTool)
		{
			PostMessage($"{console.MissionName} needs the {ToolName(console.RequiredTool)}. Equip it with [{ToolKey(console.RequiredTool)}].");
			return;
		}

		if (console.PowerReward < 0f && Power < -console.PowerReward)
		{
			PostMessage($"Not enough power for {console.MissionName} (needs {-console.PowerReward:0}%).");
			return;
		}

		ActiveMission = console;
		SetInteractPrompt("");
		EmitSignal(SignalName.MinigameRequested, console);
	}

	/// <summary>Called by the minigame's Solve button. Applies rewards and advances the Sol.</summary>
	public void CompleteActiveMission()
	{
		MissionConsole console = ActiveMission;
		if (console == null || !IsRunning)
			return;
		ActiveMission = null;

		Oxygen += console.OxygenReward;
		Power += console.PowerReward;
		SciencePoints += console.ScienceReward;
		O2Canisters += console.CanisterReward;

		PostMessage($"Sol {CurrentDay} complete: {console.MissionName}. " +
			FormatRewards(console.OxygenReward, console.PowerReward, console.ScienceReward, console.CanisterReward));
		EmitSignal(SignalName.StatsChanged);

		if (CurrentDay >= FinalDay)
		{
			EndGame(true, $"All {FinalDay} Sols survived.\nScience returned to Earth: {SciencePoints} pts\nRating: {ScienceRating()}");
			return;
		}

		AdvanceDay();
	}

	/// <summary>Called by the minigame's Abort button.</summary>
	public void CancelActiveMission()
	{
		if (ActiveMission == null)
			return;
		PostMessage($"{ActiveMission.MissionName} aborted.");
		ActiveMission = null;
	}

	/// <summary>Moves to the next Sol. On hazard days pauses the game and raises HazardTriggered.</summary>
	public void AdvanceDay()
	{
		CurrentDay = Mathf.Min(CurrentDay + 1, FinalDay);
		DayClock = 0f;
		EmitSignal(SignalName.DayChanged, CurrentDay);
		EmitSignal(SignalName.StatsChanged);

		if (IsHazardDay(CurrentDay))
		{
			GetTree().Paused = true;
			EmitSignal(SignalName.HazardTriggered, CurrentDay);
		}
	}

	/// <summary>Called by the UI after the player picks a Trade-Off option.</summary>
	public void ResolveHazard(string resultMessage)
	{
		GetTree().Paused = false;
		EmitSignal(SignalName.StatsChanged);
		if (!string.IsNullOrEmpty(resultMessage))
			PostMessage(resultMessage);

		if (Hp <= 0f)
			EndGame(false, $"Vital signs lost on Sol {CurrentDay} after the hazard response.");
	}

	public void EndGame(bool victory, string summary)
	{
		if (!IsRunning)
			return;
		IsRunning = false;
		ActiveMission = null;
		GetTree().Paused = true;
		EmitSignal(SignalName.GameEnded, victory, summary);
	}

	/// <summary>Resets every variable to its default and reloads the current scene.</summary>
	public void RestartRun()
	{
		CurrentDay = 1;
		SciencePoints = 0;
		MissionElapsed = 0f;
		DayClock = 0f;
		OxygenDrainRate = DefaultOxygenDrainRate;
		PowerDrainRate = DefaultPowerDrainRate;
		_maxPower = DefaultMaxPower;
		_hp = MaxHp;
		_oxygen = MaxOxygen;
		_power = DefaultMaxPower;
		O2Canisters = DefaultO2Canisters;
		EquippedTool = Tool.None;
		ActiveMission = null;
		IsInsideHub = false;
		ActiveVehicle = null;
		_noPowerWarned = _lowOxygenWarned = _suffocatingWarned = false;
		IsRunning = true;

		GetTree().Paused = false;
		GetTree().ReloadCurrentScene();
	}

	// ======================================================================
	//  Pressurized base, tools, consumables
	// ======================================================================

	/// <summary>Called by the base interior HubZone Area3D.</summary>
	public void SetInsideHub(bool inside)
	{
		if (IsInsideHub == inside)
			return;
		IsInsideHub = inside;
		EmitSignal(SignalName.HubStateChanged, inside);
		EmitSignal(SignalName.StatsChanged);
		if (IsRunning)
			PostMessage(inside ? "Back inside the base - O2 stable." : "EVA started - suit O2 is draining.");
	}

	/// <summary>Called by RoverController when the player boards (rover) or gets out (null).</summary>
	public void SetActiveVehicle(RoverController rover)
	{
		ActiveVehicle = rover;
		EmitSignal(SignalName.StatsChanged);
	}

	public void EquipTool(Tool tool)
	{
		if (!IsRunning)
			return;
		EquippedTool = tool;
		EmitSignal(SignalName.ToolChanged, (int)tool);
	}

	public void UseO2Canister()
	{
		if (!IsRunning)
			return;
		if (O2Canisters <= 0)
		{
			PostMessage("No O2 canisters left. Complete Bio-Dome harvests to refill.");
			return;
		}
		if (Oxygen >= MaxOxygen - 1f)
		{
			PostMessage("Suit O2 is already full.");
			return;
		}
		O2Canisters--;
		Oxygen += CanisterOxygen;
		PostMessage($"O2 canister used (+{CanisterOxygen:0} O2). {O2Canisters} left.");
		EmitSignal(SignalName.StatsChanged);
		EmitSignal(SignalName.ToolChanged, (int)EquippedTool);
	}

	// ======================================================================
	//  Modifiers used by Trade-Off choices
	// ======================================================================

	public void AddScience(int amount) => SciencePoints = Math.Max(0, SciencePoints + amount);
	public void AddHp(float amount) => Hp += amount;
	public void AddOxygen(float amount) => Oxygen += amount;
	public void AddPower(float amount) => Power += amount;
	public void MultiplyOxygenDrain(float multiplier) => OxygenDrainRate *= multiplier;
	public void MultiplyPowerDrain(float multiplier) => PowerDrainRate *= multiplier;

	/// <summary>Scales the player's MoveSpeed (e.g. 0.5 = half speed).</summary>
	public void ScalePlayerSpeed(float multiplier)
	{
		if (Player != null)
			Player.MoveSpeed *= multiplier;
	}

	// ======================================================================
	//  Player / UI plumbing
	// ======================================================================

	public void RegisterPlayer(PlayerController player) => Player = player;

	public void UnregisterPlayer(PlayerController player)
	{
		if (Player == player)
			Player = null;
	}

	public void PostMessage(string text) => EmitSignal(SignalName.MessagePosted, text);

	public void SetInteractPrompt(string prompt) => EmitSignal(SignalName.InteractPromptChanged, prompt ?? "");

	// ======================================================================
	//  Helpers
	// ======================================================================

	public static string ZoneName(Zone zone) => zone switch
	{
		Zone.BioDome => "Plant Bio-Dome",
		Zone.DrillArea => "Drill Site",
		Zone.PowerRoom => "Power Room",
		_ => zone.ToString(),
	};

	public static string ToolName(Tool tool) => tool switch
	{
		Tool.Scanner => "Scanner",
		Tool.Wrench => "Wrench",
		Tool.Drill => "Drill",
		_ => "Hands",
	};

	public static int ToolKey(Tool tool) => (int)tool + 1;

	/// <summary>Sol clock text, e.g. "06:42" (1 real second = 1 mission minute).</summary>
	public string ClockText()
	{
		int minutes = (int)DayClock + 6 * 60; // each Sol starts at 06:00
		return $"{minutes / 60 % 24:00}:{minutes % 60:00}";
	}

	/// <summary>Mission Elapsed Time, "D:HH:MM:SS" in mission time (1 real second = 1 mission minute).</summary>
	public string MetText()
	{
		long s = (long)(MissionElapsed * 60f);
		return $"{s / 86400}:{s / 3600 % 24:00}:{s / 60 % 60:00}:{s % 60:00}";
	}

	private string ScienceRating() => SciencePoints switch
	{
		>= 250 => "Artemis Legend",
		>= 175 => "Distinguished Mission",
		>= 100 => "Mission Success",
		_ => "Survived - limited science return",
	};

	private static string FormatRewards(float oxygen, float power, int science, int canisters)
	{
		var parts = new System.Collections.Generic.List<string>();
		if (oxygen != 0f) parts.Add($"O2 {oxygen:+0;-0}");
		if (power != 0f) parts.Add($"Power {power:+0;-0}");
		if (science != 0) parts.Add($"Science {science:+0;-0}");
		if (canisters != 0) parts.Add($"O2 canister {canisters:+0;-0}");
		return parts.Count > 0 ? "(" + string.Join(", ", parts) + ")" : "";
	}
}

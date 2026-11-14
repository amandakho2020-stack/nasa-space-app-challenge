using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// HUD (stat bars, tool wheel, minimap), module minigame hook, Trade-Off popup and end screen.
/// Attach to a CanvasLayer. Child nodes are looked up by Unique Name (%Name), so their position
/// in the tree does not matter as long as "Access as Unique Name" is enabled.
/// </summary>
public partial class UIManager : CanvasLayer
{
	/// <summary>Data for one hazard-day decision.</summary>
	private sealed class TradeOff
	{
		public string Title;
		public string Description;
		public string OptionAText;
		public string OptionBText;
		public Action ApplyA;
		public Action ApplyB;
		public string ResultA;
		public string ResultB;
	}

	private sealed class ToolSlot
	{
		public Panel Diamond;
		public StyleBoxFlat Style;
		public Label Label;
	}

	/// <summary>Height of the top-down minimap camera above the player.</summary>
	[Export] public float MinimapHeight = 80f;

	// ----- Top-left stats -----
	private Label _dayLabel;
	private Label _clockLabel;
	private ProgressBar _hpBar, _o2Bar, _powerBar;
	private Label _hpValue, _o2Value, _powerValue;
	private StyleBoxFlat _hpFill, _o2Fill, _powerFill;
	private Label _scienceLabel;
	private Label _statusLabel;

	// ----- Center / bottom text -----
	private Label _objectiveLabel;
	private Label _promptLabel;
	private Label _messageLabel;

	// ----- Bottom-left tool wheel -----
	private Control _toolWheel;
	private Label _toolNameLabel;
	private readonly List<ToolSlot> _toolSlots = new();

	// ----- Bottom-right minimap -----
	private SubViewport _mapViewport;
	private Camera3D _mapCamera;
	private TextureRect _mapTexture;
	private Node2D _playerArrow;

	// ----- Overlays -----
	private Control _dimmer;
	private MinigamePanel _minigame;
	private Control _tradeOffPanel;
	private Label _tradeOffTitle;
	private Label _tradeOffDescription;
	private Button _optionAButton;
	private Button _optionBButton;
	private Control _endPanel;
	private Label _endTitle;
	private Label _endDescription;
	private Button _restartButton;

	private Dictionary<int, TradeOff> _tradeOffs;
	private TradeOff _activeTradeOff;
	private Tween _messageTween;

	private static readonly Color TextNormal = new(0.85f, 0.95f, 1f);
	private static readonly Color Warning = new(1f, 0.7f, 0.2f);
	private static readonly Color Critical = new(1f, 0.3f, 0.25f);
	private static readonly Color Safe = new(0.45f, 1f, 0.6f);
	private static readonly Color HpColor = new(0.95f, 0.95f, 0.95f);
	private static readonly Color O2Color = new(0.35f, 0.85f, 1f);
	private static readonly Color PowerColor = new(1f, 0.8f, 0.2f);

	public override void _Ready()
	{
		// The tree is paused while the Trade-Off popup is open; the UI must keep processing.
		ProcessMode = ProcessModeEnum.Always;

		_dayLabel = GetNode<Label>("%DayLabel");
		_clockLabel = GetNode<Label>("%ClockLabel");
		_hpBar = GetNode<ProgressBar>("%HpBar");
		_o2Bar = GetNode<ProgressBar>("%O2Bar");
		_powerBar = GetNode<ProgressBar>("%PowerBar");
		_hpValue = GetNode<Label>("%HpValue");
		_o2Value = GetNode<Label>("%O2Value");
		_powerValue = GetNode<Label>("%PowerValue");
		_scienceLabel = GetNode<Label>("%ScienceLabel");
		_statusLabel = GetNode<Label>("%StatusLabel");

		_objectiveLabel = GetNode<Label>("%ObjectiveLabel");
		_promptLabel = GetNode<Label>("%PromptLabel");
		_messageLabel = GetNode<Label>("%MessageLabel");

		_toolWheel = GetNode<Control>("%ToolWheel");

		_mapViewport = GetNode<SubViewport>("%MinimapViewport");
		_mapCamera = GetNode<Camera3D>("%MinimapCamera");
		_mapTexture = GetNode<TextureRect>("%MinimapTexture");
		_playerArrow = GetNode<Node2D>("%PlayerArrow");

		_dimmer = GetNode<Control>("%Dimmer");
		_minigame = GetNode<MinigamePanel>("%MinigamePanel");
		_tradeOffPanel = GetNode<Control>("%TradeOffPanel");
		_tradeOffTitle = GetNode<Label>("%TradeOffTitle");
		_tradeOffDescription = GetNode<Label>("%TradeOffDescription");
		_optionAButton = GetNode<Button>("%OptionAButton");
		_optionBButton = GetNode<Button>("%OptionBButton");
		_endPanel = GetNode<Control>("%EndPanel");
		_endTitle = GetNode<Label>("%EndTitle");
		_endDescription = GetNode<Label>("%EndDescription");
		_restartButton = GetNode<Button>("%RestartButton");

		_hpFill = StyleBar(_hpBar, HpColor);
		_o2Fill = StyleBar(_o2Bar, O2Color);
		_powerFill = StyleBar(_powerBar, PowerColor);
		BuildToolWheel();
		_mapTexture.Texture = _mapViewport.GetTexture();

		_dimmer.Visible = false;
		_tradeOffPanel.Visible = false;
		_endPanel.Visible = false;
		_messageLabel.Text = "";
		_promptLabel.Text = "";

		_optionAButton.Pressed += OnOptionAPressed;
		_optionBButton.Pressed += OnOptionBPressed;
		_restartButton.Pressed += OnRestartPressed;
		_tradeOffPanel.VisibilityChanged += UpdateMouseMode;
		_endPanel.VisibilityChanged += UpdateMouseMode;
		_minigame.VisibilityChanged += UpdateMouseMode;

		BuildTradeOffs();

		var gm = GameManager.Instance;
		if (gm == null)
		{
			GD.PushError("UIManager: GameManager autoload not found. Add res://Scripts/GameManager.cs as an Autoload named GameManager.");
			return;
		}

		gm.StatsChanged += RefreshHud;
		gm.DayChanged += OnDayChanged;
		gm.HazardTriggered += OnHazardTriggered;
		gm.MessagePosted += ShowMessage;
		gm.InteractPromptChanged += OnPromptChanged;
		gm.GameEnded += OnGameEnded;
		gm.MinigameRequested += OnMinigameRequested;
		gm.ToolChanged += OnToolChanged;

		RefreshHud();
		RefreshToolWheel();
		ShowMessage($"Sol 1 of {GameManager.FinalDay}. Head through the corridors to the {GameManager.ZoneName(gm.ObjectiveZone)}. " +
			"Tools: [1] Scanner  [2] Wrench  [3] Drill  [4] O2 canister.");
	}

	public override void _ExitTree()
	{
		// The GameManager autoload outlives this scene (e.g. on restart), so unsubscribe.
		var gm = GameManager.Instance;
		if (gm == null)
			return;
		gm.StatsChanged -= RefreshHud;
		gm.DayChanged -= OnDayChanged;
		gm.HazardTriggered -= OnHazardTriggered;
		gm.MessagePosted -= ShowMessage;
		gm.InteractPromptChanged -= OnPromptChanged;
		gm.GameEnded -= OnGameEnded;
		gm.MinigameRequested -= OnMinigameRequested;
		gm.ToolChanged -= OnToolChanged;
	}

	public override void _Process(double delta)
	{
		// Minimap follows the player, north-up; the arrow shows which way the astronaut faces.
		var player = GameManager.Instance?.Player;
		if (player == null)
			return;
		Vector3 p = player.GlobalPosition;
		_mapCamera.GlobalPosition = new Vector3(p.X, p.Y + MinimapHeight, p.Z);
		_playerArrow.Rotation = -player.FacingYaw;
	}

	// ======================================================================
	//  NASA hazard Trade-Offs
	// ======================================================================

	private void BuildTradeOffs()
	{
		GameManager gm() => GameManager.Instance;

		_tradeOffs = new Dictionary<int, TradeOff>
		{
			[2] = new TradeOff
			{
				Title = "NASA ALERT - SOL 2: MUSCLE ATROPHY",
				Description =
					"In 1/6 g your muscles and bones barely work. Without countermeasures astronauts can lose " +
					"around 1% of bone density per month and significant leg muscle mass. The Flight Surgeon wants " +
					"two hours of resistance exercise every Sol - two hours taken from the science schedule.",
				OptionAText = "Skip exercise, push the science\n-50% Move Speed  |  +50 Science",
				OptionBText = "Follow the exercise protocol\n-25 Oxygen  |  -15 Power",
				ApplyA = () => { gm().ScalePlayerSpeed(0.5f); gm().AddScience(50); },
				ApplyB = () => { gm().AddOxygen(-25f); gm().AddPower(-15f); },
				ResultA = "Science data logged, but your legs feel like lead. Movement speed halved.",
				ResultB = "Exercise complete. Heavy breathing and the treadmill cost O2 and power, but you stay mobile.",
			},
			[5] = new TradeOff
			{
				Title = "NASA ALERT - SOL 5: REGOLITH STORM",
				Description =
					"Electrostatically charged lunar dust is being lofted across the outpost. Regolith grains are " +
					"jagged like broken glass: they scour solar panels, jam seals and damage lungs. The storm is a " +
					"rare chance to study dust transport - if you accept the damage.",
				OptionAText = "Leave panels deployed, record dust data\nMax Power -> 60  |  +40 Science",
				OptionBText = "EVA to stow the solar panels\n-30 Oxygen  |  O2 drain +25% (seal wear)",
				ApplyA = () => { gm().MaxPower = 60f; gm().AddScience(40); },
				ApplyB = () => { gm().AddOxygen(-30f); gm().MultiplyOxygenDrain(1.25f); },
				ResultA = "Dust data captured. Abraded panels can now only hold 60% charge.",
				ResultB = "Panels saved, but grit in the suit seals means you leak O2 faster from now on.",
			},
			[8] = new TradeOff
			{
				Title = "NASA ALERT - SOL 8: SOLAR FLARE",
				Description =
					"Space Weather forecasters report an X-class flare. A Solar Particle Event will reach the Moon " +
					"within minutes. With no atmosphere or magnetic field, the surface radiation dose can be severe. " +
					"The instruments could capture once-in-a-mission data... if someone stays with them.",
				OptionAText = "Shelter behind regolith shielding\n-40 Power  |  -10 Oxygen",
				OptionBText = "Keep the instruments running\n+75 Science  |  -35 HP  |  -20% Move Speed",
				ApplyA = () => { gm().AddPower(-40f); gm().AddOxygen(-10f); },
				ApplyB = () => { gm().AddScience(75); gm().AddHp(-35f); gm().ScalePlayerSpeed(0.8f); },
				ResultA = "The crew rode out the flare safely. Shielding and scrubbers drained the batteries.",
				ResultB = "Incredible particle data - but the radiation dose leaves you weakened and slow.",
			},
		};
	}

	private void OnHazardTriggered(int day)
	{
		if (!_tradeOffs.TryGetValue(day, out _activeTradeOff))
		{
			GameManager.Instance.ResolveHazard(null);
			return;
		}

		_tradeOffTitle.Text = _activeTradeOff.Title;
		_tradeOffDescription.Text = _activeTradeOff.Description;
		_optionAButton.Text = _activeTradeOff.OptionAText;
		_optionBButton.Text = _activeTradeOff.OptionBText;

		_promptLabel.Text = "";
		_dimmer.Visible = true;
		_tradeOffPanel.Visible = true;
	}

	private void OnOptionAPressed() => ResolveTradeOff(isOptionA: true);
	private void OnOptionBPressed() => ResolveTradeOff(isOptionA: false);

	private void ResolveTradeOff(bool isOptionA)
	{
		if (_activeTradeOff == null)
			return;

		TradeOff tradeOff = _activeTradeOff;
		_activeTradeOff = null;

		(isOptionA ? tradeOff.ApplyA : tradeOff.ApplyB)?.Invoke();

		_tradeOffPanel.Visible = false;
		_dimmer.Visible = false;

		GameManager.Instance.ResolveHazard(isOptionA ? tradeOff.ResultA : tradeOff.ResultB);
	}

	// ======================================================================
	//  Module minigame hook
	// ======================================================================

	private void OnMinigameRequested(MissionConsole console)
	{
		_promptLabel.Text = "";
		_minigame.Open(console);
	}

	// ======================================================================
	//  HUD
	// ======================================================================

	private void RefreshHud()
	{
		var gm = GameManager.Instance;
		if (gm == null)
			return;

		_dayLabel.Text = $"SOL {gm.CurrentDay} / {GameManager.FinalDay}";
		_clockLabel.Text = $"Sol Time {gm.ClockText()}";

		SetBar(_hpBar, _hpValue, _hpFill, gm.Hp, GameManager.MaxHp, HpColor, $"{gm.Hp:0}%");
		SetBar(_o2Bar, _o2Value, _o2Fill, gm.Oxygen, GameManager.MaxOxygen, O2Color, $"{gm.Oxygen:0}%");
		SetBar(_powerBar, _powerValue, _powerFill, gm.Power, 100f, PowerColor,
			gm.MaxPower < 100f ? $"{gm.Power:0}/{gm.MaxPower:0}" : $"{gm.Power:0}%");

		_scienceLabel.Text = $"SCIENCE  {gm.SciencePoints} pts";

		if (gm.IsInsideHub && gm.Power <= 0f)
		{
			_statusLabel.Text = "BASE - LIFE SUPPORT OFFLINE";
			_statusLabel.Modulate = Critical;
		}
		else if (gm.RoverProtecting)
		{
			_statusLabel.Text = $"ROVER {Mathf.Abs(gm.ActiveVehicle.ForwardSpeed):0.0} m/s - O2 SUPPLIED";
			_statusLabel.Modulate = Safe;
		}
		else if (gm.IsInsideHub)
		{
			_statusLabel.Text = "BASE - O2 STABLE";
			_statusLabel.Modulate = Safe;
		}
		else
		{
			_statusLabel.Text = $"EVA - O2 DRAINING  -{gm.OxygenDrainRate * (gm.Power <= 0f ? gm.NoPowerOxygenMultiplier : 1f):0.0}/s";
			_statusLabel.Modulate = Warning;
		}

		_objectiveLabel.Text = gm.IsRunning
			? $"OBJECTIVE: Repair the {GameManager.ZoneName(gm.ObjectiveZone)}{ObjectiveBearing(gm)}"
			: "";
	}

	/// <summary>" (175 m S)" style hint toward today's console when it is far away (e.g. the remote Drill Site).</summary>
	private string ObjectiveBearing(GameManager gm)
	{
		if (gm.Player == null)
			return "";
		Vector3 from = gm.Player.GlobalPosition;
		MissionConsole target = null;
		foreach (Node n in GetTree().GetNodesInGroup(PlayerController.InteractableGroup))
			if (n is MissionConsole c && c.Zone == gm.ObjectiveZone)
				target = c;
		if (target == null)
			return "";
		Vector3 d = target.GlobalPosition - from;
		d.Y = 0;
		float dist = d.Length();
		if (dist < 25f)
			return "";
		string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
		float deg = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), 360f); // 0 = north (-Z), 90 = east (+X)
		return $"  ({dist:0} m {names[Mathf.RoundToInt(deg / 45f) % 8]})";
	}

	private static void SetBar(ProgressBar bar, Label value, StyleBoxFlat fill, float current, float max, Color normal, string text)
	{
		bar.MaxValue = max;
		bar.Value = current;
		value.Text = text;
		float ratio = max > 0f ? current / max : 0f;
		fill.BgColor = ratio <= 0.15f ? Critical : ratio <= 0.3f ? Warning : normal;
	}

	/// <summary>Gives a ProgressBar a flat dark track and a colored fill; returns the fill style.</summary>
	private static StyleBoxFlat StyleBar(ProgressBar bar, Color color)
	{
		var track = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.12f) };
		track.SetCornerRadiusAll(3);
		var fill = new StyleBoxFlat { BgColor = color };
		fill.SetCornerRadiusAll(3);
		bar.AddThemeStyleboxOverride("background", track);
		bar.AddThemeStyleboxOverride("fill", fill);
		bar.ShowPercentage = false;
		return fill;
	}

	private void OnDayChanged(int newDay)
	{
		RefreshHud();
		if (!GameManager.Instance.IsHazardDay(newDay))
			ShowMessage($"Sol {newDay} begins. Objective: {GameManager.ZoneName(GameManager.Instance.ObjectiveZone)}.");
	}

	private void OnPromptChanged(string prompt) => _promptLabel.Text = prompt;

	private void ShowMessage(string text)
	{
		_messageTween?.Kill();
		_messageLabel.Text = text;
		_messageLabel.Modulate = Colors.White;

		_messageTween = CreateTween();
		_messageTween.TweenInterval(5.0);
		_messageTween.TweenProperty(_messageLabel, "modulate:a", 0.0f, 1.0);
	}

	// ======================================================================
	//  Diamond tool wheel (built in code inside %ToolWheel)
	// ======================================================================

	private void BuildToolWheel()
	{
		const float slotSize = 76f;
		const float diamondSize = 54f;
		const float spread = 64f;
		Vector2 center = _toolWheel.CustomMinimumSize / 2f;

		// top, right, bottom, left — like a d-pad
		Vector2[] offsets = { new(0, -spread), new(spread, 0), new(0, spread), new(-spread, 0) };

		for (int i = 0; i < 4; i++)
		{
			var holder = new Control
			{
				Position = center + offsets[i] - new Vector2(slotSize, slotSize) / 2f,
				Size = new Vector2(slotSize, slotSize),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};

			var style = new StyleBoxFlat
			{
				BgColor = new Color(0.05f, 0.1f, 0.35f, 0.9f),
				BorderColor = new Color(0.35f, 0.65f, 1f),
			};
			style.SetBorderWidthAll(2);
			style.SetCornerRadiusAll(6);

			var diamond = new Panel
			{
				Position = new Vector2(slotSize - diamondSize, slotSize - diamondSize) / 2f,
				Size = new Vector2(diamondSize, diamondSize),
				PivotOffset = new Vector2(diamondSize, diamondSize) / 2f,
				Rotation = Mathf.Pi / 4f,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			diamond.AddThemeStyleboxOverride("panel", style);

			var label = new Label
			{
				Size = new Vector2(slotSize, slotSize),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			label.AddThemeFontSizeOverride("font_size", 12);
			label.AddThemeColorOverride("font_outline_color", Colors.Black);
			label.AddThemeConstantOverride("outline_size", 3);

			holder.AddChild(diamond);
			holder.AddChild(label);
			_toolWheel.AddChild(holder);
			_toolSlots.Add(new ToolSlot { Diamond = diamond, Style = style, Label = label });
		}

		_toolNameLabel = new Label
		{
			Position = center - new Vector2(40, 10),
			Size = new Vector2(80, 20),
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_toolNameLabel.AddThemeFontSizeOverride("font_size", 10);
		_toolWheel.AddChild(_toolNameLabel);
	}

	private void OnToolChanged(int tool) => RefreshToolWheel();

	private void RefreshToolWheel()
	{
		var gm = GameManager.Instance;
		if (gm == null)
			return;

		string[] names = { "SCAN", "WRENCH", "DRILL", $"O2 x{gm.O2Canisters}" };
		for (int i = 0; i < _toolSlots.Count; i++)
		{
			ToolSlot slot = _toolSlots[i];
			bool equipped = i < 3 && (int)gm.EquippedTool == i;
			slot.Label.Text = $"{names[i]}\n[{i + 1}]";
			slot.Style.BgColor = equipped ? new Color(0.95f, 0.5f, 0.1f, 0.95f) : new Color(0.05f, 0.1f, 0.35f, 0.9f);
			slot.Style.BorderColor = equipped ? Colors.White : new Color(0.35f, 0.65f, 1f);
			if (i == 3)
				slot.Diamond.Modulate = gm.O2Canisters > 0 ? Colors.White : new Color(1f, 1f, 1f, 0.4f);
			slot.Diamond.QueueRedraw();
		}
		_toolNameLabel.Text = gm.EquippedTool == Tool.None ? "" : GameManager.ToolName(gm.EquippedTool).ToUpper();
	}

	// ======================================================================
	//  Mouse + end screen
	// ======================================================================

	/// <summary>Free the cursor whenever an overlay is open; capture it for camera orbit otherwise.</summary>
	private void UpdateMouseMode()
	{
		bool overlayOpen = _tradeOffPanel.Visible || _endPanel.Visible || _minigame.Visible;
		Input.MouseMode = overlayOpen ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}

	private void OnGameEnded(bool victory, string summary)
	{
		_minigame.Close();
		_tradeOffPanel.Visible = false;
		_promptLabel.Text = "";
		_objectiveLabel.Text = "";

		_endTitle.Text = victory ? "MISSION COMPLETE" : "MISSION FAILED";
		_endTitle.Modulate = victory ? Safe : Critical;
		_endDescription.Text = summary;

		_dimmer.Visible = true;
		_endPanel.Visible = true;
		_restartButton.GrabFocus();
	}

	private void OnRestartPressed()
	{
		GameManager.Instance?.RestartRun();
	}
}

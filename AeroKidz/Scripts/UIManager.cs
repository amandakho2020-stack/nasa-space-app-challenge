using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// UI MANAGER - the ONLY script that touches HUD / pop-up / overlay nodes.
/// Attach to: Main/UI (CanvasLayer).
///
/// RULES FOR THE UI DESIGNER:
///   * Every node this script uses has "Access as Unique Name" ON (shows a % in the Scene tree).
///   * You may MOVE, RESTYLE, RE-PARENT or wrap those nodes in containers freely: code finds them by
///     their unique name, not by path. Just DON'T RENAME them and DON'T CHANGE THEIR NODE TYPE.
///
/// Data flow:  GameManager / LevelController / BaseMap --signals--> UIManager --buttons--> LevelController
/// </summary>
public partial class UIManager : CanvasLayer
{
    private GameManager _gm;
    private LevelController _lc;
    private BaseMap _map;

    // ---- Top HUD ----
    private ProgressBar _oxygenBar, _powerBar, _shieldBar;
    private Label _oxygenValue, _powerValue, _shieldValue;
    private Label _scienceLabel, _dayLabel, _timerLabel, _objectiveLabel;
    private Control _dayPanel, _objectiveBanner;
    private DayBadge _dayBadge;

    // ---- Rocket clock (top centre) + 2-minute warning ----
    private Control _rocketPanel, _rocketWarning;
    private Label _rocketClockLabel, _rocketWarningLabel;
    private double _rocketWarningTimeLeft;
    private float _rocketSeconds = LevelController.RocketClockSeconds;

    // ---- QUIT & SAVE (only after the safe choice, during the final game) ----
    private Button _quitSaveButton, _leaveButton;

    // ---- Nova (robot helper that pops up and talks) ----
    private NovaPopup _nova;

    // ---- NASA fact / day briefing pop-up ----
    private Control _infoPanel;
    private Label _infoTag, _infoTitle, _infoBody;
    private Button _infoButton;

    // ---- Hazard alert (trade-off) ----
    private Control _tradeOffPanel;
    private Label _tradeOffTitle, _tradeOffMessage, _tradeOffDrop;
    private Button _optionAButton, _optionBButton;

    // ---- End-of-mission overlays ----
    private Control _missionFailedScreen, _levelCompleteScreen, _gameBeatenScreen;
    private Label _failReasonLabel, _completeSummaryLabel, _finalScoreLabel;
    private Button _retryButton, _failMapButton, _nextMissionButton, _completeMapButton, _playAgainButton;

    private double _time;

    /// <summary>Find a node by its Unique Name (the % in the editor).</summary>
    private T U<T>(string uniqueName) where T : Node => GetNode<T>("%" + uniqueName);

    // ==================================================================
    // SETUP
    // ==================================================================
    public override void _Ready()
    {
        Art.UseKidFont(GetParent()); // the kid font for the AeroKidz screens only (not the 3D team's boards)
        _gm = GameManager.Instance;
        _lc = U<LevelController>("LevelController");
        _map = U<BaseMap>("MapLayer");

        _oxygenBar = U<ProgressBar>("OxygenBar");
        _powerBar = U<ProgressBar>("PowerBar");
        _shieldBar = U<ProgressBar>("ShieldBar");
        _oxygenValue = U<Label>("OxygenValue");
        _powerValue = U<Label>("PowerValue");
        _shieldValue = U<Label>("ShieldValue");
        _scienceLabel = U<Label>("ScienceLabel");
        _dayPanel = U<Control>("DayPanel");
        _dayLabel = U<Label>("DayLabel");
        _timerLabel = U<Label>("TimerLabel");
        // the drawn day badge replaces the plain "Day 1: ..." text box
        _dayLabel.GetParent<Control>().Hide();
        _dayPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _dayBadge = new DayBadge();
        _dayPanel.AddChild(_dayBadge);
        _objectiveBanner = U<Control>("ObjectiveBanner");
        _objectiveLabel = U<Label>("ObjectiveLabel");
        _nova = U<NovaPopup>("Nova");
        _rocketPanel = U<Control>("RocketPanel");
        _rocketClockLabel = U<Label>("RocketClockLabel");
        _rocketWarning = U<Control>("RocketWarning");
        _rocketWarningLabel = U<Label>("RocketWarningLabel");
        _quitSaveButton = U<Button>("QuitSaveButton");
        _leaveButton = U<Button>("LeaveButton");

        _infoPanel = U<Control>("InfoPanel");
        _infoTag = U<Label>("InfoTag");
        _infoTitle = U<Label>("InfoTitle");
        _infoBody = U<Label>("InfoBody");
        _infoButton = U<Button>("InfoButton");

        _tradeOffPanel = U<Control>("TradeOffPanel");
        _tradeOffTitle = U<Label>("TradeOffTitle");
        _tradeOffMessage = U<Label>("TradeOffMessage");
        _tradeOffDrop = U<Label>("TradeOffDrop");
        _optionAButton = U<Button>("OptionAButton");
        _optionBButton = U<Button>("OptionBButton");

        _missionFailedScreen = U<Control>("MissionFailedScreen");
        _failReasonLabel = U<Label>("FailReasonLabel");
        _retryButton = U<Button>("RetryButton");
        _failMapButton = U<Button>("FailMapButton");

        _levelCompleteScreen = U<Control>("LevelCompleteScreen");
        _completeSummaryLabel = U<Label>("CompleteSummaryLabel");
        _nextMissionButton = U<Button>("NextMissionButton");
        _completeMapButton = U<Button>("CompleteMapButton");

        _gameBeatenScreen = U<Control>("GameBeatenScreen");
        _finalScoreLabel = U<Label>("FinalScoreLabel");
        _playAgainButton = U<Button>("PlayAgainButton");

        // ---- Buttons -> LevelController ----
        _infoButton.Pressed += () => { _infoPanel.Hide(); _lc.OnInfoAcknowledged(); };
        _optionAButton.Pressed += () => { _tradeOffPanel.Hide(); _lc.OnChoiceMade(true); };
        _optionBButton.Pressed += () => { _tradeOffPanel.Hide(); _lc.OnChoiceMade(false); };
        _retryButton.Pressed += () => _lc.RetryMission();
        _failMapButton.Pressed += () => _lc.ReturnToMap();
        _nextMissionButton.Pressed += () => _lc.NextMission();
        _completeMapButton.Pressed += () => _lc.ReturnToMap();
        _playAgainButton.Pressed += () => _lc.RestartCampaign();
        _quitSaveButton.Pressed += () => _lc.SaveAndExit();
        _leaveButton.Pressed += () => _lc.LeaveToOutpost();

        // ---- Base map ----
        // (the map's RoomEntered / BuildingInfo signals are handled by LevelController)

        // ---- Signals -> UI updates ----
        _gm.ResourcesChanged += OnResourcesChanged;
        _gm.ScienceChanged += OnScienceChanged;

        _lc.DayStarted += OnDayStarted;
        _lc.ObjectiveChanged += OnObjectiveChanged;
        _lc.TimerUpdated += OnTimerUpdated;
        _lc.RocketClockUpdated += OnRocketClockUpdated;
        _lc.RocketWarning += OnRocketWarning;
        _lc.InfoRequested += OnInfoRequested;
        _lc.ChoiceRequested += OnChoiceRequested;
        _lc.MinigameStarted += OnMinigameStarted;
        _lc.NovaSays += (message, speak) =>
        {
            if (string.IsNullOrEmpty(message)) _nova.ClearTips(); // an empty tip = remove the old hint
            else _nova.Tip(message, speak);
        };
        _lc.NovaDialog += message => _nova.Dialog(message);
        _nova.DialogClosed += () => _lc.OnNovaDialogDone();
        _lc.LevelFailed += OnLevelFailed;
        _lc.LevelCompleted += OnLevelCompleted;
        _lc.GameBeaten += OnGameBeaten;
        _lc.ReturnedToMap += ShowMap;
        _lc.WalkStarted += OnWalkStarted;

        // ---- Initial state ----
        OnResourcesChanged(_gm.Oxygen, _gm.Power, _gm.Shield, _gm.MaxPower);
        OnScienceChanged(_gm.TotalScience);
        ShowMap(); // LevelController then opens the opening story pop-up
    }

    public override void _ExitTree()
    {
        // GameManager is an Autoload that outlives this scene, so unsubscribe.
        if (_gm != null)
        {
            _gm.ResourcesChanged -= OnResourcesChanged;
            _gm.ScienceChanged -= OnScienceChanged;
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;

        // Low-resource warning pulse (only while a minigame is running).
        bool playing = _lc.CurrentPhase == LevelController.Phase.Playing;
        _quitSaveButton.Visible = false; // SAVE & EXIT is switched off for now (LevelController.SaveAndExit still works)
        _leaveButton.Visible = _lc.CanLeaveToOutpost;
        PulseIfLow(_oxygenBar, playing);
        PulseIfLow(_powerBar, playing);
        PulseIfLow(_shieldBar, playing && !_gm.ShieldOffline);

        // Hazard alert title flashes red/yellow.
        if (_tradeOffPanel.Visible)
        {
            // the hazard title flashes and pulses so nobody can miss it
            _tradeOffTitle.Modulate = Mathf.Sin((float)_time * 8f) > 0f ? Colors.White : new Color(1f, 0.55f, 0.5f);
            _tradeOffTitle.PivotOffset = _tradeOffTitle.Size / 2f;
            float s = 1f + 0.05f * Mathf.Sin((float)_time * 6f);
            _tradeOffTitle.Scale = new Vector2(s, s);
        }

        // Nothing overlaps: Nova hides while any pop-up, alert or repair panel is on screen.
        _nova.Blocked = _infoPanel.Visible || _tradeOffPanel.Visible || _missionFailedScreen.Visible
                        || _levelCompleteScreen.Visible || _gameBeatenScreen.Visible || _lc.IsRepairOpen;

        // Rocket clock turns red and flashes in the last 2 minutes.
        if (_rocketSeconds <= LevelController.RocketWarningAt)
            _rocketClockLabel.Modulate = Mathf.Sin((float)_time * 6f) > 0f ? new Color(1f, 0.3f, 0.25f) : Colors.White;
        else
            _rocketClockLabel.Modulate = Colors.White;

        // 2-minute warning banner (stays a few seconds, does not pause the game).
        if (_rocketWarningTimeLeft > 0)
        {
            _rocketWarningTimeLeft -= delta;
            _rocketWarning.Modulate = new Color(1, 1, 1, Mathf.Clamp((float)_rocketWarningTimeLeft, 0f, 1f));
            if (_rocketWarningTimeLeft <= 0)
                _rocketWarning.Hide();
        }
    }

    private void OnRocketClockUpdated(float secondsLeft)
    {
        _rocketSeconds = secondsLeft;
        int total = Mathf.CeilToInt(secondsLeft);
        _rocketClockLabel.Text = $"ROCKET TO EARTH  {total / 60:00}:{total % 60:00}";
    }

    private void OnRocketWarning(string message)
    {
        _rocketWarningLabel.Text = message;
        _rocketWarning.Show();
        _rocketWarningTimeLeft = 6.0;
        _nova.Tip("Two minutes left! Hurry!");
    }

    // ==================================================================
    // HUD
    // ==================================================================
    private void OnResourcesChanged(float oxygen, float power, float shield, float maxPower)
    {
        _oxygenBar.Value = oxygen;
        _powerBar.Value = power;
        _shieldBar.Value = shield;

        _oxygenValue.Text = $"{oxygen:0}%";
        _powerValue.Text = maxPower < GameManager.MaxResource ? $"{power:0}% (MAX {maxPower:0})" : $"{power:0}%";
        _shieldValue.Text = _gm.ShieldOffline ? "OFFLINE" : $"{shield:0}%";
    }

    private void OnScienceChanged(int totalScience) => _scienceLabel.Text = $"SCIENCE: {totalScience}";

    private void OnDayStarted(int day, string dayTitle, string objective)
    {
        HideAllOverlays();
        _map.Show(); // the base sits behind the pop-ups until the minigame starts
        _dayPanel.Show();
        _rocketPanel.Visible = LevelController.RocketClockOn;
        _dayLabel.Text = $"Day {day}: {dayTitle}";
        _dayBadge.Set(day, dayTitle);
        OnObjectiveChanged(objective);
        _timerLabel.Text = "";
        _dayBadge.SetTimer("");
    }

    private void OnObjectiveChanged(string objective)
    {
        _objectiveLabel.Text = objective;
        _objectiveBanner.Visible = !string.IsNullOrEmpty(objective);
    }

    /// <summary>Walking through the outpost during a mission: show the map (the HUD stays on top).</summary>
    private void OnWalkStarted(string objective)
    {
        HideAllOverlays();
        _map.Show();
        _dayPanel.Show();
        OnObjectiveChanged(objective);
        _timerLabel.Text = "";
        _dayBadge.SetTimer("");
    }

    private void OnMinigameStarted()
    {
        _map.Hide(); // the minigame draws its own full-screen scene
    }

    private void OnTimerUpdated(float secondsLeft)
    {
        if (secondsLeft < 0f)
        {
            _timerLabel.Text = ""; // no time limit
            _dayBadge.SetTimer("");
            return;
        }
        int total = Mathf.CeilToInt(secondsLeft);
        _timerLabel.Text = GameManager.TimersEnabled
            ? $"T-MINUS {total / 60:00}:{total % 60:00}"
            : $"TIME LEFT {total / 60:00}:{total % 60:00}";
        _dayBadge.SetTimer(_timerLabel.Text);
    }

    private void PulseIfLow(ProgressBar bar, bool enabled)
    {
        if (enabled && bar.Value < 25)
        {
            float t = 0.5f + 0.5f * Mathf.Sin((float)_time * 8f);
            bar.Modulate = Colors.White.Lerp(new Color(1f, 0.25f, 0.25f), t);
        }
        else
        {
            bar.Modulate = Colors.White;
        }
    }

    // ---- Nova ----
    private void QueueNovaMessage(string message) => _nova.Tip(message);

    // ==================================================================
    // MAP
    // ==================================================================
    private void ShowMap()
    {
        HideAllOverlays();
        _map.Show();
        _dayPanel.Hide();
        _objectiveBanner.Hide();
        _rocketPanel.Hide();
    }

    // ==================================================================
    // POP-UPS
    // ==================================================================
    private void OnInfoRequested(string tag, string title, string body, string buttonText)
    {
        _infoTag.Text = tag;
        _infoTitle.Text = title;
        _infoBody.Text = body;
        _infoButton.Text = buttonText;
        _infoPanel.Show();
        _infoButton.GrabFocus();
    }

    private void OnChoiceRequested(string title, string bar, int drop, string fact, string optionA, string optionB)
    {
        _tradeOffTitle.Text = title;
        _tradeOffDrop.Text = $"{bar}  -{drop}%";
        _tradeOffMessage.Text = fact;
        _optionAButton.Text = optionA;
        _optionBButton.Text = optionB;
        _tradeOffPanel.Show();
    }

    // ==================================================================
    // END-OF-MISSION SCREENS
    // ==================================================================
    private void OnLevelFailed(string reason)
    {
        HideAllOverlays();
        _failReasonLabel.Text = $"{reason}\nTry again! Your bars go back to 100%.";
        _missionFailedScreen.Show();
    }

    private void OnLevelCompleted(int levelIndex, int totalScience)
    {
        HideAllOverlays();
        string unlocked = levelIndex switch
        {
            0 => "Next: the CORE DRILL!",
            1 => "Next: the TELESCOPE!",
            _ => "Finish all 3 missions to fly home!",
        };
        _completeSummaryLabel.Text =
            $"Mission {levelIndex + 1} done!\nScience points: {totalScience}\n{unlocked}";
        _levelCompleteScreen.Show();
    }

    private void OnGameBeaten(int totalScience)
    {
        HideAllOverlays();
        BuildScoreRow();
        _currentScoreValue.Text = $"{totalScience}";
        _highScoreValue.Text = $"{_gm.HighScore}";
        _newRecordLabel.Visible = _gm.LastScoreWasRecord;
        _finalScoreLabel.Text =
            $"Garden: {ChoiceText(0)}\n" +
            $"Drill: {ChoiceText(1)}\n" +
            $"Sun: {ChoiceText(2)}";
        _gameBeatenScreen.Show();
    }

    // ---- CURRENT SCORE / HIGHEST SCORE boxes on the final screen (made in code, once) ----
    private Label _currentScoreValue, _highScoreValue, _newRecordLabel;

    private void BuildScoreRow()
    {
        if (_currentScoreValue != null) return;
        var vbox = _finalScoreLabel.GetParent<VBoxContainer>();

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 24);
        _currentScoreValue = AddScoreBox(row, "CURRENT SCORE", new Color(0.3f, 0.85f, 1f));
        _highScoreValue = AddScoreBox(row, "HIGHEST SCORE", new Color(1f, 0.8f, 0.2f));
        vbox.AddChild(row);
        vbox.MoveChild(row, _finalScoreLabel.GetIndex());

        _newRecordLabel = new Label { Text = "★ NEW HIGH SCORE! ★", HorizontalAlignment = HorizontalAlignment.Center };
        _newRecordLabel.AddThemeFontSizeOverride("font_size", 24);
        _newRecordLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.3f));
        _newRecordLabel.AddThemeColorOverride("font_outline_color", new Color(0.4f, 0.15f, 0f));
        _newRecordLabel.AddThemeConstantOverride("outline_size", 6);
        vbox.AddChild(_newRecordLabel);
        vbox.MoveChild(_newRecordLabel, row.GetIndex() + 1);
    }

    private static Label AddScoreBox(HBoxContainer row, string title, Color color)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(230, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.08f, 0.18f, 0.95f),
            BorderColor = color,
            ShadowColor = new Color(color, 0.35f),
            ShadowSize = 8,
            ContentMarginTop = 10, ContentMarginBottom = 10, ContentMarginLeft = 16, ContentMarginRight = 16,
        };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(16);
        box.AddThemeStyleboxOverride("panel", style);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 0);
        var titleLabel = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        titleLabel.AddThemeFontSizeOverride("font_size", 17);
        titleLabel.AddThemeColorOverride("font_color", color);
        var value = new Label { Text = "0", HorizontalAlignment = HorizontalAlignment.Center };
        value.AddThemeFontSizeOverride("font_size", 46);
        value.AddThemeColorOverride("font_color", Colors.White);
        value.AddThemeColorOverride("font_outline_color", new Color(color, 0.6f));
        value.AddThemeConstantOverride("outline_size", 8);
        col.AddChild(titleLabel);
        col.AddChild(value);
        box.AddChild(col);
        row.AddChild(box);
        return value;
    }

    private string ChoiceText(int level) => _gm.ChoiceHistory[level] switch
    {
        true => "you chose SCIENCE",
        false => "you chose SAFETY",
        _ => "-",
    };

    private void HideAllOverlays()
    {
        _infoPanel.Hide();
        _tradeOffPanel.Hide();
        _missionFailedScreen.Hide();
        _levelCompleteScreen.Hide();
        _gameBeatenScreen.Hide();
        _rocketWarning.Hide();
        _rocketWarningTimeLeft = 0;
    }
}

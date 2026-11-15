using Godot;

namespace AeroKidz;

/// <summary>
/// GLOBAL SINGLETON (Autoload) - "the brain" of Lunar Outpost.
///
/// Registered in project.godot under [autoload] as "GameManager", so it exists before
/// any scene loads and survives scene changes. Access it from ANY script with:
///     GameManager.Instance.Oxygen
///
/// It holds ONLY data + rules (no UI code). The UI listens to its signals, so the
/// UI designer can rebuild the HUD freely without touching this file.
///
/// Day layout (3 days per mission):
///     Mission 1: Day 1, Day 2 (CHOICE), Day 3 (final)
///     Mission 2: Day 4, Day 5 (CHOICE), Day 6 (final)
///     Mission 3: Day 7, Day 8 (CHOICE), Day 9 (gauntlet)
/// </summary>
public partial class GameManager : Node
{
    // ------------------------------------------------------------------
    // SINGLETON ACCESS
    // ------------------------------------------------------------------
    public static GameManager Instance { get; private set; }

    // ------------------------------------------------------------------
    // SIGNALS (the UI subscribes to these - never polls)
    // ------------------------------------------------------------------

    /// <summary>Fired whenever Oxygen / Power / Shield (or the Power cap) changes.</summary>
    [Signal] public delegate void ResourcesChangedEventHandler(float oxygen, float power, float shield, float maxPower);

    /// <summary>Fired whenever the Science score changes.</summary>
    [Signal] public delegate void ScienceChangedEventHandler(int totalScience);

    // ------------------------------------------------------------------
    // CONSTANTS
    // ------------------------------------------------------------------
    public const int TotalLevels = 3;
    public const float MaxResource = 100f;
    public const int DaysPerMission = 3;

    /// <summary>
    /// MASTER TIME SWITCH. false = no minigame time limits and no rocket clock (current team decision).
    /// Set to true to bring back every countdown. (Day 9 still lasts 3:00 - surviving it IS the goal.)
    /// </summary>
    public const bool TimersEnabled = false;

    /// <summary>TESTING: true = all 3 missions can be started from the map right away. Set false for the real game.</summary>
    public const bool UnlockAllForTesting = true;

    /// <summary>Mission 1: water drops to catch, and how many you actually caught (the harvest grows from it).</summary>
    public const int WaterGoal = 15;
    public int WaterCollected { get; set; }

    private const string SavePath = "user://aero_kidz_save.cfg";

    // ------------------------------------------------------------------
    // GLOBAL RESOURCES (0 - 100). Read-only from outside: use ChangeResources().
    // ------------------------------------------------------------------
    public float Oxygen { get; private set; } = MaxResource;
    public float Power { get; private set; } = MaxResource;
    public float Shield { get; private set; } = MaxResource;

    /// <summary>Power can never go above this (always 100 for now; kept so a choice can cap it later).</summary>
    public float MaxPower { get; private set; } = MaxResource;

    // ------------------------------------------------------------------
    // TRADE-OFF MODIFIERS (set by the mid-mission choice, read by minigames)
    // ------------------------------------------------------------------

    /// <summary>Minigame player controllers should multiply their move speed by this.</summary>
    public float PlayerSpeedMultiplier { get; private set; } = 1f;

    /// <summary>Every oxygen drain goes through ApplyDrain(), which multiplies by this.</summary>
    public float OxygenDrainMultiplier { get; private set; } = 1f;

    /// <summary>Mission 2: the HMR heater was used. Power fell to 10% and Day 6 is played in a blackout.</summary>
    public bool Blackout { get; private set; }

    /// <summary>Mission 3: kept studying the Sun, so the Shields drop to 0% when the storm hits on Day 9.</summary>
    public bool StormBreaksShield { get; private set; }

    /// <summary>
    /// While true the Shield is at 0% but does NOT count as a loss yet (Day 9 grace period).
    /// It switches back on by itself as soon as the Shield is raised above 0%.
    /// </summary>
    public bool ShieldOffline { get; private set; }

    /// <summary>True if the player picked the SCIENCE option this mission (whichever button it was).</summary>
    public bool ChoseScience { get; private set; }

    /// <summary>Seconds until the Earth rocket launches (counted by LevelController, read by the HUD and minigames).</summary>
    public float RocketTimeLeft { get; set; }

    // ------------------------------------------------------------------
    // PROGRESSION
    // ------------------------------------------------------------------

    /// <summary>0 = Mission 1, 1 = Mission 2, 2 = Mission 3.</summary>
    public int CurrentLevelIndex { get; private set; }

    /// <summary>Highest mission the player may start (0..2). Complete one to unlock the next.</summary>
    public int HighestUnlockedLevel { get; private set; }

    /// <summary>Global day number (1..9). Set by LevelController.</summary>
    public int CurrentDay { get; set; } = 1;

    // Science is tracked per level so that RETRYING or REPLAYING a mission can't farm points.
    private readonly int[] _bestLevelScience = new int[TotalLevels];
    private readonly bool[] _levelCompleted = new bool[TotalLevels];
    private int _pendingScience; // points earned in the CURRENT attempt (lost on retry)

    /// <summary>The best final score of any finished game (all 3 missions + the rocket). Kept after "Play Again".</summary>
    public int HighScore { get; private set; }

    /// <summary>true when the last finished game beat the old high score.</summary>
    public bool LastScoreWasRecord { get; private set; }

    /// <summary>Called when the rocket has flown home: remember the high score.</summary>
    public void RecordFinalScore(int score)
    {
        LastScoreWasRecord = score > HighScore;
        if (LastScoreWasRecord) HighScore = score;
        SaveProgress();
    }

    /// <summary>
    /// This mission's score right now: the best of an earlier attempt and this attempt. On the FIRST attempt
    /// it is just this attempt, so a penalty (Mission 2 dirty water, -300) can really take points away.
    /// </summary>
    private int MergedScience(int i) =>
        _levelCompleted[i] || _bestLevelScience[i] != 0 ? Mathf.Max(_bestLevelScience[i], _pendingScience) : _pendingScience;

    /// <summary>Total score = best score of every other mission + this attempt.</summary>
    public int TotalScience
    {
        get
        {
            int total = 0;
            for (int i = 0; i < TotalLevels; i++)
                total += i == CurrentLevelIndex ? MergedScience(i) : _bestLevelScience[i];
            return Mathf.Max(0, total); // the total score never goes below 0
        }
    }

    // ==================================================================
    // GODOT LIFECYCLE
    // ==================================================================
    public override void _EnterTree()
    {
        Instance = this;
        // (the kid font is switched on by UIManager when the AeroKidz game opens)
        LoadProgress();
    }

    public override void _Ready()
    {
        // In the team's 3D outpost (res://Main.tscn) the games run on top of the 3D world.
        // Wait one frame so the main scene is loaded, then let World3DBridge join them.
        GetTree().ProcessFrame += AttachTo3DWorldOnce;
    }

    private void AttachTo3DWorldOnce()
    {
        GetTree().ProcessFrame -= AttachTo3DWorldOnce;
        World3DBridge.AttachIfNeeded(GetTree());
    }

    // ==================================================================
    // SAVE / LOAD (best science per mission, completed missions, choices)
    // Stored in Godot's user folder, so closing the game keeps your performance.
    // ==================================================================
    public void SaveProgress()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("progress", "highest_unlocked", HighestUnlockedLevel);
        cfg.SetValue("progress", "high_score", HighScore);
        cfg.SetValue("bars", "oxygen", _startOxygen);   // bars carry over between missions, so save them too
        cfg.SetValue("bars", "power", _startPower);
        cfg.SetValue("bars", "shield", _startShield);
        for (int i = 0; i < TotalLevels; i++)
        {
            cfg.SetValue("progress", $"best_science_{i}", _bestLevelScience[i]);
            cfg.SetValue("progress", $"completed_{i}", _levelCompleted[i]);
            cfg.SetValue("progress", $"choice_{i}", ChoiceHistory[i] switch { true => 1, false => 0, _ => -1 });
        }
        cfg.Save(SavePath);
    }

    /// <summary>
    /// false = every time the game opens it is a NEW game (missions, score and bars start fresh);
    /// only the HIGHEST SCORE is kept. Set true to continue the saved game instead
    /// (e.g. for a "CONTINUE" button on the future home page).
    /// </summary>
    public static bool ResumeSavedGame = false;

    private void LoadProgress()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SavePath) != Error.Ok)
            return;
        HighScore = (int)cfg.GetValue("progress", "high_score", 0); // always kept
        if (!ResumeSavedGame)
            return; // a fresh game: everything else stays at its start value
        HighestUnlockedLevel = Mathf.Clamp((int)cfg.GetValue("progress", "highest_unlocked", 0), 0, TotalLevels - 1);
        Oxygen = _startOxygen = Mathf.Clamp((float)cfg.GetValue("bars", "oxygen", MaxResource), 1f, MaxResource);
        Power = _startPower = Mathf.Clamp((float)cfg.GetValue("bars", "power", MaxResource), 1f, MaxResource);
        Shield = _startShield = Mathf.Clamp((float)cfg.GetValue("bars", "shield", MaxResource), 1f, MaxResource);
        for (int i = 0; i < TotalLevels; i++)
        {
            _bestLevelScience[i] = (int)cfg.GetValue("progress", $"best_science_{i}", 0);
            _levelCompleted[i] = (bool)cfg.GetValue("progress", $"completed_{i}", false);
            int c = (int)cfg.GetValue("progress", $"choice_{i}", -1);
            ChoiceHistory[i] = c < 0 ? null : c == 1;
        }
    }

    public bool AllLevelsCompleted
    {
        get
        {
            for (int i = 0; i < TotalLevels; i++)
                if (!_levelCompleted[i]) return false;
            return true;
        }
    }

    // ==================================================================
    // LEVEL FLOW
    // ==================================================================

    // ---- Bars at the start of the current mission (a retry / an unfinished mission goes back to these) ----
    private float _startOxygen = MaxResource, _startPower = MaxResource, _startShield = MaxResource;

    /// <summary>
    /// Begin a mission from the map. Called by LevelController.StartMission().
    /// The bars are NOT refilled here: they carry over from the last mission.
    /// They only go back to 100% when a mission is FINISHED (CompleteCurrentLevel).
    /// </summary>
    public void StartLevel(int levelIndex)
    {
        CurrentLevelIndex = Mathf.Clamp(levelIndex, 0, TotalLevels - 1);
        _startOxygen = Mathf.Max(Oxygen, 1f);
        _startPower = Mathf.Max(Power, 1f);
        _startShield = Mathf.Max(Shield, 1f);
        ResetLevel();
    }

    /// <summary>
    /// RETRY (or leaving a mission unfinished): puts the current mission back to its starting state.
    /// Bars go back to what they were when this mission STARTED (not 100%), modifiers cleared,
    /// this attempt's science discarded.
    /// </summary>
    public void ResetLevel()
    {
        Oxygen = _startOxygen;
        Power = _startPower;
        Shield = _startShield;
        MaxPower = MaxResource;

        PlayerSpeedMultiplier = 1f;
        OxygenDrainMultiplier = 1f;
        Blackout = false;
        StormBreaksShield = false;
        ShieldOffline = false;
        ChoseScience = false;

        _pendingScience = 0;
        WaterCollected = 0;
        CurrentDay = FirstDayOfLevel(CurrentLevelIndex);

        EmitResources();
        EmitSignal(SignalName.ScienceChanged, TotalScience);
    }

    /// <summary>
    /// Marks the current mission as won (every task done) and unlocks the next.
    /// This is the ONLY place the bars are refilled to 100%. Returns true if it was the final mission.
    /// </summary>
    public bool CompleteCurrentLevel()
    {
        RefillAll();
        _bestLevelScience[CurrentLevelIndex] = MergedScience(CurrentLevelIndex); // (before marking it complete)
        _levelCompleted[CurrentLevelIndex] = true;
        HighestUnlockedLevel = Mathf.Max(HighestUnlockedLevel, Mathf.Min(CurrentLevelIndex + 1, TotalLevels - 1));
        EmitSignal(SignalName.ScienceChanged, TotalScience);
        SaveProgress();
        return CurrentLevelIndex == TotalLevels - 1;
    }

    /// <summary>Wipes all progress (used by "Play Again" after beating the game).</summary>
    public void ResetCampaign()
    {
        for (int i = 0; i < TotalLevels; i++)
        {
            _bestLevelScience[i] = 0;
            _levelCompleted[i] = false;
            ChoiceHistory[i] = null;
        }
        HighestUnlockedLevel = 0;
        CurrentLevelIndex = 0;
        RefillAll();
        ResetLevel();
        SaveProgress();
    }

    /// <summary>All bars to 100% (mission finished / new game).</summary>
    private void RefillAll()
    {
        Oxygen = Power = Shield = MaxResource;
        _startOxygen = _startPower = _startShield = MaxResource;
        EmitResources();
    }

    /// <summary>The mission was NOT finished (failed, or SAVE & EXIT): bars go back to how they were when it started.</summary>
    public void RestoreMissionStartBars()
    {
        Oxygen = _startOxygen;
        Power = _startPower;
        Shield = _startShield;
        ShieldOffline = false;
        EmitResources();
        SaveProgress();
    }

    public bool IsLevelUnlocked(int levelIndex) =>
        levelIndex >= 0 && levelIndex < TotalLevels && (UnlockAllForTesting || levelIndex <= HighestUnlockedLevel);

    /// <summary>SAVE & EXIT: keep the points of this attempt as this mission's score (the mission is not marked complete).</summary>
    public void BankScience()
    {
        _bestLevelScience[CurrentLevelIndex] = MergedScience(CurrentLevelIndex);
        SaveProgress();
        EmitSignal(SignalName.ScienceChanged, TotalScience);
    }
    public bool IsLevelCompleted(int levelIndex) => levelIndex >= 0 && levelIndex < TotalLevels && _levelCompleted[levelIndex];

    public static int FirstDayOfLevel(int levelIndex) => levelIndex * DaysPerMission + 1; // 1, 4, 7
    public static int ChoiceDayOfLevel(int levelIndex) => levelIndex * DaysPerMission + 2; // 2, 5, 8

    // ==================================================================
    // TRADE-OFF (the Day 2 / 5 / 8 choice)
    // NOTE: which BUTTON is the science one changes per mission (M1: button B, M2/M3: button A).
    // LevelController works that out, so this method only gets "did they pick science?".
    // ==================================================================

    // ---- Science rewards for the choice ----
    public const int M1ScienceMax = 500, M1ScienceLow = 100;
    public const int M2ScienceMax = 500;
    /// <summary>Mission 2 option B (ignore the germs): the dirty water costs this many points.</summary>
    public const int M2DirtyWaterPenalty = 300;
    public const int M3ScienceMax = 1000, M3ScienceLow = 50;

    /// <summary>
    /// Applies the mid-mission choice for the CURRENT level.
    /// Returns a short line describing what happened (shown in the next briefing).
    /// </summary>
    public string ApplyTradeOff(bool choseScience)
    {
        ChoseScience = choseScience;
        ChoiceHistory[CurrentLevelIndex] = choseScience;
        string summary;

        switch (CurrentLevelIndex)
        {
            // ---------------- MISSION 1: SPACE GARDEN (Day 2) - oxygen leak ----------------
            case 0:
                if (choseScience) // B: keep collecting water for the plants
                {
                    PlayerSpeedMultiplier = 0.5f;   // muscle atrophy: you walk SLOWLY from now on
                    OxygenDrainMultiplier *= 1.5f;  // the leak keeps going
                    AddScience(M1ScienceMax);
                    summary = $"+{M1ScienceMax} points! But you are slow now. Keep catching!";
                }
                else // A: stop collecting, run to the Oxygen Room
                {
                    AddScience(M1ScienceLow);
                    summary = $"+{M1ScienceLow} points. Your water is saved. Now fix the oxygen!";
                }
                break;

            // ---------------- MISSION 2: CORE DRILL (Day 5) - bioburden / HMR heater ----------------
            case 1:
                if (choseScience) // A: Turn on the HMR Heater!
                {
                    Power = 10f;        // the heater overloads the generator
                    Blackout = true;    // Day 6 is played in the dark
                    AddScience(M2ScienceMax);
                    summary = $"+{M2ScienceMax} points! The HMR heater killed the germs. But power is down to 10%. Lights out!";
                }
                else // B: Ignore the germs: no heater, power stays safe - but the dirty water costs points
                {
                    AddScience(-M2DirtyWaterPenalty);
                    summary = "No HMR heater. The germs stay in the water...";
                }
                break;

            // ---------------- MISSION 3: SUN WATCHER (Day 8) - space weather ----------------
            default:
                if (choseScience) // A: Keep Studying the Sun!
                {
                    StormBreaksShield = true; // Shield drops to 0% when the storm hits (Day 9)
                    AddScience(M3ScienceMax);
                    summary = $"+{M3ScienceMax} points! But the Shield will drop to 0% in the storm!";
                }
                else // B: Run to the Shield Room! (the shield is filled there)
                {
                    AddScience(M3ScienceLow);
                    summary = $"+{M3ScienceLow} points. Go power up the Shield!";
                }
                break;
        }

        EmitResources();
        return summary;
    }

    /// <summary>How much the hazard knocks off its bar the moment the alert appears.</summary>
    public const float HazardDrop = 30f;

    /// <summary>
    /// The hazard hits as the alert pops up: M1 oxygen leak -> Oxygen, M2 drill trouble -> Power,
    /// M3 solar flare -> Shield. (Never drops a bar to 0 here; the choice decides what happens next.)
    /// </summary>
    public void ApplyHazardDrop()
    {
        switch (CurrentLevelIndex)
        {
            case 0: Oxygen = Mathf.Max(5f, Oxygen - HazardDrop); break;
            case 1: Power = Mathf.Max(5f, Power - HazardDrop); break;
            default: Shield = Mathf.Max(5f, Shield - HazardDrop); break;
        }
        EmitResources();
    }

    /// <summary>Day 9 with "Keep Studying the Sun": the storm knocks the Shield to 0% (offline, with a grace period).</summary>
    public void KnockOutShield()
    {
        Shield = 0f;
        ShieldOffline = true;
        EmitResources();
    }

    /// <summary>What the player picked per mission: true = Science, false = Survival, null = not chosen yet.</summary>
    public readonly bool?[] ChoiceHistory = new bool?[TotalLevels];

    // ==================================================================
    // RESOURCE API (call these from LevelController AND from minigames)
    // ==================================================================

    /// <summary>
    /// Drains resources over time. perSecond: X = Oxygen, Y = Power, Z = Shield.
    /// Oxygen is automatically multiplied by OxygenDrainMultiplier.
    /// Example (inside a minigame's _Process): GameManager.Instance.ApplyDrain(new Vector3(1, 0.5f, 0), delta);
    /// </summary>
    public void ApplyDrain(Vector3 perSecond, double delta)
    {
        float dt = (float)delta;
        ChangeResources(-perSecond.X * OxygenDrainMultiplier * dt, -perSecond.Y * dt, -perSecond.Z * dt);
    }

    /// <summary>Adds (positive) or removes (negative) resources. Values are clamped.</summary>
    public void ChangeResources(float oxygenDelta, float powerDelta, float shieldDelta)
    {
        Oxygen = Mathf.Clamp(Oxygen + oxygenDelta, 0f, MaxResource);
        Power = Mathf.Clamp(Power + powerDelta, 0f, MaxPower);
        Shield = Mathf.Clamp(Shield + shieldDelta, 0f, MaxResource);
        if (ShieldOffline && Shield > 0f)
            ShieldOffline = false; // repaired: the shield counts again
        EmitResources();
    }

    /// <summary>Returns the name of the first resource at 0% (loss condition), or null if all are fine.</summary>
    public string GetDepletedResource()
    {
        if (Oxygen <= 0f) return "OXYGEN";
        if (Power <= 0f) return "POWER";
        if (!ShieldOffline && Shield <= 0f) return "SHIELD";
        return null;
    }

    public void AddScience(int amount)
    {
        _pendingScience += amount; // can go below 0 (Mission 2 dirty-water penalty); the total never does
        EmitSignal(SignalName.ScienceChanged, TotalScience);
    }

    private void EmitResources()
    {
        EmitSignal(SignalName.ResourcesChanged, Oxygen, Power, Shield, MaxPower);
    }
}

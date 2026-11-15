using Godot;
using System;

namespace AeroKidz;

/// <summary>
/// LEVEL CONTROLLER - runs ONE mission at a time, day by day, and decides win / loss.
/// Attach to: Main/LevelController (a plain Node, "Access as Unique Name" ON).
///
/// The player WALKS around the outpost map (BaseMap) and walks INTO rooms:
///   Plant / Drill / Telescope -> start that mission     Oxygen / Power / Shield -> repair task that refills the bar
///
/// Every day is a list of STEPS: a pop-up page, a minigame, or "walk to a room" (GoTo). They run in order.
///   Day 1/4/7  steps (how to play + game)
///   Day 2/5/8  HAZARD ALERT: its bar drops, then button A / button B (one is Science, one is Survival)
///   Day 3/6/9  steps played under the conditions you chose. The SAFE choice = walk to that bar's room.
///
///   Mission 1 Plant Room : Water catch (leak interrupts it) -> A: walk to Oxygen Room, back to Plant / B: keep collecting -> Harvest
///   Mission 2 Core Drill : Drill (temp gauge) -> [Earth germs] -> walk back, circuit problem -> (B: Power Room) -> Blind circuit
///   Mission 3 Telescope  : Sun watcher        -> [solar flare] -> (B: Shield Room) -> 3-minute storm (NASA data)
///
/// During any game: LEAVE = pause the mission and walk the outpost (refill in the rooms, walk back in to resume),
///                  SAVE & EXIT = keep your points and stop.
/// After ALL 3 missions are complete: walk to the rocket and fly home to Earth (RocketLaunchGame).
/// LOSS: any resource hits 0%, a minigame fails (or the rocket clock, if timers are on).
/// </summary>
public partial class LevelController : Node
{
    // ------------------------------------------------------------------
    // SIGNALS -> UIManager listens to these
    // ------------------------------------------------------------------
    [Signal] public delegate void DayStartedEventHandler(int day, string dayTitle, string objective);
    [Signal] public delegate void ObjectiveChangedEventHandler(string objective);
    /// <summary>Minigame clock. A negative value means "this minigame has no clock".</summary>
    [Signal] public delegate void TimerUpdatedEventHandler(float secondsLeft);
    [Signal] public delegate void RocketClockUpdatedEventHandler(float secondsLeft);
    [Signal] public delegate void RocketWarningEventHandler(string message);
    [Signal] public delegate void InfoRequestedEventHandler(string tag, string title, string body, string buttonText);
    /// <summary>The hazard alert. bar = which bar dropped (e.g. "OXYGEN"), drop = how much.</summary>
    [Signal] public delegate void ChoiceRequestedEventHandler(string title, string bar, int drop, string fact, string optionA, string optionB);
    [Signal] public delegate void MinigameStartedEventHandler();
    /// <summary>A short Nova hint during play (one at a time). speak = read it out loud.</summary>
    [Signal] public delegate void NovaSaysEventHandler(string message, bool speak);
    /// <summary>A Nova story line: the flow waits until the player clicks NEXT (UIManager calls OnNovaDialogDone).</summary>
    [Signal] public delegate void NovaDialogEventHandler(string message);
    [Signal] public delegate void LevelFailedEventHandler(string reason);
    [Signal] public delegate void LevelCompletedEventHandler(int levelIndex, int totalScience);
    [Signal] public delegate void GameBeatenEventHandler(int totalScience);
    [Signal] public delegate void ReturnedToMapEventHandler();
    /// <summary>The player is now walking on the outpost map during a mission (objective = where to go).</summary>
    [Signal] public delegate void WalkStartedEventHandler(string objective);

    // ------------------------------------------------------------------
    // ROCKET CLOCK (only used when GameManager.TimersEnabled is true)
    // ------------------------------------------------------------------
    public const float RocketClockSeconds = 300f;
    public const float RocketWarningAt = 120f;
    public const bool PauseRocketClockInPopups = true;
    public static bool RocketClockOn => GameManager.TimersEnabled;

    private const string RocketWarningText =
        "WARNING! The Earth supply rocket launches in exactly 2 MINUTES! Finish your tasks and prepare to evacuate!";

    // ------------------------------------------------------------------
    // STATE
    // ------------------------------------------------------------------
    /// <summary>Walking = moving around the outpost map during a mission (to a room, or on a break from a game).</summary>
    public enum Phase { Intro, Map, Briefing, Playing, Choice, Walking, Ended }

    public Phase CurrentPhase { get; private set; } = Phase.Intro;

    /// <summary>0 = first day, 1 = hazard day, 2 = final day, 3 = going home.</summary>
    public int DaySlot { get; private set; }
    private const int HomeSlot = 3;

    /// <summary>SAVE & EXIT can be pressed any time during a mission (in a game or while walking).</summary>
    public bool CanSaveAndExit => (CurrentPhase == Phase.Playing || CurrentPhase == Phase.Walking) && _roomOverlay == null
                                  && DaySlot != HomeSlot;

    /// <summary>A repair panel is open (on the map, or inside the Day 9 storm) - Nova hides so nothing overlaps.</summary>
    public bool IsRepairOpen => _roomOverlay != null || (_activeMinigame != null && IsInstanceValid(_activeMinigame) && _activeMinigame.HasOverlay);

    /// <summary>LEAVE (walk the outpost to the rooms) can be pressed now.</summary>
    public bool CanLeaveToOutpost => CurrentPhase == Phase.Playing && _activeMinigame != null
                                     && !_activeMinigame.IsDone && _activeMinigame.AllowRoomShortcuts;

    private RoomTask _roomOverlay;          // a repair pop-up opened by walking into a room
    private bool _roomIsGoal;               // that repair is the current GoTo step (must finish it)
    private OutpostRoom? _walkGoal;         // the room a GoTo step wants you to reach
    private bool _gamePausedForWalk;        // LEAVE was pressed: the game waits until you walk back in
    private bool _hazardPausedGame;         // Mission 1: the water game is paused behind the hazard alert
    private bool _continueAfterHazard;      // Mission 1 option B: finishing the water game goes to Day 3

    private MinigameBase _activeMinigame;
    private Control _minigameContainer;
    private BaseMap _map;
    private Step[] _steps = Array.Empty<Step>();
    private int _stepIndex;
    private bool _rocketWarned;

    private static GameManager Gm => GameManager.Instance;
    private static NasaData Nasa => NasaData.Instance;

    // ------------------------------------------------------------------
    // MISSION SCRIPT - all story text lives here. Edit freely!
    // ------------------------------------------------------------------
    private sealed record Page(string Tag, string Title, string Body, string Button);

    /// <summary>
    /// One step of a day: a pop-up Page, a Game, or GoTo = walk through the outpost to a room
    /// (Oxygen/Power/Shield rooms: you must also finish the repair there, which fills the bar to 100%).
    /// </summary>
    private sealed class Step
    {
        public Page Page;
        public Func<MinigameBase> Game;
        public string NovaLine;        // a Nova story line (the player clicks NEXT to continue)
        public OutpostRoom? GoTo;
        public OutpostRoom? StartAt;   // GoTo: put the astronaut here first (e.g. back at the airlock)
        public string Nova;
        public string Objective;

        public static Step Show(string tag, string title, string body, string button) =>
            new() { Page = new Page(tag, title, body, button) };
        public static Step Play(Func<MinigameBase> game, string objective, string nova) =>
            new() { Game = game, Objective = objective, Nova = nova };
        public static Step NovaSay(string line) => new() { NovaLine = line };
        public static Step Walk(OutpostRoom room, string objective, string nova, OutpostRoom? startAt = null) =>
            new() { GoTo = room, StartAt = startAt, Objective = objective, Nova = nova };
    }

    private sealed class MissionDef
    {
        public string NovaStart;               // Nova says this when the mission starts
        public bool HazardMidGame;             // true = the hazard interrupts the Day 1 game (Mission 1)
        public string[] DayTitles;             // [day 1, day 2, day 3]
        public Func<Step[]> FirstDay;
        public string HazardTitle;
        public string HazardBar;               // the bar the hazard hits: OXYGEN / POWER / SHIELD
        public Func<string> HazardMessage;     // Func so live NASA data can be inserted
        public string OptionA, OptionADesc;    // blue button
        public string OptionB, OptionBDesc;    // green button
        public bool OptionAIsScience;
        public Func<bool, Step[]> FinalDay;    // arg = player chose science
    }

    // WRITING RULES (players are 6-12): short sentences, simple words, one idea per line,
    // bullet points for controls. Nova reads her lines OUT LOUD, so keep those extra short.

    private static readonly Step[] OpeningPages =
    {
        Step.Show("AERO KIDZ", "Welcome to Aero Kidz! 🚀",
            "Your goal: get as many SCIENCE POINTS as you can in 3 missions!\n\n" +
            "But big experiments are risky.\n" +
            "They use up your OXYGEN, POWER and SHIELD.\n\n" +
            "Keep them above zero. Get a high score!",
            "LET'S GO!"),
        Step.NovaSay("Hi, I'm Nova! I'm your robot helper."),
        Step.NovaSay("Walk to a room. Let's start with the Plant Room!"),
    };

    private static readonly MissionDef[] Missions =
    {
        // ======================= MISSION 1: THE SPACE GARDEN (Plant Room) =======================
        new MissionDef
        {
            NovaStart = "Welcome to the Moon! Food can't come from Earth every day. So let's grow our own. Let's be space farmers!",
            HazardMidGame = true, // the oxygen leak interrupts the water game
            DayTitles = new[] { "Space Garden", "Oxygen Leak!", "Harvest Day" },
            FirstDay = () => new[]
            {
                Step.Show("SCIENCE: HYDROPONICS", "PLANTS WITHOUT DIRT!",
                    "HYDROPONICS = growing plants with NO soil.\nThey grow in vitamin water under bright LED lights.\n\n" +
                    "• Move the mouse to catch WATER DROPS.\n" +
                    "• Catch BATTERIES to keep the lights on.",
                    "LET'S GROW!"),
                Step.Play(() => new WaterCatchGame(), "M1: CATCH 15 WATER DROPS",
                    "Catch the water! Grab batteries too!"),
            },
            HazardTitle = "OXYGEN LEAK!",
            HazardBar = "OXYGEN",
            HazardMessage = () =>
                "NASA FACT: Low oxygen + low gravity cause MUSCLE ATROPHY: muscles get weak and slow.\n" +
                $"Water caught: {Gm.WaterCollected} / {GameManager.WaterGoal}",
            OptionA = "GO TO THE OXYGEN ROOM",
            OptionADesc = $"+{GameManager.M1ScienceLow} points\nYou stay fast",
            OptionB = "KEEP CATCHING WATER",
            OptionBDesc = $"+{GameManager.M1ScienceMax} points!\nBut you get slow",
            OptionAIsScience = false,
            FinalDay = science => science
                ? new[]
                {
                    Step.Show("DAY 3", "HARVEST DAY!",
                        "The plants are big and healthy!\nBut you are tired and SLOW.\n\n" +
                        "• Click to walk.\n• Pick carrots.\n• Drop them in the box.",
                        "HARVEST!"),
                    Step.Play(() => new HarvestGame(), "M1: HARVEST THE CARROTS", "Pick the carrots!"),
                }
                : new[]
                {
                    Step.Walk(OutpostRoom.Oxygen, "GO TO THE OXYGEN ROOM", "Go to the Oxygen Room!"),
                    Step.Walk(OutpostRoom.Plant, "GO BACK TO THE PLANT ROOM", "Oxygen is full! Back to the plants!"),
                    Step.Show("DAY 3", "HARVEST DAY!",
                        "You are fast!\nBut the plants got little water. Some are small or dry.\n\n" +
                        "• Click to walk.\n• Pick carrots.\n• Drop them in the box.",
                        "HARVEST!"),
                    Step.Play(() => new HarvestGame(), "M1: HARVEST WHAT IS LEFT", "Grab every carrot you can!"),
                },
        },

        // ======================= MISSION 2: THE CORE DRILL (outside!) =======================
        new MissionDef
        {
            NovaStart = "We are outside! There is ice under the ground. Ice means water for our base!",
            DayTitles = new[] { "Drill for Ice", "Germ Alert!", "Clean & Repair" },
            FirstDay = () => new[]
            {
                Step.Show("SCIENCE: MOON ICE", "DRILL FOR ICE!",
                    "There is ice under the moon soil (called REGOLITH). Ice = water!\n\n" +
                    "• HOLD the mouse to drill.\n" +
                    "• LET GO to cool down.\n" +
                    "• Stay in the GREEN. Red = too hot!",
                    "DRILL!"),
                Step.Play(() => new DrillGame(), "M2: DRILL DOWN TO THE ICE", "Hold to drill. Let go to cool!"),
            },
            HazardTitle = "GERM ALERT!",
            HazardBar = "POWER",
            HazardMessage = () =>
                "Earth germs are on the drill! NASA calls this BIOBURDEN.\n" +
                "NASA FACT: The HMR heater (Heat Microbial Reduction) kills germs, but it uses lots of power.",
            OptionA = "TURN ON THE HMR HEATER",
            OptionADesc = $"+{GameManager.M2ScienceMax} points!\nBut power drops to 10%",
            OptionB = "IGNORE THE GERMS",
            OptionBDesc = $"Power stays safe\nBut dirty water: -{GameManager.M2DirtyWaterPenalty} points",
            OptionAIsScience = true,
            FinalDay = science => science
                ? new[]
                {
                    // A: HMR heater -> clean the water with the heat probe, then fix the circuit IN THE DARK
                    Step.Show("SCIENCE: PLANETARY PROTECTION", "CLEAN THE WATER!",
                        "The HMR heater killed most germs. A few are left in the water.\nBut power is LOW - move carefully!\n\n" +
                        "HMR (Heat Microbial Reduction) = heat kills germs.\nUse the small HMR HEAT PROBE.\n\n" +
                        "• Move the mouse to steer the heat probe.\n• Heat up ALL the germs before time runs out!\n• The heater uses POWER when it moves.",
                        "START CLEANING!"),
                    Step.Play(() => new HeatProbeGame(), "M2: CLEAN ALL THE GERMS!", null),
                    BackToBaseStep(),
                    Step.Show("DAY 6", "LIGHTS OUT!",
                        "A circuit broke!\nIt's too dark to see the wire colors.\n\n" +
                        "• Click a LEFT plug.\n• Then click a RIGHT plug.\n• GREEN light = correct!",
                        "FIX IT!"),
                    Step.Play(() => new BlindCircuitGame(), "M2: CONNECT THE 3 WIRES", "Connect the wires!"),
                }
                : new[]
                {
                    // B: ignored the germs -> no heater game, the dirty water costs points, then fix the circuit WITH the lights on
                    Step.Show("SCIENCE: PLANETARY PROTECTION", $"DIRTY WATER!  -{GameManager.M2DirtyWaterPenalty} POINTS",
                        "You ignored the germs.\n" +
                        "Earth germs (BIOBURDEN) are still in the ice water.\n" +
                        "Dirty water can't be used for science.\n\n" +
                        $"-{GameManager.M2DirtyWaterPenalty} SCIENCE POINTS\n\n" +
                        "Now go back to the outpost.",
                        "OK"),
                    BackToBaseStep(),
                    Step.Show("DAY 6", "BROKEN CIRCUIT!",
                        "A circuit broke!\nThe lights are ON, so you can see the colors.\n\n" +
                        "• Click a LEFT plug.\n• Then click the SAME COLOR on the right.\n• GREEN light = correct!",
                        "FIX IT!"),
                    Step.Play(() => new BlindCircuitGame(), "M2: CONNECT THE 3 WIRES", "Connect the wires!"),
                },
        },

        // ======================= MISSION 3: THE SUN WATCHER (Telescope) =======================
        new MissionDef
        {
            NovaStart = "Let's watch the Sun! The Sun can send storms. Our telescope helps keep the base safe.",
            DayTitles = new[] { "Sun Watcher", "Solar Storm!", "The Storm" },
            FirstDay = () => new[]
            {
                Step.Show("SCIENCE: NASA SPACE WEATHER", "WATCH THE SUN!",
                    "We study the Sun to keep our base safe.\n\n" +
                    "• Move the mouse to aim.\n" +
                    "• Click when the Sun is in the circle.\n" +
                    "• Do it 5 times!",
                    "AIM!"),
                Step.Play(() => new SunWatcherGame(), "M3: CATCH THE SUNLIGHT 5 TIMES", "Aim, then click!"),
            },
            HazardTitle = "SOLAR STORM!",
            HazardBar = "SHIELD",
            HazardMessage = () =>
                $"NASA DONKI saw a big Class {Nasa.FlareClass} SOLAR FLARE!\n" +
                "A CME (a giant cloud from the Sun) is coming!",
            OptionA = "KEEP WATCHING THE SUN",
            OptionADesc = $"+{GameManager.M3ScienceMax} points!\nBut Shield drops to 0%",
            OptionB = "RUN TO THE SHIELD ROOM",
            OptionBDesc = $"+{GameManager.M3ScienceLow} points\nShield goes to 100%",
            OptionAIsScience = true,
            FinalDay = science => new[]
            {
                science
                    ? null
                    : Step.Walk(OutpostRoom.Shield, "GO TO THE SHIELD ROOM", "Run to the Shield Room!"),
                Step.Show("DAY 9", "THE STORM IS HERE!",
                    $"Keep Oxygen, Power and Shield above zero for {StormGauntletGame.DurationSeconds / 60:0} minutes!\n\n" +
                    "• Click to run to a room.\n" +
                    "• Stand on the glowing pad to fix it." +
                    (science ? $"\n\nYour Shield is at 0%!\nFix it in {StormGauntletGame.ShieldGraceSeconds:0} seconds!" : ""),
                    "NEXT"),
                Step.Show(Nasa.SourceText, "REAL NASA DATA",
                    "Nova will SAY what the storm is doing. Listen!\n\n" +
                    $"• {Nasa.PowerShort}\n• {Nasa.ShieldShort}\n• {Nasa.OxygenShort}",
                    "SURVIVE!"),
                Step.Play(() => new StormGauntletGame(), "M3: SURVIVE 2 MINUTES!", null),
            },
        },
    };

    /// <summary>Mission 2: back from the drill to the outpost (3D: drive the rover and walk in through the airlock).</summary>
    private static Step BackToBaseStep() => BaseMap.WorldMode
        ? Step.Walk(OutpostRoom.Airlock, "DRIVE BACK TO THE OUTPOST", "Take the rover back to the outpost. Then walk inside!")
        : Step.Play(() => new MoonWalkGame(MoonWalkGame.Route.BackToBase), "WALK BACK TO THE OUTPOST", "Let's walk back to the outpost.");

    private static readonly Step[] GoHomeSteps =
    {
        Step.Show("ALL 3 MISSIONS DONE!", "TIME TO GO HOME!",
            "Great job, Commander!\n\nNow walk to the rocket and fly back to Earth!",
            "TO THE ROCKET!"),
        Step.Play(() => new RocketLaunchGame(), "RUN TO THE ROCKET!", "Run to the rocket!"),
    };

    // ==================================================================
    // GODOT LIFECYCLE
    // ==================================================================
    public override void _Ready()
    {
        _minigameContainer = GetNode<Control>("%MinigameContainer");
        _map = GetNode<BaseMap>("%MapLayer");
        _map.RoomEntered += OnRoomEntered;
        _map.BuildingInfo += message => Tip(message);
        CallDeferred(MethodName.ShowOpeningStory); // after UIManager is ready
    }

    public override void _Process(double delta)
    {
        // The astronaut on the map can only move while you are actually walking (not behind a pop-up or a repair).
        _map.InputLocked = _roomOverlay != null || !(CurrentPhase is Phase.Map or Phase.Walking);

        bool inMission = CurrentPhase is Phase.Playing or Phase.Briefing or Phase.Choice;
        if (!inMission)
            return;

        // ---- Rocket clock (off while GameManager.TimersEnabled is false) ----
        if (RocketClockOn && DaySlot != HomeSlot)
        {
            bool ticking = CurrentPhase == Phase.Playing || !PauseRocketClockInPopups;
            if (ticking)
            {
                Gm.RocketTimeLeft = Mathf.Max(0f, Gm.RocketTimeLeft - (float)delta);
                if (!_rocketWarned && Gm.RocketTimeLeft <= RocketWarningAt)
                {
                    _rocketWarned = true;
                    EmitSignal(SignalName.RocketWarning, RocketWarningText);
                }
                if (Gm.RocketTimeLeft <= 0f)
                {
                    FailMission("The Earth rocket launched without you!");
                    return;
                }
            }
            EmitSignal(SignalName.RocketClockUpdated, Gm.RocketTimeLeft);
        }

        if (CurrentPhase != Phase.Playing || _activeMinigame == null || _activeMinigame.IsDone)
            return;

        // ---- Global drains requested by the minigame (they keep going while you visit a room) ----
        Gm.ApplyDrain(_activeMinigame.DrainPerSecond, delta);

        // ---- LOSS CHECK ----
        string depleted = Gm.GetDepletedResource();
        if (depleted != null)
        {
            FailMission($"{depleted} reached 0%!");
            return;
        }

        if (_roomOverlay == null)
            EmitSignal(SignalName.TimerUpdated, _activeMinigame.ShowTimer ? _activeMinigame.TimeLeft : -1f);
    }

    // ==================================================================
    // PUBLIC API - called by UIManager buttons and the base map
    // ==================================================================

    /// <summary>A mission room was clicked on the map.</summary>
    public void StartMission(int levelIndex)
    {
        if (CurrentPhase != Phase.Map || !Gm.IsLevelUnlocked(levelIndex))
            return;

        Gm.StartLevel(levelIndex);
        StartRocketClock();
        _map.PlaceAt(BaseMap.MissionRoom(levelIndex));
        BeginDay(0);
    }

    /// <summary>"Retry Mission": restarts the current mission with the bars it STARTED with (no refill).</summary>
    public void RetryMission()
    {
        if (DaySlot == HomeSlot)
        {
            // failed on the way to the rocket: just try the walk again
            Gm.ResetLevel();
            RunSteps(GoHomeSteps);
            return;
        }
        Gm.ResetLevel();
        StartRocketClock();
        BeginDay(0);
    }

    /// <summary>"Next Mission" on the Mission Complete screen.</summary>
    public void NextMission()
    {
        int next = Gm.CurrentLevelIndex + 1;
        if (BaseMap.WorldMode)
        {
            // 3D: the next mission starts at its own screen, so walk (or drive) there first
            ReturnToMap();
            if (next < GameManager.TotalLevels && !Gm.IsLevelCompleted(next))
                Tip(next == 1
                    ? "Next: the CORE DRILL! Take the rover from the Rover Bay. Drive to the drill."
                    : $"Next: the {BaseMap.RoomName(BaseMap.MissionRoom(next))}! Go to its screen and press E.");
            return;
        }
        if (next < GameManager.TotalLevels && Gm.IsLevelUnlocked(next) && !Gm.IsLevelCompleted(next))
        {
            Gm.StartLevel(next);
            StartRocketClock();
            BeginDay(0);
        }
        else
        {
            ReturnToMap();
        }
    }

    /// <summary>"Back to Map" buttons.</summary>
    public void ReturnToMap()
    {
        ClearMinigame();
        EnterFreeMap();
    }

    /// <summary>The first unfinished mission's room (null = all done).</summary>
    private static OutpostRoom? NextMissionRoom()
    {
        for (int i = 0; i < GameManager.TotalLevels; i++)
            if (!Gm.IsLevelCompleted(i)) return BaseMap.MissionRoom(i);
        return null;
    }

    /// <summary>Back to free walking around the outpost (no mission running).</summary>
    private void EnterFreeMap()
    {
        _walkGoal = null;
        _map.SetGoal(BaseMap.WorldMode ? NextMissionRoom() : null); // 3D: the next mission's screen glows
        _map.HelpText = BaseMap.FreeWalkText;
        CurrentPhase = Phase.Map;
        EmitSignal(SignalName.ReturnedToMap);
    }

    /// <summary>"Play Again" after beating the game.</summary>
    public void RestartCampaign()
    {
        Gm.ResetCampaign();
        ReturnToMap();
    }

    /// <summary>
    /// SAVE & EXIT (any time during a mission): keep this attempt's science points, save to disk and go back to the map.
    /// The mission is NOT marked complete - start it again to finish it.
    /// </summary>
    public void SaveAndExit()
    {
        if (!CanSaveAndExit)
            return;
        Gm.BankScience();
        Gm.RestoreMissionStartBars(); // unfinished mission: bars are NOT refilled
        ClearMinigame();
        EnterFreeMap();
        Tip(
            $"Saved! You have {Gm.TotalScience} points. Come back any time!");
    }

    // ==================================================================
    // WALKING THE OUTPOST
    // ==================================================================

    /// <summary>
    /// LEAVE: pause the current game and walk the outpost. Walk into the Oxygen / Power / Shield room to
    /// refill that bar, then walk back into the mission room to carry on where you stopped.
    /// </summary>
    public void LeaveToOutpost()
    {
        if (!CanLeaveToOutpost)
            return;
        _activeMinigame.ProcessMode = ProcessModeEnum.Disabled; // freeze the game...
        _activeMinigame.Hide();                                 // ...and show the outpost
        _gamePausedForWalk = true;
        var home = BaseMap.MissionRoom(Gm.CurrentLevelIndex);
        _map.PlaceAt(home);
        _map.SetGoal(null);
        _map.HelpText = $"REFILL IN A ROOM - THEN GO BACK TO THE {BaseMap.RoomName(home)}";
        CurrentPhase = Phase.Walking;
        EmitSignal(SignalName.WalkStarted, "GO TO A ROOM TO REFILL");
        Tip(
            $"Paused! Refill in a room. Then come back to the {BaseMap.RoomName(home)}.");
    }

    private void ResumePausedGame()
    {
        _gamePausedForWalk = false;
        _map.SetGoal(null);
        _activeMinigame.Show();
        _activeMinigame.ProcessMode = ProcessModeEnum.Inherit;
        CurrentPhase = Phase.Playing;
        EmitSignal(SignalName.MinigameStarted);

    }

    /// <summary>GoTo step: show the outpost and wait until the player walks into the room.</summary>
    private void StartWalk(Step s)
    {
        ClearMinigame();
        if (s.StartAt is OutpostRoom start)
            _map.PlaceAt(start);
        _walkGoal = s.GoTo;
        _map.SetGoal(s.GoTo);
        _map.HelpText = BaseMap.GoText(s.GoTo.Value);
        CurrentPhase = Phase.Walking;
        EmitSignal(SignalName.WalkStarted, s.Objective ?? "");
    }

    /// <summary>The astronaut walked into a room on the outpost map.</summary>
    private void OnRoomEntered(int roomIndex)
    {
        var room = (OutpostRoom)roomIndex;
        bool isRepairRoom = room is OutpostRoom.Oxygen or OutpostRoom.Power or OutpostRoom.Shield;
        int missionHere = room switch { OutpostRoom.Plant => 0, OutpostRoom.Drill => 1, OutpostRoom.Telescope => 2, _ => -1 };

        switch (CurrentPhase)
        {
            case Phase.Map: // free walking, no mission running
                if (missionHere >= 0)
                {
                    if (Gm.IsLevelUnlocked(missionHere))
                        StartMission(missionHere);
                    else
                        Tip($"Locked! Finish Mission {missionHere} first.");
                }
                break;

            case Phase.Walking when _roomOverlay == null:
                if (_walkGoal == room)
                {
                    if (isRepairRoom) OpenRepair(room, mustFinish: true);
                    else FinishWalkStep();
                }
                else if (_gamePausedForWalk && room == BaseMap.MissionRoom(Gm.CurrentLevelIndex))
                {
                    ResumePausedGame();
                }
                else if (isRepairRoom)
                {
                    OpenRepair(room, mustFinish: false); // a normal top-up (+45), you may LEAVE it
                }
                else if (missionHere >= 0)
                {
                    Tip($"Finish Mission {Gm.CurrentLevelIndex + 1} first!");
                }
                break;
        }
    }

    private void OpenRepair(OutpostRoom room, bool mustFinish)
    {
        var kind = room switch { OutpostRoom.Oxygen => RoomKind.Oxygen, OutpostRoom.Power => RoomKind.Power, _ => RoomKind.Shield };
        _roomOverlay = RoomTask.Create(kind);
        _roomOverlay.CanLeave = !mustFinish;
        _roomOverlay.FillToFull = mustFinish;
        _roomIsGoal = mustFinish;
        _roomOverlay.Done += OnRepairDone;
        _map.InputLocked = true;
        _minigameContainer.AddChild(_roomOverlay);
    }

    private void OnRepairDone(bool success)
    {
        bool wasGoal = _roomIsGoal;
        CloseRoom();

        if (wasGoal)
        {
            if (success)
            {
                FinishWalkStep();
            }
            else
            {
                // the must-finish repair failed (e.g. lost all shield lives): try again
                Tip("Oops! Try again!");
                OpenRepair(_walkGoal ?? OutpostRoom.Shield, mustFinish: true);
            }
            return;
        }
        Tip(success ? "All full! Where next?" : "OK! Where next?");
    }

    private void FinishWalkStep()
    {
        _walkGoal = null;
        _map.SetGoal(null);
        EmitSignal(SignalName.NovaSays, "", false); // the walking hint ("go to the ...") is done: remove it
        _stepIndex++;
        RunCurrentStep();
    }

    private void CloseRoom()
    {
        if (_roomOverlay != null && IsInstanceValid(_roomOverlay))
        {
            _roomOverlay.Done -= OnRepairDone;
            _roomOverlay.QueueFree();
        }
        _roomOverlay = null;
        _roomIsGoal = false;
        if (_map != null) _map.InputLocked = false;
    }

    /// <summary>The player pressed the button on the info pop-up.</summary>
    public void OnInfoAcknowledged()
    {
        if (CurrentPhase is not (Phase.Intro or Phase.Briefing))
            return;
        _stepIndex++;
        RunCurrentStep();
    }

    /// <summary>The blue button (A, true) or green button (B, false) on the Hazard Alert.</summary>
    public void OnChoiceMade(bool optionA)
    {
        if (CurrentPhase != Phase.Choice)
            return;

        bool science = optionA == Missions[Gm.CurrentLevelIndex].OptionAIsScience;
        string summary = Gm.ApplyTradeOff(science);
        bool resumeWater = _hazardPausedGame && science;
        _hazardPausedGame = false;
        CurrentPhase = Phase.Briefing;

        // Nova tells what the choice did; the player clicks NEXT, THEN the game goes on.
        NovaThen(summary, () =>
        {
            if (resumeWater)
            {
                // Mission 1 option B: carry on with the SAME water game (now slow, with oxygen leaking)
                _continueAfterHazard = true;
                _activeMinigame.ProcessMode = ProcessModeEnum.Inherit;
                CurrentPhase = Phase.Playing;
                EmitSignal(SignalName.MinigameStarted);
                EmitSignal(SignalName.ObjectiveChanged, "M1: KEEP CATCHING WATER!");
                return;
            }
            // option A stops the water game here (the water you caught is kept in GameManager.WaterCollected)
            BeginDay(2);
        });
    }

    // ---- Nova story lines: wait for the player to click NEXT ----
    private Action _afterNova;

    /// <summary>Show a Nova story line, then run 'then' once the player has clicked NEXT.</summary>
    private void NovaThen(string line, Action then)
    {
        _afterNova = then;
        EmitSignal(SignalName.NovaDialog, line);
    }

    /// <summary>UIManager calls this when the player has read every Nova story line.</summary>
    public void OnNovaDialogDone()
    {
        var then = _afterNova;
        _afterNova = null;
        then?.Invoke();
    }

    /// <summary>A minigame asked for the hazard alert in the middle of play (Mission 1 water game).</summary>
    private void OnMinigameHazardReady()
    {
        if (DaySlot != 0 || !Missions[Gm.CurrentLevelIndex].HazardMidGame || _activeMinigame == null)
            return;
        CloseRoom();
        _activeMinigame.ProcessMode = ProcessModeEnum.Disabled; // freeze it behind the alert
        _hazardPausedGame = true;
        ShowHazard();
    }

    // ==================================================================
    // INTERNAL FLOW
    // ==================================================================
    private void ShowOpeningStory()
    {
        CurrentPhase = Phase.Intro;
        _steps = OpeningPages;
        _stepIndex = 0;
        RunCurrentStep();
    }

    private void StartRocketClock()
    {
        Gm.RocketTimeLeft = RocketClockSeconds;
        _rocketWarned = false;
        if (RocketClockOn)
            EmitSignal(SignalName.RocketClockUpdated, Gm.RocketTimeLeft);
    }

    private void BeginDay(int slot)
    {
        ClearMinigame();
        _hazardPausedGame = false;
        _continueAfterHazard = false;

        if (slot == 1)
        {
            ShowHazard();
            return;
        }

        MissionDef m = Missions[Gm.CurrentLevelIndex];
        DaySlot = slot;
        Gm.CurrentDay = GameManager.FirstDayOfLevel(Gm.CurrentLevelIndex) + slot;
        EmitSignal(SignalName.DayStarted, Gm.CurrentDay, m.DayTitles[slot], "");

        if (slot == 0)
        {
            // Nova welcomes you to the mission first, then the how-to-play page, then the game
            var first = new System.Collections.Generic.List<Step>();
            if (!string.IsNullOrEmpty(m.NovaStart)) first.Add(Step.NovaSay(m.NovaStart));
            first.AddRange(m.FirstDay());
            RunSteps(first.ToArray());
        }
        else
        {
            RunSteps(m.FinalDay(Gm.ChoseScience));
        }
    }

    /// <summary>HAZARD ALERT: the hazard hits its bar first, then you choose. (Can happen mid-game in Mission 1.)</summary>
    private void ShowHazard()
    {
        MissionDef m = Missions[Gm.CurrentLevelIndex];
        DaySlot = 1;
        Gm.CurrentDay = GameManager.FirstDayOfLevel(Gm.CurrentLevelIndex) + 1;
        EmitSignal(SignalName.DayStarted, Gm.CurrentDay, m.DayTitles[1], $"M{Gm.CurrentLevelIndex + 1}: MAKE THE CALL, COMMANDER");

        Gm.ApplyHazardDrop();
        Sfx.Play(this, Sfx.Alarm, -8f);
        CurrentPhase = Phase.Choice;
        EmitSignal(SignalName.ChoiceRequested, m.HazardTitle, m.HazardBar, (int)GameManager.HazardDrop, m.HazardMessage(),
            $"{m.OptionA}\n{m.OptionADesc}", $"{m.OptionB}\n{m.OptionBDesc}");
    }

    private void RunSteps(Step[] steps)
    {
        steps = Array.FindAll(steps, s => s != null); // a mission can leave out steps with null
        _steps = steps;
        _stepIndex = 0;
        RunCurrentStep();
    }

    private void RunCurrentStep()
    {
        if (_stepIndex >= _steps.Length)
        {
            OnStepsFinished();
            return;
        }

        Step s = _steps[_stepIndex];
        if (s.Page != null)
        {
            if (CurrentPhase != Phase.Intro) CurrentPhase = Phase.Briefing;
            EmitSignal(SignalName.InfoRequested, s.Page.Tag, s.Page.Title, s.Page.Body, s.Page.Button);
        }
        else if (s.NovaLine != null)
        {
            if (CurrentPhase != Phase.Intro) CurrentPhase = Phase.Briefing;
            NovaThen(s.NovaLine, () => { _stepIndex++; RunCurrentStep(); });
        }
        else if (s.GoTo != null)
        {
            // Nova explains where to go first (click NEXT), then you walk
            if (!string.IsNullOrEmpty(s.Nova))
            {
                CurrentPhase = Phase.Briefing;
                NovaThen(s.Nova, () => StartWalk(s));
            }
            else
            {
                StartWalk(s);
            }
        }
        else
        {
            StartMinigame(s);
        }
    }

    private void OnStepsFinished()
    {
        if (CurrentPhase == Phase.Intro)
        {
            EnterFreeMap();
            return;
        }

        switch (DaySlot)
        {
            case 0: BeginDay(1); break;                 // -> hazard alert
            case HomeSlot: GoHomeFinished(); break;     // landed on Earth!
            default: CompleteMission(); break;          // final day done
        }
    }

    private void StartMinigame(Step s)
    {
        ClearMinigame();
        _activeMinigame = s.Game();
        _activeMinigame.Finished += OnMinigameFinished;
        _activeMinigame.NovaLine += OnMinigameNovaLine;
        _activeMinigame.HazardReady += OnMinigameHazardReady;
        _minigameContainer.AddChild(_activeMinigame);

        CurrentPhase = Phase.Playing;
        EmitSignal(SignalName.MinigameStarted);
        EmitSignal(SignalName.ObjectiveChanged, s.Objective ?? "");
        EmitSignal(SignalName.TimerUpdated, _activeMinigame.ShowTimer ? _activeMinigame.TimeLeft : -1f);
        // (no Nova line here: the pop-up before the game already explained it - nothing should overlap)
    }

    private void OnMinigameNovaLine(string message, bool speak) => Tip(message, speak);

    /// <summary>A one-at-a-time Nova hint (no waiting).</summary>
    private void Tip(string message, bool speak = false) => EmitSignal(SignalName.NovaSays, message, speak);

    private void OnMinigameFinished(bool won, string reason)
    {
        if (CurrentPhase != Phase.Playing)
            return;

        if (!won)
        {
            FailMission(reason);
            return;
        }

        if (_continueAfterHazard)
        {
            // Mission 1 option B: the water game was finished after the leak -> harvest day
            _continueAfterHazard = false;
            BeginDay(2);
            return;
        }

        // Keep the finished minigame on screen behind the next pop-up; the next step replaces it.
        CurrentPhase = Phase.Briefing;
        _stepIndex++;
        RunCurrentStep();
    }

    private void CompleteMission()
    {
        ClearMinigame();
        Gm.CompleteCurrentLevel(); // also saves progress to disk

        if (Gm.AllLevelsCompleted)
        {
            // only now can the Commander go home
            DaySlot = HomeSlot;
            CurrentPhase = Phase.Briefing;
            EmitSignal(SignalName.DayStarted, Gm.CurrentDay, "Return to Earth", "");
            RunSteps(GoHomeSteps);
            return;
        }

        CurrentPhase = Phase.Ended;
        EmitSignal(SignalName.LevelCompleted, Gm.CurrentLevelIndex, Gm.TotalScience);
    }

    private void GoHomeFinished()
    {
        CurrentPhase = Phase.Ended;
        ClearMinigame();
        Gm.RecordFinalScore(Gm.TotalScience); // remember the highest score
        EmitSignal(SignalName.GameBeaten, Gm.TotalScience);
    }

    private void FailMission(string reason)
    {
        CurrentPhase = Phase.Ended;
        ClearMinigame();
        Gm.RestoreMissionStartBars(); // bars go back to how they were when this mission started
        EmitSignal(SignalName.LevelFailed, reason);
    }

    private void ClearMinigame()
    {
        CloseRoom();
        _gamePausedForWalk = false;
        if (_activeMinigame != null && IsInstanceValid(_activeMinigame))
        {
            _activeMinigame.Finished -= OnMinigameFinished;
            _activeMinigame.NovaLine -= OnMinigameNovaLine;
            _activeMinigame.HazardReady -= OnMinigameHazardReady;
            _activeMinigame.QueueFree();
        }
        _activeMinigame = null;
    }
}

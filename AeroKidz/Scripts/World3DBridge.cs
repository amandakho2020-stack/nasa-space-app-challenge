using Godot;
using System.Collections.Generic;
using TeamGame = global::GameManager; // the 3D team's GameManager (player lock, hub zone)

namespace AeroKidz;

/// <summary>
/// Joins the AeroKidz games to the team's real 3D outpost (res://Main.tscn).
///
/// What it does when the 3D scene starts:
///  1. Opens the AeroKidz game (HUD, Nova, pop-ups, minigames) on top of the 3D world.
///     The old 2D map is switched off (BaseMap.WorldMode), because you now walk in 3D.
///  2. Puts a glowing RoomScreen3D in every room: walk up, press [E] -> that room's game.
///     Plant = Mission 1, Core Drill (outside, take the rover) = Mission 2, Telescope = Mission 3,
///     Oxygen / Power / Shield = the repair games that refill the bars.
///  3. Walking back in through the airlock finishes the "drive back to base" step of Mission 2.
///  4. While a game, pop-up or Nova message is open: the astronaut stops, the mouse is free to click.
///     Back to walking: the mouse controls the camera again.
///  5. Switches off the 3D team's own test loop (suit O2 drain, Sols, the old pipe consoles) and
///     hides their stats panel, so there is only ONE set of bars: Oxygen / Power / Shield / Science.
///
/// Created automatically by AeroKidz.GameManager when the 3D scene is running.
/// Room screen positions: the Screens table below (world coordinates from EnvironmentBuilder.Base.cs).
/// </summary>
public partial class World3DBridge : Node
{
    private const string GameScene = "res://AeroKidz/Scenes/Main.tscn";

    /// <summary>Where each room's screen stands, and the spot it faces (usually the room's door).</summary>
    private static readonly (OutpostRoom Room, Vector3 Pos, Vector3 Face)[] Screens =
    {
        (OutpostRoom.Plant,     new Vector3(57.5f, 0.25f, 5.0f),    new Vector3(50f, 0.25f, 0f)),
        (OutpostRoom.Telescope, new Vector3(38.5f, 0.25f, 21.5f),   new Vector3(36f, 0.25f, 12f)),
        (OutpostRoom.Oxygen,    new Vector3(-33.5f, 0.25f, -22.5f), new Vector3(-36f, 0.25f, -12f)),
        (OutpostRoom.Power,     new Vector3(-58.5f, 0.25f, 4.0f),   new Vector3(-48f, 0.25f, 0f)),
        (OutpostRoom.Shield,    new Vector3(-33.5f, 0.25f, 22.5f),  new Vector3(-36f, 0.25f, 12f)),
        (OutpostRoom.Drill,     new Vector3(17.0f, 0.0f, 195.5f),   new Vector3(13f, 0f, 184f)),
    };

    private readonly Dictionary<OutpostRoom, RoomScreen3D> _screens = new();
    private readonly List<MinigameTrigger3D> _triggers = new();
    private FloorArrows3D _arrows;
    private BaseMap _map;
    private Control _mapLayer;
    private Control _teamPrompt, _teamMinimap;
    private bool _busy = true;
    private bool _firstFrame = true;

    private static TeamGame Team => TeamGame.Instance;

    /// <summary>Called once by AeroKidz.GameManager after the first frame.</summary>
    public static void AttachIfNeeded(SceneTree tree)
    {
        Node scene = tree.CurrentScene;
        if (scene is not Node3D || scene.GetNodeOrNull<PlayerController>("Player") == null || Team == null)
            return; // not the 3D outpost (e.g. the 2D test scene AeroKidz/Scenes/Main.tscn)
        if (scene.GetNodeOrNull("AeroKidzBridge") != null)
            return;
        scene.AddChild(new World3DBridge { Name = "AeroKidzBridge" });
    }

    public override void _Ready()
    {
        BaseMap.WorldMode = true; // must be set BEFORE the game scene is created
        Team.ExternalControl = true;
        Team.ExternalInputLock = true;

        // ---- 1. the AeroKidz game on top of the 3D world ----
        var gameLayer = new CanvasLayer { Name = "AeroKidzGame", Layer = 5 };
        AddChild(gameLayer);
        var game = GD.Load<PackedScene>(GameScene).Instantiate<Control>();
        gameLayer.AddChild(game);
        game.MouseFilter = Control.MouseFilterEnum.Ignore; // clicks / mouse look pass through to the 3D world
        _mapLayer = game.GetNode<Control>("%MapLayer");
        _map = (BaseMap)_mapLayer;
        game.GetNode<Control>("%MinigameContainer").MouseFilter = Control.MouseFilterEnum.Ignore;
        var ui = game.GetNode<CanvasLayer>("UI");
        ui.Layer = 10; // HUD + pop-ups above the minigames
        ui.GetNode<Control>("HUD").MouseFilter = Control.MouseFilterEnum.Ignore;
        _map.GoalChanged += OnGoalChanged;

        // ---- the end: rocket landed -> outro video ("Moon to Earth") -> back to the main menu ----
        game.GetNode<LevelController>("LevelController").GameBeaten += score =>
        {
            global::GameFlow.LastFinalScore = score;
            global::OutroVideo.Play(this);
        };

        // ---- 2. a screen in every room ----
        var world = GetParent<Node3D>();
        foreach (var (room, pos, face) in Screens)
        {
            var screen = RoomScreen3D.Create(room, pos, face);
            screen.Used += r => _map.EnterRoom(r);
            world.CallDeferred(Node.MethodName.AddChild, screen);
            _screens[room] = screen;
        }

        // ---- 2b. 3D models that start a game (MinigameTrigger3D: Lunar Module, telescope, ...) ----
        // They call the same BaseMap.EnterRoom as the screens, so LevelController and the games are unchanged.
        foreach (Node n in GetTree().GetNodesInGroup(MinigameTrigger3D.Group))
            if (n is MinigameTrigger3D trigger)
            {
                trigger.Used += r => _map.EnterRoom(r);
                _triggers.Add(trigger);
            }

        // ---- glowing arrows on the floor towards the goal room ----
        _arrows = new FloorArrows3D { Name = "AeroFloorArrows" };
        world.CallDeferred(Node.MethodName.AddChild, _arrows);

        // ---- keep the words on the 3D boards inside their screens ----
        AddChild(new BoardTextFitter { Name = "BoardTextFitter" });

        // ---- 3. back in through the airlock ----
        Team.HubStateChanged += OnHubStateChanged;

        // ---- 5. switch off the team's own test loop + duplicate HUD ----
        foreach (Node n in GetTree().GetNodesInGroup(PlayerController.InteractableGroup))
        {
            if (n is MissionConsole console)
            {
                console.RemoveFromGroup(PlayerController.InteractableGroup);
                console.UseCollision = false;
                console.Visible = false;
                console.ProcessMode = ProcessModeEnum.Disabled;
            }
        }
        var teamHud = world.GetNodeOrNull("UI/HUD");
        if (teamHud != null)
        {
            foreach (string hide in new[] { "StatsPanel", "ObjectiveLabel", "MessageLabel", "ToolWheel" })
                if (teamHud.GetNodeOrNull<CanvasItem>(hide) is { } item) item.Visible = false;
            _teamPrompt = teamHud.GetNodeOrNull<Control>("PromptLabel");
            _teamMinimap = teamHud.GetNodeOrNull<Control>("Minimap");
        }
    }

    public override void _ExitTree()
    {
        BaseMap.WorldMode = false;
        if (Team != null)
        {
            Team.HubStateChanged -= OnHubStateChanged;
            Team.ExternalControl = false;
            Team.ExternalInputLock = false;
        }
    }

    // ------------------------------------------------------------------
    // Every frame: is a game / pop-up / Nova open? -> stop the astronaut and free the mouse
    // ------------------------------------------------------------------
    public override void _Process(double delta)
    {
        bool busy = _map.InputLocked || !_mapLayer.Visible || NovaPopup.IsTalking;
        if (busy != _busy || _firstFrame)
        {
            _busy = busy;
            _firstFrame = false;
            Team.ExternalInputLock = busy;
            Input.MouseMode = busy ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        }
        else if (busy && Input.MouseMode != Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible; // something tried to grab the mouse behind a pop-up
        }

        // the team's prompt + minimap only while walking (nothing may overlap a pop-up or Nova)
        if (_teamPrompt != null) _teamPrompt.Visible = !busy;
        if (_teamMinimap != null) _teamMinimap.Visible = !busy;

        RoomScreen3D.MarkersAllowed = !busy;
        _arrows.Allowed = !busy;

        // the 3D team's wall boards ("O2 100%  PWR 100%", science) show OUR bars
        var gm = GameManager.Instance;
        if (gm != null)
        {
            Team.Oxygen = gm.Oxygen;
            Team.Power = gm.Power;
            if (Team.SciencePoints != gm.TotalScience) Team.AddScience(gm.TotalScience - Team.SciencePoints);
        }
    }

    private void OnGoalChanged(OutpostRoom? goal)
    {
        foreach (var (room, screen) in _screens)
            screen.SetGoal(goal == room);
        foreach (var trigger in _triggers)
            if (IsInstanceValid(trigger))
                trigger.SetGoal(goal == trigger.Room);
        _arrows.SetGoal(goal);

        // already inside when "go back inside the base" starts? then it is done straight away
        if (goal == OutpostRoom.Airlock && Team.IsInsideHub && Team.ActiveVehicle == null)
            Callable.From(() => _map.EnterRoom(OutpostRoom.Airlock)).CallDeferred();
    }

    private void OnHubStateChanged(bool inside)
    {
        if (inside && _map.Goal == OutpostRoom.Airlock)
            _map.EnterRoom(OutpostRoom.Airlock);
    }
}

using Godot;

namespace AeroKidz;

/// <summary>
/// A glowing screen on a stand inside one room of the team's 3D outpost.
/// Walk up to it and press [E]: the room's game opens (it calls BaseMap.EnterRoom, exactly like
/// walking into that room on the old 2D map). LevelController decides what happens
/// (start the mission, open the Oxygen/Power/Shield repair, resume a paused game...).
///
/// When it is the room you should go to, it pulses yellow and a "GO HERE" marker floats above it.
/// You can see the marker through walls, so kids can always find their way.
/// Created in code by World3DBridge.
/// </summary>
public partial class RoomScreen3D : Node3D, IInteractable
{
    public OutpostRoom Room;
    public System.Action<OutpostRoom> Used;

    private static readonly Color Cyan = new(0.3f, 0.85f, 1f);
    private static readonly Color Gold = new(1f, 0.85f, 0.25f);

    private StandardMaterial3D _screenMat;
    private OmniLight3D _light;
    private Label3D _marker;
    private bool _isGoal;
    private float _time;

    /// <summary>Make a screen for a room at a spot, turned to face another spot (e.g. the door).</summary>
    public static RoomScreen3D Create(OutpostRoom room, Vector3 position, Vector3 faceToward)
    {
        var s = new RoomScreen3D { Room = room, Name = $"AeroScreen_{room}", Position = position };
        Vector3 d = faceToward - position;
        s.Rotation = new Vector3(0, Mathf.Atan2(d.X, d.Z), 0); // the screen's front (+Z) looks at faceToward
        return s;
    }

    public override void _Ready()
    {
        AddToGroup(PlayerController.InteractableGroup);

        var metal = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.6f, 0.68f), Metallic = 0.7f, Roughness = 0.35f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.1f, 0.14f), Metallic = 0.5f, Roughness = 0.4f };
        _screenMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.1f, 0.16f),
            EmissionEnabled = true,
            Emission = Cyan,
            EmissionEnergyMultiplier = 1.2f,
        };

        // stand: base plate + post
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.34f, Height = 0.06f }, Position = new Vector3(0, 0.03f, 0), MaterialOverride = dark });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 1.1f }, Position = new Vector3(0, 0.58f, 0), MaterialOverride = metal });

        // the screen, tilted back a little so you look down onto it
        var head = new Node3D { Position = new Vector3(0, 1.35f, 0), Rotation = new Vector3(Mathf.DegToRad(-15), 0, 0) };
        AddChild(head);
        head.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.15f, 0.75f, 0.08f) }, MaterialOverride = dark });
        head.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(1.02f, 0.62f) }, Position = new Vector3(0, 0, 0.045f), MaterialOverride = _screenMat });
        head.AddChild(new Label3D
        {
            Text = BaseMap.RoomName(Room) + "\n[E] PLAY",
            FontSize = 64,
            PixelSize = 0.0019f,
            OutlineSize = 12,
            Modulate = Colors.White,
            OutlineModulate = new Color(0, 0.1f, 0.2f),
            Position = new Vector3(0, 0, 0.05f),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        _light = new OmniLight3D { Position = new Vector3(0, 1.4f, 0.6f), LightColor = Cyan, LightEnergy = 0.8f, OmniRange = 3.5f };
        AddChild(_light);

        // "GO HERE" marker: always the same size on screen and visible through walls
        _marker = new Label3D
        {
            Text = "▼ GO HERE ▼\n" + BaseMap.RoomName(Room),
            FontSize = 56,
            PixelSize = 0.0012f,
            FixedSize = true,
            NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 14,
            Modulate = Gold,
            OutlineModulate = new Color(0.25f, 0.12f, 0f),
            Position = new Vector3(0, 2.6f, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderPriority = 10,
            OutlineRenderPriority = 9,
            Layers = 2, // render layer 2 = not drawn on the team's minimap
            Visible = false,
        };
        AddChild(_marker);
    }

    /// <summary>false while a game, pop-up or Nova is on screen (nothing may overlap them).</summary>
    public static bool MarkersAllowed { get; set; } = true;

    private const float HideMarkerWithin = 4f; // standing at the screen: no need for the marker

    /// <summary>Glow yellow + show the floating marker (this is where the player should go).</summary>
    public void SetGoal(bool isGoal) => _isGoal = isGoal;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        var player = global::GameManager.Instance?.Player;
        bool near = false;
        if (player != null)
        {
            Vector3 d = player.GlobalPosition - GlobalPosition;
            d.Y = 0;
            near = d.Length() < HideMarkerWithin;
        }
        _marker.Visible = _isGoal && MarkersAllowed && !near;
        Color c = _isGoal ? Gold : Cyan;
        float pulse = _isGoal ? 1.6f + 1.2f * Mathf.Sin(_time * 5f) : 1.2f;
        _screenMat.Emission = c;
        _screenMat.EmissionEnergyMultiplier = pulse;
        _light.LightColor = c;
        _light.LightEnergy = _isGoal ? 1.4f : 0.8f;
        if (_isGoal) _marker.Position = new Vector3(0, 2.6f + 0.12f * Mathf.Sin(_time * 3f), 0); // gentle bob
    }

    // ---- IInteractable (the friend's PlayerController finds the nearest one and shows the prompt) ----
    public Vector3 InteractPosition => GlobalPosition;

    public string GetPrompt() => $"[E] {BaseMap.RoomName(Room)} SCREEN";

    public void Interact(PlayerController player) => Used?.Invoke(Room);
}

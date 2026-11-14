using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>The places in the outpost. Junction and Airlock are corridor corners, not rooms.</summary>
public enum OutpostRoom { Hub, Oxygen, Power, Shield, Plant, Drill, Telescope, Junction, Airlock }

/// <summary>
/// THE OUTPOST - a top-down base you really WALK around (laid out like the team's sketch):
///
///            OXYGEN                  DRILL ② (outside, through the airlock)
///   POWER ──JUNCTION─────── HUB ─────────── PLANT ①
///            SHIELD                    TELESCOPE ③              NOVA
///
/// Move: CLICK a room (the astronaut walks there through the corridors) or WASD / arrows.
/// Walking into a room emits RoomEntered(room). LevelController decides what happens
/// (start a mission, open the Oxygen/Power/Shield repair, resume a paused mission...).
///
/// Attach to: Main/MapLayer (Control, Full Rect, "Access as Unique Name" ON).
/// When the 3D version comes, the 3D base only has to emit the same RoomEntered(int) signal
/// and offer PlaceAt / SetGoal / InputLocked.
/// </summary>
public partial class BaseMap : Control
{
    [Signal] public delegate void RoomEnteredEventHandler(int room);
    [Signal] public delegate void BuildingInfoEventHandler(string message);

    // ---- Tuning ----
    private const float WalkSpeed = 280f;
    private const float CorridorHalfWidth = 17f;

    private sealed class Node
    {
        public OutpostRoom Id;
        public string Name;
        public Vector2 Pos;
        public float Radius;
        public bool IsRoom;
        public int Mission = -1;
        public string Info;
        public bool Lit;
    }

    private readonly Node[] _nodes =
    {
        new() { Id = OutpostRoom.Hub,       Name = "MAIN HUB",    Pos = new(640, 430), Radius = 62, IsRoom = true,
                Info = "The heart of Outpost Alpha. Every corridor meets here." },
        new() { Id = OutpostRoom.Oxygen,    Name = "OXYGEN ROOM", Pos = new(330, 270), Radius = 74, IsRoom = true,
                Info = "Electrolysis splits water (H2O) into Hydrogen and Oxygen. Fix the pipes here to refill your Oxygen." },
        new() { Id = OutpostRoom.Power,     Name = "POWER ROOM",  Pos = new(130, 470), Radius = 64, IsRoom = true,
                Info = "Solar panels and batteries. Rebuild the solar panel here to refill Power." },
        new() { Id = OutpostRoom.Shield,    Name = "SHIELD ROOM", Pos = new(270, 590), Radius = 58, IsRoom = true,
                Info = "Deflects radiation from solar flares. Block the meteors here to refill the Shield." },
        new() { Id = OutpostRoom.Plant,     Name = "PLANT ROOM",  Pos = new(990, 290), Radius = 68, IsRoom = true, Mission = 0,
                Info = "Our space garden. Plants give us food and turn CO2 back into Oxygen." },
        new() { Id = OutpostRoom.Drill,     Name = "CORE DRILL",  Pos = new(800, 190), Radius = 56, IsRoom = true, Mission = 1,
                Info = "Outside the outpost! You walked out through the airlock in your spacesuit." },
        new() { Id = OutpostRoom.Telescope, Name = "TELESCOPE",   Pos = new(990, 555), Radius = 62, IsRoom = true, Mission = 2,
                Info = "The solar telescope watches the Sun to warn us about space storms." },
        new() { Id = OutpostRoom.Junction,  Name = "", Pos = new(400, 450), Radius = 26 },
        new() { Id = OutpostRoom.Airlock,   Name = "AIRLOCK", Pos = new(700, 300), Radius = 22 },
    };

    // corridors (walkable). The Airlock -> Drill path is OUTSIDE on the moon dust.
    private static readonly (OutpostRoom, OutpostRoom)[] Corridors =
    {
        (OutpostRoom.Hub, OutpostRoom.Junction),
        (OutpostRoom.Junction, OutpostRoom.Oxygen),
        (OutpostRoom.Junction, OutpostRoom.Power),
        (OutpostRoom.Junction, OutpostRoom.Shield),
        (OutpostRoom.Hub, OutpostRoom.Plant),
        (OutpostRoom.Hub, OutpostRoom.Telescope),
        (OutpostRoom.Hub, OutpostRoom.Airlock),
        (OutpostRoom.Airlock, OutpostRoom.Drill),
    };

    private static readonly Vector2 NovaPos = new(1170, 450);
    private const string NovaInfo = "That's me, Nova! Click a room and you'll walk there. Walk into a glowing mission room to start it.";

    // ---- State ----
    private Vector2 _pos;
    private readonly List<Vector2> _path = new();
    private OutpostRoom? _pathGoal;
    private OutpostRoom? _inRoom;
    private OutpostRoom? _goal;          // highlighted destination (set by LevelController)
    private float _walk;
    private bool _faceLeft;
    private float _time;
    private int _hover = -1;

    /// <summary>true = the astronaut can't move (a repair pop-up is open).</summary>
    public bool InputLocked { get; set; }

    /// <summary>The yellow help line at the bottom (LevelController changes it per situation).</summary>
    public string HelpText { get; set; } = "CLICK A ROOM TO WALK THERE";

    private static GameManager Gm => GameManager.Instance;

    public static OutpostRoom MissionRoom(int level) => level switch
    {
        0 => OutpostRoom.Plant,
        1 => OutpostRoom.Drill,
        _ => OutpostRoom.Telescope,
    };

    public static string RoomName(OutpostRoom r) => r switch
    {
        OutpostRoom.Oxygen => "OXYGEN ROOM",
        OutpostRoom.Power => "POWER ROOM",
        OutpostRoom.Shield => "SHIELD ROOM",
        OutpostRoom.Plant => "PLANT ROOM",
        OutpostRoom.Drill => "CORE DRILL",
        OutpostRoom.Telescope => "TELESCOPE",
        _ => "MAIN HUB",
    };

    private Node Get(OutpostRoom id) => _nodes[(int)id];

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        PlaceAt(OutpostRoom.Hub);
    }

    // ==================================================================
    // API for LevelController
    // ==================================================================

    /// <summary>Put the astronaut in a room (no RoomEntered event for it).</summary>
    public void PlaceAt(OutpostRoom room)
    {
        var n = Get(room);
        _pos = n.Pos + new Vector2(0, 14);
        _path.Clear();
        _pathGoal = null;
        _inRoom = n.IsRoom ? room : null;
        n.Lit = true;
    }

    /// <summary>Highlight where the player should walk (or null for none).</summary>
    public void SetGoal(OutpostRoom? room) => _goal = room;

    // ==================================================================
    // INPUT + WALKING
    // ==================================================================
    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
            return;

        if (mb.Position.DistanceTo(NovaPos) < 50f)
        {
            EmitSignal(SignalName.BuildingInfo, NovaInfo);
            return;
        }
        if (InputLocked)
            return;

        // clicked a room / corridor corner? -> walk there through the corridors
        Node target = null;
        float best = 110f;
        foreach (var n in _nodes)
        {
            float d = mb.Position.DistanceTo(n.Pos);
            if (d < Mathf.Max(n.Radius + 10f, 40f) && d < best) { best = d; target = n; }
        }
        if (target != null)
            WalkTo(target.Id);
    }

    private void WalkTo(OutpostRoom goal)
    {
        var start = NearestReachableNode();
        if (start == null) return;
        var route = FindRoute(start.Id, goal);
        if (route == null) return;

        _path.Clear();
        foreach (var id in route) _path.Add(Get(id).Pos + new Vector2(0, 14));
        _pathGoal = goal;
    }

    /// <summary>Breadth-first search along the corridors.</summary>
    private static List<OutpostRoom> FindRoute(OutpostRoom from, OutpostRoom to)
    {
        var prev = new Dictionary<OutpostRoom, OutpostRoom>();
        var queue = new Queue<OutpostRoom>();
        var seen = new HashSet<OutpostRoom> { from };
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == to) break;
            foreach (var (a, b) in Corridors)
            {
                OutpostRoom? next = a == cur ? b : b == cur ? a : null;
                if (next is OutpostRoom nx && seen.Add(nx))
                {
                    prev[nx] = cur;
                    queue.Enqueue(nx);
                }
            }
        }
        if (!seen.Contains(to)) return null;
        var route = new List<OutpostRoom> { to };
        while (route[0] != from) route.Insert(0, prev[route[0]]);
        return route;
    }

    private Node NearestReachableNode()
    {
        Node best = null;
        float bestD = float.MaxValue;
        foreach (var n in _nodes)
        {
            float d = _pos.DistanceTo(n.Pos);
            if (d < bestD && SegmentWalkable(_pos, n.Pos)) { bestD = d; best = n; }
        }
        return best;
    }

    private bool IsWalkable(Vector2 p)
    {
        foreach (var n in _nodes)
            if (p.DistanceTo(n.Pos) <= n.Radius - 6f) return true;
        foreach (var (a, b) in Corridors)
        {
            var pa = Get(a).Pos;
            var pb = Get(b).Pos;
            var closest = Geometry2D.GetClosestPointToSegment(p, pa, pb);
            if (p.DistanceTo(closest) <= CorridorHalfWidth) return true;
        }
        return false;
    }

    private bool SegmentWalkable(Vector2 a, Vector2 b)
    {
        for (int i = 0; i <= 12; i++)
            if (!IsWalkable(a.Lerp(b, i / 12f))) return false;
        return true;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        float dt = (float)delta;
        _time += dt;

        var mouse = GetLocalMousePosition();
        _hover = -1;
        for (int i = 0; i < _nodes.Length; i++)
            if (_nodes[i].IsRoom && mouse.DistanceTo(_nodes[i].Pos) <= _nodes[i].Radius + 6) _hover = i;

        if (!InputLocked)
            Move(dt);

        CheckRooms();
        QueueRedraw();
    }

    private void Move(float dt)
    {
        var key = new Vector2(
            (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0),
            (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0) - (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0));
        float step = WalkSpeed * dt;

        if (key != Vector2.Zero)
        {
            _path.Clear();
            _pathGoal = null;
            var d = key.Normalized() * step;
            // slide along walls: try the full move, then each axis on its own
            if (IsWalkable(_pos + d)) _pos += d;
            else if (IsWalkable(_pos + new Vector2(d.X, 0))) _pos += new Vector2(d.X, 0);
            else if (IsWalkable(_pos + new Vector2(0, d.Y))) _pos += new Vector2(0, d.Y);
            _walk += dt * 12f;
            if (key.X != 0) _faceLeft = key.X < 0;
            return;
        }

        if (_path.Count == 0) return;
        var toNext = _path[0] - _pos;
        if (toNext.Length() <= step)
        {
            _pos = _path[0];
            _path.RemoveAt(0);
            if (_path.Count == 0) _pathGoal = null;
        }
        else
        {
            _pos += toNext.Normalized() * step;
            if (Mathf.Abs(toNext.X) > 1f) _faceLeft = toNext.X < 0;
        }
        _walk += dt * 12f;
    }

    /// <summary>Fires RoomEntered when you arrive in a room (not when you just pass through one on the way).</summary>
    private void CheckRooms()
    {
        OutpostRoom? now = null;
        foreach (var n in _nodes)
            if (n.IsRoom && _pos.DistanceTo(n.Pos) < n.Radius * 0.55f) now = n.Id;

        if (now == null)
        {
            // fully out of the old room?
            if (_inRoom is OutpostRoom old && _pos.DistanceTo(Get(old).Pos) > Get(old).Radius)
                _inRoom = null;
            return;
        }
        if (now == _inRoom) return;

        bool passingThrough = _path.Count > 0 && _pathGoal != now;
        if (passingThrough) return;

        _inRoom = now;
        var room = Get(now.Value);
        bool firstVisit = !room.Lit;
        room.Lit = true;
        if (firstVisit && room.Mission < 0)
            EmitSignal(SignalName.BuildingInfo, $"Lights on in the {room.Name}! {room.Info}");
        EmitSignal(SignalName.RoomEntered, (int)now.Value);
    }

    // ==================================================================
    // DRAWING
    // ==================================================================
    public override void _Draw()
    {
        int litCount = 0, rooms = 0;
        foreach (var n in _nodes) if (n.IsRoom) { rooms++; if (n.Lit) litCount++; }
        float light = 0.5f + 0.5f * litCount / rooms;

        // Lunar ground seen from above
        DrawRect(new Rect2(Vector2.Zero, Size), Dim(new Color(0.42f, 0.42f, 0.45f), light));
        var rng = new RandomNumberGenerator { Seed = 99 };
        for (int i = 0; i < 26; i++)
        {
            var p = new Vector2(rng.RandfRange(20, 1260), rng.RandfRange(130, 710));
            float rad = rng.RandfRange(14, 46);
            DrawCircle(p, rad, Dim(new Color(0.34f, 0.34f, 0.37f), light));
            DrawCircle(p + new Vector2(rad * 0.12f, rad * 0.12f), rad * 0.8f, Dim(new Color(0.38f, 0.38f, 0.41f), light));
        }

        // Corridors (tubes); the airlock -> drill path is outside, drawn as a dusty trail
        foreach (var (a, b) in Corridors)
        {
            var pa = Get(a).Pos;
            var pb = Get(b).Pos;
            bool outside = a == OutpostRoom.Airlock && b == OutpostRoom.Drill;
            bool lit = Get(a).Lit && Get(b).Lit;
            if (outside)
            {
                DrawLine(pa, pb, Dim(new Color(0.55f, 0.52f, 0.45f, 0.6f), light), CorridorHalfWidth * 2);
                for (int k = 1; k < 7; k++)
                    Art.Ellipse(this, pa.Lerp(pb, k / 7f) + new Vector2(k % 2 == 0 ? -6 : 6, 0), new Vector2(4, 6),
                        Dim(new Color(0.3f, 0.3f, 0.33f), light));
            }
            else
            {
                DrawLine(pa, pb, new Color(0.2f, 0.22f, 0.27f), CorridorHalfWidth * 2 + 10);
                DrawLine(pa, pb, new Color(0.72f, 0.75f, 0.82f, lit ? 1f : 0.55f), CorridorHalfWidth * 2);
                if (lit) DrawLine(pa, pb, new Color(1f, 0.9f, 0.55f, 0.35f), 4f);
            }
        }
        Art.TextCentered(this, "OUTSIDE - SPACESUIT", 660, 232, 12, Art.Orange);

        // Corridor corners
        var j = Get(OutpostRoom.Junction);
        DrawCircle(j.Pos, j.Radius, new Color(0.72f, 0.75f, 0.82f));
        var al = Get(OutpostRoom.Airlock);
        DrawRect(new Rect2(al.Pos - new Vector2(20, 20), new Vector2(40, 40)), new Color(0.75f, 0.78f, 0.85f));
        DrawRect(new Rect2(al.Pos - new Vector2(20, 20), new Vector2(40, 40)), Art.Orange, false, 3f);
        Art.TextCentered(this, "AIRLOCK", al.Pos.X - 58, al.Pos.Y + 5, 12, Art.Orange);

        for (int i = 0; i < _nodes.Length; i++)
            if (_nodes[i].IsRoom) DrawRoom(_nodes[i], i == _hover);

        // Nova (not a room - click her for help)
        if (!NovaPopup.IsTalking)
            Art.Nova(this, NovaPos, 1.3f, _time);
        DrawArc(NovaPos, 46f, 0, Mathf.Tau, 32, new Color(Art.Cyan, 0.4f), 2f);

        // Path + goal arrow
        if (_path.Count > 0)
        {
            var prev = _pos;
            foreach (var p in _path)
            {
                DrawDashedLine(prev, p, new Color(Art.Yellow, 0.7f), 3f, 10f);
                prev = p;
            }
        }
        if (_goal is OutpostRoom g)
        {
            var gp = Get(g).Pos;
            float bob = 6f * Mathf.Sin(_time * 5f);
            DrawColoredPolygon(new[] { gp + new Vector2(-16, -Get(g).Radius - 40 + bob), gp + new Vector2(16, -Get(g).Radius - 40 + bob),
                gp + new Vector2(0, -Get(g).Radius - 14 + bob) }, Art.Yellow);
            DrawArc(gp, Get(g).Radius + 10, 0, Mathf.Tau, 40, Art.Yellow, 4f);
        }

        for (int i = 0; i < _nodes.Length; i++)
            if (_nodes[i].IsRoom) DrawLabel(_nodes[i], i == _hover);

        // The astronaut
        Art.Astronaut(this, _pos + new Vector2(0, 22), 0.5f, _walk, _faceLeft);

        // Help strip
        var r2 = new Rect2(370, 678, 560, 30);
        Art.Panel(this, r2, Art.Yellow);
        Art.TextCentered(this, HelpText, r2.GetCenter().X, 699, 14, Art.Yellow);
    }

    private static Color Dim(Color c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);

    private void DrawRoom(Node r, bool hover)
    {
        Color L(Color c) => r.Lit ? c : c.Darkened(0.6f);
        var p = r.Pos;
        float t = _time;
        bool missionReady = r.Mission >= 0 && Gm.IsLevelUnlocked(r.Mission) && !Gm.IsLevelCompleted(r.Mission);

        if (missionReady)
            DrawCircle(p, r.Radius + 12 + 4 * Mathf.Sin(t * 3f), new Color(Art.Yellow, 0.18f));
        if (hover)
            DrawCircle(p, r.Radius + 8, new Color(Art.Yellow, 0.25f));

        if (r.Id == OutpostRoom.Drill)
        {
            DrawCircle(p, r.Radius, new Color(0.3f, 0.28f, 0.26f, 0.7f));                 // dug-up ground, no walls
        }
        else
        {
            DrawCircle(p, r.Radius, L(new Color(0.75f, 0.78f, 0.85f)));       // wall
            DrawCircle(p, r.Radius - 9, L(new Color(0.24f, 0.27f, 0.34f)));   // floor
        }

        switch (r.Id)
        {
            case OutpostRoom.Hub:
                DrawCircle(p, 16, L(Art.Cyan));
                break;
            case OutpostRoom.Oxygen:
                for (int k = -1; k <= 1; k++)
                {
                    DrawCircle(p + new Vector2(k * 34, -22), 18, L(Art.White));
                    DrawCircle(p + new Vector2(k * 34, -22), 7, L(Art.Cyan));
                }
                break;
            case OutpostRoom.Power:
                for (int k = 0; k < 2; k++)
                    DrawRect(new Rect2(p + new Vector2(-36 + k * 38, -40), new Vector2(32, 26)), L(new Color(0.15f, 0.35f, 0.85f)));
                break;
            case OutpostRoom.Shield:
                DrawArc(p, 40, Mathf.Pi, Mathf.Tau, 24, new Color(Art.Magenta, r.Lit ? 0.8f : 0.3f), 4f);
                break;
            case OutpostRoom.Plant:
                for (int k = 0; k < 5; k++)
                {
                    float a = Mathf.Pi + k * Mathf.Pi / 4f;
                    DrawCircle(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 38, 11, L(Art.Green.Darkened(0.15f)));
                }
                break;
            case OutpostRoom.Drill:
                DrawLine(p + new Vector2(-34, -30), p + new Vector2(0, -50), L(Art.Orange), 5f);
                DrawLine(p + new Vector2(34, -30), p + new Vector2(0, -50), L(Art.Orange), 5f);
                DrawCircle(p + new Vector2(0, -36), 8, L(Art.Steel));
                break;
            case OutpostRoom.Telescope:
                DrawArc(p + new Vector2(0, -18), 26, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 16, L(Art.White), 6f);
                DrawLine(p + new Vector2(0, -18), p + new Vector2(Mathf.Cos(t * 0.5f), -1) * 20 + new Vector2(0, -30), L(Art.Steel), 3f);
                break;
        }
    }

    private void DrawLabel(Node r, bool hover)
    {
        bool isMission = r.Mission >= 0;
        var center = new Vector2(r.Pos.X, r.Pos.Y + r.Radius + 16);
        var rect = new Rect2(center.X - 82, center.Y - 13, 164, isMission ? 40 : 24);
        Color border = hover ? Art.Yellow : r.Lit ? Art.Cyan : new Color(0.4f, 0.45f, 0.5f);
        Art.Panel(this, rect, border, 0.8f);
        string name = isMission ? $"{r.Name} ({r.Mission + 1})" : r.Name;
        Art.TextCentered(this, name, center.X, center.Y + 4, 13, r.Lit ? Art.Cyan : new Color(0.6f, 0.65f, 0.7f));

        if (isMission)
        {
            string status;
            Color color;
            if (!Gm.IsLevelUnlocked(r.Mission)) { status = "LOCKED"; color = new Color(0.55f, 0.57f, 0.6f); }
            else if (Gm.IsLevelCompleted(r.Mission)) { status = "COMPLETE"; color = Art.Green; }
            else { status = "WALK IN TO START"; color = Art.Yellow; }
            Art.TextCentered(this, status, center.X, center.Y + 21, 11, color);
        }
    }
}

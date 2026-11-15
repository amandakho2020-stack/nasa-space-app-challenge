using Godot;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// Glowing arrows on the FLOOR of the team's 3D outpost that show the way to the room you need
/// (like the guide lines in many games). They follow the corridors from where you stand, flow towards
/// the goal, also go out through the Rover Bay and down the rover road to the Core Drill, and show
/// on the minimap too.
///
/// The corridor network is the Points / Links tables below (world X/Z from EnvironmentBuilder.Base.cs).
/// If the 3D team moves a room or corridor, update those two tables.
/// Created by World3DBridge, which calls SetGoal(...).
/// </summary>
public partial class FloorArrows3D : Node3D
{
    // ---- Tuning ----
    private const float SpacingInside = 2.0f;   // metres between arrows in the base
    private const float SpacingOutside = 4.0f;  // ... and on the rover road
    private const float SizeInside = 1.35f;
    private const float SizeOutside = 2.2f;     // bigger outside, so you can follow them while driving
    private const float HideWithin = 3.5f;      // at the goal: no arrows needed
    private const float RefreshSeconds = 0.25f;
    private static readonly Color ArrowColor = new(1f, 0.85f, 0.2f, 0.95f);

    // ---- The corridor network: points (X, Z) and which ones are joined ----
    private static readonly Dictionary<string, Vector2> Points = new()
    {
        ["Lobby"] = new(0, 0),
        ["WestHub"] = new(-36, 0),
        ["EastHub"] = new(36, 0),
        ["OxygenDoor"] = new(-36, -20), ["Oxygen"] = new(-33.5f, -22.5f),
        ["PowerDoor"] = new(-55, 0), ["Power"] = new(-58.5f, 4f),
        ["ShieldDoor"] = new(-36, 20), ["Shield"] = new(-33.5f, 22.5f),
        ["PlantDoor"] = new(53, 0), ["Plant"] = new(57.5f, 5f),
        ["TelescopeDoor"] = new(36, 19), ["Telescope"] = new(38.5f, 21.5f),
        ["RoverJunction"] = new(13, 0),
        ["Airlock"] = new(13, 8),          // inside, just before the airlock door
        ["RoverBay"] = new(13, 20),        // outside: the rover is parked here
        ["DrillRoad"] = new(13, 186),      // end of the rover road
        ["Drill"] = new(17f, 195.5f),
        // outside: around the south of the Telescope dome to the Lunar Module on the launch pad (58, 32)
        ["PadRoad1"] = new(16, 40), ["PadRoad2"] = new(46, 40),
        ["Rocket"] = new(52.8f, 32f),      // the Lunar Module's ladder (MinigameTrigger3D)
    };

    private static readonly (string A, string B)[] Links =
    {
        ("Lobby", "WestHub"), ("Lobby", "RoverJunction"), ("RoverJunction", "EastHub"),
        ("WestHub", "OxygenDoor"), ("OxygenDoor", "Oxygen"),
        ("WestHub", "PowerDoor"), ("PowerDoor", "Power"),
        ("WestHub", "ShieldDoor"), ("ShieldDoor", "Shield"),
        ("EastHub", "PlantDoor"), ("PlantDoor", "Plant"),
        ("EastHub", "TelescopeDoor"), ("TelescopeDoor", "Telescope"),
        ("RoverJunction", "Airlock"), ("Airlock", "RoverBay"), ("RoverBay", "DrillRoad"), ("DrillRoad", "Drill"),
        ("RoverBay", "PadRoad1"), ("PadRoad1", "PadRoad2"), ("PadRoad2", "Rocket"),
    };

    private static string PointFor(OutpostRoom room) => room switch
    {
        OutpostRoom.Oxygen => "Oxygen",
        OutpostRoom.Power => "Power",
        OutpostRoom.Shield => "Shield",
        OutpostRoom.Plant => "Plant",
        OutpostRoom.Telescope => "Telescope",
        OutpostRoom.Drill => "Drill",
        OutpostRoom.Airlock => "Airlock",
        OutpostRoom.Rocket => "Rocket",
        _ => "Lobby",
    };

    /// <summary>Outside the base (the Rover Bay and the road): no ceiling, the ground is uneven.</summary>
    private static bool IsOutside(Vector2 p) =>
        (Mathf.Abs(p.X - 13f) < 6f && p.Y > 10.6f)   // Rover Bay + drill road
        || (p.X > 14f && p.Y >= 30f)                   // path south of the Telescope dome
        || (p.X > 46f && p.Y > 24f);                   // launch pad

    private MultiMeshInstance3D _arrows;
    private OutpostRoom? _goal;
    private float _timer;
    private Vector2 _lastStart = new(9999, 9999);

    public bool Allowed { get; set; } = true;

    public void SetGoal(OutpostRoom? goal)
    {
        _goal = goal;
        _lastStart = new Vector2(9999, 9999); // rebuild now
        _timer = 0f;
    }

    public override void _Ready()
    {
        var shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 color : source_color;
varying float along;
void vertex() { along = INSTANCE_CUSTOM.x; }
void fragment() {
    // a bright wave runs along the path towards the goal
    float wave = 0.5 + 0.5 * sin(along * 0.45 - TIME * 6.0);
    ALBEDO = color.rgb * (0.8 + 0.6 * wave);
    ALPHA = color.a * (0.55 + 0.45 * wave);
}",
        };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("color", ArrowColor);

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = BuildChevron(),
            InstanceCount = 0,
        };
        _arrows = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_arrows);
    }

    /// <summary>A flat "&gt;" chevron lying on the floor, pointing along +Z.</summary>
    private static ArrayMesh BuildChevron()
    {
        Vector2[] outline =
        {
            new(-0.45f, -0.3f), new(-0.25f, -0.3f), new(0f, 0.05f), new(0.25f, -0.3f),
            new(0.45f, -0.3f), new(0f, 0.32f),
        };
        int[] tris = Geometry2D.TriangulatePolygon(outline);
        var verts = new Vector3[tris.Length];
        for (int i = 0; i < tris.Length; i++)
            verts[i] = new Vector3(outline[tris[i]].X, 0f, outline[tris[i]].Y);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public override void _Process(double delta)
    {
        var player = global::GameManager.Instance?.Player;
        if (_goal == null || !Allowed || player == null)
        {
            _arrows.Visible = false;
            return;
        }

        Vector2 me = new(player.GlobalPosition.X, player.GlobalPosition.Z);
        Vector2 target = Points[PointFor(_goal.Value)];
        if (me.DistanceTo(target) < HideWithin)
        {
            _arrows.Visible = false;
            return;
        }
        _arrows.Visible = true;

        _timer -= (float)delta;
        if (_timer > 0f) return;
        _timer = RefreshSeconds;
        if (me.DistanceTo(_lastStart) < 1.5f) return; // hardly moved: keep the arrows
        _lastStart = me;
        Rebuild(me, PointFor(_goal.Value));
    }

    // ------------------------------------------------------------------
    // Path: from the nearest corridor to the goal (shortest way through the network)
    // ------------------------------------------------------------------
    private void Rebuild(Vector2 me, string goal)
    {
        List<Vector2> path = FindPath(me, goal);
        var space = GetWorld3D().DirectSpaceState;

        var spots = new List<(Vector3 Pos, Vector3 Dir, float Size, float Along)>();
        float along = 0f;
        float carry = 1.2f; // first arrow a little in front of you
        for (int s = 0; s + 1 < path.Count; s++)
        {
            Vector2 a = path[s], b = path[s + 1];
            float len = a.DistanceTo(b);
            if (len < 0.01f) continue;
            Vector2 dir = (b - a) / len;
            float d = carry;
            while (d < len)
            {
                Vector2 p = a + dir * d;
                bool outside = IsOutside(p);
                spots.Add((FloorAt(space, p, outside), new Vector3(dir.X, 0, dir.Y), outside ? SizeOutside : SizeInside, along + d));
                d += outside ? SpacingOutside : SpacingInside;
            }
            carry = d - len;
            along += len;
        }

        var mm = _arrows.Multimesh;
        mm.InstanceCount = spots.Count;
        for (int i = 0; i < spots.Count; i++)
        {
            var (pos, dir, size, dist) = spots[i];
            var basis = Basis.LookingAt(-dir, Vector3.Up).Scaled(Vector3.One * size); // +Z of the chevron -> dir
            mm.SetInstanceTransform(i, new Transform3D(basis, pos));
            mm.SetInstanceCustomData(i, new Color(dist, 0, 0, 0)); // distance along the path: the wave runs towards the goal
        }
    }

    /// <summary>Find the floor (or the ground outside) under a point.</summary>
    private Vector3 FloorAt(PhysicsDirectSpaceState3D space, Vector2 p, bool outside)
    {
        // inside: start below the corridor ceiling; Rover Bay: below its roof; the road: high above the hills
        float top = !outside ? 1.4f : p.Y < 30f ? 2.5f : 12f;
        var query = PhysicsRayQueryParameters3D.Create(new Vector3(p.X, top, p.Y), new Vector3(p.X, -3f, p.Y));
        var player = global::GameManager.Instance?.Player;
        if (player != null) query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        var hit = space.IntersectRay(query);
        float y = hit.Count > 0 ? ((Vector3)hit["position"]).Y : (outside ? 0f : 0.25f);
        return new Vector3(p.X, y + 0.04f, p.Y);
    }

    private static List<Vector2> FindPath(Vector2 me, string goal)
    {
        // 1. nearest corridor piece to the player, and the closest spot on it
        (string A, string B) best = Links[0];
        Vector2 bestSpot = Points[Links[0].A];
        float bestDist = float.MaxValue;
        foreach (var link in Links)
        {
            Vector2 spot = Geometry2D.GetClosestPointToSegment(me, Points[link.A], Points[link.B]);
            float d = spot.DistanceTo(me);
            if (d < bestDist) { bestDist = d; best = link; bestSpot = spot; }
        }

        // 2. shortest way from both ends of that piece (Dijkstra on a tiny network)
        var dist = new Dictionary<string, float>();
        var prev = new Dictionary<string, string>();
        foreach (var k in Points.Keys) dist[k] = float.MaxValue;
        dist[goal] = 0f;
        var open = new List<string>(Points.Keys);
        while (open.Count > 0)
        {
            open.Sort((x, y) => dist[x].CompareTo(dist[y]));
            string u = open[0];
            open.RemoveAt(0);
            if (dist[u] == float.MaxValue) break;
            foreach (var (a, b) in Links)
            {
                string v = a == u ? b : b == u ? a : null;
                if (v == null) continue;
                float nd = dist[u] + Points[u].DistanceTo(Points[v]);
                if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
            }
        }

        string first = bestSpot.DistanceTo(Points[best.A]) + dist[best.A] <= bestSpot.DistanceTo(Points[best.B]) + dist[best.B]
            ? best.A : best.B;

        // 3. player's spot -> first point -> ... -> goal (walk "prev" towards the goal)
        var path = new List<Vector2> { bestSpot };
        string at = first;
        path.Add(Points[at]);
        int guard = 0;
        while (at != goal && prev.ContainsKey(at) && guard++ < 50)
        {
            at = prev[at];
            path.Add(Points[at]);
        }
        return path;
    }
}

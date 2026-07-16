using System;
using System.Collections.Generic;
using Godot;
using Fableland.Run;
using Fableland.MapCreation.Data;

namespace Fableland.Map;

/// <summary>Combat cities, map functions, and the zone-6 river hub.</summary>
public enum NodeKind
{
    Combat,
    Boss,
    TransportHub,
    Shelter,
    Event,
    River,
}

/// <summary>The visual and traversal semantics of one map edge.</summary>
public enum MapEdgeKind
{
    Road,
    VoidPassage,
    RealityBridge,
}

/// <summary>Static definition of one fable world, which supplies one realm's identity per run.</summary>
public sealed class WorldDef
{
    public string Name;
    public string Abbr;
    public Color Color;

    public WorldDef(string name, string abbr, Color color)
    {
        Name = name;
        Abbr = abbr;
        Color = color;
    }

    public static readonly List<WorldDef> Pool = new()
    {
        new WorldDef("Starland",         "SL", new Color(0.95f, 0.55f, 0.15f)),
        new WorldDef("HollowCastle",     "HC", new Color(0.55f, 0.55f, 0.62f)),
        new WorldDef("VanillaKindom",    "VK", new Color(0.95f, 0.55f, 0.75f)),
        new WorldDef("TheDeserted",      "TD", new Color(0.85f, 0.75f, 0.40f)),
        new WorldDef("Palace of LOOING", "PL", new Color(0.62f, 0.38f, 0.78f)),
        new WorldDef("Banboo Maze",      "BM", new Color(0.20f, 0.48f, 0.28f)),
    };

    public static readonly Color VoidColor = new(0.05f, 0.05f, 0.07f);
}

public sealed class MapNode
{
    public string Id;
    public NodeKind Kind;
    public int WorldIndex;       // 0..4 realm, -1 zone 6
    public string Zone;
    public string LevelTag;
    public int Level;
    public Vector2 Pos;
    public Color Color;
    public bool Devoured;
    public MissionType Mission;
    public string Terrain = CombatMapTerrain.SeaLevel;
    public float Altitude = 0.5f;
    /// <summary>Voronoi-like city field clipped to this realm. The field changes to VOID ground
    /// when its controlling combat city is devoured.</summary>
    public Vector2[] ControlledField = Array.Empty<Vector2>();

    public bool IsCombat => Kind == NodeKind.Combat || Kind == NodeKind.Boss;
}

/// <summary>
/// One river-bounded outer realm. The five polygons collectively read as one island; their gaps
/// are rendered river water, never open sea. The shared height-field parameters are copied into
/// every realm so terrain remains continuous across the island.
/// </summary>
public sealed class WorldLandmass
{
    public int WorldIndex;
    public float CenterAngleDeg;
    public Vector2 MapCenter;
    public Vector2 IslandCenter;
    public Vector2 RadialAxis;
    public Vector2 TangentAxis;
    public float InnerRadius;
    public float OuterRadius;
    public Vector2[] Coast = Array.Empty<Vector2>();
    public Rect2 Bounds;

    public Vector2 HighlandCenter;
    public Vector2 LowlandCenter;
    public float HighlandRadius;
    public float LowlandRadius;
    public float NoisePhase;

    public bool Contains(Vector2 point)
    {
        if (Coast == null || Coast.Length < 3) return false;
        bool inside = false;
        for (int i = 0, j = Coast.Length - 1; i < Coast.Length; j = i++)
        {
            Vector2 a = Coast[i];
            Vector2 b = Coast[j];
            bool straddles = (a.Y > point.Y) != (b.Y > point.Y);
            if (!straddles) continue;
            float atX = (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X;
            if (point.X < atX) inside = !inside;
        }
        return inside;
    }

    public float AltitudeAt(Vector2 point)
    {
        float highR = Mathf.Max(1f, HighlandRadius);
        float lowR = Mathf.Max(1f, LowlandRadius);
        float high = Mathf.Exp(-HighlandCenter.DistanceSquaredTo(point) / (2f * highR * highR));
        float low = Mathf.Exp(-LowlandCenter.DistanceSquaredTo(point) / (2f * lowR * lowR));
        Vector2 local = point - HighlandCenter;
        float ripple = Mathf.Sin(local.Angle() * 3f + NoisePhase) * 0.045f;
        return Mathf.Clamp(0.47f + high * 0.40f - low * 0.32f + ripple, 0f, 1f);
    }
}

/// <summary>Variable-width water corridor that connects the central VOID to the outer coast.</summary>
public sealed class MapRiver
{
    public int BoundaryIndex;
    public Vector2[] LeftBank = Array.Empty<Vector2>();   // inner → coast
    public Vector2[] RightBank = Array.Empty<Vector2>();  // inner → coast
    public Vector2[] Polygon = Array.Empty<Vector2>();
}

public sealed class MapEdge
{
    public MapNode A;
    public MapNode B;
    public int Level;
    public bool Visible;
    public MapEdgeKind Kind;
    /// <summary>Precomputed legal road route. Reality bridges use a straight two-point route.</summary>
    public Vector2[] Route;

    public MapEdge(MapNode a, MapNode b, int level, bool visible = true,
        MapEdgeKind kind = MapEdgeKind.Road, Vector2[] route = null)
    {
        A = a;
        B = b;
        Level = level;
        Visible = visible;
        Kind = kind;
        Route = route ?? new[] { a.Pos, b.Pos };
    }

    public MapNode Other(MapNode n) => n == A ? B : A;
    public bool Touches(MapNode n) => A == n || B == n;
}

/// <summary>The full seeded map plus the mutable reality bridges created during a run.</summary>
public sealed class MapGraph
{
    public string Seed;
    public List<WorldDef> Worlds = new();
    public List<WorldLandmass> Landmasses = new();
    public Vector2[] IslandCoast = Array.Empty<Vector2>();
    public Vector2[] VoidPentagon = Array.Empty<Vector2>();
    public List<MapRiver> RealmRivers = new();
    public List<MapNode> Nodes = new();
    public List<MapEdge> Edges = new();
    public MapNode StartNode;

    public Vector2 Center;
    public float Zone6Radius;
    public float RiverRadius;
    public float LakeRadius;

    private Dictionary<MapNode, List<MapEdge>> _adj;

    public void BuildAdjacency()
    {
        _adj = new Dictionary<MapNode, List<MapEdge>>();
        foreach (MapNode n in Nodes) _adj[n] = new List<MapEdge>();
        foreach (MapEdge e in Edges) AddToAdjacency(e);
    }

    public void AddEdge(MapEdge edge)
    {
        Edges.Add(edge);
        if (_adj != null) AddToAdjacency(edge);
    }

    private void AddToAdjacency(MapEdge edge)
    {
        if (!_adj.TryGetValue(edge.A, out List<MapEdge> a)) _adj[edge.A] = a = new List<MapEdge>();
        if (!_adj.TryGetValue(edge.B, out List<MapEdge> b)) _adj[edge.B] = b = new List<MapEdge>();
        a.Add(edge);
        b.Add(edge);
    }

    public IEnumerable<MapEdge> EdgesOf(MapNode n)
    {
        if (n == null || _adj == null || !_adj.TryGetValue(n, out List<MapEdge> edges))
            return Array.Empty<MapEdge>();
        return edges;
    }

    public bool HasRealityBridge(MapNode node)
    {
        foreach (MapEdge edge in EdgesOf(node))
            if (edge.Kind == MapEdgeKind.RealityBridge) return true;
        return false;
    }

    public bool HasEdge(MapNode a, MapNode b)
    {
        foreach (MapEdge edge in EdgesOf(a))
            if ((edge.A == a && edge.B == b) || (edge.A == b && edge.B == a)) return true;
        return false;
    }

    /// <summary>Create the one persistent bridge permitted for both endpoints.</summary>
    public bool AddRealityBridge(MapNode a, MapNode b)
    {
        if (a == null || b == null || a == b || a.Devoured || b.Devoured ||
            a.WorldIndex < 0 || b.WorldIndex < 0 || a.WorldIndex == b.WorldIndex ||
            HasRealityBridge(a) || HasRealityBridge(b) || HasEdge(a, b))
            return false;
        AddEdge(new MapEdge(a, b, Mathf.Max(a.Level, b.Level), visible: true,
            kind: MapEdgeKind.RealityBridge, route: new[] { a.Pos, b.Pos }));
        return true;
    }

    /// <summary>Remove reality bridges whose endpoint has been consumed by the VOID. Removing
    /// the edge releases the surviving endpoint so it may be used by a later bridge.</summary>
    public int BreakRealityBridgesAtDevouredEndpoints()
    {
        var broken = new List<MapEdge>();
        foreach (MapEdge edge in Edges)
            if (edge.Kind == MapEdgeKind.RealityBridge && (edge.A.Devoured || edge.B.Devoured))
                broken.Add(edge);
        foreach (MapEdge edge in broken)
        {
            Edges.Remove(edge);
            if (_adj != null)
            {
                if (_adj.TryGetValue(edge.A, out List<MapEdge> a)) a.Remove(edge);
                if (_adj.TryGetValue(edge.B, out List<MapEdge> b)) b.Remove(edge);
            }
        }
        return broken.Count;
    }

    public Dictionary<MapNode, int> StepsFrom(MapNode from)
    {
        var dist = new Dictionary<MapNode, int> { [from] = 0 };
        var queue = new Queue<MapNode>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            MapNode current = queue.Dequeue();
            foreach (MapEdge edge in EdgesOf(current))
            {
                MapNode next = edge.Other(current);
                if (next.Devoured) continue;
                if (current.WorldIndex == -1 && next.WorldIndex != -1) continue;
                if (dist.ContainsKey(next)) continue;
                dist[next] = dist[current] + 1;
                queue.Enqueue(next);
            }
        }
        return dist;
    }
}

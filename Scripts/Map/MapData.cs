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

/// <summary>
/// Static definition of one fable world, which supplies one realm's identity per run.
///
/// <see cref="Pool"/> is every world a run can draw its five realms from: the eight worlds
/// whose combat realm is built (tile kit, art, maps — `MapGDD.md` §2). ORDER IS DATA: a seed
/// shuffles this list, so inserting or reordering changes every seed's realms. Append only.
/// A save records the pool it was generated from (<c>RunState.WorldPool</c>), so growing the
/// pool never moves a run that already exists.
/// </summary>
public sealed class WorldDef
{
    public string Name;
    public string Abbr;
    public Color Color;

    /// <summary>The protagonist whose home this world is (`MapGDD.md` §2, `NODES.gdd` §3.1):
    /// the one a run that starts here starts with, and the one beating this realm's 4-1
    /// recruits — whichever boss kit fights there. Null for a world with no protagonist yet.
    /// Not part of any seed: it changes who joins, never what a map generates.</summary>
    public string HomeProtagonistId;

    public WorldDef(string name, string abbr, Color color, string homeProtagonistId = null)
    {
        Name = name;
        Abbr = abbr;
        Color = color;
        HomeProtagonistId = homeProtagonistId;
    }

    // Homes (v0.12.9): Pomegraknight, PumpKing and Pixolotl from their GDDs and the world
    // table; Cleopastar → Starland and Sifu Pangda → Banboo Maze are PROVISIONAL — the same
    // calls her effects and his sounds already wear (`Feedback.gdd` §8, TBD #37/#38; the
    // presentation's `Medium.HomeOf` must agree, and the recruit check says so).
    public static readonly List<WorldDef> Pool = new()
    {
        new WorldDef("Starland",         "SL", new Color(0.95f, 0.55f, 0.15f), "Cleopastar"),
        new WorldDef("HollowCastle",     "HC", new Color(0.55f, 0.55f, 0.62f)),
        new WorldDef("VanillaKindom",    "VK", new Color(0.95f, 0.55f, 0.75f), "Pomegraknight"),
        new WorldDef("TheDeserted",      "TD", new Color(0.85f, 0.75f, 0.40f)),
        new WorldDef("Palace of LOOING", "PL", new Color(0.62f, 0.38f, 0.78f)),
        new WorldDef("Banboo Maze",      "BM", new Color(0.20f, 0.48f, 0.28f), "Sifu Pangda"),
        // v0.11.0 — the two worlds whose realms were built in v0.10.9 join the pool. Their
        // colours are the two hues the first six left free (blue is the rivers' and the
        // sea's): the Dark Wood's danger red, and the jade of the Forgotten City's canals.
        new WorldDef("Kingdom of Horror", "KH", new Color(0.78f, 0.20f, 0.24f), "PumpKing"),
        new WorldDef("The Forgotten",     "TF", new Color(0.10f, 0.66f, 0.62f), "Pixolotl"),
    };

    /// <summary>The protagonist whose home this world is, or null (no such world, or a world
    /// with no protagonist yet — HollowCastle, TheDeserted, the Palace of LOOING, zone 6).</summary>
    public static string HomeProtagonistOf(string abbr) => ByAbbr(abbr)?.HomeProtagonistId;

    /// <summary>The pool world that is this protagonist's home, or null.</summary>
    public static WorldDef HomeOf(string protagonistId)
    {
        if (string.IsNullOrWhiteSpace(protagonistId)) return null;
        foreach (WorldDef world in Pool)
            if (string.Equals(world.HomeProtagonistId, protagonistId, StringComparison.Ordinal)) return world;
        return null;
    }

    public static readonly Color VoidColor = new(0.05f, 0.05f, 0.07f);

    /// <summary>The pool's abbreviations, in pool order — what a new run records as the
    /// pool it was generated from.</summary>
    public static List<string> PoolAbbrs()
    {
        var abbrs = new List<string>(Pool.Count);
        foreach (WorldDef world in Pool) abbrs.Add(world.Abbr);
        return abbrs;
    }

    /// <summary>The pool world with this abbreviation (case-insensitive), or null.</summary>
    public static WorldDef ByAbbr(string abbr)
    {
        if (string.IsNullOrWhiteSpace(abbr)) return null;
        foreach (WorldDef world in Pool)
            if (string.Equals(world.Abbr, abbr.Trim(), StringComparison.OrdinalIgnoreCase)) return world;
        return null;
    }

    /// <summary>The worlds these abbreviations name, in the order given. Unknown ones are
    /// left out (a save may name a world this build no longer has).</summary>
    public static List<WorldDef> Resolve(IEnumerable<string> abbrs)
    {
        var worlds = new List<WorldDef>();
        if (abbrs == null) return worlds;
        foreach (string abbr in abbrs)
        {
            WorldDef world = ByAbbr(abbr);
            if (world != null && !worlds.Contains(world)) worlds.Add(world);
        }
        return worlds;
    }
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
    /// <summary>True when this completed outer city can anchor Bridge of Eidolon. LV4 capitals
    /// are always marked; other cities are derived from their field touching a VOID-river bank.</summary>
    public bool IsVoidRiverPeripheral;
    /// <summary>Non-empty only for the transient Eidolon Shelter created at a reality bridge's
    /// midpoint. The id also joins the two bridge legs.</summary>
    public string RealityBridgeId;
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
    /// <summary>Shared by the two legs of an Eidolon bridge; empty for ordinary roads.</summary>
    public string RealityBridgeId;
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

    /// <summary>Create Bridge of Eidolon: two violet legs with a mandatory midpoint shelter.
    /// Either endpoint may be Zone 6, but ordinary outer bridges must join different realms.</summary>
    public bool AddRealityBridge(MapNode a, MapNode b, out MapNode shelter)
    {
        shelter = null;
        if (a == null || b == null || a == b || a.Devoured || b.Devoured ||
            a.WorldIndex < 0 || (b.WorldIndex >= 0 && a.WorldIndex == b.WorldIndex) ||
            HasRealityBridge(a) || HasRealityBridge(b) || HasEdge(a, b))
            return false;

        string bridgeId = $"TR-{a.Id}-{b.Id}";
        shelter = new MapNode
        {
            Id = $"{bridgeId}-S", Kind = NodeKind.Shelter, WorldIndex = a.WorldIndex,
            Zone = a.Zone, LevelTag = "E", Level = Mathf.Max(a.Level, b.Level),
            Pos = (a.Pos + b.Pos) * 0.5f, Color = new Color(0.88f, 0.66f, 1f),
            RealityBridgeId = bridgeId,
        };
        Nodes.Add(shelter);
        if (_adj != null) _adj[shelter] = new List<MapEdge>();
        AddRealityLeg(a, shelter, bridgeId);
        AddRealityLeg(shelter, b, bridgeId);
        return true;
    }

    private void AddRealityLeg(MapNode a, MapNode b, string bridgeId)
    {
        var edge = new MapEdge(a, b, Mathf.Max(a.Level, b.Level), visible: true,
            kind: MapEdgeKind.RealityBridge, route: new[] { a.Pos, b.Pos }) { RealityBridgeId = bridgeId };
        AddEdge(edge);
    }

    /// <summary>Returns the two endpoint cities of a dynamic Eidolon Shelter.</summary>
    public bool TryGetRealityBridgeEndpoints(MapNode shelter, out MapNode a, out MapNode b)
    {
        a = null;
        b = null;
        if (shelter == null || string.IsNullOrWhiteSpace(shelter.RealityBridgeId)) return false;
        foreach (MapEdge edge in EdgesOf(shelter))
        {
            if (edge.Kind != MapEdgeKind.RealityBridge || edge.RealityBridgeId != shelter.RealityBridgeId) continue;
            MapNode endpoint = edge.Other(shelter);
            if (a == null) a = endpoint;
            else if (b == null) b = endpoint;
        }
        return a != null && b != null;
    }

    public IEnumerable<MapNode> RealityBridgeShelters()
    {
        foreach (MapNode node in Nodes)
            if (!string.IsNullOrWhiteSpace(node.RealityBridgeId)) yield return node;
    }

    /// <summary>Remove reality bridges whose endpoint has been consumed by the VOID. Removing
    /// the edge releases the surviving endpoint so it may be used by a later bridge.</summary>
    public int BreakRealityBridgesAtDevouredEndpoints()
    {
        var collapsing = new List<MapNode>();
        foreach (MapNode shelter in RealityBridgeShelters())
            if (TryGetRealityBridgeEndpoints(shelter, out MapNode a, out MapNode b) && (a.Devoured || b.Devoured))
                collapsing.Add(shelter);

        foreach (MapNode shelter in collapsing)
        {
            var legs = new List<MapEdge>(EdgesOf(shelter));
            foreach (MapEdge leg in legs)
            {
                Edges.Remove(leg);
                if (_adj != null)
                {
                    if (_adj.TryGetValue(leg.A, out List<MapEdge> from)) from.Remove(leg);
                    if (_adj.TryGetValue(leg.B, out List<MapEdge> to)) to.Remove(leg);
                }
            }
            Nodes.Remove(shelter);
            _adj?.Remove(shelter);
        }
        return collapsing.Count;
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

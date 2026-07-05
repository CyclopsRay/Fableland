using System.Collections.Generic;
using Godot;

namespace Fableland.Map;

/// <summary>What a node is. Combat = a fight; the rest are function/structure nodes.</summary>
public enum NodeKind
{
    Combat,       // a level fight (numeric level == difficulty)
    Boss,         // level-4 combat, world boss room
    Shelter,      // camp: rest / build / upgrade / wish (+ surprises TODO)
    QuestionMark, // forbidden / teleport / other — TODO content
    River,        // zone-6 river of the VOID (shelter-beyond hub)
}

/// <summary>Static definition of one fable world (a 72° fan on the map).</summary>
public sealed class WorldDef
{
    public string Name;
    public string Abbr;   // 2-letter zone tag, e.g. "SL"
    public Color Color;   // main palette color

    public WorldDef(string name, string abbr, Color color)
    {
        Name = name; Abbr = abbr; Color = color;
    }

    /// <summary>All six defined worlds. Each run randomly picks 5 for the outer zones.</summary>
    public static readonly List<WorldDef> Pool = new()
    {
        new WorldDef("Starland",         "SL", new Color(0.95f, 0.55f, 0.15f)), // orange
        new WorldDef("HollowCastle",     "HC", new Color(0.55f, 0.55f, 0.62f)), // grey
        new WorldDef("VanillaKindom",    "VK", new Color(0.95f, 0.55f, 0.75f)), // pink
        new WorldDef("TheDeserted",      "TD", new Color(0.85f, 0.75f, 0.40f)), // sandy yellow
        new WorldDef("Palace of LOOING", "PL", new Color(0.62f, 0.38f, 0.78f)), // purple
        new WorldDef("Banboo Maze",      "BM", new Color(0.20f, 0.48f, 0.28f)), // dark green
    };

    /// <summary>The VOID / zone-6 color (the DARKEST). Abbr "XX".</summary>
    public static readonly Color VoidColor = new(0.05f, 0.05f, 0.07f);
}

public sealed class MapNode
{
    public string Id;          // e.g. "SL-1-3", "SL-4-a", "XX-5-2"
    public NodeKind Kind;
    public int WorldIndex;     // 0..4 for outer worlds, -1 for zone 6
    public string Zone;        // world abbr, or "XX" for zone 6
    public string LevelTag;    // "1-A","1-B","2-A","2-B","3","4","5","6","R" (combat) or edge-level for function
    public int Level;          // numeric difficulty level: 1..6
    public Vector2 Pos;
    public Color Color;
    public bool Devoured;      // eaten by the VOID (unavailable)

    public bool IsCombat => Kind == NodeKind.Combat || Kind == NodeKind.Boss;
}

public sealed class MapEdge
{
    public MapNode A;
    public MapNode B;
    public int Level;        // numeric level of the higher/inner node the edge belongs to
    public bool Considered;  // has the function-node pass looked at this edge?
    public bool Visible;     // zone 1-5 edges are grey lines; zone 6 edges are invisible

    public MapEdge(MapNode a, MapNode b, int level, bool visible = true)
    {
        A = a; B = b; Level = level; Visible = visible;
    }

    public MapNode Other(MapNode n) => n == A ? B : A;
    public bool Touches(MapNode n) => A == n || B == n;
}

/// <summary>The fully generated map for one seed.</summary>
public sealed class MapGraph
{
    public string Seed;
    public List<WorldDef> Worlds = new();   // the 5 chosen, in ring order
    public List<MapNode> Nodes = new();
    public List<MapEdge> Edges = new();
    public MapNode StartNode;

    // Zone-6 draw hints.
    public Vector2 Center;
    public float RiverRadius;
    public float LakeRadius;

    private Dictionary<MapNode, List<MapEdge>> _adj;

    public void BuildAdjacency()
    {
        _adj = new Dictionary<MapNode, List<MapEdge>>();
        foreach (var n in Nodes) _adj[n] = new List<MapEdge>();
        foreach (var e in Edges)
        {
            _adj[e.A].Add(e);
            _adj[e.B].Add(e);
        }
    }

    public IEnumerable<MapEdge> EdgesOf(MapNode n) => _adj[n];

    /// <summary>
    /// Shortest number of steps (edges) from <paramref name="from"/> to every reachable node,
    /// over currently-available edges. Devoured nodes are skipped. Zone-6 is one-way: once you're
    /// in zone 6 you may not step back out to an outer zone.
    /// </summary>
    public Dictionary<MapNode, int> StepsFrom(MapNode from)
    {
        var dist = new Dictionary<MapNode, int> { [from] = 0 };
        var q = new Queue<MapNode>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in _adj[cur])
            {
                var nxt = e.Other(cur);
                if (nxt.Devoured) continue;
                // One-way into the VOID: no returning to outer zones.
                if (cur.WorldIndex == -1 && nxt.WorldIndex != -1) continue;
                if (dist.ContainsKey(nxt)) continue;
                dist[nxt] = dist[cur] + 1;
                q.Enqueue(nxt);
            }
        }
        return dist;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fableland.Map;

/// <summary>
/// The "rendered atlas" precompute: turns the topological <see cref="MapGraph"/> into a
/// real-worldmap layout without touching gameplay. Each combat/function node becomes a
/// weighted Voronoi (power-diagram) *territory* — a city plus its regime area — clipped to
/// its realm's island. Borders between neighbouring territories are classified: a graph edge
/// => a road; no edge => a barrier (themed per world). Cross-realm links render as golden
/// sea causeways; zone 6 is a central pentagon.
///
/// This is DATA ONLY (polygons, segments, labels). <see cref="MapController"/> draws it and
/// layers live state (visited / devoured / mist) on top. Everything is derived from the run
/// seed (positions come straight from the generator; any jitter uses a DetRandom(seed+"R")),
/// so the atlas is reproducible.
///
/// For the current pass, barriers are only MARKED (a labelled point marker or a tinted region
/// + label) — no detail art yet. See Docs/MapGDD.md §11.
/// </summary>
public static class MapRenderModel
{
    // ---- per-world barrier / land theme -----------------------------------------
    public sealed class WorldTheme
    {
        public string Abbr;
        public string PointBarrier;  // small landmark on a blocked would-be road
        public string AreaBarrier;   // region filler between disconnected neighbours
        public Color Land;           // territory base tint
        public Color AreaColor;      // area-barrier tint
        public Color PointColor;     // point-barrier marker
    }

    // Placeholder colours; art comes later. Names are the source of truth for now.
    private static readonly Dictionary<string, WorldTheme> Themes = new()
    {
        ["SL"] = new() { Abbr = "SL", PointBarrier = "city debris",        AreaBarrier = "meteor-strike warfield", Land = new(0.95f, 0.72f, 0.45f), AreaColor = new(0.62f, 0.26f, 0.20f), PointColor = new(0.55f, 0.50f, 0.45f) },
        ["HC"] = new() { Abbr = "HC", PointBarrier = "ruined walls",       AreaBarrier = "dark forest",            Land = new(0.66f, 0.68f, 0.72f), AreaColor = new(0.14f, 0.28f, 0.18f), PointColor = new(0.45f, 0.45f, 0.50f) },
        ["VK"] = new() { Abbr = "VK", PointBarrier = "burned villages",    AreaBarrier = "lake",                   Land = new(0.97f, 0.72f, 0.82f), AreaColor = new(0.28f, 0.50f, 0.78f), PointColor = new(0.28f, 0.20f, 0.20f) },
        ["TD"] = new() { Abbr = "TD", PointBarrier = "giant beast skull",  AreaBarrier = "desert",                 Land = new(0.90f, 0.82f, 0.55f), AreaColor = new(0.85f, 0.74f, 0.44f), PointColor = new(0.92f, 0.90f, 0.82f) },
        ["PL"] = new() { Abbr = "PL", PointBarrier = "the protected throne", AreaBarrier = "abyss / quagmire",     Land = new(0.74f, 0.58f, 0.86f), AreaColor = new(0.24f, 0.14f, 0.32f), PointColor = new(0.62f, 0.46f, 0.74f) },
        ["BM"] = new() { Abbr = "BM", PointBarrier = "deserted woodhouse", AreaBarrier = "bamboo forest",          Land = new(0.55f, 0.75f, 0.52f), AreaColor = new(0.24f, 0.52f, 0.28f), PointColor = new(0.52f, 0.36f, 0.20f) },
    };
    private static readonly WorldTheme VoidTheme = new()
    {
        Abbr = "XX", PointBarrier = "", AreaBarrier = "the VOID",
        Land = new(0.10f, 0.10f, 0.16f), AreaColor = new(0.05f, 0.05f, 0.10f), PointColor = new(0.2f, 0.2f, 0.3f),
    };

    public static WorldTheme ThemeFor(string abbr) =>
        abbr == "XX" ? VoidTheme : (Themes.TryGetValue(abbr, out var t) ? t : VoidTheme);

    // ---- claim radius by node kind (controls territory size) --------------------
    // Weight in the power diagram is claimRadius²; a bigger claim => a bigger cell.
    private static float ClaimRadius(NodeKind k) => k switch
    {
        NodeKind.Boss => 78f,
        NodeKind.Combat => 66f,
        NodeKind.River => 40f,
        _ => 30f, // Shelter / QuestionMark — the small function territories
    };

    // ---- output structures ------------------------------------------------------
    public enum BorderKind { Road, Barrier }

    public sealed class Territory
    {
        public MapNode Node;
        public Vector2[] Poly;
        public Color Fill;
        public int WorldIndex;   // 0..4 outer, -1 zone 6
    }

    public sealed class BorderSeg
    {
        public Vector2 A, B;
        public BorderKind Kind;
        public int WorldIndex;   // theme owner (the world the barrier belongs to)
    }

    public sealed class PointBarrier
    {
        public Vector2 Pos;
        public int WorldIndex;
        public string Label;
    }

    public sealed class AreaLabel
    {
        public Vector2 Pos;
        public int WorldIndex;
        public string Label;
    }

    public sealed class Road
    {
        public MapNode A, B;
        public Vector2 Ctrl;      // quadratic bezier control point
        public bool CrossWorld;   // golden sea causeway between realms / into the VOID
    }

    public sealed class RenderedMap
    {
        public List<Territory> Territories = new();
        public List<BorderSeg> Borders = new();     // Road + Barrier segments (barriers get themed)
        public List<PointBarrier> Points = new();
        public List<AreaLabel> Areas = new();
        public List<Road> Roads = new();
        public List<Vector2[]> Islands = new();      // per outer world, the landmass outline
        public Vector2[] Pentagon;                    // zone-6 land
        public List<Territory> Zone6Cells = new();
        public List<MapNode> Islets = new();          // XX-S shelters, isolated in the sea ring
        public Dictionary<MapNode, int> NodeWorld = new(); // resolved realm (-1 void, -3 sea bridge)
    }

    // =============================================================================
    public static RenderedMap Build(MapGraph g)
    {
        var rm = new RenderedMap();

        // Resolve every node's realm once (needed for road styling + tiling membership).
        foreach (var n in g.Nodes) rm.NodeWorld[n] = ResolveWorld(g, n);

        // Fast lookup: does a graph edge connect these two nodes?
        var linked = new HashSet<(MapNode, MapNode)>();
        foreach (var e in g.Edges) { linked.Add((e.A, e.B)); linked.Add((e.B, e.A)); }
        bool Linked(MapNode a, MapNode b) => linked.Contains((a, b));

        // Midpoint of each barrier border between two cities — used to place a point barrier
        // ONLY on a blocked pass you can actually see across (kills the label spam).
        var barrierMid = new Dictionary<(MapNode, MapNode), Vector2>();

        // ---- (1) outer realms: weighted Voronoi territories per world ------------
        int worldCount = g.Worlds.Count;
        for (int w = 0; w < worldCount; w++)
        {
            var island = BuildIsland(g.Center, w, worldCount);
            rm.Islands.Add(island.ToArray());

            var theme = ThemeFor(g.Worlds[w].Abbr);
            var sites = g.Nodes.Where(n => rm.NodeWorld[n] == w && n.WorldIndex != -1).ToList();
            if (sites.Count == 0) continue;

            var cells = PowerCells(sites, island);

            for (int i = 0; i < sites.Count; i++)
            {
                if (cells[i].Verts.Count < 3) continue;
                Color fill = theme.Land.Lerp(g.Worlds[w].Color, 0.25f);
                if (sites[i].WorldIndex == -2) fill = fill.Lightened(0.12f); // function territories a touch paler
                rm.Territories.Add(new Territory { Node = sites[i], Poly = cells[i].Verts.ToArray(), Fill = fill, WorldIndex = w });

                // Borders: each cell edge sourced by another site j (i<j) is a shared frontier.
                var (verts, srcs) = (cells[i].Verts, cells[i].Srcs);
                for (int k = 0; k < verts.Count; k++)
                {
                    int j = srcs[k];
                    if (j < 0 || j <= i) continue; // -1 coast, or already handled from the other side
                    var a = verts[k];
                    var b = verts[(k + 1) % verts.Count];
                    bool road = Linked(sites[i], sites[j]);
                    rm.Borders.Add(new BorderSeg { A = a, B = b, WorldIndex = w, Kind = road ? BorderKind.Road : BorderKind.Barrier });
                    if (!road)
                    {
                        var m = (a + b) * 0.5f;
                        barrierMid[(sites[i], sites[j])] = m;
                        barrierMid[(sites[j], sites[i])] = m;
                    }
                }
            }

            // Area label: drop one at the centroid of this world's barrier midpoints.
            var barMids = rm.Borders.Where(bd => bd.WorldIndex == w && bd.Kind == BorderKind.Barrier)
                                    .Select(bd => (bd.A + bd.B) * 0.5f).ToList();
            if (barMids.Count > 0)
            {
                var c = Vector2.Zero;
                foreach (var m in barMids) c += m;
                rm.Areas.Add(new AreaLabel { Pos = c / barMids.Count, WorldIndex = w, Label = theme.AreaBarrier });
            }
        }

        // ---- (2) point barriers: landmarks on blocked would-be roads -------------
        foreach (var (a, b) in g.FailedCandidates)
        {
            if (a.WorldIndex < 0 || a.WorldIndex != b.WorldIndex) continue;   // intra-world only
            if (!barrierMid.TryGetValue((a, b), out var pos)) continue;       // only if the cities actually border
            rm.Points.Add(new PointBarrier { Pos = pos, WorldIndex = a.WorldIndex, Label = ThemeFor(a.Zone).PointBarrier });
        }

        // ---- (3) roads (replace edges) -------------------------------------------
        var rng = new DetRandom(g.Seed + "R");
        foreach (var e in g.Edges)
        {
            int wa = rm.NodeWorld[e.A], wb = rm.NodeWorld[e.B];
            // The isolated XX-S shelters bridge a realm's boss to the VOID: keep those legs
            // (as golden causeways) but drop the purely-internal zone-6 edges (lv5/river/core).
            bool islet = (e.A.WorldIndex == -1 && e.A.Kind == NodeKind.Shelter)
                      || (e.B.WorldIndex == -1 && e.B.Kind == NodeKind.Shelter);
            if (wa == -1 && wb == -1 && !islet) continue;
            bool cross = wa != wb || islet;
            var mid = (e.A.Pos + e.B.Pos) * 0.5f;
            var dir = (e.B.Pos - e.A.Pos);
            var perp = new Vector2(-dir.Y, dir.X).Normalized();
            float wobble = (float)(rng.NextDouble() - 0.5) * dir.Length() * 0.18f;
            rm.Roads.Add(new Road { A = e.A, B = e.B, Ctrl = mid + perp * wobble, CrossWorld = cross });
        }

        // ---- (4) zone 6: central pentagon + 5 lv5 territories --------------------
        rm.Pentagon = RegularPolygon(g.Center, g.Zone6Radius, 5, -90f);
        var lv5 = g.Nodes.Where(n => n.WorldIndex == -1 && n.Kind == NodeKind.Combat && n.LevelTag == "5").ToList();
        if (lv5.Count >= 3)
        {
            var pentaCells = PowerCells(lv5, rm.Pentagon.ToList());
            for (int i = 0; i < lv5.Count; i++)
                if (pentaCells[i].Verts.Count >= 3)
                    rm.Zone6Cells.Add(new Territory { Node = lv5[i], Poly = pentaCells[i].Verts.ToArray(), Fill = VoidTheme.Land, WorldIndex = -1 });
        }

        // XX-S shelters sit isolated in the sea ring between the realms and the pentagon.
        rm.Islets = g.Nodes.Where(n => n.WorldIndex == -1 && n.Kind == NodeKind.Shelter).ToList();

        return rm;
    }

    // ---- realm resolution -------------------------------------------------------
    /// <summary>Realm a node belongs to: 0..4 outer world, -1 the VOID, -3 a between-realm sea bridge.</summary>
    private static int ResolveWorld(MapGraph g, MapNode n)
    {
        if (n.WorldIndex >= 0) return n.WorldIndex;
        if (n.WorldIndex == -1) return -1;
        // function node (-2): belongs to the single outer world of its neighbours; else it's a bridge.
        var worlds = new HashSet<int>();
        foreach (var e in g.EdgesOf(n))
        {
            var m = e.Other(n);
            if (m.WorldIndex >= 0) worlds.Add(m.WorldIndex);
        }
        return worlds.Count == 1 ? worlds.First() : -3;
    }

    // ---- island / pentagon geometry ---------------------------------------------
    /// <summary>Convex landmass wedge for outer world w: an outer arc truncated by an inner chord.</summary>
    private static List<Vector2> BuildIsland(Vector2 center, int w, int worldCount)
    {
        float centerDeg = -90f + w * (360f / worldCount);
        const float half = 33f;      // fan is 72°; islands span 66°, leaving sea channels between realms
        float ro = MapGenerator.RimRadius;
        float ri = MapGenerator.LayoutScale * 102f; // just outside the zone-6 pentagon (r=96), inside lv4 (r=110)
        var pts = new List<Vector2>();
        int seg = 10;
        for (int i = 0; i <= seg; i++) // outer arc, left→right
        {
            float d = centerDeg - half + (2 * half) * i / seg;
            pts.Add(center + Polar(d, ro));
        }
        pts.Add(center + Polar(centerDeg + half, ri)); // inner corners (chord truncates the tip)
        pts.Add(center + Polar(centerDeg - half, ri));
        return pts;
    }

    private static Vector2[] RegularPolygon(Vector2 c, float r, int n, float startDeg)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) pts[i] = c + Polar(startDeg + i * (360f / n), r);
        return pts;
    }

    private static Vector2 Polar(float deg, float r)
    {
        float rad = Mathf.DegToRad(deg);
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r;
    }

    // ---- weighted Voronoi (power diagram) ---------------------------------------
    private struct Cell { public List<Vector2> Verts; public List<int> Srcs; }

    /// <summary>
    /// Power-diagram cells for <paramref name="sites"/>, each clipped to the convex
    /// <paramref name="clip"/> polygon. Each cell edge is tagged with the index of the
    /// neighbouring site that produced it (-1 = the clip boundary / coast).
    /// </summary>
    private static Cell[] PowerCells(List<MapNode> sites, List<Vector2> clip)
    {
        int n = sites.Count;
        var weights = sites.Select(s => { float r = ClaimRadius(s.Kind); return r * r; }).ToArray();
        var cells = new Cell[n];
        for (int i = 0; i < n; i++)
        {
            var verts = new List<Vector2>(clip);
            var srcs = Enumerable.Repeat(-1, clip.Count).ToList();
            Vector2 pi = sites[i].Pos;
            for (int j = 0; j < n && verts.Count >= 3; j++)
            {
                if (j == i) continue;
                Vector2 pj = sites[j].Pos;
                Vector2 nrm = pj - pi;
                if (nrm.LengthSquared() < 1e-6f) continue;
                // keep half-plane: |x-pi|² - wi <= |x-pj|² - wj  ⇔  nrm·x <= c
                float c = (pj.LengthSquared() - pi.LengthSquared() - (weights[j] - weights[i])) * 0.5f;
                ClipHalfPlane(ref verts, ref srcs, nrm, c, j);
            }
            cells[i] = new Cell { Verts = verts, Srcs = srcs };
        }
        return cells;
    }

    /// <summary>
    /// Sutherland–Hodgman clip of a polygon by the half-plane { x : nrm·x &lt;= c }.
    /// srcs[k] is the source tag of the edge leaving vertex k; new edges laid along the
    /// clip line are tagged <paramref name="clipSrc"/>. Subject stays convex (island ∩ planes).
    /// </summary>
    private static void ClipHalfPlane(ref List<Vector2> verts, ref List<int> srcs, Vector2 nrm, float c, int clipSrc)
    {
        var outV = new List<Vector2>();
        var outS = new List<int>();
        int m = verts.Count;
        for (int i = 0; i < m; i++)
        {
            Vector2 cur = verts[i], nxt = verts[(i + 1) % m];
            int s = srcs[i];
            float fCur = nrm.Dot(cur) - c;
            float fNxt = nrm.Dot(nxt) - c;
            bool inCur = fCur <= 0f, inNxt = fNxt <= 0f;
            if (inCur)
            {
                outV.Add(cur); outS.Add(s);
                if (!inNxt)
                {
                    float t = fCur / (fCur - fNxt);
                    outV.Add(cur.Lerp(nxt, t)); outS.Add(clipSrc); // edge from here rides the clip line
                }
            }
            else if (inNxt)
            {
                float t = fCur / (fCur - fNxt);
                outV.Add(cur.Lerp(nxt, t)); outS.Add(s);           // resume the original edge to nxt
            }
        }
        if (outV.Count < 3) { verts = new List<Vector2>(); srcs = new List<int>(); return; }
        verts = outV; srcs = outS;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fableland.Run;

namespace Fableland.Map;

/// <summary>
/// Procedural world-map generator. Everything is driven by the run seed through a
/// single <see cref="DetRandom"/>, so a seed reproduces the whole map exactly.
///
/// Pipeline (per the map GDD, Docs/MapGDD.md):
///   1. pick 5 worlds, lay out combat nodes on 72° fans (levels 1-A..4)
///   2. intra-world edges (higher-node links, siblings, the fixed lv3/lv4 links)
///   3. inter-world edges (the lv3 ring, plus 2-A/1-A cross links)
///   4. function nodes (edge crossings, then per-edge probability) — shelter / questionmark
///   5. zone 6 (the VOID): 5 lv5 nodes round the lake, the river ring, the lv6 core
/// </summary>
public static class MapGenerator
{
    // World layout. Radii shrink from the outer rim (level 1-A) toward the VOID.
    // LayoutScale blows the whole map up uniformly (v0.3.2, for the rendered atlas + camera):
    // a uniform scale preserves all topology/crossings, so a given seed yields the same map,
    // just bigger. Both views project world→screen in MapController (Project), no Camera2D.
    public const float LayoutScale = 1.8f;
    public static readonly Vector2 Center = new(576, 340);

    // Outer edge of the playable disc (just past level 1-A) — used to size world islands/wedges.
    public const float RimRadius = 300f * LayoutScale + 30f;

    private static readonly Dictionary<string, float> Radius = new()
    {
        ["1-A"] = 300f * LayoutScale, ["1-B"] = 262f * LayoutScale,
        ["2-A"] = 224f * LayoutScale, ["2-B"] = 190f * LayoutScale,
        ["3"] = 150f * LayoutScale, ["4"] = 110f * LayoutScale,
        ["5"] = 78f * LayoutScale,   // ring of lv5 nodes — sits INSIDE the zone-6 disc, around the lake
    };
    private const float Zone6Radius = 96f * LayoutScale; // dark disc bounding zone 6 (lv5 ring is inside it)
    private const float RiverRadius = 38f * LayoutScale;
    private const float LakeRadius = 58f * LayoutScale;

    // Outer→inner ordering of the outer-zone sublevels.
    private static readonly string[] OuterTags = { "1-A", "1-B", "2-A", "2-B", "3", "4" };

    // The active character always starts in their home world (zone index 0).
    // Pomegraknight's home is VanillaKindom (pink). Other playable characters will
    // pass their own home abbr when they unlock.
    public const string PomegraknightHome = "VK";

    public static MapGraph Generate(string seed, string homeAbbr = PomegraknightHome)
    {
        var rng = new DetRandom(seed);
        var g = new MapGraph { Seed = seed, Center = Center, Zone6Radius = Zone6Radius, RiverRadius = RiverRadius, LakeRadius = LakeRadius };

        // --- pick 5 of 6 worlds, in ring order. The home world is always present and
        //     placed at index 0 (the start zone); the other 4 are random from the rest. ---
        var pool = new List<WorldDef>(WorldDef.Pool);
        var home = pool.Find(w => w.Abbr == homeAbbr) ?? pool[0];
        pool.RemoveAll(w => w.Abbr == home.Abbr);
        rng.Shuffle(pool);
        g.Worlds = new List<WorldDef> { home };
        g.Worlds.AddRange(pool.Take(4));

        // nodes[worldIndex][tag] = ordered list of combat nodes in that sublevel
        var byWorld = new List<Dictionary<string, List<MapNode>>>();

        for (int w = 0; w < 5; w++)
        {
            var world = g.Worlds[w];
            float worldCenterDeg = -90f + w * 72f; // world 0 faces up; worlds go clockwise
            var slot = new Dictionary<string, List<MapNode>>();

            // per-world node counts
            int n1a = rng.Chance(0.5) ? 4 : 3;
            int n1b = rng.Chance(0.5) ? 4 : 3;
            int n2a = rng.Range(2, 4);            // 2..4
            int n2b = rng.Chance(0.5) ? 3 : 2;    // 2 or 3

            // level-1 numbering runs across 1-A then 1-B; same for level-2.
            int lvl1Run = 1;
            slot["1-A"] = PlaceRow(g, world, w, worldCenterDeg, "1-A", 1, n1a, ref lvl1Run, NodeKind.Combat);
            slot["1-B"] = PlaceRow(g, world, w, worldCenterDeg, "1-B", 1, n1b, ref lvl1Run, NodeKind.Combat);

            int lvl2Run = 1;
            slot["2-A"] = PlaceRow(g, world, w, worldCenterDeg, "2-A", 2, n2a, ref lvl2Run, NodeKind.Combat);
            slot["2-B"] = PlaceRow(g, world, w, worldCenterDeg, "2-B", 2, n2b, ref lvl2Run, NodeKind.Combat);

            int lvl3Run = 1;
            slot["3"] = PlaceRow(g, world, w, worldCenterDeg, "3", 3, 2, ref lvl3Run, NodeKind.Combat);

            int lvl4Run = 1;
            slot["4"] = PlaceRow(g, world, w, worldCenterDeg, "4", 4, 1, ref lvl4Run, NodeKind.Boss);

            byWorld.Add(slot);
        }

        // --- (2) intra-world edges ---
        for (int w = 0; w < 5; w++)
            BuildIntraWorldEdges(g, byWorld[w], rng);

        // --- (3) inter-world edges ---
        BuildInterWorldEdges(g, byWorld, rng);

        // --- (5) zone 6 (build before function nodes so lv4 shelters into the VOID exist,
        //         but keep its edges out of the crossing/probability passes via Visible=false) ---
        BuildZone6(g, byWorld);

        // --- (4) function nodes (zone 1-5 visible edges only) ---
        GenerateFunctionNodes(g, rng);

        // start: a random 1-A node in the first world; guarantee its cross-world lv1 link
        g.StartNode = rng.Pick(byWorld[0]["1-A"]);

        // --- (6) mission roll (NODES §4.1) ---
        RollMissions(g, seed);

        g.BuildAdjacency();
        return g;
    }

    // ---- mission roll -----------------------------------------------------------

    /// <summary>
    /// Assign a <see cref="MissionType"/> to every combat node (NODES §4.1). LV4/LV6/Boss-kind
    /// nodes are structurally Boss (never rolled); LV1/2/3/5 combat nodes roll 60:15:10:10
    /// Collection:Protect:Destroy:Slaughter.
    ///
    /// Rolls from a DEDICATED sub-stream (<c>DetRandom(seed+"M")</c>) — it never touches the
    /// layout stream, so a given seed's map geometry is byte-for-byte UNCHANGED (same trick the
    /// atlas uses with seed+"R"). Iteration over g.Nodes is deterministic (insertion order).
    /// </summary>
    private static void RollMissions(MapGraph g, string seed)
    {
        var mrng = new DetRandom(seed + "M");
        foreach (var n in g.Nodes)
        {
            if (!n.IsCombat) continue;
            if (n.Kind == NodeKind.Boss || n.Level == 4 || n.Level == 6)
            {
                n.Mission = MissionType.Boss; // structural
                continue;
            }
            // 60:15:10:10 over Collection/Protect/Destroy/Slaughter (weights sum to 95).
            int r = mrng.Range(0, 94);
            n.Mission = r < 60 ? MissionType.Collection
                      : r < 75 ? MissionType.Protect
                      : r < 85 ? MissionType.Destroy
                               : MissionType.Slaughter;
        }
    }

    // ---- combat-node placement --------------------------------------------------

    private static List<MapNode> PlaceRow(MapGraph g, WorldDef world, int w, float worldCenterDeg,
        string tag, int level, int count, ref int runningIndex, NodeKind kind)
    {
        const float usableDeg = 56f; // nodes use 56° of the 72° fan, leaving a ~16° gap between worlds
        float r = Radius[tag];
        float startDeg = worldCenterDeg - usableDeg / 2f;
        var row = new List<MapNode>();
        for (int k = 0; k < count; k++)
        {
            float deg = count == 1
                ? worldCenterDeg
                : startDeg + (k + 0.5f) / count * usableDeg;
            float rad = Mathf.DegToRad(deg);
            var pos = g.Center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r;
            var node = new MapNode
            {
                Id = $"{world.Abbr}-{level}-{runningIndex}",
                Kind = kind,
                WorldIndex = w,
                Zone = world.Abbr,
                LevelTag = tag,
                Level = level,
                Pos = pos,
                Color = world.Color.Lightened(0.15f),
            };
            g.Nodes.Add(node);
            row.Add(node);
            runningIndex++;
        }
        return row;
    }

    // ---- intra-world edges ------------------------------------------------------

    private static void BuildIntraWorldEdges(MapGraph g, Dictionary<string, List<MapNode>> slot, DetRandom rng)
    {
        // (a) each node links to its closest higher (next-inner) node(s).
        // Chain: 1-A→1-B→2-A→2-B→3. (3→4 is fixed, handled below.)
        for (int i = 0; i < 4; i++)
        {
            string tag = OuterTags[i];
            string innerTag = OuterTags[i + 1];
            var higher = slot[innerTag];
            foreach (var n in slot[tag])
            {
                var closest = higher.OrderBy(h => n.Pos.DistanceSquaredTo(h.Pos)).ToList();
                var h0 = closest[0];
                var h1 = closest.Count > 1 ? closest[1] : null;
                if (h1 != null && rng.Chance(0.20))
                {
                    AddEdge(g, n, h0, h0.Level);
                    AddEdge(g, n, h1, h1.Level);
                }
                else if (rng.Chance(0.70) || h1 == null)
                {
                    AddEdge(g, n, h0, h0.Level);
                    if (h1 != null) g.FailedCandidates.Add((n, h1)); // nearer link kept, farther one blocked
                }
                else
                {
                    AddEdge(g, n, h1, h1.Level);
                    g.FailedCandidates.Add((n, h0)); // farther link kept, nearest one blocked
                }
            }
        }

        // (b) sibling links within a sublevel: lv1 30%, lv2 50%.
        AddSiblings(g, slot["1-A"], 0.30, rng);
        AddSiblings(g, slot["1-B"], 0.30, rng);
        AddSiblings(g, slot["2-A"], 0.50, rng);
        AddSiblings(g, slot["2-B"], 0.50, rng);

        // (c) lv3 nodes always link to lv4 and to their sibling lv3.
        var l3 = slot["3"];
        var boss = slot["4"][0];
        foreach (var n in l3) AddEdge(g, n, boss, 4);
        if (l3.Count == 2) AddEdge(g, l3[0], l3[1], 3);
    }

    private static void AddSiblings(MapGraph g, List<MapNode> row, double prob, DetRandom rng)
    {
        for (int k = 0; k + 1 < row.Count; k++)
            if (rng.Chance(prob))
                AddEdge(g, row[k], row[k + 1], row[k].Level);
            else
                g.FailedCandidates.Add((row[k], row[k + 1])); // adjacent siblings that didn't link
    }

    // ---- inter-world edges ------------------------------------------------------

    private static void BuildInterWorldEdges(MapGraph g, List<Dictionary<string, List<MapNode>>> byWorld, DetRandom rng)
    {
        // lv3 ring: world i's 3-2 links to world i+1's 3-1 (wrap around), forming a full ring.
        for (int w = 0; w < 5; w++)
        {
            int nw = (w + 1) % 5;
            var here = byWorld[w]["3"];
            var next = byWorld[nw]["3"];
            AddEdge(g, here[here.Count - 1], next[0], 3);
        }

        // 2-A: 60% chance to link to the adjacent world (last node → next world's first).
        // 1-A: 30% chance. (2-B and 1-B never link across worlds.)
        bool startHasCross = false;
        for (int w = 0; w < 5; w++)
        {
            int nw = (w + 1) % 5;
            if (rng.Chance(0.60))
            {
                var a = byWorld[w]["2-A"];
                var b = byWorld[nw]["2-A"];
                AddEdge(g, a[a.Count - 1], b[0], 2);
            }
            if (rng.Chance(0.30))
            {
                var a = byWorld[w]["1-A"];
                var b = byWorld[nw]["1-A"];
                AddEdge(g, a[a.Count - 1], b[0], 1);
                if (w == 0 || nw == 0) startHasCross = true;
            }
        }

        // The start world (index 0) must have a 1-A cross-world edge.
        if (!startHasCross)
        {
            var a = byWorld[0]["1-A"];
            var b = byWorld[1]["1-A"];
            AddEdge(g, a[a.Count - 1], b[0], 1);
        }
    }

    // ---- function nodes ---------------------------------------------------------

    private static void GenerateFunctionNodes(MapGraph g, DetRandom rng)
    {
        var letterCount = new Dictionary<int, int>();

        // (b) edge crossings: replace the two crossing edges with 4 edges into a new
        //     function node at the intersection. Rescan until no visible pair crosses.
        while (true)
        {
            var visible = g.Edges.Where(e => e.Visible).ToList();
            (MapEdge, MapEdge, Vector2)? hit = null;
            for (int i = 0; i < visible.Count && hit == null; i++)
                for (int j = i + 1; j < visible.Count; j++)
                {
                    var e1 = visible[i];
                    var e2 = visible[j];
                    if (e1.Touches(e2.A) || e1.Touches(e2.B)) continue; // shared endpoint
                    if (SegmentsIntersect(e1.A.Pos, e1.B.Pos, e2.A.Pos, e2.B.Pos, out var p))
                    {
                        hit = (e1, e2, p);
                        break;
                    }
                }
            if (hit == null) break;

            var (ce1, ce2, point) = hit.Value;
            int lvl = Math.Max(ce1.Level, ce2.Level);
            var fn = MakeFunctionNode(g, rng, lvl, point, ref letterCount, forceShelter: false);
            var (a1, b1, a2, b2) = (ce1.A, ce1.B, ce2.A, ce2.B);
            g.Edges.Remove(ce1);
            g.Edges.Remove(ce2);
            foreach (var end in new[] { a1, b1, a2, b2 })
            {
                var e = new MapEdge(end, fn, lvl) { Considered = true };
                g.Edges.Add(e);
            }
        }

        // (c/d) per-edge probability on not-yet-considered visible edges.
        //   lv4 100% (always shelter), lv3 50%, lv2 25%, lv1 10%.
        var candidates = g.Edges.Where(e => e.Visible && !e.Considered).ToList();
        foreach (var e in candidates)
        {
            if (e.Considered) continue; // may have been consumed already
            double p = e.Level switch { 4 => 1.0, 3 => 0.50, 2 => 0.25, 1 => 0.10, _ => 0.0 };
            if (!rng.Chance(p)) { e.Considered = true; continue; }

            bool forceShelter = e.Level == 4;
            var mid = (e.A.Pos + e.B.Pos) * 0.5f;
            var fn = MakeFunctionNode(g, rng, e.Level, mid, ref letterCount, forceShelter);
            var a = e.A;
            var b = e.B;
            g.Edges.Remove(e);
            g.Edges.Add(new MapEdge(a, fn, e.Level) { Considered = true });
            g.Edges.Add(new MapEdge(fn, b, e.Level) { Considered = true });
        }

        // (e) Guarantee variety: lv1 and lv2 fire rarely (10% / 25%), so a level can end up with
        //     no shelter or no question mark. For each of levels 1 & 2, if a kind is missing, add
        //     it on a random un-split level-L city→city edge.
        foreach (int lvl in new[] { 1, 2 })
        {
            bool hasShelter = false, hasQuestion = false;
            foreach (var n in g.Nodes)
                if (n.WorldIndex == -2 && n.Level == lvl)
                {
                    if (n.Kind == NodeKind.Shelter) hasShelter = true;
                    else if (n.Kind == NodeKind.QuestionMark) hasQuestion = true;
                }

            foreach (var kind in new[] { NodeKind.Shelter, NodeKind.QuestionMark })
            {
                if (kind == NodeKind.Shelter ? hasShelter : hasQuestion) continue;
                // un-split = neither endpoint is already a function node (WorldIndex -2)
                var open = g.Edges.Where(e => e.Visible && e.Level == lvl
                                              && e.A.WorldIndex != -2 && e.B.WorldIndex != -2).ToList();
                if (open.Count == 0) continue;
                var e = rng.Pick(open);
                var fn = MakeFunctionNode(g, rng, lvl, (e.A.Pos + e.B.Pos) * 0.5f, ref letterCount, false, kind);
                var (a, b) = (e.A, e.B);
                g.Edges.Remove(e);
                g.Edges.Add(new MapEdge(a, fn, lvl) { Considered = true });
                g.Edges.Add(new MapEdge(fn, b, lvl) { Considered = true });
            }
        }
    }

    private static MapNode MakeFunctionNode(MapGraph g, DetRandom rng, int level, Vector2 pos,
        ref Dictionary<int, int> letterCount, bool forceShelter, NodeKind? forceKind = null)
    {
        int idx = letterCount.TryGetValue(level, out var c) ? c : 0;
        letterCount[level] = idx + 1;
        // forceKind pins the kind (used to guarantee variety); else forceShelter, else 40/60 roll.
        bool shelter = forceKind.HasValue ? forceKind.Value == NodeKind.Shelter
                                          : forceShelter || rng.Chance(0.40);
        var node = new MapNode
        {
            Id = $"{level}-{(char)('a' + idx)}",
            Kind = shelter ? NodeKind.Shelter : NodeKind.QuestionMark,
            WorldIndex = -2, // function node: belongs to an edge, not a world
            Zone = "",
            LevelTag = level.ToString(),
            Level = level,
            Pos = pos,
            Color = shelter ? new Color(0.85f, 0.70f, 0.40f) : new Color(0.55f, 0.80f, 0.95f),
        };
        g.Nodes.Add(node);
        return node;
    }

    // ---- zone 6 (the VOID) ------------------------------------------------------

    private static void BuildZone6(MapGraph g, List<Dictionary<string, List<MapNode>>> byWorld)
    {
        // 5 lv5 nodes on the lake rim, each aligned with (and linked to) one world's boss.
        float r5 = Radius["5"];
        var lv5 = new List<MapNode>();
        for (int w = 0; w < 5; w++)
        {
            float deg = -90f + w * 72f;
            float rad = Mathf.DegToRad(deg);
            var pos = g.Center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r5;
            var node = new MapNode
            {
                Id = $"XX-5-{w + 1}",
                Kind = NodeKind.Combat,
                WorldIndex = -1,
                Zone = "XX",
                LevelTag = "5",
                Level = 5,
                Pos = pos,
                Color = new Color(0.30f, 0.25f, 0.45f),
            };
            g.Nodes.Add(node);
            lv5.Add(node);

            // boss(4-1) → shelter → lv5, all invisible (zone 6 edges aren't drawn as lines).
            var boss = byWorld[w]["4"][0];
            var shelterPos = (boss.Pos + pos) * 0.5f;
            var shelter = new MapNode
            {
                Id = $"XX-S-{w + 1}",
                Kind = NodeKind.Shelter,
                WorldIndex = -1,
                Zone = "XX",
                LevelTag = "5",
                Level = 5,
                Pos = shelterPos,
                Color = new Color(0.85f, 0.70f, 0.40f),
            };
            g.Nodes.Add(shelter);
            AddEdge(g, boss, shelter, 5, visible: false);
            AddEdge(g, shelter, node, 5, visible: false);
        }

        // River of the VOID: a single hub node on the ring; every lv5 node links to it
        // (so the player can hop between lv5 nodes in one step). Drawn as a ring, not a line.
        var river = new MapNode
        {
            Id = "XX-R",
            Kind = NodeKind.River,
            WorldIndex = -1,
            Zone = "XX",
            LevelTag = "R",
            Level = 5,
            Pos = g.Center + new Vector2(0, -RiverRadius),
            Color = new Color(0.20f, 0.30f, 0.55f),
        };
        g.Nodes.Add(river);
        foreach (var n in lv5) AddEdge(g, n, river, 5, visible: false);

        // Level 6: the core, one edge to the river.
        var core = new MapNode
        {
            Id = "XX-6-1",
            Kind = NodeKind.Boss,
            WorldIndex = -1,
            Zone = "XX",
            LevelTag = "6",
            Level = 6,
            Pos = g.Center,
            Color = new Color(0.10f, 0.05f, 0.15f),
        };
        g.Nodes.Add(core);
        AddEdge(g, river, core, 6, visible: false);
    }

    // ---- helpers ----------------------------------------------------------------

    private static void AddEdge(MapGraph g, MapNode a, MapNode b, int level, bool visible = true)
    {
        g.Edges.Add(new MapEdge(a, b, level, visible));
    }

    /// <summary>Proper segment intersection with an interior crossing point (excludes shared endpoints).</summary>
    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 point)
    {
        point = Vector2.Zero;
        Vector2 r = p2 - p1;
        Vector2 s = p4 - p3;
        float denom = r.X * s.Y - r.Y * s.X;
        if (Mathf.Abs(denom) < 1e-6f) return false; // parallel/collinear
        Vector2 qp = p3 - p1;
        float t = (qp.X * s.Y - qp.Y * s.X) / denom;
        float u = (qp.X * r.Y - qp.Y * r.X) / denom;
        const float eps = 1e-3f;
        if (t <= eps || t >= 1f - eps || u <= eps || u >= 1f - eps) return false;
        point = p1 + t * r;
        return true;
    }
}

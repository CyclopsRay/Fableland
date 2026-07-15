namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.Linq;
using Fableland.Map;

/// <summary>
/// GDD §6 — seeded detail generation. Rule-category tiles are invisible editor
/// markers; contiguous same-rule-id cells on a layer flood-fill into "zones",
/// and each zone seeds its own child `DetRandom` stream (via `rng.Sub(...)`) so
/// adding/painting a new zone never reshuffles any other zone's rolls
/// (KNOWLEDGE: determinism rule; T00 rule 5).
///
/// Pure and deterministic: all randomness flows through the caller-supplied
/// <see cref="DetRandom"/> — this class never creates its own unseeded RNG.
///
/// Draw-order contract (kept stable so a seed always reproduces the same
/// result): per zone, (1) roll `count` once, (2) shuffle the zone's candidate
/// cells once, (3) for each attempted candidate, exactly one weighted
/// "which def" roll.
/// </summary>
public static class RuleResolver
{
    public static List<ResolvedSpawn> Resolve(MapDocument doc, DetRandom rng, out List<string> warnings)
    {
        warnings = new List<string>();
        var results = new List<ResolvedSpawn>();
        if (doc?.Layers == null || rng == null) return results;

        var allZones = new List<Zone>();
        for (int layerIndex = 0; layerIndex < doc.Layers.Count; layerIndex++)
        {
            foreach (var zone in FindZones(doc.Layers[layerIndex]))
            {
                zone.LayerIndex = layerIndex;
                allZones.Add(zone);
            }
        }

        // Stable resolution order (GDD §6): (layerIndex, anchorY, anchorX).
        allZones.Sort((a, b) =>
        {
            int c = a.LayerIndex.CompareTo(b.LayerIndex);
            if (c != 0) return c;
            c = a.AnchorY.CompareTo(b.AnchorY);
            if (c != 0) return c;
            return a.AnchorX.CompareTo(b.AnchorX);
        });

        // Per-layer running occupied set: seeded from the layer's REAL (non-Rule) tiles,
        // then grown by each accepted spawn's footprint+reserve cells as zones resolve in
        // the stable order above. Later zones see earlier spawns; nothing ever reshuffles
        // an earlier zone's own rolls (its zoneRng stream is independent of this set).
        var perLayerOccupied = new Dictionary<int, HashSet<(int x, int y)>>();

        foreach (var zone in allZones)
        {
            if (!TileRegistry.TryGet(zone.RuleDefId, out var ruleDef) || ruleDef.RuleProps == null)
                continue; // unresolvable rule def id: nothing to generate, skip silently

            if (!perLayerOccupied.TryGetValue(zone.LayerIndex, out var occupied))
            {
                occupied = BuildRealOccupancy(doc.Layers[zone.LayerIndex]);
                perLayerOccupied[zone.LayerIndex] = occupied;
            }

            var rp = ruleDef.RuleProps;
            var zoneRng = rng.Sub($"zone:{zone.LayerIndex}:{zone.AnchorX}:{zone.AnchorY}");

            int countMax = Math.Max(rp.CountMin, rp.CountMax);
            int count = zoneRng.Range(rp.CountMin, countMax); // (1) count first

            var candidates = zone.Cells.ToList();
            candidates.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            zoneRng.Shuffle(candidates); // (2) shuffle once

            int accepted = 0;
            int attempts = 0;
            int maxAttempts = Math.Max(0, 4 * count);
            int ci = 0;

            while (accepted < count && attempts < maxAttempts)
            {
                var (cx, cy) = candidates[ci % candidates.Count];
                ci++;
                attempts++;

                // A zone may be as small as one cell. Pick only definitions whose full
                // footprint can actually fit at this candidate, otherwise a heavily weighted
                // 2x1 cloud can starve the valid 1x1 cloud forever.
                List<(string DefId, int Weight)> fittingTable = FittingSpawnTable(rp.SpawnTable, zone.Cells, cx, cy);
                string chosenDefId = WeightedPick(fittingTable, zoneRng); // (3) per-attempt def roll
                if (chosenDefId == null) break; // empty/degenerate spawn table: never place anything

                if (!TileRegistry.TryGet(chosenDefId, out var chosenDef)) continue;

                int fw = chosenDef.FootprintW;
                int fh = chosenDef.FootprintH;

                // Reserve rect: ReserveW x ReserveH centered on the footprint, rounding the
                // extra margin toward top-left (an odd leftover cell goes to the top/left
                // side). Reserve may extend beyond the zone — it's spacing only.
                int totalW = Math.Max(rp.ReserveW, fw);
                int totalH = Math.Max(rp.ReserveH, fh);
                int extraW = totalW - fw;
                int extraH = totalH - fh;
                int marginLeft = (extraW + 1) / 2;
                int marginTop = (extraH + 1) / 2;
                int rx0 = cx - marginLeft;
                int ry0 = cy - marginTop;

                bool overlapsOccupied = false;
                for (int dy = 0; dy < totalH && !overlapsOccupied; dy++)
                    for (int dx = 0; dx < totalW && !overlapsOccupied; dx++)
                        if (occupied.Contains((rx0 + dx, ry0 + dy)))
                            overlapsOccupied = true;
                if (overlapsOccupied) continue;

                results.Add(new ResolvedSpawn
                {
                    LayerIndex = zone.LayerIndex,
                    DefId = chosenDefId,
                    X = cx,
                    Y = cy,
                    Tags = rp.Tags ?? Array.Empty<string>(),
                });
                accepted++;

                for (int dy = 0; dy < totalH; dy++)
                    for (int dx = 0; dx < totalW; dx++)
                        occupied.Add((rx0 + dx, ry0 + dy));
            }

            if (accepted < count)
                warnings.Add($"zone at L{zone.LayerIndex} ({zone.AnchorX},{zone.AnchorY}) placed {accepted}/{count}");
        }

        return results;
    }

    /// <summary>Pure regression guard for a one-cell Cloud Zone. The default cloud table
    /// weights its 2x1 entry highest, so this verifies that impossible entries are filtered
    /// before the weighted roll rather than starving the only fitting 1x1 cloud.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        var doc = new MapDocument
        {
            Id = "rule-resolver-self-test",
            Layers = new List<MapLayerData>
            {
                new()
                {
                    Role = MapLayerData.RoleFarview,
                    GridW = 1,
                    GridH = 1,
                    Tiles = new List<PlacedTile>
                    {
                        new() { DefId = "rule.cloudZone", X = 0, Y = 0 },
                    },
                },
            },
        };

        // Several independent parent seeds exercise several child zone streams. Every
        // resolution must keep the one fitting cloud even though the rule asks for 2..4.
        for (int i = 0; i < 16; i++)
        {
            List<ResolvedSpawn> results = Resolve(doc, new DetRandom("rule-fit-" + i), out _);
            bool correct = results.Count == 1 && results[0].DefId == "softvolume.cloud1x1" &&
                results[0].X == 0 && results[0].Y == 0;
            if (!correct) failures.Add($"one-cell Cloud Zone seed {i}: expected one cloud1x1, got {results.Count}");
        }

        return failures;
    }

    private static List<(string DefId, int Weight)> FittingSpawnTable(
        List<(string DefId, int Weight)> table, HashSet<(int x, int y)> zoneCells, int anchorX, int anchorY)
    {
        var fitting = new List<(string DefId, int Weight)>();
        if (table == null || zoneCells == null) return fitting;

        foreach (var entry in table)
        {
            if (entry.Weight <= 0 || !TileRegistry.TryGet(entry.DefId, out var def)) continue;
            if (FootprintFitsZone(zoneCells, anchorX, anchorY, def.FootprintW, def.FootprintH))
                fitting.Add(entry);
        }
        return fitting;
    }

    private static bool FootprintFitsZone(HashSet<(int x, int y)> zoneCells,
        int anchorX, int anchorY, int footprintW, int footprintH)
    {
        for (int dy = 0; dy < footprintH; dy++)
            for (int dx = 0; dx < footprintW; dx++)
                if (!zoneCells.Contains((anchorX + dx, anchorY + dy))) return false;
        return true;
    }

    private static string WeightedPick(List<(string DefId, int Weight)> table, DetRandom rng)
    {
        if (table == null || table.Count == 0) return null;

        int total = 0;
        foreach (var (_, w) in table) total += Math.Max(0, w);
        if (total <= 0) return null;

        int r = rng.Range(1, total);
        int cumulative = 0;
        foreach (var (defId, w) in table)
        {
            cumulative += Math.Max(0, w);
            if (r <= cumulative) return defId;
        }
        return table[^1].DefId; // unreachable in practice; defensive fallback
    }

    /// <summary>Real (non-Rule) tiles' footprint cells for a layer. A Rule tile is an
    /// invisible editor marker, not real occupying content — it is exactly the area zones
    /// are allowed to fill, so it's excluded here.</summary>
    private static HashSet<(int x, int y)> BuildRealOccupancy(MapLayerData layer)
    {
        var set = new HashSet<(int x, int y)>();
        if (layer?.Tiles == null) return set;

        foreach (var tile in layer.Tiles)
        {
            if (!TileRegistry.TryGet(tile.DefId, out var def) || def.Category == TileCategory.Rule)
                continue;

            for (int dy = 0; dy < def.FootprintH; dy++)
                for (int dx = 0; dx < def.FootprintW; dx++)
                    set.Add((tile.X + dx, tile.Y + dy));
        }

        return set;
    }

    /// <summary>Flood-fills contiguous (4-connectivity) same-rule-id cells on a layer into
    /// zones. Iteration/seed order is derived purely from static layer data (never RNG), so
    /// zone membership itself is order-independent and deterministic.</summary>
    private static List<Zone> FindZones(MapLayerData layer)
    {
        var zones = new List<Zone>();
        if (layer?.Tiles == null) return zones;

        var ruleCell = new Dictionary<(int x, int y), string>();
        foreach (var tile in layer.Tiles)
        {
            if (!TileRegistry.TryGet(tile.DefId, out var def) || def.Category != TileCategory.Rule)
                continue;

            for (int dy = 0; dy < def.FootprintH; dy++)
                for (int dx = 0; dx < def.FootprintW; dx++)
                    ruleCell[(tile.X + dx, tile.Y + dy)] = tile.DefId;
        }

        var visited = new HashSet<(int x, int y)>();
        var seeds = ruleCell.Keys.ToList();
        seeds.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

        foreach (var seed in seeds)
        {
            if (visited.Contains(seed)) continue;

            string ruleId = ruleCell[seed];
            var cells = new HashSet<(int x, int y)>();
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue(seed);
            visited.Add(seed);

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                cells.Add((x, y));

                var neighbors = new (int x, int y)[]
                {
                    (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1),
                };
                foreach (var n in neighbors)
                {
                    if (visited.Contains(n)) continue;
                    if (!ruleCell.TryGetValue(n, out var nRuleId) || nRuleId != ruleId) continue;
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }

            int ax = int.MaxValue, ay = int.MaxValue;
            foreach (var (x, y) in cells)
            {
                if (y < ay || (y == ay && x < ax)) { ay = y; ax = x; }
            }

            zones.Add(new Zone { RuleDefId = ruleId, Cells = cells, AnchorX = ax, AnchorY = ay });
        }

        return zones;
    }

    private sealed class Zone
    {
        public int LayerIndex;
        public string RuleDefId;
        public HashSet<(int x, int y)> Cells;
        public int AnchorX;
        public int AnchorY;
    }
}

/// <summary>One spawn resolved by <see cref="RuleResolver"/>, for a specific layer/cell.</summary>
public sealed class ResolvedSpawn
{
    public int LayerIndex { get; set; }
    public string DefId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string[] Tags { get; set; }
}

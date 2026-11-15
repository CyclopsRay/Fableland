namespace Fableland.MapCreation.Runtime;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>One runtime hazard to build: a tile kind, where it sits in the world, and its
/// collision shapes relative to <see cref="Origin"/>. Usually one placed tile; for a kind
/// flagged <see cref="TileProps.MergeAdjacent"/>, a whole connected pool of them.</summary>
public sealed class MapHazardPlacement
{
    public TileDef Def;
    public Vector2 Origin;
    public List<MapCollisionSpec> Specs;
}

/// <summary>
/// Hazard tiles → runtime hazards, by data. A hazard tile names its scene in
/// <c>Props["scene"]</c> (`TileProps.Scene`), so a new hazard is a registry entry plus a
/// small scene file that sets an existing hazard script's numbers — the two map builders no
/// longer hold a `switch (def.Id)` of the hazards they happen to know (T20 §1).
/// </summary>
public static class MapHazardFactory
{
    /// <summary>
    /// Plans the hazards for one layer's tiles, in a stable order (row, then column). Tiles
    /// of a <see cref="TileProps.MergeAdjacent"/> kind that touch edge-to-edge become one
    /// placement, so a body wading through a lava pool is ticked by one hazard, not by every
    /// tile it happens to straddle.
    /// </summary>
    public static List<MapHazardPlacement> Plan(IReadOnlyList<PlacedTile> tiles)
    {
        var placements = new List<MapHazardPlacement>();
        if (tiles == null) return placements;

        var hazards = new List<(PlacedTile Tile, TileDef Def)>();
        foreach (PlacedTile tile in tiles)
        {
            if (tile != null && TileRegistry.TryGet(tile.DefId, out TileDef def) && def.Category == TileCategory.Hazard)
                hazards.Add((tile, def));
        }
        hazards.Sort((a, b) => a.Tile.Y != b.Tile.Y ? a.Tile.Y.CompareTo(b.Tile.Y) : a.Tile.X.CompareTo(b.Tile.X));

        // Footprint-cell ownership for the kinds that merge.
        var owner = new Dictionary<(string Def, int X, int Y), int>();
        for (int i = 0; i < hazards.Count; i++)
        {
            if (!Merges(hazards[i].Def)) continue;
            for (int dy = 0; dy < hazards[i].Def.FootprintH; dy++)
            for (int dx = 0; dx < hazards[i].Def.FootprintW; dx++)
                owner[(hazards[i].Def.Id, hazards[i].Tile.X + dx, hazards[i].Tile.Y + dy)] = i;
        }

        var visited = new bool[hazards.Count];
        var queue = new Queue<int>();
        for (int i = 0; i < hazards.Count; i++)
        {
            if (visited[i]) continue;
            visited[i] = true;
            (PlacedTile tile, TileDef def) = hazards[i];

            if (!Merges(def))
            {
                placements.Add(new MapHazardPlacement
                {
                    Def = def,
                    Origin = OriginOf(tile, def),
                    Specs = MapCollision.BuildSpecs(def, tile.FlipX),
                });
                continue;
            }

            // Flood-fill the pool this tile belongs to.
            var pool = new List<int> { i };
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                (PlacedTile current, TileDef currentDef) = hazards[queue.Dequeue()];
                for (int dy = 0; dy < currentDef.FootprintH; dy++)
                for (int dx = 0; dx < currentDef.FootprintW; dx++)
                {
                    int cx = current.X + dx, cy = current.Y + dy;
                    foreach ((int nx, int ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
                    {
                        if (!owner.TryGetValue((def.Id, nx, ny), out int neighbour) || visited[neighbour]) continue;
                        visited[neighbour] = true;
                        pool.Add(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
            }

            // The pool's origin is its top-left member corner. Corners come from
            // TilePlacement like every other tile's; today's merging kinds are autotiled
            // fills, which are grid-locked, so these are whole cells — but the pool would
            // stay correct if a nudgeable kind ever opted into merging.
            Vector2 poolOrigin = new(float.MaxValue, float.MaxValue);
            foreach (int member in pool)
            {
                Vector2 corner = OriginOf(hazards[member].Tile, def);
                poolOrigin = new Vector2(Mathf.Min(poolOrigin.X, corner.X), Mathf.Min(poolOrigin.Y, corner.Y));
            }
            var specs = new List<MapCollisionSpec>();
            foreach (int member in pool)
            {
                PlacedTile memberTile = hazards[member].Tile;
                Vector2 delta = OriginOf(memberTile, def) - poolOrigin;
                foreach (MapCollisionSpec spec in MapCollision.BuildSpecs(def, memberTile.FlipX))
                    specs.Add(spec.Offset(delta));
            }
            placements.Add(new MapHazardPlacement { Def = def, Origin = poolOrigin, Specs = specs });
        }

        return placements;
    }

    /// <summary>A hazard tile's footprint corner in layer pixels: anchor cell plus sub-cell
    /// nudge (GDD §2.3), so a nudged bonfire burns where it is drawn.</summary>
    private static Vector2 OriginOf(PlacedTile tile, TileDef def)
    {
        (float x, float y) = TilePlacement.Origin(tile, def);
        return new Vector2(x, y);
    }

    /// <summary>True when the kind's scene is an event trigger (it spawns another hazard
    /// through the arena environment controller) rather than a stationary <c>Hazard</c>.</summary>
    public static bool IsEventTrigger(TileDef def) =>
        def?.Props != null && def.Props.ContainsKey(TileProps.SpawnHazard);

    public static string SceneOf(TileDef def) =>
        def?.Props != null && def.Props.TryGetValue(TileProps.Scene, out string path) ? path : null;

    /// <summary>
    /// Instantiates and configures the stationary hazard for a placement. Returns null (with
    /// a logged reason) when the kind declares no scene or the scene is not a <c>Hazard</c> —
    /// a content error must never crash a permadeath run (T00 rule 4).
    /// </summary>
    public static Hazard Create(MapHazardPlacement placement, string logTag)
    {
        if (placement?.Def == null || placement.Specs == null || placement.Specs.Count == 0) return null;

        string scenePath = SceneOf(placement.Def);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            GD.PushWarning($"[{logTag}] hazard tile '{placement.Def.Id}' declares no runtime scene.");
            return null;
        }

        PackedScene scene = ResourceLoader.Load<PackedScene>(scenePath);
        if (scene == null)
        {
            GD.PushError($"[{logTag}] hazard scene is unavailable: {scenePath}");
            return null;
        }

        Node instance = scene.Instantiate();
        if (instance is not Hazard hazard)
        {
            GD.PushWarning($"[{logTag}] '{placement.Def.Id}' uses scene '{scenePath}', which is not a stationary Hazard; skipped.");
            instance.Free();
            return null;
        }

        Rect2 bounds = MapCollision.BoundsOf(placement.Specs);
        var shapes = new List<Shape2D>(placement.Specs.Count);
        var positions = new List<Vector2>(placement.Specs.Count);
        foreach (MapCollisionSpec spec in placement.Specs)
        {
            shapes.Add(spec.Shape);
            // The hazard's origin is the centre of its effect bounds; each shape keeps its
            // exact authored place once converted into that local frame.
            positions.Add(spec.Position - bounds.GetCenter());
        }
        hazard.ConfigureRuntimeCollision(shapes, positions, new Rect2(-bounds.Size * 0.5f, bounds.Size));
        hazard.Position = placement.Origin + bounds.GetCenter();
        return hazard;
    }

    private static bool Merges(TileDef def) =>
        def.Props != null && def.Props.TryGetValue(TileProps.MergeAdjacent, out string flag) && flag == "true";
}

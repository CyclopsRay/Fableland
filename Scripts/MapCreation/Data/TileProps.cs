namespace Fableland.MapCreation.Data;

/// <summary>
/// Keys of <see cref="TileDef.Props"/> that code reads. Tile behaviour that varies by kind
/// is declared here as data on the def and looked up by key, so adding a hazard, a ground
/// material or a trigger never means editing a `switch (def.Id)` in a builder (T00 rule 1,
/// T20 §1 "no content switch statements").
/// </summary>
public static class TileProps
{
    /// <summary>`res://` path of the atlas a connected-look tile draws its cells from.</summary>
    public const string ArtSource = "artSource";

    /// <summary>Which classifier lays that atlas out — see <see cref="TileVisualResolver"/>.</summary>
    public const string AutotileKind = "autotileKind";

    /// <summary>Variant-group count of a <see cref="HillAutotile.KindStrip"/> atlas.</summary>
    public const string AutotileVariants = "autotileVariants";

    /// <summary>Hazard tiles: `res://` path of the scene to instantiate at runtime. A scene
    /// rooted in a <c>Hazard</c> gets the tile's effect area as its collision; anything else
    /// (e.g. the tsunami trigger) is an event trigger the orchestrator wires up itself.</summary>
    public const string Scene = "scene";

    /// <summary>Hazard tiles: "true" merges touching tiles of this kind into ONE runtime
    /// hazard. Required for hazards painted as a contiguous area (a lava pool): separate
    /// per-tile hazards would each tick a body standing across a tile seam, doubling damage
    /// purely because of where the grid lines fall.</summary>
    public const string MergeAdjacent = "mergeAdjacent";

    /// <summary>Trigger tiles: id of the moving hazard the trigger's event spawns.</summary>
    public const string SpawnHazard = "spawnHazard";

    /// <summary>The art brief / generation prompt the sprite was made from (provenance).</summary>
    public const string Prompt = "prompt";
}

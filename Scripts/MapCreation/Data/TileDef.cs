namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// GDD §2.2 — a tile-kind definition. Defs are code-defined (in `TileRegistry`)
/// and looked up by id; they are NOT serialized into map files, so plain
/// get/init properties are fine here (no [JsonExtensionData] needed — contrast
/// with the serialized model in MapDocument/MapLayerData/PlacedTile).
/// </summary>
public enum TileCategory
{
    Ground,
    Platform,
    SoftVolume,
    Hazard,
    EnemySpawn,
    Respawn,
    LevelGoal,
    Character,
    Decoration,
    Rule,
    Misc,
}

/// <summary>Which layer roles a TileDef may be painted on (GDD §2.2). Gameplay
/// categories are Battlefield-only; Decoration is Any; Rule is Battlefield|Farview.</summary>
[Flags]
public enum LayerRoleMask
{
    None = 0,
    Farview = 1,
    Battlefield = 2,
    Closeview = 4,
    Any = Farview | Battlefield | Closeview,
}

/// <summary>
/// GDD §2.5 extension — reserved neighbor-compatibility tags for future stamp/edge
/// matching (see `TileManifest`/`TileManifestLoader`). Each side is a free-form tag;
/// two tiles may be placed adjacent only once an authoring tool declares their facing
/// tags compatible. Null (default, or a null field within) means "no declared
/// constraint" — not yet consulted by any runtime/editor code, same status
/// `AutotileGroup` had before `AutotileAtlas` used it.
/// </summary>
public sealed class EdgeRule
{
    public string Top { get; init; }
    public string Right { get; init; }
    public string Bottom { get; init; }
    public string Left { get; init; }
}

/// <summary>
/// GDD §6 — the seeded-generation table carried by a Rule-category TileDef.
/// Not serialized (code-defined, like TileDef itself).
/// </summary>
public sealed class RuleProps
{
    /// <summary>Weighted list of TileDef ids this rule may place.</summary>
    public List<(string DefId, int Weight)> SpawnTable { get; init; } = new();

    public int CountMin { get; init; }
    public int CountMax { get; init; }

    /// <summary>Cells reserved around each placement (min spacing) — may extend beyond the
    /// zone; it's spacing only (GDD §6).</summary>
    public int ReserveW { get; init; }
    public int ReserveH { get; init; }

    /// <summary>Extra behaviors attached to spawned tiles, e.g. "foeSpawner:crab".</summary>
    public string[] Tags { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Optional per-kind tuning for an enterable <see cref="TileCategory.SoftVolume"/>.
/// The map builder applies this data to the reusable runtime scene before it enters
/// the tree; absent values deliberately preserve that scene's exported defaults.
/// </summary>
public sealed class SoftVolumeTuning
{
    /// <summary>
    /// Optional 0..1 viscosity index. It caps self-directed speed to this fraction
    /// of max speed and determines the resistance rate; see SoftVolume and GDD §2.2.
    /// </summary>
    public float? StagnationIndex { get; init; }
}

/// <summary>GDD §2.2 — a registered tile kind.</summary>
public sealed class TileDef
{
    public string Id { get; init; }
    public string DisplayName { get; init; }
    public TileCategory Category { get; init; }

    /// <summary>Cells occupied, anchored at the placed tile's (X,Y) top-left. Default 1x1.</summary>
    public int FootprintW { get; init; } = 1;
    public int FootprintH { get; init; } = 1;

    /// <summary>Which layer roles may hold this tile (GDD §2.2). Default is Any: the user
    /// can place grounds/platforms/soft-volumes/spawns on background/foreground layers too,
    /// not only the battlefield. Placement ≠ collision — Battlefield uses its normal layer
    /// setting, while a non-looping Farview can opt in only its SoftVolumes as fixed-world
    /// colliders (GDD §3).</summary>
    public LayerRoleMask AllowedRoles { get; init; } = LayerRoleMask.Any;

    /// <summary>Placeholder quad color until art lands (hex string).</summary>
    public string EditorColor { get; init; } = "#FFFFFF";

    /// <summary>Reserved: atlas region / texture path the art department fills in later (§2.5).</summary>
    public string SpriteSlot { get; init; }

    /// <summary>True for seamless terrain textures that fill the entire footprint.
    /// False (default) trims transparent padding, aspect-fits, and bottom-anchors props.</summary>
    public bool SpriteFillFootprint { get; init; }

    /// <summary>Reserved: connected-look group for future TileSet-terrain autotiling (§2.5).</summary>
    public string AutotileGroup { get; init; }

    /// <summary>Reserved: per-side neighbor-compatibility tags (§2.5 extension). Null = no
    /// declared constraint. See <see cref="EdgeRule"/>.</summary>
    public EdgeRule Edges { get; init; }

    /// <summary>Collision/trigger shape; null = footprint rectangle (§2.4).</summary>
    public ShapeDef EffectArea { get; init; }

    /// <summary>Optional per-kind gameplay tuning, valid only for <see cref="TileCategory.SoftVolume"/>.
    /// Null keeps the reusable SoftVolume scene's defaults.</summary>
    public SoftVolumeTuning SoftVolumeTuning { get; init; }

    /// <summary>Non-null only when Category == Rule (§6).</summary>
    public RuleProps RuleProps { get; init; }

    /// <summary>Misc per-kind extras (e.g. the foe-table id on EnemySpawn).</summary>
    public Dictionary<string, string> Props { get; init; }
}

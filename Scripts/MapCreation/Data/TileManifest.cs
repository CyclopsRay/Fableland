namespace Fableland.MapCreation.Data;

/// <summary>
/// GDD §2.5 extension — the on-disk schema an AI/artist-authored `&lt;name&gt;.tile.json`
/// manifest follows, sidecar to a same-named `&lt;name&gt;.png` under
/// `Assets/Sprites/Tiles/&lt;world&gt;/&lt;tile-class&gt;/`. `TileManifestLoader` maps this into a
/// `TileDef`. Deliberately a separate DTO from `TileDef` itself (not a direct dump of it):
/// the manifest is meant to be simple enough for whoever drives the art-generation prompt
/// to fill in by hand (plain strings/nested objects, no C# enum/init-record knowledge
/// required).
///
/// STRUCTURAL RULE (same as PlacedTile/MapDocument, KNOWLEDGE.md v0.6.7): this class
/// round-trips through System.Text.Json, so every member is a `{ get; set; }` property,
/// never a bare field — fields are silently skipped by the default reflection contract.
/// </summary>
public sealed class TileManifest
{
    /// <summary>Registry id, e.g. "ground.sand". Required.</summary>
    public string Id { get; set; }

    /// <summary>Palette label. Defaults to <see cref="Id"/> when omitted.</summary>
    public string DisplayName { get; set; }

    /// <summary>A <see cref="TileCategory"/> name (e.g. "Ground", "Platform"). Required.</summary>
    public string Role { get; set; }

    public TileManifestFootprint Footprint { get; set; } = new();

    /// <summary>True for seamless textures that fill the whole footprint (terrain fill);
    /// false (default) for props that get alpha-bound, aspect-fit, bottom-anchored.</summary>
    public bool FillFootprint { get; set; }

    /// <summary>Optional explicit `res://...` sprite path. When omitted, the loader assumes
    /// the convention: same directory as the manifest, same basename, `.png`.</summary>
    public string Sprite { get; set; }

    /// <summary>Optional connected-look group (§2.5) for ground tiles, e.g. "terrain.beach_sand".</summary>
    public string AutotileGroup { get; set; }

    /// <summary>Optional editor classifier family, e.g. "layered_hill". Omitted groups use
    /// the legacy two-state north-edge lookup.</summary>
    public string AutotileKind { get; set; }

    /// <summary>Optional per-side neighbor-compatibility tags (§2.5 extension).</summary>
    public TileManifestEdges Edges { get; set; }

    /// <summary>Optional provenance: the un-sliced/source art this tile was cut from, if any.</summary>
    public string ArtSource { get; set; }

    /// <summary>The exact generation prompt used, kept for reproducibility/regeneration.</summary>
    public string Prompt { get; set; }
}

public sealed class TileManifestFootprint
{
    public int W { get; set; } = 1;
    public int H { get; set; } = 1;
}

public sealed class TileManifestEdges
{
    public string Top { get; set; }
    public string Right { get; set; }
    public string Bottom { get; set; }
    public string Left { get; set; }
}

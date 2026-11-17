namespace Fableland.MapCreation.Data;

using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Small factory helpers shared by the per-realm tile-set files. They encode the two
/// conventions every code-painted realm set follows (`Tools/realm_art/`,
/// `Docs/Art/RealmStyleBible.md`), so a realm file is a readable list of what exists
/// rather than forty copies of the same path arithmetic:
///
///   • art lives at `Assets/Sprites/Tiles/&lt;RealmDir&gt;/&lt;Category&gt;/&lt;stem&gt;.png`;
///   • a fill material is `&lt;stem&gt;.png` (palette swatch / fallback fill) plus
///     `&lt;stem&gt;_atlas.png`, a <see cref="HillAutotile.KindStrip"/> atlas.
///
/// `TileSetAudit` checks every def built here against its `.tile.json` sidecar at boot.
/// </summary>
internal static class RealmTiles
{
    /// <summary>Variant groups in every painted strip atlas (`build_hill_atlas(variants=4)`).</summary>
    public const int StripVariants = 4;

    public static string Sprite(string realmDir, string categoryDir, string stem) =>
        "res://Assets/Sprites/Tiles/" + realmDir + "/" + categoryDir + "/" + stem + ".png";

    /// <summary>A connected-look fill material (ground, or a fill hazard such as lava).</summary>
    public static TileDef Fill(string set, string realmDir, TileCategory category, string categoryDir,
        string id, string name, string color, string stem, string group, Dictionary<string, string> extraProps = null)
    {
        var props = new Dictionary<string, string>
        {
            [TileProps.ArtSource] = Sprite(realmDir, categoryDir, stem + "_atlas"),
            [TileProps.AutotileKind] = HillAutotile.KindStrip,
            [TileProps.AutotileVariants] = StripVariants.ToString(CultureInfo.InvariantCulture),
        };
        if (extraProps != null)
            foreach (var pair in extraProps) props[pair.Key] = pair.Value;

        return new TileDef
        {
            Id = id, DisplayName = name, Category = category, TileSet = set, EditorColor = color,
            AutotileGroup = group, SpriteSlot = Sprite(realmDir, categoryDir, stem), SpriteFillFootprint = true,
            Props = props,
        };
    }

    public static TileDef Ground(string set, string realmDir, string id, string name, string color,
        string stem, string group) =>
        Fill(set, realmDir, TileCategory.Ground, "Ground", id, name, color, stem, group);

    /// <summary>
    /// A ramp of a fill material: a block of solid ground <paramref name="run"/> cells wide
    /// whose top rises to the right by one cell across its width, over
    /// <paramref name="solidRows"/> rows of solid material (MapCreation.gdd §2.2b). Blocks
    /// placed one step up and <paramref name="run"/> cells along join into one straight
    /// slope; a placed tile's Flip H makes it rise to the left.
    ///
    /// It is ONE sprite, not an atlas, but it carries the material's
    /// <see cref="TileDef.AutotileGroup"/>: the cells beside and beneath it then count it as
    /// more of the same ground and draw no crest or flank against it. The collider is the
    /// block's real shape (a convex polygon), so a body walks the ramp and a rolling thing
    /// rolls down it. Art: the walking line runs from the left edge one cell above the
    /// bottom of the solid rows up to the top-right corner.
    /// </summary>
    public static TileDef Slope(string set, string realmDir, string id, string name, int run, int solidRows,
        string color, string stem, string group)
    {
        float cell = MapGrid.PixelsPerCell;
        float width = run * cell, height = (1 + solidRows) * cell;
        return new TileDef
        {
            Id = id, DisplayName = name, Category = TileCategory.Ground, TileSet = set,
            FootprintW = run, FootprintH = 1 + solidRows, EditorColor = color,
            AutotileGroup = group, SpriteSlot = Sprite(realmDir, "Ground", stem),
            // low left corner of the ramp → its top right → down the right side → along the bottom
            EffectArea = ShapeDef.Polygon(new[] { 0f, cell, width, 0f, width, height, 0f, height }),
        };
    }

    /// <summary>One-way platform. EffectArea is left null on purpose: the whole footprint is
    /// the collider and its top edge is the standing surface (KNOWLEDGE v0.6.18), which is
    /// why every platform sprite paints its walkable line on the canvas top.</summary>
    public static TileDef Platform(string set, string realmDir, string id, string name, int w, int h,
        string color, string stem) => new()
    {
        Id = id, DisplayName = name, Category = TileCategory.Platform, TileSet = set,
        FootprintW = w, FootprintH = h, EditorColor = color, SpriteSlot = Sprite(realmDir, "Platform", stem),
    };

    public static TileDef Soft(string set, string realmDir, string id, string name, int w, int h,
        string color, string stem, float stagnation) => new()
    {
        Id = id, DisplayName = name, Category = TileCategory.SoftVolume, TileSet = set,
        FootprintW = w, FootprintH = h, EditorColor = color, SpriteSlot = Sprite(realmDir, "SoftVolume", stem),
        SoftVolumeTuning = new SoftVolumeTuning { StagnationIndex = stagnation },
    };

    /// <summary>A body of water (or mud): a buoyant medium that really holds a body.
    /// `stagnation` is the share of top speed left (the swimming speed), `gravityScale` the
    /// share of gravity left, `sinkSpeed` the fastest anything sinks on its own (m/s), and
    /// `drag` how hard the water enforces all of that — see <see cref="SoftVolumeTuning"/>.</summary>
    public static TileDef Water(string set, string realmDir, string id, string name, int w, int h,
        string color, string stem, float stagnation, float gravityScale, float sinkSpeed, float drag) => new()
    {
        Id = id, DisplayName = name, Category = TileCategory.SoftVolume, TileSet = set,
        FootprintW = w, FootprintH = h, AllowedRoles = LayerRoleMask.Any, EditorColor = color,
        SpriteSlot = Sprite(realmDir, "SoftVolume", stem),
        SoftVolumeTuning = new SoftVolumeTuning
        {
            StagnationIndex = stagnation, GravityScale = gravityScale, SinkSpeed = sinkSpeed, Drag = drag,
        },
    };

    /// <summary>A pure current: it carries whatever is inside at (flowX, flowY) metres per
    /// second and does nothing else — no drag, no jump support. Flip H reverses flowX.</summary>
    public static TileDef Current(string set, string realmDir, string id, string name, int w, int h,
        string color, string stem, float flowX, float flowY) => new()
    {
        Id = id, DisplayName = name, Category = TileCategory.SoftVolume, TileSet = set,
        FootprintW = w, FootprintH = h, AllowedRoles = LayerRoleMask.Any, EditorColor = color,
        SpriteSlot = Sprite(realmDir, "SoftVolume", stem),
        SoftVolumeTuning = new SoftVolumeTuning { Medium = false, FlowX = flowX, FlowY = flowY },
    };

    public static TileDef Hazard(string set, string realmDir, string id, string name, int w, int h,
        string color, string stem, string scene, ShapeDef effectArea) => new()
    {
        Id = id, DisplayName = name, Category = TileCategory.Hazard, TileSet = set,
        FootprintW = w, FootprintH = h, EditorColor = color, SpriteSlot = Sprite(realmDir, "Hazard", stem),
        EffectArea = effectArea,
        Props = new Dictionary<string, string> { [TileProps.Scene] = scene },
    };

    public static TileDef Deco(string set, string realmDir, string id, string name, int w, int h,
        string color, string stem) => new()
    {
        Id = id, DisplayName = name, Category = TileCategory.Decoration, TileSet = set,
        FootprintW = w, FootprintH = h, AllowedRoles = LayerRoleMask.Any, EditorColor = color,
        SpriteSlot = Sprite(realmDir, "Decoration", stem),
    };
}

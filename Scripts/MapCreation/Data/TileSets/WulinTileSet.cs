namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// Banboo Maze (BM) — "Wulin" (武林). Chinese ink-wash with visible brushwork. The kit is
/// built for height: every bamboo is a <see cref="TileCategory.SoftVolume"/> — never ground
/// or a platform — so a fighter can dive into a culm, hang there and chain jumps up it,
/// while temple roofs and bracket beams are thin one-way perches. Vertical, ambush-friendly
/// maps that favour assassins.
/// Art: `Tools/realm_art/wulin.py`. Direction: `Docs/Art/RealmStyleBible.md` §Wulin.
/// </summary>
internal static class WulinTileSet
{
    private const string Set = TileSets.BambooMaze;
    private const string Dir = "BambooMaze";

    /// <summary>Bamboo is light cover, not a mire: movement keeps 60% of top speed inside it
    /// (a cloud keeps 30%, a palm 10%), so it reads as a lane to travel, not a trap.</summary>
    private const float BambooStagnation = 0.6f;

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.bm_rock", "Ink Rock", "#55524C", "ground_bm_rock", "terrain.bm_rock");

        yield return RealmTiles.Platform(Set, Dir, "platform.bm_roof_wide", "Temple Roof (wide)", 6, 1, "#3A3F45", "platform_bm_roof_wide");
        yield return RealmTiles.Platform(Set, Dir, "platform.bm_roof_small", "Temple Roof (small)", 4, 1, "#3A3F45", "platform_bm_roof_small");
        yield return RealmTiles.Platform(Set, Dir, "platform.bm_beam", "Bracket Beam", 3, 1, "#7A3A2B", "platform_bm_beam");

        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_giant", "Bamboo (giant)", 2, 16, "#3F634B", "softvolume_bm_bamboo_giant", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_tall", "Bamboo (tall)", 2, 10, "#3F634B", "softvolume_bm_bamboo_tall", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_mid", "Bamboo (mid)", 2, 7, "#3F634B", "softvolume_bm_bamboo_mid", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_young", "Bamboo (young)", 1, 4, "#4E7A5C", "softvolume_bm_bamboo_young", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_stalk", "Bamboo Stalk (stackable)", 1, 4, "#2F4A3A", "softvolume_bm_bamboo_stalk", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_mist", "Valley Mist", 4, 2, "#DDD6C4", "softvolume_bm_mist", 0.3f);

        yield return RealmTiles.Hazard(Set, Dir, "hazard.bm_stakes", "Bamboo Stakes", 2, 1, "#24402F", "hazard_bm_stakes",
            "res://Scenes/SpikeHazard.tscn", ShapeDef.Rect(cell * 0.10f, cell * 0.18f, cell * 1.80f, cell * 0.82f));

        yield return RealmTiles.Deco(Set, Dir, "deco.bm_column", "Lacquer Column", 1, 3, "#7A3A2B", "deco_bm_column");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_lattice", "Lattice Wall", 2, 3, "#D9CFB6", "deco_bm_lattice");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_lantern", "Stone Lantern", 1, 2, "#8B877E", "deco_bm_lantern");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_seal", "Colophon & Seal", 1, 3, "#B23A2B", "deco_bm_seal");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_sun", "Cinnabar Sun", 3, 3, "#B23A2B", "deco_bm_sun");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_cranes", "Cranes", 3, 2, "#35322E", "deco_bm_cranes");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_peaks_far", "Karst Peaks (far)", 16, 6, "#9C978D", "deco_bm_peaks_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_peaks_mid", "Karst Peaks (near)", 16, 6, "#69655E", "deco_bm_peaks_mid");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_mist_band", "Mist Band", 16, 2, "#E5DECD", "deco_bm_mist_band");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_bamboo_far", "Distant Bamboo", 3, 9, "#9C978D", "deco_bm_bamboo_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_bamboo_curtain", "Bamboo Curtain", 4, 8, "#B5B0A5", "deco_bm_bamboo_curtain");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_bamboo_curtain_b", "Bamboo Curtain (alt)", 4, 8, "#B5B0A5", "deco_bm_bamboo_curtain_b");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_leaf_fringe", "Leaf Fringe", 4, 2, "#1B1917", "deco_bm_leaf_fringe");
    }
}

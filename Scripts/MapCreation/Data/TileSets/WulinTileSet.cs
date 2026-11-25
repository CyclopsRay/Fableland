namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// Banboo Maze (BM) — "Wulin" (武林). Freehand ink painting (写意): calligraphic strokes,
/// splashed ink, and most of the paper left bare (留白). The kit is built for height: every
/// bamboo is a <see cref="TileCategory.SoftVolume"/> — never ground or a platform — so a
/// fighter can dive into a culm, hang there, swim up it and jump off its tip, while temple roofs,
/// bracket beams and a stone bridge are thin one-way perches. The landscape is part of the
/// fight: a waterfall is a current that sweeps whoever enters it downward, so whoever holds
/// the head of a fall holds the high ground. Vertical, ambush-friendly maps that favour
/// assassins.
/// Art: `Tools/realm_art/wulin.py`. Direction: `Docs/Art/RealmStyleBible.md` §Wulin.
/// </summary>
internal static class WulinTileSet
{
    private const string Set = TileSets.BambooMaze;
    private const string Dir = "BambooMaze";

    /// <summary>Bamboo is light cover, not a mire: movement keeps 60% of top speed inside it
    /// (a cloud keeps 30%, a palm 10%), so it reads as a lane to travel, not a trap — and a
    /// lane to climb: holding Up inside a culm rises 8.6 m/s.</summary>
    private const float BambooStagnation = 0.6f;

    /// <summary>Falling water carries a body down at 14 m/s — under half a jump's launch
    /// speed (32 m/s), but with no jump support inside it, enough that the fall cannot be
    /// climbed and a crossing costs height.</summary>
    private const float WaterfallCarry = 14f;

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.bm_rock", "Ink Rock", "#4E5558", "ground_bm_rock", "terrain.bm_rock");

        yield return RealmTiles.Platform(Set, Dir, "platform.bm_roof_wide", "Temple Roof (wide)", 6, 1, "#3A3F45", "platform_bm_roof_wide");
        yield return RealmTiles.Platform(Set, Dir, "platform.bm_roof_small", "Temple Roof (small)", 4, 1, "#3A3F45", "platform_bm_roof_small");
        yield return RealmTiles.Platform(Set, Dir, "platform.bm_beam", "Bracket Beam", 3, 1, "#2E3F3A", "platform_bm_beam");
        yield return RealmTiles.Platform(Set, Dir, "platform.bm_bridge", "Stone Bridge", 6, 2, "#9BA5A7", "platform_bm_bridge");

        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_giant", "Bamboo (giant)", 2, 16, "#3F634B", "softvolume_bm_bamboo_giant", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_tall", "Bamboo (tall)", 2, 10, "#3F634B", "softvolume_bm_bamboo_tall", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_mid", "Bamboo (mid)", 2, 7, "#3F634B", "softvolume_bm_bamboo_mid", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_young", "Bamboo (young)", 1, 4, "#4E7A5C", "softvolume_bm_bamboo_young", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_bamboo_stalk", "Bamboo Stalk (stackable)", 1, 4, "#2F4A3A", "softvolume_bm_bamboo_stalk", BambooStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_mist", "Valley Mist", 4, 2, "#DDE3E2", "softvolume_bm_mist", 0.3f);
        yield return RealmTiles.Current(Set, Dir, "softvolume.bm_waterfall", "Waterfall", 2, 4, "#A6CAD0", "softvolume_bm_waterfall", 0f, WaterfallCarry);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.bm_pine", "Cliff Pine", 4, 5, "#1C352B", "softvolume_bm_pine", 0.5f);

        yield return RealmTiles.Hazard(Set, Dir, "hazard.bm_stakes", "Bamboo Stakes", 2, 1, "#24402F", "hazard_bm_stakes",
            "res://Scenes/SpikeHazard.tscn", ShapeDef.Rect(cell * 0.10f, cell * 0.18f, cell * 1.80f, cell * 0.82f));

        yield return RealmTiles.Deco(Set, Dir, "deco.bm_column", "Timber Column", 1, 3, "#2E3F3A", "deco_bm_column");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_lattice", "Lattice Wall", 2, 3, "#D8DFDD", "deco_bm_lattice");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_lantern", "Stone Lantern", 1, 2, "#8A9395", "deco_bm_lantern");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_seal", "Colophon & Seal", 1, 3, "#33574A", "deco_bm_seal");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_moon", "Moon", 3, 3, "#F7F9F8", "deco_bm_moon");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_cranes", "Cranes", 3, 2, "#2A2F33", "deco_bm_cranes");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_peak", "Karst Peak (hero)", 8, 12, "#2A2F33", "deco_bm_peak");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_peak_b", "Karst Peak (slender)", 5, 10, "#2A2F33", "deco_bm_peak_b");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_pine", "Pine", 4, 5, "#1C352B", "deco_bm_pine");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_fall_far", "Distant Waterfall", 3, 8, "#A6CAD0", "deco_bm_fall_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_spray", "Waterfall Spray", 4, 2, "#F7F9F8", "deco_bm_spray");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_village", "Distant Village", 6, 3, "#565E63", "deco_bm_village");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_pavilion", "Pavilion", 3, 3, "#3F5258", "deco_bm_pavilion");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_boat", "Fishing Boat", 3, 1, "#14171A", "deco_bm_boat");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_peaks_far", "Karst Peaks (far)", 16, 6, "#8E979B", "deco_bm_peaks_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_peaks_mid", "Karst Peaks (near)", 16, 6, "#2A2F33", "deco_bm_peaks_mid");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_mist_band", "Mist Band", 16, 2, "#F7F9F8", "deco_bm_mist_band");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_bamboo_far", "Distant Bamboo", 3, 9, "#8E979B", "deco_bm_bamboo_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_bamboo_curtain", "Bamboo Curtain", 4, 8, "#BEC6C8", "deco_bm_bamboo_curtain");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_bamboo_curtain_b", "Bamboo Curtain (alt)", 4, 8, "#BEC6C8", "deco_bm_bamboo_curtain_b");
        yield return RealmTiles.Deco(Set, Dir, "deco.bm_leaf_fringe", "Leaf Fringe", 4, 2, "#14171A", "deco_bm_leaf_fringe");
    }
}

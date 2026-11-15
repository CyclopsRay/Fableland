namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// Starland (SL) — "Starfield". Egypt at dusk under a field of stars, drawn as a flat
/// minimalist poster: no outlines, two-plane forms, hairline gold. The kit is deliberately
/// small — the realm's maps are vast and nearly empty, with a few colossal monuments built
/// from <c>ground.sl_sandstone</c>, so long sight lines favour ranged protagonists.
/// Art: `Tools/realm_art/starland.py`. Direction: `Docs/Art/RealmStyleBible.md` §Starfield.
/// </summary>
internal static class StarlandTileSet
{
    private const string Set = TileSets.Starland;
    private const string Dir = "Starland";

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.sl_sand", "Dune Sand", "#F6D79C", "ground_sl_sand", "terrain.sl_sand");
        yield return RealmTiles.Ground(Set, Dir, "ground.sl_sandstone", "Monument Sandstone", "#F2D3A0", "ground_sl_sandstone", "terrain.sl_sandstone");

        yield return RealmTiles.Platform(Set, Dir, "platform.sl_slab", "Levitating Slab", 3, 1, "#DDB07C", "platform_sl_slab");
        yield return RealmTiles.Platform(Set, Dir, "platform.sl_constellation", "Constellation Bridge", 4, 1, "#FBE08A", "platform_sl_constellation");

        // A slow, buoyant drift: star-dust is for hanging in the sky and lining up a shot.
        yield return RealmTiles.Soft(Set, Dir, "softvolume.sl_stardust", "Stardust Nebula", 3, 2, "#5C5AB8", "softvolume_sl_stardust", 0.35f);

        // Only the white-hot core burns, not the whole crater.
        yield return RealmTiles.Hazard(Set, Dir, "hazard.sl_fallen_star", "Fallen Star", 2, 1, "#F0875C", "hazard_sl_fallen_star",
            "res://Scenes/FireHazard.tscn", ShapeDef.Circle(cell, cell * 0.52f, cell * 0.42f));

        yield return RealmTiles.Deco(Set, Dir, "deco.sl_obelisk", "Obelisk", 1, 5, "#F2D3A0", "deco_sl_obelisk");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_star_gate", "Star Gate Pylon", 6, 5, "#DDB07C", "deco_sl_star_gate");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_broken_column", "Fallen Column", 1, 2, "#F2D3A0", "deco_sl_broken_column");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_pyramid", "Great Pyramid (far)", 10, 6, "#DDB07C", "deco_sl_pyramid");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_pyramid_small", "Pyramid (far, small)", 5, 3, "#C99E6E", "deco_sl_pyramid_small");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_dunes_far", "Far Dunes", 16, 6, "#8E4A78", "deco_sl_dunes_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_dunes_near", "Near Dunes", 16, 6, "#B45F6C", "deco_sl_dunes_near");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_horizon", "Dusk Horizon", 8, 12, "#A8527A", "deco_sl_horizon");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_sun", "Setting Sun", 4, 4, "#FBC57A", "deco_sl_sun");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_moon", "Crescent Moon", 3, 3, "#FFF4D2", "deco_sl_moon");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_starfield", "Starfield", 8, 4, "#3B2A6E", "deco_sl_starfield");
        yield return RealmTiles.Deco(Set, Dir, "deco.sl_constellation", "Star-Fruit Constellation", 5, 4, "#FBE08A", "deco_sl_constellation");
    }
}

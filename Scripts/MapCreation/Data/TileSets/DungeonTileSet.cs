namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// HollowCastle (HC) — "Dungeon". Lit realism: stone, iron and timber shaded as true relief
/// by a cold key light and a hot lava bounce. The kit is dense and hostile — low ceilings,
/// many short perches, chains to climb, lava pools and spike plates — so its maps are
/// crowded close-quarters gauntlets.
/// Art: `Tools/realm_art/dungeon.py`. Direction: `Docs/Art/RealmStyleBible.md` §Dungeon.
/// </summary>
internal static class DungeonTileSet
{
    private const string Set = TileSets.HollowCastle;
    private const string Dir = "HollowCastle";

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.hc_brick", "Dungeon Brick", "#6B7078", "ground_hc_brick", "terrain.hc_brick");
        yield return RealmTiles.Ground(Set, Dir, "ground.hc_basalt", "Cavern Basalt", "#3A3B42", "ground_hc_basalt", "terrain.hc_basalt");

        yield return RealmTiles.Platform(Set, Dir, "platform.hc_grate", "Iron Gantry", 3, 1, "#454B55", "platform_hc_grate");
        yield return RealmTiles.Platform(Set, Dir, "platform.hc_plank", "Timber Catwalk", 3, 1, "#5C4431", "platform_hc_plank");
        yield return RealmTiles.Platform(Set, Dir, "platform.hc_ledge", "Corbel Ledge", 2, 1, "#7A8089", "platform_hc_ledge");

        yield return RealmTiles.Soft(Set, Dir, "softvolume.hc_chains", "Hanging Chains", 1, 4, "#5B4A3F", "softvolume_hc_chains", 0.55f);
        // A web is the one thing in the realm that really holds you: 12% of top speed.
        yield return RealmTiles.Soft(Set, Dir, "softvolume.hc_cobweb", "Cobweb", 2, 2, "#AEB4BD", "softvolume_hc_cobweb", 0.12f);

        // Lava is painted as an area, so touching tiles fuse into one hazard (TileProps.MergeAdjacent).
        yield return RealmTiles.Fill(Set, Dir, TileCategory.Hazard, "Hazard", "hazard.hc_lava", "Lava", "#F26A0E",
            "hazard_hc_lava", "hazard.hc_lava", new Dictionary<string, string>
            {
                [TileProps.Scene] = "res://Scenes/LavaHazard.tscn",
                [TileProps.MergeAdjacent] = "true",
            });
        yield return RealmTiles.Hazard(Set, Dir, "hazard.hc_spikes", "Floor Spikes", 1, 1, "#8A2A1E", "hazard_hc_spikes",
            "res://Scenes/SpikeHazard.tscn", ShapeDef.Rect(cell * 0.10f, cell * 0.20f, cell * 0.80f, cell * 0.80f));

        yield return RealmTiles.Deco(Set, Dir, "deco.hc_torch", "Wall Torch", 3, 3, "#F26A0E", "deco_hc_torch");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_pillar", "Stone Pillar", 1, 4, "#6B7078", "deco_hc_pillar");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_arch", "Portcullis Arch", 4, 4, "#565A62", "deco_hc_arch");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_skulls", "Bone Pile", 2, 1, "#CFC6A8", "deco_hc_skulls");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_stalactites", "Stalactites", 3, 2, "#57504A", "deco_hc_stalactites");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_banner", "Tattered Banner", 1, 3, "#7A1E1E", "deco_hc_banner");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_wall_far", "Far Wall", 8, 6, "#25262C", "deco_hc_wall_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_cavern_far", "Far Cavern", 8, 5, "#2A0A08", "deco_hc_cavern_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_light_shaft", "Light Shaft", 2, 6, "#BFD4EA", "deco_hc_light_shaft");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_lava_glow", "Lava Glow", 2, 3, "#E8420A", "deco_hc_lava_glow");
        yield return RealmTiles.Deco(Set, Dir, "deco.hc_lava_glow_end", "Lava Glow (end)", 2, 3, "#E8420A", "deco_hc_lava_glow_end");
    }
}

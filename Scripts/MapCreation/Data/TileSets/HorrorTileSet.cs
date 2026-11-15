namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// Kingdom of Horror (KH) — "Dark Wood": PumpKing's realm. Scratchboard: light scratched out
/// of a black board in tangled, wandering lines — moon blue for the night, yellow for
/// anything that gives light, red for what should not be touched. The kit is a wood you get
/// caught in: limbs are the footing, bramble is a soft volume that all but holds you
/// (stagnation 0.15), fog and bare canopy hide what is in them, and the floor is sown with
/// traps. Some of its maps have a castle (<c>ground.kh_stone</c>); the wood needs none.
///
/// Not in the overworld pool yet (design TBD #20): its maps are tagged "KH" and play from
/// the editor until the world is added.
/// Art: `Tools/realm_art/horror.py`. Direction: `Docs/Art/RealmStyleBible.md` §Kingdom of Horror.
/// </summary>
internal static class HorrorTileSet
{
    private const string Set = TileSets.KingdomOfHorror;
    private const string Dir = "KingdomOfHorror";

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.kh_earth", "Rooted Earth", "#1B2D6B", "ground_kh_earth", "terrain.kh_earth");
        yield return RealmTiles.Ground(Set, Dir, "ground.kh_stone", "Castle Stone", "#3F6FD8", "ground_kh_stone", "terrain.kh_stone");

        yield return RealmTiles.Platform(Set, Dir, "platform.kh_branch", "Gnarled Limb", 4, 1, "#3F6FD8", "platform_kh_branch");
        yield return RealmTiles.Platform(Set, Dir, "platform.kh_plank", "Rotten Hoarding", 3, 1, "#9DBEFF", "platform_kh_plank");

        yield return RealmTiles.Soft(Set, Dir, "softvolume.kh_bramble", "Bramble", 3, 2, "#D8323A", "softvolume_kh_bramble", 0.15f);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.kh_fog", "Ground Fog", 4, 2, "#9DBEFF", "softvolume_kh_fog", 0.45f);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.kh_canopy", "Bare Canopy", 4, 3, "#3F6FD8", "softvolume_kh_canopy", 0.5f);

        yield return RealmTiles.Hazard(Set, Dir, "hazard.kh_trap", "Bear Trap", 2, 1, "#D8323A", "hazard_kh_trap",
            "res://Scenes/SpikeHazard.tscn", ShapeDef.Rect(cell * 0.30f, cell * 0.40f, cell * 1.40f, cell * 0.60f));
        yield return RealmTiles.Hazard(Set, Dir, "hazard.kh_wisp", "Will-o'-the-Wisp", 1, 2, "#FFD23F", "hazard_kh_wisp",
            "res://Scenes/FireHazard.tscn", ShapeDef.Circle(cell * 0.5f, cell * 1.20f, cell * 0.42f));

        yield return RealmTiles.Deco(Set, Dir, "deco.kh_tree", "Dead Tree (great)", 5, 8, "#3F6FD8", "deco_kh_tree");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_tree_b", "Dead Tree", 3, 6, "#3F6FD8", "deco_kh_tree_b");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_lantern", "Jack-o'-Lantern", 1, 2, "#FFD23F", "deco_kh_lantern");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_window", "Lit Window", 1, 2, "#FFD23F", "deco_kh_window");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_fence", "Iron Railings", 3, 1, "#9DBEFF", "deco_kh_fence");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_fireflies", "Fireflies", 4, 3, "#FFD23F", "deco_kh_fireflies");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_moon", "Moon", 4, 4, "#FFD23F", "deco_kh_moon");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_castle_far", "Far Castle", 10, 8, "#070A14", "deco_kh_castle_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.kh_forest_far", "Far Wood", 16, 6, "#070A14", "deco_kh_forest_far");
    }
}

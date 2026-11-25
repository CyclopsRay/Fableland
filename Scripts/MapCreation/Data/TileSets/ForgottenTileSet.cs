namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// The Forgotten (TF) — "Forgotten City": Pixolotl's home. Pixel art — a hard 32 x 32 grid to
/// the cell, a small palette, shading by ordered dither. Teotihuacan in old violet stone,
/// going back to swamp: everyone outside takes it for a ruin, but its cornices carry neon,
/// its statues have working lenses and its bridges are made of light. The kit follows the
/// land: the low ground is swamp (water you wade in, cattails, mud banks), and as a map
/// climbs it becomes pyramid — terraces of <c>ground.tf_stone</c>, floating slabs, light
/// bridges — where the city's security still works (beams, obsidian).
///
/// In the overworld pool since v0.11.0: a run can draw The Forgotten as one of its five
/// realms, and its maps are tagged "TF".
/// Art: `Tools/realm_art/forgotten.py`. Direction: `Docs/Art/RealmStyleBible.md` §The Forgotten.
/// </summary>
internal static class ForgottenTileSet
{
    private const string Set = TileSets.TheForgotten;
    private const string Dir = "TheForgotten";

    /// <summary>Swamp water is waded, not swum: 40% of top speed, and 60% of gravity left —
    /// enough to feel the bottom, not enough to float. It holds on: left alone a body sinks
    /// at 2 m/s, swims up at 2 m/s, and leaves a pool by a jump from its surface that clears
    /// the bank by a metre.</summary>
    private const float SwampStagnation = 0.4f;
    private const float SwampGravityScale = 0.6f;
    private const float SwampSinkSpeed = 2f;
    private const float SwampDrag = 4f;

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.tf_stone", "Old Stone", "#4A4168", "ground_tf_stone", "terrain.tf_stone");
        yield return RealmTiles.Ground(Set, Dir, "ground.tf_mud", "Swamp Bank", "#3B2E28", "ground_tf_mud", "terrain.tf_mud");

        yield return RealmTiles.Platform(Set, Dir, "platform.tf_slab", "Floating Slab", 3, 1, "#6F648F", "platform_tf_slab");
        yield return RealmTiles.Platform(Set, Dir, "platform.tf_light", "Light Bridge", 4, 1, "#3DF6FF", "platform_tf_light");

        yield return RealmTiles.Water(Set, Dir, "softvolume.tf_water", "Swamp Water", 4, 2, "#145259", "softvolume_tf_water", SwampStagnation, SwampGravityScale, SwampSinkSpeed, SwampDrag);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.tf_reeds", "Cattails", 2, 3, "#2F7A52", "softvolume_tf_reeds", 0.5f);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.tf_vines", "Hanging Vines", 1, 4, "#5FBF6E", "softvolume_tf_vines", 0.5f);

        yield return RealmTiles.Hazard(Set, Dir, "hazard.tf_laser", "Security Beam", 1, 3, "#FF3DBE", "hazard_tf_laser",
            "res://Scenes/FireHazard.tscn", ShapeDef.Rect(cell * 0.32f, cell * 0.18f, cell * 0.36f, cell * 2.64f));
        yield return RealmTiles.Hazard(Set, Dir, "hazard.tf_obsidian", "Obsidian Blades", 2, 1, "#07060F", "hazard_tf_obsidian",
            "res://Scenes/SpikeHazard.tscn", ShapeDef.Rect(cell * 0.10f, cell * 0.25f, cell * 1.80f, cell * 0.75f));

        yield return RealmTiles.Deco(Set, Dir, "deco.tf_serpent", "Serpent Head", 2, 3, "#6F648F", "deco_tf_serpent");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_stele", "Stele", 1, 3, "#4A4168", "deco_tf_stele");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_sign", "Hologram Glyph", 2, 2, "#FF3DBE", "deco_tf_sign");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_axolotl", "Axolotl", 2, 1, "#FF8FCF", "deco_tf_axolotl");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_tree", "Ahuehuete", 4, 6, "#2F7A52", "deco_tf_tree");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_pyramid_far", "Far Pyramid", 12, 8, "#2E2848", "deco_tf_pyramid_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_city_far", "Far City", 16, 6, "#1B1730", "deco_tf_city_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_jungle", "Canopy", 16, 4, "#1E4A38", "deco_tf_jungle");
        yield return RealmTiles.Deco(Set, Dir, "deco.tf_stars", "Stars", 8, 4, "#F4FBFF", "deco_tf_stars");
    }
}

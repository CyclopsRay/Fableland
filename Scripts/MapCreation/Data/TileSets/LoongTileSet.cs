namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// Palace of LOOING (PL) — "Cloud Court". The dragon emperor's palace above the clouds, in
/// cloisonné enamel: jewel-coloured cells inside raised gold wire. Solid footing is scarce
/// by design — a few terrace islands and thin lotus pads, bridges and roofs — and the space
/// between is hung with auspicious clouds, every one a soft volume. Its maps are aerial:
/// the fight is carried from cloud to cloud, which favours anyone at home in the air.
/// Art: `Tools/realm_art/loong.py`. Direction: `Docs/Art/RealmStyleBible.md` §Cloud Court.
/// </summary>
internal static class LoongTileSet
{
    private const string Set = TileSets.PalaceOfLoong;
    private const string Dir = "PalaceOfLoong";

    /// <summary>Same hold as the beach clouds: 30% of top speed — a cloud carries you, slowly.</summary>
    private const float CloudStagnation = 0.3f;

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.pl_terrace", "Palace Terrace", "#4B2A7A", "ground_pl_terrace", "terrain.pl_terrace");

        yield return RealmTiles.Platform(Set, Dir, "platform.pl_bridge", "Moon Bridge", 4, 1, "#1E4FA3", "platform_pl_bridge");
        yield return RealmTiles.Platform(Set, Dir, "platform.pl_lotus", "Lotus Pad", 2, 1, "#74C095", "platform_pl_lotus");
        yield return RealmTiles.Platform(Set, Dir, "platform.pl_roof", "Palace Roof", 5, 1, "#EDBE3C", "platform_pl_roof");

        yield return RealmTiles.Soft(Set, Dir, "softvolume.pl_cloud", "Auspicious Cloud", 3, 2, "#F4EAD4", "softvolume_pl_cloud", CloudStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.pl_cloud_trail", "Trailing Cloud", 4, 2, "#F4EAD4", "softvolume_pl_cloud_trail", CloudStagnation);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.pl_cloud_small", "Cloud Wisp", 2, 1, "#F4EAD4", "softvolume_pl_cloud_small", CloudStagnation);

        // The cold is the pearl and its rime, not the stand it sits on.
        yield return RealmTiles.Hazard(Set, Dir, "hazard.pl_frost_pearl", "Frost Pearl", 2, 1, "#7FD6EC", "hazard_pl_frost_pearl",
            "res://Scenes/FreezeHazard.tscn", ShapeDef.Circle(cell, cell * 0.42f, cell * 0.40f));

        yield return RealmTiles.Deco(Set, Dir, "deco.pl_dragon_column", "Dragon Column", 1, 5, "#C5382C", "deco_pl_dragon_column");
        yield return RealmTiles.Deco(Set, Dir, "deco.pl_moon_gate", "Moon Gate", 4, 4, "#F4EAD4", "deco_pl_moon_gate");
        yield return RealmTiles.Deco(Set, Dir, "deco.pl_palace_far", "Far Palace", 10, 6, "#8A6A3A", "deco_pl_palace_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.pl_cloud_sea", "Sea of Clouds", 16, 4, "#2CB5A8", "deco_pl_cloud_sea");
        yield return RealmTiles.Deco(Set, Dir, "deco.pl_lantern", "Palace Lantern", 1, 2, "#C5382C", "deco_pl_lantern");
        yield return RealmTiles.Deco(Set, Dir, "deco.pl_moon", "Night Pearl", 4, 4, "#F4EAD4", "deco_pl_moon");
        yield return RealmTiles.Deco(Set, Dir, "deco.pl_sky", "Heaven's Night", 8, 10, "#5A2E86", "deco_pl_sky");
        yield return RealmTiles.Deco(Set, Dir, "deco.pl_banner", "Court Banner", 1, 3, "#1E4FA3", "deco_pl_banner");
    }
}

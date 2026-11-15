namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// The Deserted (TD) — "Ghost Town". A frontier town everyone walked out of, printed as a
/// linocut: black key block, flat spot colours out of register, hatching for tone. The kit
/// builds streets — clapboard buildings (<c>ground.td_timber</c>) faced with one-way
/// balconies, awnings and hanging signs — so its maps are two-storey, mid-range duelling
/// grounds: always a line of sight down the street, always a ledge within one jump.
/// Art: `Tools/realm_art/deserted.py`. Direction: `Docs/Art/RealmStyleBible.md` §Ghost Town.
/// </summary>
internal static class DesertedTileSet
{
    private const string Set = TileSets.TheDeserted;
    private const string Dir = "TheDeserted";

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.td_mesa", "Mesa Rock", "#B5482E", "ground_td_mesa", "terrain.td_mesa");
        yield return RealmTiles.Ground(Set, Dir, "ground.td_timber", "Clapboard Wall", "#D9A13B", "ground_td_timber", "terrain.td_timber");

        yield return RealmTiles.Platform(Set, Dir, "platform.td_balcony", "Hotel Balcony", 3, 1, "#D9A13B", "platform_td_balcony");
        yield return RealmTiles.Platform(Set, Dir, "platform.td_awning", "Shop Awning", 3, 1, "#B5482E", "platform_td_awning");
        yield return RealmTiles.Platform(Set, Dir, "platform.td_sign", "Hanging Sign", 2, 1, "#EADFC8", "platform_td_sign");

        yield return RealmTiles.Soft(Set, Dir, "softvolume.td_tumbleweed", "Tumbleweed", 2, 2, "#8A6A3A", "softvolume_td_tumbleweed", 0.5f);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.td_dust", "Dust Devil", 3, 2, "#D9A13B", "softvolume_td_dust", 0.3f);

        // The spines, not the footprint: a narrow column up the trunk.
        yield return RealmTiles.Hazard(Set, Dir, "hazard.td_cactus", "Saguaro", 1, 2, "#7C8F58", "hazard_td_cactus",
            "res://Scenes/SpikeHazard.tscn", ShapeDef.Rect(cell * 0.14f, cell * 0.28f, cell * 0.72f, cell * 1.72f));

        yield return RealmTiles.Deco(Set, Dir, "deco.td_saloon", "Saloon Front", 6, 5, "#D9A13B", "deco_td_saloon");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_water_tower", "Water Tower", 3, 6, "#B5482E", "deco_td_water_tower");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_windmill", "Windpump", 3, 6, "#D9A13B", "deco_td_windmill");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_mesas_far", "Far Buttes", 16, 7, "#B5482E", "deco_td_mesas_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_sun", "Woodcut Sun", 4, 4, "#D9A13B", "deco_td_sun");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_clouds", "Printed Clouds", 6, 2, "#EADFC8", "deco_td_clouds");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_sky", "Printed Sky", 16, 6, "#5E8C87", "deco_td_sky");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_barrel", "Barrel", 1, 1, "#D9A13B", "deco_td_barrel");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_skull", "Steer Skull", 1, 1, "#EADFC8", "deco_td_skull");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_signpost", "Signpost", 1, 2, "#D9A13B", "deco_td_signpost");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_fence", "Ranch Fence", 3, 1, "#D9A13B", "deco_td_fence");
        // Facade pieces for the foreground: laid over a building's solid body they dress it
        // without ever covering a fighter (nobody can stand inside a wall).
        yield return RealmTiles.Deco(Set, Dir, "deco.td_window", "Sash Window", 1, 2, "#5E8C87", "deco_td_window");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_door", "Batwing Doors", 2, 2, "#B5482E", "deco_td_door");
        yield return RealmTiles.Deco(Set, Dir, "deco.td_parapet", "False-Front Parapet", 4, 2, "#D9A13B", "deco_td_parapet");
    }
}

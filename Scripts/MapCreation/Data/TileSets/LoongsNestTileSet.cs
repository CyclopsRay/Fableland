namespace Fableland.MapCreation.Data;

using System.Collections.Generic;

/// <summary>
/// Palace of LOOING (PL) — "Loong's Nest" (龙宫). The dragon king's palace under the sea,
/// in 工笔重彩: fine even ink lines enclosing graded azurite, purple and cyan. The whole map
/// is water — a buoyant, resisting medium — so nobody falls and nobody runs: every position
/// is reached on purpose. Hidden currents carry a fighter toward the urchin beds, and
/// columns of bubbles lift them; kelp groves slow and hide. It favours whoever can place
/// themselves precisely.
///
/// Water is laid as big <c>softvolume.ln_water</c> tiles on a Farview layer with Collision
/// on (MapCreation.gdd §3), so it fills the map without taking the battlefield's cells.
/// Art: `Tools/realm_art/loongnest.py`. Direction: `Docs/Art/RealmStyleBible.md` §Loong's Nest.
/// </summary>
internal static class LoongsNestTileSet
{
    private const string Set = TileSets.LoongsNest;
    private const string Dir = "LoongsNest";

    /// <summary>"Relatively high resistance": a swimmer has 55% of their top speed — 5.5 m/s
    /// for a 10 m/s fighter — and <see cref="WaterDrag"/> makes that a real limit.</summary>
    private const float WaterStagnation = 0.55f;
    /// <summary>Buoyancy and gravity are simply added: 35% of gravity is left.</summary>
    private const float WaterGravityScale = 0.35f;
    /// <summary>What that buoyancy comes to: left alone, a body sinks at 3 m/s (a cell and a
    /// half per second). Holding Down swims down at swimming speed.</summary>
    private const float WaterSinkSpeed = 3f;
    /// <summary>Five times foliage's resistance: the speed cap holds against a held
    /// direction, a jump becomes a 3.4 m swimming stroke (8.3 m in air), and knockback dies
    /// quickly. The content check measures all of these on a real character.</summary>
    private const float WaterDrag = 5f;
    /// <summary>A current carries at 4 m/s: swimming against it leaves 1.5 m/s of headway,
    /// so it takes you where it goes unless you leave it sideways.</summary>
    private const float CurrentCarry = 4f;
    /// <summary>Bubbles lift at 7 m/s: more than a body sinks or dives, so a column is an
    /// elevator you cannot refuse.</summary>
    private const float BubbleLift = 7f;
    /// <summary>Kelp is the realm's thicket: 30% of top speed inside it.</summary>
    private const float KelpStagnation = 0.3f;

    public static IEnumerable<TileDef> Defs()
    {
        float cell = MapGrid.PixelsPerCell;

        yield return RealmTiles.Ground(Set, Dir, "ground.ln_reef", "Reef Rock", "#161D4A", "ground_ln_reef", "terrain.ln_reef");
        yield return RealmTiles.Ground(Set, Dir, "ground.ln_palace", "Palace Wall", "#5B3FA8", "ground_ln_palace", "terrain.ln_palace");

        yield return RealmTiles.Platform(Set, Dir, "platform.ln_eave", "Glazed Eave", 5, 1, "#33C7D6", "platform_ln_eave");
        yield return RealmTiles.Platform(Set, Dir, "platform.ln_shell", "Giant Clam", 3, 1, "#A48BE6", "platform_ln_shell");

        yield return RealmTiles.Water(Set, Dir, "softvolume.ln_water", "Sea Water", 8, 6, "#2C63CC", "softvolume_ln_water", WaterStagnation, WaterGravityScale, WaterSinkSpeed, WaterDrag);
        yield return RealmTiles.Current(Set, Dir, "softvolume.ln_current", "Current", 4, 2, "#B6F3F4", "softvolume_ln_current", CurrentCarry, 0f);
        yield return RealmTiles.Current(Set, Dir, "softvolume.ln_bubbles", "Bubble Column", 2, 4, "#B6F3F4", "softvolume_ln_bubbles", 0f, -BubbleLift);
        yield return RealmTiles.Soft(Set, Dir, "softvolume.ln_kelp", "Kelp", 2, 6, "#127A8C", "softvolume_ln_kelp", KelpStagnation);

        yield return RealmTiles.Hazard(Set, Dir, "hazard.ln_urchin", "Sea Urchins", 2, 1, "#2C1F5C", "hazard_ln_urchin",
            "res://Scenes/SpikeHazard.tscn", ShapeDef.Rect(cell * 0.08f, cell * 0.10f, cell * 1.84f, cell * 0.90f));

        yield return RealmTiles.Deco(Set, Dir, "deco.ln_column", "Palace Column", 1, 5, "#5B3FA8", "deco_ln_column");
        yield return RealmTiles.Deco(Set, Dir, "deco.ln_lantern", "Night Pearl", 1, 2, "#EAFBFF", "deco_ln_lantern");
        yield return RealmTiles.Deco(Set, Dir, "deco.ln_coral", "Coral Tree", 3, 3, "#A48BE6", "deco_ln_coral");
        yield return RealmTiles.Deco(Set, Dir, "deco.ln_fish", "Shoal", 3, 2, "#7FB0F5", "deco_ln_fish");
        yield return RealmTiles.Deco(Set, Dir, "deco.ln_palace_far", "Far Palace", 10, 6, "#15307A", "deco_ln_palace_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.ln_ridge_far", "Seabed Ridges", 16, 5, "#161D4A", "deco_ln_ridge_far");
        yield return RealmTiles.Deco(Set, Dir, "deco.ln_rays", "Light Shafts", 4, 10, "#B6F3F4", "deco_ln_rays");
        // Four rows of open air, then the sea's skin on its fifth row: place it so that row is the
        // first row of water, or the surface a player sees is not where the water starts to hold them.
        yield return RealmTiles.Deco(Set, Dir, "deco.ln_surface", "Sea Surface", 16, 5, "#B6F3F4", "deco_ln_surface");
    }
}

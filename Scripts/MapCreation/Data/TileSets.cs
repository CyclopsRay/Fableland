namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// The realm tile sets (`Docs/Art/RealmStyleBible.md`). A tile set is one realm's art
/// language and building kit; <see cref="TileDef.TileSet"/> names the set a tile kind
/// belongs to. Ids are the world's lower-case abbreviation so map data, sidecar manifests
/// and art folders all agree on one key. Pure C# — no Godot (world colours live in
/// `Fableland.Map.WorldDef`, which this layer must not reference).
///
/// Adding a realm = one entry here, one file under `Data/TileSets/`, one line in
/// <see cref="TileRegistry"/>'s set list. Nothing else switches on a set id (T00 rule 1).
///
/// A world may have more than one set: the Palace of LOOING has Loong's Nest, under the
/// sea, and Cloud Court, its sky district. ORDER IS DATA there: the first set listed for a
/// world is its primary realm (<see cref="ForWorldAbbr"/>) — what a new, empty map of that
/// world opens on. Which of a world's realms a map belongs to is <see cref="MapRealm.Of"/>'s
/// answer, never a lookup by world alone.
/// </summary>
public static class TileSets
{
    public const string Core = "core";
    public const string VanillaKingdom = "vk";
    public const string Starland = "sl";
    public const string BambooMaze = "bm";
    public const string HollowCastle = "hc";
    public const string TheDeserted = "td";
    public const string PalaceOfLoong = "pl";
    public const string LoongsNest = "ln";
    public const string KingdomOfHorror = "kh";
    public const string TheForgotten = "tf";

    public sealed class Info
    {
        public string Id { get; init; }
        /// <summary>Palette label: world name, then the realm's art identity.</summary>
        public string DisplayName { get; init; }
        /// <summary>The realm's short name as the combat layer calls it ("Wulin", "Dungeon") —
        /// what a map card's badge says.</summary>
        public string RealmName { get; init; }
        /// <summary>Overworld world abbreviation (`MapGDD.md` §2), or null for <see cref="Core"/>.</summary>
        public string WorldAbbr { get; init; }
        /// <summary>The paper/sky colour this realm's art is painted against — the suggested
        /// `Canvas.Color` for a new map in the set.</summary>
        public string CanvasColor { get; init; }
        /// <summary>The realm's own colour for chips, badges and card borders, used when its
        /// world is not in the overworld pool yet (a pool world brings its colour with it).</summary>
        public string AccentColor { get; init; }
    }

    /// <summary>Palette order. <see cref="Core"/> first: it is always shown.</summary>
    public static readonly IReadOnlyList<Info> All = new List<Info>
    {
        new() { Id = Core, DisplayName = "Shared", RealmName = "Shared", WorldAbbr = null, CanvasColor = "#87CEEB" },
        new() { Id = VanillaKingdom, DisplayName = "Vanilla Kingdom · Beach", RealmName = "Beach", WorldAbbr = "VK", CanvasColor = "#6DB8D8" },
        new() { Id = Starland, DisplayName = "Starland · Starfield", RealmName = "Starfield", WorldAbbr = "SL", CanvasColor = "#17133A" },
        new() { Id = BambooMaze, DisplayName = "Banboo Maze · Wulin", RealmName = "Wulin", WorldAbbr = "BM", CanvasColor = "#E4E9E8" },
        new() { Id = HollowCastle, DisplayName = "HollowCastle · Dungeon", RealmName = "Dungeon", WorldAbbr = "HC", CanvasColor = "#0E0D12" },
        new() { Id = TheDeserted, DisplayName = "The Deserted · Ghost Town", RealmName = "Ghost Town", WorldAbbr = "TD", CanvasColor = "#F1E3C4" },
        // The Palace of LOOING is under the sea (v0.10.9, the design owner's direction), and
        // its court is above the clouds: Cloud Court — the earlier, aerial proposal for the
        // world — is its sky district, dealt on the Palace's high ground (provisional, TBD
        // #26; until then it sat under a world code no world owned, "CC", and no run could
        // open it). Loong's Nest is listed first: it is the world's primary realm.
        new() { Id = LoongsNest, DisplayName = "Palace of Loong · Loong's Nest", RealmName = "Loong's Nest", WorldAbbr = "PL", CanvasColor = "#0C1230" },
        new() { Id = PalaceOfLoong, DisplayName = "Palace of Loong · Cloud Court", RealmName = "Cloud Court", WorldAbbr = "PL", CanvasColor = "#2A1B4A", AccentColor = "#D9A441" },
        // The two worlds that joined the overworld pool in v0.11.0. Their accent colours are
        // kept for the day a world is taken out again; a pool world brings its own colour.
        new() { Id = KingdomOfHorror, DisplayName = "Kingdom of Horror · Dark Wood", RealmName = "Dark Wood", WorldAbbr = "KH", CanvasColor = "#111B3D", AccentColor = "#FFC83D" },
        new() { Id = TheForgotten, DisplayName = "The Forgotten · Forgotten City", RealmName = "Forgotten City", WorldAbbr = "TF", CanvasColor = "#0C0C22", AccentColor = "#FF6FCF" },
    };

    public static bool TryGet(string id, out Info info)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                info = candidate;
                return true;
            }
        }
        info = null;
        return false;
    }

    /// <summary>The PRIMARY tile set of the world with this abbreviation (case-insensitive) —
    /// the first one listed for it — or null. A world can have further sets
    /// (<see cref="AllForWorldAbbr"/>); to ask which one a map belongs to, use
    /// <see cref="MapRealm.Of"/>.</summary>
    public static string ForWorldAbbr(string worldAbbr)
    {
        List<Info> sets = AllForWorldAbbr(worldAbbr);
        return sets.Count > 0 ? sets[0].Id : null;
    }

    /// <summary>Every tile set of the world with this abbreviation (case-insensitive), in
    /// palette order: its primary realm first, then its districts. Empty when none.</summary>
    public static List<Info> AllForWorldAbbr(string worldAbbr)
    {
        var sets = new List<Info>();
        if (string.IsNullOrWhiteSpace(worldAbbr)) return sets;
        string wanted = worldAbbr.Trim();
        foreach (var candidate in All)
        {
            if (candidate.WorldAbbr != null &&
                string.Equals(candidate.WorldAbbr, wanted, StringComparison.OrdinalIgnoreCase))
                sets.Add(candidate);
        }
        return sets;
    }

    /// <summary>A short prefix that tells this set's palette groups from every other set's:
    /// the world abbreviation ("SL") for a world's primary realm, the realm's own name
    /// ("Cloud Court") for a district — two sets of one world must not both read "PL".</summary>
    public static string GroupPrefix(Info info)
    {
        if (info == null || string.IsNullOrEmpty(info.WorldAbbr)) return null;
        return ForWorldAbbr(info.WorldAbbr) == info.Id ? info.WorldAbbr : info.RealmName;
    }
}

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
        // The Palace of LOOING is under the sea (v0.10.9, the design owner's direction). Cloud
        // Court — the earlier, aerial proposal for it — keeps its kit and map in the editor
        // under a world tag of its own ("CC", not in the overworld pool) until it is given a
        // world or retired.
        new() { Id = LoongsNest, DisplayName = "Palace of Loong · Loong's Nest", RealmName = "Loong's Nest", WorldAbbr = "PL", CanvasColor = "#0C1230" },
        new() { Id = PalaceOfLoong, DisplayName = "Cloud Court (unassigned)", RealmName = "Cloud Court", WorldAbbr = "CC", CanvasColor = "#2A1B4A", AccentColor = "#D9A441" },
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

    /// <summary>The tile set whose world abbreviation matches <paramref name="worldAbbr"/>
    /// (case-insensitive), or null. Used to open the palette on the realm a map is tagged for.</summary>
    public static string ForWorldAbbr(string worldAbbr)
    {
        if (string.IsNullOrWhiteSpace(worldAbbr)) return null;
        string wanted = worldAbbr.Trim();
        foreach (var candidate in All)
        {
            if (candidate.WorldAbbr != null &&
                string.Equals(candidate.WorldAbbr, wanted, StringComparison.OrdinalIgnoreCase))
                return candidate.Id;
        }
        return null;
    }
}

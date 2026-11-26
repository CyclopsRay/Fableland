namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// Which realm a map belongs to. That used to be a lookup — one tile set per world — until
/// the Palace of LOOING got a second one (Cloud Court, its sky district): both realms' maps
/// are tagged <c>PL</c>, and only what a map is built from tells them apart. The map browser's
/// chip and badge and the editor's palette ask here; nobody maps a world to "its" set by
/// hand any more. Pure C# — no Godot.
/// </summary>
public static class MapRealm
{
    /// <summary>
    /// The tile set of the realm this map belongs to, given a world it is tagged for (an
    /// abbreviation, any case) — or null when that world has no set. A world with one set
    /// has one answer. Where it has several: the set most of the map's tiles come from; for
    /// a map with none of their tiles yet, the set whose canvas colour the map still has
    /// (what the browser's Create dialog gave it); failing both, the world's primary realm.
    /// </summary>
    public static string Of(MapDocument doc, string worldAbbr)
    {
        List<TileSets.Info> sets = TileSets.AllForWorldAbbr(worldAbbr);
        if (sets.Count == 0) return null;
        if (sets.Count == 1 || doc == null) return sets[0].Id;

        Dictionary<string, int> used = TilesPerSet(doc);
        string best = null;
        int bestCount = 0;
        foreach (TileSets.Info set in sets)          // palette order: a tie goes to the primary realm
        {
            int count = used.GetValueOrDefault(set.Id);
            if (count > bestCount) { best = set.Id; bestCount = count; }
        }
        if (best != null) return best;

        string canvas = doc.Canvas?.Color?.Trim();
        if (!string.IsNullOrEmpty(canvas))
        {
            foreach (TileSets.Info set in sets)
                if (string.Equals(set.CanvasColor, canvas, StringComparison.OrdinalIgnoreCase)) return set.Id;
        }
        return sets[0].Id;
    }

    /// <summary>The realm set a map is built from — the one most of its placed tiles belong
    /// to, shared (<see cref="TileSets.Core"/>) tiles aside — or null while it has no realm
    /// tile. A tie goes to palette order.</summary>
    public static string BuiltFrom(MapDocument doc)
    {
        Dictionary<string, int> used = TilesPerSet(doc);
        string best = null;
        int bestCount = 0;
        foreach (TileSets.Info set in TileSets.All)
        {
            int count = used.GetValueOrDefault(set.Id);
            if (count > bestCount) { best = set.Id; bestCount = count; }
        }
        return best;
    }

    private static Dictionary<string, int> TilesPerSet(MapDocument doc)
    {
        var used = new Dictionary<string, int>();
        if (doc?.Layers == null) return used;
        foreach (MapLayerData layer in doc.Layers)
        {
            if (layer?.Tiles == null) continue;
            foreach (PlacedTile tile in layer.Tiles)
            {
                if (tile == null || !TileRegistry.TryGet(tile.DefId, out TileDef def) || def.TileSet == TileSets.Core) continue;
                used[def.TileSet] = used.GetValueOrDefault(def.TileSet) + 1;
            }
        }
        return used;
    }

    /// <summary>Guards the one world that has two realms, and the table that says so: every
    /// set's world has a primary realm, and no two sets share a palette prefix.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("MapRealm mismatch: " + what); }

        static MapDocument Map(string canvas, params string[] tiles)
        {
            MapDocument doc = MapDocument.CreateNew("Realm probe");
            doc.Worlds = new List<string> { "PL" };
            doc.Canvas.Color = canvas;
            MapLayerData field = MapDocumentQueries.Battlefield(doc);
            for (int i = 0; i < tiles.Length; i++) field.Tiles.Add(new PlacedTile { DefId = tiles[i], X = i, Y = 0 });
            return doc;
        }

        const string nest = TileSets.LoongsNest, court = TileSets.PalaceOfLoong;
        string courtCanvas = TileSets.TryGet(court, out TileSets.Info courtInfo) ? courtInfo.CanvasColor : "";

        Check(TileSets.ForWorldAbbr("PL") == nest, "the Palace's primary realm is Loong's Nest");
        Check(TileSets.AllForWorldAbbr("pl").Count == 2, "the Palace of LOOING has two realm sets, Loong's Nest and Cloud Court");
        Check(Of(Map("#000000", "ground.pl_terrace", "softvolume.pl_cloud", "ground.ln_reef"), "PL") == court,
            "a Palace map built mostly of Cloud Court tiles is a Cloud Court map");
        Check(Of(Map(courtCanvas, "ground.ln_reef", "ground.ln_palace", "ground.pl_terrace"), "pl") == nest,
            "a Palace map built mostly of Loong's Nest tiles is a Loong's Nest map, whatever its canvas");
        Check(Of(Map(courtCanvas.ToLowerInvariant()), "PL") == court, "an empty Palace map on Cloud Court's canvas is a Cloud Court map");
        Check(Of(Map("#123456"), "PL") == nest, "an empty Palace map on any other canvas belongs to the primary realm");
        Check(Of(Map("#123456", "spawn.character", "goal.level"), "PL") == nest, "shared marker tiles do not name a realm");
        Check(Of(Map(courtCanvas, "ground.pl_terrace"), "BM") == TileSets.BambooMaze, "a world with one set has one answer");
        Check(Of(Map(courtCanvas), "ZZ") == null, "a world with no set has no realm");
        Check(Of(null, "PL") == nest, "no map: the world's primary realm");
        Check(BuiltFrom(Map("#000000", "ground.pl_terrace", "spawn.character")) == court, "BuiltFrom names the set a map's tiles come from");
        Check(BuiltFrom(Map("#000000", "spawn.character")) == null, "BuiltFrom is null for a map of shared tiles only");

        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TileSets.Info info in TileSets.All)
        {
            if (info.Id == TileSets.Core) continue;
            Check(TileSets.ForWorldAbbr(info.WorldAbbr) != null, $"set '{info.Id}' names no world");
            Check(prefixes.Add(TileSets.GroupPrefix(info) ?? ""), $"set '{info.Id}' shares its palette prefix '{TileSets.GroupPrefix(info)}' with another set");
        }
        return failures;
    }
}

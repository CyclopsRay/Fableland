namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// GDD §8, §11.2 — versioned (de)serialization and atomic save. Pure C#: callers
/// (the Editor/presentation layer) pass ABSOLUTE filesystem paths, globalizing
/// `user://maps/...` before calling in — this class never touches Godot.
/// </summary>
public static class MapJson
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Atomic write: serialize to `absPath + ".tmp"` in the same directory, then
    /// rename over the real path (GDD §8 / T30 §6). IO exceptions PROPAGATE — this is
    /// pure Data-layer code with no Godot logging; every Editor-layer caller wraps the
    /// call in try/catch and surfaces the failure via GD.PushError (F-MC4).</summary>
    public static void Save(MapDocument doc, string absPath)
    {
        doc.ModifiedUtc = DateTime.UtcNow.ToString("o");
        string json = JsonSerializer.Serialize(doc, WriteOptions);

        string dir = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tmpPath = absPath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, absPath, overwrite: true);
    }

    /// <summary>Load + post-load validation. Never throws: missing/unreadable/corrupt files
    /// return null with an explanatory warning so the caller can degrade gracefully
    /// (GDD §8: "degrade to an empty list + warning, never a crash").</summary>
    public static MapDocument Load(string absPath, out List<string> warnings)
    {
        warnings = new List<string>();

        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
        {
            warnings.Add($"map file not found: '{absPath}'");
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(absPath);
        }
        catch (Exception e)
        {
            warnings.Add($"could not read map file '{absPath}': {e.Message}");
            return null;
        }

        MapDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize<MapDocument>(json);
        }
        catch (Exception e)
        {
            warnings.Add($"corrupt map file '{absPath}': {e.Message}");
            return null;
        }

        if (doc == null)
        {
            warnings.Add($"map file '{absPath}' parsed to null");
            return null;
        }

        // F-MC5 (review): a syntactically-valid JSON object with unrelated keys
        // deserializes into a mostly-default MapDocument (STJ is lenient), which would
        // surface as a spurious "Untitled" card. The GUID Id is the one field every real
        // map has from creation (GDD §7.8 — identity is minted, never derived), so its
        // absence means "not a map file": skip it like a corrupt file.
        if (string.IsNullOrEmpty(doc.Id))
        {
            warnings.Add($"'{absPath}' is not a map file (no id); skipped");
            return null;
        }

        if (doc.Version > MapDocument.CurrentVersion)
            warnings.Add($"map file '{absPath}' has unknown version {doc.Version}; best-effort parse as v{MapDocument.CurrentVersion}");

        Validate(doc, warnings);
        return doc;
    }

    /// <summary>The browser only needs meta fields, but files are small at our scale — just
    /// reuse the full loader (GDD §8: "no `_index.json`").</summary>
    public static MapDocument LoadMetaOnly(string absPath, out List<string> warnings) => Load(absPath, out warnings);

    /// <summary>Post-load validation/repair: null Layers -> empty; no battlefield layer ->
    /// inject a default one; unknown tile ids and out-of-grid tiles are skipped; grid sizes
    /// clamped to spec bounds. Every correction is reported via `warnings`, never thrown.</summary>
    private static void Validate(MapDocument doc, List<string> warnings)
    {
        // v1 stored only World. Preserve its meaning when loading into v2's additive world
        // filter, then normalize every new selection field so runtime code has one contract.
        doc.Worlds ??= new List<string>();
        if (doc.Worlds.Count == 0 && !string.IsNullOrWhiteSpace(doc.World))
            doc.Worlds.Add(doc.World.Trim());
        doc.HardshipLevels ??= new List<int>();
        doc.HardshipLevels.RemoveAll(level => level < 1 || level > 6);
        doc.Goals ??= new List<string>();
        if (doc.Goals.Count == 0) doc.Goals.Add(CombatMapGoals.Claim);
        for (int i = doc.Goals.Count - 1; i >= 0; i--)
        {
            string goal = doc.Goals[i]?.Trim().ToLowerInvariant();
            if (goal == "collection") goal = CombatMapGoals.Claim; // friendly legacy spelling
            if (goal != CombatMapGoals.Claim && goal != CombatMapGoals.Protect &&
                goal != CombatMapGoals.Destroy && goal != CombatMapGoals.Slaughter)
            {
                warnings.Add($"unknown combat-map goal '{doc.Goals[i]}' removed");
                doc.Goals.RemoveAt(i);
            }
            else doc.Goals[i] = goal;
        }
        if (doc.Goals.Count == 0) doc.Goals.Add(CombatMapGoals.Claim);

        doc.Terrain = string.IsNullOrWhiteSpace(doc.Terrain)
            ? CombatMapTerrain.SeaLevel : doc.Terrain.Trim().ToLowerInvariant();
        // v0.7.0 accepted the early short forms. Preserve existing authored maps while every
        // newly-saved document uses the clearer terrain labels shared with the overworld.
        if (doc.Terrain == "high") doc.Terrain = CombatMapTerrain.High;
        if (doc.Terrain == "lowground") doc.Terrain = CombatMapTerrain.Lowground;
        if (doc.Terrain != "*" && doc.Terrain != CombatMapTerrain.SeaLevel &&
            doc.Terrain != CombatMapTerrain.High && doc.Terrain != CombatMapTerrain.Lowground)
        {
            warnings.Add($"unknown combat-map terrain '{doc.Terrain}'; using {CombatMapTerrain.SeaLevel}");
            doc.Terrain = CombatMapTerrain.SeaLevel;
        }

        doc.FoeCompositions ??= new List<FoeComposition>();
        doc.FoeCompositions.RemoveAll(c => c == null || c.Level < 0 || c.Level > 6 ||
            c.CrabWeight < 0 || c.SeagullWeight < 0 || c.CrabWeight + c.SeagullWeight <= 0);
        doc.FoeSpawnRules ??= new FoeSpawnRules();
        doc.Version = MapDocument.CurrentVersion;

        doc.Layers ??= new List<MapLayerData>();

        bool hasBattlefield = false;
        foreach (var layer in doc.Layers)
        {
            if (layer != null && layer.Role == MapLayerData.RoleBattlefield) hasBattlefield = true;
        }
        if (!hasBattlefield)
        {
            doc.Layers.Add(MapLayerData.CreateBattlefield());
            warnings.Add("map had no battlefield layer; injected a default 64x36 battlefield");
        }

        foreach (var layer in doc.Layers)
        {
            if (layer == null) continue;

            layer.Tiles ??= new List<PlacedTile>();
            layer.GridW = Math.Clamp(layer.GridW, 1, 512);
            layer.GridH = Math.Clamp(layer.GridH, 1, 256);

            var kept = new List<PlacedTile>(layer.Tiles.Count);
            foreach (var tile in layer.Tiles)
            {
                if (tile == null) continue;

                if (!TileRegistry.TryGet(tile.DefId, out var def))
                {
                    warnings.Add($"unknown tile id '{tile.DefId}' skipped");
                    continue;
                }

                if (tile.X < 0 || tile.Y < 0 ||
                    tile.X + def.FootprintW > layer.GridW ||
                    tile.Y + def.FootprintH > layer.GridH)
                {
                    warnings.Add($"tile '{tile.DefId}' at ({tile.X},{tile.Y}) is outside its layer grid; skipped");
                    continue;
                }

                kept.Add(tile);
            }
            layer.Tiles = kept;
        }
    }

    /// <summary>GDD §11.2 — the structural guard against the STJ-"public fields are
    /// ignored" bug that made every v0.5.x save `{}`. Builds a representative document
    /// (2+ layers, a multi-cell tile, a painted rule zone, per-tile Props, a non-default
    /// canvas color, World set), saves it, reloads it, and deep-compares every property.
    /// Returns an empty list on success. A later phase runs this from the browser in
    /// debug builds.</summary>
    public static List<string> RoundTripSelfTest()
    {
        var failures = new List<string>();

        var doc = MapDocument.CreateNew("Self Test Map");
        doc.Worlds.Add("TestWorld");
        doc.HardshipLevels.Add(2);
        doc.Goals = new List<string> { CombatMapGoals.Destroy };
        doc.Terrain = CombatMapTerrain.High;
        doc.FoeCompositions.Add(new FoeComposition { Level = 2, CrabWeight = 50, SeagullWeight = 50 });
        doc.FoeSpawnRules = new FoeSpawnRules { CrabMaxCellY = 4, SeagullMinCellY = 5 };
        doc.Canvas.Color = "#123456";

        var farview = MapLayerData.CreateFarview("Sky");
        farview.GridW = 40;
        farview.GridH = 20;
        farview.Tiles.Add(new PlacedTile { DefId = "deco.flower", X = 2, Y = 3 });

        // CreateNew now seeds a full layer stack; find the distinguished battlefield by role
        // rather than assuming index 0.
        MapLayerData battlefield = null;
        foreach (var l in doc.Layers)
            if (l.Role == MapLayerData.RoleBattlefield) { battlefield = l; break; }
        battlefield.GridW = 64;
        battlefield.GridH = 36;
        battlefield.Tiles.Add(new PlacedTile { DefId = "softvolume.cloud2x1", X = 5, Y = 5 });
        battlefield.Tiles.Add(new PlacedTile
        {
            DefId = "spawn.enemy",
            X = 10,
            Y = 10,
            Props = new Dictionary<string, string> { ["foeTable"] = "crabPatrol" },
        });
        battlefield.Tiles.Add(new PlacedTile { DefId = "rule.cloudZone", X = 20, Y = 5 });
        battlefield.Tiles.Add(new PlacedTile { DefId = "rule.cloudZone", X = 21, Y = 5 });

        var closeview = MapLayerData.CreateCloseview("Fog");

        doc.Layers.Insert(0, farview);
        doc.Layers.Add(closeview);

        string tmpPath = Path.Combine(Path.GetTempPath(), "fableland_maptest_" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            Save(doc, tmpPath);
            var loaded = Load(tmpPath, out var loadWarnings);

            if (loaded == null)
            {
                failures.Add("round-trip: Load returned null");
                return failures;
            }
            foreach (var w in loadWarnings) failures.Add("unexpected load warning: " + w);

            CompareDoc(doc, loaded, failures);
        }
        catch (Exception e)
        {
            failures.Add("round-trip threw: " + e.Message);
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
        }

        return failures;
    }

    private static void CompareDoc(MapDocument a, MapDocument b, List<string> failures)
    {
        void Check(bool ok, string what) { if (!ok) failures.Add("mismatch: " + what); }

        Check(a.Version == b.Version, "Version");
        Check(a.Id == b.Id, "Id");
        Check(a.Name == b.Name, "Name");
        Check(a.World == b.World, "World");
        Check(a.Worlds.Count == b.Worlds.Count, "Worlds.Count");
        Check(a.HardshipLevels.Count == b.HardshipLevels.Count, "HardshipLevels.Count");
        Check(a.Goals.Count == b.Goals.Count, "Goals.Count");
        Check(a.Terrain == b.Terrain, "Terrain");
        Check(a.FoeCompositions.Count == b.FoeCompositions.Count, "FoeCompositions.Count");
        int cn = Math.Min(a.FoeCompositions.Count, b.FoeCompositions.Count);
        for (int i = 0; i < cn; i++)
        {
            Check(a.FoeCompositions[i].Level == b.FoeCompositions[i].Level, $"FoeCompositions[{i}].Level");
            Check(a.FoeCompositions[i].CrabWeight == b.FoeCompositions[i].CrabWeight, $"FoeCompositions[{i}].CrabWeight");
            Check(a.FoeCompositions[i].SeagullWeight == b.FoeCompositions[i].SeagullWeight, $"FoeCompositions[{i}].SeagullWeight");
        }
        Check(a.FoeSpawnRules?.CrabMaxCellY == b.FoeSpawnRules?.CrabMaxCellY, "FoeSpawnRules.CrabMaxCellY");
        Check(a.FoeSpawnRules?.SeagullMinCellY == b.FoeSpawnRules?.SeagullMinCellY, "FoeSpawnRules.SeagullMinCellY");
        Check(a.CreatedUtc == b.CreatedUtc, "CreatedUtc");
        Check(a.ModifiedUtc == b.ModifiedUtc, "ModifiedUtc");
        Check(a.Canvas.Type == b.Canvas.Type, "Canvas.Type");
        Check(a.Canvas.Color == b.Canvas.Color, "Canvas.Color");
        Check(a.Layers.Count == b.Layers.Count, "Layers.Count");

        int n = Math.Min(a.Layers.Count, b.Layers.Count);
        for (int i = 0; i < n; i++)
        {
            var la = a.Layers[i];
            var lb = b.Layers[i];
            string tag = $"Layers[{i}]";

            Check(la.Role == lb.Role, tag + ".Role");
            Check(la.Name == lb.Name, tag + ".Name");
            Check(la.ParallaxX == lb.ParallaxX, tag + ".ParallaxX");
            Check(la.ParallaxY == lb.ParallaxY, tag + ".ParallaxY");
            Check(la.Loop == lb.Loop, tag + ".Loop");
            Check(la.Collision == lb.Collision, tag + ".Collision");
            Check(la.Tint == lb.Tint, tag + ".Tint");
            Check(la.Opacity == lb.Opacity, tag + ".Opacity");
            Check(la.SwayAmplitudePx == lb.SwayAmplitudePx, tag + ".SwayAmplitudePx");
            Check(la.SwayPeriodSec == lb.SwayPeriodSec, tag + ".SwayPeriodSec");
            Check(la.AutoScrollX == lb.AutoScrollX, tag + ".AutoScrollX");
            Check(la.AutoScrollY == lb.AutoScrollY, tag + ".AutoScrollY");
            Check(la.GridW == lb.GridW, tag + ".GridW");
            Check(la.GridH == lb.GridH, tag + ".GridH");
            Check(la.Tiles.Count == lb.Tiles.Count, tag + ".Tiles.Count");

            int tn = Math.Min(la.Tiles.Count, lb.Tiles.Count);
            for (int t = 0; t < tn; t++)
            {
                var ta = la.Tiles[t];
                var tb = lb.Tiles[t];
                string ttag = $"{tag}.Tiles[{t}]";

                Check(ta.DefId == tb.DefId, ttag + ".DefId");
                Check(ta.X == tb.X, ttag + ".X");
                Check(ta.Y == tb.Y, ttag + ".Y");
                Check(ta.FlipX == tb.FlipX, ttag + ".FlipX");

                bool aHasProps = ta.Props is { Count: > 0 };
                bool bHasProps = tb.Props is { Count: > 0 };
                Check(aHasProps == bHasProps, ttag + ".Props presence");

                if (aHasProps && bHasProps)
                {
                    Check(ta.Props.Count == tb.Props.Count, ttag + ".Props.Count");
                    foreach (var kv in ta.Props)
                    {
                        if (!tb.Props.TryGetValue(kv.Key, out var v) || v != kv.Value)
                            failures.Add($"mismatch: {ttag}.Props[{kv.Key}]");
                    }
                }
            }
        }
    }
}

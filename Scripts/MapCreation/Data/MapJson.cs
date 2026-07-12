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
    /// rename over the real path (GDD §8 / T30 §6).</summary>
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

        if (doc.Version != 1)
            warnings.Add($"map file '{absPath}' has unknown version {doc.Version}; best-effort parse as v1");

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
        doc.World = "TestWorld";
        doc.Canvas.Color = "#123456";

        var farview = MapLayerData.CreateFarview("Sky");
        farview.GridW = 40;
        farview.GridH = 20;
        farview.Tiles.Add(new PlacedTile { DefId = "deco.flower", X = 2, Y = 3 });

        var battlefield = doc.Layers[0]; // CreateNew seeds exactly one default battlefield
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

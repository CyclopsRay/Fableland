namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// GDD §2.5 extension — reads `*.tile.json` manifests (see <see cref="TileManifest"/>) and
/// maps them into <see cref="TileDef"/>s. Pure C#, no Godot: callers pass absolute
/// filesystem paths, same convention as `MapJson`.
///
/// This is a MAPPING step only — it does not register anything into `TileRegistry` itself
/// (T00 rule 1 keeps tile-kind registration a single hand-edited list, never a switch or an
/// implicit scan-on-boot). A caller that wants a loaded def live in the palette still adds
/// it to `TileRegistry._order` explicitly; this class just removes the hand-transcription
/// step between "AI generated an asset" and "here is its TileDef".
/// </summary>
public static class TileManifestLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Parses one manifest file. Throws on missing file / invalid JSON — callers
    /// that want graceful degradation (e.g. a directory scan) should catch per-file.</summary>
    public static TileManifest LoadManifest(string jsonPath)
    {
        string json = File.ReadAllText(jsonPath);
        var manifest = JsonSerializer.Deserialize<TileManifest>(json, Options);
        if (manifest == null)
            throw new InvalidDataException($"'{jsonPath}' did not parse as a tile manifest.");
        return manifest;
    }

    /// <summary>Maps a parsed manifest to a `TileDef`. `manifestPath` is only used to derive
    /// the sprite path by convention when the manifest omits `sprite`.</summary>
    public static TileDef ToTileDef(TileManifest manifest, string manifestPath)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidDataException($"'{manifestPath}': manifest has no id.");
        if (!Enum.TryParse<TileCategory>(manifest.Role, ignoreCase: true, out var category))
            throw new InvalidDataException(
                $"'{manifest.Id}': unknown role '{manifest.Role}' (expected a TileCategory name, e.g. Ground/Platform/SoftVolume/Hazard/Decoration).");

        string spriteSlot = string.IsNullOrWhiteSpace(manifest.Sprite)
            ? DeriveSpritePath(manifestPath)
            : manifest.Sprite;

        EdgeRule edges = manifest.Edges == null ? null : new EdgeRule
        {
            Top = manifest.Edges.Top,
            Right = manifest.Edges.Right,
            Bottom = manifest.Edges.Bottom,
            Left = manifest.Edges.Left,
        };

        Dictionary<string, string> props = null;
        if (!string.IsNullOrWhiteSpace(manifest.ArtSource) || !string.IsNullOrWhiteSpace(manifest.Prompt))
        {
            props = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(manifest.ArtSource)) props["artSource"] = manifest.ArtSource;
            if (!string.IsNullOrWhiteSpace(manifest.Prompt)) props["prompt"] = manifest.Prompt;
        }

        return new TileDef
        {
            Id = manifest.Id,
            DisplayName = string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName,
            Category = category,
            FootprintW = Math.Max(1, manifest.Footprint?.W ?? 1),
            FootprintH = Math.Max(1, manifest.Footprint?.H ?? 1),
            SpriteSlot = spriteSlot,
            SpriteFillFootprint = manifest.FillFootprint,
            AutotileGroup = manifest.AutotileGroup,
            Edges = edges,
            Props = props,
        };
    }

    /// <summary>Loads every `*.tile.json` in `dir` (non-recursive) into `TileDef`s, sorted by
    /// filename for determinism. Throws on a duplicate id within this directory scan —
    /// cross-source duplicates (e.g. against existing `TileRegistry` entries) are still the
    /// caller's responsibility to check before merging.</summary>
    public static List<TileDef> LoadDirectory(string dir)
    {
        var results = new List<TileDef>();
        var seen = new HashSet<string>();

        foreach (var path in Directory.EnumerateFiles(dir, "*.tile.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var manifest = LoadManifest(path);
            var def = ToTileDef(manifest, path);
            if (!seen.Add(def.Id))
                throw new InvalidDataException($"duplicate tile id '{def.Id}' while loading manifests in '{dir}'.");
            results.Add(def);
        }

        return results;
    }

    /// <summary>Sidecar convention: `foo.tile.json` next to `foo.png` in the same directory,
    /// exposed as a `res://` path when the manifest lives under this repo's `Sprites/` tree.</summary>
    private static string DeriveSpritePath(string manifestPath)
    {
        string fileName = Path.GetFileName(manifestPath);
        const string suffix = ".tile.json";
        string baseName = fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        string dir = Path.GetDirectoryName(manifestPath) ?? "";
        string pngPath = Path.Combine(dir, baseName + ".png").Replace('\\', '/');

        int idx = pngPath.IndexOf("Sprites/", StringComparison.Ordinal);
        return idx >= 0 ? "res://" + pngPath[idx..] : pngPath;
    }

    /// <summary>Structural guard (mirrors `MapJson.RoundTripSelfTest`): writes a representative
    /// manifest to a temp file, loads it back through the real file-reading path, and checks
    /// every `TileDef` field the mapping is supposed to set. Returns an empty list on success.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        string tmpPath = Path.Combine(Path.GetTempPath(), "fableland_tiletest_" + Guid.NewGuid().ToString("N") + ".tile.json");

        var manifest = new TileManifest
        {
            Id = "ground.selftest",
            DisplayName = "Self Test Ground",
            Role = "Ground",
            Footprint = new TileManifestFootprint { W = 1, H = 1 },
            FillFootprint = true,
            AutotileGroup = "terrain.selftest",
            Edges = new TileManifestEdges { Top = "open-air-ok", Right = "x", Bottom = "x", Left = "x" },
            ArtSource = "res://Sprites/MapCreation/Beach/Generated/terrain_beach_atlas.png",
            Prompt = "test prompt",
        };

        void Check(bool ok, string what) { if (!ok) failures.Add("mismatch: " + what); }

        try
        {
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            var loadedManifest = LoadManifest(tmpPath);
            var def = ToTileDef(loadedManifest, tmpPath);

            Check(def.Id == "ground.selftest", "Id");
            Check(def.DisplayName == "Self Test Ground", "DisplayName");
            Check(def.Category == TileCategory.Ground, "Category");
            Check(def.FootprintW == 1 && def.FootprintH == 1, "Footprint");
            Check(def.SpriteFillFootprint, "SpriteFillFootprint");
            Check(def.AutotileGroup == "terrain.selftest", "AutotileGroup");
            // The temp file lives outside Sprites/, so DeriveSpritePath can't produce a
            // res:// path here (that prefixing is only checked by SelfTestFixtures, which
            // loads the real on-disk manifests) — just confirm the sidecar-basename
            // convention picked the right file.
            string expectedPngName = Path.GetFileName(tmpPath).Replace(".tile.json", ".png");
            Check(def.SpriteSlot.EndsWith(expectedPngName, StringComparison.Ordinal), "SpriteSlot basename");
            Check(def.Edges != null && def.Edges.Top == "open-air-ok" && def.Edges.Right == "x"
                && def.Edges.Bottom == "x" && def.Edges.Left == "x", "Edges");
            Check(def.Props != null && def.Props["artSource"] == manifest.ArtSource, "Props.artSource");
            Check(def.Props != null && def.Props["prompt"] == "test prompt", "Props.prompt");
        }
        catch (Exception e)
        {
            failures.Add("self-test threw: " + e.Message);
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort cleanup */ }
        }

        return failures;
    }

    /// <summary>Loads the beach sand/grass manifests shipped alongside this loader and checks
    /// they parse into the same `TileDef`s `TileRegistry` currently hand-types for
    /// `ground.sand`/`ground.grass` — a smoke test that the checked-in `.tile.json` files are
    /// well-formed, and a template for validating future manifests the same way. `projectRoot`
    /// is the repo root (this class is pure C#/no Godot, so it can't resolve `res://` itself).</summary>
    public static List<string> SelfTestFixtures(string projectRoot)
    {
        var failures = new List<string>();
        string dir = Path.Combine(projectRoot, "Sprites", "MapCreation", "Beach", "Generated");

        void CheckDef(string fileName, string expectId, string expectAutotileGroup)
        {
            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
            {
                failures.Add($"fixture missing: '{path}'");
                return;
            }

            try
            {
                var manifest = LoadManifest(path);
                var def = ToTileDef(manifest, path);

                if (def.Id != expectId) failures.Add($"{fileName}: mismatch Id (got '{def.Id}', want '{expectId}')");
                if (def.Category != TileCategory.Ground) failures.Add($"{fileName}: mismatch Category (got {def.Category})");
                if (!def.SpriteFillFootprint) failures.Add($"{fileName}: mismatch SpriteFillFootprint (want true)");
                if (def.AutotileGroup != expectAutotileGroup)
                    failures.Add($"{fileName}: mismatch AutotileGroup (got '{def.AutotileGroup}', want '{expectAutotileGroup}')");
                if (def.SpriteSlot == null || !def.SpriteSlot.StartsWith("res://Sprites/", StringComparison.Ordinal))
                    failures.Add($"{fileName}: SpriteSlot did not resolve to a res:// path (got '{def.SpriteSlot}')");
                if (def.Edges == null || string.IsNullOrEmpty(def.Edges.Top))
                    failures.Add($"{fileName}: Edges.Top not set");
                if (def.Props == null || !def.Props.ContainsKey("prompt"))
                    failures.Add($"{fileName}: Props['prompt'] not set");
            }
            catch (Exception e)
            {
                failures.Add($"{fileName}: threw {e.Message}");
            }
        }

        CheckDef("ground_sand_seamless.tile.json", "ground.sand", "terrain.beach_sand");
        CheckDef("ground_grass_seamless.tile.json", "ground.grass", "terrain.coastal_grass");

        return failures;
    }
}

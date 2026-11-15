namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Boot-time content audit for the tile registry against the art on disk (T10 §5 / T00
/// rule 4: validate data tables loudly in debug, report every violation, never throw).
///
/// A realm tile set has three things that must agree and no compiler to make them: the
/// `TileRegistry` entry (what the game uses), the `.tile.json` sidecar next to the sprite
/// (what the art pipeline and `Tools/render_map_preview.py` read), and the PNG itself
/// (whose canvas must be the footprint, see below). Painting a sprite at a new size, or
/// retyping a footprint in one place only, is otherwise silent until someone notices a
/// tree floating or a roof you cannot stand on. Pure C#: the caller passes the repo root.
/// </summary>
public static class TileSetAudit
{
    private const string ResPrefix = "res://";

    public static List<string> Run(string projectRoot)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            problems.Add($"tile audit: project root '{projectRoot}' does not exist");
            return problems;
        }

        var seenInSidecar = new HashSet<string>(StringComparer.Ordinal);
        CheckSidecars(projectRoot, problems, seenInSidecar);

        foreach (TileDef def in TileRegistry.All)
        {
            CheckFile(projectRoot, def, def.SpriteSlot, "SpriteSlot", problems);
            string atlas = Prop(def, TileProps.ArtSource);
            CheckFile(projectRoot, def, atlas, "Props[artSource]", problems);

            if (!IsPaintedSet(def.TileSet)) continue;

            if (!seenInSidecar.Contains(def.Id) && !string.IsNullOrWhiteSpace(def.SpriteSlot))
                problems.Add($"{def.Id}: registered with art but has no .tile.json sidecar under Assets/Sprites/Tiles/");
            CheckCanvas(projectRoot, def, atlas, problems);
        }

        return problems;
    }

    /// <summary>Realm sets painted by `Tools/realm_art/` follow the pinned-canvas contract.
    /// The beach set predates it (AI-generated sprites with free padding) and is exempt.</summary>
    private static bool IsPaintedSet(string tileSet) =>
        tileSet != TileSets.Core && tileSet != TileSets.VanillaKingdom;

    private static void CheckSidecars(string projectRoot, List<string> problems, HashSet<string> seen)
    {
        string tilesRoot = Path.Combine(projectRoot, "Assets", "Sprites", "Tiles");
        if (!Directory.Exists(tilesRoot))
        {
            problems.Add($"tile audit: '{tilesRoot}' does not exist");
            return;
        }

        IEnumerable<string> sidecars;
        try
        {
            sidecars = Directory.EnumerateFiles(tilesRoot, "*.tile.json", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
        }
        catch (Exception e)
        {
            problems.Add("tile audit: could not list sidecars: " + e.Message);
            return;
        }

        foreach (string path in sidecars)
        {
            string label = Path.GetFileName(path);
            TileDef fromFile;
            try
            {
                fromFile = TileManifestLoader.ToTileDef(TileManifestLoader.LoadManifest(path), path);
            }
            catch (Exception e)
            {
                problems.Add($"{label}: unreadable sidecar ({e.Message})");
                continue;
            }

            if (!seen.Add(fromFile.Id))
            {
                problems.Add($"{label}: a second sidecar declares id '{fromFile.Id}'");
                continue;
            }
            if (!TileRegistry.TryGet(fromFile.Id, out TileDef def))
            {
                problems.Add($"{label}: sidecar id '{fromFile.Id}' is not in TileRegistry (sprite exists but is not placeable)");
                continue;
            }

            void Differs(string what, object file, object registry) =>
                problems.Add($"{def.Id}: sidecar {what} '{file}' != registry '{registry}'");

            if (fromFile.Category != def.Category) Differs("role", fromFile.Category, def.Category);
            if (fromFile.FootprintW != def.FootprintW || fromFile.FootprintH != def.FootprintH)
                Differs("footprint", fromFile.FootprintW + "x" + fromFile.FootprintH, def.FootprintW + "x" + def.FootprintH);
            if (fromFile.SpriteFillFootprint != def.SpriteFillFootprint)
                Differs("fillFootprint", fromFile.SpriteFillFootprint, def.SpriteFillFootprint);
            if (!string.Equals(fromFile.SpriteSlot, def.SpriteSlot, StringComparison.Ordinal))
                Differs("sprite", fromFile.SpriteSlot, def.SpriteSlot);
            if (!string.Equals(fromFile.AutotileGroup ?? "", def.AutotileGroup ?? "", StringComparison.Ordinal))
                Differs("autotileGroup", fromFile.AutotileGroup, def.AutotileGroup);
            if (!string.Equals(fromFile.TileSet, def.TileSet, StringComparison.Ordinal))
                Differs("tileSet", fromFile.TileSet, def.TileSet);
            foreach (string key in new[] { TileProps.ArtSource, TileProps.AutotileKind, TileProps.AutotileVariants })
            {
                string a = Prop(fromFile, key) ?? "", b = Prop(def, key) ?? "";
                if (!string.Equals(a, b, StringComparison.Ordinal)) Differs(key, a, b);
            }
        }
    }

    private static void CheckFile(string projectRoot, TileDef def, string resPath, string what, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(resPath)) return;
        string abs = Absolute(projectRoot, resPath);
        if (abs == null) problems.Add($"{def.Id}: {what} '{resPath}' is not a res:// path");
        else if (!File.Exists(abs)) problems.Add($"{def.Id}: {what} '{resPath}' does not exist");
    }

    /// <summary>
    /// The pinned-canvas contract (`Tools/realm_art/common.py`, `pin_corners`): a painted
    /// prop's canvas is exactly its footprint, so the sprite lands where it was painted in
    /// both the editor (which trims to the used rect) and in play. A strip atlas is 4 rows
    /// of square cells, `variants x 4` columns wide.
    /// </summary>
    private static void CheckCanvas(string projectRoot, TileDef def, string atlas, List<string> problems)
    {
        bool isStrip = Prop(def, TileProps.AutotileKind) == HillAutotile.KindStrip;
        if (isStrip)
        {
            if (!TryReadPngSize(Absolute(projectRoot, atlas), out int aw, out int ah)) return;
            int variants = TileVisualResolver.VariantsOf(def);
            if (ah % HillAutotile.Rows != 0 || aw != (ah / HillAutotile.Rows) * variants * HillAutotile.StripGroupCols)
                problems.Add($"{def.Id}: strip atlas is {aw}x{ah}; expected {variants * HillAutotile.StripGroupCols} x {HillAutotile.Rows} square cells");
            return;
        }

        if (def.SpriteFillFootprint || string.IsNullOrWhiteSpace(def.SpriteSlot)) return;
        if (!TryReadPngSize(Absolute(projectRoot, def.SpriteSlot), out int w, out int h)) return;
        if ((long)w * def.FootprintH != (long)h * def.FootprintW)
            problems.Add($"{def.Id}: sprite canvas {w}x{h} is not the {def.FootprintW}x{def.FootprintH} footprint's shape (pinned-canvas contract)");
    }

    private static string Prop(TileDef def, string key) =>
        def?.Props != null && def.Props.TryGetValue(key, out string value) ? value : null;

    private static string Absolute(string projectRoot, string resPath)
    {
        if (string.IsNullOrWhiteSpace(resPath) || !resPath.StartsWith(ResPrefix, StringComparison.Ordinal)) return null;
        return Path.Combine(projectRoot, resPath.Substring(ResPrefix.Length).Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Width/height from a PNG's IHDR chunk — 24 bytes, no image library needed.</summary>
    private static bool TryReadPngSize(string absPath, out int width, out int height)
    {
        width = height = 0;
        if (absPath == null || !File.Exists(absPath)) return false;
        try
        {
            using var stream = File.OpenRead(absPath);
            byte[] header = new byte[24];
            if (stream.Read(header, 0, header.Length) != header.Length) return false;
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47) return false;
            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }
}

namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// GDD §2.4 — the per-tile-KIND effect-area painter's backing store. The user's decision
/// (item 6) is "per tile-kind, code default": one effect shape per tile kind, shared by
/// EVERY placed instance in EVERY map, NOT stored per-map. TileDefs are code-defined and
/// immutable, so an authored override can't live on the def; it lives here instead — a
/// global sidecar keyed by defId, persisted to a single <c>user://tile_effects.json</c>
/// so an edit survives a restart. When a def has an override, it wins over the def's
/// code-defined <see cref="TileDef.EffectArea"/>; otherwise the code default stands.
///
/// v2: multi-cell tiles store one 16-bit mask PER FOOTPRINT CELL (row-major, length =
/// FootprintW × FootprintH). A 3×1 sun lounger stores 3 masks; a 1×1 ground tile stores
/// 1. The mask for footprint cell (cx, cy) is at index [cy * FootprintW + cx].
///
/// Pure C# — callers (Editor layer) globalize the <c>user://</c> path and pass an absolute
/// one in, exactly like <see cref="MapJson"/>.
/// </summary>
public static class TileEffectStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // defId -> per-footprint-cell masks (row-major, length = FootprintW × FootprintH).
    // Each element is a 16-bit sub-cell mask (4×4 per cell). Presence = "override exists"
    // (even all-zero masks = a deliberate no-effect authoring); absence = "use the def's
    // code default".
    private static Dictionary<string, int[]> _masks = new();

    /// <summary>True after a successful (or empty) <see cref="Load"/> this session — the
    /// Editor boots it once so the very first draw already reflects saved overrides.</summary>
    public static bool Loaded { get; private set; }

    /// <summary>Serialized shape: <c>{ get; set; }</c> props only (STJ ignores fields —
    /// the same rule that guards <see cref="MapJson"/>).</summary>
    public sealed class StoreFile
    {
        public int Version { get; set; } = 2;
        /// <summary>defId → per-footprint-cell masks (row-major). Each mask is a 16-bit
        /// 4×4 sub-cell bitfield for one cell of the tile's footprint.</summary>
        public Dictionary<string, int[]> Masks { get; set; } = new();
    }

    public static bool HasOverride(string defId) => defId != null && _masks.ContainsKey(defId);

    /// <summary>Get the stored per-footprint-cell masks for a def. A legacy v1 entry has one
    /// element (the anchor cell); use <see cref="OpeningMasksFor"/> when an array normalized
    /// to a known <see cref="TileDef"/> footprint is required.</summary>
    public static bool TryGetMasks(string defId, out int[] masks)
    {
        if (defId != null && _masks.TryGetValue(defId, out masks)) return true;
        masks = null;
        return false;
    }

    /// <summary>The masks the painter should open with: the saved override if any, else
    /// the def's code default projected onto the full footprint grid (each footprint cell
    /// gets its own 4×4 sub-cell approximation).</summary>
    public static int[] OpeningMasksFor(TileDef def)
    {
        if (def == null) return Array.Empty<int>();
        int cellCount = def.FootprintW * def.FootprintH;
        if (TryGetMasks(def.Id, out int[] saved))
        {
            // v1 files contain only the anchor-cell mask. Normalize both legacy and
            // hand-edited v2 arrays to the current footprint without mutating the store.
            var normalized = new int[cellCount];
            Array.Copy(saved, normalized, Math.Min(saved.Length, normalized.Length));
            return normalized;
        }

        var arr = new int[cellCount];
        if (def.EffectArea != null && def.EffectArea.Kind == ShapeDef.KindSubcellMask)
        {
            // The code default already defines a sub-cell mask — use it for the anchor cell.
            arr[0] = def.EffectArea.Mask & ShapeDef.FullMask;
        }
        else
        {
            // Sample in anchor-relative coordinates so a shape spanning several footprint
            // cells is projected once across the whole tile instead of repeated per cell.
            for (int cy = 0; cy < def.FootprintH; cy++)
                for (int cx = 0; cx < def.FootprintW; cx++)
                    arr[cy * def.FootprintW + cx] = ApproximateMask(def.EffectArea, cx, cy);
        }
        return arr;
    }

    public static void SetMasks(string defId, int[] masks)
    {
        if (defId == null || masks == null) return;
        // Normalize to 16 bits per element.
        var normalized = new int[masks.Length];
        for (int i = 0; i < masks.Length; i++) normalized[i] = masks[i] & ShapeDef.FullMask;
        _masks[defId] = normalized;
    }

    /// <summary>Drop the override so the def's code default applies again.</summary>
    public static void ClearOverride(string defId)
    {
        if (defId != null) _masks.Remove(defId);
    }

    /// <summary>Effect shape to draw/collide for a def: the sub-cell override if one exists
    /// (returns the anchor-cell mask for backward compat — callers that need per-cell masks
    /// should use <see cref="TryGetMasks"/> directly), else the code-defined
    /// <see cref="TileDef.EffectArea"/> (which may be null = footprint rect).</summary>
    public static ShapeDef EffectAreaOf(TileDef def)
    {
        if (def == null) return null;
        if (TryGetMasks(def.Id, out int[] masks) && masks.Length > 0)
            return ShapeDef.SubcellMask(masks[0]);
        return def.EffectArea;
    }

    /// <summary>Best-effort projection of an existing code-defined shape onto the 4×4 grid so
    /// the painter opens showing roughly what the def already collides with. Null/polygon →
    /// full cell; rect/circle → sample each sub-cell's center.</summary>
    public static int ApproximateMask(ShapeDef shape, int footprintCellX = 0, int footprintCellY = 0)
    {
        if (shape == null) return ShapeDef.FullMask;

        float cell = MapGrid.PixelsPerCell;
        float sub = cell / ShapeDef.SubcellsPerAxis;
        int mask = 0;

        for (int r = 0; r < ShapeDef.SubcellsPerAxis; r++)
            for (int c = 0; c < ShapeDef.SubcellsPerAxis; c++)
            {
                float px = footprintCellX * cell + (c + 0.5f) * sub;
                float py = footprintCellY * cell + (r + 0.5f) * sub;
                bool inside = shape.Kind switch
                {
                    ShapeDef.KindRect => px >= shape.OffsetX && px <= shape.OffsetX + shape.W &&
                                         py >= shape.OffsetY && py <= shape.OffsetY + shape.H,
                    ShapeDef.KindCircle => (px - shape.OffsetX) * (px - shape.OffsetX) +
                                           (py - shape.OffsetY) * (py - shape.OffsetY) <= shape.Radius * shape.Radius,
                    ShapeDef.KindSubcellMask => (shape.Mask & (1 << (r * ShapeDef.SubcellsPerAxis + c))) != 0,
                    _ => true, // polygon or unknown: approximate as the whole cell
                };
                if (inside) mask |= 1 << (r * ShapeDef.SubcellsPerAxis + c);
            }

        return mask;
    }

    // ---------------------------------------------------------------- persistence

    /// <summary>Loads overrides from an absolute path. Handles both v1 (single int per def)
    /// and v2 (int[] per def) formats. Never throws: a missing/corrupt file leaves the store
    /// empty with a warning (same degrade-don't-crash contract as MapJson).</summary>
    public static void Load(string absPath, out List<string> warnings)
    {
        warnings = new List<string>();
        Loaded = true;

        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
        {
            _masks = new Dictionary<string, int[]>();
            return;
        }

        try
        {
            string json = File.ReadAllText(absPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _masks = new Dictionary<string, int[]>();

            // Existing v1 files and our STJ writer use PascalCase. Also accept camelCase
            // so hand-authored/exported sidecars remain compatible.
            bool hasMasks = root.TryGetProperty("Masks", out var masksEl) ||
                            root.TryGetProperty("masks", out masksEl);
            if (hasMasks && masksEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in masksEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        // v1 format: single int → wrap as single-element array.
                        _masks[prop.Name] = new[] { prop.Value.GetInt32() & ShapeDef.FullMask };
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        // v2 format: array of ints.
                        var arr = new int[prop.Value.GetArrayLength()];
                        int i = 0;
                        foreach (var el in prop.Value.EnumerateArray())
                            arr[i++] = el.GetInt32() & ShapeDef.FullMask;
                        _masks[prop.Name] = arr;
                    }
                    // Unknown kind → skip silently.
                }
            }
        }
        catch (Exception e)
        {
            _masks = new Dictionary<string, int[]>();
            warnings.Add($"could not read tile-effects file '{absPath}': {e.Message}");
        }
    }

    /// <summary>Atomic write (temp + rename), mirroring <see cref="MapJson.Save"/>. IO
    /// exceptions PROPAGATE — the Editor caller catches and surfaces via GD.PushError.</summary>
    public static void Save(string absPath)
    {
        var file = new StoreFile { Version = 2, Masks = new Dictionary<string, int[]>(_masks) };
        string json = JsonSerializer.Serialize(file, WriteOptions);

        string dir = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tmpPath = absPath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, absPath, overwrite: true);
    }

    /// <summary>Structural guard (mirrors <see cref="MapJson.RoundTripSelfTest"/>): set a few
    /// masks, save, reload, compare. Returns an empty list on success. Restores the live
    /// in-memory state afterward so running the check never clobbers the session's overrides.</summary>
    public static List<string> RoundTripSelfTest()
    {
        var failures = new List<string>();
        var saved = new Dictionary<string, int[]>(_masks);

        string tmpPath = Path.Combine(Path.GetTempPath(), "fableland_tileeffects_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            _masks = new Dictionary<string, int[]> { ["ground.grass"] = new[] { 0x00FF }, ["hazard.bonfire"] = new[] { 0x0660, 0x0000 } };
            Save(tmpPath);
            Load(tmpPath, out var warnings);
            foreach (var w in warnings) failures.Add("unexpected load warning: " + w);

            if (!TryGetMasks("ground.grass", out var m1) || m1.Length != 1 || m1[0] != 0x00FF) failures.Add("ground.grass masks mismatch");
            if (!TryGetMasks("hazard.bonfire", out var m2) || m2.Length != 2 || m2[0] != 0x0660 || m2[1] != 0x0000) failures.Add("hazard.bonfire masks mismatch");
            if (HasOverride("ground.stone")) failures.Add("phantom override survived round-trip");

            // A v1 single-int entry must still load as the anchor-cell mask.
            File.WriteAllText(tmpPath, "{\"Version\":1,\"Masks\":{\"platform.sun_lounger\":4660}}");
            Load(tmpPath, out warnings);
            foreach (var w in warnings) failures.Add("unexpected v1 load warning: " + w);
            if (!TryGetMasks("platform.sun_lounger", out var legacy) ||
                legacy.Length != 1 || legacy[0] != 0x1234)
                failures.Add("v1 single-mask compatibility mismatch");

            var legacyDef = new TileDef
            {
                Id = "platform.sun_lounger",
                FootprintW = 3,
                FootprintH = 1,
            };
            var normalizedLegacy = OpeningMasksFor(legacyDef);
            if (normalizedLegacy.Length != 3 || normalizedLegacy[0] != 0x1234 ||
                normalizedLegacy[1] != 0 || normalizedLegacy[2] != 0)
                failures.Add("v1 mask did not normalize to the current footprint");

            ClearOverride("platform.sun_lounger");
            var defaultDef = new TileDef
            {
                Id = "platform.sun_lounger",
                FootprintW = 3,
                FootprintH = 1,
                EffectArea = ShapeDef.Rect(0f, MapGrid.PixelsPerCell * 0.25f,
                    MapGrid.PixelsPerCell * 3f, MapGrid.PixelsPerCell * 0.25f),
            };
            var projectedDefault = OpeningMasksFor(defaultDef);
            if (projectedDefault.Length != 3 || projectedDefault[0] != 0x00F0 ||
                projectedDefault[1] != 0x00F0 || projectedDefault[2] != 0x00F0)
                failures.Add("multi-cell code default projection mismatch");
        }
        catch (Exception e)
        {
            failures.Add("round-trip threw: " + e.Message);
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best-effort */ }
            _masks = saved; // never let the self-test leak its fixtures into the live store
        }

        return failures;
    }
}

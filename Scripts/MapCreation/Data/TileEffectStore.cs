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
/// Pure C# — callers (Editor layer) globalize the <c>user://</c> path and pass an absolute
/// one in, exactly like <see cref="MapJson"/>. A mask is a 4×4 sub-cell bitfield anchored to
/// a tile's top-left cell (<see cref="ShapeDef.KindSubcellMask"/>); multi-cell tiles author
/// the anchor cell only in v1.
/// </summary>
public static class TileEffectStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // defId -> 16-bit sub-cell mask. Presence = "override exists" (even mask 0 = a deliberate
    // no-effect authoring); absence = "use the def's code default".
    private static Dictionary<string, int> _masks = new();

    /// <summary>True after a successful (or empty) <see cref="Load"/> this session — the
    /// Editor boots it once so the very first draw already reflects saved overrides.</summary>
    public static bool Loaded { get; private set; }

    /// <summary>Serialized shape: <c>{ get; set; }</c> props only (STJ ignores fields —
    /// the same rule that guards <see cref="MapJson"/>).</summary>
    public sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, int> Masks { get; set; } = new();
    }

    public static bool HasOverride(string defId) => defId != null && _masks.ContainsKey(defId);

    public static bool TryGetMask(string defId, out int mask)
    {
        if (defId != null && _masks.TryGetValue(defId, out mask)) return true;
        mask = 0;
        return false;
    }

    /// <summary>The mask the painter should open with: the saved override if any, else the
    /// def's code default projected onto the 4×4 grid (a footprint-rect/null default fills
    /// the whole anchor cell; an existing rect/circle is approximated by sampling sub-cells).</summary>
    public static int OpeningMaskFor(TileDef def)
    {
        if (def != null && TryGetMask(def.Id, out int m)) return m;
        return ApproximateMask(def?.EffectArea);
    }

    public static void SetMask(string defId, int mask)
    {
        if (defId == null) return;
        _masks[defId] = mask & ShapeDef.FullMask;
    }

    /// <summary>Drop the override so the def's code default applies again.</summary>
    public static void ClearOverride(string defId)
    {
        if (defId != null) _masks.Remove(defId);
    }

    /// <summary>Effect shape to draw/collide for a def: the sub-cell override if one exists,
    /// else the code-defined <see cref="TileDef.EffectArea"/> (which may be null = footprint
    /// rect, preserved).</summary>
    public static ShapeDef EffectAreaOf(TileDef def)
    {
        if (def == null) return null;
        if (TryGetMask(def.Id, out int mask)) return ShapeDef.SubcellMask(mask);
        return def.EffectArea;
    }

    /// <summary>Best-effort projection of an existing code-defined shape onto the 4×4 grid so
    /// the painter opens showing roughly what the def already collides with. Null/polygon →
    /// full cell; rect/circle → sample each sub-cell's center.</summary>
    public static int ApproximateMask(ShapeDef shape)
    {
        if (shape == null) return ShapeDef.FullMask;

        float cell = 32f;                 // one authoring cell in px (Units.PixelsPerMeter); this
        float sub = cell / ShapeDef.SubcellsPerAxis; // layer is headless, so avoid the Godot Units ref
        int mask = 0;

        for (int r = 0; r < ShapeDef.SubcellsPerAxis; r++)
            for (int c = 0; c < ShapeDef.SubcellsPerAxis; c++)
            {
                float px = (c + 0.5f) * sub;
                float py = (r + 0.5f) * sub;
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

    /// <summary>Loads overrides from an absolute path. Never throws: a missing/corrupt file
    /// leaves the store empty with a warning (same degrade-don't-crash contract as MapJson).</summary>
    public static void Load(string absPath, out List<string> warnings)
    {
        warnings = new List<string>();
        Loaded = true;

        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
        {
            _masks = new Dictionary<string, int>();
            return;
        }

        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(absPath));
            _masks = file?.Masks ?? new Dictionary<string, int>();
            // Normalize to 16 bits so a hand-edited file can't inject stray high bits.
            foreach (var key in new List<string>(_masks.Keys)) _masks[key] &= ShapeDef.FullMask;
        }
        catch (Exception e)
        {
            _masks = new Dictionary<string, int>();
            warnings.Add($"could not read tile-effects file '{absPath}': {e.Message}");
        }
    }

    /// <summary>Atomic write (temp + rename), mirroring <see cref="MapJson.Save"/>. IO
    /// exceptions PROPAGATE — the Editor caller catches and surfaces via GD.PushError.</summary>
    public static void Save(string absPath)
    {
        var file = new StoreFile { Version = 1, Masks = new Dictionary<string, int>(_masks) };
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
        var saved = new Dictionary<string, int>(_masks);

        string tmpPath = Path.Combine(Path.GetTempPath(), "fableland_tileeffects_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            _masks = new Dictionary<string, int> { ["ground.grass"] = 0x00FF, ["hazard.bonfire"] = 0x0660 };
            Save(tmpPath);
            Load(tmpPath, out var warnings);
            foreach (var w in warnings) failures.Add("unexpected load warning: " + w);

            if (!TryGetMask("ground.grass", out int m1) || m1 != 0x00FF) failures.Add("ground.grass mask mismatch");
            if (!TryGetMask("hazard.bonfire", out int m2) || m2 != 0x0660) failures.Add("hazard.bonfire mask mismatch");
            if (HasOverride("ground.stone")) failures.Add("phantom override survived round-trip");
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

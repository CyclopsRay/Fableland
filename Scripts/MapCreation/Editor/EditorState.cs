namespace Fableland.MapCreation.Editor;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// GDD §10 — the ONE owner of editor-session state (current document, layer, tool,
/// brush, selection, pan/zoom-adjacent flags, undo stack). A plain class, not a Node —
/// this is presentation-layer state (Godot types like <see cref="Color"/> are fine
/// here, unlike `Scripts/MapCreation/Data/`, which stays headless per T10).
///
/// <see cref="MapEditor"/> owns the one instance for the scene's lifetime; <see cref="GridView"/>
/// reads it every `_Draw`. <see cref="StateChanged"/> is a scene-local event (dies with
/// the editor scene) — no `_ExitTree` unsubscribe needed; this isn't an autoload
/// publisher (KNOWLEDGE v0.5.4 caveat is about that different, longer-lived case).
/// </summary>
public sealed class EditorState
{
    public MapDocument Document;

    /// <summary>Absolute filesystem path this document saves to (already globalized
    /// from `user://maps/&lt;id&gt;.json` — GDD §7.8/§8).</summary>
    public string SavePath;

    private int _currentLayerIndex;

    /// <summary>Clamped into the current document's layer list on every set. MC4:
    /// switching layers clears <see cref="Selection"/> — selected tiles are
    /// `PlacedTile` refs that belong to a specific layer's list, so they stop making
    /// sense the moment the current layer changes (GDD §7.2 note). Centralized here
    /// (rather than at every call site, e.g. the future MC5 layer panel) so no
    /// future caller can forget it.</summary>
    public int CurrentLayerIndex
    {
        get => _currentLayerIndex;
        set
        {
            int count = Document?.Layers?.Count ?? 0;
            int clamped = count <= 0 ? 0 : Math.Clamp(value, 0, count - 1);
            if (clamped != _currentLayerIndex) Selection.Clear();
            _currentLayerIndex = clamped;
        }
    }

    public MapLayerData CurrentLayer =>
        Document?.Layers != null && _currentLayerIndex >= 0 && _currentLayerIndex < Document.Layers.Count
            ? Document.Layers[_currentLayerIndex]
            : null;

    /// <summary>GDD §7.2 — the left tool rail, in top-to-bottom order.</summary>
    public enum EditorTool { Paint, Erase, Rect, Marquee, Lasso, Move, Eyedropper, Bucket }

    public EditorTool ActiveTool = EditorTool.Paint;

    /// <summary>Current palette brush (a `TileRegistry` id). MC5's palette panel writes
    /// this; MC4's paint/rect/bucket tools read it.</summary>
    public string BrushDefId = "ground.grass";

    /// <summary>Whole-`PlacedTile` references on the current layer (reference identity —
    /// GDD §7.2 marquee/lasso select). MC4 fills/consumes this.</summary>
    public readonly HashSet<PlacedTile> Selection = new();

    /// <summary>Cut/copy payload, cell-relative to the selection's anchor. MC4 fills
    /// this on Cut/Copy and consumes it on Paste.</summary>
    public readonly List<(string DefId, int Dx, int Dy, bool FlipX)> Clipboard = new();

    public bool ShowGrid = true;
    public bool ShowEffectAreas;

    /// <summary>null = no preview; MC5's "Preview generation" button (`RuleResolver.Resolve`
    /// output) fills this. <see cref="GridView"/> renders it whenever non-null (GDD §6).</summary>
    public List<ResolvedSpawn> Preview;

    public readonly CommandStack Commands = new();

    private readonly Dictionary<int, LayerOccupancy> _occupancyCache = new();

    /// <summary>Build-on-demand per-layer occupancy cache (GDD §2.3). Any command that
    /// mutates tiles MUST call <see cref="InvalidateOccupancy"/> afterward.</summary>
    public LayerOccupancy OccupancyOf(int layerIndex)
    {
        if (Document?.Layers == null || layerIndex < 0 || layerIndex >= Document.Layers.Count)
            return new LayerOccupancy();

        if (_occupancyCache.TryGetValue(layerIndex, out var cached)) return cached;

        var built = LayerOccupancy.Build(Document.Layers[layerIndex],
            id => TileRegistry.TryGet(id, out var def) ? def : null);
        _occupancyCache[layerIndex] = built;
        return built;
    }

    public void InvalidateOccupancy() => _occupancyCache.Clear();

    public event Action StateChanged;
    public void RaiseStateChanged() => StateChanged?.Invoke();

    // Six distinct hues, cycled by layer index (GDD §7.4 sketchline / hover accent).
    private static readonly Color[] AccentPalette =
    {
        new(1f, 0.55f, 0.15f),  // orange
        new(0.2f, 0.8f, 1f),    // cyan
        new(0.85f, 0.3f, 0.9f), // violet
        new(0.4f, 1f, 0.4f),    // green
        new(1f, 0.9f, 0.2f),    // yellow
        new(1f, 0.35f, 0.55f),  // pink-red
    };

    /// <summary>Per-layer accent color from a small fixed palette (GDD §7.4: current-layer
    /// sketchline outline; also used for the hover-cell outline).</summary>
    public Color AccentOf(int layerIndex)
    {
        if (layerIndex < 0) layerIndex = 0;
        return AccentPalette[layerIndex % AccentPalette.Length];
    }
}

namespace Fableland.MapCreation.Editor;

using System;
using Fableland.MapCreation.Data;

/// <summary>
/// GDD §7.7 ("layer add/remove/reorder and property edits are commands too") — the
/// command types the MC5 layer panel / canvas-properties sub-panel push onto
/// <see cref="EditorState.Commands"/>. Kept in this dedicated file (rather than
/// Tools/Commands.cs) since these mutate the document's layer LIST / top-level
/// properties, not a layer's tile list — a different mutation family from MC4's
/// <c>TileBatchCommand</c>/<c>MoveCommand</c>.
/// </summary>

/// <summary>
/// GDD §7.7 — one property edit (any field on a <see cref="MapLayerData"/> or the
/// document's <see cref="CanvasData"/>) as a single undoable unit. Deliberately
/// generic (apply/revert closures) rather than one command type per field: the
/// GDD §3 farview collision-auto-off coupling ("when a property edit makes
/// collision illegal ... the SAME command also sets Collision = false, one
/// command, undo restores both") is just the caller's closures touching two
/// fields instead of one — no special-case command type needed for it.
/// </summary>
public sealed class PropertyEditCommand : IEditorCommand
{
    public string Name { get; }

    private readonly Action _apply;
    private readonly Action _revert;

    public PropertyEditCommand(string name, Action apply, Action revert)
    {
        Name = name;
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _revert = revert ?? throw new ArgumentNullException(nameof(revert));
    }

    public void Do() => _apply();
    public void Undo() => _revert();
}

/// <summary>
/// GDD §1/§7.7 — inserts a new farview/closeview <see cref="MapLayerData"/> at a
/// band-correct index (see <see cref="LayerPanel"/>'s ordering comment: new
/// farview inserts just below the battlefield, new closeview appends at the end).
/// Holds the layer object reference itself (not a copy) so Redo reuses the exact
/// instance a live selection/reference may already point at.
///
/// Occupancy caching note (<see cref="EditorState.OccupancyOf"/>): the cache is
/// keyed by LAYER INDEX, and inserting a layer shifts every later layer's index —
/// so both <see cref="Do"/> and <see cref="Undo"/> must invalidate it, not just one.
/// </summary>
public sealed class LayerAddCommand : IEditorCommand
{
    public string Name => "Add Layer";

    private readonly EditorState _state;
    private readonly MapDocument _doc;
    private readonly MapLayerData _layer;
    private readonly int _index;

    public LayerAddCommand(EditorState state, MapDocument doc, MapLayerData layer, int index)
    {
        _state = state;
        _doc = doc;
        _layer = layer;
        _index = index;
    }

    /// <summary>The new layer becomes current (brief: "New layer becomes current").</summary>
    public void Do()
    {
        _doc.Layers.Insert(_index, _layer);
        _state.InvalidateOccupancy();
        _state.CurrentLayerIndex = _index;
    }

    /// <summary>On Undo, current layer falls back to the battlefield (brief: "on Undo
    /// fall back to the battlefield index") since the just-removed layer no longer exists.</summary>
    public void Undo()
    {
        _doc.Layers.RemoveAt(_index);
        _state.InvalidateOccupancy();
        _state.CurrentLayerIndex = FindBattlefieldIndex(_doc);
    }

    internal static int FindBattlefieldIndex(MapDocument doc)
    {
        for (int i = 0; i < (doc?.Layers?.Count ?? 0); i++)
            if (doc.Layers[i]?.Role == MapLayerData.RoleBattlefield) return i;
        return 0;
    }
}

/// <summary>
/// GDD §7.7 — inverse of <see cref="LayerAddCommand"/>: removes a farview/closeview
/// layer (the layer panel only ever constructs this for farview/closeview rows —
/// the battlefield is never addable/removable, per GDD §1). Holds the removed
/// layer object + its original index so Undo reinserts the very same instance at
/// the very same position.
/// </summary>
public sealed class LayerRemoveCommand : IEditorCommand
{
    public string Name => "Remove Layer";

    private readonly EditorState _state;
    private readonly MapDocument _doc;
    private readonly MapLayerData _layer;
    private readonly int _index;

    public LayerRemoveCommand(EditorState state, MapDocument doc, MapLayerData layer, int index)
    {
        _state = state;
        _doc = doc;
        _layer = layer;
        _index = index;
    }

    /// <summary>Current layer falls back to the battlefield (the removed layer may have
    /// been current) — same fallback rule as <see cref="LayerAddCommand.Undo"/>.</summary>
    public void Do()
    {
        _doc.Layers.RemoveAt(_index);
        _state.InvalidateOccupancy();
        _state.CurrentLayerIndex = LayerAddCommand.FindBattlefieldIndex(_doc);
    }

    /// <summary>Undo reinserts the layer and makes it current again (symmetric with
    /// <see cref="LayerAddCommand.Do"/>).</summary>
    public void Undo()
    {
        _doc.Layers.Insert(_index, _layer);
        _state.InvalidateOccupancy();
        _state.CurrentLayerIndex = _index;
    }
}

/// <summary>
/// GDD §7.7/§1 (ORDERING) — swaps two ADJACENT, SAME-ROLE layers (the layer panel
/// only ever constructs this for a same-band neighbor swap; the battlefield never
/// moves and never participates). A swap is its own inverse, so <see cref="Do"/> and
/// <see cref="Undo"/> share one implementation. If <see cref="EditorState.CurrentLayerIndex"/>
/// pointed at either swapped slot, it's updated to follow the moved layer to its new
/// index (brief: "adjust CurrentLayerIndex to follow the moved layer").
/// </summary>
public sealed class LayerReorderCommand : IEditorCommand
{
    public string Name => "Reorder Layer";

    private readonly EditorState _state;
    private readonly MapDocument _doc;
    private readonly int _fromIndex;
    private readonly int _toIndex;

    public LayerReorderCommand(EditorState state, MapDocument doc, int fromIndex, int toIndex)
    {
        _state = state;
        _doc = doc;
        _fromIndex = fromIndex;
        _toIndex = toIndex;
    }

    public void Do() => Swap();
    public void Undo() => Swap();

    private void Swap()
    {
        bool currentWasFrom = _state.CurrentLayerIndex == _fromIndex;
        bool currentWasTo = _state.CurrentLayerIndex == _toIndex;

        (_doc.Layers[_fromIndex], _doc.Layers[_toIndex]) = (_doc.Layers[_toIndex], _doc.Layers[_fromIndex]);
        _state.InvalidateOccupancy();

        if (currentWasFrom) _state.CurrentLayerIndex = _toIndex;
        else if (currentWasTo) _state.CurrentLayerIndex = _fromIndex;
    }
}

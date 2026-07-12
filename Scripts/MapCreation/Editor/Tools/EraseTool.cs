namespace Fableland.MapCreation.Editor.Tools;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Erase (E)": click/drag erases the WHOLE tile under the cursor
/// (GDD §2.3 — no half-deleting a multi-cell tile). Every visited cell resolves to
/// its owning <see cref="PlacedTile"/> via the occupancy index and is deduped by
/// reference, so a multi-cell tile crossed twice during one drag is only queued for
/// removal once. One drag = one undo step (GDD §7.7).
/// </summary>
public sealed class EraseTool : ToolBase
{
    private List<PlacedTile> _pendingRemove;
    private HashSet<PlacedTile> _pendingSet;
    private MapLayerData _strokeLayer;

    public EraseTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        BeginStroke();
        TryErase(cell);
    }

    public override void OnDragged(Vector2I cell)
    {
        if (_pendingRemove == null) return;
        TryErase(cell);
    }

    public override void OnReleased(Vector2I cell)
    {
        if (_pendingRemove == null) return;
        TryErase(cell);
        Commit();
    }

    public override void OnDeactivated() => Reset();

    private void BeginStroke()
    {
        _pendingRemove = new List<PlacedTile>();
        _pendingSet = new HashSet<PlacedTile>();
        _strokeLayer = State.CurrentLayer;
    }

    private void TryErase(Vector2I cell)
    {
        var layer = State.CurrentLayer;
        if (layer == null || layer != _strokeLayer) return;

        var occ = State.OccupancyOf(State.CurrentLayerIndex);
        var tile = occ.TileAt(cell.X, cell.Y);
        if (tile == null) return;
        if (_pendingSet.Add(tile)) _pendingRemove.Add(tile);
    }

    private void Commit()
    {
        if (_pendingRemove.Count > 0)
            State.Commands.Push(new TileBatchCommand(State, _strokeLayer, "Erase", null, _pendingRemove));
        Reset();
    }

    private void Reset()
    {
        _pendingRemove = null;
        _pendingSet = null;
        _strokeLayer = null;
    }

    /// <summary>GDD §7.2: "Ghost: 1x1 red outline at cursor" — always the illegal
    /// tint regardless of whether the hovered cell is occupied; it's a cursor
    /// indicator, not a placement-legality preview.</summary>
    public override GhostInfo GetGhost()
    {
        var cell = View.HoverCell;
        if (!cell.HasValue) return null;

        return new GhostInfo
        {
            Valid = false,
            Cells = new List<(Vector2I, int, int, string)> { (cell.Value, 1, 1, null) },
        };
    }
}

namespace Fableland.MapCreation.Editor.Tools;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Paint (B)": click/drag paints <see cref="EditorState.BrushDefId"/>
/// on the current layer; a whole drag is one undo step (GDD §7.7). Cells that would
/// be illegal (wrong role, out of grid, or already occupied — including by a tile
/// painted earlier in THIS same drag) are silently skipped, matching the red-ghost/
/// no-mutation contract of GDD §2.3.
/// </summary>
public sealed class PaintTool : ToolBase
{
    private List<PlacedTile> _pending;
    private HashSet<(int x, int y)> _pendingCells;
    private MapLayerData _strokeLayer;

    public PaintTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        BeginStroke();
        TryPaint(cell);
    }

    public override void OnDragged(Vector2I cell)
    {
        if (_pending == null) return;
        TryPaint(cell);
    }

    public override void OnReleased(Vector2I cell)
    {
        if (_pending == null) return;
        TryPaint(cell);
        Commit();
    }

    public override void OnDeactivated() => Reset();

    private void BeginStroke()
    {
        _pending = new List<PlacedTile>();
        _pendingCells = new HashSet<(int, int)>();
        _strokeLayer = State.CurrentLayer;
    }

    private void TryPaint(Vector2I cell)
    {
        var layer = State.CurrentLayer;
        if (layer == null || layer != _strokeLayer) return;
        if (!TileRegistry.TryGet(State.BrushDefId, out var def)) return;
        if (!IsLegalOnCurrentLayer(def)) return;

        int fw = def.FootprintW, fh = def.FootprintH;
        if (cell.X < 0 || cell.Y < 0 || cell.X + fw > layer.GridW || cell.Y + fh > layer.GridH) return;

        var occ = State.OccupancyOf(State.CurrentLayerIndex);
        for (int dy = 0; dy < fh; dy++)
            for (int dx = 0; dx < fw; dx++)
            {
                var c = (cell.X + dx, cell.Y + dy);
                if (occ.Cells.ContainsKey(c) || _pendingCells.Contains(c)) return; // blocked (real or in-stroke)
            }

        var tile = new PlacedTile { DefId = def.Id, X = cell.X, Y = cell.Y };
        _pending.Add(tile);
        for (int dy = 0; dy < fh; dy++)
            for (int dx = 0; dx < fw; dx++)
                _pendingCells.Add((cell.X + dx, cell.Y + dy));
    }

    private void Commit()
    {
        if (_pending.Count > 0)
            State.Commands.Push(new TileBatchCommand(State, _strokeLayer, "Paint", _pending, null));
        Reset();
    }

    private void Reset()
    {
        _pending = null;
        _pendingCells = null;
        _strokeLayer = null;
    }

    public override GhostInfo GetGhost()
    {
        var layer = State.CurrentLayer;
        var cell = View.HoverCell;
        if (layer == null || !cell.HasValue) return null;

        bool haveDef = TileRegistry.TryGet(State.BrushDefId, out var def);
        int fw = haveDef ? def.FootprintW : 1;
        int fh = haveDef ? def.FootprintH : 1;

        bool valid = haveDef && IsLegalOnCurrentLayer(def) &&
                     cell.Value.X >= 0 && cell.Value.Y >= 0 &&
                     cell.Value.X + fw <= layer.GridW && cell.Value.Y + fh <= layer.GridH;

        if (valid)
        {
            var occ = State.OccupancyOf(State.CurrentLayerIndex);
            for (int dy = 0; dy < fh && valid; dy++)
                for (int dx = 0; dx < fw && valid; dx++)
                {
                    var c = (cell.Value.X + dx, cell.Value.Y + dy);
                    if (occ.Cells.ContainsKey(c) || (_pendingCells != null && _pendingCells.Contains(c))) valid = false;
                }
        }

        return new GhostInfo
        {
            Valid = valid,
            Cells = new List<(Vector2I, int, int, string)> { (cell.Value, fw, fh, State.BrushDefId) },
        };
    }
}

namespace Fableland.MapCreation.Editor.Tools;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Move (V)": press on a cell covered by a currently-SELECTED tile's
/// footprint starts a move drag (press elsewhere = no-op — Marquee/Lasso are the
/// selectors, not this tool); drag ghosts the whole selection translated by
/// (cursor − grab) in cell units; release executes ONE <see cref="MoveCommand"/>
/// with Q-MC3 overwrite semantics (non-selected tiles under the destination
/// footprints are removed). If ANY moved tile would leave the grid at the final
/// delta, the WHOLE move is aborted with no command — simpler and acceptable per
/// the brief's own call — rather than partially moving or reverting individual
/// tiles.
/// </summary>
public sealed class MoveTool : ToolBase
{
    private Vector2I? _grabCell;
    private Vector2I? _currentCell;
    private List<(PlacedTile Tile, int OrigX, int OrigY, int Fw, int Fh)> _dragging;

    public MoveTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        var layer = State.CurrentLayer;
        if (layer == null) return;

        PlacedTile hit = null;
        foreach (var t in State.Selection)
        {
            int fw = 1, fh = 1;
            if (TileRegistry.TryGet(t.DefId, out var def)) { fw = def.FootprintW; fh = def.FootprintH; }
            if (cell.X >= t.X && cell.X < t.X + fw && cell.Y >= t.Y && cell.Y < t.Y + fh) { hit = t; break; }
        }
        if (hit == null) return; // press not on a selected tile: do nothing (selection stays)

        _grabCell = cell;
        _currentCell = cell;
        _dragging = new List<(PlacedTile, int, int, int, int)>();
        foreach (var t in State.Selection)
        {
            int fw = 1, fh = 1;
            if (TileRegistry.TryGet(t.DefId, out var def)) { fw = def.FootprintW; fh = def.FootprintH; }
            _dragging.Add((t, t.X, t.Y, fw, fh));
        }
    }

    public override void OnDragged(Vector2I cell)
    {
        if (_dragging == null) return;
        _currentCell = cell;
    }

    public override void OnReleased(Vector2I cell)
    {
        if (_dragging == null) return;
        _currentCell = cell;
        Commit();
        Reset();
    }

    public override void OnDeactivated() => Reset();

    private void Reset()
    {
        _grabCell = null;
        _currentCell = null;
        _dragging = null;
    }

    private (int dx, int dy) CurrentDelta() =>
        (_currentCell.Value.X - _grabCell.Value.X, _currentCell.Value.Y - _grabCell.Value.Y);

    private void Commit()
    {
        var layer = State.CurrentLayer;
        if (layer == null) return;

        var (dx, dy) = CurrentDelta();
        if (dx == 0 && dy == 0) return; // no-op move: nothing to record

        foreach (var (_, ox, oy, fw, fh) in _dragging)
        {
            int nx = ox + dx, ny = oy + dy;
            if (nx < 0 || ny < 0 || nx + fw > layer.GridW || ny + fh > layer.GridH) return; // abort whole move
        }

        // INVARIANT (review-adjudicated): every dragged tile moves by the SAME (dx,dy),
        // so pairwise-disjoint footprints stay disjoint — moved tiles can never overlap
        // EACH OTHER, only non-moved tiles (handled by the overwrite scan below, which
        // must keep excluding movedSet or a selected tile would be removed AND moved).
        // If per-tile skips are ever introduced here (paste-style), that uniformity
        // breaks and a self-overlap check on destCells becomes mandatory.
        var movedSet = new HashSet<PlacedTile>();
        foreach (var (t, _, _, _, _) in _dragging) movedSet.Add(t);

        var destCells = new HashSet<(int, int)>();
        foreach (var (_, ox, oy, fw, fh) in _dragging)
        {
            int nx = ox + dx, ny = oy + dy;
            for (int cy = 0; cy < fh; cy++)
                for (int cx = 0; cx < fw; cx++)
                    destCells.Add((nx + cx, ny + cy));
        }

        var overwritten = new List<PlacedTile>();
        var overwrittenSet = new HashSet<PlacedTile>();
        foreach (var tile in layer.Tiles)
        {
            if (movedSet.Contains(tile)) continue;
            int fw = 1, fh = 1;
            if (TileRegistry.TryGet(tile.DefId, out var def)) { fw = def.FootprintW; fh = def.FootprintH; }

            bool overlaps = false;
            for (int cy = 0; cy < fh && !overlaps; cy++)
                for (int cx = 0; cx < fw && !overlaps; cx++)
                    if (destCells.Contains((tile.X + cx, tile.Y + cy))) overlaps = true;

            if (overlaps && overwrittenSet.Add(tile)) overwritten.Add(tile);
        }

        var moved = new List<(PlacedTile, int, int)>();
        foreach (var (t, ox, oy, _, _) in _dragging) moved.Add((t, ox, oy));

        State.Commands.Push(new MoveCommand(State, layer, moved, dx, dy, overwritten));
    }

    public override GhostInfo GetGhost()
    {
        if (_dragging == null || !_currentCell.HasValue) return null;

        var (dx, dy) = CurrentDelta();
        var layer = State.CurrentLayer;
        bool valid = layer != null;
        var cells = new List<(Vector2I, int, int, string)>();

        foreach (var (t, ox, oy, fw, fh) in _dragging)
        {
            int nx = ox + dx, ny = oy + dy;
            if (layer != null && (nx < 0 || ny < 0 || nx + fw > layer.GridW || ny + fh > layer.GridH)) valid = false;
            cells.Add((new Vector2I(nx, ny), fw, fh, t.DefId));
        }

        return new GhostInfo { Valid = valid, Cells = cells };
    }
}

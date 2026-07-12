namespace Fableland.MapCreation.Editor.Tools;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Rect fill (R)": press anchors a corner, drag shows the normalized
/// rect ghost, release fills every legal cell in the rect with the current brush
/// (skipping blocked cells) as ONE command. Multi-cell brushes tile from the rect's
/// top-left corner, stepping by <c>FootprintW</c>/<c>FootprintH</c> so they never
/// self-overlap (GDD §7.2 "skips blocked").
/// </summary>
public sealed class RectTool : ToolBase
{
    private Vector2I? _anchor;
    private Vector2I? _current;

    public RectTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        _anchor = cell;
        _current = cell;
    }

    public override void OnDragged(Vector2I cell)
    {
        if (!_anchor.HasValue) return;
        _current = cell;
    }

    public override void OnReleased(Vector2I cell)
    {
        if (!_anchor.HasValue) return;
        _current = cell;
        Commit();
        Reset();
    }

    public override void OnDeactivated() => Reset();

    private void Reset()
    {
        _anchor = null;
        _current = null;
    }

    /// <summary>(x0,y0) inclusive min, (x1,y1) EXCLUSIVE max.</summary>
    private (int x0, int y0, int x1, int y1) NormalizedRect()
    {
        int ax = _anchor.Value.X, ay = _anchor.Value.Y;
        int bx = _current.Value.X, by = _current.Value.Y;
        int x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx);
        int y0 = Math.Min(ay, by), y1 = Math.Max(ay, by);
        return (x0, y0, x1 + 1, y1 + 1);
    }

    private void Commit()
    {
        var layer = State.CurrentLayer;
        if (layer == null || !TileRegistry.TryGet(State.BrushDefId, out var def) || !IsLegalOnCurrentLayer(def))
            return;

        var (x0, y0, x1, y1) = NormalizedRect();
        int fw = def.FootprintW, fh = def.FootprintH;
        var occ = State.OccupancyOf(State.CurrentLayerIndex);
        var pendingCells = new HashSet<(int, int)>();
        var toAdd = new List<PlacedTile>();

        for (int y = y0; y + fh <= y1; y += fh)
        {
            for (int x = x0; x + fw <= x1; x += fw)
            {
                if (x < 0 || y < 0 || x + fw > layer.GridW || y + fh > layer.GridH) continue;

                bool blocked = false;
                for (int dy = 0; dy < fh && !blocked; dy++)
                    for (int dx = 0; dx < fw && !blocked; dx++)
                    {
                        var c = (x + dx, y + dy);
                        if (occ.Cells.ContainsKey(c) || pendingCells.Contains(c)) blocked = true;
                    }
                if (blocked) continue;

                toAdd.Add(new PlacedTile { DefId = def.Id, X = x, Y = y });
                for (int dy = 0; dy < fh; dy++)
                    for (int dx = 0; dx < fw; dx++)
                        pendingCells.Add((x + dx, y + dy));
            }
        }

        if (toAdd.Count > 0)
            State.Commands.Push(new TileBatchCommand(State, layer, "Rect fill", toAdd, null));
    }

    public override GhostInfo GetGhost()
    {
        if (!_anchor.HasValue || !_current.HasValue) return null;
        var (x0, y0, x1, y1) = NormalizedRect();
        return new GhostInfo
        {
            Valid = true,
            RectCells = new Rect2I(new Vector2I(x0, y0), new Vector2I(x1 - x0, y1 - y0)),
        };
    }
}

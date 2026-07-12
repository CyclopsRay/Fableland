namespace Fableland.MapCreation.Editor.Tools;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Lasso select (L)": press starts a freehand polygon (cursor-cell
/// centers, world px, deduped so holding still doesn't spam points), GridView draws
/// the in-progress polyline via <see cref="GetGhost"/>. Release closes the polygon
/// and selects tiles that have ANY footprint cell whose CENTER falls inside it
/// (even-odd / ray-casting point-in-polygon — one test per footprint cell, not just
/// the tile's overall bounding-box center, so a big lasso arc through part of a
/// multi-cell tile still catches it). Shift/Alt captured at press, same as Marquee.
/// </summary>
public sealed class LassoTool : ToolBase
{
    private List<Vector2> _points; // world px, in press/drag order
    private Vector2I? _lastCell;
    private bool _active;
    private bool _shift;
    private bool _alt;

    public LassoTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        _active = true;
        _shift = ev.ShiftPressed;
        _alt = ev.AltPressed;
        _points = new List<Vector2> { CellCenterWorld(cell) };
        _lastCell = cell;
    }

    public override void OnDragged(Vector2I cell)
    {
        if (!_active) return;
        if (cell == _lastCell) return;
        _points.Add(CellCenterWorld(cell));
        _lastCell = cell;
    }

    public override void OnReleased(Vector2I cell)
    {
        if (!_active) return;
        if (cell != _lastCell) _points.Add(CellCenterWorld(cell));
        Commit();
        Reset();
    }

    public override void OnDeactivated() => Reset();

    private void Reset()
    {
        _active = false;
        _points = null;
        _lastCell = null;
    }

    private static Vector2 CellCenterWorld(Vector2I cell)
    {
        float c = Units.PixelsPerMeter;
        return new Vector2((cell.X + 0.5f) * c, (cell.Y + 0.5f) * c);
    }

    private void Commit()
    {
        var layer = State.CurrentLayer;
        if (layer?.Tiles == null || _points == null || _points.Count < 3) return;

        var hits = new List<PlacedTile>();
        foreach (var tile in layer.Tiles)
        {
            int fw = 1, fh = 1;
            if (TileRegistry.TryGet(tile.DefId, out var def)) { fw = def.FootprintW; fh = def.FootprintH; }

            bool anyInside = false;
            for (int dy = 0; dy < fh && !anyInside; dy++)
                for (int dx = 0; dx < fw && !anyInside; dx++)
                    if (PointInPolygon(CellCenterWorld(new Vector2I(tile.X + dx, tile.Y + dy)), _points))
                        anyInside = true;

            if (anyInside) hits.Add(tile);
        }

        if (!_shift && !_alt) State.Selection.Clear();
        foreach (var t in hits)
        {
            if (_alt) State.Selection.Remove(t);
            else State.Selection.Add(t);
        }
    }

    /// <summary>Even-odd (ray casting) point-in-polygon test; `poly` is treated as
    /// implicitly closed (last point connects back to the first).</summary>
    private static bool PointInPolygon(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = poly[i], pj = poly[j];
            bool crosses = (pi.Y > p.Y) != (pj.Y > p.Y);
            if (crosses)
            {
                float xIntersect = pi.X + (p.Y - pi.Y) / (pj.Y - pi.Y) * (pj.X - pi.X);
                if (p.X < xIntersect) inside = !inside;
            }
        }
        return inside;
    }

    public override GhostInfo GetGhost()
    {
        if (!_active || _points == null || _points.Count < 2) return null;
        return new GhostInfo { Valid = true, LassoPointsWorld = new List<Vector2>(_points) };
    }
}

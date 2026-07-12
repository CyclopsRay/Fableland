namespace Fableland.MapCreation.Editor.Tools;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Marquee select (M)": drag a rectangle selection on the current
/// layer. A tile is selected if ANY of its footprint cells overlaps the drag rect
/// (not just its anchor). No document mutation, no command — only
/// <see cref="EditorState.Selection"/> changes. Shift adds, Alt subtracts
/// (GDD §7.2 note); the modifier is captured from the PRESS event, per the brief.
/// </summary>
public sealed class MarqueeTool : ToolBase
{
    private Vector2I? _anchor;
    private Vector2I? _current;
    private bool _shift;
    private bool _alt;

    public MarqueeTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        _anchor = cell;
        _current = cell;
        _shift = ev.ShiftPressed;
        _alt = ev.AltPressed;
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
        if (layer?.Tiles == null) return;
        var (x0, y0, x1, y1) = NormalizedRect();

        var hits = new List<PlacedTile>();
        foreach (var tile in layer.Tiles)
        {
            int fw = 1, fh = 1;
            if (TileRegistry.TryGet(tile.DefId, out var def)) { fw = def.FootprintW; fh = def.FootprintH; }
            bool overlaps = tile.X < x1 && tile.X + fw > x0 && tile.Y < y1 && tile.Y + fh > y0;
            if (overlaps) hits.Add(tile);
        }

        // Both held: Alt (subtract) wins — GDD is silent on the combo, this is the
        // smallest/least-surprising default (treat Alt as the higher-priority verb).
        if (!_shift && !_alt) State.Selection.Clear();
        foreach (var t in hits)
        {
            if (_alt) State.Selection.Remove(t);
            else State.Selection.Add(t);
        }
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

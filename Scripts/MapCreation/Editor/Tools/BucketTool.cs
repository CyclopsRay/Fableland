namespace Fableland.MapCreation.Editor.Tools;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.2 row "Bucket fill (F)": flood-fills (4-connectivity) the contiguous
/// region of "same content" cells starting at the pressed cell — same content means
/// same DefId if the seed cell is occupied (the whole connected same-def region is
/// replaced with the brush), or the contiguous EMPTY region if the seed is empty
/// (filled with the brush). Either way, placement steps by the brush's own
/// footprint so multi-cell brushes tile without self-overlap, and any step whose
/// footprint doesn't fit entirely inside the region is skipped (bounded to the
/// layer grid; "keep it simple and correct over clever" per the brief). One command.
/// </summary>
public sealed class BucketTool : ToolBase
{
    public BucketTool(EditorState state, GridView view) : base(state, view) { }

    public override void OnPressed(Vector2I cell, InputEventMouseButton ev)
    {
        var layer = State.CurrentLayer;
        if (layer == null) return;
        if (!TileRegistry.TryGet(State.BrushDefId, out var def) || !IsLegalOnCurrentLayer(def)) return;

        var occ = State.OccupancyOf(State.CurrentLayerIndex);
        var seedTile = occ.TileAt(cell.X, cell.Y);

        HashSet<(int, int)> region;
        var toRemove = new List<PlacedTile>();

        if (seedTile != null)
        {
            string targetDefId = seedTile.DefId;
            region = FloodFill(cell, layer, c => occ.TileAt(c.x, c.y)?.DefId == targetDefId);

            var seen = new HashSet<PlacedTile>();
            foreach (var (x, y) in region)
            {
                var t = occ.TileAt(x, y);
                if (t != null && seen.Add(t)) toRemove.Add(t);
            }
        }
        else
        {
            region = FloodFill(cell, layer, c => occ.TileAt(c.x, c.y) == null);
        }

        if (region.Count == 0) return;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var (x, y) in region)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        int fw = def.FootprintW, fh = def.FootprintH;
        var toAdd = new List<PlacedTile>();

        for (int y = minY; y + fh <= maxY + 1; y += fh)
        {
            for (int x = minX; x + fw <= maxX + 1; x += fw)
            {
                bool fits = true;
                for (int dy = 0; dy < fh && fits; dy++)
                    for (int dx = 0; dx < fw && fits; dx++)
                        if (!region.Contains((x + dx, y + dy))) fits = false;
                if (!fits) continue;

                toAdd.Add(new PlacedTile { DefId = def.Id, X = x, Y = y });
            }
        }

        if (toAdd.Count > 0 || toRemove.Count > 0)
            State.Commands.Push(new TileBatchCommand(State, layer, "Bucket fill", toAdd, toRemove));
    }

    private static HashSet<(int, int)> FloodFill(Vector2I seed, MapLayerData layer, Func<(int x, int y), bool> matches)
    {
        var region = new HashSet<(int, int)>();
        if (seed.X < 0 || seed.Y < 0 || seed.X >= layer.GridW || seed.Y >= layer.GridH) return region;
        if (!matches((seed.X, seed.Y))) return region;

        var stack = new Stack<(int, int)>();
        stack.Push((seed.X, seed.Y));
        region.Add((seed.X, seed.Y));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (n.Item1 < 0 || n.Item2 < 0 || n.Item1 >= layer.GridW || n.Item2 >= layer.GridH) continue;
                if (region.Contains(n)) continue;
                if (!matches(n)) continue;
                region.Add(n);
                stack.Push(n);
            }
        }

        return region;
    }

    public override GhostInfo GetGhost()
    {
        var cell = View.HoverCell;
        if (!cell.HasValue) return null;

        bool haveDef = TileRegistry.TryGet(State.BrushDefId, out var def);
        int fw = haveDef ? def.FootprintW : 1;
        int fh = haveDef ? def.FootprintH : 1;
        bool valid = haveDef && IsLegalOnCurrentLayer(def);

        return new GhostInfo
        {
            Valid = valid,
            Cells = new List<(Vector2I, int, int, string)> { (cell.Value, fw, fh, State.BrushDefId) },
        };
    }
}

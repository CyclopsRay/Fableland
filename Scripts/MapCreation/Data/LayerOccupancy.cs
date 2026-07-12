namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// GDD §2.3 — the occupancy index. Derived from a layer's sparse placed-tile
/// list into "every cell a footprint covers", so placement/erase checks don't
/// need to re-scan every tile. Unknown def ids are treated as 1x1 rather than
/// crashing (a stale/forward-incompatible tile shouldn't break the editor).
///
/// One tile per cell: erasing any occupied cell removes the whole tile — the
/// editor's erase tool should use <see cref="TileAt"/> to find that whole tile.
/// Pure C# — no Godot.
/// </summary>
public sealed class LayerOccupancy
{
    private readonly Dictionary<(int x, int y), PlacedTile> _cells = new();

    public IReadOnlyDictionary<(int x, int y), PlacedTile> Cells => _cells;

    public static LayerOccupancy Build(MapLayerData layer, Func<string, TileDef> lookup)
    {
        var occ = new LayerOccupancy();
        if (layer?.Tiles == null) return occ;

        foreach (var tile in layer.Tiles)
        {
            var def = lookup?.Invoke(tile.DefId);
            int fw = def?.FootprintW ?? 1;
            int fh = def?.FootprintH ?? 1;

            for (int dy = 0; dy < fh; dy++)
                for (int dx = 0; dx < fw; dx++)
                    occ._cells[(tile.X + dx, tile.Y + dy)] = tile;
        }

        return occ;
    }

    /// <summary>The whole tile occupying this cell, or null.</summary>
    public PlacedTile TileAt(int x, int y) => _cells.TryGetValue((x, y), out var t) ? t : null;

    /// <summary>True iff every footprint cell for `def` anchored at (x,y) is inside the
    /// grid AND unoccupied (GDD §2.3: "placement is rejected if any footprint cell is
    /// already occupied, or the footprint would extend outside the grid").</summary>
    public bool CanPlace(TileDef def, int x, int y, int gridW, int gridH)
    {
        int fw = def?.FootprintW ?? 1;
        int fh = def?.FootprintH ?? 1;

        if (x < 0 || y < 0 || x + fw > gridW || y + fh > gridH) return false;

        for (int dy = 0; dy < fh; dy++)
            for (int dx = 0; dx < fw; dx++)
                if (_cells.ContainsKey((x + dx, y + dy))) return false;

        return true;
    }
}

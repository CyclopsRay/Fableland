namespace Fableland.MapCreation.Data;

using System;

/// <summary>
/// "Is the cell over there part of the same connected-look group?" — the one neighbour rule
/// behind every autotile lookup, shared by the editor canvas and the play view so the two
/// cannot classify the same cell differently.
///
/// Edge rule: a layer's grid is a window onto the world, not its limit. Terrain is taken to
/// continue for one cell past the LEFT, RIGHT and BOTTOM edges (as a copy of the edge cell),
/// so a floor that runs off the bottom of the map is not drawn with a ragged underside, and a
/// wall that runs off the side is not given an exposed face at the screen edge. Above the TOP
/// edge is always open air. Only one virtual cell is granted, so the classifier's depth walk
/// still terminates on its own.
/// </summary>
public static class AutotileProbe
{
    public static bool SameGroup(LayerOccupancy occupancy, int gridW, int gridH, int x, int y,
        string group, Func<string, TileDef> lookup)
    {
        if (occupancy == null || string.IsNullOrEmpty(group) || lookup == null) return false;
        if (y < 0 || x < -1 || x > gridW || y > gridH) return false;

        int cx = Math.Clamp(x, 0, Math.Max(0, gridW - 1));
        int cy = Math.Min(y, Math.Max(0, gridH - 1));
        PlacedTile tile = occupancy.TileAt(cx, cy);
        if (tile == null) return false;
        TileDef def = lookup(tile.DefId);
        return def != null && def.AutotileGroup == group;
    }

    /// <summary>Structural guard for the edge rule.</summary>
    public static System.Collections.Generic.List<string> SelfTest()
    {
        var failures = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("AutotileProbe mismatch: " + what); }

        var rock = new TileDef { Id = "t.rock", AutotileGroup = "terrain.t" };
        var other = new TileDef { Id = "t.other", AutotileGroup = "terrain.u" };
        Func<string, TileDef> lookup = id => id == rock.Id ? rock : id == other.Id ? other : null;

        // A 3x3 layer: rock along the bottom row, something else in the top-left corner.
        var layer = new MapLayerData { GridW = 3, GridH = 3 };
        for (int x = 0; x < 3; x++) layer.Tiles.Add(new PlacedTile { DefId = rock.Id, X = x, Y = 2 });
        layer.Tiles.Add(new PlacedTile { DefId = other.Id, X = 0, Y = 0 });
        var occ = LayerOccupancy.Build(layer, lookup);
        bool Same(int x, int y) => SameGroup(occ, 3, 3, x, y, "terrain.t", lookup);

        Check(Same(1, 2), "in-grid same group");
        Check(!Same(1, 1), "in-grid empty cell");
        Check(!Same(0, 0), "in-grid other group");
        Check(Same(1, 3), "one cell below the bottom edge continues the floor");
        Check(!Same(1, 4), "two cells below the bottom edge is nothing (the depth walk must end)");
        Check(Same(-1, 2) && Same(3, 2), "one cell past either side continues the terrain");
        Check(!Same(-2, 2) && !Same(4, 2), "two cells past a side is nothing");
        Check(!Same(1, -1), "above the top edge is open air");

        // The point of the rule: a floor on the map's bottom row is not classified as an underside.
        HillAutotile.Classify((dx, dy) => Same(1 + dx, 2 + dy), out var layerKind, out var position);
        Check(layerKind == HillAutotile.HillLayer.Layer1 && position == HillAutotile.HillPosition.Mid,
            "bottom-row floor cell is a surface Mid, not an exposed Layer 4");
        return failures;
    }
}

namespace Fableland.MapCreation.Data;

/// <summary>
/// GDD §2.5 — best-effort v1 atlas-region table for <see cref="TileDef.AutotileGroup"/>-tagged
/// ground tiles (`Docs/Art/BeachTileSet.md`'s beach terrain atlas). This is deliberately NOT a
/// full 16-tile bitmask blob set: Fableland is a 2.5D SIDE-VIEW arena fighter, so the dominant
/// visual cue for a ground cell is binary — is there open air directly above it (the surface
/// cap) or not (fully buried fill)? Left/right/bottom edges fall back to the interior region as
/// an approximation rather than guessing at specific corner cells.
///
/// Authored without a Godot editor available to visually confirm the atlas grid (no toolchain in
/// this dev environment) — the source image was read visually and counted as a 7-column x 6-row
/// grid, sand terrain occupying row 0's block and grass row 3's (`Docs/Art/BeachTileSet.md`'s
/// row order). Coordinates are DERIVED from <see cref="Cols"/>/<see cref="Rows"/>, never
/// hand-typed pixel offsets, so if that cell count is wrong once someone opens the actual atlas
/// in Godot, fixing it is a one-constant edit here, not a re-type of every region.
/// </summary>
public static class AutotileAtlas
{
    public const int Cols = 7;
    public const int Rows = 6;

    private const int SandRow = 0;
    private const int GrassRow = 3;

    private const int InteriorCol = 0; // full block, every orthogonal neighbor same-group
    private const int TopEdgeCol = 1;  // flat cap over fill, north neighbor NOT same-group

    /// <summary>Row/col of the atlas cell to use for `autotileGroup`, given whether the cell
    /// directly north (above) is also same-group. False (row/col left at 0) for an unknown
    /// group — caller should fall back to a flat quad rather than draw a wrong region.</summary>
    public static bool TryGetCell(string autotileGroup, bool northSameGroup, out int row, out int col)
    {
        int baseRow = autotileGroup switch
        {
            "terrain.beach_sand" => SandRow,
            "terrain.coastal_grass" => GrassRow,
            _ => -1,
        };

        if (baseRow < 0)
        {
            row = 0;
            col = 0;
            return false;
        }

        row = baseRow;
        col = northSameGroup ? InteriorCol : TopEdgeCol;
        return true;
    }
}

namespace Fableland.MapCreation.Data;

/// <summary>
/// The authoring/runtime scale of one map cell. Map cells are deliberately larger than a
/// physical metre: Pomegraknight's 8 m ground jump spans four 2 m cells, keeping combat
/// traversal readable without changing character physics.
/// </summary>
public static class MapGrid
{
    public const float MetersPerCell = 2f;

    /// <summary>64 px at the project's 32 px/m physical scale. Use this for every map-cell
    /// coordinate, footprint, effect area, grid line, and runtime map placement.</summary>
    public static readonly float PixelsPerCell = Units.Px(MetersPerCell);
}

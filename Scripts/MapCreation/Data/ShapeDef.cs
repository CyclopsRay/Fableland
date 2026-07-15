namespace Fableland.MapCreation.Data;

/// <summary>
/// GDD §2.4 — effect areas. A TileDef's runtime collider/trigger shape, authored
/// in pixels relative to the tile's anchor cell's top-left corner. ShapeDef only
/// ever lives inside code-defined TileDefs (never serialized into a map file), so
/// this stays a simple, immutable value type with static factory helpers rather
/// than a full serializable model.
/// </summary>
public sealed class ShapeDef
{
    public const string KindRect = "rect";
    public const string KindCircle = "circle";
    public const string KindPolygon = "polygon";

    /// <summary>4×4 sub-cell grid (each sub-cell = 16 px = MapGrid.PixelsPerCell/4). The effect
    /// area is the union of the "on" sub-cells; see <see cref="Mask"/>. This is the
    /// authoring model the per-tile-kind effect painter (GDD §2.4) produces.</summary>
    public const string KindSubcellMask = "subcellMask";

    /// <summary>Number of sub-cells per axis for <see cref="KindSubcellMask"/> (4 → 16 px cells).</summary>
    public const int SubcellsPerAxis = 4;

    /// <summary>"rect" | "circle" | "polygon" | "subcellMask".</summary>
    public string Kind { get; }

    // rect: top-left offset + size. circle: center offset + Radius.
    public float OffsetX { get; }
    public float OffsetY { get; }

    // rect only.
    public float W { get; }
    public float H { get; }

    // circle only.
    public float Radius { get; }

    // polygon only: flat x,y pairs, relative to the anchor cell's top-left.
    public float[] Points { get; }

    /// <summary>subcellMask only: 16-bit grid, bit (row*4 + col) set = that 16 px sub-cell
    /// of the anchor cell is part of the effect area (row/col from the cell's top-left).</summary>
    public int Mask { get; }

    private ShapeDef(string kind, float offsetX, float offsetY, float w, float h, float radius, float[] points, int mask)
    {
        Kind = kind;
        OffsetX = offsetX;
        OffsetY = offsetY;
        W = w;
        H = h;
        Radius = radius;
        Points = points;
        Mask = mask;
    }

    public static ShapeDef Rect(float offsetX, float offsetY, float w, float h) =>
        new(KindRect, offsetX, offsetY, w, h, 0f, null, 0);

    public static ShapeDef Circle(float offsetX, float offsetY, float radius) =>
        new(KindCircle, offsetX, offsetY, 0f, 0f, radius, null, 0);

    public static ShapeDef Polygon(float[] points) =>
        new(KindPolygon, 0f, 0f, 0f, 0f, 0f, points, 0);

    /// <summary>A 4×4 sub-cell effect area (GDD §2.4). <paramref name="mask"/> uses bit
    /// (row*4 + col) for the 16 px sub-cell at that row/col of the anchor cell.</summary>
    public static ShapeDef SubcellMask(int mask) =>
        new(KindSubcellMask, 0f, 0f, 0f, 0f, 0f, null, mask & 0xFFFF);

    /// <summary>Full 4×4 mask (all 16 sub-cells on) — a whole-cell effect area expressed
    /// as a mask, the default the effect painter opens with for a footprint-rect tile.</summary>
    public const int FullMask = 0xFFFF;
}

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

    /// <summary>"rect" | "circle" | "polygon".</summary>
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

    private ShapeDef(string kind, float offsetX, float offsetY, float w, float h, float radius, float[] points)
    {
        Kind = kind;
        OffsetX = offsetX;
        OffsetY = offsetY;
        W = w;
        H = h;
        Radius = radius;
        Points = points;
    }

    public static ShapeDef Rect(float offsetX, float offsetY, float w, float h) =>
        new(KindRect, offsetX, offsetY, w, h, 0f, null);

    public static ShapeDef Circle(float offsetX, float offsetY, float radius) =>
        new(KindCircle, offsetX, offsetY, 0f, 0f, radius, null);

    public static ShapeDef Polygon(float[] points) =>
        new(KindPolygon, 0f, 0f, 0f, 0f, 0f, points);
}

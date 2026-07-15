namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// Pure geometry transforms for a placed tile's authored effect area. Tile definitions are
/// immutable and shared globally, so a placed instance's <c>FlipX</c> is applied at render/runtime
/// construction time rather than mutating its <see cref="ShapeDef"/>.
/// </summary>
public static class EffectAreaTransform
{
    /// <summary>
    /// Reflects a shape horizontally inside an authored tile of <paramref name="tileWidthPx"/>
    /// pixels. Polygon point order is reversed as it is mirrored, preserving its original winding
    /// for collision backends that care about it.
    /// </summary>
    public static ShapeDef FlipHorizontally(ShapeDef shape, float tileWidthPx)
    {
        if (shape == null) return null;
        float width = Math.Max(0f, tileWidthPx);
        return shape.Kind switch
        {
            ShapeDef.KindRect => ShapeDef.Rect(width - shape.OffsetX - shape.W, shape.OffsetY, shape.W, shape.H),
            ShapeDef.KindCircle => ShapeDef.Circle(width - shape.OffsetX, shape.OffsetY, shape.Radius),
            ShapeDef.KindPolygon => FlipPolygon(shape, width),
            ShapeDef.KindSubcellMask => ShapeDef.SubcellMask(FlipMaskColumns(shape.Mask)),
            _ => shape,
        };
    }

    /// <summary>
    /// Reflects a row-major footprint array of 4×4 sub-cell masks. The source is normalized to
    /// <paramref name="footprintW"/> × <paramref name="footprintH"/> first so old/partial data
    /// remains safe to render and collide.
    /// </summary>
    public static int[] FlipMasksHorizontally(int[] masks, int footprintW, int footprintH)
    {
        int width = Math.Max(0, footprintW);
        int height = Math.Max(0, footprintH);
        var flipped = new int[width * height];
        if (masks == null) return flipped;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int source = y * width + x;
            if (source >= masks.Length) continue;
            int target = y * width + (width - 1 - x);
            flipped[target] = FlipMaskColumns(masks[source]);
        }
        return flipped;
    }

    /// <summary>Reflects a 4×4 sub-cell mask's columns while preserving its rows.</summary>
    public static int FlipMaskColumns(int mask)
    {
        int source = mask & ShapeDef.FullMask;
        int result = 0;
        for (int row = 0; row < ShapeDef.SubcellsPerAxis; row++)
        for (int column = 0; column < ShapeDef.SubcellsPerAxis; column++)
        {
            int sourceBit = row * ShapeDef.SubcellsPerAxis + column;
            if ((source & (1 << sourceBit)) == 0) continue;
            int targetBit = row * ShapeDef.SubcellsPerAxis +
                (ShapeDef.SubcellsPerAxis - 1 - column);
            result |= 1 << targetBit;
        }
        return result;
    }

    /// <summary>Small headless regression guard for the asymmetric cases that a sprite-only
    /// flip would miss. MapBrowser reports any failures in debug boot validation.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        const float epsilon = 0.001f;

        ShapeDef rect = FlipHorizontally(ShapeDef.Rect(8f, 5f, 12f, 7f), 64f);
        if (rect == null || Math.Abs(rect.OffsetX - 44f) > epsilon || Math.Abs(rect.OffsetY - 5f) > epsilon)
            failures.Add("effect-area horizontal flip did not mirror a rect offset");

        ShapeDef circle = FlipHorizontally(ShapeDef.Circle(12f, 9f, 4f), 64f);
        if (circle == null || Math.Abs(circle.OffsetX - 52f) > epsilon || Math.Abs(circle.OffsetY - 9f) > epsilon)
            failures.Add("effect-area horizontal flip did not mirror a circle center");

        ShapeDef polygon = FlipHorizontally(ShapeDef.Polygon(new[] { 0f, 0f, 16f, 0f, 8f, 20f }), 64f);
        if (polygon?.Points == null || polygon.Points.Length != 6 ||
            Math.Abs(polygon.Points[0] - 56f) > epsilon || Math.Abs(polygon.Points[1] - 20f) > epsilon ||
            Math.Abs(polygon.Points[4] - 64f) > epsilon || Math.Abs(polygon.Points[5]) > epsilon)
            failures.Add("effect-area horizontal flip did not mirror/re-wind a polygon");

        int[] masks = FlipMasksHorizontally(new[] { 1 << 0, 1 << 15 }, 2, 1);
        if (masks.Length != 2 || masks[0] != (1 << 12) || masks[1] != (1 << 3))
            failures.Add("effect-area horizontal flip did not mirror multi-cell sub-cell masks");

        return failures;
    }

    private static ShapeDef FlipPolygon(ShapeDef shape, float tileWidthPx)
    {
        if (shape.Points == null || shape.Points.Length < 6 || shape.Points.Length % 2 != 0)
            return shape;

        int pointCount = shape.Points.Length / 2;
        var flipped = new float[shape.Points.Length];
        for (int target = 0; target < pointCount; target++)
        {
            int source = pointCount - 1 - target;
            flipped[target * 2] = tileWidthPx - shape.Points[source * 2];
            flipped[target * 2 + 1] = shape.Points[source * 2 + 1];
        }
        return ShapeDef.Polygon(flipped);
    }
}

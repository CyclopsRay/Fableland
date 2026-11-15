namespace Fableland.MapCreation.Runtime;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>One collision shape of a placed tile, positioned relative to the tile's
/// top-left anchor cell corner.</summary>
public readonly struct MapCollisionSpec
{
    public readonly Shape2D Shape;
    public readonly Vector2 Position;
    public readonly Rect2 Bounds;

    public MapCollisionSpec(Shape2D shape, Vector2 position, Rect2 bounds)
    {
        Shape = shape;
        Position = position;
        Bounds = bounds;
    }

    /// <summary>The same shape moved by <paramref name="delta"/> (shapes are shared, not copied).</summary>
    public MapCollisionSpec Offset(Vector2 delta) =>
        new(Shape, Position + delta, new Rect2(Bounds.Position + delta, Bounds.Size));
}

/// <summary>
/// Turns a tile kind's effect area (GDD §2.4) into engine collision shapes. The editor
/// Playtest and the combat arena both build physics from this one function — they used to
/// carry identical private copies, so a fix to one silently missed the other.
/// </summary>
public static class MapCollision
{
    /// <summary>Shapes for one placed tile, honouring the per-kind painted sub-cell override
    /// (`TileEffectStore`) and the tile's horizontal flip across its whole footprint.</summary>
    public static List<MapCollisionSpec> BuildSpecs(TileDef def, bool flipX)
    {
        float cell = MapGrid.PixelsPerCell;
        if (TileEffectStore.HasOverride(def.Id) || def.EffectArea?.Kind == ShapeDef.KindSubcellMask)
        {
            int[] masks = TileEffectStore.OpeningMasksFor(def);
            if (flipX)
                masks = EffectAreaTransform.FlipMasksHorizontally(masks, def.FootprintW, def.FootprintH);
            var specs = new List<MapCollisionSpec>();
            float sub = cell / ShapeDef.SubcellsPerAxis;
            for (int cy = 0; cy < def.FootprintH; cy++)
            for (int cx = 0; cx < def.FootprintW; cx++)
            {
                int index = cy * def.FootprintW + cx;
                int mask = index < masks.Length ? masks[index] : 0;
                for (int row = 0; row < ShapeDef.SubcellsPerAxis; row++)
                for (int col = 0; col < ShapeDef.SubcellsPerAxis; col++)
                {
                    if ((mask & (1 << (row * ShapeDef.SubcellsPerAxis + col))) == 0) continue;
                    specs.Add(RectSpec(new Rect2(cx * cell + col * sub, cy * cell + row * sub, sub, sub)));
                }
            }
            return specs;
        }

        ShapeDef effect = def.EffectArea;
        if (flipX)
            effect = EffectAreaTransform.FlipHorizontally(effect, def.FootprintW * cell);
        Rect2 footprint = new(0f, 0f, def.FootprintW * cell, def.FootprintH * cell);
        if (effect == null)
            return new List<MapCollisionSpec> { RectSpec(footprint) };

        return effect.Kind switch
        {
            ShapeDef.KindRect => new List<MapCollisionSpec>
            {
                RectSpec(new Rect2(effect.OffsetX, effect.OffsetY, effect.W, effect.H)),
            },
            ShapeDef.KindCircle => new List<MapCollisionSpec>
            {
                CircleSpec(new Vector2(effect.OffsetX, effect.OffsetY), effect.Radius),
            },
            ShapeDef.KindPolygon => PolygonSpecs(effect.Points),
            _ => new List<MapCollisionSpec> { RectSpec(footprint) },
        };
    }

    public static Rect2 BoundsOf(IReadOnlyList<MapCollisionSpec> specs)
    {
        if (specs == null || specs.Count == 0) return new Rect2(Vector2.Zero, Vector2.One);
        Rect2 bounds = specs[0].Bounds;
        for (int i = 1; i < specs.Count; i++) bounds = bounds.Merge(specs[i].Bounds);
        return bounds;
    }

    private static MapCollisionSpec RectSpec(Rect2 rect) =>
        new(new RectangleShape2D { Size = rect.Size }, rect.GetCenter(), rect);

    private static MapCollisionSpec CircleSpec(Vector2 center, float radius)
    {
        float r = Mathf.Max(radius, 1f);
        return new MapCollisionSpec(new CircleShape2D { Radius = r }, center,
            new Rect2(center - new Vector2(r, r), new Vector2(r * 2f, r * 2f)));
    }

    private static List<MapCollisionSpec> PolygonSpecs(float[] points)
    {
        if (points == null || points.Length < 6 || points.Length % 2 != 0)
            return new List<MapCollisionSpec>();

        var polygon = new Vector2[points.Length / 2];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            polygon[i] = new Vector2(points[i * 2], points[i * 2 + 1]);
            minX = Mathf.Min(minX, polygon[i].X); minY = Mathf.Min(minY, polygon[i].Y);
            maxX = Mathf.Max(maxX, polygon[i].X); maxY = Mathf.Max(maxY, polygon[i].Y);
        }
        return new List<MapCollisionSpec>
        {
            new(new ConvexPolygonShape2D { Points = polygon }, Vector2.Zero,
                new Rect2(minX, minY, maxX - minX, maxY - minY)),
        };
    }
}

namespace Fableland.MapCreation.Runtime;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>One collision shape of a placed tile, positioned relative to the tile's
/// footprint corner — `TilePlacement.Origin`: its anchor cell's top-left, moved by the
/// tile's sub-cell nudge. Shapes are built in that local frame, so a nudge (a translation
/// of the frame) and a flip (a mirror inside it) cannot interfere.</summary>
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
    private const string SoftVolumeScenePath = "res://Scenes/SoftVolume.tscn";

    /// <summary>
    /// Builds the runtime SoftVolume of one placed tile: the reusable scene, this kind's
    /// tuning (drag, buoyancy, carry — <see cref="ApplyTuning"/>) and its shapes. The Playtest
    /// and the arena both call this; each used to carry its own copy, and a new tuning field
    /// would have reached only one of them. Returns null (after logging) when there is nothing
    /// to build. The caller parents the result.
    /// </summary>
    public static SoftVolume CreateSoftVolume(TileDef def, bool flipX, Vector2 origin,
        IReadOnlyList<MapCollisionSpec> specs, string logTag)
    {
        if (def == null || specs == null || specs.Count == 0) return null;
        PackedScene scene = ResourceLoader.Load<PackedScene>(SoftVolumeScenePath);
        if (scene == null)
        {
            GD.PushError($"[{logTag}] SoftVolume scene is unavailable.");
            return null;
        }

        var volume = scene.Instantiate<SoftVolume>();
        ApplyTuning(volume, def.SoftVolumeTuning, flipX);
        var shapes = new List<Shape2D>(specs.Count);
        var positions = new List<Vector2>(specs.Count);
        foreach (MapCollisionSpec spec in specs)
        {
            shapes.Add(spec.Shape);
            positions.Add(spec.Position);
        }
        volume.ConfigureRuntimeCollision(shapes, positions);
        volume.Position = origin;
        return volume;
    }

    /// <summary>Copies a tile kind's SoftVolume tuning onto the runtime node. Null tuning
    /// keeps the scene's defaults. A carry velocity is authored in metres per second and
    /// mirrored by the placed tile's horizontal flip.</summary>
    public static void ApplyTuning(SoftVolume volume, SoftVolumeTuning tuning, bool flipX)
    {
        if (volume == null || tuning == null) return;
        if (tuning.StagnationIndex is float stagnation) volume.StagnationIndex = stagnation;
        if (tuning.GravityScale is float gravityScale) volume.GravityScale = gravityScale;
        if (tuning.SinkSpeed is float sinkSpeed) volume.SinkSpeed = Units.Px(sinkSpeed);
        if (tuning.Drag is float drag) volume.Drag = drag;
        volume.IsMedium = tuning.Medium;
        if (!tuning.Medium)
        {
            // a pure current: no cap on the body's own speed, no drift, no buoyancy, no
            // extra damping — whatever else the kind's tuning says
            volume.StagnationIndex = 1f;
            volume.GravityIndex = 0f;
            volume.GravityScale = 1f;
            volume.SinkSpeed = 0f;
            volume.Drag = 1f;
            volume.ExternalDampingMult = 1f;
        }
        volume.Flow = new Vector2(Units.Px(flipX ? -tuning.FlowX : tuning.FlowX), Units.Px(tuning.FlowY));
    }

    /// <summary>Checks <see cref="ApplyTuning"/> on detached volumes: units, the flip, and
    /// that a pure current is stripped of everything but its carry.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        static bool Near(float a, float b) => Mathf.Abs(a - b) <= 0.001f;

        var plain = new SoftVolume();
        var water = new SoftVolume();
        var current = new SoftVolume();
        var flipped = new SoftVolume();
        try
        {
            float stagnation = plain.StagnationIndex, drift = plain.GravityIndex, damping = plain.ExternalDampingMult;

            ApplyTuning(plain, null, flipX: false);
            Check(Near(plain.StagnationIndex, stagnation) && Near(plain.GravityScale, 1f) && Near(plain.Drag, 1f) &&
                  Near(plain.SinkSpeed, 0f) && plain.IsMedium && plain.Flow == Vector2.Zero,
                "a tile kind without tuning must keep the SoftVolume scene's defaults");

            ApplyTuning(water, new SoftVolumeTuning { StagnationIndex = 0.55f, GravityScale = 0.35f, SinkSpeed = 3f, Drag = 5f }, flipX: true);
            Check(Near(water.StagnationIndex, 0.55f) && Near(water.GravityScale, 0.35f) && Near(water.Drag, 5f) && water.IsMedium,
                "water tuning must reach the volume as written");
            Check(Near(water.SinkSpeed, 3f * Units.PixelsPerMeter), "SinkSpeed is authored in m/s and must reach the volume in px/s");
            Check(Near(water.GravityIndex, drift) && Near(water.ExternalDampingMult, damping),
                "tuning must leave the settings it does not name at the scene's defaults");
            Check(water.Flow == Vector2.Zero, "a flip must not invent a carry");

            var carry = new SoftVolumeTuning { Medium = false, FlowX = 4f, FlowY = -7f, StagnationIndex = 0.2f, GravityScale = 0.3f, SinkSpeed = 2f, Drag = 6f };
            ApplyTuning(current, carry, flipX: false);
            ApplyTuning(flipped, carry, flipX: true);
            Check(current.Flow == new Vector2(4f, -7f) * Units.PixelsPerMeter, "a carry is authored in m/s (+x right, +y down) and must reach the volume in px/s");
            Check(flipped.Flow == new Vector2(-4f, -7f) * Units.PixelsPerMeter, "Flip H must mirror a carry's x and leave its y alone");
            Check(!current.IsMedium && Near(current.StagnationIndex, 1f) && Near(current.GravityIndex, 0f) &&
                  Near(current.GravityScale, 1f) && Near(current.SinkSpeed, 0f) && Near(current.Drag, 1f) &&
                  Near(current.ExternalDampingMult, 1f),
                "a pure current must carry and nothing else: no cap, no drift, no buoyancy, no extra damping");
        }
        finally
        {
            plain.Free();
            water.Free();
            current.Free();
            flipped.Free();
        }
        return failures;
    }

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

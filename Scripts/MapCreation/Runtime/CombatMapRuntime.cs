namespace Fableland.MapCreation.Runtime;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// Builds an authored MapDocument into a live combat scene. This is intentionally an
/// orchestration adapter: map data stays Godot-free, while the builder owns presentation,
/// collision, and the marker lists consumed by GameManager/FoeSpawner/missions.
/// </summary>
public static class CombatMapRuntime
{
    private const string SoftVolumeScenePath = "res://Scenes/SoftVolume.tscn";
    private const string FireHazardScenePath = "res://Scenes/FireHazard.tscn";
    private const string FreezeHazardScenePath = "res://Scenes/FreezeHazard.tscn";

    public static CombatMapBuild Build(MapDocument document, Node2D world, Node2D hazards)
    {
        var result = new CombatMapBuild();
        if (document?.Layers == null || world == null) return result;

        var visuals = new Node2D { Name = "AuthoredMapVisuals" };
        var physics = new Node2D { Name = "AuthoredMapPhysics" };
        world.AddChild(visuals);
        world.AddChild(physics);

        foreach (MapLayerData layer in document.Layers)
        {
            if (layer == null) continue;
            bool battlefield = layer.Role == MapLayerData.RoleBattlefield;
            if (battlefield)
            {
                result.WidthPx = layer.GridW * MapGrid.PixelsPerCell;
                result.HeightPx = layer.GridH * MapGrid.PixelsPerCell;
            }

            var visualLayer = new Node2D { Name = layer.Name };
            visualLayer.Modulate = LayerModulate(layer);
            visuals.AddChild(visualLayer);

            if (layer.Tiles == null) continue;
            foreach (PlacedTile tile in layer.Tiles)
            {
                if (!TileRegistry.TryGet(tile.DefId, out TileDef def) || def.Category == TileCategory.Rule)
                {
                    GD.PushWarning("[CombatMapRuntime] skipped unknown/rule tile '" + tile?.DefId + "'.");
                    continue;
                }

                if (!IsRuntimeMarker(def.Category)) visualLayer.AddChild(CreateTileVisual(tile, def));
                if (!battlefield) continue; // map combat markers and physics live only on Battlefield

                Vector2 origin = new(tile.X * MapGrid.PixelsPerCell, tile.Y * MapGrid.PixelsPerCell);
                Vector2 marker = new((tile.X + 0.5f) * MapGrid.PixelsPerCell,
                    (tile.Y + 1f) * MapGrid.PixelsPerCell - Units.Px(1f));

                switch (def.Category)
                {
                    case TileCategory.Character:
                        result.CharacterSpawns.Add(marker);
                        break;
                    case TileCategory.Respawn:
                        result.RespawnSpawns.Add(marker);
                        break;
                    case TileCategory.EnemySpawn:
                        result.EnemySpawns.Add(new CombatMapSpawnPoint(marker, tile.Y));
                        break;
                    case TileCategory.LevelGoal:
                        result.LevelGoalSpawns.Add(marker);
                        break;
                    case TileCategory.Ground:
                        CreateStaticCollider(physics, "Ground", origin, BuildCollisionSpecs(def, tile.FlipX), Units.LayerGround, false);
                        break;
                    case TileCategory.Platform:
                        CreateStaticCollider(physics, "Platform", origin, BuildCollisionSpecs(def, tile.FlipX), Units.LayerPlatform, true);
                        break;
                    case TileCategory.SoftVolume:
                        CreateSoftVolume(physics, def, origin, BuildCollisionSpecs(def, tile.FlipX));
                        break;
                    case TileCategory.Hazard:
                        CreateHazard(hazards, def, origin, BuildCollisionSpecs(def, tile.FlipX));
                        break;
                }
            }
        }

        if (result.WidthPx <= 0f) result.WidthPx = MapGrid.PixelsPerCell * MapLayerData.DefaultGridWidth;
        if (result.HeightPx <= 0f) result.HeightPx = MapGrid.PixelsPerCell * MapLayerData.DefaultGridHeight;
        return result;
    }

    private static bool IsRuntimeMarker(TileCategory category) => category == TileCategory.EnemySpawn ||
        category == TileCategory.Respawn || category == TileCategory.LevelGoal || category == TileCategory.Character;

    private static Color LayerModulate(MapLayerData layer)
    {
        Color tint = ColorFromHex(layer.Tint, Colors.White);
        tint.A *= Mathf.Clamp(layer.Opacity, 0f, 1f);
        return tint;
    }

    private static Node2D CreateTileVisual(PlacedTile tile, TileDef def)
    {
        float cell = MapGrid.PixelsPerCell;
        Vector2 footprint = new(def.FootprintW * cell, def.FootprintH * cell);
        var host = new Node2D
        {
            Name = def.Id.Replace('.', '_'),
            Position = new Vector2(tile.X * cell, tile.Y * cell),
        };
        Texture2D texture = string.IsNullOrWhiteSpace(def.SpriteSlot) ? null : ResourceLoader.Load<Texture2D>(def.SpriteSlot);
        if (texture == null)
        {
            host.AddChild(new Polygon2D
            {
                Polygon = new[] { Vector2.Zero, new Vector2(footprint.X, 0f), footprint, new Vector2(0f, footprint.Y) },
                Color = ColorFromHex(def.EditorColor, Colors.Magenta),
            });
            return host;
        }

        Vector2 source = texture.GetSize();
        if (source.X <= 0f || source.Y <= 0f) source = Vector2.One;
        Vector2 drawSize = def.SpriteFillFootprint
            ? footprint
            : source * Mathf.Min(footprint.X / source.X, footprint.Y / source.Y);
        host.AddChild(new Sprite2D
        {
            Texture = texture,
            Centered = false,
            FlipH = tile.FlipX,
            Position = new Vector2((footprint.X - drawSize.X) * 0.5f, footprint.Y - drawSize.Y),
            Scale = drawSize / source,
        });
        return host;
    }

    private static void CreateStaticCollider(Node2D parent, string name, Vector2 origin,
        IReadOnlyList<CollisionSpec> specs, int layer, bool oneWay)
    {
        if (specs.Count == 0) return;
        var body = new StaticBody2D { Name = name, Position = origin, CollisionMask = 0 };
        body.SetCollisionLayerValue(layer, true);
        parent.AddChild(body);
        foreach (CollisionSpec spec in specs)
            body.AddChild(new CollisionShape2D { Shape = spec.Shape, Position = spec.Position, OneWayCollision = oneWay });
    }

    private static void CreateSoftVolume(Node2D parent, TileDef def, Vector2 origin, IReadOnlyList<CollisionSpec> specs)
    {
        if (specs.Count == 0) return;
        PackedScene scene = ResourceLoader.Load<PackedScene>(SoftVolumeScenePath);
        if (scene == null) { GD.PushError("[CombatMapRuntime] SoftVolume scene is unavailable."); return; }
        var volume = scene.Instantiate<SoftVolume>();
        if (def.SoftVolumeTuning?.StagnationIndex is float stagnation) volume.StagnationIndex = stagnation;
        var shapes = new List<Shape2D>(specs.Count);
        var positions = new List<Vector2>(specs.Count);
        foreach (CollisionSpec spec in specs) { shapes.Add(spec.Shape); positions.Add(spec.Position); }
        volume.ConfigureRuntimeCollision(shapes, positions);
        volume.Position = origin;
        parent.AddChild(volume);
    }

    private static void CreateHazard(Node2D parent, TileDef def, Vector2 origin, IReadOnlyList<CollisionSpec> specs)
    {
        if (parent == null || specs.Count == 0) return;
        string scenePath = def.Id switch
        {
            "hazard.bonfire" => FireHazardScenePath,
            "hazard.freezepit" => FreezeHazardScenePath,
            _ => null,
        };
        if (scenePath == null)
        {
            GD.PushWarning("[CombatMapRuntime] no runtime hazard is registered for '" + def.Id + "'.");
            return;
        }
        PackedScene scene = ResourceLoader.Load<PackedScene>(scenePath);
        if (scene == null) { GD.PushError("[CombatMapRuntime] hazard scene is unavailable: " + scenePath); return; }
        Rect2 bounds = BoundsOf(specs);
        var hazard = scene.Instantiate<Hazard>();
        var shapes = new List<Shape2D>(specs.Count);
        var positions = new List<Vector2>(specs.Count);
        foreach (CollisionSpec spec in specs) { shapes.Add(spec.Shape); positions.Add(spec.Position - bounds.GetCenter()); }
        hazard.ConfigureRuntimeCollision(shapes, positions, new Rect2(-bounds.Size * 0.5f, bounds.Size));
        hazard.Position = origin + bounds.GetCenter();
        parent.AddChild(hazard);
    }

    private static List<CollisionSpec> BuildCollisionSpecs(TileDef def, bool flipX)
    {
        float cell = MapGrid.PixelsPerCell;
        if (TileEffectStore.HasOverride(def.Id) || def.EffectArea?.Kind == ShapeDef.KindSubcellMask)
        {
            int[] masks = TileEffectStore.OpeningMasksFor(def);
            if (flipX) masks = EffectAreaTransform.FlipMasksHorizontally(masks, def.FootprintW, def.FootprintH);
            var specs = new List<CollisionSpec>();
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
        if (flipX) effect = EffectAreaTransform.FlipHorizontally(effect, def.FootprintW * cell);
        if (effect == null)
            return new List<CollisionSpec> { RectSpec(new Rect2(0f, 0f, def.FootprintW * cell, def.FootprintH * cell)) };
        return effect.Kind switch
        {
            ShapeDef.KindRect => new List<CollisionSpec> { RectSpec(new Rect2(effect.OffsetX, effect.OffsetY, effect.W, effect.H)) },
            ShapeDef.KindCircle => new List<CollisionSpec> { CircleSpec(new Vector2(effect.OffsetX, effect.OffsetY), effect.Radius) },
            ShapeDef.KindPolygon => PolygonSpecs(effect.Points),
            _ => new List<CollisionSpec> { RectSpec(new Rect2(0f, 0f, def.FootprintW * cell, def.FootprintH * cell)) },
        };
    }

    private static CollisionSpec RectSpec(Rect2 rect) => new(new RectangleShape2D { Size = rect.Size }, rect.GetCenter(), rect);
    private static CollisionSpec CircleSpec(Vector2 center, float radius)
    {
        float r = Mathf.Max(radius, 1f);
        return new CollisionSpec(new CircleShape2D { Radius = r }, center, new Rect2(center - new Vector2(r, r), new Vector2(r * 2f, r * 2f)));
    }

    private static List<CollisionSpec> PolygonSpecs(float[] points)
    {
        if (points == null || points.Length < 6 || points.Length % 2 != 0) return new List<CollisionSpec>();
        var polygon = new Vector2[points.Length / 2];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            polygon[i] = new Vector2(points[i * 2], points[i * 2 + 1]);
            minX = Mathf.Min(minX, polygon[i].X); minY = Mathf.Min(minY, polygon[i].Y);
            maxX = Mathf.Max(maxX, polygon[i].X); maxY = Mathf.Max(maxY, polygon[i].Y);
        }
        return new List<CollisionSpec> { new(new ConvexPolygonShape2D { Points = polygon }, Vector2.Zero, new Rect2(minX, minY, maxX - minX, maxY - minY)) };
    }

    private static Rect2 BoundsOf(IReadOnlyList<CollisionSpec> specs)
    {
        Rect2 bounds = specs[0].Bounds;
        for (int i = 1; i < specs.Count; i++) bounds = bounds.Merge(specs[i].Bounds);
        return bounds;
    }

    private static Color ColorFromHex(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return new Color(hex); }
        catch
        {
            GD.PushWarning("[CombatMapRuntime] invalid color '" + hex + "'; using fallback.");
            return fallback;
        }
    }

    private readonly struct CollisionSpec
    {
        public readonly Shape2D Shape;
        public readonly Vector2 Position;
        public readonly Rect2 Bounds;
        public CollisionSpec(Shape2D shape, Vector2 position, Rect2 bounds) { Shape = shape; Position = position; Bounds = bounds; }
    }
}

/// <summary>Runtime outputs of an authored combat-map build. Marker lists are in world pixels.</summary>
public sealed class CombatMapBuild
{
    public float WidthPx;
    public float HeightPx;
    public readonly List<Vector2> CharacterSpawns = new();
    public readonly List<Vector2> RespawnSpawns = new();
    public readonly List<CombatMapSpawnPoint> EnemySpawns = new();
    public readonly List<Vector2> LevelGoalSpawns = new();
}

/// <summary>An enemy marker carries both its physical position and authored grid row for
/// map-level spawn eligibility rules.</summary>
public readonly struct CombatMapSpawnPoint
{
    public readonly Vector2 Position;
    public readonly int CellY;
    public CombatMapSpawnPoint(Vector2 position, int cellY) { Position = position; CellY = cellY; }
}

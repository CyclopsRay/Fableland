namespace Fableland.MapCreation.Runtime;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// Builds an authored MapDocument into a live combat scene. This is intentionally an
/// orchestration adapter: map data stays Godot-free, while the builder owns presentation,
/// collision, and the marker lists consumed by GameManager/FoeSpawner/missions.
///
/// Artwork, collision shapes and hazards come from the builders shared with the editor
/// Playtest (<see cref="MapVisualBuilder"/>, <see cref="MapCollision"/>,
/// <see cref="MapHazardFactory"/>), so a map plays in a run exactly as it played from the
/// editor's Play button.
/// </summary>
public static class CombatMapRuntime
{
    private const string LogTag = "CombatMapRuntime";
    private const string SoftVolumeScenePath = "res://Scenes/SoftVolume.tscn";

    public static CombatMapBuild Build(MapDocument document, Node2D world, Node2D hazards)
    {
        var result = new CombatMapBuild();
        if (document?.Layers == null || world == null) return result;

        var visuals = new Node2D { Name = "AuthoredMapVisuals" };
        var physics = new Node2D { Name = "AuthoredMapPhysics" };
        world.AddChild(visuals);
        world.AddChild(physics);

        result.ParallaxLayers.AddRange(MapVisualBuilder.Build(document, visuals, LogTag));

        foreach (MapLayerData layer in document.Layers)
        {
            // Map combat markers and physics live only on the Battlefield.
            if (layer == null || layer.Role != MapLayerData.RoleBattlefield) continue;

            result.WidthPx = layer.GridW * MapGrid.PixelsPerCell;
            result.HeightPx = layer.GridH * MapGrid.PixelsPerCell;
            if (layer.Tiles == null) continue;

            foreach (PlacedTile tile in layer.Tiles)
            {
                if (tile == null || !TileRegistry.TryGet(tile.DefId, out TileDef def) || def.Category == TileCategory.Rule)
                    continue;   // unknown ids were already reported by the visual pass

                // Anchor cell plus sub-cell nudge (GDD §2.3) — the same origin the art uses,
                // so a nudged platform is stood on exactly where it is drawn.
                (float originX, float originY) = TilePlacement.Origin(tile, def);
                (float markerX, float markerY) = TilePlacement.Marker(tile, def);
                Vector2 origin = new(originX, originY);
                Vector2 marker = new(markerX, markerY);

                switch (def.Category)
                {
                    case TileCategory.Character:
                        result.CharacterSpawns.Add(marker);
                        break;
                    case TileCategory.Respawn:
                        result.RespawnSpawns.Add(marker);
                        break;
                    case TileCategory.EnemySpawn:
                        // The spawn-rule row is the anchor row: a nudge moves the point, not
                        // which authored row the nest belongs to.
                        result.EnemySpawns.Add(new CombatMapSpawnPoint(marker, tile.Y));
                        break;
                    case TileCategory.LevelGoal:
                        result.LevelGoalSpawns.Add(marker);
                        break;
                    case TileCategory.Ground:
                        CreateStaticCollider(physics, "Ground", origin, MapCollision.BuildSpecs(def, tile.FlipX), Units.LayerGround, false);
                        break;
                    case TileCategory.Platform:
                        CreateStaticCollider(physics, "Platform", origin, MapCollision.BuildSpecs(def, tile.FlipX), Units.LayerPlatform, true);
                        break;
                    case TileCategory.SoftVolume:
                        CreateSoftVolume(physics, def, origin, MapCollision.BuildSpecs(def, tile.FlipX));
                        break;
                    // Hazards are planned per layer below so touching pool tiles can merge.
                }
            }

            if (hazards == null) continue;
            foreach (MapHazardPlacement placement in MapHazardFactory.Plan(layer.Tiles))
            {
                Hazard hazard = MapHazardFactory.Create(placement, LogTag);
                if (hazard != null) hazards.AddChild(hazard);
            }
        }

        if (result.WidthPx <= 0f) result.WidthPx = MapGrid.PixelsPerCell * MapLayerData.DefaultGridWidth;
        if (result.HeightPx <= 0f) result.HeightPx = MapGrid.PixelsPerCell * MapLayerData.DefaultGridHeight;
        return result;
    }

    private static void CreateStaticCollider(Node2D parent, string name, Vector2 origin,
        IReadOnlyList<MapCollisionSpec> specs, int layer, bool oneWay)
    {
        if (specs.Count == 0) return;
        var body = new StaticBody2D { Name = name, Position = origin, CollisionMask = 0 };
        body.SetCollisionLayerValue(layer, true);
        parent.AddChild(body);
        foreach (MapCollisionSpec spec in specs)
            body.AddChild(new CollisionShape2D { Shape = spec.Shape, Position = spec.Position, OneWayCollision = oneWay });
    }

    private static void CreateSoftVolume(Node2D parent, TileDef def, Vector2 origin, IReadOnlyList<MapCollisionSpec> specs)
    {
        if (specs.Count == 0) return;
        PackedScene scene = ResourceLoader.Load<PackedScene>(SoftVolumeScenePath);
        if (scene == null) { GD.PushError("[CombatMapRuntime] SoftVolume scene is unavailable."); return; }
        var volume = scene.Instantiate<SoftVolume>();
        if (def.SoftVolumeTuning?.StagnationIndex is float stagnation) volume.StagnationIndex = stagnation;
        var shapes = new List<Shape2D>(specs.Count);
        var positions = new List<Vector2>(specs.Count);
        foreach (MapCollisionSpec spec in specs) { shapes.Add(spec.Shape); positions.Add(spec.Position); }
        volume.ConfigureRuntimeCollision(shapes, positions);
        volume.Position = origin;
        parent.AddChild(volume);
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

    /// <summary>The map's art layers, back to front. Call <see cref="AnchorParallax"/> once
    /// the entering character has been placed.</summary>
    public readonly List<MapParallaxLayer> ParallaxLayers = new();

    /// <summary>Fixes the world point where authored tile positions are visually exact —
    /// the character spawn — for every layer (MapCreation.gdd §1 "Playtest parallax anchor";
    /// the same rule now holds in a real run).</summary>
    public void AnchorParallax(Vector2 anchorWorld)
    {
        foreach (MapParallaxLayer layer in ParallaxLayers) layer.SetAnchor(anchorWorld);
    }
}

/// <summary>An enemy marker carries both its physical position and authored grid row for
/// map-level spawn eligibility rules.</summary>
public readonly struct CombatMapSpawnPoint
{
    public readonly Vector2 Position;
    public readonly int CellY;
    public CombatMapSpawnPoint(Vector2 position, int cellY) { Position = position; CellY = cellY; }
}

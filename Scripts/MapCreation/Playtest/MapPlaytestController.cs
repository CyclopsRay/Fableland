namespace Fableland.MapCreation.Playtest;

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;

/// <summary>
/// An isolated, disposable runtime for Map Creation's Play button (MapCreation.gdd §7.1a).
/// It consumes the in-memory document without mutating it: visuals are parallaxed, while
/// gameplay nodes are constructed once in absolute world space. This keeps Farview magic
/// SoftVolumes deterministic even though their artwork moves with the camera.
///
/// Artwork, collision shapes and hazards come from the builders shared with the combat
/// arena (`Scripts/MapCreation/Runtime/`), so what plays here is what plays in a run.
/// </summary>
public partial class MapPlaytestController : Node2D
{
    private const string LogTag = "MapPlaytest";
    private const string PomegraknightScenePath = "res://Scenes/Pomegraknight.tscn";
    private const string SoftVolumeScenePath = "res://Scenes/SoftVolume.tscn";

    private MapDocument _document;
    private Node2D _worldVisual;
    private Node2D _physics;
    private Node2D _hazards;
    private Node2D _entities;
    private ArenaEnvironmentController _environment;
    private CharacterController _player;
    private readonly List<MapParallaxLayer> _visualLayers = new();
    private float _battlefieldWidth;
    private float _battlefieldHeight;
    private bool _escWasDown;

    /// <summary>Raised on Escape. The editor owns disposal and restoring its chrome.</summary>
    public event Action ExitRequested;

    /// <summary>Must be called before the controller is added to the scene tree.</summary>
    public void Initialize(MapDocument document)
    {
        if (IsInsideTree())
        {
            GD.PushError("MapPlaytestController: Initialize must run before AddChild.");
            return;
        }
        _document = document;
    }

    public override void _Ready()
    {
        if (_document?.Layers == null)
        {
            GD.PushError("MapPlaytestController: cannot playtest a map without layers.");
            ExitRequested?.Invoke();
            return;
        }

        Name = "MapPlaytest";
        BuildCanvas();

        _worldVisual = new Node2D { Name = "WorldVisual" };
        _physics = new Node2D { Name = "Physics" };
        _hazards = new Node2D { Name = "Hazards" };
        _entities = new Node2D { Name = "Entities" };
        AddChild(_worldVisual);
        AddChild(_physics);
        AddChild(_hazards);
        AddChild(_entities);

        // The controller enters before authored trigger tiles. They register themselves
        // immediately after instantiation, sharing the normal Arena event lifecycle.
        _environment = new ArenaEnvironmentController
        {
            Name = "Environment",
            CanvasPath = "../Canvas/Background",
            WorldVisualPath = "../WorldVisual",
            HazardsPath = "../Hazards",
            TsunamiTriggerPath = "",
        };
        AddChild(_environment);

        BuildVisualMap();
        BuildPhysicsMap();
        SpawnPomegraknight();
        BuildPlaytestHint();
        _escWasDown = Input.IsPhysicalKeyPressed(Key.Escape);
    }

    public override void _Process(double delta)
    {
        // This is presentation/session input rather than simulation input. It deliberately
        // stays out of _PhysicsProcess (T10 input rule) and detects the edge so a held Escape
        // that launched Play does not immediately close it.
        bool escapeDown = Input.IsPhysicalKeyPressed(Key.Escape);
        if (escapeDown && !_escWasDown)
        {
            _escWasDown = true;
            ExitRequested?.Invoke();
            return;
        }
        _escWasDown = escapeDown;
    }

    private void BuildCanvas()
    {
        var canvas = new CanvasLayer { Name = "Canvas", Layer = -1 };
        var background = new ColorRect
        {
            Name = "Background",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = ColorFromHex(_document.Canvas?.Color, new Color("87CEEB")),
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        canvas.AddChild(background);
        AddChild(canvas);
    }

    private void BuildPlaytestHint()
    {
        var hud = new CanvasLayer { Name = "PlaytestHud", Layer = 2 };
        var hint = new Label
        {
            Text = "PLAYTEST  ·  Esc: return to editor",
            Position = new Vector2(18f, 14f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hint.AddThemeColorOverride("font_color", Colors.White);
        hint.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        hint.AddThemeConstantOverride("outline_size", 5);
        hud.AddChild(hint);
        AddChild(hud);
    }

    private void BuildVisualMap()
    {
        foreach (MapLayerData layer in _document.Layers)
        {
            if (layer?.Role != MapLayerData.RoleBattlefield) continue;
            _battlefieldWidth = layer.GridW * MapGrid.PixelsPerCell;
            _battlefieldHeight = layer.GridH * MapGrid.PixelsPerCell;
        }

        // Presentation loops repeat every kind of artwork. Physics is built by the separate
        // fixed-world pass below, so a repeated cloud sprite never implies repeated collision.
        _visualLayers.AddRange(MapVisualBuilder.Build(_document, _worldVisual, LogTag));
    }

    private void BuildPhysicsMap()
    {
        foreach (MapLayerData layer in _document.Layers)
        {
            if (layer?.Tiles == null) continue;
            bool isBattlefield = layer.Role == MapLayerData.RoleBattlefield;
            bool canBuildBattlefieldPhysics = isBattlefield && layer.Collision;
            bool canBuildFarviewSoftVolumes = layer.Role == MapLayerData.RoleFarview &&
                layer.Collision && !layer.Loop;

            foreach (PlacedTile tile in layer.Tiles)
            {
                if (!TileRegistry.TryGet(tile.DefId, out TileDef def))
                    continue;   // unknown ids were already reported by the visual pass

                bool canBuildSoftVolume = def.Category == TileCategory.SoftVolume &&
                    (canBuildBattlefieldPhysics || canBuildFarviewSoftVolumes);
                if (!canBuildBattlefieldPhysics && !canBuildSoftVolume) continue;
                if (def.Category == TileCategory.Hazard) continue;   // planned per layer below

                (float originX, float originY) = TilePlacement.Origin(tile, def);   // cell + nudge (§2.3)
                Vector2 origin = new(originX, originY);
                List<MapCollisionSpec> shapes = MapCollision.BuildSpecs(def, tile.FlipX);
                if (shapes.Count == 0) continue;

                switch (def.Category)
                {
                    case TileCategory.Ground when canBuildBattlefieldPhysics:
                        CreateStaticCollider("Ground", origin, shapes, Units.LayerGround, oneWay: false);
                        break;
                    case TileCategory.Platform when canBuildBattlefieldPhysics:
                        CreateStaticCollider("Platform", origin, shapes, Units.LayerPlatform, oneWay: true);
                        break;
                    case TileCategory.SoftVolume when canBuildSoftVolume:
                        CreateSoftVolume(def, origin, shapes);
                        break;
                }
            }

            if (!canBuildBattlefieldPhysics) continue;
            foreach (MapHazardPlacement placement in MapHazardFactory.Plan(layer.Tiles))
            {
                if (placement.Specs.Count == 0) continue;
                if (MapHazardFactory.IsEventTrigger(placement.Def))
                {
                    CreateTsunamiTrigger(placement);
                    continue;
                }
                Hazard hazard = MapHazardFactory.Create(placement, LogTag);
                if (hazard != null) _hazards.AddChild(hazard);
            }
        }
    }

    private void CreateStaticCollider(string kind, Vector2 origin, IReadOnlyList<MapCollisionSpec> specs,
        int collisionLayer, bool oneWay)
    {
        var body = new StaticBody2D { Name = kind, Position = origin, CollisionMask = 0 };
        body.SetCollisionLayerValue(collisionLayer, true);
        _physics.AddChild(body);
        foreach (MapCollisionSpec spec in specs)
        {
            var shape = new CollisionShape2D { Shape = spec.Shape, Position = spec.Position, OneWayCollision = oneWay };
            body.AddChild(shape);
        }
    }

    private void CreateSoftVolume(TileDef def, Vector2 origin, IReadOnlyList<MapCollisionSpec> specs)
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(SoftVolumeScenePath);
        if (scene == null)
        {
            GD.PushError("MapPlaytest: SoftVolume scene is unavailable.");
            return;
        }

        var volume = scene.Instantiate<SoftVolume>();
        SoftVolumeTuning tuning = def.SoftVolumeTuning;
        if (tuning != null && tuning.StagnationIndex.HasValue)
            volume.StagnationIndex = tuning.StagnationIndex.Value;

        var shapes = new List<Shape2D>(specs.Count);
        var positions = new List<Vector2>(specs.Count);
        foreach (MapCollisionSpec spec in specs)
        {
            shapes.Add(spec.Shape);
            positions.Add(spec.Position);
        }
        volume.ConfigureRuntimeCollision(shapes, positions);
        volume.Position = origin;
        _physics.AddChild(volume);
    }

    /// <summary>An event-trigger tile (its def names the hazard it spawns in
    /// <c>Props["spawnHazard"]</c>): instantiate its scene and hand it to this playtest's
    /// environment controller, which owns the event's lifecycle.</summary>
    private void CreateTsunamiTrigger(MapHazardPlacement placement)
    {
        string scenePath = MapHazardFactory.SceneOf(placement.Def);
        PackedScene triggerScene = string.IsNullOrWhiteSpace(scenePath) ? null : ResourceLoader.Load<PackedScene>(scenePath);
        if (triggerScene == null)
        {
            GD.PushError($"MapPlaytest: trigger scene for '{placement.Def.Id}' is unavailable.");
            return;
        }

        Node instance = triggerScene.Instantiate();
        if (instance is not TsunamiTrigger trigger)
        {
            GD.PushWarning($"[MapPlaytest] no event controller is registered for trigger '{placement.Def.Id}'.");
            instance.Free();
            return;
        }

        Rect2 bounds = MapCollision.BoundsOf(placement.Specs);
        trigger.BoxSize = bounds.Size;
        trigger.Position = placement.Origin + bounds.GetCenter();
        trigger.SpawnPosition = new Vector2(
            Mathf.Max(_battlefieldWidth + Units.Px(8f), trigger.Position.X + Units.Px(8f)),
            trigger.Position.Y + bounds.Size.Y * 0.5f);
        _hazards.AddChild(trigger);
        _environment.RegisterTsunamiTrigger(trigger);
    }

    private void SpawnPomegraknight()
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(PomegraknightScenePath);
        if (scene == null)
        {
            GD.PushError("MapPlaytest: Pomegraknight scene is unavailable.");
            return;
        }

        _player = scene.Instantiate<CharacterController>();
        _player.Position = FindPlayerSpawn();
        _entities.AddChild(_player);

        // Parallax must follow the camera's real rendered center, rather than the player
        // body (which diverges during smoothing and screen shake). The spawn is the authored
        // playtest anchor: all layers line up with their absolute tile positions there.
        Camera2D camera = _player.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera == null)
        {
            GD.PushError("MapPlaytest: Pomegraknight is missing its Camera2D.");
            return;
        }
        camera.MakeCurrent();
        ConfigureCameraBounds(camera);
        foreach (MapParallaxLayer layer in _visualLayers)
            layer.SetAnchor(_player.GlobalPosition);
    }

    /// <summary>
    /// Replaces the prototype character scene's fixed Arena-sized camera limits with the
    /// authored Battlefield rectangle. Camera2D interprets these as its frame limits, so no
    /// extra inset/outset is applied here: the visible frame stays within the map and reaches
    /// the final map row rather than stopping at the old 720 px arena bottom.
    /// </summary>
    private void ConfigureCameraBounds(Camera2D camera)
    {
        if (_battlefieldWidth <= 0f || _battlefieldHeight <= 0f)
        {
            GD.PushError("MapPlaytest: cannot set camera bounds without a Battlefield layer.");
            return;
        }

        camera.LimitLeft = 0;
        camera.LimitTop = 0;
        camera.LimitRight = Mathf.CeilToInt(_battlefieldWidth);
        camera.LimitBottom = Mathf.CeilToInt(_battlefieldHeight);
    }

    private Vector2 FindPlayerSpawn()
    {
        // The same marker point a real run is handed (`CombatMapRuntime`): the spawn tile's
        // cell centre, a metre above its floor, moved by the tile's nudge.
        if (MapDocumentQueries.TryGetCharacterSpawnPoint(_document, out float spawnX, out float spawnY))
            return new Vector2(spawnX, spawnY);

        GD.PushWarning("[MapPlaytest] no battlefield Character Spawn; using the battlefield center.");
        return new Vector2(_battlefieldWidth * 0.5f, Units.Px(2f));
    }

    private static Color ColorFromHex(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        string value = hex.Trim().TrimStart('#');
        if (value.Length != 6 && value.Length != 8) return fallback;
        if (!int.TryParse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r) ||
            !int.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g) ||
            !int.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b)) return fallback;
        int a = 255;
        if (value.Length == 8 && !int.TryParse(value.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) return fallback;
        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }
}

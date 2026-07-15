namespace Fableland.MapCreation.Playtest;

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// An isolated, disposable runtime for Map Creation's Play button (MapCreation.gdd §7.1a).
/// It consumes the in-memory document without mutating it: visuals are parallaxed, while
/// gameplay nodes are constructed once in absolute world space. This keeps Farview magic
/// SoftVolumes deterministic even though their artwork moves with the camera.
/// </summary>
public partial class MapPlaytestController : Node2D
{
    private const string PomegraknightScenePath = "res://Scenes/Pomegraknight.tscn";
    private const string SoftVolumeScenePath = "res://Scenes/SoftVolume.tscn";
    private const string FireHazardScenePath = "res://Scenes/FireHazard.tscn";
    private const string FreezeHazardScenePath = "res://Scenes/FreezeHazard.tscn";
    private const string TsunamiTriggerScenePath = "res://Scenes/TsunamiTrigger.tscn";

    private MapDocument _document;
    private Node2D _worldVisual;
    private Node2D _physics;
    private Node2D _hazards;
    private Node2D _entities;
    private ArenaEnvironmentController _environment;
    private CharacterController _player;
    private readonly List<MapParallaxVisualLayer> _visualLayers = new();
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
        for (int i = 0; i < _document.Layers.Count; i++)
        {
            MapLayerData layer = _document.Layers[i];
            if (layer == null) continue;

            if (layer.Role == MapLayerData.RoleBattlefield)
            {
                _battlefieldWidth = layer.GridW * MapGrid.PixelsPerCell;
                _battlefieldHeight = layer.GridH * MapGrid.PixelsPerCell;
            }

            var runtimeLayer = new MapParallaxVisualLayer(layer);
            runtimeLayer.Modulate = LayerModulate(layer);
            _worldVisual.AddChild(runtimeLayer);
            _visualLayers.Add(runtimeLayer);

            if (layer.Tiles == null) continue;
            foreach (PlacedTile tile in layer.Tiles)
            {
                if (!TileRegistry.TryGet(tile.DefId, out TileDef def) || def.Category == TileCategory.Rule)
                    continue;

                Node2D visual = CreateTileVisual(tile, def);
                // Presentation loops repeat every kind of artwork. Physics is built by the
                // separate fixed-world pass below, so a repeated cloud sprite never implies
                // repeated collision.
                runtimeLayer.AddVisual(visual);
            }
        }
    }

    private static Color LayerModulate(MapLayerData layer)
    {
        Color tint = string.IsNullOrWhiteSpace(layer.Tint) ? Colors.White : ColorFromHex(layer.Tint, Colors.White);
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

        Texture2D texture = string.IsNullOrWhiteSpace(def.SpriteSlot)
            ? null
            : ResourceLoader.Load<Texture2D>(def.SpriteSlot);
        if (texture == null)
        {
            host.AddChild(new Polygon2D
            {
                Polygon = new[] { Vector2.Zero, new Vector2(footprint.X, 0f), footprint, new Vector2(0f, footprint.Y) },
                Color = ColorFromHex(def.EditorColor, Colors.Magenta),
            });
            return host;
        }

        Vector2 sourceSize = texture.GetSize();
        if (sourceSize.X <= 0f || sourceSize.Y <= 0f) sourceSize = Vector2.One;
        Vector2 drawSize;
        if (def.SpriteFillFootprint)
            drawSize = footprint;
        else
        {
            float scale = Mathf.Min(footprint.X / sourceSize.X, footprint.Y / sourceSize.Y);
            drawSize = sourceSize * scale;
        }

        host.AddChild(new Sprite2D
        {
            Texture = texture,
            Centered = false,
            FlipH = tile.FlipX,
            Position = new Vector2((footprint.X - drawSize.X) * 0.5f, footprint.Y - drawSize.Y),
            Scale = drawSize / sourceSize,
        });
        return host;
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
                {
                    GD.PushWarning($"[MapPlaytest] skipped unknown tile '{tile.DefId}'.");
                    continue;
                }

                bool canBuildSoftVolume = def.Category == TileCategory.SoftVolume &&
                    (canBuildBattlefieldPhysics || canBuildFarviewSoftVolumes);
                if (!canBuildBattlefieldPhysics && !canBuildSoftVolume) continue;

                Vector2 origin = new(tile.X * MapGrid.PixelsPerCell, tile.Y * MapGrid.PixelsPerCell);
                List<CollisionSpec> shapes = BuildCollisionSpecs(def, tile.FlipX);
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
                    case TileCategory.Hazard when canBuildBattlefieldPhysics:
                        CreateHazard(def, origin, shapes);
                        break;
                }
            }
        }
    }

    private void CreateStaticCollider(string kind, Vector2 origin, IReadOnlyList<CollisionSpec> specs,
        int collisionLayer, bool oneWay)
    {
        var body = new StaticBody2D { Name = kind, Position = origin, CollisionMask = 0 };
        body.SetCollisionLayerValue(collisionLayer, true);
        _physics.AddChild(body);
        foreach (CollisionSpec spec in specs)
        {
            var shape = new CollisionShape2D { Shape = spec.Shape, Position = spec.Position, OneWayCollision = oneWay };
            body.AddChild(shape);
        }
    }

    private void CreateSoftVolume(TileDef def, Vector2 origin, IReadOnlyList<CollisionSpec> specs)
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
        foreach (CollisionSpec spec in specs)
        {
            shapes.Add(spec.Shape);
            positions.Add(spec.Position);
        }
        volume.ConfigureRuntimeCollision(shapes, positions);
        volume.Position = origin;
        _physics.AddChild(volume);
    }

    private void CreateHazard(TileDef def, Vector2 origin, IReadOnlyList<CollisionSpec> specs)
    {
        Rect2 bounds = BoundsOf(specs);
        if (def.Id == "hazard.tsunami_trigger")
        {
            PackedScene triggerScene = ResourceLoader.Load<PackedScene>(TsunamiTriggerScenePath);
            if (triggerScene == null)
            {
                GD.PushError("MapPlaytest: TsunamiTrigger scene is unavailable.");
                return;
            }

            var trigger = triggerScene.Instantiate<TsunamiTrigger>();
            trigger.BoxSize = bounds.Size;
            trigger.Position = origin + bounds.GetCenter();
            trigger.SpawnPosition = new Vector2(
                Mathf.Max(_battlefieldWidth + Units.Px(8f), trigger.Position.X + Units.Px(8f)),
                trigger.Position.Y + bounds.Size.Y * 0.5f);
            _hazards.AddChild(trigger);
            _environment.RegisterTsunamiTrigger(trigger);
            return;
        }

        string scenePath = def.Id switch
        {
            "hazard.bonfire" => FireHazardScenePath,
            "hazard.freezepit" => FreezeHazardScenePath,
            _ => null,
        };
        if (scenePath == null)
        {
            GD.PushWarning($"[MapPlaytest] no runtime hazard is registered for '{def.Id}'.");
            return;
        }

        PackedScene scene = ResourceLoader.Load<PackedScene>(scenePath);
        if (scene == null)
        {
            GD.PushError($"MapPlaytest: hazard scene '{scenePath}' is unavailable.");
            return;
        }

        var hazard = scene.Instantiate<Hazard>();
        var shapes = new List<Shape2D>(specs.Count);
        var positions = new List<Vector2>(specs.Count);
        foreach (CollisionSpec spec in specs)
        {
            shapes.Add(spec.Shape);
            // The hazard's origin is the effect bounds center; its individual shapes remain
            // exact after converting authored anchor-space positions to that local origin.
            positions.Add(spec.Position - bounds.GetCenter());
        }
        hazard.ConfigureRuntimeCollision(shapes, positions, new Rect2(-bounds.Size * 0.5f, bounds.Size));
        hazard.Position = origin + bounds.GetCenter();
        _hazards.AddChild(hazard);
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
        foreach (MapParallaxVisualLayer layer in _visualLayers)
            layer.ConfigureCamera(camera, _player.GlobalPosition);
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
        float cell = MapGrid.PixelsPerCell;
        foreach (MapLayerData layer in _document.Layers)
        {
            if (layer?.Role != MapLayerData.RoleBattlefield || layer.Tiles == null) continue;
            foreach (PlacedTile tile in layer.Tiles)
            {
                if (tile.DefId != "spawn.character") continue;
                // Spawn tiles author the character's feet at the bottom of their cell.
                return new Vector2((tile.X + 0.5f) * cell, (tile.Y + 1f) * cell - Units.Px(1f));
            }
        }

        GD.PushWarning("[MapPlaytest] no battlefield Character Spawn; using the battlefield center.");
        return new Vector2(_battlefieldWidth * 0.5f, Units.Px(2f));
    }

    private static List<CollisionSpec> BuildCollisionSpecs(TileDef def, bool flipX)
    {
        float cell = MapGrid.PixelsPerCell;
        if (TileEffectStore.HasOverride(def.Id) || def.EffectArea?.Kind == ShapeDef.KindSubcellMask)
        {
            int[] masks = TileEffectStore.OpeningMasksFor(def);
            if (flipX)
                masks = EffectAreaTransform.FlipMasksHorizontally(masks, def.FootprintW, def.FootprintH);
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
                    Rect2 rect = new((cx * cell) + col * sub, (cy * cell) + row * sub, sub, sub);
                    specs.Add(RectSpec(rect));
                }
            }
            return specs;
        }

        ShapeDef effect = def.EffectArea;
        if (flipX)
            effect = EffectAreaTransform.FlipHorizontally(effect, def.FootprintW * cell);
        if (effect == null)
            return new List<CollisionSpec> { RectSpec(new Rect2(0f, 0f, def.FootprintW * cell, def.FootprintH * cell)) };

        return effect.Kind switch
        {
            ShapeDef.KindRect => new List<CollisionSpec>
            {
                RectSpec(new Rect2(effect.OffsetX, effect.OffsetY, effect.W, effect.H)),
            },
            ShapeDef.KindCircle => new List<CollisionSpec>
            {
                CircleSpec(new Vector2(effect.OffsetX, effect.OffsetY), effect.Radius),
            },
            ShapeDef.KindPolygon => PolygonSpecs(effect.Points),
            _ => new List<CollisionSpec> { RectSpec(new Rect2(0f, 0f, def.FootprintW * cell, def.FootprintH * cell)) },
        };
    }

    private static CollisionSpec RectSpec(Rect2 rect) => new(
        new RectangleShape2D { Size = rect.Size }, rect.GetCenter(), rect);

    private static CollisionSpec CircleSpec(Vector2 center, float radius)
    {
        float r = Mathf.Max(radius, 1f);
        return new CollisionSpec(new CircleShape2D { Radius = r }, center,
            new Rect2(center - new Vector2(r, r), new Vector2(r * 2f, r * 2f)));
    }

    private static List<CollisionSpec> PolygonSpecs(float[] points)
    {
        if (points == null || points.Length < 6 || points.Length % 2 != 0)
            return new List<CollisionSpec>();

        var polygon = new Vector2[points.Length / 2];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            polygon[i] = new Vector2(points[i * 2], points[i * 2 + 1]);
            minX = Mathf.Min(minX, polygon[i].X); minY = Mathf.Min(minY, polygon[i].Y);
            maxX = Mathf.Max(maxX, polygon[i].X); maxY = Mathf.Max(maxY, polygon[i].Y);
        }
        return new List<CollisionSpec>
        {
            new(new ConvexPolygonShape2D { Points = polygon }, Vector2.Zero,
                new Rect2(minX, minY, maxX - minX, maxY - minY)),
        };
    }

    private static Rect2 BoundsOf(IReadOnlyList<CollisionSpec> specs)
    {
        if (specs == null || specs.Count == 0) return new Rect2(Vector2.Zero, Vector2.One);
        float minX = specs[0].Bounds.Position.X, minY = specs[0].Bounds.Position.Y;
        float maxX = specs[0].Bounds.End.X, maxY = specs[0].Bounds.End.Y;
        for (int i = 1; i < specs.Count; i++)
        {
            minX = Mathf.Min(minX, specs[i].Bounds.Position.X);
            minY = Mathf.Min(minY, specs[i].Bounds.Position.Y);
            maxX = Mathf.Max(maxX, specs[i].Bounds.End.X);
            maxY = Mathf.Max(maxY, specs[i].Bounds.End.Y);
        }
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
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

    private readonly struct CollisionSpec
    {
        public readonly Shape2D Shape;
        public readonly Vector2 Position;
        public readonly Rect2 Bounds;

        public CollisionSpec(Shape2D shape, Vector2 position, Rect2 bounds)
        {
            Shape = shape;
            Position = position;
            Bounds = bounds;
        }
    }

    /// <summary>Presentation-only parallax transform. Its child positions stay authored;
    /// this node applies the active camera's anchored offset, auto-scroll, opacity/tint, and
    /// optional visual looping. Physics nodes never inherit this transform.</summary>
    private sealed partial class MapParallaxVisualLayer : Node2D
    {
        private readonly Vector2 _parallax;
        private readonly Vector2 _autoScroll;
        private readonly bool _loop;
        private readonly float _loopWidth;
        private readonly Node2D _singleVisuals = new() { Name = "SingleVisuals" };
        private readonly Node2D[] _loopVisuals;
        private double _time;

        private Camera2D _camera;
        private Vector2 _anchorWorld;

        public MapParallaxVisualLayer(MapLayerData data)
        {
            Name = data.Name;
            _parallax = new Vector2(data.ParallaxX, data.ParallaxY);
            _autoScroll = new Vector2(data.AutoScrollX, data.AutoScrollY);
            _loop = data.Loop && data.GridW > 0;
            _loopWidth = data.GridW * MapGrid.PixelsPerCell;
            AddChild(_singleVisuals);

            if (!_loop) return;
            _loopVisuals = new Node2D[3];
            for (int i = 0; i < _loopVisuals.Length; i++)
            {
                _loopVisuals[i] = new Node2D { Name = "LoopCopy" + i };
                AddChild(_loopVisuals[i]);
            }
        }

        public void AddVisual(Node2D visual)
        {
            if (!_loop)
            {
                _singleVisuals.AddChild(visual);
                return;
            }

            for (int i = 0; i < _loopVisuals.Length; i++)
            {
                Node2D copy = i == 1 ? visual : visual.Duplicate() as Node2D;
                if (copy != null) _loopVisuals[i].AddChild(copy);
            }
        }

        /// <summary>Sets the active camera and the world point at which authored tile positions
        /// must be visually exact. The spawn point is this playtest's anchor.</summary>
        public void ConfigureCamera(Camera2D camera, Vector2 anchorWorld)
        {
            _camera = camera;
            _anchorWorld = anchorWorld;
        }

        public override void _Process(double delta)
        {
            _time += delta;
            if (_camera == null || !IsInstanceValid(_camera)) return;

            Vector2 cameraWorld = _camera.GetScreenCenterPosition();
            Position = (cameraWorld - _anchorWorld) * (Vector2.One - _parallax) +
                _autoScroll * (float)_time;
            if (!_loop || _loopWidth <= 0f) return;

            // Screen space is tile + copyOffset - (camera*p + anchor*(1-p)). Center the
            // three visual copies around that same anchored parallax-space coordinate.
            float loopCenter = cameraWorld.X * _parallax.X + _anchorWorld.X * (1f - _parallax.X);
            int centerRepeat = Mathf.FloorToInt(loopCenter / _loopWidth);
            for (int i = 0; i < _loopVisuals.Length; i++)
                _loopVisuals[i].Position = new Vector2((centerRepeat + i - 1) * _loopWidth, 0f);
        }
    }
}

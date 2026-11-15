namespace Fableland.MapCreation.Runtime;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// Builds a map's artwork — every layer, back to front — for anything that PLAYS a map:
/// the editor's Playtest and the combat arena. One builder means one look: before v0.10.6
/// the two each had a private `CreateTileVisual`, neither consulted a ground atlas, and
/// only the Playtest applied parallax, so a realm's layered terrain and far scenery would
/// have appeared in neither the same way as in the editor nor the same way as each other.
///
/// Contracts it holds for callers:
///   • which art a tile shows comes only from <see cref="TileVisualResolver"/> (play view);
///   • a prop sprite is trimmed to its used rect, aspect-fitted in its footprint and
///     anchored bottom-centre — the same rule the editor canvas draws by, so transparent
///     export padding can never float a prop above its footprint floor;
///   • Closeview layers draw ABOVE every actor (<see cref="CloseviewZIndex"/>): that is the
///     "hide behind" in MapCreation.gdd §1, and is what lets a foreground haze or leaf
///     fringe sit over the fight.
/// </summary>
public static class MapVisualBuilder
{
    /// <summary>Above players, foes, projectiles and pickups (all default z 0), below the
    /// HUD (a separate CanvasLayer).</summary>
    public const int CloseviewZIndex = 20;

    private static readonly Dictionary<string, Rect2> UsedRectCache = new();

    /// <summary>Adds one <see cref="MapParallaxLayer"/> per map layer under
    /// <paramref name="parent"/> and returns them so the caller can anchor the parallax.</summary>
    public static List<MapParallaxLayer> Build(MapDocument document, Node2D parent, string logTag)
    {
        var layers = new List<MapParallaxLayer>();
        if (document?.Layers == null || parent == null) return layers;

        foreach (MapLayerData layer in document.Layers)
        {
            if (layer == null) continue;

            var node = new MapParallaxLayer(layer) { Modulate = LayerModulate(layer) };
            if (layer.Role == MapLayerData.RoleCloseview) node.ZIndex = CloseviewZIndex;
            parent.AddChild(node);
            layers.Add(node);
            if (layer.Tiles == null) continue;

            LayerOccupancy occupancy = LayerOccupancy.Build(layer, Lookup);
            foreach (PlacedTile tile in layer.Tiles)
            {
                if (tile == null) continue;
                if (!TileRegistry.TryGet(tile.DefId, out TileDef def))
                {
                    GD.PushWarning($"[{logTag}] skipped unknown tile '{tile.DefId}'.");
                    continue;
                }
                // Rule zones are generation markers and spawn/goal tiles are gameplay markers:
                // both are editor-only quads with nothing to show in play.
                if (def.Category == TileCategory.Rule || IsRuntimeMarker(def.Category)) continue;
                node.AddVisual(CreateTileVisual(tile, def, occupancy, layer.GridW, layer.GridH));
            }
        }
        return layers;
    }

    public static bool IsRuntimeMarker(TileCategory category) =>
        category == TileCategory.EnemySpawn || category == TileCategory.Respawn ||
        category == TileCategory.LevelGoal || category == TileCategory.Character;

    /// <summary>One placed tile's art, positioned at its authored cell.</summary>
    public static Node2D CreateTileVisual(PlacedTile tile, TileDef def, LayerOccupancy occupancy,
        int gridW, int gridH)
    {
        float cell = MapGrid.PixelsPerCell;
        Vector2 footprint = new(def.FootprintW * cell, def.FootprintH * cell);
        var host = new Node2D
        {
            Name = def.Id.Replace('.', '_'),
            Position = new Vector2(tile.X * cell, tile.Y * cell),
        };

        TileVisual visual = TileVisualResolver.Resolve(def, tile.X,
            (dx, dy) => AutotileProbe.SameGroup(occupancy, gridW, gridH, tile.X + dx, tile.Y + dy,
                def.AutotileGroup, Lookup),
            authoringView: false);

        string texturePath = visual.TexturePath;
        Texture2D texture = LoadTexture(texturePath);
        bool region = visual.HasRegion;
        if (texture == null && region)
        {
            // The atlas failed to load: fall back to the kind's plain swatch rather than a quad.
            texturePath = def.SpriteSlot;
            texture = LoadTexture(texturePath);
            region = false;
        }
        if (texture == null)
        {
            host.AddChild(new Polygon2D
            {
                Polygon = new[] { Vector2.Zero, new Vector2(footprint.X, 0f), footprint, new Vector2(0f, footprint.Y) },
                Color = ColorFromHex(def.EditorColor, Colors.Magenta),
            });
            return host;
        }

        Rect2 source;
        if (region)
        {
            Vector2 size = texture.GetSize();
            float cw = size.X / Mathf.Max(1, visual.Cols), ch = size.Y / Mathf.Max(1, visual.Rows);
            source = new Rect2(visual.Col * cw, visual.Row * ch, cw, ch);
        }
        else if (def.SpriteFillFootprint)
        {
            source = new Rect2(Vector2.Zero, texture.GetSize());    // a fill is the whole texture
        }
        else
        {
            source = UsedRectOf(texturePath, texture);
        }
        if (source.Size.X <= 0f || source.Size.Y <= 0f) source = new Rect2(Vector2.Zero, Vector2.One);

        Vector2 drawSize = def.SpriteFillFootprint
            ? footprint
            : source.Size * Mathf.Min(footprint.X / source.Size.X, footprint.Y / source.Size.Y);

        host.AddChild(new Sprite2D
        {
            Texture = texture,
            Centered = false,
            RegionEnabled = true,
            RegionRect = source,
            // Keep bilinear sampling inside the chosen atlas cell: its neighbours in the
            // sheet are other variants, not the tile's real neighbours on the map.
            RegionFilterClipEnabled = region,
            FlipH = tile.FlipX,
            Position = new Vector2((footprint.X - drawSize.X) * 0.5f, footprint.Y - drawSize.Y),
            Scale = drawSize / source.Size,
        });
        return host;
    }

    private static TileDef Lookup(string id) => TileRegistry.TryGet(id, out TileDef def) ? def : null;

    private static Texture2D LoadTexture(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : ResourceLoader.Load<Texture2D>(path);

    /// <summary>The texture's non-transparent bounds (cached per path). Reading the image is
    /// a one-off cost per texture; a headless run has no image data, where the full rect is
    /// the right answer anyway.</summary>
    private static Rect2 UsedRectOf(string path, Texture2D texture)
    {
        Rect2 full = new(Vector2.Zero, texture.GetSize());
        if (string.IsNullOrEmpty(path)) return full;
        if (UsedRectCache.TryGetValue(path, out Rect2 cached)) return cached;

        Rect2 used = full;
        Image image = texture.GetImage();
        if (image != null && !image.IsEmpty())
        {
            Rect2I rect = image.GetUsedRect();
            if (rect.Size.X > 0 && rect.Size.Y > 0) used = new Rect2(rect.Position, rect.Size);
        }
        UsedRectCache[path] = used;
        return used;
    }

    private static Color LayerModulate(MapLayerData layer)
    {
        Color tint = ColorFromHex(layer.Tint, Colors.White);
        tint.A *= Mathf.Clamp(layer.Opacity, 0f, 1f);
        return tint;
    }

    private static Color ColorFromHex(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        string value = hex.Trim().TrimStart('#');
        if (value.Length != 6 && value.Length != 8) return fallback;
        foreach (char c in value)
            if (!System.Uri.IsHexDigit(c)) return fallback;
        return new Color(value);
    }
}

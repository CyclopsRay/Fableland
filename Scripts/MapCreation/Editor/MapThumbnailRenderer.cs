namespace Fableland.MapCreation.Editor;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;

/// <summary>
/// Paints a small picture of a map for its browser card (MapCreation.gdd §7.8) — the map
/// itself, drawn by the same <see cref="MapVisualBuilder"/> that draws it in play, so a
/// card can never show something the map is not. No thumbnail files exist: a card's picture
/// is rendered when the browser opens and is always current. Which part of the map is
/// pictured is <see cref="MapThumbnailFraming"/>'s decision, not this class's.
///
/// One map at a time is built in an off-screen <see cref="SubViewport"/>, given a few frames
/// for its parallax layers to settle, captured, and freed, so opening the browser never
/// hitches on a wall of maps. Results are cached for the process, keyed by the caller (file
/// path + modified stamp), which makes returning from the editor instant for every map that
/// did not change. In a headless run nothing can be rendered and no callback fires; cards
/// simply keep their flat colour.
/// </summary>
public partial class MapThumbnailRenderer : Node
{
    /// <summary>Card picture size. The browser sizes its cards so their picture area keeps
    /// this shape at any window width (`MapBrowser.FitCards`).</summary>
    public static readonly Vector2I ThumbnailSize = new(448, 320);

    // Rendered at twice the size and resampled down: the map's art is far larger than a
    // card, and a single hard minification step shimmers.
    private const int Supersample = 2;
    private const int SettleFrames = 3;

    private static readonly Dictionary<string, ImageTexture> Cache = new();

    private sealed class Job
    {
        public MapDocument Doc;
        public string Key;
        public Action<Texture2D> OnReady;
    }

    private readonly Queue<Job> _queue = new();
    private Job _active;
    private SubViewport _viewport;
    private int _framesLeft;

    /// <summary>Asks for a map's picture. <paramref name="onReady"/> runs at once when it is
    /// cached, otherwise some frames later — or never, if it cannot be rendered. The callback
    /// must tolerate its target having been freed in the meantime.</summary>
    public void Request(MapDocument doc, string key, Action<Texture2D> onReady)
    {
        if (doc == null || string.IsNullOrEmpty(key) || onReady == null) return;
        if (Cache.TryGetValue(key, out ImageTexture cached))
        {
            onReady(cached);
            return;
        }
        _queue.Enqueue(new Job { Doc = doc, Key = key, OnReady = onReady });
    }

    /// <summary>Drops every queued request (the browser is about to rebuild its cards).</summary>
    public void CancelPending() => _queue.Clear();

    public override void _Process(double delta)
    {
        if (_active == null)
        {
            if (_queue.Count == 0) return;
            Job next = _queue.Dequeue();
            if (Cache.TryGetValue(next.Key, out ImageTexture cached))   // rendered while it waited
            {
                next.OnReady(cached);
                return;
            }
            Begin(next);
            return;
        }

        if (--_framesLeft > 0) return;
        Finish();
    }

    private void Begin(Job job)
    {
        _active = job;
        _framesLeft = SettleFrames;

        _viewport = new SubViewport
        {
            Size = ThumbnailSize * Supersample,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            Disable3D = true,
            GuiDisableInput = true,
        };
        AddChild(_viewport);

        MapLayerData field = MapDocumentQueries.Battlefield(job.Doc);
        float cell = MapGrid.PixelsPerCell;
        Vector2 mapSize = field != null
            ? new Vector2(field.GridW * cell, field.GridH * cell)
            : new Vector2(MapLayerData.DefaultGridWidth * cell, MapLayerData.DefaultGridHeight * cell);

        var canvas = new ColorRect
        {
            Color = ParseColor(job.Doc.Canvas?.Color, new Color("87CEEB")),
            Position = -mapSize,
            Size = mapSize * 3f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _viewport.AddChild(canvas);

        var world = new Node2D();
        _viewport.AddChild(world);
        List<MapParallaxLayer> layers = MapVisualBuilder.Build(job.Doc, world, "MapThumbnail");

        // Same parallax anchor as a fight (the spawn), so far layers sit where they would.
        Vector2 anchor = MapDocumentQueries.TryGetCharacterSpawnPoint(job.Doc, out float spawnX, out float spawnY)
            ? new Vector2(spawnX, spawnY)
            : mapSize * 0.5f;
        foreach (MapParallaxLayer layer in layers) layer.SetAnchor(anchor);

        // Picture the part of the map its content deserves (MapThumbnailFraming), not the
        // whole grid: a sparse map is mostly sky.
        Vector2 view = _viewport.Size;
        MapThumbnailFraming.Frame frame = MapThumbnailFraming.Compute(job.Doc, view.X / view.Y);
        float zoom = view.X / Mathf.Max(1f, frame.Width * cell);
        Vector2 center = new((frame.Left + frame.Width * 0.5f) * cell, (frame.Top + frame.Height * 0.5f) * cell);

        var camera = new Camera2D { Position = center, Zoom = new Vector2(zoom, zoom) };
        _viewport.AddChild(camera);
        camera.MakeCurrent();
    }

    private void Finish()
    {
        Job job = _active;
        _active = null;

        ImageTexture texture = null;
        Image image = _viewport?.GetTexture()?.GetImage();
        if (image != null && !image.IsEmpty())
        {
            image.Resize(ThumbnailSize.X, ThumbnailSize.Y, Image.Interpolation.Lanczos);
            texture = ImageTexture.CreateFromImage(image);
            Cache[job.Key] = texture;
        }

        _viewport?.QueueFree();
        _viewport = null;
        if (texture != null) job.OnReady(texture);
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return new Color(hex); }
        catch { return fallback; }
    }
}

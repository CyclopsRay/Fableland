namespace Fableland.MapCreation.Editor;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor.Tools;

/// <summary>
/// GDD §7.6 — the dedicated world-draw child. ALL world content (grid, tiles, ghosts,
/// sketchlines, effect areas, preview, selection, hover cell) is drawn HERE, never in
/// the editor root's own `_Draw`: a Godot node's own `_Draw` renders BEHIND its
/// children, so the old v0.5.x editor's opaque background child occluded its whole
/// canvas. `MapEditor` keeps `_Draw` empty/absent and relies entirely on this control,
/// placed above the canvas backdrop and below the UI panels (GDD §11.3).
///
/// Manual screen-space pan/zoom, NO `Camera2D` — mirrors the overworld map pattern
/// (KNOWLEDGE v0.3.3): `_pan` (screen-space offset) + `_zoom` (scalar), so UI panels
/// stay static while only this control's drawn content moves.
/// </summary>
public partial class GridView : Control
{
    public EditorState State;

    /// <summary>null = cursor is outside the current layer's grid.</summary>
    public event Action<Vector2I?> CursorCellChanged;

    /// <summary>Pan or zoom changed — `MapEditor` refreshes the zoom % label.</summary>
    public event Action ViewChanged;

    // MC4 plumbing: routed by MapEditor to the active tool (looked up fresh each event).
    public event Action<Vector2I, InputEventMouseButton> CellPressed;
    public event Action<Vector2I> CellDragged;
    public event Action<Vector2I> CellReleased;

    /// <summary>MC4 — MapEditor assigns this once; called every `_Draw` to fetch the
    /// active tool's (or paste-mode's) ghost, if any. Null return = nothing drawn.</summary>
    public Func<GhostInfo> GhostProvider;

    /// <summary>The cell the mouse is currently hovering, or null if outside the
    /// current layer's grid — tools read this directly (via their `View` reference)
    /// to build a live cursor-following ghost without MapEditor relaying motion
    /// events separately.</summary>
    public Vector2I? HoverCell => _lastCursorCell;

    private Vector2 _pan = Vector2.Zero;
    private float _zoom = 1f;
    public float Zoom => _zoom;

    private const float ZoomMin = 0.25f;
    private const float ZoomMax = 4.0f;
    private const float ZoomFactor = 1.1f;

    private Vector2I? _lastCursorCell;
    private Vector2I? _lastValidStrokeCell;

    private bool _leftDown;
    private bool _mousePanning;
    private bool _spacePanning;

    /// <summary>True while either middle-drag or Space+left-drag panning is active. No
    /// cell events (`CellPressed`/`CellDragged`/`CellReleased`/`CursorCellChanged`) fire
    /// while this is true.</summary>
    public bool IsPanning { get; private set; }

    private static readonly Color MagentaFallback = new(1f, 0f, 1f);
    private readonly Dictionary<string, Texture2D> _textureCache = new();

    public override void _Ready()
    {
        // Must receive GUI input directly; UI panels are siblings drawn ABOVE this
        // control and get first crack at clicks that land on them.
        MouseFilter = Control.MouseFilterEnum.Stop;
    }

    // ------------------------------------------------------------ transform

    public Vector2 WorldToScreen(Vector2 worldPx) => worldPx * _zoom + _pan;
    public Vector2 ScreenToWorld(Vector2 screenPx) => (screenPx - _pan) / _zoom;

    /// <summary>Cell under a screen-space point, or null if it falls outside the
    /// CURRENT layer's grid (there is no "current layer" without a document either).</summary>
    public Vector2I? CellAtScreen(Vector2 screenPos)
    {
        var layer = State?.CurrentLayer;
        if (layer == null) return null;

        Vector2 world = ScreenToWorld(screenPos);
        int cx = Mathf.FloorToInt(world.X / Units.PixelsPerMeter);
        int cy = Mathf.FloorToInt(world.Y / Units.PixelsPerMeter);

        if (cx < 0 || cy < 0 || cx >= layer.GridW || cy >= layer.GridH) return null;
        return new Vector2I(cx, cy);
    }

    /// <summary>×1.1 step toward the view center, clamped 0.25×-4.0× (the `+`/`-` shortcuts).</summary>
    public void ZoomStep(int dir)
    {
        Vector2 center = Size / 2f;
        ApplyZoom(dir > 0 ? _zoom * ZoomFactor : _zoom / ZoomFactor, center);
    }

    /// <summary>Resets to 100%, keeping the current view center fixed (the `0` shortcut).</summary>
    public void ZoomReset()
    {
        Vector2 center = Size / 2f;
        Vector2 worldAtCenter = ScreenToWorld(center);
        _zoom = 1f;
        _pan = center - worldAtCenter * _zoom;
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    private void ApplyZoom(float newZoom, Vector2 screenAnchor)
    {
        newZoom = Mathf.Clamp(newZoom, ZoomMin, ZoomMax);
        if (Mathf.IsEqualApprox(newZoom, _zoom)) return;

        // Keep the world point under `screenAnchor` fixed on screen.
        Vector2 worldUnderAnchor = ScreenToWorld(screenAnchor);
        _zoom = newZoom;
        _pan = screenAnchor - worldUnderAnchor * _zoom;
        ViewChanged?.Invoke();
        QueueRedraw();
    }

    // ------------------------------------------------------------ input

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            HandleMouseButton(mb);
            return;
        }

        if (@event is InputEventMouseMotion mm)
        {
            HandleMouseMotion(mm);
            return;
        }

        if (@event is InputEventPanGesture pan)
        {
            HandlePanGesture(pan);
        }
    }

    /// <summary>Bug-fix (user report) — Mac trackpad two-finger swipe up/down: Godot
    /// surfaces this as `InputEventPanGesture` (never `InputEventMouseButton` wheel
    /// events), so without this handler a trackpad swipe did nothing at all. Only the
    /// vertical component drives zoom (horizontal swipe is left alone — no trackpad-pan
    /// feature was requested); `PanZoomSensitivity` converts the gesture's pixel-ish
    /// delta into the same ZoomFactor-per-step curve the wheel/keys already use, so all
    /// three input paths feel consistent.</summary>
    private const float PanZoomSensitivity = 0.02f;

    private void HandlePanGesture(InputEventPanGesture pan)
    {
        float steps = -pan.Delta.Y * PanZoomSensitivity;
        if (Mathf.IsZeroApprox(steps)) return;

        ApplyZoom(_zoom * Mathf.Pow(ZoomFactor, steps), pan.Position);
        AcceptEvent();
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
        {
            ApplyZoom(_zoom * ZoomFactor, mb.Position);
            AcceptEvent();
            return;
        }

        if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
        {
            ApplyZoom(_zoom / ZoomFactor, mb.Position);
            AcceptEvent();
            return;
        }

        if (mb.ButtonIndex == MouseButton.Middle)
        {
            _mousePanning = mb.Pressed;
            RefreshPanning();
            AcceptEvent();
            return;
        }

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _leftDown = true;
                RefreshPanning();
                if (IsPanning) { AcceptEvent(); return; }

                var cell = CellAtScreen(mb.Position);
                if (cell.HasValue)
                {
                    _lastValidStrokeCell = cell;
                    CellPressed?.Invoke(cell.Value, mb);
                }
            }
            else
            {
                bool wasPanning = IsPanning;
                _leftDown = false;
                RefreshPanning();

                if (!wasPanning)
                {
                    // Emit even if the cursor has left the grid, using the last valid
                    // cell, so strokes always close (GDD §7.2/§7.7 stroke = one command).
                    var cell = CellAtScreen(mb.Position) ?? _lastValidStrokeCell;
                    if (cell.HasValue) CellReleased?.Invoke(cell.Value);
                }
                _lastValidStrokeCell = null;
            }
            AcceptEvent();
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        RefreshPanning();

        if (IsPanning)
        {
            _pan += mm.Relative;
            ViewChanged?.Invoke();
            QueueRedraw();
            AcceptEvent();
            return;
        }

        var cell = CellAtScreen(mm.Position);
        if (cell != _lastCursorCell)
        {
            _lastCursorCell = cell;
            CursorCellChanged?.Invoke(cell);
            QueueRedraw(); // hover-cell outline moved
        }

        if (_leftDown && cell.HasValue)
        {
            _lastValidStrokeCell = cell;
            CellDragged?.Invoke(cell.Value);
        }

        AcceptEvent();
    }

    private void RefreshPanning()
    {
        _spacePanning = Input.IsActionPressed("mapedit_pan");
        IsPanning = _mousePanning || (_leftDown && _spacePanning);
    }

    // ------------------------------------------------------------ drawing

    public override void _Draw()
    {
        var doc = State?.Document;
        if (doc?.Layers == null) return;

        // In-editor, every layer draws aligned at world origin — parallax, autoscroll,
        // and sway are RUNTIME-only visuals (GDD §1.2/§4); the authoring view
        // deliberately ignores them so tiles stay where the designer clicked them.
        for (int i = 0; i < doc.Layers.Count; i++)
        {
            var layer = doc.Layers[i];
            if (layer != null) DrawLayerTiles(i, layer);
        }

        var currentLayer = State.CurrentLayer;
        (int vx0, int vy0, int vx1, int vy1) visible = default;
        if (currentLayer != null)
        {
            visible = VisibleCellRange(currentLayer);
            var occ = State.OccupancyOf(State.CurrentLayerIndex);
            DrawSketchline(occ, State.AccentOf(State.CurrentLayerIndex), visible);

            if (State.ShowEffectAreas)
                DrawEffectAreas(currentLayer, visible);
        }

        DrawPreview();
        DrawSelection();
        DrawGhost(); // GDD §7.2 tool ghosts — above selection, below grid lines (brief §C)

        if (currentLayer != null)
        {
            if (State.ShowGrid) DrawGrid(currentLayer, visible);
            if (_lastCursorCell.HasValue)
                DrawHoverCell(_lastCursorCell.Value, State.AccentOf(State.CurrentLayerIndex));
        }
    }

    /// <summary>Visible world-cell range for `layer`, clamped to its own grid — grid
    /// lines and effect-area/selection sweeps use this so a zoomed-out 512-wide map
    /// never issues an off-screen draw call (GDD §7.5).</summary>
    private (int x0, int y0, int x1, int y1) VisibleCellRange(MapLayerData layer)
    {
        Vector2 topLeftWorld = ScreenToWorld(Vector2.Zero);
        Vector2 bottomRightWorld = ScreenToWorld(Size);
        float cell = Units.PixelsPerMeter;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(topLeftWorld.X / cell) - 1, 0, layer.GridW);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(topLeftWorld.Y / cell) - 1, 0, layer.GridH);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(bottomRightWorld.X / cell) + 1, 0, layer.GridW);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(bottomRightWorld.Y / cell) + 1, 0, layer.GridH);

        return (x0, y0, x1, y1);
    }

    private void DrawLayerTiles(int layerIndex, MapLayerData layer)
    {
        if (layer.Tiles == null) return;

        var (vx0, vy0, vx1, vy1) = VisibleCellRange(layer);
        bool isCurrent = layerIndex == (State?.CurrentLayerIndex ?? -1);
        float layerAlphaMul = layer.Opacity * (isCurrent ? 1f : 0.35f); // never 0 (GDD §7.4)
        Color? tint = string.IsNullOrEmpty(layer.Tint) ? null : ColorFromHex(layer.Tint, Colors.White);

        foreach (var tile in layer.Tiles)
        {
            TileRegistry.TryGet(tile.DefId, out var def); // def may stay null: unknown id -> magenta 1x1
            int fw = def?.FootprintW ?? 1;
            int fh = def?.FootprintH ?? 1;

            if (tile.X + fw <= vx0 || tile.X >= vx1 || tile.Y + fh <= vy0 || tile.Y >= vy1)
                continue;

            Color baseColor = def != null ? ColorFromHex(def.EditorColor) : MagentaFallback;
            bool hatched = def != null && def.Category == TileCategory.Rule;
            Texture2D texture = def != null ? TextureFor(def.SpriteSlot) : null;
            Rect2? atlasRegion = null;

            // Bug-fix (user report, item 5): connected-look ground autotiling. Best-effort
            // v1 (see AutotileAtlas doc) — only wired for tiles carrying a non-empty
            // AutotileGroup with an `artSource` atlas path; everything else keeps using
            // SpriteSlot/flat color exactly as before.
            if (def != null && !string.IsNullOrEmpty(def.AutotileGroup) &&
                def.Props != null && def.Props.TryGetValue("artSource", out var artPath))
            {
                var atlasTex = TextureFor(artPath);
                if (atlasTex != null)
                {
                    bool northSame = NeighborSharesGroup(layerIndex, tile.X, tile.Y - 1, def.AutotileGroup);
                    if (AutotileAtlas.TryGetCell(def.AutotileGroup, northSame, out int row, out int col))
                    {
                        Vector2 texSize = atlasTex.GetSize();
                        float cellW = texSize.X / AutotileAtlas.Cols;
                        float cellH = texSize.Y / AutotileAtlas.Rows;
                        atlasRegion = new Rect2(col * cellW, row * cellH, cellW, cellH);
                        texture = atlasTex;
                    }
                }
            }

            DrawTileQuad(tile.X, tile.Y, fw, fh, baseColor, layerAlphaMul, tint, hatched, texture, atlasRegion);
        }
    }

    /// <summary>Whether the cell at (x, y) on `layerIndex` holds a tile whose def shares
    /// `autotileGroup` — out-of-bounds/empty/unknown-def all count as "not same" (an edge).
    /// Only tests the anchor cell, not a placed tile's full footprint: fine while every
    /// AutotileGroup-tagged def is 1x1 (GDD §2.5/Docs/Art/BeachTileSet.md).</summary>
    private bool NeighborSharesGroup(int layerIndex, int x, int y, string autotileGroup)
    {
        var neighbor = State?.OccupancyOf(layerIndex)?.TileAt(x, y);
        if (neighbor == null) return false;
        return TileRegistry.TryGet(neighbor.DefId, out var neighborDef) && neighborDef.AutotileGroup == autotileGroup;
    }

    private Texture2D TextureFor(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_textureCache.TryGetValue(path, out var cached)) return cached;

        var texture = ResourceLoader.Load<Texture2D>(path);
        _textureCache[path] = texture;
        return texture;
    }

    private void DrawTileQuad(int x, int y, int fw, int fh, Color baseColor, float alphaMul, Color? tint,
        bool hatched, Texture2D texture = null, Rect2? srcRect = null)
    {
        Color color = baseColor;
        if (tint.HasValue)
        {
            var t = tint.Value;
            color = new Color(color.R * t.R, color.G * t.G, color.B * t.B, color.A);
        }

        float cell = Units.PixelsPerMeter;
        Vector2 screenTL = WorldToScreen(new Vector2(x * cell, y * cell));
        Vector2 screenSize = new Vector2(fw * cell, fh * cell) * _zoom;
        var rect = new Rect2(screenTL, screenSize);

        if (hatched)
        {
            Color fill = color;
            fill.A *= 0.25f * alphaMul;
            DrawRect(rect, fill);

            Color lineColor = color;
            lineColor.A *= alphaMul;
            DrawHatch(rect, lineColor);
        }
        else
        {
            color.A *= alphaMul;
            if (texture != null)
            {
                Color spriteModulate = tint ?? Colors.White;
                spriteModulate.A *= alphaMul;
                if (srcRect.HasValue)
                    DrawTextureRectRegion(texture, rect, srcRect.Value, spriteModulate);
                else
                    DrawTextureRect(texture, rect, tile: false, modulate: spriteModulate);
            }
            else
                DrawRect(rect, color);
        }
    }

    /// <summary>Diagonal hatch (~6 world px spacing, so it scales with zoom like the
    /// tile it decorates), analytically clipped to `rect` (GDD §6 rule-zone overlay).</summary>
    private void DrawHatch(Rect2 rect, Color lineColor)
    {
        float spacing = Mathf.Max(1f, 6f * _zoom);
        float x0 = rect.Position.X, x1 = rect.Position.X + rect.Size.X;
        float y0 = rect.Position.Y, y1 = rect.Position.Y + rect.Size.Y;

        for (float off = 0; off < rect.Size.X + rect.Size.Y; off += spacing)
        {
            float sx = x0 + off, sy = y0;
            if (sx > x1) { sy += sx - x1; sx = x1; }

            float ex = x0, ey = y0 + off;
            if (ey > y1) { ex += ey - y1; ey = y1; }

            if (sx < x0 || ey < y0 || sy > y1 || ex > x1) continue;
            DrawLine(new Vector2(sx, sy), new Vector2(ex, ey), lineColor, 1f);
        }
    }

    /// <summary>GDD §7.4 — 1.5 px boundary trace around each contiguous group of
    /// occupied cells on the current layer (boundary-edge tracing; no polyline
    /// assembly needed: draw an edge segment wherever the 4-neighbor is unoccupied).</summary>
    private void DrawSketchline(LayerOccupancy occ, Color accent, (int x0, int y0, int x1, int y1) visible)
    {
        float cell = Units.PixelsPerMeter;
        foreach (var (cx, cy) in occ.Cells.Keys)
        {
            if (cx < visible.x0 - 1 || cx > visible.x1 || cy < visible.y0 - 1 || cy > visible.y1) continue;

            bool up = occ.Cells.ContainsKey((cx, cy - 1));
            bool down = occ.Cells.ContainsKey((cx, cy + 1));
            bool left = occ.Cells.ContainsKey((cx - 1, cy));
            bool right = occ.Cells.ContainsKey((cx + 1, cy));

            Vector2 tl = WorldToScreen(new Vector2(cx * cell, cy * cell));
            Vector2 tr = WorldToScreen(new Vector2((cx + 1) * cell, cy * cell));
            Vector2 bl = WorldToScreen(new Vector2(cx * cell, (cy + 1) * cell));
            Vector2 br = WorldToScreen(new Vector2((cx + 1) * cell, (cy + 1) * cell));

            if (!up) DrawLine(tl, tr, accent, 1.5f);
            if (!down) DrawLine(bl, br, accent, 1.5f);
            if (!left) DrawLine(tl, bl, accent, 1.5f);
            if (!right) DrawLine(tr, br, accent, 1.5f);
        }
    }

    /// <summary>GDD §2.4 — orange outline of every visible non-Rule tile's effect area
    /// on the current layer (null EffectArea = outline the footprint rect).</summary>
    private void DrawEffectAreas(MapLayerData layer, (int x0, int y0, int x1, int y1) visible)
    {
        if (layer.Tiles == null) return;
        Color orange = new(1f, 0.55f, 0f);
        float cell = Units.PixelsPerMeter;

        foreach (var tile in layer.Tiles)
        {
            if (!TileRegistry.TryGet(tile.DefId, out var def) || def.Category == TileCategory.Rule) continue;
            if (tile.X + def.FootprintW <= visible.x0 || tile.X >= visible.x1 ||
                tile.Y + def.FootprintH <= visible.y0 || tile.Y >= visible.y1)
                continue;

            Vector2 anchorWorld = new(tile.X * cell, tile.Y * cell);
            var shape = def.EffectArea;

            if (shape == null)
            {
                Vector2 tl = WorldToScreen(anchorWorld);
                Vector2 size = new Vector2(def.FootprintW * cell, def.FootprintH * cell) * _zoom;
                DrawRect(new Rect2(tl, size), orange, filled: false, width: 1.5f);
                continue;
            }

            switch (shape.Kind)
            {
                case ShapeDef.KindRect:
                {
                    Vector2 tl = WorldToScreen(anchorWorld + new Vector2(shape.OffsetX, shape.OffsetY));
                    Vector2 size = new Vector2(shape.W, shape.H) * _zoom;
                    DrawRect(new Rect2(tl, size), orange, filled: false, width: 1.5f);
                    break;
                }
                case ShapeDef.KindCircle:
                {
                    Vector2 center = WorldToScreen(anchorWorld + new Vector2(shape.OffsetX, shape.OffsetY));
                    DrawArc(center, shape.Radius * _zoom, 0f, Mathf.Tau, 32, orange, 1.5f);
                    break;
                }
                case ShapeDef.KindPolygon:
                {
                    if (shape.Points == null || shape.Points.Length < 4) break;
                    int n = shape.Points.Length / 2;
                    var pts = new Vector2[n + 1]; // +1 to close the loop
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 local = new(shape.Points[i * 2], shape.Points[i * 2 + 1]);
                        pts[i] = WorldToScreen(anchorWorld + local);
                    }
                    pts[n] = pts[0];
                    DrawPolyline(pts, orange, 1.5f);
                    break;
                }
            }
        }
    }

    /// <summary>GDD §6 — each resolved preview spawn as a translucent quad + white
    /// outline; null <see cref="EditorState.Preview"/> draws nothing.</summary>
    private void DrawPreview()
    {
        if (State?.Preview == null) return;
        float cell = Units.PixelsPerMeter;

        foreach (var spawn in State.Preview)
        {
            if (!TileRegistry.TryGet(spawn.DefId, out var def)) continue;

            Vector2 tl = WorldToScreen(new Vector2(spawn.X * cell, spawn.Y * cell));
            Vector2 size = new Vector2(def.FootprintW * cell, def.FootprintH * cell) * _zoom;
            var rect = new Rect2(tl, size);

            Color fill = ColorFromHex(def.EditorColor);
            fill.A = 0.5f;
            DrawRect(rect, fill);
            DrawRect(rect, Colors.White, filled: false, width: 1f);
        }
    }

    private void DrawSelection()
    {
        if (State?.Selection == null || State.Selection.Count == 0) return;
        float cell = Units.PixelsPerMeter;
        Color yellow = new(1f, 1f, 0.2f);

        foreach (var tile in State.Selection)
        {
            int fw = 1, fh = 1;
            if (TileRegistry.TryGet(tile.DefId, out var def)) { fw = def.FootprintW; fh = def.FootprintH; }

            Vector2 tl = WorldToScreen(new Vector2(tile.X * cell, tile.Y * cell));
            Vector2 size = new Vector2(fw * cell, fh * cell) * _zoom;
            DrawRect(new Rect2(tl, size), yellow, filled: false, width: 2f);
        }
    }

    /// <summary>MC4 — draws whatever the active tool (or MapEditor's paste-mode
    /// pseudo-tool) reports via <see cref="GhostProvider"/>: footprint-rect ghosts
    /// (paint/erase/bucket/move/paste), a drag rectangle (rect fill/marquee), and/or
    /// an in-progress lasso polyline. Legal = green-tinted fill + outline; illegal =
    /// red (GDD §2.3 "red ghost preview" on rejected placement).</summary>
    private void DrawGhost()
    {
        var ghost = GhostProvider?.Invoke();
        if (ghost == null) return;

        float cell = Units.PixelsPerMeter;
        Color fill = ghost.Valid ? new Color(0.3f, 1f, 0.3f, 0.4f) : new Color(1f, 0.15f, 0.15f, 0.45f);
        Color outline = ghost.Valid ? new Color(0.6f, 1f, 0.6f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.9f);

        if (ghost.Cells != null)
        {
            foreach (var (c, w, h, _) in ghost.Cells)
            {
                Vector2 tl = WorldToScreen(new Vector2(c.X * cell, c.Y * cell));
                Vector2 size = new Vector2(w * cell, h * cell) * _zoom;
                var rect = new Rect2(tl, size);
                DrawRect(rect, fill);
                DrawRect(rect, outline, filled: false, width: 1.5f);
            }
        }

        if (ghost.RectCells.HasValue)
        {
            var r = ghost.RectCells.Value;
            Vector2 tl = WorldToScreen(new Vector2(r.Position.X * cell, r.Position.Y * cell));
            Vector2 size = new Vector2(r.Size.X * cell, r.Size.Y * cell) * _zoom;
            var rect = new Rect2(tl, size);
            DrawRect(rect, fill);
            DrawRect(rect, outline, filled: false, width: 1.5f);
        }

        if (ghost.LassoPointsWorld != null && ghost.LassoPointsWorld.Count > 1)
        {
            var screenPts = new Vector2[ghost.LassoPointsWorld.Count];
            for (int i = 0; i < screenPts.Length; i++) screenPts[i] = WorldToScreen(ghost.LassoPointsWorld[i]);
            DrawPolyline(screenPts, outline, 1.5f);
        }
    }

    /// <summary>GDD §7.5 — 1 px lines every cell across the visible rect only, majors
    /// every 8 cells, LOD fade below 50% zoom (gone at/below 25%).</summary>
    private void DrawGrid(MapLayerData layer, (int x0, int y0, int x1, int y1) visible)
    {
        float cell = Units.PixelsPerMeter;
        float lod = Mathf.Clamp((_zoom - ZoomMin) / ZoomMin, 0f, 1f); // full >=0.5x, 0 at <=0.25x
        Color minor = new(1f, 1f, 1f, 0.12f * lod);
        Color major = new(1f, 1f, 1f, 0.30f);

        for (int x = visible.x0; x <= visible.x1; x++)
        {
            bool isMajor = x % 8 == 0;
            if (!isMajor && lod <= 0f) continue;
            Vector2 top = WorldToScreen(new Vector2(x * cell, visible.y0 * cell));
            Vector2 bottom = WorldToScreen(new Vector2(x * cell, visible.y1 * cell));
            DrawLine(top, bottom, isMajor ? major : minor, 1f);
        }

        for (int y = visible.y0; y <= visible.y1; y++)
        {
            bool isMajor = y % 8 == 0;
            if (!isMajor && lod <= 0f) continue;
            Vector2 left = WorldToScreen(new Vector2(visible.x0 * cell, y * cell));
            Vector2 right = WorldToScreen(new Vector2(visible.x1 * cell, y * cell));
            DrawLine(left, right, isMajor ? major : minor, 1f);
        }

        // Full grid-border rect, brighter, so the map bounds always read clearly.
        Vector2 borderTL = WorldToScreen(Vector2.Zero);
        Vector2 borderBR = WorldToScreen(new Vector2(layer.GridW * cell, layer.GridH * cell));
        DrawRect(new Rect2(borderTL, borderBR - borderTL), new Color(1f, 1f, 1f, 0.6f), filled: false, width: 1.5f);
    }

    private void DrawHoverCell(Vector2I cell, Color accent)
    {
        float cellPx = Units.PixelsPerMeter;
        Vector2 tl = WorldToScreen(new Vector2(cell.X * cellPx, cell.Y * cellPx));
        Vector2 size = new Vector2(cellPx, cellPx) * _zoom;
        DrawRect(new Rect2(tl, size), accent, filled: false, width: 1f);
    }

    /// <summary>Hex string (`"#RRGGBB"`/`"#RRGGBBAA"`) to Color, magenta on anything
    /// `new Color(string)` can't parse.</summary>
    private static Color ColorFromHex(string hex, Color? fallback = null)
    {
        Color fb = fallback ?? MagentaFallback;
        if (string.IsNullOrEmpty(hex)) return fb;
        try { return new Color(hex); }
        catch { return fb; }
    }
}

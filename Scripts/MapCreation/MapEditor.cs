using System.Collections.Generic;
using Godot;

namespace Fableland.MapCreation;

/// <summary>
/// Grid-based level-map editor. Select tiles from the bottom palette bar,
/// drag on the grid to paint, right-click to erase, scroll to zoom,
/// middle-drag to pan. Save writes the map to user://maps/.
///
/// Keyboard shortcuts:
///   G=Ground  P=Platform  V=SoftVolume  E=EnemySpawn  R=Respawn  L=LevelGoal  C=Character
///   Ctrl+S=Save
/// </summary>
public partial class MapEditor : Control
{
    /// <summary>Set before adding to the tree — the map to edit.</summary>
    public CustomMap LoadedMap;

    // Editing state
    private TileKind _brushKind = TileKind.Ground;
    private int _brushVariant;
    private bool _drawing;
    private bool _erasing;
    private bool _panning;
    private Vector2 _lastMouse;

    // View
    private float _zoom = 1f;
    private Vector2 _pan;
    private const float CellSize = 32f;
    private const float ZoomMin = 0.25f;
    private const float ZoomMax = 3f;
    private Font _font;

    // Palette panels — positioned at the bottom; their area is excluded from grid painting
    private PanelContainer _palettePanel;
    private HBoxContainer _paletteBar;
    private HBoxContainer _variantBar;
    private readonly Dictionary<TileKind, Button> _paletteBtns = new();
    private readonly List<Button> _variantBtns = new();

    // UI
    private PanelContainer _topPanel;
    private Label _infoLabel;
    private Label _brushLabel;

    /// <summary>Height reserved at the bottom for the palette. Grid interaction is disabled there.</summary>
    private float PaletteAreaTop => GetViewport().GetVisibleRect().Size.Y - 88f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;

        if (LoadedMap == null)
            LoadedMap = CustomMap.CreateEmpty(60, 30, "Untitled");

        _font = ThemeDB.FallbackFont;

        SetupUI();
        QueueRedraw();
    }

    #region UI Setup

    private void SetupUI()
    {
        // Dark backdrop (behind everything)
        var bg = new ColorRect();
        bg.AnchorsPreset = (int)LayoutPreset.FullRect;
        bg.Color = new Color(0.08f, 0.08f, 0.11f, 1);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        // ---- Top bar ----
        _topPanel = new PanelContainer();
        _topPanel.SetAnchorsPreset(LayoutPreset.TopWide);
        _topPanel.SetAnchorAndOffset(Side.Bottom, 0f, 44);
        AddChild(_topPanel);

        var topStyle = new StyleBoxFlat();
        topStyle.BgColor = new Color(0.1f, 0.1f, 0.14f, 1);
        _topPanel.AddThemeStyleboxOverride("panel", topStyle);

        var topHbox = new HBoxContainer();
        topHbox.AddThemeConstantOverride("separation", 10);
        topHbox.AnchorsPreset = (int)LayoutPreset.FullRect;
        _topPanel.AddChild(topHbox);

        var backBtn = new Button();
        backBtn.Text = "< Back";
        backBtn.CustomMinimumSize = new Vector2(72, 36);
        backBtn.Pressed += OnBack;
        topHbox.AddChild(backBtn);

        _infoLabel = new Label();
        _infoLabel.Text = MapName();
        _infoLabel.VerticalAlignment = VerticalAlignment.Center;
        _infoLabel.AddThemeFontSizeOverride("font_size", 18);
        _infoLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        topHbox.AddChild(_infoLabel);

        _brushLabel = new Label();
        _brushLabel.Text = BrushLabel();
        _brushLabel.VerticalAlignment = VerticalAlignment.Center;
        _brushLabel.AddThemeFontSizeOverride("font_size", 14);
        _brushLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.4f));
        _brushLabel.CustomMinimumSize = new Vector2(200, 0);
        topHbox.AddChild(_brushLabel);

        var saveBtn = new Button();
        saveBtn.Text = "Save";
        saveBtn.CustomMinimumSize = new Vector2(100, 36);
        saveBtn.Pressed += OnSave;
        topHbox.AddChild(saveBtn);

        // ---- Bottom palette ----
        BuildPalette();
        Resized += OnResized;
        OnResized();
    }

    private void OnResized()
    {
        if (_palettePanel != null)
        {
            var vp = GetViewport().GetVisibleRect().Size;
            _palettePanel.Position = new Vector2(0, PaletteAreaTop);
            _palettePanel.Size = new Vector2(vp.X, vp.Y - PaletteAreaTop);
        }
    }

    private void BuildPalette()
    {
        _palettePanel = new PanelContainer();
        _palettePanel.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_palettePanel);

        var palStyle = new StyleBoxFlat();
        palStyle.BgColor = new Color(0.1f, 0.1f, 0.14f, 0.98f);
        _palettePanel.AddThemeStyleboxOverride("panel", palStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        _palettePanel.AddChild(vbox);

        // Category row
        _paletteBar = new HBoxContainer();
        _paletteBar.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_paletteBar);

        // Variant row
        _variantBar = new HBoxContainer();
        _variantBar.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_variantBar);

        var kinds = new[]
        {
            TileKind.Ground, TileKind.Platform, TileKind.SoftVolume,
            TileKind.EnemySpawn, TileKind.Respawn, TileKind.LevelGoal, TileKind.Character,
        };

        foreach (var kind in kinds)
        {
            var btn = new Button();
            btn.Text = TileLabels.CategoryNames[kind];
            btn.CustomMinimumSize = new Vector2(90, 36);
            btn.ToggleMode = true;
            var k = kind;
            btn.Pressed += () => SelectPaletteKind(k);
            _paletteBtns[kind] = btn;
            _paletteBar.AddChild(btn);

            // Colour-code the category button
            var color = TileLabels.Colors.TryGetValue(kind, out var c) ? c : Colors.Gray;
            var style = new StyleBoxFlat();
            style.BgColor = new Color(color.R, color.G, color.B, 0.3f);
            style.SetCornerRadiusAll(4);
            style.SetContentMarginAll(4);
            btn.AddThemeStyleboxOverride("normal", style);
        }

        SelectPaletteKind(TileKind.Ground);
    }

    private void SelectPaletteKind(TileKind kind)
    {
        foreach (var kv in _paletteBtns)
            kv.Value.ButtonPressed = (kv.Key == kind);

        foreach (var b in _variantBtns) b.QueueFree();
        _variantBtns.Clear();

        var variants = TileLabels.Variants.TryGetValue(kind, out var v) ? v : new[] { "default" };
        for (int i = 0; i < variants.Length; i++)
        {
            var btn = new Button();
            btn.Text = variants[i];
            btn.CustomMinimumSize = new Vector2(100, 28);
            btn.ToggleMode = true;
            int vi = i;
            var k = kind;
            btn.Pressed += () => SelectVariant(k, vi);
            _variantBtns.Add(btn);
            _variantBar.AddChild(btn);

            var color = TileLabels.GetVariantColor(kind, i);
            var style = new StyleBoxFlat();
            style.BgColor = new Color(color.R, color.G, color.B, 0.4f);
            style.SetCornerRadiusAll(4);
            style.SetContentMarginAll(4);
            btn.AddThemeStyleboxOverride("normal", style);
        }

        if (_variantBtns.Count > 0)
        {
            _variantBtns[0].ButtonPressed = true;
            SelectVariant(kind, 0);
        }
    }

    private void SelectVariant(TileKind kind, int variant)
    {
        _brushKind = kind;
        _brushVariant = variant;
        foreach (var b in _variantBtns)
            b.ButtonPressed = false;
        if (variant >= 0 && variant < _variantBtns.Count)
            _variantBtns[variant].ButtonPressed = true;
        _brushLabel.Text = BrushLabel();
    }

    private string MapName() => LoadedMap?.Meta?.Name ?? "Untitled";
    private string BrushLabel()
    {
        var v = TileLabels.Variants.TryGetValue(_brushKind, out var arr) ? arr : new[] { "?" };
        string vName = _brushVariant >= 0 && _brushVariant < arr.Length ? arr[_brushVariant] : "?";
        return $"Brush: {TileLabels.CategoryNames[_brushKind]} / {vName}";
    }

    /// <summary>True if the mouse position is over the palette area (bottom) or top bar.</summary>
    private bool InUIChrome(Vector2 screenPos)
    {
        return screenPos.Y >= PaletteAreaTop || screenPos.Y <= 44;
    }

    #endregion

    #region Input

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            // Always allow wheel zoom anywhere
            if (mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
            {
                float oldZ = _zoom;
                _zoom = Mathf.Clamp(
                    _zoom * (mb.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1f / 1.15f),
                    ZoomMin, ZoomMax);
                var cursor = mb.Position;
                _pan = cursor - (_zoom / oldZ) * (cursor - _pan);
                QueueRedraw();
                return;
            }

            // Always process mouse RELEASES regardless of position — prevents stuck
            // drawing/erasing/panning state when releasing in the chrome area.
            bool inChrome = InUIChrome(mb.Position);

            // Middle button → pan
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _panning = mb.Pressed && !inChrome;
                _lastMouse = mb.Position;
                return;
            }

            // Left click → start painting
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _drawing = mb.Pressed && !inChrome;
                _erasing = false;
                if (_drawing)
                    PaintAt(mb.Position);
                return;
            }

            // Right click → start erasing
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _drawing = false;
                _erasing = mb.Pressed && !inChrome;
                if (_erasing)
                    EraseAt(mb.Position);
                return;
            }
        }

        if (@event is InputEventMouseMotion motion)
        {
            if (_panning)
            {
                _pan += motion.Position - _lastMouse;
                _lastMouse = motion.Position;
                QueueRedraw();
                return;
            }
            if (_drawing && !InUIChrome(motion.Position))
            {
                PaintAt(motion.Position);
                return;
            }
            if (_erasing && !InUIChrome(motion.Position))
            {
                EraseAt(motion.Position);
                return;
            }
        }

        // Keyboard shortcuts
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.G: SelectPaletteKind(TileKind.Ground); AcceptEvent(); return;
                case Key.P: SelectPaletteKind(TileKind.Platform); AcceptEvent(); return;
                case Key.V: SelectPaletteKind(TileKind.SoftVolume); AcceptEvent(); return;
                case Key.E: SelectPaletteKind(TileKind.EnemySpawn); AcceptEvent(); return;
                case Key.R: SelectPaletteKind(TileKind.Respawn); AcceptEvent(); return;
                case Key.L: SelectPaletteKind(TileKind.LevelGoal); AcceptEvent(); return;
                case Key.C: SelectPaletteKind(TileKind.Character); AcceptEvent(); return;
                case Key.S when key.CtrlPressed:
                    DoSave(); AcceptEvent(); return;
            }
        }
    }

    private Vector2 ScreenToGrid(Vector2 screen)
    {
        return (screen - _pan) / (CellSize * _zoom);
    }

    private void PaintAt(Vector2 screen)
    {
        if (InUIChrome(screen)) return;
        var g = ScreenToGrid(screen);
        int x = Mathf.FloorToInt(g.X);
        int y = Mathf.FloorToInt(g.Y);
        if (x < 0 || y < 0 || x >= LoadedMap.Width || y >= LoadedMap.Height) return;
        LoadedMap.SetCell(x, y, _brushKind, _brushVariant);
        QueueRedraw();
    }

    private void EraseAt(Vector2 screen)
    {
        if (InUIChrome(screen)) return;
        var g = ScreenToGrid(screen);
        int x = Mathf.FloorToInt(g.X);
        int y = Mathf.FloorToInt(g.Y);
        if (x < 0 || y < 0 || x >= LoadedMap.Width || y >= LoadedMap.Height) return;
        LoadedMap.EraseCell(x, y);
        QueueRedraw();
    }

    #endregion

    #region Draw

    public override void _Draw()
    {
        if (LoadedMap == null) return;
        if (_font == null) _font = ThemeDB.FallbackFont;
        if (_font == null) return; // can't render labels

        float cs = CellSize * _zoom;
        int w = LoadedMap.Width;
        int h = LoadedMap.Height;
        var vpSize = GetViewport().GetVisibleRect().Size;

        // Map bounds in screen space
        var mapTopLeft = _pan;
        var mapSize = new Vector2(w * cs, h * cs);
        var mapRect = new Rect2(mapTopLeft, mapSize);

        // ---- Draw grey "outside map" areas ----
        // Top strip
        DrawRect(new Rect2(0, 0, vpSize.X, mapTopLeft.Y),
            new Color(0.25f, 0.25f, 0.28f, 0.7f));
        // Bottom strip
        float mapBottom = mapTopLeft.Y + mapSize.Y;
        DrawRect(new Rect2(0, mapBottom, vpSize.X, vpSize.Y - mapBottom),
            new Color(0.25f, 0.25f, 0.28f, 0.7f));
        // Left strip
        DrawRect(new Rect2(0, mapTopLeft.Y, mapTopLeft.X, mapSize.Y),
            new Color(0.25f, 0.25f, 0.28f, 0.7f));
        // Right strip (only if map doesn't fill viewport)
        float mapRight = mapTopLeft.X + mapSize.X;
        if (mapRight < vpSize.X)
            DrawRect(new Rect2(mapRight, mapTopLeft.Y, vpSize.X - mapRight, mapSize.Y),
                new Color(0.25f, 0.25f, 0.28f, 0.7f));

        // Only draw visible cells
        int startX = Mathf.Max(0, Mathf.FloorToInt(-_pan.X / cs) - 1);
        int startY = Mathf.Max(0, Mathf.FloorToInt(-_pan.Y / cs) - 1);
        int endX = Mathf.Min(w, Mathf.CeilToInt((vpSize.X - _pan.X) / cs) + 1);
        int endY = Mathf.Min(h, Mathf.CeilToInt((vpSize.Y - _pan.Y) / cs) + 1);

        // Draw cells
        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                var kind = LoadedMap.GetKind(x, y);
                var variant = LoadedMap.GetVariant(x, y);
                var rect = new Rect2(
                    _pan.X + x * cs,
                    _pan.Y + y * cs,
                    cs, cs);

                if (kind == TileKind.Empty)
                {
                    // Subtle checkerboard-like grid inside the map area
                    bool light = (x + y) % 2 == 0;
                    DrawRect(rect, light
                        ? new Color(0.15f, 0.15f, 0.18f, 0.6f)
                        : new Color(0.12f, 0.12f, 0.15f, 0.6f));
                }
                else
                {
                    var color = TileLabels.GetVariantColor(kind, variant);
                    DrawRect(rect, color);

                    // Show short label if cell is big enough
                    if (cs > 14f)
                    {
                        int fontSize = Mathf.Max(6, Mathf.RoundToInt(cs * 0.24f));
                        string label = GetCellLabel(kind, variant);
                        if (!string.IsNullOrEmpty(label))
                        {
                            DrawString(_font,
                                rect.Position + new Vector2(2, rect.Size.Y * 0.35f),
                                label,
                                HorizontalAlignment.Left,
                                rect.Size.X - 4,
                                fontSize,
                                new Color(1, 1, 1, 0.9f));
                        }
                    }

                    // Thin border
                    DrawRect(rect, new Color(0, 0, 0, 0.25f), false, 0.5f);
                }
            }
        }

        // Grid bounds — prominent double border
        var bounds = new Rect2(_pan, new Vector2(w * cs, h * cs));
        // Outer glow
        DrawRect(bounds.Grow(4f), new Color(0.9f, 0.85f, 0.2f, 0.5f), false, 3f);
        // Inner crisp border
        DrawRect(bounds, new Color(0.9f, 0.85f, 0.2f, 0.85f), false, 2f);
    }

    private string GetCellLabel(TileKind kind, int variant)
    {
        var v = TileLabels.Variants.TryGetValue(kind, out var arr) ? arr : null;
        if (v == null || variant < 0 || variant >= v.Length) return "?";
        // Shorten some labels for display
        return v[variant] switch
        {
            "wonder_core" => "w.core",
            "condensed_core" => "c.core",
            "player_start" => "start",
            "respawn" => "resp",
            "fortress" => "fort",
            _ => v[variant],
        };
    }

    #endregion

    #region Save / Back

    private void OnSave()
    {
        var dlg = new AcceptDialog();
        dlg.Title = "Save Map";
        dlg.OkButtonText = "Save";

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);

        var nameLabel = new Label();
        nameLabel.Text = "Map Name:";
        vbox.AddChild(nameLabel);

        var nameEdit = new LineEdit();
        nameEdit.Text = LoadedMap.Meta.Name;
        nameEdit.CustomMinimumSize = new Vector2(300, 36);
        vbox.AddChild(nameEdit);

        dlg.AddChild(vbox);
        vbox.Position = new Vector2(16, 16);

        dlg.Confirmed += () =>
        {
            LoadedMap.Meta.Name = string.IsNullOrWhiteSpace(nameEdit.Text)
                ? "Untitled" : nameEdit.Text.Trim();
            DoSave();
        };

        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void DoSave()
    {
        MapSaveLoad.Save(LoadedMap);
        _infoLabel.Text = MapName();
        ShowToast("✓ Saved!");
    }

    private async void ShowToast(string msg)
    {
        var toast = new Label();
        toast.Text = msg;
        toast.AddThemeFontSizeOverride("font_size", 20);
        toast.AddThemeColorOverride("font_color", new Color(0.3f, 0.95f, 0.4f));
        toast.HorizontalAlignment = HorizontalAlignment.Center;
        toast.SetAnchorsPreset(LayoutPreset.TopWide);
        toast.Position = new Vector2(0, 48);
        AddChild(toast);

        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        toast.QueueFree();
    }

    private void OnBack()
    {
        // Auto-save before going back
        MapSaveLoad.Save(LoadedMap);

        var browserScene = GD.Load<PackedScene>("res://Scenes/MapCreation/MapBrowser.tscn");
        GetTree().Root.AddChild(browserScene.Instantiate());
        QueueFree();
    }

    #endregion
}

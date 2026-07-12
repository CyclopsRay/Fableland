namespace Fableland.MapCreation.Editor;

using System.Collections.Generic;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor.Tools;

/// <summary>
/// GDD §7.1 — the map editor shell. Root script of `Scenes/MapCreation/MapEditor.tscn`
/// (a thin root Control); every widget is built in code in <see cref="_Ready"/>.
///
/// STRUCTURAL RULE (GDD §7.6/§11.3): this class's own `_Draw` MUST stay empty/absent.
/// A Godot node's own `_Draw` renders BEHIND its children — the v0.5.x editor drew its
/// canvas in the root's `_Draw` and the opaque background child occluded the whole
/// thing. All world content lives in the dedicated <see cref="GridView"/> child instead
/// (built as child #2, above the canvas backdrop and below every UI panel).
/// </summary>
public partial class MapEditor : Control
{
    private EditorState _state;
    private GridView _gridView;
    private ColorRect _canvasBg;

    // MC4: the 8 editing tools + the routing/paste-mode bookkeeping that drives them.
    private ToolRegistry _tools;
    private EditorState.EditorTool _lastActiveTool;

    /// <summary>MC4 lightweight pseudo-tool state (GDD §7.3 "paste ghost follows
    /// cursor, click to stamp"): true while a paste is pending. While true, GridView's
    /// CellPressed is intercepted here (stamp + exit) instead of reaching the active
    /// tool, and <see cref="GetActiveGhost"/> shows the paste preview instead of the
    /// active tool's ghost. Not implemented as a real <see cref="ToolBase"/> because it
    /// isn't one of GDD §7.2's 8 rail tools and doesn't have a rail button/ActiveTool
    /// value of its own — it's an overlay on top of whichever tool was active.</summary>
    private bool _pasteModeActive;

    // MC5 palette/layer panel scaffolds — kept as fields per this phase's contract.
    internal VBoxContainer LayersBox;
    internal VBoxContainer PaletteBox;
    internal Button PreviewButton;

    // MC5: the panels themselves (built after the scaffolds exist, in _Ready).
    private LayerPanel _layerPanel;
    private PalettePanel _palettePanel;

    /// <summary>GDD §6 — scratch seed of the most recent "Preview generation" roll, shown
    /// appended to the button text; null/empty while no preview is showing.</summary>
    private string _previewScratchSeed;

    private Label _mapNameLabel;
    private Button _gridToggleBtn;
    private Button _effectAreasToggleBtn;
    private Label _zoomLabel;
    private Label _cursorCellLabel;
    private Label _selectionCountLabel;
    private Label _unsavedDotLabel;

    private ConfirmationDialog _discardConfirmDialog;

    private readonly System.Collections.Generic.List<(Button Btn, EditorState.EditorTool Tool)> _toolButtons = new();

    private const float TopBarHeight = 44f;
    private const float StatusBarHeight = 28f;
    private const float RailWidth = 130f;
    private const float SidePanelWidth = 260f;

    public override void _Ready()
    {
        ResolveDocument(out var doc, out var savePath);

        _state = new EditorState { Document = doc, SavePath = savePath };
        _state.CurrentLayerIndex = DefaultLayerIndex(doc);

        BuildUi();

        // MC5: LayersBox/PaletteBox exist now (built inside BuildUi -> BuildRightPanel);
        // each panel self-manages via its own EditorState subscriptions from here on.
        _layerPanel = new LayerPanel(_state, LayersBox);
        _palettePanel = new PalettePanel(_state, PaletteBox);

        _gridView.State = _state;
        _gridView.CursorCellChanged += OnCursorCellChanged;
        _gridView.ViewChanged += OnViewChanged;

        _state.StateChanged += OnStateChanged;
        _state.Commands.Changed += OnCommandsChanged;

        // MC4: tools constructed once, after GridView/State are wired above so their
        // (state, view) references are already valid.
        _tools = new ToolRegistry(_state, _gridView);
        _lastActiveTool = _state.ActiveTool;
        _gridView.GhostProvider = GetActiveGhost;
        _gridView.CellPressed += OnCellPressed;
        _gridView.CellDragged += OnCellDragged;
        _gridView.CellReleased += OnCellReleased;

        RefreshAll();
    }

    /// <summary>GDD §10/T10 §3 — one-shot handoff from the browser (or a fresh document
    /// on a direct F5 launch, per the debug rule). Consumes `EditorLaunch.MapId` and
    /// clears it immediately after reading, whether or not the load succeeded.</summary>
    private void ResolveDocument(out MapDocument doc, out string savePath)
    {
        doc = null;
        savePath = null;

        if (EditorLaunch.MapId != null)
        {
            savePath = ProjectSettings.GlobalizePath("user://maps/" + EditorLaunch.MapId + ".json");
            doc = MapJson.Load(savePath, out var warnings);
            foreach (var w in warnings) GD.PushWarning("[MapEditor] " + w);
        }
        EditorLaunch.MapId = null;

        if (doc == null)
        {
            doc = MapDocument.CreateNew("Untitled");
            savePath = ProjectSettings.GlobalizePath("user://maps/" + doc.Id + ".json");
        }
    }

    /// <summary>Open decision (GDD silent): default the current layer to the
    /// battlefield if one exists, since that's the layer gameplay content is painted
    /// on; fall back to index 0 (e.g. `CreateNew` always seeds exactly one battlefield
    /// at index 0 anyway).</summary>
    private static int DefaultLayerIndex(MapDocument doc)
    {
        if (doc?.Layers == null) return 0;
        for (int i = 0; i < doc.Layers.Count; i++)
            if (doc.Layers[i]?.Role == MapLayerData.RoleBattlefield) return i;
        return 0;
    }

    // ---------------------------------------------------------------- UI build

    private void BuildUi()
    {
        // 1) Canvas backdrop (GDD §5): decorative, MUST ignore mouse (KNOWLEDGE v0.2.4)
        // so it never eats clicks meant for GridView/panels. MC5 updates .Color when
        // the map's canvas color changes.
        _canvasBg = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        _canvasBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _canvasBg.Color = ColorFromHex(_state.Document.Canvas?.Color, new Color(0.53f, 0.81f, 0.92f));
        AddChild(_canvasBg);

        // 2) World-draw child, full-rect; the UI panels below overlap it visually
        // (GDD §7.6/§11.3 — see the class-header comment for why this ordering matters).
        _gridView = new GridView();
        _gridView.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_gridView);

        // 3) UI chrome.
        BuildTopBar();
        BuildLeftRail();
        BuildRightPanel();
        BuildStatusBar();
        BuildDialogs();
    }

    private void BuildTopBar()
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 0f; panel.AnchorRight = 1f; panel.AnchorTop = 0f; panel.AnchorBottom = 0f;
        panel.OffsetBottom = TopBarHeight;
        AddChild(panel);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(hbox);

        _mapNameLabel = new Label { Text = _state.Document.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        hbox.AddChild(_mapNameLabel);

        var saveBtn = new Button { Text = "Save" };
        saveBtn.Pressed += DoSave;
        hbox.AddChild(saveBtn);

        _gridToggleBtn = new Button { Text = "Grid", ToggleMode = true };
        _gridToggleBtn.SetPressedNoSignal(_state.ShowGrid);
        _gridToggleBtn.Pressed += () =>
        {
            _state.ShowGrid = _gridToggleBtn.ButtonPressed;
            _state.RaiseStateChanged();
        };
        hbox.AddChild(_gridToggleBtn);

        _effectAreasToggleBtn = new Button { Text = "Effect areas", ToggleMode = true };
        _effectAreasToggleBtn.SetPressedNoSignal(_state.ShowEffectAreas);
        _effectAreasToggleBtn.Pressed += () =>
        {
            _state.ShowEffectAreas = _effectAreasToggleBtn.ButtonPressed;
            _state.RaiseStateChanged();
        };
        hbox.AddChild(_effectAreasToggleBtn);

        // GDD §6 — rolls a scratch seed and previews rule-tile generation; Esc clears
        // (handled in _UnhandledKeyInput's mapedit_deselect branch, already wired).
        PreviewButton = new Button
        {
            Text = "Preview gen",
            TooltipText = "Roll a scratch seed and preview rule-tile generation (Esc clears)",
        };
        PreviewButton.Pressed += OnPreviewGenPressed;
        hbox.AddChild(PreviewButton);

        _zoomLabel = new Label { Text = "100%" };
        hbox.AddChild(_zoomLabel);

        // Computed, never a literal — GDD §7.5 permanent cell-size indicator.
        var cellLabel = new Label { Text = $"Cell = {(int)Units.PixelsPerMeter} px = 1 m" };
        hbox.AddChild(cellLabel);

        var backBtn = new Button { Text = "Back" };
        backBtn.Pressed += OnBackPressed;
        hbox.AddChild(backBtn);
    }

    private void BuildLeftRail()
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 0f; panel.AnchorRight = 0f; panel.AnchorTop = 0f; panel.AnchorBottom = 1f;
        panel.OffsetRight = RailWidth;
        panel.OffsetTop = TopBarHeight; panel.OffsetBottom = -StatusBarHeight;
        AddChild(panel);

        var rail = new VBoxContainer();
        rail.AddThemeConstantOverride("separation", 4);
        panel.AddChild(rail);

        var group = new ButtonGroup();
        void AddTool(string label, EditorState.EditorTool tool)
        {
            var btn = new Button
            {
                Text = label,
                ToggleMode = true,
                ButtonGroup = group,
                CustomMinimumSize = new Vector2(0, 32),
            };
            btn.Pressed += () => SetTool(tool);
            rail.AddChild(btn);
            _toolButtons.Add((btn, tool));
        }

        AddTool("Paint (B)", EditorState.EditorTool.Paint);
        AddTool("Erase (E)", EditorState.EditorTool.Erase);
        AddTool("Rect (R)", EditorState.EditorTool.Rect);
        AddTool("Marquee (M)", EditorState.EditorTool.Marquee);
        AddTool("Lasso (L)", EditorState.EditorTool.Lasso);
        AddTool("Move (V)", EditorState.EditorTool.Move);
        AddTool("Eyedrop (I)", EditorState.EditorTool.Eyedropper);
        AddTool("Bucket (F)", EditorState.EditorTool.Bucket);

        SyncRailHighlight();
    }

    private void BuildRightPanel()
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 1f; panel.AnchorRight = 1f; panel.AnchorTop = 0f; panel.AnchorBottom = 1f;
        panel.OffsetLeft = -SidePanelWidth;
        panel.OffsetTop = TopBarHeight; panel.OffsetBottom = -StatusBarHeight;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        panel.AddChild(vbox);

        vbox.AddChild(new Label { Text = "Layers" });
        LayersBox = new VBoxContainer();
        vbox.AddChild(LayersBox);

        vbox.AddChild(new Label { Text = "Palette" });
        PaletteBox = new VBoxContainer();
        vbox.AddChild(PaletteBox);
    }

    private void BuildStatusBar()
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 0f; panel.AnchorRight = 1f; panel.AnchorTop = 1f; panel.AnchorBottom = 1f;
        panel.OffsetTop = -StatusBarHeight;
        AddChild(panel);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 16);
        panel.AddChild(hbox);

        _cursorCellLabel = new Label { Text = "Cell: —" };
        hbox.AddChild(_cursorCellLabel);

        _selectionCountLabel = new Label { Text = "Sel: 0" };
        hbox.AddChild(_selectionCountLabel);

        _unsavedDotLabel = new Label { Text = "●", TooltipText = "Unsaved changes", Visible = false };
        hbox.AddChild(_unsavedDotLabel);
    }

    private void BuildDialogs()
    {
        _discardConfirmDialog = new ConfirmationDialog
        {
            Title = "Discard changes?",
            DialogText = "Discard unsaved changes?",
        };
        AddChild(_discardConfirmDialog);
        _discardConfirmDialog.Confirmed += () =>
            GetTree().ChangeSceneToFile("res://Scenes/MapCreation/MapBrowser.tscn");
    }

    // ---------------------------------------------------------------- actions

    private void DoSave()
    {
        // Orchestrator fix (F-MC4, review MINOR): MapJson.Save is pure Data-layer code and
        // lets IO exceptions propagate; the Editor layer catches and degrades (T10 §5 —
        // an IO failure must not crash the editor). MarkSaved only on success, so the
        // unsaved dot keeps telling the truth after a failed save.
        try
        {
            MapJson.Save(_state.Document, _state.SavePath);
            _state.Commands.MarkSaved();
        }
        catch (System.Exception e)
        {
            GD.PushError($"[MapEditor] save failed for '{_state.SavePath}': {e.Message}");
        }
    }

    private void OnBackPressed()
    {
        if (_state.Commands.IsDirty) _discardConfirmDialog.PopupCentered();
        else GetTree().ChangeSceneToFile("res://Scenes/MapCreation/MapBrowser.tscn");
    }

    private void SetTool(EditorState.EditorTool tool)
    {
        _state.ActiveTool = tool;
        _state.RaiseStateChanged();
    }

    /// <summary>GDD §6 — mints a fresh scratch seed via the one sanctioned non-deterministic
    /// mint (<see cref="DetRandom.NewSeed"/>, an editor-only affordance), resolves rule zones
    /// deterministically from THAT seed, and stores the result as view-only preview state
    /// (never a command, never dirties the save). Pressing again re-rolls; debug warnings
    /// only (GDD §6 "degrade gracefully ... GD.PushWarning in debug").</summary>
    private void OnPreviewGenPressed()
    {
        _previewScratchSeed = DetRandom.NewSeed();
        var rng = new DetRandom(_previewScratchSeed);
        var results = RuleResolver.Resolve(_state.Document, rng, out var warnings);

        if (OS.IsDebugBuild())
            foreach (var w in warnings) GD.PushWarning("[PreviewGen] " + w);

        _state.Preview = results;
        _state.RaiseStateChanged();
    }

    // ---------------------------------------------------------------- refresh

    private void RefreshAll()
    {
        OnViewChanged();
        OnCursorCellChanged(null);
        OnStateChanged();
        OnCommandsChanged();
    }

    private void OnCursorCellChanged(Vector2I? cell)
    {
        _cursorCellLabel.Text = cell.HasValue ? $"Cell: {cell.Value.X}, {cell.Value.Y}" : "Cell: —";
    }

    private void OnViewChanged()
    {
        _zoomLabel.Text = $"{_gridView.Zoom * 100f:0}%";
    }

    private void OnStateChanged()
    {
        // MC4: a tool switch (rail click or keyboard shortcut) deactivates the
        // PREVIOUS tool so it clears its in-progress stroke/drag/lasso state.
        if (_state.ActiveTool != _lastActiveTool)
        {
            _tools?.Get(_lastActiveTool)?.OnDeactivated();
            _lastActiveTool = _state.ActiveTool;
        }

        _gridView.QueueRedraw();
        SyncRailHighlight();
        _gridToggleBtn.SetPressedNoSignal(_state.ShowGrid);
        _effectAreasToggleBtn.SetPressedNoSignal(_state.ShowEffectAreas);
        _selectionCountLabel.Text = $"Sel: {_state.Selection.Count}";

        // GDD §5 — canvas color is editable in map properties (MC5's LayerPanel Canvas
        // row); re-read it every StateChanged rather than special-casing the one command
        // that can change it (cheap: one hex-parse + ColorRect write).
        _canvasBg.Color = ColorFromHex(_state.Document.Canvas?.Color, new Color(0.53f, 0.81f, 0.92f));

        // GDD §6 — button text carries the active scratch seed; resets when Esc clears
        // State.Preview (mapedit_deselect branch below) or before the first roll.
        PreviewButton.Text = _state.Preview != null && !string.IsNullOrEmpty(_previewScratchSeed)
            ? $"Preview gen [{_previewScratchSeed}]"
            : "Preview gen";
    }

    private void OnCommandsChanged()
    {
        _unsavedDotLabel.Visible = _state.Commands.IsDirty;
    }

    // ---------------------------------------------------------------- tool routing (MC4)

    /// <summary>Routes a GridView press to either the paste-stamp (if paste mode is
    /// pending) or the currently active tool, looked up fresh by
    /// <see cref="EditorState.ActiveTool"/> so a tool switch mid-frame can't leave a
    /// stale reference. Raises <see cref="EditorState.StateChanged"/> exactly once
    /// afterward (tools themselves never raise it — see `ToolBase`'s class doc).</summary>
    private void OnCellPressed(Vector2I cell, InputEventMouseButton ev)
    {
        if (_pasteModeActive)
        {
            StampPaste(cell);
        }
        else
        {
            _tools.Get(_state.ActiveTool)?.OnPressed(cell, ev);
        }
        _state.RaiseStateChanged();
    }

    private void OnCellDragged(Vector2I cell)
    {
        if (_pasteModeActive) return; // paste stamps on press only; nothing to drag
        _tools.Get(_state.ActiveTool)?.OnDragged(cell);
        _state.RaiseStateChanged();
    }

    private void OnCellReleased(Vector2I cell)
    {
        if (_pasteModeActive) return;
        _tools.Get(_state.ActiveTool)?.OnReleased(cell);
        _state.RaiseStateChanged();
    }

    /// <summary>GridView's ghost hook: paste mode's stamp preview takes over from
    /// whatever the active tool would otherwise show (GDD §7.3 "paste ghost follows
    /// cursor").</summary>
    private GhostInfo GetActiveGhost()
    {
        if (_pasteModeActive) return BuildPasteGhost();
        return _tools.Get(_state.ActiveTool)?.GetGhost();
    }

    /// <summary>Q-MC3 overwrite semantics, same rule as Move: any existing tile whose
    /// footprint overlaps a dropped tile's footprint is removed as part of the SAME
    /// command. Per-entry skips (not a whole-paste abort, unlike Move): a clipboard
    /// entry that would land partly outside the grid, or whose def isn't legal on the
    /// current layer's role, is simply dropped from the stamp.</summary>
    private void StampPaste(Vector2I anchorCell)
    {
        _pasteModeActive = false; // exits paste mode regardless of outcome (GDD §7.3)

        var layer = _state.CurrentLayer;
        if (layer == null || _state.Clipboard.Count == 0) return;

        var occ = _state.OccupancyOf(_state.CurrentLayerIndex);
        var toAdd = new List<PlacedTile>();
        var toRemoveSet = new HashSet<PlacedTile>();

        foreach (var (defId, dx, dy) in _state.Clipboard)
        {
            if (!TileRegistry.TryGet(defId, out var def)) continue;
            if ((def.AllowedRoles & ToolBase.RoleMaskOf(layer.Role)) == 0) continue;

            int x = anchorCell.X + dx, y = anchorCell.Y + dy;
            int fw = def.FootprintW, fh = def.FootprintH;
            if (x < 0 || y < 0 || x + fw > layer.GridW || y + fh > layer.GridH) continue;

            for (int cy = 0; cy < fh; cy++)
                for (int cx = 0; cx < fw; cx++)
                {
                    var existing = occ.TileAt(x + cx, y + cy);
                    if (existing != null) toRemoveSet.Add(existing);
                }

            toAdd.Add(new PlacedTile { DefId = def.Id, X = x, Y = y });
        }

        if (toAdd.Count > 0)
            _state.Commands.Push(new TileBatchCommand(_state, layer, "Paste", toAdd, new List<PlacedTile>(toRemoveSet)));
    }

    private GhostInfo BuildPasteGhost()
    {
        var layer = _state.CurrentLayer;
        var cursor = _gridView.HoverCell;
        if (layer == null || !cursor.HasValue || _state.Clipboard.Count == 0) return null;

        var cells = new List<(Vector2I, int, int, string)>();
        bool valid = true;

        foreach (var (defId, dx, dy) in _state.Clipboard)
        {
            int fw = 1, fh = 1;
            bool legal = TileRegistry.TryGet(defId, out var def);
            if (legal)
            {
                fw = def.FootprintW;
                fh = def.FootprintH;
                legal = (def.AllowedRoles & ToolBase.RoleMaskOf(layer.Role)) != 0;
            }

            int x = cursor.Value.X + dx, y = cursor.Value.Y + dy;
            if (!legal || x < 0 || y < 0 || x + fw > layer.GridW || y + fh > layer.GridH) valid = false;

            cells.Add((new Vector2I(x, y), fw, fh, defId));
        }

        return new GhostInfo { Valid = valid, Cells = cells };
    }

    // ---------------------------------------------------------------- selection shortcuts (MC4)

    /// <summary>Clipboard = selection's tiles as (DefId, Dx, Dy) offsets relative to
    /// the selection's min-(y,x) anchor tile (GDD §7.3; same sort order as the §6
    /// zone-resolution "sorted by anchor y, then x" convention).</summary>
    private void CopySelectionInternal()
    {
        _state.Clipboard.Clear();
        if (_state.Selection.Count == 0) return;

        PlacedTile anchor = null;
        foreach (var t in _state.Selection)
            if (anchor == null || t.Y < anchor.Y || (t.Y == anchor.Y && t.X < anchor.X)) anchor = t;

        foreach (var t in _state.Selection)
            _state.Clipboard.Add((t.DefId, t.X - anchor.X, t.Y - anchor.Y));
    }

    /// <summary>Deletes every selected tile as one command; Selection is cleared
    /// (an Undo restores the tiles to the layer but does not re-select them — the
    /// simplest default; see final report).</summary>
    private void DeleteSelectionInternal()
    {
        var layer = _state.CurrentLayer;
        if (layer == null || _state.Selection.Count == 0) return;
        var toRemove = new List<PlacedTile>(_state.Selection);
        _state.Commands.Push(new TileBatchCommand(_state, layer, "Delete", null, toRemove));
        _state.Selection.Clear();
    }

    private void SyncRailHighlight()
    {
        foreach (var (btn, tool) in _toolButtons)
            btn.SetPressedNoSignal(tool == _state.ActiveTool);
    }

    // ---------------------------------------------------------------- shortcuts

    /// <summary>GDD §7.3. Runs from `_UnhandledKeyInput` so a focused LineEdit/dialog
    /// eats keys first. No `_Process` loop anywhere in this class — everything here is
    /// event-driven. `mapedit_redo` is checked BEFORE `mapedit_undo`: Ctrl+Shift+Z also
    /// satisfies the Ctrl+Z action match (Godot's default non-exact modifier match), so
    /// redo must win the tie or Shift+Ctrl+Z would silently undo instead.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event.IsActionPressed("mapedit_save")) { DoSave(); Accept(); return; }

        if (@event.IsActionPressed("mapedit_grid"))
        {
            _state.ShowGrid = !_state.ShowGrid;
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        if (@event.IsActionPressed("mapedit_redo"))
        {
            _state.Commands.Redo();
            _state.InvalidateOccupancy();
            _state.RaiseStateChanged();
            Accept();
            return;
        }
        if (@event.IsActionPressed("mapedit_undo"))
        {
            _state.Commands.Undo();
            _state.InvalidateOccupancy();
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        if (@event.IsActionPressed("mapedit_layer_up"))
        {
            _state.CurrentLayerIndex -= 1;
            _state.RaiseStateChanged();
            Accept();
            return;
        }
        if (@event.IsActionPressed("mapedit_layer_down"))
        {
            _state.CurrentLayerIndex += 1;
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        if (@event.IsActionPressed("mapedit_zoom_in")) { _gridView.ZoomStep(1); Accept(); return; }
        if (@event.IsActionPressed("mapedit_zoom_out")) { _gridView.ZoomStep(-1); Accept(); return; }
        if (@event.IsActionPressed("mapedit_zoom_reset")) { _gridView.ZoomReset(); Accept(); return; }

        if (@event.IsActionPressed("mapedit_select_all"))
        {
            _state.Selection.Clear();
            var layer = _state.CurrentLayer;
            if (layer?.Tiles != null) foreach (var t in layer.Tiles) _state.Selection.Add(t);
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        // GDD §6: Esc clears the generation preview FIRST if one is showing; MC4 adds
        // paste-mode cancel (highest priority — it's the most "modal" of the three)
        // and, failing both, clears Selection + cancels any in-progress tool drag.
        if (@event.IsActionPressed("mapedit_deselect"))
        {
            if (_pasteModeActive)
            {
                _pasteModeActive = false;
            }
            else if (_state.Preview != null)
            {
                _state.Preview = null;
            }
            else
            {
                _state.Selection.Clear();
                _tools.Get(_state.ActiveTool)?.OnDeactivated();
            }
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        if (@event.IsActionPressed("mapedit_delete"))
        {
            DeleteSelectionInternal();
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        if (@event.IsActionPressed("mapedit_cut"))
        {
            CopySelectionInternal();
            DeleteSelectionInternal();
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        if (@event.IsActionPressed("mapedit_copy"))
        {
            CopySelectionInternal();
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        if (@event.IsActionPressed("mapedit_paste"))
        {
            // Empty clipboard: no-op (paste mode never engages).
            _pasteModeActive = _state.Clipboard.Count > 0;
            _state.RaiseStateChanged();
            Accept();
            return;
        }

        // Orchestrator fix (F-MC2, review BLOCKER): the bare-key tool shortcuts MUST be
        // checked AFTER every modifier-combo action above. IsActionPressed defaults to
        // non-exact modifier matching, so Ctrl+V also satisfies the bare-V
        // `mapedit_tool_move` action — checked first, it swallowed the event and
        // `mapedit_paste` could never fire. Same tie-break family as the
        // redo-before-undo ordering documented at the top of this method.
        if (@event.IsActionPressed("mapedit_tool_paint")) { SetTool(EditorState.EditorTool.Paint); Accept(); return; }
        if (@event.IsActionPressed("mapedit_tool_erase")) { SetTool(EditorState.EditorTool.Erase); Accept(); return; }
        if (@event.IsActionPressed("mapedit_tool_rect")) { SetTool(EditorState.EditorTool.Rect); Accept(); return; }
        if (@event.IsActionPressed("mapedit_tool_marquee")) { SetTool(EditorState.EditorTool.Marquee); Accept(); return; }
        if (@event.IsActionPressed("mapedit_tool_lasso")) { SetTool(EditorState.EditorTool.Lasso); Accept(); return; }
        if (@event.IsActionPressed("mapedit_tool_move")) { SetTool(EditorState.EditorTool.Move); Accept(); return; }
        if (@event.IsActionPressed("mapedit_tool_eyedropper")) { SetTool(EditorState.EditorTool.Eyedropper); Accept(); return; }
        if (@event.IsActionPressed("mapedit_tool_bucket")) { SetTool(EditorState.EditorTool.Bucket); Accept(); return; }
    }

    private void Accept() => GetViewport().SetInputAsHandled();

    /// <summary>Hex string to Color, with a caller-supplied fallback for bad/missing
    /// strings (GDD §5 canvas default is skyblue, not magenta, unlike tile colors).</summary>
    private static Color ColorFromHex(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try { return new Color(hex); }
        catch { return fallback; }
    }
}

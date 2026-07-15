namespace Fableland.MapCreation.Editor;

using System.Collections.Generic;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor.Tools;
using Fableland.MapCreation.Playtest;

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
	// Item 1: the palette now lives in a bottom dock (its own foldable strip), not the right panel.
	private Control _paletteDockContent;
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

	// Bug-fix (user report): every bar/panel can be folded to a header-only strip so it
	// stops covering the canvas. Panel refs kept so each fold closure (built where the bar
	// is constructed) can resize the right dimension.
	private PanelContainer _topBarPanel;
	private PanelContainer _leftRailPanel;
	private PanelContainer _rightPanel;
	private PanelContainer _statusBarPanel;
	private PanelContainer _paletteDockPanel;

	// Item 6: per-tile-kind effect-area painter (global store, TileEffectStore).
	private AcceptDialog _effectDialog;
	private Label _effectDialogTitle;
	private Panel[] _effectCells;      // sub-cell toggles, row-major across full footprint
	private bool[] _effectMaskBits;    // same length as _effectCells
	private bool _effectDragging;
	private bool _effectPaintValue;    // the value being painted during a drag (true=on, false=off)
	private Color _effectPaintColor = Colors.White;
	private string _effectDialogDefId;
	private string _effectStorePath;
	private TextureRect _effectSpriteBg; // tile sprite shown behind the sub-cell grid
	private Control _effectGridHost;     // container that stacks sprite + grid + cell lines
	private GridContainer _effectGrid;   // single flat grid, 0 separation
	private Control _effectCellLines;    // transparent overlay that draws footprint-cell boundaries
	private int _effectGridCols;         // FootprintW × SubcellsPerAxis, for CellIndexAt
	private float _effectSubCellSize;    // computed px per sub-cell, for CellIndexAt
	private int _effectFootprintW;       // tile footprint cells wide, for cell-line drawing
	private int _effectFootprintH;       // tile footprint cells tall

	// Playtest session. It owns a disposable runtime map and Pomegraknight while the
	// editor document remains untouched beneath it.
	private bool _previewActive;
	private Label _previewHint;
	private MapPlaytestController _playtest;

	private const float TopBarHeight = 44f;
	private const float StatusBarHeight = 28f;
	private const float RailWidth = 130f;
	private const float SidePanelWidth = 260f;
	private const float PaletteDockHeight = 132f;

	private const float FoldedBarThickness = 22f;
	private const float FoldedRailWidth = 26f;
	private const float FoldedPanelWidth = 140f;

	/// <summary>Bottom offset (from the control's bottom edge) where the left rail and right
	/// panel stop — above both the status bar and the palette dock.</summary>
	private float SidePanelBottomOffset => -(StatusBarHeight + (_paletteDockFolded ? FoldedBarThickness : PaletteDockHeight));
	private bool _paletteDockFolded;

	public override void _Ready()
	{
		ResolveDocument(out var doc, out var savePath);

		_state = new EditorState { Document = doc, SavePath = savePath };
		_state.CurrentLayerIndex = DefaultLayerIndex(doc);

		// Item 6: load the global per-tile-kind effect overrides before the first draw so the
		// "Show effect areas" overlay already reflects saved masks.
		_effectStorePath = ProjectSettings.GlobalizePath("user://tile_effects.json");
		TileEffectStore.Load(_effectStorePath, out var effectWarnings);
		foreach (var w in effectWarnings) GD.PushWarning("[MapEditor] " + w);

		BuildUi();

		// LayersBox / the palette dock exist now (built inside BuildUi); each panel
		// self-manages via its own EditorState subscriptions from here on.
		_layerPanel = new LayerPanel(_state, LayersBox);
		_palettePanel = new PalettePanel(_state, _paletteDockContent, OnConfigureEffect);

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
		BuildPaletteDock();
		BuildStatusBar();
		BuildPreviewHint();
		BuildDialogs();
		BuildEffectDialog();
	}

	private void BuildTopBar()
	{
		_topBarPanel = new PanelContainer();
		_topBarPanel.AnchorLeft = 0f; _topBarPanel.AnchorRight = 1f; _topBarPanel.AnchorTop = 0f; _topBarPanel.AnchorBottom = 0f;
		_topBarPanel.OffsetBottom = TopBarHeight;
		AddChild(_topBarPanel);

		var outer = new HBoxContainer();
		outer.AddThemeConstantOverride("separation", 4);
		_topBarPanel.AddChild(outer);

		var foldBtn = BuildFoldButton("Fold top bar");
		outer.AddChild(foldBtn);

		var hbox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		hbox.AddThemeConstantOverride("separation", 10);
		outer.AddChild(hbox);

		_mapNameLabel = new Label { Text = _state.Document.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		hbox.AddChild(_mapNameLabel);

		// Enters an isolated runtime playtest of this map.
		var playBtn = new Button
		{
			Text = "▶ Play",
			TooltipText = "Playtest this map as Pomegraknight. Esc returns to editing.",
		};
		playBtn.Pressed += EnterPreview;
		hbox.AddChild(playBtn);

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

		// Item 4 — commit rule-tile generation as REAL, undoable tiles (Preview gen only
		// shows a throwaway overlay; this stamps the clouds into the layers).
		var generateBtn = new Button
		{
			Text = "Generate",
			TooltipText = "Stamp real tiles from rule zones into the map (undoable)",
		};
		generateBtn.Pressed += OnGeneratePressed;
		hbox.AddChild(generateBtn);

		var flipBtn = new Button
		{
			Text = "Flip H",
			TooltipText = "Flip selected object tiles horizontally (H)",
		};
		flipBtn.Pressed += FlipSelectionHorizontal;
		hbox.AddChild(flipBtn);

		_zoomLabel = new Label { Text = "100%" };
		hbox.AddChild(_zoomLabel);

		// Computed, never a literal — GDD §7.5 permanent cell-size indicator.
		var cellLabel = new Label { Text = $"Cell = {(int)MapGrid.PixelsPerCell} px = {MapGrid.MetersPerCell:0} m" };
		hbox.AddChild(cellLabel);

		var backBtn = new Button { Text = "Back" };
		backBtn.Pressed += OnBackPressed;
		hbox.AddChild(backBtn);

		bool folded = false;
		foldBtn.Pressed += () =>
		{
			folded = !folded;
			foldBtn.Text = folded ? "▸" : "▾";
			hbox.Visible = !folded;
			_topBarPanel.OffsetBottom = folded ? FoldedBarThickness : TopBarHeight;
		};
	}

	private void BuildLeftRail()
	{
		_leftRailPanel = new PanelContainer();
		_leftRailPanel.AnchorLeft = 0f; _leftRailPanel.AnchorRight = 0f; _leftRailPanel.AnchorTop = 0f; _leftRailPanel.AnchorBottom = 1f;
		_leftRailPanel.OffsetRight = RailWidth;
		_leftRailPanel.OffsetTop = TopBarHeight; _leftRailPanel.OffsetBottom = SidePanelBottomOffset;
		AddChild(_leftRailPanel);

		var outer = new VBoxContainer();
		outer.AddThemeConstantOverride("separation", 4);
		_leftRailPanel.AddChild(outer);

		var foldBtn = BuildFoldButton("Fold tool rail");
		outer.AddChild(foldBtn);

		var rail = new VBoxContainer();
		rail.AddThemeConstantOverride("separation", 4);
		outer.AddChild(rail);

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

		bool folded = false;
		foldBtn.Pressed += () =>
		{
			folded = !folded;
			foldBtn.Text = folded ? "▸" : "▾";
			rail.Visible = !folded;
			_leftRailPanel.OffsetRight = folded ? FoldedRailWidth : RailWidth;
		};
	}

	/// <summary>Item 1 — the right panel is now Layers-only (the palette moved to the bottom
	/// dock, <see cref="BuildPaletteDock"/>), so the Layers scroll expands to the full panel
	/// height instead of splitting space with the palette.</summary>
	private void BuildRightPanel()
	{
		_rightPanel = new PanelContainer();
		_rightPanel.AnchorLeft = 1f; _rightPanel.AnchorRight = 1f; _rightPanel.AnchorTop = 0f; _rightPanel.AnchorBottom = 1f;
		_rightPanel.OffsetLeft = -SidePanelWidth;
		_rightPanel.OffsetTop = TopBarHeight; _rightPanel.OffsetBottom = SidePanelBottomOffset;
		AddChild(_rightPanel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		_rightPanel.AddChild(vbox);

		var layersHeader = new HBoxContainer();
		var layersFoldBtn = BuildFoldButton("Fold layers panel");
		layersHeader.AddChild(layersFoldBtn);
		layersHeader.AddChild(new Label { Text = "Layers", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		vbox.AddChild(layersHeader);

		var layersScroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 100),
		};
		LayersBox = new VBoxContainer();
		layersScroll.AddChild(LayersBox);
		vbox.AddChild(layersScroll);

		bool folded = false;
		layersFoldBtn.Pressed += () =>
		{
			folded = !folded;
			layersFoldBtn.Text = folded ? "▸" : "▾";
			layersScroll.Visible = !folded;
			_rightPanel.OffsetLeft = folded ? -FoldedPanelWidth : -SidePanelWidth;
		};
	}

	/// <summary>Item 1 — the tile palette as a full-width foldable dock strip along the bottom,
	/// above the status bar (a horizontal scroll of category-grouped tile chips, built by
	/// <see cref="PalettePanel"/>). Folding it collapses to a header strip and lets the left
	/// rail / right panel reclaim the freed vertical space (<see cref="SidePanelBottomOffset"/>).</summary>
	private void BuildPaletteDock()
	{
		_paletteDockPanel = new PanelContainer();
		_paletteDockPanel.AnchorLeft = 0f; _paletteDockPanel.AnchorRight = 1f;
		_paletteDockPanel.AnchorTop = 1f; _paletteDockPanel.AnchorBottom = 1f;
		_paletteDockPanel.OffsetTop = -(StatusBarHeight + PaletteDockHeight);
		_paletteDockPanel.OffsetBottom = -StatusBarHeight;
		AddChild(_paletteDockPanel);

		var outer = new HBoxContainer();
		outer.AddThemeConstantOverride("separation", 4);
		_paletteDockPanel.AddChild(outer);

		var foldBtn = BuildFoldButton("Fold palette dock");
		outer.AddChild(foldBtn);

		var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		outer.AddChild(body);
		body.AddChild(new Label { Text = "Palette", MouseFilter = Control.MouseFilterEnum.Ignore });

		// A MarginContainer lays out PalettePanel's single scroll child to fill the dock body.
		var content = new MarginContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		body.AddChild(content);
		_paletteDockContent = content;

		foldBtn.Pressed += () =>
		{
			_paletteDockFolded = !_paletteDockFolded;
			foldBtn.Text = _paletteDockFolded ? "▸" : "▾";
			body.Visible = !_paletteDockFolded;
			_paletteDockPanel.OffsetTop = _paletteDockFolded
				? -(StatusBarHeight + FoldedBarThickness)
				: -(StatusBarHeight + PaletteDockHeight);
			// Let the side panels reclaim (or yield) the freed space.
			_leftRailPanel.OffsetBottom = SidePanelBottomOffset;
			_rightPanel.OffsetBottom = SidePanelBottomOffset;
		};
	}

	private void BuildStatusBar()
	{
		_statusBarPanel = new PanelContainer();
		_statusBarPanel.AnchorLeft = 0f; _statusBarPanel.AnchorRight = 1f; _statusBarPanel.AnchorTop = 1f; _statusBarPanel.AnchorBottom = 1f;
		_statusBarPanel.OffsetTop = -StatusBarHeight;
		AddChild(_statusBarPanel);

		var outer = new HBoxContainer();
		outer.AddThemeConstantOverride("separation", 4);
		_statusBarPanel.AddChild(outer);

		var foldBtn = BuildFoldButton("Fold status bar");
		outer.AddChild(foldBtn);

		var hbox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		hbox.AddThemeConstantOverride("separation", 16);
		outer.AddChild(hbox);

		_cursorCellLabel = new Label { Text = "Cell: —" };
		hbox.AddChild(_cursorCellLabel);

		_selectionCountLabel = new Label { Text = "Sel: 0" };
		hbox.AddChild(_selectionCountLabel);

		_unsavedDotLabel = new Label { Text = "●", TooltipText = "Unsaved changes", Visible = false };
		hbox.AddChild(_unsavedDotLabel);

		bool folded = false;
		foldBtn.Pressed += () =>
		{
			folded = !folded;
			foldBtn.Text = folded ? "▸" : "▾";
			hbox.Visible = !folded;
			_statusBarPanel.OffsetTop = folded ? -FoldedBarThickness : -StatusBarHeight;
		};
	}

	/// <summary>Small always-visible toggle button shared by every foldable bar/section
	/// (top bar, tool rail, layers/palette sections, status bar) — flat so it doesn't look
	/// like a tool button, fixed tiny size so it stays a minimal header even when folded.</summary>
	private static Button BuildFoldButton(string tooltip) => new()
	{
		Text = "▾",
		Flat = true,
		FocusMode = Control.FocusModeEnum.None,
		CustomMinimumSize = new Vector2(20, 20),
		TooltipText = tooltip,
	};

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

	/// <summary>Legacy editor hint retained for layout compatibility; the runtime playtest
	/// provides its own fixed-screen hint so a Camera2D cannot move it.</summary>
	private void BuildPreviewHint()
	{
		_previewHint = new Label
		{
			Text = "▶ Playtest — Esc to exit",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		_previewHint.AnchorLeft = 0f; _previewHint.AnchorRight = 1f;
		_previewHint.AnchorTop = 0f; _previewHint.AnchorBottom = 0f;
		_previewHint.OffsetTop = 10f; _previewHint.OffsetBottom = 34f;
		AddChild(_previewHint);
	}

	/// <summary>Item 6 — the per-tile-kind effect-area painter. Shows every sub-cell across
	/// the tile's ENTIRE footprint (e.g. a 3×1 sun lounger shows a 12×4 sub-cell grid), with
	/// the tile's sprite rendered behind the grid as a visual reference. Drag to paint.
	/// OK saves the masks into the global <see cref="TileEffectStore"/> (applies to every
	/// instance of the kind, in every map); "Clear" reverts to the def's code default.
	/// The dialog shell is built once; <see cref="OnConfigureEffect"/> rebuilds the grid
	/// and sprite background each time a different tile's gear is clicked.</summary>
	private void BuildEffectDialog()
	{
		_effectDialog = new AcceptDialog
		{
			Title = "Effect Area",
			MinSize = new Vector2I(280, 300),
		};
		AddChild(_effectDialog);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 6);
		_effectDialog.AddChild(vbox);

		_effectDialogTitle = new Label { Text = "" };
		vbox.AddChild(_effectDialogTitle);
		vbox.AddChild(new Label
		{
			Text = "Click or drag to toggle sub-cells across the full footprint.\nApplies to every placed instance, in every map.",
		});

		// Host stacks three overlapping layers: sprite (back), sub-cell grid, cell-boundary lines (front).
		// The host handles all drag input; children use MouseFilter.Ignore so events bubble up.
		_effectGridHost = new Control
		{
			// Keep the host at the calculated grid dimensions even when the dialog is wider.
			// Otherwise a VBox stretches a 1x1 grid to the dialog width while hit-testing
			// still divides by the calculated sub-cell size.
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_effectGridHost.GuiInput += OnEffectGridInput;
		vbox.AddChild(_effectGridHost);

		// Layer 1 — sprite background, scaled to exactly fill the host.
		_effectSpriteBg = new TextureRect
		{
			StretchMode = TextureRect.StretchModeEnum.Scale,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_effectSpriteBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_effectGridHost.AddChild(_effectSpriteBg);

		// Layer 2 — sub-cell grid, zero gaps (boundaries are drawn by layer 3).
		_effectGrid = new GridContainer();
		_effectGrid.AddThemeConstantOverride("h_separation", 0);
		_effectGrid.AddThemeConstantOverride("v_separation", 0);
		_effectGrid.MouseFilter = Control.MouseFilterEnum.Ignore;
		_effectGrid.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_effectGridHost.AddChild(_effectGrid);

		// Layer 3 — transparent overlay that draws thick lines at footprint-cell boundaries.
		_effectCellLines = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_effectCellLines.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_effectCellLines.Draw += OnEffectCellLinesDraw;
		_effectGridHost.AddChild(_effectCellLines);

		_effectDialog.GetOkButton().Text = "Save";
		_effectDialog.Confirmed += OnEffectDialogSave;
		_effectDialog.Canceled += () => _effectDragging = false;
		_effectDialog.AddButton("Clear (use default)", right: false, action: "clear");
		_effectDialog.CustomAction += OnEffectDialogCustomAction;
	}

	/// <summary>Drag-to-paint input handler attached to the grid (not individual cells, so
	/// mouse-move across cell boundaries works).</summary>
	private void OnEffectGridInput(InputEvent ev)
	{
		if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
		{
			if (mb.Pressed)
			{
				int idx = CellIndexAt(mb.Position);
				if (idx >= 0 && idx < _effectMaskBits.Length)
				{
					_effectDragging = true;
					_effectPaintValue = !_effectMaskBits[idx];
					_effectMaskBits[idx] = _effectPaintValue;
					UpdateEffectCell(idx);
				}
			}
			else
			{
				_effectDragging = false;
			}
		}
		else if (ev is InputEventMouseMotion mm && _effectDragging)
		{
			int idx = CellIndexAt(mm.Position);
			if (idx >= 0 && idx < _effectMaskBits.Length && _effectMaskBits[idx] != _effectPaintValue)
			{
				_effectMaskBits[idx] = _effectPaintValue;
				UpdateEffectCell(idx);
			}
		}
	}

	/// <summary>Map host-local coordinates to a flat sub-cell index by simple division
	/// (zero gaps between all cells). Returns -1 when outside the grid.</summary>
	private int CellIndexAt(Vector2 pos)
	{
		if (_effectSubCellSize <= 0 || _effectGridCols <= 0 || _effectCells == null) return -1;
		int col = (int)(pos.X / _effectSubCellSize);
		int row = (int)(pos.Y / _effectSubCellSize);
		int gridRows = _effectCells.Length / _effectGridCols;
		if (col < 0 || col >= _effectGridCols || row < 0 || row >= gridRows) return -1;
		return row * _effectGridCols + col;
	}

	private void OnConfigureEffect(string defId)
	{
		if (_effectDialog == null || !TileRegistry.TryGet(defId, out var def)) return;

		_effectDialogDefId = defId;
		_effectPaintColor = ColorFromHex(def.EditorColor, Colors.White);
		_effectDialogTitle.Text = $"{def.DisplayName}  ({def.Category})  —  {def.FootprintW}×{def.FootprintH} cells";

		// Rebuild the sub-cell grid for this tile's full footprint.
		RebuildEffectGrid(def);

		// Load the tile's sprite as a dimmed background reference behind the grid.
		var spriteTex = (!string.IsNullOrEmpty(def.SpriteSlot) && ResourceLoader.Exists(def.SpriteSlot))
			? ResourceLoader.Load<Texture2D>(def.SpriteSlot) : null;
		_effectSpriteBg.Texture = spriteTex;
		_effectSpriteBg.Visible = spriteTex != null;
		// Dim the sprite so the overlay cells read clearly.
		_effectSpriteBg.Modulate = new Color(1f, 1f, 1f, 0.35f);

		_effectDialog.PopupCentered();
	}

	/// <summary>Clear and rebuild a single flat sub-cell grid (zero gaps) for the tile's
	/// full footprint. Sub-cell size is clamped to keep the dialog within reasonable bounds.
	/// Footprint-cell boundaries are drawn by <see cref="OnEffectCellLinesDraw"/>.</summary>
	private void RebuildEffectGrid(TileDef def)
	{
		// Remove and free old grid children.
		while (_effectGrid.GetChildCount() > 0)
		{
			var child = _effectGrid.GetChild(0);
			_effectGrid.RemoveChild(child);
			child.QueueFree();
		}

		_effectFootprintW = def.FootprintW;
		_effectFootprintH = def.FootprintH;
		_effectGridCols = def.FootprintW * ShapeDef.SubcellsPerAxis;
		int rows = def.FootprintH * ShapeDef.SubcellsPerAxis;

		// Scale sub-cells so the grid fits within ~320×240 px.
		const float maxGridW = 320f;
		const float maxGridH = 240f;
		const float maxSubCell = 42f;
		const float minSubCell = 12f;
		_effectSubCellSize = Mathf.Clamp(
			Mathf.Min(maxGridW / _effectGridCols, maxGridH / rows),
			minSubCell, maxSubCell);

		_effectGrid.Columns = _effectGridCols;

		float totalW = _effectGridCols * _effectSubCellSize;
		float totalH = rows * _effectSubCellSize;
		var gridSize = new Vector2(totalW, totalH);
		_effectGridHost.CustomMinimumSize = gridSize;
		_effectGridHost.Size = gridSize;

		int cellCount = _effectGridCols * rows;
		_effectCells = new Panel[cellCount];
		_effectMaskBits = new bool[cellCount];

		// Load the saved masks for this def (or the code-default approximation).
		int[] savedMasks = TileEffectStore.OpeningMasksFor(def);

		for (int i = 0; i < cellCount; i++)
		{
			// Map flat sub-cell index → (footprint cell index, sub-cell bit within that cell).
			int subCol = i % _effectGridCols;
			int subRow = i / _effectGridCols;
			int cellX = subCol / ShapeDef.SubcellsPerAxis;
			int cellY = subRow / ShapeDef.SubcellsPerAxis;
			int subX = subCol % ShapeDef.SubcellsPerAxis;
			int subY = subRow % ShapeDef.SubcellsPerAxis;
			int cellIdx = cellY * def.FootprintW + cellX;
			int bit = subY * ShapeDef.SubcellsPerAxis + subX;

			int mask = (cellIdx < savedMasks.Length) ? savedMasks[cellIdx] : 0;
			_effectMaskBits[i] = (mask & (1 << bit)) != 0;

			var p = new Panel
			{
				CustomMinimumSize = new Vector2(_effectSubCellSize, _effectSubCellSize),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			_effectGrid.AddChild(p);
			_effectCells[i] = p;
			UpdateEffectCell(i);
		}

		// Redraw the footprint-cell boundary lines (layer 3).
		_effectCellLines.QueueRedraw();
	}

	private void UpdateEffectCell(int i)
	{
		// Sub-cell border: subtle dark line so individual sub-cells are visible
		// but footprint-cell boundary lines (drawn by OnEffectCellLinesDraw) read clearly.
		var style = new StyleBoxFlat
		{
			// Keep both states translucent: the SpriteSlot texture is the alignment reference
			// and must remain visible through the painted overlay.
			BgColor = _effectMaskBits[i]
				? new Color(_effectPaintColor.R, _effectPaintColor.G, _effectPaintColor.B, 0.62f)
				: new Color(0.04f, 0.04f, 0.06f, 0.12f),
			BorderColor = new Color(0f, 0f, 0f, 0.35f),
		};
		style.SetBorderWidthAll(1);
		_effectCells[i].AddThemeStyleboxOverride("panel", style);
	}

	/// <summary>Draw thick highlight lines at footprint-cell boundaries (every 4th sub-cell)
	/// so the user can see which sub-cells belong to which tile cell.</summary>
	private void OnEffectCellLinesDraw()
	{
		if (_effectSubCellSize <= 0 || _effectCellLines == null) return;

		float w = _effectFootprintW * ShapeDef.SubcellsPerAxis * _effectSubCellSize;
		float h = _effectFootprintH * ShapeDef.SubcellsPerAxis * _effectSubCellSize;
		Color lineColor = new(1f, 1f, 1f, 0.55f);
		const float thickness = 2f;

		// Include the outer frame plus every internal footprint-cell boundary.
		for (int cx = 0; cx <= _effectFootprintW; cx++)
		{
			float x = cx * ShapeDef.SubcellsPerAxis * _effectSubCellSize;
			_effectCellLines.DrawLine(new Vector2(x, 0), new Vector2(x, h), lineColor, thickness);
		}

		for (int cy = 0; cy <= _effectFootprintH; cy++)
		{
			float y = cy * ShapeDef.SubcellsPerAxis * _effectSubCellSize;
			_effectCellLines.DrawLine(new Vector2(0, y), new Vector2(w, y), lineColor, thickness);
		}
	}

	private void OnEffectDialogSave()
	{
		_effectDragging = false;
		if (_effectDialogDefId == null || !TileRegistry.TryGet(_effectDialogDefId, out var def)) return;

		// Pack the flat sub-cell bit array into per-footprint-cell 16-bit masks.
		int cellCount = def.FootprintW * def.FootprintH;
		var masks = new int[cellCount];
		for (int i = 0; i < _effectMaskBits.Length; i++)
		{
			if (!_effectMaskBits[i]) continue;
			int subCol = i % _effectGridCols;
			int subRow = i / _effectGridCols;
			int cellX = subCol / ShapeDef.SubcellsPerAxis;
			int cellY = subRow / ShapeDef.SubcellsPerAxis;
			int subX = subCol % ShapeDef.SubcellsPerAxis;
			int subY = subRow % ShapeDef.SubcellsPerAxis;
			int cellIdx = cellY * def.FootprintW + cellX;
			int bit = subY * ShapeDef.SubcellsPerAxis + subX;
			masks[cellIdx] |= 1 << bit;
		}

		TileEffectStore.SetMasks(_effectDialogDefId, masks);
		PersistEffectStore();
		_state.RaiseStateChanged(); // GridView redraws the effect-area overlay
	}

	private void OnEffectDialogCustomAction(StringName action)
	{
		_effectDragging = false;
		if (action.ToString() == "clear" && _effectDialogDefId != null)
		{
			TileEffectStore.ClearOverride(_effectDialogDefId);
			PersistEffectStore();
			_state.RaiseStateChanged();
		}
		_effectDialog.Hide();
	}

	private void PersistEffectStore()
	{
		try { TileEffectStore.Save(_effectStorePath); }
		catch (System.Exception e) { GD.PushError($"[MapEditor] could not save tile effects: {e.Message}"); }
	}

	// ---------------------------------------------------------------- playtest

	private void EnterPreview()
	{
		if (_previewActive) return;
		_previewActive = true;

		_topBarPanel.Visible = false;
		_leftRailPanel.Visible = false;
		_rightPanel.Visible = false;
		_paletteDockPanel.Visible = false;
		_statusBarPanel.Visible = false;
		_previewHint.Visible = false;
		_canvasBg.Visible = false;
		_gridView.Visible = false;

		_playtest = new MapPlaytestController();
		_playtest.Initialize(_state.Document);
		_playtest.ExitRequested += ExitPreview;
		AddChild(_playtest);
	}

	private void ExitPreview()
	{
		if (!_previewActive) return;
		_previewActive = false;

		if (_playtest != null)
		{
			_playtest.ExitRequested -= ExitPreview;
			_playtest.QueueFree();
			_playtest = null;
		}

		_topBarPanel.Visible = true;
		_leftRailPanel.Visible = true;
		_rightPanel.Visible = true;
		_paletteDockPanel.Visible = true;
		_statusBarPanel.Visible = true;
		_previewHint.Visible = false;
		_canvasBg.Visible = true;
		_gridView.Visible = true;
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

	/// <summary>Item 4 — commits rule-tile generation as REAL, undoable tiles (Preview gen
	/// only shows a throwaway overlay). Reuses the visible preview's seed when one is showing
	/// so "what you saw is what you commit", else rolls a fresh one. Resolved spawns are
	/// grouped by layer and pushed as one <see cref="TileBatchCommand"/> per layer; any spawn
	/// overlapping tiles already on that layer is skipped defensively.</summary>
	private void OnGeneratePressed()
	{
		string seed = _state.Preview != null && !string.IsNullOrEmpty(_previewScratchSeed)
			? _previewScratchSeed
			: DetRandom.NewSeed();

		var rng = new DetRandom(seed);
		var results = RuleResolver.Resolve(_state.Document, rng, out var warnings);
		if (OS.IsDebugBuild())
			foreach (var w in warnings) GD.PushWarning("[Generate] " + w);

		var byLayer = new System.Collections.Generic.Dictionary<int, List<PlacedTile>>();
		foreach (var spawn in results)
		{
			if (spawn.LayerIndex < 0 || spawn.LayerIndex >= _state.Document.Layers.Count) continue;
			if (!TileRegistry.TryGet(spawn.DefId, out var def)) continue;

			var occ = _state.OccupancyOf(spawn.LayerIndex);
			bool blocked = false;
			for (int dy = 0; dy < def.FootprintH && !blocked; dy++)
				for (int dx = 0; dx < def.FootprintW && !blocked; dx++)
					if (occ.TileAt(spawn.X + dx, spawn.Y + dy) != null) blocked = true;
			if (blocked) continue;

			if (!byLayer.TryGetValue(spawn.LayerIndex, out var list))
				byLayer[spawn.LayerIndex] = list = new List<PlacedTile>();
			list.Add(new PlacedTile { DefId = spawn.DefId, X = spawn.X, Y = spawn.Y });
		}

		int total = 0;
		foreach (var kv in byLayer)
		{
			if (kv.Value.Count == 0) continue;
			_state.Commands.Push(new TileBatchCommand(_state, _state.Document.Layers[kv.Key], "Generate", kv.Value, null));
			total += kv.Value.Count;
		}

		// The scratch preview is now committed (or nothing generated) — clear it either way.
		_state.Preview = null;
		_previewScratchSeed = null;
		if (total == 0) GD.PushWarning("[Generate] no rule zones produced any tiles (paint a rule tile first)");
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

		foreach (var (defId, dx, dy, flipX) in _state.Clipboard)
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

			toAdd.Add(new PlacedTile { DefId = def.Id, X = x, Y = y, FlipX = flipX });
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

		foreach (var (defId, dx, dy, _) in _state.Clipboard)
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
			_state.Clipboard.Add((t.DefId, t.X - anchor.X, t.Y - anchor.Y, t.FlipX));
	}

	private void FlipSelectionHorizontal()
	{
		if (_state.Selection.Count == 0) return;
		_state.Commands.Push(new FlipTilesCommand(_state.Selection));
		_state.RaiseStateChanged();
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
		// While the isolated playtest owns the screen, suppress every editor shortcut so
		// nothing paints/undoes behind Pomegraknight's runtime session.
		if (_previewActive) return;

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

		if (@event.IsActionPressed("mapedit_flip_horizontal"))
		{
			FlipSelectionHorizontal();
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

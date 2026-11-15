using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;

namespace Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.8 — the map browser. Root script of <c>Scenes/MapCreation/MapBrowser.tscn</c>
/// (a thin root Control); every widget below is built in code in <see cref="_Ready"/>.
///
/// Store (GDD §8): one file per map at <c>user://maps/&lt;guid&gt;.json</c>, no
/// <c>_index.json</c> — the browser lists by directory scan, reading each file's own meta
/// block. Built-in maps are offered as safe "Edit Copy" cards: the copy preserves its GUID and
/// therefore overrides that built-in map at runtime, without ever writing under <c>res://</c>.
/// File identity is the GUID (<see cref="MapDocument.Id"/>); renames only ever touch
/// <see cref="MapDocument.Name"/>, never the filename, so two maps can share a display name
/// (GDD §7.8 / KNOWLEDGE "name-derived filenames collapse duplicates").
///
/// Layout (v0.10.7): a wall of cards, always <see cref="Columns"/> to a row and never wider
/// than the window. Each card is a picture of the map itself (<see cref="MapThumbnailRenderer"/>)
/// under a bold name, small details and realm / level / status badges (the card half of this
/// class is <c>MapBrowserCards.cs</c>). Above the wall: a search box, a strip of realm chips
/// — the realm selector — and filters for level, goal, terrain, source and sort. Maps that
/// cannot be fought on yet (<see cref="MapReadiness"/>) are tucked behind "Show unfinished";
/// the same test keeps them out of a run (<c>CombatMapCatalog</c>).
/// </summary>
public partial class MapBrowser : Control
{
    private const string BuiltInDirectory = "res://Maps";
    private const string ArchiveFolder = "_archive";

    /// <summary>Cards per row. Cards share the row's width, so this holds at any window size.</summary>
    private const int Columns = 4;
    private const int GridGap = 14;
    /// <summary>A card's height until <see cref="FitCards"/> has seen the wall's width.</summary>
    private const float CardHeight = 232f;
    private const float CardBorder = 2f;
    /// <summary>Width kept aside for the wall's scrollbar when sizing cards (see <see cref="FitCards"/>).</summary>
    private const float ScrollbarAllowance = 12f;

    private const string Any = "any";
    private const string Untagged = "untagged";

    // Filter state is static: the browser scene is rebuilt every time the editor returns to
    // it, and coming back to a reset filter bar after each edit would be maddening.
    private static string _search = "";
    private static string _realmFilter = Any;       // Any, Untagged, or a TileSets id
    private static int _levelFilter;                // 0 = any level
    private static string _goalFilter = Any;
    private static string _terrainFilter = Any;
    private static int _sourceFilter;               // 0 all, 1 built-in, 2 mine
    private static int _sortMode;                   // 0 recently edited, 1 name, 2 realm
    private static bool _showUnfinished;

    /// <summary>The map last handed to the editor from here. It stays on the wall even while
    /// unfinished — a map you just created must not vanish when you come back.</summary>
    private static string _lastOpenedId;

    private static readonly Color Ink = new(0.06f, 0.06f, 0.09f);
    private static readonly Color DetailColor = new(0.86f, 0.87f, 0.92f);
    private static readonly Color FaintColor = new(0.70f, 0.72f, 0.80f);
    private static readonly Color WarnColor = new(1.00f, 0.74f, 0.30f);
    private static readonly Color NeutralAccent = new(0.55f, 0.58f, 0.68f);

    /// <summary>One listed map and everything derived from it once at load time.</summary>
    private sealed class Entry
    {
        public MapDocument Doc;
        public string Path;
        public bool BuiltIn;
        public bool OverridesBuiltIn;    // a user copy that carries a built-in map's id
        public List<string> Problems;
        public string RealmSet;          // TileSets id, or null when the map is untagged
        public string RealmName;
        public Color Accent;
        public string Haystack;          // lower-case text the search box matches against
        public Control Card;
        public Control Footer;
        public TextureRect Art;
        public bool ArtRequested;
    }

    private string _mapsDir;
    private string _builtInMapsDir;

    private readonly List<Entry> _entries = new();
    private readonly List<string> _strayFiles = new();   // *.json in the user folder that are not maps

    /// <summary>One chip of the realm strip. <see cref="Id"/> is <see cref="Any"/>,
    /// <see cref="Untagged"/> or a <see cref="TileSets"/> id.</summary>
    private sealed class RealmChip
    {
        public string Id;
        public string Label;
        public Color Color;
        public Button Button;
    }

    private readonly List<RealmChip> _realmChips = new();

    private ScrollContainer _wallScroll;
    private GridContainer _grid;
    private Label _countLabel;
    private Control _emptyState;
    private CheckBox _unfinishedToggle;
    private Button _cleanUpButton;
    private LineEdit _searchBox;
    private MapThumbnailRenderer _thumbnails;
    private Font _boldFont;
    private Texture2D _scrim;

    private ConfirmationDialog _createDialog;
    private LineEdit _createName;
    private OptionButton _createRealm;
    private readonly List<string> _createRealmIds = new();   // OptionButton index -> TileSets id; null = no realm

    private ConfirmationDialog _confirmDialog;
    private Action _confirmAction;

    private ConfirmationDialog _promptDialog;
    private LineEdit _promptLineEdit;
    private Action<string> _promptCallback;

    public override void _Ready()
    {
        _mapsDir = ProjectSettings.GlobalizePath("user://maps");
        _builtInMapsDir = ProjectSettings.GlobalizePath(BuiltInDirectory);
        try
        {
            Directory.CreateDirectory(_mapsDir);
        }
        catch (Exception e)
        {
            GD.PushWarning("[MapBrowser] could not create maps directory: " + e.Message);
        }

        // Item 6: warm the global per-tile-kind effect-mask store so any editor opened from
        // here already reflects saved overrides.
        TileEffectStore.Load(ProjectSettings.GlobalizePath("user://tile_effects.json"), out var effectWarnings);
        foreach (var w in effectWarnings) GD.PushWarning("[MapBrowser] " + w);

        _thumbnails = new MapThumbnailRenderer { Name = "Thumbnails" };
        AddChild(_thumbnails);

        BuildUi();

        if (OS.IsDebugBuild()) RunBootValidation();

        RefreshMaps();
    }

    /// <summary>
    /// GDD §8/§11.2 boot guard, wired to a real boot path (T10 §5: "print all violations,
    /// not just the first"). Runs the domain layer's own self-checks and surfaces every
    /// failure as a <c>GD.PushError</c> — never throws, never blocks the browser opening.
    /// </summary>
    private void RunBootValidation()
    {
        foreach (var failure in MapJson.RoundTripSelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in TileEffectStore.RoundTripSelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in EffectAreaTransform.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var problem in TileRegistry.Validate())
            GD.PushError("[MapCreation] " + problem);

        foreach (var failure in RuleResolver.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in HillAutotile.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in TileVisualResolver.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in AutotileProbe.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in MapReadiness.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in MapThumbnailFraming.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in CombatMapCatalog.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        foreach (var failure in TileManifestLoader.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        string projectRoot = ProjectSettings.GlobalizePath("res://");
        foreach (var failure in TileManifestLoader.SelfTestFixtures(projectRoot))
            GD.PushError("[MapCreation] " + failure);

        // Registry <-> sidecar <-> sprite-canvas agreement for every realm tile set. Only
        // meaningful where the art sits on disk as loose files, i.e. a debug/editor run.
        foreach (var problem in TileSetAudit.Run(projectRoot))
            GD.PushError("[MapCreation] " + problem);
    }

    // ---------------------------------------------------------------- UI build

    private void BuildUi()
    {
        _boldFont = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.85f };
        _scrim = BuildScrim();

        // Decorative full-rect background; MUST ignore mouse or it eats clicks meant for
        // the buttons/cards on top of it (KNOWLEDGE v0.2.4).
        var bg = new ColorRect { Color = Ink, MouseFilter = Control.MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        // -- title row: name on the left; search, Create and Back on the right
        var topBar = new HBoxContainer();
        topBar.AddThemeConstantOverride("separation", 10);
        root.AddChild(topBar);

        var title = new Label { Text = "Map Creation", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontOverride("font", _boldFont);
        title.AddThemeFontSizeOverride("font_size", 28);
        topBar.AddChild(title);

        _searchBox = new LineEdit
        {
            PlaceholderText = "Search maps",
            Text = _search,
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(240, 38),
            TooltipText = "Matches a map's name, realm, world, goals or id",
        };
        _searchBox.TextChanged += text =>
        {
            _search = text ?? "";
            ApplyFilters();
        };
        topBar.AddChild(_searchBox);

        var createBtn = new Button { Text = "Create", CustomMinimumSize = new Vector2(96, 38) };
        createBtn.Pressed += OnCreatePressed;
        topBar.AddChild(createBtn);

        var backBtn = new Button { Text = "Back", CustomMinimumSize = new Vector2(96, 38) };
        backBtn.Pressed += OnBackPressed;
        topBar.AddChild(backBtn);

        // -- realm strip: the realm selector. One chip per realm, in that realm's colour,
        //    with how many maps it holds; it wraps rather than widening the page.
        //    The running count sits at the right end of the same row.
        var realmRow = new HBoxContainer();
        realmRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(realmRow);

        var realms = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        realms.AddThemeConstantOverride("h_separation", 8);
        realms.AddThemeConstantOverride("v_separation", 6);
        realmRow.AddChild(realms);

        _countLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _countLabel.AddThemeFontSizeOverride("font_size", 13);
        _countLabel.AddThemeColorOverride("font_color", FaintColor);
        realmRow.AddChild(_countLabel);

        AddRealmChip(realms, Any, "All realms", NeutralAccent);
        foreach (TileSets.Info info in TileSets.All)
        {
            if (info.Id == TileSets.Core) continue;
            AddRealmChip(realms, info.Id, info.RealmName, WorldColor(info.WorldAbbr));
        }
        AddRealmChip(realms, Untagged, "No realm", NeutralAccent);

        // -- filter row: wraps instead of pushing the page wider than the window
        var filters = new HFlowContainer();
        filters.AddThemeConstantOverride("h_separation", 14);
        filters.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(filters);

        var levelLabels = new List<string> { "Any level" };
        for (int level = 1; level <= 6; level++) levelLabels.Add("LV " + level);
        AddFilter(filters, "Level", levelLabels, _levelFilter, i => _levelFilter = i);

        var goalIds = new List<string> { Any, CombatMapGoals.Claim, CombatMapGoals.Protect, CombatMapGoals.Destroy, CombatMapGoals.Slaughter };
        AddFilter(filters, "Goal", new List<string> { "Any goal", "Claim", "Protect", "Destroy", "Slaughter" },
            Math.Max(0, goalIds.IndexOf(_goalFilter)), i => _goalFilter = goalIds[i]);

        var terrainIds = new List<string> { Any, CombatMapTerrain.SeaLevel, CombatMapTerrain.High, CombatMapTerrain.Lowground };
        AddFilter(filters, "Terrain", new List<string> { "Any terrain", "Sea level", "High ground", "Low ground" },
            Math.Max(0, terrainIds.IndexOf(_terrainFilter)), i => _terrainFilter = terrainIds[i]);

        AddFilter(filters, "Source", new List<string> { "All maps", "Built-in", "Mine" }, _sourceFilter, i => _sourceFilter = i);
        AddFilter(filters, "Sort", new List<string> { "Recently edited", "Name", "Realm" }, _sortMode, i => _sortMode = i);

        _unfinishedToggle = new CheckBox
        {
            Text = "Show unfinished",
            ButtonPressed = _showUnfinished,
            TooltipText = "Maps that cannot be fought on yet: empty, or missing ground, a character spawn, an enemy spawn or a goal marker",
        };
        _unfinishedToggle.AddThemeFontSizeOverride("font_size", 13);
        _unfinishedToggle.Toggled += on =>
        {
            _showUnfinished = on;
            ApplyFilters();
        };
        filters.AddChild(_unfinishedToggle);

        _cleanUpButton = new Button
        {
            Text = "Clean up…",
            TooltipText = "Move unfinished maps and stray files out of the list, into an archive folder (nothing is deleted)",
        };
        _cleanUpButton.AddThemeFontSizeOverride("font_size", 13);
        _cleanUpButton.Pressed += OnCleanUpPressed;
        filters.AddChild(_cleanUpButton);

        // -- the wall. Vertical scroll only: the grid is always exactly as wide as the window.
        _wallScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _wallScroll.Resized += FitCards;
        root.AddChild(_wallScroll);

        var wall = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _wallScroll.AddChild(wall);

        _grid = new GridContainer { Columns = Columns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", GridGap);
        _grid.AddThemeConstantOverride("v_separation", GridGap);
        wall.AddChild(_grid);

        _emptyState = BuildEmptyState();
        wall.AddChild(_emptyState);

        BuildDialogs();
    }

    /// <summary>A captioned drop-down in the filter row. Choosing an item stores it through
    /// <paramref name="store"/> and re-filters the wall.</summary>
    private void AddFilter(Container row, string caption, List<string> items, int selected, Action<int> store)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 5);

        var label = new Label { Text = caption, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", FaintColor);
        box.AddChild(label);

        var option = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        option.AddThemeFontSizeOverride("font_size", 13);
        foreach (string item in items) option.AddItem(item);
        option.Select(Mathf.Clamp(selected, 0, items.Count - 1));
        option.ItemSelected += index =>
        {
            store((int)index);
            ApplyFilters();
        };
        box.AddChild(option);
        row.AddChild(box);
    }

    /// <summary>One chip of the realm strip. Its look and count are set by
    /// <see cref="RefreshRealmChips"/> every time the wall is filtered.</summary>
    private void AddRealmChip(Container row, string id, string label, Color color)
    {
        var button = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
        button.AddThemeFontOverride("font", _boldFont);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += () =>
        {
            _realmFilter = id;
            ApplyFilters();
        };
        row.AddChild(button);
        _realmChips.Add(new RealmChip { Id = id, Label = label, Color = color, Button = button });
    }

    /// <summary>A realm's colour on the overworld (`WorldDef`), so a chip, a card's border
    /// and its badge all say "this world" the way the map screen does.</summary>
    private static Color WorldColor(string worldAbbr)
    {
        foreach (WorldDef world in WorldDef.Pool)
            if (string.Equals(world.Abbr, worldAbbr, StringComparison.OrdinalIgnoreCase)) return world.Color;
        return NeutralAccent;
    }

    private Control BuildEmptyState()
    {
        var box = new VBoxContainer { Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 60), MouseFilter = Control.MouseFilterEnum.Ignore });

        var label = new Label
        {
            Text = "No maps match these filters.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", FaintColor);
        box.AddChild(label);

        var clear = new Button { Text = "Clear filters", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        clear.Pressed += () =>
        {
            _search = "";
            _realmFilter = Any;
            _levelFilter = 0;
            _goalFilter = Any;
            _terrainFilter = Any;
            _sourceFilter = 0;
            GetTree().ReloadCurrentScene();     // rebuilds the filter row from the cleared state
        };
        box.AddChild(clear);
        return box;
    }

    /// <summary>A soft dark veil laid over the top of a card's picture, where its name and
    /// details sit, so they read on any map — a pale ink painting as well as a dungeon. It
    /// fades out before the lower half, which stays the map's own colours.</summary>
    private static Texture2D BuildScrim()
    {
        var gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.50f, 0.84f, 1f },
            Colors = new[]
            {
                new Color(0f, 0f, 0f, 0.88f), new Color(0f, 0f, 0f, 0.70f),
                new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f),
            },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            FillFrom = new Vector2(0f, 0f),
            FillTo = new Vector2(0f, 1f),
            Width = 8,
            Height = 128,
        };
    }

    private void BuildDialogs()
    {
        _confirmDialog = new ConfirmationDialog { Title = "Confirm" };
        AddChild(_confirmDialog);
        _confirmDialog.Confirmed += () => _confirmAction?.Invoke();

        _promptDialog = new ConfirmationDialog { Title = "Name" };
        AddChild(_promptDialog);
        _promptLineEdit = new LineEdit { CustomMinimumSize = new Vector2(300, 32) };
        _promptDialog.AddChild(_promptLineEdit);
        _promptDialog.Confirmed += OnPromptConfirmed;
        _promptLineEdit.TextSubmitted += _ =>
        {
            OnPromptConfirmed();
            _promptDialog.Hide();
        };

        BuildCreateDialog();
    }

    /// <summary>Create asks for a name and a realm. The realm is the map's first label: it
    /// tags the map for that world, paints the canvas the colour the realm's art is drawn
    /// against, and is what the editor's palette opens on.</summary>
    private void BuildCreateDialog()
    {
        _createDialog = new ConfirmationDialog { Title = "Create Map", OkButtonText = "Create" };
        AddChild(_createDialog);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        _createDialog.AddChild(body);

        body.AddChild(new Label { Text = "Name" });
        _createName = new LineEdit { CustomMinimumSize = new Vector2(340, 32), PlaceholderText = "Untitled" };
        body.AddChild(_createName);

        body.AddChild(new Label { Text = "Realm" });
        _createRealm = new OptionButton();
        _createRealm.AddItem("No realm  (any world)");
        _createRealmIds.Add(null);
        foreach (TileSets.Info info in TileSets.All)
        {
            if (info.Id == TileSets.Core) continue;
            _createRealm.AddItem(info.DisplayName);
            _createRealmIds.Add(info.Id);
        }
        body.AddChild(_createRealm);

        var hint = new Label
        {
            Text = "Sets the map's world, its canvas colour and the tile kit the palette opens on. " +
                   "Level, goals and terrain are set in the editor, under Map info.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(340, 0),
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", FaintColor);
        body.AddChild(hint);

        _createDialog.Confirmed += OnCreateConfirmed;
        _createName.TextSubmitted += _ =>
        {
            _createDialog.Hide();
            OnCreateConfirmed();
        };
    }

    private void ShowPrompt(string title, string initialText, Action<string> onConfirm)
    {
        _promptDialog.Title = title;
        _promptLineEdit.Text = initialText ?? "";
        _promptCallback = onConfirm;
        _promptDialog.PopupCentered();
        _promptLineEdit.GrabFocus();
    }

    private void OnPromptConfirmed()
    {
        string name = _promptLineEdit.Text?.Trim();
        if (string.IsNullOrEmpty(name)) name = "Untitled";
        _promptCallback?.Invoke(name);
    }

    private void ShowConfirm(string text, Action onConfirm, string okText = "OK")
    {
        _confirmDialog.DialogText = text;
        _confirmDialog.OkButtonText = okText;
        _confirmAction = onConfirm;
        _confirmDialog.PopupCentered();
    }

    // ---------------------------------------------------------------- listing

    /// <summary>
    /// Rescans the built-in and user map directories and rebuilds every card. The wall shows
    /// what a run would be dealt (<c>CombatMapCatalog</c>): a user copy that carries a built-in
    /// map's id stands in for it — but only once the copy is playable. While the copy is
    /// unfinished the built-in card stays, and the copy is listed beside it as a draft.
    /// Filtering afterwards only shows, hides and reorders these cards.
    /// </summary>
    private void RefreshMaps()
    {
        _thumbnails.CancelPending();
        foreach (Node child in _grid.GetChildren())
            child.QueueFree();
        _entries.Clear();
        _strayFiles.Clear();

        List<Entry> builtIns = CollectMaps(_builtInMapsDir, true);
        List<Entry> mine = CollectMaps(_mapsDir, false);

        var builtInIds = new HashSet<string>();
        foreach (Entry entry in builtIns) builtInIds.Add(entry.Doc.Id);

        var replaced = new HashSet<string>();   // built-in ids a playable user copy stands in for
        foreach (Entry entry in mine)
        {
            entry.OverridesBuiltIn = builtInIds.Contains(entry.Doc.Id);
            Describe(entry);
            if (entry.OverridesBuiltIn && entry.Problems.Count == 0) replaced.Add(entry.Doc.Id);
        }

        foreach (Entry entry in builtIns)
        {
            if (replaced.Contains(entry.Doc.Id)) continue;
            Describe(entry);
            _entries.Add(entry);
        }
        _entries.AddRange(mine);

        foreach (Entry entry in _entries)
        {
            entry.Card = BuildCard(entry);
            _grid.AddChild(entry.Card);
        }

        FitCards();
        ApplyFilters();
    }

    /// <summary>
    /// Gives every card the height that keeps its picture the shape it was rendered in, so a
    /// wider window makes cards larger instead of cropping the floor off their pictures.
    /// Sized from the scroll area's width with room always kept for the scrollbar — never
    /// from the grid's own width — so the scrollbar appearing cannot change a card's height
    /// and set the two chasing each other.
    /// </summary>
    private void FitCards()
    {
        if (_wallScroll == null) return;
        float column = (_wallScroll.Size.X - ScrollbarAllowance - (Columns - 1) * GridGap) / Columns;
        if (column < 40f) return;       // the wall has not been laid out yet

        float aspect = (float)MapThumbnailRenderer.ThumbnailSize.X / MapThumbnailRenderer.ThumbnailSize.Y;
        float picture = (column - CardBorder * 2f) / aspect;
        foreach (Entry entry in _entries)
        {
            if (!IsInstanceValid(entry.Card)) continue;
            float footer = IsInstanceValid(entry.Footer) ? entry.Footer.GetCombinedMinimumSize().Y : 42f;
            entry.Card.CustomMinimumSize = new Vector2(0, Mathf.Round(CardBorder * 2f + picture + footer));
        }
    }

    /// <summary>Loads every map file in a directory. Two files claiming one id: the first by
    /// file name wins, as it does for a run.</summary>
    private List<Entry> CollectMaps(string directory, bool builtIn)
    {
        var found = new List<Entry>();
        var ids = new HashSet<string>();
        foreach (var path in SafeListMapFiles(directory))
        {
            var doc = MapJson.Load(path, out var warnings);
            if (doc == null)
            {
                // Not a map (a stray or pre-GUID file). Remember it so Clean up can offer to
                // move it away; say so once instead of a stack trace per file per visit.
                if (!builtIn) _strayFiles.Add(path);
                continue;
            }
            foreach (var w in warnings)
                GD.PushWarning("[MapBrowser] " + w);

            if (!ids.Add(doc.Id)) continue;
            found.Add(new Entry { Doc = doc, Path = path, BuiltIn = builtIn });
        }
        return found;
    }

    private static string[] SafeListMapFiles(string directory)
    {
        try
        {
            // "*.json" naturally excludes the ".tmp" files MapJson.Save uses mid-write; the
            // top-directory-only default keeps the archive sub-folder out of the list.
            string[] paths = Directory.GetFiles(directory, "*.json");
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }
        catch (Exception e)
        {
            GD.PushWarning("[MapBrowser] could not list map directory '" + directory + "': " + e.Message);
            return Array.Empty<string>();
        }
    }

    /// <summary>Fills in everything about an entry that filtering and its card need.</summary>
    private static void Describe(Entry entry)
    {
        MapDocument doc = entry.Doc;
        entry.Problems = MapReadiness.Problems(doc);
        entry.Accent = NeutralAccent;

        var worldWords = new List<string>();
        if (doc.Worlds != null)
        {
            foreach (string tag in doc.Worlds)
            {
                string abbr = tag?.Trim();
                if (string.IsNullOrEmpty(abbr)) continue;
                worldWords.Add(abbr);
                Color color = NeutralAccent;
                foreach (WorldDef world in WorldDef.Pool)
                {
                    if (!string.Equals(world.Abbr, abbr, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(world.Name, abbr, StringComparison.OrdinalIgnoreCase)) continue;
                    abbr = world.Abbr;
                    color = world.Color;
                    worldWords.Add(world.Name);
                    break;
                }

                if (entry.RealmSet != null) continue;       // the first recognised world names the realm
                string set = TileSets.ForWorldAbbr(abbr);
                if (set == null || !TileSets.TryGet(set, out TileSets.Info info)) continue;
                entry.RealmSet = set;
                entry.RealmName = info.RealmName;
                entry.Accent = color;
                worldWords.Add(info.RealmName);
            }
        }

        // The search box matches everything a card says — and a level as "lv3", which a map
        // serving every level answers to as well (the same rule as the Level filter).
        var levelWords = new List<string>();
        for (int level = 1; level <= 6; level++)
            if (doc.HardshipLevels is not { Count: > 0 } || doc.HardshipLevels.Contains(level)) levelWords.Add("lv" + level);

        entry.Haystack = string.Join(" ", new[]
        {
            doc.Name, doc.Id, string.Join(" ", worldWords), string.Join(" ", doc.Goals ?? new List<string>()),
            string.Join(" ", levelWords), LevelLabel(doc), doc.Terrain, TerrainLabel(doc.Terrain),
            entry.BuiltIn ? "built-in" : "mine", entry.OverridesBuiltIn ? "edited copy" : "",
            entry.Problems.Count > 0 ? "unfinished " + string.Join(" ", entry.Problems) : "",
        }).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- filtering

    /// <param name="anyRealm">Ignore the realm chip — used to count what each chip would show.</param>
    private bool Matches(Entry entry, bool anyRealm = false)
    {
        MapDocument doc = entry.Doc;
        bool justEdited = !entry.BuiltIn && doc.Id == _lastOpenedId;
        if (entry.Problems.Count > 0 && !_showUnfinished && !justEdited) return false;

        if (!anyRealm && !InRealm(entry, _realmFilter)) return false;

        // Level, goal and terrain ask "could this map be chosen for such a node?", so a map
        // that serves every level / every terrain passes any choice (§9 selection rules).
        if (_levelFilter > 0 && doc.HardshipLevels is { Count: > 0 } && !doc.HardshipLevels.Contains(_levelFilter))
            return false;
        if (_goalFilter != Any && doc.Goals is { Count: > 0 } && !doc.Goals.Contains(_goalFilter)) return false;
        if (_terrainFilter != Any && doc.Terrain != "*" && doc.Terrain != _terrainFilter) return false;

        if (_sourceFilter == 1 && !entry.BuiltIn) return false;
        if (_sourceFilter == 2 && entry.BuiltIn) return false;

        foreach (string word in _search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!entry.Haystack.Contains(word)) return false;
        return true;
    }

    private static bool InRealm(Entry entry, string realm) => realm switch
    {
        Any => true,
        Untagged => entry.RealmSet == null,
        _ => entry.RealmSet == realm,
    };

    private int Compare(Entry a, Entry b)
    {
        int byName = string.Compare(a.Doc.Name, b.Doc.Name, StringComparison.OrdinalIgnoreCase);
        // ModifiedUtc is ISO-8601 ("o"), which sorts chronologically as a plain string.
        int byRecent = string.CompareOrdinal(b.Doc.ModifiedUtc, a.Doc.ModifiedUtc);
        switch (_sortMode)
        {
            case 1:
                return byName != 0 ? byName : byRecent;
            case 2:
                int byRealm = string.Compare(a.RealmName ?? "~", b.RealmName ?? "~", StringComparison.OrdinalIgnoreCase);
                return byRealm != 0 ? byRealm : (byName != 0 ? byName : byRecent);
            default:
                return byRecent != 0 ? byRecent : byName;
        }
    }

    /// <summary>Shows, hides and orders the cards for the current search and filters. Cards
    /// are never rebuilt here, so typing in the search box stays instant.</summary>
    private void ApplyFilters()
    {
        if (_grid == null) return;

        var order = new List<Entry>(_entries);
        order.Sort(Compare);

        int shown = 0, unfinished = 0, userUnfinished = 0;
        for (int i = 0; i < order.Count; i++)
        {
            Entry entry = order[i];
            if (entry.Problems.Count > 0)
            {
                unfinished++;
                if (!entry.BuiltIn) userUnfinished++;
            }
            bool visible = Matches(entry);
            _grid.MoveChild(entry.Card, i);
            entry.Card.Visible = visible;
            if (!visible) continue;
            shown++;
            RequestArt(entry);
        }

        RefreshRealmChips();

        _emptyState.Visible = shown == 0;
        _unfinishedToggle.Text = unfinished > 0 ? $"Show unfinished ({unfinished})" : "Show unfinished";
        _unfinishedToggle.Disabled = unfinished == 0;
        int junk = userUnfinished + _strayFiles.Count;
        _cleanUpButton.Visible = junk > 0;
        _cleanUpButton.Text = $"Clean up {junk}…";

        string summary = shown == _entries.Count ? $"{shown} maps" : $"{shown} of {_entries.Count} maps";
        if (!_showUnfinished && unfinished > 0) summary += $"  ·  {unfinished} unfinished hidden";
        _countLabel.Text = summary;
    }

    /// <summary>Recounts and restyles the realm strip: each chip says how many maps it would
    /// show under the other filters, and the chosen one is filled with its realm's colour.
    /// "No realm" only appears while there is something untagged to find (or it is chosen).</summary>
    private void RefreshRealmChips()
    {
        foreach (RealmChip chip in _realmChips)
        {
            int count = 0;
            foreach (Entry entry in _entries)
                if (Matches(entry, anyRealm: true) && InRealm(entry, chip.Id)) count++;

            bool chosen = chip.Id == _realmFilter;
            chip.Button.Text = chip.Label + "   " + count;
            chip.Button.Visible = chip.Id != Untagged || count > 0 || chosen;
            StyleRealmChip(chip, chosen, count == 0);
        }
    }

    /// <summary>Asks for a card's picture the first time the card is actually shown.</summary>
    private void RequestArt(Entry entry)
    {
        if (entry.ArtRequested) return;
        entry.ArtRequested = true;
        TextureRect art = entry.Art;
        string key = entry.Path + "|" + entry.Doc.ModifiedUtc + "|" + MapDocumentQueries.TileCount(entry.Doc);
        _thumbnails.Request(entry.Doc, key, texture =>
        {
            if (IsInstanceValid(art)) art.Texture = texture;    // the card may have been rebuilt meanwhile
        });
    }

    // ---------------------------------------------------------------- actions

    private void OnCreatePressed()
    {
        // Offer the realm the wall is showing: creating "a Wulin map" while looking at Wulin.
        int preset = _createRealmIds.IndexOf(_realmFilter);
        _createRealm.Select(preset > 0 ? preset : 0);
        _createName.Text = "";
        _createDialog.PopupCentered();
        _createName.GrabFocus();
    }

    private void OnCreateConfirmed()
    {
        string name = _createName.Text?.Trim();
        var doc = MapDocument.CreateNew(string.IsNullOrEmpty(name) ? "Untitled" : name);

        int choice = _createRealm.Selected;
        string set = choice >= 0 && choice < _createRealmIds.Count ? _createRealmIds[choice] : null;
        if (set != null && TileSets.TryGet(set, out TileSets.Info realm))
        {
            doc.Worlds = new List<string> { realm.WorldAbbr };
            doc.World = realm.WorldAbbr;               // v1 card compatibility, as the editor writes it
            doc.Canvas.Color = realm.CanvasColor;
        }

        string path = Path.Combine(_mapsDir, doc.Id + ".json");
        // F-MC4: MapJson.Save propagates IO exceptions (pure Data layer); catch and
        // degrade here — a failed create must not crash the browser (T10 §5).
        try
        {
            MapJson.Save(doc, path);
        }
        catch (Exception e)
        {
            GD.PushError("[MapBrowser] could not create map file: " + e.Message);
            RefreshMaps();
            return;
        }
        OpenMap(doc.Id);
    }

    private void OnBackPressed()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
    }

    private void OpenMap(string mapId)
    {
        _lastOpenedId = mapId;
        EditorLaunch.MapId = mapId;
        GetTree().ChangeSceneToFile("res://Scenes/MapCreation/MapEditor.tscn");
    }

    /// <summary>Copies a built-in map to the editor-owned user directory without changing its
    /// immutable ID. CombatMapCatalog resolves the user directory first, making this a deliberate
    /// local override rather than an extra random-selection candidate.
    ///
    /// It never writes over a file that is already there. A readable copy — finished or not —
    /// is simply opened (an unfinished copy leaves the built-in card on the wall, so this
    /// button can meet one). An unreadable file is moved to the archive first, and only on a
    /// yes: it may be the one recoverable copy of someone's work (KNOWLEDGE v0.9.0).</summary>
    private void CreateOverrideAndOpen(MapDocument builtIn)
    {
        if (builtIn == null || string.IsNullOrWhiteSpace(builtIn.Id)) return;
        string overridePath = Path.Combine(_mapsDir, builtIn.Id + ".json");
        if (!File.Exists(overridePath))
        {
            SaveOverrideAndOpen(builtIn, overridePath);
            return;
        }

        if (MapJson.Load(overridePath, out _) != null)
        {
            OpenMap(builtIn.Id);
            return;
        }

        ShowConfirm(
            "A file for this map is already in your maps folder, but it cannot be read as a map:\n\n" +
            "    " + Path.GetFileName(overridePath) + "\n\n" +
            "Move it to maps/" + ArchiveFolder + "/ and start a fresh copy of the built-in map?",
            () =>
            {
                try
                {
                    string archive = Path.Combine(_mapsDir, ArchiveFolder, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
                    Directory.CreateDirectory(archive);
                    MoveToArchive(overridePath, archive);
                }
                catch (Exception e)
                {
                    GD.PushError("[MapBrowser] could not move the unreadable file aside: " + e.Message);
                    RefreshMaps();
                    return;
                }
                SaveOverrideAndOpen(builtIn, overridePath);
            }, "Archive and copy");
    }

    private void SaveOverrideAndOpen(MapDocument builtIn, string overridePath)
    {
        try
        {
            MapJson.Save(builtIn, overridePath);
        }
        catch (Exception e)
        {
            GD.PushError("[MapBrowser] could not create local map override: " + e.Message);
            RefreshMaps();
            return;
        }
        OpenMap(builtIn.Id);
    }

    private void OnRenamePressed(MapDocument doc, string path)
    {
        ShowPrompt("Rename Map", doc.Name, newName =>
        {
            doc.Name = newName;
            try
            {
                MapJson.Save(doc, path); // same path — file identity is the GUID, never move it
            }
            catch (Exception e) // F-MC4: degrade, never crash (T10 §5)
            {
                GD.PushError("[MapBrowser] rename save failed: " + e.Message);
            }
            RefreshMaps();
        });
    }

    private void OnDuplicatePressed(string path)
    {
        // Reload fresh from disk rather than reusing the card's in-memory doc, to avoid
        // aliasing it with the copy we're about to mutate.
        var fresh = MapJson.Load(path, out var warnings);
        foreach (var w in warnings)
            GD.PushWarning("[MapBrowser] " + w);

        if (fresh == null)
        {
            RefreshMaps(); // source vanished/corrupted between listing and click; degrade quietly
            return;
        }

        fresh.Id = Guid.NewGuid().ToString("N");
        fresh.Name = (string.IsNullOrEmpty(fresh.Name) ? "Untitled" : fresh.Name) + " (copy)";
        string newPath = Path.Combine(_mapsDir, fresh.Id + ".json");
        try
        {
            MapJson.Save(fresh, newPath);
        }
        catch (Exception e) // F-MC4: degrade, never crash (T10 §5)
        {
            GD.PushError("[MapBrowser] duplicate save failed: " + e.Message);
        }
        RefreshMaps();
    }

    private void OnDeletePressed(MapDocument doc, string path)
    {
        string name = string.IsNullOrWhiteSpace(doc.Name) ? "(unnamed)" : doc.Name;
        ShowConfirm($"Delete map '{name}'? This cannot be undone.", () =>
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                GD.PushWarning("[MapBrowser] could not delete '" + path + "': " + e.Message);
            }
            RefreshMaps();
        });
    }

    // ---------------------------------------------------------------- clean up

    /// <summary>
    /// Clears the wall of things that are not maps you can play: the user's unfinished maps
    /// (<see cref="MapReadiness"/>) and stray <c>*.json</c> files that are not maps at all.
    /// Everything is MOVED into a dated <c>maps/_archive/</c> folder after a confirmation that
    /// lists each file — never deleted, so a mistake is undone by moving the file back.
    /// Built-in maps are never touched.
    /// </summary>
    private void OnCleanUpPressed()
    {
        var unfinished = new List<Entry>();
        foreach (Entry entry in _entries)
            if (!entry.BuiltIn && entry.Problems.Count > 0) unfinished.Add(entry);
        if (unfinished.Count == 0 && _strayFiles.Count == 0) return;

        var text = new System.Text.StringBuilder();
        text.AppendLine($"Move {unfinished.Count + _strayFiles.Count} file(s) out of the map list?");
        if (unfinished.Count > 0)
        {
            text.AppendLine().AppendLine("Unfinished maps:");
            foreach (Entry entry in unfinished)
                text.AppendLine($"  •  {(string.IsNullOrWhiteSpace(entry.Doc.Name) ? "(unnamed)" : entry.Doc.Name)}  —  {string.Join(", ", entry.Problems)}");
        }
        if (_strayFiles.Count > 0)
        {
            text.AppendLine().AppendLine("Files that are not maps:");
            foreach (string stray in _strayFiles) text.AppendLine("  •  " + Path.GetFileName(stray));
        }
        text.AppendLine().Append("They are moved to maps/" + ArchiveFolder + "/ — nothing is deleted.");

        ShowConfirm(text.ToString(), () =>
        {
            string archive = Path.Combine(_mapsDir, ArchiveFolder, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            try
            {
                Directory.CreateDirectory(archive);
                foreach (Entry entry in unfinished) MoveToArchive(entry.Path, archive);
                foreach (string stray in _strayFiles) MoveToArchive(stray, archive);
            }
            catch (Exception e)
            {
                GD.PushError("[MapBrowser] clean up stopped: " + e.Message);
            }
            RefreshMaps();
        }, "Archive");
    }

    private static void MoveToArchive(string path, string archive)
    {
        if (!File.Exists(path)) return;
        File.Move(path, Path.Combine(archive, Path.GetFileName(path)), overwrite: false);
    }
}

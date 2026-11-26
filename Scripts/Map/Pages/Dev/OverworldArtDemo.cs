using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Fableland.Map;
using Fableland.Run;

namespace Fableland.Map.Pages.Dev;

/// <summary>
/// Dev scene: the overworld as torn storybook pages, for any seed
/// (`Docs/Art/OverworldStyle.md`). It generates a map exactly as a run does
/// (<see cref="MapGenerator.Generate"/>) but owns it — no <c>RunState</c>, no save, nothing
/// written to <c>user://</c> — and lets you scrub the VOID's 45-day clock.
///
///     Godot --path . res://Scenes/Dev/OverworldArtDemo.tscn
///
/// Wheel zooms on the cursor, left-drag pans, F fits the island. The panel holds the seed,
/// the day and the toggles (symbols, torn edges, mist, labels, the atlas side by side).
///
/// Capture mode renders review images in the real renderer and quits (needs a window):
///
///     open -g -n -W -a /Applications/Godot_mono.app --args --path "$PWD" --resolution 1600x900 \
///          res://Scenes/Dev/OverworldArtDemo.tscn -- --shots=/abs/dir [--seeds=A,B,C,D] [--map]
///
/// <c>--map</c> also photographs the real Map scene switched to its pages view (flat, tilted
/// boss-up, mist). That starts a run, so it first points the save service at a scratch folder
/// inside the output directory — the player's own slots are never opened.
/// </summary>
public partial class OverworldArtDemo : Node2D
{
    private const float ZoomMin = 0.22f, ZoomMax = 6f;
    private const int LastDay = 46;   // one past the day everything is gone

    private Control _leftClip, _rightClip;
    private OverworldPageView _pages;
    private AtlasPreview _atlas;
    private MapGraph _graph;
    private string _seed = "STORYBK1";
    private int _day = 1;
    private bool _torn = true, _compare, _mist;
    private Vector2 _focus;
    private float _zoom = 0.5f;
    private bool _dragging;
    private float _time;
    private Viewport.Msaa _msaaBefore;
    private double _refreshMs;

    private Control _panel;
    private LineEdit _seedEdit;
    private Label _dayLabel, _infoLabel;
    private HSlider _daySlider;
    private bool _capturing;

    public override void _Ready()
    {
        _msaaBefore = GetViewport().Msaa2D;
        GetViewport().Msaa2D = Viewport.Msaa.Msaa4X;   // torn edges are polygons: let them be smooth
        RenderingServer.SetDefaultClearColor(new Color(0.06f, 0.19f, 0.23f));

        _leftClip = new Control { Name = "PagesClip", ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _rightClip = new Control { Name = "AtlasClip", ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        AddChild(_leftClip);
        AddChild(_rightClip);
        _pages = new OverworldPageView { Name = "Pages" };
        _leftClip.AddChild(_pages);
        _atlas = new AtlasPreview { Name = "Atlas" };
        _rightClip.AddChild(_atlas);
        BuildPanel();
        _pages.LabelFont = _infoLabel.GetThemeDefaultFont();

        string shots = null;
        string[] seeds = null;
        bool realMap = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--map") realMap = true;
            else if (arg.StartsWith("--shots=")) shots = arg.Substring("--shots=".Length);
            else if (arg.StartsWith("--seeds=")) seeds = arg.Substring("--seeds=".Length).Split(',', StringSplitOptions.RemoveEmptyEntries);
            else if (arg.StartsWith("--seed=")) _seed = arg.Substring("--seed=".Length);
        }

        Generate(_seed);
        if (shots != null) _ = RunShots(shots, seeds, realMap);
    }

    public override void _ExitTree()
    {
        GetViewport().Msaa2D = _msaaBefore;
    }

    // ---- the map ---------------------------------------------------------------------

    private void Generate(string seed)
    {
        _seed = string.IsNullOrWhiteSpace(seed) ? DetRandom.NewSeed() : seed.Trim();
        _graph = MapGenerator.Generate(_seed);
        if (_seedEdit != null) _seedEdit.Text = _seed;
        ApplyDay(_day, refresh: false);
        ulong before = Time.GetTicksUsec();
        _pages.Build(_graph, _torn);
        double buildMs = (Time.GetTicksUsec() - before) / 1000.0;
        WireState();
        _atlas.Show(_graph);
        FitView();
        string realms = string.Join("  ·  ", _graph.Worlds.Select(world => $"{world.Abbr} {PageKit.Load(world.Abbr)?.Medium ?? "no kit"}"));
        int stamps = _pages.Model.Pages.Sum(page => page.Stamps.Count);
        _infoLabel.Text = $"{realms}\n{stamps} symbols · {_pages.Model.VertexCount} vertices · built in {buildMs:0} ms";
        GD.Print($"OverworldArtDemo: seed {_seed} → {string.Join(",", _graph.Worlds.Select(w => w.Abbr))}; {stamps} symbols, {_pages.Model.VertexCount} vertices, {_pages.Model.TriangleCount} triangles, build {buildMs:0.0} ms");
    }

    /// <summary>Stand the demo in for a run: the player is on the start node, has been
    /// nowhere else, and can step to its neighbours. With mist on only the home realm shows.</summary>
    private void WireState()
    {
        MapNode start = _graph.StartNode;
        string home = _graph.Worlds[0].Abbr;
        var neighbours = new HashSet<MapNode>();
        foreach (MapEdge edge in _graph.EdgesOf(start)) neighbours.Add(edge.Other(start));
        _pages.Mist = _mist;
        _pages.Day = _day;
        _pages.Token = start.Devoured ? null : start.Pos;
        _pages.RegionRevealed = region => !_mist || region == home;
        _pages.NodeVisible = node => !_mist || (node.WorldIndex >= 0 && node.Zone == home);
        _pages.NodeVisited = node => node == start;
        _pages.NodeReachable = node => neighbours.Contains(node) && !node.Devoured;
        _pages.NodeFrontier = _ => false;
        ulong before = Time.GetTicksUsec();
        _pages.RefreshState();
        _refreshMs = (Time.GetTicksUsec() - before) / 1000.0;
    }

    /// <summary>Put the map in the state the VOID's clock leaves it in on the morning of
    /// <paramref name="day"/> — the same pulses, in the same order, as the day-end pipeline
    /// (`VoidSchedule`, `VoidDevourStep`), on this demo's own graph.</summary>
    private void ApplyDay(int day, bool refresh = true)
    {
        _day = Mathf.Clamp(day, 1, LastDay);
        foreach (MapNode node in _graph.Nodes) node.Devoured = false;
        int[] pulses =
        {
            VoidSchedule.FirstLevelHalfDay, VoidSchedule.FirstLevelAllDay, VoidSchedule.SecondLevelHalfDay,
            VoidSchedule.SecondLevelAllDay, VoidSchedule.ThirdLevelAllDay, VoidSchedule.AllOuterCitiesDay,
        };
        foreach (int pulse in pulses)
        {
            if (pulse >= _day) break;
            foreach (MapNode node in _graph.Nodes)
                if (!node.Devoured && VoidSchedule.ShouldDevour(_graph, node, pulse)) node.Devoured = true;
            foreach (MapNode node in _graph.Nodes)
            {
                if (node.Devoured || node.WorldIndex < 0 || (node.Kind != NodeKind.TransportHub && node.Kind != NodeKind.Event)) continue;
                int cities = 0, gone = 0;
                foreach (MapEdge edge in _graph.EdgesOf(node))
                {
                    MapNode other = edge.Other(node);
                    if (!other.IsCombat) continue;
                    cities++;
                    if (other.Devoured) gone++;
                }
                if (cities > 0 && gone * 2 > cities) node.Devoured = true;
            }
        }
        if (_dayLabel != null) _dayLabel.Text = _day >= LastDay ? "Day 45+ (all devoured)" : $"Day {_day}";
        if (!refresh) return;
        WireState();
        _atlas.QueueRedraw();
        if (_capturing) GD.Print($"OverworldArtDemo: day {_day} — state refreshed in {_refreshMs:0.0} ms");
    }

    // ---- view ------------------------------------------------------------------------

    private Rect2 PagesRect()
    {
        Vector2 size = GetViewportRect().Size;
        return _compare ? new Rect2(0, 0, size.X * 0.5f, size.Y) : new Rect2(Vector2.Zero, size);
    }

    private void FitView()
    {
        Rect2 rect = PagesRect();
        float span = 2f * (MapGenerator.RimRadius + 150f);
        _zoom = Mathf.Clamp(Mathf.Min(rect.Size.X, rect.Size.Y) / span, ZoomMin, ZoomMax);
        _focus = MapGenerator.Center;
        ApplyView();
    }

    private void FocusRealm(int world, float fill = 0.92f)
    {
        RealmPage page = _pages.Model.Pages[Mathf.Clamp(world, 0, _pages.Model.Pages.Count - 1)];
        Rect2 rect = PagesRect();
        _zoom = Mathf.Clamp(Mathf.Min(rect.Size.X / page.Bounds.Size.X, rect.Size.Y / page.Bounds.Size.Y) * fill, ZoomMin, ZoomMax);
        _focus = page.Bounds.GetCenter();
        ApplyView();
    }

    private void ApplyView()
    {
        Vector2 size = GetViewportRect().Size;
        Rect2 left = PagesRect();
        _leftClip.Position = left.Position;
        _leftClip.Size = left.Size;
        _rightClip.Visible = _compare;
        if (_compare)
        {
            _rightClip.Position = new Vector2(size.X * 0.5f, 0);
            _rightClip.Size = new Vector2(size.X * 0.5f, size.Y);
        }
        // World → the clip's own space: the focus sits at the middle of its half.
        var view = new Transform2D(new Vector2(_zoom, 0), new Vector2(0, _zoom), left.Size * 0.5f - _focus * _zoom);
        _pages.ScreenRect = new Rect2(Vector2.Zero, left.Size);
        _pages.SetView(view, _zoom);
        _pages.RedrawOverlays();
        _atlas.Transform = view;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _pages.Animate(_time);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_capturing) return;
        if (@event is InputEventMouseButton button)
        {
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float factor = button.ButtonIndex == MouseButton.WheelUp ? 1.12f : 1f / 1.12f;
                Vector2 centre = PagesRect().Size * 0.5f;
                Vector2 cursor = button.Position - (_compare && button.Position.X > centre.X * 2f ? new Vector2(centre.X * 2f, 0) : Vector2.Zero);
                Vector2 under = _focus + (cursor - centre) / _zoom;
                _zoom = Mathf.Clamp(_zoom * factor, ZoomMin, ZoomMax);
                _focus = under - (cursor - centre) / _zoom;
                ApplyView();
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                _dragging = button.Pressed;
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            _focus -= motion.Relative / _zoom;
            ApplyView();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode == Key.F)
        {
            FitView();
        }
    }

    // ---- panel -----------------------------------------------------------------------

    private void BuildPanel()
    {
        var layer = new CanvasLayer { Name = "UI" };
        AddChild(layer);
        var panel = new PanelContainer { Name = "Panel", Position = new Vector2(26, 26) };
        panel.SelfModulate = new Color(1f, 1f, 1f, 0.92f);
        layer.AddChild(panel);
        _panel = panel;
        var margin = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 10);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);

        var seedRow = new HBoxContainer();
        seedRow.AddChild(new Label { Text = "Seed" });
        _seedEdit = new LineEdit { Text = _seed, CustomMinimumSize = new Vector2(120, 0), MaxLength = 16 };
        _seedEdit.TextSubmitted += text => { Generate(text); _seedEdit.ReleaseFocus(); };
        seedRow.AddChild(_seedEdit);
        var dice = new Button { Text = "New seed" };
        dice.Pressed += () => Generate(DetRandom.NewSeed());
        seedRow.AddChild(dice);
        box.AddChild(seedRow);

        var dayRow = new HBoxContainer();
        _dayLabel = new Label { Text = "Day 1", CustomMinimumSize = new Vector2(96, 0) };
        dayRow.AddChild(_dayLabel);
        _daySlider = new HSlider { MinValue = 1, MaxValue = LastDay, Step = 1, Value = 1, CustomMinimumSize = new Vector2(190, 0) };
        _daySlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _daySlider.ValueChanged += value => ApplyDay((int)value);
        dayRow.AddChild(_daySlider);
        box.AddChild(dayRow);

        var toggles = new GridContainer { Columns = 2 };
        toggles.AddChild(Toggle("Symbols", true, on => { _pages.ShowStamps = on; _pages.RedrawOverlays(); }));
        toggles.AddChild(Toggle("Torn edges", true, on => { _torn = on; Rebuild(); }));
        toggles.AddChild(Toggle("Mist", false, on => { _mist = on; WireState(); }));
        toggles.AddChild(Toggle("Labels", true, on => { _pages.ShowLabels = on; _pages.RedrawOverlays(); }));
        toggles.AddChild(Toggle("Atlas beside", false, on => { _compare = on; FitView(); }));
        toggles.AddChild(Toggle("Smooth (MSAA)", true, on => GetViewport().Msaa2D = on ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled));
        box.AddChild(toggles);

        _infoLabel = new Label { Text = "" };
        _infoLabel.AddThemeFontSizeOverride("font_size", 11);
        box.AddChild(_infoLabel);
    }

    private static CheckBox Toggle(string text, bool on, Action<bool> changed)
    {
        var check = new CheckBox { Text = text, ButtonPressed = on };
        check.Toggled += pressed => changed(pressed);
        return check;
    }

    /// <summary>Tear the same map again (the torn-edges toggle), keeping the view.</summary>
    private void Rebuild()
    {
        Vector2 focus = _focus;
        float zoom = _zoom;
        _pages.Build(_graph, _torn);
        WireState();
        _focus = focus;
        _zoom = zoom;
        ApplyView();
    }

    // ---- capture ---------------------------------------------------------------------

    /// <summary>Seeds whose realms between them show every pool world's kit.</summary>
    private static string[] ShowcaseSeeds(int count = 4)
    {
        var picked = new List<string>();
        var seen = new HashSet<string>();
        var candidates = new List<(string Seed, List<string> Worlds)>();
        for (int i = 1; i <= 120; i++)
        {
            string seed = "PAGES" + i.ToString("000");
            candidates.Add((seed, MapGenerator.Generate(seed).Worlds.Select(world => world.Abbr).ToList()));
        }
        while (picked.Count < count)
        {
            (string Seed, List<string> Worlds) best = candidates
                .Where(c => !picked.Contains(c.Seed))
                .OrderByDescending(c => c.Worlds.Count(w => !seen.Contains(w)))
                .ThenBy(c => c.Seed, StringComparer.Ordinal)
                .First();
            picked.Add(best.Seed);
            foreach (string world in best.Worlds) seen.Add(world);
        }
        return picked.ToArray();
    }

    private async Task RunShots(string dir, string[] seeds, bool realMap)
    {
        _capturing = true;
        try
        {
            Directory.CreateDirectory(dir);
            _panel.Visible = false;
            // The project's debug overlay (an autoload) is not part of the picture.
            if (Fableland.Debug.DebugManager.Instance is CanvasLayer overlay) overlay.Visible = false;
            seeds ??= ShowcaseSeeds();
            GD.Print($"OverworldArtDemo: capturing seeds {string.Join(", ", seeds)} into {dir}");
            int written = 0;

            // Every seed: the whole island on day 1, and one realm up close.
            for (int i = 0; i < seeds.Length; i++)
            {
                _day = 1;
                Generate(seeds[i]);
                written += await Shot(dir, $"{i + 1}_{seeds[i]}_d01_overview");
                FocusRealm(1 + i % 4);
                written += await Shot(dir, $"{i + 1}_{seeds[i]}_d01_realm{1 + i % 4}_{_graph.Worlds[1 + i % 4].Abbr}");
            }

            // The first seed through the VOID's clock: early, middle, late — overview and close.
            Generate(seeds[0]);
            foreach (int day in new[] { 12, 22, 32, 41, 44 })
            {
                ApplyDay(day);
                FitView();
                written += await Shot(dir, $"1_{seeds[0]}_d{day:00}_overview");
                FocusRealm(0, 0.95f);
                written += await Shot(dir, $"1_{seeds[0]}_d{day:00}_realm0_{_graph.Worlds[0].Abbr}");
            }
            // The frontier and the hole, close enough to judge an edge.
            ApplyDay(22);
            _focus = _graph.Center + new Vector2(60, -30);
            _zoom = 2.6f;
            ApplyView();
            written += await Shot(dir, $"1_{seeds[0]}_d22_void_close");
            RealmPage first = _pages.Model.Pages[0];
            _focus = first.Bounds.GetCenter() + new Vector2(0, -first.Bounds.Size.Y * 0.30f);
            _zoom = 2.6f;
            ApplyView();
            written += await Shot(dir, $"1_{seeds[0]}_d22_frontier_close");

            // Mist, the toggles, and the atlas beside it.
            ApplyDay(1);
            _mist = true;
            WireState();
            FitView();
            written += await Shot(dir, $"1_{seeds[0]}_d01_mist");
            _mist = false;
            WireState();
            _pages.ShowStamps = false;
            _pages.RedrawOverlays();
            written += await Shot(dir, $"1_{seeds[0]}_d01_no_symbols");
            _pages.ShowStamps = true;
            _torn = false;
            Rebuild();
            FitView();
            written += await Shot(dir, $"1_{seeds[0]}_d01_no_torn_edges");
            _torn = true;
            Rebuild();
            _compare = true;
            FitView();
            written += await Shot(dir, $"1_{seeds[0]}_d01_beside_atlas");
            ApplyDay(32);
            written += await Shot(dir, $"1_{seeds[0]}_d32_beside_atlas");
            _compare = false;
            _panel.Visible = true;
            ApplyDay(12);
            FitView();
            written += await Shot(dir, $"1_{seeds[0]}_d12_with_panel");

            if (realMap) written += await ShootRealMap(dir, seeds[0]);
            GD.Print($"OverworldArtDemo: wrote {written} image(s) to {dir}");
        }
        catch (Exception error)
        {
            GD.PushError($"OverworldArtDemo: capture failed: {error}");
        }
        GetTree().Quit();
    }

    /// <summary>The real Map scene, switched to its pages view with its own View button —
    /// proof that the third view mode works in the game, under every camera mode. Runs
    /// against a scratch save folder.</summary>
    private async Task<int> ShootRealMap(string dir, string seed)
    {
        string savesBefore = SaveGameService.DirectoryOverride;
        string scratch = Path.Combine(dir, "scratch_saves");
        Directory.CreateDirectory(scratch);
        SaveGameService.DirectoryOverride = scratch;      // never the player's own slots
        int written = 0;
        try
        {
            RunState.Instance?.NewRun(seed);
            _leftClip.Visible = false;
            _rightClip.Visible = false;
            _panel.Visible = false;
            if (ResourceLoader.Load<PackedScene>("res://Scenes/Map.tscn")?.Instantiate() is not Node map) return 0;
            AddChild(map);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var view = map.GetNode<Button>("UI/RenderButton");
            written += await Shot(dir, "map_scene_1_atlas");
            view.EmitSignal(BaseButton.SignalName.Pressed);       // atlas → schematic
            view.EmitSignal(BaseButton.SignalName.Pressed);       // schematic → pages
            written += await Shot(dir, "map_scene_2_pages_flat");
            map.GetNode<Button>("UI/MistButton").EmitSignal(BaseButton.SignalName.Pressed);
            written += await Shot(dir, "map_scene_3_pages_mist");
            map.GetNode<Button>("UI/MistButton").EmitSignal(BaseButton.SignalName.Pressed);
            map.GetNode<Button>("UI/ViewModeButton").EmitSignal(BaseButton.SignalName.Pressed);   // boss-up: rotated and tilted
            for (int i = 0; i < 90; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            written += await Shot(dir, "map_scene_4_pages_boss_up");
            view.EmitSignal(BaseButton.SignalName.Pressed);       // pages → atlas: it must come back
            written += await Shot(dir, "map_scene_5_atlas_again");
            map.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        finally
        {
            SaveGameService.DirectoryOverride = savesBefore;
        }
        return written;
    }

    private async Task<int> Shot(string dir, string name)
    {
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        string file = Path.Combine(dir, name + ".png");
        Error error = image.SavePng(file);
        if (error != Error.Ok) GD.PushError($"OverworldArtDemo: could not write {file}: {error}");
        return error == Error.Ok ? 1 : 0;
    }
}

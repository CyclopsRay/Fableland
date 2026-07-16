using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Fableland.MapCreation.Data;

namespace Fableland.MapCreation.Editor;

/// <summary>
/// GDD §7.8 — the map browser. Root script of <c>Scenes/MapCreation/MapBrowser.tscn</c>
/// (a thin root Control); every widget below is built in code in <see cref="_Ready"/>.
///
/// Store (GDD §8): one file per map at <c>user://maps/&lt;guid&gt;.json</c>, no
/// <c>_index.json</c> — the browser lists by directory scan, reading each file's own meta
/// block. File identity is the GUID (<see cref="MapDocument.Id"/>); renames only ever touch
/// <see cref="MapDocument.Name"/>, never the filename, so two maps can share a display name
/// (GDD §7.8 / KNOWLEDGE "name-derived filenames collapse duplicates").
/// </summary>
public partial class MapBrowser : Control
{
    private string _mapsDir;

    private GridContainer _grid;

    private ConfirmationDialog _confirmDialog;
    private Action _confirmAction;

    private ConfirmationDialog _promptDialog;
    private LineEdit _promptLineEdit;
    private Action<string> _promptCallback;

    public override void _Ready()
    {
        _mapsDir = ProjectSettings.GlobalizePath("user://maps");
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

        foreach (var failure in TileManifestLoader.SelfTest())
            GD.PushError("[MapCreation] " + failure);

        string projectRoot = ProjectSettings.GlobalizePath("res://");
        foreach (var failure in TileManifestLoader.SelfTestFixtures(projectRoot))
            GD.PushError("[MapCreation] " + failure);
    }

    // ---------------------------------------------------------------- UI build

    private void BuildUi()
    {
        // Decorative full-rect background; MUST ignore mouse or it eats clicks meant for
        // the buttons/cards on top of it (KNOWLEDGE v0.2.4).
        var bg = new ColorRect
        {
            Color = new Color(0.06f, 0.06f, 0.09f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
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
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        var topBar = new HBoxContainer();
        topBar.AddThemeConstantOverride("separation", 12);
        root.AddChild(topBar);

        var title = new Label { Text = "Map Creation", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 28);
        topBar.AddChild(title);

        var createBtn = new Button { Text = "Create", CustomMinimumSize = new Vector2(110, 40) };
        createBtn.Pressed += OnCreatePressed;
        topBar.AddChild(createBtn);

        var backBtn = new Button { Text = "Back", CustomMinimumSize = new Vector2(110, 40) };
        backBtn.Pressed += OnBackPressed;
        topBar.AddChild(backBtn);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(scroll);

        _grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 14);
        _grid.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(_grid);

        BuildDialogs();
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

    private void ShowConfirm(string text, Action onConfirm)
    {
        _confirmDialog.DialogText = text;
        _confirmAction = onConfirm;
        _confirmDialog.PopupCentered();
    }

    // ---------------------------------------------------------------- listing

    /// <summary>Rescans <c>user://maps</c> and rebuilds every card. Simplest-correct refresh
    /// (GDD says nothing fancier is needed at this scale) — called after every action.</summary>
    private void RefreshMaps()
    {
        foreach (Node child in _grid.GetChildren())
            child.QueueFree();

        var loaded = new List<(MapDocument Doc, string Path)>();
        foreach (var path in SafeListMapFiles())
        {
            var doc = MapJson.Load(path, out var warnings);
            foreach (var w in warnings)
                GD.PushWarning("[MapBrowser] " + w);

            if (doc == null) continue; // corrupt/unreadable file: skip it, never crash the browser
            loaded.Add((doc, path));
        }

        // ModifiedUtc is written via DateTime.ToString("o") (ISO-8601), which sorts
        // chronologically as a plain string — no need to parse it to compare.
        loaded.Sort((a, b) => string.CompareOrdinal(b.Doc.ModifiedUtc, a.Doc.ModifiedUtc));

        foreach (var (doc, path) in loaded)
            _grid.AddChild(BuildCard(doc, path));
    }

    private string[] SafeListMapFiles()
    {
        try
        {
            // "*.json" naturally excludes the ".tmp" files MapJson.Save uses mid-write.
            return Directory.GetFiles(_mapsDir, "*.json");
        }
        catch (Exception e)
        {
            GD.PushWarning("[MapBrowser] could not list maps directory: " + e.Message);
            return Array.Empty<string>();
        }
    }

    private Control BuildCard(MapDocument doc, string path)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(230, 190) };

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        string name = string.IsNullOrWhiteSpace(doc.Name) ? "(unnamed)" : doc.Name;
        var nameLabel = new Label { Text = name, ClipText = true };
        nameLabel.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(nameLabel);

        string world = doc.Worlds is { Count: > 0 } ? string.Join(", ", doc.Worlds)
            : string.IsNullOrEmpty(doc.World) ? "All worlds" : doc.World;
        vbox.AddChild(new Label { Text = "World: " + world });
        string levels = doc.HardshipLevels is { Count: > 0 } ? string.Join(", ", doc.HardshipLevels) : "all";
        string goals = doc.Goals is { Count: > 0 } ? string.Join(", ", doc.Goals) : CombatMapGoals.Claim;
        vbox.AddChild(new Label { Text = "LV: " + levels + "  ·  " + goals + "  ·  " + doc.Terrain });

        vbox.AddChild(new Label { Text = BattlefieldDims(doc) });

        vbox.AddChild(new Label { Text = FormatModified(doc.ModifiedUtc) });

        var actionRow1 = new HBoxContainer();
        vbox.AddChild(actionRow1);
        var openBtn = new Button { Text = "Open" };
        openBtn.Pressed += () => OpenMap(doc.Id);
        actionRow1.AddChild(openBtn);

        var renameBtn = new Button { Text = "Rename" };
        renameBtn.Pressed += () => OnRenamePressed(doc, path);
        actionRow1.AddChild(renameBtn);

        var actionRow2 = new HBoxContainer();
        vbox.AddChild(actionRow2);
        var dupBtn = new Button { Text = "Duplicate" };
        dupBtn.Pressed += () => OnDuplicatePressed(path);
        actionRow2.AddChild(dupBtn);

        var delBtn = new Button { Text = "Delete" };
        delBtn.Pressed += () => OnDeletePressed(doc, path);
        actionRow2.AddChild(delBtn);

        return panel;
    }

    private static string BattlefieldDims(MapDocument doc)
    {
        if (doc.Layers != null)
        {
            foreach (var layer in doc.Layers)
            {
                if (layer != null && layer.Role == MapLayerData.RoleBattlefield)
                    return $"{layer.GridW} x {layer.GridH}";
            }
        }
        // The loader (MapJson.Validate) always injects a battlefield layer, so this
        // shouldn't happen in practice — fall back gracefully rather than crash.
        return "—";
    }

    private static string FormatModified(string modifiedUtc)
    {
        if (DateTime.TryParse(modifiedUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.ToString("yyyy-MM-dd HH:mm");
        }
        return modifiedUtc ?? "";
    }

    // ---------------------------------------------------------------- actions

    private void OnCreatePressed()
    {
        ShowPrompt("Create Map", "", name =>
        {
            var doc = MapDocument.CreateNew(name);
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
        });
    }

    private void OnBackPressed()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
    }

    private void OpenMap(string mapId)
    {
        EditorLaunch.MapId = mapId;
        GetTree().ChangeSceneToFile("res://Scenes/MapCreation/MapEditor.tscn");
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
}

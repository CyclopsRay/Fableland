using System.Collections.Generic;
using Godot;

namespace Fableland.MapCreation;

/// <summary>
/// Map Browser — lists all saved custom maps, lets the player create new ones,
/// open existing ones (left-click), or modify properties (right-click).
/// </summary>
public partial class MapBrowser : Control
{
    private VBoxContainer _listContainer;
    private Button _createButton;
    private ScrollContainer _scroll;

    public override void _Ready()
    {
        // Backdrop
        var bg = new ColorRect();
        bg.AnchorsPreset = (int)LayoutPreset.FullRect;
        bg.Color = new Color(0.06f, 0.06f, 0.09f, 1);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

        // Title
        var title = new Label();
        title.Text = "Map Creation";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 36);
        title.SetAnchorsPreset(LayoutPreset.TopWide);
        title.Position = new Vector2(0, 24);
        title.Size = new Vector2(0, 50);
        AddChild(title);

        // Back button
        var backBtn = new Button();
        backBtn.Text = "< Back";
        backBtn.Position = new Vector2(16, 16);
        backBtn.CustomMinimumSize = new Vector2(80, 36);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
        AddChild(backBtn);

        // Create button (top right)
        _createButton = new Button();
        _createButton.Text = "+ Create New Map";
        _createButton.CustomMinimumSize = new Vector2(180, 40);
        _createButton.Pressed += OnCreateNew;
        AddChild(_createButton);

        // Scrollable map list
        _scroll = new ScrollContainer();
        _scroll.Position = new Vector2(40, 90);
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(_scroll);

        _listContainer = new VBoxContainer();
        _listContainer.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_listContainer);

        RefreshList();
    }

    public override void _Process(double delta)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        _createButton.Position = new Vector2(vp.X - 220, 20);
        _scroll.Size = new Vector2(vp.X - 80, vp.Y - 130);
    }

    private void RefreshList()
    {
        foreach (var child in _listContainer.GetChildren())
            child.QueueFree();

        var maps = MapSaveLoad.ListMaps();
        if (maps.Count == 0)
        {
            var emptyLabel = new Label();
            emptyLabel.Text = "No maps yet. Click \"+ Create New Map\" to get started.";
            emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            emptyLabel.AddThemeFontSizeOverride("font_size", 18);
            emptyLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
            _listContainer.AddChild(emptyLabel);
            return;
        }

        foreach (var meta in maps)
        {
            var row = MakeMapRow(meta);
            _listContainer.AddChild(row);
        }
    }

    private Control MakeMapRow(MapMeta meta)
    {
        // Each row is a Button that looks like a panel
        var btn = new Button();
        btn.CustomMinimumSize = new Vector2(0, 52);
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.Text = $"{meta.Name}    —    {meta.World}    —    {meta.GridWidth}×{meta.GridHeight}";
        btn.AddThemeFontSizeOverride("font_size", 18);
        btn.Alignment = HorizontalAlignment.Left;

        // Left-click → open
        btn.Pressed += () => OpenMap(meta.FileName);

        // Right-click → properties menu
        btn.GuiInput += (InputEvent ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
            {
                ShowContextMenu(meta, mb.GlobalPosition);
            }
        };

        return btn;
    }

    private void ShowContextMenu(MapMeta meta, Vector2 screenPos)
    {
        var popup = new PopupMenu();
        popup.AddItem("Open", 0);
        popup.AddItem("Edit Properties", 1);
        popup.AddSeparator();
        popup.AddItem("Delete", 2);

        popup.IdPressed += (long id) =>
        {
            switch ((int)id)
            {
                case 0: OpenMap(meta.FileName); break;
                case 1: ShowPropertiesDialog(meta); break;
                case 2: ConfirmDelete(meta.FileName); break;
            }
        };

        popup.Position = (Vector2I)screenPos;
        AddChild(popup);
        popup.Popup();
    }

    private void ShowPropertiesDialog(MapMeta meta)
    {
        var dlg = new AcceptDialog();
        dlg.Title = "Map Properties";

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);

        var nameLabel = new Label();
        nameLabel.Text = "Name:";
        vbox.AddChild(nameLabel);

        var nameEdit = new LineEdit();
        nameEdit.Text = meta.Name;
        vbox.AddChild(nameEdit);

        var worldLabel = new Label();
        worldLabel.Text = "World:";
        vbox.AddChild(worldLabel);

        var worldOpt = new OptionButton();
        int selIdx = 0;
        for (int i = 0; i < Fableland.Map.WorldDef.Pool.Count; i++)
        {
            var w = Fableland.Map.WorldDef.Pool[i];
            worldOpt.AddItem($"{w.Name} ({w.Abbr})");
            if (w.Name == meta.World) selIdx = i;
        }
        worldOpt.Selected = selIdx;
        vbox.AddChild(worldOpt);

        var infoLabel = new Label();
        infoLabel.Text = $"Size: {meta.GridWidth}×{meta.GridHeight}\nSaved: {meta.SavedAt}";
        infoLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
        vbox.AddChild(infoLabel);

        dlg.AddChild(vbox);
        vbox.Position = new Vector2(16, 16);

        dlg.OkButtonText = "Save";
        dlg.Confirmed += () =>
        {
            meta.Name = string.IsNullOrWhiteSpace(nameEdit.Text) ? meta.Name : nameEdit.Text.Trim();
            meta.World = Fableland.Map.WorldDef.Pool[worldOpt.Selected].Name;

            // Reload the full map, update its meta, and save
            var map = MapSaveLoad.Load(meta.FileName);
            if (map != null)
            {
                map.Meta = meta;
                MapSaveLoad.Save(map);
            }
            RefreshList();
        };

        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void OpenMap(string fileName)
    {
        var map = MapSaveLoad.Load(fileName);
        if (map == null) return;

        var editorScene = GD.Load<PackedScene>("res://Scenes/MapCreation/MapEditor.tscn");
        var editor = editorScene.Instantiate<MapEditor>();
        editor.LoadedMap = map;
        GetTree().Root.AddChild(editor);
        QueueFree();
    }

    private void OnCreateNew()
    {
        var dlg = new AcceptDialog();
        dlg.Title = "New Map";
        dlg.OkButtonText = "Create";

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        vbox.CustomMinimumSize = new Vector2(360, 0);

        // Name field
        vbox.AddChild(MakeFieldLabel("Map Name:"));
        var nameEdit = new LineEdit();
        nameEdit.PlaceholderText = "My Awesome Level";
        nameEdit.CustomMinimumSize = new Vector2(0, 36);
        vbox.AddChild(nameEdit);

        // Width
        vbox.AddChild(MakeFieldLabel("Width (cells):"));
        var wSpin = new SpinBox();
        wSpin.MinValue = 20; wSpin.MaxValue = 200; wSpin.Value = 60;
        wSpin.CustomMinimumSize = new Vector2(0, 36);
        vbox.AddChild(wSpin);

        // Height
        vbox.AddChild(MakeFieldLabel("Height (cells):"));
        var hSpin = new SpinBox();
        hSpin.MinValue = 10; hSpin.MaxValue = 100; hSpin.Value = 30;
        hSpin.CustomMinimumSize = new Vector2(0, 36);
        vbox.AddChild(hSpin);

        // World
        vbox.AddChild(MakeFieldLabel("World:"));
        var worldOpt = new OptionButton();
        foreach (var w in Fableland.Map.WorldDef.Pool)
            worldOpt.AddItem($"{w.Name} ({w.Abbr})");
        worldOpt.CustomMinimumSize = new Vector2(0, 36);
        vbox.AddChild(worldOpt);

        dlg.AddChild(vbox);
        vbox.Position = new Vector2(16, 16);

        dlg.Confirmed += () =>
        {
            string name = string.IsNullOrWhiteSpace(nameEdit.Text) ? "Untitled" : nameEdit.Text.Trim();
            int w = (int)wSpin.Value;
            int h = (int)hSpin.Value;
            string world = Fableland.Map.WorldDef.Pool[worldOpt.Selected >= 0 ? worldOpt.Selected : 0].Name;

            var map = CustomMap.CreateEmpty(w, h, name);
            map.Meta.World = world;
            MapSaveLoad.Save(map);

            var editorScene = GD.Load<PackedScene>("res://Scenes/MapCreation/MapEditor.tscn");
            var editor = editorScene.Instantiate<MapEditor>();
            editor.LoadedMap = map;
            GetTree().Root.AddChild(editor);
            QueueFree();
        };

        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void ConfirmDelete(string fileName)
    {
        var dlg = new ConfirmationDialog();
        dlg.Title = "Delete Map";
        dlg.DialogText = $"Delete \"{fileName}\"?\nThis cannot be undone.";
        dlg.GetOkButton().Text = "Delete";
        dlg.Confirmed += () =>
        {
            MapSaveLoad.Delete(fileName);
            RefreshList();
        };
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private static Label MakeFieldLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 14);
        return lbl;
    }
}

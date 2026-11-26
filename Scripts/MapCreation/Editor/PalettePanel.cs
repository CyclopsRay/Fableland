namespace Fableland.MapCreation.Editor;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor.Tools;

/// <summary>
/// GDD §2.2, §7.1 — the tile palette, now a HORIZONTAL bottom-dock strip (item 1) instead
/// of a right-panel column. Every registered tile def is a "chip" (sprite thumbnail +
/// name + a ⚙ effect-config gear), grouped by realm tile set and <see cref="TileCategory"/>
/// in <see cref="TileRegistry.All"/>'s own order (a header appears whenever either changes;
/// the registry is authored grouped that way so no re-sort is needed — T00 rule 1 stays true).
///
/// A **Realm** selector at the left end filters the strip to one tile set (plus the shared
/// markers, which always stay). Each realm has its own art language and a map is meant to
/// stay inside one, so the selector opens on the realm the map is tagged for; with six
/// realms' kits registered, an unfiltered strip is far too long to scan.
///
/// Chips show the def's real sprite when its <see cref="TileDef.SpriteSlot"/> loads (item 8),
/// so "implemented vs placeholder" is obvious at a glance; a missing/absent sprite falls back
/// to the <see cref="TileDef.EditorColor"/> swatch. The registry is a fixed code-defined
/// table, so the panel is built EXACTLY ONCE; only per-chip highlight (selected brush) and
/// greyed/disabled state (illegal on the current layer's role, GDD §2.2 allowedRoles) track
/// live state, refreshed in place on every <see cref="EditorState.StateChanged"/>.
/// </summary>
public sealed class PalettePanel
{
    private readonly EditorState _state;
    private readonly Action<string> _onConfigureEffect;
    private readonly Dictionary<string, PanelContainer> _chipByDefId = new();
    private readonly Dictionary<string, Texture2D> _thumbCache = new();
    private readonly List<(Control Group, Control Separator, string TileSet)> _groups = new();
    private readonly List<string> _filterSetIds = new();   // OptionButton index -> set id (null = all)
    private OptionButton _setFilter;

    private const float ThumbSize = 32f;
    private const float ChipWidth = 76f;

    public PalettePanel(EditorState state, Control container, Action<string> onConfigureEffect)
    {
        _state = state;
        _onConfigureEffect = onConfigureEffect;
        Build(container);
        _state.StateChanged += Refresh;
        Refresh();
    }

    private void Build(Control container)
    {
        var dock = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        dock.AddThemeConstantOverride("separation", 8);
        container.AddChild(dock);

        dock.AddChild(BuildRealmFilter());
        dock.AddChild(new VSeparator());

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        dock.AddChild(scroll);

        // One horizontal strip of per-(tile set, category) groups.
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(strip);

        TileCategory? lastCategory = null;
        string lastSet = null;
        HBoxContainer chipsRow = null;

        foreach (var def in TileRegistry.All)
        {
            if (lastCategory != def.Category || lastSet != def.TileSet)
            {
                lastCategory = def.Category;
                lastSet = def.TileSet;

                Control separator = null;
                if (_groups.Count > 0)
                {
                    separator = new VSeparator();
                    strip.AddChild(separator);
                }

                var group = new VBoxContainer();
                group.AddThemeConstantOverride("separation", 2);
                strip.AddChild(group);
                _groups.Add((group, separator, def.TileSet));

                group.AddChild(new Label
                {
                    Text = GroupTitle(def),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });

                chipsRow = new HBoxContainer();
                chipsRow.AddThemeConstantOverride("separation", 4);
                group.AddChild(chipsRow);
            }

            chipsRow.AddChild(BuildChip(def));
        }

        ApplyRealmFilter(TileSetForDocument(_state.Document));
    }

    /// <summary>"Ground" for shared tiles, "SL · Ground" for a realm's — the prefix keeps
    /// groups distinguishable when the selector is on "All realms". A world's second realm
    /// goes by its own name ("Cloud Court · Ground"): two sets must not both read "PL".</summary>
    private static string GroupTitle(TileDef def)
    {
        if (TileSets.TryGet(def.TileSet, out var info) && TileSets.GroupPrefix(info) is { Length: > 0 } prefix)
            return prefix + " · " + def.Category;
        return def.Category.ToString();
    }

    private Control BuildRealmFilter()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(new Label { Text = "Realm", MouseFilter = Control.MouseFilterEnum.Ignore });

        _setFilter = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Show one realm's tile set. Shared marker tiles are always shown.",
        };
        _setFilter.AddItem("All realms");
        _filterSetIds.Add(null);
        foreach (var info in TileSets.All)
        {
            if (info.Id == TileSets.Core) continue;
            _setFilter.AddItem(info.DisplayName);
            _filterSetIds.Add(info.Id);
        }
        _setFilter.ItemSelected += index =>
        {
            int i = (int)index;
            ApplyRealmFilter(i >= 0 && i < _filterSetIds.Count ? _filterSetIds[i] : null);
        };
        box.AddChild(_setFilter);
        return box;
    }

    /// <summary>Shows the groups of <paramref name="tileSet"/> plus the shared set; null shows
    /// everything. Only visibility changes — chips are still built exactly once.</summary>
    private void ApplyRealmFilter(string tileSet)
    {
        int index = _filterSetIds.IndexOf(tileSet);
        if (index < 0) { index = 0; tileSet = null; }
        if (_setFilter != null && _setFilter.Selected != index) _setFilter.Select(index);

        bool first = true;
        foreach (var (group, separator, set) in _groups)
        {
            bool show = tileSet == null || set == TileSets.Core || set == tileSet;
            group.Visible = show;
            if (separator != null) separator.Visible = show && !first;
            if (show) first = false;
        }
    }

    /// <summary>The tile set of the first world this map is tagged for (`MapDocument.Worlds`
    /// holds names or abbreviations), or null when it is untagged / unknown. A world with two
    /// realms (the Palace: Loong's Nest and Cloud Court) opens on the one the map is built
    /// from (<see cref="MapRealm.Of"/>).</summary>
    private static string TileSetForDocument(MapDocument doc)
    {
        if (doc?.Worlds == null) return null;
        foreach (string world in doc.Worlds)
        {
            string abbr = world?.Trim();
            if (string.IsNullOrEmpty(abbr)) continue;
            foreach (WorldDef def in WorldDef.Pool)
            {
                if (string.Equals(def.Name, abbr, StringComparison.OrdinalIgnoreCase)) { abbr = def.Abbr; break; }
            }
            string set = MapRealm.Of(doc, abbr);
            if (set != null) return set;
        }
        return null;
    }

    private Control BuildChip(TileDef def)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(ChipWidth, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) });

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 1);
        panel.AddChild(vbox);

        // Row 1: thumbnail (selects the brush on click) + gear (opens the effect painter).
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 2);
        vbox.AddChild(topRow);

        var thumb = MakeThumb(def);
        topRow.AddChild(thumb);

        // Rule tiles are invisible generation markers — they have no effect area to author.
        if (def.Category != TileCategory.Rule)
        {
            string defIdForGear = def.Id;
            var gear = new Button
            {
                Text = "⚙",
                Flat = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(20, 20),
                TooltipText = $"Configure effect area ({def.FootprintW * ShapeDef.SubcellsPerAxis}×{def.FootprintH * ShapeDef.SubcellsPerAxis} sub-cells)",
            };
            gear.Pressed += () => _onConfigureEffect?.Invoke(defIdForGear);
            topRow.AddChild(gear);
        }

        vbox.AddChild(new Label
        {
            Text = def.DisplayName,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        string defId = def.Id;
        panel.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && IsAllowedOnCurrentLayer(def))
            {
                _state.BrushDefId = defId;
                // Selecting a palette tile always re-arms Paint (bug-fix: picking a brush while
                // Erase/Lasso was active used to strand the user with no way to paint it).
                _state.ActiveTool = EditorState.EditorTool.Paint;
                _state.RaiseStateChanged();
            }
        };

        _chipByDefId[def.Id] = panel;
        return panel;
    }

    /// <summary>Item 8 — a real sprite thumbnail when <see cref="TileDef.SpriteSlot"/> loads
    /// (trimmed to the art's used rect, aspect-fit), else the flat <see cref="TileDef.EditorColor"/>
    /// swatch so placeholders read as placeholders.</summary>
    private Control MakeThumb(TileDef def)
    {
        var tex = LoadThumb(def.SpriteSlot);
        if (tex != null)
        {
            Texture2D shown = tex;
            var image = tex.GetImage();
            if (image != null)
            {
                var used = image.GetUsedRect();
                if (used.Size.X > 0 && used.Size.Y > 0 &&
                    (used.Position != Vector2I.Zero || used.Size != new Vector2I(tex.GetWidth(), tex.GetHeight())))
                {
                    shown = new AtlasTexture { Atlas = tex, Region = new Rect2(used.Position, used.Size) };
                }
            }

            return new TextureRect
            {
                Texture = shown,
                CustomMinimumSize = new Vector2(ThumbSize, ThumbSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                TooltipText = def.DisplayName,
            };
        }

        return new ColorRect
        {
            CustomMinimumSize = new Vector2(ThumbSize, ThumbSize),
            Color = ColorFromHex(def.EditorColor),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TooltipText = def.DisplayName + " (no sprite yet)",
        };
    }

    private Texture2D LoadThumb(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_thumbCache.TryGetValue(path, out var cached)) return cached;
        var tex = ResourceLoader.Load<Texture2D>(path);
        _thumbCache[path] = tex; // cache null too, so a missing path isn't retried every refresh
        return tex;
    }

    private bool IsAllowedOnCurrentLayer(TileDef def)
    {
        var layer = _state.CurrentLayer;
        if (layer == null) return false;
        return (def.AllowedRoles & ToolBase.RoleMaskOf(layer.Role)) != 0;
    }

    /// <summary>Per-chip highlight (selected brush) + grey-out (illegal on the current layer's
    /// role, GDD §2.2). Runs on every StateChanged — cheap (fixed chip count, style writes only).</summary>
    private void Refresh()
    {
        foreach (var def in TileRegistry.All)
        {
            if (!_chipByDefId.TryGetValue(def.Id, out var chip)) continue;

            bool allowed = IsAllowedOnCurrentLayer(def);
            bool selected = def.Id == _state.BrushDefId;

            chip.Modulate = allowed ? Colors.White : new Color(1f, 1f, 1f, 0.35f);
            chip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = selected ? new Color(1f, 1f, 1f, 0.25f) : new Color(0, 0, 0, 0),
            });
            chip.TooltipText = allowed ? "" : $"Not allowed on this layer's role ({_state.CurrentLayer?.Role})";
        }
    }

    private static Color ColorFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return new Color(1f, 0f, 1f);
        try { return new Color(hex); }
        catch { return new Color(1f, 0f, 1f); }
    }
}

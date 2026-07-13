namespace Fableland.MapCreation.Editor;

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Editor.Tools;

/// <summary>
/// GDD §2.2, §7.1 — fills <see cref="MapEditor.PaletteBox"/> with every registered
/// tile def, grouped by <see cref="TileCategory"/> in <see cref="TileRegistry.All"/>'s
/// own order (a category header Label appears whenever the category changes — the
/// registry is already authored grouped-by-category, so no sort/re-group is needed
/// here; T00 rule 1 "adding a tile kind is adding a registry entry" stays true).
///
/// The registry is a fixed, code-defined table (no runtime add/remove), so unlike
/// <see cref="LayerPanel"/> this panel is built EXACTLY ONCE; only per-row highlight
/// (selected brush) and greyed/disabled state (illegal on the current layer's role,
/// GDD §2.2 allowedRoles) need to track live state, refreshed in place on every
/// <see cref="EditorState.StateChanged"/> — including after MC4's EyedropperTool sets
/// <see cref="EditorState.BrushDefId"/> (it never raises StateChanged itself; the
/// GridView cell-press router does, right after the tool runs, which is what drives
/// this panel's highlight to follow the eyedropped tile).
/// </summary>
public sealed class PalettePanel
{
    private readonly EditorState _state;
    private readonly Dictionary<string, PanelContainer> _rowByDefId = new();

    public PalettePanel(EditorState state, VBoxContainer container)
    {
        _state = state;
        Build(container);
        _state.StateChanged += Refresh;
        Refresh();
    }

    private void Build(VBoxContainer container)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200),
        };
        container.AddChild(scroll);

        var list = new VBoxContainer();
        scroll.AddChild(list);

        TileCategory? lastCategory = null;

        foreach (var def in TileRegistry.All)
        {
            if (lastCategory != def.Category)
            {
                lastCategory = def.Category;
                list.AddChild(new Label
                {
                    Text = def.Category.ToString(),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }

            list.AddChild(BuildRow(def));
        }
    }

    private Control BuildRow(TileDef def)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) });

        var hbox = new HBoxContainer();
        panel.AddChild(hbox);

        hbox.AddChild(new ColorRect
        {
            CustomMinimumSize = new Vector2(16, 16),
            Color = ColorFromHex(def.EditorColor),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        hbox.AddChild(new Label
        {
            Text = def.DisplayName,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        string defId = def.Id;
        panel.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && IsAllowedOnCurrentLayer(def))
            {
                _state.BrushDefId = defId;
                // Bug-fix (user report): picking a brush while Erase/Lasso/etc. is active
                // used to leave that tool selected with no way to paint the new brush
                // without a manual rail click. Selecting a palette tile always re-arms Paint.
                _state.ActiveTool = EditorState.EditorTool.Paint;
                _state.RaiseStateChanged();
            }
        };

        _rowByDefId[def.Id] = panel;
        return panel;
    }

    private bool IsAllowedOnCurrentLayer(TileDef def)
    {
        var layer = _state.CurrentLayer;
        if (layer == null) return false;
        return (def.AllowedRoles & ToolBase.RoleMaskOf(layer.Role)) != 0;
    }

    /// <summary>Per-row highlight (selected brush, ~25% white overlay) + grey-out
    /// (illegal on the current layer's role, GDD §2.2). Runs on every StateChanged —
    /// cheap (fixed row count, style-override writes only, no rebuild).</summary>
    private void Refresh()
    {
        foreach (var def in TileRegistry.All)
        {
            if (!_rowByDefId.TryGetValue(def.Id, out var row)) continue;

            bool allowed = IsAllowedOnCurrentLayer(def);
            bool selected = def.Id == _state.BrushDefId;

            row.Modulate = allowed ? Colors.White : new Color(1f, 1f, 1f, 0.35f);
            row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = selected ? new Color(1f, 1f, 1f, 0.25f) : new Color(0, 0, 0, 0),
            });
            row.TooltipText = allowed ? "" : $"Not allowed on this layer's role ({_state.CurrentLayer?.Role})";
        }
    }

    private static Color ColorFromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return new Color(1f, 0f, 1f);
        try { return new Color(hex); }
        catch { return new Color(1f, 0f, 1f); }
    }
}

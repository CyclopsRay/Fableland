using System;
using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

namespace Fableland.MapCreation.Editor;

/// <summary>
/// The look of the map browser (MapCreation.gdd §7.8): its cards, badges and realm chips.
/// The other half — state, listing, filtering and actions — is <c>MapBrowser.cs</c>.
///
/// A card, top to bottom:
///   * the picture of the map (<see cref="MapThumbnailRenderer"/>), filling the card;
///   * laid over the picture's top: badges (realm, level, source, status), the map's name in
///     bold, and two small lines of detail — a side-view arena keeps its sky up there, so
///     the words cover the least, and the floor and what stands on it stay in view;
///   * a plain footer with the one primary action and a "⋯" menu for the rest.
///
/// Nothing on a card has a minimum width of its own (text clips or wraps), so a long name
/// can never make a card — and with it the wall — wider than its column.
/// </summary>
public partial class MapBrowser
{
    private static readonly Color CardColor = new(0.10f, 0.10f, 0.14f);

    private Control BuildCard(Entry entry)
    {
        MapDocument doc = entry.Doc;
        bool unfinished = entry.Problems.Count > 0;
        string name = string.IsNullOrWhiteSpace(doc.Name) ? "(unnamed)" : doc.Name;

        // Until its picture arrives a card shows the map's own canvas colour, darkened.
        Color ground = Ink;
        try { ground = new Color(doc.Canvas?.Color ?? "#1A1A22").Darkened(0.45f); }
        catch { /* malformed colour in the file: keep the neutral card */ }

        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, CardHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipContents = true,
            TooltipText = CardTooltip(entry, name),
        };
        Color edge = entry.Accent.Darkened(unfinished ? 0.5f : 0.1f);
        StyleBoxFlat frame = CardFrame(edge);
        StyleBoxFlat lit = CardFrame(edge.Lightened(0.45f));
        card.AddThemeStyleboxOverride("panel", frame);
        card.MouseEntered += () => card.AddThemeStyleboxOverride("panel", lit);
        card.MouseExited += () => card.AddThemeStyleboxOverride("panel", frame);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        card.AddChild(column);

        // -- the picture, with the name block laid over its top. A MarginContainer gives all
        //    of its children the same rect, so the layers simply stack in draw order.
        var picture = new MarginContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        column.AddChild(picture);

        picture.AddChild(new ColorRect { Color = ground, MouseFilter = Control.MouseFilterEnum.Ignore });
        entry.Art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = unfinished ? new Color(0.55f, 0.55f, 0.60f) : Colors.White,
        };
        picture.AddChild(entry.Art);
        picture.AddChild(new TextureRect
        {
            Texture = _scrim,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pad.AddThemeConstantOverride("margin_left", 10);
        pad.AddThemeConstantOverride("margin_right", 10);
        pad.AddThemeConstantOverride("margin_top", 8);
        pad.AddThemeConstantOverride("margin_bottom", 6);
        picture.AddChild(pad);

        var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", 1);
        pad.AddChild(words);

        // badges: a flow, so a narrow card wraps them rather than growing wider than its column
        var badges = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        badges.AddThemeConstantOverride("h_separation", 5);
        badges.AddThemeConstantOverride("v_separation", 4);
        words.AddChild(badges);
        badges.AddChild(entry.RealmSet != null
            ? Badge(entry.RealmName, entry.Accent.Darkened(0.25f), Colors.White)
            : Badge("No realm", new Color(0.25f, 0.26f, 0.32f), FaintColor));
        badges.AddChild(Badge(LevelLabel(doc), new Color(0.05f, 0.05f, 0.08f, 0.78f), Colors.White));
        if (entry.BuiltIn) badges.AddChild(Badge("Built-in", new Color(0.16f, 0.18f, 0.26f, 0.92f), DetailColor));
        if (entry.OverridesBuiltIn) badges.AddChild(Badge("Edited copy", new Color(0.13f, 0.30f, 0.34f, 0.95f), DetailColor));
        if (unfinished) badges.AddChild(Badge("Unfinished", new Color(0.42f, 0.27f, 0.05f, 0.95f), WarnColor));

        words.AddChild(new Control { CustomMinimumSize = new Vector2(0, 3), MouseFilter = Control.MouseFilterEnum.Ignore });

        // the name: bold, and allowed a second line before it gives up with an ellipsis
        var title = new Label
        {
            Text = name,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        title.AddThemeFontOverride("font", _boldFont);
        title.AddThemeFontSizeOverride("font_size", 18);
        // (no line_spacing override: a negative one makes Label count one visible line too few)
        Shadow(title);
        words.AddChild(title);

        words.AddChild(TextLine(GoalsLabel(doc) + "  ·  " + TerrainLabel(doc.Terrain), 12, DetailColor));
        words.AddChild(TextLine(BattlefieldDims(doc) + "  ·  edited " + FormatModified(doc.ModifiedUtc), 12, FaintColor));
        if (unfinished)
            words.AddChild(TextLine("Needs: " + string.Join(", ", entry.Problems), 12, WarnColor));

        // -- footer: one clear primary action, the rest behind a small menu
        var footer = new MarginContainer();
        footer.AddThemeConstantOverride("margin_left", 8);
        footer.AddThemeConstantOverride("margin_right", 8);
        footer.AddThemeConstantOverride("margin_top", 6);
        footer.AddThemeConstantOverride("margin_bottom", 6);
        column.AddChild(footer);
        entry.Footer = footer;

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        footer.AddChild(actions);

        Action primary = entry.BuiltIn ? () => CreateOverrideAndOpen(doc) : () => OpenMap(doc.Id);
        var primaryBtn = new Button
        {
            Text = entry.BuiltIn ? "Edit Copy" : "Open",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = entry.BuiltIn
                ? "Built-in map: editing saves a local copy with the same id, which replaces it in play"
                : "Open in the map editor",
        };
        primaryBtn.AddThemeFontSizeOverride("font_size", 13);
        primaryBtn.Pressed += primary;
        actions.AddChild(primaryBtn);

        var more = new MenuButton { Text = "⋯", Flat = false, CustomMinimumSize = new Vector2(34, 0), TooltipText = "More actions" };
        PopupMenu menu = more.GetPopup();
        if (!entry.BuiltIn) menu.AddItem("Rename", 0);
        menu.AddItem(entry.BuiltIn ? "Duplicate as a new map" : "Duplicate", 1);
        if (!entry.BuiltIn) menu.AddItem(entry.OverridesBuiltIn ? "Delete copy (restore built-in)" : "Delete", 2);
        string path = entry.Path;
        menu.IdPressed += id =>
        {
            if (id == 0) OnRenamePressed(doc, path);
            else if (id == 1) OnDuplicatePressed(path);
            else if (id == 2) OnDeletePressed(doc, path);
        };
        actions.AddChild(more);

        // double-clicking the picture is the primary action too
        card.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left }) primary();
        };
        return card;
    }

    private static StyleBoxFlat CardFrame(Color edge)
    {
        var frame = new StyleBoxFlat { BgColor = CardColor, BorderColor = edge };
        frame.SetCornerRadiusAll(8);
        frame.SetBorderWidthAll(2);
        frame.SetContentMarginAll(2);
        return frame;
    }

    /// <summary>Everything a card says, unclipped, for the moments a name or a list of
    /// missing pieces does not fit.</summary>
    private static string CardTooltip(Entry entry, string name)
    {
        MapDocument doc = entry.Doc;
        var lines = new List<string>
        {
            name,
            (entry.RealmName ?? "No realm") + "  ·  " + LevelLabel(doc) + "  ·  " + GoalsLabel(doc) + "  ·  " + TerrainLabel(doc.Terrain),
            BattlefieldDims(doc) + " cells  ·  edited " + FormatModified(doc.ModifiedUtc),
        };
        if (entry.BuiltIn) lines.Add("Built-in map — Edit Copy saves your own version, which replaces it in play.");
        else if (entry.OverridesBuiltIn) lines.Add("Your edited copy of a built-in map.");
        if (entry.Problems.Count > 0)
            lines.Add("Unfinished — needs: " + string.Join(", ", entry.Problems) + ". It is never used in a run.");
        return string.Join("\n", lines);
    }

    /// <summary>One line of card text. It clips with an ellipsis and has no minimum width, so
    /// it can never widen its card — the reason the old wall outgrew the window.</summary>
    private static Label TextLine(string text, int size, Color color)
    {
        var label = new Label
        {
            Text = text,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        Shadow(label);
        return label;
    }

    private static void Shadow(Label label)
    {
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    private Control Badge(string text, Color background, Color foreground)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = new StyleBoxFlat { BgColor = background };
        box.SetCornerRadiusAll(5);
        box.ContentMarginLeft = 7;
        box.ContentMarginRight = 7;
        box.ContentMarginTop = 1;
        box.ContentMarginBottom = 2;
        chip.AddThemeStyleboxOverride("panel", box);

        var label = new Label { Text = text.ToUpperInvariant(), MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", _boldFont);
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", foreground);
        chip.AddChild(label);
        return chip;
    }

    /// <summary>A realm chip: a pill tinted with its realm's colour, filled when chosen and
    /// dimmed when it would show nothing.</summary>
    private static void StyleRealmChip(RealmChip chip, bool chosen, bool empty)
    {
        Color colour = chip.Color;
        StyleBoxFlat Pill(Color fill, Color border, int width)
        {
            var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
            box.SetCornerRadiusAll(15);
            box.SetBorderWidthAll(width);
            box.ContentMarginLeft = 13;
            box.ContentMarginRight = 13;
            box.ContentMarginTop = 3;
            box.ContentMarginBottom = 4;
            return box;
        }

        StyleBoxFlat rest = chosen
            ? Pill(colour.Darkened(0.18f), colour.Lightened(0.45f), 2)
            : Pill(Ink.Lerp(colour, 0.20f), Ink.Lerp(colour, empty ? 0.30f : 0.60f), 1);
        StyleBoxFlat hover = chosen
            ? Pill(colour.Darkened(0.08f), colour.Lightened(0.60f), 2)
            : Pill(Ink.Lerp(colour, 0.34f), colour, 1);

        Button button = chip.Button;
        button.AddThemeStyleboxOverride("normal", rest);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        Color ink = chosen ? Colors.White : (empty ? FaintColor.Darkened(0.25f) : colour.Lightened(0.55f));
        button.AddThemeColorOverride("font_color", ink);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", ink);
    }

    private static string LevelLabel(MapDocument doc)
    {
        if (doc.HardshipLevels is not { Count: > 0 }) return "All levels";
        var levels = new List<int>(doc.HardshipLevels);
        levels.Sort();
        return "LV " + string.Join(" · ", levels);
    }

    private static string GoalsLabel(MapDocument doc)
    {
        List<string> goals = doc.Goals ?? new List<string>();
        bool all = goals.Contains(CombatMapGoals.Claim) && goals.Contains(CombatMapGoals.Protect) &&
                   goals.Contains(CombatMapGoals.Destroy) && goals.Contains(CombatMapGoals.Slaughter);
        if (all) return "All goals";
        if (goals.Count == 0) return "Claim";
        var names = new List<string>();
        foreach (string goal in goals)
            names.Add(string.IsNullOrEmpty(goal) ? goal : char.ToUpperInvariant(goal[0]) + goal.Substring(1));
        return string.Join(", ", names);
    }

    private static string TerrainLabel(string terrain) => terrain switch
    {
        "*" => "any terrain",
        CombatMapTerrain.High => "high ground",
        CombatMapTerrain.Lowground => "low ground",
        _ => "sea level",
    };

    private static string BattlefieldDims(MapDocument doc)
    {
        MapLayerData field = MapDocumentQueries.Battlefield(doc);
        // The loader (MapJson.Validate) always injects a battlefield layer, so a miss
        // shouldn't happen in practice — fall back gracefully rather than crash.
        return field == null ? "—" : $"{field.GridW} × {field.GridH}";
    }

    private static string FormatModified(string modifiedUtc)
    {
        if (DateTime.TryParse(modifiedUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.ToString("yyyy-MM-dd");
        }
        return modifiedUtc ?? "";
    }
}

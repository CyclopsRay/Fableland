using System.Collections.Generic;
using Godot;
using Fableland.Items;
using Fableland.Run;

namespace Fableland.Shelter;

/// <summary>
/// The enchanting altar's screen (ITEMS.gdd §4.4, §7.1; NODES.gdd §5.0): every wonder item the
/// run owns — in the backpack or held — with Enchant on each one the Blessing of Shieldon may
/// take to (not itself, not Eternal, not already Possession: those say why instead). Enchanting
/// asks once, then spends the Blessing through <see cref="RunState.TryEnchantPossession"/>.
/// TODO(art): the altar's glyph and an enchanted item's mark. Buttons are named
/// (<c>Enchant_{instanceId}</c>, <c>Done</c>; Godot strips ":" from node names) so a check can press them.
/// </summary>
public partial class EnchantPanel : PanelContainer
{
    private ShelterVisit _visit;
    private ScrollContainer _scroll;
    private VBoxContainer _rows;
    private Label _news;

    /// <summary>Rows shown before the list scrolls, and the height each takes.</summary>
    private const int VisibleRows = 7;
    private const float RowPx = 36f;

    /// <summary>The dialog asking to spend the Blessing, while it is up (read-only, for checks).</summary>
    public ConfirmationDialog Asking { get; private set; }

    public static EnchantPanel Create(ShelterVisit visit)
    {
        var panel = new EnchantPanel { Name = "EnchantPanel", _visit = visit };
        panel.Build();
        return panel;
    }

    private void Build()
    {
        ShelterPanels.Frame(this, 660f);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 10);
        AddChild(layout);
        layout.AddChild(ShelterPanels.Title("ENCHANTING ALTAR"));
        layout.AddChild(ShelterPanels.Line(
            "Spend the Blessing of Shieldon to enchant Possession onto one wonder item: its passive then works " +
            "for the whole team, even from the backpack. The enchantment stays with that item."));
        // A long backpack scrolls inside the panel instead of running off the screen.
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(_rows);
        layout.AddChild(_scroll);
        _news = ShelterPanels.Line("");
        _news.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
        layout.AddChild(_news);
        Button done = ShelterPanels.Button("Done", "Done");
        done.Pressed += () => Visible = false;
        layout.AddChild(done);
        Rebuild();
    }

    /// <summary>Every owned item: (instance, definition, where it lies).</summary>
    private static List<(string InstanceId, string DefId, string Where)> Owned(RunState run)
    {
        var owned = new List<(string, string, string)>();
        foreach (ProtagonistState p in run.Owned)
            if (!string.IsNullOrEmpty(p.HeldItemDefId)) owned.Add((p.HeldItemInstanceId, p.HeldItemDefId, $"held by {p.Id}"));
        foreach (ItemInstance item in run.Items) owned.Add((item.InstanceId, item.DefId, "in the backpack"));
        return owned;
    }

    public void Rebuild()
    {
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        RunState run = _visit?.Run;
        ProtagonistState holder = run?.TeamHolderOf(RunState.BlessingOfShieldonId);
        if (holder == null)
        {
            _rows.AddChild(ShelterPanels.Line(run == null ? "No run." : "No one in the team holds the Blessing of Shieldon — equip it at the team board."));
            _scroll.CustomMinimumSize = new Vector2(0f, 2f * RowPx);
            return;
        }
        int shown = 0;
        foreach ((string instanceId, string defId, string where) in Owned(run))
        {
            if (instanceId == holder.HeldItemInstanceId) continue;   // the Blessing itself
            shown++;
        }
        _scroll.CustomMinimumSize = new Vector2(0f, Mathf.Clamp(shown, 1, VisibleRows) * RowPx);
        if (shown == 0) _rows.AddChild(ShelterPanels.Line("There is no other wonder item to enchant."));
        foreach ((string instanceId, string defId, string where) in Owned(run))
        {
            if (instanceId == holder.HeldItemInstanceId) continue;   // the Blessing itself
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var name = ShelterPanels.Line($"{ItemCatalog.DisplayName(defId)} — {where}", 16);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            string why = WhyNot(run, defId, instanceId);
            Button enchant = ShelterPanels.Button($"Enchant_{instanceId}", why == null ? "Enchant" : why);
            enchant.Disabled = why != null;
            string target = instanceId;
            enchant.Pressed += () => Ask(target, defId);
            row.AddChild(enchant);
            _rows.AddChild(row);
        }
    }

    /// <summary>Why the Blessing will not take to an item (null: it will).</summary>
    private static string WhyNot(RunState run, string defId, string instanceId)
    {
        if (run.Ledger.CanEnchant(defId, instanceId)) return null;
        if (ItemCatalog.TryGet(defId, out ItemDef def) && (def.Tags & ItemTags.Eternal) != 0) return "Eternal — it resists";
        return "Already works for the team";
    }

    private void Ask(string instanceId, string defId)
    {
        Asking?.QueueFree();
        Asking = new ConfirmationDialog
        {
            Title = "Enchanting altar",
            DialogText = $"Spend the Blessing of Shieldon to enchant {ItemCatalog.DisplayName(defId)} with Possession?\n\n" +
                         "Its passive will work for the whole team, even from the backpack. The Blessing is gone.",
        };
        AddChild(Asking);
        ConfirmationDialog asking = Asking;
        asking.Confirmed += () =>
        {
            asking.QueueFree();
            Asking = null;
            Enchant(instanceId, defId);
        };
        asking.Canceled += () => { asking.QueueFree(); Asking = null; };
        asking.PopupCentered();
    }

    private void Enchant(string instanceId, string defId)
    {
        RunState run = _visit?.Run;
        if (run == null) return;
        string line = run.TryEnchantPossession(instanceId, out string reason)
            ? $"{ItemCatalog.DisplayName(defId)} now works for the whole team, wherever it lies. The Blessing of Shieldon is spent."
            : reason ?? "The altar is cold.";
        if (_news != null) _news.Text = line;
        _visit.Shelter?.Say(line);
        Rebuild();
    }

    public string NewsText => _news?.Text ?? "";
}

/// <summary>The look the shelter's own panels share (Team Build's frame): a dark card with a
/// gold edge, centred over the room. Code-built, like the rest of the shelter (TODO(art)).</summary>
internal static class ShelterPanels
{
    public static void Frame(PanelContainer panel, float width)
    {
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.06f, 0.11f, 0.97f),
            BorderColor = new Color(0.78f, 0.68f, 0.39f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 18, ContentMarginBottom = 18,
        });
        panel.CustomMinimumSize = new Vector2(width, 0f);
        // Under the station prompt (which ends at 160 px), growing down — centred, it covered
        // the prompt's lower edge (the first capture).
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.KeepSize);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.End;
        panel.OffsetTop = TopPx;
    }

    /// <summary>Where a station's panel starts: below the prompt box.</summary>
    public const float TopPx = 172f;

    public static Label Title(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        return label;
    }

    public static Label Line(string text, int size = 15)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(0.86f, 0.87f, 0.92f));
        return label;
    }

    public static Button Button(string name, string text) => new() { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None };
}

using System.Collections.Generic;
using Godot;
using Fableland.Items;
using Fableland.Run;

namespace Fableland.Shelter;

/// <summary>
/// The plantation's screen (ITEMS.gdd §4.3, §5.4; NODES.gdd §5.0): the hub's plots — empty,
/// growing (days left) or ripe — with a button per plantable kind in the backpack to sow an
/// empty plot, and Harvest on a ripe one. Every action is a RunState verb
/// (<see cref="RunState.TryPlant"/>, <see cref="RunState.TryHarvest"/>); the panel only shows and
/// asks. Built in code like the rest of the shelter: TODO(art) — the plot cards and crop icons.
/// Buttons are named (<c>Plant{plot}_{defId}</c>, <c>Harvest{plot}</c>, <c>Done</c> — no ":", which
/// Godot strips from node names) so a check
/// can press them as a player would.
/// </summary>
public partial class PlantationPanel : PanelContainer
{
    private ShelterVisit _visit;
    private VBoxContainer _rows;
    private Label _news;

    /// <summary>The panel for this visit, ready for <c>ShelterController.ShowPanel</c>.</summary>
    public static PlantationPanel Create(ShelterVisit visit)
    {
        var panel = new PlantationPanel { Name = "PlantationPanel", _visit = visit };
        panel.Build();
        return panel;
    }

    private void Build()
    {
        ShelterPanels.Frame(this, 620f);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 10);
        AddChild(layout);
        layout.AddChild(ShelterPanels.Title("PLANTATION"));
        layout.AddChild(ShelterPanels.Line(
            "Sow a plantable item from the backpack; each day's end grows it. Harvest what is ripe. " +
            "If the VOID eats this hub, its plots go with it."));
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        layout.AddChild(_rows);
        _news = ShelterPanels.Line("");
        _news.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
        layout.AddChild(_news);
        Button done = ShelterPanels.Button("Done", "Done");
        done.Pressed += () => Visible = false;
        layout.AddChild(done);
        Rebuild();
    }

    /// <summary>Redraw the plots from the run (after every action).</summary>
    public void Rebuild()
    {
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        RunState run = _visit?.Run;
        if (run == null)
        {
            _rows.AddChild(ShelterPanels.Line("No run — nothing grows here."));
            return;
        }
        PlotState[] plots = run.Ledger.PlotsAt(_visit.NodeId);
        for (int i = 0; i < plots.Length; i++)
            _rows.AddChild(PlotRow(run, i, plots[i]));
    }

    private Control PlotRow(RunState run, int index, PlotState plot)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        string head = $"Plot {index + 1}: ";
        if (plot.Empty) head += "empty";
        else
        {
            (int _, string fruit, int count) = ItemLedger.Harvests[plot.DefId];
            string crop = ItemCatalog.DisplayName(plot.DefId);
            head += plot.Ripe
                ? $"{crop} — ripe: {count}× {ItemCatalog.DisplayName(fruit)}"
                : $"{crop} — ripe in {plot.DaysLeft} day{(plot.DaysLeft == 1 ? "" : "s")} ({count}× {ItemCatalog.DisplayName(fruit)})";
        }
        row.AddChild(ShelterPanels.Line(head, 17));

        var buttons = new HFlowContainer();
        buttons.AddThemeConstantOverride("h_separation", 8);
        if (plot.Empty)
        {
            // One button per plantable kind in the backpack; sowing takes the first of that kind.
            var kinds = new List<string>();
            foreach (ItemInstance item in run.Items)
                if (ItemLedger.IsPlantable(item.DefId) && !kinds.Contains(item.DefId)) kinds.Add(item.DefId);
            // An autowrapping Label inside a flow container shrinks to nothing and stacks its
            // letters (the first capture): the line goes in the row, at the panel's width.
            if (kinds.Count == 0) row.AddChild(ShelterPanels.Line("Nothing plantable in the backpack (a Rotten Mushroom, Pixolotl's Wish, Pome's Seed).", 14));
            foreach (string defId in kinds)
            {
                (int days, string fruit, int count) = ItemLedger.Harvests[defId];
                int held = run.Items.FindAll(item => item.DefId == defId).Count;
                Button sow = ShelterPanels.Button($"Plant{index}_{defId}",
                    $"Plant {ItemCatalog.DisplayName(defId)} (×{held}) — {days} days → {count}× {ItemCatalog.DisplayName(fruit)}");
                string kind = defId;
                sow.Pressed += () => Plant(index, kind);
                buttons.AddChild(sow);
            }
        }
        else if (plot.Ripe)
        {
            Button harvest = ShelterPanels.Button($"Harvest{index}", "Harvest");
            harvest.Pressed += () => Harvest(index);
            buttons.AddChild(harvest);
        }
        row.AddChild(buttons);
        return row;
    }

    private void Plant(int plot, string defId)
    {
        RunState run = _visit?.Run;
        if (run == null) return;
        ItemInstance first = run.Items.Find(item => item.DefId == defId);
        string reason = "There is none left in the backpack.";
        if (first != null && run.TryPlant(plot, first.InstanceId, out reason))
            Say($"{ItemCatalog.DisplayName(defId)} is planted in plot {plot + 1}.");
        else Say(reason ?? "It will not take root.");
        Rebuild();
    }

    private void Harvest(int plot)
    {
        RunState run = _visit?.Run;
        if (run == null) return;
        PlotState state = run.Ledger.PlotsAt(_visit.NodeId)[plot];
        string crop = state.DefId;
        if (run.TryHarvest(plot, out string reason))
        {
            (int _, string fruit, int count) = ItemLedger.Harvests[crop];
            Say($"Harvested {count}× {ItemCatalog.DisplayName(fruit)} — in the backpack now.");
        }
        else Say(reason ?? "Nothing is ripe there.");
        Rebuild();
    }

    private void Say(string text)
    {
        if (_news != null) _news.Text = text ?? "";
        _visit?.Shelter?.Say(text);
    }

    /// <summary>The panel's own news line (read-only, for checks).</summary>
    public string NewsText => _news?.Text ?? "";
}

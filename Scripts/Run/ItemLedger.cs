using System;
using System.Collections.Generic;
using Fableland.Items;
using Fableland.Map;

namespace Fableland.Run;

/// <summary>What a Weird Mushroom does to its eater the day after (ITEMS.gdd §7.13).</summary>
public enum MushroomOutcome
{
    None = 0,
    /// <summary>(a) max HP −40 pp for that day.</summary>
    Frail = 1,
    /// <summary>(b) left and right swapped for that day.</summary>
    Swapped = 2,
    /// <summary>(c) move speed ×4 for that day.</summary>
    Fleet = 3,
    /// <summary>(d) jump height ×4 for that day.</summary>
    Springy = 4,
    /// <summary>(e) +20 pp max HP, for good (applied when rolled).</summary>
    Hearty = 5,
}

/// <summary>One plot of a hub's plantation (ITEMS.gdd §4.3): what grows and how long it has left.</summary>
public sealed class PlotState
{
    public string DefId = "";
    public int DaysLeft;
    public bool Empty => string.IsNullOrEmpty(DefId);
    public bool Ripe => !Empty && DaysLeft <= 0;
}

/// <summary>
/// The wonder items' run state beyond an instance's identity and cooldowns (ITEMS.gdd §4, §7;
/// v0.12.8): how long a perishable item has left, which items carry an enchanted
/// <c>Possession</c>, the hubs' plantation plots, THE VOID's phantoms, and the Weird Mushroom's
/// next-day effects. Pure C#; one owner — <see cref="RunState"/> — writes it, through the verbs
/// below. Kept apart from RunState so the item layer is one file to read; saved as one optional
/// section (<see cref="ItemLedgerSaveData"/>): a save from before v0.12.8 loads an empty ledger.
/// </summary>
public sealed class ItemLedger
{
    // ── Numbers (ITEMS.gdd §7; first pass where the GDD is silent) ─────────────────────
    public const int WeirdMushroomPerishDays = 2;
    public const int PlotsPerHub = 2;
    public const int PhantomEveryDays = 4;
    /// <summary>A phantom spawns at least this many roads from you.</summary>
    public const int PhantomSpawnMinDistance = 3;
    /// <summary>Placeholder for the boss fight the GDD defers: what a phantom's touch costs.</summary>
    public const float PhantomTouchHpShare = 0.25f;
    public const int MushroomMaxHpPoints = 20;
    public const int MushroomFrailPoints = 40;
    public const float MushroomBoost = 4f;

    /// <summary>The harvest each plantable item gives (ITEMS.gdd §4.3, §7): days and fruit.</summary>
    public static readonly Dictionary<string, (int Days, string Fruit, int Count)> Harvests = new()
    {
        ["pome_seed"] = (7, "pome_bravery", 2),          // the GDD leaves its fruit TBD: v0.12.8 chose its parent, twice
        ["pixolotls_wish"] = (7, "pixolotls_feather", 2),
        ["rotten_mushroom"] = (4, "weird_mushroom", 4),
    };

    /// <summary>What a perishable item becomes when its days run out.</summary>
    public static readonly Dictionary<string, (int Days, string Becomes)> Perishes = new()
    {
        ["weird_mushroom"] = (WeirdMushroomPerishDays, "rotten_mushroom"),
    };

    /// <summary>Days left before a perishable instance rots (instance id → days).</summary>
    public readonly Dictionary<string, int> PerishDaysLeft = new(StringComparer.Ordinal);
    /// <summary>Instances the Blessing of Shieldon enchanted with Possession.</summary>
    public readonly HashSet<string> EnchantedPossession = new(StringComparer.Ordinal);
    /// <summary>Hub node id → its plots.</summary>
    public readonly Dictionary<string, PlotState[]> Plots = new(StringComparer.Ordinal);
    /// <summary>The node each VOID phantom stands on.</summary>
    public readonly List<string> Phantoms = new();
    /// <summary>Days until THE VOID's next phantom (counted only while THE VOID is owned).</summary>
    public int PhantomClock = PhantomEveryDays;
    /// <summary>Protagonists who ate a Weird Mushroom today: their outcome rolls at day end.</summary>
    public readonly HashSet<string> MushroomEaten = new(StringComparer.Ordinal);
    /// <summary>Today's mushroom effect per protagonist (cleared at the next day end).</summary>
    public readonly Dictionary<string, MushroomOutcome> MushroomToday = new(StringComparer.Ordinal);

    // ── Perishing ────────────────────────────────────────────────────────────────────

    /// <summary>Start a perishable instance's clock (called when it is created).</summary>
    public void OnCreated(string defId, string instanceId)
    {
        if (!string.IsNullOrEmpty(instanceId) && Perishes.TryGetValue(defId ?? "", out var perish))
            PerishDaysLeft[instanceId] = perish.Days;
    }

    /// <summary>One day end: every perishable instance — held or in the backpack — loses a day;
    /// at zero it turns into its rotten form (<paramref name="convert"/>, by instance id). Returns
    /// the notes for the day summary.</summary>
    public List<string> TickPerish(Func<string, string> defIdOf, Action<string, string> convert)
    {
        var notes = new List<string>();
        var ids = new List<string>(PerishDaysLeft.Keys);
        ids.Sort(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            string defId = defIdOf(id);
            if (defId == null || !Perishes.TryGetValue(defId, out var perish)) { PerishDaysLeft.Remove(id); continue; }
            int left = PerishDaysLeft[id] - 1;
            if (left > 0) { PerishDaysLeft[id] = left; continue; }
            PerishDaysLeft.Remove(id);
            convert(id, perish.Becomes);
            notes.Add($"{ItemCatalog.DisplayName(defId)} went bad: it is {ItemCatalog.DisplayName(perish.Becomes)} now.");
        }
        return notes;
    }

    // ── Possession ───────────────────────────────────────────────────────────────────

    /// <summary>Whether an instance's passive works party-wide from anywhere (ITEMS.gdd §4.4):
    /// its catalog says so, or the Blessing of Shieldon enchanted it.</summary>
    public bool HasPossession(string defId, string instanceId) =>
        (ItemCatalog.TryGet(defId, out ItemDef def) && (def.Tags & ItemTags.Possession) != 0)
        || (!string.IsNullOrEmpty(instanceId) && EnchantedPossession.Contains(instanceId));

    /// <summary>Whether the Blessing of Shieldon may enchant this item: not itself, not Eternal,
    /// not already Possession.</summary>
    public bool CanEnchant(string defId, string instanceId) =>
        ItemCatalog.TryGet(defId, out ItemDef def) && def.Id != "blessing_of_shieldon"
        && (def.Tags & ItemTags.Eternal) == 0 && !HasPossession(defId, instanceId);

    // ── The plantation ──────────────────────────────────────────────────────────────

    public PlotState[] PlotsAt(string hubId)
    {
        if (string.IsNullOrEmpty(hubId)) return Array.Empty<PlotState>();
        if (!Plots.TryGetValue(hubId, out PlotState[] plots))
        {
            plots = new PlotState[PlotsPerHub];
            for (int i = 0; i < plots.Length; i++) plots[i] = new PlotState();
            Plots[hubId] = plots;
        }
        return plots;
    }

    public static bool IsPlantable(string defId) => defId != null && Harvests.ContainsKey(defId);

    /// <summary>One day end: every plot grows a day; a hub the VOID ate loses its plots.</summary>
    public List<string> TickPlots(Func<string, bool> devoured)
    {
        var notes = new List<string>();
        var hubs = new List<string>(Plots.Keys);
        hubs.Sort(StringComparer.Ordinal);
        foreach (string hub in hubs)
        {
            PlotState[] plots = Plots[hub];
            if (devoured(hub))
            {
                foreach (PlotState plot in plots)
                    if (!plot.Empty) notes.Add($"The VOID ate the {ItemCatalog.DisplayName(plot.DefId)} growing at {hub}.");
                Plots.Remove(hub);
                continue;
            }
            foreach (PlotState plot in plots)
            {
                if (plot.Empty || plot.DaysLeft <= 0) continue;
                plot.DaysLeft--;
                if (plot.DaysLeft == 0) notes.Add($"The {ItemCatalog.DisplayName(plot.DefId)} at {hub} is ready to harvest.");
            }
        }
        return notes;
    }

    // ── THE VOID's phantoms ─────────────────────────────────────────────────────────

    /// <summary>One day end while THE VOID is owned: every <see cref="PhantomEveryDays"/> days a
    /// phantom rises on a node at least <see cref="PhantomSpawnMinDistance"/> roads from you.</summary>
    public string TickPhantomClock(MapGraph graph, string playerNodeId, DetRandom rng)
    {
        if (--PhantomClock > 0) return null;
        PhantomClock = PhantomEveryDays;
        MapNode from = Find(graph, playerNodeId);
        if (from == null) return null;
        Dictionary<MapNode, int> distance = Distances(graph, from);
        var candidates = new List<MapNode>();
        foreach ((MapNode node, int d) in distance)
            if (d >= PhantomSpawnMinDistance && !node.Devoured && !Phantoms.Contains(node.Id)) candidates.Add(node);
        if (candidates.Count == 0) return null;
        candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        MapNode spawn = candidates[rng.Range(0, candidates.Count - 1)];
        Phantoms.Add(spawn.Id);
        return $"A VOID phantom rises at {spawn.Id}. It sees what you see.";
    }

    /// <summary>You challenged a new node: every phantom takes one road toward you. Returns how many
    /// reached you (each is gone after its touch).</summary>
    public int StepPhantoms(MapGraph graph, string playerNodeId)
    {
        MapNode target = Find(graph, playerNodeId);
        if (target == null || Phantoms.Count == 0) return 0;
        Dictionary<MapNode, int> toPlayer = Distances(graph, target);
        int touched = 0;
        for (int i = Phantoms.Count - 1; i >= 0; i--)
        {
            // Devoured ground still carries a phantom (it is the VOID's own); only a node gone
            // from the graph (a collapsed Eidolon Shelter) loses it.
            MapNode at = Find(graph, Phantoms[i]);
            if (at == null) { Phantoms.RemoveAt(i); continue; }
            MapNode next = at;
            if (toPlayer.TryGetValue(at, out int d) && d > 0)
                foreach (MapNode n in Neighbors(graph, at))
                    if (toPlayer.TryGetValue(n, out int nd) && nd < d && (next == at || string.CompareOrdinal(n.Id, next.Id) < 0)) next = n;
            if (next == target) { Phantoms.RemoveAt(i); touched++; }
            else Phantoms[i] = next.Id;
        }
        return touched;
    }

    private static MapNode Find(MapGraph graph, string id) =>
        graph == null || string.IsNullOrEmpty(id) ? null : graph.Nodes.Find(node => node.Id == id);

    private static IEnumerable<MapNode> Neighbors(MapGraph graph, MapNode node)
    {
        foreach (MapEdge edge in graph.EdgesOf(node)) yield return edge.Other(node);
    }

    /// <summary>Roads from a node to every node it reaches (breadth first; devoured nodes still
    /// carry a phantom — it is the VOID's own).</summary>
    public static Dictionary<MapNode, int> Distances(MapGraph graph, MapNode from)
    {
        var distance = new Dictionary<MapNode, int> { [from] = 0 };
        var queue = new Queue<MapNode>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            MapNode node = queue.Dequeue();
            foreach (MapNode next in Neighbors(graph, node))
                if (!distance.ContainsKey(next)) { distance[next] = distance[node] + 1; queue.Enqueue(next); }
        }
        return distance;
    }

    // ── The Weird Mushroom ───────────────────────────────────────────────────────────

    /// <summary>
    /// The next-day outcome for an eater (ITEMS.gdd §7.13): (a)–(e) 18 % each, (f) 10 %. Outcome
    /// (f), a second held slot, needs a slot that does not exist yet; until it does its 10 % is
    /// (e)'s (recorded in ITEMS.gdd).
    /// </summary>
    public static MushroomOutcome RollMushroom(DetRandom rng)
    {
        int roll = rng.Range(0, 99);
        if (roll < 18) return MushroomOutcome.Frail;
        if (roll < 36) return MushroomOutcome.Swapped;
        if (roll < 54) return MushroomOutcome.Fleet;
        if (roll < 72) return MushroomOutcome.Springy;
        return MushroomOutcome.Hearty;    // (e) 18 % + (f) 10 % until a second slot exists
    }

    public static string Describe(MushroomOutcome outcome) => outcome switch
    {
        MushroomOutcome.Frail => "feels hollow: max HP −40% for today",
        MushroomOutcome.Swapped => "sees the world backwards: left and right swap for today",
        MushroomOutcome.Fleet => "can't stop running: move speed ×4 for today",
        MushroomOutcome.Springy => "bounces: jumps ×4 as high today",
        MushroomOutcome.Hearty => "grows stronger: +20% max HP for good",
        _ => "",
    };

    // ── Save ─────────────────────────────────────────────────────────────────────────

    public ItemLedgerSaveData ToSave()
    {
        var save = new ItemLedgerSaveData { PhantomClock = PhantomClock };
        foreach ((string id, int days) in PerishDaysLeft) save.PerishDaysLeft[id] = days;
        save.EnchantedPossession.AddRange(EnchantedPossession);
        save.EnchantedPossession.Sort(StringComparer.Ordinal);
        foreach ((string hub, PlotState[] plots) in Plots)
            for (int i = 0; i < plots.Length; i++)
                if (!plots[i].Empty) save.Plots.Add(new PlotSaveData { HubId = hub, Index = i, DefId = plots[i].DefId, DaysLeft = plots[i].DaysLeft });
        save.Plots.Sort((a, b) => string.CompareOrdinal(a.HubId, b.HubId) != 0 ? string.CompareOrdinal(a.HubId, b.HubId) : a.Index.CompareTo(b.Index));
        save.Phantoms.AddRange(Phantoms);
        save.MushroomEaten.AddRange(MushroomEaten);
        save.MushroomEaten.Sort(StringComparer.Ordinal);
        foreach ((string id, MushroomOutcome outcome) in MushroomToday) save.MushroomToday[id] = (int)outcome;
        return save;
    }

    public static ItemLedger FromSave(ItemLedgerSaveData save)
    {
        var ledger = new ItemLedger();
        if (save == null) return ledger;
        ledger.PhantomClock = save.PhantomClock > 0 ? save.PhantomClock : PhantomEveryDays;
        if (save.PerishDaysLeft != null) foreach ((string id, int days) in save.PerishDaysLeft) ledger.PerishDaysLeft[id] = days;
        if (save.EnchantedPossession != null) foreach (string id in save.EnchantedPossession) ledger.EnchantedPossession.Add(id);
        if (save.Plots != null)
            foreach (PlotSaveData plot in save.Plots)
            {
                if (plot == null || plot.Index < 0 || plot.Index >= PlotsPerHub || !IsPlantable(plot.DefId)) continue;
                PlotState state = ledger.PlotsAt(plot.HubId)[plot.Index];
                state.DefId = plot.DefId;
                state.DaysLeft = Math.Max(0, plot.DaysLeft);
            }
        if (save.Phantoms != null) ledger.Phantoms.AddRange(save.Phantoms);
        if (save.MushroomEaten != null) foreach (string id in save.MushroomEaten) ledger.MushroomEaten.Add(id);
        if (save.MushroomToday != null)
            foreach ((string id, int outcome) in save.MushroomToday)
                if (Enum.IsDefined(typeof(MushroomOutcome), outcome)) ledger.MushroomToday[id] = (MushroomOutcome)outcome;
        return ledger;
    }
}

/// <summary>The item ledger's part of a run save (<c>RunSaveData.ItemLedger</c>, optional:
/// null in a save from before v0.12.8, which loads as an empty ledger — no version bump).</summary>
public sealed class ItemLedgerSaveData
{
    public Dictionary<string, int> PerishDaysLeft { get; set; } = new();
    public List<string> EnchantedPossession { get; set; } = new();
    public List<PlotSaveData> Plots { get; set; } = new();
    public List<string> Phantoms { get; set; } = new();
    public int PhantomClock { get; set; } = ItemLedger.PhantomEveryDays;
    public List<string> MushroomEaten { get; set; } = new();
    public Dictionary<string, int> MushroomToday { get; set; } = new();
}

public sealed class PlotSaveData
{
    public string HubId { get; set; } = "";
    public int Index { get; set; }
    public string DefId { get; set; } = "";
    public int DaysLeft { get; set; }
}

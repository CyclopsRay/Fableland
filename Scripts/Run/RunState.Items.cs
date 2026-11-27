using System;
using System.Collections.Generic;
using Godot;
using Fableland.Items;
using Fableland.Map;
using Fableland.Missions;

namespace Fableland.Run;

/// <summary>
/// The wonder items' run verbs (ITEMS.gdd §4, §7; v0.12.8): perishing, the plantation, the
/// Blessing's enchant, Pixolotl's Feather's rewind, the Weird Mushroom, THE VOID's phantoms and
/// the QiongFeng Emperor's Claw. Their state is the <see cref="ItemLedger"/>; this half of
/// RunState is the one writer of it, and the rest of RunState calls in at a handful of hooks
/// (a new run, save and load, an item created, the day's end, a node challenged, a capital won).
/// </summary>
public partial class RunState
{
    public const string ClawItemId = "qiongfeng_emperors_claw";
    public const string VoidItemId = "the_void";
    public const string BlessingOfShieldonId = "blessing_of_shieldon";
    public const string FeatherItemId = "pixolotls_feather";
    public const string WeirdMushroomId = "weird_mushroom";
    public const string DdItemId = "dd";
    /// <summary>The world whose capital yields the Claw on its first fall: the Palace of LOOING.</summary>
    public const string ClawWorldAbbr = "PL";

    /// <summary>The item layer's run state (perish clocks, enchantments, plots, phantoms, mushrooms).</summary>
    public ItemLedger Ledger { get; private set; } = new();

    /// <summary>Raised when a wonder core is picked up in a fight (DD's passive heals on it).</summary>
    public event Action CorePickedUp;

    /// <summary>The line the arena toasts when a phantom touched the party on the way in, or "".</summary>
    public string LastPhantomNote { get; private set; } = "";

    public void NotifyCorePickedUp() => CorePickedUp?.Invoke();

    // ── Instances, wherever they are ─────────────────────────────────────────────────

    /// <summary>The definition id of an instance in the backpack or a held slot, or null.</summary>
    public string ItemDefIdOf(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return null;
        ItemInstance inBag = Items.Find(item => item.InstanceId == instanceId);
        if (inBag != null) return inBag.DefId;
        foreach (ProtagonistState p in Owned)
            if (p.HeldItemInstanceId == instanceId && !string.IsNullOrEmpty(p.HeldItemDefId)) return p.HeldItemDefId;
        return null;
    }

    /// <summary>Whether the run has this item anywhere — backpack or a held slot.</summary>
    public bool OwnsItem(string defId)
    {
        if (Items.Exists(item => item.DefId == defId)) return true;
        foreach (ProtagonistState p in Owned) if (p.HeldItemDefId == defId) return true;
        return false;
    }

    /// <summary>The owned protagonist holding this item in the active team, or null.</summary>
    public ProtagonistState TeamHolderOf(string defId)
    {
        foreach (string id in ActiveBuild)
        {
            ProtagonistState p = FindProtagonist(id);
            if (p != null && p.HeldItemDefId == defId) return p;
        }
        return null;
    }

    /// <summary>Turn an instance into another item where it lies (ITEMS.gdd §4.2: the new item
    /// inherits the slot). Keeps its identity.</summary>
    public void ConvertItemInstance(string instanceId, string newDefId)
    {
        ItemInstance inBag = Items.Find(item => item.InstanceId == instanceId);
        if (inBag != null) inBag.DefId = newDefId;
        else
            foreach (ProtagonistState p in Owned)
                if (p.HeldItemInstanceId == instanceId) p.HeldItemDefId = newDefId;
        Ledger.OnCreated(newDefId, instanceId);
    }

    /// <summary>Remove an instance from the run, wherever it lies (eaten, set free, fainted).</summary>
    public bool RemoveItemInstance(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return false;
        int index = Items.FindIndex(item => item.InstanceId == instanceId);
        if (index >= 0)
        {
            Items.RemoveAt(index);
            Ledger.PerishDaysLeft.Remove(instanceId);
            Ledger.EnchantedPossession.Remove(instanceId);
            return true;
        }
        foreach (ProtagonistState p in Owned)
        {
            if (p.HeldItemInstanceId != instanceId) continue;
            p.HeldItemDefId = null;
            p.HeldItemInstanceId = null;
            p.HeldItemFromBackpack = false;
            p.HeldItemDayCooldownRemaining = 0;
            p.HeldItemSecondCooldownRemaining = 0f;
            Ledger.PerishDaysLeft.Remove(instanceId);
            Ledger.EnchantedPossession.Remove(instanceId);
            return true;
        }
        return false;
    }

    /// <summary>An item left loose in a fight the player has left — DD — is set free and gone
    /// for good (ITEMS.gdd §7.3). Returns whether it was owned.</summary>
    public bool SetItemFree(string instanceId)
    {
        string defId = ItemDefIdOf(instanceId);
        if (defId == null || !RemoveItemInstance(instanceId)) return false;
        DayEndNotes.Add($"{ItemCatalog.DisplayName(defId)} was left behind in the fight — it is free now.");
        return true;
    }

    /// <summary>Every owned item with a Possession passive (catalog or enchanted) other than the
    /// one <paramref name="exceptInstanceId"/>: what the arena puts on the body in play as
    /// passive-only runtimes (ITEMS.gdd §4.4: party-wide, from the backpack too).</summary>
    public List<(string DefId, string InstanceId)> PossessionItems(string exceptInstanceId)
    {
        var found = new List<(string, string)>();
        void Consider(string defId, string instanceId)
        {
            if (string.IsNullOrEmpty(defId) || instanceId == exceptInstanceId) return;
            if (Ledger.HasPossession(defId, instanceId)) found.Add((defId, instanceId));
        }
        foreach (ItemInstance item in Items) Consider(item.DefId, item.InstanceId);
        foreach (ProtagonistState p in Owned) Consider(p.HeldItemDefId, p.HeldItemInstanceId);
        return found;
    }

    // ── The day's end (DayEndPipeline's item step) ──────────────────────────────────

    /// <summary>Perishing, growing, the mushroom's morning after and THE VOID's clock — one day.</summary>
    public void RunItemDay()
    {
        DayEndNotes.AddRange(Ledger.TickPerish(ItemDefIdOf, ConvertItemInstance));
        DayEndNotes.AddRange(Ledger.TickPlots(id => FindNode(id)?.Devoured ?? true));

        // The Weird Mushroom: yesterday's effects end; today's eaters learn their fate.
        Ledger.MushroomToday.Clear();
        var eaters = new List<string>(Ledger.MushroomEaten);
        eaters.Sort(StringComparer.Ordinal);
        foreach (string id in eaters)
        {
            ProtagonistState p = FindProtagonist(id);
            if (p == null) continue;
            MushroomOutcome outcome = ItemLedger.RollMushroom(Rng.Sub($"mushroom:{Day}:{id}"));
            if (outcome == MushroomOutcome.Hearty) p.MaxHpPercentPoints += ItemLedger.MushroomMaxHpPoints;
            else Ledger.MushroomToday[id] = outcome;
            DayEndNotes.Add($"{id} {ItemLedger.Describe(outcome)} (the Weird Mushroom).");
        }
        Ledger.MushroomEaten.Clear();

        if (OwnsItem(VoidItemId))
        {
            string risen = Ledger.TickPhantomClock(Graph, CurrentNodeId, Rng.Sub($"void-phantom:{Day}"));
            if (risen != null) DayEndNotes.Add(risen);
        }
    }

    /// <summary>The player challenges a new combat node: every VOID phantom takes a road toward
    /// them (ITEMS.gdd §7.2). A phantom that arrives touches the party — until its boss fight
    /// is designed, a quarter of every fighter's HP (placeholder, ITEMS.gdd).</summary>
    internal void OnChallengeNode(MapNode node)
    {
        LastPhantomNote = "";
        if (node == null || !node.IsCombat || CompletedNodeIds.Contains(node.Id) || Ledger.Phantoms.Count == 0) return;
        int touched = Ledger.StepPhantoms(Graph, node.Id);
        if (touched <= 0) return;
        HurtParty(ItemLedger.PhantomTouchHpShare * touched);
        LastPhantomNote = touched == 1 ? "A VOID phantom caught up with you on the way in."
            : $"{touched} VOID phantoms caught up with you on the way in.";
        DayEndNotes.Add(LastPhantomNote);
    }

    // ── The Weird Mushroom ───────────────────────────────────────────────────────────

    /// <summary>Today's Weird Mushroom effect on a protagonist (None without one).</summary>
    public MushroomOutcome MushroomToday(string protagonistId) =>
        protagonistId != null && Ledger.MushroomToday.TryGetValue(protagonistId, out MushroomOutcome outcome) ? outcome : MushroomOutcome.None;

    /// <summary>The max-HP percentage points a body is hydrated with today: the permanent pool,
    /// less a Weird Mushroom's one-day −40 (ITEMS.gdd §7.13 (a)) — never below −90.</summary>
    public int EffectiveMaxHpPoints(ProtagonistState p)
    {
        if (p == null) return 0;
        int points = p.MaxHpPercentPoints;
        if (MushroomToday(p.Id) == MushroomOutcome.Frail) points -= ItemLedger.MushroomFrailPoints;
        return Math.Max(-90, points);
    }

    /// <summary>Eat the held Weird Mushroom (its single-use skill): +20 pp max HP for good, and a
    /// consequence the next day. The live body is grown by the caller from the returned points.</summary>
    public bool TryEatMushroom(ProtagonistState p, out string reason)
    {
        reason = null;
        if (p == null || p.HeldItemDefId != WeirdMushroomId) { reason = "No Weird Mushroom is held."; return false; }
        p.MaxHpPercentPoints += ItemLedger.MushroomMaxHpPoints;
        Ledger.MushroomEaten.Add(p.Id);
        RemoveItemInstance(p.HeldItemInstanceId);
        return true;
    }

    // ── Pixolotl's Feather ───────────────────────────────────────────────────────────

    /// <summary>Whether the Feather's rewind can be used now: a team member holds it and the day
    /// has a start to return to.</summary>
    public bool CanRewindToday(out string reason)
    {
        reason = null;
        if (TeamHolderOf(FeatherItemId) == null) { reason = "A fighter in the team must hold Pixolotl's Feather."; return false; }
        if (_lastDayCheckpoint == null) { reason = "There is no start of the day to return to."; return false; }
        return true;
    }

    /// <summary>
    /// Pixolotl's Feather (ITEMS.gdd §7.8): flash back to the start of today — the safest
    /// reading of its design note: where you stood, every fighter's HP and the stamina return
    /// to this morning's; the day counter does not (time only flows forward for the VOID), nor
    /// does anything won. Then the Feather becomes Pixolotl's Wish.
    /// </summary>
    public bool TryRewindToday(out string reason)
    {
        if (!CanRewindToday(out reason)) return false;
        RunSaveData morning = _lastDayCheckpoint;
        if (FindNode(morning.CurrentNodeId) is { Devoured: false })
        {
            CurrentNodeId = morning.CurrentNodeId;
            PreviousNodeId = morning.PreviousNodeId;
        }
        Stamina = Math.Clamp(morning.Stamina, 0, MaxStamina);
        foreach (ProtagonistSaveData then in morning.Owned)
            if (FindProtagonist(then.Id) is { } p) p.HpRatio = Mathf.Clamp(then.HpRatio, 0f, 1f);
        ProtagonistState holder = TeamHolderOf(FeatherItemId);
        ConvertItemInstance(holder.HeldItemInstanceId, "pixolotls_wish");
        return true;
    }

    // ── The Blessing of Shieldon ─────────────────────────────────────────────────────

    /// <summary>The Blessing of Shieldon's single use (ITEMS.gdd §7.1): enchant Possession onto a
    /// wonder item — its passive then works for the whole team, from the backpack too. The
    /// Blessing must be held by a team member and is consumed.</summary>
    public bool TryEnchantPossession(string targetInstanceId, out string reason)
    {
        reason = null;
        ProtagonistState holder = TeamHolderOf(BlessingOfShieldonId);
        if (holder == null) { reason = "A fighter in the team must hold the Blessing of Shieldon."; return false; }
        string target = ItemDefIdOf(targetInstanceId);
        if (target == null || targetInstanceId == holder.HeldItemInstanceId) { reason = "Choose another wonder item."; return false; }
        if (!Ledger.CanEnchant(target, targetInstanceId)) { reason = $"{ItemCatalog.DisplayName(target)} cannot take the enchantment."; return false; }
        Ledger.EnchantedPossession.Add(targetInstanceId);
        RemoveItemInstance(holder.HeldItemInstanceId);
        return true;
    }

    // ── The plantation ──────────────────────────────────────────────────────────────

    /// <summary>Plant a plantable backpack item in a plot of the hub you stand on (ITEMS.gdd §4.3,
    /// §5.4): it is consumed and grows for its harvest time.</summary>
    public bool TryPlant(int plotIndex, string instanceId, out string reason)
    {
        reason = null;
        MapNode hub = FindNode(CurrentNodeId);
        if (hub == null || hub.Kind != NodeKind.TransportHub || hub.Devoured) { reason = "Plant at a Transportation Hub."; return false; }
        PlotState[] plots = Ledger.PlotsAt(hub.Id);
        if (plotIndex < 0 || plotIndex >= plots.Length || !plots[plotIndex].Empty) { reason = "That plot is not free."; return false; }
        ItemInstance item = Items.Find(it => it.InstanceId == instanceId);
        if (item == null || !ItemLedger.IsPlantable(item.DefId)) { reason = "Only a plantable item from the backpack can be planted."; return false; }
        plots[plotIndex].DefId = item.DefId;
        plots[plotIndex].DaysLeft = ItemLedger.Harvests[item.DefId].Days;
        RemoveItemInstance(instanceId);
        return true;
    }

    /// <summary>Harvest a ripe plot of the hub you stand on: its fruit goes to the backpack.</summary>
    public bool TryHarvest(int plotIndex, out string reason)
    {
        reason = null;
        MapNode hub = FindNode(CurrentNodeId);
        if (hub == null || hub.Kind != NodeKind.TransportHub) { reason = "Harvest at the hub where it grows."; return false; }
        PlotState[] plots = Ledger.PlotsAt(hub.Id);
        if (plotIndex < 0 || plotIndex >= plots.Length || !plots[plotIndex].Ripe) { reason = "Nothing is ripe there."; return false; }
        (int _, string fruit, int count) = ItemLedger.Harvests[plots[plotIndex].DefId];
        for (int i = 0; i < count; i++) AddItem(fruit);
        plots[plotIndex].DefId = "";
        plots[plotIndex].DaysLeft = 0;
        return true;
    }

    // ── A capital won: the Claw ─────────────────────────────────────────────────────

    /// <summary>A realm's LV4 capital fell. With the QiongFeng Emperor's Claw held in the team
    /// (or enchanted with Possession), it fires (ITEMS.gdd §7.11); the Palace of LOOING's first
    /// fall yields a Claw.</summary>
    internal void OnCapitalWon(MapNode node, bool firstTime)
    {
        if (!firstTime || node == null || node.Kind != NodeKind.Boss || node.Level != 4 || node.WorldIndex < 0) return;
        bool clawReady = TeamHolderOf(ClawItemId) != null;
        if (!clawReady)
            foreach ((string defId, string _) in PossessionItems(null))
                if (defId == ClawItemId) clawReady = true;
        if (clawReady) UnleashClaw(node);
        if (node.Zone == ClawWorldAbbr && !OwnsItem(ClawItemId))
        {
            AddItem(ClawItemId);
            DayEndNotes.Add("The Palace of LOOING yields the QiongFeng Emperor's Claw.");
            LastRewardLine = LastRewardLine.Length > 0 ? LastRewardLine + " · " + ItemCatalog.DisplayName(ClawItemId) : ItemCatalog.DisplayName(ClawItemId);
        }
    }

    /// <summary>
    /// The QiongFeng Emperor's Claw (ITEMS.gdd §7.11, its design note): every combat node of the
    /// capital's realm still unconquered — its "unopened chests" — pays its goal's reward now;
    /// the realm cracks down (every node of it is devoured); the claw faints (it is removed);
    /// and you are thrown to a Transportation Hub of another realm, chosen from the run's own
    /// stream — one you have visited if there is one. With nowhere to be thrown the realm holds
    /// (cracking it would end the run under your feet). Returns what it gave.
    /// </summary>
    public string UnleashClaw(MapNode capital)
    {
        string realm = capital.Zone;
        MapNode landing = ClawLanding(capital);
        var loot = new RewardBundle();
        int chests = 0;
        foreach (MapNode node in Graph.Nodes)
        {
            if (node.Zone != realm || node.Devoured || !node.IsCombat || node == capital || CompletedNodeIds.Contains(node.Id)) continue;
            chests++;
            CompletedNodeIds.Add(node.Id);
            switch (EffectiveMission(node))
            {
                case MissionType.Collection: loot.WonderCores += MissionTable.CollectionCores(node.Level); break;
                case MissionType.Protect: loot.WonderCores += 10; loot.ItemDefIds.Add("fanchens_heart"); break;
                case MissionType.Destroy: loot.ItemDefIds.Add("forgotten_kashaya"); break;
                case MissionType.Slaughter: loot.AtkBonus += 10; break;
            }
        }
        ApplyRewards(loot);

        if (landing != null)
        {
            // The throw, then the crack: nothing of the realm can be entered again.
            CurrentNodeId = landing.Id;
            PreviousNodeId = landing.Id;
            MarkNodeVisited(landing.Id);
            foreach (MapNode node in Graph.Nodes)
                if (node.Zone == realm && node.WorldIndex >= 0) node.Devoured = true;
            if (Graph.BreakRealityBridgesAtDevouredEndpoints() > 0) RebuildNodeIndex();
        }

        ProtagonistState holder = TeamHolderOf(ClawItemId);
        if (holder != null) RemoveItemInstance(holder.HeldItemInstanceId);
        else
            foreach ((string defId, string instanceId) in PossessionItems(null))
                if (defId == ClawItemId) { RemoveItemInstance(instanceId); break; }

        string gave = loot.Describe();
        string crack = landing != null ? $"the realm cracks down and throws you to {landing.Id}" : "the realm holds (there is nowhere to throw you)";
        string line = $"The QiongFeng Emperor's Claw tears open {chests} unopened chest{(chests == 1 ? "" : "s")} of the realm ({(gave.Length > 0 ? gave : "nothing")}); {crack}; the claw faints.";
        DayEndNotes.Add(line);
        LastRewardLine = LastRewardLine.Length > 0 ? LastRewardLine + " · " + line : line;
        return line;
    }

    /// <summary>Where the Claw throws you (ITEMS.gdd §7.11: "a random valid location", from an
    /// explicit set): a Transportation Hub of another outer realm the VOID has not eaten — one you
    /// have visited when there is one — picked from the run's own stream. Null: nowhere.</summary>
    public MapNode ClawLanding(MapNode capital)
    {
        if (capital == null || Graph == null) return null;
        var hubs = new List<MapNode>();
        foreach (MapNode node in Graph.Nodes)
            if (node.Kind == NodeKind.TransportHub && !node.Devoured && node.WorldIndex >= 0 && node.Zone != capital.Zone) hubs.Add(node);
        List<MapNode> visited = hubs.FindAll(node => VisitedNodeIds.Contains(node.Id));
        List<MapNode> pool = visited.Count > 0 ? visited : hubs;
        if (pool.Count == 0) return null;
        pool.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return Rng.Sub("claw:" + capital.Id).Pick(pool);
    }
}

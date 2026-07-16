using System;
using System.Collections.Generic;
using Godot;
using Fableland.Data;
using Fableland.Map;
using Fableland.MapCreation.Runtime;
using Fableland.Debug;

namespace Fableland.Run;

/// <summary>How a run ended (drives the RunOver screen).</summary>
public enum RunEndKind { Death, VoidDevoured, BossTimer, Victory }

/// <summary>
/// The run-state autoload — the single source of truth for everything that spans an entire
/// run (T30 §1). It holds DATA and SWAPS SCENES; it does <b>no</b> scene-node lookups and owns
/// no view logic. The Exploration view (MapController) and every Adventure scene read/report
/// through this; nothing reaches in to mutate a foe/stamina directly (T10 §3).
///
/// <b>Null-tolerance rule:</b> Instance always exists (autoload), but a run may not have started
/// (fresh F5 on any scene). Consumers must treat <see cref="CurrentAdventure"/> == null and an
/// unstarted run (Graph == null) as debug defaults, never a crash.
/// </summary>
public partial class RunState : Node
{
    public static RunState Instance;
    public const int MaxStamina = 5;
    public const string TwistedRealityItemId = "twisted_reality";

    // ---- identity / clock ----
    public string Seed;
    public DetRandom Rng;                 // root stream; subsystems derive via Rng.Sub("tag")
    public int Day;
    public bool InVoid;                   // past the singularity ⇒ HUD shows "???"
    public int Stamina;

    // ---- map ----
    public MapGraph Graph;                // node.Devoured is the live devour flag
    public string CurrentNodeId;
    public string PreviousNodeId;         // node moved FROM (combat-failure bounce, NODES §2.3)
    public readonly HashSet<string> VisitedNodeIds = new();
    public readonly HashSet<string> CompletedNodeIds = new(); // combat goals achieved / events resolved
    public readonly HashSet<string> MundaneShelterIds = new();// shelters whose Blessing was consumed
    public readonly HashSet<string> ResolvedEventIds = new(); // "?" nodes whose event is done

    // ---- party & inventory (stubs beyond the party for now) ----
    public readonly List<ProtagonistState> Owned = new();
    public readonly List<string> ActiveBuild = new();
    public int ActiveProtagonistIndex;   // chosen starting slot for the NEXT battle; Arena owns its local cursor
    public int WonderCores;
    public readonly List<ItemInstance> Items = new();

    // ---- run-performance counters (NODES §8) ----
    public int NodesTraversed;
    public int GoalsSucceeded;
    public int ProtagonistsCollected;
    public int ItemsCollected;
    private readonly HashSet<string> _worldsSet = new();      // distinct zones set foot in
    public int WorldsVisited => _worldsSet.Count;

    // ---- adventure handshake ----
    public AdventureContext CurrentAdventure;                 // null ⇒ debug-launched scene

    // ---- run lifecycle ----
    public bool RunFinished;                                  // guards double scene-swaps
    public RunEndKind LastEndKind;

    // ---- day-end summary (T30 §5 residual, v0.5.0) ----
    /// <summary>Scratch list day-end steps append a short note to (e.g. "The VOID devoured 1-B
    /// (14 nodes)."). One owner (DayEndPipeline steps write; EndDay reads+clears) — steps must
    /// NOT do UI themselves, they only describe what happened.</summary>
    public readonly List<string> DayEndNotes = new();
    /// <summary>Human-readable summary of the day that just ended, built by EndDay(). Read once by
    /// MapController on the next scene load (toast), then cleared so it only shows once.</summary>
    public string LastDayEndSummary = "";

    private readonly DayEndPipeline _pipeline = new();
    private Dictionary<string, MapNode> _nodeById;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    // ============================================================= run lifecycle

    /// <summary>Start a brand-new run: generate the map, reset everything, day 1, stamina full.</summary>
    public void NewRun(string seed)
    {
        seed = string.IsNullOrWhiteSpace(seed) ? DetRandom.NewSeed() : seed.Trim().ToUpperInvariant();
        Seed = seed;
        Rng = new DetRandom(seed);
        Graph = MapGenerator.Generate(seed);
        _nodeById = new Dictionary<string, MapNode>();
        foreach (var n in Graph.Nodes) _nodeById[n.Id] = n;

        Day = 1;
        Stamina = MaxStamina;
        InVoid = false;
        RunFinished = false;
        LastEndKind = RunEndKind.Death;
        CurrentAdventure = null;

        VisitedNodeIds.Clear();
        CompletedNodeIds.Clear();
        MundaneShelterIds.Clear();
        ResolvedEventIds.Clear();
        _worldsSet.Clear();

        Owned.Clear();
        Owned.Add(new ProtagonistState("Pomegraknight"));
        Owned.Add(new ProtagonistState("PumpKing"));
        // The vertical slice exposes every implemented protagonist through the real
        // bench so Team Build can exercise the complete switch flow. Cleopastar starts
        // benched; the default active pair remains Pomegraknight + PumpKing.
        Owned.Add(new ProtagonistState("Cleopastar"));
        ActiveBuild.Clear();
        ActiveBuild.Add("Pomegraknight");
        ActiveBuild.Add("PumpKing");
        ActiveProtagonistIndex = 0;
        WonderCores = 0;
        Items.Clear();

        NodesTraversed = 0;
        GoalsSucceeded = 0;
        ProtagonistsCollected = Owned.Count;
        ItemsCollected = 0;

        var start = Graph.StartNode;
        CurrentNodeId = start.Id;
        PreviousNodeId = start.Id;
        MarkVisited(start); // start node visited (NodesTraversed = 1, world revealed)
    }

    public MapNode FindNode(string id) =>
        id != null && _nodeById != null && _nodeById.TryGetValue(id, out var n) ? n : null;

    // ============================================================= handshake

    /// <summary>
    /// Snapshot the destination node into <see cref="CurrentAdventure"/> and swap to its scene
    /// (T10 §3). Marks the node visited on first entry. Combat entry drains ALL stamina
    /// (NODES §2.3) — one combat per day.
    /// </summary>
    public void BeginAdventure(string nodeId)
    {
        if (RunFinished) return;
        var node = FindNode(nodeId);
        // Computed BEFORE MarkVisited flips the visited flag: a re-attempt of a combat node we've
        // already been to but haven't conquered (NODES §1.3).
        bool revisitCombat = node != null && node.IsCombat
                             && VisitedNodeIds.Contains(nodeId) && !CompletedNodeIds.Contains(nodeId);

        if (node != null) MarkVisited(node);
        CurrentNodeId = nodeId;

        var kind = node?.Kind ?? NodeKind.Combat;
        var adventure = new AdventureContext
        {
            NodeId = nodeId,
            NodeLevel = node?.Level ?? 1,
            Mission = node?.Mission ?? MissionType.Collection,
            Kind = kind,
            Day = Day,
            IsRevisitCombat = revisitCombat,
            Terrain = node?.Terrain ?? "sea-level",
        };
        // The overworld selects an authored combat map once and snapshots only its path into
        // the handshake. The arena never reaches back into the map graph or rolls selection.
        if (node?.IsCombat == true)
        {
            var selected = CombatMapCatalog.Select(Seed, node.Id, node.Zone, node.Level,
                adventure.Mission, adventure.Terrain);
            adventure.CombatMapPath = selected?.AbsolutePath ?? "";
            if (selected == null)
                GD.PushWarning($"[RunState] no combat map matched {node.Zone} LV{node.Level} {adventure.Mission} {adventure.Terrain}; using legacy Arena.");
        }
        bool isCombat = kind == NodeKind.Combat || kind == NodeKind.Boss;
        if (isCombat) adventure.BattleTeam = CaptureBattleTeam();
        CurrentAdventure = adventure;
        if (isCombat) Stamina = 0; // entering any combat node depletes all stamina (NODES §2.3)

        string scene = kind switch
        {
            NodeKind.Combat or NodeKind.Boss => "res://Scenes/Arena.tscn",
            NodeKind.TransportHub => "res://Scenes/Shelter.tscn",
            NodeKind.Shelter => "res://Scenes/Shelter.tscn",
            NodeKind.Event => "res://Scenes/Event.tscn",
            _ => null, // River (and any future inert kind): no Adventure scene — stay on the map
        };
        if (scene == null) { CurrentAdventure = null; return; }
        GetTree().ChangeSceneToFile(scene);
    }

    /// <summary>
    /// An Adventure reports its outcome (T10 §3). Success: mark completed, apply rewards, bump
    /// counters. Combat failure: bounce the player back to the previous node with 0 stamina
    /// (NODES §2.3) — node stays incomplete and re-attemptable.
    /// </summary>
    public void ReportGoal(bool success, RewardBundle rewards)
    {
        var node = FindNode(CurrentAdventure?.NodeId);
        if (success)
        {
            if (node != null)
            {
                bool newlyDone = CompletedNodeIds.Add(node.Id);
                DebugManager.Instance?.LogMission($"Goal {CurrentAdventure?.Mission} at {CurrentAdventure?.NodeId} {(newlyDone ? "SUCCEEDED" : "re-completed")} (total goals: {GoalsSucceeded + (newlyDone ? 1 : 0)})");
                if (newlyDone && node.IsCombat) GoalsSucceeded++;
                if (newlyDone && node.Kind == NodeKind.Boss && node.WorldIndex == 0 && !HasTwistedReality())
                {
                    AddItem(TwistedRealityItemId);
                    DayEndNotes.Add("The capital yields TwistedReality.");
                }
            }
            ApplyRewards(rewards);
        }
        else if (node != null && node.IsCombat)
        {
            // Non-death combat failure: sent back to where they came from, 0 stamina.
            // Guard: after crossing the singularity the previous node may be a devoured
            // outer node — bouncing onto eaten ground would be an instant unfair death,
            // so stay put on the combat node instead (it exists, it's just unconquered).
            var prev = FindNode(PreviousNodeId);
            if (prev != null && !prev.Devoured) CurrentNodeId = PreviousNodeId;
            Stamina = 0;
        }
    }

    private void ApplyRewards(RewardBundle r)
    {
        if (r == null) return;
        DebugManager.Instance?.LogMission($"Rewards: cores={r.WonderCores} atk={r.AtkBonus} def={r.DefBonus} items={r.ItemDefIds?.Count ?? 0}");
        if (r.WonderCores != 0) WonderCores += r.WonderCores;
        foreach (var id in r.ItemDefIds) AddItem(id);
        if (r.AtkBonus != 0) AddAtk(r.AtkBonus);
        if (r.DefBonus != 0) AddDef(r.DefBonus);
        foreach (var pid in r.ProtagonistGrants) GrantProtagonist(pid);
    }

    // ============================================================= day end / run end

    /// <summary>
    /// Run the ordered day-end pipeline (T30 §5), then return to the map (unless the run ended).
    /// Builds <see cref="LastDayEndSummary"/> from the day label + whatever notes the steps left
    /// in <see cref="DayEndNotes"/> (one owner of the summary text: this method; steps only
    /// describe, never render).
    /// </summary>
    public void EndDay()
    {
        if (RunFinished) return;
        DayEndNotes.Clear();
        _pipeline.Run(this);
        if (!RunFinished)
        {
            string dayLabel = InVoid ? "???" : Day.ToString(); // hidden day still ticks; only the display is "???"
            var parts = new List<string> { $"Day {dayLabel} begins." };
            parts.AddRange(DayEndNotes);
            LastDayEndSummary = string.Join(" ", parts);
            ReturnToMap();
        }
    }

    public string DayEndOrder() => _pipeline.Order();

    /// <summary>Swap to the overworld map. No-op once the run has ended (avoids clobbering RunOver).</summary>
    public void ReturnToMap()
    {
        if (RunFinished) return;
        GetTree().ChangeSceneToFile("res://Scenes/Map.tscn");
    }

    /// <summary>End the run and swap to the run-over screen. Idempotent — first caller wins.</summary>
    public void EndRun(RunEndKind kind)
    {
        if (RunFinished) return;
        RunFinished = true;
        LastEndKind = kind;
        GetTree().ChangeSceneToFile("res://Scenes/RunOver.tscn");
    }

    /// <summary>
    /// Cross the zone-6 singularity (MapGDD §7): latch InVoid and devour the whole outer ring at
    /// once. The player is on a zone-6 node (safe); the day display becomes "???".
    /// </summary>
    public void EnterVoid()
    {
        if (Graph == null) return;
        InVoid = true;
        foreach (var n in Graph.Nodes)
            if (n.WorldIndex != -1) n.Devoured = true; // everything outside zone 6
        Graph.BreakRealityBridgesAtDevouredEndpoints();
    }

    // ============================================================= mutators (single owner of truth)

    /// <summary>Mark a node visited without starting an Adventure (inert destinations, e.g. the
    /// zone-6 River hub). No-op when already visited.</summary>
    public void MarkNodeVisited(string nodeId) => MarkVisited(FindNode(nodeId));

    private void MarkVisited(MapNode node)
    {
        if (node == null) return;
        if (VisitedNodeIds.Add(node.Id))
        {
            NodesTraversed++;
            if (node.WorldIndex >= 0) _worldsSet.Add(node.Zone);
            else if (node.WorldIndex == -1) _worldsSet.Add("XX");
        }
    }

    // shelter Blessing state
    public bool IsShelterBlessed(string shelterId) => !MundaneShelterIds.Contains(shelterId);
    public void ConsumeBlessing(string shelterId) { if (shelterId != null) MundaneShelterIds.Add(shelterId); }

    // "?" resolution
    public void ResolveEvent(string nodeId)
    {
        if (nodeId == null) return;
        ResolvedEventIds.Add(nodeId);
        CompletedNodeIds.Add(nodeId); // resolved ? is inert thereafter (NODES §1.3)
    }

    // Global stat Blessings (NODES §3.3: ATK/DEF/MaxHP are global — applied to every protagonist).
    public void AddAtk(int amount) { foreach (var p in Owned) p.BonusAtk += amount; DebugManager.Instance?.LogBuff("ATK", amount, Owned.Count > 0 ? Owned[0].BonusAtk : 0); }
    public void AddDef(int amount) { foreach (var p in Owned) p.BonusDef += amount; DebugManager.Instance?.LogBuff("DEF", amount, Owned.Count > 0 ? Owned[0].BonusDef : 0); }

    /// <summary>
    /// Rest Blessing (NODES §5.5): heal 30% max HP; 1/3 of any excess above 100% becomes a
    /// permanent additive max-HP percentage-point bump. Global across the party.
    /// </summary>
    public void RestBlessing()
    {
        DebugManager.Instance?.LogSystem("Rest Blessing applied to " + Owned.Count + " protagonists");
        foreach (var p in Owned)
        {
            float healed = p.HpRatio + 0.30f;
            if (healed > 1f)
            {
                float excessPp = (healed - 1f) * 100f;      // e.g. full HP → 30 pp
                p.MaxHpPercentPoints += Mathf.RoundToInt(excessPp / 3f); // → +10 pp
            }
            p.HpRatio = Mathf.Min(1f, healed);
        }
    }

    /// <summary>Heal the party by a HP ratio, no excess conversion (used by "?" events).</summary>
    public void HealParty(float ratio)
    {
        DebugManager.Instance?.LogHeal(ratio * 100f, $"party heal {ratio*100f:F0}%");
        foreach (var p in Owned) p.HpRatio = Mathf.Min(1f, p.HpRatio + ratio);
    }

    public void AddWonderCores(int amount) { WonderCores += amount; DebugManager.Instance?.LogCores(amount, WonderCores); }

    public void AddItem(string defId)
    {
        Items.Add(new ItemInstance(defId));
        ItemsCollected++;
        DebugManager.Instance?.LogItem(defId, ItemsCollected);
    }

    /// <summary>Whether the unique cross-realm key is owned, either in a held slot or backpack.</summary>
    public bool HasTwistedReality()
    {
        foreach (var item in Items)
            if (item.DefId == TwistedRealityItemId) return true;
        foreach (var protagonist in Owned)
            if (protagonist.HeldItemDefId == TwistedRealityItemId) return true;
        return false;
    }

    /// <summary>Gets the held copy of TwistedReality. Its skill only works while equipped.</summary>
    private ProtagonistState HeldTwistedReality()
    {
        foreach (var protagonist in Owned)
            if (protagonist.HeldItemDefId == TwistedRealityItemId) return protagonist;
        return null;
    }

    /// <summary>Checks whether TwistedReality can create its next permanent bridge.</summary>
    public bool CanCreateRealityBridge(string originId, out string reason)
    {
        reason = null;
        var origin = FindNode(originId);
        var holder = HeldTwistedReality();
        if (Graph == null || origin == null) { reason = "No map destination is available."; return false; }
        if (holder == null) { reason = "Hold TwistedReality at a Transportation Hub first."; return false; }
        if (holder.HeldItemDayCooldownRemaining > 0)
        {
            reason = $"TwistedReality re-forms in {holder.HeldItemDayCooldownRemaining} day(s).";
            return false;
        }
        if (Stamina < MapGenTable.TwistedRealityStaminaCost) { reason = "Not enough stamina."; return false; }
        if (origin.WorldIndex < 0 || origin.Kind != NodeKind.Combat || origin.LevelTag != "1-A"
            || !CompletedNodeIds.Contains(origin.Id))
        {
            reason = "TwistedReality needs a completed 1-A city at a realm's edge.";
            return false;
        }
        if (Graph.HasRealityBridge(origin))
        {
            reason = "This city is already anchored to another reality.";
            return false;
        }
        if (FindClosestRealityDestination(origin) == null)
        {
            reason = "No eligible city remains in another realm.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Spend one stamina and create a permanent cross-realm edge. The caller can then travel
    /// through it using normal map movement, without a second item use.
    /// </summary>
    public bool TryCreateRealityBridge(string originId, out MapNode destination, out string reason)
    {
        destination = null;
        if (!CanCreateRealityBridge(originId, out reason)) return false;
        var origin = FindNode(originId);
        destination = FindClosestRealityDestination(origin);
        if (destination == null || !Graph.AddRealityBridge(origin, destination))
        {
            destination = null;
            reason = "Reality could not find a stable destination.";
            return false;
        }
        Stamina -= MapGenTable.TwistedRealityStaminaCost;
        HeldTwistedReality().HeldItemDayCooldownRemaining = MapGenTable.TwistedRealityCooldownDays;
        reason = null;
        return true;
    }

    private MapNode FindClosestRealityDestination(MapNode origin)
    {
        MapNode best = null;
        foreach (var candidate in Graph.Nodes)
        {
            if (candidate.WorldIndex < 0 || candidate.WorldIndex == origin.WorldIndex || candidate.Devoured
                || Graph.HasRealityBridge(candidate)) continue;
            if (best == null
                || candidate.Pos.DistanceSquaredTo(origin.Pos) < best.Pos.DistanceSquaredTo(origin.Pos)
                || (Mathf.IsEqualApprox(candidate.Pos.DistanceSquaredTo(origin.Pos), best.Pos.DistanceSquaredTo(origin.Pos))
                    && (candidate.WorldIndex < best.WorldIndex
                        || (candidate.WorldIndex == best.WorldIndex && string.CompareOrdinal(candidate.Id, best.Id) < 0))))
                best = candidate;
        }
        return best;
    }

    /// <summary>
    /// Move a wonder item into a protagonist's single held slot. The item instance's daily
    /// cooldown follows it; TwistedReality is the current gameplay user of that state. Signature takes a ProtagonistState by
    /// reference (not an id) so the shelter Team Build menu can assign to either a real Owned
    /// protagonist OR an ephemeral debug-only one that is not in Owned — RunState remains the one
    /// writer of the Items backpack either way. <paramref name="fromBackpack"/> is true for a real
    /// backpack item (consumed from Items here, and returned to Items on unhold/bump-out) and false
    /// for a debug Team-Build catalog item that is NOT in Items — that one is a pure display bypass
    /// and must never materialize into the real economy. Null-tolerant.
    /// </summary>
    public void HoldItem(ProtagonistState p, string defId, bool fromBackpack)
    {
        if (p == null || string.IsNullOrEmpty(defId)) return;
        // Bump whatever was previously held back to the backpack — but ONLY if it came from the
        // backpack. A previously-held debug catalog item vanishes (it was never in the economy).
        if (!string.IsNullOrEmpty(p.HeldItemDefId) && p.HeldItemFromBackpack)
            Items.Add(new ItemInstance(p.HeldItemDefId)
            {
                DayCooldownRemaining = p.HeldItemDayCooldownRemaining,
            });
        // Consume the incoming item from the backpack only when it's a real backpack item. A debug
        // catalog item isn't in Items, so nothing is removed and — crucially — nothing is granted.
        int incomingCooldown = 0;
        if (fromBackpack)
        {
            int idx = Items.FindIndex(it => it.DefId == defId);
            if (idx >= 0)
            {
                incomingCooldown = Items[idx].DayCooldownRemaining;
                Items.RemoveAt(idx);
            }
        }
        p.HeldItemDefId = defId;
        p.HeldItemFromBackpack = fromBackpack;
        p.HeldItemDayCooldownRemaining = incomingCooldown;
        // NOTE(T30 §4): benching a protagonist should auto-return its held item to the backpack;
        // deferred until party-benching UI lands. No perish/cooldown/passive hooks in this stub.
    }

    /// <summary>Return a protagonist's held item to the backpack, clearing the slot. A real backpack
    /// item goes back to Items; a debug-conjured catalog item (HeldItemFromBackpack == false)
    /// simply vanishes so it never pollutes the real economy. Null-tolerant. The daily cooldown
    /// is preserved for real backpack instances.</summary>
    public void UnholdItem(ProtagonistState p)
    {
        if (p == null || string.IsNullOrEmpty(p.HeldItemDefId)) return;
        if (p.HeldItemFromBackpack)
            Items.Add(new ItemInstance(p.HeldItemDefId)
            {
                DayCooldownRemaining = p.HeldItemDayCooldownRemaining,
            });
        p.HeldItemDefId = null;
        p.HeldItemFromBackpack = false;
        p.HeldItemDayCooldownRemaining = 0;
    }

    /// <summary>Party size is a query so later item/meta modifiers can raise it without
    /// rewriting the shelter UI or battle handoff.</summary>
    public int PartyCap() => 3;

    /// <summary>Validate and atomically replace the shelter-configured team order.
    /// Only owned, unique protagonists may be fielded; a team always has at least one
    /// member. The changed build applies to the next combat snapshot, never in place
    /// during an existing battle.</summary>
    public bool TrySetActiveBuild(IReadOnlyList<string> ids, out string error)
    {
        if (ids == null || ids.Count == 0)
        {
            error = "A team needs at least one protagonist.";
            return false;
        }
        if (ids.Count > PartyCap())
        {
            error = $"A team can field at most {PartyCap()} protagonists.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || FindProtagonist(id) == null)
            {
                error = $"'{id}' is not in the owned roster.";
                return false;
            }
            if (!seen.Add(id))
            {
                error = "A protagonist can occupy only one team slot.";
                return false;
            }
        }

        ActiveBuild.Clear();
        foreach (string id in ids) ActiveBuild.Add(id);
        ActiveProtagonistIndex = 0;
        error = null;
        return true;
    }

    /// <summary>Place an owned protagonist at a shelter team slot. Selecting a current
    /// member reorders it; selecting a benched member replaces the selected occupied
    /// slot or fills the next empty one.</summary>
    public bool TrySetActiveBuildSlot(int slot, string id, out string error)
    {
        if (slot < 0 || slot >= PartyCap())
        {
            error = "That team slot does not exist.";
            return false;
        }
        if (FindProtagonist(id) == null)
        {
            error = $"'{id}' is not in the owned roster.";
            return false;
        }

        var next = ValidActiveBuild();
        int existing = next.IndexOf(id);
        if (existing >= 0)
        {
            next.RemoveAt(existing);
            next.Insert(Math.Min(slot, next.Count), id);
        }
        else if (slot < next.Count)
        {
            next[slot] = id;
        }
        else
        {
            next.Add(id);
        }
        return TrySetActiveBuild(next, out error);
    }

    public bool TryRemoveActiveBuildSlot(int slot, out string error)
    {
        var next = ValidActiveBuild();
        if (slot < 0 || slot >= next.Count)
        {
            error = "That team slot is already empty.";
            return false;
        }
        if (next.Count <= 1)
        {
            error = "A team needs at least one protagonist.";
            return false;
        }
        next.RemoveAt(slot);
        return TrySetActiveBuild(next, out error);
    }

    /// <summary>Capture a safe, immutable battle order from the editable shelter team.
    /// Corrupt legacy state falls back to the first owned protagonist rather than
    /// letting Arena silently hydrate one character as another.</summary>
    public BattleTeamSnapshot CaptureBattleTeam()
    {
        var ids = ValidActiveBuild();
        if (ids.Count == 0 && Owned.Count > 0) ids.Add(Owned[0].Id);

        string selected = ActiveProtagonistIndex >= 0 && ActiveProtagonistIndex < ActiveBuild.Count
            ? ActiveBuild[ActiveProtagonistIndex] : null;
        int initialIndex = selected == null ? 0 : ids.IndexOf(selected);
        return new BattleTeamSnapshot(ids.ToArray(), initialIndex < 0 ? 0 : initialIndex);
    }

    private List<string> ValidActiveBuild()
    {
        var valid = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ActiveBuild)
        {
            if (valid.Count >= PartyCap()) break;
            if (string.IsNullOrWhiteSpace(id) || FindProtagonist(id) == null) continue;
            if (seen.Add(id)) valid.Add(id);
        }
        return valid;
    }

    /// <summary>Find a protagonist's run state by Id from the Owned roster. Null if not found.</summary>
    public ProtagonistState FindProtagonist(string id)
    {
        if (id == null) return null;
        foreach (var p in Owned)
            if (p.Id == id) return p;
        return null;
    }

    public void GrantProtagonist(string id)
    {
        Owned.Add(new ProtagonistState(id));
        ProtagonistsCollected++;
    }

    // ============================================================= debug

    /// <summary>One-line state dump for the debug overlay / logs (T30 §1 test hook).</summary>
    public string DebugSnapshot()
    {
        string day = InVoid ? "???" : Day.ToString();
        return $"seed={Seed} day={day} sta={Stamina}/{MaxStamina} node={CurrentNodeId} " +
               $"visited={VisitedNodeIds.Count} done={CompletedNodeIds.Count} " +
               $"[nodes {NodesTraversed} worlds {WorldsVisited} goals {GoalsSucceeded} " +
               $"prot {ProtagonistsCollected} items {ItemsCollected}]";
    }
}

using System.Collections.Generic;
using Godot;
using Fableland.Map;

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
        ActiveBuild.Clear();
        ActiveBuild.Add("Pomegraknight");
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
        CurrentAdventure = new AdventureContext
        {
            NodeId = nodeId,
            NodeLevel = node?.Level ?? 1,
            Mission = node?.Mission ?? MissionType.Collection,
            Kind = kind,
            Day = Day,
            IsRevisitCombat = revisitCombat,
        };

        bool isCombat = kind == NodeKind.Combat || kind == NodeKind.Boss;
        if (isCombat) Stamina = 0; // entering any combat node depletes all stamina (NODES §2.3)

        string scene = kind switch
        {
            NodeKind.Combat or NodeKind.Boss => "res://Scenes/Arena.tscn",
            NodeKind.Shelter => "res://Scenes/Shelter.tscn",
            NodeKind.QuestionMark => "res://Scenes/Event.tscn",
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
                if (newlyDone && node.IsCombat) GoalsSucceeded++;
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
    public void AddAtk(int amount) { foreach (var p in Owned) p.BonusAtk += amount; }
    public void AddDef(int amount) { foreach (var p in Owned) p.BonusDef += amount; }

    /// <summary>
    /// Rest Blessing (NODES §5.5): heal 30% max HP; 1/3 of any excess above 100% becomes a
    /// permanent additive max-HP percentage-point bump. Global across the party.
    /// </summary>
    public void RestBlessing()
    {
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
        foreach (var p in Owned) p.HpRatio = Mathf.Min(1f, p.HpRatio + ratio);
    }

    public void AddWonderCores(int amount) => WonderCores += amount;

    public void AddItem(string defId)
    {
        Items.Add(new ItemInstance(defId));
        ItemsCollected++;
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

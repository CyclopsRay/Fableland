using System;
using System.Collections.Generic;
using Godot;
using Fableland.Data;
using Fableland.Map;
using Fableland.MapCreation.Runtime;
using Fableland.Debug;
using Fableland.Items;

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
    private static readonly string[] PrototypeDefaultTeam =
    {
        "Sifu Pangda", "Pixolotl", "Cleopastar",
    };

    // ---- identity / clock ----
    public string Seed;
    public DetRandom Rng;                 // root stream; subsystems derive via Rng.Sub("tag")
    public int Day;
    public bool InVoid;                   // past the singularity ⇒ HUD shows "???"
    public int Stamina;

    // ---- map ----
    public MapGraph Graph;                // node.Devoured is the live devour flag
    /// <summary>The world pool this run's map was generated from, as abbreviations in pool
    /// order. Recorded with the seed: together they regenerate the map, whatever the pool has
    /// become since (v0.11.0).</summary>
    public IReadOnlyList<string> WorldPool => _worldPool;
    private List<string> _worldPool = new();
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

    // ---- zone 6 (Docs/Zone6.gdd) ----
    /// <summary>This run's zone 6: its variant (drawn from the seed on a stream of its own),
    /// the Void Frontier's gravity, Avīci's seals, wins and losses per node. Written here only,
    /// when a zone-6 fight resolves (<see cref="ReportGoal"/>) or by a debug knob.</summary>
    public Zone6State Zone6 = new();
    /// <summary>What the last zone-6 fight changed, in a player's words ("gravity ×0.80 (was
    /// ×1.00)") — the arena's toast and the map's next note. "" when nothing did.</summary>
    public string LastZone6Note = "";
    /// <summary>What the last goal paid, in a player's words ("+10 wonder cores · FanChen's
    /// Heart"), for the arena's toast; "" when it paid nothing.</summary>
    public string LastRewardLine { get; private set; } = "";

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

    // Save ownership: a loaded DTO is retained only to preserve its unknown forward-compatible
    // JSON fields when this build writes it again. Gameplay continues to read/write this class.
    private RunSaveData _saveTemplate;
    // The start-of-current-day checkpoint used only by TwistedReality's boss-loss possession.
    // It is persisted as a bounded nested DTO; see SaveGameService.CloneRunSave.
    private RunSaveData _lastDayCheckpoint;
    public int ActiveSaveSlot { get; private set; } = -1;
    private string _pendingBattleResumeNodeId;
    // True only while ResumeLoadedRun re-enters a saved battle: BeginAdventure then sets the
    // scene up again but must not repeat what a challenge does once (THE VOID's phantom step).
    private bool _resumingBattle;
    private int _nextItemInstanceOrdinal = 1;

    public override void _EnterTree()
    {
        Instance = this;
        if (OS.IsDebugBuild() && !SaveGameService.RoundTripSelfTest(out string error))
            GD.PushError($"[RunState] Save DTO self-test failed: {error}");
        if (OS.IsDebugBuild())
            foreach (string failure in LocalTime.SelfTest())
                GD.PushError($"[RunState] Local-time self-test failed: {failure}");
        // Realm laws and zone 6 (Docs/Zone6.gdd): every violation is reported at boot.
        if (OS.IsDebugBuild())
        {
            foreach (string problem in RealmLawTable.Validate()) GD.PushError("[RunState] " + problem);
            foreach (string problem in Zone6Table.Validate()) GD.PushError("[RunState] " + problem);
            foreach (string failure in Zone6State.SelfTest()) GD.PushError("[RunState] Zone-6 self-test failed: " + failure);
        }
        // A tester can pin every new run's zone 6 to one variant from outside the game.
        string forced = OS.GetEnvironment("FABLELAND_ZONE6");
        if (Zone6Table.Variant(forced) != null) Zone6State.ForcedVariantId = Zone6Table.Variant(forced).Id;
        // …and start every new run with all five protagonists (the debug page's switch).
        if (string.Equals(OS.GetEnvironment("FABLELAND_ROSTER"), "all", StringComparison.OrdinalIgnoreCase))
            DebugAllProtagonistsOnNewRun = true;
        if (OS.IsDebugBuild())
            foreach (string problem in ShelterTable.Validate()) GD.PushError("[RunState] " + problem);
    }
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    // ============================================================= run lifecycle

    /// <summary>Read-only title-menu summaries for the three player-visible save slots.</summary>
    public SaveSlotInfo[] GetSaveSlots()
    {
        var slots = new SaveSlotInfo[SaveGameService.SlotCount];
        for (int i = 0; i < slots.Length; i++) slots[i] = SaveGameService.GetSlotInfo(i);
        return slots;
    }

    /// <summary>Start a fresh seeded run in the selected slot and immediately claim it with an
    /// initial snapshot. The title UI never has to know the save-file format.</summary>
    public bool StartNewRunInSlot(int slot, string seed, out string error)
    {
        error = null;
        if (slot < 0 || slot >= SaveGameService.SlotCount)
        {
            error = "Save slot is out of range.";
            return false;
        }
        ActiveSaveSlot = slot;
        NewRun(seed);
        return SaveActiveRun(out error);
    }

    /// <summary>Hydrate a slot into RunState. A normal checkpoint resumes on the map; an
    /// unfinished battle is rebuilt from its deterministic node contract, never from live
    /// mission/foe/projectile objects.</summary>
    public bool TryLoadRunFromSlot(int slot, out string error)
    {
        error = null;
        if (!SaveGameService.TryRead(slot, out RunSaveData save, out error)) return false;
        try
        {
            HydrateSave(save);
            ActiveSaveSlot = slot;
            _saveTemplate = save;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not restore save slot {slot + 1}: {ex.Message}";
            GD.PushError($"[RunState] {error}");
            return false;
        }
    }

    /// <summary>Write the active slot. Runs launched directly for debugging default to slot 1 so
    /// the same pause command remains useful outside the title menu.</summary>
    public bool SaveActiveRun(out string error)
    {
        if (Graph == null) { error = "There is no run to save."; return false; }
        if (ActiveSaveSlot < 0) ActiveSaveSlot = 0;
        return SaveGameService.TryWrite(ActiveSaveSlot, BuildSaveData(), out error);
    }

    /// <summary>Checkpoint the run, return to the title screen, and leave no live Adventure
    /// handshake behind. This is the shared Save &amp; Quit endpoint for map and arena pause menus.</summary>
    public bool SaveAndQuit(bool resumeUnfinishedBattle = false)
    {
        _pendingBattleResumeNodeId = resumeUnfinishedBattle && CurrentAdventure != null
            && (CurrentAdventure.Kind == NodeKind.Combat || CurrentAdventure.Kind == NodeKind.Boss)
            ? CurrentAdventure.NodeId : null;
        CurrentAdventure = null;
        if (!SaveActiveRun(out string error))
        {
            GD.PushError($"[RunState] Save & Quit failed: {error}");
            return false;
        }
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");
        return true;
    }

    /// <summary>Enter a just-loaded run. Battle Save &amp; Quit restarts the same unfinished node
    /// immediately, with the persisted party/HP/build but a fresh deterministic encounter. All
    /// other checkpoints land on the map.</summary>
    public void ResumeLoadedRun()
    {
        string battleNodeId = _pendingBattleResumeNodeId;
        _pendingBattleResumeNodeId = null;
        if (!string.IsNullOrEmpty(battleNodeId))
        {
            _resumingBattle = true;
            try { BeginAdventure(battleNodeId); }
            finally { _resumingBattle = false; }
            return;
        }
        ReturnToMap();
    }

    /// <summary>Start a brand-new run: generate the map, reset everything, day 1, stamina full.</summary>
    public void NewRun(string seed)
    {
        seed = string.IsNullOrWhiteSpace(seed) ? DetRandom.NewSeed() : seed.Trim().ToUpperInvariant();
        Seed = seed;
        Rng = new DetRandom(seed);
        _worldPool = WorldDef.PoolAbbrs();
        Graph = MapGenerator.Generate(seed, MapGenerator.PomegraknightHome, WorldDef.Resolve(_worldPool));
        RebuildNodeIndex();

        Day = 1;
        Stamina = MaxStamina;
        InVoid = false;
        RunFinished = false;
        LastEndKind = RunEndKind.Death;
        CurrentAdventure = null;
        _saveTemplate = null;
        _lastDayCheckpoint = null;
        _pendingBattleResumeNodeId = null;
        if (ActiveSaveSlot < 0) ActiveSaveSlot = 0;
        // Its own stream (seed:zone6:variant): the draw moves nothing the generator made.
        Zone6 = Zone6State.ForSeed(seed);
        LastZone6Note = "";

        VisitedNodeIds.Clear();
        CompletedNodeIds.Clear();
        MundaneShelterIds.Clear();
        ResolvedEventIds.Clear();
        _worldsSet.Clear();

        // NODES §3.1: a run starts with its home world's protagonist alone; the others are
        // recruited (§4.5). The debug page can still start runs with all five.
        StartRoster(MapGenerator.PomegraknightHome);
        WonderCores = 0;
        Items.Clear();
        _nextItemInstanceOrdinal = 1;
        Ledger = new ItemLedger();   // perish clocks, plots, phantoms, mushrooms (ITEMS.gdd §4, §7)
        LastPhantomNote = "";

        NodesTraversed = 0;
        GoalsSucceeded = 0;
        ProtagonistsCollected = Owned.Count;
        ItemsCollected = 0;

        var start = Graph.StartNode;
        CurrentNodeId = start.Id;
        PreviousNodeId = start.Id;
        MarkVisited(start); // start node visited (NodesTraversed = 1, world revealed)
        CaptureLastDayCheckpoint();
    }

    public MapNode FindNode(string id) =>
        id != null && _nodeById != null && _nodeById.TryGetValue(id, out var n) ? n : null;

    /// <summary>Dynamic Eidolon Shelters are added and removed at runtime, so their lookup index
    /// must be refreshed whenever the graph mutates.</summary>
    public void RebuildNodeIndex()
    {
        _nodeById = new Dictionary<string, MapNode>();
        if (Graph == null) return;
        foreach (MapNode node in Graph.Nodes) _nodeById[node.Id] = node;
    }

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
        // THE VOID's phantoms take a road toward a challenged node — before the battle team's
        // HP is captured, so a phantom's touch is felt in this fight (ITEMS.gdd §7.2). A saved
        // battle resumed on Continue is the same challenge: they took that road already.
        if (_resumingBattle) LastPhantomNote = "";
        else OnChallengeNode(node);

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
        // A realm's 4-1 is held by that realm's protagonist as a BOSS (NODES §4.5); a world
        // whose own is not built yet fields the stand-in (BossTable.ForWorld — PumpKing, for
        // now, in every realm).
        if (kind == NodeKind.Boss && node != null && node.Level == 4)
        {
            adventure.BossId = BossTable.ForWorld(node.Zone)?.Id ?? "";
            adventure.RecruitId = RecruitOf(node);
        }
        // Zone 6 is a stage of its own (Docs/Zone6.gdd §3): each LV5 node is one of the
        // variant's five mini-bosses, the LV6 core its final boss. The mission the generator
        // rolled for an LV5 node stays in the graph untouched (no draw moves); the handshake
        // overrides it here.
        Zone6Role zone6Role = Zone6RoleOf(node);
        if (zone6Role != Zone6Role.None)
        {
            adventure.Mission = MissionType.Boss;
            adventure.BossId = zone6Role == Zone6Role.MiniBoss
                ? Zone6.MiniBossAt(node.Id)
                : Zone6.Variant?.FinalBossId ?? "";
        }
        // The overworld selects an authored combat map once and snapshots only its path into
        // the handshake. The arena never reaches back into the map graph or rolls selection.
        if (node?.IsCombat == true)
        {
            // Zone 6 has no maps of its own yet: it wears one of the worlds this run crossed
            // (MapGDD §7) — a map of one of the run's five, by the node's mission, drawn from a
            // stream of its own. An outer node is dealt its own world's, by level and band.
            bool inVoid = node.WorldIndex == -1;
            // Dealt for the mission the NODE rolled — a zone-6 gate's map is the one it was
            // dealt before v0.12.0 (no map moves, none is orphaned); the fight on it is a boss's.
            var selected = DealCombatMap(Seed, node, Graph?.Worlds.ConvertAll(world => world.Abbr), Zone6.Variant,
                node.Mission, adventure.Terrain);
            adventure.CombatMapPath = selected?.AbsolutePath ?? "";
            adventure.VoidTouched = inVoid && selected != null;
            if (selected == null)
                GD.PushWarning($"[RunState] no combat map matched {node.Zone} LV{node.Level} {adventure.Mission} {adventure.Terrain}; using legacy Arena.");
        }
        bool isCombat = kind == NodeKind.Combat || kind == NodeKind.Boss;
        if (isCombat) adventure.BattleTeam = CaptureBattleTeam();
        // The rules of this place (Docs/Zone6.gdd §7), computed here so the arena never asks:
        // an outer node carries its realm's law, a zone-6 node its variant's rules; seals the
        // run carries go everywhere.
        if (isCombat && node != null) adventure.Rules = ArenaRules.Build(node.Zone, Zone6, zone6Role);
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
        LastZone6Note = "";
        LastRewardLine = "";
        bool zone6Mini = node != null && CurrentAdventure?.Rules?.Zone6Role == Zone6Role.MiniBoss;
        if (zone6Mini)
        {
            // Docs/Zone6.gdd §3: a mini-boss decides the variant's ladder — a first victory
            // pays it once; every defeat costs, and the battle team limps home on at least a
            // quarter of its HP instead of the run ending.
            if (success && !CompletedNodeIds.Contains(node.Id)) LastZone6Note = Zone6.RecordWin(node.Id);
            else if (!success)
            {
                LastZone6Note = Zone6.RecordLoss(node.Id);
                FloorBattleTeamHp(Zone6Table.DefeatHpFloor);
            }
        }
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
                    LastRewardLine = ItemCatalog.DisplayName(TwistedRealityItemId);
                }
                // A realm capital's fall: the QiongFeng Claw fires, the Palace of LOOING yields one.
                OnCapitalWon(node, newlyDone);
            }
            int ownedBefore = Owned.Count;
            ApplyRewards(rewards);
            // What the arena says the player got (audit v0.11.1 #2): the bundle, the capital's
            // TwistedReality, and a recruit only when one actually joined.
            var said = new List<string>();
            string bundle = rewards?.Describe() ?? "";
            if (bundle.Length > 0) said.Add(bundle);
            if (LastRewardLine.Length > 0) said.Add(LastRewardLine);
            if (Owned.Count > ownedBefore && rewards != null)
                foreach (string id in rewards.ProtagonistGrants) said.Add($"{id} joins you");
            LastRewardLine = string.Join(" · ", said);
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

    /// <summary>Every member of the battle that just ended keeps at least this share of HP
    /// (a zone-6 mini-boss defeat, Docs/Zone6.gdd §3).</summary>
    private void FloorBattleTeamHp(float floor)
    {
        string[] ids = CurrentAdventure?.BattleTeam?.MemberIds ?? Array.Empty<string>();
        foreach (string id in ids)
        {
            ProtagonistState member = FindProtagonist(id);
            if (member != null) member.HpRatio = Math.Max(member.HpRatio, floor);
        }
    }

    /// <summary>Which zone-6 fight a node is: an LV5 node's mini-boss, the LV6 core's final
    /// boss, or none (any node outside zone 6, and its river hub).</summary>
    public static Zone6Role Zone6RoleOf(MapNode node)
    {
        if (node == null || node.WorldIndex != -1 || !node.IsCombat) return Zone6Role.None;
        if (node.Level >= 6) return Zone6Role.Final;
        return Zone6Table.MiniBossSlot(node.Id) >= 0 ? Zone6Role.MiniBoss : Zone6Role.None;
    }

    /// <summary>
    /// The authored map a combat node is dealt — the one rule <see cref="BeginAdventure"/> and
    /// the run check share. An outer node gets one of its own world's maps by level, band and
    /// mission; a zone-6 node wears one of the run's worlds (MapGDD §7) for the mission the
    /// generator rolled into it (a gate's boss fights on the map the gate was always dealt) —
    /// except a variant's final boss with a fixed arena of its own (Zone6.gdd §3).
    /// Null ⇒ nothing matched; the arena opens its legacy stage.
    /// </summary>
    public static CombatMapSelection DealCombatMap(string seed, MapNode node, IReadOnlyList<string> runWorlds,
        Zone6VariantDef zone6Variant, MissionType mission, string terrain)
    {
        if (node == null || !node.IsCombat) return null;
        string fixedArena = Zone6RoleOf(node) == Zone6Role.Final ? zone6Variant?.FinalArenaMapId : null;
        if (!string.IsNullOrEmpty(fixedArena)) return CombatMapCatalog.ById(fixedArena);
        return node.WorldIndex == -1
            ? CombatMapCatalog.SelectForVoid(seed, node.Id, runWorlds, mission)
            : CombatMapCatalog.Select(seed, node.Id, node.Zone, node.Level, mission, terrain);
    }

    /// <summary>The mission a node is actually fought as: the generator's roll, except in zone
    /// 6, where every combat node is a boss (the run check reads this, not the graph).</summary>
    public static MissionType EffectiveMission(MapNode node) =>
        node == null ? MissionType.Collection
        : Zone6RoleOf(node) != Zone6Role.None ? MissionType.Boss : node.Mission;

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
        // Notes the day left (the capital yielding TwistedReality, a recruit) are kept for this
        // summary — clearing them first threw them away (audit v0.11.1 #2).
        _pipeline.Run(this);
        if (!RunFinished)
        {
            string dayLabel = InVoid ? "???" : Day.ToString(); // hidden day still ticks; only the display is "???"
            var parts = new List<string> { $"Day {dayLabel} begins." };
            parts.AddRange(DayEndNotes);
            DayEndNotes.Clear();
            LastDayEndSummary = string.Join(" ", parts);
            // flow/day.end: a day is over and the next begins. Variant = the new day.
            CueBus.EmitAt(CueOwners.Flow, "day.end", Vector2.Zero, variant: Day, tag: InVoid ? "VOID" : null);
            CaptureLastDayCheckpoint();
            if (!SaveActiveRun(out string saveError))
                GD.PushError($"[RunState] Day-end save failed: {saveError}");
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
        _pendingBattleResumeNodeId = null;
        // Permadeath and victory close the run; retaining the checkpoint would turn a terminal
        // state into a Continue option. Auto-saves that happened just before this call are
        // therefore intentionally removed.
        if (ActiveSaveSlot >= 0) SaveGameService.Delete(ActiveSaveSlot);
        ActiveSaveSlot = -1;
        _saveTemplate = null;
        GetTree().ChangeSceneToFile("res://Scenes/RunOver.tscn");
    }

    /// <summary>Consume TwistedReality when a boss would kill the player and restore the last
    /// completed day's checkpoint. This is intentionally called only by the arena's boss death
    /// paths; ordinary deaths remain terminal.</summary>
    public bool TryRecoverFromBossFailure()
    {
        if (!HasTwistedReality() || _lastDayCheckpoint == null) return false;
        RunSaveData checkpoint = SaveGameService.CloneRunSave(_lastDayCheckpoint);
        if (checkpoint == null) return false;
        HydrateSave(checkpoint);
        RemoveTwistedReality();
        CaptureLastDayCheckpoint();
        if (!SaveActiveRun(out string saveError))
            GD.PushError($"[RunState] TwistedReality rewind save failed: {saveError}");
        ReturnToMap();
        return true;
    }

    /// <summary>
    /// Cross the zone-6 singularity (MapGDD §7): latch InVoid and devour the whole outer ring at
    /// once. The player is on a zone-6 node (safe); the day display becomes "???".
    /// </summary>
    public void EnterVoid()
    {
        if (Graph == null) return;
        InVoid = true;
        int eaten = 0;
        foreach (var n in Graph.Nodes)
            if (n.WorldIndex != -1) { if (!n.Devoured) eaten++; n.Devoured = true; } // everything outside zone 6
        // flow/void.devour, tag `singularity`: the whole outer ring goes at once. Magnitude = nodes eaten.
        CueBus.EmitAt(CueOwners.Flow, "void.devour", Vector2.Zero, variant: Day, magnitude: eaten, tag: "singularity");
        Graph.BreakRealityBridgesAtDevouredEndpoints();
        RebuildNodeIndex();
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

    /// <summary>Spend wonder cores (a "?" event's price): false, and nothing spent, without enough.</summary>
    public bool SpendWonderCores(int amount)
    {
        if (amount <= 0) return true;
        if (WonderCores < amount) return false;
        WonderCores -= amount;
        DebugManager.Instance?.LogCores(-amount, WonderCores);
        return true;
    }

    /// <summary>Every fighter loses a share of its max HP (a "?" event's cost) — never below 5 %:
    /// an event never kills.</summary>
    public void HurtParty(float ratio)
    {
        // Down to 5 % at the lowest — and a fighter already below that is not raised to it.
        foreach (var p in Owned) p.HpRatio = Mathf.Min(p.HpRatio, Mathf.Max(0.05f, p.HpRatio - ratio));
    }

    public void AddItem(string defId)
    {
        if (!ItemCatalog.Contains(defId))
        {
            GD.PushWarning($"[RunState] skipped unknown item '{defId}'.");
            return;
        }
        Items.Add(CreateItemInstance(defId));
        ItemsCollected++;
        DebugManager.Instance?.LogItem(defId, ItemsCollected);
    }

    private ItemInstance CreateItemInstance(string defId)
    {
        var item = new ItemInstance(defId, $"item-{_nextItemInstanceOrdinal++}");
        Ledger.OnCreated(defId, item.InstanceId);   // a perishable item starts its clock
        return item;
    }

    private void NoteLoadedInstanceId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        const string prefix = "item-";
        if (!instanceId.StartsWith(prefix, StringComparison.Ordinal)) return;
        if (int.TryParse(instanceId[prefix.Length..], out int value))
            _nextItemInstanceOrdinal = Math.Max(_nextItemInstanceOrdinal, value + 1);
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

    private void RemoveTwistedReality()
    {
        Items.RemoveAll(item => item.DefId == TwistedRealityItemId);
        foreach (ProtagonistState protagonist in Owned)
        {
            if (protagonist.HeldItemDefId != TwistedRealityItemId) continue;
            protagonist.HeldItemDefId = null;
            protagonist.HeldItemInstanceId = null;
            protagonist.HeldItemFromBackpack = false;
            protagonist.HeldItemDayCooldownRemaining = 0;
            protagonist.HeldItemSecondCooldownRemaining = 0f;
        }
    }

    /// <summary>Gets the held copy of TwistedReality. Its skill only works while equipped.</summary>
    private ProtagonistState HeldTwistedReality()
    {
        foreach (var protagonist in Owned)
            if (protagonist.HeldItemDefId == TwistedRealityItemId) return protagonist;
        return null;
    }

    /// <summary>Checks whether TwistedReality can create its next Bridge of Eidolon.</summary>
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
        if (origin.WorldIndex < 0 || !origin.IsCombat || !origin.IsVoidRiverPeripheral
            || !CompletedNodeIds.Contains(origin.Id))
        {
            reason = "Bridge of Eidolon needs a completed city on the VOID river.";
            return false;
        }
        if (Graph.HasRealityBridge(origin))
        {
            reason = "This city is already anchored to another reality.";
            return false;
        }
        if (FindEidolonDestination(origin) == null)
        {
            reason = origin.Level == 4 ? "No available Zone 6 gate remains." : "No eligible city remains in another realm.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Build Bridge of Eidolon. The player remains at the origin; reaching the midpoint shelter
    /// and spending its Blessing is the only way to cross to the opposite city.
    /// </summary>
    public bool TryCreateRealityBridge(string originId, out MapNode shelter, out string reason)
    {
        shelter = null;
        if (!CanCreateRealityBridge(originId, out reason)) return false;
        var origin = FindNode(originId);
        MapNode destination = FindEidolonDestination(origin);
        if (destination == null || !Graph.AddRealityBridge(origin, destination, out shelter))
        {
            shelter = null;
            reason = "Reality could not find a stable destination.";
            return false;
        }
        RebuildNodeIndex();
        HeldTwistedReality().HeldItemDayCooldownRemaining = ItemCatalog.TryGet(TwistedRealityItemId, out ItemDef item)
            ? item.DayCooldownDays : 4;
        reason = null;
        return true;
    }

    private MapNode FindEidolonDestination(MapNode origin)
    {
        // A completed LV4 capital does not choose another outer realm: it is the exclusive
        // player-created entrance to the nearest surviving Zone-6 LV5 gate.
        if (origin.Level == 4)
        {
            MapNode zone6 = null;
            foreach (MapNode candidate in Graph.Nodes)
            {
                if (candidate.WorldIndex != -1 || candidate.Level != 5 || !candidate.IsCombat
                    || candidate.Devoured || Graph.HasRealityBridge(candidate)) continue;
                if (zone6 == null || candidate.Pos.DistanceSquaredTo(origin.Pos) < zone6.Pos.DistanceSquaredTo(origin.Pos)
                    || (Mathf.IsEqualApprox(candidate.Pos.DistanceSquaredTo(origin.Pos), zone6.Pos.DistanceSquaredTo(origin.Pos))
                        && string.CompareOrdinal(candidate.Id, zone6.Id) < 0)) zone6 = candidate;
            }
            return zone6;
        }

        int closestRealm = -1;
        float closestRealmDistance = float.MaxValue;
        foreach (MapNode candidate in Graph.Nodes)
        {
            if (!candidate.IsCombat || candidate.WorldIndex < 0 || candidate.WorldIndex == origin.WorldIndex
                || candidate.Devoured || Graph.HasRealityBridge(candidate)) continue;
            float dist = candidate.Pos.DistanceSquaredTo(origin.Pos);
            if (dist < closestRealmDistance || (Mathf.IsEqualApprox(dist, closestRealmDistance)
                && candidate.WorldIndex < closestRealm))
            {
                closestRealmDistance = dist;
                closestRealm = candidate.WorldIndex;
            }
        }

        MapNode best = null;
        foreach (var candidate in Graph.Nodes)
        {
            if (!candidate.IsCombat || candidate.WorldIndex != closestRealm || candidate.Devoured
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

    /// <summary>Spend the Eidolon Shelter's one Blessing, then transfer to the other bridge
    /// endpoint without ending the day. Team Build remains a free shelter action.</summary>
    public bool TryCrossEidolonShelter(string shelterId, out string reason)
    {
        if (!CanCrossEidolonShelter(shelterId, out MapNode shelter, out MapNode target, out reason)) return false;
        ConsumeBlessing(shelterId);
        PreviousNodeId = shelter.Id;
        CurrentNodeId = target.Id;
        MarkVisited(target);
        CurrentAdventure = null;
        if (!InVoid && target.WorldIndex == -1) EnterVoid();
        if (target.IsCombat && !CompletedNodeIds.Contains(target.Id)) BeginAdventure(target.Id);
        else ReturnToMap();
        return true;
    }

    /// <summary>Whether an Eidolon Shelter's Blessing can still carry the player across — the
    /// bridge stands, the Blessing is unspent, the far shore is not devoured — and where to.
    /// The shelter screen asks it so that a crossing that cannot happen never leaves the player
    /// on a screen with every exit disabled (audit v0.11.1 #16).</summary>
    public bool CanCrossEidolonShelter(string shelterId, out string reason) =>
        CanCrossEidolonShelter(shelterId, out _, out _, out reason);

    private bool CanCrossEidolonShelter(string shelterId, out MapNode shelter, out MapNode target, out string reason)
    {
        reason = null;
        target = null;
        shelter = FindNode(shelterId);
        if (Graph == null || shelter == null || !Graph.TryGetRealityBridgeEndpoints(shelter, out MapNode a, out MapNode b))
        {
            reason = "This shelter no longer connects two realities.";
            return false;
        }
        if (!IsShelterBlessed(shelterId)) { reason = "This shelter's Blessing is already spent."; return false; }
        target = PreviousNodeId == a.Id ? b : a;
        if (target.Devoured) { reason = "The far shore has been devoured."; return false; }
        return true;
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
            Items.Add(new ItemInstance(p.HeldItemDefId, p.HeldItemInstanceId)
            {
                DayCooldownRemaining = p.HeldItemDayCooldownRemaining,
                SecondCooldownRemaining = p.HeldItemSecondCooldownRemaining,
            });
        // Consume the incoming item from the backpack only when it's a real backpack item. A debug
        // catalog item isn't in Items, so nothing is removed and — crucially — nothing is granted.
        int incomingCooldown = 0;
        float incomingSecondCooldown = 0f;
        string incomingInstanceId = $"debug:{defId}";
        if (fromBackpack)
        {
            int idx = Items.FindIndex(it => it.DefId == defId);
            if (idx >= 0)
            {
                incomingCooldown = Items[idx].DayCooldownRemaining;
                incomingSecondCooldown = Items[idx].SecondCooldownRemaining;
                incomingInstanceId = Items[idx].InstanceId;
                Items.RemoveAt(idx);
            }
            else return; // never conjure a claimed item from an absent backpack instance
        }
        p.HeldItemDefId = defId;
        p.HeldItemInstanceId = incomingInstanceId;
        p.HeldItemFromBackpack = fromBackpack;
        p.HeldItemDayCooldownRemaining = incomingCooldown;
        p.HeldItemSecondCooldownRemaining = incomingSecondCooldown;
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
            Items.Add(new ItemInstance(p.HeldItemDefId, p.HeldItemInstanceId)
            {
                DayCooldownRemaining = p.HeldItemDayCooldownRemaining,
                SecondCooldownRemaining = p.HeldItemSecondCooldownRemaining,
            });
        p.HeldItemDefId = null;
        p.HeldItemInstanceId = null;
        p.HeldItemFromBackpack = false;
        p.HeldItemDayCooldownRemaining = 0;
        p.HeldItemSecondCooldownRemaining = 0f;
    }

    /// <summary>
    /// Swap two equipped item instances without routing either through the backpack. Team Build
    /// uses this when the player selects another active member's greyed item cell; copying every
    /// held-slot field together preserves concrete instance identity and both cooldown axes.
    /// </summary>
    public bool TrySwapHeldItems(ProtagonistState first, ProtagonistState second, out string error)
    {
        if (first == null || second == null)
        {
            error = "Both item holders must exist.";
            return false;
        }
        if (ReferenceEquals(first, second))
        {
            error = "That item is already equipped here.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(second.HeldItemDefId))
        {
            error = "The selected holder has no item to swap.";
            return false;
        }

        (first.HeldItemDefId, second.HeldItemDefId) = (second.HeldItemDefId, first.HeldItemDefId);
        (first.HeldItemInstanceId, second.HeldItemInstanceId) = (second.HeldItemInstanceId, first.HeldItemInstanceId);
        (first.HeldItemDayCooldownRemaining, second.HeldItemDayCooldownRemaining) = (second.HeldItemDayCooldownRemaining, first.HeldItemDayCooldownRemaining);
        (first.HeldItemSecondCooldownRemaining, second.HeldItemSecondCooldownRemaining) = (second.HeldItemSecondCooldownRemaining, first.HeldItemSecondCooldownRemaining);
        (first.HeldItemFromBackpack, second.HeldItemFromBackpack) = (second.HeldItemFromBackpack, first.HeldItemFromBackpack);

        error = null;
        return true;
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

    /// <summary>Place an owned protagonist at a shelter team slot. Selecting a current member
    /// swaps it with the selected slot; selecting a benched member replaces an occupied slot or
    /// fills the next empty one. The ordered list stays compact because empty slots have no battle
    /// identity; Team Build renders the unused capacity after its final member.</summary>
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
            if (slot < next.Count)
            {
                (next[slot], next[existing]) = (next[existing], next[slot]);
            }
            else
            {
                // An existing member can be placed in the first visual empty slot. There is no
                // durable empty entry in ActiveBuild, so this is an intentional reorder to tail.
                next.RemoveAt(existing);
                next.Add(id);
            }
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

    /// <summary>Recruit a protagonist (a realm's beaten 4-1, NODES §4.5). Idempotent: one
    /// already on the roster is not added twice and not counted again. A recruit joins at
    /// full HP with the Blessings the roster shares, and takes an empty team slot if there is
    /// one (else the bench). Returns true when it was new.</summary>
    public bool GrantProtagonist(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || FindProtagonist(id) != null) return false;
        if (ProtagonistRoster.GetScene(id) == null)
        {
            GD.PushError($"[RunState] cannot recruit '{id}': no such protagonist.");
            return false;
        }
        Owned.Add(JoiningState(id));
        ProtagonistsCollected++;
        DayEndNotes.Add(FieldIfRoom(id)
            ? $"{id} joins your team."
            : $"{id} joins your roster — field them with Team Build at a shelter.");
        return true;
    }

    /// <summary>Debug (the debug page, or <c>FABLELAND_ROSTER=all</c>): new runs start with
    /// every implemented protagonist and the prototype's team, as every run did before v0.12.9.
    /// Not saved — a tester's switch; a run started this way keeps its five like any roster.</summary>
    public static bool DebugAllProtagonistsOnNewRun;

    /// <summary>The protagonist a run that starts in this world starts with (NODES §3.1): the
    /// world's home protagonist. A home world always has one; the first roster entry stands
    /// in for one that does not.</summary>
    public static string StarterOf(string homeAbbr) =>
        WorldDef.HomeProtagonistOf(homeAbbr) ?? ProtagonistRoster.Entries[0].Id;

    /// <summary>Who beating this node recruits: a realm's 4-1 recruits the realm's home
    /// protagonist (none for a realm with no protagonist yet); nothing else recruits.</summary>
    public static string RecruitOf(MapNode node) =>
        node != null && node.Kind == NodeKind.Boss && node.Level == 4 && node.WorldIndex >= 0
            ? WorldDef.HomeProtagonistOf(node.Zone) ?? "" : "";

    /// <summary>A new run's roster: its home world's protagonist alone, fielded (NODES §3.1) —
    /// or, with the debug switch on, every implemented protagonist and the prototype's team.</summary>
    private void StartRoster(string homeAbbr)
    {
        Owned.Clear();
        ActiveBuild.Clear();
        ActiveProtagonistIndex = 0;
        if (DebugAllProtagonistsOnNewRun)
        {
            foreach ((string id, _) in ProtagonistRoster.Entries) Owned.Add(new ProtagonistState(id));
            ApplyPrototypeDefaultTeam();
            return;
        }
        string starter = StarterOf(homeAbbr);
        Owned.Add(new ProtagonistState(starter));
        ActiveBuild.Add(starter);
    }

    /// <summary>Debug (the debug page): every implemented protagonist joins the run in
    /// progress, empty team slots filled in roster order, so a tester can play with all five
    /// without beating a realm. No day notes — nothing happened in the story. Returns how
    /// many joined.</summary>
    public int DebugRecruitAll()
    {
        int joined = 0;
        foreach ((string id, _) in ProtagonistRoster.Entries)
        {
            if (FindProtagonist(id) != null) continue;
            Owned.Add(JoiningState(id));
            ProtagonistsCollected++;
            FieldIfRoom(id);
            joined++;
        }
        DebugManager.Instance?.LogSystem($"Debug roster: {joined} protagonist(s) joined; team {string.Join(", ", ActiveBuild)}");
        return joined;
    }

    /// <summary>A protagonist joining mid-run: full HP, and the Blessings every fighter
    /// already shares — ATK, DEF and max HP are global (NODES §3.3), so a newcomer gets what
    /// the whole roster has in common (the least any one of them has).</summary>
    private ProtagonistState JoiningState(string id)
    {
        var joining = new ProtagonistState(id);
        if (Owned.Count == 0) return joining;
        int atk = int.MaxValue, def = int.MaxValue, hp = int.MaxValue;
        foreach (ProtagonistState member in Owned)
        {
            atk = Math.Min(atk, member.BonusAtk);
            def = Math.Min(def, member.BonusDef);
            hp = Math.Min(hp, member.MaxHpPercentPoints);
        }
        joining.BonusAtk = atk;
        joining.BonusDef = def;
        joining.MaxHpPercentPoints = Math.Max(0, hp);
        return joining;
    }

    /// <summary>A recruit takes the first empty team slot — the party cap is never exceeded
    /// (NODES §4.5) — and waits on the bench when the team is full. The fighter who opens the
    /// next battle stays the same. True when the recruit is in the team.</summary>
    private bool FieldIfRoom(string id)
    {
        List<string> team = ValidActiveBuild();
        if (team.Contains(id)) return true;
        if (team.Count >= PartyCap()) return false;
        string opener = ActiveProtagonistIndex >= 0 && ActiveProtagonistIndex < ActiveBuild.Count ? ActiveBuild[ActiveProtagonistIndex] : null;
        team.Add(id);
        if (!TrySetActiveBuild(team, out _)) return false;
        ActiveProtagonistIndex = Math.Max(0, ActiveBuild.IndexOf(opener ?? ""));
        return true;
    }

    private void ApplyPrototypeDefaultTeam()
    {
        ActiveBuild.Clear();
        foreach (string id in PrototypeDefaultTeam)
            if (FindProtagonist(id) != null) ActiveBuild.Add(id);
        if (ActiveBuild.Count == 0 && Owned.Count > 0) ActiveBuild.Add(Owned[0].Id);
        ActiveProtagonistIndex = 0;
    }

    // ============================================================= persistence

    /// <summary>Capture the post-day-end state (or fresh day-one state) without recursively
    /// nesting prior snapshots. The checkpoint is never used for normal save/load flow.</summary>
    private void CaptureLastDayCheckpoint()
    {
        if (Graph == null) return;
        _lastDayCheckpoint = SaveGameService.CloneRunSave(BuildSaveData());
    }

    /// <summary>Copy the owned run state into the versioned file DTO. Static map geometry is
    /// regenerated from Seed; this records only the durable mutations that differ per run.</summary>
    private RunSaveData BuildSaveData()
    {
        RunSaveData save = _saveTemplate ?? new RunSaveData();
        save.Version = SaveGameService.CurrentVersion;
        save.Seed = Seed ?? "";
        save.WorldPool = new List<string>(_worldPool);
        save.Day = Day;
        save.InVoid = InVoid;
        save.Stamina = Stamina;
        save.CurrentNodeId = CurrentNodeId ?? "";
        save.PreviousNodeId = PreviousNodeId ?? "";
        save.ResumeBattleNodeId = _pendingBattleResumeNodeId ?? "";
        save.VisitedNodeIds = SortedIds(VisitedNodeIds);
        save.CompletedNodeIds = SortedIds(CompletedNodeIds);
        save.MundaneShelterIds = SortedIds(MundaneShelterIds);
        save.ResolvedEventIds = SortedIds(ResolvedEventIds);
        save.ActiveBuild = new List<string>(ActiveBuild);
        save.ActiveProtagonistIndex = ActiveProtagonistIndex;
        save.WonderCores = WonderCores;
        save.NodesTraversed = NodesTraversed;
        save.GoalsSucceeded = GoalsSucceeded;
        save.ProtagonistsCollected = ProtagonistsCollected;
        save.ItemsCollected = ItemsCollected;
        save.WorldsVisited = SortedIds(_worldsSet);
        save.Zone6 = Zone6?.ToSave();
        save.ItemLedger = Ledger?.ToSave();

        save.Map ??= new MapSaveData();
        var devoured = new List<string>();
        if (Graph != null)
            foreach (MapNode node in Graph.Nodes)
                if (node.Devoured) devoured.Add(node.Id);
        devoured.Sort(StringComparer.Ordinal);
        save.Map.DevouredNodeIds = devoured;

        var priorBridges = save.Map.RealityBridges ?? new List<RealityBridgeSaveData>();
        var bridges = new List<RealityBridgeSaveData>();
        if (Graph != null)
        {
            foreach (MapNode shelter in Graph.RealityBridgeShelters())
            {
                if (!Graph.TryGetRealityBridgeEndpoints(shelter, out MapNode a, out MapNode b)) continue;
                RealityBridgeSaveData dto = FindBridgeDto(priorBridges, a.Id, b.Id)
                    ?? new RealityBridgeSaveData();
                dto.NodeAId = a.Id;
                dto.NodeBId = b.Id;
                bridges.Add(dto);
            }
        }
        save.Map.RealityBridges = bridges;

        var priorParty = new Dictionary<string, ProtagonistSaveData>(StringComparer.Ordinal);
        if (save.Owned != null)
            foreach (ProtagonistSaveData prior in save.Owned)
                if (prior != null && !string.IsNullOrEmpty(prior.Id) && !priorParty.ContainsKey(prior.Id))
                    priorParty.Add(prior.Id, prior);
        var party = new List<ProtagonistSaveData>();
        foreach (ProtagonistState protagonist in Owned)
        {
            ProtagonistSaveData dto = priorParty.TryGetValue(protagonist.Id, out ProtagonistSaveData prior)
                ? prior : new ProtagonistSaveData();
            CopyToSave(protagonist, dto);
            party.Add(dto);
        }
        save.Owned = party;

        var priorItems = save.Items ?? new List<ItemSaveData>();
        var items = new List<ItemSaveData>();
        for (int i = 0; i < Items.Count; i++)
        {
            ItemInstance item = Items[i];
            ItemSaveData dto = FindItemDto(priorItems, item.InstanceId) ?? new ItemSaveData();
            dto.InstanceId = item.InstanceId ?? "";
            dto.DefId = item.DefId ?? "";
            dto.DayCooldownRemaining = item.DayCooldownRemaining;
            dto.SecondCooldownRemaining = item.SecondCooldownRemaining;
            items.Add(dto);
        }
        save.Items = items;
        save.LastDayCheckpoint = SaveGameService.CloneRunSave(_lastDayCheckpoint);
        return save;
    }

    /// <summary>Restore a version-migrated DTO. Invalid content ids are warned about and skipped
    /// rather than making a save file unplayable after content changes.</summary>
    private void HydrateSave(RunSaveData save)
    {
        if (save == null || string.IsNullOrWhiteSpace(save.Seed))
            throw new InvalidOperationException("The save has no run seed.");

        Seed = save.Seed.Trim().ToUpperInvariant();
        Rng = new DetRandom(Seed);
        // The map is regenerated, not stored: it must come from the pool the run was made
        // from, or a save from a smaller pool would wake up in different realms.
        _worldPool = save.WorldPool is { Count: > 0 }
            ? new List<string>(save.WorldPool)
            : new List<string>(SaveGameService.WorldPoolBeforeV4);
        List<WorldDef> worlds = WorldDef.Resolve(_worldPool);
        if (worlds.Count < _worldPool.Count)
            GD.PushWarning($"[RunState] the save's world pool ({string.Join(", ", _worldPool)}) names a world this build does not have; the map may differ from the one it was saved on.");
        Graph = MapGenerator.Generate(Seed, MapGenerator.PomegraknightHome, worlds);
        RebuildNodeIndex();

        var devoured = new HashSet<string>(save.Map?.DevouredNodeIds ?? new List<string>(), StringComparer.Ordinal);
        foreach (MapNode node in Graph.Nodes) node.Devoured = devoured.Contains(node.Id);
        if (save.Map?.RealityBridges != null)
        {
            foreach (RealityBridgeSaveData bridge in save.Map.RealityBridges)
            {
                MapNode a = FindNode(bridge?.NodeAId);
                MapNode b = FindNode(bridge?.NodeBId);
                if (!Graph.AddRealityBridge(a, b, out _))
                    GD.PushWarning($"[RunState] skipped invalid saved reality bridge '{bridge?.NodeAId}' ↔ '{bridge?.NodeBId}'.");
            }
            RebuildNodeIndex();
        }

        // Optional since v0.12.0: an older save has none and starts an empty zone 6 on the
        // variant its seed draws (no save-version bump — Zone6.gdd §7).
        Zone6 = Zone6State.FromSave(save.Zone6, Seed);
        LastZone6Note = "";
        // Optional since v0.12.8: an older save loads an empty ledger (no save-version bump).
        Ledger = ItemLedger.FromSave(save.ItemLedger);
        LastPhantomNote = "";

        Day = Math.Max(1, save.Day);
        Stamina = Math.Clamp(save.Stamina, 0, MaxStamina);
        InVoid = save.InVoid;
        RunFinished = false;
        LastEndKind = RunEndKind.Death;
        CurrentAdventure = null; // live arena state is not part of a checkpoint
        MapNode resumeNode = FindNode(save.ResumeBattleNodeId);
        _pendingBattleResumeNodeId = resumeNode?.IsCombat == true ? resumeNode.Id : null;
        if (!string.IsNullOrEmpty(save.ResumeBattleNodeId) && _pendingBattleResumeNodeId == null)
            GD.PushWarning($"[RunState] skipped invalid saved battle resume '{save.ResumeBattleNodeId}'.");
        LastDayEndSummary = "";
        DayEndNotes.Clear();

        RestoreNodeIdSet(VisitedNodeIds, save.VisitedNodeIds, "visited node");
        RestoreNodeIdSet(CompletedNodeIds, save.CompletedNodeIds, "completed node");
        RestoreNodeIdSet(MundaneShelterIds, save.MundaneShelterIds, "mundane shelter");
        RestoreNodeIdSet(ResolvedEventIds, save.ResolvedEventIds, "resolved event");

        CurrentNodeId = FindNode(save.CurrentNodeId)?.Id ?? Graph.StartNode.Id;
        PreviousNodeId = FindNode(save.PreviousNodeId)?.Id ?? CurrentNodeId;
        if (!VisitedNodeIds.Contains(CurrentNodeId)) VisitedNodeIds.Add(CurrentNodeId);

        Owned.Clear();
        if (save.Owned != null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProtagonistSaveData protagonist in save.Owned)
            {
                if (protagonist == null || string.IsNullOrWhiteSpace(protagonist.Id) || !seen.Add(protagonist.Id))
                    continue;
                if (ProtagonistRoster.GetScene(protagonist.Id) == null)
                {
                    GD.PushWarning($"[RunState] skipped unknown saved protagonist '{protagonist.Id}'.");
                    continue;
                }
                ProtagonistState restored = CopyFromSave(protagonist);
                if (!string.IsNullOrWhiteSpace(restored.HeldItemDefId)
                    && string.IsNullOrWhiteSpace(restored.HeldItemInstanceId))
                    restored.HeldItemInstanceId = $"legacy-{_nextItemInstanceOrdinal++}";
                NoteLoadedInstanceId(restored.HeldItemInstanceId);
                Owned.Add(restored);
            }
        }
        if (Owned.Count == 0)
        {
            string starter = StarterOf(Graph.Worlds.Count > 0 ? Graph.Worlds[0].Abbr : MapGenerator.PomegraknightHome);
            GD.PushWarning($"[RunState] save had no valid protagonists; restored the run's starter, {starter}.");
            Owned.Add(new ProtagonistState(starter));
        }

        // A save keeps the roster it was saved with: loading never recruits (NODES §3.1).
        // Until v0.12.9 every load topped a save up to all five — the prototype roster.
        ActiveBuild.Clear();
        var activeSeen = new HashSet<string>(StringComparer.Ordinal);
        if (save.ActiveBuild != null)
            foreach (string id in save.ActiveBuild)
                if (FindProtagonist(id) != null && activeSeen.Add(id) && ActiveBuild.Count < PartyCap())
                    ActiveBuild.Add(id);
        if (ActiveBuild.Count == 0) ActiveBuild.Add(Owned[0].Id);
        ActiveProtagonistIndex = Math.Clamp(save.ActiveProtagonistIndex, 0, ActiveBuild.Count - 1);

        WonderCores = Math.Max(0, save.WonderCores);
        Items.Clear();
        _nextItemInstanceOrdinal = 1;
        if (save.Items != null)
            foreach (ItemSaveData item in save.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.DefId))
                {
                    GD.PushWarning("[RunState] skipped saved item with no id.");
                    continue;
                }
                string instanceId = string.IsNullOrWhiteSpace(item.InstanceId)
                    ? $"legacy-{_nextItemInstanceOrdinal++}" : item.InstanceId;
                NoteLoadedInstanceId(instanceId);
                Items.Add(new ItemInstance(item.DefId, instanceId)
                {
                    DayCooldownRemaining = Math.Max(0, item.DayCooldownRemaining),
                    SecondCooldownRemaining = Math.Max(0f, item.SecondCooldownRemaining),
                });
            }

        NodesTraversed = Math.Max(VisitedNodeIds.Count, Math.Max(0, save.NodesTraversed));
        GoalsSucceeded = Math.Max(0, save.GoalsSucceeded);
        ProtagonistsCollected = Math.Max(Owned.Count, Math.Max(0, save.ProtagonistsCollected));
        ItemsCollected = Math.Max(Items.Count, Math.Max(0, save.ItemsCollected));
        _worldsSet.Clear();
        if (save.WorldsVisited != null)
            foreach (string world in save.WorldsVisited)
                if (!string.IsNullOrWhiteSpace(world)) _worldsSet.Add(world);
        if (_worldsSet.Count == 0)
            foreach (string nodeId in VisitedNodeIds)
            {
                MapNode node = FindNode(nodeId);
                if (node?.WorldIndex >= 0) _worldsSet.Add(node.Zone);
                else if (node?.WorldIndex == -1) _worldsSet.Add("XX");
            }

        _lastDayCheckpoint = save.LastDayCheckpoint != null
            ? SaveGameService.CloneRunSave(save.LastDayCheckpoint)
            : SaveGameService.CloneRunSave(save);
    }

    private void RestoreNodeIdSet(HashSet<string> target, List<string> ids, string label)
    {
        target.Clear();
        if (ids == null) return;
        foreach (string id in ids)
        {
            if (FindNode(id) == null)
            {
                GD.PushWarning($"[RunState] skipped unknown saved {label} '{id}'.");
                continue;
            }
            target.Add(id);
        }
    }

    private static List<string> SortedIds(IEnumerable<string> values)
    {
        var result = new List<string>(values);
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static RealityBridgeSaveData FindBridgeDto(List<RealityBridgeSaveData> bridges, string a, string b)
    {
        foreach (RealityBridgeSaveData bridge in bridges)
            if (bridge != null && ((bridge.NodeAId == a && bridge.NodeBId == b)
                || (bridge.NodeAId == b && bridge.NodeBId == a)))
                return bridge;
        return null;
    }

    private static ItemSaveData FindItemDto(List<ItemSaveData> items, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return null;
        foreach (ItemSaveData item in items)
            if (item?.InstanceId == instanceId) return item;
        return null;
    }

    private static void CopyToSave(ProtagonistState source, ProtagonistSaveData destination)
    {
        destination.Id = source.Id ?? "";
        destination.HpRatio = source.HpRatio;
        destination.BonusAtk = source.BonusAtk;
        destination.BonusDef = source.BonusDef;
        destination.MaxHpPercentPoints = source.MaxHpPercentPoints;
        destination.HeldItemDefId = source.HeldItemDefId;
        destination.HeldItemInstanceId = source.HeldItemInstanceId;
        destination.HeldItemDayCooldownRemaining = source.HeldItemDayCooldownRemaining;
        destination.HeldItemSecondCooldownRemaining = source.HeldItemSecondCooldownRemaining;
        destination.HeldItemFromBackpack = source.HeldItemFromBackpack;
        destination.ShiftCdRemaining = source.ShiftCdRemaining;
        destination.ESkillCdRemaining = source.ESkillCdRemaining;
        destination.ShiftAltCdRemaining = source.ShiftAltCdRemaining;
        destination.ESkillAltCdRemaining = source.ESkillAltCdRemaining;
        destination.AmmoInitialized = source.AmmoInitialized;
        destination.AmmoCurrent = source.AmmoCurrent;
        destination.AmmoAttackCooldownRemaining = source.AmmoAttackCooldownRemaining;
        destination.AmmoReloadActive = source.AmmoReloadActive;
        destination.AmmoReloadRemaining = source.AmmoReloadRemaining;
    }

    private static ProtagonistState CopyFromSave(ProtagonistSaveData source)
    {
        return new ProtagonistState(source.Id)
        {
            HpRatio = Math.Clamp(source.HpRatio, 0f, 1f),
            BonusAtk = source.BonusAtk,
            BonusDef = source.BonusDef,
            MaxHpPercentPoints = Math.Max(0, source.MaxHpPercentPoints),
            HeldItemDefId = source.HeldItemDefId,
            HeldItemInstanceId = source.HeldItemInstanceId,
            HeldItemDayCooldownRemaining = Math.Max(0, source.HeldItemDayCooldownRemaining),
            HeldItemSecondCooldownRemaining = Math.Max(0f, source.HeldItemSecondCooldownRemaining),
            HeldItemFromBackpack = source.HeldItemFromBackpack,
            ShiftCdRemaining = Math.Max(0f, source.ShiftCdRemaining),
            ESkillCdRemaining = Math.Max(0f, source.ESkillCdRemaining),
            ShiftAltCdRemaining = Math.Max(0f, source.ShiftAltCdRemaining),
            ESkillAltCdRemaining = Math.Max(0f, source.ESkillAltCdRemaining),
            AmmoInitialized = source.AmmoInitialized,
            AmmoCurrent = Math.Max(0, source.AmmoCurrent),
            AmmoAttackCooldownRemaining = Math.Max(0f, source.AmmoAttackCooldownRemaining),
            AmmoReloadActive = source.AmmoReloadActive,
            AmmoReloadRemaining = Math.Max(0f, source.AmmoReloadRemaining),
        };
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

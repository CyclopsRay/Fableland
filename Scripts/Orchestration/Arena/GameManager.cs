using Godot;
using Fableland.Map;
using Fableland.Missions;
using Fableland.Run;
using Fableland.Data;
using Fableland.Debug;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;
using Fableland.UI;
using Fableland.Items;

/// <summary>
/// The arena's integrator (T30 §3, NODES §4). Owns the "Entities"/foe-spawn plumbing the
/// mission layer expects (<see cref="Entities"/>, <see cref="Spawner"/>, <see cref="FoeLevel"/>,
/// <see cref="RandomFoeSpawn"/>, <see cref="RandomPlacementPoint"/>), builds the arena via
/// <see cref="ArenaBuilder"/>, instantiates the node's <see cref="Mission"/>, ticks it, and
/// routes every outcome through <see cref="RunState"/> per the locked integration contract:
/// <c>Arena reads RunState.CurrentAdventure; Arena → RunState.ReportGoal; "Finish the Day" →
/// RunState.EndDay()</c>. In debug mode (no run — direct F5 on this scene) it falls back to the
/// old local win/lose banner + lives/respawn/restart loop instead of touching RunState at all.
///
/// Missions never touch the HUD (see Mission.cs) — this class polls their read-props every
/// frame and forwards to <see cref="Hud"/>, and forwards HUD button presses back into the
/// mission (reward choice) or into RunState (Finish the Day).
/// </summary>
public partial class GameManager : Node2D
{
    [Export] public PackedScene CrabScene;
    [Export] public PackedScene SeagullScene;
    [Export] public PackedScene WonderCorePickupScene;
    [Export] public PackedScene ProtectCoreScene;
    [Export] public PackedScene DestroyObjectiveScene;
    [Export] public PackedScene BossCrabScene;

    // Hazard scenes for ArenaBuilder's procedural placement (S1) — already ext_resources of
    // Arena.tscn; exported here instead of instanced as fixed set pieces (G3).
    [Export] public PackedScene FireHazardScene;
    [Export] public PackedScene FreezeHazardScene;

    [Export] public NodePath EntitiesPath = "Entities";
    [Export] public NodePath HudPath = "Hud";
    [Export] public NodePath WorldPath = "World";
    [Export] public NodePath HazardsPath = "Hazards";

    // Debug foe-level knob: when > 0 it forces the spawn level directly; set to 0 to use
    // day-based scaling via FoeStats.LevelForDay(DebugDay). Only consulted when no run exists.
    [Export] public int DebugFoeLevel = 1;
    [Export] public int DebugDay = 1;

    /// <summary>
    /// Optional direct-F5 map preview. It is ignored whenever RunState supplied a selected map,
    /// so debug convenience can never override a real node's deterministic selection.
    /// </summary>
    [Export] public string DebugCombatMapPath = "";

    /// <summary>
    /// Direct-F5 mission knobs, consulted only when NO run exists (the v0.5.0 rule: a debug
    /// default must never reach a real run). Together they stage any fight without the
    /// overworld: <c>DebugMission = Boss</c>, <c>DebugNodeLevel = 4</c> and a
    /// <see cref="BossTable"/> id in <c>DebugBossId</c> is a boss fight — and with
    /// <see cref="DebugCombatMapPath"/> left empty the arena is chosen by the run's own map
    /// selection for that boss's world. `Scenes/Bosses/PumpKingBossFight.tscn` is exactly that.
    /// </summary>
    [Export] public MissionType DebugMission = MissionType.Collection;
    [Export] public int DebugNodeLevel = 1;
    [Export] public string DebugBossId = "";

    /// <summary>Debug mode's "Boss fights" page stages a boss here; every no-run Arena
    /// fields it until the page clears it (so R-restart keeps the same fight). Never read
    /// while a run's adventure is in progress.</summary>
    public static string StagedDebugBossId;

    [Export] public float RespawnDelay = 1.2f;
    /// <summary>Universal arena opening countdown. All mission and ambient foe sources stay
    /// gated until it completes; individual missions never own this policy.</summary>
    [Export] public float PreCombatCountdownSeconds = 3f;

    /// <summary>The "Entities" child every spawned foe/pickup/objective is parented under.</summary>
    public Node2D Entities { get; private set; }

    /// <summary>The arena's foe-spawning service (owned/ticked here, configured by the mission).</summary>
    public FoeSpawner Spawner { get; private set; }

    /// <summary>Current foe level for this fight (debug override, else day-based; FOES §2).</summary>
    public int FoeLevel { get; private set; }

    /// <summary><see cref="BossTable"/> id of the boss this node fields, or empty — the
    /// handshake's <c>AdventureContext.BossId</c> (a debug knob when no run exists).</summary>
    public string BossId { get; private set; } = "";

    /// <summary>The selected map's Level Goal marker points, in authored order (empty in
    /// the legacy arena). A boss reads them as its perches (FOES §8.5).</summary>
    public System.Collections.Generic.IReadOnlyList<Vector2> LevelGoalPoints => _authoredLevelGoalSpawns;

    /// <summary>The mission this arena is hosting (read-only: tests and tooling).</summary>
    public Mission CurrentMission => _mission;

    /// <summary>True on an authored combat map, false on the legacy arena — which is what a
    /// node gets when no shipped map matches it (read-only: tests and tooling).</summary>
    public bool UsesAuthoredMap => _usingAuthoredMap;

    /// <summary>The authored map's size in pixels (zero on the legacy arena).</summary>
    public Vector2 AuthoredMapSize => _usingAuthoredMap ? new Vector2(_authoredMapWidth, _authoredMapHeight) : Vector2.Zero;

    /// <summary>True once the mission has resolved and its outcome was reported to the run.</summary>
    public bool GoalResolved => _goalResolved;

    /// <summary>Where the player's body is right now (the origin when there is none).</summary>
    public Vector2 PlayerPosition => _player != null && IsInstanceValid(_player) ? _player.GlobalPosition : Vector2.Zero;

    /// <summary>The body the player is fighting with right now (null between bodies).</summary>
    public CharacterController PlayerBody => _player != null && IsInstanceValid(_player) ? _player : null;

    /// <summary>The rules of this place (Docs/Zone6.gdd §7): the handshake's, or — with no run —
    /// the ones a staged debug fight would be dealt. Applied to every body that enters.</summary>
    public ArenaRules Rules => _rules;

    /// <summary>The node hazards are parented under (a boss's field attacks join it).</summary>
    public Node2D HazardLayer => _hazards;

    /// <summary>The arena's main floor — the broadest lowest ground — as a line: its height
    /// and the arena's left and right edges, in px. What a floor-sweeping attack runs along.</summary>
    public float FloorY => _floorY;
    public float ArenaLeft => _usingAuthoredMap ? 0f : ArenaBuilder.PlayLeft - Units.Px(6f);
    public float ArenaRight => _usingAuthoredMap ? _authoredMapWidth : ArenaBuilder.PlayRight + Units.Px(6f);

    private Node2D _world;
    private Node2D _hazards;
    private Hud _hud;
    private CharacterController _player;
    private ItemRuntime _itemRuntime;
    private Godot.Collections.Array<Node> _foeSpawnMarkers;
    private System.Collections.Generic.List<Vector2> _surfacePoints = new();
    private readonly System.Collections.Generic.List<CombatMapSpawnPoint> _authoredEnemySpawns = new();
    private readonly System.Collections.Generic.List<Vector2> _authoredLevelGoalSpawns = new();
    private readonly System.Collections.Generic.List<Vector2> _authoredRespawnSpawns = new();
    private bool _usingAuthoredMap;
    private float _authoredMapWidth;
    private float _authoredMapHeight;
    private Vector2 _authoredCharacterSpawn;
    private int _nextRespawnIndex;
    private FoeSpawnRules _authoredFoeSpawnRules;

    private DetRandom _rng;
    private Mission _mission;
    private ArenaRules _rules = ArenaRules.None;
    private bool _voidTouched;           // the map is drawn as the VOID left it (zone 6)
    private float _floorY = ArenaBuilder.GroundTopY;
    // A zone-6 defeat has floored the battle team's HP (RunState): the fallen body must not
    // write its 0 back over that afterwards (Finish the Day, a save-and-quit).
    private bool _hpSettled;
    private ProtagonistState _protagonist;   // hydrated protagonist for HP write-back
    // Captured once from AdventureContext on arena entry. The shelter's editable
    // ActiveBuild is intentionally never consulted by the live battle loop.
    private string[] _battlePartyIds = System.Array.Empty<string>();
    private int _battlePartyIndex;
    private float _switchCd;                 // Tab cooldown remaining (12s per NODES §3.3)
    private const float SwitchCooldown = 12f;
    private bool _hasRun;
    private int _nodeLevel;
    private MissionType _missionType;
    private bool _goalResolved;
    // The run is ending (the last fighter fell): the fight holds still for a beat before RUN
    // OVER, and nothing in the arena may resolve or end the run a second time meanwhile.
    private bool _dying;

    /// <summary>Seconds the fall holds before the run-over screen (audit v0.11.1 #20: death
    /// cut straight to it).</summary>
    public const float DeathBeatSeconds = 1.2f;

    private float _graceTimer;           // universal pre-combat grace: no foes, timer frozen, countdown shown
    private bool _ended;   // debug-only local end-of-match latch (banner + restart)

    // Flow cues (Docs/Feedback.gdd §6): what the arena tells the cue bus about itself.
    private string _mapWorld = "";       // the world the authored map was built for; "" if unknown
    private int _countdownShown = -1;    // the last whole second the countdown raised a tick for
    private int _timeCueShown = -1;      // the last whole second of the mission clock a cue was raised for

    public override void _Ready()
    {
        // Keep running while the tree is paused so the debug restart flow works after win/lose.
        ProcessMode = ProcessModeEnum.Always;
        SettingsStore.EnsureLoaded();   // F5 straight into an arena still gets the player's settings
        _graceTimer = Mathf.Max(0f, PreCombatCountdownSeconds);

        Entities = GetNode<Node2D>(EntitiesPath);
        _world = GetNode<Node2D>(WorldPath);
        _hazards = GetNode<Node2D>(HazardsPath);
        _hud = GetNode<Hud>(HudPath);
        _player = GetTree().GetFirstNodeInGroup("player") as CharacterController;

        var rs = RunState.Instance;
        var adv = rs?.CurrentAdventure;
        _hasRun = adv != null;
        _rules = _hasRun ? adv.Rules ?? ArenaRules.None : ArenaRules.None;
        _voidTouched = _hasRun && adv.VoidTouched;

        // A real battle starts from the immutable team snapshot in its adventure
        // handoff, not the authoring placeholder baked into Arena.tscn and not the
        // mutable shelter list. This is the sole team-build → battle boundary.
        if (_hasRun) ConfigureBattleParty(rs, adv);

        // Direct-F5 debug selection remains a separate test convenience. It does not
        // rewrite a real run's captured party or its shelter configuration.
        if (!_hasRun && DebugManager.Instance != null && DebugManager.Instance.Enabled
            && DebugManager.Instance.SelectedProtagonistId is string selId
            && _player != null && selId != _player.Name.ToString())
        {
            var scene = ProtagonistRoster.GetScene(selId);
            if (scene != null)
                _player = ReplacePlayerNode(_player, scene);
        }

        _foeSpawnMarkers = GetTree().GetNodesInGroup("enemy_spawn");

        // Debug mode's boss page: a staged boss turns any no-run Arena into its fight. A
        // zone-6 boss is fought at its node's level (5 for a mini-boss, 6 for a final boss).
        if (!_hasRun && !string.IsNullOrWhiteSpace(StagedDebugBossId))
        {
            DebugBossId = StagedDebugBossId;
            DebugMission = MissionType.Boss;
            DebugNodeLevel = BossTable.TryGet(StagedDebugBossId, out BossDef staged) && staged.Role != BossRole.Realm
                ? (staged.Role == BossRole.Zone6Final ? 6 : 5) : 4;
            DebugCombatMapPath = "";
        }

        string nodeId = adv?.NodeId ?? "debug";
        _nodeLevel = adv?.NodeLevel ?? Mathf.Clamp(DebugNodeLevel, 1, 6);
        _missionType = adv?.Mission ?? DebugMission;
        BossId = _hasRun ? adv.BossId ?? "" : DebugBossId ?? "";
        int day = adv?.Day ?? DebugDay;

        // Foe level (FOES §2): day-based outside zone 6; zone 6 is per-node — LV5 nodes fight
        // level-7 ("Hellish"), the LV6 core fights level-8 ("God-Forbid"). The debug knob is
        // only consulted when NO run exists (its default of 1 must never clamp a real run).
        FoeLevel = _hasRun
            ? (_nodeLevel >= 6 ? 8 : _nodeLevel == 5 ? 7 : FoeStats.LevelForDay(day))
            : (DebugFoeLevel > 0 ? DebugFoeLevel : FoeStats.LevelForDay(day));

        // Determinism (C2): every arena random draw flows from this seed-derived stream, never
        // GD.Randf/Randi/System.Random.
        _rng = _hasRun ? rs.Rng.Sub("arena:" + nodeId) : new DetRandom("debug-arena");

        string combatMapPath = !string.IsNullOrWhiteSpace(adv?.CombatMapPath)
            ? adv.CombatMapPath : (!_hasRun ? DebugCombatMapPath : "");
        // A debug boss fight with no map named asks the run's own selection for that boss's
        // world: the same rule, and so the same arena, a real 4-1 would be dealt. A zone-6
        // boss gets what its node would: the variant's fixed arena, or a world's boss arena
        // drawn as the VOID left it — with the rules a run would carry (Zone6State.Debug).
        if (!_hasRun && _missionType == MissionType.Boss && BossTable.TryGet(BossId, out BossDef debugBoss))
        {
            if (debugBoss.Role != BossRole.Realm)
            {
                _rules = DebugZone6Rules(debugBoss);
                if (string.IsNullOrWhiteSpace(combatMapPath))
                {
                    Zone6VariantDef variant = Zone6Table.Variant(debugBoss.Zone6Variant);
                    CombatMapSelection dealt = debugBoss.Role == BossRole.Zone6Final && !string.IsNullOrEmpty(variant?.FinalArenaMapId)
                        ? CombatMapCatalog.ById(variant.FinalArenaMapId)
                        : CombatMapCatalog.SelectForVoid("debug", nodeId, WorldDef.PoolAbbrs(), _missionType);
                    combatMapPath = dealt?.AbsolutePath ?? "";
                    _voidTouched = dealt != null;
                }
            }
            else
            {
                _rules = ArenaRules.Build(debugBoss.WorldAbbr, null, Zone6Role.None);
                if (string.IsNullOrWhiteSpace(combatMapPath))
                    combatMapPath = CombatMapCatalog.Select("debug", nodeId, debugBoss.WorldAbbr, _nodeLevel,
                        _missionType, CombatMapTerrain.SeaLevel)?.AbsolutePath ?? "";
            }
        }
        _usingAuthoredMap = TryBuildSelectedMap(combatMapPath);
        if (!_usingAuthoredMap)
        {
            var platformTex = GD.Load<Texture2D>("res://Assets/Sprites/Gameplay/World/platform_placeholder.svg");
            _surfacePoints = ArenaBuilder.Build(_world, _hazards, _rng, platformTex, FireHazardScene, FreezeHazardScene);
        }

        Spawner = new FoeSpawner(this, _rng.Sub("spawn")); // gate opens after the universal countdown
        ApplySelectedMapFoeComposition(combatMapPath);

        _mission = CreateMission(_missionType);
        _mission.Setup(this, _nodeLevel, _rng.Sub("mission"));
        DebugManager.Instance?.LogMission($"Mission start: {MissionName(_missionType)} LV{_nodeLevel} foeLV{FoeLevel} day{day} node{nodeId}");
        SetupPlayer(rs);
        SetupHud();
        ApplyRulesToArena();
        RaiseArenaEntered();
    }

    /// <summary>
    /// Puts this place's rules on every body in the arena now, and on every one that enters it
    /// later (a switch-in, a boss staged after the countdown, adds) — Docs/Zone6.gdd §7.
    /// Nothing here reads the map or the run: the rules came in the handshake.
    /// </summary>
    private void ApplyRulesToArena()
    {
        GetTree().NodeAdded += OnNodeAdded;
        if (_player != null) ArenaRulesApplier.ApplyToFighter(_player, _rules);
        foreach (Node node in GetTree().GetNodesInGroup("foe"))
            if (node is BaseFoe foe) ArenaRulesApplier.ApplyToFoe(foe, _rules);
        if (_rules.HasLaw || _rules.InZone6 || _rules.HasSeals)
            DebugManager.Instance?.LogMission("Rules: " + string.Join(" | ", _rules.HudLines()));
    }

    private void OnNodeAdded(Node node)
    {
        if (node is CharacterController body) ArenaRulesApplier.ApplyToFighter(body, _rules);
        else if (node is BaseFoe foe) ArenaRulesApplier.ApplyToFoe(foe, _rules);
    }

    /// <summary>A boss signature's backdrop (Māra's wheel, Docs/Mara.gdd §8): drawn over the
    /// map's art and under every body — it goes in right after the World.</summary>
    public void AddBackdrop(Node2D backdrop)
    {
        if (backdrop == null) return;
        AddChild(backdrop);
        MoveChild(backdrop, _world.GetIndex() + 1);
    }

    /// <summary>The Tempter's Offering was taken (Docs/Mara.gdd §7.5): one more stack of a
    /// poison in the party for the rest of this fight — on the body in play now and on every
    /// body switched in later (they take <see cref="_rules"/> as they enter). The run is not
    /// touched: the final fight ends it either way.</summary>
    public void TakeFightSeal(string poisonId)
    {
        _rules = _rules.WithFightSeal(poisonId);
        if (_player != null && IsInstanceValid(_player)) ArenaRulesApplier.ApplyToFighter(_player, _rules);
        _hud.SetRulesLines(_rules.HudLines());
        DebugManager.Instance?.LogMission("Offering taken: " + _rules.SealLine);
    }

    /// <summary>The rules a debug fight with a zone-6 boss is dealt: its variant's, from the
    /// run's zone 6 when a run of that variant exists, else from the debug state the debug
    /// page's knobs edit (gravity, seals).</summary>
    private static ArenaRules DebugZone6Rules(BossDef boss)
    {
        Zone6State state = RunState.Instance?.Zone6 is { } runZone6 && runZone6.VariantId == boss.Zone6Variant
            ? runZone6 : Zone6State.Debug;
        if (state.VariantId != boss.Zone6Variant) state.VariantId = boss.Zone6Variant;
        return ArenaRules.Build("XX", state, boss.Role == BossRole.Zone6Final ? Zone6Role.Final : Zone6Role.MiniBoss);
    }

    /// <summary>
    /// <c>flow</c>/<c>arena.enter</c>: the arena is built. Tag = the world the map was authored
    /// for ("" on the legacy arena or a map that names none), magnitude = how light the
    /// backdrop is (0 black – 1 white; −1 if there is none) so presentation can keep its marks
    /// readable against it, size = the battlefield's width in px, source = this arena — what a
    /// listener watches to know the arena has gone (its ambience ends when this node leaves
    /// the tree).
    /// </summary>
    private void RaiseArenaEntered()
    {
        var backdrop = GetNodeOrNull<ColorRect>("Background");
        float luminance = backdrop == null ? -1f
            : _voidTouched && VoidTreatment.IsOn(backdrop) ? VoidTreatment.DrawnLightness(backdrop.Color, CurrentLook())
            : backdrop.Color.Luminance;
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", PlayerPosition, this, CombatSide.Player,
            variant: _nodeLevel, size: _authoredMapWidth, magnitude: luminance, tag: ArenaTag);
    }

    /// <summary>What this arena's cues call it (flow/arena.enter's tag): in zone 6 the variant
    /// ("void_frontier", "avici") — presentation draws and scores every zone-6 fight in the
    /// VOID's medium, never the medium of the world whose map it borrows; elsewhere the world
    /// the map was authored for.</summary>
    public string ArenaTag => _rules.InZone6 ? _rules.Zone6VariantId : _voidTouched ? "VOID" : _mapWorld;

    private Zone6Look CurrentLook() => Zone6Table.Variant(_rules.Zone6VariantId)?.Look ?? Zone6Table.DefaultLook;

    /// <summary>The mission type as a cue tag: lower-case, constant strings.</summary>
    private static string MissionTag(MissionType t) => t switch
    {
        MissionType.Collection => "collection",
        MissionType.Protect => "protect",
        MissionType.Destroy => "destroy",
        MissionType.Slaughter => "slaughter",
        MissionType.Boss => "boss",
        _ => "",
    };

    private void RaiseFlow(string action, string tag = null, int variant = 0) =>
        CueBus.EmitAt(CueOwners.Flow, action, PlayerPosition, null, CombatSide.Player, variant: variant, tag: tag);

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
        // DebugManager is an autoload and outlives this arena scene; without this the
        // subscription added in SetupHud() leaks a handler pointing at a disposed GameManager,
        // and a later SKIP press (from a different arena visit) would invoke it.
        if (DebugManager.Instance != null)
            DebugManager.Instance.SkipRequested -= OnSkipRequested;
        _itemRuntime?.Dispose();
        _itemRuntime = null;
    }

    private static Mission CreateMission(MissionType type) => type switch
    {
        MissionType.Protect => new ProtectMission(),
        MissionType.Destroy => new DestroyMission(),
        MissionType.Slaughter => new SlaughterMission(),
        MissionType.Boss => new BossMission(),
        _ => new CollectionMission(),
    };

    /// <summary>
    /// Replace the legacy Arena geometry with the combat map selected by RunState. The map path
    /// is already frozen in AdventureContext, so re-entering a failed node uses the same arena
    /// and this scene never queries the overworld graph directly.
    /// </summary>
    private bool TryBuildSelectedMap(string mapPath)
    {
        if (string.IsNullOrWhiteSpace(mapPath)) return false;
        if (mapPath.StartsWith("res://") || mapPath.StartsWith("user://"))
            mapPath = ProjectSettings.GlobalizePath(mapPath);
        MapDocument document = CombatMapCatalog.LoadDocument(mapPath);
        if (document == null) return false;

        ClearChildren(_world);
        ClearChildren(_hazards);
        CombatMapBuild build = CombatMapRuntime.Build(document, _world, _hazards);
        _usingAuthoredMap = true;
        _authoredMapWidth = build.WidthPx;
        _authoredMapHeight = build.HeightPx;
        _authoredEnemySpawns.AddRange(build.EnemySpawns);
        _authoredLevelGoalSpawns.AddRange(build.LevelGoalSpawns);
        _authoredRespawnSpawns.AddRange(build.RespawnSpawns);
        _authoredFoeSpawnRules = document.FoeSpawnRules;
        _floorY = MainFloorY(document);
        // The arena is never told which world it is in (T00); the map says whose art it wears.
        _mapWorld = document.Worlds != null && document.Worlds.Count > 0 ? document.Worlds[0] ?? ""
            : document.World ?? "";

        if (build.CharacterSpawns.Count == 0)
            GD.PushWarning("[GameManager] authored map '" + document.Name + "' has no Character Spawn; keeping the scene fallback.");
        if (build.EnemySpawns.Count == 0)
            GD.PushWarning("[GameManager] authored map '" + document.Name + "' has no Enemy Spawn; periodic foes are suppressed.");
        if (_missionType != MissionType.Slaughter && build.LevelGoalSpawns.Count == 0)
            GD.PushWarning("[GameManager] authored map '" + document.Name + "' has no Level Goal marker for " + _missionType + "; using fallback placement.");

        if (_player != null && build.CharacterSpawns.Count > 0)
        {
            Vector2 start = build.CharacterSpawns[0];
            _authoredCharacterSpawn = start;
            _player.GlobalPosition = start;
            _player.SetSpawnPoint(start); // AddChild/_Ready captured the old scene position.
        }
        if (_authoredRespawnSpawns.Count == 0 && _player != null)
            GD.PushWarning("[GameManager] authored map '" + document.Name + "' has no Respawn Point; using its Character Spawn.");

        // Far layers are painted to line up when the camera sits on the character spawn, so
        // that is the parallax anchor here exactly as in the editor Playtest (MapCreation §1).
        build.AnchorParallax(build.CharacterSpawns.Count > 0
            ? build.CharacterSpawns[0]
            : new Vector2(_authoredMapWidth * 0.5f, _authoredMapHeight * 0.5f));

        var camera = _player?.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera != null)
        {
            camera.LimitLeft = 0;
            camera.LimitTop = 0;
            camera.LimitRight = Mathf.CeilToInt(_authoredMapWidth);
            camera.LimitBottom = Mathf.CeilToInt(_authoredMapHeight);
        }

        var backdrop = GetNodeOrNull<ColorRect>("Background");
        if (backdrop != null)
        {
            // The scene's backdrop rect is sized for the legacy 2000 px arena. An authored map
            // can be several screens wide or tall (a 96-cell Starland plain is 6144 px), so
            // stretch the canvas over the whole battlefield plus a screen of margin for shake
            // and for views wider than the map.
            Vector2 margin = new(Units.Px(60f), Units.Px(40f));
            backdrop.Position = -margin;
            backdrop.Size = new Vector2(_authoredMapWidth, _authoredMapHeight) + margin * 2f;

            if (!string.IsNullOrWhiteSpace(document.Canvas?.Color))
            {
                try { backdrop.Color = new Color(document.Canvas.Color); }
                catch { GD.PushWarning("[GameManager] invalid canvas color on authored map '" + document.Name + "'; keeping Arena backdrop."); }
            }
        }

        ApplyVoidTreatment(build, backdrop);
        DebugManager.Instance?.LogMission("Combat map: " + document.Name + " (" + document.Id + ")");
        return true;
    }

    /// <summary>
    /// Zone 6 wears a world it has swallowed (MapGDD §7): the handshake says so
    /// (<see cref="AdventureContext.VoidTouched"/>), and the map just built — a map of one of
    /// the run's worlds — is drawn drained and dark. Its art and the backdrop only: fighters,
    /// foes, hazards and telegraphs are not touched (<see cref="VoidTreatment"/>).
    /// </summary>
    private void ApplyVoidTreatment(CombatMapBuild build, ColorRect backdrop)
    {
        if (!_voidTouched) return;
        int drained = VoidTreatment.Apply(build.ParallaxLayers, backdrop, CurrentLook());
        DebugManager.Instance?.LogMission("Zone 6: the map is VOID-touched (" + drained + " pieces of art drained, look '" + (_rules.Zone6VariantId.Length > 0 ? _rules.Zone6VariantId : "default") + "')");
    }

    /// <summary>
    /// The height of a map's main floor in px: the top of the ground that most columns stand
    /// on (full-height side walls ignored) — the broad floor a fight happens on, not a ledge
    /// or a tower. Read from the document, so it is known before physics has run.
    /// </summary>
    public static float MainFloorY(MapDocument document)
    {
        MapLayerData battlefield = null;
        foreach (MapLayerData layer in document?.Layers ?? new System.Collections.Generic.List<MapLayerData>())
            if (layer.Role == MapLayerData.RoleBattlefield) { battlefield = layer; break; }
        if (battlefield == null) return ArenaBuilder.GroundTopY;

        var top = new System.Collections.Generic.Dictionary<int, int>();
        foreach (PlacedTile tile in battlefield.Tiles)
        {
            if (tile?.DefId == null || !tile.DefId.StartsWith("ground.", System.StringComparison.Ordinal)) continue;
            top[tile.X] = top.TryGetValue(tile.X, out int y) ? Mathf.Min(y, tile.Y) : tile.Y;
        }
        var votes = new System.Collections.Generic.Dictionary<int, int>();
        foreach (int row in top.Values)
            if (row > 1) votes[row] = votes.TryGetValue(row, out int n) ? n + 1 : 1;   // row ≤ 1: a side wall
        int best = -1, bestVotes = 0;
        foreach (var vote in votes)
            if (vote.Value > bestVotes || (vote.Value == bestVotes && vote.Key > best)) { best = vote.Key; bestVotes = vote.Value; }
        return best < 0 ? ArenaBuilder.GroundTopY : best * MapGrid.PixelsPerCell;
    }

    private static void ClearChildren(Node parent)
    {
        if (parent == null) return;
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ApplySelectedMapFoeComposition(string mapPath)
    {
        if (!_usingAuthoredMap || string.IsNullOrWhiteSpace(mapPath)) return;
        if (mapPath.StartsWith("res://") || mapPath.StartsWith("user://"))
            mapPath = ProjectSettings.GlobalizePath(mapPath);
        MapDocument document = CombatMapCatalog.LoadDocument(mapPath);
        if (document?.FoeCompositions == null) return;

        FoeComposition selected = null;
        for (int i = 0; i < document.FoeCompositions.Count; i++)
        {
            FoeComposition entry = document.FoeCompositions[i];
            if (entry?.Level == _nodeLevel) { selected = entry; break; }
            if (entry?.Level == 0) selected = entry;
        }
        if (selected != null) Spawner.SetComposition(selected.CrabWeight, selected.SeagullWeight);
    }

    private void SetupPlayer(RunState rs)
    {
        _itemRuntime?.Dispose();
        _itemRuntime = null;
        if (_player == null) return;

        _player.HpChanged += OnPlayerHpChanged;
        _player.Died += OnPlayerDied;
        _hud.SetPlayer(_player);
        _hud.SetPortrait(_hasRun && _battlePartyIds.Length > 0 ? _battlePartyIds[_battlePartyIndex] : _player.Name.ToString());
        _hud.SetHp(_player.CurrentHP, _player.MaxHP, _player.Shield, _player.TempHP);

        if (_hasRun && _battlePartyIds.Length > 0)
        {
            // Resolve only through the frozen battle order captured at node entry.
            _protagonist = rs.FindProtagonist(_battlePartyIds[_battlePartyIndex]);
            if (_protagonist == null)
            {
                GD.PushError($"GameManager: captured battle member '{_battlePartyIds[_battlePartyIndex]}' is no longer owned.");
                return;
            }
            float baseMaxHp = _player.MaxHP;   // authored default, captured before hydrating
            _player.HydrateRun(baseMaxHp, _protagonist.MaxHpPercentPoints, _protagonist.HpRatio,
                                _protagonist.BonusAtk, _protagonist.BonusDef);
            // Restore saved cooldown remaining from the protagonist's run state (background-CD, NODES §3.3)
            _player.LoadCooldownsFromState(_protagonist);
            _player.LoadAmmoFromState(_protagonist);
            _itemRuntime = new ItemRuntime(_player, _protagonist);
            _hud.SetLivesVisible(false);       // permadeath in a run — lives don't apply (NODES §2.2)
            UpdateNextMugshot();
        }
        else
        {
            _hud.SetLivesVisible(true);
            _hud.SetLives(_player.LivesRemaining);
        }
        PushItemHud();
    }

    /// <summary>Push the next protagonist's mugshot to the HUD (NODES §3.3).
    /// Shows the portrait of the party member Tab will switch to, or hides it when
    /// the party has fewer than 2 members.</summary>
    private void UpdateNextMugshot()
    {
        if (!_hasRun || _battlePartyIds.Length < 2)
        {
            _hud.SetNextProtagonist(null);
            return;
        }
        int nextIdx = (_battlePartyIndex + 1) % _battlePartyIds.Length;
        _hud.SetNextProtagonist(_battlePartyIds[nextIdx]);
    }

    /// <summary>Install the party snapshot that belongs to this combat entry and replace
    /// Arena.tscn's authoring placeholder with its selected starting protagonist.</summary>
    private void ConfigureBattleParty(RunState rs, AdventureContext adventure)
    {
        BattleTeamSnapshot snapshot = adventure?.BattleTeam ?? rs?.CaptureBattleTeam();
        _battlePartyIds = snapshot?.MemberIds ?? System.Array.Empty<string>();
        _battlePartyIndex = snapshot?.InitialIndex ?? 0;
        if (_battlePartyIds.Length == 0 || _player == null)
        {
            GD.PushError("GameManager: combat entry has no valid battle team.");
            return;
        }

        string initialId = _battlePartyIds[_battlePartyIndex];
        if (_player.Name.ToString() == initialId) return;
        PackedScene scene = ProtagonistRoster.GetScene(initialId);
        if (scene == null)
        {
            GD.PushError($"GameManager: battle member '{initialId}' has no registered scene.");
            return;
        }
        _player = ReplacePlayerNode(_player, scene);
    }

    /// <summary>
    /// Debug-mode mid-combat protagonist swap (D1), driven by <see cref="DebugManager"/>'s
    /// protagonist page. Never touches run economy: HP write-back still targets
    /// <c>RunState.Owned[0]</c> regardless of which debug body is currently worn. Returns false
    /// (no-op) on any guard failure.
    /// </summary>
    public bool DebugSwapProtagonist(string id)
    {
        if (DebugManager.Instance == null || !DebugManager.Instance.Enabled) return false;
        if (_player == null || !IsInstanceValid(_player)) return false;
        if (_ended || _goalResolved) return false;      // match already over
        if (_player.HpRatio <= 0f) return false;         // dead body awaiting respawn/run-end
        if (_player.Name.ToString() == id) return false; // already worn
        var scene = ProtagonistRoster.GetScene(id);
        if (scene == null) return false;

        // Debug selection preserves the visible HP ratio but must not copy the outgoing
        // body's magazine/reload state into a different character's BA controller.
        float carriedRatio = _player.HpRatio;
        if (_hasRun && _protagonist != null) _protagonist.HpRatio = carriedRatio;

        _player.HpChanged -= OnPlayerHpChanged;
        _player.Died -= OnPlayerDied;

        _player = ReplacePlayerNode(_player, scene, switched: true);

        SetupPlayer(RunState.Instance);   // re-subscribes signals, re-wires HUD, hydrates/lives
        _player.ResetDebugCombatState();  // debug body starts immediately testable; state stays non-persistent

        DebugManager.Instance.LogSystem($"Protagonist swap → {id}");
        return true;
    }

    /// <summary>
    /// Mid-combat protagonist switch via Tab (NODES §3.3). Writes back the current HP ratio,
    /// cycles to the next active party member, physically replaces the player body, and applies
    /// a 12s cooldown. No-op when the party has fewer than 2 members, the cooldown is active, the
    /// player is dead, or no run exists.
    /// </summary>
    private void TrySwitchProtagonist()
    {
        // Guards (NODES §3.3: Tab unavailable with 1 protagonist; 12s cooldown)
        if (!_hasRun) return;
        if (_switchCd > 0f) return;
        if (_player == null || !IsInstanceValid(_player)) return;
        if (!_player.CanSwitchProtagonist) return;
        if (_player.HpRatio <= 0f) return; // dead — permadeath, switching won't save you
        if (_ended || _goalResolved) return;

        var rs = RunState.Instance;
        if (_battlePartyIds.Length < 2) return; // short party — nothing to cycle to

        // Capture the outgoing protagonist's HP ratio BEFORE cycling — the incoming
        // protagonist inherits it (NODES §3.3: "HP ratio inheritance").
        float carriedRatio = _player.HpRatio;

        // Write back HP and skill cooldowns to the outgoing protagonist's run state
        WriteBackHp();
        _player.SaveCooldownsToState(_protagonist);

        // The Arena owns this combat-local cursor. Never reread the editable
        // shelter build after battle entry.
        int nextIndex = (_battlePartyIndex + 1) % _battlePartyIds.Length;
        string nextId = _battlePartyIds[nextIndex];

        // Carry the HP ratio into the incoming protagonist's run state
        var incoming = rs.FindProtagonist(nextId);
        if (incoming == null)
        {
            GD.PushError($"GameManager: captured battle member '{nextId}' is no longer owned.");
            return;
        }
        incoming.HpRatio = carriedRatio;

        var scene = ProtagonistRoster.GetScene(nextId);
        if (scene == null)
        {
            DebugManager.Instance?.LogSystem($"Switch protagonist FAILED: no scene for '{nextId}'");
            return;
        }

        // Unsubscribe from old player before replacing the body
        _player.HpChanged -= OnPlayerHpChanged;
        _player.Died -= OnPlayerDied;

        _battlePartyIndex = nextIndex;
        _player = ReplacePlayerNode(_player, scene, switched: true);

        // Re-wire everything for the new body: signals, HUD, hydration from the new protagonist's
        // run state. SetupPlayer reads the captured party's local cursor.
        SetupPlayer(rs);

        // Apply the switch cooldown
        _switchCd = SwitchCooldown;

        UpdateNextMugshot();

        DebugManager.Instance?.LogSystem($"Protagonist switch → {nextId} ({_battlePartyIds.Length}-party, {SwitchCooldown}s CD)");
    }

    /// <summary>
    /// Tick benched protagonists' skill cooldowns at the reduced background rate
    /// (NODES §3.3). Rate = 1/(2n−2): 2-party → 0.5×, 3-party → 0.25×, 4-party → 0.167×.
    /// Only the active protagonist's cooldowns tick at full speed (in their own _Process).
    /// </summary>
    private void TickBackgroundCooldowns(float dt)
    {
        if (!_hasRun) return;
        var rs = RunState.Instance;
        int n = _battlePartyIds.Length;
        if (n < 2) return;
        float bgRate = 1f / (2f * n - 2f);
        float bgDt = dt * bgRate;
        for (int i = 0; i < n; i++)
        {
            if (i == _battlePartyIndex) continue; // active protagonist ticks at 1× in its own _Process
            var p = rs.FindProtagonist(_battlePartyIds[i]);
            if (p == null) continue;
            if (p.ShiftCdRemaining > 0f) p.ShiftCdRemaining = Mathf.Max(0f, p.ShiftCdRemaining - bgDt);
            if (p.ESkillCdRemaining > 0f) p.ESkillCdRemaining = Mathf.Max(0f, p.ESkillCdRemaining - bgDt);
            if (p.ShiftAltCdRemaining > 0f) p.ShiftAltCdRemaining = Mathf.Max(0f, p.ShiftAltCdRemaining - bgDt);
            if (p.ESkillAltCdRemaining > 0f) p.ESkillAltCdRemaining = Mathf.Max(0f, p.ESkillAltCdRemaining - bgDt);
            AmmoController.TickPersisted(p, bgDt);
        }
    }

    /// <summary>
    /// Physically replace the current player body with a fresh instance of <paramref name="scene"/>
    /// at the same world position. Caller owns unsubscribing any signals on <paramref name="old"/>
    /// first. Preserves velocity so a mid-air switch continues the trajectory (NODES §3.3).
    /// </summary>
    private CharacterController ReplacePlayerNode(CharacterController old, PackedScene scene, bool switched = false)
    {
        // A swap during a fight is a switch (`switch.out` / `switch.in`); replacing the
        // scene's placeholder before the fight is just the fighter's `spawn`.
        if (switched) old.NotifySwitchedOut();
        Vector2 pos = old.GlobalPosition;
        Node parent = old.GetParent();
        var oldCamera = old.GetNodeOrNull<Camera2D>("Camera2D");

        var np = scene.Instantiate<CharacterController>();
        parent.AddChild(np);              // _Ready runs here — configure after
        np.GlobalPosition = pos;
        // Carry over velocity BEFORE QueueFree so InheritVelocityFrom can read old's private fields
        // (mid-air switch — NODES §3.3).
        np.InheritVelocityFrom(old);
        // _Ready already latched _spawnPoint at the scene-file origin (it runs inside AddChild,
        // before the line above) — re-anchor it or a debug-lives Respawn() teleports to (0,0).
        np.SetSpawnPoint(pos);
        if (switched) np.NotifySwitchedIn();

        // Camera2D currentness is not inherited through a body replacement. Explicitly
        // retire the outgoing camera and make the incoming character's camera current
        // before the old node is deferred for deletion.
        if (oldCamera != null) oldCamera.Enabled = false;
        var newCamera = np.GetNodeOrNull<Camera2D>("Camera2D");
        if (newCamera == null)
            GD.PushError($"GameManager: protagonist '{np.Name}' has no Camera2D child.");
        else
        {
            // An authored combat map overwrites the original body's scene-default
            // limits during setup; a swap must retain those runtime bounds too.
            if (oldCamera != null)
            {
                newCamera.LimitLeft = oldCamera.LimitLeft;
                newCamera.LimitTop = oldCamera.LimitTop;
                newCamera.LimitRight = oldCamera.LimitRight;
                newCamera.LimitBottom = oldCamera.LimitBottom;
            }
            newCamera.Enabled = true;
            newCamera.MakeCurrent();
        }

        // Foes poll GetFirstNodeInGroup("player") every tick — QueueFree alone leaves the dying
        // node in the group until end of frame, so pull it out of the group immediately.
        old.RemoveFromGroup("player");
        old.SetProcess(false);
        old.SetPhysicsProcess(false);
        old.QueueFree();

        return np;
    }

    private void SetupHud()
    {
        string title = $"{FoeStats.DifficultyName(FoeLevel)} {MissionTable.DifficultyName(_nodeLevel)} — {MissionName(_missionType)}";
        _hud.SetMissionTitle(title);
        // The rules of this place: listed for the whole fight, and the first said once as the
        // fight opens (the law of a realm, or which zone 6 this is).
        var ruleLines = _rules.HudLines();
        _hud.SetRulesLines(ruleLines);
        if (ruleLines.Count > 0) _hud.ShowToast(ruleLines[0], 4f);
        _hud.ConfigureFinishDay(_hasRun);
        _hud.SetFinishDayEnabled(false);
        _hud.FinishDayPressed += OnFinishDayPressed;
        DebugManager.Instance.SkipRequested += OnSkipRequested;
        _hud.RewardChoicePressed += OnRewardChoicePressed;
        _hud.HideBanner();
        PushMissionHud();
    }

    private static string MissionName(MissionType t) => t switch
    {
        MissionType.Collection => "Collection",
        MissionType.Protect => "Protect",
        MissionType.Destroy => "Destroy",
        MissionType.Slaughter => "Slaughter",
        MissionType.Boss => "Boss",
        _ => "",
    };

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (!_hasRun && _ended)
        {
            if (Input.IsActionJustPressed("restart"))
            {
                GetTree().Paused = false;
                GetTree().ReloadCurrentScene();
            }
            return;
        }

        // This node normally processes while paused for the debug win/restart path above. A real
        // pause menu must freeze every combat timer, spawn, and mission tick instead.
        if (GetTree().Paused) return;

        // Universal pre-combat grace: no foe source, including mission-owned waves and bosses,
        // may begin until the Arena opens the shared spawner gate. Keep the countdown HUD
        // separate from mission HUD so its text cannot be overwritten this frame.
        if (_graceTimer > 0f)
        {
            // One tick per whole second shown (3, 2, 1), raised before the number changes.
            int shown = Mathf.CeilToInt(_graceTimer);
            if (shown != _countdownShown)
            {
                _countdownShown = shown;
                RaiseFlow("countdown.tick", variant: shown);
            }
            _graceTimer = Mathf.Max(0f, _graceTimer - dt);
            _hud.SetProgress(_graceTimer > 0.01f
                ? $"Get ready... {Mathf.CeilToInt(_graceTimer)}\n{Fableland.UI.ControlsCard.OneLine()}"
                : "Go!");
        }
        else if (_graceTimer == 0f)
        {
            // Grace just ended — release all deferred waves, then let the mission resume.
            _graceTimer = -1f;
            Spawner?.OpenSpawnGate();
            RaiseFlow("countdown.go");
            RaiseFlow("mission.start", MissionTag(_missionType), _nodeLevel);
        }

        Spawner?.Tick(dt);

        // Tab protagonist switch (NODES §3.3) — available mid-combat, 12s CD, only when
        // party has 2+ members. Tick cooldown every frame.
        if (_switchCd > 0f) _switchCd -= dt;
        if (Input.IsActionJustPressed("switch_protagonist"))
            TrySwitchProtagonist();

        // Held-item second cooldowns and active effects belong to the active body,
        // so Pixolotl's local-time boost advances them with her other timers.
        _itemRuntime?.Tick(_player != null ? _player.GetActDelta(dt) : dt);
        if (Input.IsActionJustPressed("use_item"))
        {
            string reason = null;
            string activatedName = _itemRuntime?.Definition?.DisplayName;
            bool used = _player != null && _player.CanUseHeldItem
                && _itemRuntime != null && _itemRuntime.TryUse(out reason);
            if (!used)
                _hud.ShowToast(reason ?? "That action is unavailable right now.");
            else
            {
                bool converted = _itemRuntime.RebindRequested;
                string newName = _protagonist?.HeldItemDefId != null
                    ? ItemCatalog.DisplayName(_protagonist.HeldItemDefId) : null;
                if (converted)
                {
                    _itemRuntime.Dispose();
                    _itemRuntime = new ItemRuntime(_player, _protagonist);
                }
                _hud.ShowToast(converted ? $"{activatedName} became {newName}." : $"{activatedName} activated.");
            }
        }

        // Background cooldown tick for benched protagonists (NODES §3.3): recover at
        // rate = 1/(2n−2) — 2-party → 0.5×, 3-party → 0.25×, 4-party → 0.167×.
        TickBackgroundCooldowns(dt);

        // Push switch-protagonist slot state to the HUD every frame (CD overlay + label).
        PushSwitchHud();
        PushItemHud();

        if (_mission == null) return;
        // QA cheat (40-QA §1): F9 in a debug build force-completes the goal — lets a tester
        // walk the whole run loop without playing out every mission.
        if (OS.IsDebugBuild() && _mission.Status == MissionStatus.Running
            && Input.IsKeyPressed(Key.F9))
            _mission.DebugForceComplete();

        // Don't tick the mission during the grace period — its timer is frozen while the
        // player gets their bearings (grace = -1f once the 3s countdown finishes).
        if (_mission.Status == MissionStatus.Running && _graceTimer < 0f && !_dying) _mission.Tick(dt);
        if (_graceTimer < 0f) PushMissionHud();

        if (_mission.Status != MissionStatus.Running && !_goalResolved && !_dying) OnMissionResolved();
    }

    private void PushMissionHud()
    {
        _hud.SetProgress(_mission.ProgressText);
        _hud.SetTimer(_mission.HasTimer, _mission.TimeRemaining);
        // flow/mission.time: the mission's clock is running out — raised once at 30, 20 and 10
        // seconds, then every second from 5. Variant = whole seconds left on the HUD clock.
        if (_mission.HasTimer && _mission.Status == MissionStatus.Running)
        {
            int left = Mathf.CeilToInt(_mission.TimeRemaining);
            if (left != _timeCueShown)
            {
                _timeCueShown = left;
                if (left == 30 || left == 20 || left == 10 || (left >= 1 && left <= 5))
                    RaiseFlow("mission.time", MissionTag(_missionType), left);
            }
        }
        _hud.SetSecondaryBar(_mission.HasSecondaryBar, _mission.SecondaryValue, _mission.SecondaryMax, _mission.SecondaryLabel);
        _hud.SetSecondaryNotch(_mission.HasSecondaryBar ? _mission.SecondaryNotch : 0f);
        _hud.ShowRewardChoice(_mission.NeedsRewardChoice);
        string said = _mission.ConsumeAnnouncement();
        if (!string.IsNullOrEmpty(said)) _hud.ShowToast(said, 3.5f);
    }

    /// <summary>Push the switch-protagonist slot state to the HUD every frame:
    /// cooldown overlay fraction and seconds-remaining label.</summary>
    private void PushSwitchHud()
    {
        _hud.SetSwitchCooldown(_switchCd, SwitchCooldown);
    }

    private void PushItemHud()
    {
        if (_protagonist == null || string.IsNullOrWhiteSpace(_protagonist.HeldItemDefId))
        {
            _hud.SetHeldItem(null, 0f, 0f);
            return;
        }
        float max = ItemCatalog.TryGet(_protagonist.HeldItemDefId, out ItemDef definition)
            ? definition.SecondCooldownSeconds : 0f;
        _hud.SetHeldItem(_protagonist.HeldItemDefId, _protagonist.HeldItemSecondCooldownRemaining, max);
    }

    // ── Goal resolution ───────────────────────────────────────────────────────────────────

    private void OnMissionResolved()
    {
        _goalResolved = true;
        Spawner.Enabled = false;   // FOES §11: the cap only gates the periodic spawner anyway
        _mission.OnResolved();     // e.g. a boss still standing after a debug skip stands down
        RaiseFlow(_mission.Status == MissionStatus.Succeeded ? "mission.success" : "mission.fail",
            MissionTag(_missionType), _nodeLevel);

        // Resolution is a terminal combat state: both sides stop taking damage while
        // the reward/Finish Day flow is visible. The controller-level gate covers
        // direct hits, hazards, and already-running damage-over-time effects.
        if (_player != null && IsInstanceValid(_player))
            _player.SetInvincible(CharacterController.ResolvedInvincibleSource, true);

        // All living foes become invincible — the mission is over, the player shouldn't
        // be able to farm kills or take stray damage after the objective resolves.
        foreach (var node in GetTree().GetNodesInGroup("foe"))
        {
            if (node is BaseFoe foe && IsInstanceValid(foe))
                foe.Invincible = true;
        }

        if (_hasRun)
        {
            WriteBackHp();

            // A zone-6 mini-boss is survivable (Docs/Zone6.gdd §3): its timer running out is a
            // defeat the run lives through, not a kill — that branch is for every other boss.
            if (_mission.Status == MissionStatus.Failed && _mission.FatalTimeout && !_rules.SurvivableDefeat)
            {
                // A fatal boss timer is a real boss kill. TwistedReality may consume itself to
                // return the run to its last completed day; every other timer loss is terminal.
                if (RunState.Instance.TryRecoverFromBossFailure()) return;
                RaiseFlow("run.over", "boss_timer");
                RunState.Instance.EndRun(RunEndKind.BossTimer);
                return;
            }

            bool success = _mission.Status == MissionStatus.Succeeded;
            RunState.Instance.ReportGoal(success, success ? _mission.Reward() : null);
            // RunState has floored the battle team's HP; the fallen body writes nothing more.
            if (!success && _rules.SurvivableDefeat) _hpSettled = true;

            // Goal and non-fatal timer expiry are checkpoints. This happens after ReportGoal so
            // node completion/rewards/failure bounce are durable before the player can Finish Day.
            if (!RunState.Instance.SaveActiveRun(out string saveError))
                GD.PushError($"[GameManager] Mission-resolution save failed: {saveError}");

            if (success && _mission.IsFinalBoss)
            {
                RaiseFlow("run.over", "victory");
                RunState.Instance.EndRun(RunEndKind.Victory);
                return;
            }

            string note = RunState.Instance.LastZone6Note;
            string toast = success ? _mission.SuccessToast : _rules.SurvivableDefeat ? "Defeated — you wake back on the map." : "Mission failed.";
            // Name what the goal paid (audit v0.11.1 #2).
            string reward = success ? RunState.Instance.LastRewardLine : "";
            if (reward.Length > 0) note = note.Length > 0 ? reward + " " + note : reward;
            _hud.ShowToast(string.IsNullOrEmpty(note) ? toast : toast + " " + note, string.IsNullOrEmpty(note) ? 2.5f : 6f);
            if (_rules.InZone6) _hud.SetRulesLines(RulesAfterResult());
            _hud.SetFinishDayEnabled(true);
        }
        else
        {
            bool success = _mission.Status == MissionStatus.Succeeded;
            EndGameDebug(success ? "GOAL ACHIEVED!\nPress R to play again" : "MISSION FAILED\nPress R to try again");
        }
    }

    /// <summary>The rules label once a zone-6 fight has changed the run: the fight's own lines,
    /// then where zone 6 stands now.</summary>
    private System.Collections.Generic.List<string> RulesAfterResult()
    {
        var lines = _rules.HudLines();
        string status = RunState.Instance?.Zone6?.StatusLine() ?? "";
        if (status.Length > 0) lines.Add("Now: " + status);
        return lines;
    }

    private void OnFinishDayPressed()
    {
        if (!_hasRun || !_goalResolved) return;

        var dlg = new ConfirmationDialog { DialogText = "Finish the Day?", Title = "Adventure" };
        AddChild(dlg);
        dlg.Confirmed += () =>
        {
            dlg.QueueFree();
            WriteBackHp();
            RunState.Instance.EndDay();
        };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }

    private void OnRewardChoicePressed(bool atk)
    {
        _mission?.ChooseReward(atk);
    }

    private void OnSkipRequested()
    {
        if (_mission == null) return;
        if (_mission.Status == MissionStatus.Running)
        {
            _mission.DebugForceComplete();
            return;
        }
        if (_mission.Status == MissionStatus.Failed && _goalResolved)
        {
            _mission.DebugForceComplete();
            WriteBackHp();
            if (!_hasRun)
            {
                EndGameDebug("SKIPPED!\nPress R to play again");
                return;
            }
            RunState.Instance.ReportGoal(true, _mission.Reward());
            if (!RunState.Instance.SaveActiveRun(out string saveError))
                GD.PushError($"[GameManager] Debug-skip save failed: {saveError}");
            if (_mission.IsFinalBoss)
            {
                RunState.Instance.EndRun(RunEndKind.Victory);
                return;
            }
            _hud.ShowToast("Skipped — marked as victory!");
            _hud.SetFinishDayEnabled(true);
        }
    }

    private void WriteBackHp()
    {
        if (_hasRun && _player != null && !_hpSettled)
            _player.WriteBackToState(_protagonist);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("pause")) return;
        GetViewport().SetInputAsHandled();
        PauseMenu.Open(this, SaveBattleAndQuit);
    }

    /// <summary>Pause-menu checkpoint boundary. The active body owns live HP, ammo, and skill
    /// cooldowns, so copy them into its ProtagonistState before RunState serializes the run.</summary>
    private bool SaveBattleAndQuit()
    {
        if (!_hasRun || RunState.Instance == null) return false;
        WriteBackHp();
        if (_player != null && _protagonist != null)
            _player.SaveCooldownsToState(_protagonist);
        return RunState.Instance.SaveAndQuit(resumeUnfinishedBattle: !_goalResolved);
    }

    // ── Player death ──────────────────────────────────────────────────────────────────────

    private void OnPlayerHpChanged(float cur, float max, float shield, float tempHP) =>
        _hud.SetHp(cur, max, shield, tempHP);

    private async void OnPlayerDied()
    {
        RaiseFlow("player.down");
        if (_hasRun)
        {
            WriteBackHp();   // CurrentHP is already 0 here, so this writes back ~0
            // A zone-6 mini-boss beats you without ending the run (Docs/Zone6.gdd §3): the fight
            // is lost, the run goes on — resolved as a failed mission, which RunState turns
            // into the variant's penalty and a battle team on at least a quarter of its HP.
            if (_rules.SurvivableDefeat && _mission != null && !_goalResolved)
            {
                _mission.Concede();
                OnMissionResolved();
                return;
            }
            if (RunState.Instance.CurrentAdventure?.Kind == NodeKind.Boss
                && RunState.Instance.TryRecoverFromBossFailure()) return;
            // Permadeath — no lives, no respawn in a run (NODES §2.2). A beat first: the fall is
            // seen, the mission's clock stops, then the run-over screen.
            _dying = true;
            RaiseFlow("run.over", "death");
            _hud.ShowBanner("You fell.");
            await ToSignal(GetTree().CreateTimer(DeathBeatSeconds), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || RunState.Instance.RunFinished) return;
            RunState.Instance.EndRun(RunEndKind.Death);
            return;
        }

        // Debug fallback: the old lives/respawn/restart loop.
        _hud.SetLives(_player.LivesRemaining);
        if (_player.LivesRemaining <= 0)
        {
            RaiseFlow("run.over", "death");
            EndGameDebug("GAME OVER\nPress R to try again");
            return;
        }
        await ToSignal(GetTree().CreateTimer(RespawnDelay), SceneTreeTimer.SignalName.Timeout);
        if (!_ended)
        {
            _player.SetSpawnPoint(NextRespawnPoint());
            _player.Respawn();
            ArenaRulesApplier.ApplyToFighter(_player, _rules);   // Respawn clears the defense pool
        }
    }

    private void EndGameDebug(string message)
    {
        _ended = true;
        _hud.ShowBanner(message);
        GetTree().Paused = true;
    }

    // ── Foe/placement services the mission layer expects ─────────────────────────────────

    /// <summary>Pick a spawn point among the "enemy_spawn" markers (or the play space if none),
    /// aerial ⇒ 3–5 m above ground (C1: seagulls patrol at spawn height).</summary>
    public Vector2 RandomFoeSpawn(DetRandom rng, bool aerial)
    {
        if (_usingAuthoredMap && _authoredEnemySpawns.Count > 0)
        {
            var eligible = new System.Collections.Generic.List<CombatMapSpawnPoint>();
            for (int i = 0; i < _authoredEnemySpawns.Count; i++)
            {
                CombatMapSpawnPoint point = _authoredEnemySpawns[i];
                if (IsEligibleFoeSpawn(point, aerial)) eligible.Add(point);
            }
            if (eligible.Count > 0)
            {
                CombatMapSpawnPoint nest = eligible[rng.Range(0, eligible.Count - 1)];
                if (aerial)
                    return nest.Position - new Vector2(0f, Units.Px(rng.Range(3, 5)));
                return nest.Position;
            }
        }

        float x = PickSpawnX(rng);
        if (aerial)
        {
            float upPx = Units.Px(rng.Range(3, 5));
            return new Vector2(x, ArenaBuilder.GroundTopY - upPx);
        }
        return new Vector2(x, ArenaBuilder.GroundTopY - 20f);
    }

    /// <summary>Whether this map contains at least one valid nest for crab (ground) or seagull
    /// (aerial) spawning. With no authored map the legacy Arena markers remain valid.</summary>
    public bool HasFoeSpawnFor(bool aerial)
    {
        if (!_usingAuthoredMap) return true;
        for (int i = 0; i < _authoredEnemySpawns.Count; i++)
            if (IsEligibleFoeSpawn(_authoredEnemySpawns[i], aerial)) return true;
        return false;
    }

    private bool IsEligibleFoeSpawn(CombatMapSpawnPoint point, bool aerial)
    {
        if (!aerial)
        {
            int? maxY = _authoredFoeSpawnRules?.CrabMaxCellY;
            return !maxY.HasValue || point.CellY <= maxY.Value;
        }
        int? minY = _authoredFoeSpawnRules?.SeagullMinCellY;
        return !minY.HasValue || point.CellY >= minY.Value;
    }

    private float PickSpawnX(DetRandom rng)
    {
        if (_foeSpawnMarkers != null && _foeSpawnMarkers.Count > 0)
        {
            var marker = _foeSpawnMarkers[rng.Range(0, _foeSpawnMarkers.Count - 1)] as Node2D;
            if (marker != null) return marker.GlobalPosition.X;
        }
        return rng.Range((int)ArenaBuilder.PlayLeft, (int)ArenaBuilder.PlayRight);
    }

    /// <summary>Pick a placement point for a mission entity (core/objective/pickup): a mix of
    /// ArenaBuilder's platform surface points and ground-level points within the play space.</summary>
    public Vector2 RandomPlacementPoint(DetRandom rng)
    {
        if (_usingAuthoredMap)
        {
            if (_surfacePoints.Count > 0 && rng.Chance(0.5))
                return _surfacePoints[rng.Range(0, _surfacePoints.Count - 1)];
            float authoredX = rng.Range((int)Units.Px(2f), Mathf.Max((int)Units.Px(2f), (int)_authoredMapWidth - (int)Units.Px(2f)));
            return new Vector2(authoredX, Mathf.Max(Units.Px(2f), _authoredMapHeight - MapGrid.PixelsPerCell * 2f - Units.Px(1f)));
        }
        if (_surfacePoints.Count > 0 && rng.Chance(0.5))
            return _surfacePoints[rng.Range(0, _surfacePoints.Count - 1)];
        float x = rng.Range((int)ArenaBuilder.PlayLeft, (int)ArenaBuilder.PlayRight);
        return new Vector2(x, ArenaBuilder.GroundTopY - 30f);
    }

    /// <summary>
    /// Mission objective placement: claim uses these for Wonder Core spawns, protect for the
    /// Condensed Core, and destroy for enemy objectives. Slaughter never calls this method.
    /// Missing markers degrade to normal arena placement, while content validation logs the gap.
    /// </summary>
    public Vector2 RandomLevelGoalPoint(DetRandom rng)
    {
        if (_authoredLevelGoalSpawns.Count > 0)
            return _authoredLevelGoalSpawns[rng.Range(0, _authoredLevelGoalSpawns.Count - 1)];
        return RandomPlacementPoint(rng);
    }

    private Vector2 NextRespawnPoint()
    {
        if (_authoredRespawnSpawns.Count == 0)
            return _usingAuthoredMap ? _authoredCharacterSpawn : _player?.GlobalPosition ?? Vector2.Zero;
        Vector2 point = _authoredRespawnSpawns[_nextRespawnIndex % _authoredRespawnSpawns.Count];
        _nextRespawnIndex++;
        return point;
    }
}

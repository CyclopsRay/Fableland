using Godot;
using Fableland.Map;
using Fableland.Missions;
using Fableland.Run;
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

    [Export] public float RespawnDelay = 1.2f;

    /// <summary>The "Entities" child every spawned foe/pickup/objective is parented under.</summary>
    public Node2D Entities { get; private set; }

    /// <summary>The arena's foe-spawning service (owned/ticked here, configured by the mission).</summary>
    public FoeSpawner Spawner { get; private set; }

    /// <summary>Current foe level for this fight (debug override, else day-based; FOES §2).</summary>
    public int FoeLevel { get; private set; }

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

    private float _graceTimer = 3f;      // 3s pre-combat grace: no foes, timer frozen, countdown shown
    private bool _ended;   // debug-only local end-of-match latch (banner + restart)

    public override void _Ready()
    {
        // Keep running while the tree is paused so the debug restart flow works after win/lose.
        ProcessMode = ProcessModeEnum.Always;

        Entities = GetNode<Node2D>(EntitiesPath);
        _world = GetNode<Node2D>(WorldPath);
        _hazards = GetNode<Node2D>(HazardsPath);
        _hud = GetNode<Hud>(HudPath);
        _player = GetTree().GetFirstNodeInGroup("player") as CharacterController;

        var rs = RunState.Instance;
        var adv = rs?.CurrentAdventure;
        _hasRun = adv != null;

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

        string nodeId = adv?.NodeId ?? "debug";
        _nodeLevel = adv?.NodeLevel ?? 1;
        _missionType = adv?.Mission ?? MissionType.Collection;
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
        _usingAuthoredMap = TryBuildSelectedMap(combatMapPath);
        if (!_usingAuthoredMap)
        {
            var platformTex = GD.Load<Texture2D>("res://Assets/Sprites/Gameplay/World/platform_placeholder.svg");
            _surfacePoints = ArenaBuilder.Build(_world, _hazards, _rng, platformTex, FireHazardScene, FreezeHazardScene);
        }

        Spawner = new FoeSpawner(this, _rng.Sub("spawn")) { Enabled = false }; // armed after grace
        ApplySelectedMapFoeComposition(combatMapPath);

        _mission = CreateMission(_missionType);
        _mission.Setup(this, _nodeLevel, _rng.Sub("mission"));
        DebugManager.Instance?.LogMission($"Mission start: {MissionName(_missionType)} LV{_nodeLevel} foeLV{FoeLevel} day{day} node{nodeId}");
        SetupPlayer(rs);
        SetupHud();
    }

    public override void _ExitTree()
    {
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

        var camera = _player?.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera != null)
        {
            camera.LimitLeft = 0;
            camera.LimitTop = 0;
            camera.LimitRight = Mathf.CeilToInt(_authoredMapWidth);
            camera.LimitBottom = Mathf.CeilToInt(_authoredMapHeight);
        }

        var backdrop = GetNodeOrNull<ColorRect>("Background");
        if (backdrop != null && !string.IsNullOrWhiteSpace(document.Canvas?.Color))
        {
            try { backdrop.Color = new Color(document.Canvas.Color); }
            catch { GD.PushWarning("[GameManager] invalid canvas color on authored map '" + document.Name + "'; keeping Arena backdrop."); }
        }

        DebugManager.Instance?.LogMission("Combat map: " + document.Name + " (" + document.Id + ")");
        return true;
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

        _player = ReplacePlayerNode(_player, scene);

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
        _player = ReplacePlayerNode(_player, scene);

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
            AmmoController.TickPersisted(p, bgDt);
        }
    }

    /// <summary>
    /// Physically replace the current player body with a fresh instance of <paramref name="scene"/>
    /// at the same world position. Caller owns unsubscribing any signals on <paramref name="old"/>
    /// first. Preserves velocity so a mid-air switch continues the trajectory (NODES §3.3).
    /// </summary>
    private CharacterController ReplacePlayerNode(CharacterController old, PackedScene scene)
    {
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

        // 3-second pre-combat grace period: no foe spawns, mission timer frozen, countdown
        // shown so the player can orient before the fight begins. Must run BEFORE
        // Spawner.Tick so the spawner is already disabled on the very first frame.
        if (_graceTimer > 0f)
        {
            _graceTimer -= dt;
            _hud.SetProgress(_graceTimer > 0.01f
                ? $"Get ready... {Mathf.CeilToInt(_graceTimer)}"
                : "Go!");
            // Keep the spawner suppressed during grace; the mission timer is also paused
            // (we skip _mission.Tick below).
            Spawner.Enabled = false;
        }
        else if (_graceTimer != -1f)
        {
            // Grace just ended — arm the spawner and let the mission resume.
            _graceTimer = -1f;
            if (Spawner != null) Spawner.Enabled = true;
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
        if (_mission.Status == MissionStatus.Running && _graceTimer < 0f) _mission.Tick(dt);
        PushMissionHud();

        if (_mission.Status != MissionStatus.Running && !_goalResolved) OnMissionResolved();
    }

    private void PushMissionHud()
    {
        _hud.SetProgress(_mission.ProgressText);
        _hud.SetTimer(_mission.HasTimer, _mission.TimeRemaining);
        _hud.SetSecondaryBar(_mission.HasSecondaryBar, _mission.SecondaryValue, _mission.SecondaryMax, _mission.SecondaryLabel);
        _hud.ShowRewardChoice(_mission.NeedsRewardChoice);
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

        // Resolution is a terminal combat state: both sides stop taking damage while
        // the reward/Finish Day flow is visible. The controller-level gate covers
        // direct hits, hazards, and already-running damage-over-time effects.
        if (_player != null && IsInstanceValid(_player))
            _player.Invincible = true;

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

            if (_mission.Status == MissionStatus.Failed && _mission.FatalTimeout)
            {
                // A fatal boss timer is a real boss kill. TwistedReality may consume itself to
                // return the run to its last completed day; every other timer loss is terminal.
                if (RunState.Instance.TryRecoverFromBossFailure()) return;
                RunState.Instance.EndRun(RunEndKind.BossTimer);
                return;
            }

            bool success = _mission.Status == MissionStatus.Succeeded;
            RunState.Instance.ReportGoal(success, success ? _mission.Reward() : null);

            // Goal and non-fatal timer expiry are checkpoints. This happens after ReportGoal so
            // node completion/rewards/failure bounce are durable before the player can Finish Day.
            if (!RunState.Instance.SaveActiveRun(out string saveError))
                GD.PushError($"[GameManager] Mission-resolution save failed: {saveError}");

            if (success && _mission.IsFinalBoss)
            {
                RunState.Instance.EndRun(RunEndKind.Victory);
                return;
            }

            _hud.ShowToast(success ? "Goal achieved!" : "Mission failed.");
            _hud.SetFinishDayEnabled(true);
        }
        else
        {
            bool success = _mission.Status == MissionStatus.Succeeded;
            EndGameDebug(success ? "GOAL ACHIEVED!\nPress R to play again" : "MISSION FAILED\nPress R to try again");
        }
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
        if (_hasRun && _player != null)
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
        if (_hasRun)
        {
            WriteBackHp();   // CurrentHP is already 0 here, so this writes back ~0
            if (RunState.Instance.CurrentAdventure?.Kind == NodeKind.Boss
                && RunState.Instance.TryRecoverFromBossFailure()) return;
            // Permadeath — no lives, no respawn in a run (NODES §2.2).
            RunState.Instance.EndRun(RunEndKind.Death);
            return;
        }

        // Debug fallback: the old lives/respawn/restart loop.
        _hud.SetLives(_player.LivesRemaining);
        if (_player.LivesRemaining <= 0)
        {
            EndGameDebug("GAME OVER\nPress R to try again");
            return;
        }
        await ToSignal(GetTree().CreateTimer(RespawnDelay), SceneTreeTimer.SignalName.Timeout);
        if (!_ended)
        {
            _player.SetSpawnPoint(NextRespawnPoint());
            _player.Respawn();
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

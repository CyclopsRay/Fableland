using Godot;
using Fableland.Map;
using Fableland.Missions;
using Fableland.Run;
using Fableland.Debug;

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
    private Godot.Collections.Array<Node> _foeSpawnMarkers;
    private System.Collections.Generic.List<Vector2> _surfacePoints = new();

    private DetRandom _rng;
    private Mission _mission;
    private ProtagonistState _protagonist;   // hydrated protagonist for HP write-back
    private int _activePartyIndex;           // index into RunState.ActiveBuild (NODES §3.3)
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

        // Debug-mode protagonist override (D1): apply BEFORE any RunState/mission/SetupPlayer
        // wiring below, so nothing has to be re-wired — SetupPlayer(rs) further down wires the
        // swapped-in body with zero extra code. Byte-for-byte no-op when debug is off or no
        // selection exists.
        if (DebugManager.Instance != null && DebugManager.Instance.Enabled
            && DebugManager.Instance.SelectedProtagonistId is string selId
            && _player != null && selId != _player.Name.ToString())
        {
            var scene = ProtagonistRoster.GetScene(selId);
            if (scene != null)
                _player = ReplacePlayerNode(_player, scene);
        }

        _foeSpawnMarkers = GetTree().GetNodesInGroup("enemy_spawn");

        var rs = RunState.Instance;
        var adv = rs?.CurrentAdventure;
        _hasRun = adv != null;

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

        var platformTex = GD.Load<Texture2D>("res://Sprites/platform_placeholder.svg");
        _surfacePoints = ArenaBuilder.Build(_world, _hazards, _rng, platformTex, FireHazardScene, FreezeHazardScene);

        Spawner = new FoeSpawner(this, _rng.Sub("spawn")) { Enabled = false }; // armed after grace

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
    }

    private static Mission CreateMission(MissionType type) => type switch
    {
        MissionType.Protect => new ProtectMission(),
        MissionType.Destroy => new DestroyMission(),
        MissionType.Slaughter => new SlaughterMission(),
        MissionType.Boss => new BossMission(),
        _ => new CollectionMission(),
    };

    private void SetupPlayer(RunState rs)
    {
        if (_player == null) return;

        _player.HpChanged += OnPlayerHpChanged;
        _player.Died += OnPlayerDied;
        _hud.SetPlayer(_player);
        _hud.SetHp(_player.CurrentHP, _player.MaxHP, _player.Shield, _player.TempHP);

        if (_hasRun && rs.ActiveBuild.Count > 0)
        {
            // Find the protagonist state for the active party slot (NODES §3.3).
            _activePartyIndex = rs.ActiveProtagonistIndex;
            _protagonist = ResolveProtagonistForSlot(rs, _activePartyIndex);
            float baseMaxHp = _player.MaxHP;   // authored default, captured before hydrating
            _player.HydrateRun(baseMaxHp, _protagonist.MaxHpPercentPoints, _protagonist.HpRatio,
                                _protagonist.BonusAtk, _protagonist.BonusDef);
            // Restore saved cooldown remaining from the protagonist's run state (background-CD, NODES §3.3)
            _player.LoadCooldownsFromState(_protagonist);
            _hud.SetLivesVisible(false);       // permadeath in a run — lives don't apply (NODES §2.2)
            UpdateNextMugshot();
        }
        else
        {
            _hud.SetLivesVisible(true);
            _hud.SetLives(_player.LivesRemaining);
        }
    }

    /// <summary>Push the next protagonist's mugshot to the HUD (NODES §3.3).
    /// Shows the portrait of the party member Tab will switch to, or hides it when
    /// the party has fewer than 2 members.</summary>
    private void UpdateNextMugshot()
    {
        var rs = RunState.Instance;
        if (!_hasRun || rs == null || rs.ActiveBuild.Count < 2)
        {
            _hud.SetNextProtagonist(null);
            return;
        }
        int nextIdx = (rs.ActiveProtagonistIndex + 1) % rs.ActiveBuild.Count;
        _hud.SetNextProtagonist(rs.ActiveBuild[nextIdx]);
    }

    /// <summary>Find the <see cref="ProtagonistState"/> for a party slot, safely.
    /// Falls back to Owned[0] when ActiveBuild and Owned are out of sync (shouldn't happen,
    /// but RunState is the truth — Owned always has at least the start protagonist).</summary>
    private static ProtagonistState ResolveProtagonistForSlot(RunState rs, int slot)
    {
        if (slot >= 0 && slot < rs.ActiveBuild.Count)
        {
            var p = rs.FindProtagonist(rs.ActiveBuild[slot]);
            if (p != null) return p;
        }
        return rs.Owned.Count > 0 ? rs.Owned[0] : null;
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
        if (_ended) return false;                       // debug match already over
        if (_player.HpRatio <= 0f) return false;         // dead body awaiting respawn/run-end
        if (_player.Name.ToString() == id) return false; // already worn
        var scene = ProtagonistRoster.GetScene(id);
        if (scene == null) return false;

        WriteBackHp();   // in-run: carry HP ratio into the new body via Owned[0]

        _player.HpChanged -= OnPlayerHpChanged;
        _player.Died -= OnPlayerDied;

        _player = ReplacePlayerNode(_player, scene);

        SetupPlayer(RunState.Instance);   // re-subscribes signals, re-wires HUD, hydrates/lives

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
        if (_player.HpRatio <= 0f) return; // dead — permadeath, switching won't save you
        if (_ended) return;

        var rs = RunState.Instance;
        if (rs.ActiveBuild.Count < 2) return; // short party — nothing to cycle to

        // Capture the outgoing protagonist's HP ratio BEFORE cycling — the incoming
        // protagonist inherits it (NODES §3.3: "HP ratio inheritance").
        float carriedRatio = _player.HpRatio;

        // Write back HP and skill cooldowns to the outgoing protagonist's run state
        WriteBackHp();
        _player.SaveCooldownsToState(_protagonist);

        // Cycle to the next party member (RunState owns the index)
        string nextId = rs.CycleNextProtagonist();
        if (nextId == null) return;

        // Carry the HP ratio into the incoming protagonist's run state
        var incoming = rs.FindProtagonist(nextId);
        if (incoming != null) incoming.HpRatio = carriedRatio;

        var scene = ProtagonistRoster.GetScene(nextId);
        if (scene == null)
        {
            DebugManager.Instance?.LogSystem($"Switch protagonist FAILED: no scene for '{nextId}'");
            return;
        }

        // Unsubscribe from old player before replacing the body
        _player.HpChanged -= OnPlayerHpChanged;
        _player.Died -= OnPlayerDied;

        _player = ReplacePlayerNode(_player, scene);

        // Re-wire everything for the new body: signals, HUD, hydration from the new protagonist's
        // run state. SetupPlayer reads the updated ActiveProtagonistIndex from RunState.
        SetupPlayer(rs);

        // Apply the switch cooldown
        _switchCd = SwitchCooldown;

        UpdateNextMugshot();

        DebugManager.Instance?.LogSystem($"Protagonist switch → {nextId} ({rs.ActiveBuild.Count}-party, {SwitchCooldown}s CD)");
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
        int n = rs.ActiveBuild.Count;
        if (n < 2) return;
        float bgRate = 1f / (2f * n - 2f);
        float bgDt = dt * bgRate;
        for (int i = 0; i < n; i++)
        {
            if (i == _activePartyIndex) continue; // active protagonist ticks at 1× in its own _Process
            var p = rs.FindProtagonist(rs.ActiveBuild[i]);
            if (p == null) continue;
            if (p.ShiftCdRemaining > 0f) p.ShiftCdRemaining = Mathf.Max(0f, p.ShiftCdRemaining - bgDt);
            if (p.ESkillCdRemaining > 0f) p.ESkillCdRemaining = Mathf.Max(0f, p.ESkillCdRemaining - bgDt);
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

        var np = scene.Instantiate<CharacterController>();
        parent.AddChild(np);              // _Ready runs here — configure after
        np.GlobalPosition = pos;
        // Carry over velocity BEFORE QueueFree so InheritVelocityFrom can read old's private fields
        // (mid-air switch — NODES §3.3).
        np.InheritVelocityFrom(old);
        // _Ready already latched _spawnPoint at the scene-file origin (it runs inside AddChild,
        // before the line above) — re-anchor it or a debug-lives Respawn() teleports to (0,0).
        np.SetSpawnPoint(pos);
        np.GetNodeOrNull<Camera2D>("Camera2D")?.MakeCurrent();

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

        // Background cooldown tick for benched protagonists (NODES §3.3): recover at
        // rate = 1/(2n−2) — 2-party → 0.5×, 3-party → 0.25×, 4-party → 0.167×.
        TickBackgroundCooldowns(dt);

        // Push switch-protagonist slot state to the HUD every frame (CD overlay + label).
        PushSwitchHud();

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

    // ── Goal resolution ───────────────────────────────────────────────────────────────────

    private void OnMissionResolved()
    {
        _goalResolved = true;
        Spawner.Enabled = false;   // FOES §11: the cap only gates the periodic spawner anyway

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
                // Boss timer expiry = immediate death (NODES §4.5/§2.2) — bypass the normal
                // survivable-failure bounce entirely.
                RunState.Instance.EndRun(RunEndKind.BossTimer);
                return;
            }

            bool success = _mission.Status == MissionStatus.Succeeded;
            RunState.Instance.ReportGoal(success, success ? _mission.Reward() : null);

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

    // ── Player death ──────────────────────────────────────────────────────────────────────

    private void OnPlayerHpChanged(float cur, float max, float shield, float tempHP) =>
        _hud.SetHp(cur, max, shield, tempHP);

    private async void OnPlayerDied()
    {
        if (_hasRun)
        {
            // Permadeath — no lives, no respawn in a run (NODES §2.2).
            WriteBackHp();   // CurrentHP is already 0 here, so this writes back ~0
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
        if (!_ended) _player.Respawn();
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
        float x = PickSpawnX(rng);
        if (aerial)
        {
            float upPx = Units.Px(rng.Range(3, 5));
            return new Vector2(x, ArenaBuilder.GroundTopY - upPx);
        }
        return new Vector2(x, ArenaBuilder.GroundTopY - 20f);
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
        if (_surfacePoints.Count > 0 && rng.Chance(0.5))
            return _surfacePoints[rng.Range(0, _surfacePoints.Count - 1)];
        float x = rng.Range((int)ArenaBuilder.PlayLeft, (int)ArenaBuilder.PlayRight);
        return new Vector2(x, ArenaBuilder.GroundTopY - 30f);
    }
}

using Godot;
using Fableland.Map;
using Fableland.Missions;
using Fableland.Run;

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
    private ProtagonistState _protagonist;   // hydrated protagonist (Owned[0]) for HP write-back
    private bool _hasRun;
    private int _nodeLevel;
    private MissionType _missionType;
    private bool _goalResolved;

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

        Spawner = new FoeSpawner(this, _rng.Sub("spawn"));

        _mission = CreateMission(_missionType);
        _mission.Setup(this, _nodeLevel, _rng.Sub("mission"));

        SetupPlayer(rs);
        SetupHud();
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
        _hud.SetHp(_player.CurrentHP, _player.MaxHP);

        if (_hasRun && rs.Owned.Count > 0)
        {
            _protagonist = rs.Owned[0];
            float baseMaxHp = _player.MaxHP;   // authored default, captured before hydrating
            _player.HydrateRun(baseMaxHp, _protagonist.MaxHpPercentPoints, _protagonist.HpRatio,
                                _protagonist.BonusAtk, _protagonist.BonusDef);
            _hud.SetLivesVisible(false);       // permadeath in a run — lives don't apply (NODES §2.2)
        }
        else
        {
            _hud.SetLivesVisible(true);
            _hud.SetLives(_player.LivesRemaining);
        }
    }

    private void SetupHud()
    {
        string title = $"{FoeStats.DifficultyName(FoeLevel)} {MissionTable.DifficultyName(_nodeLevel)} — {MissionName(_missionType)}";
        _hud.SetMissionTitle(title);
        _hud.ConfigureFinishDay(_hasRun);
        _hud.SetFinishDayEnabled(false);
        _hud.FinishDayPressed += OnFinishDayPressed;
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

        Spawner?.Tick(dt);

        if (_mission == null) return;
        // QA cheat (40-QA §1): F9 in a debug build force-completes the goal — lets a tester
        // walk the whole run loop without playing out every mission.
        if (OS.IsDebugBuild() && _mission.Status == MissionStatus.Running
            && Input.IsKeyPressed(Key.F9))
            _mission.DebugForceComplete();
        if (_mission.Status == MissionStatus.Running) _mission.Tick(dt);
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

    // ── Goal resolution ───────────────────────────────────────────────────────────────────

    private void OnMissionResolved()
    {
        _goalResolved = true;
        Spawner.Enabled = false;   // FOES §11: the cap only gates the periodic spawner anyway

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

    private void WriteBackHp()
    {
        if (_hasRun && _protagonist != null && _player != null)
            _protagonist.HpRatio = _player.HpRatio;
    }

    // ── Player death ──────────────────────────────────────────────────────────────────────

    private void OnPlayerHpChanged(float cur, float max) => _hud.SetHp(cur, max);

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

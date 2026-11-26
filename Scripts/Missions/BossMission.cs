using System.Collections.Generic;
using Godot;
using Fableland.Bosses;
using Fableland.Data;
using Fableland.Debug;
using Fableland.Map;
using Fableland.Run;

namespace Fableland.Missions;

/// <summary>
/// Boss (NODES §4.1/§4.5, FOES §8): beat the node's boss within the level's boss timer.
///
/// The boss is the one the handshake names (<see cref="GameManager.BossId"/> →
/// <see cref="BossTable"/>): a protagonist's kit on the Foe side, driven by a rule-based
/// brain, staged on its arena's perches. Beating it recruits that protagonist — once. Every
/// realm's 4-1 names a boss (its own, or the stand-in: <see cref="BossTable.ForWorld"/>);
/// the placeholder (S3, <see cref="BossCrab"/>) is left for a node that names none — the LV6
/// core — and for a boss whose scene fails to load.
///
/// The ambient spawner is OFF — the boss owns the field; a single small add wave spawns
/// once it drops below 50% HP (kept simple, not GDD-specified). LV6 is the final boss
/// (<see cref="IsFinalBoss"/> ⇒ GameManager ends the run in victory on success); outer
/// bosses grant Yukai's Rope, while the first capital's RunState reward also grants
/// TwistedReality. Timer expiry is FATAL (<see cref="FatalTimeout"/> ⇒ GameManager turns
/// this into permadeath when a run exists, NODES §4.5/§2.2; a normal lose banner in debug mode).
/// </summary>
public sealed class BossMission : Mission
{
    private GameManager _arena;
    private int _nodeLevel;
    private DetRandom _rng;

    private BossDef _def;                    // the boss this node names; null ⇒ placeholder
    private CharacterController _boss;       // the real boss, once staged
    private IBossFighter _fighter;
    private BossCrab _placeholder;
    private bool _alreadyRecruited;          // the run owned this protagonist before the fight
    private bool _staged;
    private bool _bossWasAlive;
    private bool _addWaveSpawned;
    private float _timer;
    private bool _timedOut;
    private Vector2 _bossPosition;           // where the boss last stood — where it fell
    private string _bossCueId = "";          // the boss's content id, the tag of its flow cues

    public override void Setup(GameManager arena, int nodeLevel, DetRandom rng)
    {
        _arena = arena;
        _nodeLevel = nodeLevel;
        _timer = MissionTable.BossTimeLimit(nodeLevel);
        _rng = rng;

        if (!string.IsNullOrWhiteSpace(arena.BossId) && !BossTable.TryGet(arena.BossId, out _def))
            GD.PushError($"[BossMission] unknown boss '{arena.BossId}'; fielding the placeholder.");

        arena.Spawner.Enabled = false;   // boss owns the field (ambient trickle off)
    }

    /// <summary>True once the named boss — not the stand-in — is on the field.</summary>
    public bool FieldsRealBoss => _boss != null;

    private void StageBoss()
    {
        if (!(_def != null && TryStageBossFighter())) StagePlaceholder();

        // flow/boss.intro: the boss is on the field. Tag = which boss.
        Node2D boss = _boss != null ? _boss : _placeholder;
        if (boss == null) return;
        _bossPosition = boss.GlobalPosition;
        _bossCueId = (boss as IFeedbackBody)?.FeedbackId ?? "";
        CueBus.EmitAt(CueOwners.Flow, "boss.intro", _bossPosition, boss, CombatSide.Foe,
            variant: _nodeLevel, tag: _bossCueId);
    }

    /// <summary>
    /// Stages the named boss: a protagonist's class on the Foe side. The map's Level Goal
    /// markers are its perches (FOES §8.5); it opens on the one farthest from the player.
    /// Any content error (missing scene, a scene that is not a boss) degrades to the
    /// placeholder instead of breaking a permadeath run (T00 rule 4).
    /// </summary>
    private bool TryStageBossFighter()
    {
        var scene = ResourceLoader.Load<PackedScene>(_def.ScenePath);
        if (scene == null)
        {
            GD.PushError($"[BossMission] boss '{_def.Id}': scene '{_def.ScenePath}' does not load; fielding the placeholder.");
            return false;
        }

        Node node = scene.Instantiate();
        if (node is not CharacterController body || node is not IBossFighter fighter || body.Side != CombatSide.Foe)
        {
            GD.PushError($"[BossMission] boss '{_def.Id}': '{_def.ScenePath}' is not a Foe-side boss fighter; fielding the placeholder.");
            node.Free();
            return false;
        }

        var perches = new List<Vector2>(_arena.LevelGoalPoints);
        // No authored perch (the legacy arena): the boss gets the one spot the placeholder
        // would have been given, from the same deterministic stream.
        if (perches.Count == 0) perches.Add(_arena.RandomLevelGoalPoint(_rng));
        int home = BossArena.FarthestFrom(perches, _arena.PlayerPosition);

        _arena.Entities.AddChild(body);                       // _Ready runs here
        body.GlobalPosition = perches[home];
        body.SetSpawnPoint(perches[home]);                    // _Ready latched the scene origin
        fighter.BeginFight(_def, _arena.FoeLevel, new BossArena(perches, home));   // AFTER AddChild (caveat)

        _boss = body;
        _fighter = fighter;
        _alreadyRecruited = RunState.Instance?.FindProtagonist(_def.ProtagonistId) != null;
        DebugManager.Instance?.LogMission($"Boss staged: {_def.Id} at perch {home + 1}/{perches.Count}, HP {body.MaxHP:0}");
        return true;
    }

    private void StagePlaceholder()
    {
        if (_arena.BossCrabScene == null) return;
        _placeholder = _arena.BossCrabScene.Instantiate<BossCrab>();
        // C2: BossMission bypasses FoeSpawner (it owns its own single spawn), so seed the
        // boss's Rng from the mission's deterministic stream BEFORE AddChild — _Ready()
        // reads Rng synchronously (BaseFoe._Ready → Dir = Rng.Randf() < 0.5f ...).
        _placeholder.Rng = new RandomNumberGenerator { Seed = _rng.NextULong() };
        _arena.Entities.AddChild(_placeholder);                 // _Ready runs here
        // Authored LV4 maps use their central level-goal marker as the boss stage. The
        // legacy Arena has no authored markers, so GameManager preserves its old fallback.
        _placeholder.GlobalPosition = _arena.RandomLevelGoalPoint(_rng);
        _placeholder.Init(_arena.FoeLevel);                      // Init AFTER AddChild (caveat)
    }

    private bool BossAlive => _boss != null
        ? GodotObject.IsInstanceValid(_boss) && !_boss.IsDefeated
        : _placeholder != null && GodotObject.IsInstanceValid(_placeholder);

    private float BossHp => !BossAlive ? 0f : _boss != null ? _boss.CurrentHP : _placeholder.CurrentHp;
    private float BossMaxHp => !BossAlive ? 1f : _boss != null ? _boss.MaxHP : _placeholder.MaxHP;

    public override void Tick(float dt)
    {
        // GameManager never ticks missions until the universal pre-combat gate opens.
        // Deferring this direct spawn keeps boss nodes under that same countdown rule.
        if (!_staged)
        {
            _staged = true;
            StageBoss();
            return;
        }

        bool alive = BossAlive;
        if (alive)
        {
            _bossWasAlive = true;
            _bossPosition = _boss != null ? _boss.GlobalPosition : _placeholder.GlobalPosition;
        }

        if (!alive)
        {
            if (_bossWasAlive)
            {
                Status = MissionStatus.Succeeded;   // boss fell
                // flow/boss.defeated: the beat a boss kill gets. A fighter-boss is still
                // there, fading; the stand-in has already freed itself.
                Node2D fallen = _boss != null && GodotObject.IsInstanceValid(_boss) ? _boss : null;
                CueBus.EmitAt(CueOwners.Flow, "boss.defeated", _bossPosition, fallen, CombatSide.Foe,
                    variant: _nodeLevel, tag: _bossCueId);
            }
            return;
        }

        if (!_addWaveSpawned && BossMaxHp > 0f && BossHp <= BossMaxHp * 0.5f)
        {
            _addWaveSpawned = true;
            _arena.Spawner.SpawnWave(2, Mathf.Max(1, _arena.FoeLevel - 1));
        }

        _timer -= dt;
        if (_timer <= 0f)
        {
            _timedOut = true;
            Status = MissionStatus.Failed;
        }
    }

    /// <summary>A boss still standing when the mission ends (a debug skip, the fatal timer)
    /// stops fighting; the placeholder is frozen by GameManager's foe sweep.</summary>
    public override void OnResolved()
    {
        if (_boss != null && BossAlive) _fighter?.StandDown();
    }

    // LV6 is irrelevant — victory ends the run before any reward would matter. Beating the
    // named boss recruits its protagonist (NODES §4.5); the stand-in recruits nobody.
    public override RewardBundle Reward()
    {
        var reward = _nodeLevel >= 6 ? new RewardBundle() : new RewardBundle { ItemDefIds = { "yukais_rope" } };
        if (FieldsRealBoss && _nodeLevel < 6) reward.ProtagonistGrants.Add(_def.ProtagonistId);
        return reward;
    }

    // Recruiting happens once (RunState.GrantProtagonist), so it is announced once: a boss
    // standing in for a realm whose own is not built, or met again, simply falls.
    public override string SuccessToast => !FieldsRealBoss ? base.SuccessToast
        : _alreadyRecruited ? $"{_def.DisplayName} falls!"
        : $"{_def.DisplayName} falls — {_def.ProtagonistId} joins you!";

    private string BossName => FieldsRealBoss ? _def.DisplayName : "the BOSS";

    public override string ProgressText
    {
        get
        {
            string text = "Defeat " + BossName;
            // The rule the brain is running, while debug mode is on — the tuning readout.
            if (FieldsRealBoss && BossAlive && DebugManager.Instance is { Enabled: true })
                text += "   [" + _fighter.BrainState + "]";
            return text;
        }
    }

    public override bool HasTimer => true;
    public override float TimeRemaining => _timer;

    public override bool HasSecondaryBar => BossAlive;
    public override float SecondaryValue => BossHp;
    public override float SecondaryMax => BossMaxHp;

    public override string SecondaryLabel
    {
        get
        {
            if (!FieldsRealBoss) return "Boss";
            float shield = BossAlive ? _boss.Shield : 0f;
            return shield > 0.5f ? $"{_def.DisplayName} (+{Mathf.CeilToInt(shield)} shell)" : _def.DisplayName;
        }
    }

    public override bool FatalTimeout => _timedOut;
    public override bool IsFinalBoss => _nodeLevel >= 6;
}

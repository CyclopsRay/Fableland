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
/// brain, staged on its arena's perches. Beating a realm's 4-1 recruits that REALM's own
/// protagonist — once, whichever kit held it (<see cref="GameManager.RecruitId"/>, from the
/// handshake; NODES §4.5). Every
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
    private string _recruitId = "";          // whom beating this node recruits (the realm's own)
    private bool _alreadyRecruited;          // the run owned that protagonist before the fight
    private bool _staged;
    private bool _bossWasAlive;
    private bool _addWaveSpawned;
    private float _timer;
    private bool _timedOut;
    private Vector2 _bossPosition;           // where the boss last stood — where it fell
    private string _bossCueId = "";          // the boss's content id, the tag of its flow cues
    private StyxTideSchedule _styx;          // the Unwritten's signature (Docs/Zone6.gdd §4.2)
    private WheelOfBecomingField _wheel;     // Māra's signature (Docs/Mara.gdd §4)

    /// <summary>The Styx Tide's schedule when this boss runs it (read-only: checks).</summary>
    public StyxTideSchedule StyxTide => _styx;

    /// <summary>The Wheel of Becoming when this boss turns it (read-only: checks).</summary>
    public WheelOfBecomingField Wheel => _wheel;

    public override void Setup(GameManager arena, int nodeLevel, DetRandom rng)
    {
        _arena = arena;
        _nodeLevel = nodeLevel;
        _timer = MissionTable.BossTimeLimit(nodeLevel);
        _rng = rng;

        if (!string.IsNullOrWhiteSpace(arena.BossId) && !BossTable.TryGet(arena.BossId, out _def))
            GD.PushError($"[BossMission] unknown boss '{arena.BossId}'; fielding the placeholder.");
        // Who this realm's 4-1 recruits is the handshake's call (NODES §4.5), not the kit's: a
        // stand-in PumpKing beaten in Starland recruits Starland's own. Read before the fight,
        // so the toast can tell a newcomer from someone already on the roster.
        _recruitId = arena.RecruitId ?? "";
        _alreadyRecruited = _recruitId.Length > 0 && RunState.Instance?.FindProtagonist(_recruitId) != null;

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
        // A field mechanic the fight runs whatever kit stands in: the Styx rising from the
        // horizon on the Unwritten's floor, from a stream of its own; Māra's wheel behind his.
        if (_def.Signature == "styx_tide") _styx = new StyxTideSchedule(RaiseStyxTide, _rng.Sub("styx"));
        if (_def.Signature == WheelTable.Signature) _wheel = StartWheel(body, body.Feet);
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

        _styx?.Tick(dt, BossMaxHp > 0f ? BossHp / BossMaxHp : 1f);
        _wheel?.Advance(dt);

        // No adds on the wheel: in Avīci the realms are his army (Mara.gdd §7.1).
        if (!_addWaveSpawned && _wheel == null && BossMaxHp > 0f && BossHp <= BossMaxHp * 0.5f)
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
        if (_boss != null && BossAlive)
        {
            _fighter?.StandDown();
            _wheel?.Halt();
        }
    }

    /// <summary>Starts the Wheel of Becoming behind the boss (Docs/Mara.gdd §4): the rules say
    /// what is sealed in him and how many stacks of each poison the party carries — read live,
    /// so an Offering taken mid-fight counts from the next turn. The throne, until the arena is
    /// painted, is the perch he opened on (§8).</summary>
    private WheelOfBecomingField StartWheel(CharacterController body, Vector2 throne)
    {
        var field = new WheelOfBecomingField { Name = "WheelOfBecoming" };
        _arena.AddBackdrop(field);
        field.Begin(body, _def, _arena.Rules.MaraSealedPoisons, poison => _arena.Rules.SealStacksOf(poison),
            () => _arena.PlayerBody, _arena.ArenaLeft, _arena.ArenaRight, _arena.FloorY, throne);
        field.OfferingTaken += poison => _arena.TakeFightSeal(poison);
        DebugManager.Instance?.LogMission($"The wheel turns: {field.Realm}, sealed [{string.Join(", ", _arena.Rules.MaraSealedPoisons)}]");
        return field;
    }

    public override string ConsumeAnnouncement() => _wheel?.ConsumeAnnouncement();

    /// <summary>Māra's last sliver, in gold, while it holds (Mara.gdd §7.2).</summary>
    public override float SecondaryNotch => _wheel != null && BossAlive && !_wheel.Liberated ? WheelTable.LockShare : 0f;

    /// <summary>One wave of the Styx Tide on this arena's main floor.</summary>
    private StyxTideHazard RaiseStyxTide(float direction)
    {
        if (_arena.HazardLayer == null) return null;
        var wave = new StyxTideHazard { Name = "StyxTide" };
        wave.Configure(direction, _arena.FloorY, _arena.ArenaLeft, _arena.ArenaRight);
        _arena.HazardLayer.AddChild(wave);
        return wave;
    }

    /// <summary>True when the boss on the field is one of zone 6's (Docs/Zone6.gdd).</summary>
    private bool Zone6Boss => _def != null && _def.Role != BossRole.Realm;

    // LV6 is irrelevant — victory ends the run before any reward would matter. Beating a
    // realm's 4-1 recruits the realm's own protagonist (NODES §4.5) — whoever held it, the
    // stand-in kit or the placeholder included; a realm with none recruits nobody. A zone-6
    // mini-boss's reward is its variant's (gravity, a seal on Māra — RunState), nothing else.
    public override RewardBundle Reward()
    {
        if (Zone6Boss || _nodeLevel >= 6) return new RewardBundle();
        var reward = new RewardBundle { ItemDefIds = { "yukais_rope" } };
        if (_recruitId.Length > 0) reward.ProtagonistGrants.Add(_recruitId);
        return reward;
    }

    // Recruiting happens once (RunState.GrantProtagonist), so it is announced once: a boss
    // met again, of a realm with no protagonist yet, or of zone 6 simply falls.
    public override string SuccessToast =>
        _wheel is { Liberated: true } ? "The wheel stops. You step off it."
        : Zone6Boss || _nodeLevel >= 6 || _recruitId.Length == 0 || _alreadyRecruited
            ? (FieldsRealBoss ? $"{_def.DisplayName} falls!" : base.SuccessToast)
            : $"{BossName} falls — {_recruitId} joins you!";

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
            string name = _wheel != null && !_wheel.Liberated ? $"{_def.DisplayName} · {WheelTable.Of(_wheel.Realm).Title}" : _def.DisplayName;
            return shield > 0.5f ? $"{name} (+{Mathf.CeilToInt(shield)} shell)" : name;
        }
    }

    public override bool FatalTimeout => _timedOut;
    public override bool IsFinalBoss => _nodeLevel >= 6;
}

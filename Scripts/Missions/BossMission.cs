using Godot;
using Fableland.Map;
using Fableland.Run;

namespace Fableland.Missions;

/// <summary>
/// Boss (NODES §4.1/§4.5, G2): kill the placeholder boss (S3, <see cref="BossCrab"/>) within the
/// level's boss timer. The ambient spawner is OFF — the boss owns the field; a single small add
/// wave spawns once it drops below 50% HP (kept simple, not GDD-specified — the brief left the
/// exact threshold/composition to this pass). LV6 is the final boss
/// (<see cref="IsFinalBoss"/> ⇒ GameManager ends the run in victory on success); LV4 grants a
/// placeholder wonder item (report Q2 — no boss-as-protagonist kits exist yet, so the "recruit"
/// reward is stubbed). Timer expiry is FATAL (<see cref="FatalTimeout"/> ⇒ GameManager turns this
/// into permadeath when a run exists, NODES §4.5/§2.2; a normal lose banner in debug mode).
/// </summary>
public sealed class BossMission : Mission
{
    private GameManager _arena;
    private int _nodeLevel;
    private BossCrab _boss;
    private bool _bossWasAlive;
    private bool _addWaveSpawned;
    private float _timer;
    private bool _timedOut;

    public override void Setup(GameManager arena, int nodeLevel, DetRandom rng)
    {
        _arena = arena;
        _nodeLevel = nodeLevel;
        _timer = MissionTable.BossTimeLimit(nodeLevel);

        arena.Spawner.Enabled = false;   // boss owns the field (ambient trickle off)

        if (arena.BossCrabScene != null)
        {
            _boss = arena.BossCrabScene.Instantiate<BossCrab>();
            // C2: BossMission bypasses FoeSpawner (it owns its own single spawn), so seed the
            // boss's Rng from the mission's deterministic stream BEFORE AddChild — _Ready()
            // reads Rng synchronously (BaseFoe._Ready → Dir = Rng.Randf() < 0.5f ...).
            _boss.Rng = new RandomNumberGenerator { Seed = rng.NextULong() };
            arena.Entities.AddChild(_boss);                 // _Ready runs here
            _boss.GlobalPosition = new Vector2(
                (ArenaBuilder.PlayLeft + ArenaBuilder.PlayRight) * 0.5f, ArenaBuilder.GroundTopY - 60f);
            _boss.Init(arena.FoeLevel);                      // Init AFTER AddChild (caveat)
        }
    }

    public override void Tick(float dt)
    {
        bool alive = _boss != null && GodotObject.IsInstanceValid(_boss);
        if (alive) _bossWasAlive = true;

        if (!alive)
        {
            if (_bossWasAlive) Status = MissionStatus.Succeeded;   // boss died
            return;
        }

        if (!_addWaveSpawned && _boss.MaxHP > 0f && _boss.CurrentHp <= _boss.MaxHP * 0.5f)
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

    // LV4: placeholder item (recruit-as-protagonist is TBD, Q2). LV6: irrelevant — victory ends
    // the run before any reward would matter.
    public override RewardBundle Reward() =>
        _nodeLevel >= 6 ? new RewardBundle() : new RewardBundle { ItemDefIds = { "placeholder" } };

    public override string ProgressText => "Defeat the BOSS";
    public override bool HasTimer => true;
    public override float TimeRemaining => _timer;

    public override bool HasSecondaryBar => _boss != null && GodotObject.IsInstanceValid(_boss);
    public override float SecondaryValue => _boss != null ? _boss.CurrentHp : 0f;
    public override float SecondaryMax => _boss != null ? _boss.MaxHP : 1f;
    public override string SecondaryLabel => "Boss";

    public override bool FatalTimeout => _timedOut;
    public override bool IsFinalBoss => _nodeLevel >= 6;
}

using Godot;
using Fableland.Map;
using Fableland.Run;

namespace Fableland.Missions;

/// <summary>
/// Protect (NODES §4.1/§4.4): defend the Condensed Wonder Core for the level's duration. The
/// ambient foe spawner keeps attacking; a foe in contact range of the core chips its HP
/// (handled inside <see cref="ProtectCore"/>, Q10). Survive the duration ⇒ Succeeded; core HP
/// hits 0 ⇒ Failed immediately (survivable, NODES §2.3). Not healable (S8). Reward = the core:
/// +10 wonder cores + 1 placeholder wonder item (NODES §4.1).
/// </summary>
public sealed class ProtectMission : Mission
{
    private GameManager _arena;
    private float _duration;
    private float _timer;
    private ProtectCore _core;

    public override void Setup(GameManager arena, int nodeLevel, DetRandom rng)
    {
        _arena = arena;
        _duration = MissionTable.ProtectDuration(nodeLevel);
        _timer = _duration;

        _core = arena.ProtectCoreScene.Instantiate<ProtectCore>();
        arena.Entities.AddChild(_core);
        _core.GlobalPosition = new Vector2(
            (ArenaBuilder.PlayLeft + ArenaBuilder.PlayRight) * 0.5f, ArenaBuilder.GroundTopY - 44f);
        float perHit = 18f * FoeStats.ForLevel(arena.FoeLevel).AtkMult;
        _core.Configure(MissionTable.ProtectCoreHp(nodeLevel), perHit);
    }

    public override void Tick(float dt)
    {
        if (_core == null || !GodotObject.IsInstanceValid(_core)) return;
        if (_core.IsDestroyed) { Status = MissionStatus.Failed; return; }

        _timer -= dt;
        if (_timer <= 0f) Status = MissionStatus.Succeeded;
    }

    public override RewardBundle Reward() =>
        new() { WonderCores = 10, ItemDefIds = { "placeholder" } };

    public override string ProgressText => "Defend the Condensed Wonder Core";
    public override bool HasTimer => true;
    public override float TimeRemaining => _timer;

    public override bool HasSecondaryBar => _core != null && GodotObject.IsInstanceValid(_core);
    public override float SecondaryValue => _core != null ? _core.CurrentHp : 0f;
    public override float SecondaryMax => _core != null ? _core.MaxHp : 1f;
    public override string SecondaryLabel => "Core";
}

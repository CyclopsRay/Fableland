using System.Collections.Generic;
using Godot;
using Fableland.Map;
using Fableland.Run;

namespace Fableland.Missions;

/// <summary>
/// Destroy (NODES §4.1/§4.2): destroy all N objectives within the time limit. Objectives are
/// stationary <see cref="DestroyObjective"/>s (report Q8) with table-driven HP, placed from the
/// mission RNG. The ambient foe spawner keeps harassing the player. All destroyed ⇒ Succeeded;
/// timer expiry ⇒ Failed (survivable). Reward = The Forgotten Kashaya prototype item (NODES §4.1).
/// </summary>
public sealed class DestroyMission : Mission
{
    private GameManager _arena;
    private DetRandom _rng;
    private float _timer;
    private int _total;
    private readonly List<DestroyObjective> _objectives = new();

    public override void Setup(GameManager arena, int nodeLevel, DetRandom rng)
    {
        _arena = arena;
        _rng = rng;
        _timer = MissionTable.TimeLimit(nodeLevel);
        _total = MissionTable.DestroyObjectives(nodeLevel);
        float hp = MissionTable.DestroyObjectiveHp(nodeLevel);

        for (int i = 0; i < _total; i++)
        {
            var obj = arena.DestroyObjectiveScene.Instantiate<DestroyObjective>();
            obj.Rng = new RandomNumberGenerator { Seed = rng.NextULong() };
            arena.Entities.AddChild(obj);                 // _Ready auto-Inits at level 1
            // Destroy maps author Level Goal tiles as enemy-objective positions.
            obj.GlobalPosition = arena.RandomLevelGoalPoint(rng);
            obj.OverrideMaxHp(hp);                        // fixed table HP, not level-scaled
            _objectives.Add(obj);
        }
    }

    public override void Tick(float dt)
    {
        if (Remaining() == 0) { Status = MissionStatus.Succeeded; return; }
        _timer -= dt;
        if (_timer <= 0f) Status = MissionStatus.Failed;
    }

    private int Remaining()
    {
        int r = 0;
        foreach (var o in _objectives) if (GodotObject.IsInstanceValid(o)) r++;
        return r;
    }

    public override RewardBundle Reward() => new() { ItemDefIds = { "forgotten_kashaya" } };

    public override string ProgressText => $"Objectives {_total - Remaining()}/{_total} destroyed";
    public override bool HasTimer => true;
    public override float TimeRemaining => _timer;
}

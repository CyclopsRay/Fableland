using System.Collections.Generic;
using Godot;
using Fableland.Map;
using Fableland.Run;

namespace Fableland.Missions;

/// <summary>
/// Collection (NODES §4.1, 60% of combat nodes) — the flagship mission. Keep 3 wonder-core
/// pickups on the field at once (report Q4); each un-collected core despawns after 12 s and a
/// replacement spawns elsewhere. Collect N (per <see cref="MissionTable"/>). The instant N is
/// reached: every remaining foe dies, the mission timer stops, and a 10 s grace window lets the
/// player grab any cores still on the field; then Succeeded.
///
/// <b>Reward crediting:</b> each core is credited to the run <i>on pickup</i>
/// (<see cref="RunState.AddWonderCores"/>), so they are kept even on a timer failure ("collected
/// cores are kept", NODES §2.3). The success <see cref="RewardBundle"/> therefore carries ZERO
/// cores — crediting only on pickup avoids the double-count the report warns about.
/// </summary>
public sealed class CollectionMission : Mission
{
    private const int Concurrent = 3;
    private const float DespawnTime = 12f;
    private const float GraceTime = 10f;

    private GameManager _arena;
    private DetRandom _rng;
    private int _required;
    private int _collected;
    private float _timeLimit;
    private float _timer;

    private bool _completing;        // N reached → grace window running
    private float _graceTimer;

    private sealed class Core { public WonderCorePickup Node; public float Despawn; }
    private readonly List<Core> _cores = new();

    public override void Setup(GameManager arena, int nodeLevel, DetRandom rng)
    {
        _arena = arena;
        _rng = rng;
        _required = MissionTable.CollectionCores(nodeLevel);
        _timeLimit = MissionTable.TimeLimit(nodeLevel);
        _timer = _timeLimit;
        for (int i = 0; i < Concurrent; i++) SpawnCore();
    }

    public override void Tick(float dt)
    {
        if (_completing)
        {
            _graceTimer -= dt;
            if (_graceTimer <= 0f)
            {
                ClearCores();
                Status = MissionStatus.Succeeded;
            }
            return;
        }

        _timer -= dt;
        // Age out cores and respawn replacements so 3 stay on the field.
        for (int i = _cores.Count - 1; i >= 0; i--)
        {
            var c = _cores[i];
            if (!IsInstanceValid(c.Node)) { _cores.RemoveAt(i); SpawnCore(); continue; }
            c.Despawn -= dt;
            if (c.Despawn <= 0f)
            {
                c.Node.QueueFree();
                _cores.RemoveAt(i);
                SpawnCore();
            }
        }

        if (_timer <= 0f)
        {
            // Timer failure: collected cores already credited on pickup and kept (NODES §2.3).
            ClearCores();
            Status = MissionStatus.Failed;
        }
    }

    private void SpawnCore()
    {
        if (_completing || _arena?.WonderCorePickupScene == null) return;
        var node = _arena.WonderCorePickupScene.Instantiate<WonderCorePickup>();
        _arena.Entities.AddChild(node);
        node.GlobalPosition = _arena.RandomPlacementPoint(_rng);
        var core = new Core { Node = node, Despawn = DespawnTime };
        node.Collected += () => OnCollected(core);
        _cores.Add(core);
    }

    private void OnCollected(Core core)
    {
        _cores.Remove(core);
        _collected++;
        RunState.Instance?.AddWonderCores(1);   // credited immediately → kept on failure

        if (_collected >= _required && !_completing)
        {
            // Completion: kill remaining foes, stop the timer, open the 10 s grace window.
            _completing = true;
            _graceTimer = GraceTime;
            KillAllFoes();
        }
        else if (!_completing)
        {
            SpawnCore();   // keep 3 on the field
        }
    }

    private void KillAllFoes()
    {
        foreach (Node n in _arena.GetTree().GetNodesInGroup("foe"))
            if (n is BaseFoe f && IsInstanceValid(f)) f.QueueFree();
    }

    private void ClearCores()
    {
        foreach (var c in _cores) if (IsInstanceValid(c.Node)) c.Node.QueueFree();
        _cores.Clear();
    }

    private static bool IsInstanceValid(GodotObject o) => GodotObject.IsInstanceValid(o);

    public override RewardBundle Reward() => new();   // cores already credited on pickup

    public override string ProgressText =>
        _completing ? $"Cores {_collected}/{_required} — grab leftovers!"
                    : $"Cores {_collected}/{_required}";

    public override bool HasTimer => true;
    public override float TimeRemaining => _completing ? _graceTimer : _timer;
}

using Godot;
using Fableland.Map;

namespace Fableland.Missions;

/// <summary>
/// The arena's foe-spawning service (S2, C1–C3). Owned by <see cref="GameManager"/> and
/// ticked by it; a mission configures it (on/off, cap, wave spawns). All randomness flows
/// from a deterministic sub-stream, and every spawned foe is handed a seeded
/// <see cref="RandomNumberGenerator"/> derived from it (C2). Crabs get their BabyCrabScene
/// injected (C3, cyclic-resource caveat); seagulls spawn a few metres up (C1).
/// </summary>
public sealed class FoeSpawner
{
    private readonly GameManager _arena;
    private readonly DetRandom _rng;

    /// <summary>Periodic spawner on/off. Slaughter/Boss disable it and drive waves themselves.</summary>
    public bool Enabled = true;
    public int Cap = 6;                 // gates ONLY the periodic spawner (FOES §11)
    public float Interval = 3f;
    private float _timer;

    public FoeSpawner(GameManager arena, DetRandom rng)
    {
        _arena = arena;
        _rng = rng;
    }

    /// <summary>Periodic trickle spawn (the ambient arena population).</summary>
    public void Tick(float dt)
    {
        if (!Enabled) return;
        _timer -= dt;
        if (_timer <= 0f)
        {
            _timer = Interval;
            if (LiveFoeCount() < Cap) SpawnOne(_arena.FoeLevel);
        }
    }

    /// <summary>Foes currently alive in the arena (group "foe"). Boss adds count too (fine —
    /// the cap only throttles the ambient trickle); DestroyObjectives do NOT (they leave the
    /// group in _Ready so they can't starve the cap — review fix, v0.5.0).</summary>
    public int LiveFoeCount() => _arena.GetTree().GetNodesInGroup("foe").Count;

    /// <summary>Spawn a whole wave at once (Slaughter/Boss adds). 60/40 crab/seagull per slot.</summary>
    public void SpawnWave(int count, int level)
    {
        for (int i = 0; i < count; i++) SpawnOne(level);
    }

    /// <summary>Spawn a single foe (60% crab / 40% seagull) at the given level.</summary>
    public BaseFoe SpawnOne(int level)
    {
        bool crab = _rng.NextDouble() < 0.6;
        return crab ? SpawnCrab(level) : SpawnSeagull(level);
    }

    public CrabFoe SpawnCrab(int level)
    {
        if (_arena.CrabScene == null) return null;
        var foe = _arena.CrabScene.Instantiate<CrabFoe>();
        SeedRng(foe);
        _arena.Entities.AddChild(foe);               // _Ready runs here (uses seeded Rng)
        foe.GlobalPosition = _arena.RandomFoeSpawn(_rng, aerial: false);
        foe.BabyCrabScene = _arena.CrabScene;         // C3: inject for spawn-on-death
        foe.Init(level);                              // Init AFTER AddChild (caveat)
        return foe;
    }

    public SeagullFoe SpawnSeagull(int level)
    {
        if (_arena.SeagullScene == null) return null;
        var foe = _arena.SeagullScene.Instantiate<SeagullFoe>();
        SeedRng(foe);
        _arena.Entities.AddChild(foe);
        foe.GlobalPosition = _arena.RandomFoeSpawn(_rng, aerial: true);   // C1: 3–5 m up
        foe.Init(level);
        return foe;
    }

    // Seed the foe's RNG from the deterministic stream BEFORE AddChild (foe._Ready reads it).
    private void SeedRng(BaseFoe foe)
    {
        foe.Rng = new RandomNumberGenerator { Seed = _rng.NextULong() };
    }
}

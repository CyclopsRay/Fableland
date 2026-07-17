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
    /// <summary>Combat-wide start gate owned by the Arena. It applies to every foe source,
    /// including mission waves, so a universal countdown cannot be bypassed.</summary>
    public bool SpawnGateOpen { get; private set; }
    public int Cap = 6;                 // gates ONLY the periodic spawner (FOES §11)
    /// <summary>Current authored prototype cadence: one ambient foe every four seconds.</summary>
    public float Interval = 4f;
    private int _crabWeight = 60;
    private int _seagullWeight = 40;
    private float _timer;
    private readonly System.Collections.Generic.List<(int Count, int Level)> _deferredWaves = new();

    public FoeSpawner(GameManager arena, DetRandom rng)
    {
        _arena = arena;
        _rng = rng;
        _timer = Interval; // first ambient foe arrives after one full authored interval
    }

    /// <summary>
    /// Apply the selected map's level composition. Values are weights, not probabilities; invalid
    /// entries retain the safe legacy 60/40 mix. Eligibility is resolved separately from map
    /// spawn markers, so a type is never generated only to discover it has no valid nest.
    /// </summary>
    public void SetComposition(int crabWeight, int seagullWeight)
    {
        if (crabWeight < 0 || seagullWeight < 0 || crabWeight + seagullWeight <= 0) return;
        _crabWeight = crabWeight;
        _seagullWeight = seagullWeight;
    }

    /// <summary>Periodic trickle spawn (the ambient arena population).</summary>
    public void Tick(float dt)
    {
        if (!Enabled || !SpawnGateOpen) return;
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

    /// <summary>Spawn a whole wave at once (Slaughter/Boss adds), using the selected map mix.</summary>
    public void SpawnWave(int count, int level)
    {
        if (count <= 0) return;
        if (!SpawnGateOpen)
        {
            _deferredWaves.Add((count, level));
            return;
        }
        SpawnWaveNow(count, level);
    }

    /// <summary>Open the universal pre-combat gate and flush mission waves requested during setup.</summary>
    public void OpenSpawnGate()
    {
        if (SpawnGateOpen) return;
        SpawnGateOpen = true;
        foreach ((int count, int level) in _deferredWaves) SpawnWaveNow(count, level);
        _deferredWaves.Clear();
    }

    private void SpawnWaveNow(int count, int level)
    {
        for (int i = 0; i < count; i++) SpawnOne(level);
    }

    /// <summary>
    /// Spawn a single foe using this map's weights after filtering types that have no eligible
    /// nest. Crab and seagull instances capture their final spawn origin on their first physics
    /// frame, so that selected marker is also their patrol nest.
    /// </summary>
    public BaseFoe SpawnOne(int level)
    {
        if (!SpawnGateOpen) return null;
        bool crabAllowed = _arena.HasFoeSpawnFor(aerial: false);
        bool seagullAllowed = _arena.HasFoeSpawnFor(aerial: true);
        if (!crabAllowed && !seagullAllowed) return null;
        if (!crabAllowed) return SpawnSeagull(level);
        if (!seagullAllowed) return SpawnCrab(level);

        int total = _crabWeight + _seagullWeight;
        bool crab = _rng.Range(1, total) <= _crabWeight;
        return crab ? SpawnCrab(level) : SpawnSeagull(level);
    }

    public CrabFoe SpawnCrab(int level)
    {
        if (!SpawnGateOpen) return null;
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
        if (!SpawnGateOpen) return null;
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

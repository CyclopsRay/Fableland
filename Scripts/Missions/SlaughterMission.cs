using Godot;
using Fableland.Map;
using Fableland.Run;

namespace Fableland.Missions;

/// <summary>
/// Slaughter (NODES §4.1/§4.3): clear a set number of enemy waves within the time limit. Wave
/// n+1 spawns only when wave n is fully dead. Wave size = 3 + nodeLevel; the FINAL wave is
/// fought at foe level +1, capped at 8 (report Q5, NODES §4.3). This mission OWNS the spawner
/// (ambient trickle off) and drives waves through it. All waves cleared ⇒ the player picks a
/// reward (+10 ATK or +10 DEF); timer expiry ⇒ Failed (survivable).
/// </summary>
public sealed class SlaughterMission : Mission
{
    private GameManager _arena;
    private int _totalWaves;
    private int _waveSize;
    private int _foeLevel;
    private float _timer;

    private int _waveIndex;          // 1-based index of the wave currently live
    private bool _awaitingChoice;
    private bool _chosen;
    private bool _atkChoice;

    /// <summary>Final-wave foe level: current foe level +1, capped at 8 (NODES §4.3). One place.</summary>
    public static int FinalWaveLevel(int foeLevel) => Mathf.Min(8, foeLevel + 1);

    public override void Setup(GameManager arena, int nodeLevel, DetRandom rng)
    {
        _arena = arena;
        _totalWaves = MissionTable.SlaughterWaves(nodeLevel);
        _waveSize = 3 + nodeLevel;
        _foeLevel = arena.FoeLevel;
        _timer = MissionTable.TimeLimit(nodeLevel);

        arena.Spawner.Enabled = false;   // mission owns spawning
        SpawnWave(1);
    }

    public override void Tick(float dt)
    {
        if (_awaitingChoice)
        {
            if (_chosen) Status = MissionStatus.Succeeded;   // timer frozen while choosing
            return;
        }

        _timer -= dt;
        if (_timer <= 0f) { Status = MissionStatus.Failed; return; }

        if (_arena.Spawner.LiveFoeCount() == 0)
        {
            if (_waveIndex >= _totalWaves) _awaitingChoice = true;   // all clear → pick reward
            else SpawnWave(_waveIndex + 1);
        }
    }

    private void SpawnWave(int index)
    {
        _waveIndex = index;
        int level = index == _totalWaves ? FinalWaveLevel(_foeLevel) : _foeLevel;
        _arena.Spawner.SpawnWave(_waveSize, level);
    }

    public override bool NeedsRewardChoice => _awaitingChoice && !_chosen;
    public override void ChooseReward(bool atk) { _atkChoice = atk; _chosen = true; }

    public override RewardBundle Reward() =>
        _atkChoice ? new RewardBundle { AtkBonus = 10 } : new RewardBundle { DefBonus = 10 };

    public override string ProgressText =>
        _awaitingChoice ? "All waves cleared — choose your reward"
                        : $"Wave {_waveIndex}/{_totalWaves}";
    public override bool HasTimer => true;
    public override float TimeRemaining => _timer;
}

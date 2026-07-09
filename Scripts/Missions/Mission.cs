using Fableland.Map;
using Fableland.Run;

namespace Fableland.Missions;

/// <summary>Mission lifecycle state (T30 §3). Only a BOSS timer expiry is fatal — see
/// <see cref="Mission.FatalTimeout"/>; every other Failed is survivable (NODES §2.3).</summary>
public enum MissionStatus { Running, Succeeded, Failed }

/// <summary>
/// Strategy object for a combat node's goal (T30 §3, NODES §4). The arena
/// (<see cref="GameManager"/>) instantiates one by <see cref="MissionType"/>, calls
/// <see cref="Setup"/> once, then <see cref="Tick"/> each frame, and reads the HUD-facing
/// props below. Missions are ORCHESTRATION (T00): they touch gameplay + data (spawn foes,
/// place entities, read the player) but NEVER the HUD — GameManager polls these read-props
/// and forwards to the HUD, and forwards button presses back in (reward choice).
/// </summary>
public abstract class Mission
{
    public MissionStatus Status { get; protected set; } = MissionStatus.Running;

    /// <summary>Configure from the node level + the mission's own deterministic RNG sub-stream.</summary>
    public abstract void Setup(GameManager arena, int nodeLevel, DetRandom rng);

    /// <summary>Advance the mission by dt seconds (called from GameManager._Process while Running).</summary>
    public abstract void Tick(float dt);

    /// <summary>What the run gains on success (applied by RunState.ReportGoal). Cores that are
    /// credited progressively on pickup are NOT repeated here (avoid double-count).</summary>
    public abstract RewardBundle Reward();

    // ── HUD read-props (GameManager polls; missions never GetNode into the HUD) ──────
    public virtual string ProgressText => "";
    public virtual bool HasTimer => false;
    public virtual float TimeRemaining => 0f;

    // Secondary HP bar (protect core / boss). Hidden unless HasSecondaryBar is true.
    public virtual bool HasSecondaryBar => false;
    public virtual float SecondaryValue => 0f;
    public virtual float SecondaryMax => 1f;
    public virtual string SecondaryLabel => "";

    // ── Signals GameManager acts on ──────────────────────────────────────────────────

    /// <summary>Boss-only: the timer ran out. GameManager turns this into EndRun(BossTimer)
    /// (permadeath) when a run exists, instead of the normal survivable Failed flow.</summary>
    public virtual bool FatalTimeout => false;

    /// <summary>LV6 boss: killing it wins the run (EndRun(Victory)).</summary>
    public virtual bool IsFinalBoss => false;

    /// <summary>Slaughter: success is pending a +10 ATK / +10 DEF choice. While true, GameManager
    /// shows a 2-button choice UI; Status stays Running until <see cref="ChooseReward"/> is called.</summary>
    public virtual bool NeedsRewardChoice => false;
    public virtual void ChooseReward(bool atk) { }

    /// <summary>QA cheat (40-QA §1): force the goal to Succeeded. Debug builds only — called by
    /// GameManager on F9. Skips any pending reward choice (the mission's default reward applies).</summary>
    public void DebugForceComplete() => Status = MissionStatus.Succeeded;
}

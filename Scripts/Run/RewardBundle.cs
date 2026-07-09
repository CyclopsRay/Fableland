using System.Collections.Generic;

namespace Fableland.Run;

/// <summary>
/// Dumb bag of rewards an Adventure hands back to <see cref="RunState.ReportGoal"/> on success.
/// The mission/scene decides WHAT is in it (per NODES §4.1); RunState decides how to APPLY it.
/// </summary>
public sealed class RewardBundle
{
    public int WonderCores;
    public List<string> ItemDefIds = new();      // items granted (added to inventory)
    public int AtkBonus;                          // e.g. Slaughter "+10 ATK"
    public int DefBonus;                          // e.g. Slaughter "+10 DEF"
    public List<string> ProtagonistGrants = new();// e.g. LV4 boss recruit
}

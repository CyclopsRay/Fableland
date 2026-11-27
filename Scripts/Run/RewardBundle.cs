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

    /// <summary>"+10 wonder cores · FanChen's Heart · +10 ATK" — what this bundle gives, in a
    /// player's words, or "" when it gives nothing (audit v0.11.1 #2: rewards were never named).</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (WonderCores != 0) parts.Add($"+{WonderCores} wonder core{(WonderCores == 1 ? "" : "s")}");
        if (ItemDefIds != null)
            foreach (string id in ItemDefIds) parts.Add(Fableland.Items.ItemCatalog.DisplayName(id));
        if (AtkBonus != 0) parts.Add($"+{AtkBonus} ATK");
        if (DefBonus != 0) parts.Add($"+{DefBonus} DEF");
        return string.Join(" · ", parts);
    }
}

namespace Fableland.Run;

/// <summary>
/// The <b>run copy</b> of a protagonist's mutable state (T30 §1). Permanent changes from
/// Blessings (Sharpen / Rest excess) and future items write here — never to Data tables or
/// to live scene nodes (nodes die with scenes; RunState is the truth). On scene entry a
/// character hydrates from this; on exit / death it writes back.
/// </summary>
public sealed class ProtagonistState
{
    public string Id;               // e.g. "Pomegraknight"

    /// <summary>Current HP / max HP, carried between fights and inherited on Tab-switch (NODES §3.3).</summary>
    public float HpRatio = 1f;

    // Permanent per-run pools (additive). NODES §5.5 / §3.3: ATK/DEF/MaxHP Blessings are GLOBAL,
    // so RunState applies them to every owned protagonist equally — the pools live per-protagonist
    // so a future non-global source can target one character without reworking the model.
    public int BonusAtk;
    public int BonusDef;
    public int MaxHpPercentPoints;  // additive percentage points: two full-HP Rests → +10, +20 (NOT compounding)

    /// <summary>The single held wonder-item slot (defId, null = empty). ONE held slot per
    /// protagonist (ITEMS.gdd §1.1 / T30 §4). These fields are the held location of the same
    /// concrete instance that otherwise lives in RunState.Items; RunState inventory transactions
    /// move all four fields together.</summary>
    public string HeldItemDefId;
    public string HeldItemInstanceId;

    /// <summary>Remaining daily cooldown of the held item. This lives with the held slot so
    /// backpack items cannot progress their cooldown outside the party.</summary>
    public int HeldItemDayCooldownRemaining;

    /// <summary>Remaining real-time combat cooldown for the held item's skill. It ticks only
    /// while this protagonist is the live arena body; backpack items are inert.</summary>
    public float HeldItemSecondCooldownRemaining;

    /// <summary>True when <see cref="HeldItemDefId"/> was drawn from the real backpack (so it must
    /// return there on unhold / bump-out). False for a debug Team-Build "conjured" catalog item
    /// that was never in <c>RunState.Items</c> — that one vanishes on return and must NEVER
    /// materialize into the real economy. In the full item system every held item comes from the
    /// backpack, so this is always true there; it exists only to keep the v0.6.0 debug display
    /// bypass honest (see RunState.HoldItem/UnholdItem).</summary>
    public bool HeldItemFromBackpack;

    // Per-skill cooldown remaining, saved on switch-out and ticked down while benched
    // at the background CD rate = 1/(2n−2) (NODES §3.3). Max values come from the live
    // character's exported properties (ShiftCooldown / ESkillCooldown); only the remaining
    // time is persisted here.
    public float ShiftCdRemaining;
    public float ESkillCdRemaining;

    // Universal basic-attack magazine state. It is persisted while this protagonist is
    // benched so a Tab switch cannot create a free reload (Gameplay.gdd §A.2.1/A.3).
    public bool AmmoInitialized;
    public int AmmoCurrent;
    public float AmmoAttackCooldownRemaining;
    public bool AmmoReloadActive;
    public float AmmoReloadRemaining;

    public ProtagonistState(string id) { Id = id; }
}

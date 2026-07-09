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

    public ProtagonistState(string id) { Id = id; }
}

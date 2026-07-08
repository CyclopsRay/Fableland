namespace Fableland.Run;

/// <summary>
/// A concrete wonder item carried during a run. STUB for v0.5.0 — only the def id exists;
/// the full instance model (day/second cooldowns, perish timer, rolled stats, location)
/// lands with the item system in v0.6.0 (T30 §4).
/// </summary>
public sealed class ItemInstance
{
    public string DefId;   // → future ItemRegistry

    public ItemInstance(string defId) { DefId = defId; }
}

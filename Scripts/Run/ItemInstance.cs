namespace Fableland.Run;

/// <summary>
/// A concrete wonder item carried during a run. The minimal instance state includes its
/// day cooldown so a held wonder can retain its progress when the player changes equipment.
/// </summary>
public sealed class ItemInstance
{
    public string DefId;   // → future ItemRegistry
    public int DayCooldownRemaining;

    public ItemInstance(string defId) { DefId = defId; }
}

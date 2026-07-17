namespace Fableland.Run;

/// <summary>
/// A concrete wonder item carried during a run. Identity and mutable cooldown state belong to the
/// instance, never to its catalog definition. An instance is either in the backpack or represented
/// by the matching held-item fields on one protagonist (RunState owns every move between them).
/// </summary>
public sealed class ItemInstance
{
    public string InstanceId;
    public string DefId;
    public int DayCooldownRemaining;
    public float SecondCooldownRemaining;

    public ItemInstance(string defId, string instanceId = null)
    {
        DefId = defId;
        InstanceId = instanceId ?? "";
    }
}

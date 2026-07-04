using Godot;

/// <summary>
/// Frozen pit — applies a stack of the Frozen hazard debuff every
/// <see cref="Hazard.TickInterval"/> while a body stands in it. No knockback.
/// </summary>
public partial class FreezeHazard : Hazard
{
    [Export] public float FrozenStack = 20f;

    protected override Color TintFill => new Color(0.35f, 0.75f, 1f, 0.55f);
    protected override Color TintEdge => new Color(0.6f, 0.9f, 1f, 0.95f);

    protected override void ApplyTick(Node2D body) =>
        Deliver(body, 0f, Vector2.Zero, frozenStack: FrozenStack);
}

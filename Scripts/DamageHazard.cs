using Godot;

/// <summary>
/// A plain damage hazard — no debuff, no knockback, just raw damage every
/// <see cref="Hazard.TickInterval"/> while a body stands in it.
/// </summary>
public partial class DamageHazard : Hazard
{
    [Export] public float Damage = 20f;

    protected override Color TintFill => new Color(0.75f, 0.1f, 0.1f, 0.55f);
    protected override Color TintEdge => new Color(1f, 0.25f, 0.2f, 0.95f);

    protected override void ApplyTick(Node2D body) => Deliver(body, Damage, Vector2.Zero);
}

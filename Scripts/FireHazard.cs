using Godot;

/// <summary>
/// Bonfire — applies a stack of the OnFire hazard debuff plus a slight knockback
/// every <see cref="Hazard.TickInterval"/> while a body stands in it.
/// </summary>
public partial class FireHazard : Hazard
{
    [Export] public float FireStack = 20f;
    [Export] public float Knockback = 64f;   // ~2 m/s — a slight shove, not a launch

    protected override Color TintFill => new Color(1f, 0.4f, 0.1f, 0.55f);
    protected override Color TintEdge => new Color(1f, 0.65f, 0.2f, 0.95f);

    protected override void ApplyTick(Node2D body)
    {
        Vector2 to = body.GlobalPosition - GlobalPosition;
        Vector2 dir = to.LengthSquared() > 0.01f ? to.Normalized() : Vector2.Up;
        Deliver(body, 0f, dir * Knockback, fireStack: FireStack);
    }
}

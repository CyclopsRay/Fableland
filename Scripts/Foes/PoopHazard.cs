using Godot;

/// <summary>
/// The lingering micro-hazard left by a landed <see cref="PoopProjectile"/> (FOES §4).
/// A <see cref="Hazard"/> subclass so it reuses the box-from-BoxSize + per-body ticking
/// machinery; it only damages the player (a seagull dropping shouldn't hurt other foes)
/// and self-frees after <see cref="Life"/> seconds.
///
/// <see cref="BoxSize"/>, <see cref="Damage"/>, and <see cref="Life"/> are set by the
/// projectile before AddChild (object initializer), so base._Ready sees the box size.
/// </summary>
public partial class PoopHazard : Hazard
{
    public float Damage = 30f;
    public float Life = 3f;
    private float _life;

    public override void _Ready()
    {
        base._Ready();
        _life = Life;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        _life -= (float)delta;
        if (_life <= 0f) QueueFree();
    }

    protected override void ApplyTick(Node2D body)
    {
        // Player-only: bypasses i-frames via ApplyHazard, like any hazard tick.
        if (body is CharacterController player) player.ApplyHazard(Damage, Vector2.Zero);
    }

    protected override Color TintFill => new Color(0.45f, 0.32f, 0.12f, 0.5f);
    protected override Color TintEdge => new Color(0.3f, 0.2f, 0.08f, 0.9f);
}

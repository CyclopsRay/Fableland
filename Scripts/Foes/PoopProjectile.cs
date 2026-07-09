using Godot;

/// <summary>
/// Seagull Poop (FOES.gdd §4, Skill 1) — falls with gravity, deals its damage on a
/// direct hit mid-air, and on reaching a surface spawns a short-lived
/// <see cref="PoopHazard"/> that lingers 3 s dealing the same damage. Reuses the
/// existing Hazard ticking machinery for the linger (T30 §2), rather than re-inventing it.
///
/// <see cref="Init"/> runs after AddChild; the lifetime is seeded in _Ready so the
/// projectile never self-frees on the first physics frame (KNOWLEDGE caveat).
/// </summary>
public partial class PoopProjectile : Area2D
{
    [Export] public float FallGravity = Units.Gravity;   // avoid hiding Area2D.Gravity
    [Export] public float LingerTime = 3f;
    [Export] public float Lifetime = 6f;
    [Export] public Vector2 LingerBox = new Vector2(40f, 24f);

    private float _damage = 30f;
    private Vector2 _vel;
    private float _life;
    private bool _spent;

    /// <summary>Called by the seagull right after AddChild (so it runs after _Ready).</summary>
    public void Init(float damage) => _damage = damage;

    public override void _Ready()
    {
        SetCollisionLayerValue(Units.LayerProjectile, true);
        SetCollisionMaskValue(Units.LayerPlayer, true);
        SetCollisionMaskValue(Units.LayerGround, true);
        SetCollisionMaskValue(Units.LayerPlatform, true);
        BodyEntered += OnBodyEntered;
        _life = Lifetime;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_spent) return;
        float dt = (float)delta;
        _vel.Y += FallGravity * dt;
        Position += _vel * dt;
        _life -= dt;
        if (_life <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_spent) return;

        // Direct hit on the player during the drop (FOES §4: "Yes — direct hit deals it").
        if (body is CharacterController player)
        {
            _spent = true;
            player.TakeHit(new HitInfo(_damage, new Vector2(0f, 120f), 0.1f));
            QueueFree();
            return;
        }

        // Surface (Ground/Platform) while falling → land and leave a lingering hazard.
        if (_vel.Y > 0f)
        {
            _spent = true;
            var h = new PoopHazard { BoxSize = LingerBox, Damage = _damage, Life = LingerTime };
            GetParent().AddChild(h);
            h.GlobalPosition = GlobalPosition;
            QueueFree();
        }
    }
}

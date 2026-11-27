using Godot;

namespace Fableland.Bosses;

/// <summary>
/// One coin of the Ferryman's Obol Toss (Docs/Zone6.gdd §4.1): thrown in an arc under the
/// thrower's share of gravity (the Void Frontier's G included); the first fighter of the other
/// side it touches takes its damage and pays the toll — ult charge. It ends on that fighter, on
/// the ground, or after a few seconds.
/// </summary>
public partial class ObolCoin : Area2D
{
    public const float Lifetime = 4f;
    public const float Radius = 9f;

    private Vector2 _velocity;
    private float _gravityScale = 1f;
    private float _damage;
    private float _toll;
    private CharacterController _owner;
    private CombatSide _side = CombatSide.Foe;
    private float _life = Lifetime;
    private bool _spent;

    /// <summary>How much ult charge the coin took from the fighter it hit (checks read it).</summary>
    public float TollTaken { get; private set; }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionLayerValue(Units.LayerProjectile, true);
        SetCollisionMaskValue(Units.LayerGround, true);
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = Radius } });
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Called right after AddChild.</summary>
    public void Init(Vector2 velocity, CharacterController owner, float damage, float toll)
    {
        _velocity = velocity;
        _owner = owner;
        _damage = damage * (owner?.DamageDealtScale ?? 1f);
        _toll = toll;
        _gravityScale = owner?.GravityScale ?? 1f;
        _side = owner?.Side ?? CombatSide.Foe;
        CombatTargets.AimMask(this, _side);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _velocity.Y += Units.Gravity * _gravityScale * dt;
        Position += _velocity * dt;
        _life -= dt;
        if (_life <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_spent) return;
        if (CombatTargets.TryGetHostile(body, _side, out ICombatTarget target))
        {
            _spent = true;
            // The toll is net: the blow itself would pay the victim charge for the damage it
            // takes, and the Ferryman takes his 150 all the same.
            float before = (target as CharacterController)?.UltCharge ?? 0f;
            HitInfo hit = _owner != null && IsInstanceValid(_owner)
                ? new HitInfo(_damage, new Vector2(Mathf.Sign(_velocity.X) * Units.Px(3f), -Units.Px(2f))).FromRanged(_owner, "s1.obol")
                : new HitInfo(_damage).FromRanged("ferryman", "s1.obol");
            target.TakeHit(hit, GlobalPosition);
            if (target is CharacterController fighter)
            {
                fighter.DrainUltCharge(fighter.UltCharge - Mathf.Max(0f, before - _toll));
                TollTaken = before - fighter.UltCharge;
            }
            QueueFree();
            return;
        }
        if (body is StaticBody2D) QueueFree();   // the ground keeps what it is thrown
    }

    /// <summary>NOTE(art): placeholder — a worn bronze coin with the guardian's amber spot.</summary>
    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, new Color("#C98A2B"));
        DrawArc(Vector2.Zero, Radius - 2f, 0f, Mathf.Tau, 16, new Color("#FFD27A"), 2f);
    }
}

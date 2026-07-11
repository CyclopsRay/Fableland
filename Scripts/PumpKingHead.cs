using Godot;
using System;

/// <summary>
/// PumpKing's detached rolling head — a player projectile launched from a skill.
/// Manual kinematic physics (no RigidBody2D): gravity + clamp + MoveAndCollide,
/// bouncing off Ground/Platform and reflecting until it comes to rest, then
/// detonating (or on foe contact, or after Lifetime, or via a manual Explode()
/// call from the owning skill). Mouse steering existed in the Unity source but
/// was OFF by default there — NOT ported here.
/// </summary>
public partial class PumpKingHead : CharacterBody2D
{
    [Export] public float MaxRollSpeed = Units.Px(6f);        // 6 m/s = 192 px/s, horizontal cap
    [Export] public float MaxVerticalSpeed = Units.Px(15f);   // 15 m/s = 480 px/s, |vy| cap
    [Export] public float BounceRetention = 0.85f;
    [Export] public float FallGravity = Units.Gravity;        // 2048 px/s² (never "Gravity" — shadows CharacterBody2D)
    [Export] public float Lifetime = 10f;
    [Export] public float StillVelThreshold = Units.Px(0.1f); // 0.1 m/s = 3.2 px/s
    [Export] public float StillDuration = 0.5f;
    [Export] public float ContactDamage = 30f;
    [Export] public float RollFriction = Units.Px(3f);        // 3 m/s² = 96 px/s² tangential decel while resting on a floor (Godot adaptation — see Behavior notes)
    [Export] public float ExplosionAnimHold = 0.65f;          // seconds from Explode() to QueueFree, covers the 0.583 s explode clip
    [Export] public float ExplosionVisualScale = 0.55f;       // Sprite2D scale during "explode" so 384-px art covers the 3 m / 192 px explosion diameter

    public bool Exploded => _exploded;

    private Vector2 _velocity;
    private Node2D _owner;
    private float _dmgMult = 1f;
    private Action<Vector2> _onExplode;
    private bool _autonomous;

    private bool _initialized;
    private bool _exploded;
    private float _age;
    private float _stillTimer;
    private float _freeTimer;

    private Vector2 _baseSpriteScale;
    private Sprite2D _sprite;
    private AnimationPlayer _anim;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _anim = GetNode<AnimationPlayer>("AnimationPlayer");
        _baseSpriteScale = _sprite.Scale;   // captured before Init overwrites it
    }

    /// <summary>Called by the launching skill right after AddChild (so it runs after _Ready).</summary>
    public void Init(Vector2 launchVelocity, Node2D owner, float scaleMult, float dmgMult, Action<Vector2> onExplode)
    {
        _owner = owner;   // kept for reference/debug only — collision mask 14 already excludes
                           // the Player layer, so no physical collision exception is needed.
        _dmgMult = dmgMult;
        _onExplode = onExplode;
        _velocity = launchVelocity;

        _sprite.Scale = _baseSpriteScale * scaleMult;
        _anim.Play("roll");
        _initialized = true;
    }

    public void SetAutonomous(bool value) => _autonomous = value;

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized) return;
        float dt = (float)delta;

        if (_exploded)
        {
            _freeTimer -= dt;
            if (_freeTimer <= 0f) QueueFree();
            return;
        }

        _age += dt;
        if (_age >= Lifetime) { Explode(); return; }

        _velocity.Y += FallGravity * dt;
        _velocity.X = Mathf.Clamp(_velocity.X, -MaxRollSpeed, MaxRollSpeed);
        _velocity.Y = Mathf.Clamp(_velocity.Y, -MaxVerticalSpeed, MaxVerticalSpeed);

        var col = MoveAndCollide(_velocity * dt);
        if (col != null)
        {
            if (col.GetCollider() is BaseFoe foe)
            {
                foe.TakeHit(new HitInfo(ContactDamage * _dmgMult, Vector2.Zero), GlobalPosition);
                Explode();
                return;
            }

            // MoveAndCollide does not mutate _velocity on collision (it only reports the
            // collision info) — _velocity here is still the pre-collision velocity, so no
            // Unity-style "cache velocity before physics step" snapshot is needed.
            Vector2 reflected = _velocity.Bounce(col.GetNormal()) * BounceRetention;

            // Rest-state handling: a near-flat floor hit with a tiny bounce should settle
            // into a roll instead of micro-bouncing forever (manual bounce has no built-in
            // "resting" concept the way a RigidBody2D would).
            bool floorLike = col.GetNormal().Y < -0.7f;
            if (floorLike && Mathf.Abs(reflected.Y) < Units.Px(1.5f))
                reflected.Y = 0f;

            _velocity = reflected;

            if (floorLike)
                _velocity.X = Mathf.MoveToward(_velocity.X, 0f, RollFriction * dt);
        }

        // Cosmetic: the roll art carries its own spin, so flip instead of rotating the node.
        _sprite.FlipH = _velocity.X < 0f;

        if (_velocity.Length() < StillVelThreshold)
        {
            _stillTimer += dt;
            if (_stillTimer >= StillDuration) { Explode(); return; }
        }
        else
        {
            _stillTimer = 0f;
        }
    }

    /// <summary>Idempotent — safe to call more than once (contact, still-timer, lifetime,
    /// or an external skill can all race to trigger it).</summary>
    public void Explode()
    {
        if (_exploded) return;
        _exploded = true;

        // An autonomous head (e.g. a wandering/loose head) plays the visual but deals NO
        // area damage — exact port of the Unity behaviour. Only a non-autonomous head
        // (still owned/aimed by the skill that launched it) invokes the AoE callback.
        if (!_autonomous) _onExplode?.Invoke(GlobalPosition);

        _velocity = Vector2.Zero;

        var shape = GetNode<CollisionShape2D>("CollisionShape2D");
        shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

        _sprite.FlipH = false;
        _sprite.Scale = Vector2.One * ExplosionVisualScale;
        _anim.Play("explode");

        _freeTimer = ExplosionAnimHold;
    }
}

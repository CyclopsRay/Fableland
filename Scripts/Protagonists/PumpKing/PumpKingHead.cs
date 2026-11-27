using Godot;
using System;

/// <summary>
/// PumpKing's detached rolling head — a player projectile launched from a skill.
/// Manual kinematic physics (no RigidBody2D): gravity + clamp + MoveAndCollide,
/// bouncing off Ground/Platform and reflecting until it comes to rest, then
/// detonating (or on foe contact, or after Lifetime, or via a manual Explode()
/// call from the owning skill). Mouse steering existed in the Unity source but
/// was OFF by default there — NOT ported here.
/// <see cref="MaxRollSpeed"/> only clamps horizontal speed once the head has actually
/// touched down (<see cref="_grounded"/>) — the initial launch impulse flies free so it
/// covers real distance before settling into a capped "roll," matching the name.
///
/// Bounce or roll (v0.10.11). A touchdown that would leave the surface faster than
/// <see cref="RestBounceSpeed"/> is a bounce: the whole velocity is reflected and keeps
/// <see cref="BounceRetention"/>. Anything gentler is a ROLL: the part of the velocity
/// along the surface is kept, slowed only by <see cref="RollFriction"/>, and the rest of
/// the tick's motion is spent sliding along it. So on level ground the head rolls to a
/// stop and goes off; on a slope gravity out-pulls the friction and it runs downhill at
/// the roll cap until something stops it — the head "crashing along the slope" that
/// PumpKing's castle is built around (FOES.gdd §8.3).
///
/// The head fights for whoever launched it (<see cref="CombatSide"/>): the player's
/// PumpKing bowls at foes, the BOSS PumpKing bowls at the player.
/// </summary>
public partial class PumpKingHead : CharacterBody2D
{
    [Export] public float MaxRollSpeed = Units.Px(6f);        // 6 m/s = 192 px/s, horizontal cap ONCE GROUNDED (not during the initial flight arc)
    [Export] public float MaxVerticalSpeed = Units.Px(15f);   // 15 m/s = 480 px/s, |vy| cap
    [Export] public float BounceRetention = 0.85f;
    [Export] public float FallGravity = Units.Gravity;        // 2048 px/s² (never "Gravity" — shadows CharacterBody2D)
    [Export] public float Lifetime = 10f;
    [Export] public float StillVelThreshold = Units.Px(0.1f); // 0.1 m/s = 3.2 px/s
    [Export] public float StillDuration = 0.5f;
    [Export] public float ContactDamage = 30f;
    [Export] public float RollFriction = Units.Px(3f);        // 3 m/s² = 96 px/s² tangential decel while rolling on a floor (Godot adaptation — see Behavior notes)
    [Export] public float RestBounceSpeed = Units.Px(1.5f);   // 1.5 m/s: a touchdown leaving the floor slower than this rolls instead of bouncing
    [Export] public float ExplosionAnimHold = 0.65f;          // seconds from Explode() to QueueFree, covers the 0.583 s explode clip
    [Export] public float ExplosionVisualScale = 0.55f;       // Sprite2D scale during "explode" so 384-px art covers the 3 m / 192 px explosion diameter

    public bool Exploded => _exploded;

    private Vector2 _velocity;
    private Node2D _owner;
    private float _gravityScale = 1f;   // the thrower's share of gravity at the throw (Zone6.gdd §7)
    private string _cueOwner = "pumpking";   // whose style the head's cues and hits take
    private float _dmgMult = 1f;
    private Action _onResolved;
    private CombatSide _side = CombatSide.Player;
    private bool _autonomous;
    private float _explosionRadius;
    private float _explosionDamage;
    private float _explosionKnockback;

    private bool _initialized;
    private bool _exploded;
    private bool _grounded;    // true once the head has touched Ground/Platform at least once
    private int _ticksSinceRoll = int.MaxValue / 2;   // physics ticks since it was last rolling on a floor
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
    public void Init(Vector2 launchVelocity, Node2D owner, float scaleMult, float dmgMult,
        float explosionRadius, float explosionDamage, float explosionKnockback, Action onResolved,
        CombatSide side = CombatSide.Player)
    {
        _gravityScale = owner is CharacterController launcher ? launcher.GravityScale : 1f;
        _owner = owner;   // credited with the damage the head deals (Credit); the mask below never
                           // includes the launcher's own layer, so no collision exception is needed.
        // The head looks for bodies on the other side: the Foes layer when the player's
        // PumpKing threw it (the scene's authored mask), the Player layer when the BOSS did.
        _side = side;
        CombatTargets.AimMask(this, side);
        if (owner is IFeedbackBody thrower) _cueOwner = thrower.FeedbackId;
        _dmgMult = dmgMult;
        _explosionRadius = explosionRadius;
        _explosionDamage = explosionDamage;
        _explosionKnockback = explosionKnockback;
        _onResolved = onResolved;
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
        _ticksSinceRoll++;

        _velocity.Y += FallGravity * _gravityScale * dt;
        // MaxRollSpeed models tangential rolling friction, not flight drag — clamping it
        // unconditionally used to eat the entire launch impulse on the very first tick
        // (Init() → this runs before the head has moved at all), so the head could never
        // actually travel on the initial throw. Only cap once it's touched a floor.
        if (_grounded)
            _velocity.X = Mathf.Clamp(_velocity.X, -MaxRollSpeed, MaxRollSpeed);
        _velocity.Y = Mathf.Clamp(_velocity.Y, -MaxVerticalSpeed, MaxVerticalSpeed);

        Vector2 motion = _velocity * dt;
        var col = MoveAndCollide(motion);
        if (col != null && ResolveContact(col, motion, dt)) return;

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

    /// <summary>
    /// What one contact does to the head. Returns true when the head went off (it touched
    /// someone it fights) and the tick is over.
    ///
    /// MoveAndCollide does not mutate _velocity on collision (it only reports the collision
    /// info) — _velocity here is still the pre-collision velocity, so no Unity-style "cache
    /// velocity before physics step" snapshot is needed.
    /// </summary>
    private bool ResolveContact(KinematicCollision2D col, Vector2 motion, float dt)
    {
        if (TryStrike(col)) return true;

        Vector2 normal = col.GetNormal();
        bool floorLike = normal.Y < -0.7f;
        if (!floorLike)
        {
            _velocity = _velocity.Bounce(normal) * BounceRetention;   // wall or ceiling
            return false;
        }

        _grounded = true;
        float into = -_velocity.Dot(normal);                 // speed into the surface (> 0)
        // A head that was rolling a tick ago and meets floor at another angle — the foot of
        // a slope — keeps rolling: the change of slope is not a fall. Without this it bounced
        // four times there and arrived on the level with half its speed.
        bool wasRolling = _ticksSinceRoll <= 1;
        if (!wasRolling && into * BounceRetention >= RestBounceSpeed)
        {
            _velocity = _velocity.Bounce(normal) * BounceRetention;
            return false;
        }
        _ticksSinceRoll = 0;

        // Too gentle to bounce: roll. Keep what runs along the surface — gravity's pull
        // down a slope included, since it was added to _velocity before the move — and let
        // the roll friction work against it. (Until v0.10.11 this branch scaled the whole
        // velocity by BounceRetention on EVERY resting tick and threw away the rest of the
        // move, so a "rolling" head died within 0.7 m on the flat and crawled on a slope.)
        Vector2 along = _velocity - _velocity.Dot(normal) * normal;
        _velocity = along.MoveToward(Vector2.Zero, RollFriction * dt);
        // The roll cap is on speed ACROSS, and it must hold on a slope too: there the pull
        // of gravity is added after the cap at the top of the tick, so cap what is kept.
        float across = Mathf.Abs(_velocity.X);
        if (across > MaxRollSpeed) _velocity *= MaxRollSpeed / across;

        // Spend what is left of the tick rolling: at the rolling velocity, not along the
        // leftover of the pre-contact motion (that one still carries this tick's uncapped
        // gravity, and on a slope would outrun the cap).
        float unspent = motion.LengthSquared() > 0.0001f ? col.GetRemainder().Length() / motion.Length() : 0f;
        Vector2 rest = _velocity * (dt * unspent);
        if (rest.LengthSquared() > 0.0001f)
        {
            KinematicCollision2D next = MoveAndCollide(rest);
            if (next != null)
            {
                if (TryStrike(next)) return true;
                // Rolled into something that is not floor (a wall at the foot of a slope).
                Vector2 hit = next.GetNormal();
                if (hit.Y >= -0.7f) _velocity = _velocity.Bounce(hit) * BounceRetention;
            }
        }
        return false;
    }

    /// <summary>Touching a body the head fights: contact damage, then it goes off.</summary>
    private bool TryStrike(KinematicCollision2D col)
    {
        if (!CombatTargets.TryGetHostile(col.GetCollider(), _side, out ICombatTarget target)) return false;
        Credit(target.TakeHit(new HitInfo(ContactDamage * _dmgMult, Vector2.Zero)
            .FromRanged(_cueOwner, "ba.head", _owner), GlobalPosition));
        Explode();
        return true;
    }

    /// <summary>Idempotent — safe to call more than once (contact, still-timer, lifetime,
    /// or an external skill can all race to trigger it). <paramref name="manual"/> is true
    /// only for a player-issued BA detonate.</summary>
    public void Explode(bool manual = false)
    {
        if (_exploded) return;
        _exploded = true;

        // An autonomous head that goes off BY ITSELF (still-timer/lifetime while
        // PumpKing is off in Soul Form) plays the visual but deals NO area damage —
        // exact port of the Unity behaviour. Every resolved explosion still notifies
        // its owner-state so the universal magazine can reload. A deliberate manual
        // detonate (BA, once PumpKing is back and Rolling — see ExitSoul) deals area
        // damage even when autonomous.
        bool dealsAreaDamage = !_autonomous || manual;
        // One cue for the blast, with its real radius; a head that only pops (no area
        // damage) says so. What it shakes or sounds like is a listener's table row.
        CueBus.EmitAt(_cueOwner, "ba.explode", GlobalPosition, null, _side, size: _explosionRadius,
            magnitude: dealsAreaDamage ? _explosionDamage : 0f, tag: dealsAreaDamage ? null : "dud",
            ownerClass: CueOwners.Projectile);
        if (dealsAreaDamage) DealExplosion();
        _onResolved?.Invoke();

        _velocity = Vector2.Zero;

        var shape = GetNode<CollisionShape2D>("CollisionShape2D");
        shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

        _sprite.FlipH = false;
        _sprite.Scale = Vector2.One * ExplosionVisualScale;
        _anim.Play("explode");

        _freeTimer = ExplosionAnimHold;
    }

    /// <summary>Explosion ownership lives on the projectile, not a transient
    /// protagonist body, so a rolling head continues to damage foes after Tab. It hits
    /// whoever the head's side fights — foes for the player's head, the player for the BOSS's.</summary>
    private void DealExplosion()
    {
        foreach (ICombatTarget foe in CombatTargets.HostileTo(GetTree(), _side))
        {
            Vector2 to = foe.GlobalPosition - GlobalPosition;
            float distance = to.Length();
            if (distance - foe.HitRadius > _explosionRadius) continue;

            Vector2 knock = (distance > 0.01f ? to / distance : Vector2.Right) * _explosionKnockback;
            Credit(foe.TakeHit(new HitInfo(_explosionDamage, knock).FromRanged(_cueOwner, "ba.explode", _owner),
                GlobalPosition));
        }
    }

    /// <summary>What the head dealt is its thrower's, while the thrower is still there (a Tab
    /// switch frees the body and the head rolls on): omni-vamp and the like hear it (audit
    /// v0.11.1 #14).</summary>
    private void Credit(float dealt)
    {
        if (dealt > 0f && _owner is CharacterController thrower && IsInstanceValid(thrower)) thrower.ReportDamageDealt(dealt);
    }
}

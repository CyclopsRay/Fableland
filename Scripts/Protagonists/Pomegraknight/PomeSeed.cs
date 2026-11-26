using Godot;
using System.Collections.Generic;

/// <summary>
/// A Pome Seed projectile — gravity-affected, spawned in waves by Pomegraknight's
/// E. Seeds of the same wave share a hit registry so the first seed to strike a
/// target deals full damage and later seeds of that wave deal the reduced amount.
///
/// Ground and Platform resolve a seed immediately without a damage hit, as specified
/// by Pomegraknight.gdd. SoftVolumes are Areas, so seeds pass through them.
/// </summary>
public partial class PomeSeed : Area2D
{
    [Export] public float FallGravity = 1200f;   // avoid hiding Area2D.Gravity
    [Export] public float Lifetime = 6f;
    [Export] public float DamageFirst = 30f;
    [Export] public float DamageSubsequent = 6f;
    [Export] public float BurnDuration = 2f;

    private Vector2 _velocity;
    private HashSet<ulong> _waveHits;
    private bool _burning;
    private float _life = 6f;
    private float _dmgMult = 1f;
    private CharacterController _damageOwner;
    private CombatSide _side = CombatSide.Player;
    // The owner's content id, kept as a string: a seed outlives a Tab switch, and its hit
    // and its last cue still belong to whoever fired it.
    private string _cueOwner = "pomegraknight";
    private bool _resolved;   // a seed ends once: QueueFree is deferred, body_entered is not

    /// <summary>Called by the spawner right after AddChild (so it runs after _Ready).</summary>
    public void Init(Vector2 velocity, HashSet<ulong> waveHits, bool burning, float dmgMult = 1f,
        CharacterController damageOwner = null)
    {
        _velocity = velocity;
        _waveHits = waveHits;
        _burning = burning;
        _dmgMult = dmgMult;
        _damageOwner = damageOwner;
        // A seed fights for whoever fired it, and looks for bodies on the other side.
        _side = damageOwner?.Side ?? CombatSide.Player;
        if (damageOwner != null) _cueOwner = damageOwner.FeedbackId;
        CombatTargets.AimMask(this, _side);
        _life = Lifetime;
        if (_burning)
        {
            var s = GetNodeOrNull<Sprite2D>("Sprite2D");
            if (s != null) s.SelfModulate = new Color(1f, 0.55f, 0.3f);
        }
    }

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        _velocity.Y += FallGravity * dt;
        Position += _velocity * dt;
        _life -= dt;
        if (_life <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        // Two bodies can be entered in one physics frame (a foe standing on the floor, the
        // floor and a wall): the seed is still alive for the second signal. First one wins.
        if (_resolved) return;
        _resolved = true;

        if (CombatTargets.TryGetHostile(body, _side, out ICombatTarget e))
        {
            ulong id = e.TargetId;
            float dmg;
            if (_waveHits != null && _waveHits.Contains(id))
            {
                dmg = DamageSubsequent;
            }
            else
            {
                dmg = DamageFirst;
                _waveHits?.Add(id);
            }

            Vector2 knock = (_velocity.LengthSquared() > 0.01f ? _velocity.Normalized() : Vector2.Down) * 80f;
            // A projectile's hit: the victim is held for it, the owner never is.
            float dealt = e.TakeHit(new HitInfo(dmg * _dmgMult, knock, 0.1f)   // small gain-no for seeds
                .FromRanged(_cueOwner, "s2.seed", _damageOwner), GlobalPosition);
            _damageOwner?.ReportDamageDealt(dealt);
            if (_burning) e.SetBurning(BurnDuration);
            QueueFree();
            return;
        }

        // Ground / Platform (or another terrain body) resolves the projectile without
        // applying its enemy hit or Burning effect. The seed is about to free itself, so the
        // cue carries a position and no source.
        CueBus.EmitAt(_cueOwner, "s2.seed.expire", GlobalPosition, null, _side,
            direction: _velocity.LengthSquared() > 0.01f ? _velocity.Normalized() : Vector2.Down,
            tag: _burning ? "burning" : null, ownerClass: CueOwners.Projectile);
        QueueFree();
    }
}

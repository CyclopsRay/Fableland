using Godot;
using System.Collections.Generic;

/// <summary>
/// A Pome Seed projectile — gravity-affected, spawned in waves by Pomegraknight's
/// E. Seeds of the same wave share a hit registry so the first seed to strike a
/// target deals full damage and later seeds of that wave deal the reduced amount.
///
/// After landing on Ground or a Platform (while falling), a seed lingers there for
/// a few seconds — a spent seed still bites a foe that walks into it before fading.
/// </summary>
public partial class PomeSeed : Area2D
{
    [Export] public float FallGravity = 1200f;   // avoid hiding Area2D.Gravity
    [Export] public float Lifetime = 6f;
    [Export] public float DamageFirst = 30f;
    [Export] public float DamageSubsequent = 6f;
    [Export] public float BurnDuration = 2f;
    [Export] public float LingerTime = 3f;   // rest on the ground after landing

    private Vector2 _velocity;
    private HashSet<ulong> _waveHits;
    private bool _burning;
    private bool _landed;
    private float _life = 6f;
    private float _lingerTimer;
    private float _dmgMult = 1f;

    /// <summary>Called by the spawner right after AddChild (so it runs after _Ready).</summary>
    public void Init(Vector2 velocity, HashSet<ulong> waveHits, bool burning, float dmgMult = 1f)
    {
        _velocity = velocity;
        _waveHits = waveHits;
        _burning = burning;
        _dmgMult = dmgMult;
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

        if (_landed)
        {
            _lingerTimer -= dt;
            if (_lingerTimer <= 0f) QueueFree();
            return;
        }

        _velocity.Y += FallGravity * dt;
        Position += _velocity * dt;
        _life -= dt;
        if (_life <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is Enemy e)
        {
            ulong id = e.GetInstanceId();
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
            e.TakeHit(new HitInfo(dmg * _dmgMult, knock, 0.1f));   // small gain-no for seeds
            if (_burning) e.SetBurning(BurnDuration);
            QueueFree();
            return;
        }

        // Ground / Platform (or any non-foe body): land and linger, but only when
        // falling — this lets a rising seed pass up through a one-way platform.
        if (!_landed && _velocity.Y > 0f)
        {
            _landed = true;
            _velocity = Vector2.Zero;
            _lingerTimer = LingerTime;
        }
    }
}

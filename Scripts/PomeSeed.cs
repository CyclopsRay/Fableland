using Godot;
using System.Collections.Generic;

/// <summary>
/// A Pome Seed projectile — gravity-affected, spawned in waves by Pomegraknight's
/// E. Seeds of the same wave share a hit registry so the first seed to strike a
/// target deals full damage and later seeds of that wave deal the reduced amount.
/// </summary>
public partial class PomeSeed : Area2D
{
    [Export] public float Gravity = 900f;
    [Export] public float Lifetime = 6f;
    [Export] public float DamageFirst = 30f;
    [Export] public float DamageSubsequent = 6f;
    [Export] public float BurnDuration = 2f;

    private Vector2 _velocity;
    private HashSet<ulong> _waveHits;
    private bool _burning;
    private float _life = 6f;

    /// <summary>Called by the spawner right after AddChild (so it runs after _Ready).</summary>
    public void Init(Vector2 velocity, HashSet<ulong> waveHits, bool burning)
    {
        _velocity = velocity;
        _waveHits = waveHits;
        _burning = burning;
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
        _velocity.Y += Gravity * dt;
        Position += _velocity * dt;
        _life -= dt;
        if (_life <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not Enemy e) return;

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
        e.TakeDamage(dmg, knock);
        if (_burning) e.SetBurning(BurnDuration);
        QueueFree();
    }
}

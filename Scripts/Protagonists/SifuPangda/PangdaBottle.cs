using Godot;

/// <summary>Simple straight projectile for Pangda's Heli E. It is initialized after
/// AddChild and therefore remains inert until every authored value is present.</summary>
public partial class PangdaBottle : Area2D
{
    private SifuPangda _pangda;
    private Vector2 _direction;
    private float _speedPxPerSecond;
    private float _remainingLifetime;
    private float _radiusPx;
    private float _damage;
    private float _trappedSeconds;
    private bool _initialized;

    public override void _Ready() => BodyEntered += OnBodyEntered;

    public void Init(SifuPangda pangda, Vector2 direction, float speedMps, float lifetime, float radiusM,
        float damage, float trappedSeconds)
    {
        _pangda = pangda;
        _direction = direction.Normalized();
        _speedPxPerSecond = Units.Px(speedMps);
        _remainingLifetime = lifetime;
        _radiusPx = Units.Px(radiusM);
        _damage = damage;
        _trappedSeconds = trappedSeconds;
        if (GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape is CircleShape2D circle)
            circle.Radius = _radiusPx;
        _initialized = true;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized) return;
        float dt = (float)delta;
        _remainingLifetime -= dt;
        if (_remainingLifetime <= 0f)
        {
            QueueFree();
            return;
        }
        GlobalPosition += _direction * _speedPxPerSecond * dt;
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!_initialized) return;
        if (body is BaseFoe foe)
        {
            float dealt = foe.TakeHit(new HitInfo(_damage, Vector2.Zero, 0f), GlobalPosition);
            foe.ApplyTrapped(_trappedSeconds);
            if (IsInstanceValid(_pangda)) _pangda.ReportDamageDealt(dealt);
        }
        QueueFree(); // Foes, Ground, and Platform are the only bodies on the authored mask.
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, _radiusPx, new Color(0.78f, 0.58f, 0.24f, 0.9f));
        DrawArc(Vector2.Zero, _radiusPx, 0f, Mathf.Tau, 16, new Color(1f, 0.9f, 0.55f), 2f);
    }
}

using System.Collections.Generic;
using Godot;

namespace Fableland.Bosses;

/// <summary>
/// The Asura's Vajra Throw (Docs/Zone6.gdd §5.1): flies out level for its reach, turns, and
/// flies back to the thrower's hand, hitting each fighter of the other side once on the way
/// out and once more on the way back.
/// </summary>
public partial class Vajra : Area2D
{
    public const float Radius = 14f;
    public const float MaxSeconds = 4f;

    private CharacterController _owner;
    private CombatSide _side = CombatSide.Foe;
    private float _direction = 1f, _speed, _outLeft, _damage, _life = MaxSeconds;
    private bool _returning;
    private readonly HashSet<ulong> _hit = new();

    /// <summary>Hits landed out and back (checks read them).</summary>
    public int HitsOut { get; private set; }
    public int HitsBack { get; private set; }
    public bool Returning => _returning;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionLayerValue(Units.LayerProjectile, true);
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = Radius } });
        BodyEntered += OnBodyEntered;
    }

    public void Init(CharacterController owner, float direction, float speedPx, float reachPx, float damage)
    {
        _owner = owner;
        _side = owner?.Side ?? CombatSide.Foe;
        _direction = direction >= 0f ? 1f : -1f;
        _speed = speedPx;
        _outLeft = reachPx;
        _damage = damage * (owner?.DamageDealtScale ?? 1f);
        CombatTargets.AimMask(this, _side);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _life -= dt;
        if (_life <= 0f || _owner == null || !IsInstanceValid(_owner)) { QueueFree(); return; }
        if (!_returning)
        {
            float step = _speed * dt;
            Position += new Vector2(_direction * step, 0f);
            _outLeft -= step;
            if (_outLeft <= 0f)
            {
                _returning = true;
                _hit.Clear();                 // the way back is a second chance to hit
                RecheckOverlaps();
            }
        }
        else
        {
            Vector2 to = _owner.GlobalPosition - GlobalPosition;
            if (to.Length() <= _speed * dt + Units.Px(0.4f)) { QueueFree(); return; }
            Position += to.Normalized() * _speed * dt;
        }
        Rotation += dt * 14f;
    }

    /// <summary>A body still inside the vajra when it turns is struck on the way back too.</summary>
    private void RecheckOverlaps()
    {
        foreach (Node2D body in GetOverlappingBodies()) OnBodyEntered(body);
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!CombatTargets.TryGetHostile(body, _side, out ICombatTarget target) || !_hit.Add(body.GetInstanceId())) return;
        float away = Mathf.Sign(body.GlobalPosition.X - GlobalPosition.X);
        if (away == 0f) away = _direction;
        HitInfo hit = new HitInfo(_damage, new Vector2(away * Units.Px(5f), -Units.Px(2f))).FromRanged(_owner, "s2.vajra", _returning ? 2 : 1);
        float dealt = target.TakeHit(hit, GlobalPosition);
        if (dealt <= 0f) return;
        if (_returning) HitsBack++;
        else HitsOut++;
    }

    /// <summary>NOTE(art): placeholder — a gold vajra spinning.</summary>
    public override void _Draw()
    {
        var gold = new Color("#E8B33A");
        DrawLine(new Vector2(-Radius, 0f), new Vector2(Radius, 0f), gold, 5f);
        DrawCircle(new Vector2(-Radius, 0f), 5f, gold);
        DrawCircle(new Vector2(Radius, 0f), 5f, gold);
        DrawCircle(Vector2.Zero, 4f, new Color("#B23A26"));
    }
}

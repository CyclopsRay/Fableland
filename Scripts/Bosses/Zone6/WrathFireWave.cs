using System.Collections.Generic;
using Godot;

namespace Fableland.Bosses;

/// <summary>
/// One of the two floor waves of the Asura's Wrath Fire (Docs/Zone6.gdd §5.1): a low wall of
/// fire, 1 m tall, that runs along the floor for its reach and hits each fighter of the other
/// side once — damage and OnFire stacks. A jump clears it.
/// </summary>
public partial class WrathFireWave : Area2D
{
    public const float HeightM = 1f;
    public const float WidthM = 1.2f;

    private CharacterController _owner;
    private CombatSide _side = CombatSide.Foe;
    private float _direction = 1f, _speed, _left, _damage, _stacks;
    private readonly HashSet<ulong> _hit = new();

    public int HitCount => _hit.Count;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionLayerValue(Units.LayerHazard, true);
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(Units.Px(WidthM), Units.Px(HeightM)) },
            Position = new Vector2(0f, -Units.Px(HeightM) * 0.5f),   // its foot on the floor
        });
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Called right after AddChild, with the wave's foot on the floor.</summary>
    public void Init(CharacterController owner, float direction, float speedPx, float reachPx, float damage, float stacks)
    {
        _owner = owner;
        _side = owner?.Side ?? CombatSide.Foe;
        _direction = direction >= 0f ? 1f : -1f;
        _speed = speedPx;
        _left = reachPx;
        _damage = damage * (owner?.DamageDealtScale ?? 1f);
        _stacks = stacks;
        CombatTargets.AimMask(this, _side);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        float step = _speed * (float)delta;
        Position += new Vector2(_direction * step, 0f);
        _left -= step;
        if (_left <= 0f) QueueFree();
        else QueueRedraw();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!CombatTargets.TryGetHostile(body, _side, out ICombatTarget target) || !_hit.Add(body.GetInstanceId())) return;
        HitInfo hit = _owner != null && IsInstanceValid(_owner)
            ? new HitInfo(_damage, new Vector2(_direction * Units.Px(4f), -Units.Px(3f))).FromRanged(_owner, "s1.wrath_fire")
            : new HitInfo(_damage).FromRanged("asura_wrath", "s1.wrath_fire");
        target.TakeHit(hit, GlobalPosition);
        if (target is CharacterController fighter) fighter.AddFireStack(_stacks);
    }

    /// <summary>NOTE(art): placeholder — vermilion and gold flame on the nagtang's black.</summary>
    public override void _Draw()
    {
        float w = Units.Px(WidthM), h = Units.Px(HeightM);
        DrawColoredPolygon(new[] { new Vector2(-w * 0.5f, 0f), new Vector2(w * 0.5f, 0f), new Vector2(w * 0.15f * _direction, -h) }, new Color(0.86f, 0.22f, 0.10f, 0.9f));
        DrawColoredPolygon(new[] { new Vector2(-w * 0.25f, 0f), new Vector2(w * 0.25f, 0f), new Vector2(w * 0.05f * _direction, -h * 0.6f) }, new Color(1f, 0.78f, 0.25f, 0.95f));
    }
}

using System.Collections.Generic;
using Godot;

namespace Fableland.Bosses;

/// <summary>
/// One of Doubt's Chain Hooks (Docs/Mara.gdd §5.4): it flies in from the side at the height it
/// was aimed and strikes the first fighter of the other side it passes, once. Two come, one
/// from each wall; only one is real — the one his third eye looks at. The false one fades
/// <see cref="Mara.FalseFade"/> before it would reach where it was aimed, and hits nobody.
/// Tested by position each tick, like a thrown blade. NOTE(art): placeholder drawing.
/// </summary>
public partial class ChainHook : Node2D
{
    private Mara _owner;
    private float _direction = 1f, _speed, _left, _damage, _arriveIn;
    private bool _real = true, _faded;
    private readonly HashSet<ulong> _hit = new();

    public bool Real => _real;
    public bool Faded => _faded;
    public int Hits => _hit.Count;

    /// <summary>Call right after AddChild. <paramref name="aimDistancePx"/>: how far away the
    /// point it was aimed at lies — the false hook fades just before it gets there.</summary>
    public void Init(Mara owner, float direction, float speedPx, float reachPx, float aimDistancePx, float damage, bool real)
    {
        _owner = owner;
        _direction = direction >= 0f ? 1f : -1f;
        _speed = speedPx;
        _left = reachPx;
        _arriveIn = speedPx > 0f ? aimDistancePx / speedPx : 0f;
        _damage = damage;
        _real = real;
        ZIndex = 5;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float step = _speed * dt;
        Position += new Vector2(_direction * step, 0f);
        _left -= step;
        _arriveIn -= dt;
        QueueRedraw();
        if (!_real)
        {
            if (_arriveIn <= Mara.FalseFade) _faded = true;
            if (_faded || _left <= 0f) QueueFree();
            return;
        }
        if (_owner != null && IsInstanceValid(_owner))
            foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), _owner.Side))
            {
                if (target is not Node2D node || _hit.Contains(target.TargetId)) continue;
                Vector2 to = node.GlobalPosition - GlobalPosition;
                if (Mathf.Abs(to.X) > Units.Px(0.6f) + target.HitRadius || Mathf.Abs(to.Y) > Units.Px(1.1f)) continue;
                _hit.Add(target.TargetId);
                var shove = new Vector2(_direction * Units.Px(6f), -Units.Px(2f));
                float dealt = target.TakeHit(new HitInfo(_damage * _owner.DamageDealtScale, shove).FromRanged(_owner, "realm.chain_hook"), GlobalPosition);
                if (dealt > 0f) _owner.ReportDamageDealt(dealt);
            }
        if (_left <= 0f) QueueFree();
    }

    public override void _Draw()
    {
        if (_faded) return;
        var iron = new Color(0.62f, 0.64f, 0.70f);
        DrawLine(Vector2.Zero, new Vector2(-_direction * Units.Px(4f), 0f), new Color(0.45f, 0.45f, 0.5f), 2f);
        DrawArc(new Vector2(_direction * 4f, 0f), 7f, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 8, iron, 3f);
        DrawLine(new Vector2(_direction * 4f, 7f), new Vector2(_direction * 10f, 2f), iron, 3f);
    }
}

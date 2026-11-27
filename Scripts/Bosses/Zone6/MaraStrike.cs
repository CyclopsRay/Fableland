using System.Collections.Generic;
using Godot;

namespace Fableland.Bosses;

/// <summary>What kind of field strike a <see cref="MaraStrike"/> is.</summary>
public enum MaraStrikeKind
{
    /// <summary>Hand of the Wheel (轮手): the colossus slams a stretch of floor. Its shadow is the tell.</summary>
    Hand,
    /// <summary>Heavy Halo (Iron Throne): a gold ring drops where you stood.</summary>
    Halo,
    /// <summary>Wilting Garland (Iron Throne): petals fall in a lane and burn where they land.</summary>
    Garland,
}

/// <summary>
/// A strike that falls on a stretch of floor after a tell (Docs/Mara.gdd §5.1, §6): the
/// colossus's hand, the throne's halo, the wilting garland. It is placed where it lands — x the
/// target's, y the surface under it — shows its tell for a while, then hits every fighter of
/// the other side in its column once (the garland burns instead, tick by tick). Under Doubt one
/// of a pair is false: it fades <see cref="Mara.FalseFade"/> before the strike and hits nobody.
/// No physics shape: the column is tested by position at the moment it lands, so what the tell
/// shows is exactly what is hit. NOTE(art): placeholder drawing.
/// </summary>
public partial class MaraStrike : Node2D
{
    private Mara _owner;
    private MaraStrikeKind _kind;
    private float _halfWidth, _height, _tell, _age, _damage, _knockUp, _burn, _tickLeft;
    private bool _real = true, _struck, _faded;
    private string _action = "";
    private readonly HashSet<ulong> _hit = new();

    public MaraStrikeKind Kind => _kind;
    public bool Real => _real;
    public bool Struck => _struck;
    public int Hits => _hit.Count;
    public float HalfWidth => _halfWidth;
    public float TellSeconds => _tell;
    public float Age => _age;

    /// <summary>Call right after AddChild, at the point on the surface where it lands.</summary>
    public void Init(Mara owner, MaraStrikeKind kind, float widthPx, float heightPx, float tell, float damage,
        float knockUpPx, bool real, string action, float burnSeconds = 0f)
    {
        _owner = owner;
        _kind = kind;
        _halfWidth = widthPx * 0.5f;
        _height = heightPx;
        _tell = tell;
        _damage = damage;
        _knockUp = knockUpPx;
        _real = real;
        _action = action ?? "";
        _burn = burnSeconds;
        ZIndex = 4;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        QueueRedraw();
        if (!_real)
        {
            if (!_faded && _age >= _tell - Mara.FalseFade) _faded = true;
            if (_age >= _tell) QueueFree();
            return;
        }
        if (!_struck && _age >= _tell)
        {
            _struck = true;
            if (_kind == MaraStrikeKind.Garland) _tickLeft = 0f;
            else
            {
                HitColumn(_damage, new Vector2(0f, -_knockUp), once: true);
                if (_owner != null && IsInstanceValid(_owner))
                    CueBus.EmitAt(_owner.FeedbackId, _action, GlobalPosition, this, CombatSide.Foe,
                        size: _halfWidth * 2f, magnitude: _damage, ownerClass: _owner.FeedbackClass);
            }
        }
        if (!_struck) return;
        if (_kind == MaraStrikeKind.Garland)
        {
            // The petals burn where they lie: a hazard tick, i-frames or not, every quarter second.
            _tickLeft -= dt;
            if (_tickLeft <= 0f)
            {
                _tickLeft += 0.25f;
                BurnLane();
            }
            if (_age >= _tell + _burn) QueueFree();
        }
        else if (_age >= _tell + 0.35f) QueueFree();
    }

    private bool InColumn(Node2D body, float radius, float feetBelow)
    {
        Vector2 to = body.GlobalPosition - GlobalPosition;
        return Mathf.Abs(to.X) <= _halfWidth + radius && to.Y <= feetBelow && to.Y >= -_height;
    }

    private void HitColumn(float damage, Vector2 knock, bool once)
    {
        if (_owner == null || !IsInstanceValid(_owner)) return;
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), _owner.Side))
        {
            if (target is not Node2D node || (once && _hit.Contains(target.TargetId))) continue;
            // A body's origin is a metre above its feet: standing on the surface it is at −32 px.
            if (!InColumn(node, target.HitRadius, Units.Px(0.5f))) continue;
            _hit.Add(target.TargetId);
            float dealt = target.TakeHit(new HitInfo(damage * _owner.DamageDealtScale, knock).FromRanged(_owner, _action), GlobalPosition);
            if (dealt > 0f) _owner.ReportDamageDealt(dealt);
        }
    }

    private void BurnLane()
    {
        if (_owner == null || !IsInstanceValid(_owner)) return;
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), _owner.Side))
        {
            if (target is not CharacterController body || body.IsDefeated) continue;
            Vector2 feet = body.Feet - GlobalPosition;
            if (Mathf.Abs(feet.X) > _halfWidth || Mathf.Abs(feet.Y) > Units.Px(0.6f)) continue;
            _hit.Add(target.TargetId);
            body.ApplyHazard(_damage * _owner.DamageDealtScale, Vector2.Zero);
        }
    }

    public override void _Draw()
    {
        float t = _tell > 0f ? Mathf.Clamp(_age / _tell, 0f, 1f) : 1f;
        float alpha = _faded ? 0f : 1f;
        if (alpha <= 0f) return;
        var gold = new Color(0.96f, 0.78f, 0.28f, alpha);
        switch (_kind)
        {
            case MaraStrikeKind.Hand:
            {
                // The shadow of a claw darkens on the floor; it falls in the last tenth of a second.
                var shadow = new Color(0.02f, 0.02f, 0.05f, (0.2f + 0.55f * t) * alpha);
                DrawRect(new Rect2(-_halfWidth, -Units.Px(0.3f), _halfWidth * 2f, Units.Px(0.35f)), shadow);
                if (!_struck)
                {
                    float y = Mathf.Lerp(-_height, -Units.Px(0.4f), t * t);
                    var claw = new Color(0.10f, 0.14f, 0.32f, 0.75f * alpha);
                    DrawRect(new Rect2(-_halfWidth * 0.9f, y - Units.Px(1.2f), _halfWidth * 1.8f, Units.Px(1.2f)), claw);
                    for (int k = -2; k <= 2; k++)
                        DrawLine(new Vector2(k * _halfWidth * 0.4f, y), new Vector2(k * _halfWidth * 0.4f, y + Units.Px(0.6f)), new Color(0.93f, 0.9f, 0.8f, alpha), 3f);
                }
                else DrawRect(new Rect2(-_halfWidth, -_height, _halfWidth * 2f, _height), new Color(0.10f, 0.14f, 0.32f, 0.35f));
                break;
            }
            case MaraStrikeKind.Halo:
            {
                DrawColoredPolygon(Oval(_halfWidth, Units.Px(0.25f)), new Color(0f, 0f, 0f, (0.2f + 0.4f * t) * alpha));
                float y = _struck ? 0f : Mathf.Lerp(-Units.Px(6f), 0f, t * t);
                DrawArc(new Vector2(0f, y - Units.Px(0.3f)), _halfWidth * 0.8f, 0f, Mathf.Tau, 24, gold, 4f);
                break;
            }
            case MaraStrikeKind.Garland:
            {
                if (!_struck)
                    for (int k = 0; k < 7; k++)
                    {
                        float x = -_halfWidth + (k + 0.5f) * _halfWidth * 2f / 7f;
                        float y = Mathf.Lerp(-Units.Px(7f), 0f, Mathf.Clamp(t * 1.2f - (k % 3) * 0.1f, 0f, 1f));
                        DrawCircle(new Vector2(x, y), 4f, new Color(0.95f, 0.85f, 0.5f, alpha));
                    }
                else
                    DrawRect(new Rect2(-_halfWidth, -Units.Px(0.25f), _halfWidth * 2f, Units.Px(0.25f)), new Color(0.9f, 0.35f, 0.1f, 0.65f));
                break;
            }
        }
    }

    private static Vector2[] Oval(float rx, float ry)
    {
        var points = new Vector2[16];
        for (int i = 0; i < points.Length; i++)
        {
            float a = i * Mathf.Tau / points.Length;
            points[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return points;
    }
}

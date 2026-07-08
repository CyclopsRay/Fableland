using Godot;
using System.Collections.Generic;

/// <summary>
/// Base for a stationary, always-on environmental hazard box: while a player or
/// foe body overlaps it, <see cref="ApplyTick"/> fires immediately on contact and
/// then every <see cref="TickInterval"/> seconds for as long as they stay inside.
/// The collision shape is built from <see cref="BoxSize"/> at ready time — the
/// same field the debug telegraph draws from — so the two can never drift apart.
/// </summary>
public abstract partial class Hazard : Area2D
{
    [Export] public Vector2 BoxSize = new Vector2(64f, 64f);
    [Export] public float TickInterval = 0.25f;

    private readonly Dictionary<Node2D, float> _timers = new();

    public override void _Ready()
    {
        SetCollisionLayerValue(Units.LayerHazard, true);
        SetCollisionMaskValue(Units.LayerPlayer, true);
        SetCollisionMaskValue(Units.LayerFoes, true);

        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = BoxSize } });

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
        QueueRedraw();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is not CharacterController && body is not BaseFoe) return;
        _timers[body] = 0f;   // tick immediately on first contact
    }

    private void OnBodyExited(Node2D body) => _timers.Remove(body);

    public override void _PhysicsProcess(double delta)
    {
        if (_timers.Count == 0) return;
        float dt = (float)delta;

        var bodies = new List<Node2D>(_timers.Keys);
        foreach (Node2D body in bodies)
        {
            if (!IsInstanceValid(body)) { _timers.Remove(body); continue; }
            float t = _timers[body] - dt;
            if (t <= 0f)
            {
                t += TickInterval;
                ApplyTick(body);
            }
            _timers[body] = t;
        }
    }

    /// <summary>Apply this hazard's effect to a body that's due for a tick.</summary>
    protected abstract void ApplyTick(Node2D body);

    /// <summary>Route an effect to whichever target type it is. Hazard ticks bypass
    /// combat i-frames (see CharacterController.ApplyHazard) since they reapply
    /// every <see cref="TickInterval"/>, faster than the post-hit invuln window.</summary>
    protected static void Deliver(Node2D body, float damage, Vector2 knockback,
                                  float fireStack = 0f, float frozenStack = 0f)
    {
        if (body is CharacterController c)
        {
            if (damage > 0f || knockback != Vector2.Zero) c.ApplyHazard(damage, knockback);
            if (fireStack > 0f) c.AddFireStack(fireStack);
            if (frozenStack > 0f) c.AddFrozenStack(frozenStack);
        }
        else if (body is BaseFoe e)
        {
            if (damage > 0f || knockback != Vector2.Zero) e.ApplyHazard(damage, knockback);
            if (fireStack > 0f) e.AddFireStack(fireStack);
            if (frozenStack > 0f) e.AddFrozenStack(frozenStack);
        }
    }

    public override void _Draw()
    {
        var rect = new Rect2(-BoxSize / 2f, BoxSize);
        DrawRect(rect, TintFill);
        DrawRect(rect, TintEdge, false, 3f);
    }

    protected abstract Color TintFill { get; }
    protected abstract Color TintEdge { get; }
}

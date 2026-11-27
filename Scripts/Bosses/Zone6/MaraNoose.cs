using System;
using Godot;

namespace Fableland.Bosses;

/// <summary>
/// The Noose of Craving (爱索, Docs/Mara.gdd §6): a loop of gold-and-red cord thrown low along
/// the floor. It catches the first fighter of the other side whose body reaches below
/// <see cref="TopM"/> — so a jump clears it — and is done. Māra's own noose hands the catch
/// back to him (the yank and the bite); a mirror image's bites at once, weaker, with no yank.
/// </summary>
public partial class MaraNoose : Area2D
{
    /// <summary>The loop's span above the floor: 0.1 m to <see cref="TopM"/>.</summary>
    public const float TopM = 0.8f;
    public const float WidthM = 1.2f;

    private CharacterController _owner;
    private CombatSide _side = CombatSide.Foe;
    private float _direction = 1f, _speed, _left;
    private Action<ICombatTarget> _caught;
    private bool _done;

    public bool Caught { get; private set; }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionLayerValue(Units.LayerHazard, true);
        float h = Units.Px(TopM - 0.1f);
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(Units.Px(WidthM), h) },
            Position = new Vector2(0f, -Units.Px(0.1f) - h * 0.5f),
        });
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Call right after AddChild, with the noose's foot on the floor line.</summary>
    public void Init(CharacterController owner, float direction, float speedPx, float reachPx, Action<ICombatTarget> caught)
    {
        _owner = owner;
        _side = owner?.Side ?? CombatSide.Foe;
        _direction = direction >= 0f ? 1f : -1f;
        _speed = speedPx;
        _left = reachPx;
        _caught = caught;
        CombatTargets.AimMask(this, _side);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        float step = _speed * (float)delta;
        Position += new Vector2(_direction * step, 0f);
        _left -= step;
        if (_left <= 0f) Finish();
        else QueueRedraw();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (_done || !CombatTargets.TryGetHostile(body, _side, out ICombatTarget target)) return;
        Caught = true;
        _caught?.Invoke(target);
        Finish();
    }

    private void Finish()
    {
        if (_done) return;
        _done = true;
        // Freed after this physics step: it may be inside a body_entered callback.
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        QueueFree();
    }

    /// <summary>NOTE(art): placeholder — a gold loop trailing a red cord back toward its thrower.</summary>
    public override void _Draw()
    {
        float r = Units.Px(0.35f);
        Vector2 loop = new(0f, -Units.Px(0.45f));
        DrawArc(loop, r, 0f, Mathf.Tau, 16, new Color(0.96f, 0.78f, 0.28f), 3f);
        DrawLine(loop + new Vector2(-_direction * r, 0f), loop + new Vector2(-_direction * Units.Px(2.5f), Units.Px(0.15f)), new Color(0.8f, 0.16f, 0.1f), 2f);
    }
}

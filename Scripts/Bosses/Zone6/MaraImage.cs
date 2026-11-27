using Godot;

namespace Fableland.Bosses;

/// <summary>
/// One of Delusion's mirror images (Docs/Mara.gdd §5.5): a figure the same as his but for a
/// cold crown and no shadow on the fire. It stands beside him and copies his attacks a beat
/// late and weaker (<see cref="Mara"/> drives it). The player's attacks find it like any foe —
/// that is the delusion — but it takes no damage: one hit pops it, and it reforms after
/// <see cref="Mara.ImageReform"/> seconds. On the Foes layer so a projectile meets it too.
/// </summary>
public partial class MaraImage : StaticBody2D, ICombatTarget
{
    private Mara _owner;
    private MaraFigure _figure;
    private Sprite2D _frame;
    private float _reformIn;

    /// <summary>Where it stands, from his origin.</summary>
    public Vector2 Offset { get; set; }
    /// <summary>Which way it faces: its copied blows go that way.</summary>
    public float Facing { get; set; } = 1f;
    public bool Popped => _reformIn > 0f;
    public int Pops { get; private set; }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        SetCollisionLayerValue(Units.LayerFoes, true);
        AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(64f, 120f) },
            Position = new Vector2(0f, -28f),
        });
        // The figure faces by its parent sprite's FlipH, as his does.
        _frame = new Sprite2D { Name = "Sprite2D" };
        AddChild(_frame);
        _figure = new MaraFigure { Name = "Figure", Image = true, UseParentMaterial = true };
        _frame.AddChild(_figure);
        AddToGroup(CombatTargets.EnemyGroup);
    }

    public void Init(Mara owner, Vector2 offset)
    {
        _owner = owner;
        Offset = offset;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_reformIn > 0f)
        {
            _reformIn -= (float)delta;
            if (_reformIn <= 0f) Reform();
        }
        if (_frame != null) _frame.FlipH = Facing < 0f;
    }

    private void Pop()
    {
        if (Popped) return;
        Pops++;
        _reformIn = Mara.ImageReform;
        Visible = false;
        RemoveFromGroup(CombatTargets.EnemyGroup);
        // Possibly inside a projectile's body_entered: change collision after this step.
        SetDeferred(CollisionObject2D.PropertyName.CollisionLayer, 0u);
        if (_owner != null && IsInstanceValid(_owner))
            CueBus.EmitAt(_owner.FeedbackId, "image.pop", GlobalPosition, this, CombatSide.Foe, ownerClass: _owner.FeedbackClass);
    }

    private void Reform()
    {
        _reformIn = 0f;
        Visible = true;
        AddToGroup(CombatTargets.EnemyGroup);
        SetDeferred(CollisionObject2D.PropertyName.CollisionLayer, 1u << (Units.LayerFoes - 1));
    }

    // ── ICombatTarget: it looks like him to every attack, and takes nothing ──────────
    public CombatSide Side => CombatSide.Foe;
    public float HitRadius => Units.Px(1.4f);
    public float MaxHP => _owner != null && IsInstanceValid(_owner) ? _owner.MaxHP : 1f;
    public float CurrentHp => _owner != null && IsInstanceValid(_owner) ? _owner.CurrentHP : 1f;
    public bool Invincible => Popped;
    public ulong TargetId => GetInstanceId();

    public float TakeHit(HitInfo hit, Vector2 sourcePos)
    {
        Pop();
        return 0f;
    }

    public void SetBurning(float duration) { }
    public void ApplyTrapped(float duration) { }
    public void AddImpulse(Vector2 impulse) { }
    public void Execute() => Pop();
    public void SetContinuousExternalVelocity(string source, Vector2 velocity) { }
    public void ClearContinuousExternalVelocity(string source) { }
    // An image does not walk, jump, cast or look: crowd control finds nothing to hold.
    public void SetMoveSpeedScale(string source, float scale) { }
    public void SetJumpHeightScale(string source, float scale) { }
    public void SetSilenced(string source, bool on) { }
    public void SetSightLimit(string source, float metres) { }
    public void ClearSightLimit(string source) { }
}

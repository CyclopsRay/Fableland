using Godot;
using System.Collections.Generic;
using Fableland.Data;

/// <summary>
/// Pooled deterministic bubble. Its motion is a function of launch data plus local
/// simulation age, so Shift can rewind it without storing a snapshot every tick.
/// </summary>
public partial class PixolotlBubble : Area2D
{
    private Pixolotl _owner;
    private Vector2 _origin;
    private Vector2 _launchVelocity;
    private float _age;
    private float _damageMultiplier = 1f;
    private bool _active;
    private readonly HashSet<ulong> _hitTargets = new();

    public bool Active => _active;
    public Pixolotl TemporalOwner => _owner;
    public float LocalAge => _age;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        Deactivate();
    }

    public void Activate(Pixolotl owner, Vector2 origin, Vector2 launchVelocity, float damageMultiplier)
    {
        _owner = owner;
        _origin = origin;
        _launchVelocity = launchVelocity;
        _damageMultiplier = damageMultiplier;
        _age = 0f;
        _hitTargets.Clear();
        _active = true;
        Monitoring = true;
        Monitorable = true;
        Visible = true;
        ProcessMode = ProcessModeEnum.Inherit;
        GlobalPosition = _origin;
        QueueRedraw();
    }

    public void BindOwner(Pixolotl owner) => _owner = owner;

    public void Rewind(float localSeconds)
    {
        if (!_active || localSeconds <= 0f) return;
        _age = Mathf.Max(0f, _age - localSeconds);
        ApplyAge();
    }

    public void Deactivate()
    {
        _active = false;
        Monitoring = false;
        Monitorable = false;
        Visible = false;
        ProcessMode = ProcessModeEnum.Disabled;
        _owner = null;
        _hitTargets.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active) return;
        float localDelta = _owner != null && IsInstanceValid(_owner)
            ? _owner.GetActDelta((float)delta)
            : (float)delta;
        _age += localDelta;
        if (_age >= CharacterTable.Pixolotl.BubbleLifetime)
        {
            Deactivate();
            return;
        }
        ApplyAge();
    }

    private void ApplyAge()
    {
        float launch = CharacterTable.Pixolotl.BubbleDirectionalSeconds;
        float brake = CharacterTable.Pixolotl.BubbleBrakeSeconds;
        Vector2 position;

        if (_age <= launch)
        {
            position = _origin + _launchVelocity * _age;
        }
        else
        {
            Vector2 launchEnd = _origin + _launchVelocity * launch;
            float brakeAge = Mathf.Min(_age - launch, brake);
            float xDistance = _launchVelocity.X * (brakeAge - brakeAge * brakeAge / (2f * brake));
            float riseVelocity = -Units.Px(CharacterTable.Pixolotl.BubbleRiseSpeedMps);
            float yDistance = ((_launchVelocity.Y + riseVelocity) * 0.5f) * brakeAge;
            position = launchEnd + new Vector2(xDistance, yDistance);
            if (_age > launch + brake)
                position += Vector2.Up * Units.Px(CharacterTable.Pixolotl.BubbleRiseSpeedMps) * (_age - launch - brake);
        }

        GlobalPosition = position;
        QueueRedraw();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (!_active) return;
        if (body is BaseFoe foe)
        {
            ulong id = foe.GetInstanceId();
            if (!_hitTargets.Add(id)) return;
            float dealt = foe.TakeHit(new HitInfo(CharacterTable.Pixolotl.BubbleDamage * _damageMultiplier,
                Vector2.Zero, 0f), GlobalPosition);
            _owner?.ReportDamageDealt(dealt);
            return; // enemy contact never despawns the bubble
        }

        // SoftVolumes are Area2D and do not emit BodyEntered. Any other body on the
        // projectile mask is Ground or Platform and resolves the bubble immediately.
        Deactivate();
    }

    public override void _Draw()
    {
        if (!_active) return;
        // Legacy bubble art is not present in this checkout. Keep the fallback's rendered
        // footprint honest: the GDD specifies a 2 m diameter, matching the scene collider.
        float radius = Units.Px(1f);
        DrawCircle(Vector2.Zero, radius, new Color(0.26f, 0.78f, 1f, 0.34f));
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 32, new Color(0.92f, 0.98f, 1f, 0.9f), 2f);
    }
}

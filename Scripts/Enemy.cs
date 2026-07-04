using Godot;

/// <summary>
/// Prototype crab foe — a simplified port of BaseFoe/CrabFoe. Patrols, chases the
/// player when close, deals contact damage, can be set on fire (burn DoT), and
/// reacts to hits with a knockback bounce + blink + floating damage numbers.
/// </summary>
public partial class Enemy : CharacterBody2D
{
    [Export] public float MaxHP = 60f;
    [Export] public float MoveSpeed = 90f;
    [Export] public float Gravity = Units.Gravity;
    [Export] public float AggroRange = 320f;
    [Export] public float ContactRange = 44f;
    [Export] public float ContactDamage = 18f;
    [Export] public float ContactCooldown = 0.9f;
    [Export] public float BurnDamagePerSecond = 14f;
    [Export] public float KnockbackTime = 0.18f;   // window where patrol yields to knockback
    [Export] public float HitRadius = 26f;         // bounding circle for melee/AoE overlap

    private Sprite2D _sprite;
    private float _hp;
    private float _dir = 1f;
    private float _contactTimer;
    private float _flashTimer;
    private float _blinkTimer;
    private float _knockbackTimer;
    private float _burnTimer;
    private float _burnAccum;      // batched so burn numbers don't spam
    private float _burnPopTimer;
    private SoftVolume _softVolume;

    public void EnterSoftVolume(SoftVolume v) => _softVolume = v;
    public void ExitSoftVolume(SoftVolume v) { if (_softVolume == v) _softVolume = null; }

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _hp = MaxHP;
        AddToGroup("enemy");
        _dir = GD.Randf() < 0.5f ? -1f : 1f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_contactTimer > 0f) _contactTimer -= dt;
        if (_knockbackTimer > 0f) _knockbackTimer -= dt;

        if (_burnTimer > 0f)
        {
            _burnTimer -= dt;
            float d = BurnDamagePerSecond * dt;
            _hp -= d;
            _burnAccum += d;
            _burnPopTimer -= dt;
            if (_burnPopTimer <= 0f && _burnAccum >= 1f)
            {
                PopNumber(_burnAccum);
                _burnAccum = 0f;
                _burnPopTimer = 0.5f;
            }
            if (_hp <= 0f) { QueueFree(); return; }
        }

        if (_blinkTimer > 0f)
        {
            _blinkTimer -= dt;
            _sprite.Visible = Mathf.PosMod(_blinkTimer, 0.08f) < 0.04f;
            if (_blinkTimer <= 0f) _sprite.Visible = true;
        }
        if (_flashTimer > 0f)
        {
            _flashTimer -= dt;
            if (_flashTimer <= 0f)
                _sprite.Modulate = _burnTimer > 0f ? new Color(1f, 0.6f, 0.35f) : Colors.White;
        }

        // Aggro + contact damage (runs unless being knocked back).
        if (_knockbackTimer <= 0f)
        {
            CharacterController player = GetTree().GetFirstNodeInGroup("player") as CharacterController;
            if (player != null)
            {
                Vector2 to = player.GlobalPosition - GlobalPosition;
                if (Mathf.Abs(to.X) < AggroRange) _dir = Mathf.Sign(to.X);

                if (to.Length() < ContactRange && _contactTimer <= 0f)
                {
                    _contactTimer = ContactCooldown;
                    Vector2 knock = new Vector2(Mathf.Sign(to.X == 0f ? 1f : to.X), -0.4f).Normalized() * 300f;
                    player.TakeDamage(ContactDamage, knock);
                }
            }
        }

        // Inside a go-inside volume: same stagnation + gravity drift as the player.
        if (_softVolume != null)
        {
            float h = _knockbackTimer > 0f ? 0f : _dir;
            Velocity = _softVolume.ComputeVelocity(MoveSpeed, h, 0f);
            MoveAndSlide();
            _sprite.FlipH = _dir < 0f;
            return;
        }

        Vector2 v = Velocity;
        if (!IsOnFloor()) v.Y += Gravity * dt;

        if (_knockbackTimer > 0f)
        {
            // Let the knockback carry, bleeding off horizontally — a visible bounce.
            v.X = Mathf.MoveToward(v.X, 0f, 900f * dt);
        }
        else
        {
            if (IsOnWall()) _dir = -_dir;
            v.X = _dir * MoveSpeed;
            _sprite.FlipH = _dir < 0f;
        }

        Velocity = v;
        MoveAndSlide();
    }

    public void TakeDamage(float amount, Vector2 knockback)
    {
        _hp -= amount;
        Velocity = knockback;
        _knockbackTimer = KnockbackTime;
        _sprite.Modulate = new Color(1f, 0.6f, 0.6f);
        _flashTimer = 0.12f;
        _blinkTimer = 0.24f;
        PopNumber(amount);
        if (_hp <= 0f) QueueFree();
    }

    public void SetBurning(float duration)
    {
        _burnTimer = Mathf.Max(_burnTimer, duration);
        if (_flashTimer <= 0f) _sprite.Modulate = new Color(1f, 0.6f, 0.35f);
    }

    private void PopNumber(float amount) =>
        DamageNumberManager.Instance?.Pop(GlobalPosition + new Vector2(0f, -28f), amount, false);
}

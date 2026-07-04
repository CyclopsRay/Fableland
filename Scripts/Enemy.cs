using Godot;

/// <summary>
/// Prototype crab foe — a simplified port of BaseFoe/CrabFoe. Patrols the ground,
/// chases the player when close, deals contact damage, and can be set on fire
/// (burn DoT) by Pomegraknight's combo/seeds.
/// </summary>
public partial class Enemy : CharacterBody2D
{
    [Export] public float MaxHP = 60f;
    [Export] public float MoveSpeed = 90f;
    [Export] public float Gravity = 1200f;
    [Export] public float AggroRange = 320f;
    [Export] public float ContactRange = 44f;
    [Export] public float ContactDamage = 18f;
    [Export] public float ContactCooldown = 0.9f;
    [Export] public float BurnDamagePerSecond = 14f;

    private Sprite2D _sprite;
    private float _hp;
    private float _dir = 1f;
    private float _contactTimer;
    private float _flashTimer;
    private float _burnTimer;

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

        // Burn damage-over-time.
        if (_burnTimer > 0f)
        {
            _burnTimer -= dt;
            _hp -= BurnDamagePerSecond * dt;
            if (_hp <= 0f) { QueueFree(); return; }
            if (_burnTimer <= 0f && _flashTimer <= 0f) _sprite.Modulate = Colors.White;
        }

        if (_flashTimer > 0f)
        {
            _flashTimer -= dt;
            if (_flashTimer <= 0f) _sprite.Modulate = _burnTimer > 0f ? new Color(1f, 0.6f, 0.35f) : Colors.White;
        }

        Vector2 v = Velocity;
        if (!IsOnFloor()) v.Y += Gravity * dt;

        CharacterController player = GetTree().GetFirstNodeInGroup("player") as CharacterController;
        if (player != null)
        {
            Vector2 to = player.GlobalPosition - GlobalPosition;
            if (Mathf.Abs(to.X) < AggroRange)
                _dir = Mathf.Sign(to.X);

            if (to.Length() < ContactRange && _contactTimer <= 0f)
            {
                _contactTimer = ContactCooldown;
                Vector2 knock = new Vector2(Mathf.Sign(to.X == 0f ? 1f : to.X), -0.4f).Normalized() * 300f;
                player.TakeDamage(ContactDamage, knock);
            }
        }

        if (IsOnWall()) _dir = -_dir;

        v.X = _dir * MoveSpeed;
        Velocity = v;
        MoveAndSlide();
        _sprite.FlipH = _dir < 0f;
    }

    public void TakeDamage(float amount, Vector2 knockback)
    {
        _hp -= amount;
        Velocity = knockback;
        _sprite.Modulate = new Color(1f, 0.6f, 0.6f);
        _flashTimer = 0.12f;
        if (_hp <= 0f) QueueFree();
    }

    public void SetBurning(float duration)
    {
        _burnTimer = Mathf.Max(_burnTimer, duration);
        if (_flashTimer <= 0f) _sprite.Modulate = new Color(1f, 0.6f, 0.35f);
    }
}

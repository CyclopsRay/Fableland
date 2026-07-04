using Godot;

/// <summary>
/// Prototype crab foe — a simplified port of BaseFoe/CrabFoe. Patrols, chases the
/// player, deals contact damage, can burn, and uses the same intent + external
/// velocity model as the player so knockback is a real, decaying shove and a
/// gain-no window freezes its AI + animation.
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
    [Export] public float ContactKnockback = 260f;   // delta-v it puts on the player
    [Export] public float ContactStun = 0.25f;       // gain-no it inflicts (not the 0.005·dmg default)
    [Export] public float BurnDamagePerSecond = 14f;
    [Export] public float HitRadius = 26f;           // bounding circle for melee/AoE overlap
    [Export] public float ExternalDamping = 1200f;   // px/s² decay of knockback

    private SoftVolume _softVolume;
    public void EnterSoftVolume(SoftVolume v) => _softVolume = v;
    public void ExitSoftVolume(SoftVolume v) { if (_softVolume == v) _softVolume = null; }

    private Sprite2D _sprite;
    private float _hp;
    private float _dir = 1f;
    private float _contactTimer;
    private float _flashTimer;
    private float _blinkTimer;
    private float _stunTimer;
    private float _burnTimer;
    private float _burnAccum;      // batched so burn numbers don't spam
    private float _burnPopTimer;
    private Vector2 _intentVel;
    private Vector2 _externalVel;

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
        if (_stunTimer > 0f) _stunTimer -= dt;

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

        bool frozen = _stunTimer > 0f;   // gain-no: no AI, animation frozen

        // Aggro + contact damage (only when acting).
        if (!frozen)
        {
            CharacterController player = GetTree().GetFirstNodeInGroup("player") as CharacterController;
            if (player != null)
            {
                Vector2 to = player.GlobalPosition - GlobalPosition;
                if (Mathf.Abs(to.X) < AggroRange) _dir = Mathf.Sign(to.X == 0f ? _dir : to.X);

                if (to.Length() < ContactRange && _contactTimer <= 0f)
                {
                    _contactTimer = ContactCooldown;
                    Vector2 dir = new Vector2(Mathf.Sign(to.X == 0f ? 1f : to.X), -0.4f).Normalized();
                    player.TakeHit(new HitInfo(ContactDamage, dir * ContactKnockback, ContactStun));
                }
            }
        }

        // External velocity decays; a soft volume makes it viscous (muffled knockback).
        float damp = ExternalDamping * (_softVolume != null ? _softVolume.ExternalDampingMult : 1f);
        _externalVel = _externalVel.MoveToward(Vector2.Zero, damp * dt);

        if (_softVolume != null)
        {
            float h = frozen ? 0f : _dir;
            _intentVel = _softVolume.ComputeVelocity(MoveSpeed, h, 0f);
        }
        else
        {
            if (!IsOnFloor()) _intentVel.Y += Gravity * dt;
            else if (_intentVel.Y > 0f) _intentVel.Y = 0f;

            if (frozen) _intentVel.X = 0f;
            else
            {
                if (IsOnWall()) _dir = -_dir;
                _intentVel.X = _dir * MoveSpeed;
            }
        }

        Velocity = _intentVel + _externalVel;
        MoveAndSlide();

        if (IsOnFloor() && _externalVel.Y > 0f) _externalVel.Y = 0f;
        if (IsOnWall()) _externalVel.X = 0f;

        _sprite.FlipH = _dir < 0f;
    }

    public void TakeDamage(float amount, Vector2 knockback) => TakeHit(new HitInfo(amount, knockback));

    public void TakeHit(HitInfo hit)
    {
        _hp -= hit.Damage;
        AddImpulse(hit.Knockback);
        float stun = hit.ResolveStun();
        if (stun > 0f) _stunTimer = Mathf.Max(_stunTimer, stun);

        _sprite.Modulate = new Color(1f, 0.6f, 0.6f);
        _flashTimer = 0.12f;
        _blinkTimer = 0.24f;
        PopNumber(hit.Damage);
        if (_hp <= 0f) QueueFree();
    }

    public void AddImpulse(Vector2 impulse) => _externalVel += impulse;

    public void SetBurning(float duration)
    {
        _burnTimer = Mathf.Max(_burnTimer, duration);
        if (_flashTimer <= 0f) _sprite.Modulate = new Color(1f, 0.6f, 0.35f);
    }

    private void PopNumber(float amount) =>
        DamageNumberManager.Instance?.Pop(GlobalPosition + new Vector2(0f, -28f), amount, false);
}

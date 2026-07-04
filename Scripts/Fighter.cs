using Godot;
using System;

/// <summary>
/// Prototype player fighter — a slimmed-down port of the Unity CharacterController +
/// Pomegraknight. Captures the core feel: move, double-jump, a 3-hit melee combo,
/// the "Blush" self-buff, HP/lives, damage/knockback/i-frames, death and respawn.
/// Feature parity with the Unity original is intentionally NOT a goal here.
/// </summary>
public partial class Fighter : CharacterBody2D
{
    [ExportGroup("Stats")]
    [Export] public float MaxHP = 200f;
    [Export] public int MaxLives = 3;

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed = 320f;
    [Export] public float JumpVelocity = -560f;
    [Export] public float Gravity = 1200f;
    [Export] public int MaxJumps = 2;

    [ExportGroup("Melee Combo")]
    [Export] public float SlashRange = 130f;      // ~4m at 32px/m
    [Export] public float SlashHalfAngleDeg = 45f;
    [Export] public float SwingCooldown = 0.28f;  // between stages
    [Export] public float ComboWindow = 0.9f;     // combo resets if idle longer
    [Export] public float KnockbackSpeed = 320f;

    [ExportGroup("Blush (Skill 1)")]
    [Export] public float BlushDuration = 5f;
    [Export] public float BlushCooldown = 10f;
    [Export] public float BlushDamageMult = 1.5f;
    [Export] public float BlushSpeedMult = 1.2f;

    // Combo damage per stage (matches Pomegraknight 15/15/30).
    private static readonly float[] ComboDamage = { 15f, 15f, 30f };

    // C# events — GameManager subscribes to drive HUD and life flow.
    public event Action<float, float> HpChanged;   // (current, max)
    public event Action Died;

    public float CurrentHP { get; private set; }
    public int LivesRemaining { get; private set; }
    public bool ControlsLocked { get; set; }

    private Sprite2D _sprite;
    private Vector2 _spawnPoint;
    private float _facing = 1f;
    private int _jumpsRemaining;

    private int _comboStage;
    private float _comboTimer;      // time since last swing
    private float _swingCooldownTimer;

    private bool _blushActive;
    private float _blushTimer;
    private float _blushCooldownTimer;

    private float _invulnTimer;
    private bool _dead;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _spawnPoint = GlobalPosition;
        CurrentHP = MaxHP;
        LivesRemaining = MaxLives;
        AddToGroup("player");
        HpChanged?.Invoke(CurrentHP, MaxHP);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_dead) return;

        // Timers
        _comboTimer += dt;
        if (_comboTimer > ComboWindow) _comboStage = 0;
        if (_swingCooldownTimer > 0f) _swingCooldownTimer -= dt;
        if (_invulnTimer > 0f)
        {
            _invulnTimer -= dt;
            _sprite.Visible = Mathf.PosMod(_invulnTimer, 0.12f) < 0.06f;
            if (_invulnTimer <= 0f) _sprite.Visible = true;
        }

        if (_blushCooldownTimer > 0f) _blushCooldownTimer -= dt;
        if (_blushActive)
        {
            _blushTimer -= dt;
            if (_blushTimer <= 0f)
            {
                _blushActive = false;
                _sprite.Modulate = Colors.White;
            }
        }

        if (ControlsLocked) return;

        if (Input.IsActionJustPressed("attack") && _swingCooldownTimer <= 0f)
            DoSwing();
        if (Input.IsActionJustPressed("skill1"))
            TryBlush();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector2 v = Velocity;

        if (!IsOnFloor())
            v.Y += Gravity * dt;
        else if (v.Y >= 0f)
            _jumpsRemaining = MaxJumps;

        float inputDir = 0f;
        if (!ControlsLocked && !_dead)
        {
            if (Input.IsActionPressed("move_left")) inputDir -= 1f;
            if (Input.IsActionPressed("move_right")) inputDir += 1f;

            if (Input.IsActionJustPressed("jump") && _jumpsRemaining > 0)
            {
                v.Y = JumpVelocity;
                _jumpsRemaining--;
            }
        }

        float speed = MoveSpeed * (_blushActive ? BlushSpeedMult : 1f);
        v.X = inputDir * speed;
        Velocity = v;
        MoveAndSlide();

        if (inputDir > 0.01f) { _facing = 1f; _sprite.FlipH = false; }
        else if (inputDir < -0.01f) { _facing = -1f; _sprite.FlipH = true; }
    }

    private void DoSwing()
    {
        _swingCooldownTimer = SwingCooldown;
        _comboTimer = 0f;
        float dmg = ComboDamage[_comboStage] * (_blushActive ? BlushDamageMult : 1f);

        // Brief swing flash.
        if (!_blushActive) FlashColor(new Color(1f, 0.85f, 0.4f), SwingCooldown);

        // Cone hit test against all enemies.
        Vector2 origin = GlobalPosition;
        Vector2 aim = new Vector2(_facing, 0f);
        float cos = Mathf.Cos(Mathf.DegToRad(SlashHalfAngleDeg));
        foreach (Node n in GetTree().GetNodesInGroup("enemy"))
        {
            if (n is not Enemy e) continue;
            Vector2 to = e.GlobalPosition - origin;
            if (to.Length() > SlashRange) continue;
            if (to.Length() > 0.01f && to.Normalized().Dot(aim) < cos) continue;
            e.TakeDamage(dmg, new Vector2(_facing, -0.35f).Normalized() * KnockbackSpeed);
        }

        _comboStage = (_comboStage + 1) % ComboDamage.Length;
    }

    private void TryBlush()
    {
        if (_blushCooldownTimer > 0f) return;
        _blushActive = true;
        _blushTimer = BlushDuration;
        _blushCooldownTimer = BlushCooldown;
        _sprite.Modulate = new Color(1f, 0.55f, 0.35f); // ignited tint
    }

    public void TakeDamage(float amount, Vector2 knockback)
    {
        if (_dead || _invulnTimer > 0f) return;
        CurrentHP = Mathf.Max(0f, CurrentHP - amount);
        HpChanged?.Invoke(CurrentHP, MaxHP);
        Velocity = knockback;
        _invulnTimer = 0.8f;
        if (CurrentHP <= 0f) Die();
    }

    private void Die()
    {
        _dead = true;
        LivesRemaining = Mathf.Max(0, LivesRemaining - 1);
        _sprite.Modulate = new Color(0.4f, 0.4f, 0.4f);
        Velocity = Vector2.Zero;
        Died?.Invoke();
    }

    /// <summary>Called by GameManager to bring the fighter back after a death.</summary>
    public void Respawn()
    {
        _dead = false;
        CurrentHP = MaxHP;
        GlobalPosition = _spawnPoint;
        Velocity = Vector2.Zero;
        _invulnTimer = 1.2f;
        _sprite.Modulate = _blushActive ? new Color(1f, 0.55f, 0.35f) : Colors.White;
        _sprite.Visible = true;
        HpChanged?.Invoke(CurrentHP, MaxHP);
    }

    private async void FlashColor(Color c, float seconds)
    {
        if (_blushActive) return;
        _sprite.Modulate = c;
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        if (!_blushActive && !_dead) _sprite.Modulate = Colors.White;
    }
}

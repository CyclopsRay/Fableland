using Godot;
using System;

/// <summary>
/// Base class for playable characters — a Godot port of the Unity
/// CharacterController, trimmed to what the prototype needs and structured so
/// individual characters (Pomegraknight, and later Pixolotl/PumpKing/Cleopastar)
/// subclass it and override the ability hooks.
///
/// Provides: movement + double-jump, HP/lives, i-frames, knockback,
/// death/respawn, a generic Burning status (DoT for non-immune targets; a passive
/// trigger for those who are), a post-swing move-speed penalty, a melee cone
/// helper, and a debug-draw hook that subclasses use to visualize skill ranges.
///
/// ── ADDING A CHARACTER ────────────────────────────────────────────────
///   1. Subclass CharacterController.
///   2. Override InitCharacter() for stats / immunities.
///   3. Override HandleBA / HandleSkill1 / HandleSkill2 / HandleSkillUlt.
///   4. Override DrawDebug() to telegraph ranges.
///   5. (Later) override UpdateAnimator() once real animations exist.
/// ──────────────────────────────────────────────────────────────────────
/// </summary>
public partial class CharacterController : CharacterBody2D
{
    [ExportGroup("Stats")]
    [Export] public float MaxHP = 200f;
    [Export] public int MaxLives = 3;

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed = 320f;
    [Export] public float JumpVelocity = -560f;
    [Export] public float Gravity = 1200f;
    [Export] public int MaxJumps = 2;

    [ExportGroup("Debug")]
    [Export] public bool ShowDebugRanges = true;

    // Events — GameManager subscribes to drive HUD and the life flow.
    public event Action<float, float> HpChanged;   // (current, max)
    public event Action Died;

    public float CurrentHP { get; private set; }
    public int LivesRemaining { get; private set; }
    public bool ControlsLocked { get; set; }

    // Facing: +1 right, -1 left. Local-space, so debug draws and fire points read from it.
    protected float Facing = 1f;

    // Status shared across characters.
    protected bool IsBurning;
    protected bool BurnImmune;             // set in InitCharacter (Pomegraknight = true)
    protected float BurnDotPerSecond = 12f;
    protected float DamageTakenMult = 1f;  // e.g. Fire Tornado grants 0.6 while active

    protected Sprite2D Sprite;
    protected AnimationPlayer Anim;        // may be null until animations are authored
    protected Node2D FirePoint;            // optional projectile origin

    private float _burnTimer;
    private float _speedPenaltyTimer;
    private float _speedPenaltyMult = 1f;
    private float _invulnTimer;
    private int _jumpsRemaining;
    private bool _dead;
    private Vector2 _spawnPoint;
    private Color _baseTint = Colors.White;

    public override void _Ready()
    {
        Sprite = GetNode<Sprite2D>("Sprite2D");
        Anim = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");   // room for animation
        FirePoint = GetNodeOrNull<Node2D>("FirePoint");
        _spawnPoint = GlobalPosition;

        CurrentHP = MaxHP;
        LivesRemaining = MaxLives;
        AddToGroup("player");

        InitCharacter();
        HpChanged?.Invoke(CurrentHP, MaxHP);
    }

    /// <summary>Override for per-character stats, immunities, ammo config.</summary>
    protected virtual void InitCharacter() { }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_dead) { if (ShowDebugRanges) QueueRedraw(); return; }

        UpdateTimers(dt);
        UpdateStatus(dt);
        if (!ControlsLocked) HandleAbilities();
        UpdateAnimator(dt);

        if (ShowDebugRanges) QueueRedraw();
    }

    private void UpdateTimers(float dt)
    {
        if (_speedPenaltyTimer > 0f)
        {
            _speedPenaltyTimer -= dt;
            if (_speedPenaltyTimer <= 0f) _speedPenaltyMult = 1f;
        }
        if (_invulnTimer > 0f)
        {
            _invulnTimer -= dt;
            Sprite.Visible = Mathf.PosMod(_invulnTimer, 0.12f) < 0.06f;
            if (_invulnTimer <= 0f) Sprite.Visible = true;
        }
    }

    private void UpdateStatus(float dt)
    {
        if (!IsBurning) return;
        _burnTimer -= dt;
        if (!BurnImmune)
            ApplyDamageInternal(BurnDotPerSecond * dt, Vector2.Zero, ignoreIFrames: true);
        if (_burnTimer <= 0f)
        {
            IsBurning = false;
            Sprite.SelfModulate = _baseTint;
        }
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

        v.X = inputDir * MoveSpeed * _speedPenaltyMult;
        Velocity = v;
        MoveAndSlide();

        if (inputDir > 0.01f) { Facing = 1f; Sprite.FlipH = false; }
        else if (inputDir < -0.01f) { Facing = -1f; Sprite.FlipH = true; }
    }

    // ── Ability hooks ─────────────────────────────────────────────────────
    private void HandleAbilities()
    {
        if (Input.IsActionJustPressed("attack")) HandleBA();
        if (Input.IsActionJustPressed("skill1")) HandleSkill1();
        if (Input.IsActionJustPressed("skill2")) HandleSkill2();
        if (Input.IsActionJustPressed("ult")) HandleSkillUlt();
    }

    protected virtual void HandleBA() { }
    protected virtual void HandleSkill1() { }
    protected virtual void HandleSkill2() { }
    protected virtual void HandleSkillUlt() { }

    /// <summary>No-op until real animations exist; override and drive Anim here.</summary>
    protected virtual void UpdateAnimator(float dt) { }

    // ── Shared combat helpers ─────────────────────────────────────────────

    /// <summary>
    /// Damage every enemy inside a cone in front of the character.
    /// Returns the number of enemies hit. Damage timing here stands in for the
    /// Unity animation-event trigger; when animations land, call this from the
    /// AnimationPlayer "call method" track instead.
    /// </summary>
    protected int MeleeCone(float range, float halfAngleDeg, float damage,
                            float knockbackSpeed, bool applyBurn, float burnDuration)
    {
        int hits = 0;
        Vector2 origin = GlobalPosition;
        Vector2 aim = new Vector2(Facing, 0f);
        float cos = Mathf.Cos(Mathf.DegToRad(halfAngleDeg));

        foreach (Node n in GetTree().GetNodesInGroup("enemy"))
        {
            if (n is not Enemy e) continue;
            Vector2 to = e.GlobalPosition - origin;
            float dist = to.Length();
            if (dist > range) continue;
            if (dist > 0.01f && to.Normalized().Dot(aim) < cos) continue;

            Vector2 knock = new Vector2(Facing, -0.35f).Normalized() * knockbackSpeed;
            e.TakeDamage(damage, knock);
            if (applyBurn) e.SetBurning(burnDuration);
            hits++;
        }
        return hits;
    }

    protected void ApplyMovePenalty(float mult, float duration)
    {
        _speedPenaltyMult = mult;
        _speedPenaltyTimer = duration;
    }

    /// <summary>Set the character on fire. For BurnImmune characters this is a
    /// pure passive trigger (no damage); for others it deals DoT.</summary>
    public void SetBurning(float duration)
    {
        IsBurning = true;
        _burnTimer = Mathf.Max(_burnTimer, duration);
        Sprite.SelfModulate = new Color(1f, 0.55f, 0.35f);
    }

    public void TakeDamage(float amount, Vector2 knockback)
    {
        ApplyDamageInternal(amount, knockback, ignoreIFrames: false);
    }

    private void ApplyDamageInternal(float amount, Vector2 knockback, bool ignoreIFrames)
    {
        if (_dead) return;
        if (!ignoreIFrames && _invulnTimer > 0f) return;

        CurrentHP = Mathf.Max(0f, CurrentHP - amount * DamageTakenMult);
        HpChanged?.Invoke(CurrentHP, MaxHP);

        if (knockback != Vector2.Zero)
        {
            Velocity = knockback;
            _invulnTimer = 0.8f;
        }
        if (CurrentHP <= 0f) Die();
    }

    private void Die()
    {
        _dead = true;
        LivesRemaining = Mathf.Max(0, LivesRemaining - 1);
        Sprite.Modulate = new Color(0.4f, 0.4f, 0.4f);
        Velocity = Vector2.Zero;
        IsBurning = false;
        Died?.Invoke();
    }

    /// <summary>Brought back by GameManager after a death (if lives remain).</summary>
    public void Respawn()
    {
        _dead = false;
        CurrentHP = MaxHP;
        GlobalPosition = _spawnPoint;
        Velocity = Vector2.Zero;
        _invulnTimer = 1.2f;
        Sprite.Modulate = Colors.White;
        Sprite.SelfModulate = _baseTint;
        Sprite.Visible = true;
        HpChanged?.Invoke(CurrentHP, MaxHP);
    }

    // ── Debug range drawing ───────────────────────────────────────────────
    public override void _Draw()
    {
        if (ShowDebugRanges) DrawDebug();
    }

    /// <summary>Override to draw skill ranges/telegraphs in local space
    /// (origin = character center). Facing gives left/right.</summary>
    protected virtual void DrawDebug() { }

    /// <summary>Draw a filled-outline cone (two edge lines + an arc) in front of
    /// the character. <paramref name="centerAngleRad"/> is measured from +X.</summary>
    protected void DrawConeLocal(float radius, float centerAngleRad, float halfAngleRad,
                                 Color color, float width = 2f)
    {
        float a0 = centerAngleRad - halfAngleRad;
        float a1 = centerAngleRad + halfAngleRad;
        Vector2 p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
        Vector2 p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
        DrawLine(Vector2.Zero, p0, color, width);
        DrawLine(Vector2.Zero, p1, color, width);
        DrawArc(Vector2.Zero, radius, a0, a1, 24, color, width);
    }
}

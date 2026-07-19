using Godot;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// Sifu Pangda — a two-stance tank. Fuhu is selected only while physically grounded;
/// Heli is selected in air and in SoftVolumes. See Docs/Sifu Pangda.gdd v1.1.
/// Presentation is driven from the generated idle, walk, jump, Fuhu palm, and
/// Fuhu kick sheets. States without an approved sheet deliberately fall back to
/// idle until their final art is available.
/// </summary>
public partial class SifuPangda : CharacterController
{
    private enum PangdaState
    {
        Normal,
        FuhuPalmWindup,
        FuhuPalmRecovery,
        HeliDive,
        HeliDiveRecovery,
        FuhuShiftWindup,
        HeliShift,
        FuhuDrink,
    }

    [Export] public PackedScene BottleScene;

    private PangdaState _state;
    private float _stateTimer;
    private float _fuhuLungeTimer;
    private int _fuhuComboStage;
    private bool _drinkShieldGranted;
    private float _chiShield;
    private float _fuhuShiftCd;
    private float _heliShiftCd;
    private float _fuhuECd;
    private float _heliECd;
    private Vector2 _dashDirection = Vector2.Right;
    private float _dashDistanceRemaining;

    private SifuPangdaDef Def => CharacterTable.SifuPangda;

    // Keep the movement threshold in authored metres/second, then convert at the
    // presentation edge like the other protagonist animation controllers.
    private const float WalkAnimationThresholdMps = 0.25f;

    public override float Shield => _chiShield;
    public override bool CanSwitchProtagonist => _state == PangdaState.Normal;
    public override bool CanUseHeldItem => _state == PangdaState.Normal;
    public override bool LocksPhysicalMotion => _state is PangdaState.HeliDive or PangdaState.HeliShift;
    public override (float Remaining, float Max) ShiftCooldown => IsOnFloor()
        ? (_fuhuShiftCd, Def.FuhuShiftCooldown) : (_heliShiftCd, Def.HeliShiftCooldown);
    public override (float Remaining, float Max) ESkillCooldown => IsOnFloor()
        ? (_fuhuECd, Def.FuhuECooldown) : (_heliECd, Def.HeliECooldown);

    public override void _Ready()
    {
        base._Ready();
        DamageDealt += OnDamageDealt;
    }

    public override void _ExitTree()
    {
        DamageDealt -= OnDamageDealt;
        base._ExitTree();
    }

    protected override void InitCharacter()
    {
        SetBaseMaxHP(Def.BaseHp);
        MoveSpeed = Units.Px(Def.MoveSpeedMps);
        MaxJumps = Def.MaxJumps;
        ConfigureAmmo("Sifu Pangda");
    }

    protected override void OnAmmoReloaded(int restored) => _fuhuComboStage = 0;

    public override void SaveCooldownsToState(ProtagonistState p)
    {
        if (p == null) return;
        p.ShiftCdRemaining = _fuhuShiftCd;
        p.ESkillCdRemaining = _fuhuECd;
        p.ShiftAltCdRemaining = _heliShiftCd;
        p.ESkillAltCdRemaining = _heliECd;
    }

    public override void LoadCooldownsFromState(ProtagonistState p)
    {
        if (p == null) return;
        _fuhuShiftCd = p.ShiftCdRemaining;
        _fuhuECd = p.ESkillCdRemaining;
        _heliShiftCd = p.ShiftAltCdRemaining;
        _heliECd = p.ESkillAltCdRemaining;
    }

    public override void ResetDebugCombatState()
    {
        base.ResetDebugCombatState();
        _state = PangdaState.Normal;
        _stateTimer = 0f;
        _fuhuLungeTimer = 0f;
        _fuhuComboStage = 0;
        _drinkShieldGranted = false;
        _chiShield = 0f;
        _fuhuShiftCd = _heliShiftCd = _fuhuECd = _heliECd = 0f;
        ControlsLocked = false;
        RestoreMovementVelocityState(Vector2.Zero, Vector2.Zero);
        NotifyHpChanged();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (Dead)
        {
            SetChiShield(0f);
            return;
        }
        float worldDt = (float)delta;
        float actDt = GetActDelta(worldDt);
        TickCooldowns(actDt);
        TickState(actDt);
        TickChiShield(worldDt);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        float dt = GetActDelta((float)delta);

        if (_state == PangdaState.FuhuPalmRecovery && _fuhuLungeTimer > 0f)
        {
            SetIntentHorizontalVelocity(Facing * Units.Px(Def.FuhuPalmLungeDistanceM / Def.FuhuPalmLungeSeconds));
            _fuhuLungeTimer = Mathf.Max(0f, _fuhuLungeTimer - dt);
            if (_fuhuLungeTimer <= 0f) SetIntentHorizontalVelocity(0f);
        }

        if (_state == PangdaState.HeliDive) TickHeliDive(dt);
        else if (_state == PangdaState.HeliShift) TickHeliShift(dt);
    }

    protected override void HandleBA()
    {
        if (_state != PangdaState.Normal) return;
        if (IsOnFloor()) StartFuhuPalm();
        else StartHeliDive();
    }

    protected override void HandleSkill1()
    {
        if (_state != PangdaState.Normal) return;
        if (IsOnFloor()) StartFuhuShift();
        else StartHeliShift();
    }

    protected override void HandleSkill2()
    {
        if (_state != PangdaState.Normal) return;
        if (IsOnFloor()) StartFuhuDrink();
        else ThrowHeliBottle();
    }

    protected override void HandleSkillUlt()
    {
        // Deferred: the project-wide ultimate model owns Pangda's eventual implementation.
    }

    protected override float AbsorbDamage(float damage)
    {
        damage = base.AbsorbDamage(damage);
        if (_chiShield <= 0f || damage <= 0f) return damage;

        float absorbed = Mathf.Min(_chiShield, damage);
        _chiShield -= absorbed;
        NotifyHpChanged();
        return damage - absorbed;
    }

    private void TickCooldowns(float dt)
    {
        _fuhuShiftCd = Mathf.Max(0f, _fuhuShiftCd - dt);
        _heliShiftCd = Mathf.Max(0f, _heliShiftCd - dt);
        _fuhuECd = Mathf.Max(0f, _fuhuECd - dt);
        _heliECd = Mathf.Max(0f, _heliECd - dt);
    }

    private void TickState(float dt)
    {
        switch (_state)
        {
            case PangdaState.FuhuPalmWindup:
                _stateTimer -= dt;
                if (_stateTimer <= 0f)
                {
                    PerformFuhuPalm(); // NOTE(animation): palm impact event.
                    _state = PangdaState.FuhuPalmRecovery;
                    _stateTimer = Def.FuhuPalmRecovery;
                    _fuhuLungeTimer = Def.FuhuPalmLungeSeconds;
                }
                break;
            case PangdaState.FuhuPalmRecovery:
            case PangdaState.HeliDiveRecovery:
            case PangdaState.FuhuShiftWindup:
                _stateTimer -= dt;
                if (_stateTimer <= 0f)
                {
                    if (_state == PangdaState.FuhuShiftWindup) PerformFuhuKick(); // NOTE(animation): kick impact event.
                    _state = PangdaState.Normal;
                    ControlsLocked = false;
                }
                break;
            case PangdaState.FuhuDrink:
                _stateTimer -= dt;
                if (!_drinkShieldGranted && _stateTimer <= Def.DrinkDuration - Def.DrinkShieldAt)
                {
                    _drinkShieldGranted = true;
                    SetChiShield(Def.ChiShieldCap);
                }
                if (_stateTimer <= 0f) _state = PangdaState.Normal;
                break;
        }
    }

    private void TickChiShield(float worldDt)
    {
        if (_chiShield <= 0f || CombatInactivitySeconds < Def.CombatInactivityGrace) return;
        SetChiShield(Mathf.Max(0f, _chiShield - Def.ChiDecayPerSecond * worldDt));
    }

    private void OnDamageDealt(float dealt)
    {
        if (dealt <= 0f) return;
        SetChiShield(Mathf.Min(Def.ChiShieldCap, _chiShield + dealt * Def.ChiDamageConversion));
    }

    private void SetChiShield(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, Def.ChiShieldCap);
        if (Mathf.IsEqualApprox(_chiShield, clamped)) return;
        _chiShield = clamped;
        NotifyHpChanged();
    }

    private void StartFuhuPalm()
    {
        if (!TryConsumeAmmo()) return;
        _fuhuComboStage = (_fuhuComboStage + 1) % Def.FuhuPalmStacks;
        StartAmmoAttackInterval();
        RequestAmmoReload();
        _state = PangdaState.FuhuPalmWindup;
        _stateTimer = Def.FuhuPalmWindup;
        ControlsLocked = true;
        PlayAnim("fuhu_palm", restart: true);
    }

    private void PerformFuhuPalm()
    {
        Vector2 center = GlobalPosition + new Vector2(Facing * Units.Px(Def.FuhuPalmWidthM * 0.5f), 0f);
        HitAxisAlignedRect(center, Def.FuhuPalmWidthM, Def.FuhuPalmHeightM,
            Def.FuhuPalmDamage, Vector2.Zero, Def.FuhuPalmStun);
        ShakeCamera(0.28f);
    }

    private void StartHeliDive()
    {
        _state = PangdaState.HeliDive;
        ControlsLocked = true;
        RestoreMovementVelocityState(Vector2.Zero, Vector2.Zero);
    }

    private void TickHeliDive(float dt)
    {
        Velocity = Vector2.Down * Units.Px(Def.HeliDiveSpeedMps);
        MoveAndSlide();
        if (!IsOnFloor()) return;

        Vector2 impactCenter = GlobalPosition + new Vector2(0f, Units.Px(0.5f));
        HitAxisAlignedRect(impactCenter, Def.HeliDiveImpactWidthM, Def.HeliDiveImpactHeightM,
            Def.HeliDiveDamage, Vector2.Up * Units.Px(Def.HeliDiveKnockupMps), Def.HeliDiveStun);
        ShakeCamera(0.45f);
        RestoreMovementVelocityState(Vector2.Zero, Vector2.Zero);
        Velocity = Vector2.Zero;
        _state = PangdaState.HeliDiveRecovery;
        _stateTimer = Def.HeliDiveRecovery;
    }

    private void StartFuhuShift()
    {
        if (_fuhuShiftCd > 0f) return;
        _fuhuShiftCd = Def.FuhuShiftCooldown;
        _state = PangdaState.FuhuShiftWindup;
        _stateTimer = Def.FuhuShiftWindup;
        ControlsLocked = true;
        PlayAnim("fuhu_kick", restart: true);
    }

    private void PerformFuhuKick()
    {
        float width = Def.FuhuKickWidthM;
        float height = Def.FuhuKickHeightM;
        Vector2 center = GlobalPosition + new Vector2(Facing * Units.Px(width * 0.5f),
            -Units.Px((height - Units.PlayerHeightM) * 0.5f));
        Vector2 push = new Vector2(Facing * Units.Px(Def.FuhuKickKnockbackMps), 0f);
        HitAxisAlignedRect(center, width, height, Def.FuhuKickDamage, push, Def.FuhuKickStun);
        ShakeCamera(0.42f);
    }

    private void StartHeliShift()
    {
        if (_heliShiftCd > 0f) return;
        _heliShiftCd = Def.HeliShiftCooldown;
        Vector2 aim = GetGlobalMousePosition() - GlobalPosition;
        _dashDirection = aim.LengthSquared() > 0.01f ? aim.Normalized() : new Vector2(Facing, 0f);
        _dashDistanceRemaining = Units.Px(Def.HeliDashDistanceM);
        _state = PangdaState.HeliShift;
        ControlsLocked = true;
        RestoreMovementVelocityState(Vector2.Zero, Vector2.Zero);
    }

    private void TickHeliShift(float dt)
    {
        if (HasFoeInLeadingDashBox())
        {
            ResolveHeliShift();
            return;
        }

        float travel = Units.Px(Def.HeliDashSpeedMps) * dt;
        Velocity = _dashDirection * Units.Px(Def.HeliDashSpeedMps);
        MoveAndSlide();
        _dashDistanceRemaining -= travel;
        if (IsOnFloor() || IsOnWall() || IsOnCeiling() || _dashDistanceRemaining <= 0f || HasFoeInLeadingDashBox())
            ResolveHeliShift();
    }

    private bool HasFoeInLeadingDashBox()
    {
        float size = Def.HeliDashCheckBoxM;
        Vector2 center = GlobalPosition + _dashDirection * Units.Px(size * 0.5f);
        return AnyFoeInOrientedRect(center, size, size, _dashDirection);
    }

    private void ResolveHeliShift()
    {
        if (_state != PangdaState.HeliShift) return;
        HitAxisAlignedRect(GlobalPosition, Def.HeliDashImpactBoxM, Def.HeliDashImpactBoxM,
            Def.HeliDashDamage, _dashDirection * Units.Px(Def.HeliDashKnockbackMps), 0f);
        ShakeCamera(0.38f);
        RestoreMovementVelocityState(Vector2.Zero, Vector2.Zero);
        Velocity = Vector2.Zero;
        _state = PangdaState.Normal;
        ControlsLocked = false;
    }

    private void StartFuhuDrink()
    {
        if (_fuhuECd > 0f) return;
        _fuhuECd = Def.FuhuECooldown;
        _state = PangdaState.FuhuDrink;
        _stateTimer = Def.DrinkDuration;
        _drinkShieldGranted = false;
        ApplyMovePenalty(Def.DrinkMoveMultiplier, Def.DrinkDuration);
    }

    private void ThrowHeliBottle()
    {
        if (_heliECd > 0f || BottleScene == null || GetParent() == null) return;
        _heliECd = Def.HeliECooldown;
        Vector2 origin = FirePoint != null ? FirePoint.GlobalPosition : GlobalPosition;
        Vector2 aim = GetGlobalMousePosition() - origin;
        Vector2 direction = aim.LengthSquared() > 0.01f ? aim.Normalized() : new Vector2(Facing, 0f);
        PangdaBottle bottle = BottleScene.Instantiate<PangdaBottle>();
        GetParent().AddChild(bottle);
        bottle.GlobalPosition = origin;
        bottle.Init(this, direction, Def.BottleSpeedMps, Def.BottleLifetime, Def.BottleRadiusM,
            Def.BottleDamage * DamageDealtMultiplier, Def.BottleTrappedSeconds);
    }

    private void HitAxisAlignedRect(Vector2 center, float widthM, float heightM, float damage,
        Vector2 knockback, float stun)
    {
        float halfW = Units.Px(widthM * 0.5f);
        float halfH = Units.Px(heightM * 0.5f);
        foreach (Node node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not BaseFoe foe) continue;
            Vector2 delta = foe.GlobalPosition - center;
            if (Mathf.Abs(delta.X) > halfW + foe.HitRadius || Mathf.Abs(delta.Y) > halfH + foe.HitRadius) continue;
            float dealt = foe.TakeHit(new HitInfo(damage * DamageDealtMultiplier, knockback, stun), GlobalPosition);
            ReportDamageDealt(dealt);
        }
    }

    private bool AnyFoeInOrientedRect(Vector2 center, float widthM, float heightM, Vector2 forward)
    {
        float halfW = Units.Px(widthM * 0.5f);
        float halfH = Units.Px(heightM * 0.5f);
        Vector2 perpendicular = new(-forward.Y, forward.X);
        foreach (Node node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not BaseFoe foe) continue;
            Vector2 delta = foe.GlobalPosition - center;
            if (Mathf.Abs(delta.Dot(forward)) <= halfW + foe.HitRadius
                && Mathf.Abs(delta.Dot(perpendicular)) <= halfH + foe.HitRadius)
                return true;
        }
        return false;
    }

    // ── Animation automata ───────────────────────────────────────────────
    protected override void UpdateAnimator(float dt)
    {
        // No death/hurt sheet has been approved yet, so death intentionally uses
        // idle as the safe presentation fallback too.
        if (Dead) { PlayAnim("idle"); return; }

        switch (_state)
        {
            case PangdaState.FuhuPalmWindup:
            case PangdaState.FuhuPalmRecovery:
                PlayAnim("fuhu_palm");
                return;
            case PangdaState.FuhuShiftWindup:
                PlayAnim("fuhu_kick");
                return;

            // No final sheets yet for either Heli action, Drink, or Bottle Throw.
            // Do not repurpose the raw source sheets; idle keeps these states readable
            // without promoting unfinished art into the runtime.
            case PangdaState.HeliDive:
            case PangdaState.HeliDiveRecovery:
            case PangdaState.HeliShift:
            case PangdaState.FuhuDrink:
                PlayAnim("idle");
                return;
        }

        if (!IsOnFloor())
        {
            // A completed jump clip holds its final frame until Pangda lands rather
            // than restarting each process frame while he remains airborne.
            if (LastAnim != "jump" || (Anim?.IsPlaying() ?? false)) PlayAnim("jump");
            return;
        }

        if (Mathf.Abs(Velocity.X) > Units.Px(WalkAnimationThresholdMps)) PlayAnim("walk");
        else PlayAnim("idle");
    }

    protected override void DrawDebug()
    {
        Color fuhuPalm = new(0.95f, 0.72f, 0.2f, 0.7f);
        Color fuhuKick = new(0.95f, 0.35f, 0.18f,
            _state == PangdaState.FuhuShiftWindup ? 0.95f : 0.48f);
        Color heli = new(0.35f, 0.8f, 0.95f, 0.7f);
        if (IsOnFloor())
        {
            float palmWidth = Units.Px(Def.FuhuPalmWidthM);
            float palmStartX = Facing > 0f ? 0f : -palmWidth;
            DrawRect(new Rect2(new Vector2(palmStartX, -Units.Px(Def.FuhuPalmHeightM * 0.5f)),
                new Vector2(palmWidth, Units.Px(Def.FuhuPalmHeightM))), fuhuPalm, false, 2f);

            // Fuhu Shift is intentionally much wider/taller than the palm. Its bottom
            // aligns with Pangda's feet, matching PerformFuhuKick's centre calculation.
            float kickWidth = Units.Px(Def.FuhuKickWidthM);
            float kickHeight = Units.Px(Def.FuhuKickHeightM);
            float kickStartX = Facing > 0f ? 0f : -kickWidth;
            float kickTop = Units.Px(Units.PlayerHeightM * 0.5f - Def.FuhuKickHeightM);
            DrawRect(new Rect2(new Vector2(kickStartX, kickTop), new Vector2(kickWidth, kickHeight)),
                fuhuKick, false, _state == PangdaState.FuhuShiftWindup ? 4f : 2f);
        }
        else
        {
            // Falling Crane's knock-up lands in this 4 m × 1 m rectangle at the feet.
            // Draw it while airborne so a player can line up the landing before committing;
            // the bright outline identifies an already-active dive.
            float impactWidth = Units.Px(Def.HeliDiveImpactWidthM);
            float impactHeight = Units.Px(Def.HeliDiveImpactHeightM);
            Color diveImpact = new(0.48f, 0.92f, 1f,
                _state == PangdaState.HeliDive ? 0.95f : 0.48f);
            DrawRect(new Rect2(new Vector2(-impactWidth * 0.5f, 0f), new Vector2(impactWidth, impactHeight)),
                diveImpact, false, _state == PangdaState.HeliDive ? 4f : 2f);

            float half = Units.Px(Def.HeliDashCheckBoxM * 0.5f);
            DrawRect(new Rect2(_dashDirection * half - new Vector2(half, half), new Vector2(half * 2f, half * 2f)), heli, false, 2f);
        }
    }
}

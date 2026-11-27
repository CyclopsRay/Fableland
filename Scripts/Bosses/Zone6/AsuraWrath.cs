using System.Collections.Generic;
using Godot;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>
/// The Asura of Wrath (修罗), Avīci's warden of 嗔 Anger (Docs/Zone6.gdd §5.1): three faces, six
/// arms, a flame aureole. Numbers are the design's first pass.
///
/// • <b>Six-Arm Flurry</b> (BA): three cone hits 0.12 s apart, 3.5 m reach, 12 damage each;
///   four when enraged.
/// • <b>Wrath Fire</b> (S1, 8 s): two floor waves, left and right, at 10 m/s for 12 m — 15
///   damage and 20 OnFire stacks; a jump clears them.
/// • <b>Vajra Throw</b> (S2, 7 s): flies 12 m and returns — 20 damage each way, a small shove.
/// • <b>Rising Wrath</b> (passive): attack interval and cooldowns × (1 − 0.4 × the share of HP
///   missing); <b>enraged</b> below half HP (cue <c>passive.wrath.enrage</c>).
/// • Brain: Flurry within 3.5 m; Vajra at 5–12 m; Wrath Fire when the player is on the ground
///   within 12 m; otherwise close in.
/// </summary>
public partial class AsuraWrath : Zone6BossFighter
{
    public const float FlurryRangeM = 3.5f, FlurryHalfAngle = 60f, FlurryDamage = 12f, FlurryGap = 0.12f, FlurryShoveMps = 5f;
    public const int FlurryHits = 3, EnragedFlurryHits = 4;
    public const float WrathFireCooldown = 8f, WrathFireReachM = 12f, WrathFireSpeedMps = 10f, WrathFireDamage = 15f, WrathFireStacks = 20f;
    public const float VajraCooldown = 7f, VajraReachM = 12f, VajraSpeedMps = 18f, VajraDamage = 20f, VajraNearM = 5f;
    public const float RisingWrath = 0.4f, EnrageBelowHp = 0.5f;
    private const string WrathSource = "rising_wrath";

    protected override BossDef Row => BossTable.Asura;

    private float _fireCd, _vajraCd;
    private readonly List<float> _flurry = new();   // seconds until each queued hit
    private int _flurryStage;
    private bool _enraged;
    private Vajra _vajra;

    /// <summary>Read-only, for checks and the debug HUD.</summary>
    public bool Enraged => _enraged;
    public bool VajraOut => _vajra != null && IsInstanceValid(_vajra);
    public float WrathScale => 1f - RisingWrath * Mathf.Clamp(1f - HpRatio, 0f, 1f);
    public override (float Remaining, float Max) ShiftCooldown => (_fireCd, ScaleCooldown(WrathFireCooldown));
    public override (float Remaining, float Max) ESkillCooldown => (_vajraCd, ScaleCooldown(VajraCooldown));

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_fireCd > 0f) _fireCd = Mathf.Max(0f, _fireCd - dt);
        if (_vajraCd > 0f) _vajraCd = Mathf.Max(0f, _vajraCd - dt);
        UpdateWrath();
        TickFlurry(dt);
        base._PhysicsProcess(delta);
    }

    /// <summary>Rising Wrath: the more it is hurt, the faster it acts — through the body's own
    /// cooldown and attack-interval scales, so every kit rule reads it the same way.</summary>
    private void UpdateWrath()
    {
        if (IsDefeated) return;
        float scale = WrathScale;
        SetCooldownScale(WrathSource, scale);
        SetAttackIntervalScale(WrathSource, scale);
        if (!_enraged && HpRatio <= EnrageBelowHp)
        {
            _enraged = true;
            CueBus.Emit(this, "passive.wrath.enrage", magnitude: HpRatio);
        }
    }

    // ── Six-Arm Flurry ────────────────────────────────────────────────────────

    protected override void HandleBA()
    {
        if (_flurry.Count > 0 || !TryConsumeAmmo()) return;
        int hits = _enraged ? EnragedFlurryHits : FlurryHits;
        for (int i = 0; i < hits; i++) _flurry.Add(i * FlurryGap);
        _flurryStage = 0;
        CueBus.Emit(this, "ba.flurry", variant: hits, size: Units.Px(FlurryRangeM), magnitude: FlurryDamage, direction: FacingDirection,
            extents: CueShapes.Cone(Units.Px(FlurryRangeM), FlurryHalfAngle));
        StartAmmoAttackInterval();
        RequestAmmoReload();
    }

    private void TickFlurry(float dt)
    {
        for (int i = _flurry.Count - 1; i >= 0; i--)
        {
            _flurry[i] -= dt;
            if (_flurry[i] > 0f) continue;
            _flurry.RemoveAt(i);
            _flurryStage++;
            MeleeCone(Units.Px(FlurryRangeM), FlurryHalfAngle, FlurryDamage, Units.Px(FlurryShoveMps), false, 0f,
                cueAction: "ba.flurry", cueVariant: _flurryStage);
        }
    }

    /// <summary>How many hits the last flurry was made of (checks read it).</summary>
    public int LastFlurryHits => _flurryStage;

    // ── Wrath Fire ────────────────────────────────────────────────────────────

    protected override void HandleSkill1()
    {
        if (_fireCd > 0f) return;
        _fireCd = ScaleCooldown(WrathFireCooldown);
        CueBus.Emit(this, "s1.wrath_fire", size: Units.Px(WrathFireReachM), magnitude: WrathFireDamage);
        foreach (float direction in new[] { -1f, 1f })
        {
            var wave = new WrathFireWave { Name = "WrathFire" };
            GetParent().AddChild(wave);
            wave.GlobalPosition = new Vector2(GlobalPosition.X, Feet.Y);
            wave.Init(this, direction, Units.Px(WrathFireSpeedMps), Units.Px(WrathFireReachM), WrathFireDamage, WrathFireStacks);
        }
    }

    // ── Vajra Throw ───────────────────────────────────────────────────────────

    protected override void HandleSkill2()
    {
        if (_vajraCd > 0f || VajraOut) return;
        _vajraCd = ScaleCooldown(VajraCooldown);
        CueBus.Emit(this, "s2.vajra", magnitude: VajraDamage, direction: FacingDirection);
        _vajra = new Vajra { Name = "Vajra" };
        GetParent().AddChild(_vajra);
        _vajra.GlobalPosition = GlobalPosition + new Vector2(Facing * Units.Px(0.8f), -Units.Px(0.6f));
        _vajra.Init(this, Facing, Units.Px(VajraSpeedMps), Units.Px(VajraReachM), VajraDamage);
    }

    protected override void OnStandDown() => _flurry.Clear();

    // ── Brain ─────────────────────────────────────────────────────────────────

    protected override void BuildRules(BossRuleSet rules, VirtualPad pad)
    {
        // legs: close in to flurry range.
        rules.Add(new BossRule(BossBrain.Move, "close in", () => Senses.HasTarget && TargetMetres > FlurryRangeM * 0.8f)
        {
            Each = () =>
            {
                WalkToward(pad, Senses.TargetPosition.X, Units.Px(FlurryRangeM * 0.6f));
                FaceTarget(pad);
            },
        });
        rules.Add(new BossRule(BossBrain.Move, "stand", () => true)
        {
            Each = () =>
            {
                StopWalking(pad);
                FaceTarget(pad);
            },
        });

        // hands, most urgent first
        rules.Add(new BossRule(BossBrain.Act, "six-arm flurry", () => TargetMetres <= FlurryRangeM && _flurry.Count == 0)
        {
            OnStart = () =>
            {
                FaceTarget(pad);
                pad.Tap(ControlAction.Attack);
            },
            Cooldown = 0.2f,
        });
        rules.Add(new BossRule(BossBrain.Act, "vajra", () => _vajraCd <= 0f && !VajraOut && TargetMetres >= VajraNearM && TargetMetres <= VajraReachM)
        {
            OnStart = () =>
            {
                FaceTarget(pad);
                pad.Tap(ControlAction.Skill2);
            },
            Cooldown = 0.5f,
        });
        rules.Add(new BossRule(BossBrain.Act, "wrath fire", () => _fireCd <= 0f && TargetMetres <= WrathFireReachM && (TargetBody?.IsOnFloor() ?? false))
        {
            OnStart = () => pad.Tap(ControlAction.Skill1),
            Cooldown = 0.5f,
        });
    }
}

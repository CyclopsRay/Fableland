using System.Collections.Generic;
using Godot;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>
/// The Ferryman (渡魂人), the Void Frontier's first guardian (Docs/Zone6.gdd §4.1): a hooded
/// giant who poles the dead across the Styx. Numbers are the design's first pass.
///
/// • <b>Oar Sweep</b> (BA): a cone 5 m deep, ±50°: 30 damage and an 18 m/s shove; every 1.4 s.
/// • <b>Obol Toss</b> (S1, 6 s): a fan of three coins thrown under gravity — the place's
///   gravity too — each 10 damage and the toll: 150 of the victim's ult charge.
/// • <b>Undertow</b> (S2, 12 s): every fighter within 14 m is dragged toward him at 4 m/s for
///   2.5 s; then he sweeps.
/// • <b>Skiff Ram</b> (below half HP, every 15 s): a 0.8 s lantern telegraph, then he rides
///   across at 16 m/s — 40 damage and a knock-up to whatever is in his way.
/// • Brain: Undertow when the player is beyond 8 m and it is ready; Sweep within 5 m; Obol at
///   5–14 m; the Ram in phase 2 when it is ready; otherwise hold near the river's edge — the
///   perch he opened on.
/// </summary>
public partial class Ferryman : Zone6BossFighter
{
    public const float SweepRangeM = 5f, SweepHalfAngle = 50f, SweepDamage = 30f, SweepShoveMps = 18f;
    public const float ObolCooldown = 6f, ObolDamage = 10f, ObolUltToll = 150f;
    /// <summary>The fan: three coins at 45°, thrown to land short of, on and past the player.</summary>
    public static readonly float[] ObolFan = { 0.8f, 1f, 1.15f };
    public const float UndertowCooldown = 12f, UndertowRangeM = 14f, UndertowPullMps = 4f, UndertowSeconds = 2.5f;
    public const float UndertowBeyondM = 8f;
    public const float RamBelowHp = 0.5f, RamEvery = 15f, RamTelegraph = 0.8f, RamSpeedMps = 16f, RamDamage = 40f;
    public const float RamKnockUpMps = 22f, RamMaxSeconds = 1.6f;
    private const string UndertowSource = "undertow";
    private const string RamSource = "skiff_ram";

    protected override BossDef Row => BossTable.Ferryman;

    private float _obolCd, _undertowCd, _ramCd;
    private float _undertowLeft;                                // > 0 while the river pulls
    private readonly List<CharacterController> _dragged = new();
    private float _ramTelegraph, _ramLeft, _ramDirection;       // telegraph, then the ride
    private readonly HashSet<ulong> _rammed = new();

    /// <summary>Read-only, for checks and the debug HUD.</summary>
    public bool Undertowing => _undertowLeft > 0f;
    public bool Ramming => _ramTelegraph > 0f || _ramLeft > 0f;
    public bool InPhaseTwo => HpRatio <= RamBelowHp;
    public override (float Remaining, float Max) ShiftCooldown => (_obolCd, ScaleCooldown(ObolCooldown));
    public override (float Remaining, float Max) ESkillCooldown => (_undertowCd, ScaleCooldown(UndertowCooldown));

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_obolCd > 0f) _obolCd = Mathf.Max(0f, _obolCd - dt);
        if (_undertowCd > 0f) _undertowCd = Mathf.Max(0f, _undertowCd - dt);
        if (InPhaseTwo && _ramCd > 0f) _ramCd = Mathf.Max(0f, _ramCd - dt);
        TickUndertow(dt);
        TickRam(dt);
        base._PhysicsProcess(delta);
    }

    // ── Oar Sweep ─────────────────────────────────────────────────────────────

    protected override void HandleBA()
    {
        if (!TryConsumeAmmo()) return;
        Sweep();
        StartAmmoAttackInterval();
        RequestAmmoReload();
    }

    private void Sweep()
    {
        CueBus.Emit(this, "ba.sweep", size: Units.Px(SweepRangeM), magnitude: SweepDamage, direction: FacingDirection,
            extents: CueShapes.Cone(Units.Px(SweepRangeM), SweepHalfAngle));
        MeleeCone(Units.Px(SweepRangeM), SweepHalfAngle, SweepDamage, Units.Px(SweepShoveMps), false, 0f, cueAction: "ba.sweep");
    }

    // ── Obol Toss ─────────────────────────────────────────────────────────────

    protected override void HandleSkill1()
    {
        if (_obolCd > 0f) return;
        _obolCd = ScaleCooldown(ObolCooldown);
        CueBus.Emit(this, "s1.obol", magnitude: ObolDamage, direction: FacingDirection);
        // A fan of three, lobbed at 45°: the middle coin is thrown to land on the player (range
        // v²/g, under the gravity he — and so his coins — feel here), one short, one long.
        float reach = Senses.HasTarget ? Mathf.Clamp(Mathf.Abs(Senses.ToTarget.X), Units.Px(3f), Units.Px(16f)) : Units.Px(9f);
        float speed = Mathf.Sqrt(reach * Units.Gravity * Mathf.Max(0.05f, GravityScale));
        foreach (float share in ObolFan)
        {
            var velocity = new Vector2(Facing, -1f).Normalized() * speed * Mathf.Sqrt(share);
            var coin = new ObolCoin { Name = "Obol" };
            GetParent().AddChild(coin);
            coin.GlobalPosition = GlobalPosition + new Vector2(Facing * Units.Px(0.8f), -Units.Px(1.2f));
            coin.Init(velocity, this, ObolDamage, ObolUltToll);
        }
    }

    // ── Undertow ──────────────────────────────────────────────────────────────

    protected override void HandleSkill2()
    {
        if (_undertowCd > 0f || Undertowing) return;
        _undertowCd = ScaleCooldown(UndertowCooldown);
        _undertowLeft = UndertowSeconds;
        _dragged.Clear();
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
            if (target is CharacterController body && body.GlobalPosition.DistanceTo(GlobalPosition) <= Units.Px(UndertowRangeM))
                _dragged.Add(body);
        CueBus.Start(this, "s2.undertow", size: Units.Px(UndertowRangeM), magnitude: UndertowSeconds);
    }

    private void TickUndertow(float dt)
    {
        if (_undertowLeft <= 0f) return;
        _undertowLeft -= dt;
        foreach (CharacterController body in _dragged)
        {
            if (!IsInstanceValid(body) || body.IsDefeated) continue;
            float side = Mathf.Sign(GlobalPosition.X - body.GlobalPosition.X);
            body.SetContinuousExternalVelocity(UndertowSource, new Vector2(side * Units.Px(UndertowPullMps), 0f));
        }
        if (_undertowLeft > 0f) return;
        EndUndertow();
        Sweep();   // the river delivers them to his oar
    }

    private void EndUndertow()
    {
        _undertowLeft = 0f;
        foreach (CharacterController body in _dragged)
            if (IsInstanceValid(body)) body.ClearContinuousExternalVelocity(UndertowSource);
        _dragged.Clear();
        CueBus.Stop(this, "s2.undertow");
    }

    // ── Skiff Ram ─────────────────────────────────────────────────────────────

    /// <summary>Starts the ram toward the player's side (the brain calls it in phase 2).</summary>
    public void BeginRam()
    {
        if (Ramming) return;
        _ramCd = RamEvery;
        _ramTelegraph = RamTelegraph;
        _ramDirection = Senses.HasTarget && Senses.ToTarget.X < 0f ? -1f : 1f;
        _rammed.Clear();
        CueBus.Emit(this, "s3.ram", magnitude: RamTelegraph, direction: new Vector2(_ramDirection, 0f), tag: "telegraph");
    }

    private void TickRam(float dt)
    {
        if (_ramTelegraph > 0f)
        {
            _ramTelegraph -= dt;
            if (_ramTelegraph > 0f) return;
            _ramLeft = RamMaxSeconds;
            CueBus.Emit(this, "s3.ram", magnitude: RamDamage, direction: new Vector2(_ramDirection, 0f), tag: "ride");
        }
        if (_ramLeft <= 0f) return;
        _ramLeft -= dt;
        SetContinuousExternalVelocity(RamSource, new Vector2(_ramDirection * Units.Px(RamSpeedMps), 0f));

        // Whatever stands in the skiff's way is hit once and thrown up.
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
        {
            if (target is not Node2D node || _rammed.Contains(node.GetInstanceId())) continue;
            Vector2 to = node.GlobalPosition - GlobalPosition;
            if (Mathf.Abs(to.X) > Units.Px(1.4f) + target.HitRadius || Mathf.Abs(to.Y) > Units.Px(2.2f)) continue;
            _rammed.Add(node.GetInstanceId());
            var knock = new Vector2(_ramDirection * Units.Px(6f), -Units.Px(RamKnockUpMps));
            float dealt = target.TakeHit(new HitInfo(RamDamage * DamageDealtMultiplier, knock).From(this, "s3.ram"), GlobalPosition);
            if (dealt > 0f) ReportDamageDealt(dealt);
        }
        if (_ramLeft <= 0f || (IsOnWall() && _ramLeft < RamMaxSeconds - 0.15f)) EndRam();
    }

    private void EndRam()
    {
        _ramLeft = 0f;
        _ramTelegraph = 0f;
        ClearContinuousExternalVelocity(RamSource);
    }

    protected override void OnStandDown()
    {
        EndUndertow();
        EndRam();
    }

    public override void _ExitTree()
    {
        EndUndertow();
        base._ExitTree();
    }

    // ── Brain ─────────────────────────────────────────────────────────────────

    protected override void BuildRules(BossRuleSet rules, VirtualPad pad)
    {
        // legs: still while the river pulls or the skiff rides; else back to the river's edge.
        rules.Add(new BossRule(BossBrain.Move, "ride", () => Ramming || Undertowing) { Each = () => StopWalking(pad) });
        rules.Add(new BossRule(BossBrain.Move, "hold the river's edge", () => true)
        {
            Each = () =>
            {
                WalkToward(pad, HomePoint.X, Units.Px(1f));
                FaceTarget(pad);
            },
        });

        // hands, most urgent first
        rules.Add(new BossRule(BossBrain.Act, "skiff ram", () => InPhaseTwo && _ramCd <= 0f && !Ramming && !Undertowing && Senses.HasTarget)
        {
            OnStart = BeginRam,
            Commit = RamTelegraph + RamMaxSeconds,
            Done = () => !Ramming,
        });
        rules.Add(new BossRule(BossBrain.Act, "undertow",
            () => _undertowCd <= 0f && !Ramming && TargetMetres > UndertowBeyondM && TargetMetres <= UndertowRangeM)
        {
            OnStart = () => pad.Tap(ControlAction.Skill2),
            Commit = UndertowSeconds,
            Done = () => !Undertowing,
        });
        rules.Add(new BossRule(BossBrain.Act, "oar sweep", () => !Ramming && TargetMetres <= SweepRangeM)
        {
            OnStart = () =>
            {
                FaceTarget(pad);
                pad.Tap(ControlAction.Attack);
            },
            Cooldown = 0.3f,
        });
        rules.Add(new BossRule(BossBrain.Act, "obol toss", () => !Ramming && _obolCd <= 0f && TargetMetres > SweepRangeM && TargetMetres <= UndertowRangeM)
        {
            OnStart = () =>
            {
                FaceTarget(pad);
                pad.Tap(ControlAction.Skill1);
            },
            Cooldown = 0.5f,
        });
    }
}

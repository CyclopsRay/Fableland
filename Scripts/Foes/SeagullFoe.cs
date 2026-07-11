using Godot;

/// <summary>
/// Seagull foe (FOES.gdd §4) — the aerial harasser. No gravity, momentum-based flight,
/// no contact damage. Patrols at a fixed height, aggros by accelerating toward the
/// player's X while holding height. Skill 1 = Poop (lv4+), Skill 2 = Dash (lv6+).
/// </summary>
public partial class SeagullFoe : BaseFoe
{
    [Export] public PackedScene PoopScene;

    // Mirrors CharacterController.GroundAccel's export default (3200). Used only as a
    // fallback when no player instance is available to query the live value from.
    private const float PlayerGroundAccelDefault = 3200f;

    // Skill numbers (FOES §4). Poop is available from level 1 (the seagull's basic
    // attack); Dash unlocks at level 6 per the standard skill gate.
    private const float PoopCooldown = 5f;
    private const float PoopBaseDamage = 30f;
    private const float DashCooldown = 10f;

    // Override: Poop (skill 1) is the seagull's basic attack — available at all levels.
    private bool PoopReady => _poopCd <= 0f;
    private float _poopCd;
    private const float DashTelegraph = 1f;                 // stop + red tint
    private const float DashDuration = 0.5f;                // 15 m at 30 m/s
    private static readonly float DashSpeed = Units.Px(30f);// 960 px/s
    private const float DashBaseDamage = 50f;

    private float _spawnX;
    private float _heightY;
    private float _flightAccel;

    private enum DashPhase { None, Telegraph, Dashing }
    private DashPhase _dash = DashPhase.None;
    private float _dashTimer;
    private float _dashDir = 1f;
    private bool _dashHit;

    protected override void InitFoe()
    {
        // FOES §4 base stats.
        BaseHP = 35f;
        BaseDamage = 18f;
        // Base flight top-speed — the GDD leaves the seagull's max speed open (SPD is
        // "accel-based"); 160 px/s (5 m/s) reads as a brisk-but-catchable cruise. Scaled
        // by SpdMult like any foe. (Design decision — see final report.)
        BaseMoveSpeed = 160f;
        UseGravity = false;
        HasContactDamage = false;
        HitRadius = 20f;
        ExternalDamping = 600f;
        SightInterval = 2f;
    }

    protected override void OnSpawnPlaced()
    {
        _spawnX = SpawnOrigin.X;
        _heightY = SpawnOrigin.Y;   // fixed patrol height (set at spawn)
        var p = GetTree().GetFirstNodeInGroup("player") as CharacterController;
        float groundAccel = p != null ? p.GroundAccel : PlayerGroundAccelDefault;
        _flightAccel = groundAccel / 3f;   // FOES §4: turn accel = player ground accel / 3
    }

    // ── Patrol: fixed height, turn at 100 m from spawn (or on a wall) ────────────────
    protected override void UpdatePatrol(float dt)
    {
        if (_dash != DashPhase.None) return;   // dash owns movement while active
        float off = GlobalPosition.X - _spawnX;
        if (off >= Units.Px(100f)) Dir = -1f;
        else if (off <= -Units.Px(100f)) Dir = 1f;
        if (IsOnWall()) Dir = -Dir;            // arena is narrower than 100 m → wall-turn
        Steer(Dir * CurrentMoveSpeed, dt);
        HoldHeight();
    }

    // ── Aggro: accelerate toward player's X, keep height ────────────────────────────
    protected override void UpdateAggro(float dt, CharacterController player)
    {
        if (_dash != DashPhase.None) return;
        float dx = player.GlobalPosition.X - GlobalPosition.X;
        Dir = Mathf.Sign(dx == 0f ? Dir : dx);
        Steer(Dir * CurrentMoveSpeed, dt);
        HoldHeight();
    }

    private void Steer(float targetVx, float dt)
    {
        IntentVel.X = Mathf.MoveToward(IntentVel.X, targetVx, _flightAccel * dt);
        if (Mathf.Abs(IntentVel.X) > 5f) FacingDir = Mathf.Sign(IntentVel.X);
    }

    private void HoldHeight()
    {
        // Steer vertically back to the fixed patrol height (also recovers from knockback).
        float dy = _heightY - GlobalPosition.Y;
        IntentVel.Y = Mathf.Clamp(dy * 6f, -CurrentMoveSpeed, CurrentMoveSpeed);
    }

    // ── Skills ───────────────────────────────────────────────────────────────────────
    protected override void UpdateSkills(float dt, CharacterController player)
    {
        // Ongoing telegraph / dash progresses regardless of aggro so it always completes.
        if (_dash == DashPhase.Telegraph)
        {
            _dashTimer -= dt;
            IntentVel = Vector2.Zero;   // "stops moving for 1 s"
            if (_dashTimer <= 0f)
            {
                _dash = DashPhase.Dashing;
                _dashTimer = DashDuration;
                _dashHit = false;
                ClearTelegraphTint();
            }
            return;
        }
        if (_dash == DashPhase.Dashing)
        {
            _dashTimer -= dt;
            IntentVel = new Vector2(_dashDir * DashSpeed, 0f);   // horizontal swoop
            if (!_dashHit && player != null &&
                (player.GlobalPosition - GlobalPosition).Length() < HitRadius + 28f)
            {
                _dashHit = true;   // pierces: one hit per dash, does not stop
                Vector2 kb = new Vector2(_dashDir, -0.3f).Normalized() * 200f;
                player.TakeHit(new HitInfo(EffectiveDamage(DashBaseDamage), kb, 0.2f));
            }
            if (_dashTimer <= 0f) _dash = DashPhase.None;
            return;
        }

        if (!CanAct || !IsAggro || player == null) return;

        // Tick the local poop cooldown (available from level 1, unlike standard Skill1 gate)
        if (_poopCd > 0f) _poopCd -= dt;

        // Skill 2 — Dash (priority when ready): telegraph then swoop.
        if (Skill2Ready)
        {
            StartSkill2Cooldown(DashCooldown);
            _dash = DashPhase.Telegraph;
            _dashTimer = DashTelegraph;
            _dashDir = Mathf.Sign(player.GlobalPosition.X - GlobalPosition.X);
            if (_dashDir == 0f) _dashDir = FacingDir;
            SetTelegraphTint(new Color(1f, 0.3f, 0.3f));   // red tell
            return;
        }

        // Skill 1 — Poop: basic attack available at all levels.
        if (PoopReady)
        {
            _poopCd = PoopCooldown;
            DropPoop();
        }
    }

    private void DropPoop()
    {
        if (PoopScene == null) return;
        var poop = PoopScene.Instantiate<PoopProjectile>();
        GetParent().AddChild(poop);
        poop.GlobalPosition = GlobalPosition + new Vector2(0f, 12f);
        poop.Init(EffectiveDamage(PoopBaseDamage));   // Init after AddChild
    }

    // ── Sight: movement-facing cone, 60°/10 m (120°/12 m improved) ──────────────────
    protected override bool CanSeePlayer(CharacterController player)
    {
        bool improved = FoeStats.HasImprovedSight(CurrentLevel);
        float range = Units.Px(improved ? 12f : 10f);
        float half = Mathf.DegToRad((improved ? 120f : 60f) * 0.5f);
        Vector2 to = player.GlobalPosition - GlobalPosition;
        float dist = to.Length();
        if (dist > range) return false;
        if (dist < 1f) return true;
        Vector2 aim = new Vector2(FacingDir, 0f);   // retains last facing when still
        return to.Normalized().Dot(aim) >= Mathf.Cos(half);
    }

    protected override void DrawSight()
    {
        bool improved = FoeStats.HasImprovedSight(CurrentLevel);
        float range = Units.Px(improved ? 12f : 10f);
        float half = Mathf.DegToRad((improved ? 120f : 60f) * 0.5f);
        float center = FacingDir < 0f ? Mathf.Pi : 0f;
        Color c = IsAggro ? new Color(1f, 0.4f, 0.3f, 0.7f) : new Color(0.9f, 0.9f, 0.5f, 0.5f);
        Vector2 p0 = new Vector2(Mathf.Cos(center - half), Mathf.Sin(center - half)) * range;
        Vector2 p1 = new Vector2(Mathf.Cos(center + half), Mathf.Sin(center + half)) * range;
        DrawLine(Vector2.Zero, p0, c, 2f);
        DrawLine(Vector2.Zero, p1, c, 2f);
        DrawArc(Vector2.Zero, range, center - half, center + half, 24, c, 2f);
    }

    // NOTE(animation): empty AnimationPlayer sits in SeagullFoe.tscn; drive Fly/Dash/Poop
    // clips once real animations land (dash telegraph already a gameplay promise, T30 §2).
}

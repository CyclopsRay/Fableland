using Godot;
using Fableland.Debug;
using System.Collections.Generic;

/// <summary>
/// Shared foe base (FOES.gdd §9). Replaces the old single <c>Enemy.cs</c>; reuses its
/// working intent + external-velocity model, hazard-driven OnFire/Frozen statuses,
/// defense pool, hit-flash/popup/shake, and gain-no stun — and adds the FOES systems:
/// level scaling (<see cref="FoeStats"/>), a sight/aggro FSM, level-gated skills, and
/// in-combat evolution.
///
/// Subclasses override:
///   InitFoe()                       — set base stats, sight interval, gravity/contact
///   UpdatePatrol(dt)/UpdateAggro    — movement intent per FSM state
///   UpdateSkills(dt, player)        — active skills (gate with Skill1Ready/Skill2Ready)
///   OnDeath()                       — spawn babies / drop loot, then base.OnDeath()
///   CanSeePlayer(player)            — sight-shape test
///   DrawSight()                     — debug telegraph of the sight shape
///
/// <b>Init(level) is called AFTER AddChild</b> (Godot runs _Ready synchronously during
/// AddChild). Base stats come from InitFoe in _Ready; level multipliers apply once in
/// Init. _PhysicsProcess guards against running before Init (KNOWLEDGE caveat).
/// </summary>
public partial class BaseFoe : CharacterBody2D
{
    // ── Base stats (set by subclass InitFoe, then scaled once by Init(level)) ──────
    protected float BaseHP = 70f;
    protected float BaseDamage = 18f;          // contact/skill base; ×AtkMult ×OnFire
    protected float BaseMoveSpeed = 90f;       // max move/flight speed at level 1
    public float Gravity = Units.Gravity;
    protected bool UseGravity = true;
    protected float ContactRange = 44f;
    protected float ContactCooldown = 0.9f;
    protected float ContactKnockback = 260f;
    protected float ContactStun = 0.25f;
    protected bool HasContactDamage = true;
    protected float FootOffset = 22f;          // center→feet (half collision height)

    /// <summary>When true, <see cref="TakeHit"/> and <see cref="ApplyHazard"/> are no-ops —
    /// set on all living foes when the mission resolves so the player can't farm them after
    /// the objective completes (NODES §2.3).</summary>
    public bool Invincible { get; set; }
    public float BurnDamagePerSecond = 14f;
    public float HitRadius = 26f;              // melee/AoE bounding circle (read by attackers)
    public float ExternalDamping = 1200f;      // px/s² decay of knockback (read by TsunamiHazard)
    protected float SightInterval = 1f;        // sight-check period (crab 1 s, gull 2 s)

    [ExportGroup("Debug")]
    [Export] public bool ShowDebugSight = false;

    // ── Scaled runtime stats ──────────────────────────────────────────────────────
    public int CurrentLevel { get; private set; } = 1;
    public float MaxHP { get; private set; }

    /// <summary>Live HP — read by mission HUD bars (boss/objective). Additive Phase-3 hook.</summary>
    public float CurrentHp => _hp;

    /// <summary>Force a fixed max HP regardless of level scaling and refill — used by mission
    /// objectives (DestroyObjective) that need a table-driven HP, not a foe-level one.
    /// Additive Phase-3 hook; call AFTER Init.</summary>
    public void OverrideMaxHp(float hp) { MaxHP = hp; _hp = hp; }
    protected float MoveSpeed;                  // BaseMoveSpeed × SpdMult
    protected float AtkMult = 1f;

    /// <summary>No-grandchildren rule (FOES §3, T30 §2): a foe created by Spawn-on-death
    /// carries this flag so it cannot itself spawn. Instance flag, not a level check.</summary>
    public bool SpawnedByDeath = false;

    // RNG for foe-internal randomness (patrol start dir, spawn offsets). Golden rule is
    // DetRandom, but the arena's DetRandom plumbing lands in Phase 3 — for now this is a
    // single unseeded generator per foe. Phase 3 swaps in a seeded source by assigning
    // Rng (a settable property) before/right after spawn; nothing else changes.
    private RandomNumberGenerator _rng;
    public RandomNumberGenerator Rng
    {
        get { if (_rng == null) { _rng = new RandomNumberGenerator(); _rng.Randomize(); } return _rng; }
        set => _rng = value;
    }

    // ── Soft volume (go-inside), same contract as Enemy ─────────────────────────────
    private SoftVolume _softVolume;
    public void EnterSoftVolume(SoftVolume v) => _softVolume = v;
    public void ExitSoftVolume(SoftVolume v) { if (_softVolume == v) _softVolume = null; }

    // ── Hazard-driven statuses, identical semantics to CharacterController ──────────
    private readonly DecayingDebuff _fire = new();
    private readonly DecayingDebuff _frozenDebuff = new();
    private readonly Dictionary<string, float> _dealtDmgBonuses = new();
    private readonly Dictionary<string, float> _defenseBonuses = new();

    protected float DealtDamageMultiplier
    {
        get { float s = 0f; foreach (float v in _dealtDmgBonuses.Values) s += v; return 1f + s; }
    }
    /// <summary>Defense → damage-taken multiplier: dmg = 100/(100+defense).</summary>
    private float DefenseMultiplier
    {
        get { float d = 0f; foreach (float v in _defenseBonuses.Values) d += v;
              return 100f / (100f + Mathf.Max(-99f, d)); }
    }
    private float MoveMultiplier
    {
        get { float m = 1f; if (_fire.Active) m *= 1.2f; if (_frozenDebuff.Active) m *= 0.8f; return m; }
    }
    protected float CurrentMoveSpeed => MoveSpeed * MoveMultiplier;
    /// <summary>A base damage amount scaled by foe ATK and any active OnFire bonus.</summary>
    protected float EffectiveDamage(float baseAmount) => baseAmount * AtkMult * DealtDamageMultiplier;

    // ── Visuals ─────────────────────────────────────────────────────────────────────
    protected Sprite2D Sprite;
    private float _flashTimer;
    private float _blinkTimer;
    private float _evolveFlashTimer;
    private bool _telegraphActive;
    private Color _telegraphTint = Colors.White;

    // ── Runtime state ────────────────────────────────────────────────────────────────
    private bool _initialized;
    private bool _dead;
    private bool _spawnCaptured;
    protected Vector2 SpawnOrigin;
    protected float Dir = 1f;                    // intended movement direction
    protected float FacingDir = 1f;              // last non-zero movement dir (cone sight)
    protected Vector2 IntentVel;
    protected Vector2 ExternalVel;
    private readonly Dictionary<string, Vector2> _continuousExternalVelocity = new();
    private float _hp;
    private float _contactTimer;
    private float _stunTimer;
    private float _burnTimer;
    private float _burnAccum;
    private float _burnPopTimer;

    // Sight / aggro FSM (FOES §6)
    protected enum FoeState { Patrol, Aggro }
    protected FoeState State = FoeState.Patrol;
    private float _sightTimer;
    private int _missCount;

    // Skills (per-skill cooldowns; level gating via FoeStats)
    private float _skill1Cd;
    private float _skill2Cd;

    // Evolution (FOES §5)
    private float _age;
    private bool _evolved;

    protected bool Stunned => _stunTimer > 0f;
    protected bool IsAggro => State == FoeState.Aggro;
    protected bool CanAct => !Stunned && !_dead;
    protected bool Skill1Ready => FoeStats.HasSkill1(CurrentLevel) && _skill1Cd <= 0f;
    protected bool Skill2Ready => FoeStats.HasSkill2(CurrentLevel) && _skill2Cd <= 0f;
    protected void StartSkill1Cooldown(float cd) => _skill1Cd = cd;
    protected void StartSkill2Cooldown(float cd) => _skill2Cd = cd;
    protected void SetTelegraphTint(Color tint) { _telegraphActive = true; _telegraphTint = tint; }
    protected void ClearTelegraphTint() => _telegraphActive = false;

    /// <summary>Refresh a named continuous velocity contribution from a world field.
    /// Unlike impulse knockback, this does not decay; the field owns clearing its key.</summary>
    public void SetContinuousExternalVelocity(string source, Vector2 velocity)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        _continuousExternalVelocity[source] = velocity;
    }

    public void ClearContinuousExternalVelocity(string source)
    {
        if (!string.IsNullOrWhiteSpace(source)) _continuousExternalVelocity.Remove(source);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        Sprite = GetNode<Sprite2D>("Sprite2D");
        AddToGroup("enemy");   // existing GameManager cap counting
        AddToGroup("foe");
        InitFoe();
        Dir = Rng.Randf() < 0.5f ? -1f : 1f;
        FacingDir = Dir;
        // If no spawner calls Init (e.g. a foe placed directly in a scene), default to
        // level 1. A spawner's Init(level) right after AddChild overrides this.
        if (!_initialized) Init(1);
    }

    /// <summary>Override to set base stats, sight interval, gravity/contact config.</summary>
    protected virtual void InitFoe() { }

    /// <summary>Apply level multipliers once and store CurrentLevel. Call AFTER AddChild.</summary>
    public void Init(int level)
    {
        CurrentLevel = Mathf.Clamp(level, 1, 8);
        ApplyLevelStats();
        _hp = MaxHP;
        _age = 0f;
        _evolved = false;
        _initialized = true;
    }

    private void ApplyLevelStats()
    {
        FoeStats.LevelStats s = FoeStats.ForLevel(CurrentLevel);
        MaxHP = BaseHP * s.HpMult;
        MoveSpeed = BaseMoveSpeed * s.SpdMult;
        AtkMult = s.AtkMult;
    }

    /// <summary>Called once on the first physics frame after the spawner has placed the
    /// foe (so GlobalPosition is final). Override to capture spawn-relative anchors.</summary>
    protected virtual void OnSpawnPlaced() { }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized || _dead) return;      // guard un-Init'd first frame (caveat)
        float dt = (float)delta;

        if (!_spawnCaptured) { SpawnOrigin = GlobalPosition; _spawnCaptured = true; OnSpawnPlaced(); }

        TickTimers(dt);
        var player = GetTree().GetFirstNodeInGroup("player") as CharacterController;

        // 1. status decay (may Die)
        if (UpdateStatus(dt)) return;
        // 2. sight timer / FSM
        UpdateSight(dt, player);
        // 3. state behaviour (movement intent) + passive contact damage
        if (!Stunned)
        {
            if (State == FoeState.Aggro && player != null) UpdateAggro(dt, player);
            else UpdatePatrol(dt);
            if (HasContactDamage && player != null) TryContactDamage(player);
        }
        else IntentVel.X = 0f;                    // gain-no: no self-directed horizontal move
        // 4. skill gates (ongoing telegraph/dash must progress even while stunned)
        UpdateSkills(dt, player);
        // 5. movement integration
        Integrate(dt);
        // 6. evolution timer
        UpdateEvolution(dt);

        UpdateVisuals(dt);
    }

    private void TickTimers(float dt)
    {
        if (_contactTimer > 0f) _contactTimer -= dt;
        if (_stunTimer > 0f) _stunTimer -= dt;
        if (_skill1Cd > 0f) _skill1Cd -= dt;
        if (_skill2Cd > 0f) _skill2Cd -= dt;
    }

    // ── Status ──────────────────────────────────────────────────────────────────────
    private bool UpdateStatus(float dt)
    {
        if (_burnTimer > 0f)
        {
            _burnTimer -= dt;
            float d = BurnDamagePerSecond * dt * DefenseMultiplier;
            _hp -= d; _burnAccum += d; _burnPopTimer -= dt;
            if (_burnPopTimer <= 0f && _burnAccum >= 1f)
            {
                PopNumber(_burnAccum); _burnAccum = 0f; _burnPopTimer = 0.5f;
            }
            if (_hp <= 0f) { Die(); return true; }
        }

        float fireDmg = _fire.Tick(dt) * DefenseMultiplier;
        if (fireDmg > 0f) { _hp -= fireDmg; PopNumber(fireDmg); }
        float frozenDmg = _frozenDebuff.Tick(dt) * DefenseMultiplier;
        if (frozenDmg > 0f) { _hp -= frozenDmg; PopNumber(frozenDmg); }

        if (_fire.Active) _dealtDmgBonuses["OnFire"] = 0.2f; else _dealtDmgBonuses.Remove("OnFire");
        if (_frozenDebuff.Active) _defenseBonuses["Frozen"] = 30f; else _defenseBonuses.Remove("Frozen");

        if (_hp <= 0f) { Die(); return true; }
        return false;
    }

    // ── Sight / aggro FSM ─────────────────────────────────────────────────────────
    private void UpdateSight(float dt, CharacterController player)
    {
        if (!FoeStats.HasAggro(CurrentLevel)) { State = FoeState.Patrol; return; }

        _sightTimer -= dt;
        if (_sightTimer > 0f) return;
        _sightTimer = SightInterval;

        bool seen = player != null && CanSeePlayer(player);
        if (seen) { State = FoeState.Aggro; _missCount = 0; }   // hit → aggro, reset misses
        else if (State == FoeState.Aggro)                        // misses counted only while AGGRO
        {
            _missCount++;
            if (_missCount >= 3) { State = FoeState.Patrol; _missCount = 0; }
        }
    }

    /// <summary>Sight-shape test — subclass implements (crab rect / gull cone).</summary>
    protected virtual bool CanSeePlayer(CharacterController player) => false;

    // ── Movement (subclass sets intent; base integrates) ────────────────────────────
    protected virtual void UpdatePatrol(float dt) { }
    protected virtual void UpdateAggro(float dt, CharacterController player) { }
    protected virtual void UpdateSkills(float dt, CharacterController player) { }

    private void TryContactDamage(CharacterController player)
    {
        Vector2 to = player.GlobalPosition - GlobalPosition;
        if (to.Length() < ContactRange && _contactTimer <= 0f)
        {
            _contactTimer = ContactCooldown;
            Vector2 dir = new Vector2(Mathf.Sign(to.X == 0f ? 1f : to.X), -0.4f).Normalized();
            player.TakeHit(new HitInfo(EffectiveDamage(BaseDamage), dir * ContactKnockback, ContactStun));
        }
    }

    private void Integrate(float dt)
    {
        float damp = ExternalDamping * (_softVolume != null ? _softVolume.ExternalDampingMult : 1f);
        ExternalVel = ExternalVel.MoveToward(Vector2.Zero, damp * dt);

        if (UseGravity)
        {
            if (!IsOnFloor()) IntentVel.Y += Gravity * dt;
            else if (IntentVel.Y > 0f) IntentVel.Y = 0f;
        }
        // no-gravity foes: IntentVel is fully controlled by the subclass movement/skills.

        // Match the player contract: the volume slows existing velocity over time but
        // never replaces gravity, an aerial move, or a launched foe with a fixed field.
        if (_softVolume != null)
            IntentVel = _softVolume.ApplyVelocityResistance(IntentVel, CurrentMoveSpeed, dt);

        Vector2 continuous = Vector2.Zero;
        foreach (Vector2 value in _continuousExternalVelocity.Values) continuous += value;
        Velocity = IntentVel + ExternalVel + continuous;
        MoveAndSlide();

        if (IsOnFloor() && ExternalVel.Y > 0f) ExternalVel.Y = 0f;
        if (IsOnWall()) ExternalVel.X = 0f;

        if (Dir != 0f) Sprite.FlipH = Dir < 0f;
    }

    // ── Evolution (FOES §5) ─────────────────────────────────────────────────────────
    private void UpdateEvolution(float dt)
    {
        if (_evolved || CurrentLevel >= 8 || !CanEvolve) return;
        _age += dt;
        if (_age >= 25f) Evolve();
    }

    /// <summary>Additive Phase-3 hook: mission objectives (DestroyObjective) suppress the
    /// 25 s in-combat evolution by overriding this to false.</summary>
    protected virtual bool CanEvolve => true;

    private void Evolve()
    {
        _evolved = true;
        int oldLevel = CurrentLevel;
        float oldMult = FoeStats.ForLevel(oldLevel).HpMult;
        CurrentLevel = Mathf.Min(8, oldLevel + 1);
        float newMult = FoeStats.ForLevel(CurrentLevel).HpMult;

        _hp = EvolveHp(_hp, BaseHP, oldMult, newMult);   // ratio-preserved + 30% new-max heal
        FoeStats.LevelStats s = FoeStats.ForLevel(CurrentLevel);
        MaxHP = BaseHP * s.HpMult;
        MoveSpeed = BaseMoveSpeed * s.SpdMult;
        AtkMult = s.AtkMult;

        _evolveFlashTimer = 0.5f;   // brief tint tell (art deferred, FOES §5)
        OnEvolve();
    }

    /// <summary>Evolution HP (FOES §5, canonized): re-derive max from base, preserve the
    /// current-HP ratio into the new scale, then heal 30% of the new max, capped at it.
    /// Isolated + static so it is unit-testable.</summary>
    public static float EvolveHp(float hp, float baseHp, float oldMult, float newMult)
    {
        float newMax = baseHp * newMult;
        return Mathf.Min(newMax, hp * newMult / oldMult + 0.30f * newMax);
    }

    /// <summary>Override for a per-type evolution reaction (e.g. resize). Optional.</summary>
    protected virtual void OnEvolve() { }

    // ── Damage reception ────────────────────────────────────────────────────────────
    public void TakeDamage(float amount, Vector2 knockback) => TakeHit(new HitInfo(amount, knockback));

    /// <summary>Overload with no directional source — no cone-based passive applies.</summary>
    public void TakeHit(HitInfo hit) => TakeHit(hit, GlobalPosition);

    /// <summary>Apply an authored hit. <paramref name="sourcePos"/> feeds directional
    /// passives (crab Soft Shell). No i-frame gate (parity with the old Enemy).</summary>
    public float TakeHit(HitInfo hit, Vector2 sourcePos)
    {
        if (_dead || Invincible) return 0f;

        float dealt = hit.Damage * DefenseMultiplier * IncomingDamageMult(sourcePos);
        DebugManager.Instance?.LogPlayerDmgDealt(dealt, GetType().Name);
        _hp -= dealt;
        AddImpulse(hit.Knockback);
        float stun = hit.ResolveStun();
        if (stun > 0f) _stunTimer = Mathf.Max(_stunTimer, stun);

        _flashTimer = 0.12f;
        _blinkTimer = 0.24f;
        PopNumber(dealt);
        ShakeCamera2D.Instance?.AddTrauma(0.35f);

        // Getting hit alerts a sight-capable foe: force aggro + reset the miss counter.
        if (FoeStats.HasAggro(CurrentLevel)) { State = FoeState.Aggro; _missCount = 0; }

        // Single-player prototype: any damage a foe takes came from the player, so credit
        // "damage dealt" ult charge here for every source without threading a dealer ref.
        var player = CharacterController.LocalPlayer;
        player?.GainUltCharge(dealt * player.UltChargeDealtRate);

        if (_hp <= 0f) Die();
        return dealt;
    }

    /// <summary>Directional damage-taken multiplier (default none). Crab overrides for
    /// Soft Shell (2× from a source in the upward cone).</summary>
    protected virtual float IncomingDamageMult(Vector2 sourcePos) => 1f;

    /// <summary>Hazard tick: damage + knockback, no i-frame gate (see Hazard.cs).</summary>
    public void ApplyHazard(float damage, Vector2 knockback)
    {
        if (_dead || Invincible) return;
        if (damage > 0f) { float d = damage * DefenseMultiplier; DebugManager.Instance?.LogHazardDmg(d, GetType().Name); _hp -= d; PopNumber(d); }
        if (knockback != Vector2.Zero) AddImpulse(knockback);
        if (_hp <= 0f) Die();
    }

    public void AddFireStack(float amount) => _fire.AddStack(amount);
    public void AddFrozenStack(float amount) => _frozenDebuff.AddStack(amount);
    public void AddImpulse(Vector2 impulse) => ExternalVel += impulse;

    /// <summary>Remove this foe without awarding another damage event. Used only by
    /// authored execute effects after their normal damage has resolved.</summary>
    public void Execute()
    {
        if (_dead || Invincible) return;
        Die();
    }

    /// <summary>Kit-level burn DoT (distinct from the OnFire hazard debuff).</summary>
    public void SetBurning(float duration)
    {
        _burnTimer = Mathf.Max(_burnTimer, duration);
    }

    // ── Death ────────────────────────────────────────────────────────────────────────
    private void Die()
    {
        if (_dead) return;
        _dead = true;
        OnDeath();
        QueueFree();
    }

    /// <summary>Loot / on-death hook (FOES §7). Fired before QueueFree. Override to spawn
    /// babies (crab) or drop pickups, then call base.OnDeath().</summary>
    protected virtual void OnDeath()
    {
        // Loot content TBD — the architecture supports drops without knowing them yet.
    }

    // ── Visuals ────────────────────────────────────────────────────────────────────
    private void UpdateVisuals(float dt)
    {
        Color resting = RestingTint();
        if (_evolveFlashTimer > 0f)
        {
            _evolveFlashTimer -= dt;
            Sprite.Modulate = new Color(0.6f, 1f, 0.6f);
            if (_evolveFlashTimer <= 0f) Sprite.Modulate = resting;
        }
        else if (_flashTimer > 0f)
        {
            _flashTimer -= dt;
            Sprite.Modulate = new Color(1f, 0.6f, 0.6f);
            if (_flashTimer <= 0f) Sprite.Modulate = resting;
        }
        else Sprite.Modulate = resting;

        if (_blinkTimer > 0f)
        {
            _blinkTimer -= dt;
            Sprite.Visible = Mathf.PosMod(_blinkTimer, 0.08f) < 0.04f;
            if (_blinkTimer <= 0f) Sprite.Visible = true;
        }

        if (ShowDebugSight) QueueRedraw();
    }

    private Color RestingTint()
    {
        if (_telegraphActive) return _telegraphTint;
        if (_frozenDebuff.Active) return new Color(0.55f, 0.85f, 1f);
        if (_burnTimer > 0f || _fire.Active) return new Color(1f, 0.6f, 0.35f);
        return Colors.White;
    }

    private void PopNumber(float amount) =>
        DamageNumberManager.Instance?.Pop(GlobalPosition + new Vector2(0f, -28f), amount, false);

    // ── Debug sight drawing (mirrors CharacterController.ShowDebugRanges style) ──────
    public override void _Draw()
    {
        if (ShowDebugSight) DrawSight();
    }

    /// <summary>Override to draw the current sight shape in local space (FOES §3–4).</summary>
    protected virtual void DrawSight() { }
}

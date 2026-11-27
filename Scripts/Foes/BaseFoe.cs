using Godot;
using Fableland.Data;
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
///
/// Feedback (<c>Docs/Feedback.gdd</c>): a foe raises its own cues on <see cref="CueBus"/>
/// (spawn, aggro, evolve, telegraph, jump, land, death, statuses, one <see cref="HitCue"/>
/// per hit taken), holds still for hit-stop and owns one hit-flash channel. It never names
/// an effect, a sound or a shake.
/// </summary>
public partial class BaseFoe : CharacterBody2D, ICombatTarget, IFeedbackBody
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

    // ── ICombatTarget: a foe is a target on the Foe side (CombatTarget.cs) ──────────
    public CombatSide Side => CombatSide.Foe;
    float ICombatTarget.HitRadius => HitRadius;
    public ulong TargetId => GetInstanceId();
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
    // Every volume this foe overlaps, and what they add up to (same contract as the player).
    private readonly SoftVolumeSet _softVolumes = new();
    private const string SoftVolumeFlowSource = "softvolume.flow";
    public void EnterSoftVolume(SoftVolume v)
    {
        bool wasInside = _softVolumes.InMedium;
        _softVolumes.Enter(v);
        if (!wasInside && _softVolumes.InMedium) SoftVolume.RaiseCrossing(this, v, entered: true);
    }

    public void ExitSoftVolume(SoftVolume v)
    {
        bool wasInside = _softVolumes.InMedium;
        _softVolumes.Exit(v);
        if (wasInside && !_softVolumes.InMedium) SoftVolume.RaiseCrossing(this, v, entered: false);
    }

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
    private float _evolveFlashTimer;
    private bool _telegraphActive;
    private Color _telegraphTint = Colors.White;

    // ── Feedback (Docs/Feedback.gdd): hit-stop, merged hurt ticks, status loops, hit flash ──
    private HitStopClock _hitStop;
    private readonly HurtTickMeter _hurtTicks = new();
    private readonly BodyFlash _flash = new();
    private FeedbackState _statusLoops;
    private string _feedbackId;
    private bool _wasOnFloor;

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
    private float _trappedTimer;
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
    public bool Trapped => _trappedTimer > 0f;
    protected bool IsAggro => State == FoeState.Aggro;
    protected bool CanAct => !Stunned && !_dead;
    protected bool Skill1Ready => FoeStats.HasSkill1(CurrentLevel) && _skill1Cd <= 0f;
    protected bool Skill2Ready => FoeStats.HasSkill2(CurrentLevel) && _skill2Cd <= 0f;
    protected void StartSkill1Cooldown(float cd) => _skill1Cd = cd;
    protected void StartSkill2Cooldown(float cd) => _skill2Cd = cd;

    /// <summary>Show the "I am about to attack" tint and start the <c>telegraph</c> loop.
    /// Give it the tell's length and the attack's real reach and direction when it has them,
    /// so whatever is drawn for it comes from the numbers the attack itself uses.</summary>
    protected void SetTelegraphTint(Color tint, float seconds = 0f, float reach = 0f, Vector2 direction = default)
    {
        if (!_telegraphActive) CueBus.Start(this, "telegraph", size: reach, magnitude: seconds, direction: direction);
        _telegraphActive = true;
        _telegraphTint = tint;
    }

    protected void ClearTelegraphTint()
    {
        if (_telegraphActive) CueBus.Stop(this, "telegraph");
        _telegraphActive = false;
    }

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

    // Rules of a place (Docs/Zone6.gdd §7): source-keyed shares of gravity, multiplied —
    // the Void Frontier's ladder, a realm law that binds foes. Set by orchestration only.
    private readonly Dictionary<string, float> _gravityScales = new();

    /// <summary>Share of gravity this foe feels (all sources multiplied; 1 when none).</summary>
    public float GravityScale
    {
        get { float m = 1f; foreach (float v in _gravityScales.Values) m *= v; return m; }
    }

    public void SetGravityScale(string source, float scale)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        if (Mathf.IsEqualApprox(scale, 1f)) _gravityScales.Remove(source);
        else _gravityScales[source] = Mathf.Max(0f, scale);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        Sprite = GetNode<Sprite2D>("Sprite2D");
        AddToGroup("enemy");   // existing GameManager cap counting
        AddToGroup("foe");
        InitFoe();
        // A walking foe stays on a slope instead of stepping off it every tick (Units.cs).
        if (UseGravity) FloorSnapLength = Mathf.Max(FloorSnapLength, Units.FloorSnapPx);
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

        if (!_spawnCaptured)
        {
            SpawnOrigin = GlobalPosition;
            _spawnCaptured = true;
            OnSpawnPlaced();
            CueBus.Emit(this, "spawn", variant: CurrentLevel, tag: SpawnedByDeath ? "born" : null);
        }

        // Hit-stop, counted in world time. A held foe keeps every clock running — timers,
        // statuses, sight, evolution — and holds what moves it: behaviour, skills (a dash
        // keeps its full length) and integration, so it resumes with the velocity it had.
        bool held = _hitStop.Tick(dt);
        _flash.Tick(dt);
        _hurtTicks.Tick(dt);

        TickTimers(dt);
        var player = GetTree().GetFirstNodeInGroup("player") as CharacterController;

        // 1. status decay (may Die)
        if (UpdateStatus(dt)) return;
        // 2. sight timer / FSM
        UpdateSight(dt, player);
        if (!held)
        {
            // 3. state behaviour (movement intent) + passive contact damage
            if (!Stunned && !Trapped)
            {
                if (State == FoeState.Aggro && player != null) UpdateAggro(dt, player);
                else UpdatePatrol(dt);
                if (HasContactDamage && player != null) TryContactDamage(player);
            }
            else IntentVel.X = 0f;                    // gain-no/root: no self-directed horizontal move
            // 4. skill gates (ongoing telegraph/dash must progress even while stunned)
            UpdateSkills(dt, player);
            // 5. movement integration
            Integrate(dt);
        }
        else Velocity = Vector2.Zero;   // held: not moving; IntentVel / ExternalVel are kept
        // 6. evolution timer
        UpdateEvolution(dt);

        _statusLoops = StatusLoops.Sync(this, _statusLoops, StatusState());
        UpdateVisuals(dt);
    }

    public override void _ExitTree() => StopLoops();

    /// <summary>End every loop this foe started on the cue bus (death, or leaving the tree).</summary>
    private void StopLoops()
    {
        _statusLoops = StatusLoops.Sync(this, _statusLoops, FeedbackState.None);
        ClearTelegraphTint();
        _flash.Clear();
    }

    private void TickTimers(float dt)
    {
        if (_contactTimer > 0f) _contactTimer -= dt;
        if (_stunTimer > 0f) _stunTimer -= dt;
        if (_trappedTimer > 0f) _trappedTimer -= dt;
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
            _hurtTicks.Add(this, HurtSource.Burn, d);
            _hp -= d; _burnAccum += d; _burnPopTimer -= dt;
            if (_burnPopTimer <= 0f && _burnAccum >= 1f)
            {
                PopNumber(_burnAccum); _burnAccum = 0f; _burnPopTimer = 0.5f;
            }
            if (_hp <= 0f) { Die(); return true; }
        }

        float fireDmg = _fire.Tick(dt) * DefenseMultiplier;
        if (fireDmg > 0f) { _hp -= fireDmg; PopNumber(fireDmg); _hurtTicks.Add(this, HurtSource.OnFire, fireDmg); }
        float frozenDmg = _frozenDebuff.Tick(dt) * DefenseMultiplier;
        if (frozenDmg > 0f) { _hp -= frozenDmg; PopNumber(frozenDmg); _hurtTicks.Add(this, HurtSource.Frozen, frozenDmg); }

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
        if (seen) { EnterAggro(); _missCount = 0; }             // hit → aggro, reset misses
        else if (State == FoeState.Aggro)                        // misses counted only while AGGRO
        {
            _missCount++;
            if (_missCount >= 3) { State = FoeState.Patrol; _missCount = 0; }
        }
    }

    /// <summary>Turn on the player. Raises <c>aggro</c> once, on the change — not on every
    /// sight check that confirms it.</summary>
    private void EnterAggro()
    {
        if (State == FoeState.Aggro) return;
        State = FoeState.Aggro;
        CueBus.Emit(this, "aggro", direction: new Vector2(FacingDir, 0f));
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
            Strike(player, new HitInfo(EffectiveDamage(BaseDamage), dir * ContactKnockback, ContactStun)
                .From(this, "contact"));
        }
    }

    /// <summary>Land one of this foe's own blows: the hit, from where the foe stands, and —
    /// for a blow of its body, not a projectile — the foe's share of hit-stop for it.
    /// Returns the damage dealt.</summary>
    protected float Strike(CharacterController target, HitInfo hit)
    {
        if (target == null) return 0f;
        float dealt = target.TakeHit(hit, GlobalPosition);
        if (dealt > 0f && !hit.Ranged)
        {
            float score = FeedbackTable.ImpactScore(dealt, target.Side == CombatSide.Player, target.MaxHP);
            ApplyHitStop(FeedbackTable.AttackerStop(FeedbackTable.Resolve(hit.Weight, score), score,
                target.IsDefeated, FeedbackSettings.HitStop));
        }
        return dealt;
    }

    private void Integrate(float dt)
    {
        _softVolumes.Prune();
        if (_softVolumes.Flow != Vector2.Zero) SetContinuousExternalVelocity(SoftVolumeFlowSource, _softVolumes.Flow);
        else ClearContinuousExternalVelocity(SoftVolumeFlowSource);

        float damp = ExternalDamping * _softVolumes.ExternalDampingMult;
        ExternalVel = ExternalVel.MoveToward(Vector2.Zero, damp * dt);

        if (UseGravity)
        {
            // a buoyant medium leaves only part of gravity. Foes are carried passively by a
            // soft volume; a fighter swims in one instead (SoftVolume.cs)
            if (!IsOnFloor()) IntentVel.Y += Gravity * GravityScale * _softVolumes.GravityScale * dt;
            else if (IntentVel.Y > 0f) IntentVel.Y = 0f;
        }
        // no-gravity foes: IntentVel is fully controlled by the subclass movement/skills.

        // The passive contract: the volume slows existing velocity over time but never
        // replaces gravity, an aerial move, or a launched foe with a fixed field.
        IntentVel = _softVolumes.Resist(IntentVel, CurrentMoveSpeed, dt);

        Vector2 continuous = Vector2.Zero;
        foreach (Vector2 value in _continuousExternalVelocity.Values) continuous += value;
        Velocity = IntentVel + ExternalVel + continuous;
        float fallSpeed = Velocity.Y;
        // Leaving the floor on its own upward intent is a jump (knockback is ExternalVel).
        bool leaping = UseGravity && _wasOnFloor && IntentVel.Y <= -FeedbackTable.FoeJumpCueMinSpeed;
        MoveAndSlide();

        bool onFloor = IsOnFloor();
        if (UseGravity)
        {
            if (leaping && !onFloor)
                CueBus.Emit(this, "jump", variant: 1, magnitude: -IntentVel.Y, direction: Vector2.Up, at: Feet);
            else if (onFloor && !_wasOnFloor && fallSpeed >= FeedbackTable.LandCueMinSpeed)
                CueBus.Emit(this, "land", magnitude: fallSpeed, direction: Vector2.Down, at: Feet);
        }
        _wasOnFloor = onFloor;

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
        CueBus.Emit(this, "evolve", variant: CurrentLevel);
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
        Vector2 knockback = Trapped ? Vector2.Zero : hit.Knockback;
        AddImpulse(knockback);
        float stun = hit.ResolveStun();
        if (stun > 0f) _stunTimer = Mathf.Max(_stunTimer, stun);

        PopNumber(dealt);

        // Getting hit alerts a sight-capable foe: force aggro + reset the miss counter.
        if (FoeStats.HasAggro(CurrentLevel)) { EnterAggro(); _missCount = 0; }

        // Single-player prototype: any damage a foe takes came from the player, so credit
        // "damage dealt" ult charge here for every source without threading a dealer ref.
        var player = CharacterController.LocalPlayer;
        player?.GainUltCharge(dealt * player.UltChargeDealtRate);

        // Feedback (T10 §4 stage 6): resolve how heavy the hit was, hold this foe for it, and
        // raise ONE cue — flash, shake and the rest are a listener's business. A foe that
        // dies is freed this frame, so it is not held.
        bool killed = _hp <= 0f;
        float score = FeedbackTable.ImpactScore(dealt, false, MaxHP);
        HitWeight weight = FeedbackTable.Resolve(hit.Weight, score);
        float held = killed ? 0f
            : ApplyHitStop(FeedbackTable.VictimStop(weight, score, false, FeedbackSettings.HitStop));
        CueBus.EmitHit(new HitCue(this, hit, weight, dealt, knockback, sourcePos, killed, held));

        if (killed) Die();
        return dealt;
    }

    /// <summary>Directional damage-taken multiplier (default none). Crab overrides for
    /// Soft Shell (2× from a source in the upward cone).</summary>
    protected virtual float IncomingDamageMult(Vector2 sourcePos) => 1f;

    /// <summary>Hazard tick: damage + knockback, no i-frame gate (see Hazard.cs).</summary>
    public void ApplyHazard(float damage, Vector2 knockback)
    {
        if (_dead || Invincible) return;
        if (damage > 0f)
        {
            float d = damage * DefenseMultiplier;
            DebugManager.Instance?.LogHazardDmg(d, GetType().Name);
            _hp -= d;
            PopNumber(d);
            _hurtTicks.Add(this, HurtSource.Hazard, d);   // a tick stops nobody
        }
        if (knockback != Vector2.Zero) AddImpulse(knockback);
        if (_hp <= 0f) Die();
    }

    public void AddFireStack(float amount) => _fire.AddStack(amount);
    public void AddFrozenStack(float amount) => _frozenDebuff.AddStack(amount);
    /// <summary>Canonical Trapped root: movement is locked and new knockback is ignored,
    /// while the foe's skill logic may still run and gravity remains active.</summary>
    public void ApplyTrapped(float duration)
    {
        if (_dead || Invincible || duration <= 0f) return;
        _trappedTimer = Mathf.Max(_trappedTimer, duration);
        ExternalVel.X = 0f;
    }

    public void AddImpulse(Vector2 impulse)
    {
        if (Trapped) return;
        ExternalVel += impulse;
    }

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
        _hitStop.Clear();
        StopLoops();
        CueBus.Emit(this, "death", variant: CurrentLevel);
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
        // Sprite.Modulate is this class's one tint channel: the evolution tell over the
        // resting tint (telegraph, frozen, burning). The hit flash is a separate channel
        // (FlashTint), and a foe no longer blinks when hit — blinking means "cannot be hit",
        // and a foe has no such window (Docs/Feedback.gdd §5).
        Color resting = RestingTint();
        if (_evolveFlashTimer > 0f)
        {
            _evolveFlashTimer -= dt;
            Sprite.Modulate = _evolveFlashTimer > 0f ? new Color(0.6f, 1f, 0.6f) : resting;
        }
        else Sprite.Modulate = resting;

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

    // ── Feedback: identity, hit-stop, flash (Docs/Feedback.gdd §2, §4, §5) ───────────

    /// <summary>The content id this foe's cues are raised under — snake_case. Defaults to
    /// the class name; each foe states its own ("crab", "seagull").</summary>
    public virtual string FeedbackId => _feedbackId ??= FeedbackIds.FromTypeName(GetType().Name);

    /// <summary>What this body's cues fall back to: <c>foe</c>; a mission object that only
    /// takes hits overrides it to <c>objective</c>.</summary>
    public virtual string FeedbackClass => CueOwners.Foe;

    public Vector2 FeedbackCenter => GlobalPosition;
    public float FeedbackRadius => HitRadius;
    /// <summary>Where this foe's feet are, in world space.</summary>
    public Vector2 Feet => GlobalPosition + new Vector2(0f, FootOffset * Scale.Y);

    /// <summary>True while hit-stop holds this foe.</summary>
    public bool InHitStop => _hitStop.Active;

    public FeedbackState FeedbackState
    {
        get
        {
            if (_dead) return FeedbackState.Dead;
            FeedbackState state = StatusState();
            if (_hp <= MaxHP * FeedbackTable.LowHpRatio) state |= FeedbackState.LowHp;
            if (UseGravity && !IsOnFloor()) state |= FeedbackState.Airborne;
            if (_softVolumes.InMedium) state |= FeedbackState.Submerged;
            if (Invincible) state |= FeedbackState.Invulnerable;
            return state;
        }
    }

    private FeedbackState StatusState()
    {
        FeedbackState state = FeedbackState.None;
        if (_burnTimer > 0f) state |= FeedbackState.Burning;
        if (_fire.Active) state |= FeedbackState.OnFire;
        if (_frozenDebuff.Active) state |= FeedbackState.Frozen;
        if (_trappedTimer > 0f) state |= FeedbackState.Trapped;
        if (_stunTimer > 0f) state |= FeedbackState.Stunned;
        return state;
    }

    /// <summary>Hold this foe still for a moment — hit-stop. GAMEPLAY: only the foe itself
    /// (its hit intake, its own blows) may call it, never a cue listener. Returns the seconds
    /// it is held from now (0 if it is free). Rules: <see cref="HitStopClock"/>.</summary>
    public float ApplyHitStop(float seconds) => _dead ? 0f : _hitStop.Request(seconds);

    /// <summary>Show the whole foe in one colour for a moment. It does not touch
    /// <c>Sprite.Modulate</c> (the telegraph, status and evolution tints) or <c>Visible</c>.</summary>
    public void FlashTint(Color tint, float seconds)
    {
        if (!_dead) _flash.Flash(this, tint, seconds);
    }

    // ── Debug sight drawing (mirrors CharacterController.ShowDebugRanges style) ──────
    public override void _Draw()
    {
        if (ShowDebugSight) DrawSight();
    }

    /// <summary>Override to draw the current sight shape in local space (FOES §3–4).</summary>
    protected virtual void DrawSight() { }
}

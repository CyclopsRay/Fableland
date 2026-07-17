using Godot;
using System;
using System.Collections.Generic;
using Fableland.Debug;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// Base class for playable characters — a Godot port of the Unity
/// CharacterController, trimmed to what the prototype needs and structured so
/// individual characters (Pomegraknight, and later Pixolotl/PumpKing/Cleopastar)
/// subclass it and override the ability hooks.
///
/// Provides: movement + double-jump (with coyote time), HP/lives, i-frames,
/// knockback, death/respawn, a generic Burning status (DoT for non-immune
/// targets; a passive trigger for those who are), the hazard-driven OnFire/Frozen
/// statuses (stackable, self-decaying — see <see cref="DecayingDebuff"/>), a
/// post-swing move-speed penalty, a melee cone helper, and a debug-draw hook that
/// subclasses use to visualize skill ranges.
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
	[Export] public float MaxUltCharge = 1500f;
	[Export] public float UltChargeDealtRate = 1f;     // charge per point of damage dealt
	[Export] public float UltChargeReceivedRate = 2f;  // charge per point of damage received

	[ExportGroup("Movement")]
	[Export] public float MoveSpeed = 320f;              // ~10 m/s target
	[Export] public float JumpVelocity = -Units.JumpSpeed; // -1024 px/s (8 m jump)
	[Export] public float Gravity = Units.Gravity;         // 2048 px/s²
	[Export] public int MaxJumps = 2;                      // jumps before touching down again
	[Export] public float JumpCooldown = 0.3f;             // universal min gap between jumps
	[Export] public float CoyoteTime = 0.2f;               // grace window after leaving a surface

	[ExportGroup("Momentum")]
	[Export] public float GroundAccel = 3200f;   // intent horizontal accel on ground
	[Export] public float AirAccel = 1900f;      // …in the air
	[Export] public float GroundFriction = 3000f;// intent decel when no input, grounded
	[Export] public float AirFriction = 700f;    // …in the air
	[Export] public float ExternalDamping = 900f;// px/s² decay of external (knockback) velocity

	[ExportGroup("Debug")]
	[Export] public bool ShowDebugRanges = true;

	// Events — GameManager/Hud subscribe to drive HUD and the life flow.
	public event Action<float, float, float, float> HpChanged;   // (curHP, maxHP, shield, tempHP)
	public event Action<float, float> UltChargeChanged; // (current, max)
	public event Action Died;
	/// <summary>Post-mitigation direct enemy damage that reached normal HP after shields/temp HP.
	/// Item reactions subscribe while held; hazards and DoT deliberately do not raise it.</summary>
	public event Action<float> DirectDamageTaken;
	/// <summary>Post-mitigation damage dealt by a melee-cone hit. The foe parameter identifies the
	/// target so a held reaction can bounce damage without re-triggering the melee event.</summary>
	public event Action<BaseFoe, float> MeleeDamageDealt;
	/// <summary>Post-mitigation damage caused by this player. Projectiles and area skills report
	/// through <see cref="ReportDamageDealt"/> so omni-vamp-style held items are not melee-only.</summary>
	public event Action<float> DamageDealt;

	// Single-player prototype: lets Enemy credit ult charge for damage dealt
	// without needing a reference back to the player (mirrors ShakeCamera2D.Instance).
	public static CharacterController LocalPlayer { get; private set; }

	public float CurrentHP { get; private set; }
	public bool HasFrozenStatus => _frozenDebuff.Active;
	public bool HasOnFireStatus => _fire.Active;
	public Vector2 FacingDirection => new(Facing, 0f);

	/// <summary>Subclass hook: expose a shield pool for the HUD. Base returns 0;
	/// characters with a shield (e.g. PumpKing) override this.</summary>
	public virtual float Shield => 0f;

	/// <summary>Temporary HP granted by items/buffs. Absorbed before normal HP
	/// when taking damage. Shown in green on the HP bar.</summary>
	public float TempHP { get; protected set; }
	public float UltCharge { get; private set; }
	public int LivesRemaining { get; private set; }
	public bool ControlsLocked { get; set; }
	/// <summary>Combat-side damage gate used for terminal mission resolution. Unlike
	/// the short post-hit i-frame timer, this also suppresses hazards and DoT.</summary>
	public bool Invincible { get; set; }

	/// <summary>Per-character cooldown state for the HUD's skill icons. Base has
	/// nothing to report; characters with a Shift/E override these.</summary>
	public virtual (float Remaining, float Max) ShiftCooldown => (0f, 0f);
	public virtual (float Remaining, float Max) ESkillCooldown => (0f, 0f);

	// Frozen = no self-directed action (control lock, death, or a gain-no window).
	private bool Frozen => ControlsLocked || _dead || _stunTimer > 0f;

	// Facing: +1 right, -1 left. Local-space, so debug draws and fire points read from it.
	protected float Facing = 1f;

	// Status shared across characters.
	protected bool IsBurning;
	protected bool BurnImmune;             // set in InitCharacter (Pomegraknight = true)
	protected float BurnDotPerSecond = 12f;

	/// <summary>1 + sum of active damage-dealt bonuses (e.g. OnFire → +0.2). Multiple
	/// sources add rather than multiply, so a 0.2 and a 0.3 bonus together give 0.5.</summary>
	protected float DamageDealtMultiplier
	{
		get
		{
			float sum = 0f;
			foreach (float v in _dealtDmgBonuses.Values) sum += v;
			return 1f + sum;
		}
	}

	/// <summary>Defense → damage-taken multiplier: dmg = 100/(100+defense). Aggregatable
	/// by source (e.g. Frozen → +30); base is 0, so no status means no mitigation.</summary>
	private float DefenseMultiplier
	{
		get
		{
			float defense = 0f;
			foreach (float v in _defenseBonuses.Values) defense += v;
			return 100f / (100f + Mathf.Max(-99f, defense));
		}
	}

	// OnFire's/Frozen's move-speed change. Multiplicative (not aggregated) since
	// only these two named statuses ever touch it. Only MoveSpeed is scaled —
	// accel/friction are left alone so this doesn't complicate momentum tuning.
	private float MoveMultiplier
	{
		get
		{
			float m = 1f;
			if (_fire.Active) m *= 1.2f;
			if (_frozenDebuff.Active) m *= 0.8f;
			return m;
		}
	}

	protected Sprite2D Sprite;
	protected AnimationPlayer Anim;        // may be null until animations are authored
	protected Node2D FirePoint;            // optional projectile origin
	protected ShakeCamera2D CameraShake;   // optional; present on the player

	// Hazard-driven statuses (distinct from the kit-level Burn status above):
	// OnFire boosts move + damage dealt while decaying itself as damage; Frozen
	// slows + grants defense the same way. Both are stackable "points" (max 99).
	private readonly DecayingDebuff _fire = new();
	private readonly DecayingDebuff _frozenDebuff = new();
	// Aggregatable damage-dealt bonuses by source name (e.g. "OnFire" → +0.2);
	// total multiplier = 1 + sum of all active entries.
	private readonly Dictionary<string, float> _dealtDmgBonuses = new();
	// Aggregatable defense bonuses by source name (e.g. "Frozen" → +30).
	private readonly Dictionary<string, float> _defenseBonuses = new();
	private readonly Dictionary<string, float> _fireDurationExtensions = new();
	private readonly Dictionary<string, float> _frozenDurationExtensions = new();
	// Continuous environmental movement contributions (e.g. Cleopastar Blackholes).
	// Unlike _externalVel impulses, these are source-keyed target velocities and do not
	// decay. The owning field refreshes/clears its own key every physics tick.
	private readonly Dictionary<string, Vector2> _continuousExternalVelocity = new();
	protected Vector2 ContinuousExternalVelocity
	{
		get
		{
			Vector2 total = Vector2.Zero;
			foreach (Vector2 value in _continuousExternalVelocity.Values) total += value;
			return total;
		}
	}

	/// <summary>Shared basic-attack magazine. Characters configure it in InitCharacter;
	/// the base owns ticking, persistence, reset, and item-facing speed modifiers.</summary>
	protected AmmoController Ammo { get; private set; }
	// The active scene node is a view of this run-state record. Keeping the binding lets
	// a projectile that outlives a Tab switch persist a just-triggered reload safely.
	private ProtagonistState _boundAmmoState;

	private float _burnTimer;
	private float _speedPenaltyTimer;
	private float _speedPenaltyMult = 1f;
	private float _invulnTimer;
	private int _jumpsRemaining;
	private float _airborneTimer;      // time since last touching a surface
	private bool _coyoteConsumed;      // whether the coyote-time jump loss already fired
	private bool _jumpedSinceGrounded; // whether a manual jump has used the grace window
	private bool _dead;
	protected bool Dead => _dead;
	private bool _dropping;
	private float _dropReleaseTimer;
	private float _jumpCdTimer;
	private float _stunTimer;          // gain-no window: no control, animation frozen
	private SoftVolume _softVolume;   // non-null while inside a go-inside volume
	private Vector2 _intentVel;        // self-directed velocity (input/gravity/jump/field)
	private Vector2 _externalVel;      // impulses (knockback/wind/…); decays over time
	private Vector2 _spawnPoint;
	private Color _baseTint = Colors.White;

	public override void _EnterTree() => LocalPlayer = this;
	public override void _ExitTree() { if (LocalPlayer == this) LocalPlayer = null; }

	public override void _Ready()
	{
		Sprite = GetNode<Sprite2D>("Sprite2D");
		Anim = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");   // room for animation
		FirePoint = GetNodeOrNull<Node2D>("FirePoint");
		CameraShake = GetNodeOrNull<ShakeCamera2D>("Camera2D");
		_spawnPoint = GlobalPosition;

		CurrentHP = MaxHP;
		LivesRemaining = MaxLives;
		AddToGroup("player");

		InitCharacter();
		_jumpsRemaining = MaxJumps;
		NotifyHpChanged();
		UltChargeChanged?.Invoke(UltCharge, MaxUltCharge);
	}

	/// <summary>Override for per-character stats, immunities, ammo config.</summary>
	protected virtual void InitCharacter() { }

	/// <summary>Configure this character's one shared BA magazine from pure balance data.</summary>
	protected void ConfigureAmmo(string characterId)
	{
		if (!CharacterTable.TryGetAmmo(characterId, out CharacterAmmoDef def))
		{
			GD.PushError($"CharacterController: no ammo definition for '{characterId}'.");
			return;
		}
		Ammo = new AmmoController(def);
		Ammo.ReloadCompleted += OnAmmoReloaded;
	}

	/// <summary>Subclass hook for visual/combo state that changes when a reload completes.</summary>
	protected virtual void OnAmmoReloaded(int restored) { }

	protected bool TryConsumeAmmo(int amount = 1)
	{
		bool consumed = Ammo != null && Ammo.TryConsume(amount);
		if (consumed) Ammo.Save(_boundAmmoState);
		return consumed;
	}
	protected void StartAmmoAttackInterval()
	{
		Ammo?.StartAttackInterval();
		Ammo?.Save(_boundAmmoState);
	}
	protected void RequestAmmoReload()
	{
		Ammo?.RequestReload();
		Ammo?.Save(_boundAmmoState);
	}
	protected void SetAttackIntervalMultiplier(string source, float multiplier) => Ammo?.SetAttackIntervalMultiplier(source, multiplier);
	protected void ClearAttackIntervalMultiplier(string source) => Ammo?.ClearAttackIntervalMultiplier(source);

	/// <summary>Set or refresh a named continuous environmental velocity source. Multiple
	/// sources add; callers must clear their key when their field no longer affects us.</summary>
	public void SetContinuousExternalVelocity(string source, Vector2 velocity)
	{
		if (string.IsNullOrWhiteSpace(source)) return;
		_continuousExternalVelocity[source] = velocity;
	}

	public void ClearContinuousExternalVelocity(string source)
	{
		if (!string.IsNullOrWhiteSpace(source)) _continuousExternalVelocity.Remove(source);
	}

	/// <summary>
	/// Hydrate the live character from its run-state (T30 §1) — called by GameManager after
	/// spawn. MaxHP scales by the permanent additive max-HP percentage points; current HP is
	/// the carried ratio; run ATK/DEF fold into the existing aggregatable source dictionaries
	/// keyed "run:atk" / "run:def" (ATK → +BonusAtk/100 damage-dealt, DEF → flat defense).
	/// Additive Phase-3 hook. Purely additive to the status pools, so OnFire/Frozen still stack.
	/// </summary>
	public void HydrateRun(float baseMaxHp, int maxHpPercentPoints, float hpRatio, int bonusAtk, int bonusDef)
	{
		MaxHP = baseMaxHp * (1f + maxHpPercentPoints / 100f);
		CurrentHP = Mathf.Clamp(hpRatio, 0f, 1f) * MaxHP;
		if (bonusAtk != 0) _dealtDmgBonuses["run:atk"] = bonusAtk / 100f;
		if (bonusDef != 0) _defenseBonuses["run:def"] = bonusDef;
			DebugManager.Instance?.LogSystem($"HydrateRun HP%+{maxHpPercentPoints} ATK+{bonusAtk} DEF+{bonusDef} hpRatio={hpRatio:F2}");
					NotifyHpChanged();
	}

	/// <summary>Current HP as a fraction of max — written back to ProtagonistState on scene exit.</summary>
	public float HpRatio => MaxHP > 0f ? CurrentHP / MaxHP : 0f;

	/// <summary>Write this character's current HP ratio back to a <see cref="ProtagonistState"/>
	/// (the run copy). Used both on scene exit and on mid-combat protagonist switch (NODES §3.3).</summary>
	public void WriteBackToState(ProtagonistState p)
	{
		if (p == null) return;
		p.HpRatio = HpRatio;
		Ammo?.Save(p);
	}

	/// <summary>Restore persisted BA ammo after InitCharacter has configured it.</summary>
	public void LoadAmmoFromState(ProtagonistState p)
	{
		_boundAmmoState = p;
		Ammo?.Load(p);
	}

	/// <summary>Save this character's current skill cooldown remaining times to a
	/// <see cref="ProtagonistState"/> (called on switch-out). The base has no skills —
	/// subclasses with Shift/E cooldowns override this.</summary>
	public virtual void SaveCooldownsToState(ProtagonistState p)
	{
		if (p == null) return;
		p.ShiftCdRemaining = 0f;
		p.ESkillCdRemaining = 0f;
	}

	/// <summary>Restore this character's skill cooldown remaining times from a
	/// <see cref="ProtagonistState"/> (called on switch-in, after HydrateRun). The base
	/// has no skills — subclasses with Shift/E cooldowns override this.</summary>
	public virtual void LoadCooldownsFromState(ProtagonistState p)
	{
		// No-op in the base — no skill cooldowns to restore.
	}

	/// <summary>Copy the internal velocity channels from another character so the incoming
	/// protagonist continues the outgoing one's trajectory (mid-air switch — NODES §3.3).
	/// Must be called while <paramref name="other"/> is still alive (before QueueFree).</summary>
	public void InheritVelocityFrom(CharacterController other)
	{
		if (other == null) return;
		_intentVel = other._intentVel;
		_externalVel = other._externalVel;
		Velocity = _intentVel + _externalVel;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_dead) { if (ShowDebugRanges) QueueRedraw(); return; }

		UpdateTimers(dt);
		Ammo?.Tick(dt);
		UpdateStatus(dt);
		if (!Frozen) HandleAbilities();

		// Gain-no canon: the animation itself freezes on the current frame during
		// a stun window (knockback/gravity still move the body). Idempotent, so
		// it also auto-restores the instant the stun timer clears — and Die()
		// zeroes _stunTimer, so the death animation is never left frozen.
		if (Anim != null) Anim.SpeedScale = _stunTimer > 0f ? 0f : 1f;
		if (_stunTimer <= 0f) UpdateAnimator(dt);   // animation frozen during gain-no

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
		if (_stunTimer > 0f) _stunTimer -= dt;
	}

	private void UpdateStatus(float dt)
	{
		if (IsBurning)
		{
			_burnTimer -= dt;
			if (!BurnImmune) ApplyDotDamage(BurnDotPerSecond * dt);
			if (_burnTimer <= 0f) IsBurning = false;
		}

		float fireDmg = _fire.Tick(dt);
		if (fireDmg > 0f) ApplyDotDamage(fireDmg);
		float frozenDmg = _frozenDebuff.Tick(dt);
		if (frozenDmg > 0f) ApplyDotDamage(frozenDmg);

		if (_fire.Active) _dealtDmgBonuses["OnFire"] = 0.2f;
		else _dealtDmgBonuses.Remove("OnFire");

		if (_frozenDebuff.Active) _defenseBonuses["Frozen"] = 30f;
		else _defenseBonuses.Remove("Frozen");

		UpdateStatusTint();
	}

	private void UpdateStatusTint()
	{
		if (_frozenDebuff.Active) Sprite.SelfModulate = new Color(0.55f, 0.85f, 1f);
		else if (IsBurning || _fire.Active) Sprite.SelfModulate = new Color(1f, 0.55f, 0.35f);
		else Sprite.SelfModulate = _baseTint;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		if (_jumpCdTimer > 0f) _jumpCdTimer -= dt;

		// Vertical intent: gravity accumulates (momentum); jump sets it.
		if (!IsOnFloor())
		{
			_intentVel.Y += Gravity * dt;

			// Coyote time: leaving a surface doesn't cost a jump for CoyoteTime
			// seconds (so you can still edge-jump), but if that window elapses
			// without a manual jump, one jump is forfeited — this is what stops
			// a 1-jump character from jumping anytime mid-air after walking off
			// a ledge, while still allowing the edge jump itself.
			_airborneTimer += dt;
			if (!_jumpedSinceGrounded && !_coyoteConsumed && _airborneTimer >= CoyoteTime)
			{
				_coyoteConsumed = true;
				_jumpsRemaining = Mathf.Max(0, _jumpsRemaining - 1);
			}
		}
		else
		{
			if (_intentVel.Y > 0f) _intentVel.Y = 0f;
			if (ShouldRefreshJumpsFromFloor())
				RefreshJumps();   // ordinary ground/platform support refreshes jumps
		}

		// A SoftVolume is not a physical floor, but its fable traversal contract grants
		// jump support while the body is inside (cloud/tree jump chains). Refresh before
		// reading jump input so a spent charge can be used on this same physics tick.
		if (_softVolume != null && !_dead) RefreshJumps();

		float inputDir = 0f;
		if (!Frozen)
		{
			if (Input.IsActionPressed("move_left")) inputDir -= 1f;
			if (Input.IsActionPressed("move_right")) inputDir += 1f;

			if (Input.IsActionJustPressed("jump") && _jumpCdTimer <= 0f)
			{
				bool spentNormalJump = _jumpsRemaining > 0;
				// A character-specific support (Cleopastar's normal star) is asked
				// only when every ordinary jump charge has been spent.
				if (spentNormalJump || TryUseExhaustedJumpSupport())
				{
					_intentVel.Y = JumpVelocity;
					if (spentNormalJump) _jumpsRemaining--;
					_jumpCdTimer = JumpCooldown;
					_jumpedSinceGrounded = true;
				}
			}

			UpdateDropThrough(Input.IsActionPressed("move_down"), dt);
		}

		// Horizontal intent with momentum: accelerate toward the target, friction to stop.
		// OnFire/Frozen only scale MoveSpeed — accel/friction are untouched.
		float target = inputDir * MoveSpeed * _speedPenaltyMult * MoveMultiplier;
		float rate = Mathf.Abs(inputDir) > 0.01f ? (IsOnFloor() ? GroundAccel : AirAccel)
												 : (IsOnFloor() ? GroundFriction : AirFriction);
		_intentVel.X = Mathf.MoveToward(_intentVel.X, target, rate * dt);

		if (_softVolume != null && !_dead)
			_intentVel = _softVolume.ApplyVelocityResistance(_intentVel, MoveSpeed * MoveMultiplier, dt);

		// External velocity (knockback/wind/…) decays independently. SoftVolumes only
		// add drag; they never replace the player's own jump/gravity velocity.
		float damping = ExternalDamping * (_softVolume != null ? _softVolume.ExternalDampingMult : 1f);
		_externalVel = _externalVel.MoveToward(Vector2.Zero, damping * dt);

		Velocity = _intentVel + _externalVel + ContinuousExternalVelocity;
		MoveAndSlide();
		ReconcileCollisions();

		if (inputDir > 0.01f) { Facing = 1f; Sprite.FlipH = false; }
		else if (inputDir < -0.01f) { Facing = -1f; Sprite.FlipH = true; }
	}

	/// <summary>Full jump refresh + reset of the coyote-time bookkeeping, called
	/// whenever a solid surface (ground/platform) or active SoftVolume supports a jump.</summary>
	private void RefreshJumps()
	{
		_jumpsRemaining = MaxJumps;
		_airborneTimer = 0f;
		_coyoteConsumed = false;
		_jumpedSinceGrounded = false;
	}

	/// <summary>Most solid floors restore jump charges. A character may opt out for a
	/// transient support surface that should be consumed only by an exhausted jump.</summary>
	protected virtual bool ShouldRefreshJumpsFromFloor() => true;

	/// <summary>Optional last-resort support, considered only after normal jump charges
	/// are exhausted. Returning true authorizes the normal jump launch for this input.</summary>
	protected virtual bool TryUseExhaustedJumpSupport() => false;

	/// <summary>Zero the velocity channels that ran into a surface, so gravity/knockback
	/// don't accumulate against a wall or floor.</summary>
	private void ReconcileCollisions()
	{
		if (IsOnFloor() && _externalVel.Y > 0f) _externalVel.Y = 0f;
		if (IsOnCeiling())
		{
			if (_intentVel.Y < 0f) _intentVel.Y = 0f;
			if (_externalVel.Y < 0f) _externalVel.Y = 0f;
		}
		if (IsOnWall()) _externalVel.X = 0f;
	}

	/// <summary>
	/// Press-down drop-through for one-way platforms. Starting a drop removes the
	/// Platform layer from our collision mask so we fall through; holding down
	/// keeps it off (fall further — "how long you press decides how far"), and
	/// releasing re-solidifies so the next platform below catches us. Ground is a
	/// separate layer and stays solid, so this never drops us through the ground.
	/// </summary>
	private void UpdateDropThrough(bool wantDrop, float dt)
	{
		if (wantDrop && IsOnFloor() && !_dropping)
		{
			_dropping = true;
			_dropReleaseTimer = 0.25f;
			SetCollisionMaskValue(Units.LayerPlatform, false);
		}
		if (_dropping)
		{
			_dropReleaseTimer = wantDrop ? 0.25f : _dropReleaseTimer - dt;
			if (_dropReleaseTimer <= 0f)
			{
				_dropping = false;
				SetCollisionMaskValue(Units.LayerPlatform, true);
			}
		}
	}

	// ── SoftVolume resistance ─────────────────────────────────────────────
	public void EnterSoftVolume(SoftVolume v)
	{
		_softVolume = v;
		// Cancel any in-progress platform drop so we don't leave the mask off.
		if (_dropping) { _dropping = false; SetCollisionMaskValue(Units.LayerPlatform, true); }
	}

	public void ExitSoftVolume(SoftVolume v)
	{
		if (_softVolume == v) _softVolume = null;
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

	/// <summary>Subclass hook: absorb incoming damage (e.g. a shield pool) after the
	/// defense multiplier, before HP loss. Returns the damage that remains.
	/// Base implementation absorbs from TempHP first; subclasses that override this
	/// should call base.AbsorbDamage() to preserve that ordering.</summary>
	protected virtual float AbsorbDamage(float damage)
	{
		if (TempHP > 0f)
		{
			float absorbed = Mathf.Min(TempHP, damage);
			TempHP -= absorbed;
			damage -= absorbed;
			NotifyHpChanged();
		}
		return damage;
	}

	/// <summary>Fire HpChanged with the current values of all four HP-related
	/// properties. Call this instead of invoking the event directly.</summary>
	protected void NotifyHpChanged() => HpChanged?.Invoke(CurrentHP, MaxHP, Shield, TempHP);

	/// <summary>Grant temporary HP (green bar). Absorbed before normal HP and shield
	/// when taking damage. Does not persist across death.</summary>
	public void GainTempHP(float amount)
	{
		if (_dead || amount <= 0f) return;
		TempHP += amount;
		NotifyHpChanged();
	}

	/// <summary>Last animation name requested via <see cref="PlayAnim"/>. Godot clears
	/// <c>AnimationPlayer.CurrentAnimation</c> back to "" once a non-looping clip
	/// finishes, so the automata needs its own memory of what it last asked for
	/// (e.g. to hold jump/dead's final frame without replaying, or to know a
	/// one-shot is still "in flight").</summary>
	protected string LastAnim { get; private set; } = "";

	/// <summary>Request an animation by library name. No-ops on scenes without an
	/// AnimationPlayer/library (keeps characters without authored animations valid).
	/// <paramref name="restart"/> forces a replay even if it's already the current
	/// clip (Play() alone won't restart a same-name animation); otherwise it only
	/// switches when the requested clip differs from what's already playing.</summary>
	protected void PlayAnim(string name, bool restart = false)
	{
		if (Anim == null || !Anim.HasAnimation(name)) return;
		if (restart)
		{
			Anim.Stop();
			Anim.Play(name);
		}
		else if (Anim.CurrentAnimation != name)
		{
			Anim.Play(name);
		}
		LastAnim = name;
	}

	// ── Shared combat helpers ─────────────────────────────────────────────

	/// <summary>
	/// Damage every enemy inside a cone in front of the character.
	/// Returns the number of enemies hit. Damage timing here stands in for the
	/// Unity animation-event trigger; when animations land, call this from the
	/// AnimationPlayer "call method" track instead.
	/// </summary>
	protected int MeleeCone(float range, float halfAngleDeg, float damage,
							float knockbackSpeed, bool applyBurn, float burnDuration,
							float stun = -1f)
	{
		int hits = 0;
		Vector2 origin = GlobalPosition;
		Vector2 aim = new Vector2(Facing, 0f);
		float halfRad = Mathf.DegToRad(halfAngleDeg);
		float scaledDamage = damage * DamageDealtMultiplier;

		foreach (Node n in GetTree().GetNodesInGroup("enemy"))
		{
			if (n is not BaseFoe e) continue;

			// Treat the foe as a circle of radius HitRadius and test whether the
			// cone overlaps it at all — so a glancing clip still counts, not just
			// a hit dead-on the foe's center.
			Vector2 to = e.GlobalPosition - origin;
			float dist = to.Length();
			float r = e.HitRadius;
			if (dist - r > range) continue;                       // nearest point out of reach
			if (dist > 0.01f)
			{
				// Widen the cone by the angle the foe's radius subtends at this distance.
				float angTol = dist > r ? Mathf.Asin(Mathf.Clamp(r / dist, 0f, 1f)) : Mathf.Pi;
				if (to.Normalized().Dot(aim) < Mathf.Cos(halfRad + angTol)) continue;
			}

			Vector2 knock = new Vector2(Facing, -0.35f).Normalized() * knockbackSpeed;
			float dealt = e.TakeHit(new HitInfo(scaledDamage, knock, stun), origin);   // origin feeds crab Soft Shell
			if (applyBurn) e.SetBurning(burnDuration);
			if (dealt > 0f)
			{
				MeleeDamageDealt?.Invoke(e, dealt);
				ReportDamageDealt(dealt);
			}
			hits++;
		}
		return hits;
	}

	/// <summary>Report a non-melee hit dealt by this protagonist. Projectile and persistent-skill
	/// owners call this with the actual post-mitigation result returned by <see cref="BaseFoe.TakeHit"/>.</summary>
	public void ReportDamageDealt(float dealt)
	{
		if (dealt > 0f) DamageDealt?.Invoke(dealt);
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
		if (Invincible) return;
		IsBurning = true;
		_burnTimer = Mathf.Max(_burnTimer, duration);
		Sprite.SelfModulate = new Color(1f, 0.55f, 0.35f);
	}

	/// <summary>Convenience for a hit with default (damage-linked) gain-no window.</summary>
	public void TakeDamage(float amount, Vector2 knockback) => TakeHit(new HitInfo(amount, knockback));

	/// <summary>Apply a fully-authored hit: damage, delta-v knockback, and a gain-no
	/// (hitstun) window during which control is off and animation is frozen.</summary>
	public void TakeHit(HitInfo hit)
	{
		if (_dead || Invincible || _invulnTimer > 0f) return;

		float dealt = hit.Damage * DefenseMultiplier;
		dealt = AbsorbDamage(dealt);
		if (dealt > 0f) DirectDamageTaken?.Invoke(dealt);
		DebugManager.Instance?.LogPlayerDmgReceived(hit.Damage, dealt, "foe");
		CurrentHP = Mathf.Max(0f, CurrentHP - dealt);
		NotifyHpChanged();
		PopNumber(dealt, heal: false);
		GainUltCharge(dealt * UltChargeReceivedRate);

		AddImpulse(hit.Knockback);
		float stun = hit.ResolveStun();
		if (stun > 0f) _stunTimer = Mathf.Max(_stunTimer, stun);

		ShakeCamera(hit.Knockback != Vector2.Zero ? 0.5f : 0.3f);
		if (hit.Knockback != Vector2.Zero) _invulnTimer = 0.8f;
		if (CurrentHP <= 0f) Die();
	}

	/// <summary>Environmental hazard tick: damage + knockback that bypasses the
	/// combat i-frame gate. Hazards reapply every ~0.25s — far faster than the
	/// post-hit invuln window — so standing in one should keep hurting you.</summary>
	public void ApplyHazard(float damage, Vector2 knockback)
	{
		if (_dead || Invincible) return;

		// Respawn invincibility also blocks hazard damage
		if (_invulnTimer > 0f) return;

		if (damage > 0f)
		{
			float dealt = damage * DefenseMultiplier;
			dealt = AbsorbDamage(dealt);
			DebugManager.Instance?.LogHazardDmg(dealt, "player");
			CurrentHP = Mathf.Max(0f, CurrentHP - dealt);
			NotifyHpChanged();
			PopNumber(dealt, heal: false);
			GainUltCharge(dealt * UltChargeReceivedRate);
		}
		if (knockback != Vector2.Zero)
		{
			AddImpulse(knockback);
			ShakeCamera(0.22f);
		}
		if (CurrentHP <= 0f) Die();
	}

	/// <summary>Add integer "points" to the stackable OnFire hazard debuff (decays
	/// itself as damage over time; see <see cref="DecayingDebuff"/>).</summary>
	public void AddFireStack(float amount)
	{
		if (Invincible) return;
		_fire.AddStack(amount, StatusDurationExtension(_fireDurationExtensions));
		DebugManager.Instance?.LogStatus("OnFire", amount);
	}

	/// <summary>Add integer "points" to the stackable Frozen hazard debuff.</summary>
	public void AddFrozenStack(float amount)
	{
		if (Invincible) return;
		_frozenDebuff.AddStack(amount, StatusDurationExtension(_frozenDurationExtensions));
		DebugManager.Instance?.LogStatus("Frozen", amount);
	}

	/// <summary>Set/clear a named, aggregatable defense contribution (e.g. a skill's
	/// temporary damage mitigation). Defense is the only damage-taken lever — there's
	/// no separate flat "damage reduction" multiplier alongside it.</summary>
	public void SetDefenseSource(string source, float defense) => _defenseBonuses[source] = defense;
	public void ClearDefenseSource(string source) => _defenseBonuses.Remove(source);

	/// <summary>Set or clear a held item's extension for future OnFire applications. Extensions
	/// are source-keyed and additive, following the modifier-stack rule.</summary>
	public void SetFireDurationExtension(string source, float seconds)
	{
		if (string.IsNullOrWhiteSpace(source)) return;
		if (seconds > 0f) _fireDurationExtensions[source] = seconds;
		else _fireDurationExtensions.Remove(source);
	}

	public void ClearFireDurationExtension(string source) => _fireDurationExtensions.Remove(source);

	/// <summary>Set or clear a held item's extension for future Frozen applications.</summary>
	public void SetFrozenDurationExtension(string source, float seconds)
	{
		if (string.IsNullOrWhiteSpace(source)) return;
		if (seconds > 0f) _frozenDurationExtensions[source] = seconds;
		else _frozenDurationExtensions.Remove(source);
	}

	public void ClearFrozenDurationExtension(string source) => _frozenDurationExtensions.Remove(source);

	private static float StatusDurationExtension(Dictionary<string, float> sources)
	{
		float total = 0f;
		foreach (float seconds in sources.Values) total += seconds;
		return total;
	}

	/// <summary>Add a delta-v impulse to the external-velocity channel (knockback/wind/…).</summary>
	public void AddImpulse(Vector2 impulse) => _externalVel += impulse;

	private void ApplyDotDamage(float amount)
	{
		if (_dead || Invincible) return;
		float dealt = amount * DefenseMultiplier;
		dealt = AbsorbDamage(dealt);
		DebugManager.Instance?.LogDotDmg(dealt, IsBurning ? "Burn" : _fire.Active ? "OnFire" : "Frozen");
		CurrentHP = Mathf.Max(0f, CurrentHP - dealt);
		NotifyHpChanged();
		GainUltCharge(dealt * UltChargeReceivedRate);
		if (CurrentHP <= 0f) Die();
	}

	/// <summary>Add ult charge (clamped to MaxUltCharge) and notify the HUD. Enemy
	/// credits the rate for damage dealt via <see cref="LocalPlayer"/>; this
	/// character credits the rate for damage it receives itself (see TakeHit/
	/// ApplyHazard/ApplyDotDamage above).</summary>
	public void GainUltCharge(float amount)
	{
		if (_dead || amount <= 0f) return;
		UltCharge = Mathf.Min(MaxUltCharge, UltCharge + amount);
		UltChargeChanged?.Invoke(UltCharge, MaxUltCharge);
	}

	/// <summary>Restore HP (green damage number). No source uses it yet, but the
	/// system is wired for pickups/regen.</summary>
	public void Heal(float amount)
	{
		if (_dead || amount <= 0f) return;
		DebugManager.Instance?.LogHeal(amount, "combat");
		CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
		NotifyHpChanged();
		PopNumber(amount, heal: true);
	}

	private void PopNumber(float amount, bool heal) =>
		DamageNumberManager.Instance?.Pop(
			GlobalPosition + new Vector2(0f, -Units.PlayerHeightPx * 0.5f), amount, heal);

	protected void ShakeCamera(float amount) => CameraShake?.AddTrauma(amount);

	private void Die()
	{
		_dead = true;
		LivesRemaining = Mathf.Max(0, LivesRemaining - 1);
		Sprite.Modulate = new Color(0.4f, 0.4f, 0.4f);
		Velocity = _intentVel = _externalVel = Vector2.Zero;
		_stunTimer = 0f;
		IsBurning = false;
		_fire.Clear();
		_frozenDebuff.Clear();
		_dealtDmgBonuses.Clear();
		_defenseBonuses.Clear();
		_fireDurationExtensions.Clear();
		_frozenDurationExtensions.Clear();
		_continuousExternalVelocity.Clear();
		TempHP = 0f;
		Died?.Invoke();
	}

	/// <summary>
	/// Re-anchor the respawn point after an external spawner/swapper has placed this body.
	/// _Ready latches _spawnPoint, but _Ready runs synchronously inside AddChild — BEFORE the
	/// caller sets GlobalPosition — so anything that instances a character at runtime (debug
	/// protagonist swap) must call this after positioning, or Respawn() teleports to the
	/// character scene's own origin. Same bug class as the seagull patrol anchor (KNOWLEDGE.md).
	/// </summary>
	public void SetSpawnPoint(Vector2 point) => _spawnPoint = point;

	/// <summary>Brought back by GameManager after a death (if lives remain).</summary>
	public void Respawn()
	{
		_dead = false;
		Invincible = false;
		CurrentHP = MaxHP;
		GlobalPosition = _spawnPoint;
		Velocity = _intentVel = _externalVel = Vector2.Zero;
		_stunTimer = 0f;
		_invulnTimer = 2f;
		_fire.Clear();
		_frozenDebuff.Clear();
		_dealtDmgBonuses.Clear();
		_defenseBonuses.Clear();
		_fireDurationExtensions.Clear();
		_frozenDurationExtensions.Clear();
		_continuousExternalVelocity.Clear();
		Ammo?.ResetForRespawn();
		TempHP = 0f;
		Sprite.Modulate = Colors.White;
		Sprite.SelfModulate = _baseTint;
		Sprite.Visible = true;
		NotifyHpChanged();
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

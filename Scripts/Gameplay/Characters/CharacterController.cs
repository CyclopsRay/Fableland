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
/// A fighter is on one <see cref="CombatSide"/> and reads its input from
/// <see cref="Controls"/>, never from the Input singleton: the player's keys by default,
/// a boss brain's virtual pad when the same kit fights on the Foe side (FOES.gdd §8).
///
/// Provides: movement + double-jump (with coyote time), HP/lives, i-frames,
/// knockback, death/respawn, a generic Burning status (DoT for non-immune
/// targets; a passive trigger for those who are), the hazard-driven OnFire/Frozen
/// statuses (stackable, self-decaying — see <see cref="DecayingDebuff"/>), a
/// post-swing move-speed penalty, a melee cone helper, and a debug-draw hook that
/// subclasses use to visualize skill ranges.
///
/// Feedback (<c>Docs/Feedback.gdd</c>): the body raises its own cues on <see cref="CueBus"/>
/// (spawn, jump, land, death, statuses, one <see cref="HitCue"/> per hit taken), holds still
/// for hit-stop, and owns one hit-flash channel — it never names an effect, a sound or a
/// shake, and never calls a director.
///
/// ── ADDING A CHARACTER ────────────────────────────────────────────────
///   1. Subclass CharacterController.
///   2. Override InitCharacter() for stats / immunities.
///   3. Override HandleBA / HandleSkill1 / HandleSkill2 / HandleSkillUlt.
///   4. Override DrawDebug() to telegraph ranges.
///   5. (Later) override UpdateAnimator() once real animations exist.
/// ──────────────────────────────────────────────────────────────────────
/// </summary>
public partial class CharacterController : CharacterBody2D, ICombatTarget, IFeedbackBody
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
	public event Action<ICombatTarget, float> MeleeDamageDealt;
	/// <summary>Post-mitigation damage caused by this player. Projectiles and area skills report
	/// through <see cref="ReportDamageDealt"/> so omni-vamp-style held items are not melee-only.</summary>
	public event Action<float> DamageDealt;

	// Single-player prototype: lets Enemy credit ult charge for damage dealt
	// without needing a reference back to the player (mirrors ShakeCamera2D.Instance).
	// Only a Player-side body ever claims it.
	public static CharacterController LocalPlayer { get; private set; }

	/// <summary>Which side this fighter is on. The player's protagonists are on the Player
	/// side; a BOSS variant overrides this to <see cref="CombatSide.Foe"/> and from then on
	/// the base treats the body as an opponent: it joins the group the player's attacks
	/// iterate, sits on the Foes collision layer, owns no camera, never reads the keyboard,
	/// and its own attacks look for the player.</summary>
	public virtual CombatSide Side => CombatSide.Player;

	/// <summary>Where this body reads its input. Kits ask this — never <c>Input</c> or the
	/// mouse — so the same kit can be worn by the player or driven by an AI.</summary>
	public ICharacterControls Controls { get; private set; } = PlayerControls.Instance;

	/// <summary>Hand this body to another controller (a boss brain's pad, a test).</summary>
	public void UseControls(ICharacterControls controls) =>
		Controls = controls ?? (Side == CombatSide.Player ? PlayerControls.Instance : new VirtualPad());

	/// <summary>Radius of the circle the other side's hit tests treat this body as
	/// (KNOWLEDGE v0.1.5) — between the half-width and half-height of the 48×64 class shape.</summary>
	public float HitRadius { get; protected set; } = Units.PlayerHeightPx * 0.45f;

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
	/// <summary>World seconds since this protagonist last dealt valid damage or received a
	/// valid damaging hit. Shared passives and wonder items use this single out-of-combat
	/// clock instead of maintaining drift-prone duplicate timers.</summary>
	public float CombatInactivitySeconds { get; private set; }
	public int LivesRemaining { get; private set; }
	public bool ControlsLocked { get; set; }
	/// <summary>Root status: blocks self-directed movement/jumps and new knockback, but not skills.</summary>
	public bool Trapped => _trappedTimer > 0f;
	/// <summary>Combat-side damage gate used for terminal mission resolution. Unlike
	/// the short post-hit i-frame timer, this also suppresses hazards and DoT.</summary>
	public bool Invincible { get; set; }

	/// <summary>Per-character cooldown state for the HUD's skill icons. Base has
	/// nothing to report; characters with a Shift/E override these.</summary>
	public virtual (float Remaining, float Max) ShiftCooldown => (0f, 0f);
	public virtual (float Remaining, float Max) ESkillCooldown => (0f, 0f);
	/// <summary>External arena actions honour this while a character is committed to a skill state.</summary>
	public virtual bool CanSwitchProtagonist => !ControlsLocked;
	/// <summary>Whether the arena may activate this body's held item this frame.</summary>
	public virtual bool CanUseHeldItem => !ControlsLocked;
	/// <summary>Freeze the body's world position without discarding a subclass-owned
	/// velocity snapshot. Temporal rewinds use this while their own state machine moves
	/// between historical positions.</summary>
	public virtual bool LocksPhysicalMotion => false;
	/// <summary>Relative simulation rate for this body. The default keeps existing protagonists unchanged.</summary>
	public virtual float LocalTimeRate => LocalTime.DefaultRate;
	/// <summary>Convert a world delta into this character's simulation delta.</summary>
	public float GetActDelta(float worldDelta) => LocalTime.ActDelta(worldDelta, LocalTimeRate);

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

	/// <summary>What this fighter's damage is multiplied by right now — read-only, for
	/// checks and tooling (a boss's is its row's share × the day's ATK multiplier).</summary>
	public float DamageDealtScale => DamageDealtMultiplier;

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
	// Poise: how much of an incoming hit's knockback / gain-no window this body takes.
	// Multiplicative by source (a boss's poise × an item's), 1 when nothing is set — so a
	// protagonist is byte-for-byte unaffected. Set by BossTable rows today.
	private readonly Dictionary<string, float> _knockbackTakenScales = new();
	private readonly Dictionary<string, float> _stunTakenScales = new();
	// ── Feedback (Docs/Feedback.gdd): hit-stop, merged hurt ticks, status loops, hit flash ──
	private HitStopClock _hitStop;
	private readonly HurtTickMeter _hurtTicks = new();
	private readonly BodyFlash _flash = new();
	private FeedbackState _statusLoops;    // which status loops are started on the cue bus
	private string _feedbackId;
	private bool _spawnCueRaised;
	private bool _jumpBuffered;            // Jump pressed while the body was held by a stop
	private int _jumpOrdinal;              // which jump of this airtime the next one is
	private bool _wasOnFloor;
	// Where the bottom of this body's collision shape is, relative to its origin: its feet.
	private Vector2 _feetOffset = new(0f, Units.PlayerHeightPx * 0.5f);
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
	/// <summary>Velocity expressed in this body's local simulation seconds. Temporal
	/// projectiles inherit this value, then apply their own Local Time Rate exactly once.</summary>
	protected Vector2 LocalMotionVelocity => _intentVel + _externalVel + ContinuousExternalVelocity;

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
	private float _trappedTimer;
	// Every go-inside volume this body overlaps (a current inside a sea, kelp in water),
	// and what they add up to: resistance, buoyancy, carry.
	private readonly SoftVolumeSet _softVolumes = new();
	private const string SoftVolumeFlowSource = "softvolume.flow";
	// Where the top of this body's own collision shape is, relative to its origin: its head.
	private Vector2 _crownOffset = new(0f, -Units.PlayerHeightPx * 0.5f);
	private Vector2 _intentVel;        // self-directed velocity (input/gravity/jump/field)
	private Vector2 _externalVel;      // impulses (knockback/wind/…); decays over time
	private Vector2 _spawnPoint;
	private Color _baseTint = Colors.White;

	public override void _EnterTree()
	{
		if (Side == CombatSide.Player)
		{
			LocalPlayer = this;
			return;
		}

		// A Foe-side fighter is built from a protagonist's scene, which carries the player's
		// view. Take the camera out before it enters the tree: once inside it would claim
		// ShakeCamera2D.Instance and could become the current camera. Children enter after
		// their parent, so this is the last moment it is still inert.
		Camera2D camera = GetNodeOrNull<Camera2D>("Camera2D");
		if (camera != null)
		{
			RemoveChild(camera);
			camera.QueueFree();
		}
		// Never the keyboard, not even for the frame before a brain is attached.
		if (Controls is PlayerControls) Controls = new VirtualPad();
	}

	public override void _ExitTree()
	{
		// Leaving mid-status (a Tab switch, a scene change): end the loops this body started.
		_statusLoops = StatusLoops.Sync(this, _statusLoops, FeedbackState.None);
		_flash.Clear();
		if (LocalPlayer == this) LocalPlayer = null;
	}

	public override void _Ready()
	{
		Sprite = GetNode<Sprite2D>("Sprite2D");
		Anim = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");   // room for animation
		FirePoint = GetNodeOrNull<Node2D>("FirePoint");
		CameraShake = GetNodeOrNull<ShakeCamera2D>("Camera2D");
		_spawnPoint = GlobalPosition;

		foreach (Node child in GetChildren())
		{
			if (child is not CollisionShape2D { Shape: not null } body) continue;
			Rect2 box = body.Shape.GetRect();
			_crownOffset = body.Position + new Vector2(box.GetCenter().X, box.Position.Y);
			_feetOffset = body.Position + new Vector2(box.GetCenter().X, box.End.Y);
			break;
		}

		CurrentHP = MaxHP;
		LivesRemaining = MaxLives;
		if (Side == CombatSide.Player)
		{
			AddToGroup(CombatTargets.PlayerGroup);
		}
		else
		{
			// "enemy" = the player's attacks iterate me. Deliberately not "foe": that group
			// is the ambient spawn cap and the missions' foe sweeps (KNOWLEDGE v0.5.0).
			AddToGroup(CombatTargets.EnemyGroup);
			CollisionLayer = 0;
			SetCollisionLayerValue(Units.LayerFoes, true);      // player projectiles find it here
			CollisionMask = 0;
			SetCollisionMaskValue(Units.LayerGround, true);
			SetCollisionMaskValue(Units.LayerPlatform, true);
		}
		// Keep a walking body on a slope instead of stepping off it every tick (Units.cs).
		FloorSnapLength = Mathf.Max(FloorSnapLength, Units.FloorSnapPx);

		InitCharacter();
		_jumpsRemaining = MaxJumps;
		NotifyHpChanged();
		UltChargeChanged?.Invoke(UltCharge, MaxUltCharge);
	}

	/// <summary>Override for per-character stats, immunities, ammo config.</summary>
	protected virtual void InitCharacter() { }

	/// <summary>Set an authored base HP during <see cref="InitCharacter"/> without leaving
	/// the pre-initialized current HP above the new maximum. Run hydration may replace this later.</summary>
	protected void SetBaseMaxHP(float maxHp)
	{
		MaxHP = Mathf.Max(1f, maxHp);
		CurrentHP = Mathf.Min(CurrentHP, MaxHP);
	}

	/// <summary>Configure this character's one shared BA magazine from pure balance data.</summary>
	protected void ConfigureAmmo(string characterId)
	{
		CharacterTable.TryGetAmmo(characterId, out CharacterAmmoDef def);
		ConfigureAmmo(def, characterId);
	}

	/// <summary>Configure the magazine from a definition the kit resolved itself (a BOSS
	/// variant's own row). <paramref name="what"/> only names it in the error.</summary>
	protected void ConfigureAmmo(CharacterAmmoDef def, string what)
	{
		if (def == null)
		{
			GD.PushError($"CharacterController: no ammo definition for '{what}'.");
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
	/// <summary>Set the stored self-directed horizontal velocity for a committed movement
	/// phase. This stays separate from external knockback and is applied by normal physics.</summary>
	protected void SetIntentHorizontalVelocity(float velocityPxPerSecond) => _intentVel.X = velocityPxPerSecond;
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
		if (_boundAmmoState != null) Ammo?.Save(p);
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

	/// <summary>Prepare a manually debug-swapped body for immediate test input without
	/// writing that convenience state back into the real run.</summary>
	public virtual void ResetDebugCombatState()
	{
		Ammo?.ResetForRespawn();
		// A debug-selected body may share the active party record for HP hydration, but
		// its own test attacks must not overwrite that record with another character's
		// magazine definition or reload timer.
		_boundAmmoState = null;
	}

	/// <summary>Copy the internal velocity channels from another character so the incoming
	/// protagonist continues the outgoing one's trajectory (mid-air switch — NODES §3.3).
	/// Must be called while <paramref name="other"/> is still alive (before QueueFree).</summary>
	public void InheritVelocityFrom(CharacterController other)
	{
		if (other == null) return;
		_intentVel = other._intentVel;
		_externalVel = other._externalVel;
		Velocity = (_intentVel + _externalVel) * LocalTime.ClampRate(LocalTimeRate);
	}

	/// <summary>Capture both velocity channels for a temporal rewind. A total Velocity alone
	/// loses the distinction between controllable intent and decaying external impulses.</summary>
	protected (Vector2 Intent, Vector2 External) CaptureMovementVelocityState() => (_intentVel, _externalVel);

	/// <summary>Restore a velocity snapshot captured by <see cref="CaptureMovementVelocityState"/>.</summary>
	protected void RestoreMovementVelocityState(Vector2 intent, Vector2 external)
	{
		_intentVel = intent;
		_externalVel = external;
		Velocity = (_intentVel + _externalVel) * LocalTime.ClampRate(LocalTimeRate);
	}

	public override void _Process(double delta)
	{
		float dt = GetActDelta((float)delta);
		// What is only shown runs in world time, whatever the body's own clock is doing.
		_flash.Tick((float)delta);
		_hurtTicks.Tick((float)delta);
		if (_dead) { if (ShowDebugRanges) QueueRedraw(); return; }
		CombatInactivitySeconds += (float)delta;

		// Every clock keeps running through a hit-stop — cooldowns, ammo, statuses, the
		// gain-no window — and abilities are still read, so a press during a stop is never
		// lost and no timing in a fight depends on how often it was stopped. A stop holds
		// motion (_PhysicsProcess) and the animation (below), nothing else.
		UpdateTimers(dt);
		Ammo?.Tick(dt);
		UpdateStatus(dt);
		if (!_dead) _statusLoops = StatusLoops.Sync(this, _statusLoops, StatusState());
		if (!Frozen) HandleAbilities();

		// Gain-no canon: the animation itself freezes on the current frame during
		// a stun window (knockback/gravity still move the body). Idempotent, so
		// it also auto-restores the instant the stun timer clears — and Die()
		// zeroes _stunTimer, so the death animation is never left frozen.
		// A hit-stop holds the frame the same way.
		bool holdFrame = _stunTimer > 0f || _hitStop.Active;
		if (Anim != null) Anim.SpeedScale = holdFrame ? 0f : LocalTime.ClampRate(LocalTimeRate);
		if (!holdFrame) UpdateAnimator(dt);   // animation frozen during gain-no and hit-stop

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
			// The blink waits for the hit flash: 0.8 s of i-frames opens on the blink's "off"
			// half, which hid the body on the very frame it was hit — and the flash with it
			// (seen in the VFX pass's first captures, v0.11.6). Flash means "hit"; then blink.
			Sprite.Visible = _flash.Active || Mathf.PosMod(_invulnTimer, 0.12f) < 0.06f;
			if (_invulnTimer <= 0f) Sprite.Visible = true;
		}
		if (_stunTimer > 0f) _stunTimer -= dt;
		if (_trappedTimer > 0f) _trappedTimer -= dt;
	}

	private void UpdateStatus(float dt)
	{
		if (IsBurning)
		{
			_burnTimer -= dt;
			if (!BurnImmune) ApplyDotDamage(BurnDotPerSecond * dt, HurtSource.Burn);
			if (_burnTimer <= 0f) IsBurning = false;
		}

		float fireDmg = _fire.Tick(dt);
		if (fireDmg > 0f) ApplyDotDamage(fireDmg, HurtSource.OnFire);
		float frozenDmg = _frozenDebuff.Tick(dt);
		if (frozenDmg > 0f) ApplyDotDamage(frozenDmg, HurtSource.Frozen);

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
		// The first tick finds the body where its spawner put it (_Ready ran before that).
		if (!_spawnCueRaised)
		{
			_spawnCueRaised = true;
			CueBus.Emit(this, "spawn");
		}

		// Hit-stop: the body holds exactly where it is and keeps both velocity channels, so
		// it resumes with the motion it had (plus any knockback the hit added). Jump is an
		// edge read here, in the physics loop — remember a press that falls inside a stop.
		if (HoldForHitStop((float)delta))
		{
			if (Controls.JustPressed(ControlAction.Jump)) _jumpBuffered = true;
			return;
		}

		if (LocksPhysicalMotion)
		{
			// Do not integrate gravity, momentum, knockback, or continuous fields while a
			// kit has explicitly frozen this body. The stored channels are preserved so
			// the kit can restore its selected historical velocity when the lock ends.
			Velocity = Vector2.Zero;
			return;
		}

		float dt = GetActDelta((float)delta);
		float motionRate = LocalTime.ClampRate(LocalTimeRate);
		if (_jumpCdTimer > 0f) _jumpCdTimer -= dt;

		// Soft volumes: forget any that were freed, then let their currents carry the body.
		// Refreshed every tick (not on enter/exit) so a respawn inside a current keeps it.
		_softVolumes.Prune();
		if (_softVolumes.Flow != Vector2.Zero && !_dead) SetContinuousExternalVelocity(SoftVolumeFlowSource, _softVolumes.Flow);
		else ClearContinuousExternalVelocity(SoftVolumeFlowSource);

		// Inside a soft volume a fighter swims: Up and Down steer as Left and Right do, and
		// the volume's slow drop stands in for gravity (SoftVolumeSet.SwimVertical).
		// NOTE(animation): `swimming` and `rise` are the state a swim / float clip would read.
		bool controllable = !Frozen && !Trapped;
		bool swimming = _softVolumes.Swims && !_dead;
		float swimSpeed = MoveSpeed * MoveMultiplier;
		float rise = 0f;   // −1 swims up, +1 swims down
		if (swimming && controllable)
		{
			// Through Controls, like every other button: a BOSS is this class on a brain's
			// pad, and the player's Up / Down must swim the player only (KNOWLEDGE v0.10.11).
			if (Controls.Held(ControlAction.MoveDown)) rise += 1f;
			// Up pushes only while the head is under. At the surface a swimmer settles with
			// its head there; a jump carries it out.
			if (Controls.Held(ControlAction.MoveUp) && _softVolumes.Covers(Crown)) rise -= 1f;
		}

		// Vertical intent: gravity accumulates (momentum); jump sets it. A passive body in a
		// buoyant medium keeps only part of gravity: buoyancy and gravity are simply added.
		if (!IsOnFloor())
		{
			if (swimming) _intentVel.Y = SwimVertical(rise, swimSpeed, dt);
			else _intentVel.Y += Gravity * _softVolumes.GravityScale * dt;

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
			if (swimming && rise < 0f) _intentVel.Y = SwimVertical(rise, swimSpeed, dt);   // swim up off the bottom
			if (ShouldRefreshJumpsFromFloor())
				RefreshJumps();   // ordinary ground/platform support refreshes jumps
		}

		// A SoftVolume is not a physical floor, but its fable traversal contract grants
		// jump support while the body is inside: a jump there is a stroke, as often as the
		// universal JumpCooldown allows, on top of the swimming (cloud/tree jump chains).
		// Refresh before reading jump input so a charge can be used on this same physics tick.
		if (_softVolumes.InMedium && !_dead) RefreshJumps();

		float inputDir = 0f;
		if (controllable)
		{
			if (Controls.Held(ControlAction.MoveLeft)) inputDir -= 1f;
			if (Controls.Held(ControlAction.MoveRight)) inputDir += 1f;

			if ((Controls.JustPressed(ControlAction.Jump) || _jumpBuffered) && _jumpCdTimer <= 0f)
			{
				bool spentNormalJump = _jumpsRemaining > 0;
				// A character-specific support (Cleopastar's normal star) is asked
				// only when every ordinary jump charge has been spent.
				if (spentNormalJump || TryUseExhaustedJumpSupport())
				{
					// Off the ground (or its coyote grace) it is a ground jump; inside a soft
					// volume a stroke; anything else is an air jump.
					bool fromGround = IsOnFloor() || (!_jumpedSinceGrounded && !_coyoteConsumed);
					_intentVel.Y = JumpVelocity;
					if (spentNormalJump) _jumpsRemaining--;
					_jumpCdTimer = JumpCooldown;
					_jumpedSinceGrounded = true;
					_jumpOrdinal++;
					CueBus.Emit(this, "jump", variant: _jumpOrdinal, magnitude: -JumpVelocity,
						direction: Vector2.Up, tag: _softVolumes.InMedium ? "swim" : fromGround ? null : "air",
						at: Feet);
				}
			}

			UpdateDropThrough(Controls.Held(ControlAction.MoveDown), dt);
		}
		_jumpBuffered = false;   // a press held over a stop is good for the tick after it only

		// Horizontal intent with momentum: accelerate toward the target, friction to stop.
		// OnFire/Frozen only scale MoveSpeed — accel/friction are untouched.
		float target = inputDir * MoveSpeed * _speedPenaltyMult * MoveMultiplier;
		float rate = Mathf.Abs(inputDir) > 0.01f ? (IsOnFloor() ? GroundAccel : AirAccel)
												 : (IsOnFloor() ? GroundFriction : AirFriction);
		_intentVel.X = Mathf.MoveToward(_intentVel.X, target, rate * dt);

		if (swimming) _intentVel = _softVolumes.Swim(_intentVel, swimSpeed, dt);
		else if (!_dead) _intentVel = _softVolumes.Resist(_intentVel, swimSpeed, dt);

		// External velocity (knockback/wind/…) decays independently; a SoftVolume only
		// damps it harder.
		float damping = ExternalDamping * _softVolumes.ExternalDampingMult;
		_externalVel = _externalVel.MoveToward(Vector2.Zero, damping * dt);

		// Intent/external velocity is stored in local-time units. Godot integrates its
		// Velocity over the world physics tick, so output must be rate-scaled as well.
		Velocity = LocalMotionVelocity * motionRate;
		float fallSpeed = Velocity.Y;
		MoveAndSlide();
		ReconcileCollisions();

		bool onFloor = IsOnFloor();
		if (onFloor && !_wasOnFloor && fallSpeed >= FeedbackTable.LandCueMinSpeed && !_dead)
			CueBus.Emit(this, "land", magnitude: fallSpeed, direction: Vector2.Down, at: Feet);
		_wasOnFloor = onFloor;

		if (inputDir > 0.01f) { Facing = 1f; Sprite.FlipH = false; }
		else if (inputDir < -0.01f) { Facing = -1f; Sprite.FlipH = true; }
		else if (!Frozen) FaceByHint();
	}

	/// <summary>A controller may turn a standing fighter (a boss watching the player). A
	/// player's facing only ever follows their movement keys: their hint is always 0.</summary>
	protected void FaceByHint()
	{
		float hint = Controls.FacingHint;
		if (hint > 0.01f) { Facing = 1f; Sprite.FlipH = false; }
		else if (hint < -0.01f) { Facing = -1f; Sprite.FlipH = true; }
	}

	/// <summary>Full jump refresh + reset of the coyote-time bookkeeping, called
	/// whenever a solid surface (ground/platform) or active SoftVolume supports a jump.</summary>
	private void RefreshJumps()
	{
		_jumpsRemaining = MaxJumps;
		_airborneTimer = 0f;
		_coyoteConsumed = false;
		_jumpedSinceGrounded = false;
		_jumpOrdinal = 0;
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

	// ── SoftVolumes: swimming ─────────────────────────────────────────────
	/// <summary>The top of this body's head, in world space.</summary>
	private Vector2 Crown => GlobalPosition + _crownOffset;

	private float SwimVertical(float rise, float swimSpeed, float dt) =>
		_softVolumes.SwimVertical(_intentVel.Y, rise, swimSpeed * _speedPenaltyMult, swimSpeed,
			AirAccel, AirFriction, Gravity, dt);

	public void EnterSoftVolume(SoftVolume v)
	{
		bool wasInside = _softVolumes.InMedium;
		_softVolumes.Enter(v);
		// One cue for going into "a medium", not one per tile of it.
		if (!wasInside && _softVolumes.InMedium) SoftVolume.RaiseCrossing(this, v, entered: true);
		// Cancel any in-progress platform drop so we don't leave the mask off.
		if (_dropping) { _dropping = false; SetCollisionMaskValue(Units.LayerPlatform, true); }
	}

	public void ExitSoftVolume(SoftVolume v)
	{
		bool wasInside = _softVolumes.InMedium;
		_softVolumes.Exit(v);
		if (wasInside && !_softVolumes.InMedium) SoftVolume.RaiseCrossing(this, v, entered: false);
	}

	// ── Ability hooks ─────────────────────────────────────────────────────
	private void HandleAbilities()
	{
		if (Controls.JustPressed(ControlAction.Attack)) HandleBA();
		if (Controls.JustPressed(ControlAction.Skill1)) HandleSkill1();
		if (Controls.JustPressed(ControlAction.Skill2)) HandleSkill2();
		if (Controls.JustPressed(ControlAction.Ult)) HandleSkillUlt();
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
	/// <para>Feedback: every hit carries <c>From(this, cueAction, cueVariant)</c>, and the
	/// swing holds its attacker ONCE — for the heaviest hit it landed, never per victim
	/// (<c>Docs/Feedback.gdd</c> §4).</para>
	protected int MeleeCone(float range, float halfAngleDeg, float damage,
							float knockbackSpeed, bool applyBurn, float burnDuration,
							float stun = -1f, string cueAction = null, int cueVariant = 0,
							HitWeight weight = HitWeight.Auto)
	{
		int hits = 0;
		Vector2 origin = GlobalPosition;
		Vector2 aim = new Vector2(Facing, 0f);
		float halfRad = Mathf.DegToRad(halfAngleDeg);
		float scaledDamage = damage * DamageDealtMultiplier;
		HitInfo blow = new HitInfo(scaledDamage, Vector2.Zero, stun).From(this, cueAction, cueVariant, weight);
		var swing = new SwingStop();

		foreach (ICombatTarget e in CombatTargets.HostileTo(GetTree(), Side))
		{
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

			blow.Knockback = new Vector2(Facing, -0.35f).Normalized() * knockbackSpeed;
			float dealt = e.TakeHit(blow, origin);   // origin feeds crab Soft Shell
			swing.Landed(e, dealt, weight);
			if (applyBurn) e.SetBurning(burnDuration);
			if (dealt > 0f)
			{
				MeleeDamageDealt?.Invoke(e, dealt);
				ReportDamageDealt(dealt);
			}
			hits++;
		}
		ApplyHitStop(swing.AttackerStop());
		return hits;
	}

	/// <summary>
	/// What one swing of a body's own attack landed, for the attacker's share of hit-stop:
	/// feed it every victim the swing hit, then hold the attacker once for
	/// <see cref="AttackerStop"/>. A kit with its own melee shape (a box, a dash) uses this
	/// exactly as <see cref="MeleeCone"/> does. Projectiles, fields and ticks do not: a
	/// ranged hit never holds its owner.
	/// </summary>
	protected struct SwingStop
	{
		private HitWeight _weight;
		private float _score;
		private bool _killed;

		/// <summary>Note a victim this swing hit and the damage <c>TakeHit</c> returned.</summary>
		public void Landed(ICombatTarget victim, float dealt, HitWeight named = HitWeight.Auto)
		{
			if (victim == null || dealt <= 0f) return;   // a hit with no damage holds nobody
			float score = FeedbackTable.ImpactScore(dealt, victim.Side == CombatSide.Player, victim.MaxHP);
			HitWeight weight = FeedbackTable.Resolve(named, score);
			if (weight > _weight || (weight == _weight && score > _score))
			{
				_weight = weight;
				_score = score;
			}
			if (victim.CurrentHp <= 0f) _killed = true;
		}

		/// <summary>Seconds to hold the attacker for this swing (0 if it landed nothing).</summary>
		public readonly float AttackerStop() => _weight == HitWeight.Auto
			? 0f
			: FeedbackTable.AttackerStop(_weight, _score, _killed, FeedbackSettings.HitStop);
	}

	/// <summary>Report a non-melee hit dealt by this protagonist. Projectile and persistent-skill
	/// owners call this with the actual post-mitigation result returned by <see cref="BaseFoe.TakeHit"/>.</summary>
	public void ReportDamageDealt(float dealt)
	{
		if (dealt <= 0f) return;
		MarkCombatActivity();
		DamageDealt?.Invoke(dealt);
	}

	/// <summary>Reset the common out-of-combat clock after a valid damage exchange.</summary>
	protected void MarkCombatActivity() => CombatInactivitySeconds = 0f;

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
	public void TakeHit(HitInfo hit) => TakeHit(hit, GlobalPosition);

	/// <summary>
	/// The one intake for a discrete hit from the other side (<see cref="ICombatTarget"/>,
	/// T10 §4). Returns the damage the hit dealt after defense — what a shield then soaked
	/// still counts as dealt, exactly as it reads on the attacker's side.
	///
	/// A Player-side body is protected by its post-hit i-frames (0.8 s after a hit that
	/// shoved it). A Foe-side body has none — parity with <see cref="BaseFoe"/>: the player
	/// must never be gated to one hit per window — and credits the player's ult charge for
	/// the damage, as every foe does.
	/// </summary>
	public float TakeHit(HitInfo hit, Vector2 sourcePos)
	{
		bool playerSide = Side == CombatSide.Player;
		if (_dead || Invincible || (playerSide && _invulnTimer > 0f)) return 0f;
		if (hit.Damage > 0f) MarkCombatActivity();

		float dealt = hit.Damage * DefenseMultiplier;
		float toHp = AbsorbDamage(dealt);
		if (toHp > 0f) DirectDamageTaken?.Invoke(toHp);
		if (playerSide) DebugManager.Instance?.LogPlayerDmgReceived(hit.Damage, toHp, "foe");
		else DebugManager.Instance?.LogPlayerDmgDealt(dealt, GetType().Name);
		CurrentHP = Mathf.Max(0f, CurrentHP - toHp);
		NotifyHpChanged();
		float shown = playerSide ? toHp : dealt;
		PopNumber(shown, heal: false);
		GainUltCharge(toHp * UltChargeReceivedRate);

		Vector2 knockback = Trapped ? Vector2.Zero : hit.Knockback * ScaleOf(_knockbackTakenScales);
		AddImpulse(knockback);
		float stun = hit.ResolveStun() * ScaleOf(_stunTakenScales);
		if (stun > 0f) _stunTimer = Mathf.Max(_stunTimer, stun);

		if (playerSide)
		{
			if (hit.Knockback != Vector2.Zero) _invulnTimer = 0.8f;
		}
		else
		{
			// Single-player prototype: damage a Foe-side body takes came from the player.
			CharacterController player = LocalPlayer;
			player?.GainUltCharge(dealt * player.UltChargeDealtRate);
		}

		// Feedback (T10 §4 stage 6): resolve how heavy the hit was, hold this body for it,
		// and raise ONE cue. Shake, flash and everything else seen or heard is a listener's
		// business; nothing below reads anything back. A body that dies is not held — it has
		// its own exit.
		bool killed = CurrentHP <= 0f;
		float score = FeedbackTable.ImpactScore(dealt, playerSide, MaxHP);
		HitWeight weight = FeedbackTable.Resolve(hit.Weight, score);
		float held = killed ? 0f
			: ApplyHitStop(FeedbackTable.VictimStop(weight, score, playerSide, FeedbackSettings.HitStop));
		CueBus.EmitHit(new HitCue(this, hit, weight, shown, knockback, sourcePos, killed, held));

		if (killed) Die();
		return dealt;
	}

	private static float ScaleOf(Dictionary<string, float> sources)
	{
		float scale = 1f;
		foreach (float value in sources.Values) scale *= value;
		return scale;
	}

	/// <summary>Set or clear (scale 1) a named share of incoming knockback this body
	/// takes. Sources multiply. A boss's poise is one source; an item can be another.</summary>
	public void SetKnockbackTakenScale(string source, float scale) => SetScale(_knockbackTakenScales, source, scale);

	/// <summary>Set or clear (scale 1) a named share of incoming gain-no (hitstun).</summary>
	public void SetStunTakenScale(string source, float scale) => SetScale(_stunTakenScales, source, scale);

	private static void SetScale(Dictionary<string, float> sources, string source, float scale)
	{
		if (string.IsNullOrWhiteSpace(source)) return;
		if (Mathf.IsEqualApprox(scale, 1f)) sources.Remove(source);
		else sources[source] = Mathf.Max(0f, scale);
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
			MarkCombatActivity();
			float dealt = damage * DefenseMultiplier;
			dealt = AbsorbDamage(dealt);
			DebugManager.Instance?.LogHazardDmg(dealt, Side == CombatSide.Player ? "player" : GetType().Name);
			CurrentHP = Mathf.Max(0f, CurrentHP - dealt);
			NotifyHpChanged();
			PopNumber(dealt, heal: false);
			GainUltCharge(dealt * UltChargeReceivedRate);
			_hurtTicks.Add(this, HurtSource.Hazard, damage * DefenseMultiplier);   // a tick stops nobody
		}
		if (knockback != Vector2.Zero) AddImpulse(knockback);
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

	/// <summary>Set/clear a named, aggregatable damage-dealt bonus (+0.5 = half again as
	/// much; bonuses add, so 0.2 and 0.3 give 1.5×). OnFire and run ATK are sources already;
	/// a boss's level scaling is another.</summary>
	public void SetDamageDealtBonus(string source, float bonus)
	{
		if (string.IsNullOrWhiteSpace(source)) return;
		if (Mathf.IsZeroApprox(bonus)) _dealtDmgBonuses.Remove(source);
		else _dealtDmgBonuses[source] = bonus;
	}

	public void ClearDamageDealtBonus(string source) => _dealtDmgBonuses.Remove(source);

	/// <summary>Give this body a new maximum and fill it — a boss sized by its table row
	/// and the day's foe level. (A run's protagonist is sized by <see cref="HydrateRun"/>.)</summary>
	public void SetFullHealth(float maxHp)
	{
		MaxHP = Mathf.Max(1f, maxHp);
		CurrentHP = MaxHP;
		NotifyHpChanged();
	}

	// ── ICombatTarget (what the other side's attacks see) ─────────────────────
	float ICombatTarget.MaxHP => MaxHP;
	float ICombatTarget.CurrentHp => CurrentHP;
	public ulong TargetId => GetInstanceId();

	/// <summary>An authored execute (Cleopastar's Blackhole under 10% HP). Only a Foe-side
	/// body can be executed: nothing on the Foe side executes the player in this slice.</summary>
	public void Execute()
	{
		if (_dead || Invincible || Side == CombatSide.Player) return;
		Die();
	}

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

	/// <summary>Apply the canonical Trapped root. Unlike stun, this still permits skills.
	/// New external impulses are rejected while trapped; gravity remains active.</summary>
	public void ApplyTrapped(float duration)
	{
		if (_dead || Invincible || duration <= 0f) return;
		_trappedTimer = Mathf.Max(_trappedTimer, duration);
		_externalVel.X = 0f;
	}

	/// <summary>Add a delta-v impulse to the external-velocity channel (knockback/wind/…).</summary>
	public void AddImpulse(Vector2 impulse)
	{
		if (Trapped) return;
		_externalVel += impulse;
	}

	private void ApplyDotDamage(float amount, HurtSource source)
	{
		if (_dead || Invincible) return;
		if (amount > 0f) MarkCombatActivity();
		float dealt = amount * DefenseMultiplier;
		_hurtTicks.Add(this, source, dealt);
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
		bool wasReady = UltCharge >= MaxUltCharge;
		UltCharge = Mathf.Min(MaxUltCharge, UltCharge + amount);
		UltChargeChanged?.Invoke(UltCharge, MaxUltCharge);
		if (!wasReady && UltCharge >= MaxUltCharge) CueBus.Emit(this, "ult.ready");
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
		CueBus.Emit(this, "heal", magnitude: amount);
	}

	private void PopNumber(float amount, bool heal) =>
		DamageNumberManager.Instance?.Pop(
			GlobalPosition + new Vector2(0f, -Units.PlayerHeightPx * 0.5f), amount, heal);

	/// <summary>NOTE(feedback): the pre-cue way a kit shook the camera itself. The base no
	/// longer uses it — hits shake through <see cref="CueBus"/> and the feel director — and
	/// each remaining call (Pixolotl, Sifu Pangda) goes when that kit is given its cues
	/// (<c>Docs/Instructions/20-ENGINEERING.md</c> §4, "give an attack its feedback").</summary>
	protected void ShakeCamera(float amount) => CameraShake?.AddTrauma(amount);

	private void Die()
	{
		_dead = true;
		LivesRemaining = Mathf.Max(0, LivesRemaining - 1);
		Sprite.Modulate = new Color(0.4f, 0.4f, 0.4f);
		Velocity = _intentVel = _externalVel = Vector2.Zero;
		_stunTimer = 0f;
		_trappedTimer = 0f;
		IsBurning = false;
		_fire.Clear();
		_frozenDebuff.Clear();
		_dealtDmgBonuses.Clear();
		_defenseBonuses.Clear();
		_fireDurationExtensions.Clear();
		_frozenDurationExtensions.Clear();
		_continuousExternalVelocity.Clear();
		TempHP = 0f;
		_hitStop.Clear();
		_hurtTicks.Clear();
		_jumpBuffered = false;
		// The flash of the blow that killed is left to run out: it is the last thing the
		// body shows before its exit.
		_statusLoops = StatusLoops.Sync(this, _statusLoops, FeedbackState.None);
		CueBus.Emit(this, "death");
		Died?.Invoke();
		if (Side == CombatSide.Foe) OnDefeated();
	}

	/// <summary>Foe side only — the body's own exit once its HP is gone. There are no
	/// lives and no respawn on this side: it fades and frees itself. Whoever staged the
	/// fight listens to <see cref="Died"/>; nobody else has to clean the body up.</summary>
	protected virtual void OnDefeated()
	{
		// Nothing more can hit it. Deferred: the killing blow usually arrives inside a physics
		// callback (a projectile's body_entered), where collision state must not change.
		SetDeferred(CollisionObject2D.PropertyName.CollisionLayer, 0u);
		Tween fade = CreateTween();
		fade.TweenProperty(this, "modulate:a", 0f, DefeatFadeSeconds);
		fade.TweenCallback(Callable.From(QueueFree));
	}

	protected const float DefeatFadeSeconds = 1.2f;

	/// <summary>True once this fighter's HP is gone (until a Player-side respawn).</summary>
	public bool IsDefeated => _dead;

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
		_trappedTimer = 0f;
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
		_hitStop.Clear();
		_flash.Clear();
		_wasOnFloor = false;
		NotifyHpChanged();
		CueBus.Emit(this, "respawn");
	}

	// ── Feedback: identity, hit-stop, flash (Docs/Feedback.gdd §2, §4, §5) ───────────

	/// <summary>The content id this body's cues are raised under — snake_case, the key a
	/// director's table uses for its look and sound. Defaults to the class name; a kit states
	/// its own (a BOSS variant inherits its protagonist's: same kit, same style).</summary>
	public virtual string FeedbackId => _feedbackId ??= FeedbackIds.FromTypeName(GetType().Name);

	/// <summary>What this body's cues fall back to: a protagonist on the player's side is a
	/// <c>fighter</c>, the same class on the Foe side a <c>boss</c>.</summary>
	public virtual string FeedbackClass => Side == CombatSide.Player ? CueOwners.Fighter : CueOwners.Boss;

	public Vector2 FeedbackCenter => GlobalPosition;
	public float FeedbackRadius => HitRadius;
	/// <summary>Where this body's feet are, in world space (dust, landings).</summary>
	public Vector2 Feet => GlobalPosition + _feetOffset;

	/// <summary>True while hit-stop holds this body: it does not move and its animation
	/// keeps its frame; every clock and every button still runs.</summary>
	public bool InHitStop => _hitStop.Active;

	/// <summary>What the body is under right now — computed on request, never cached.</summary>
	public FeedbackState FeedbackState
	{
		get
		{
			if (_dead) return FeedbackState.Dead;
			FeedbackState state = StatusState();
			if (Shield > 0f || TempHP > 0f) state |= FeedbackState.Shielded;
			if (CurrentHP <= MaxHP * FeedbackTable.LowHpRatio) state |= FeedbackState.LowHp;
			if (!IsOnFloor()) state |= FeedbackState.Airborne;
			if (_softVolumes.InMedium) state |= FeedbackState.Submerged;
			if (Invincible || _invulnTimer > 0f) state |= FeedbackState.Invulnerable;
			return state;
		}
	}

	/// <summary>The statuses that run as loops on the cue bus (<see cref="StatusLoops"/>).</summary>
	private FeedbackState StatusState()
	{
		FeedbackState state = FeedbackState.None;
		if (IsBurning) state |= FeedbackState.Burning;
		if (_fire.Active) state |= FeedbackState.OnFire;
		if (_frozenDebuff.Active) state |= FeedbackState.Frozen;
		if (_trappedTimer > 0f) state |= FeedbackState.Trapped;
		if (_stunTimer > 0f) state |= FeedbackState.Stunned;
		return state;
	}

	/// <summary>
	/// Hold this body still for a moment — hit-stop. GAMEPLAY, not presentation: only the
	/// body itself (its hit intake, its own melee helper) may call it, never a cue listener.
	/// Stops take the max, a body just out of one is not stopped again at once, and a stream
	/// of them is budgeted (<see cref="HitStopClock"/>). Returns the seconds the body is held
	/// from now (0 if it is free).
	/// </summary>
	public float ApplyHitStop(float seconds) => _dead ? 0f : _hitStop.Request(seconds);

	/// <summary>
	/// Counts the hit-stop down by one physics tick of WORLD time and says whether the body
	/// must hold still for it. The base calls it first thing in <c>_PhysicsProcess</c>. A kit
	/// that moves the body WITHOUT going through the base that tick (PumpKing's Soul Form)
	/// calls it itself and returns when it says true; a kit that moves the body AFTER calling
	/// the base (Sifu Pangda's dive and dash) reads <see cref="HitStopHeld"/> instead — never
	/// both, or the stop runs out twice as fast.
	/// </summary>
	protected bool HoldForHitStop(float worldDelta)
	{
		if (!_hitStop.Tick(worldDelta)) return false;
		// Clocks keep running while the body holds: the jump cooldown is one of them.
		if (_jumpCdTimer > 0f) _jumpCdTimer -= GetActDelta(worldDelta);
		// It is not moving, and says so to whoever reads its Velocity; the two channels it
		// resumes from (_intentVel, _externalVel) are untouched.
		Velocity = Vector2.Zero;
		return true;
	}

	/// <summary>Whether this physics tick was held by hit-stop (set by <see cref="HoldForHitStop"/>).</summary>
	protected bool HitStopHeld => _hitStop.Held;

	/// <summary>Show the whole body in one colour for a moment and hand it back as it was.
	/// It writes neither <c>Modulate</c>, <c>SelfModulate</c> nor <c>Visible</c>: those stay
	/// with the status tints, the i-frame blink and the defeat fade.</summary>
	public void FlashTint(Color tint, float seconds) => _flash.Flash(this, tint, seconds);

	/// <summary>The arena swapped this body in for another (Tab): it arrives as
	/// <c>switch.in</c>, not as a fresh <c>spawn</c>.</summary>
	public void NotifySwitchedIn()
	{
		_spawnCueRaised = true;
		CueBus.Emit(this, "switch.in");
	}

	/// <summary>The arena is about to swap this body out.</summary>
	public void NotifySwitchedOut() => CueBus.Emit(this, "switch.out");

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

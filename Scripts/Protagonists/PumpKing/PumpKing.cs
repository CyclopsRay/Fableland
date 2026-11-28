using Godot;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// PumpKing — Tank/Guard. Port of the Unity character (see Docs/PumpKing.gdd).
///
/// PASSIVE  Pumpkin Shell — pumping up (E) builds a shield pool (max 100, 20/stack)
///          active only in Normal state; entering Rolling or Soul clears it. Each
///          stack also grows the head visually and applies a linear move-speed
///          penalty (0%→40% at 5 stacks).
/// BA       Normal: fire the head as a rolling projectile (magazine 1, 1.5s reload).
///          Rolling: detonate the head manually. Soul: no-op.
/// SHIFT    Soul Form (12s CD, 0.5s windup) — 6s free-flight ghost form; a live head
///          becomes autonomous (visual bounce only, no AoE on its own explosion).
/// E        Pump Up (0.5s inner CD, max 5 stacks) — Normal state only.
/// Q        The Black Prison (黑狱, a full ult ring) — a circle 8 m in radius where he aims
///          (8 m at most): 1 s of warning, then 4 s in which every foe inside is slowed, jumps
///          low, is silenced (skills and items) and blind past one cell, while pumpkins fly out of
///          its 3 m heart. Q again while it stands (free, once): its mouth opens, he blinks into
///          it and his head goes off there at its biggest. <see cref="BlackPrison"/>.
///
/// Animation automata mirrors the Unity "PumpKing" animator — only idle/walk/jump/
/// soul body clips exist in the source art (no dead/stun clip); the neck-head child
/// (NeckHead) is driven separately by <see cref="SyncNeckHead"/> + <see cref="RefreshHeadVisual"/>
/// rather than its own AnimationPlayer, since its "animation" is just a static per-stage
/// sprite + code-driven scale. `// NOTE(animation)` markers call out where a future
/// call-method track pass should move inline logic (mirrors the Pomegraknight/PumpKingHead
/// convention).
///
/// DEVIATIONS from the Unity source (called out inline where they occur):
///  - Soul Form entry resets pump stacks to 0 (not just the shield) — the GDD's Pump Up
///    section keeps shield == stacks×20 as an invariant; Unity only cleared the shield.
///  - No dead/stun body clip exists in the delivered art — death freezes the last frame
///    (base gray tint carries the "dead" read) and stun uses the base's universal
///    Anim.SpeedScale freeze, same as every other character.
///  - Soul free-flight ignores transient knockback impulses and gravity while active
///    (ghost form), while continuous environmental velocities still apply — damage
///    still lands normally via TakeHit.
/// </summary>
public partial class PumpKing : CharacterController
{
    public override string FeedbackId => "pumpking";

    public enum PKState { Normal, Rolling, Soul }
    public PKState CurrentState { get; private set; } = PKState.Normal;

    [Export] public PackedScene PumpKingHeadScene;

    [ExportGroup("Pump Up (Passive + E)")]
    [Export] public int MaxPumpStacks = 5;
    [Export] public float ShieldPerPump = 20f;
    [Export] public float PumpInnerCD = 0.5f;
    [Export] public float MaxMoveSpeedPenalty = 0.4f;

    [ExportGroup("Soul Form (Shift)")]
    [Export] public float SoulFormDuration = 6f;
    [Export] public float SoulWindupTime = 0.5f;
    [Export] public float SoulCooldown = 12f;
    [Export] public float SoulMoveSpeed = Units.Px(5f);   // 160 px/s

    [ExportGroup("Head / Explosion (BA)")]
    [Export] public float HeadLaunchSpeed = Units.Px(16f);    // 512 px/s (was 12 m/s; also PumpKingHead.MaxRollSpeed used to clamp this away on the very first tick — fixed there to only cap once grounded)
    [Export] public float ExplosionRadius = Units.Px(3f);     // 96 px
    [Export] public float ExplosionDamage = 50f;
    [Export] public float ExplosionKnockback = Units.Px(15f); // 480 px/s ⇒ ~96 px (3 m) travel vs foes' 1200 px/s² ExternalDamping — matches the blast radius

    // Designer knob: head visual scale per pump-stack count (index = stacks).
    [Export] public float[] HeadScales = { 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f };

    // ── Q — the Black Prison (PumpKing.gdd; first pass). The prison's own timings and what it
    // does to whoever is inside are BlackPrison's constants. ──
    /// <summary>How far from him the prison's centre may be: an aim farther off is pulled back
    /// along the same line. About where a level throw of his head comes down (7.7 m).</summary>
    public const float PrisonCastRangeM = 8f;
    /// <summary>The recast's blast is his head's at its biggest: the head's blast radius (the
    /// prison's <see cref="BlackPrison.Heart"/>) and this many times the head's damage. It shoves
    /// nobody: the prison keeps its prisoners.</summary>
    public const float UltBlastDamageScale = 2f;

    // Unity's headNeckOffset (0.6 m ⇒ 19 px) put the head at chest/waist height on
    // our sprites — the body art's collar sits ~28 px above origin (measured from the
    // idle frame's alpha bounds) and the head graphic's own half-height (~21.5 px at
    // scale 0.25) hangs below its anchor, so the neck anchor must sit above the collar
    // by that much: -48 px puts the chin right at the collar instead of at the waist.
    [Export] public Vector2 NeckOffset = new Vector2(0f, -48f);

    // Neck-head stage sub-sprites on pump_head_static_final.png (612x408), Godot regions.
    private static readonly Rect2[] NeckRegions =
    {
        new Rect2(26f, 18f, 152f, 172f),
        new Rect2(230f, 18f, 152f, 172f),
        new Rect2(429f, 19f, 155f, 171f),
        new Rect2(24f, 218f, 156f, 172f),
        new Rect2(227f, 218f, 155f, 172f),
        new Rect2(426f, 219f, 159f, 171f),
    };

    private int _pumpStacks;
    private float _shield;
    public override float Shield => _shield;
    private float _pumpCd;
    private float _soulCd;
    private float _soulWindup;
    private float _soulActive;
    private float _baseMoveSpeed;
    private float _baTelegraph;

    private PumpKingHead _activeHead;
    private BlackPrison _prison;
    private bool _neckShown = true;
    private Sprite2D _neckHead;
    private AtlasTexture _neckAtlas;
    private Vector2 _neckBaseScale;

    // ── Read-only kit state (the HUD, tests, and a BOSS variant's brain read these) ──
    public int PumpStacks => _pumpStacks;
    /// <summary>The head is on his neck and the magazine can fire it.</summary>
    public bool HeadReady => CurrentState == PKState.Normal && _activeHead == null && Ammo != null && Ammo.CanUse;
    /// <summary>A fired head is still out there, rolling.</summary>
    public bool HasLiveHead => _activeHead != null && IsInstanceValid(_activeHead) && !_activeHead.Exploded;
    /// <summary>Where the live head is (only meaningful while <see cref="HasLiveHead"/>).</summary>
    public Vector2 LiveHeadPosition => HasLiveHead ? _activeHead.GlobalPosition : GlobalPosition;
    public bool SoulReady => _soulCd <= 0f && _soulWindup <= 0f && CurrentState != PKState.Soul;
    public bool PumpReady => CurrentState == PKState.Normal && _pumpCd <= 0f && _pumpStacks < MaxPumpStacks;
    /// <summary>Soul Form has been cast and has not ended (windup included).</summary>
    public bool SoulCommitted => _soulWindup > 0f || CurrentState == PKState.Soul;
    /// <summary>Where a fired head leaves his body.</summary>
    public Vector2 HeadLaunchPoint => FirePoint != null ? FirePoint.GlobalPosition : GlobalPosition + NeckOffset;
    /// <summary>The Black Prison he opened, while it stands (warning included); null otherwise.</summary>
    public BlackPrison Prison => _prison != null && IsInstanceValid(_prison) && !_prison.Over ? _prison : null;
    /// <summary>Q would open a prison now: the ring is full and none of his stands.</summary>
    public bool UltReady => UltCharge >= MaxUltCharge && Prison == null;

    // HUD skill-icon cooldowns: Shift = Soul Form, E = Pump Up.
    public override (float Remaining, float Max) ShiftCooldown => (_soulCd, ScaleCooldown(SoulCooldown));
    public override (float Remaining, float Max) ESkillCooldown => (_pumpCd, ScaleCooldown(PumpInnerCD));

    /// <summary>The magazine this body uses. The BOSS variant answers with its own
    /// table row: it is not the protagonist (FOES.gdd §8).</summary>
    protected virtual CharacterAmmoDef AmmoDefinition =>
        CharacterTable.TryGetAmmo("PumpKing", out CharacterAmmoDef def) ? def : null;

    protected override void InitCharacter()
    {
        MaxJumps = 1;
        MoveSpeed = Units.Px(8f);
        _baseMoveSpeed = MoveSpeed;
        ConfigureAmmo(AmmoDefinition, "PumpKing");

        _neckHead = GetNode<Sprite2D>("NeckHead");
        _neckBaseScale = _neckHead.Scale;

        // The NeckHead's AtlasTexture is a shared resource — mutating .Region in place
        // would corrupt every PumpKing instance sharing it. Duplicate once per-instance.
        _neckAtlas = (AtlasTexture)_neckHead.Texture.Duplicate();
        _neckHead.Texture = _neckAtlas;

        RefreshHeadVisual(0);
    }

    // Background-CD persistence (NODES §3.3): save/load per-skill cooldown remaining
    // so benched protagonists' cooldowns tick down at the reduced rate.
    public override void SaveCooldownsToState(ProtagonistState p)
    {
        if (p == null) return;
        p.ShiftCdRemaining = _soulCd;
        p.ESkillCdRemaining = _pumpCd;
    }

    public override void LoadCooldownsFromState(ProtagonistState p)
    {
        if (p == null) return;
        _soulCd = p.ShiftCdRemaining;
        _pumpCd = p.ESkillCdRemaining;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        TickTimers((float)delta);
    }

    private void TickTimers(float dt)
    {
        if (_pumpCd > 0f) _pumpCd -= dt;
        if (_soulCd > 0f) _soulCd -= dt;
        if (_baTelegraph > 0f) _baTelegraph -= dt;

        if (_soulWindup > 0f)
        {
            _soulWindup -= dt;
            if (_soulWindup <= 0f) EnterSoul();
        }

        if (_soulActive > 0f)
        {
            _soulActive -= dt;
            if (_soulActive <= 0f) ExitSoul();
        }
    }

    protected override void OnAmmoReloaded(int restored)
    {
        _neckShown = true;
        RefreshHeadVisual(_pumpStacks);   // stacks are 0 after any fire, but stay faithful to state
    }

    // ── BA — Fire / Detonate Head ───────────────────────────────────────────
    protected override void HandleBA()
    {
        switch (CurrentState)
        {
            case PKState.Normal:
                if (_activeHead == null && PumpKingHeadScene != null && TryConsumeAmmo())
                    FireHead(ResolveLaunchDirection());
                break;
            case PKState.Rolling:
                if (IsInstanceValid(_activeHead) && !_activeHead.Exploded) _activeHead.Explode(manual: true);
                break;
            case PKState.Soul:
                break;
        }
    }

    private void FireHead(Vector2 dir)
    {
        if (PumpKingHeadScene == null) return;

        // Capture the visual scale BEFORE resetting stacks below.
        float scale = HeadScales[Mathf.Clamp(_pumpStacks, 0, HeadScales.Length - 1)];

        var head = PumpKingHeadScene.Instantiate<PumpKingHead>();
        GetParent().AddChild(head);
        head.GlobalPosition = HeadLaunchPoint;
        // Carry PumpKing's own momentum into the throw (a head fired while running
        // forward should fly farther than one fired standing still). The head fights on
        // his side: the player's PumpKing bowls at foes, the BOSS bowls at the player.
        head.Init(dir * HeadLaunchSpeed + Velocity, this, scale, DamageDealtMultiplier,
            ExplosionRadius, ExplosionDamage * DamageDealtMultiplier, ExplosionKnockback,
            HandleHeadResolved, Side);

        _activeHead = head;
        _pumpStacks = 0;
        _shield = 0f;
        NotifyHpChanged();
        RefreshPassive();
        RefreshHeadVisual(0);
        _neckShown = false;
        _baTelegraph = 0.15f;
        CurrentState = PKState.Rolling;
    }

    /// <summary>Callback from every resolved head explosion. Damage is resolved by the
    /// surviving head itself, so this state-only callback remains safe after Tab removes
    /// the PumpKing body; it still starts the shared reload in persisted run state.</summary>
    private void HandleHeadResolved()
    {
        _activeHead = null;
        RequestAmmoReload();
        if (CurrentState == PKState.Rolling) CurrentState = PKState.Normal;
    }

    // ── Shift — Soul Form ────────────────────────────────────────────────────
    protected override void HandleSkill1()
    {
        if (_soulCd > 0f || CurrentState == PKState.Soul) return;

        _soulCd = ScaleCooldown(SoulCooldown);
        if (_activeHead != null && IsInstanceValid(_activeHead))
        {
            _activeHead.SetAutonomous(true);
            // Deliberately NOT nulling _activeHead here: ExitSoul() needs it to tell a
            // still-live released head apart from "no head" (see KNOWLEDGE.md caveat).
        }
        _soulWindup = SoulWindupTime;   // normal control persists during windup
    }

    private void EnterSoul()
    {
        // DEVIATION from Unity (deliberate): Unity cleared only the shield on Soul
        // entry and let pump stacks persist; the GDD's Pump Up section says stacks
        // reset too, so we follow the GDD to keep the shield == stacks×20 invariant.
        _pumpStacks = 0;
        _shield = 0f;
        NotifyHpChanged();
        RefreshPassive();
        RefreshHeadVisual(0);
        CurrentState = PKState.Soul;
        _soulActive = SoulFormDuration;
    }

    private void ExitSoul()
    {
        // A head released autonomous on Shift press keeps rolling/bouncing on its own
        // for the whole 6 s Soul Form — it may still be live (→ Rolling, BA can detonate
        // it per the GDD) or may already have auto-exploded via its own still-timer/
        // lifetime while we were away (→ Normal; its callback already started reload).
        CurrentState = (_activeHead != null && IsInstanceValid(_activeHead) && !_activeHead.Exploded)
            ? PKState.Rolling : PKState.Normal;

        if (CurrentState == PKState.Normal)
        {
            // The normal-explosion callback clears this eagerly. Keep the defensive
            // cleanup for a head freed externally without reaching its callback.
            _activeHead = null;
        }
    }

    // ── E — Pump Up ──────────────────────────────────────────────────────────
    protected override void HandleSkill2()
    {
        if (CurrentState != PKState.Normal) return;
        if (_pumpStacks >= MaxPumpStacks) return;
        if (_pumpCd > 0f) return;   // pumping during reload IS allowed, as in Unity

        _pumpCd = ScaleCooldown(PumpInnerCD);
        _pumpStacks++;
        _shield = Mathf.Min(_shield + ShieldPerPump, MaxPumpStacks * ShieldPerPump);
        NotifyHpChanged();
        RefreshPassive();
        RefreshHeadVisual(_pumpStacks);
    }

    // ── Q — the Black Prison (黑狱) ──────────────────────────────────────────
    /// <summary>
    /// With a full ring and no prison of his standing: open one where he aims, at most
    /// <see cref="PrisonCastRangeM"/> away, and spend the whole ring. While his prison stands:
    /// the recast — free, once, only after its warning (<see cref="BlackPrison.Recast"/>). One
    /// prison at a time: a full ring waits for the standing one to end.
    /// </summary>
    protected override void HandleSkillUlt()
    {
        BlackPrison standing = Prison;
        if (standing != null)
        {
            // NOTE(animation): the recast's windup clip (he is drawn toward the mouth) starts here.
            if (standing.Recast()) CueBus.Emit(this, "ult.recast", magnitude: BlackPrison.MouthSeconds,
                direction: (standing.Centre - GlobalPosition).Normalized());
            return;
        }
        if (UltCharge < MaxUltCharge || GetParent() == null) return;

        Vector2 centre = ClampPrisonCentre(GlobalPosition, Controls.AimPoint(this), Units.Px(PrisonCastRangeM));
        DrainUltCharge(UltCharge);
        var prison = new BlackPrison { Name = "BlackPrison" };
        GetParent().AddChild(prison);
        prison.Begin(this, centre, Units.Px(BlackPrison.RadiusM), ExplosionRadius, DamageDealtMultiplier);
        _prison = prison;
        // NOTE(animation): the cast clip (he reaches toward the circle) starts here.
        CueBus.Emit(this, "ult.cast", size: prison.Radius, magnitude: BlackPrison.WarnSeconds,
            direction: (centre - GlobalPosition).Normalized());
    }

    /// <summary>Where a prison aimed at <paramref name="aim"/> from <paramref name="from"/> goes:
    /// the aim itself, or the point <paramref name="range"/> along the same line when the aim is
    /// farther. Static so a brain (and a check) can ask before pressing.</summary>
    public static Vector2 ClampPrisonCentre(Vector2 from, Vector2 aim, float range)
    {
        Vector2 to = aim - from;
        return to.Length() <= range ? aim : from + to.Normalized() * range;
    }

    /// <summary>
    /// The recast lands (<see cref="BlackPrison"/>, when its mouth has opened): he blinks into his
    /// mouth at the centre of the prison's horror world — into the realm if he stood outside it,
    /// across it if inside — on the nearest spot his body fits, arriving still. His head goes off
    /// there at its biggest: his head's blast radius (the prison's heart), <see cref="UltBlastDamageScale"/>×
    /// the head's damage, no shove. It costs no head: the magazine is not touched.
    /// </summary>
    public void LandInPrisonMouth(BlackPrison prison)
    {
        Fableland.Realms.RealmInstance realm = prison?.Realm;
        if (realm == null) return;
        // Where he blinks from, in the prison's own world: from outside, his projected spot.
        Vector2 from = realm.Holds(this) ? GlobalPosition : realm.ToSub(GlobalPosition);
        Vector2 mouth = realm.Space.Anchor;
        if (!realm.Admit(this, mouth)) return;
        Vector2 spot = GlobalPosition;
        RestoreMovementVelocityState(Vector2.Zero, Vector2.Zero);
        CueBus.Emit(this, "ult.blink", magnitude: from.DistanceTo(spot), direction: (spot - from).Normalized(), at: spot);

        float damage = ExplosionDamage * UltBlastDamageScale * DamageDealtMultiplier;
        CueBus.EmitAt(FeedbackId, "ult.blast", mouth, this, Side, size: prison.Heart, magnitude: damage,
            ownerClass: FeedbackClass);
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
        {
            if (target.GlobalPosition.DistanceTo(mouth) - target.HitRadius > prison.Heart) continue;
            ReportDamageDealt(target.TakeHit(new HitInfo(damage, Vector2.Zero).FromRanged(this, "ult.blast"), mouth));
        }
    }

    private void RefreshPassive()
    {
        float t = (float)_pumpStacks / MaxPumpStacks;
        MoveSpeed = _baseMoveSpeed * (1f - MaxMoveSpeedPenalty * t);
    }

    /// <summary>Shield absorbs damage after DefenseMultiplier and TempHP, before HP
    /// loss. Only ever non-zero in Normal state — Fire/Soul clear it.</summary>
    protected override float AbsorbDamage(float damage)
    {
        damage = base.AbsorbDamage(damage);   // TempHP absorbs first
        if (_shield <= 0f) return damage;
        float absorbed = Mathf.Min(_shield, damage);
        _shield -= absorbed;
        NotifyHpChanged();
        return damage - absorbed;
    }

    private void RefreshHeadVisual(int stacks)
    {
        int i = Mathf.Clamp(stacks, 0, NeckRegions.Length - 1);
        _neckAtlas.Region = NeckRegions[i];
        _neckHead.Scale = _neckBaseScale * HeadScales[i];
    }

    // ── Soul free-flight ─────────────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        if (CurrentState == PKState.Soul)
        {
            // Soul Form flies on its own and never reaches the base tick, so it counts its
            // hit-stop down here: a stopped soul holds like any other body (Feedback.gdd §4).
            if (HoldForHitStop((float)delta)) return;
            SoulFlight((float)delta);
            return;
        }
        base._PhysicsProcess(delta);
    }

    private void SoulFlight(float dt)
    {
        float h = 0f, v = 0f;
        if (!Dead && !ControlsLocked)
        {
            if (Controls.Held(ControlAction.MoveLeft)) h -= 1f;
            if (Controls.Held(ControlAction.MoveRight)) h += 1f;
            if (Controls.Held(ControlAction.Jump)) v -= 1f;        // Space = up
            if (Controls.Held(ControlAction.MoveDown)) v += 1f;    // S = down
        }

        // Accepted simplification: during Soul, transient knockback/gravity don't
        // apply; source-keyed environmental movement (such as a Blackhole) does. A soul flies
        // at the share of speed the rules leave a walker (a Black Prison, a seal): flying is
        // moving too.
        Vector2 dir = new Vector2(h, v);
        Velocity = (dir != Vector2.Zero ? dir.Normalized() * SoulMoveSpeed * MoveSpeedScale : Vector2.Zero)
            + ContinuousExternalVelocity;
        MoveAndSlide();

        if (h > 0.01f) { Facing = 1f; Sprite.FlipH = false; }
        else if (h < -0.01f) { Facing = -1f; Sprite.FlipH = true; }
        else if (!Dead && !ControlsLocked) FaceByHint();
    }

    /// <summary>World-space aim constrained to two firing segments — front (−60°..60°)
    /// and back (120°..240°), Y-up convention. Clamps to the nearest segment edge when
    /// the raw mouse aim falls in a forbidden gap.</summary>
    private Vector2 ResolveLaunchDirection()
    {
        return ClampToFiringSegments(Controls.AimPoint(this) - GlobalPosition, Facing);
    }

    /// <summary>The firing-segment rule on its own (front −60°..60°, back 120°..240°,
    /// Y-up): an aim inside a segment is kept, one in a gap snaps to the nearer edge.
    /// Static so a brain can ask what a throw will really be before it commits to it.</summary>
    public static Vector2 ClampToFiringSegments(Vector2 v, float facing)
    {
        if (v.LengthSquared() < 0.0001f) return new Vector2(facing, 0f);

        float deg = Mathf.RadToDeg(Mathf.Atan2(-v.Y, v.X));   // Y-up degrees, -180..180

        bool front = deg >= -60f && deg <= 60f;
        bool back = deg >= 120f || deg <= -120f;
        if (front || back) return v.Normalized();

        float snapDeg;
        if (deg > 60f && deg < 120f)          // upper gap
            snapDeg = (deg - 60f <= 120f - deg) ? 60f : 120f;
        else                                  // lower gap: -120 < deg < -60
            snapDeg = (deg - (-120f) <= -60f - deg) ? -120f : -60f;

        float rad = Mathf.DegToRad(snapDeg);
        return new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));
    }

    // ── Animation automata ───────────────────────────────────────────────────
    protected override void UpdateAnimator(float dt)
    {
        if (Dead) { Anim?.Pause(); return; }   // no dead clip in the source art; freeze + base gray tint

        SyncNeckHead();

        if (CurrentState == PKState.Soul) { PlayAnim("soul"); return; }
        if (!IsOnFloor()) { PlayAnim("jump"); return; }

        if (Mathf.Abs(Velocity.X) > Units.Px(0.25f)) PlayAnim("walk");
        else PlayAnim("idle");
    }

    // Note: the neck head stays visible during Soul if it was never fired — faithful
    // to the Unity source; whether it should hide during Soul too is an open art-pass
    // question, not resolved here.
    private void SyncNeckHead()
    {
        _neckHead.FlipH = Sprite.FlipH;
        _neckHead.Visible = _neckShown && Sprite.Visible;
        _neckHead.SelfModulate = Sprite.SelfModulate;
    }

    // ── Range telegraphs ─────────────────────────────────────────────────────
    protected override void DrawDebug()
    {
        bool hot = _baTelegraph > 0f;
        Color col = hot ? new Color(0.2f, 0.9f, 1f, 0.95f) : new Color(0.2f, 0.9f, 1f, 0.35f);
        float w = hot ? 4f : 2f;

        // Both firing segments (front + back), vertically symmetric so Y-down flip
        // maps them onto themselves.
        DrawConeLocal(ExplosionRadius, 0f, Mathf.DegToRad(60f), col, w);
        DrawConeLocal(ExplosionRadius, Mathf.Pi, Mathf.DegToRad(60f), col, w);

        // Explosion radius ring around self.
        DrawArc(Vector2.Zero, ExplosionRadius, 0f, Mathf.Tau, 48, new Color(1f, 0.5f, 0.1f, 0.4f), 2f);

        // TODO(art): the Black Prison's aim preview (PumpKing.gdd). While Q would open one: where
        // it would go, and how far it can. The player's only: a boss's aim is his brain's business.
        if (UltReady && !Dead && Side == CombatSide.Player)
        {
            Vector2 centre = ClampPrisonCentre(GlobalPosition, Controls.AimPoint(this), Units.Px(PrisonCastRangeM)) - GlobalPosition;
            DrawArc(centre, Units.Px(BlackPrison.RadiusM), 0f, Mathf.Tau, 160, new Color(0.85f, 0.2f, 0.23f, 0.7f), 2f);
            DrawArc(centre, ExplosionRadius, 0f, Mathf.Tau, 48, new Color(0.85f, 0.2f, 0.23f, 0.35f), 1f);   // its heart
            DrawArc(Vector2.Zero, Units.Px(PrisonCastRangeM), 0f, Mathf.Tau, 96, new Color(0.85f, 0.2f, 0.23f, 0.18f), 1f);
        }
    }
}

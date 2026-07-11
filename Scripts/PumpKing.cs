using Godot;

/// <summary>
/// PumpKing — Tank/Guard. Port of the Unity character (see Docs/PumpKing.gdd).
///
/// PASSIVE  Pumpkin Shell — pumping up (E) builds a shield pool (max 100, 20/stack)
///          active only in Normal state; entering Rolling or Soul clears it. Each
///          stack also grows the head visually and applies a linear move-speed
///          penalty (0%→40% at 5 stacks).
/// BA       Normal: fire the head as a rolling projectile (magazine 1, 0.5s reload).
///          Rolling: detonate the head manually. Soul: no-op.
/// SHIFT    Soul Form (12s CD, 0.5s windup) — 6s free-flight ghost form; a live head
///          becomes autonomous (visual bounce only, no AoE on its own explosion).
/// E        Pump Up (0.5s inner CD, max 5 stacks) — Normal state only.
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
///  - Soul free-flight ignores external knockback impulses and gravity while active
///    (ghost form; CharacterController's velocity channels are private) — damage still
///    lands normally via TakeHit.
/// </summary>
public partial class PumpKing : CharacterController
{
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
    [Export] public float HeadLaunchSpeed = Units.Px(12f);    // 384 px/s
    [Export] public float ExplosionRadius = Units.Px(3f);     // 96 px
    [Export] public float ExplosionDamage = 50f;
    [Export] public float ExplosionKnockback = Units.Px(9f);  // 288 px/s
    [Export] public float ReplenishWindup = 0.5f;

    // Designer knob: head visual scale per pump-stack count (index = stacks).
    [Export] public float[] HeadScales = { 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f };

    // ~0.6 m up (Unity headNeckOffset) ⇒ 19 px.
    [Export] public Vector2 NeckOffset = new Vector2(0f, -19f);

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
    private float _pumpCd;
    private float _reloadTimer;
    private int _ammo;
    private float _soulCd;
    private float _soulWindup;
    private float _soulActive;
    private float _baseMoveSpeed;
    private float _baTelegraph;

    private PumpKingHead _activeHead;
    private bool _neckShown = true;
    private Sprite2D _neckHead;
    private AtlasTexture _neckAtlas;
    private Vector2 _neckBaseScale;

    // HUD skill-icon cooldowns: Shift = Soul Form, E = Pump Up.
    public override (float Remaining, float Max) ShiftCooldown => (_soulCd, SoulCooldown);
    public override (float Remaining, float Max) ESkillCooldown => (_pumpCd, PumpInnerCD);

    protected override void InitCharacter()
    {
        MaxJumps = 1;
        MoveSpeed = Units.Px(8f);
        _baseMoveSpeed = MoveSpeed;
        _ammo = 1;

        _neckHead = GetNode<Sprite2D>("NeckHead");
        _neckBaseScale = _neckHead.Scale;

        // The NeckHead's AtlasTexture is a shared resource — mutating .Region in place
        // would corrupt every PumpKing instance sharing it. Duplicate once per-instance.
        _neckAtlas = (AtlasTexture)_neckHead.Texture.Duplicate();
        _neckHead.Texture = _neckAtlas;

        RefreshHeadVisual(0);
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

        if (_reloadTimer > 0f)
        {
            _reloadTimer -= dt;
            if (_reloadTimer <= 0f) OnHeadReloaded();
        }

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

    private void OnHeadReloaded()
    {
        _ammo = 1;
        _neckShown = true;
        RefreshHeadVisual(_pumpStacks);   // stacks are 0 after any fire, but stay faithful to state
    }

    // ── BA — Fire / Detonate Head ───────────────────────────────────────────
    protected override void HandleBA()
    {
        switch (CurrentState)
        {
            case PKState.Normal:
                if (_activeHead == null && _ammo > 0) FireHead(ResolveLaunchDirection());
                break;
            case PKState.Rolling:
                if (IsInstanceValid(_activeHead) && !_activeHead.Exploded) _activeHead.Explode();
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
        head.GlobalPosition = FirePoint != null ? FirePoint.GlobalPosition : GlobalPosition + NeckOffset;
        head.Init(dir * HeadLaunchSpeed, this, scale, DamageDealtMultiplier, HandleHeadExplosion);

        _activeHead = head;
        _pumpStacks = 0;
        _shield = 0f;
        RefreshPassive();
        RefreshHeadVisual(0);
        _neckShown = false;
        _ammo = 0;
        _baTelegraph = 0.15f;
        CurrentState = PKState.Rolling;
    }

    /// <summary>onExplode callback from a non-autonomous head — AoE + reload.</summary>
    private void HandleHeadExplosion(Vector2 pos)
    {
        foreach (Node n in GetTree().GetNodesInGroup("enemy"))
        {
            if (n is not BaseFoe e) continue;
            Vector2 to = e.GlobalPosition - pos;
            float dist = to.Length();
            if (dist - e.HitRadius > ExplosionRadius) continue;

            Vector2 knock = (dist > 0.01f ? to / dist : Vector2.Right) * ExplosionKnockback;
            e.TakeHit(new HitInfo(ExplosionDamage * DamageDealtMultiplier, knock), pos);
        }

        ShakeCamera(0.35f);
        _activeHead = null;
        _reloadTimer = ReplenishWindup;
        if (CurrentState == PKState.Rolling) CurrentState = PKState.Normal;
    }

    // ── Shift — Soul Form ────────────────────────────────────────────────────
    protected override void HandleSkill1()
    {
        if (_soulCd > 0f || CurrentState == PKState.Soul) return;

        _soulCd = SoulCooldown;
        if (_activeHead != null && IsInstanceValid(_activeHead))
        {
            _activeHead.SetAutonomous(true);
        }
        _activeHead = null;
        _soulWindup = SoulWindupTime;   // normal control persists during windup
    }

    private void EnterSoul()
    {
        // DEVIATION from Unity (deliberate): Unity cleared only the shield on Soul
        // entry and let pump stacks persist; the GDD's Pump Up section says stacks
        // reset too, so we follow the GDD to keep the shield == stacks×20 invariant.
        _pumpStacks = 0;
        _shield = 0f;
        RefreshPassive();
        RefreshHeadVisual(0);
        CurrentState = PKState.Soul;
        _soulActive = SoulFormDuration;
    }

    private void ExitSoul()
    {
        // In practice always Normal — the head was released as autonomous on Shift
        // press — but the check is kept as a faithful port of Unity's structure.
        CurrentState = (_activeHead != null && IsInstanceValid(_activeHead) && !_activeHead.Exploded)
            ? PKState.Rolling : PKState.Normal;

        if (CurrentState == PKState.Normal && _ammo < 1 && _reloadTimer <= 0f)
            _reloadTimer = ReplenishWindup;
    }

    // ── E — Pump Up ──────────────────────────────────────────────────────────
    protected override void HandleSkill2()
    {
        if (CurrentState != PKState.Normal) return;
        if (_pumpStacks >= MaxPumpStacks) return;
        if (_pumpCd > 0f) return;   // pumping during reload IS allowed, as in Unity

        _pumpCd = PumpInnerCD;
        _pumpStacks++;
        _shield = Mathf.Min(_shield + ShieldPerPump, MaxPumpStacks * ShieldPerPump);
        RefreshPassive();
        RefreshHeadVisual(_pumpStacks);
    }

    protected override void HandleSkillUlt() { }

    private void RefreshPassive()
    {
        float t = (float)_pumpStacks / MaxPumpStacks;
        MoveSpeed = _baseMoveSpeed * (1f - MaxMoveSpeedPenalty * t);
    }

    /// <summary>Shield absorbs damage after DefenseMultiplier, before HP loss. Only
    /// ever non-zero in Normal state — Fire/Soul clear it. No HUD binding yet.</summary>
    protected override float AbsorbDamage(float damage)
    {
        if (_shield <= 0f) return damage;
        float absorbed = Mathf.Min(_shield, damage);
        _shield -= absorbed;
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
        if (CurrentState == PKState.Soul) { SoulFlight((float)delta); return; }
        base._PhysicsProcess(delta);
    }

    private void SoulFlight(float dt)
    {
        float h = 0f, v = 0f;
        if (!Dead && !ControlsLocked)
        {
            if (Input.IsActionPressed("move_left")) h -= 1f;
            if (Input.IsActionPressed("move_right")) h += 1f;
            if (Input.IsActionPressed("jump")) v -= 1f;        // Space = up
            if (Input.IsActionPressed("move_down")) v += 1f;   // S = down
        }

        // Accepted simplification: during Soul, external knockback/gravity don't
        // apply (ghost form; the base's intent/external velocity channels are
        // private) — damage still lands via TakeHit normally.
        Vector2 dir = new Vector2(h, v);
        Velocity = dir != Vector2.Zero ? dir.Normalized() * SoulMoveSpeed : Vector2.Zero;
        MoveAndSlide();

        if (h > 0.01f) { Facing = 1f; Sprite.FlipH = false; }
        else if (h < -0.01f) { Facing = -1f; Sprite.FlipH = true; }
    }

    /// <summary>World-space aim constrained to two firing segments — front (−60°..60°)
    /// and back (120°..240°), Y-up convention. Clamps to the nearest segment edge when
    /// the raw mouse aim falls in a forbidden gap.</summary>
    private Vector2 ResolveLaunchDirection()
    {
        Vector2 v = GetGlobalMousePosition() - GlobalPosition;
        if (v.LengthSquared() < 0.0001f) return new Vector2(Facing, 0f);

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
    }
}

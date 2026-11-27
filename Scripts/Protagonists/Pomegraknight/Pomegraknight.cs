using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fableland.Data;
using Fableland.Run;

/// <summary>
/// Pomegraknight — Skirmisher. Port of the Unity character. Sprite animations
/// are wired via <see cref="UpdateAnimator"/> and the AnimationLibrary baked into
/// Pomegraknight.tscn; BA/seed damage still fires inline (see the
/// NOTE(animation) comment in HandleBA) rather than off an AnimationPlayer
/// call-method track — a later pass can move it.
///
/// PASSIVE  Pomegranate Shell — immune to burn DoT. While burning: combo deals
///          1.5x and E fires burning seeds.
/// BA       3-stage melee combo (15/15/30), magazine 3, 0.25s between stages,
///          0.75s reload. 80 cone, 4m range, knockback + 30% move penalty 0.4s.
/// SHIFT    Blush (10s CD) — self-ignite 5s (activates passive) + a 3s Fire
///          Tornado charge; the next BA in that window spawns a 2.5s spin AoE.
/// E        Pome Seed Eruption (9s CD) — 3 waves (6/8/10) of gravity seeds in a
///          50-130 world cone; 30 first hit / 6 subsequent per wave per target.
///
/// Debug: BA cone, E launch cone, and Fire Tornado box are drawn as range lines
/// (toggle with ShowDebugRanges).
///
/// FEEDBACK — THE REFERENCE KIT (<c>Docs/Feedback.gdd</c> §6, 20-ENGINEERING §4): every
/// moment of this kit raises a cue with the numbers the attack itself uses, and every hit
/// names itself. Copy this file's pattern when wiring another kit:
///   ba.swing #1–3 (size = the cone's reach, tag "burning")      hits: From(this, "ba", stage)
///   s1.cast                                                      Blush
///   s1.tornado  loop (extents = the box) + s1.tornado.tick       hits: FromRanged(this, "s1.tornado")
///   s2.cast, then s2.wave #1–3 (magnitude = seeds in the fan)    seeds: FromRanged(owner, "s2.seed")
///   s2.seed.expire, at a seed that died on terrain               (raised by PomeSeed)
/// </summary>
public partial class Pomegraknight : CharacterController
{
    public override string FeedbackId => "pomegraknight";

    [ExportGroup("BA — Melee Combo")]
    [Export] public float SlashRange = 130f;       // ~4m at 32px/m
    [Export] public float SlashHalfAngle = 40f;    // full 80 cone
    [Export] public float SlashKnockback = 96f;    // ~3 m/s
    [Export] public float SwingPenaltyMult = 0.7f; // 30% slower
    [Export] public float SwingPenaltyDur = 0.4f;

    [ExportGroup("Blush + Fire Tornado (Skill 1)")]
    [Export] public float BlushDuration = 5f;
    [Export] public float BlushCooldown = 10f;
    [Export] public float TornadoChargeWindow = 3f;
    [Export] public float TornadoDuration = 2.5f;
    [Export] public float TornadoTick = 0.5f;
    [Export] public float TornadoDamage = 15f;
    [Export] public float TornadoHalfW = 128f;     // 8m wide — much bigger than her sideways
    [Export] public float TornadoHalfH = 40f;      // 2.5m tall — slightly bigger than her (2m)
    [Export] public float TornadoPush = 64f;       // ~2 m/s — a slight knockoff, not a launch
    [Export] public float TornadoDefense = 66.7f;  // dmg mult 100/(100+66.7) ≈ 0.6 while active

    [ExportGroup("Pome Seed Eruption (Skill 2)")]
    [Export] public PackedScene PomeSeedScene;
    [Export] public float ESeedSpeed = 320f;
    [Export] public float ECooldown = 9f;
    [Export] public float EConeMinDeg = 50f;       // world-space, CCW from +X (Y up)
    [Export] public float EConeMaxDeg = 130f;
    [Export] public float EDrawRange = 200f;
    private readonly int[] _seedsPerWave = { 6, 8, 10 };

    // Combo (15/15/30) — passive multiplies by 1.5 while burning.
    private static readonly float[] ComboDamage = { 15f, 15f, 30f };
    private const float BurnComboMult = 1.5f;

    // Animation automata (mirror of the Unity "Pomegraknight 1" animator, minus
    // the states we deliberately don't drive — see UpdateAnimator below).
    //
    // One-shot clips: once triggered, they play to completion and are not
    // interrupted by the idle/walk/jump fallback while still IsPlaying(). "dead"
    // and "jump" are NOT in this set — they're state-held instead (PlayAnim's
    // same-name guard keeps them from restarting, and the sprite just holds
    // their final frame once the non-looping clip runs out).
    //
    // "stun" and "levelup" are authored in the library (Deliverable 1) but are
    // NOT driven here: stun because the gain-no canon freezes the current frame
    // in place instead of playing a dedicated stun clip (CharacterController
    // handles this via Anim.SpeedScale), and levelup because in-run leveling was
    // dropped in v0.3.7 (see KNOWLEDGE/GDD history) — there is no trigger left
    // to fire it from.
    private static readonly HashSet<string> OneShots = new()
    {
        "slash1", "slash2", "slash3",
        "slash1_burning", "slash2_burning", "slash3_burning",
        "ignite", "erupt", "levelup",
    };

    // Unity used Speed > 0.1 (m/s) as the walk threshold; derive the equivalent
    // in px/s from Units rather than hardcoding a raw pixel constant.
    private const float WalkSpeedThresholdMps = 0.25f;

    private int _comboStage;
    private float _baTelegraph;

    private float _blushCd;
    private float _tornadoCharge;
    private float _tornadoActive;
    private float _tornadoTickTimer;
    private int _tornadoTicks;
    private int _eruptionWave;

    private float _eCd;
    private float _eTelegraph;

    // HUD skill-icon cooldowns: Shift = Blush, E = Pome Seed Eruption.
    public override (float Remaining, float Max) ShiftCooldown => (_blushCd, ScaleCooldown(BlushCooldown));
    public override (float Remaining, float Max) ESkillCooldown => (_eCd, ScaleCooldown(ECooldown));

    protected override void InitCharacter()
    {
        BurnImmune = true;            // Pomegranate Shell
        MaxJumps = 1;                 // Pomegraknight: single jump
        ConfigureAmmo("Pomegraknight");
    }

    protected override void OnAmmoReloaded(int restored) => _comboStage = 0;

    // Background-CD persistence (NODES §3.3): save/load per-skill cooldown remaining
    // so benched protagonists' cooldowns tick down at the reduced rate.
    public override void SaveCooldownsToState(ProtagonistState p)
    {
        if (p == null) return;
        p.ShiftCdRemaining = _blushCd;
        p.ESkillCdRemaining = _eCd;
    }

    public override void LoadCooldownsFromState(ProtagonistState p)
    {
        if (p == null) return;
        _blushCd = p.ShiftCdRemaining;
        _eCd = p.ESkillCdRemaining;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        TickTimers((float)delta);
        UpdateTornado((float)delta);
    }

    private void TickTimers(float dt)
    {
        if (_blushCd > 0f) _blushCd -= dt;
        if (_eCd > 0f) _eCd -= dt;
        if (_baTelegraph > 0f) _baTelegraph -= dt;
        if (_eTelegraph > 0f) _eTelegraph -= dt;
        if (_tornadoCharge > 0f) _tornadoCharge -= dt;
    }

    // ── BA ────────────────────────────────────────────────────────────────
    protected override void HandleBA()
    {
        if (!TryConsumeAmmo()) return;

        float mult = IsBurning ? BurnComboMult : 1f;   // passive
        float dmg = ComboDamage[_comboStage % ComboDamage.Length] * mult;
        int stage = (_comboStage % 3) + 1;             // captured before _comboStage++ below

        // The swing, whether or not it lands: the cone's real reach and half-angle, which
        // way, which stage.
        CueBus.Emit(this, "ba.swing", variant: stage, size: SlashRange, magnitude: dmg,
            direction: FacingDirection, tag: IsBurning ? "burning" : null,
            extents: CueShapes.Cone(SlashRange, SlashHalfAngle));

        // NOTE(animation): with call-method tracks, move this MeleeCone call onto
        // the Slash clip's damage-event frame instead of firing it immediately here.
        MeleeCone(SlashRange, SlashHalfAngle, dmg, SlashKnockback, applyBurn: false, 0f,
            cueAction: "ba", cueVariant: stage);
        PlayAnim($"slash{stage}" + (IsBurning ? "_burning" : ""), restart: true);

        ApplyMovePenalty(SwingPenaltyMult, SwingPenaltyDur);
        _baTelegraph = 0.15f;
        _comboStage++;
        StartAmmoAttackInterval();
        RequestAmmoReload();

        if (_tornadoCharge > 0f) { ActivateTornado(); _tornadoCharge = 0f; }
    }

    // ── Blush + Fire Tornado ───────────────────────────────────────────────
    protected override void HandleSkill1()
    {
        if (_blushCd > 0f) return;
        SetBurning(BlushDuration);         // ignites passive (no self-damage; immune)
        _tornadoCharge = TornadoChargeWindow;
        _blushCd = ScaleCooldown(BlushCooldown);
        PlayAnim("ignite", restart: true);
        CueBus.Emit(this, "s1.cast", magnitude: BlushDuration);
    }

    private void ActivateTornado()
    {
        // A re-cast while one is spinning restarts it; the loop is only started once.
        if (_tornadoActive <= 0f)
            CueBus.Start(this, "s1.tornado", size: TornadoHalfW, magnitude: TornadoDuration,
                extents: new Vector2(TornadoHalfW, TornadoHalfH));
        _tornadoActive = TornadoDuration;
        _tornadoTickTimer = 0f;            // tick on the next frame
        _tornadoTicks = 0;
        SetDefenseSource("FireTornado", TornadoDefense);
    }

    private void EndTornado()
    {
        ClearDefenseSource("FireTornado");
        CueBus.Stop(this, "s1.tornado");
    }

    public override void _ExitTree()
    {
        // Switched out (Tab) or the scene ended mid-spin: a loop is always stopped by name.
        if (_tornadoActive > 0f) CueBus.Stop(this, "s1.tornado");
        base._ExitTree();
    }

    private void UpdateTornado(float dt)
    {
        if (_tornadoActive <= 0f) return;
        _tornadoActive -= dt;
        _tornadoTickTimer -= dt;
        if (_tornadoTickTimer <= 0f)
        {
            _tornadoTickTimer = TornadoTick;
            TornadoHit();
        }
        if (_tornadoActive <= 0f) EndTornado();
    }

    private void TornadoHit()
    {
        Vector2 origin = GlobalPosition;
        _tornadoTicks++;
        CueBus.Emit(this, "s1.tornado.tick", variant: _tornadoTicks, size: TornadoHalfW,
            magnitude: TornadoDamage, extents: new Vector2(TornadoHalfW, TornadoHalfH));
        foreach (ICombatTarget e in CombatTargets.HostileTo(GetTree(), Side))
        {
            Vector2 to = e.GlobalPosition - origin;
            if (Mathf.Abs(to.X) > TornadoHalfW || Mathf.Abs(to.Y) > TornadoHalfH) continue;
            Vector2 push = (to.LengthSquared() > 0.01f ? to.Normalized() : Vector2.Up) * TornadoPush;
            // A field's tick: ranged, so the victim is held and she never is. Named Light —
            // a tick stays a light hit however far a buff pushes its damage.
            float dealt = e.TakeHit(new HitInfo(TornadoDamage * DamageDealtMultiplier, push)
                .FromRanged(this, "s1.tornado", _tornadoTicks, HitWeight.Light), origin);
            ReportDamageDealt(dealt);
            e.SetBurning(2f);
        }
    }

    // ── Pome Seed Eruption ────────────────────────────────────────────────
    protected override void HandleSkill2()
    {
        if (_eCd > 0f || PomeSeedScene == null) return;
        _eCd = ScaleCooldown(ECooldown);
        _eTelegraph = 2.6f;
        // Erupt is a 1s idle-frame hold — Unity's erupt clip was empty (showed
        // the default sprite), so this is a faithful port, not a placeholder.
        PlayAnim("erupt", restart: true);
        _eruptionWave = 0;
        CueBus.Emit(this, "s2.cast", magnitude: _seedsPerWave.Length, direction: Vector2.Up,
            tag: IsBurning ? "burning" : null);
        _ = FireEruption();
    }

    private async Task FireEruption()
    {
        SpawnSeedWave(_seedsPerWave[0]);
        if (!await WaitAlive(1.0f)) return;
        SpawnSeedWave(_seedsPerWave[1]);
        if (!await WaitAlive(1.5f)) return;   // 2.5s total from press
        SpawnSeedWave(_seedsPerWave[2]);
    }

    private async Task<bool> WaitAlive(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        return IsInstanceValid(this) && IsInsideTree();
    }

    private void SpawnSeedWave(int count)
    {
        var waveHits = new HashSet<ulong>();       // shared: first hit 30, rest 6
        bool burning = IsBurning;                  // passive → burning seeds
        Vector2 origin = FirePoint != null ? FirePoint.GlobalPosition
                                           : GlobalPosition + new Vector2(0f, -20f);
        Node container = GetParent();

        // One cue for the whole fan — never one per seed (T40 §2).
        _eruptionWave++;
        CueBus.Emit(this, "s2.wave", variant: _eruptionWave, magnitude: count,
            direction: Vector2.Up, tag: burning ? "burning" : null, at: origin);

        for (int i = 0; i < count; i++)
        {
            float t = count > 1 ? (i + 0.5f) / count : 0.5f;
            float deg = Mathf.Lerp(EConeMinDeg, EConeMaxDeg, t);
            float rad = Mathf.DegToRad(deg);
            // World cone is CCW-from-+X with Y up; Godot Y is down, so negate sin.
            Vector2 vel = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * ESeedSpeed;

            var seed = PomeSeedScene.Instantiate<PomeSeed>();
            container.AddChild(seed);
            seed.GlobalPosition = origin;
            seed.Init(vel, waveHits, burning, DamageDealtMultiplier, this);
        }
    }

    // ── Animation automata ───────────────────────────────────────────────
    // Mirror of the Unity "Pomegraknight 1" animator. Runs only while not
    // stunned — CharacterController freezes Anim.SpeedScale during a gain-no
    // window instead of calling in here, so the current frame just holds.
    protected override void UpdateAnimator(float dt)
    {
        if (Dead) { PlayAnim("dead"); return; }              // holds last frame when done

        // A latched one-shot (slash/ignite/erupt) keeps playing to completion
        // before the state machine falls back to movement/idle.
        if (OneShots.Contains(LastAnim) && Anim != null && Anim.IsPlaying()) return;

        if (_tornadoActive > 0f) { PlayAnim("tornado"); return; }   // loops

        if (!IsOnFloor()) { PlayAnim("jump"); return; }             // non-loop; holds last frame airborne

        float walkEpsilon = Units.Px(WalkSpeedThresholdMps);        // ~8 px/s
        if (Mathf.Abs(Velocity.X) > walkEpsilon) PlayAnim("walk");
        else PlayAnim("idle");
    }

    // ── Range telegraphs ──────────────────────────────────────────────────
    protected override void DrawDebug()
    {
        // BA cone (brightens during the active swing window).
        float faceAng = Facing > 0f ? 0f : Mathf.Pi;
        bool baHot = _baTelegraph > 0f;
        Color baCol = baHot ? new Color(1f, 0.85f, 0.2f, 0.95f)
                            : new Color(1f, 0.85f, 0.2f, 0.35f);
        DrawConeLocal(SlashRange, faceAng, Mathf.DegToRad(SlashHalfAngle), baCol, baHot ? 4f : 2f);

        // Fire Tornado box (while active).
        if (_tornadoActive > 0f)
        {
            var rect = new Rect2(new Vector2(-TornadoHalfW, -TornadoHalfH),
                                 new Vector2(TornadoHalfW * 2f, TornadoHalfH * 2f));
            DrawRect(rect, new Color(1f, 0.4f, 0.1f, 0.95f), false, 3f);
        }

        // E launch cone (during the eruption).
        if (_eTelegraph > 0f)
        {
            Vector2 fp = FirePoint != null ? FirePoint.Position : new Vector2(0f, -20f);
            float c = -Mathf.Pi / 2f;                 // "up" in Godot's Y-down space
            float h = Mathf.DegToRad((EConeMaxDeg - EConeMinDeg) / 2f);
            var eCol = new Color(0.45f, 1f, 0.4f, 0.85f);
            Vector2 e0 = fp + new Vector2(Mathf.Cos(c - h), Mathf.Sin(c - h)) * EDrawRange;
            Vector2 e1 = fp + new Vector2(Mathf.Cos(c + h), Mathf.Sin(c + h)) * EDrawRange;
            DrawLine(fp, e0, eCol, 2f);
            DrawLine(fp, e1, eCol, 2f);
            DrawArc(fp, EDrawRange, c - h, c + h, 24, eCol, 2f);
        }
    }
}

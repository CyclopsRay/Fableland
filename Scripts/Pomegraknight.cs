using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Pomegraknight — Skirmisher. Port of the Unity character, minus animation
/// (hooks are left in <see cref="UpdateAnimator"/> and the swing/wave timing so
/// AnimationPlayer "call method" tracks can drive damage later).
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
/// </summary>
public partial class Pomegraknight : CharacterController
{
    [ExportGroup("BA — Melee Combo")]
    [Export] public float SlashRange = 130f;       // ~4m at 32px/m
    [Export] public float SlashHalfAngle = 40f;    // full 80 cone
    [Export] public float SlashKnockback = 96f;    // ~3 m/s
    [Export] public int MagazineSize = 3;
    [Export] public float BetweenStages = 0.25f;
    [Export] public float ReloadTime = 0.75f;
    [Export] public float SwingPenaltyMult = 0.7f; // 30% slower
    [Export] public float SwingPenaltyDur = 0.4f;

    [ExportGroup("Blush + Fire Tornado (Skill 1)")]
    [Export] public float BlushDuration = 5f;
    [Export] public float BlushCooldown = 10f;
    [Export] public float TornadoChargeWindow = 3f;
    [Export] public float TornadoDuration = 2.5f;
    [Export] public float TornadoTick = 0.5f;
    [Export] public float TornadoDamage = 15f;
    [Export] public float TornadoHalfW = 48f;      // 3m wide
    [Export] public float TornadoHalfH = 32f;      // 2m tall
    [Export] public float TornadoPush = 160f;      // ~5 m/s
    [Export] public float TornadoDamageReduction = 0.6f;

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

    private int _ammo;
    private int _comboStage;
    private float _swingCd;
    private float _reloadTimer;
    private float _baTelegraph;

    private float _blushCd;
    private float _tornadoCharge;
    private float _tornadoActive;
    private float _tornadoTickTimer;

    private float _eCd;
    private float _eTelegraph;

    protected override void InitCharacter()
    {
        BurnImmune = true;            // Pomegranate Shell
        _ammo = MagazineSize;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        TickTimers((float)delta);
        UpdateTornado((float)delta);
    }

    private void TickTimers(float dt)
    {
        if (_swingCd > 0f) _swingCd -= dt;
        if (_blushCd > 0f) _blushCd -= dt;
        if (_eCd > 0f) _eCd -= dt;
        if (_baTelegraph > 0f) _baTelegraph -= dt;
        if (_eTelegraph > 0f) _eTelegraph -= dt;
        if (_tornadoCharge > 0f) _tornadoCharge -= dt;
        if (_reloadTimer > 0f)
        {
            _reloadTimer -= dt;
            if (_reloadTimer <= 0f) { _ammo = MagazineSize; _comboStage = 0; }
        }
    }

    // ── BA ────────────────────────────────────────────────────────────────
    protected override void HandleBA()
    {
        if (_reloadTimer > 0f || _swingCd > 0f) return;

        float mult = IsBurning ? BurnComboMult : 1f;   // passive
        float dmg = ComboDamage[_comboStage % ComboDamage.Length] * mult;

        // NOTE: with animation, move this MeleeCone call onto the Slash clip's
        // damage-event frame instead of firing it immediately here.
        MeleeCone(SlashRange, SlashHalfAngle, dmg, SlashKnockback, applyBurn: false, 0f);
        // Anim?.Play($"Slash{(_comboStage % 3) + 1}");

        ApplyMovePenalty(SwingPenaltyMult, SwingPenaltyDur);
        _baTelegraph = 0.15f;
        _swingCd = BetweenStages;
        _comboStage++;
        _ammo--;
        if (_ammo <= 0) _reloadTimer = ReloadTime;

        if (_tornadoCharge > 0f) { ActivateTornado(); _tornadoCharge = 0f; }
    }

    // ── Blush + Fire Tornado ───────────────────────────────────────────────
    protected override void HandleSkill1()
    {
        if (_blushCd > 0f) return;
        SetBurning(BlushDuration);         // ignites passive (no self-damage; immune)
        _tornadoCharge = TornadoChargeWindow;
        _blushCd = BlushCooldown;
    }

    private void ActivateTornado()
    {
        _tornadoActive = TornadoDuration;
        _tornadoTickTimer = 0f;            // tick on the next frame
        DamageTakenMult = TornadoDamageReduction;
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
        if (_tornadoActive <= 0f) DamageTakenMult = 1f;
    }

    private void TornadoHit()
    {
        Vector2 origin = GlobalPosition;
        foreach (Node n in GetTree().GetNodesInGroup("enemy"))
        {
            if (n is not Enemy e) continue;
            Vector2 to = e.GlobalPosition - origin;
            if (Mathf.Abs(to.X) > TornadoHalfW || Mathf.Abs(to.Y) > TornadoHalfH) continue;
            Vector2 push = (to.LengthSquared() > 0.01f ? to.Normalized() : Vector2.Up) * TornadoPush;
            e.TakeDamage(TornadoDamage, push);
            e.SetBurning(2f);
        }
    }

    // ── Pome Seed Eruption ────────────────────────────────────────────────
    protected override void HandleSkill2()
    {
        if (_eCd > 0f || PomeSeedScene == null) return;
        _eCd = ECooldown;
        _eTelegraph = 2.6f;
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
            seed.Init(vel, waveHits, burning);
        }
    }

    // ── Animation hook (left intentionally empty for now) ──────────────────
    protected override void UpdateAnimator(float dt)
    {
        // TODO(animation): drive Anim state (Idle/Run/Jump/Slash1-3/Blush) here,
        // and move BA/seed damage onto AnimationPlayer call-method tracks.
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

using System.Collections.Generic;
using Godot;
using Fableland.MapCreation.Data;

/// <summary>
/// PumpKing's ultimate, the Black Prison (黑狱) — <c>Docs/PumpKing.gdd</c>, "Q — the Black Prison".
///
/// A circle he opens where he aims (at most <see cref="PumpKing.PrisonCastRangeM"/> from him),
/// with the radius of his head's biggest blast. For <see cref="WarnSeconds"/> it only warns:
/// nothing happens to anyone. Then for <see cref="HoldSeconds"/> every body of the other side
/// inside it (its hit circle touching the prison) moves at half speed, jumps half as high, is
/// silenced, and sees only one map cell around itself. Walking out frees a body at once; walking
/// in catches it. Every <see cref="PumpkinEvery"/> a pumpkin grows on the prison's vines and is
/// flung outward (<see cref="PrisonPumpkin"/>). While it stands he may recast once, free
/// (<see cref="Recast"/>): its mouth opens at the centre, he blinks into it and his head goes off
/// there at its biggest (<see cref="PumpKing.LandInPrisonMouth"/>).
///
/// Like his head, it lives on its own once opened: a Tab switch or his death leaves it standing
/// for its time, pumpkins and all; only the recast needs him. It fights for the caster's side,
/// so a BOSS PumpKing's prison would hold the player — and darken the player's view
/// (<see cref="PrisonVeil"/>).
///
/// It is gameplay (it sets bodies' rules and deals hits through its pumpkins) and draws its own
/// placeholder art in <see cref="_Draw"/>, as Māra's wheel does. Every TODO(art) it stands in for
/// is listed in PumpKing.gdd.
/// </summary>
public partial class BlackPrison : Node2D
{
    // ── The rule (PumpKing.gdd; first pass) ──────────────────────────────────────
    public const float WarnSeconds = 1f;
    public const float HoldSeconds = 4f;
    /// <summary>Share of move speed and of jump height a prisoner keeps.</summary>
    public const float MoveScale = 0.5f;
    public const float JumpScale = 0.5f;
    /// <summary>"Only one map cell around yourself": the 3 × 3 cells centred on the body, i.e.
    /// 1.5 cells — 3 m — from its centre.</summary>
    public const float SightCells = 1.5f;
    public const float SightMetres = SightCells * MapGrid.MetersPerCell;

    // ── Pumpkins ──
    public const float PumpkinEvery = 0.5f;
    /// <summary>One as it forms, then every <see cref="PumpkinEvery"/> while it stands: 8.</summary>
    public const int PumpkinCount = (int)(HoldSeconds / PumpkinEvery);
    public const float PumpkinGrowSeconds = 0.25f;
    public const float PumpkinMinRadiusM = 0.25f;
    public const float PumpkinMaxRadiusM = 0.6f;
    public const float PumpkinMinDamage = 10f;
    public const float PumpkinMaxDamage = 25f;
    public const float PumpkinMinSpeedMps = 9f;
    public const float PumpkinMaxSpeedMps = 13f;
    /// <summary>Upward kick on top of the outward throw, so a pumpkin arcs out instead of
    /// rolling along the floor.</summary>
    public const float PumpkinKickMps = 4f;
    /// <summary>Pumpkins grow anywhere within this share of the radius.</summary>
    public const float PumpkinSpawnShare = 0.8f;

    // ── The recast ──
    /// <summary>From the press to the blink: the mouth opening.</summary>
    public const float MouthSeconds = 0.4f;

    private const float Epsilon = 1e-4f;
    private const float FadeSeconds = 0.3f;

    public enum Phase { Warning, Standing, Over }

    public Phase Stage { get; private set; } = Phase.Warning;
    public bool Standing => Stage == Phase.Standing;
    public bool Over => Stage == Phase.Over;
    public Vector2 Centre => _centre;
    public float Radius => _radius;
    /// <summary>Seconds since he opened it (warning included).</summary>
    public float Age => _age;
    /// <summary>Its age when the warning ended and it began to hold, and when it let go
    /// (−1 until then). Read-only, for checks.</summary>
    public float FormedAt { get; private set; } = -1f;
    public float EndedAt { get; private set; } = -1f;
    public bool RecastUsed => _recastUsed;
    public bool MouthOpen => _mouth > 0f;
    /// <summary>The recast has landed: he blinked in and the blast went off.</summary>
    public bool Swallowed { get; private set; }
    public int PumpkinsGrown { get; private set; }
    public int HeldCount => _held.Count;
    public bool Holds(ICombatTarget target) => target != null && _held.ContainsKey(target.TargetId);
    /// <summary>The local player is a prisoner and sees only the cell around them. Safe to ask a
    /// prison that has ended and freed itself (its veil went with it).</summary>
    public bool VeilShown => _veil != null && IsInstanceValid(_veil) && _veil.Visible;
    /// <summary>The name every rule this prison lays on a body is held under.</summary>
    public string Source => _source;

    /// <summary>Where the pumpkins grow, how big and which way: unseeded like a foe's. Nothing
    /// is drawn from it before the prison forms, so a check may hand it a seeded one any time
    /// during the warning.</summary>
    public RandomNumberGenerator Rng { get; set; }

    private PumpKing _caster;
    private CombatSide _side;
    private string _cueOwner = "pumpking";
    private string _cueClass = CueOwners.Fighter;
    private float _gravityScale = 1f;
    private float _damageScale = 1f;
    private Vector2 _centre;
    private float _radius;
    private string _source;
    private bool _begun;

    private float _age;
    private float _standing;
    private float _mouth;
    private float _mouthShut;
    private bool _recastUsed;
    private float _fade;

    private readonly Dictionary<ulong, ICombatTarget> _held = new();
    private readonly HashSet<ulong> _inside = new();
    private readonly List<ulong> _leaving = new();
    private PrisonVeil _veil;
    private CharacterController _veiled;

    /// <summary>Open the prison. Call right after AddChild.</summary>
    public void Begin(PumpKing caster, Vector2 centre, float radius, float damageScale)
    {
        _caster = caster;
        _side = caster.Side;
        _cueOwner = caster.FeedbackId;
        _cueClass = caster.FeedbackClass;
        _gravityScale = caster.GravityScale;
        _damageScale = damageScale;
        _centre = centre;
        _radius = radius;
        GlobalPosition = centre;
        _source = "pumpking.black_prison#" + GetInstanceId();
        if (Rng == null)
        {
            Rng = new RandomNumberGenerator();
            Rng.Randomize();
        }

        _veil = new PrisonVeil { Name = "Veil", Visible = false, ZIndex = PrisonVeil.Z, ZAsRelative = false };
        AddChild(_veil);
        _veil.Setup(Units.Px(SightMetres));

        _begun = true;
        CueBus.StartAt(_cueOwner, "ult.warn", this, _centre, _side, size: _radius, magnitude: WarnSeconds,
            ownerClass: _cueClass);
        QueueRedraw();
    }

    /// <summary>The recast: free, once, only once the prison has formed (not in its warning).
    /// The mouth opens; when it has, the caster blinks into it. Returns whether it opened.</summary>
    public bool Recast()
    {
        if (Stage != Phase.Standing || _recastUsed) return false;
        _recastUsed = true;
        _mouth = MouthSeconds;
        CueBus.EmitAt(_cueOwner, "ult.mouth", _centre, this, _side, size: _radius * 0.5f,
            magnitude: MouthSeconds, ownerClass: _cueClass);
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_begun) return;
        float dt = (float)delta;
        _age += dt;
        if (_mouthShut > 0f) _mouthShut -= dt;

        switch (Stage)
        {
            case Phase.Warning:
                if (_age >= WarnSeconds - Epsilon) Form();
                break;
            case Phase.Standing:
                _standing += dt;
                TickStanding(dt);
                break;
            case Phase.Over:
                _fade -= dt;
                Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_fade / FadeSeconds, 0f, 1f));
                if (_fade <= 0f) QueueFree();
                break;
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // The veil follows the body it darkens at the frame rate the camera follows it.
        if (_veil != null && _veil.Visible && _veiled != null && IsInstanceValid(_veiled))
            _veil.GlobalPosition = _veiled.GlobalPosition;
    }

    private void Form()
    {
        Stage = Phase.Standing;
        _standing = 0f;
        FormedAt = _age;
        CueBus.StopAt(_cueOwner, "ult.warn", this, ownerClass: _cueClass);
        CueBus.StartAt(_cueOwner, "ult.prison", this, _centre, _side, size: _radius, magnitude: HoldSeconds,
            ownerClass: _cueClass);
        TickStanding(0f);
    }

    private void TickStanding(float dt)
    {
        UpdatePrisoners();
        while (PumpkinsGrown < PumpkinCount && _standing + Epsilon >= PumpkinsGrown * PumpkinEvery) GrowPumpkin();

        if (_mouth > 0f)
        {
            _mouth -= dt;
            if (_mouth <= 0f) Swallow();
        }
        // A recast pressed in the last moments still lands: the prison waits for its mouth.
        if (_standing >= HoldSeconds - Epsilon && _mouth <= 0f) End();
    }

    // ── Who is inside ─────────────────────────────────────────────────────────

    private void UpdatePrisoners()
    {
        _inside.Clear();
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), _side))
        {
            // The fallen and the untouchable are let go: invincibility holds off every status.
            if (target.CurrentHp <= 0f || target.Invincible) continue;
            if (target.GlobalPosition.DistanceTo(_centre) - target.HitRadius > _radius) continue;
            _inside.Add(target.TargetId);
            if (_held.TryAdd(target.TargetId, target)) Hold(target);
        }

        _leaving.Clear();
        foreach (KeyValuePair<ulong, ICombatTarget> pair in _held)
            if (!_inside.Contains(pair.Key)) _leaving.Add(pair.Key);
        foreach (ulong id in _leaving)
        {
            Release(_held[id]);
            _held.Remove(id);
        }

        UpdateVeil();
    }

    private void Hold(ICombatTarget target)
    {
        target.SetMoveSpeedScale(_source, MoveScale);
        target.SetJumpHeightScale(_source, JumpScale);
        target.SetSilenced(_source, true);
        target.SetSightLimit(_source, SightMetres);
        CueBus.EmitAt(_cueOwner, "ult.catch", target.GlobalPosition, this, _side, size: target.HitRadius,
            ownerClass: _cueClass);
    }

    private static void Release(ICombatTarget target, string source)
    {
        if (!CombatTargets.IsAlive(target)) return;
        target.SetMoveSpeedScale(source, 1f);
        target.SetJumpHeightScale(source, 1f);
        target.SetSilenced(source, false);
        target.ClearSightLimit(source);
    }

    private void Release(ICombatTarget target) => Release(target, _source);

    private void ReleaseAll()
    {
        foreach (ICombatTarget target in _held.Values) Release(target);
        _held.Clear();
        UpdateVeil();
    }

    /// <summary>Only the player's own view is darkened, and only while they are a prisoner.</summary>
    private void UpdateVeil()
    {
        CharacterController player = CharacterController.LocalPlayer;
        bool show = player != null && IsInstanceValid(player) && Stage == Phase.Standing && _held.ContainsKey(player.TargetId);
        _veiled = show ? player : null;
        if (_veil == null) return;
        _veil.Visible = show;
        if (show) _veil.GlobalPosition = player.GlobalPosition;
    }

    // ── Pumpkins ─────────────────────────────────────────────────────────────

    private void GrowPumpkin()
    {
        PumpkinsGrown++;
        Node parent = GetParent();
        if (parent == null) return;

        Vector2 at = PumpkinSpot();
        float size = Rng.Randf();
        float radius = Units.Px(Mathf.Lerp(PumpkinMinRadiusM, PumpkinMaxRadiusM, size));
        float damage = Mathf.Lerp(PumpkinMinDamage, PumpkinMaxDamage, size) * _damageScale;

        // Flung outward, away from the centre through the point it grew at.
        Vector2 outward = at - _centre;
        outward = outward.LengthSquared() > 1f
            ? outward.Normalized()
            : Vector2.Right.Rotated(Rng.RandfRange(0f, Mathf.Tau));
        float speed = Units.Px(Rng.RandfRange(PumpkinMinSpeedMps, PumpkinMaxSpeedMps));
        Vector2 launch = outward * speed + Vector2.Up * Units.Px(PumpkinKickMps);

        var pumpkin = new PrisonPumpkin { Name = "PrisonPumpkin" };
        parent.AddChild(pumpkin);
        pumpkin.GlobalPosition = at;
        pumpkin.Init(launch, radius, damage, PumpkinGrowSeconds, _side, _cueOwner, _cueClass,
            _caster != null && IsInstanceValid(_caster) ? _caster : null, _gravityScale, size);
        CueBus.EmitAt(_cueOwner, "ult.pumpkin", at, pumpkin, _side, variant: PumpkinsGrown, size: radius,
            magnitude: damage, direction: launch.Normalized(), ownerClass: _cueClass);
    }

    /// <summary>A random point inside the prison that is not inside solid ground (a few tries).</summary>
    private Vector2 PumpkinSpot()
    {
        PhysicsDirectSpaceState2D space = GetWorld2D()?.DirectSpaceState;
        var query = new PhysicsPointQueryParameters2D { CollisionMask = 1u << (Units.LayerGround - 1) };
        Vector2 spot = _centre;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Rng.RandfRange(0f, Mathf.Tau);
            float d = Mathf.Sqrt(Rng.Randf()) * _radius * PumpkinSpawnShare;   // uniform over the disc
            spot = _centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            if (space == null) break;
            query.Position = spot;
            if (space.IntersectPoint(query, 1).Count == 0) break;
        }
        return spot;
    }

    // ── The recast lands ─────────────────────────────────────────────────────

    private void Swallow()
    {
        _mouthShut = 0.2f;
        if (_caster == null || !IsInstanceValid(_caster) || _caster.IsDefeated || !_caster.IsInsideTree()) return;
        _caster.LandInPrisonMouth(this);
        Swallowed = true;
    }

    private void End()
    {
        Stage = Phase.Over;
        EndedAt = _age;
        _fade = FadeSeconds;
        ReleaseAll();
        CueBus.StopAt(_cueOwner, "ult.prison", this, ownerClass: _cueClass);
    }

    public override void _ExitTree()
    {
        if (!_begun || Stage == Phase.Over) return;
        // Freed early (the arena closed): nobody keeps a rule from a prison that is gone.
        ReleaseAll();
        CueBus.StopAt(_cueOwner, Stage == Phase.Warning ? "ult.warn" : "ult.prison", this, ownerClass: _cueClass);
        Stage = Phase.Over;
    }

    // ── Placeholder art (TODO(art), PumpKing.gdd) ─────────────────────────────
    // The Dark Wood's scratchboard code (RealmStyleBible §7A): a black board, moonlight blue,
    // yellow for what gives light, red for danger.
    private static readonly Color Board = new("#070A14");
    private static readonly Color MoonDeep = new("#1B2D6B");
    private static readonly Color Moon = new("#3F6FD8");
    private static readonly Color MoonPale = new("#9DBEFF");
    private static readonly Color Light = new("#FFD23F");
    private static readonly Color LightPale = new("#FFF1A8");
    private static readonly Color Danger = new("#D8323A");
    private static readonly Color DangerDeep = new("#7A161C");

    private const int RimSegments = 64;
    private const int Bristles = 44;
    private Vector2[] _circle;

    public override void _Draw()
    {
        if (!_begun) return;
        float r = _radius;
        _circle ??= Ring(r, RimSegments);

        if (Stage == Phase.Warning)
        {
            float t = Mathf.Clamp(_age / WarnSeconds, 0f, 1f);
            DrawCircle(Vector2.Zero, r, new Color(Board, 0.12f * t));
            // The tell: a red rim that flickers faster as the second runs out, bristles growing.
            float pulse = 0.45f + 0.35f * Mathf.Sin(_age * Mathf.Lerp(8f, 30f, t));
            DrawDashedRim(r, new Color(Danger, pulse), 2f);
            DrawBristles(r, t, new Color(Colors.Black, 0.6f * t));
            return;
        }

        // Until the art puts the realm behind the battlefield (TODO(eng), PumpKing.gdd), all of
        // this is drawn OVER the fight: it stays faint, so a fighter inside still reads first
        // (RealmStyleBible §2.6). Only the border is solid.
        float rise = Stage == Phase.Standing ? Mathf.Clamp(_standing / 0.6f, 0f, 1f) : 1f;
        DrawCircle(Vector2.Zero, r, new Color(Board, 0.22f));   // inside: a little darker
        DrawHouses(r, rise);                                     // behind: cardboard houses rising
        DrawVines(r);                                            // in front: vines the pumpkins grow on
        DrawBristles(r, 1f, Colors.Black);                       // the border: black, bristling
        DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, RimSegments, Colors.Black, 5f);
        DrawArc(Vector2.Zero, r - 3f, 0f, Mathf.Tau, RimSegments, new Color(DangerDeep, 0.9f), 1.5f);
        DrawMouth(r);
    }

    private static Vector2[] Ring(float r, int segments)
    {
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return points;
    }

    private void DrawDashedRim(float r, Color colour, float width)
    {
        const int dashes = 24;
        for (int i = 0; i < dashes; i++)
        {
            float a0 = i * Mathf.Tau / dashes;
            DrawArc(Vector2.Zero, r, a0, a0 + Mathf.Tau / dashes * 0.55f, 4, colour, width);
        }
    }

    /// <summary>Thorns all round the rim, outward and in, their lengths twitching.</summary>
    private void DrawBristles(float r, float grow, Color colour)
    {
        for (int i = 0; i < Bristles; i++)
        {
            float a = i * Mathf.Tau / Bristles + 0.05f * Mathf.Sin(i * 12.9898f);
            float jitter = 0.65f + 0.35f * Mathf.Sin(_age * (5f + i % 7) + i * 1.7f);
            float outLen = r * 0.20f * jitter * grow * (i % 3 == 0 ? 1.4f : 1f);
            float inLen = r * 0.07f * jitter * grow;
            float half = 0.055f;
            Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 baseL = new Vector2(Mathf.Cos(a - half), Mathf.Sin(a - half)) * r;
            Vector2 baseR = new Vector2(Mathf.Cos(a + half), Mathf.Sin(a + half)) * r;
            if (outLen > 0.5f) DrawColoredPolygon(new[] { baseL, dir * (r + outLen), baseR }, colour);
            if (inLen > 0.5f) DrawColoredPolygon(new[] { baseR, dir * (r - inLen), baseL }, colour);
        }
    }

    /// <summary>Cardboard houses that rise from below, turning upright — seen only through the
    /// prison: each is cut to the circle.</summary>
    private void DrawHouses(float r, float rise)
    {
        for (int i = 0; i < 4; i++)
        {
            float x = Mathf.Lerp(-0.62f, 0.62f, i / 3f) * r;
            float w = r * (i % 2 == 0 ? 0.34f : 0.26f);
            float h = r * (i % 2 == 0 ? 0.42f : 0.55f);
            float baseY = r * 0.78f + (1f - rise) * r * 0.9f;
            float turn = (1f - rise) * (i % 2 == 0 ? 1.2f : -1.2f);
            Vector2 pivot = new(x, baseY);
            Vector2[] house =
            {
                new(-w * 0.5f, 0f), new(w * 0.5f, 0f), new(w * 0.5f, -h), new(0f, -h - w * 0.45f), new(-w * 0.5f, -h),
            };
            for (int k = 0; k < house.Length; k++) house[k] = pivot + house[k].Rotated(turn);
            foreach (Vector2[] piece in Geometry2D.IntersectPolygons(house, _circle))
                DrawColoredPolygon(piece, new Color(MoonDeep, 0.35f));

            Vector2 c = pivot + new Vector2(0f, -h * 0.55f).Rotated(turn);
            Vector2[] window =
            {
                c + new Vector2(-w * 0.12f, -h * 0.1f).Rotated(turn), c + new Vector2(w * 0.12f, -h * 0.1f).Rotated(turn),
                c + new Vector2(w * 0.12f, h * 0.1f).Rotated(turn), c + new Vector2(-w * 0.12f, h * 0.1f).Rotated(turn),
            };
            foreach (Vector2[] piece in Geometry2D.IntersectPolygons(window, _circle))
                DrawColoredPolygon(piece, new Color(Light, 0.45f));
        }
    }

    /// <summary>Vines hanging from the top of the circle, swaying.</summary>
    private void DrawVines(float r)
    {
        for (int i = 0; i < 5; i++)
        {
            float x = Mathf.Lerp(-0.7f, 0.7f, i / 4f) * r;
            float top = -Mathf.Sqrt(Mathf.Max(0f, r * r - x * x));
            float length = r * (0.55f + 0.3f * ((i * 37) % 5) / 4f);
            Vector2 previous = new(x, top);
            for (int s = 1; s <= 6; s++)
            {
                float u = s / 6f;
                Vector2 next = new(x + Mathf.Sin(_age * 1.6f + i * 2.1f + u * 3f) * r * 0.06f * u, top + length * u);
                DrawLine(previous, next, new Color(Moon, 0.5f), 2f);
                if (s % 2 == 0)
                    DrawColoredPolygon(new[] { next, next + new Vector2(5f, -3f), next + new Vector2(2f, 4f) }, new Color(MoonPale, 0.45f));
                previous = next;
            }
        }
    }

    /// <summary>The recast's mouth: PumpKing's grin opening at the centre, lit from inside.</summary>
    private void DrawMouth(float r)
    {
        float open = _mouth > 0f ? 1f - _mouth / MouthSeconds : _mouthShut > 0f ? _mouthShut / 0.2f : 0f;
        if (open <= 0.01f) return;
        float w = r * 0.75f;
        float h = r * 0.45f * open;
        // Upper lip left to right (a shallow arch), then the grin's belly right to left. The two
        // meet only at the corners, once each: a repeated point would fail triangulation.
        const int n = 16;
        var lips = new Vector2[n * 2 - 2];
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);
            lips[i] = new Vector2(Mathf.Lerp(-w, w, u), -h * 0.35f * Mathf.Sin(u * Mathf.Pi) + h * 0.15f);
        }
        for (int i = 1; i < n - 1; i++)
        {
            float u = 1f - i / (float)(n - 1);
            lips[n - 1 + i] = new Vector2(Mathf.Lerp(-w, w, u), h * Mathf.Sin(u * Mathf.Pi) + h * 0.15f);
        }
        DrawColoredPolygon(lips, new Color(Light, 0.55f * open));
        for (int i = 0; i < lips.Length; i++) lips[i] *= 0.85f;
        DrawColoredPolygon(lips, Colors.Black);
        // Teeth: two up, two down.
        for (int k = -1; k <= 1; k += 2)
        {
            float x = k * w * 0.35f;
            DrawColoredPolygon(new[] { new Vector2(x - w * 0.1f, -h * 0.15f), new Vector2(x + w * 0.1f, -h * 0.15f), new Vector2(x, h * 0.3f) }, LightPale);
            float xb = k * w * 0.15f;
            DrawColoredPolygon(new[] { new Vector2(xb - w * 0.09f, h * 0.95f), new Vector2(xb, h * 0.45f), new Vector2(xb + w * 0.09f, h * 0.95f) }, LightPale);
        }
    }
}

using Godot;
using Fableland.Data;
using Fableland.Realms;

/// <summary>
/// PumpKing's ultimate, the Black Prison (黑狱), as the first sub-realm (`Docs/Realms.gdd` §12,
/// `Docs/PumpKing.gdd` "Q — the Black Prison").
///
/// A circle <see cref="RadiusM"/> wide that he opens where he aims (at most
/// <see cref="PumpKing.PrisonCastRangeM"/> from him). For <see cref="WarnSeconds"/> it only warns,
/// on the battlefield. Then it opens a sub-realm — a patch of his horror world, the Dark Wood, at
/// ratio 2 (`RealmTable.BlackPrisonId`) — and everyone in the circle is in that world for
/// <see cref="HoldSeconds"/>, him included if he stands in it. Inside: silence on his enemies (skills
/// and items), nothing else; the ratio makes the way out twice as far. Every
/// <see cref="PumpkinEvery"/> a pumpkin grows at a random spot of the patch and is shot in a random
/// direction (<see cref="PrisonPumpkin"/>). While it stands he may recast once, free
/// (<see cref="Recast"/>): his mouth opens at the patch's centre and he blinks into it — into the
/// realm if he was outside — and his head goes off there at its biggest, the prison's
/// <see cref="Heart"/> (<see cref="PumpKing.LandInPrisonMouth"/>). When it ends, everyone comes out
/// where the projection puts them.
///
/// From the battlefield it is a window onto that world (`RealmView`) under this node's bristling
/// rim; a prisoner sees only the horror world — no border (the owner's rule). Like his head it
/// lives on its own once opened: a Tab switch or his death leaves it standing for its time. It
/// fights for the caster's side, so a BOSS PumpKing's prison holds the player.
/// TODO(art): every placeholder is listed in PumpKing.gdd.
/// </summary>
public partial class BlackPrison : Node2D
{
    // ── The rule (the owner's numbers) ───────────────────────────────────────────
    public const float WarnSeconds = 1f;
    public const float HoldSeconds = 4f;
    /// <summary>The owner's number, v0.12.15. The sub-realm's ratio (2) sets the exit border: 16 m.</summary>
    public const float RadiusM = 8f;

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
    /// <summary>Pumpkins grow anywhere within this share of the exit border (the patch's
    /// fighting ground), at random, and fly off in a random direction (the owner: random for now).</summary>
    public const float PumpkinSpawnShare = 0.85f;

    // ── The recast ──
    /// <summary>From the press to the blink: the mouth opening.</summary>
    public const float MouthSeconds = 0.4f;

    /// <summary>Above the realm's window (`Realm.WindowZ`): the rim is drawn over its edge.</summary>
    public const int Z = Fableland.Realms.Realm.WindowZ + 1;

    private const float Epsilon = 1e-4f;
    private const float FadeSeconds = 0.3f;

    public enum Phase { Warning, Standing, Over }

    public Phase Stage { get; private set; } = Phase.Warning;
    public bool Standing => Stage == Phase.Standing;
    public bool Over => Stage == Phase.Over;
    public Vector2 Centre => _centre;
    public float Radius => _radius;
    /// <summary>Its heart, px: his head's biggest blast (the recast's blast, the mouth's size).</summary>
    public float Heart => _heart;
    /// <summary>Seconds since he opened it (warning included).</summary>
    public float Age => _age;
    public float FormedAt { get; private set; } = -1f;
    public float EndedAt { get; private set; } = -1f;
    public bool RecastUsed => _recastUsed;
    public bool MouthOpen => _mouth > 0f;
    /// <summary>The recast has landed: he blinked in and the blast went off.</summary>
    public bool Swallowed { get; private set; }
    public int PumpkinsGrown { get; private set; }
    /// <summary>The sub-realm it opened (null in the warning, and after it ends).</summary>
    public RealmInstance Realm => _realm != null && _realm.IsOpen ? _realm : null;
    public int HeldCount => Realm?.MemberCount ?? 0;
    public bool Holds(ICombatTarget target) => Realm != null && target is Node2D body && _realm.Holds(body);
    /// <summary>The name every rule this prison lays on a body is held under.</summary>
    public string Source => _source;

    /// <summary>Where the pumpkins grow and how they fly: unseeded like a foe's. Nothing is drawn
    /// from it before the prison forms, so a check may hand it a seeded one during the warning.</summary>
    public RandomNumberGenerator Rng { get; set; }

    private PumpKing _caster;
    private CombatSide _side;
    private string _cueOwner = "pumpking";
    private string _cueClass = CueOwners.Fighter;
    private float _gravityScale = 1f;
    private float _damageScale = 1f;
    private Vector2 _centre;
    private float _radius;
    private float _heart;
    private string _source;
    private bool _begun;
    private RealmInstance _realm;
    private PrisonMouth _mouthArt;

    private float _age;
    private float _standing;
    private float _mouth;
    private bool _recastUsed;
    private float _fade;

    /// <summary>Open the prison: <paramref name="radius"/> wide, its <paramref name="heart"/> his
    /// head's blast (both px). Call right after AddChild.</summary>
    public void Begin(PumpKing caster, Vector2 centre, float radius, float heart, float damageScale)
    {
        _caster = caster;
        _side = caster.Side;
        _cueOwner = caster.FeedbackId;
        _cueClass = caster.FeedbackClass;
        _gravityScale = caster.GravityScale;
        _damageScale = damageScale;
        _centre = centre;
        _radius = radius;
        _heart = Mathf.Min(heart, radius);
        GlobalPosition = centre;
        ZIndex = Z;
        ZAsRelative = false;
        _source = "pumpking.black_prison#" + GetInstanceId();
        if (Rng == null)
        {
            Rng = new RandomNumberGenerator();
            Rng.Randomize();
        }
        _begun = true;
        CueBus.StartAt(_cueOwner, "ult.warn", this, _centre, _side, size: _radius, magnitude: WarnSeconds,
            ownerClass: _cueClass);
        QueueRedraw();
    }

    /// <summary>The recast: free, once, only once the prison has formed. The mouth opens; when it
    /// has, the caster blinks into it. Returns whether it opened.</summary>
    public bool Recast()
    {
        if (Stage != Phase.Standing || _recastUsed || Realm == null) return false;
        _recastUsed = true;
        _mouth = MouthSeconds;
        _mouthArt?.Open(MouthSeconds);
        CueBus.EmitAt(_cueOwner, "ult.mouth", _realm.Space.Anchor, this, _side, size: _heart * 0.5f,
            magnitude: MouthSeconds, ownerClass: _cueClass);
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_begun) return;
        float dt = (float)delta;
        _age += dt;

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

    private void Form()
    {
        CueBus.StopAt(_cueOwner, "ult.warn", this, ownerClass: _cueClass);
        _realm = Fableland.Realms.Realm.Open(this, RealmTable.BlackPrisonId, _centre, _radius, _side, _source);
        if (_realm == null)
        {
            // No space to open it in (a scene with no realm provider): fail loudly, end quietly.
            GD.PushError("[BlackPrison] could not open its sub-realm; the prison does nothing");
            Stage = Phase.Over;
            EndedAt = _age;
            _fade = FadeSeconds;
            return;
        }
        Stage = Phase.Standing;
        _standing = 0f;
        FormedAt = _age;
        _mouthArt = new PrisonMouth { Name = "PrisonMouth", Width = _heart * 1.5f };
        AddChild(_mouthArt);
        _mouthArt.GlobalPosition = _realm.Space.Anchor;
        CueBus.StartAt(_cueOwner, "ult.prison", this, _centre, _side, size: _radius, magnitude: HoldSeconds,
            ownerClass: _cueClass);
        TickStanding(0f);
    }

    private void TickStanding(float dt)
    {
        while (PumpkinsGrown < PumpkinCount && _standing + Epsilon >= PumpkinsGrown * PumpkinEvery) GrowPumpkin();

        if (_mouth > 0f)
        {
            _mouth -= dt;
            if (_mouth <= 0f) Swallow();
        }
        // A recast pressed in the last moments still lands: the prison waits for its mouth.
        if (_standing >= HoldSeconds - Epsilon && _mouth <= 0f) End();
    }

    // ── Pumpkins (random for now: the owner, `Realms.gdd` §12) ────────────────

    private void GrowPumpkin()
    {
        PumpkinsGrown++;
        Node parent = GetParent();
        if (parent == null || Realm == null) return;

        Vector2 at = PumpkinSpot();
        float size = Rng.Randf();
        float radius = Units.Px(Mathf.Lerp(PumpkinMinRadiusM, PumpkinMaxRadiusM, size));
        float damage = Mathf.Lerp(PumpkinMinDamage, PumpkinMaxDamage, size) * _damageScale;
        Vector2 direction = Vector2.Right.Rotated(Rng.RandfRange(0f, Mathf.Tau));
        float speed = Units.Px(Rng.RandfRange(PumpkinMinSpeedMps, PumpkinMaxSpeedMps));
        Vector2 launch = direction * speed;

        var pumpkin = new PrisonPumpkin { Name = "PrisonPumpkin" };
        parent.AddChild(pumpkin);
        pumpkin.GlobalPosition = at;   // in the patch: it lives and hits in the horror world only
        pumpkin.Init(launch, radius, damage, PumpkinGrowSeconds, _side, _cueOwner, _cueClass,
            _caster != null && IsInstanceValid(_caster) ? _caster : null, _gravityScale, size);
        CueBus.EmitAt(_cueOwner, "ult.pumpkin", at, pumpkin, _side, variant: PumpkinsGrown, size: radius,
            magnitude: damage, direction: direction, ownerClass: _cueClass);
    }

    /// <summary>A random point of the patch's fighting ground — within the share of the exit border
    /// around its anchor — that is not inside solid ground (a few tries).</summary>
    private Vector2 PumpkinSpot()
    {
        Vector2 anchor = _realm.Space.Anchor;
        float reach = _realm.ExitRadiusPx * PumpkinSpawnShare;
        PhysicsDirectSpaceState2D space = GetWorld2D()?.DirectSpaceState;
        var query = new PhysicsPointQueryParameters2D { CollisionMask = 1u << (Units.LayerGround - 1) };
        Vector2 spot = anchor;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = Rng.RandfRange(0f, Mathf.Tau);
            float d = Mathf.Sqrt(Rng.Randf()) * reach;   // uniform over the disc
            spot = anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            if (space == null) break;
            query.Position = spot;
            if (space.IntersectPoint(query, 1).Count == 0) break;
        }
        return spot;
    }

    // ── The recast lands ─────────────────────────────────────────────────────

    private void Swallow()
    {
        _mouthArt?.Shut(0.2f);
        if (_caster == null || !IsInstanceValid(_caster) || _caster.IsDefeated || !_caster.IsInsideTree() || Realm == null) return;
        _caster.LandInPrisonMouth(this);
        Swallowed = true;
    }

    private void End()
    {
        Stage = Phase.Over;
        EndedAt = _age;
        _fade = FadeSeconds;
        _realm?.Close();
        _realm = null;
        if (_mouthArt != null && IsInstanceValid(_mouthArt)) _mouthArt.QueueFree();
        CueBus.StopAt(_cueOwner, "ult.prison", this, ownerClass: _cueClass);
    }

    public override void _ExitTree()
    {
        if (!_begun || Stage == Phase.Over) return;
        // Freed early (the arena closed): the realm brings everyone out (or goes with the arena).
        if (_realm != null && _realm.IsOpen && _realm.IsInsideTree()) _realm.Close();
        CueBus.StopAt(_cueOwner, Stage == Phase.Warning ? "ult.warn" : "ult.prison", this, ownerClass: _cueClass);
        Stage = Phase.Over;
    }

    // ── Placeholder art: the rim, seen from the battlefield (TODO(art), PumpKing.gdd) ─────
    // The Dark Wood's scratchboard code (RealmStyleBible §7A, carried into KH-C's shadow theatre):
    // a black board, moonlight blue, yellow for what gives light, red for danger. Sizes in metres
    // (KNOWLEDGE v0.12.13), so the rim reads the same at any radius.
    private static readonly Color Board = new("#070A14");
    private static readonly Color Danger = new("#D8323A");
    private static readonly Color DangerDeep = new("#7A161C");
    private const float BristleSpacingM = 0.5f;
    private const float BristleOutM = 0.6f;
    private const float BristleInM = 0.21f;
    private const float BristleHalfWidthM = 0.165f;

    private static int SegmentsFor(float r) => Mathf.Clamp((int)(Mathf.Tau * r / 24f), 64, 360);

    public override void _Draw()
    {
        if (!_begun) return;
        float r = _radius;
        int segments = SegmentsFor(r);
        if (Stage == Phase.Warning)
        {
            float t = Mathf.Clamp(_age / WarnSeconds, 0f, 1f);
            DrawCircle(Vector2.Zero, r, new Color(Board, 0.12f * t));
            float pulse = 0.45f + 0.35f * Mathf.Sin(_age * Mathf.Lerp(8f, 30f, t));
            DrawDashedRim(r, new Color(Danger, pulse), 2f);
            DrawBristles(r, t, new Color(Colors.Black, 0.6f * t));
            return;
        }
        // Standing: the window onto the horror world fills the circle (RealmView); this is its
        // bristling black rim, drawn over the window's edge.
        DrawBristles(r, 1f, Colors.Black);
        DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, segments, Colors.Black, 5f);
        DrawArc(Vector2.Zero, r - 3f, 0f, Mathf.Tau, segments, new Color(DangerDeep, 0.9f), 1.5f);
    }

    private void DrawDashedRim(float r, Color colour, float width)
    {
        int dashes = Mathf.Max(24, (int)(Mathf.Tau * r / Units.Px(1f)));
        for (int i = 0; i < dashes; i++)
        {
            float a0 = i * Mathf.Tau / dashes;
            DrawArc(Vector2.Zero, r, a0, a0 + Mathf.Tau / dashes * 0.55f, 4, colour, width);
        }
    }

    private void DrawBristles(float r, float grow, Color colour)
    {
        int count = Mathf.Max(44, (int)(Mathf.Tau * r / Units.Px(BristleSpacingM)));
        float step = Mathf.Tau / count;
        float half = Units.Px(BristleHalfWidthM) / r;
        float outBase = Units.Px(BristleOutM), inBase = Units.Px(BristleInM);
        for (int i = 0; i < count; i++)
        {
            float a = i * step + 0.35f * step * Mathf.Sin(i * 12.9898f);
            float jitter = 0.65f + 0.35f * Mathf.Sin(_age * (5f + i % 7) + i * 1.7f);
            float outLen = outBase * jitter * grow * (i % 3 == 0 ? 1.4f : 1f);
            float inLen = inBase * jitter * grow;
            Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 baseL = new Vector2(Mathf.Cos(a - half), Mathf.Sin(a - half)) * r;
            Vector2 baseR = new Vector2(Mathf.Cos(a + half), Mathf.Sin(a + half)) * r;
            if (outLen > 0.5f) DrawColoredPolygon(new[] { baseL, dir * (r + outLen), baseR }, colour);
            if (inLen > 0.5f) DrawColoredPolygon(new[] { baseR, dir * (r - inLen), baseL }, colour);
        }
    }
}

/// <summary>
/// The recast's mouth, in the horror world at the patch's centre: PumpKing's grin opening, lit
/// from inside. Seen by the prisoners, and through the window from outside.
/// TODO(art): the mouth (PumpKing.gdd).
/// </summary>
public partial class PrisonMouth : Node2D
{
    private static readonly Color Light = new("#FFD23F");
    private static readonly Color LightPale = new("#FFF1A8");

    /// <summary>Half the grin's width, px (the prison's heart × 1.5 / 2).</summary>
    public float Width { get; set; } = 72f;
    private float _opening;
    private float _openFor = 1f;
    private float _shut;
    private float _shutFor = 1f;

    public void Open(float seconds) { _opening = _openFor = Mathf.Max(0.01f, seconds); _shut = 0f; }
    public void Shut(float seconds) { _opening = 0f; _shut = _shutFor = Mathf.Max(0.01f, seconds); }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_opening > 0f) _opening -= dt;
        else if (_shut > 0f) _shut -= dt;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float open = _opening > 0f ? 1f - _opening / _openFor : _shut > 0f ? _shut / _shutFor : 0f;
        if (open <= 0.01f) return;
        float w = Width;
        float h = w * 0.6f * open;
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
        for (int k = -1; k <= 1; k += 2)
        {
            float x = k * w * 0.35f;
            DrawColoredPolygon(new[] { new Vector2(x - w * 0.1f, -h * 0.15f), new Vector2(x + w * 0.1f, -h * 0.15f), new Vector2(x, h * 0.3f) }, LightPale);
            float xb = k * w * 0.15f;
            DrawColoredPolygon(new[] { new Vector2(xb - w * 0.09f, h * 0.95f), new Vector2(xb, h * 0.45f), new Vector2(xb + w * 0.09f, h * 0.95f) }, LightPale);
        }
    }
}

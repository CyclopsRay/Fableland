using System.Collections.Generic;
using Godot;
using Fableland.Data;
using Fableland.Map;

namespace Fableland.Bosses;

/// <summary>
/// Māra Pāpīyas (天魔波旬), Avīci's dark leader (Docs/Mara.gdd): his emanation on the floor,
/// while the colossus behind the arena holds the Wheel of Becoming (the fight's signature,
/// <see cref="WheelOfBecomingField"/>). One HP bar: the emanation's. Numbers are the design's
/// first pass (§12), before his row's damage share and the core's foe level.
///
/// His own moves (§6):
/// • <b>Claw Rake</b> (BA): a 0.35 s tell, then two swipes 0.15 s apart in a 4 m cone, 16 each
///   — three in the asuras' realm.
/// • <b>Noose of Craving</b> (S1, 9 s): a 0.4 s tell, then a noose thrown 12 m low along the
///   floor; jump it, or be yanked to his claws (0.4 s) and bitten — 25 and a 0.5 s stun.
/// • <b>Hand of the Wheel</b> (S2, 12 s): the colossus slams a 6 m stretch of floor where you
///   stand after a 1 s shadow — 40 and a knock-up. Never in the human or the gods' realm.
/// • <b>Crown of Five Skulls</b>: the seals, shown on his body (<see cref="MaraFigure"/>).
///
/// What each realm does to him while it is in force (§5) — the wheel tells him which:
/// • Gods — <b>Iron Throne</b>: he leaps onto the throne and holds it (the wheel guards him);
///   Heavy Halo every 2.5 s, one Wilting Garland a rest.
/// • Asuras — <b>Wrath</b>: arrives as Wrath Fire; the wheel quickens him; three swipes a rake.
/// • Hungry ghosts — <b>Devour</b>: arrives as Gorge — every fighter within 14 m pulled in for
///   2 s, then a bite; the wheel heals him for what he lands and lets his front swallow shots.
/// • Hell — <b>Doubt</b>: arrives as Chain Hooks, one real; every attack then shows a false tell
///   mirrored beside the true one — the true one is where his third eye looks.
/// • Animals — <b>Delusion</b>: arrives as a Blind Charge with two mirror images, which then
///   stay beside him and copy his attacks 0.3 s late at 40%.
/// • Humans — the Interval: his powers lapse; no Hand of the Wheel.
/// The Last Turn passes each unsealed realm's arrival strike; Liberation opens his claws.
///
/// Brain (§6): the realm decides first — in the gods' realm he holds the throne. Otherwise:
/// the Noose at 6–12 m when it is ready; Claw Rake within 4 m; the Hand on a player who has
/// stood still, or 3 m or more up, for 1.5 s; walk toward you. A heavy walk, no jump.
/// </summary>
public partial class Mara : Zone6BossFighter, IWheelFighter
{
    // Claw Rake
    public const float ClawRangeM = 4f, ClawHalfAngle = 55f, ClawDamage = 16f, ClawGap = 0.15f, ClawTell = 0.35f, ClawShoveMps = 6f;
    public const int ClawSwipes = 2, WrathClawSwipes = 3;
    // Noose of Craving
    public const float NooseCooldown = 9f, NooseReachM = 12f, NooseSpeedMps = 20f, NooseTell = 0.4f, NooseDamage = 25f;
    public const float NooseStun = 0.5f, NooseYankSeconds = 0.4f, NooseNearM = 6f;
    // Hand of the Wheel
    public const float HandCooldown = 12f, HandWidthM = 6f, HandColumnM = 8f, HandTell = 1f, HandDamage = 40f, HandKnockUpMps = 20f;
    public const float HandStillSeconds = 1.5f, HandStillM = 1f, HandHighM = 3f, HandOffLingers = 0.4f;
    public const float WalkMps = 3f;
    // Iron Throne
    public const float ThroneLeapSeconds = 0.8f, ThroneLeapArcM = 3f;
    public const float HaloEvery = 2.5f, HaloFirst = 1.2f, HaloTell = 0.8f, HaloDamage = 30f, HaloRadiusM = 1.5f, HaloColumnM = 3f;
    public const float GarlandAt = 2f, GarlandWidthM = 6f, GarlandFall = 0.6f, GarlandBurnSeconds = 3f, GarlandTickDamage = 6f;
    // Wrath Fire (the Asura's)
    public const float WrathFireReachM = 12f, WrathFireSpeedMps = 10f, WrathFireDamage = 15f, WrathFireStacks = 20f;
    // Gorge
    public const float GorgeSeconds = 2f, GorgePassSeconds = 0.8f, GorgeRangeM = 14f, GorgePullMps = 4f, GorgeBiteDamage = 30f, GorgeBiteRangeM = 3.5f;
    // Chain Hooks
    public const float HookDamage = 25f, HookSpeedMps = 22f, HookFromM = 14f;
    /// <summary>Doubt: a false tell fades this long before its strike.</summary>
    public const float FalseFade = 0.15f;
    // Blind Charge and the images
    public const float ChargeDamage = 30f, ChargeSpeedMps = 14f, ChargeSeconds = 1.6f, ChargeSpacingM = 5f;
    public const int ImageCount = 2;
    public const float ImageDelay = 0.3f, ImageDamageShare = 0.4f, ImageReform = 3f, ImageBesideM = 3f;

    private const string NooseSource = "mara_noose";
    private const string GorgeSource = "mara_gorge";
    private const string ChargeSource = "mara_charge";
    /// <summary>His third eye, from his origin: where the gaze of Doubt starts.</summary>
    private static readonly Vector2 Brow = new(0f, -77f);

    private enum TellKind { None, Claw, Noose }

    protected override BossDef Row => BossTable.Mara;

    /// <summary>His look and his wheel are one style (Mara.gdd §13 cues: owner z6_mara).</summary>
    public override string FeedbackId => WheelTable.CueOwner;

    private MaraFigure _figure;
    private readonly DetRandom _rng = new("z6_mara:doubt");
    private readonly HashSet<string> _sealed = new();
    // No realm holds until the wheel says which does (it says so as the fight begins): a body
    // fielded without the wheel fights with his own hands only.
    private WheelRealm _realm = WheelRealm.Humans;
    private bool _spinning, _freed;

    // An attack in its tell.
    private TellKind _tell;
    private float _tellLeft, _tellFacing = 1f;
    private bool _tellDoubled, _tellMirrored;
    // Claw Rake.
    private readonly List<float> _swipes = new();
    private int _swipeStage, _lastSwipes;
    private float _swipeFacing = 1f;
    // Noose.
    private float _nooseCd;
    private CharacterController _yanked;
    private float _yankLeft;
    // Hand of the Wheel.
    private float _handCd, _handOffLeft;
    private readonly List<MaraStrike> _hands = new();
    private MaraStrike _trueHand;
    // Iron Throne.
    private bool _onThrone, _leapUp;
    private float _leapT = -1f;
    private Vector2 _leapFrom, _leapTo, _floorSpot;
    private float _haloLeft, _realmAge;
    private bool _garlandCast;
    // Gorge.
    private float _gorgeLeft;
    private readonly List<CharacterController> _gorged = new();
    // Blind Charge.
    private float _chargeLeft, _chargeDir = 1f;
    private bool _imagesStay;
    private readonly HashSet<ulong> _charged = new();
    // Delusion's images and the blows they copy.
    private readonly List<MaraImage> _images = new();
    private readonly Dictionary<MaraImage, HashSet<ulong>> _imageCharged = new();
    private readonly List<(float Left, MaraImage Image, TellKind Kind)> _copies = new();
    // Doubt's hooks.
    private readonly List<ChainHook> _hooks = new();
    // What the brain watches for the Hand.
    private float _stillFor, _highFor;
    private Vector2 _stillAnchor;

    // ── Read-only, for checks and the debug HUD ──────────────────────────────────
    public WheelRealm RealmInForce => _realm;
    public bool Spinning => _spinning;
    public bool Freed => _freed;
    public bool OnThrone => _onThrone;
    public bool Leaping => _leapT >= 0f;
    public bool Gorging => _gorgeLeft > 0f;
    public bool Charging => _chargeLeft > 0f;
    public bool Yanking => _yanked != null;
    public bool TellShowing => _tell != TellKind.None;
    public bool TellDoubled => _tellDoubled;
    /// <summary>Under Doubt, whether the true tell is the mirrored one (behind him).</summary>
    public bool TellMirrored => _tellMirrored;
    public int LastSwipes => _lastSwipes;
    public IReadOnlyList<MaraImage> Images => _images;
    public bool HandOut => _hands.Exists(hand => IsInstanceValid(hand));
    public bool InDoubt => _realm == WheelRealm.Hell && !_spinning;
    public bool InWrath => _realm == WheelRealm.Asuras && !_spinning;
    public float StillFor => _stillFor;
    public override (float Remaining, float Max) ShiftCooldown => (_nooseCd, ScaleCooldown(NooseCooldown));
    public override (float Remaining, float Max) ESkillCooldown => (_handCd, ScaleCooldown(HandCooldown));
    public override bool LocksPhysicalMotion => _leapT >= 0f;

    protected override void InitCharacter()
    {
        base.InitCharacter();
        MaxJumps = 0;                       // he leaps only onto and off the throne: his own arc
        MoveSpeed = Units.Px(WalkMps);
        HitRadius = Units.Px(1.4f);
        _figure = new MaraFigure { Name = "Figure", UseParentMaterial = true };
        Sprite.AddChild(_figure);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!IsDefeated)
        {
            if (_nooseCd > 0f) _nooseCd = Mathf.Max(0f, _nooseCd - dt);
            if (_handCd > 0f) _handCd = Mathf.Max(0f, _handCd - dt);
            if (_handOffLeft > 0f) _handOffLeft = Mathf.Max(0f, _handOffLeft - dt);
            _realmAge += dt;
            TickTell(dt);
            TickSwipes(dt);
            TickCopies(dt);
            TickYank(dt);
            TickGorge(dt);
            TickCharge(dt);
            TickThrone(dt);
            TickImages();
            _hands.RemoveAll(hand => !IsInstanceValid(hand));
            _hooks.RemoveAll(hook => !IsInstanceValid(hook));
        }
        base._PhysicsProcess(delta);
        TickLeap(dt);
    }

    private bool CanStart => !_freed && !_spinning && _tell == TellKind.None && _swipes.Count == 0 && _yanked == null
                             && _leapT < 0f && _gorgeLeft <= 0f && _chargeLeft <= 0f;

    private bool CanHand => !_freed && !_spinning && _handCd <= 0f && !HandOut
                            && _realm != WheelRealm.Humans && _realm != WheelRealm.Gods;

    private void TurnTo(float direction)
    {
        Facing = direction >= 0f ? 1f : -1f;
        Sprite.FlipH = Facing < 0f;
    }

    // ── Claw Rake and the Noose: a tell, then the strike ─────────────────────────

    protected override void HandleBA()
    {
        if (!CanStart || !TryConsumeAmmo()) return;
        BeginTell(TellKind.Claw, ClawTell);
        StartAmmoAttackInterval();
        RequestAmmoReload();
    }

    protected override void HandleSkill1()
    {
        if (!CanStart || _nooseCd > 0f || _realm == WheelRealm.Gods) return;
        _nooseCd = ScaleCooldown(NooseCooldown);
        BeginTell(TellKind.Noose, NooseTell);
    }

    /// <summary>Starts an attack's tell. Under Doubt a false tell shows too, mirrored on his
    /// other side, and which of the two is true is a coin his third eye has already read.</summary>
    private void BeginTell(TellKind kind, float seconds)
    {
        // A press is read before the physics tick that turns the body to the pad's facing, so
        // a tell would face the old way: take the way the controller faces now (KNOWLEDGE v0.12.3).
        if (Controls.FacingHint != 0f) TurnTo(Controls.FacingHint);
        _tell = kind;
        _tellLeft = seconds;
        _tellFacing = Facing;
        _tellDoubled = InDoubt;
        _tellMirrored = _tellDoubled && _rng.Chance(0.5);
        if (_figure != null) _figure.Telling = true;
        CueBus.Emit(this, kind == TellKind.Claw ? "ba.tell" : "s1.tell", variant: _tellDoubled ? 2 : 1,
            magnitude: seconds, direction: FacingDirection);
    }

    private void TickTell(float dt)
    {
        if (_tell == TellKind.None) return;
        _tellLeft -= dt;
        if (_tellLeft > 0f) return;
        TellKind kind = _tell;
        _tell = TellKind.None;
        if (_figure != null) _figure.Telling = false;
        float direction = _tellMirrored ? -_tellFacing : _tellFacing;
        if (kind == TellKind.Claw)
        {
            int swipes = InWrath ? WrathClawSwipes : ClawSwipes;
            _swipeFacing = direction;
            _swipes.Clear();
            for (int i = 0; i < swipes; i++) _swipes.Add(i * ClawGap);
            _swipeStage = 0;
            _lastSwipes = swipes;
            foreach (MaraImage image in LiveImages())
                for (int i = 0; i < swipes; i++) _copies.Add((ImageDelay + i * ClawGap, image, TellKind.Claw));
        }
        else
        {
            ThrowNoose(direction);
            foreach (MaraImage image in LiveImages()) _copies.Add((ImageDelay, image, TellKind.Noose));
        }
    }

    private void CancelAttack()
    {
        _tell = TellKind.None;
        _swipes.Clear();
        _copies.Clear();
        if (_figure != null) _figure.Telling = false;
    }

    private void TickSwipes(float dt)
    {
        for (int i = _swipes.Count - 1; i >= 0; i--)
        {
            _swipes[i] -= dt;
            if (_swipes[i] > 0f) continue;
            _swipes.RemoveAt(i);
            _swipeStage++;
            TurnTo(_swipeFacing);
            CueBus.Emit(this, "ba.swing", variant: _swipeStage, size: Units.Px(ClawRangeM), magnitude: ClawDamage, direction: FacingDirection,
                extents: CueShapes.Cone(Units.Px(ClawRangeM), ClawHalfAngle));
            // Only the last swipe shoves: a shove gives the player 0.8 s of grace, which would
            // swallow every swipe after it (KNOWLEDGE v0.12.3).
            float shove = _swipes.Count == 0 ? Units.Px(ClawShoveMps) : 0f;
            MeleeCone(Units.Px(ClawRangeM), ClawHalfAngle, ClawDamage, shove, false, 0f,
                cueAction: "ba.swing", cueVariant: _swipeStage);
        }
    }

    private void ThrowNoose(float direction)
    {
        var noose = new MaraNoose { Name = "Noose" };
        GetParent().AddChild(noose);
        noose.GlobalPosition = new Vector2(GlobalPosition.X + direction * Units.Px(0.8f), Feet.Y);
        noose.Init(this, direction, Units.Px(NooseSpeedMps), Units.Px(NooseReachM), NooseCaught);
        CueBus.Emit(this, "s1.cast", magnitude: NooseDamage, direction: new Vector2(direction, 0f));
    }

    /// <summary>The noose caught someone: craving binds them to the wheel — yanked to his claws.</summary>
    private void NooseCaught(ICombatTarget target)
    {
        if (_freed || target is not CharacterController body || body.IsDefeated) return;
        EndYank(bite: false);
        _yanked = body;
        _yankLeft = NooseYankSeconds;
        float dx = GlobalPosition.X - body.GlobalPosition.X;
        float gap = Mathf.Max(0f, Mathf.Abs(dx) - Units.Px(1.6f));
        body.SetContinuousExternalVelocity(NooseSource, new Vector2(Mathf.Sign(dx) * gap / NooseYankSeconds, 0f));
        CueBus.Emit(this, "s1.catch", magnitude: NooseDamage);
    }

    private void TickYank(float dt)
    {
        if (_yanked == null) return;
        if (!IsInstanceValid(_yanked) || _yanked.IsDefeated)
        {
            _yanked = null;
            return;
        }
        _yankLeft -= dt;
        if (_yankLeft <= 0f) EndYank(bite: true);
    }

    private void EndYank(bool bite)
    {
        CharacterController body = _yanked;
        _yanked = null;
        if (body == null || !IsInstanceValid(body)) return;
        body.ClearContinuousExternalVelocity(NooseSource);
        if (!bite || body.IsDefeated) return;
        TurnTo(body.GlobalPosition.X - GlobalPosition.X);
        float dealt = body.TakeHit(new HitInfo(NooseDamage * DamageDealtMultiplier, new Vector2(Facing * Units.Px(3f), -Units.Px(2f)), NooseStun)
            .From(this, "s1.bite"), GlobalPosition);
        if (dealt > 0f) ReportDamageDealt(dealt);
    }

    // ── Hand of the Wheel ─────────────────────────────────────────────────────────

    protected override void HandleSkill2()
    {
        if (!CanHand) return;
        CharacterController target = TargetBody;
        if (target == null || target.IsDefeated) return;
        _handCd = ScaleCooldown(HandCooldown);
        bool doubled = InDoubt;
        bool mirrored = doubled && _rng.Chance(0.5);
        _hands.Clear();
        _trueHand = null;
        Vector2 at = target.GlobalPosition;
        SpawnStrike(MaraStrikeKind.Hand, at, !mirrored);
        if (doubled) SpawnStrike(MaraStrikeKind.Hand, new Vector2(2f * GlobalPosition.X - at.X, at.Y), mirrored);
        _handOffLeft = HandTell + HandOffLingers;
        _stillFor = _highFor = 0f;
        CueBus.Emit(this, "s2.cast", variant: doubled ? 2 : 1, size: Units.Px(HandWidthM), magnitude: HandTell);
    }

    /// <summary>A strike that falls on the surface under <paramref name="at"/>.</summary>
    private MaraStrike SpawnStrike(MaraStrikeKind kind, Vector2 at, bool real)
    {
        var strike = new MaraStrike { Name = kind.ToString() };
        GetParent().AddChild(strike);
        strike.GlobalPosition = new Vector2(at.X, SurfaceUnder(at));
        switch (kind)
        {
            case MaraStrikeKind.Hand:
                strike.Init(this, kind, Units.Px(HandWidthM), Units.Px(HandColumnM), HandTell, HandDamage, Units.Px(HandKnockUpMps), real, "s2.slam");
                _hands.Add(strike);
                if (real) _trueHand = strike;
                break;
            case MaraStrikeKind.Halo:
                strike.Init(this, kind, Units.Px(HaloRadiusM * 2f), Units.Px(HaloColumnM), HaloTell, HaloDamage, Units.Px(4f), real, "realm.halo");
                break;
            case MaraStrikeKind.Garland:
                strike.Init(this, kind, Units.Px(GarlandWidthM), 0f, GarlandFall, GarlandTickDamage, 0f, real, "realm.garland", GarlandBurnSeconds);
                break;
        }
        return strike;
    }

    /// <summary>The height of the ground or ledge under a point (where a strike from above
    /// lands) — a fall back to the feet of a body standing there when nothing is below.</summary>
    private float SurfaceUnder(Vector2 point)
    {
        uint mask = (1u << (Units.LayerGround - 1)) | (1u << (Units.LayerPlatform - 1));
        var query = PhysicsRayQueryParameters2D.Create(point - new Vector2(0f, Units.Px(0.5f)), point + new Vector2(0f, Units.Px(40f)), mask);
        Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? ((Vector2)hit["position"]).Y : point.Y + Units.PlayerHeightPx * 0.5f;
    }

    // ── The realms' strikes ───────────────────────────────────────────────────────

    /// <summary>Iron Throne: onto the throne — the perch he opened on until the arena is painted.</summary>
    private void TickThrone(float dt)
    {
        if (_realm != WheelRealm.Gods || _spinning || _freed || !_onThrone) return;
        _haloLeft -= dt;
        if (_haloLeft <= 0f)
        {
            _haloLeft += HaloEvery;
            CastHalo();
        }
        if (!_garlandCast && _realmAge >= GarlandAt)
        {
            _garlandCast = true;
            CharacterController target = TargetBody;
            if (target != null && !target.IsDefeated) SpawnStrike(MaraStrikeKind.Garland, target.GlobalPosition, real: true);
        }
    }

    private void CastHalo()
    {
        CharacterController target = TargetBody;
        if (target == null || target.IsDefeated) return;
        SpawnStrike(MaraStrikeKind.Halo, target.GlobalPosition, real: true);
        CueBus.Emit(this, "realm.halo_cast", magnitude: HaloDamage);
    }

    private void LeapTo(Vector2 target, bool up)
    {
        CancelAttack();
        _leapFrom = GlobalPosition;
        _leapTo = target;
        _leapT = 0f;
        _leapUp = up;
        _onThrone = false;
        CueBus.Emit(this, "realm.leap", variant: up ? 1 : 0);
    }

    private void TickLeap(float dt)
    {
        if (_leapT < 0f || HitStopHeld) return;
        _leapT = Mathf.Min(1f, _leapT + dt / ThroneLeapSeconds);
        float t = _leapT;
        GlobalPosition = _leapFrom.Lerp(_leapTo, t) + new Vector2(0f, -Units.Px(ThroneLeapArcM) * 4f * t * (1f - t));
        if (t < 1f) return;
        _leapT = -1f;
        _onThrone = _leapUp;
        Velocity = Vector2.Zero;
    }

    private void LeapDown()
    {
        if (_onThrone || (_leapT >= 0f && _leapUp)) LeapTo(_floorSpot, up: false);
    }

    private void WrathFire()
    {
        CueBus.Emit(this, "realm.wrath_fire", size: Units.Px(WrathFireReachM), magnitude: WrathFireDamage);
        foreach (float direction in new[] { -1f, 1f })
        {
            var wave = new WrathFireWave { Name = "WrathFire" };
            GetParent().AddChild(wave);
            wave.GlobalPosition = new Vector2(GlobalPosition.X, Feet.Y);
            wave.Init(this, direction, Units.Px(WrathFireSpeedMps), Units.Px(WrathFireReachM), WrathFireDamage, WrathFireStacks);
        }
    }

    private void BeginGorge(float seconds)
    {
        EndGorge(bite: false);
        CancelAttack();
        _gorgeLeft = seconds;
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
            if (target is CharacterController body && body.GlobalPosition.DistanceTo(GlobalPosition) <= Units.Px(GorgeRangeM))
                _gorged.Add(body);
        CueBus.Start(this, "realm.gorge", size: Units.Px(GorgeRangeM), magnitude: seconds);
    }

    private void TickGorge(float dt)
    {
        if (_gorgeLeft <= 0f) return;
        _gorgeLeft -= dt;
        foreach (CharacterController body in _gorged)
        {
            if (!IsInstanceValid(body) || body.IsDefeated) continue;
            float side = Mathf.Sign(GlobalPosition.X - body.GlobalPosition.X);
            body.SetContinuousExternalVelocity(GorgeSource, new Vector2(side * Units.Px(GorgePullMps), 0f));
        }
        if (_gorgeLeft <= 0f) EndGorge(bite: true);
    }

    private void EndGorge(bool bite)
    {
        bool was = _gorgeLeft > 0f || _gorged.Count > 0;
        _gorgeLeft = 0f;
        foreach (CharacterController body in _gorged)
            if (IsInstanceValid(body)) body.ClearContinuousExternalVelocity(GorgeSource);
        _gorged.Clear();
        if (was) CueBus.Stop(this, "realm.gorge");
        if (bite) GorgeBite();
    }

    /// <summary>The bite at the end of the pull: whatever it delivered, on every side of him (a
    /// cone cannot reach behind, so a circle).</summary>
    private void GorgeBite()
    {
        CueBus.Emit(this, "realm.gorge_bite", size: Units.Px(GorgeBiteRangeM), magnitude: GorgeBiteDamage);
        var swing = new SwingStop();
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
        {
            Vector2 to = target.GlobalPosition - GlobalPosition;
            if (to.Length() - target.HitRadius > Units.Px(GorgeBiteRangeM)) continue;
            var knock = new Vector2(to.X >= 0f ? 1f : -1f, -0.35f).Normalized() * Units.Px(6f);
            float dealt = target.TakeHit(new HitInfo(GorgeBiteDamage * DamageDealtMultiplier, knock).From(this, "realm.gorge_bite"), GlobalPosition);
            swing.Landed(target, dealt);
            if (dealt > 0f) ReportDamageDealt(dealt);
        }
        ApplyHitStop(swing.AttackerStop());
    }

    private void ChainHooks()
    {
        CharacterController target = TargetBody;
        if (target == null || target.IsDefeated) return;
        Vector2 aim = target.GlobalPosition;
        bool leftIsReal = _rng.Chance(0.5);
        foreach (float wall in new[] { -1f, 1f })
        {
            var hook = new ChainHook { Name = "ChainHook" };
            GetParent().AddChild(hook);
            hook.GlobalPosition = aim + new Vector2(wall * Units.Px(HookFromM), 0f);
            hook.Init(this, -wall, Units.Px(HookSpeedMps), Units.Px(HookFromM * 2f), Units.Px(HookFromM), HookDamage, (wall < 0f) == leftIsReal);
            _hooks.Add(hook);
        }
        CueBus.Emit(this, "realm.chain_hooks", magnitude: HookDamage);
    }

    /// <summary>Delusion: he charges, and two mirror images charge with him, one ahead and one
    /// behind. Jump them. <paramref name="stay"/>: the images stay beside him afterwards.</summary>
    private void BlindCharge(bool stay)
    {
        CancelAttack();
        CharacterController target = TargetBody;
        _chargeDir = target != null && target.GlobalPosition.X < GlobalPosition.X ? -1f : 1f;
        _chargeLeft = ChargeSeconds;
        _charged.Clear();
        _imagesStay = stay;
        TurnTo(_chargeDir);
        EnsureImages(new Vector2(_chargeDir * Units.Px(ChargeSpacingM), 0f), new Vector2(-_chargeDir * Units.Px(ChargeSpacingM), 0f));
        foreach (MaraImage image in _images)
        {
            image.Facing = _chargeDir;
            _imageCharged[image] = new HashSet<ulong>();
        }
        CueBus.Emit(this, "realm.blind_charge", magnitude: ChargeDamage, direction: new Vector2(_chargeDir, 0f));
    }

    private void TickCharge(float dt)
    {
        if (_chargeLeft <= 0f) return;
        _chargeLeft -= dt;
        SetContinuousExternalVelocity(ChargeSource, new Vector2(_chargeDir * Units.Px(ChargeSpeedMps), 0f));
        ChargeHits(GlobalPosition, ChargeDamage, _charged, image: false);
        foreach (MaraImage image in LiveImages())
            if (_imageCharged.TryGetValue(image, out HashSet<ulong> hit)) ChargeHits(image.GlobalPosition, ChargeDamage * ImageDamageShare, hit, image: true);
        if (_chargeLeft <= 0f || (IsOnWall() && _chargeLeft < ChargeSeconds - 0.15f)) EndCharge();
    }

    private void ChargeHits(Vector2 origin, float damage, HashSet<ulong> already, bool image)
    {
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
        {
            if (target is not Node2D node || already.Contains(target.TargetId)) continue;
            Vector2 to = node.GlobalPosition - origin;
            if (Mathf.Abs(to.X) > Units.Px(1.3f) + target.HitRadius || to.Y < -Units.Px(3.2f) || to.Y > Units.Px(1.2f)) continue;
            already.Add(target.TargetId);
            var knock = new Vector2(_chargeDir * Units.Px(6f), -Units.Px(8f));
            HitInfo hit = new HitInfo(damage * DamageDealtMultiplier, knock);
            hit = image ? hit.FromRanged(this, "realm.blind_charge", variant: 2) : hit.From(this, "realm.blind_charge");
            float dealt = target.TakeHit(hit, origin);
            if (dealt > 0f) ReportDamageDealt(dealt);
        }
    }

    private void EndCharge()
    {
        _chargeLeft = 0f;
        ClearContinuousExternalVelocity(ChargeSource);
        if (!_imagesStay) ClearImages();
        else
            for (int i = 0; i < _images.Count; i++)
                if (IsInstanceValid(_images[i])) _images[i].Offset = BesideOffset(i);
    }

    // ── Delusion's images ─────────────────────────────────────────────────────────

    private static Vector2 BesideOffset(int i) => new((i == 0 ? -1f : 1f) * Units.Px(ImageBesideM), 0f);

    private void EnsureImages(params Vector2[] offsets)
    {
        for (int i = 0; i < ImageCount; i++)
        {
            MaraImage image = i < _images.Count && IsInstanceValid(_images[i]) ? _images[i] : null;
            if (image == null)
            {
                image = new MaraImage { Name = "MaraImage" + i };
                GetParent().AddChild(image);
                image.Init(this, offsets[i]);
                if (i < _images.Count) _images[i] = image;
                else _images.Add(image);
            }
            image.Offset = offsets[i];
            image.GlobalPosition = GlobalPosition + offsets[i];
        }
    }

    private IEnumerable<MaraImage> LiveImages()
    {
        foreach (MaraImage image in _images)
            if (IsInstanceValid(image) && !image.Popped) yield return image;
    }

    private void ClearImages()
    {
        foreach (MaraImage image in _images)
            if (IsInstanceValid(image)) image.QueueFree();
        _images.Clear();
        _imageCharged.Clear();
        _copies.Clear();
    }

    private void TickImages()
    {
        if (_images.Count == 0) return;
        CharacterController target = TargetBody;
        foreach (MaraImage image in _images)
        {
            if (!IsInstanceValid(image)) continue;
            image.GlobalPosition = GlobalPosition + image.Offset;
            if (_chargeLeft <= 0f && target != null)
                image.Facing = target.GlobalPosition.X >= image.GlobalPosition.X ? 1f : -1f;
        }
    }

    /// <summary>The images copy his blows a beat late and weaker; a popped one copies nothing.</summary>
    private void TickCopies(float dt)
    {
        for (int i = _copies.Count - 1; i >= 0; i--)
        {
            (float left, MaraImage image, TellKind kind) = _copies[i];
            left -= dt;
            if (left > 0f)
            {
                _copies[i] = (left, image, kind);
                continue;
            }
            _copies.RemoveAt(i);
            if (!IsInstanceValid(image) || image.Popped) continue;
            if (kind == TellKind.Claw) ImageSwipe(image);
            else ImageNoose(image);
        }
    }

    private void ImageSwipe(MaraImage image)
    {
        Vector2 origin = image.GlobalPosition;
        float half = Mathf.DegToRad(ClawHalfAngle);
        var aim = new Vector2(image.Facing, 0f);
        CueBus.EmitAt(FeedbackId, "ba.image_swing", origin, image, Side, size: Units.Px(ClawRangeM), direction: aim, ownerClass: FeedbackClass);
        foreach (ICombatTarget target in CombatTargets.HostileTo(GetTree(), Side))
        {
            Vector2 to = target.GlobalPosition - origin;
            float dist = to.Length(), r = target.HitRadius;
            if (dist - r > Units.Px(ClawRangeM)) continue;
            float tolerance = dist > r ? Mathf.Asin(Mathf.Clamp(r / dist, 0f, 1f)) : Mathf.Pi;
            if (dist > 0.01f && to.Normalized().Dot(aim) < Mathf.Cos(half + tolerance)) continue;
            var knock = new Vector2(image.Facing, -0.35f).Normalized() * Units.Px(ClawShoveMps) * ImageDamageShare;
            float dealt = target.TakeHit(new HitInfo(ClawDamage * ImageDamageShare * DamageDealtMultiplier, knock).FromRanged(this, "ba.image_swing"), origin);
            if (dealt > 0f) ReportDamageDealt(dealt);
        }
    }

    private void ImageNoose(MaraImage image)
    {
        var noose = new MaraNoose { Name = "ImageNoose" };
        GetParent().AddChild(noose);
        noose.GlobalPosition = new Vector2(image.GlobalPosition.X + image.Facing * Units.Px(0.8f), image.GlobalPosition.Y + Units.PlayerHeightPx * 0.5f);
        noose.Init(this, image.Facing, Units.Px(NooseSpeedMps), Units.Px(NooseReachM), target =>
        {
            if (_freed || target == null) return;
            float dealt = target.TakeHit(new HitInfo(NooseDamage * ImageDamageShare * DamageDealtMultiplier, Vector2.Zero, NooseStun)
                .FromRanged(this, "s1.image_catch"), noose.GlobalPosition);
            if (dealt > 0f) ReportDamageDealt(dealt);
        });
    }

    // ── IWheelFighter: the wheel tells him which realm is in force ───────────────

    public void WheelBegins(IReadOnlyCollection<string> sealedPoisons)
    {
        _sealed.Clear();
        if (sealedPoisons != null) foreach (string id in sealedPoisons) _sealed.Add(id);
        _figure?.SetSealed(_sealed);
    }

    public void RealmLanded(WheelRealm realm, bool arrival)
    {
        WheelRealm before = _realm;
        _spinning = false;
        _realm = realm;
        _realmAge = 0f;
        if (before == WheelRealm.Animals && realm != WheelRealm.Animals) ClearImages();
        if (realm != WheelRealm.Gods) LeapDown();
        if (_freed) return;
        switch (realm)
        {
            case WheelRealm.Gods:
                if (!_onThrone && !(_leapT >= 0f && _leapUp))
                {
                    _floorSpot = GlobalPosition;
                    LeapTo(HomePoint, up: true);
                }
                _haloLeft = HaloFirst;
                _garlandCast = false;
                break;
            case WheelRealm.Asuras:
                if (arrival) WrathFire();
                break;
            case WheelRealm.HungryGhosts:
                if (arrival) BeginGorge(GorgeSeconds);
                break;
            case WheelRealm.Hell:
                if (arrival) ChainHooks();
                break;
            case WheelRealm.Animals:
                if (arrival) BlindCharge(stay: true);
                else EnsureImages(BesideOffset(0), BesideOffset(1));
                break;
        }
    }

    public void RealmPassed(WheelRealm realm)
    {
        if (_freed) return;
        switch (realm)
        {
            case WheelRealm.Gods: CastHalo(); break;
            case WheelRealm.Asuras: WrathFire(); break;
            case WheelRealm.HungryGhosts: BeginGorge(GorgePassSeconds); break;
            case WheelRealm.Hell: ChainHooks(); break;
            case WheelRealm.Animals: BlindCharge(stay: false); break;
        }
    }

    public void LastTurnBegan()
    {
        _spinning = true;
        CancelAttack();
        LeapDown();
        ClearImages();
    }

    public bool HandOffWheel => HandOut || _handOffLeft > 0f;

    public void Liberated()
    {
        _freed = true;
        StopEverything();
        if (_figure != null) _figure.Freed = true;
        Brain?.Sleep();
    }

    private void StopEverything()
    {
        CancelAttack();
        EndYank(bite: false);
        EndGorge(bite: false);
        if (_chargeLeft > 0f) EndCharge();
        ClearContinuousExternalVelocity(ChargeSource);
        ClearImages();
        foreach (MaraStrike hand in _hands) if (IsInstanceValid(hand)) hand.QueueFree();
        foreach (ChainHook hook in _hooks) if (IsInstanceValid(hook)) hook.QueueFree();
        _hands.Clear();
        _hooks.Clear();
    }

    protected override void OnStandDown() => StopEverything();

    protected override void OnDefeated()
    {
        StopEverything();
        base.OnDefeated();
    }

    public override void _ExitTree()
    {
        EndYank(bite: false);
        EndGorge(bite: false);
        ClearImages();
        base._ExitTree();
    }

    // ── Brain ─────────────────────────────────────────────────────────────────────

    protected override void Sense(float dt)
    {
        if (!Senses.HasTarget)
        {
            _stillFor = _highFor = 0f;
            return;
        }
        Vector2 at = Senses.TargetPosition;
        if (at.DistanceTo(_stillAnchor) <= Units.Px(HandStillM)) _stillFor += dt;
        else
        {
            _stillFor = 0f;
            _stillAnchor = at;
        }
        // "3 m or more up": above his own feet — he stands on the floor except on the throne.
        float theirFeet = at.Y + Units.PlayerHeightPx * 0.5f;
        if (!_onThrone && _leapT < 0f && Feet.Y - theirFeet >= Units.Px(HandHighM)) _highFor += dt;
        else _highFor = 0f;
    }

    protected override void BuildRules(BossRuleSet rules, VirtualPad pad)
    {
        // legs
        rules.Add(new BossRule(BossBrain.Move, "held by the wheel",
            () => _freed || _spinning || _leapT >= 0f || _gorgeLeft > 0f || _chargeLeft > 0f || _yanked != null)
        {
            Each = () => StopWalking(pad),
        });
        rules.Add(new BossRule(BossBrain.Move, "hold the throne", () => _realm == WheelRealm.Gods)
        {
            Each = () =>
            {
                StopWalking(pad);
                FaceTarget(pad);
            },
        });
        rules.Add(new BossRule(BossBrain.Move, "walk toward you",
            () => Senses.HasTarget && TargetMetres > ClawRangeM * 0.75f && _tell == TellKind.None && _swipes.Count == 0)
        {
            Each = () =>
            {
                WalkToward(pad, Senses.TargetPosition.X, Units.Px(ClawRangeM * 0.6f));
                FaceTarget(pad);
            },
        });
        rules.Add(new BossRule(BossBrain.Move, "stand", () => true)
        {
            Each = () =>
            {
                StopWalking(pad);
                if (_tell == TellKind.None && _swipes.Count == 0) FaceTarget(pad);
            },
        });

        // hands: the realm decides first — in the gods' realm he holds the throne
        rules.Add(new BossRule(BossBrain.Act, "throne: claw rake", () => _realm == WheelRealm.Gods && CanStart && TargetMetres <= ClawRangeM)
        {
            OnStart = () =>
            {
                FaceTarget(pad);
                pad.Tap(ControlAction.Attack);
            },
            Cooldown = 0.3f,
        });
        rules.Add(new BossRule(BossBrain.Act, "noose of craving",
            () => _realm != WheelRealm.Gods && CanStart && _nooseCd <= 0f && TargetMetres >= NooseNearM && TargetMetres <= NooseReachM)
        {
            OnStart = () =>
            {
                FaceTarget(pad);
                pad.Tap(ControlAction.Skill1);
            },
            Cooldown = 0.5f,
        });
        rules.Add(new BossRule(BossBrain.Act, "claw rake", () => _realm != WheelRealm.Gods && CanStart && TargetMetres <= ClawRangeM)
        {
            OnStart = () =>
            {
                FaceTarget(pad);
                pad.Tap(ControlAction.Attack);
            },
            Cooldown = 0.3f,
        });
        rules.Add(new BossRule(BossBrain.Act, "hand of the wheel",
            () => CanHand && (_stillFor >= HandStillSeconds || _highFor >= HandStillSeconds))
        {
            OnStart = () => pad.Tap(ControlAction.Skill2),
            Cooldown = 1f,
        });
    }

    // ── Tells (placeholder drawing) ──────────────────────────────────────────────

    /// <summary>The tell of the attack he is starting; under Doubt its false twin on his other
    /// side and, from his third eye, a hairline of gold to the true one. The false one fades
    /// <see cref="FalseFade"/> before the strike.</summary>
    public override void _Draw()
    {
        base._Draw();
        var tellColour = new Color(1f, 0.45f, 0.2f, 0.85f);
        var gaze = new Color(1f, 0.85f, 0.35f, 0.95f);
        if (_tell != TellKind.None)
        {
            float trueDirection = _tellMirrored ? -_tellFacing : _tellFacing;
            DrawTell(trueDirection, tellColour);
            if (_tellDoubled)
            {
                if (_tellLeft > FalseFade) DrawTell(-trueDirection, tellColour);
                DrawLine(Brow, TellPoint(trueDirection), gaze, 1.2f);
            }
        }
        if (_trueHand != null && IsInstanceValid(_trueHand) && _hands.Count > 1)
            DrawLine(Brow, ToLocal(_trueHand.GlobalPosition) - new Vector2(0f, 6f), gaze, 1.2f);
        if (_hooks.Count > 1)
            foreach (ChainHook hook in _hooks)
                if (IsInstanceValid(hook) && hook.Real) DrawLine(Brow, ToLocal(hook.GlobalPosition), gaze, 1.2f);
    }

    private void DrawTell(float direction, Color colour)
    {
        if (_tell == TellKind.Claw)
            DrawConeLocal(Units.Px(ClawRangeM), direction > 0f ? 0f : Mathf.Pi, Mathf.DegToRad(ClawHalfAngle), colour, 2f);
        else
            DrawLine(new Vector2(direction * Units.Px(1f), 26f), new Vector2(direction * Units.Px(NooseReachM), 26f), colour, 3f);
    }

    private static Vector2 TellPoint(float direction) => new(direction * Units.Px(2.5f), -8f);
}

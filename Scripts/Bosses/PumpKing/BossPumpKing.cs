namespace Fableland.Bosses;

using Godot;
using Fableland.Data;

/// <summary>
/// BOSS PumpKing — the PumpKing you meet at the Kingdom of Horror's 4-1, on the balcony of
/// his castle, and recruit by beating him (NODES.gdd §4.5, FOES.gdd §8.3).
///
/// He IS the protagonist's kit — the same head, shell and Soul Form, inherited — on the
/// other side: <see cref="Side"/> is Foe, so the base class makes his head bowl at the
/// player, takes his camera away and never lets him read the keyboard. He is NOT the
/// protagonist: bulk, damage, poise, magazine and cooldowns come from his own row,
/// <see cref="BossTable.PumpKing"/>.
///
/// His hands are a rule list (<see cref="BuildRules"/>) pressing the kit's own buttons:
///
///   legs   soul flight   — in Soul Form: fly to the perch he chose
///          hold perch    — otherwise: walk (and jump) back to his perch, face the player
///   hands  detonate      — his rolling head is beside the player: set it off
///          call back     — his head has rolled past the player: set it off, reload
///          soul away     — the player has been in his face too long: Soul Form, new perch
///          bowl          — head on his neck and pumped enough for the range: throw it
///          pump up       — pump toward the shell the range calls for
///
/// He holds the high ground and throws downhill: on the flat a head dies within a few
/// metres, on the slope under his balcony it runs all the way down.
/// </summary>
public partial class BossPumpKing : PumpKing, IBossFighter
{
    public override CombatSide Side => CombatSide.Foe;
    protected override CharacterAmmoDef AmmoDefinition => BossTable.PumpKing.Ammo;

    // ── Rule tuning (FOES.gdd §8.3; metres and seconds; first pass) ──────────────
    /// <summary>Share of the blast radius the player must be inside before he detonates.</summary>
    private const float DetonateShare = 0.8f;
    /// <summary>A head that has been rolling away from the player this long is called back.</summary>
    private const float HeadGoneSeconds = 0.9f;
    /// <summary>…provided it is at least this many blast radii from the player.</summary>
    private const float HeadGoneRadii = 1.5f;
    /// <summary>"In my face": the player this close, and about level with him…</summary>
    private const float PressureRangeM = 3.5f;
    private const float PressureLevelM = 2.5f;
    /// <summary>…for this long, and he leaves in Soul Form.</summary>
    private const float PressureSeconds = 1.2f;
    /// <summary>Pump stacks he wants before a throw, by range: none up close.</summary>
    private const float PumpOneBeyondM = 5f;
    private const float PumpTwoBeyondM = 8f;
    private const float PumpThreeBeyondM = 14f;
    /// <summary>How far ahead of a moving player he aims.</summary>
    private const float LeadSeconds = 0.25f;
    /// <summary>Close enough to a perch to call it reached.</summary>
    private const float PerchReachM = 0.6f;
    /// <summary>In Soul Form he crosses this far above the higher of the two ends…</summary>
    private const float SoulCruiseM = 1.5f;
    /// <summary>…and starts coming down once this close to the perch, sideways.</summary>
    private const float SoulDescendWithinM = 2f;

    private BossBrain _brain;
    private BossArena _arena;
    private readonly BossSenses _senses = new();

    private int _perch;                // index of the perch he is holding / heading for
    private float _pressure;           // seconds the player has been in his face
    private float _headGone;           // seconds his live head has been rolling away
    private float _lastHeadDistance;

    // ── The boss mark ─────────────────────────────────────────────────────────

    /// <summary>
    /// NOTE(art): placeholder. A black-coated PumpKing whose head is off is nearly invisible
    /// against his own castle's black stone, and the realm's rule is that a fighter reads
    /// first (RealmStyleBible §2.6). Until his boss art exists, a small mark in the realm's
    /// danger red hangs over him — the same colour as the pennants on his battlements.
    /// </summary>
    public override void _Draw()
    {
        base._Draw();
        float top = -Units.Px(2.3f);
        Vector2[] mark = { new(-9f, top), new(9f, top), new(0f, top + 14f) };
        DrawColoredPolygon(mark, new Color("#D8323A"));
        DrawPolyline(new[] { mark[0], mark[1], mark[2], mark[0] }, new Color("#FFF1A8"), 1.5f, antialiased: true);
    }

    // ── IBossFighter ──────────────────────────────────────────────────────────

    public void BeginFight(BossDef def, int foeLevel, BossArena arena)
    {
        BossSetup.ApplyRow(this, def, foeLevel);
        SoulCooldown *= def.CooldownScale;
        PumpInnerCD *= def.CooldownScale;

        _arena = arena;
        _perch = arena.HomeIndex;
        _brain = BossBrain.Attach(this, def.DecisionSeconds);
        _brain.Sense = Sense;
        BuildRules(_brain.Rules);
    }

    public void StandDown()
    {
        Invincible = true;
        _brain?.Sleep();
    }

    public string BrainState => _brain?.Describe() ?? "no brain";

    /// <summary>The perch he is holding or flying to — for tests and the debug HUD.</summary>
    public Vector2 PerchPoint => _arena != null ? _arena.Perches[_perch] : GlobalPosition;

    // ── Senses ────────────────────────────────────────────────────────────────

    private void Sense(float dt)
    {
        _senses.Sample(this, dt);

        bool crowded = _senses.HasTarget && _senses.Distance <= Units.Px(PressureRangeM)
            && Mathf.Abs(_senses.ToTarget.Y) <= Units.Px(PressureLevelM);
        _pressure = crowded ? _pressure + dt : Mathf.Max(0f, _pressure - dt * 0.5f);

        // A head that rolls on past the player is wasted time: the magazine is one head and
        // only reloads once that head has gone off.
        if (HasLiveHead && _senses.HasTarget)
        {
            float distance = LiveHeadPosition.DistanceTo(_senses.TargetPosition);
            bool leaving = distance > _lastHeadDistance + 0.5f && distance > ExplosionRadius * HeadGoneRadii;
            _headGone = leaving ? _headGone + dt : 0f;
            _lastHeadDistance = distance;
        }
        else
        {
            _headGone = 0f;
            _lastHeadDistance = float.MaxValue;
        }
    }

    // ── Rules ─────────────────────────────────────────────────────────────────

    private void BuildRules(BossRuleSet rules)
    {
        VirtualPad pad = _brain.Pad;

        // legs
        rules.Add(new BossRule(BossBrain.Move, "soul flight", () => CurrentState == PKState.Soul) { Each = FlyToPerch });
        rules.Add(new BossRule(BossBrain.Move, "hold perch", () => true) { Each = HoldPerch });

        // hands, most urgent first
        rules.Add(new BossRule(BossBrain.Act, "detonate", HeadIsBesidePlayer)
        {
            OnStart = () => pad.Tap(ControlAction.Attack),
            Cooldown = 0.3f,
        });
        rules.Add(new BossRule(BossBrain.Act, "call back", () => CurrentState == PKState.Rolling && _headGone >= HeadGoneSeconds)
        {
            OnStart = () => pad.Tap(ControlAction.Attack),
            Cooldown = 0.3f,
        });
        rules.Add(new BossRule(BossBrain.Act, "soul away",
            () => SoulReady && _pressure >= PressureSeconds && _arena.Perches.Count > 1)
        {
            OnStart = () =>
            {
                // Leave for the perch farthest from the player — never the one he is on.
                Vector2 from = _senses.HasTarget ? _senses.TargetPosition : GlobalPosition;
                _perch = BossArena.FarthestFrom(_arena.Perches, from, except: BossArena.NearestTo(_arena.Perches, GlobalPosition));
                _pressure = 0f;
                _cruiseY = null;
                pad.Tap(ControlAction.Skill1);
            },
            Cooldown = 2f,
        });
        rules.Add(new BossRule(BossBrain.Act, "bowl", () => HeadReady && _senses.HasTarget && PumpStacks >= WantedPumps())
        {
            OnStart = () =>
            {
                pad.Aim = GlobalPosition + ThrowDirection() * Units.Px(4f);
                pad.Tap(ControlAction.Attack);
            },
            Cooldown = 0.4f,
        });
        rules.Add(new BossRule(BossBrain.Act, "pump up", () => PumpReady && _senses.HasTarget && PumpStacks < WantedPumps())
        {
            OnStart = () => pad.Tap(ControlAction.Skill2),
            Cooldown = PumpInnerCD + 0.05f,
        });
    }

    private bool HeadIsBesidePlayer()
    {
        if (CurrentState != PKState.Rolling || !HasLiveHead || !_senses.HasTarget) return false;
        float gap = LiveHeadPosition.DistanceTo(_senses.TargetPosition) - _senses.TargetRadius;
        return gap <= ExplosionRadius * DetonateShare;
    }

    /// <summary>The shell he wants before a throw: the farther the player, the longer he
    /// can afford to pump — and the more he will need it when they arrive.</summary>
    private int WantedPumps()
    {
        float metres = _senses.Distance / Units.PixelsPerMeter;
        int wanted = metres > PumpThreeBeyondM ? 3 : metres > PumpTwoBeyondM ? 2 : metres > PumpOneBeyondM ? 1 : 0;
        return Mathf.Min(wanted, MaxPumpStacks);
    }

    /// <summary>
    /// Where to throw. If a straight (low-arc) throw reaches the player, that; otherwise
    /// the angle that carries farthest toward them — from a perch above a slope that puts
    /// the head on the slope, and the slope does the rest. Always inside the kit's firing
    /// segments (±60° of level, either way).
    /// </summary>
    private Vector2 ThrowDirection()
    {
        Vector2 from = HeadLaunchPoint;
        Vector2 to = _senses.TargetPosition + _senses.TargetVelocity * LeadSeconds;
        float side = to.X >= from.X ? 1f : -1f;
        float x = Mathf.Abs(to.X - from.X);
        float rise = from.Y - to.Y;                      // how far ABOVE the launch point the target is
        float v = HeadLaunchSpeed;
        float g = Units.Gravity;

        float angle;
        float reach = v * v * v * v - g * (g * x * x + 2f * rise * v * v);
        if (reach >= 0f && x > 1f)
            angle = Mathf.Atan((v * v - Mathf.Sqrt(reach)) / (g * x));          // the flatter of the two arcs
        else
            angle = Mathf.Atan(v / Mathf.Sqrt(Mathf.Max(1f, v * v - 2f * g * rise)));   // farthest carry for this drop

        angle = Mathf.Clamp(angle, Mathf.DegToRad(-60f), Mathf.DegToRad(60f));
        return new Vector2(side * Mathf.Cos(angle), -Mathf.Sin(angle));
    }

    // ── Legs ──────────────────────────────────────────────────────────────────

    /// <summary>On foot: get back to the perch and stand on it, watching the player.</summary>
    private void HoldPerch()
    {
        VirtualPad pad = _brain.Pad;
        Vector2 offset = PerchPoint - GlobalPosition;
        float reach = Units.Px(PerchReachM);

        if (Mathf.Abs(offset.X) > reach)
        {
            pad.Hold(offset.X > 0f ? ControlAction.MoveRight : ControlAction.MoveLeft);
            pad.Facing = 0f;                             // walking sets his facing
        }
        else if (_senses.HasTarget)
        {
            pad.Facing = Mathf.Sign(_senses.ToTarget.X);
        }

        // A perch is a ledge above its approach: once under it, or stopped by a step, jump.
        bool perchIsAbove = offset.Y < -Units.Px(1.5f);
        bool underIt = Mathf.Abs(offset.X) < Units.Px(3f);
        bool blocked = _senses.OnWall && Mathf.Abs(offset.X) > reach;
        if (_senses.OnFloor && ((perchIsAbove && underIt) || blocked)) pad.Tap(ControlAction.Jump);
    }

    /// <summary>In Soul Form: climb clear of the higher end, cross, then come down on the
    /// perch. Soul flight has no gravity, so "up" and "down" are held buttons (Jump /
    /// MoveDown), and holding two directions flies the diagonal at the same 5 m/s.</summary>
    private void FlyToPerch()
    {
        VirtualPad pad = _brain.Pad;
        Vector2 offset = PerchPoint - GlobalPosition;
        float near = Units.Px(PerchReachM);

        // The crossing height is fixed when the flight starts, just above the higher end:
        // the whole trip has to fit in six seconds at 5 m/s, so no height is wasted.
        _cruiseY ??= Mathf.Min(GlobalPosition.Y, PerchPoint.Y) - Units.Px(SoulCruiseM);

        bool crossing = Mathf.Abs(offset.X) > Units.Px(SoulDescendWithinM);
        float wantedY = crossing ? _cruiseY.Value : PerchPoint.Y;
        float dy = wantedY - GlobalPosition.Y;

        if (Mathf.Abs(offset.X) > near)
            pad.Hold(offset.X > 0f ? ControlAction.MoveRight : ControlAction.MoveLeft);
        if (dy < -near * 0.5f) pad.Hold(ControlAction.Jump);
        else if (dy > near * 0.5f) pad.Hold(ControlAction.MoveDown);
    }

    private float? _cruiseY;           // the height the current flight crosses at
}

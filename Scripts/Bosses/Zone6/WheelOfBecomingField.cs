using System;
using System.Collections.Generic;
using Godot;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>
/// The Wheel of Becoming on the field (Docs/Mara.gdd §4–§7): Avīci's dark leader's signature,
/// run by the boss mission whatever kit the boss fields. It owns the wheel's clock
/// (<see cref="WheelOfBecoming"/>) and does what the wheel does to the fight:
///
/// • the realm in force puts its power on the boss's body, as modifiers keyed
///   <see cref="WheelTable.Source"/>: the Interval ×1.5 damage taken and a protagonist's poise;
///   Iron Throne ×0.3 and no stagger; Wrath ×0.6 on cooldowns and the attack interval; Devour's
///   heal on every hit he lands and the projectiles his front swallows;
/// • the lock: outside the Interval his HP cannot fall below <see cref="WheelTable.LockShare"/>;
///   the first time it holds, the Last Turn;
/// • Liberation: struck down in the Interval, the wheel stops on the human realm;
/// • the Tempter's Offering, once, when the fighter in play first drops below 30% HP;
/// • the realm's light, the turn and the toasts — told to whoever listens, never read back.
///
/// A kit that implements <see cref="IWheelFighter"/> (Māra's) also gets each realm's arrival
/// strike and holds. Placeholder drawing until the arena is painted (§8–§9): the colossus and
/// the wheel behind the arena, a small wheel at the top right of the screen, a wash of light.
/// </summary>
public partial class WheelOfBecomingField : Node2D
{
    /// <summary>The hub's height above the floor, and the wheel's radius (placeholder).</summary>
    public const float HubHeightM = 11f;
    public const float WheelRadiusM = 5f;
    /// <summary>Devour swallows a projectile that reaches him below this height above his origin
    /// — his jaw; a shot that comes down on his crown is not swallowed.</summary>
    public const float SwallowBelowM = 2.4f;

    private CharacterController _boss;
    private BossDef _def;
    private IWheelFighter _kit;
    private Func<string, int> _stacksOf;
    private Func<CharacterController> _player;
    private readonly List<string> _sealed = new();
    private Vector2 _throne;
    private float _left, _right, _floorY;

    private readonly List<string> _announcements = new();
    private bool _lockSaid, _intervalSaid, _offered;
    private OfferingBowl _bowl;
    private WheelRealm _loopRealm;
    private bool _realmLoop, _intervalLoop;
    private int _segments;              // how far the wheel has turned, in segments (drawing only)
    private float _angle;               // the drawn angle, easing toward the turned one
    private Node2D _light;
    private WheelIndicator _indicator;
    private float _age;

    public WheelOfBecoming Wheel { get; private set; }
    public CharacterController Boss => _boss;
    public WheelRealm Realm => Wheel?.Current ?? WheelTable.OpensOn;
    /// <summary>He fell in the Interval and the wheel stopped: the run is won.</summary>
    public bool Liberated { get; private set; }
    /// <summary>How many times Devour healed him for a hit he landed, and how many shots he
    /// swallowed (checks read them).</summary>
    public int DevourHeals { get; private set; }
    public int Swallowed { get; private set; }
    /// <summary>The bowl, while it is offered.</summary>
    public OfferingBowl Bowl => _bowl != null && IsInstanceValid(_bowl) ? _bowl : null;
    public bool Offered => _offered;

    /// <summary>The Offering was taken: this poison's stack must go into the fighter's party.</summary>
    public event Action<string> OfferingTaken;

    /// <summary>
    /// Starts the wheel on a staged boss. Call after the boss's row is on it (its HP is the one
    /// the lock is a share of). <paramref name="stacksOf"/> is read at each landing;
    /// <paramref name="player"/> is the fighter in play now (null between bodies).
    /// </summary>
    public void Begin(CharacterController boss, BossDef def, IEnumerable<string> sealedPoisons, Func<string, int> stacksOf,
        Func<CharacterController> player, float left, float right, float floorY, Vector2 throne)
    {
        _boss = boss;
        _def = def;
        _kit = boss as IWheelFighter;
        _stacksOf = stacksOf ?? (_ => 0);
        _player = player;
        _sealed.Clear();
        if (sealedPoisons != null) _sealed.AddRange(sealedPoisons);
        _left = left;
        _right = right;
        _floorY = floorY;
        _throne = throne;
        Position = new Vector2((left + right) * 0.5f, floorY - Units.Px(HubHeightM));
        ZIndex = 0;

        Wheel = new WheelOfBecoming(_sealed, _stacksOf);
        Wheel.Landed += OnLanded;
        Wheel.TellStarted += OnTell;
        Wheel.Passed += OnPassed;
        Wheel.LastTurnBegan += OnLastTurn;
        boss.DamageDealt += OnBossDealt;
        boss.Died += OnBossDied;

        _light = new WheelRealmLight(this) { Name = "RealmLight", ZIndex = 80, ZAsRelative = false };
        AddChild(_light);
        var layer = new CanvasLayer { Name = "WheelIndicator", Layer = 2 };
        AddChild(layer);
        _indicator = new WheelIndicator(this) { Name = "Wheel" };
        layer.AddChild(_indicator);

        _kit?.WheelBegins(_sealed);
        _segments = (int)Wheel.Current;
        _angle = _segments;
        Announce("无间 · Avīci — the wheel turns. Only the human realm opens.");
        ApplyRealm(Wheel.Current, arrival: false);
        QueueRedraw();
    }

    /// <summary>One step of the fight's clock (the boss mission's tick).</summary>
    public void Advance(float dt)
    {
        if (Wheel == null || Liberated || _boss == null || !IsInstanceValid(_boss) || _boss.IsDefeated) return;
        _age += dt;
        Wheel.Tick(dt, _boss.HpRatio, _kit?.HandOffWheel ?? false);

        // The lock: at the floor outside the Interval, his last sliver waits for the human realm.
        float floor = _boss.HpFloor;
        if (floor > 0f && _boss.CurrentHP <= floor + 0.01f && !Wheel.InInterval)
        {
            if (!_lockSaid)
            {
                _lockSaid = true;
                Announce("Only in the human realm.");
            }
            Wheel.BeginLastTurn();
        }
        TickOffering();
    }

    /// <summary>Lines for the HUD's toast, oldest first, joined; null when there is none.</summary>
    public string ConsumeAnnouncement()
    {
        if (_announcements.Count == 0) return null;
        string said = string.Join("   ·   ", _announcements);
        _announcements.Clear();
        return said;
    }

    /// <summary>The fight ended with him standing (a debug skip, the fatal timer): stop.</summary>
    public void Halt()
    {
        StopLoops();
        if (_bowl != null && IsInstanceValid(_bowl)) _bowl.QueueFree();
        _offered = true;
    }

    public override void _Process(double delta)
    {
        float target = _segments;
        float rate = Wheel != null && Wheel.InLastTurn ? 6f : 3f;
        _angle = Mathf.MoveToward(_angle, target, rate * (float)delta);
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        StopLoops();
        if (_boss != null && IsInstanceValid(_boss))
        {
            _boss.DamageDealt -= OnBossDealt;
            _boss.Died -= OnBossDied;
        }
    }

    // ── The wheel's moments ─────────────────────────────────────────────────────────

    private void OnTell(WheelRealm incoming)
    {
        CueBus.EmitAt(WheelTable.CueOwner, "wheel.tell", GlobalPosition, this, CombatSide.Foe,
            variant: (int)incoming, magnitude: WheelTable.TellSeconds, tag: WheelTable.Of(incoming).Id);
    }

    private void OnLanded(WheelRealm realm)
    {
        TurnTo(realm);
        WheelRealmDef def = WheelTable.Of(realm);
        CueBus.EmitAt(WheelTable.CueOwner, "wheel.turn", GlobalPosition, this, CombatSide.Foe,
            variant: (int)realm, tag: def.Id);
        Announce(def.IsInterval ? $"{def.Title} — the Interval" : $"{def.Title} — {def.Power}");
        if (def.IsInterval && !_intervalSaid)
        {
            _intervalSaid = true;
            Announce("人身难得 — a human birth is hard to come by.");
        }
        ApplyRealm(realm, arrival: true);
    }

    private void OnPassed(WheelRealm realm)
    {
        TurnTo(realm);
        CueBus.EmitAt(WheelTable.CueOwner, "wheel.pass", GlobalPosition, this, CombatSide.Foe,
            variant: (int)realm, tag: WheelTable.Of(realm).Id);
        _kit?.RealmPassed(realm);
    }

    private void OnLastTurn()
    {
        ClearPowers();
        SetLock(true);
        StopLoops();
        CueBus.EmitAt(WheelTable.CueOwner, "last_turn", GlobalPosition, this, CombatSide.Foe,
            variant: Wheel.Spin.Count, magnitude: Wheel.Spin.Count * WheelTable.LastTurnStepSeconds);
        _kit?.LastTurnBegan();
    }

    private void OnBossDied()
    {
        if (Liberated) return;
        // The lock means he can only fall in the Interval; however he fell, the run is won and
        // the wheel stops on the human realm.
        Liberated = true;
        Wheel?.Stop();
        TurnTo(WheelRealm.Humans);
        StopLoops();
        if (_bowl != null && IsInstanceValid(_bowl)) _bowl.QueueFree();
        _offered = true;
        CueBus.EmitAt(WheelTable.CueOwner, "liberation", GlobalPosition, this, CombatSide.Foe);
        Announce("The wheel stops. You step off it.");
        _kit?.Liberated();
    }

    /// <summary>Devour: every hit he lands heals him (the body reports what it dealt — a
    /// gameplay hook, never the cue bus).</summary>
    private void OnBossDealt(float dealt)
    {
        if (dealt <= 0f || Liberated || Wheel == null || Wheel.InLastTurn || Wheel.Current != WheelRealm.HungryGhosts) return;
        if (_boss == null || !IsInstanceValid(_boss) || _boss.IsDefeated) return;
        DevourHeals++;
        _boss.Heal(_boss.MaxHP * WheelTable.DevourHealPerHit);
    }

    /// <summary>Devour: a projectile, field or tick that reaches his front below his jaw is
    /// swallowed — it heals him instead of hurting him.</summary>
    private bool Swallows(HitInfo hit, Vector2 from)
    {
        if (!hit.Ranged || hit.Damage <= 0f || _boss == null) return false;
        Vector2 to = from - _boss.GlobalPosition;
        if (to.X * _boss.FacingDirection.X <= 0f) return false;           // behind him
        if (to.Y < -Units.Px(SwallowBelowM)) return false;                 // down on his crown
        Swallowed++;
        _boss.Heal(_boss.MaxHP * WheelTable.SwallowHealShare);
        CueBus.EmitAt(WheelTable.CueOwner, "devour.swallow", from, _boss, CombatSide.Foe, magnitude: hit.Damage);
        return true;
    }

    // ── What the realm does to his body ─────────────────────────────────────────────

    private void ApplyRealm(WheelRealm realm, bool arrival)
    {
        ClearPowers();
        bool interval = realm == WheelRealm.Humans;
        SetLock(!interval);
        switch (realm)
        {
            case WheelRealm.Humans:
                // His guard opens: more damage, and a protagonist's poise — whatever his row's is.
                _boss.SetDefenseSource(WheelTable.Source, WheelTable.DefenseFor(WheelTable.IntervalDamageTaken));
                if (_def != null && _def.KnockbackTaken > 0.001f) _boss.SetKnockbackTakenScale(WheelTable.Source, 1f / _def.KnockbackTaken);
                if (_def != null && _def.StunTaken > 0.001f) _boss.SetStunTakenScale(WheelTable.Source, 1f / _def.StunTaken);
                break;
            case WheelRealm.Gods:
                _boss.SetDefenseSource(WheelTable.Source, WheelTable.DefenseFor(WheelTable.ThroneDamageTaken));
                _boss.SetKnockbackTakenScale(WheelTable.Source, 0f);
                _boss.SetStunTakenScale(WheelTable.Source, 0f);
                break;
            case WheelRealm.Asuras:
                _boss.SetCooldownScale(WheelTable.Source, WheelTable.WrathScale);
                _boss.SetAttackIntervalScale(WheelTable.Source, WheelTable.WrathScale);
                break;
            case WheelRealm.HungryGhosts:
                _boss.SetHitFilter(WheelTable.Source, Swallows);
                break;
        }
        StartLoops(realm);
        _kit?.RealmLanded(realm, arrival);
    }

    private void ClearPowers()
    {
        if (_boss == null || !IsInstanceValid(_boss)) return;
        _boss.ClearDefenseSource(WheelTable.Source);
        _boss.SetKnockbackTakenScale(WheelTable.Source, 1f);
        _boss.SetStunTakenScale(WheelTable.Source, 1f);
        _boss.SetCooldownScale(WheelTable.Source, 1f);
        _boss.SetAttackIntervalScale(WheelTable.Source, 1f);
        _boss.ClearHitFilter(WheelTable.Source);
    }

    private void SetLock(bool held)
    {
        if (_boss == null || !IsInstanceValid(_boss)) return;
        if (held) _boss.SetHpFloor(WheelTable.Source, _boss.MaxHP * WheelTable.LockShare);
        else _boss.ClearHpFloor(WheelTable.Source);
    }

    private void StartLoops(WheelRealm realm)
    {
        StopLoops();
        WheelRealmDef def = WheelTable.Of(realm);
        if (def.IsInterval)
        {
            CueBus.StartAt(WheelTable.CueOwner, "interval", this, GlobalPosition, CombatSide.Foe, tag: def.Id);
            _intervalLoop = true;
        }
        else
        {
            CueBus.StartAt(WheelTable.CueOwner, "realm", this, GlobalPosition, CombatSide.Foe, variant: (int)realm, tag: def.Id);
            _realmLoop = true;
            _loopRealm = realm;
        }
    }

    private void StopLoops()
    {
        if (_realmLoop) CueBus.StopAt(WheelTable.CueOwner, "realm", this, WheelTable.Of(_loopRealm).Id);
        if (_intervalLoop) CueBus.StopAt(WheelTable.CueOwner, "interval", this, WheelTable.Of(WheelRealm.Humans).Id);
        _realmLoop = _intervalLoop = false;
    }

    // ── The Tempter's Offering (§7.5) ───────────────────────────────────────────────

    private void TickOffering()
    {
        if (_offered || Liberated) return;
        CharacterController player = _player?.Invoke();
        if (player == null || !IsInstanceValid(player) || player.IsDefeated || player.HpRatio >= WheelTable.OfferingBelowHp) return;
        _offered = true;
        string poison = OfferingPoison();
        if (poison == null) return;   // every poison at its cap: he has nothing left to sell
        Zone6PoisonDef def = Zone6Table.Poison(poison);
        _bowl = new OfferingBowl { Name = "OfferingBowl" };
        AddChild(_bowl);
        _bowl.GlobalPosition = _throne;
        _bowl.Init(poison, $"供 — healed to full; {def.Name} enters you.", WheelTable.OfferingSeconds, TakeOffering);
        CueBus.EmitAt(WheelTable.CueOwner, "offering", _throne, _bowl, CombatSide.Foe, tag: poison);
    }

    /// <summary>The poison the bowl puts in you now.</summary>
    public string OfferingPoison() => OfferingPoisonFor(_stacksOf);

    /// <summary>The poison the bowl puts in a party with these stacks: the one carried most,
    /// gate order breaking ties; one at its cap is passed over; none carried — Greed's (first in
    /// gate order); every one at its cap — null, and no bowl.</summary>
    public static string OfferingPoisonFor(Func<string, int> stacksOf)
    {
        string best = null;
        int most = -1;
        foreach (Zone6PoisonDef poison in Zone6Table.Poisons)
        {
            int stacks = stacksOf?.Invoke(poison.Id) ?? 0;
            if (stacks >= Zone6Table.SealStackCap || stacks <= most) continue;
            best = poison.Id;
            most = stacks;
        }
        return best;
    }

    private void TakeOffering(CharacterController taker)
    {
        if (taker == null || taker.IsDefeated) return;
        taker.Heal(taker.MaxHP - taker.CurrentHP);
        string poison = _bowl?.PoisonId;
        _bowl = null;
        if (!string.IsNullOrEmpty(poison)) OfferingTaken?.Invoke(poison);
    }

    // ── Drawing (placeholder, Mara.gdd §8–§9) ──────────────────────────────────────

    private void TurnTo(WheelRealm realm)
    {
        int from = Mathf.PosMod(_segments, WheelTable.Count);
        int forward = Mathf.PosMod((int)realm - from, WheelTable.Count);
        _segments += forward == 0 && Wheel != null && Wheel.InLastTurn ? WheelTable.Count : forward;
    }

    private void Announce(string line)
    {
        if (!string.IsNullOrEmpty(line)) _announcements.Add(line);
    }

    /// <summary>The realm whose light the arena shows now.</summary>
    internal WheelRealmDef LightRealm => Liberated || Wheel == null ? WheelTable.Of(WheelRealm.Humans)
        : Wheel.InLastTurn && Wheel.SpinIndex < Wheel.Spin.Count ? WheelTable.Of(Wheel.Spin[Wheel.SpinIndex])
        : WheelTable.Of(Wheel.Current);

    internal float Left => _left;
    internal float Right => _right;
    internal float FloorY => _floorY;
    internal bool IsSealed(WheelRealm realm) => Wheel != null && Wheel.IsSealed(realm);

    /// <summary>The wheel's angle in segments: the realm under his fangs is at the top.</summary>
    internal float AngleSegments => _angle;

    /// <summary>The colossus behind the arena, biting the top of the wheel and gripping its rim
    /// — black-blue, three eyes, a crown of five skulls — and the wheel's six segments with
    /// the poisons' sigils on the rim. NOTE(art): placeholder for the painted backdrop.</summary>
    public override void _Draw()
    {
        float r = Units.Px(WheelRadiusM);
        var ink = new Color(0.05f, 0.06f, 0.12f, 0.55f);
        var gold = new Color(0.95f, 0.78f, 0.30f, 0.75f);
        var skin = new Color(0.10f, 0.14f, 0.32f, 0.55f);

        // The colossus: shoulders and head above the wheel.
        DrawCircle(new Vector2(0f, -r * 1.25f), r * 0.55f, skin);
        DrawColoredPolygon(new[] { new Vector2(-r * 1.9f, r * 0.2f), new Vector2(-r * 1.2f, -r * 0.9f), new Vector2(r * 1.2f, -r * 0.9f), new Vector2(r * 1.9f, r * 0.2f) }, skin);
        for (int eye = -1; eye <= 1; eye++)
        {
            Vector2 at = eye == 0 ? new Vector2(0f, -r * 1.55f) : new Vector2(eye * r * 0.2f, -r * 1.32f);
            DrawCircle(at, r * 0.06f, new Color(1f, 0.72f, 0.20f, Liberated ? 0.35f : 0.9f));
        }
        for (int skull = -2; skull <= 2; skull++)
        {
            Vector2 at = new(skull * r * 0.17f, -r * 1.78f + Mathf.Abs(skull) * r * 0.04f);
            DrawCircle(at, r * 0.07f, new Color(0.92f, 0.9f, 0.82f, 0.7f));
        }

        // The wheel, turned so the realm in force sits under his fangs.
        float baseAngle = -Mathf.Pi * 0.5f - _angle * Mathf.Tau / WheelTable.Count;
        DrawCircle(Vector2.Zero, r, ink);
        for (int i = 0; i < WheelTable.Count; i++)
        {
            WheelRealmDef def = WheelTable.Realms[i];
            float a0 = baseAngle + (i - 0.5f) * Mathf.Tau / WheelTable.Count;
            float a1 = a0 + Mathf.Tau / WheelTable.Count;
            bool inForce = !Liberated && Wheel != null && !Wheel.InLastTurn && Wheel.Current == def.Realm;
            Color fill = inForce ? new Color(def.LightR, def.LightG, def.LightB, 0.55f)
                : IsSealed(def.Realm) ? new Color(0.08f, 0.08f, 0.10f, 0.6f)
                : new Color(def.LightR * 0.5f, def.LightG * 0.5f, def.LightB * 0.5f, 0.35f);
            var wedge = new List<Vector2> { Vector2.Zero };
            for (int s = 0; s <= 8; s++)
            {
                float a = Mathf.Lerp(a0, a1, s / 8f);
                wedge.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.92f);
            }
            DrawColoredPolygon(wedge.ToArray(), fill);
            DrawLine(Vector2.Zero, new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, gold, 2f);
            // The sigil on the rim: its poison's colour; dark under a closed gold lotus when sealed.
            if (def.IsInterval) continue;
            float mid = (a0 + a1) * 0.5f;
            Vector2 sigil = new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * r * 0.97f;
            if (IsSealed(def.Realm))
            {
                DrawCircle(sigil, r * 0.1f, new Color(0.15f, 0.15f, 0.15f, 0.9f));
                DrawArc(sigil, r * 0.1f, Mathf.Pi, Mathf.Tau, 8, gold, 3f);
            }
            else
            {
                DrawCircle(sigil, r * 0.1f, new Color(def.SigilR, def.SigilG, def.SigilB, 0.95f));
                if (Wheel != null && Wheel.Incoming == def.Realm)
                    DrawArc(sigil, r * (0.13f + 0.03f * Mathf.Sin(_age * 18f)), 0f, Mathf.Tau, 16, new Color(1f, 0.9f, 0.5f, 0.95f), 3f);
            }
        }
        DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 48, gold, 4f);
        // The hub: the three animals — rooster, snake, pig.
        DrawCircle(Vector2.Zero, r * 0.2f, new Color(0.06f, 0.06f, 0.08f, 0.85f));
        DrawCircle(new Vector2(-r * 0.08f, -r * 0.05f), r * 0.05f, new Color(0.88f, 0.16f, 0.12f, 0.9f));
        DrawCircle(new Vector2(r * 0.08f, -r * 0.05f), r * 0.05f, new Color(0.2f, 0.4f, 0.92f, 0.9f));
        DrawCircle(new Vector2(0f, r * 0.08f), r * 0.05f, new Color(0.95f, 0.94f, 0.9f, 0.9f));

        // Fangs biting the top; claws on the rim (open once he is struck down).
        var fang = new Color(0.95f, 0.93f, 0.85f, 0.8f);
        DrawColoredPolygon(new[] { new Vector2(-r * 0.16f, -r * 1.02f), new Vector2(-r * 0.08f, -r * 1.02f), new Vector2(-r * 0.12f, -r * 0.86f) }, fang);
        DrawColoredPolygon(new[] { new Vector2(r * 0.08f, -r * 1.02f), new Vector2(r * 0.16f, -r * 1.02f), new Vector2(r * 0.12f, -r * 0.86f) }, fang);
        float open = Liberated ? r * 0.35f : 0f;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2 palm = new(side * (r * 1.02f + open), -r * 0.1f);
            DrawCircle(palm, r * 0.16f, skin);
            for (int k = -1; k <= 1; k++)
                DrawLine(palm, palm + new Vector2(-side * r * 0.18f, k * r * 0.12f), fang, 3f);
        }

        // Liberation: a white moon in the upper corner, outside the wheel.
        if (Liberated)
            DrawCircle(new Vector2(r * 2.6f, -r * 1.7f), r * 0.28f, new Color(0.97f, 0.97f, 1f, 0.95f));
    }
}

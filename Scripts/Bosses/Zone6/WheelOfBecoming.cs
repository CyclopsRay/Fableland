using System;
using System.Collections.Generic;
using Fableland.Data;

namespace Fableland.Bosses;

/// <summary>
/// The Wheel of Becoming's clock (Docs/Mara.gdd §4, §7): which realm is in force, how long it
/// rests, where it turns next, and the Last Turn. Pure C# — no nodes, no time source of its
/// own: whoever owns it calls <see cref="Tick"/> with the fight's seconds, so the order, the
/// rests and the spin can be checked headless (Zone6Check).
///
/// • The fight opens on <see cref="WheelTable.OpensOn"/> (Hell), or the first unsealed realm
///   after it.
/// • A realm rests from its landing to the next landing: 8 s (6 s below half his HP), +1 s for
///   each stack of its poison in you; the human realm — the Interval — 6 s, always. The last
///   <see cref="WheelTable.TellSeconds"/> of a rest is the tell.
/// • It then turns to the next UNSEALED realm in wheel order. With all five sealed there is
///   nowhere to go: it stays on the human realm.
/// • A turn waits while the colossus has a hand off the wheel, at most 1.5 s.
/// • The Last Turn (once): every unsealed realm passes, 0.9 s each, starting after the one in
///   force and wrapping round to it; then the Interval opens.
/// </summary>
public sealed class WheelOfBecoming
{
    private readonly HashSet<string> _sealed;
    private readonly Func<string, int> _stacks;
    private readonly List<WheelRealm> _spin = new();

    private float _elapsed;      // seconds since the realm in force landed
    private float _rest;         // its rest, fixed at its landing
    private int _spinIndex;
    private float _stepLeft;

    /// <summary>A realm landed (a turn, or the end of the Last Turn). Its power arrives.</summary>
    public event Action<WheelRealm> Landed;
    /// <summary>The tell began: this realm lands in <see cref="WheelTable.TellSeconds"/>.</summary>
    public event Action<WheelRealm> TellStarted;
    /// <summary>The Last Turn passes a realm: its arrival strike, and nothing that holds.</summary>
    public event Action<WheelRealm> Passed;
    public event Action LastTurnBegan;

    /// <param name="sealedPoisons">The poisons whose power is sealed in Māra (their realms never come round).</param>
    /// <param name="stacksOf">Stacks of a poison in the player's party, asked at each landing
    /// (an Offering accepted mid-fight counts from the next one).</param>
    public WheelOfBecoming(IEnumerable<string> sealedPoisons, Func<string, int> stacksOf)
    {
        _sealed = new HashSet<string>(sealedPoisons ?? Array.Empty<string>(), StringComparer.Ordinal);
        _stacks = stacksOf ?? (_ => 0);
        Current = IsSealed(WheelTable.OpensOn) ? Next(WheelTable.OpensOn) : WheelTable.OpensOn;
        _rest = RestSeconds(Current, 1f);
    }

    public WheelRealm Current { get; private set; }
    /// <summary>The realm the tell names, while it runs.</summary>
    public WheelRealm? Incoming { get; private set; }
    public bool Telling => Incoming.HasValue;
    public bool InLastTurn { get; private set; }
    /// <summary>The Last Turn has happened (it comes once a fight).</summary>
    public bool LastTurnDone { get; private set; }
    /// <summary>The wheel stopped on the human realm: he was struck down in the Interval.</summary>
    public bool Stopped { get; private set; }
    /// <summary>The human realm is in force: his powers lapse and his last sliver can be taken.</summary>
    public bool InInterval => Current == WheelRealm.Humans && !InLastTurn;
    /// <summary>All five realms are sealed: the wheel never leaves the human realm.</summary>
    public bool Still => Next(Current) == Current;

    public float RestTotal => _rest;
    public float Elapsed => _elapsed;
    /// <summary>Seconds until the next landing (∞ when the wheel cannot turn).</summary>
    public float UntilTurn => InLastTurn ? _stepLeft : Still || Stopped ? float.PositiveInfinity : Math.Max(0f, _rest - _elapsed);
    /// <summary>The realms the Last Turn passes, in order (empty until it begins).</summary>
    public IReadOnlyList<WheelRealm> Spin => _spin;
    public int SpinIndex => _spinIndex;

    public bool IsSealed(WheelRealm realm)
    {
        WheelRealmDef def = WheelTable.Of(realm);
        return !def.IsInterval && _sealed.Contains(def.PoisonId);
    }

    /// <summary>The next unsealed realm after <paramref name="from"/> in wheel order — itself
    /// when nothing else is open.</summary>
    public WheelRealm Next(WheelRealm from)
    {
        for (int step = 1; step <= WheelTable.Count; step++)
        {
            var realm = (WheelRealm)(((int)from + step) % WheelTable.Count);
            if (!IsSealed(realm)) return realm;
        }
        return from;
    }

    /// <summary>A realm's rest at a given share of his HP, with the stacks you carry now.</summary>
    public float RestSeconds(WheelRealm realm, float bossHpShare)
    {
        WheelRealmDef def = WheelTable.Of(realm);
        if (def.IsInterval) return WheelTable.IntervalSeconds;
        float rest = bossHpShare <= WheelTable.RestsShortenBelowHp ? WheelTable.RestBelowHalfSeconds : WheelTable.RestSeconds;
        return rest + WheelTable.SecondsPerPoisonStack * Math.Max(0, _stacks(def.PoisonId));
    }

    /// <summary>One cycle — every unsealed realm's rest and the Interval — at a share of his HP
    /// (Mara.gdd §4.3). Zero when the wheel stands still.</summary>
    public float CycleSeconds(float bossHpShare)
    {
        if (Next(WheelRealm.Humans) == WheelRealm.Humans) return 0f;
        float total = 0f;
        foreach (WheelRealmDef def in WheelTable.Realms)
            if (!IsSealed(def.Realm)) total += RestSeconds(def.Realm, bossHpShare);
        return total;
    }

    /// <summary>Advances the wheel by <paramref name="dt"/> fight-seconds. Stun, knock-back and
    /// hit-stop on his body never reach here: the wheel is the arena's (§4.4).</summary>
    public void Tick(float dt, float bossHpShare, bool handOffWheel)
    {
        if (Stopped || dt <= 0f) return;
        if (InLastTurn)
        {
            _stepLeft -= dt;
            while (InLastTurn && _stepLeft <= 0f)
            {
                _spinIndex++;
                if (_spinIndex < _spin.Count)
                {
                    _stepLeft += WheelTable.LastTurnStepSeconds;
                    Passed?.Invoke(_spin[_spinIndex]);
                }
                else
                {
                    InLastTurn = false;
                    LastTurnDone = true;
                    Land(WheelRealm.Humans, bossHpShare);
                }
            }
            return;
        }
        if (Still) return;

        _elapsed += dt;
        if (!Telling && _elapsed >= _rest - WheelTable.TellSeconds)
        {
            Incoming = Next(Current);
            TellStarted?.Invoke(Incoming.Value);
        }
        if (_elapsed < _rest) return;
        // The colossus has a claw off the rim: the wheel waits for it, but not for ever.
        if (handOffWheel && _elapsed < _rest + WheelTable.HandWaitMaxSeconds) return;
        Land(Incoming ?? Next(Current), bossHpShare);
    }

    /// <summary>His HP has reached the lock outside the Interval: the wheel spins to the human
    /// realm through every unsealed realm. Once a fight; not while a turn to the human realm is
    /// already under way. Returns whether it began.</summary>
    public bool BeginLastTurn()
    {
        if (Stopped || InLastTurn || LastTurnDone || InInterval) return false;
        if (Incoming == WheelRealm.Humans) return false;   // the Interval is a second away
        _spin.Clear();
        for (int step = 1; step <= WheelTable.Count; step++)
        {
            var realm = (WheelRealm)(((int)Current + step) % WheelTable.Count);
            if (realm != WheelRealm.Humans && !IsSealed(realm)) _spin.Add(realm);
        }
        Incoming = null;
        LastTurnBegan?.Invoke();
        if (_spin.Count == 0)
        {
            LastTurnDone = true;
            Land(WheelRealm.Humans, 1f);
            return true;
        }
        InLastTurn = true;
        _spinIndex = 0;
        _stepLeft = WheelTable.LastTurnStepSeconds;
        Passed?.Invoke(_spin[0]);
        return true;
    }

    /// <summary>He fell in the Interval: the wheel stops on the human realm for good.</summary>
    public void Stop()
    {
        Stopped = true;
        InLastTurn = false;
        Incoming = null;
        Current = WheelRealm.Humans;
    }

    private void Land(WheelRealm realm, float bossHpShare)
    {
        Current = realm;
        Incoming = null;
        _elapsed = 0f;
        _rest = RestSeconds(realm, bossHpShare);
        Landed?.Invoke(realm);
    }

    // ── Checks ──────────────────────────────────────────────────────────────────────

    /// <summary>The wheel's own rules, ticked through (run by the zone-6 check).</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        const float Step = 1f / 64f;   // a power of two: rests land on whole ticks

        // Nothing sealed, nothing carried: Hell, then the animals, then the Interval at 16 s.
        var wheel = new WheelOfBecoming(null, null);
        var landed = new List<(WheelRealm Realm, float At)>();
        float t = 0f;
        wheel.Landed += realm => landed.Add((realm, t));
        Check(wheel.Current == WheelRealm.Hell, $"the fight must open on Hell, not {wheel.Current}");
        for (; t < 60f; t += Step) wheel.Tick(Step, 1f, false);
        string order = string.Join(" ", landed.ConvertAll(l => $"{l.Realm}@{l.At:0.##}"));
        Check(landed.Count >= 5 && landed[0].Realm == WheelRealm.Animals && Math.Abs(landed[0].At - 8f) < 0.05f
              && landed[1].Realm == WheelRealm.Humans && Math.Abs(landed[1].At - 16f) < 0.05f
              && landed[2].Realm == WheelRealm.Gods && Math.Abs(landed[2].At - 22f) < 0.05f
              && landed[3].Realm == WheelRealm.Asuras && landed[4].Realm == WheelRealm.HungryGhosts,
            $"from Hell the wheel must turn to the animals at 8 s, the humans at 16 s, then the gods at 22 s, the asuras, the hungry ghosts ({order})");

        // Sealed realms are skipped; a stack holds its realm a second longer.
        var sealedWheel = new WheelOfBecoming(new[] { "delusion", "pride" }, id => id == "doubt" ? 2 : 0);
        var seen = new List<WheelRealm>();
        sealedWheel.Landed += realm => seen.Add(realm);
        Check(Math.Abs(sealedWheel.RestTotal - 10f) < 1e-4f, $"two stacks of Doubt must hold Hell 10 s, not {sealedWheel.RestTotal}");
        for (float s = 0f; s < 40f; s += Step) sealedWheel.Tick(Step, 1f, false);
        Check(seen.Count >= 4 && seen[0] == WheelRealm.Humans && seen[1] == WheelRealm.Asuras && seen[2] == WheelRealm.HungryGhosts && seen[3] == WheelRealm.Hell
              && !seen.Contains(WheelRealm.Animals) && !seen.Contains(WheelRealm.Gods),
            $"with the animals and the gods sealed the wheel must go Hell → Humans → Asuras → Hungry ghosts → Hell ({string.Join(" ", seen)})");

        // Five sealed: it stands on the human realm.
        var still = new WheelOfBecoming(new[] { "greed", "anger", "delusion", "pride", "doubt" }, null);
        int turns = 0;
        still.Landed += _ => turns++;
        for (float s = 0f; s < 30f; s += Step) still.Tick(Step, 1f, false);
        Check(still.Current == WheelRealm.Humans && still.Still && turns == 0 && still.InInterval,
            $"with all five sealed the wheel must stand on the human realm ({still.Current}, {turns} turn(s))");

        // The hand holds a turn, for 1.5 s at most.
        var held = new WheelOfBecoming(null, null);
        float clock = 0f;
        while (clock < 12f && held.Current == WheelRealm.Hell)
        {
            held.Tick(Step, 1f, true);
            clock += Step;
        }
        Check(held.Current == WheelRealm.Animals && Math.Abs(clock - 9.5f) < 0.05f,
            $"a hand off the wheel must hold the turn 1.5 s at most (Hell turned at {clock:0.00} s; 8 s + 1.5 s)");

        // The Last Turn: every unsealed realm, 0.9 s each, then the Interval.
        var spin = new WheelOfBecoming(new[] { "anger" }, null);
        var passed = new List<WheelRealm>();
        WheelRealm? afterSpin = null;
        spin.Passed += realm => passed.Add(realm);
        spin.Landed += realm => afterSpin ??= realm;
        Check(spin.BeginLastTurn() && spin.InLastTurn && !spin.InInterval, "the Last Turn did not begin from Hell");
        float spun = 0f;
        for (; spun < 10f && spin.InLastTurn; spun += Step) spin.Tick(Step, 0.05f, false);
        Check(string.Join(" ", passed) == "Animals Gods HungryGhosts Hell" && afterSpin == WheelRealm.Humans && spin.InInterval,
            $"from Hell with the asuras sealed the Last Turn must pass Animals, Gods, Hungry ghosts, Hell and land on the humans ({string.Join(" ", passed)} → {afterSpin})");
        Check(Math.Abs(spun - 4 * WheelTable.LastTurnStepSeconds) < 0.05f, $"four unsealed realms must spin for {4 * WheelTable.LastTurnStepSeconds:0.0} s, not {spun:0.00} s");
        Check(!spin.BeginLastTurn(), "the Last Turn came twice");
        return failures;
    }
}

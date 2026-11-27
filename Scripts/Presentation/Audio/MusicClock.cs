using System;

namespace Fableland.Audio;

/// <summary>
/// The clock we own (<c>Docs/Audio.gdd</c> §7.3). The engine cannot tell us where the music
/// is — a synchronized stream reports the largest of its stems' positions, which wraps at a
/// different moment for every stem (measured), and the interactive stream keeps no time for
/// runtime clips at all (R1 §5.7–5.8) — so the beat is counted here, from the moment the
/// stems were started, in real seconds.
///
/// The beat length is a whole number of samples, the same number the stems were bounced
/// with, so the clock and the audio agree for as long as the system clock and the sound card
/// do; it is re-anchored every time music starts. Pure arithmetic on a time the caller
/// supplies: a check drives it with a made-up clock.
/// </summary>
public sealed class MusicClock
{
    /// <summary>The moment beat 0 is HEARD, in the caller's seconds.</summary>
    public double T0 { get; private set; }
    public double SecondsPerBeat { get; private set; } = 0.5;
    public int BeatsPerBar { get; private set; } = 4;
    public bool Running { get; private set; }
    /// <summary>The last beat announced (−1 before the first).</summary>
    public int Beat { get; private set; } = -1;
    public int Bar => Beat < 0 ? -1 : Beat / BeatsPerBar;

    /// <summary>Raised once per beat, in order, with the beat's index since the music began.</summary>
    public event Action<int> BeatHit;
    /// <summary>Raised on the first beat of each bar, with the bar's index.</summary>
    public event Action<int> BarHit;

    public void Start(double t0, double secondsPerBeat, int beatsPerBar)
    {
        T0 = t0;
        SecondsPerBeat = Math.Max(0.05, secondsPerBeat);
        BeatsPerBar = Math.Max(1, beatsPerBar);
        Beat = -1;
        Running = true;
    }

    public void Stop() => Running = false;

    /// <summary>Beats since the music began, with the fraction (negative before it starts).</summary>
    public double Position(double now) => (now - T0) / SecondsPerBeat;

    public double TimeOfBeat(int beat) => T0 + beat * SecondsPerBeat;

    /// <summary>Announce every beat that has been reached since the last call. A long frame
    /// announces several — never more than <paramref name="most"/>, after which the clock
    /// skips ahead silently rather than machine-gun the listeners.</summary>
    public int Advance(double now, int most = 4)
    {
        if (!Running) return 0;
        int reached = (int)Math.Floor(Position(now) + 1e-9);
        if (reached <= Beat) return 0;
        if (reached - Beat > most) Beat = reached - most;
        int announced = 0;
        while (Beat < reached)
        {
            Beat++;
            announced++;
            BeatHit?.Invoke(Beat);
            if (Beat % BeatsPerBar == 0) BarHit?.Invoke(Beat / BeatsPerBar);
        }
        return announced;
    }

    /// <summary>The next multiple of 1/<paramref name="division"/> of a beat strictly after
    /// <paramref name="now"/> (division 1 = a beat; a negative division = that many bars).</summary>
    public double NextBoundary(double now, int division)
    {
        double unit = division > 0 ? SecondsPerBeat / division : SecondsPerBeat * BeatsPerBar * -division;
        double n = Math.Floor((now - T0) / unit + 1e-9) + 1.0;
        return T0 + n * unit;
    }

    /// <summary>How long a stinger may be held back for the grid, at most.</summary>
    public const double GridWait = 0.25, BeatWait = 0.6, BarWait = 0.7;

    /// <summary>
    /// When a stinger asked for at <paramref name="now"/> should sound. It lands on the
    /// music's grid — a bar, a beat, or the coarsest subdivision of the beat that is near
    /// enough — so the worst case is a wait of <see cref="GridWait"/> … <see cref="BarWait"/>,
    /// never a stinger that arrives a second after the moment it marks.
    /// </summary>
    public double Quantized(double now, Quantize mode)
    {
        if (!Running || mode == Quantize.None || now < T0) return now;
        if (mode == Quantize.Bar)
        {
            double bar = NextBoundary(now, -1);
            if (bar - now <= BarWait) return bar;
            mode = Quantize.Beat;
        }
        if (mode == Quantize.Beat)
        {
            double beat = NextBoundary(now, 1);
            if (beat - now <= BeatWait) return beat;
        }
        double best = NextBoundary(now, 4);
        for (int division = 2; division >= 1; division--)
        {
            double t = NextBoundary(now, division);
            if (t - now <= GridWait) best = t;
        }
        return best;
    }
}

/// <summary>Where the music is in a fight (<c>Docs/Audio.gdd</c> §7.2).</summary>
public enum MusicState : byte { Silent, Menu, Overworld, Shelter, Event, Combat, Boss, Victory, Defeat }

/// <summary>
/// The context blackboard (<c>Docs/Audio.gdd</c> §8): a few named values the director keeps
/// from what it hears on the cue bus, and that sound rows and the music read. Nothing here
/// is asked of gameplay — every number is derived from cues — so some are coarse on purpose
/// (the player's HP is "fine / low / down", because that is what the bus says).
/// Pure; a check feeds it by hand.
/// </summary>
public sealed class AudioBlackboard
{
    // ── Where we are ────────────────────────────────────────────────────────────────
    public string Realm = "VK";
    public int NodeLevel;
    public string Mission = "";
    public MusicState State = MusicState.Silent;
    /// <summary>Content id of the body the player wears, as last heard.</summary>
    public string PlayerOwner = "";
    /// <summary>0 at day 1 … 1 at day 45 or inside the VOID: how much the VOID has taken.</summary>
    public float Void;

    // ── The fight ───────────────────────────────────────────────────────────────────
    /// <summary>0…1, smoothed (0.4 s up, 4 s down): what the music's intensity follows.</summary>
    public float Intensity { get; private set; }
    /// <summary>Recent damage dealt and taken, decaying by half every 2.5 s.</summary>
    public float Heat { get; private set; }
    public int Foes;
    /// <summary>1 fine · the low-HP share when low · 0 down — as coarse as the bus tells it.</summary>
    public float PlayerHp = 1f;
    public bool Boss;
    public string BossId = "";
    /// <summary>Seconds on the mission's clock as last announced; negative = unknown / none.</summary>
    public float MissionTimeLeft = -1f;
    public bool Submerged;
    /// <summary>False until the mission starts: the countdown is not yet a fight.</summary>
    public bool Fighting;

    // ── The combo ───────────────────────────────────────────────────────────────────
    public int Combo { get; private set; }
    public const double ComboWindow = 1.6;
    public const double ComboStepEvery = 0.06;
    private double _lastHitAt = double.NegativeInfinity;
    private double _lastStepAt = double.NegativeInfinity;

    public const float LowHpShare = Fableland.Data.FeedbackTable.LowHpRatio;
    public const float UrgentSeconds = 30f;
    public const float BossFloor = 0.6f;
    public const float CountdownCeiling = 0.15f;

    /// <summary>The value intensity is heading for (before smoothing).</summary>
    public float Target
    {
        get
        {
            if (State != MusicState.Combat && State != MusicState.Boss) return Intensity;
            if (!Fighting) return CountdownCeiling;
            float foes = Math.Min(1f, Foes / 4f);
            float heat = Math.Min(1f, Heat / 60f);
            float urgency = MissionTimeLeft >= 0f && MissionTimeLeft < UrgentSeconds ? 1f - MissionTimeLeft / UrgentSeconds : 0f;
            float target = 0.2f + 0.24f * foes + 0.24f * heat + 0.16f * (1f - PlayerHp) + 0.16f * urgency;
            if (Boss) target = Math.Max(target, BossFloor);
            return Math.Clamp(target, 0f, 1f);
        }
    }

    public BoardFlag Flags
    {
        get
        {
            BoardFlag flags = BoardFlag.None;
            if (Submerged) flags |= BoardFlag.Submerged;
            if (PlayerHp <= LowHpShare && PlayerHp > 0f) flags |= BoardFlag.LowHp;
            if (Boss) flags |= BoardFlag.Boss;
            if (MissionTimeLeft >= 0f && MissionTimeLeft <= 10f) flags |= BoardFlag.Urgent;
            if (Intensity >= 0.7f) flags |= BoardFlag.Heated;
            return flags;
        }
    }

    public void Update(float dt)
    {
        Heat *= MathF.Pow(0.5f, dt / 2.5f);
        float target = Target;
        float rate = target > Intensity ? dt / 0.4f : dt / 4f;
        Intensity += (target - Intensity) * Math.Min(1f, rate);
    }

    /// <summary>Set intensity directly (the screens between fights, the audition page).</summary>
    public void SetIntensity(float value) => Intensity = Math.Clamp(value, 0f, 1f);

    /// <summary>A new arena: everything about the last fight is forgotten.</summary>
    public void EnterArena(string realm, int nodeLevel)
    {
        Realm = realm;
        NodeLevel = nodeLevel;
        Mission = "";
        Foes = 0;
        Heat = 0f;
        PlayerHp = 1f;
        Boss = false;
        BossId = "";
        MissionTimeLeft = -1f;
        Submerged = false;
        Fighting = false;
        Combo = 0;
    }

    /// <summary>The player's side landed a hit. Returns the combo count if this hit is a new
    /// step of the ladder, 0 if it is part of the same moment (a volley is one step).</summary>
    public int PlayerHit(float damage, double now)
    {
        Heat += damage;
        if (now - _lastHitAt > ComboWindow) Combo = 0;
        _lastHitAt = now;
        if (now - _lastStepAt < ComboStepEvery) return 0;
        _lastStepAt = now;
        Combo++;
        return Combo;
    }

    /// <summary>A kill resolves the phrase: the next hit starts the climb again.</summary>
    public void Resolve() => Combo = 0;

    /// <summary>The player was hit: felt half again as much as hitting, and the combo drops.</summary>
    public void PlayerHurt(float damage)
    {
        Heat += damage * 1.5f;
        Combo = 0;
    }

    /// <summary>Semitones above the tonic for the <paramref name="step"/>-th hit of a combo
    /// (1-based): up the realm's scale, and down an octave whenever it would pass a ninth —
    /// so it always climbs and never leaves the instrument's range.</summary>
    public static int LadderSemis(int[] scale, int step)
    {
        int semis = Synth.Dsp.ScaleSemis(scale, Math.Max(0, step - 1));
        while (semis > 14) semis -= 12;
        return semis;
    }
}

using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>
/// How heavy a hit is — a feedback tier (<c>Docs/Feedback.gdd</c> §3), never a damage rule.
/// An attack may name its own weight (<c>HitInfo.Weight</c>); <see cref="Auto"/> derives it
/// from the damage the hit actually dealt.
/// </summary>
public enum HitWeight
{
    Auto = 0,
    Graze = 1,     // T0 — a tick, a scratch: a number and a tint, nothing else
    Light = 2,     // T1
    Medium = 3,    // T2
    Heavy = 4,     // T3
    Massive = 5,   // T4 — an ultimate, a finisher
}

/// <summary>
/// The numbers behind hit feedback that change a fight's timing — hit-stop — and the
/// thresholds every other layer of feedback keys off. Pure data (no Godot types): gameplay
/// reads it to hold bodies still, presentation reads it to scale what it shows. Every value
/// is a starting number from <c>Docs/Research/R2-vfx-feel-research.md</c> §4.2, owned by
/// <c>Docs/Feedback.gdd</c> §3–§4; change it there and here together.
///
/// A hit is measured by its IMPACT SCORE <c>D</c>: the damage it dealt after mitigation —
/// or, on the body the player wears, <c>150 × damage / MaxHP</c>, so a hit is felt by what
/// it costs. The score picks the tier, and the stop is linear in it:
/// <c>stop = clamp(20 ms + 1.6 ms · D, 33 ms, 180 ms)</c>.
/// </summary>
public static class FeedbackTable
{
    public sealed class HitTier
    {
        public HitWeight Weight { get; init; }
        /// <summary>Smallest impact score that reaches this tier when the attack names none.</summary>
        public float MinScore { get; init; }
    }

    /// <summary>Ordered light to heavy. Index = (int)HitWeight - 1.</summary>
    public static readonly HitTier[] Tiers =
    {
        new() { Weight = HitWeight.Graze,   MinScore = 0f },
        new() { Weight = HitWeight.Light,   MinScore = 8f },
        new() { Weight = HitWeight.Medium,  MinScore = 25f },
        new() { Weight = HitWeight.Heavy,   MinScore = 60f },
        new() { Weight = HitWeight.Massive, MinScore = 150f },
    };

    // ── Hit-stop: victim stop = clamp(StopBase + StopPerScore · D, StopMin, StopMax) ──
    public const float StopBaseSeconds = 0.020f;
    public const float StopPerScore = 0.0016f;
    /// <summary>Two frames at 60 Hz: the shortest stop that reads.</summary>
    public const float StopMinSeconds = 0.033f;
    /// <summary>No single hit holds a body longer than this.</summary>
    public const float StopMaxSeconds = 0.180f;
    /// <summary>A melee attacker is held for this share of the victim's stop — shorter, so
    /// landing a blow never feels like losing control.</summary>
    public const float AttackerStopShare = 0.7f;
    /// <summary>Added to the attacker's stop on the blow that kills…</summary>
    public const float KillAttackerBonusSeconds = 0.033f;
    /// <summary>…which is never shorter than this in all.</summary>
    public const float KillAttackerMinSeconds = 0.067f;
    /// <summary>The body the player wears is held at least this long by any hit that
    /// stops it, and never longer than <see cref="PlayerVictimMaxSeconds"/>.</summary>
    public const float PlayerVictimMinSeconds = 0.050f;
    public const float PlayerVictimMaxSeconds = 0.150f;
    /// <summary>The player's impact score is <c>this × damage / MaxHP</c>.</summary>
    public const float PlayerScoreScale = 150f;

    /// <summary>A body that came out of a stop this recently is not stopped again — a
    /// stream of small hits must not hold a body still.</summary>
    public const float StopRearmSeconds = 0.10f;
    /// <summary>The most a body may have been held "recently": the budget every granted
    /// stop is taken from. One full-length stop always fits.</summary>
    public const float StopBudgetSeconds = 0.18f;
    /// <summary>How fast that budget comes back, in seconds of stop per second — so under
    /// any sustained fire a body is held about this share of the time, and no more.</summary>
    public const float StopBudgetRecoveryPerSecond = 0.15f;

    // ── Thresholds the cue raisers use ──────────────────────────────────────────────
    /// <summary>HP share at or under which a body counts as low (<c>FeedbackState.LowHp</c>).</summary>
    public const float LowHpRatio = 0.30f;
    /// <summary>A touchdown slower than this (px/s; 6 m/s, a 28 cm drop) raises no "land".</summary>
    public const float LandCueMinSpeed = 192f;
    /// <summary>A foe leaving the floor with at least this much upward intent (px/s) jumped.</summary>
    public const float FoeJumpCueMinSpeed = 160f;
    /// <summary>Hazard and damage-over-time ticks are merged into one "hurt.tick" per source
    /// kind at most this often, carrying the damage since the last one. Under three a second,
    /// so nothing a listener hangs on it can pulse faster than that.</summary>
    public const float HurtTickCueSeconds = 0.35f;

    public static HitTier Tier(HitWeight weight)
    {
        int index = Math.Clamp((int)weight - 1, 0, Tiers.Length - 1);
        return Tiers[index];
    }

    /// <summary>A hit's impact score: what it dealt, or — on the body the player wears —
    /// what share of that body it cost.</summary>
    public static float ImpactScore(float damageDealt, bool playerVictim, float victimMaxHp)
    {
        if (damageDealt <= 0f) return 0f;
        return playerVictim && victimMaxHp > 0f ? PlayerScoreScale * damageDealt / victimMaxHp : damageDealt;
    }

    /// <summary>The tier a hit of this impact score reaches on its own.</summary>
    public static HitWeight WeightFor(float score)
    {
        HitWeight weight = HitWeight.Graze;
        for (int i = 0; i < Tiers.Length; i++)
            if (score >= Tiers[i].MinScore) weight = Tiers[i].Weight;
        return weight;
    }

    /// <summary>Resolve an attack's named weight, or derive one from its impact score.</summary>
    public static HitWeight Resolve(HitWeight named, float score) =>
        named == HitWeight.Auto ? WeightFor(score) : named;

    /// <summary>The score a hit of a given weight is held to. A weight derived from the
    /// score leaves it alone; a NAMED weight clamps it into that tier's band, so a tick
    /// named Light stays a light hit however far a buff pushes its damage.</summary>
    public static float ScoreWithin(HitWeight weight, float score)
    {
        int index = Math.Clamp((int)weight - 1, 0, Tiers.Length - 1);
        float low = Tiers[index].MinScore;
        float high = index + 1 < Tiers.Length ? Tiers[index + 1].MinScore - 1f : float.MaxValue;
        return Math.Clamp(score, low, Math.Max(low, high));
    }

    private static float RawStop(HitWeight weight, float score) =>
        Math.Clamp(StopBaseSeconds + StopPerScore * ScoreWithin(weight, score), StopMinSeconds, StopMaxSeconds);

    /// <summary>Seconds to hold the body that was hit. A graze holds nobody.
    /// <paramref name="scale"/> is the player's hit-stop setting (0 = off).</summary>
    public static float VictimStop(HitWeight weight, float score, bool playerVictim, float scale)
    {
        if (scale <= 0f || weight <= HitWeight.Graze) return 0f;
        float stop = RawStop(weight, score);
        if (playerVictim) stop = Math.Clamp(stop, PlayerVictimMinSeconds, PlayerVictimMaxSeconds);
        return stop * Math.Min(scale, 1f);
    }

    /// <summary>Seconds to hold a melee attacker for a swing whose heaviest hit was this —
    /// once per swing, however many it hit. A kill is always felt, even by a graze.</summary>
    public static float AttackerStop(HitWeight weight, float score, bool killed, float scale)
    {
        if (scale <= 0f) return 0f;
        float stop = weight <= HitWeight.Graze ? 0f : AttackerStopShare * RawStop(weight, score);
        if (killed) stop = Math.Max(stop + KillAttackerBonusSeconds, KillAttackerMinSeconds);
        return Math.Min(StopMaxSeconds, stop) * Math.Min(scale, 1f);
    }

    /// <summary>Boot-time checks (T10 §5): tiers ordered, thresholds rising, and the worked
    /// examples of <c>Docs/Feedback.gdd</c> §3.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        for (int i = 0; i < Tiers.Length; i++)
        {
            HitTier t = Tiers[i];
            if ((int)t.Weight != i + 1) failures.Add($"tier {i} is {t.Weight}, expected weight {i + 1}");
            if (i > 0 && t.MinScore <= Tiers[i - 1].MinScore) failures.Add($"{t.Weight}: MinScore does not rise");
        }
        if (WeightFor(0f) != HitWeight.Graze) failures.Add("score 0 is not a graze");
        if (WeightFor(7.9f) != HitWeight.Graze || WeightFor(8f) != HitWeight.Light) failures.Add("the Light threshold is not 8");
        if (WeightFor(1e6f) != HitWeight.Massive) failures.Add("a huge score is not massive");
        if (Resolve(HitWeight.Heavy, 1f) != HitWeight.Heavy) failures.Add("a named weight is not kept");

        Near(VictimStop(HitWeight.Light, 15f, false, 1f), 0.044f, "a 15-damage hit holds 44 ms", failures);
        Near(VictimStop(HitWeight.Medium, 30f, false, 1f), 0.068f, "a 30-damage hit holds 68 ms", failures);
        Near(VictimStop(HitWeight.Massive, 400f, false, 1f), StopMaxSeconds, "no hit holds longer than the cap", failures);
        Near(VictimStop(HitWeight.Light, 8f, false, 1f), StopMinSeconds, "the lightest stop is the minimum", failures);
        Near(VictimStop(HitWeight.Light, 200f, false, 1f), 0.0584f, "a named Light is held to the Light band", failures);
        Near(VictimStop(HitWeight.Heavy, 10f, false, 1f), 0.116f, "a named Heavy is lifted into the Heavy band", failures);
        if (VictimStop(HitWeight.Graze, 5f, false, 1f) != 0f) failures.Add("a graze holds a body");
        if (VictimStop(HitWeight.Heavy, 80f, false, 0f) != 0f) failures.Add("scale 0 does not switch hit-stop off");
        Near(VictimStop(HitWeight.Heavy, 80f, false, 0.5f), 0.074f, "scale 0.5 halves a stop", failures);

        // The player: 18 damage out of 200 HP scores 13.5 — a light hit, lifted to the floor.
        float crab = ImpactScore(18f, true, 200f);
        Near(crab, 13.5f, "the player's score is 150 × damage / MaxHP", failures);
        Near(VictimStop(WeightFor(crab), crab, true, 1f), PlayerVictimMinSeconds, "the player is held at least 50 ms", failures);
        Near(VictimStop(HitWeight.Massive, 300f, true, 1f), PlayerVictimMaxSeconds, "the player is held at most 150 ms", failures);

        Near(AttackerStop(HitWeight.Light, 15f, false, 1f), 0.0308f, "the attacker holds 0.7 of the victim's stop", failures);
        Near(AttackerStop(HitWeight.Light, 15f, true, 1f), KillAttackerMinSeconds, "a kill holds the attacker at least 67 ms", failures);
        Near(AttackerStop(HitWeight.Graze, 3f, true, 1f), KillAttackerMinSeconds, "a graze that kills is still felt", failures);
        if (AttackerStop(HitWeight.Graze, 3f, false, 1f) != 0f) failures.Add("a graze holds its attacker");
        float budget = StopBudgetSeconds, longest = StopMaxSeconds;
        if (budget < longest) failures.Add("the stop budget is smaller than one full stop");
        return failures;
    }

    private static void Near(float actual, float expected, string label, List<string> failures)
    {
        if (MathF.Abs(actual - expected) > 0.0006f) failures.Add($"{label}: expected {expected:F4}, got {actual:F4}");
    }
}

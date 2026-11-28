namespace Fableland.Bosses;

using System.Collections.Generic;
using Godot;
using Fableland.Data;

/// <summary>
/// What the map tells a boss about its arena. A boss map is built for its boss (FOES.gdd
/// §8.5): its Level Goal markers are the boss's <b>perches</b> — the places its kit works
/// best from — and the fight opens with the boss on the one farthest from the player.
/// </summary>
public sealed class BossArena
{
    /// <summary>The map's perches, in authored order. Never empty: a map without markers
    /// gives the boss the one point it was placed at.</summary>
    public IReadOnlyList<Vector2> Perches { get; }

    /// <summary>Index into <see cref="Perches"/> of the one the boss starts on.</summary>
    public int HomeIndex { get; }

    public Vector2 Home => Perches[HomeIndex];

    public BossArena(IReadOnlyList<Vector2> perches, int homeIndex)
    {
        Perches = perches is { Count: > 0 } ? perches : new[] { Vector2.Zero };
        HomeIndex = Mathf.Clamp(homeIndex, 0, Perches.Count - 1);
    }

    /// <summary>The perch farthest from <paramref name="from"/> — where a boss opens the
    /// fight, and where it retreats to when the player is on top of it.</summary>
    public static int FarthestFrom(IReadOnlyList<Vector2> perches, Vector2 from, int except = -1)
    {
        int best = -1;
        float bestDistance = -1f;
        for (int i = 0; i < perches.Count; i++)
        {
            if (i == except) continue;
            float distance = perches[i].DistanceSquaredTo(from);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best < 0 ? Mathf.Clamp(except, 0, perches.Count - 1) : best;
    }

    /// <summary>The perch nearest to <paramref name="from"/>.</summary>
    public static int NearestTo(IReadOnlyList<Vector2> perches, Vector2 from)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < perches.Count; i++)
        {
            float distance = perches[i].DistanceSquaredTo(from);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }
}

/// <summary>
/// The mission's side of a boss (FOES.gdd §8). A boss is a protagonist's class on the Foe
/// side — <c>class BossPumpKing : PumpKing</c> — so its HP, shield and death are the
/// ordinary <see cref="CharacterController"/> ones; this interface is only what a
/// protagonist does not have: being told the fight has started, and that it is over.
/// </summary>
public interface IBossFighter
{
    /// <summary>Sizes the boss from its table row and the day's foe level, shows it the
    /// arena and wakes its brain. Call AFTER AddChild and after placing it (KNOWLEDGE:
    /// "Init after AddChild").</summary>
    void BeginFight(BossDef def, int foeLevel, BossArena arena);

    /// <summary>The mission resolved while the boss still stands (a debug skip): stop
    /// acting and stop taking damage.</summary>
    void StandDown();

    /// <summary>What the brain is doing, for the debug HUD.</summary>
    string BrainState { get; }
}

/// <summary>The part of becoming a boss that is the same for every kit.</summary>
public static class BossSetup
{
    /// <summary>Source key of every modifier a boss row contributes (golden rule 7).</summary>
    public const string Source = "boss";

    /// <summary>Applies a table row: bulk and damage scale with the day's foe level exactly
    /// as a foe's do (FOES.gdd §2) — speed does not: a boss is not a faster protagonist —
    /// and poise is set.</summary>
    public static void ApplyRow(CharacterController body, BossDef def, int foeLevel)
    {
        FoeStats.LevelStats level = FoeStats.ForLevel(foeLevel);
        body.SetFullHealth(def.BaseHp * level.HpMult);
        body.SetDamageDealtBonus(Source, def.DamageScale * level.AtkMult - 1f);
        body.SetKnockbackTakenScale(Source, def.KnockbackTaken);
        body.SetStunTakenScale(Source, def.StunTaken);
        // The boss ring (Realms.gdd §13): over time, and per point dealt or taken.
        body.UltChargePerSecond = def.UltRingPerSecond;
        body.UltChargeDealtRate = def.UltRingPerDamage;
        body.UltChargeReceivedRate = def.UltRingPerDamage;
    }
}

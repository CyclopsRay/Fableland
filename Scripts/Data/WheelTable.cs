using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>The six realms of the Wheel of Becoming (Docs/Mara.gdd §4.1), in wheel order —
/// clockwise from the top, as many Tibetan paintings lay them out. The order is data: the
/// wheel turns through it.</summary>
public enum WheelRealm
{
    Gods = 0,
    Asuras = 1,
    HungryGhosts = 2,
    Hell = 3,
    Animals = 4,
    Humans = 5,
}

/// <summary>One realm of the wheel: its name, the poison whose gate it belongs to, the power it
/// gives Māra while it is in force, and the light it throws over the arena (placeholder until
/// the arena is painted, Mara.gdd §9.4).</summary>
public sealed class WheelRealmDef
{
    public WheelRealm Realm { get; init; }
    /// <summary>Stable id, the tag of the wheel's cues: "gods" … "humans".</summary>
    public string Id { get; init; }
    /// <summary>天 修罗 饿鬼 地狱 畜生 人.</summary>
    public string Glyph { get; init; }
    public string Name { get; init; }
    /// <summary>The <c>Zone6Table</c> poison whose gate this realm belongs to; "" for the humans.</summary>
    public string PoisonId { get; init; } = "";
    /// <summary>What it gives Māra ("Iron Throne"), or "the Interval".</summary>
    public string Power { get; init; }
    /// <summary>The wash of light over the arena while it is in force (RGBA, A = strength).</summary>
    public float LightR { get; init; }
    public float LightG { get; init; }
    public float LightB { get; init; }
    public float LightA { get; init; }
    /// <summary>The sigil's colour on the rim: its poison's buddha-family colour (§4.1).</summary>
    public float SigilR { get; init; }
    public float SigilG { get; init; }
    public float SigilB { get; init; }

    public bool IsInterval => Realm == WheelRealm.Humans;
    /// <summary>"地狱 Hell".</summary>
    public string Title => $"{Glyph} {Name}";
}

/// <summary>
/// The Wheel of Becoming (轮回之轮, Docs/Mara.gdd §4–§7, §12): Avīci's dark leader's signature.
/// Pure data. Every number is the design's FIRST PASS (Mara.gdd §12, TBD #66).
/// </summary>
public static class WheelTable
{
    /// <summary>The value of <c>BossDef.Signature</c> that runs the wheel.</summary>
    public const string Signature = "wheel";

    /// <summary>Owner of the wheel's cues (the fight's, not a body's: it turns whatever kit
    /// stands in) — the same id as Māra's body, so his look and his wheel share one style.</summary>
    public const string CueOwner = "z6_mara";

    /// <summary>The source key of every modifier the wheel puts on the boss (golden rule 7).</summary>
    public const string Source = "zone6:wheel";

    /// <summary>The fight opens here: you are in Avīci.</summary>
    public const WheelRealm OpensOn = WheelRealm.Hell;

    // ── Rests ───────────────────────────────────────────────────────────────────────
    /// <summary>A non-human realm's rest, from the moment it lands to the next landing.</summary>
    public const float RestSeconds = 8f;
    /// <summary>…below <see cref="RestsShortenBelowHp"/> of his HP.</summary>
    public const float RestBelowHalfSeconds = 6f;
    public const float RestsShortenBelowHp = 0.5f;
    /// <summary>The human realm's rest: the Interval. Never shortened.</summary>
    public const float IntervalSeconds = 6f;
    /// <summary>Each stack of a poison in you holds its realm this much longer.</summary>
    public const float SecondsPerPoisonStack = 1f;
    /// <summary>The last part of a rest: the incoming sigil flares and the stone grinds.</summary>
    public const float TellSeconds = 1f;
    /// <summary>A turn waits this long at most for the colossus's hand to come back to the wheel.</summary>
    public const float HandWaitMaxSeconds = 1.5f;

    // ── The finish ──────────────────────────────────────────────────────────────────
    /// <summary>His last share of HP: only the Interval can take it (§7.2).</summary>
    public const float LockShare = 0.05f;
    /// <summary>The Last Turn passes each unsealed realm in this long (§7.3).</summary>
    public const float LastTurnStepSeconds = 0.9f;

    // ── What the realms put on his body ────────────────────────────────────────────
    public const float IntervalDamageTaken = 1.5f;
    public const float ThroneDamageTaken = 0.3f;
    /// <summary>Wrath: his cooldowns and attack interval, ×.</summary>
    public const float WrathScale = 0.6f;
    /// <summary>Devour: each hit he lands heals this share of his max HP.</summary>
    public const float DevourHealPerHit = 0.025f;
    /// <summary>Devour: a projectile that reaches his front heals this share instead of hurting.</summary>
    public const float SwallowHealShare = 0.01f;

    // ── The Tempter's Offering (§7.5) ───────────────────────────────────────────────
    public const float OfferingBelowHp = 0.3f;
    public const float OfferingSeconds = 8f;
    /// <summary>The poison the bowl puts in a fighter who carries none.</summary>
    public const string OfferingDefaultPoison = "greed";

    /// <summary>The realms, in wheel order (index = <see cref="WheelRealm"/>).</summary>
    public static readonly WheelRealmDef[] Realms =
    {
        new() { Realm = WheelRealm.Gods, Id = "gods", Glyph = "天", Name = "Gods", PoisonId = "pride", Power = "Iron Throne",
                LightR = 1.00f, LightG = 0.90f, LightB = 0.62f, LightA = 0.10f, SigilR = 0.96f, SigilG = 0.80f, SigilB = 0.22f },
        new() { Realm = WheelRealm.Asuras, Id = "asuras", Glyph = "修罗", Name = "Asuras", PoisonId = "anger", Power = "Wrath",
                LightR = 0.95f, LightG = 0.34f, LightB = 0.12f, LightA = 0.14f, SigilR = 0.20f, SigilG = 0.40f, SigilB = 0.92f },
        new() { Realm = WheelRealm.HungryGhosts, Id = "hungry_ghosts", Glyph = "饿鬼", Name = "Hungry ghosts", PoisonId = "greed", Power = "Devour",
                LightR = 0.52f, LightG = 0.62f, LightB = 0.46f, LightA = 0.18f, SigilR = 0.88f, SigilG = 0.16f, SigilB = 0.12f },
        new() { Realm = WheelRealm.Hell, Id = "hell", Glyph = "地狱", Name = "Hell", PoisonId = "doubt", Power = "Doubt",
                LightR = 0.62f, LightG = 0.04f, LightB = 0.06f, LightA = 0.20f, SigilR = 0.18f, SigilG = 0.66f, SigilB = 0.34f },
        new() { Realm = WheelRealm.Animals, Id = "animals", Glyph = "畜生", Name = "Animals", PoisonId = "delusion", Power = "Delusion",
                LightR = 0.58f, LightG = 0.42f, LightB = 0.20f, LightA = 0.16f, SigilR = 0.95f, SigilG = 0.94f, SigilB = 0.90f },
        new() { Realm = WheelRealm.Humans, Id = "humans", Glyph = "人", Name = "Humans", PoisonId = "", Power = "the Interval",
                LightR = 1.00f, LightG = 0.94f, LightB = 0.80f, LightA = 0.06f },
    };

    public static int Count => Realms.Length;

    public static WheelRealmDef Of(WheelRealm realm) => Realms[(int)realm];

    /// <summary>The defense that makes damage taken <paramref name="scale"/>× (a body's damage
    /// taken is 100/(100+defense)): ×1.5 → −33.3, ×0.3 → +233.3.</summary>
    public static float DefenseFor(float scale) => 100f / Math.Max(0.01f, scale) - 100f;

    /// <summary>The realm whose gate a poison keeps, or null.</summary>
    public static WheelRealmDef OfPoison(string poisonId)
    {
        if (string.IsNullOrEmpty(poisonId)) return null;
        foreach (WheelRealmDef realm in Realms)
            if (string.Equals(realm.PoisonId, poisonId, StringComparison.Ordinal)) return realm;
        return null;
    }

    /// <summary>Boot validation (T10 §5): every violation, not just the first.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        var poisons = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Realms.Length; i++)
        {
            WheelRealmDef realm = Realms[i];
            if ((int)realm.Realm != i) problems.Add($"WheelTable: '{realm.Id}' sits at {i} but is realm {(int)realm.Realm}; the table is the wheel's order");
            if (realm.IsInterval)
            {
                if (realm.PoisonId.Length > 0) problems.Add("WheelTable: the human realm belongs to no gate");
                continue;
            }
            if (Zone6Table.Poison(realm.PoisonId) == null) problems.Add($"WheelTable {realm.Id}: poison '{realm.PoisonId}' is not one of Avīci's five");
            else if (!poisons.Add(realm.PoisonId)) problems.Add($"WheelTable {realm.Id}: poison '{realm.PoisonId}' has two realms");
            else if (Zone6Table.Poison(realm.PoisonId).MaraAbility != realm.Power)
                problems.Add($"WheelTable {realm.Id}: its power '{realm.Power}' is not the one its gate seals ('{Zone6Table.Poison(realm.PoisonId).MaraAbility}')");
        }
        if (poisons.Count != Zone6Table.Poisons.Length) problems.Add($"WheelTable: {poisons.Count} realms carry a poison; Avīci has {Zone6Table.Poisons.Length}");

        // Constants, asked through a function (KNOWLEDGE v0.12.0).
        void Require(bool ok, string what) { if (!ok) problems.Add(what); }
        Require(TellSeconds > 0f && TellSeconds < RestBelowHalfSeconds && TellSeconds < IntervalSeconds, "WheelTable: the tell must fit inside the shortest rest");
        Require(RestBelowHalfSeconds <= RestSeconds, "WheelTable: rests must shorten below half HP, not lengthen");
        Require(LockShare > 0f && LockShare < RestsShortenBelowHp, "WheelTable: the lock must be a sliver of his HP");
        Require(IntervalDamageTaken > 1f && ThroneDamageTaken > 0f && ThroneDamageTaken < 1f, "WheelTable: the Interval opens him and the throne guards him");
        Require(WrathScale > 0f && WrathScale < 1f, "WheelTable: Wrath must quicken him");
        Require(Zone6Table.Poisons.Length > 0 && Zone6Table.Poisons[0].Id == OfferingDefaultPoison,
            "WheelTable: the Offering's default poison must come first in gate order (ties go to gate order)");
        return problems;
    }
}

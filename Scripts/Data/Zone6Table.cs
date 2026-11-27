using System;
using System.Collections.Generic;
using Fableland.Map;

namespace Fableland.Data;

/// <summary>
/// One of the five poisons of Avīci (五毒, Docs/Zone6.gdd §4): a gate warden, the seal it puts
/// on you when it beats you, and the power it takes from Māra when you beat it.
/// </summary>
public sealed class Zone6PoisonDef
{
    /// <summary>Stable id: greed, anger, delusion, pride, doubt.</summary>
    public string Id { get; init; }
    /// <summary>贪 嗔 痴 慢 疑.</summary>
    public string Glyph { get; init; }
    /// <summary>"Greed".</summary>
    public string Name { get; init; }
    /// <summary>The <c>BossTable</c> id of the warden that keeps this gate.</summary>
    public string MiniBossId { get; init; }

    /// <summary>The seal's name ("Hunger") and its effect in a player's words.</summary>
    public string SealName { get; init; }
    public string SealSummary { get; init; }

    // What one stack of the seal does to every protagonist (each a share added per stack).
    public float ReloadTimePerStack { get; init; }      // reload time +x
    public float DamageTakenPerStack { get; init; }     // damage taken +x
    public float CooldownPerStack { get; init; }        // skill cooldowns +x
    public float MoveSpeedLossPerStack { get; init; }   // move speed −x
    public float AttackIntervalPerStack { get; init; }  // basic-attack interval +x

    /// <summary>The ability Māra loses when this warden falls ("Devour") and what it was.</summary>
    public string MaraAbility { get; init; }
    public string MaraAbilitySummary { get; init; }
}

/// <summary>How a zone-6 variant's borrowed maps are drawn (<c>VoidTreatment</c>'s uniforms):
/// grey = luma(c); out = mix(grey, c, ColourKept) × Shade + Lift.</summary>
public sealed class Zone6Look
{
    public float ColourKept { get; init; } = 0.22f;
    public float ShadeR { get; init; } = 0.50f;
    public float ShadeG { get; init; } = 0.44f;
    public float ShadeB { get; init; } = 0.62f;
    public float LiftR { get; init; }
    public float LiftG { get; init; }
    public float LiftB { get; init; }
}

/// <summary>A zone-6 variant (Docs/Zone6.gdd): its story, its five mini-bosses in node order
/// (XX-5-1 … XX-5-5), its final boss, the arena that boss is fought on, and its look.</summary>
public sealed class Zone6VariantDef
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string NameZh { get; init; }
    /// <summary>One sentence: where you are.</summary>
    public string Premise { get; init; }
    /// <summary><c>BossTable</c> ids of the five mini-bosses, for nodes XX-5-1 … XX-5-5.</summary>
    public string[] MiniBossIds { get; init; }
    /// <summary><c>BossTable</c> id of the final boss at the core (XX-6-1).</summary>
    public string FinalBossId { get; init; }
    /// <summary>Id of the map the final boss is always fought on, or null: then the core is dealt
    /// a boss arena of one of the run's worlds, as every zone-6 node is (MapGDD §7).</summary>
    public string FinalArenaMapId { get; init; }
    public Zone6Look Look { get; init; }
}

/// <summary>
/// Zone 6 (第六区, Docs/Zone6.gdd): the run's last stage, one of a rotation of variants each with
/// its own mechanic and story. Pure data and pure arithmetic; <c>Zone6State</c> holds a run's
/// progress. Every number is FIRST-PASS (the owner set the 20% / 10% gravity steps; the rest
/// are this document's proposals) and is recorded as provisional in Zone6.gdd's Decisions log.
/// </summary>
public static class Zone6Table
{
    public const string VoidFrontierId = "void_frontier";
    public const string AviciId = "avici";

    // ── The Void Frontier: the gravity ladder ─────────────────────────────────────
    /// <summary>Gravity on arrival.</summary>
    public const float GravityOnArrival = 1f;
    /// <summary>A mini-boss victory multiplies gravity by this (−20%, the owner's number).</summary>
    public const float GravityWinFactor = 0.8f;
    /// <summary>A defeat multiplies it by this (+10%, the owner's number).</summary>
    public const float GravityLossFactor = 1.1f;
    /// <summary>Gravity never leaves [Min, Max]. Multiplicative steps never reach zero, but a
    /// long run of defeats would grow without end.</summary>
    public const float GravityMin = 0.25f;
    public const float GravityMax = 2f;

    // ── Any zone-6 mini-boss: losing ends the fight, not the run ──────────────────
    /// <summary>Every fighter of the battle team returns with at least this share of HP.</summary>
    public const float DefeatHpFloor = 0.25f;

    // ── Avīci ────────────────────────────────────────────────────────────────────
    /// <summary>A further defeat by the same poison adds a stack, up to this.</summary>
    public const int SealStackCap = 3;
    /// <summary>Until Māra has a kit, each ability sealed in him costs the stand-in this share
    /// of its HP and this much of its damage (Zone6.gdd §4.3).</summary>
    public const float MaraHpPerSeal = 0.12f;
    public const float MaraDamagePerSeal = 0.10f;

    public static readonly Zone6PoisonDef Greed = new()
    {
        Id = "greed", Glyph = "贪", Name = "Greed", MiniBossId = "z6_preta",
        SealName = "Hunger", SealSummary = "reload time +30%", ReloadTimePerStack = 0.30f,
        MaraAbility = "Devour", MaraAbilitySummary = "heals from the damage he deals",
    };

    public static readonly Zone6PoisonDef Anger = new()
    {
        Id = "anger", Glyph = "嗔", Name = "Anger", MiniBossId = "z6_asura",
        SealName = "Smoulder", SealSummary = "damage taken +15%", DamageTakenPerStack = 0.15f,
        MaraAbility = "Wrath", MaraAbilitySummary = "an enraged phase below half HP",
    };

    public static readonly Zone6PoisonDef Delusion = new()
    {
        Id = "delusion", Glyph = "痴", Name = "Delusion", MiniBossId = "z6_boar",
        SealName = "Fog", SealSummary = "skill cooldowns +25%", CooldownPerStack = 0.25f,
        MaraAbility = "Delusion", MaraAbilitySummary = "mirror images",
    };

    public static readonly Zone6PoisonDef Pride = new()
    {
        Id = "pride", Glyph = "慢", Name = "Pride", MiniBossId = "z6_fallen_deva",
        SealName = "Burden", SealSummary = "move speed −15%", MoveSpeedLossPerStack = 0.15f,
        MaraAbility = "Iron Throne", MaraAbilitySummary = "an invulnerable throne phase",
    };

    public static readonly Zone6PoisonDef Doubt = new()
    {
        Id = "doubt", Glyph = "疑", Name = "Doubt", MiniBossId = "z6_ox_horse",
        SealName = "Hesitation", SealSummary = "attack interval +20%", AttackIntervalPerStack = 0.20f,
        MaraAbility = "Doubt", MaraAbilitySummary = "a curse that makes some of your casts fizzle",
    };

    /// <summary>The five poisons, in gate (node) order: XX-5-1 is Greed … XX-5-5 is Doubt.</summary>
    public static readonly Zone6PoisonDef[] Poisons = { Greed, Anger, Delusion, Pride, Doubt };

    public static readonly Zone6VariantDef VoidFrontier = new()
    {
        Id = VoidFrontierId,
        Name = "The Void Frontier",
        NameZh = "虚无边境",
        Premise = "Past the River Styx, the shore where life and death touch.",
        MiniBossIds = new[] { "z6_ferryman", "z6_weigher", "z6_meng_po", "z6_xolotl", "z6_stingy_jack" },
        FinalBossId = "z6_unwritten",
        // Until its own arena is painted (an empty floor, the Styx at the horizon — §6), the
        // emptiest shipped floor: Starland's sea-level plain, one unbroken floor and three slabs,
        // in the medium whose rule is "emptiness is the composition". Not Starland's boss arena:
        // its 12 m plateau would stand above the Styx Tide for the whole fight.
        FinalArenaMapId = "starfield_sea_level_sl",
        // Drained toward a cold grey-teal mist: little colour, lifted, cooled.
        Look = new Zone6Look
        {
            ColourKept = 0.14f, ShadeR = 0.56f, ShadeG = 0.68f, ShadeB = 0.70f,
            LiftR = 0.10f, LiftG = 0.14f, LiftB = 0.15f,
        },
    };

    public static readonly Zone6VariantDef Avici = new()
    {
        Id = AviciId,
        Name = "Avīci",
        NameZh = "无间地狱",
        Premise = "The lowest hell, without interval; Māra keeps the five poisons at its gates.",
        MiniBossIds = new[] { Greed.MiniBossId, Anger.MiniBossId, Delusion.MiniBossId, Pride.MiniBossId, Doubt.MiniBossId },
        FinalBossId = "z6_mara",
        FinalArenaMapId = null,
        // Drained toward ember red on black: dark, and what colour is left is fire.
        Look = new Zone6Look
        {
            ColourKept = 0.24f, ShadeR = 0.66f, ShadeG = 0.22f, ShadeB = 0.16f,
        },
    };

    /// <summary>The rotation. Order is data: a seed picks an index, so append only.</summary>
    public static readonly Zone6VariantDef[] Variants = { VoidFrontier, Avici };

    /// <summary>The look a zone-6 map had before variants (v0.11.3): what a zone-6 node with no
    /// variant (a save from before v0.12.0 never resolves to one; this is only a fallback) wears.</summary>
    public static readonly Zone6Look DefaultLook = new();

    public static Zone6VariantDef Variant(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (Zone6VariantDef variant in Variants)
            if (string.Equals(variant.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) return variant;
        return null;
    }

    public static Zone6PoisonDef Poison(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (Zone6PoisonDef poison in Poisons)
            if (string.Equals(poison.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) return poison;
        return null;
    }

    /// <summary>The poison whose warden this boss is, or null.</summary>
    public static Zone6PoisonDef PoisonOfBoss(string bossId)
    {
        foreach (Zone6PoisonDef poison in Poisons)
            if (string.Equals(poison.MiniBossId, bossId, StringComparison.Ordinal)) return poison;
        return null;
    }

    /// <summary>
    /// Which variant a run's zone 6 is. Drawn from a stream of its own — never one the map
    /// generator or anything else draws from — so adding variants or this choice moves nothing
    /// else a seed makes (T40 §6).
    /// </summary>
    public static string VariantForSeed(string seed)
    {
        var rng = new DetRandom((seed ?? "") + ":zone6:variant");
        return Variants[rng.Range(0, Variants.Length - 1)].Id;
    }

    /// <summary>One step of the gravity ladder, clamped.</summary>
    public static float StepGravity(float gravity, bool won)
    {
        float next = gravity * (won ? GravityWinFactor : GravityLossFactor);
        return Math.Clamp(next, GravityMin, GravityMax);
    }

    /// <summary>
    /// The zone-6 mini-boss slot of a node id: XX-5-1 … XX-5-5 → 0 … 4; anything else −1.
    /// Node ids are fixed by the generator (MapGenerator.BuildZone6), one per angle of the
    /// pentagon, so the mapping is deterministic and never draws.
    /// </summary>
    public static int MiniBossSlot(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || !nodeId.StartsWith("XX-5-", StringComparison.Ordinal)) return -1;
        return int.TryParse(nodeId.Substring(5), out int k) && k >= 1 && k <= 5 ? k - 1 : -1;
    }

    /// <summary>Boot validation (T10 §5): every violation, not just the first.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        var variantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Zone6VariantDef v in Variants)
        {
            string name = v.Id ?? "(no id)";
            if (!variantIds.Add(name)) problems.Add($"Zone6Table: variant '{name}' is listed twice");
            if (v.MiniBossIds is not { Length: 5 }) problems.Add($"Zone6Table {name}: needs exactly five mini-bosses, one per LV5 node");
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in v.MiniBossIds)
                {
                    if (!BossTable.TryGet(id, out BossDef def)) problems.Add($"Zone6Table {name}: mini-boss '{id}' is not in BossTable");
                    else if (def.Role != BossRole.Zone6Mini) problems.Add($"Zone6Table {name}: '{id}' is not a zone-6 mini-boss row");
                    if (!seen.Add(id ?? "")) problems.Add($"Zone6Table {name}: mini-boss '{id}' is used twice");
                }
            }
            if (!BossTable.TryGet(v.FinalBossId, out BossDef final)) problems.Add($"Zone6Table {name}: final boss '{v.FinalBossId}' is not in BossTable");
            else if (final.Role != BossRole.Zone6Final) problems.Add($"Zone6Table {name}: '{v.FinalBossId}' is not a zone-6 final-boss row");
            if (v.Look == null) problems.Add($"Zone6Table {name}: no look");
            else if (v.Look.ColourKept < 0f || v.Look.ColourKept > 1f) problems.Add($"Zone6Table {name}: colour kept must be 0..1");
        }
        var poisonIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Zone6PoisonDef p in Poisons)
        {
            if (!poisonIds.Add(p.Id ?? "")) problems.Add($"Zone6Table: poison '{p.Id}' is listed twice");
            float effects = p.ReloadTimePerStack + p.DamageTakenPerStack + p.CooldownPerStack + p.MoveSpeedLossPerStack + p.AttackIntervalPerStack;
            if (!(effects > 0f)) problems.Add($"Zone6Table {p.Id}: its seal does nothing");
            if (p.MoveSpeedLossPerStack * SealStackCap >= 1f) problems.Add($"Zone6Table {p.Id}: at the stack cap its seal would stop a fighter");
            if (string.IsNullOrWhiteSpace(p.MaraAbility)) problems.Add($"Zone6Table {p.Id}: names no ability of Māra's");
        }
        // The numbers are constants: asked through a function, so a rule that holds today is
        // still checked (and a compiler does not call the other branch unreachable).
        void Require(bool ok, string what) { if (!ok) problems.Add(what); }
        Require(GravityWinFactor > 0f && GravityWinFactor < 1f, "Zone6Table: a victory must make gravity lighter");
        Require(GravityLossFactor > 1f, "Zone6Table: a defeat must make gravity heavier");
        Require(GravityMin > 0f && GravityMin <= GravityOnArrival && GravityOnArrival <= GravityMax, "Zone6Table: gravity's clamp must hold the arrival value");
        Require(MaraHpPerSeal * Poisons.Length < 1f, "Zone6Table: five seals would leave Māra's stand-in no HP");
        Require(DefeatHpFloor > 0f && DefeatHpFloor <= 1f, "Zone6Table: the defeat HP floor must be in (0, 1]");
        return problems;
    }
}

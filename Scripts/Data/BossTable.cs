using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>
/// One BOSS: the protagonist a realm's 4-1 node pits you against, and — once beaten —
/// recruits to your roster (NODES.gdd §4.5, FOES.gdd §8).
///
/// A boss fights with its protagonist's own kit but is NOT that protagonist: it has its own
/// table row, with its own bulk, damage, poise, magazine and cooldowns. The class behind
/// <see cref="ScenePath"/> inherits the protagonist's class and reads everything that
/// differs from here, so "what makes the boss different" is one row, not code.
/// </summary>
public sealed class BossDef
{
    /// <summary>Stable snake_case id (T20 §1) — what an AdventureContext carries.</summary>
    public string Id { get; init; }

    /// <summary>The name on the boss bar.</summary>
    public string DisplayName { get; init; }

    /// <summary>Roster id of the protagonist this boss is, granted on victory.</summary>
    public string ProtagonistId { get; init; }

    /// <summary>The world whose LV4 node this boss holds (`MapGDD.md` §2 abbreviation).</summary>
    public string WorldAbbr { get; init; }

    /// <summary>`res://` scene rooted in the boss's class.</summary>
    public string ScenePath { get; init; }

    /// <summary>HP at foe level 1. Multiplied by the day's `FoeStats` HP multiplier, like
    /// every foe, so the day axis keeps its meaning in a boss room (FOES.gdd §1).</summary>
    public float BaseHp { get; init; }

    /// <summary>Share of the protagonist kit's damage this boss deals at foe level 1
    /// (then × the day's ATK multiplier). A kit balanced for hitting crabs hits a
    /// 150–300 HP player much too hard at 1.</summary>
    public float DamageScale { get; init; } = 1f;

    /// <summary>Share of an incoming hit's knockback / gain-no window the boss takes
    /// (poise). 1 = flinches like a protagonist.</summary>
    public float KnockbackTaken { get; init; } = 1f;
    public float StunTaken { get; init; } = 1f;

    /// <summary>Multiplier on the kit's skill cooldowns (below 1 = quicker).</summary>
    public float CooldownScale { get; init; } = 1f;

    /// <summary>The boss's own basic-attack magazine.</summary>
    public CharacterAmmoDef Ammo { get; init; }

    /// <summary>A wonder item the boss holds, or null. Reserved: no boss holds one yet.</summary>
    public string HeldItemDefId { get; init; }

    /// <summary>How often the boss's brain makes a decision — its reaction time.</summary>
    public float DecisionSeconds { get; init; } = 0.15f;
}

/// <summary>
/// Read-only registry of bosses (T20 §1). Pure C#. The numbers here are FIRST-PASS
/// placeholders: the owner postponed boss design ("maybe larger magazine, quicker CD, extra
/// holding item, or something else" — FOES.gdd §8.2, TBD #2), so each knob exists and sits
/// at a value that makes the fight playable, not balanced.
/// </summary>
public static class BossTable
{
    public const string PumpKingId = "boss_pumpking";

    /// <summary>BOSS PumpKing — lord of the Kingdom of Horror, met on his castle's balcony
    /// (FOES.gdd §8.3). Same head, same shell, same Soul Form as the protagonist.</summary>
    public static readonly BossDef PumpKing = new()
    {
        Id = PumpKingId,
        DisplayName = "BOSS PumpKing",
        ProtagonistId = "PumpKing",
        WorldAbbr = "KH",
        ScenePath = "res://Scenes/Bosses/BossPumpKing.tscn",
        BaseHp = 600f,           // protagonist: 200. The crab stand-in is 560 (70 × 8).
        DamageScale = 0.6f,      // a head that lands: 18 contact + 30 blast at foe level 1
        KnockbackTaken = 0.4f,
        StunTaken = 0.25f,
        CooldownScale = 1f,      // "quicker CD" knob — neutral until designed
        Ammo = new CharacterAmmoDef
        {
            CharacterId = PumpKingId,
            Capacity = 1,                 // "larger magazine" knob — he has one head
            ReloadSize = 1,
            ReloadDelaySec = 1.5f,
            RepeatReloadWhileMissing = false,
            BaseAttackIntervalSec = 0f,
            RespawnMode = AmmoRespawnMode.Full,
        },
        HeldItemDefId = null,    // "extra holding item" knob — none yet
        DecisionSeconds = 0.15f,
    };

    private static readonly BossDef[] Rows = { PumpKing };

    public static IReadOnlyList<BossDef> All => Rows;

    public static bool TryGet(string id, out BossDef def)
    {
        def = null;
        if (string.IsNullOrWhiteSpace(id)) return false;
        foreach (BossDef row in Rows)
        {
            if (string.Equals(row.Id, id, StringComparison.Ordinal))
            {
                def = row;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The boss a realm fields while its own protagonist has no boss kit — the owner's interim
    /// rule (v0.11.0): "use PumpKing Boss as the BOSS for every realm for now". It is what
    /// makes every realm's 4-1 a real boss fight to test against; each world gets its own row
    /// as its boss is built, and this then covers only the worlds still without one.
    /// </summary>
    public static BossDef StandIn => PumpKing;

    /// <summary>The boss whose home this world is, or null while that world's protagonist
    /// has no boss kit yet.</summary>
    public static BossDef HomeBossOf(string worldAbbr)
    {
        if (string.IsNullOrWhiteSpace(worldAbbr)) return null;
        string wanted = worldAbbr.Trim();
        foreach (BossDef row in Rows)
            if (string.Equals(row.WorldAbbr, wanted, StringComparison.OrdinalIgnoreCase)) return row;
        return null;
    }

    /// <summary>The boss that holds a world's LV4 node: the world's own, else
    /// <see cref="StandIn"/>. Null only for no world at all.</summary>
    public static BossDef ForWorld(string worldAbbr) =>
        string.IsNullOrWhiteSpace(worldAbbr) ? null : HomeBossOf(worldAbbr) ?? StandIn;

    /// <summary>Boot validation (T10 §5): every violation, not just the first.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var worlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BossDef row in Rows)
        {
            string name = string.IsNullOrWhiteSpace(row.Id) ? "(no id)" : row.Id;
            if (string.IsNullOrWhiteSpace(row.Id)) problems.Add("BossTable: a row has no id");
            else if (!ids.Add(row.Id)) problems.Add($"BossTable: id '{row.Id}' is used twice");
            if (string.IsNullOrWhiteSpace(row.WorldAbbr)) problems.Add($"BossTable {name}: no world");
            else if (!worlds.Add(row.WorldAbbr)) problems.Add($"BossTable {name}: world '{row.WorldAbbr}' already has a boss");
            if (string.IsNullOrWhiteSpace(row.ProtagonistId)) problems.Add($"BossTable {name}: no protagonist to recruit");
            if (string.IsNullOrWhiteSpace(row.ScenePath)) problems.Add($"BossTable {name}: no scene");
            if (string.IsNullOrWhiteSpace(row.DisplayName)) problems.Add($"BossTable {name}: no display name");
            if (!(row.BaseHp > 0f)) problems.Add($"BossTable {name}: BaseHp must be positive");
            if (!(row.DamageScale > 0f)) problems.Add($"BossTable {name}: DamageScale must be positive");
            if (row.KnockbackTaken < 0f || row.StunTaken < 0f) problems.Add($"BossTable {name}: poise shares cannot be negative");
            if (!(row.CooldownScale > 0f)) problems.Add($"BossTable {name}: CooldownScale must be positive");
            if (!(row.DecisionSeconds > 0f)) problems.Add($"BossTable {name}: DecisionSeconds must be positive");
            if (row.Ammo == null) problems.Add($"BossTable {name}: no magazine");
            else if (row.Ammo.Capacity < 1 || row.Ammo.ReloadSize < 1) problems.Add($"BossTable {name}: magazine capacity and reload size must be at least 1");
        }
        return problems;
    }
}

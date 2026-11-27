using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>
/// A realm's law (Docs/MapGDD.md §2.3, Docs/Zone6.gdd §2): one rule unique to a realm, in force
/// in every fight on its maps. It is pure data — orchestration turns it into source-keyed
/// modifiers on the bodies in the arena (<c>ArenaRulesApplier</c>, source <c>realm:&lt;code&gt;</c>),
/// so a kit never learns which realm it is in (T00).
///
/// A law applies to every fighter on both sides (protagonists and a Foe-side boss alike);
/// to ordinary foes only when <see cref="AppliesToFoes"/> says so. Zone 6 never applies the laws
/// of the worlds whose maps it borrows: it has rules of its own.
/// </summary>
public sealed class RealmLawDef
{
    /// <summary>The world whose realm this is (`MapGDD.md` §2 abbreviation).</summary>
    public string WorldAbbr { get; init; }
    /// <summary>Stable snake_case id.</summary>
    public string Id { get; init; }
    /// <summary>The realm's name in combat ("Wulin").</summary>
    public string RealmName { get; init; }
    /// <summary>The law's name ("Qinggong") and its Chinese name ("轻功").</summary>
    public string Name { get; init; }
    public string NameZh { get; init; }
    /// <summary>One plain line a player reads at the start of a fight.</summary>
    public string Summary { get; init; }

    /// <summary>Jumps added on top of each kit's own <c>MaxJumps</c>.</summary>
    public int ExtraJumps { get; init; }
    /// <summary>Share of every jump's height that is left (launch speed × √ of it).</summary>
    public float JumpHeightScale { get; init; } = 1f;
    /// <summary>Share of gravity that is left.</summary>
    public float GravityScale { get; init; } = 1f;
    /// <summary>Share of move speed that is left.</summary>
    public float MoveSpeedScale { get; init; } = 1f;
    /// <summary>Whether ordinary foes are bound by it too (only its gravity reaches a foe).</summary>
    public bool AppliesToFoes { get; init; }

    /// <summary>"Wulin · Qinggong (轻功) — one more jump, each 75% as high".</summary>
    public string Line => $"{RealmName} · {Name} ({NameZh}) — {Summary}";
}

/// <summary>
/// Read-only registry of realm laws (T20 §1). One row per world that has a law; a world with no
/// row has none yet (TBD — every realm is meant to get one). Numbers are first-pass, the
/// owner's own for Wulin.
/// </summary>
public static class RealmLawTable
{
    /// <summary>Wulin (Banboo Maze): light-body kung fu. The owner's brief: "all characters
    /// get one more jump, but each jump is only 0.75× as high".</summary>
    public static readonly RealmLawDef Qinggong = new()
    {
        WorldAbbr = "BM",
        Id = "qinggong",
        RealmName = "Wulin",
        Name = "Qinggong",
        NameZh = "轻功",
        Summary = "one more jump, each 75% as high",
        ExtraJumps = 1,
        JumpHeightScale = 0.75f,
        AppliesToFoes = false,
    };

    private static readonly RealmLawDef[] Rows = { Qinggong };

    public static IReadOnlyList<RealmLawDef> All => Rows;

    /// <summary>The law of a world's realm, or null when it has none.</summary>
    public static RealmLawDef ForWorld(string worldAbbr)
    {
        if (string.IsNullOrWhiteSpace(worldAbbr)) return null;
        string wanted = worldAbbr.Trim();
        foreach (RealmLawDef row in Rows)
            if (string.Equals(row.WorldAbbr, wanted, StringComparison.OrdinalIgnoreCase)) return row;
        return null;
    }

    /// <summary>Boot validation (T10 §5): every violation, not just the first.</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        var worlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (RealmLawDef row in Rows)
        {
            string name = string.IsNullOrWhiteSpace(row.Id) ? "(no id)" : row.Id;
            if (string.IsNullOrWhiteSpace(row.Id)) problems.Add("RealmLawTable: a row has no id");
            else if (!ids.Add(row.Id)) problems.Add($"RealmLawTable: id '{row.Id}' is used twice");
            if (string.IsNullOrWhiteSpace(row.WorldAbbr)) problems.Add($"RealmLawTable {name}: no world");
            else if (!worlds.Add(row.WorldAbbr)) problems.Add($"RealmLawTable {name}: world '{row.WorldAbbr}' already has a law");
            if (string.Equals(row.WorldAbbr, "XX", StringComparison.OrdinalIgnoreCase)) problems.Add($"RealmLawTable {name}: zone 6 has rules of its own, not a realm law");
            if (row.ExtraJumps < 0) problems.Add($"RealmLawTable {name}: extra jumps cannot be negative");
            if (!(row.JumpHeightScale > 0f) || !(row.GravityScale > 0f) || !(row.MoveSpeedScale > 0f))
                problems.Add($"RealmLawTable {name}: every scale must be positive");
            if (string.IsNullOrWhiteSpace(row.Summary)) problems.Add($"RealmLawTable {name}: no line for the player");
        }
        return problems;
    }
}

using System;
using System.Collections.Generic;
using Fableland.Data;

namespace Fableland.Run;

/// <summary>Which zone-6 fight an arena is: none, a mini-boss's (LV5) or the final boss's (LV6).</summary>
public enum Zone6Role
{
    None,
    MiniBoss,
    Final,
}

/// <summary>
/// The rules of a place (Docs/Zone6.gdd §7): what the realm, zone 6 and the run's seals do to
/// the bodies in one fight. Computed once, at node entry, by <see cref="RunState"/> into the
/// handshake (<see cref="AdventureContext.Rules"/>) — the arena never reads the map or the run
/// (T00) — and applied by the arena to every fighter, foe and projectile as source-keyed
/// modifiers (<c>ArenaRulesApplier</c>). Pure data.
/// </summary>
public sealed class ArenaRules
{
    /// <summary>No law, no zone 6, no seal: what a debug arena or an outer realm without a law gets.</summary>
    public static ArenaRules None => new();

    // ── The realm's law ─────────────────────────────────────────────────────────────
    public string LawWorld = "";
    public string LawId = "";
    /// <summary>"Wulin · Qinggong (轻功) — one more jump, each 75% as high"; "" without a law.</summary>
    public string LawLine = "";
    public int LawExtraJumps;
    public float LawJumpHeightScale = 1f;
    public float LawGravityScale = 1f;
    public float LawMoveSpeedScale = 1f;
    public bool LawAppliesToFoes;

    // ── Zone 6 ──────────────────────────────────────────────────────────────────────
    public string Zone6VariantId = "";
    public Zone6Role Zone6Role;
    /// <summary>The Void Frontier's gravity: on every fighter, foe and projectile in its arenas.</summary>
    public float Zone6Gravity = 1f;
    /// <summary>"The Void Frontier (虚无边境) · gravity ×0.64".</summary>
    public string Zone6Line = "";
    /// <summary>A zone-6 mini-boss fight: losing it ends the fight, not the run.</summary>
    public bool SurvivableDefeat => Zone6Role == Zone6Role.MiniBoss;

    // ── Avīci's seals on you (every protagonist) ────────────────────────────────────
    /// <summary>Stacks of each poison in the player's party (poison id → 1..cap): what the
    /// scales below are made of, and what holds each of Māra's realms longer (Mara.gdd §4.2).</summary>
    public Dictionary<string, int> SealStacks { get; private set; } = new(StringComparer.Ordinal);
    public float SealReloadScale = 1f;
    public float SealDamageTakenScale = 1f;
    public float SealCooldownScale = 1f;
    public float SealMoveSpeedScale = 1f;
    public float SealAttackIntervalScale = 1f;
    /// <summary>"Sealed: Hunger ×1 (reload time +30%), Burden ×2 (move speed −15%)".</summary>
    public string SealLine = "";

    // ── Māra's sealed powers (Avīci's final fight) ──────────────────────────────────
    /// <summary>The poisons whose power is sealed in Māra, in gate order: their realms of his
    /// wheel never come round (Mara.gdd §4).</summary>
    public List<string> MaraSealedPoisons { get; private set; } = new();
    /// <summary>The same, by the names of the powers ("Devour", "Wrath").</summary>
    public List<string> SealedBossAbilities { get; private set; } = new();
    public string FinalBossLine = "";

    public int SealStacksOf(string poisonId) =>
        poisonId != null && SealStacks.TryGetValue(poisonId, out int stacks) ? stacks : 0;

    public bool HasLaw => !string.IsNullOrEmpty(LawId);
    public bool InZone6 => Zone6Role != Zone6Role.None;
    public bool HasSeals => SealLine.Length > 0;

    /// <summary>Gravity a fighter or a foe bound by the law carries here, all sources together.</summary>
    public float FighterGravity => LawGravityScale * Zone6Gravity;

    /// <summary>The lines the HUD shows under the mission title, in order.</summary>
    public List<string> HudLines()
    {
        var lines = new List<string>();
        if (LawLine.Length > 0) lines.Add(LawLine);
        if (Zone6Line.Length > 0) lines.Add(Zone6Line);
        if (SealLine.Length > 0) lines.Add(SealLine);
        if (FinalBossLine.Length > 0) lines.Add(FinalBossLine);
        return lines;
    }

    /// <summary>
    /// The rules for an outer node of <paramref name="worldAbbr"/> (its realm's law, and any seal
    /// the run carries), or — with <paramref name="zone6"/> and a zone-6 role — for a zone-6
    /// fight. Zone 6 never applies the law of the world whose map it borrows.
    /// </summary>
    public static ArenaRules Build(string worldAbbr, Zone6State zone6, Zone6Role role)
    {
        var rules = new ArenaRules();
        if (role == Zone6Role.None)
        {
            RealmLawDef law = RealmLawTable.ForWorld(worldAbbr);
            if (law != null)
            {
                rules.LawWorld = law.WorldAbbr;
                rules.LawId = law.Id;
                rules.LawLine = law.Line;
                rules.LawExtraJumps = law.ExtraJumps;
                rules.LawJumpHeightScale = law.JumpHeightScale;
                rules.LawGravityScale = law.GravityScale;
                rules.LawMoveSpeedScale = law.MoveSpeedScale;
                rules.LawAppliesToFoes = law.AppliesToFoes;
            }
        }
        else if (zone6?.Variant != null)
        {
            rules.Zone6VariantId = zone6.Variant.Id;
            rules.Zone6Role = role;
            if (zone6.IsVoidFrontier) rules.Zone6Gravity = zone6.Gravity;
            rules.Zone6Line = zone6.IsVoidFrontier
                ? $"{zone6.Variant.Name} ({zone6.Variant.NameZh}) · gravity ×{zone6.Gravity:0.00}"
                : $"{zone6.Variant.Name} ({zone6.Variant.NameZh})";

            if (role == Zone6Role.Final && zone6.IsAvici)
            {
                foreach (Zone6PoisonDef poison in Zone6Table.Poisons)
                {
                    if (!zone6.MaraSealed.Contains(poison.Id)) continue;
                    rules.MaraSealedPoisons.Add(poison.Id);
                    rules.SealedBossAbilities.Add(poison.MaraAbility);
                }
                int sealedCount = rules.SealedBossAbilities.Count;
                rules.FinalBossLine = sealedCount == 0 ? "Māra holds all five realms of his wheel"
                    : sealedCount == Zone6Table.Poisons.Length ? "All five sealed in Māra: his wheel stands on the human realm"
                    : $"Sealed in Māra: {string.Join(", ", rules.SealedBossAbilities)} — his wheel skips their realms";
            }
        }

        // Seals are on you for the rest of the run, wherever you fight.
        if (zone6 != null)
            foreach (Zone6PoisonDef poison in Zone6Table.Poisons)
            {
                int k = zone6.SealStacks(poison.Id);
                if (k > 0) rules.SealStacks[poison.Id] = k;
            }
        rules.RecomputeSeals();
        return rules;
    }

    /// <summary>
    /// These rules with one more stack of a poison in the party, for the rest of this fight
    /// only — the Tempter's Offering (Mara.gdd §7.5). The run is not touched: the final fight
    /// ends it either way. A poison at its cap stays there. A new record: the handshake's is
    /// never changed.
    /// </summary>
    public ArenaRules WithFightSeal(string poisonId)
    {
        ArenaRules copy = Clone();
        if (Zone6Table.Poison(poisonId) != null)
            copy.SealStacks[poisonId] = Math.Min(Zone6Table.SealStackCap, copy.SealStacksOf(poisonId) + 1);
        copy.RecomputeSeals();
        return copy;
    }

    public ArenaRules Clone()
    {
        var copy = (ArenaRules)MemberwiseClone();
        copy.SealStacks = new Dictionary<string, int>(SealStacks, StringComparer.Ordinal);
        copy.MaraSealedPoisons = new List<string>(MaraSealedPoisons);
        copy.SealedBossAbilities = new List<string>(SealedBossAbilities);
        return copy;
    }

    /// <summary>The seals' scales and line, from the stacks.</summary>
    private void RecomputeSeals()
    {
        SealReloadScale = SealDamageTakenScale = SealCooldownScale = SealMoveSpeedScale = SealAttackIntervalScale = 1f;
        SealLine = "";
        var parts = new List<string>();
        foreach (Zone6PoisonDef poison in Zone6Table.Poisons)
        {
            int k = SealStacksOf(poison.Id);
            if (k <= 0) continue;
            SealReloadScale += poison.ReloadTimePerStack * k;
            SealDamageTakenScale += poison.DamageTakenPerStack * k;
            SealCooldownScale += poison.CooldownPerStack * k;
            SealMoveSpeedScale -= poison.MoveSpeedLossPerStack * k;
            SealAttackIntervalScale += poison.AttackIntervalPerStack * k;
            parts.Add($"{poison.SealName} ×{k}");
        }
        SealMoveSpeedScale = Math.Max(0.1f, SealMoveSpeedScale);
        if (parts.Count > 0) SealLine = "Sealed in you: " + string.Join(", ", parts);
    }
}

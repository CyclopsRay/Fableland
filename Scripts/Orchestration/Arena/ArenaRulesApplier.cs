using Fableland.Run;

/// <summary>
/// Puts the rules of a place (<see cref="ArenaRules"/>, Docs/Zone6.gdd §7) on the bodies in an
/// arena as source-keyed modifiers (golden rule 7). The arena calls it for every fighter and
/// foe that enters its tree — the opening bodies, a switch-in, a boss staged after the
/// countdown, a wave of adds — so nothing that joins a fight later escapes its rules, and a
/// kit never has to know where it is (T00). Applying the same rules twice changes nothing:
/// each source holds one value.
///
/// Who takes what:
/// • a realm's law — every fighter on both sides (protagonists and a Foe-side boss alike);
///   an ordinary foe only when the law says so, and then only its gravity;
/// • the Void Frontier's gravity — every fighter, every foe; a projectile inherits its
///   owner's share when it is thrown;
/// • Avīci's seals — Player-side fighters only (every protagonist: each switch-in is a body
///   entering the tree);
/// • Māra's sealed powers — the final boss only, after its row is applied
///   (<see cref="ApplyToFinalBoss"/>).
/// </summary>
public static class ArenaRulesApplier
{
    /// <summary>Source of the Void Frontier's gravity.</summary>
    public const string Zone6GravitySource = "zone6:gravity";
    /// <summary>Source of Avīci's seals on a protagonist.</summary>
    public const string SealSource = "zone6:seals";
    /// <summary>Source of what Māra lost to the seals.</summary>
    public const string MaraSource = "zone6:mara";

    /// <summary>A realm law's source: <c>realm:&lt;world code&gt;</c> (lower case).</summary>
    public static string LawSource(ArenaRules rules) => "realm:" + (rules?.LawWorld ?? "").ToLowerInvariant();

    public static void ApplyToFighter(CharacterController body, ArenaRules rules)
    {
        if (body == null || rules == null) return;
        if (rules.HasLaw)
        {
            string law = LawSource(rules);
            body.SetExtraJumps(law, rules.LawExtraJumps);
            body.SetJumpHeightScale(law, rules.LawJumpHeightScale);
            body.SetGravityScale(law, rules.LawGravityScale);
            body.SetMoveSpeedScale(law, rules.LawMoveSpeedScale);
        }
        body.SetGravityScale(Zone6GravitySource, rules.Zone6Gravity);

        if (body.Side != CombatSide.Player) return;
        // Seals: every protagonist, never the Foe side. Damage taken goes through the defense
        // pool, so a scale s is the defense that gives 100/(100+d) = s.
        body.SetReloadScale(SealSource, rules.SealReloadScale);
        body.SetCooldownScale(SealSource, rules.SealCooldownScale);
        body.SetMoveSpeedScale(SealSource, rules.SealMoveSpeedScale);
        body.SetAttackIntervalScale(SealSource, rules.SealAttackIntervalScale);
        if (rules.SealDamageTakenScale > 1.0001f || rules.SealDamageTakenScale < 0.9999f)
            body.SetDefenseSource(SealSource, DefenseForDamageTaken(rules.SealDamageTakenScale));
        else
            body.ClearDefenseSource(SealSource);
    }

    public static void ApplyToFoe(BaseFoe foe, ArenaRules rules)
    {
        if (foe == null || rules == null) return;
        float gravity = rules.Zone6Gravity * (rules.HasLaw && rules.LawAppliesToFoes ? rules.LawGravityScale : 1f);
        foe.SetGravityScale(Zone6GravitySource, gravity);
    }

    /// <summary>Māra's stand-in, once its row has set its HP and damage (Zone6.gdd §4.3): each
    /// power sealed in him costs a share of both. A no-op for any other fight.</summary>
    public static void ApplyToFinalBoss(CharacterController boss, ArenaRules rules)
    {
        if (boss == null || rules == null || rules.SealedBossAbilities.Count == 0) return;
        boss.SetFullHealth(boss.MaxHP * rules.FinalBossHpScale);
        boss.SetDamageDealtBonus(MaraSource, rules.FinalBossDamageBonus);
    }

    /// <summary>The defense that makes damage taken <paramref name="scale"/>× (DefenseMultiplier
    /// is 100/(100+defense)): 1.15 → −13.0.</summary>
    public static float DefenseForDamageTaken(float scale) => 100f / System.Math.Max(0.01f, scale) - 100f;
}

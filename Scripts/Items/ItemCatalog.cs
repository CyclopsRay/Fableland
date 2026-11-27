namespace Fableland.Items;

/// <summary>
/// Wonder-item registry. Definitions are pure data, validated through lookup at module
/// boundaries; mutable cooldowns and locations remain RunState-owned ItemInstances.
///
/// Pure data — NO Godot types here, matching <c>Scripts/Foes/FoeStats.cs</c> /
/// <c>Scripts/Missions/MissionTable.cs</c>, which live Godot-free in their own system folders.
/// </summary>
public static class ItemCatalog
{
    private const string IconRoot = "res://Assets/Sprites/Items/WonderItems/";

    public static readonly ItemDef[] Entries =
    {
        new("twisted_reality", "TwistedReality", ItemDomain.Hybrid, ItemTags.Possession, dayCooldownDays: 4,
            passiveDescription: "Possession: a terminal BOSS loss consumes it and restores the last completed day.",
            skillDescription: "Bridge of Eidolon — map only, 4-day cooldown. Build a bridge from a valid VOID-river city.",
            iconPath: IconRoot + "00_twisted_reality.png", displayOrder: 0),
        new("fanchens_heart", "FanChen's Heart", ItemDomain.Combat, secondCooldownSeconds: 30f,
            combatBehaviorId: ItemBehaviorId.FanChensHeart,
            passiveDescription: "Frozen lasts 0.2 s longer. While Frozen, heal 30% of post-mitigation direct damage received.",
            skillDescription: "Combat — grant yourself 60 Frozen stacks (30 s cooldown).",
            iconPath: IconRoot + "01_fanchens_heart.png", displayOrder: 1),
        new("yukais_rope", "Yukai's Rope", ItemDomain.Combat, secondCooldownSeconds: 30f,
            combatBehaviorId: ItemBehaviorId.YukaisRope,
            passiveDescription: "While airborne, gain 30 DEF.",
            skillDescription: "Combat — fire 20 m forward and pull to the first obstacle hit (30 s cooldown).",
            iconPath: IconRoot + "02_yukais_rope.png", displayOrder: 2),
        new("forgotten_kashaya", "The Forgotten Kashaya", ItemDomain.Combat, secondCooldownSeconds: 15f,
            combatBehaviorId: ItemBehaviorId.ForgottenKashaya,
            passiveDescription: "Melee hits deal an additional 50% of their post-mitigation damage.",
            skillDescription: "Combat — knock back enemies within 4 m and deal 10 damage (15 s cooldown).",
            iconPath: IconRoot + "03_bloodstained_kashaya.png", displayOrder: 3),
        new("pome_bravery", "Pome's Bravery", ItemDomain.Hybrid, ItemTags.Convertible,
            combatBehaviorId: ItemBehaviorId.PomesBravery, conversionTargetDefId: "pome_seed",
            passiveDescription: "OnFire lasts 0.2 s longer. While OnFire, heal for 5% of damage dealt.",
            skillDescription: "Single use, combat — gain 60 OnFire stacks, then become Pome's Seed.",
            iconPath: IconRoot + "04_pomos_bravery.png", displayOrder: 4),
        new("pome_seed", "Pome's Seed", ItemDomain.Exploration, ItemTags.Plantable,
            combatBehaviorId: ItemBehaviorId.PomesSeed,
            passiveDescription: "OnFire lasts 0.2 s longer. While OnFire, heal for 5% of damage dealt.",
            skillDescription: "No active skill. Plant it at a Transportation Hub: two Pome's Bravery after 7 days.",
            iconPath: IconRoot + "05_pomos_seed.png", displayOrder: 5),
        new("the_void", "THE VOID", ItemDomain.Hybrid, ItemTags.Possession | ItemTags.Eternal, dayCooldownDays: 2,
            combatBehaviorId: ItemBehaviorId.TheVoid,
            passiveDescription: "Eternal, Possession: every 4 days a VOID phantom rises on the map; each node you challenge, every phantom takes a road toward you.",
            skillDescription: "Combat — every foe loses its sight for 10 seconds (2-day cooldown).",
            iconPath: IconRoot + "11_the_void.png", displayOrder: 20),
        new("dd", "DD", ItemDomain.Hybrid, dayCooldownDays: 2, combatBehaviorId: ItemBehaviorId.Dd,
            passiveDescription: "Picking up a wonder core heals 2% maximum health.",
            skillDescription: "Combat, 2-day cooldown — release DD: it crawls along the ground devouring each target it touches for 200 damage and turns at walls. Walk into it to reclaim it; leave the fight without it and it is set free.",
            iconPath: IconRoot + "10_dd.png", displayOrder: 21),
        new("blessing_of_shieldon", "Blessing of Shieldon", combatBehaviorId: ItemBehaviorId.Shieldon,
            passiveDescription: "Increase movement speed by 10%.",
            skillDescription: "Single use, at a Transportation Hub — enchant Possession onto another wonder item: its passive then works for the whole team, even from the backpack.",
            displayOrder: 30),
        new("pixolotls_feather", "Pixolotl's Feather", ItemDomain.Hybrid, ItemTags.Convertible,
            combatBehaviorId: ItemBehaviorId.PixolotlsFeather, conversionTargetDefId: "pixolotls_wish",
            passiveDescription: "Reduce gravity by 20%.",
            skillDescription: "Single use, on the map — return to this morning: where you stood, everyone's HP and your stamina (the day count and anything won stay). Then it becomes Pixolotl's Wish.",
            iconPath: IconRoot + "06_pixolotls_feather.png", displayOrder: 31),
        new("pixolotls_wish", "Pixolotl's Wish", ItemDomain.Exploration, ItemTags.Plantable,
            combatBehaviorId: ItemBehaviorId.PixolotlsWish,
            passiveDescription: "Reduce gravity by 20%.", skillDescription: "No active skill. Plant it at a Transportation Hub: two Pixolotl's Feathers after 7 days.",
            iconPath: IconRoot + "07_pixolotls_wish.png", displayOrder: 32),
        new("qiongfeng_emperors_claw", "QiongFeng Emperor's Claw", ItemDomain.Exploration,
            passiveDescription: "Held in the team: when a realm's LV4 capital falls, loot every combat node of it you never conquered, crack the realm down and be thrown to a hub of another realm; then the claw faints.",
            skillDescription: "No active skill.", iconPath: IconRoot + "08_loongs_claw.png", displayOrder: 33),
        new("weird_mushroom", "A Weird Mushroom", ItemDomain.Hybrid, ItemTags.Perishable | ItemTags.Convertible,
            combatBehaviorId: ItemBehaviorId.WeirdMushroom, conversionTargetDefId: "rotten_mushroom",
            passiveDescription: "Perishable: rots into A Rotten Mushroom after 2 days if not eaten.",
            skillDescription: "Single use, combat — eat it: +20% max HP for good, and something happens to you tomorrow.",
            iconPath: IconRoot + "09_weird_mushroom.png", displayOrder: 34),
        new("rotten_mushroom", "A Rotten Mushroom", ItemDomain.Exploration, ItemTags.Plantable,
            passiveDescription: "None.", skillDescription: "No active skill. Plant it at a Transportation Hub: four Weird Mushrooms after 4 days.",
            displayOrder: 35),
    };

    /// <summary>The catalog's DisplayName for an id, or the id itself as a fallback (never
    /// throws, never null — safe to call with an unknown or absent id).</summary>
    public static string DisplayName(string id)
    {
        if (id == null) return null;
        foreach (var e in Entries)
            if (e.Id == id) return e.DisplayName;
        return id;
    }

    /// <summary>Resolve a definition without throwing on corrupted/removed save content.</summary>
    public static bool TryGet(string id, out ItemDef definition)
    {
        definition = null;
        if (id == null) return false;
        foreach (ItemDef entry in Entries)
            if (entry.Id == id) { definition = entry; return true; }
        return false;
    }

    /// <summary>True if an entry with this Id exists in the catalog.</summary>
    public static bool Contains(string id)
    {
        if (id == null) return false;
        foreach (var e in Entries)
            if (e.Id == id) return true;
        return false;
    }

    /// <summary>Inventory tooltip text is catalog-owned so every icon surface gives the same
    /// authored name/passive/skill label beside the cursor.</summary>
    public static string Tooltip(ItemDef definition)
    {
        if (definition == null) return "Unknown wonder item.";
        return $"{definition.DisplayName}\n\nPassive: {definition.PassiveDescription}\n\nSkill: {definition.SkillDescription}";
    }

    public static int CompareForDisplay(ItemDef a, ItemDef b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        int order = a.DisplayOrder.CompareTo(b.DisplayOrder);
        return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
    }
}

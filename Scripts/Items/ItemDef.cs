using System;

namespace Fableland.Items;

/// <summary>Where an item's authored behaviour primarily runs. The domain is presentation and
/// validation data; individual cooldown axes remain explicit definition fields.</summary>
public enum ItemDomain { Combat, Exploration, Hybrid }

/// <summary>Closed, validated feature set from ITEMS.gdd §4.</summary>
[Flags]
public enum ItemTags { None = 0, Perishable = 1, Convertible = 2, Plantable = 4, Possession = 8, Eternal = 16 }

/// <summary>Small behavior identifiers bound by <see cref="ItemRuntime"/>. Content uses a data
/// id, not a switch in RunState; bespoke code stays beside the item runtime.</summary>
public static class ItemBehaviorId
{
    public const string None = "";
    public const string FanChensHeart = "fanchens_heart";
    public const string YukaisRope = "yukais_rope";
    public const string ForgottenKashaya = "forgotten_kashaya";
    public const string PomesBravery = "pomes_bravery";
    public const string PomesSeed = "pomes_seed";
}

/// <summary>Godot-free catalog definition. Mutable cooldowns and future rolled stats live on
/// ItemInstance, never here.</summary>
public sealed class ItemDef
{
    public string Id { get; }
    public string DisplayName { get; }
    public ItemDomain Domain { get; }
    public ItemTags Tags { get; }
    public int DayCooldownDays { get; }
    public float SecondCooldownSeconds { get; }
    public string CombatBehaviorId { get; }
    public string ConversionTargetDefId { get; }
    public string PassiveDescription { get; }
    public string SkillDescription { get; }
    /// <summary>Project-relative texture path consumed only by presentation code.</summary>
    public string IconPath { get; }
    /// <summary>Stable inventory presentation order. Lower values appear first.</summary>
    public int DisplayOrder { get; }

    public ItemDef(string id, string displayName, ItemDomain domain = ItemDomain.Exploration,
        ItemTags tags = ItemTags.None, int dayCooldownDays = 0, float secondCooldownSeconds = 0f,
        string combatBehaviorId = ItemBehaviorId.None, string conversionTargetDefId = null,
        string passiveDescription = "None.", string skillDescription = "No active skill.",
        string iconPath = null, int displayOrder = 100)
    {
        Id = id;
        DisplayName = displayName;
        Domain = domain;
        Tags = tags;
        DayCooldownDays = dayCooldownDays;
        SecondCooldownSeconds = secondCooldownSeconds;
        CombatBehaviorId = combatBehaviorId;
        ConversionTargetDefId = conversionTargetDefId;
        PassiveDescription = passiveDescription;
        SkillDescription = skillDescription;
        IconPath = iconPath;
        DisplayOrder = displayOrder;
    }
}

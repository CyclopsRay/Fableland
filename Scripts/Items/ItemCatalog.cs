namespace Fableland.Items;

/// <summary>
/// v0.6.0 STUB catalog of wonder items: id + display name ONLY. This is deliberately the
/// smallest possible slice of the full wonder-item system — passive/skill fields, day-based
/// and second-based cooldowns, perish/convert/plant lifecycle, Possession/Eternal tag
/// semantics, and RolledStats all remain future work; see <c>Docs/ITEMS.gdd</c> and
/// <c>Docs/Tech/T30-FEATURE-BLUEPRINTS.md</c> §4 for the real design this will grow into.
///
/// Pure data — NO Godot types here, matching <c>Scripts/Foes/FoeStats.cs</c> /
/// <c>Scripts/Missions/MissionTable.cs</c>, which live Godot-free in their own system folders.
/// </summary>
public static class ItemCatalog
{
    public static readonly (string Id, string DisplayName)[] Entries =
    {
        ("blessing_of_shieldon", "Blessing of Shieldon"),
        ("the_void", "THE VOID"),
        ("dd", "DD"),
        ("pome_bravery", "Pome's Bravery"),
        ("pome_seed", "Pome's Seed"),
        ("fanchens_heart", "FanChen's Heart"),
        ("yukais_rope", "Yukai's Rope"),
        ("pixolotls_feather", "Pixolotl's Feather"),
        ("pixolotls_wish", "Pixolotl's Wish"),
        ("forgotten_kashaya", "Forgotten Kashaya"),
        ("qiongfeng_emperors_claw", "QiongFeng Emperor's Claw"),
        ("weird_mushroom", "A Weird Mushroom"),
        ("rotten_mushroom", "A Rotten Mushroom"),
        ("twisted_reality", "TwistedReality"),
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

    /// <summary>True if an entry with this Id exists in the catalog.</summary>
    public static bool Contains(string id)
    {
        if (id == null) return false;
        foreach (var e in Entries)
            if (e.Id == id) return true;
        return false;
    }
}

using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>What a boss row is for. A realm boss holds a world's 4-1 and recruits its
/// protagonist; a zone-6 boss (Docs/Zone6.gdd) is a character of the run's last stage — a
/// mini-boss on an LV5 node or the final boss on the core — and recruits nobody.</summary>
public enum BossRole
{
    Realm,
    Zone6Mini,
    Zone6Final,
}

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

    /// <summary>A boss's ult ring (`Realms.gdd` §13, the owner, 2026-11-28): it fills this much
    /// every second of the fight, and this much per point of damage the boss deals or takes.
    /// Players fill theirs more slowly (their kit's own rates, nothing over time).</summary>
    public float UltRingPerSecond { get; init; } = 10f;
    public float UltRingPerDamage { get; init; } = 5f;

    /// <summary>The boss's own basic-attack magazine.</summary>
    public CharacterAmmoDef Ammo { get; init; }

    /// <summary>A wonder item the boss holds, or null. Reserved: no boss holds one yet.</summary>
    public string HeldItemDefId { get; init; }

    /// <summary>How often the boss's brain makes a decision — its reaction time.</summary>
    public float DecisionSeconds { get; init; } = 0.15f;

    /// <summary>Realm boss, or a zone-6 mini-boss / final boss (Docs/Zone6.gdd).</summary>
    public BossRole Role { get; init; } = BossRole.Realm;

    /// <summary>The <c>Zone6Table</c> variant a zone-6 boss belongs to; empty for a realm boss.</summary>
    public string Zone6Variant { get; init; } = "";

    /// <summary>Whether beating it puts <see cref="ProtagonistId"/> on the roster. Only a realm
    /// boss recruits; a zone-6 boss fields another kit as its stand-in and recruits nobody.</summary>
    public bool Recruits => Role == BossRole.Realm && !string.IsNullOrWhiteSpace(ProtagonistId);

    /// <summary>One line on who it is (zone 6: the look and skills its own kit is built from —
    /// Docs/Zone6.gdd §4–§5 is the authority).</summary>
    public string Concept { get; init; } = "";

    /// <summary>A field mechanic the fight runs whatever kit the boss fields (Docs/Zone6.gdd):
    /// "styx_tide" — the Unwritten's floor-sweeping wave; "wheel" — Māra's Wheel of Becoming
    /// (Docs/Mara.gdd §4). Empty for none.</summary>
    public string Signature { get; init; } = "";
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

    // ── Zone 6 (Docs/Zone6.gdd) ───────────────────────────────────────────────────────────
    // Every zone-6 boss fields the stand-in kit — BOSS PumpKing's scene — until its own kit is
    // built, the same interim rule every realm's boss follows (v0.11.0). The concept line is
    // what its kit is to be. Numbers are first-pass: a mini-boss is a realm boss's bulk at its
    // zone-6 foe level (7), the final bosses a half again at level 8.

    private static BossDef Zone6Boss(string id, string name, BossRole role, string variant, string concept,
        float baseHp = 600f, float damageScale = 0.6f, string signature = "") => new()
    {
        Id = id,
        DisplayName = name,
        ProtagonistId = "",
        WorldAbbr = "XX",
        ScenePath = PumpKing.ScenePath,
        BaseHp = baseHp,
        DamageScale = damageScale,
        KnockbackTaken = PumpKing.KnockbackTaken,
        StunTaken = PumpKing.StunTaken,
        CooldownScale = 1f,
        Ammo = new CharacterAmmoDef
        {
            CharacterId = id,
            Capacity = PumpKing.Ammo.Capacity,
            ReloadSize = PumpKing.Ammo.ReloadSize,
            ReloadDelaySec = PumpKing.Ammo.ReloadDelaySec,
            RepeatReloadWhileMissing = PumpKing.Ammo.RepeatReloadWhileMissing,
            BaseAttackIntervalSec = PumpKing.Ammo.BaseAttackIntervalSec,
            RespawnMode = PumpKing.Ammo.RespawnMode,
        },
        DecisionSeconds = PumpKing.DecisionSeconds,
        Role = role,
        Zone6Variant = variant,
        Concept = concept,
        Signature = signature,
    };

    /// <summary>The same row on a kit of its own: its scene and its magazine. Its damage share
    /// and bulk stay the row's, so a kit's numbers (Zone6.gdd) scale like every boss's: × the
    /// row's share × the node's foe level.</summary>
    private static BossDef WithKit(BossDef row, string scenePath, CharacterAmmoDef ammo) => new()
    {
        Id = row.Id, DisplayName = row.DisplayName, ProtagonistId = row.ProtagonistId, WorldAbbr = row.WorldAbbr,
        ScenePath = scenePath, BaseHp = row.BaseHp, DamageScale = row.DamageScale,
        KnockbackTaken = row.KnockbackTaken, StunTaken = row.StunTaken, CooldownScale = row.CooldownScale,
        Ammo = ammo, DecisionSeconds = row.DecisionSeconds, Role = row.Role, Zone6Variant = row.Zone6Variant,
        Concept = row.Concept, Signature = row.Signature,
    };

    private const string VF = "void_frontier";
    private const string AV = "avici";

    // The first two with kits of their own (v0.12.1): their own scene, their own magazine.
    public static readonly BossDef Ferryman = WithKit(Zone6Boss("z6_ferryman", "The Ferryman (渡魂人)", BossRole.Zone6Mini, VF,
        "A hooded giant in a skiff: amber lantern, oar, obols. Oar Sweep, Obol Toss (the toll drains ult charge), Undertow, Skiff Ram."),
        "res://Scenes/Bosses/Ferryman.tscn",
        new CharacterAmmoDef { CharacterId = "z6_ferryman", Capacity = 1, ReloadSize = 1, ReloadDelaySec = 1.4f, RepeatReloadWhileMissing = false, BaseAttackIntervalSec = 0f, RespawnMode = AmmoRespawnMode.Full });
    public static readonly BossDef Weigher = Zone6Boss("z6_weigher", "The Weigher of Hearts (称心官)", BossRole.Zone6Mini, VF,
        "A jackal-headed judge, gold collar, a balance. Feather Fan, Judgement (tilting pan platforms; the low pan sinks into a hazard), Devourer's Jaws.");
    public static readonly BossDef MengPo = Zone6Boss("z6_meng_po", "Meng Po of the Naihe Bridge (孟婆)", BossRole.Zone6Mini, VF,
        "A small old woman: iron ladle, cauldron, red lantern, bowls. Soup Lob (Forget: your next skill press within 4 s fails), Ladle Sweep, Bridge Mist; one long bridge.");
    public static readonly BossDef Xolotl = Zone6Boss("z6_xolotl", "Xolotl, the Soul-Dog of Mictlan (冥犬修洛特)", BossRole.Zone6Mini, VF,
        "A skeletal dog-god on backward feet, jade collar, lightning. Pounce, Carry (a 1.5 s drag), Lightning Howl.");
    public static readonly BossDef StingyJack = Zone6Boss("z6_stingy_jack", "Stingy Jack, the Lantern Wanderer (守灯杰克)", BossRole.Zone6Mini, VF,
        "A lanky wanderer with an ember in a carved turnip. Ember Toss (sets OnFire), Lantern Step, Lights Out (3 s of dark).");
    public static readonly BossDef Unwritten = Zone6Boss("z6_unwritten", "The Unwritten (无名者)", BossRole.Zone6Final, VF,
        "A faceless figure of torn pages and erased ink, a hole for a face. Styx Tide (jump the wave or be carried), Rising Tide, Borrowed Shapes, Blank Page.",
        baseHp: 900f, signature: "styx_tide");

    public static readonly BossDef Preta = Zone6Boss("z6_preta", "Preta, the Hungry Ghost (饿鬼)", BossRole.Zone6Mini, AV,
        "贪 Greed. A grey-green ghost, a huge belly, a needle throat. Gorge (inhales; swallowed projectiles heal it), Spew, Hungry Grasp.");
    public static readonly BossDef Asura = WithKit(Zone6Boss("z6_asura", "Asura of Wrath (修罗)", BossRole.Zone6Mini, AV,
        "嗔 Anger. Three faces, six arms, tiger skin, a flame aureole. Six-Arm Flurry, Wrath Fire, Vajra Throw, Rising Wrath."),
        "res://Scenes/Bosses/AsuraWrath.tscn",
        new CharacterAmmoDef { CharacterId = "z6_asura", Capacity = 1, ReloadSize = 1, ReloadDelaySec = 0.2f, RepeatReloadWhileMissing = false, BaseAttackIntervalSec = 1.1f, RespawnMode = AmmoRespawnMode.Full });
    public static readonly BossDef Boar = Zone6Boss("z6_boar", "The Boar of Delusion (痴猪)", BossRole.Zone6Mini, AV,
        "痴 Delusion. A black boar with closed eyes. Blind Charge (stuns itself on a wall), Mirror Herd, Mire (left and right swap).");
    public static readonly BossDef FallenDeva = Zone6Boss("z6_fallen_deva", "The Fallen Deva (衰天)", BossRole.Zone6Mini, AV,
        "慢 Pride. A god in the five decays. Heavy Halo, Wilting Garland, Pride's Armour (poise, and a phase that takes 70% less damage).");
    public static readonly BossDef OxHorse = Zone6Boss("z6_ox_horse", "Ox-Head and Horse-Face (牛头马面)", BossRole.Zone6Mini, AV,
        "疑 Doubt. Two wardens. Two Minds (only the real one takes damage; they swap), Chain Hook, Trident Feint.");
    // Avīci's dark leader on his own kit (v0.12.3): the Wheel of Becoming is his signature — it
    // turns whatever kit stands in — and each warden you beat seals one realm of it (Mara.gdd).
    public static readonly BossDef Mara = WithKit(Zone6Boss("z6_mara", "Māra Pāpīyas (天魔波旬)", BossRole.Zone6Final, AV,
        "Lord of the Wheel of Life: black-blue, three eyes, a crown of five skulls; a colossus behind the arena holds the wheel. The realm under his fangs lends him its poison's power — Iron Throne, Wrath, Devour, Doubt, Delusion; only the human realm opens him. Claw Rake, Noose of Craving, Hand of the Wheel.",
        baseHp: 900f, signature: WheelTable.Signature),
        "res://Scenes/Bosses/Mara.tscn",
        new CharacterAmmoDef { CharacterId = "z6_mara", Capacity = 1, ReloadSize = 1, ReloadDelaySec = 0.2f, RepeatReloadWhileMissing = false, BaseAttackIntervalSec = 1.3f, RespawnMode = AmmoRespawnMode.Full });

    private static readonly BossDef[] Rows =
    {
        PumpKing,
        Ferryman, Weigher, MengPo, Xolotl, StingyJack, Unwritten,
        Preta, Asura, Boar, FallenDeva, OxHorse, Mara,
    };

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
    /// has no boss kit yet. Realm bosses only: zone 6's characters hold no world.</summary>
    public static BossDef HomeBossOf(string worldAbbr)
    {
        if (string.IsNullOrWhiteSpace(worldAbbr)) return null;
        string wanted = worldAbbr.Trim();
        foreach (BossDef row in Rows)
            if (row.Role == BossRole.Realm && string.Equals(row.WorldAbbr, wanted, StringComparison.OrdinalIgnoreCase)) return row;
        return null;
    }

    /// <summary>The realm bosses — the rows that hold a world's 4-1.</summary>
    public static List<BossDef> RealmBosses() => new List<BossDef>(Rows).FindAll(row => row.Role == BossRole.Realm);

    /// <summary>The zone-6 bosses, mini-bosses before final bosses, in table order.</summary>
    public static List<BossDef> Zone6Bosses() => new List<BossDef>(Rows).FindAll(row => row.Role != BossRole.Realm);

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
            if (row.Role == BossRole.Realm)
            {
                if (string.IsNullOrWhiteSpace(row.WorldAbbr)) problems.Add($"BossTable {name}: no world");
                else if (!worlds.Add(row.WorldAbbr)) problems.Add($"BossTable {name}: world '{row.WorldAbbr}' already has a boss");
                if (string.IsNullOrWhiteSpace(row.ProtagonistId)) problems.Add($"BossTable {name}: no protagonist to recruit");
            }
            else
            {
                // A zone-6 boss holds no world and recruits nobody: its kit is a stand-in.
                if (!string.Equals(row.WorldAbbr, "XX", StringComparison.Ordinal)) problems.Add($"BossTable {name}: a zone-6 boss belongs to zone 6 (XX), not '{row.WorldAbbr}'");
                if (!string.IsNullOrWhiteSpace(row.ProtagonistId)) problems.Add($"BossTable {name}: a zone-6 boss recruits nobody");
                if (string.IsNullOrWhiteSpace(row.Zone6Variant)) problems.Add($"BossTable {name}: names no zone-6 variant");
            }
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

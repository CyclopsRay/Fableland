namespace Fableland.Vfx;

using System.Collections.Generic;
using Godot;
using Fableland.Data;

/// <summary>
/// The VFX director's table (Docs/VFX.gdd §4–§6): which medium an owner works in, and what
/// every cue of the vocabulary (Docs/Feedback.gdd §6) looks like. PRESENTATION data.
///
/// Adding a world: a <see cref="VfxStyle"/> in <see cref="Styles"/> and a line in
/// <see cref="Looks"/>. Adding a character: a look (a medium and three colours) and rows for
/// the cues its kit raises — until then every cue it raises resolves owner → class → "any"
/// and is drawn in its home medium's generic form.
/// </summary>
public static class VfxTable
{
    public const string DefaultStyle = "watercolour";

    /// <summary>One style per medium. Order is the gallery's column order.</summary>
    public static readonly VfxStyle[] Styles =
    {
        new WatercolourStyle(), new PosterStyle(), new InkStyle(), new RealismStyle(), new LinocutStyle(),
        new GongbiStyle(), new ScratchboardStyle(), new PixelStyle(), new EnamelStyle(), new VoidStyle(),
    };

    private static readonly Dictionary<string, VfxStyle> StyleById = BuildStyleIndex();

    private static Dictionary<string, VfxStyle> BuildStyleIndex()
    {
        var index = new Dictionary<string, VfxStyle>();
        foreach (VfxStyle style in Styles) index[style.Id] = style;
        return index;
    }

    public static VfxStyle Style(string id) =>
        id != null && StyleById.TryGetValue(id, out VfxStyle style) ? style : null;

    // ── who works in which medium ────────────────────────────────────────────────
    private static Color C(string hex) => new(hex);
    private static VfxLook Look(string style, string main, string alt, string accent, bool provisional = false) =>
        new() { Style = style, HasColours = true, Main = C(main), Alt = C(alt), Accent = C(accent), Provisional = provisional };
    private static VfxLook Realm(string style) => new() { Style = style };

    /// <summary>Owner → look. A content id carries its home world's medium into every fight;
    /// a world code is the medium of that world's arenas.</summary>
    public static readonly Dictionary<string, VfxLook> Looks = new()
    {
        // Protagonists — their home world's medium, and their own three colours in it.
        ["pomegraknight"] = Look("watercolour", "cf3f5c", "8e1f3c", "fff1d2"),              // ruby wash, deep rim, cream
        ["cleopastar"] = Look("poster", "fbe08a", "d9a441", "35b3a8", provisional: true),   // gold on gold, teal
        ["sifu_pangda"] = Look("ink", "14171a", "565e63", "33574a", provisional: true),     // burnt ink, heavy ink, ink green
        ["pumpking"] = Look("scratchboard", "ffd23f", "fff1a8", "9dbeff"),                  // yellow gives light; moon blue
        ["pixolotl"] = Look("pixel", "ff8fcf", "d9589f", "3df6ff"),                         // her pink, neon cyan
        // Foes — VK natives.
        ["crab"] = Look("watercolour", "ec7a3c", "a8431f", "ffe9c9"),
        ["seagull"] = Look("watercolour", "f1ece0", "9aa4ad", "fffbf0"),
        ["boss_crab"] = Look("watercolour", "d9503a", "7e2218", "ffe2b8"),
        // Worlds — the arena's medium (the tag of flow/arena.enter).
        ["VK"] = Realm("watercolour"),
        ["SL"] = Realm("poster"),
        ["BM"] = Realm("ink"),
        ["HC"] = Realm("realism"),
        ["TD"] = Realm("linocut"),
        ["PL"] = Realm("gongbi"),
        ["KH"] = Realm("scratchboard"),
        ["TF"] = Realm("pixel"),
        ["CC"] = Realm("enamel"),
        ["VOID"] = Realm("void"),
        ["Z6"] = Realm("void"),
    };

    public static VfxLook LookOf(string owner) =>
        owner != null && Looks.TryGetValue(owner, out VfxLook look) ? look : null;

    /// <summary>The medium of the arena a world code names; the default for none or unknown.</summary>
    public static VfxStyle ArenaStyle(string world) => Style(LookOf(world)?.Style) ?? Style(DefaultStyle);

    // ── numbers ──────────────────────────────────────────────────────────────────
    /// <summary>The contact mark's radius in px by hit weight (index = (int)HitWeight). A
    /// fighter is 64 px tall: a light hit's mark is a fifth of that across and a massive
    /// one about two thirds — seen in the first captures, twice these sizes buried whoever
    /// was hit (Docs/VFX.gdd §11).</summary>
    public static readonly float[] WeightSize = { 0f, 4f, 7f, 11f, 16f, 23f };
    /// <summary>0 graze … 1 massive.</summary>
    public static float Power(HitWeight weight) => Mathf.Clamp(((int)weight - 1) / 4f, 0f, 1f);

    /// <summary>Hits by the player's side within this long of each other are one combo.</summary>
    public const float ComboWindow = 1.4f;
    /// <summary>Every this-many hits of a combo is one step of escalation, up to three.</summary>
    public const int ComboStep = 3;
    public const float ComboScalePerStep = 0.07f;
    public const float ComboCountPerStep = 0.18f;
    /// <summary>What a hit and any other cue add to combat intensity (0–1), and how fast it cools.</summary>
    public const float IntensityPerHit = 0.07f;
    public const float IntensityPerCue = 0.012f;
    public const float IntensityHalfLife = 1.6f;
    /// <summary>Above this intensity the fight is busy: decoration thins so it stays readable.</summary>
    public const float BusyIntensity = 0.70f;
    public const float BusyCount = 0.6f;
    public const float BusyLife = 0.85f;
    /// <summary>How large every realm's ambient motes are drawn, as a multiple of their recipe.</summary>
    public const float AmbientScale = 1.6f;
    /// <summary>A hit this heavy (or a kill) also throws pieces off the victim.</summary>
    public const HitWeight ReactFrom = HitWeight.Medium;

    /// <summary>The index a style is told for each status loop (<see cref="VfxStyle"/>'s aura).</summary>
    public static int StatusIndex(string action) => action switch
    {
        "status.burning" => 0,
        "status.on_fire" => 1,
        "status.frozen" => 2,
        "status.trapped" => 3,
        "status.stunned" => 4,
        _ => 0,
    };

    // ── shared modifiers ─────────────────────────────────────────────────────────
    private static readonly VfxModifier WhenBurningTag = new() { Tag = "burning", Element = VfxElement.Fire, Flags = VfxFlags.Burning };
    private static readonly VfxModifier WhenUnderWater = new() { State = FeedbackState.Submerged, Flags = VfxFlags.Submerged };
    private static readonly VfxModifier WhenKilled = new() { Killed = true, Count = 1.5f, Scale = 1.15f };
    private static readonly VfxModifier WhenCombo = new() { MinCombo = 4, Scale = 1.06f };
    private static readonly VfxModifier WhenBossEnraged = new() { MinBossPhase = 2, Scale = 1.12f, Count = 1.25f };
    private static readonly VfxModifier WhenVictimFrozen = new() { State = FeedbackState.Frozen, Element = VfxElement.Ice };

    private static readonly VfxDef FireLick = new()
    {
        Id = "fire_lick", Archetype = VfxArchetype.Sting, Element = VfxElement.Fire, OwnerColours = false,
        SizeFrom = VfxSizeFrom.Weight, Scale = 1.2f, Priority = VfxPriority.Cosmetic,
    };
    private static readonly VfxModifier WhenAttackerBurns = new() { State = FeedbackState.Burning, Add = FireLick };

    private static readonly VfxDef GroundDust = new()
    {
        Id = "contact_dust", Archetype = VfxArchetype.DustPuff, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Dust,
        OwnerColours = false, Size = 12f, Priority = VfxPriority.Cosmetic,
    };

    /// <summary>What flies off a body that was hit hard, in that body's own medium.</summary>
    public static readonly VfxDef React = new()
    {
        Id = "hit_react", Archetype = VfxArchetype.HitReact, StyleFrom = VfxStyleFrom.Victim,
        SizeFrom = VfxSizeFrom.Weight, Scale = 0.9f, Priority = VfxPriority.Cosmetic,
        Modifiers = new[] { WhenVictimFrozen },
    };

    // ── hits: (attack owner, attack action), then the owner's class, then "any" ──
    private static VfxDef Hit(string id, float scale = 1f, VfxElement element = VfxElement.Impact, bool ownerColours = true,
                              VfxDef[] layers = null, int flourishOneIn = 20) => new()
    {
        Id = id, Archetype = VfxArchetype.HitSpark, SizeFrom = VfxSizeFrom.Weight, Scale = scale, Element = element,
        OwnerColours = ownerColours, Variants = 3, RotJitterDeg = 14f, ScaleJitter = 0.12f, FlourishOneIn = flourishOneIn,
        Priority = VfxPriority.Confirm, Layers = layers,
        Modifiers = new[] { WhenKilled, WhenCombo, WhenBossEnraged, WhenAttackerBurns },
    };

    public static readonly CueTable<VfxDef> Hits = new CueTable<VfxDef>()
        // Pomegraknight — juice.
        .Add("pomegraknight", "ba", Hit("pome_slash_hit"))
        .Add("pomegraknight", "s1.tornado", Hit("pome_tornado_hit", 0.9f, VfxElement.Fire, ownerColours: false, flourishOneIn: 0))
        .Add("pomegraknight", "s2.seed", Hit("pome_seed_hit", 0.85f))
        // PumpKing — the head, and the roster's heaviest beat.
        .Add("pumpking", "ba.head", Hit("pumpking_head_hit"))
        .Add("pumpking", "ba.explode", Hit("pumpking_blast_hit", 1.2f))
        // Foes.
        .Add(CueOwners.Any, "contact", Hit("contact_hit", 0.9f, layers: new[] { GroundDust }, flourishOneIn: 0))
        .Add("seagull", "s2.dash", Hit("seagull_dash_hit", 1.1f, flourishOneIn: 0))
        .Add("seagull", "s1.poop", Hit("seagull_poop_hit", 1.0f, VfxElement.Poison, ownerColours: false, flourishOneIn: 0))
        // The wave is the world's: a splash in the arena's medium.
        .Add(CueOwners.Hazard, "tsunami", new VfxDef
        {
            Id = "tsunami_hit", Archetype = VfxArchetype.Splash, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Water,
            OwnerColours = false, Size = 30f, Priority = VfxPriority.Critical,
        })
        // Every hit that names nothing — the four kits not wired yet — in its attacker's medium.
        .Add(CueOwners.Any, "", Hit("hit"));

    // ── cues ─────────────────────────────────────────────────────────────────────
    private static VfxDef Status(string id, VfxElement element, int index) => new()
    {
        Id = id, Archetype = VfxArchetype.StatusLoop, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1f, Element = element,
        OwnerColours = false, Follow = true, Priority = VfxPriority.Critical, Stage = index,
        ScaleJitter = 0f, RotJitterDeg = 0f, Variants = 1,
    };

    private static VfxDef Sting(string id, VfxElement element, float scale = 0.9f) => new()
    {
        Id = id, Archetype = VfxArchetype.Sting, SizeFrom = VfxSizeFrom.BodyRadius, Scale = scale, Element = element,
        OwnerColours = false, Follow = true, Cooldown = 0.30f, CriticalForPlayer = true, Priority = VfxPriority.Confirm,
    };

    private static VfxDef HazardTick(string id, VfxElement element, VfxArchetype archetype = VfxArchetype.Sting, float size = 13f) => new()
    {
        Id = id, Archetype = archetype, StyleFrom = VfxStyleFrom.Arena, Element = element, OwnerColours = false,
        Size = size, Cooldown = 0.20f, CriticalForPlayer = true, Priority = VfxPriority.Confirm,
    };

    private static VfxDef Crossing(string id, VfxArchetype archetype, VfxElement element, bool exit) => new()
    {
        Id = id, Archetype = archetype, StyleFrom = VfxStyleFrom.Arena, Element = element, OwnerColours = false,
        SizeFrom = VfxSizeFrom.BodyRadius, Scale = exit ? 0.8f : 1f, MagLo = 80f, MagHi = 900f, Cooldown = 0.25f,
        Flags = exit ? VfxFlags.Exit : VfxFlags.None, Priority = VfxPriority.Cosmetic,
    };

    private static VfxDef Beat(string id, VfxArchetype archetype, VfxElement element, float size,
                               VfxPriority priority = VfxPriority.Confirm, VfxFlags flags = VfxFlags.None) => new()
    {
        Id = id, Archetype = archetype, StyleFrom = VfxStyleFrom.Arena, Element = element, OwnerColours = false,
        Size = size, Priority = priority, Flags = flags, ScaleJitter = 0f, RotJitterDeg = 0f, Variants = 1,
    };

    private static readonly VfxDef SoftExitDefault = Crossing("soft_exit", VfxArchetype.Rustle, VfxElement.Leaf, exit: true);
    private static readonly VfxDef SoftEnterDefault = Crossing("soft_enter", VfxArchetype.Rustle, VfxElement.Leaf, exit: false);

    public static readonly CueTable<VfxDef> Cues = new CueTable<VfxDef>()
        // ── bodies (Feedback.gdd §6.2): one "any" row serves every body ─────────
        .Add(CueOwners.Any, "spawn", new VfxDef
        {
            Id = "spawn", Archetype = VfxArchetype.SpawnPop, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.3f,
            Priority = VfxPriority.Confirm,
            ByTag = new[]
            {
                ("born", new VfxDef { Id = "spawn_born", Archetype = VfxArchetype.SpawnPop, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 0.8f, Priority = VfxPriority.Cosmetic }),
            },
        })
        .Add(CueOwners.Any, "jump", new VfxDef
        {
            // Dust belongs to the ground it is kicked from: the arena's medium.
            Id = "jump_dust", Archetype = VfxArchetype.DustPuff, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Dust,
            OwnerColours = false, Size = 15f, Priority = VfxPriority.Cosmetic, Modifiers = new[] { WhenUnderWater },
            ByTag = new[]
            {
                // An air jump touches no ground: a step-ring in the body's own medium.
                ("air", new VfxDef { Id = "jump_air", Archetype = VfxArchetype.Ring, Size = 15f, Element = VfxElement.Light, OwnerColours = false, Priority = VfxPriority.Cosmetic }),
                // A stroke in a volume: bubbles, in the arena's medium.
                ("swim", new VfxDef { Id = "jump_swim", Archetype = VfxArchetype.DustPuff, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Water, OwnerColours = false, Size = 13f, Flags = VfxFlags.Submerged, Priority = VfxPriority.Cosmetic, Cooldown = 0.2f }),
            },
        })
        .Add(CueOwners.Any, "land", new VfxDef
        {
            Id = "land_dust", Archetype = VfxArchetype.DustPuff, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Dust,
            OwnerColours = false, SizeFrom = VfxSizeFrom.Magnitude, MagLo = 192f, MagHi = 1100f, SizeMin = 11f, SizeMax = 30f,
            Priority = VfxPriority.Cosmetic, Modifiers = new[] { WhenUnderWater },
        })
        .Add(CueOwners.Any, "death", new VfxDef
        {
            Id = "death", Archetype = VfxArchetype.DeathBurst, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.15f, Priority = VfxPriority.Confirm,
        })
        .Add(CueOwners.Fighter, "death", new VfxDef
        {
            Id = "death_fighter", Archetype = VfxArchetype.DeathBurst, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.3f,
            Flags = VfxFlags.Big, Priority = VfxPriority.Critical,
        })
        .Add(CueOwners.Boss, "death", new VfxDef
        {
            Id = "death_boss", Archetype = VfxArchetype.DeathBurst, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.5f,
            Flags = VfxFlags.Big, Priority = VfxPriority.Critical,
        })
        .Add(CueOwners.Any, "respawn", new VfxDef
        {
            Id = "respawn", Archetype = VfxArchetype.SpawnPop, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.9f, Priority = VfxPriority.Confirm,
        })
        .Add(CueOwners.Any, "heal", new VfxDef
        {
            Id = "heal", Archetype = VfxArchetype.Heal, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1f, Element = VfxElement.Heal,
            OwnerColours = false, Follow = true, MagLo = 5f, MagHi = 60f, Cooldown = 0.25f, Priority = VfxPriority.Confirm,
        })
        .Add(CueOwners.Any, HurtTickMeter.Action, new VfxDef
        {
            Id = "hurt_tick", Archetype = VfxArchetype.Sting, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 0.9f, Element = VfxElement.Danger,
            OwnerColours = false, Follow = true, Cooldown = 0.30f, CriticalForPlayer = true, Priority = VfxPriority.Confirm,
            ByTag = new[]
            {
                ("hazard", Sting("hurt_hazard", VfxElement.Danger)),
                ("burn", Sting("hurt_burn", VfxElement.Fire)),
                ("on_fire", Sting("hurt_on_fire", VfxElement.Fire)),
                ("frozen", Sting("hurt_frozen", VfxElement.Ice)),
            },
        })
        .Add(CueOwners.Any, "ult.ready", new VfxDef
        {
            Id = "ult_ready", Archetype = VfxArchetype.Shine, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.5f, Element = VfxElement.Light,
            OwnerColours = false, Follow = true, Anchor = -1f, Priority = VfxPriority.Critical,
        })
        .Add(CueOwners.Any, "switch.out", new VfxDef
        {
            Id = "switch_out", Archetype = VfxArchetype.SpawnPop, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.2f,
            Flags = VfxFlags.Exit, Priority = VfxPriority.Confirm,
        })
        .Add(CueOwners.Any, "switch.in", new VfxDef
        {
            Id = "switch_in", Archetype = VfxArchetype.SpawnPop, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.5f, Priority = VfxPriority.Confirm,
        })
        .Add(CueOwners.Any, "aggro", new VfxDef
        {
            Id = "aggro", Archetype = VfxArchetype.Shine, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 0.55f, Element = VfxElement.Danger,
            OwnerColours = false, Follow = true, Anchor = -1.6f, Priority = VfxPriority.Confirm,
        })
        .Add(CueOwners.Any, "evolve", new VfxDef
        {
            // A gameplay tell: this foe just got stronger. Never culled.
            Id = "evolve", Archetype = VfxArchetype.Cast, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.7f, Element = VfxElement.Light,
            OwnerColours = false, Follow = true, Priority = VfxPriority.Critical,
        })
        .Add(CueOwners.Any, "hurt", new VfxDef
        {
            // The Protect core being struck: the arena's danger colour, on the side it came from.
            Id = "core_hurt", Archetype = VfxArchetype.Sting, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Danger,
            OwnerColours = false, Size = 20f, Cooldown = 0.12f, Priority = VfxPriority.Critical,
        })
        .Add(CueOwners.Any, "status.burning", Status("status_burning", VfxElement.Fire, 0))
        .Add(CueOwners.Any, "status.on_fire", Status("status_on_fire", VfxElement.Fire, 1))
        .Add(CueOwners.Any, "status.frozen", Status("status_frozen", VfxElement.Ice, 2))
        .Add(CueOwners.Any, "status.trapped", Status("status_trapped", VfxElement.Danger, 3))
        .Add(CueOwners.Any, "status.stunned", Status("status_stunned", VfxElement.Light, 4))
        .Add(CueOwners.Any, "telegraph", new VfxDef
        {
            // Drawn on the attack's real reach, in the realm's danger colour, in the foe's medium.
            Id = "telegraph", Archetype = VfxArchetype.Telegraph, SizeFrom = VfxSizeFrom.CueSize, Element = VfxElement.Danger,
            OwnerColours = false, Follow = true, Priority = VfxPriority.Critical, ScaleJitter = 0f, RotJitterDeg = 0f, Variants = 1,
            Modifiers = new[] { WhenBossEnraged },
        })

        // ── Pomegraknight, the reference kit (§6.3) ─────────────────────────────
        .Add("pomegraknight", "ba.swing", new VfxDef
        {
            // The crescent fills the real cone: reach and half-angle are the cue's.
            Id = "pome_slash", Archetype = VfxArchetype.SlashArc, SizeFrom = VfxSizeFrom.CueSize, Follow = true,
            Variants = 3, RotJitterDeg = 4f, ScaleJitter = 0f, HueJitter = 0.25f, FlourishOneIn = 20, Priority = VfxPriority.Confirm,
            Modifiers = new[] { WhenBurningTag },
        })
        .Add("pomegraknight", "s1.cast", new VfxDef
        {
            Id = "pome_blush", Archetype = VfxArchetype.Cast, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.6f, Element = VfxElement.Fire,
            OwnerColours = false, Follow = true, Priority = VfxPriority.Confirm,
        })
        .Add("pomegraknight", "s1.tornado", new VfxDef
        {
            // A loop on the field's real box.
            Id = "pome_tornado", Archetype = VfxArchetype.FieldLoop, SizeFrom = VfxSizeFrom.CueExtents, Element = VfxElement.Fire,
            OwnerColours = false, Follow = true, Priority = VfxPriority.Critical, ScaleJitter = 0f, RotJitterDeg = 0f, Variants = 1,
        })
        .Add("pomegraknight", "s1.tornado.tick", new VfxDef
        {
            Id = "pome_tornado_tick", Archetype = VfxArchetype.Ring, SizeFrom = VfxSizeFrom.CueExtents, Element = VfxElement.Fire,
            OwnerColours = false, Follow = true, Priority = VfxPriority.Confirm, ScaleJitter = 0f, RotJitterDeg = 0f,
        })
        .Add("pomegraknight", "s2.cast", new VfxDef
        {
            Id = "pome_erupt_cast", Archetype = VfxArchetype.Cast, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 1.2f, Follow = true,
            Anchor = -1f, Priority = VfxPriority.Confirm, Modifiers = new[] { WhenBurningTag },
        })
        .Add("pomegraknight", "s2.wave", new VfxDef
        {
            // One effect for the fan — the seeds themselves are the projectiles.
            Id = "pome_seed_wave", Archetype = VfxArchetype.Trail, Size = 44f, Priority = VfxPriority.Confirm,
            Modifiers = new[] { WhenBurningTag },
        })
        .Add("pomegraknight", "s2.seed.expire", new VfxDef
        {
            Id = "pome_seed_expire", Archetype = VfxArchetype.ExpirePop, Size = 7f, Priority = VfxPriority.Cosmetic,
            Modifiers = new[] { WhenBurningTag },
        })
        // Any projectile that dies on terrain and has no row of its own.
        .Add(CueOwners.Projectile, "s2.seed.expire", new VfxDef { Id = "projectile_expire", Archetype = VfxArchetype.ExpirePop, Size = 7f, Priority = VfxPriority.Cosmetic })

        // ── PumpKing's head ─────────────────────────────────────────────────────
        .Add(CueOwners.Any, "ba.explode", new VfxDef
        {
            // To exactly the blast radius. A head that only pops ("dud") is a small pop.
            Id = "blast", Archetype = VfxArchetype.Burst, SizeFrom = VfxSizeFrom.CueSize, Priority = VfxPriority.Critical,
            ScaleJitter = 0f, Variants = 3, Flags = VfxFlags.Big,
            ByTag = new[] { ("dud", new VfxDef { Id = "blast_dud", Archetype = VfxArchetype.ExpirePop, Size = 16f, Priority = VfxPriority.Confirm }) },
        })

        // ── world (§6.4): the arena's medium ────────────────────────────────────
        .Add(CueOwners.World, "soft.enter", new VfxDef
        {
            Id = SoftEnterDefault.Id, Archetype = SoftEnterDefault.Archetype, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Leaf,
            OwnerColours = false, SizeFrom = VfxSizeFrom.BodyRadius, MagLo = 80f, MagHi = 900f, Cooldown = 0.25f, Priority = VfxPriority.Cosmetic,
            ByTag = new[]
            {
                ("water", Crossing("splash_in", VfxArchetype.Splash, VfxElement.Water, exit: false)),
                ("foliage", Crossing("rustle_in", VfxArchetype.Rustle, VfxElement.Leaf, exit: false)),
                ("cloud", Crossing("cloud_in", VfxArchetype.DustPuff, VfxElement.Smoke, exit: false)),
                ("kit", Crossing("kit_volume_in", VfxArchetype.Rustle, VfxElement.Light, exit: false)),
            },
        })
        .Add(CueOwners.World, "soft.exit", new VfxDef
        {
            Id = SoftExitDefault.Id, Archetype = SoftExitDefault.Archetype, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Leaf,
            OwnerColours = false, SizeFrom = VfxSizeFrom.BodyRadius, Scale = 0.8f, MagLo = 80f, MagHi = 900f, Cooldown = 0.25f,
            Flags = VfxFlags.Exit, Priority = VfxPriority.Cosmetic,
            ByTag = new[]
            {
                ("water", Crossing("splash_out", VfxArchetype.Splash, VfxElement.Water, exit: true)),
                ("foliage", Crossing("rustle_out", VfxArchetype.Rustle, VfxElement.Leaf, exit: true)),
                ("cloud", Crossing("cloud_out", VfxArchetype.DustPuff, VfxElement.Smoke, exit: true)),
                ("kit", Crossing("kit_volume_out", VfxArchetype.Rustle, VfxElement.Light, exit: true)),
            },
        })
        .Add(CueOwners.Hazard, "tick", new VfxDef
        {
            Id = "hazard_tick", Archetype = VfxArchetype.Sting, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Danger,
            OwnerColours = false, Size = 13f, Cooldown = 0.20f, CriticalForPlayer = true, Priority = VfxPriority.Confirm,
            ByTag = new[]
            {
                ("fire", HazardTick("hazard_fire", VfxElement.Fire)),
                ("freeze", HazardTick("hazard_freeze", VfxElement.Ice)),
                ("lava", HazardTick("hazard_lava", VfxElement.Fire, VfxArchetype.ExpirePop, 16f)),
                ("spike", HazardTick("hazard_spike", VfxElement.Danger, VfxArchetype.HitSpark, 12f)),
                ("poop", HazardTick("hazard_poop", VfxElement.Poison)),
                ("damage", HazardTick("hazard_damage", VfxElement.Danger)),
            },
        })

        // ── flow (§6.4): beats in the arena's medium ────────────────────────────
        // arena.enter starts the realm's own air; the director does it (EnterArena).
        .Add(CueOwners.Flow, "arena.enter", new VfxDef { Id = "ambience", Archetype = VfxArchetype.Ambient, StyleFrom = VfxStyleFrom.Arena, OwnerColours = false, Priority = VfxPriority.Cosmetic })
        .Add(CueOwners.Flow, "countdown.tick", Beat("countdown_tick", VfxArchetype.SpawnPop, VfxElement.Light, 34f))
        .Add(CueOwners.Flow, "countdown.go", Beat("countdown_go", VfxArchetype.Shine, VfxElement.Light, 46f, VfxPriority.Critical))
        .Add(CueOwners.Flow, "mission.start", Beat("mission_start", VfxArchetype.Ring, VfxElement.Light, 70f, VfxPriority.Cosmetic))
        .Add(CueOwners.Flow, "mission.success", Beat("mission_success", VfxArchetype.Cast, VfxElement.Light, 54f, VfxPriority.Critical))
        .Add(CueOwners.Flow, "mission.fail", Beat("mission_fail", VfxArchetype.DustPuff, VfxElement.Smoke, 44f, VfxPriority.Critical))
        .Add(CueOwners.Flow, "boss.intro", Beat("boss_intro", VfxArchetype.Shine, VfxElement.Danger, 72f, VfxPriority.Critical))
        .Add(CueOwners.Flow, "boss.defeated", Beat("boss_defeated", VfxArchetype.Burst, VfxElement.Light, 130f, VfxPriority.Critical, VfxFlags.Big))
        .Add(CueOwners.Flow, "player.down", Beat("player_down", VfxArchetype.Ring, VfxElement.Danger, 110f, VfxPriority.Critical))
        .Add(CueOwners.Flow, "run.over", new VfxDef
        {
            Id = "run_over", Archetype = VfxArchetype.DustPuff, StyleFrom = VfxStyleFrom.Arena, Element = VfxElement.Smoke,
            OwnerColours = false, Size = 40f, Priority = VfxPriority.Cosmetic,
            ByTag = new[] { ("victory", Beat("run_victory", VfxArchetype.Shine, VfxElement.Light, 90f, VfxPriority.Critical)) },
        });

    /// <summary>The row a cue's tag selects, or the row itself.</summary>
    public static VfxDef ForTag(VfxDef def, string tag)
    {
        if (def?.ByTag == null || tag == null) return def;
        (string Tag, VfxDef Def)[] alternates = def.ByTag;
        for (int i = 0; i < alternates.Length; i++)
            if (alternates[i].Tag == tag) return alternates[i].Def;
        return def;
    }

    /// <summary>Every row, alternates and layers included — for warm-up and the checks.</summary>
    public static List<VfxDef> AllDefs()
    {
        var all = new List<VfxDef>();
        void Walk(VfxDef def)
        {
            if (def == null || all.Contains(def)) return;
            all.Add(def);
            if (def.ByTag != null) foreach ((string _, VfxDef alt) in def.ByTag) Walk(alt);
            if (def.Layers != null) foreach (VfxDef layer in def.Layers) Walk(layer);
            if (def.Modifiers != null) foreach (VfxModifier modifier in def.Modifiers) Walk(modifier.Add);
        }
        foreach ((string owner, string action) in Cues.Keys) Walk(Cues.Resolve(owner, null, action));
        foreach ((string owner, string action) in Hits.Keys) Walk(Hits.Resolve(owner, null, action));
        Walk(React);
        return all;
    }

    /// <summary>Boot-time checks (T10 §5): every look names a real medium, every row a real
    /// archetype with a sane size, every medium says every colour on every canvas.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        if (Styles.Length != 10) failures.Add($"{Styles.Length} styles, expected the ten media");
        if (Style(DefaultStyle) == null) failures.Add("the default style does not exist");
        foreach ((string owner, VfxLook look) in Looks)
            if (Style(look.Style) == null) failures.Add($"look '{owner}' names the unknown medium '{look.Style}'");
        foreach (VfxStyle style in Styles)
        {
            if (StyleById[style.Id] != style) failures.Add($"two styles share the id '{style.Id}'");
            if (style.Ambience.Length > VfxBudget.Ambient) failures.Add($"{style.Id}: more ambient emitters than the budget");
            int motes = 0;
            foreach (VfxAmbientRecipe recipe in style.Ambience) motes += recipe.Amount;
            if (motes > 24) failures.Add($"{style.Id}: {motes} ambient particles, over 24");
            for (int e = 0; e < (int)VfxElement.Count; e++)
            {
                for (int c = 0; c < 3; c++)
                {
                    VfxPalette p = style.Element((VfxElement)e, (VfxCanvas)c);
                    if (p.Main.A <= 0f) failures.Add($"{style.Id}: no colour for {(VfxElement)e} on a {(VfxCanvas)c} canvas");
                }
            }
        }
        var ids = new HashSet<string>();
        foreach (VfxDef def in AllDefs())
        {
            if (string.IsNullOrEmpty(def.Id)) failures.Add($"a {def.Archetype} row has no id");
            else if (!ids.Add(def.Id)) failures.Add($"two rows share the id '{def.Id}'");
            if (def.Archetype >= VfxArchetype.Count) failures.Add($"{def.Id}: no archetype");
            if (def.Style != null && Style(def.Style) == null) failures.Add($"{def.Id}: unknown medium '{def.Style}'");
            if (def.SizeFrom == VfxSizeFrom.Fixed && (def.Size <= 0f || def.Size > 400f)) failures.Add($"{def.Id}: fixed size {def.Size}");
            if (def.SizeFrom == VfxSizeFrom.Magnitude && def.MagHi <= def.MagLo) failures.Add($"{def.Id}: magnitude range is empty");
            if (def.Variants < 1 || def.Variants > 8) failures.Add($"{def.Id}: {def.Variants} variants");
            bool geometryTrue = def.Archetype is VfxArchetype.SlashArc or VfxArchetype.FieldLoop or VfxArchetype.Telegraph or VfxArchetype.Burst;
            if (geometryTrue && def.SizeFrom != VfxSizeFrom.Fixed && def.ScaleJitter != 0f)
                failures.Add($"{def.Id}: an effect drawn on a hitbox's real size must not jitter its scale");
            if (def.Archetype is VfxArchetype.StatusLoop or VfxArchetype.Telegraph && def.Priority != VfxPriority.Critical)
                failures.Add($"{def.Id}: a status loop or a telegraph must be Critical — it tells the player something");
        }
        for (int w = 1; w < WeightSize.Length; w++)
            if (WeightSize[w] <= WeightSize[w - 1]) failures.Add("hit sizes must rise with weight");
        return failures;
    }
}

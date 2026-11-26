namespace Fableland.Vfx;

using Godot;
using Fableland.Data;

// The VFX director's vocabulary (Docs/VFX.gdd §3–§5): what an effect IS (an archetype), how
// much it matters (a priority), whose medium draws it, and the data a table row is made of.
// PRESENTATION data — nothing here can change a fight.

/// <summary>What an effect is, whatever medium draws it. A style renders every one of these;
/// a table row names one for a cue.</summary>
public enum VfxArchetype : byte
{
    HitSpark,     // the contact of a hit, by weight, along the blow
    HitReact,     // what flies off the victim, in the victim's medium
    SlashArc,     // a swing: the cone's real reach and half-angle
    Burst,        // an explosion to exactly its radius
    Ring,         // one pulse ring (a tick of a field, a shockwave)
    Trail,        // a fan of streaks (a volley leaving, a dash)
    DustPuff,     // dust from the ground: jump, land
    Splash,       // crossing a water surface
    Rustle,       // crossing foliage or cloud
    StatusLoop,   // a status worn by a body, for as long as it lasts
    Telegraph,    // the mark of an attack that is about to happen, on its real reach
    DeathBurst,   // a body comes apart
    SpawnPop,     // a body arrives
    Heal,         // health comes back
    Shine,        // a glint that says "ready" / "noticed" / "stronger"
    ExpirePop,    // a projectile dies on terrain
    FieldLoop,    // an area that lasts, on its real extents
    Ambient,      // the arena's own air
    Cast,         // the flourish of a skill being cast
    Sting,        // damage that is not a hit: a hazard tick, a burn
    Count,
}

/// <summary>Who may be dropped when the budget is full (R2 §4.9). Critical tells the player
/// something they must know and is never culled; Confirm is shortened or skipped; Cosmetic
/// goes first.</summary>
public enum VfxPriority : byte { Critical, Confirm, Cosmetic }

/// <summary>Whose medium an effect is drawn in.</summary>
public enum VfxStyleFrom : byte
{
    Owner,      // the cue's owner (a body's own effects; an attack's impact = the attacker)
    Arena,      // the world being fought in (dust, water, hazards, ambience, flow beats)
    Victim,     // the body that was hit (its reaction)
}

/// <summary>The colours every medium must be able to say, each in its own palette rule —
/// "fire" is orange wash in watercolour, yellow scratches on the scratchboard, and has no
/// warm hue at all in ink.</summary>
public enum VfxElement : byte
{
    Impact,     // a plain hit: the medium's default mark colour
    Dust,       // ground dust, neutral
    Fire,
    Ice,
    Water,
    Leaf,
    Poison,     // poop, sludge
    Danger,     // the realm's warning colour (telegraphs, spikes)
    Heal,
    Light,      // shine, ready, level-up
    Smoke,
    Count,
}

/// <summary>What a pooled mark node draws. A style draws each of these from its five
/// primitives (mass, brush, hoop, chip, tone).</summary>
public enum VfxMarkKind : byte
{
    None,
    Impact,     // a contact mark of radius Size
    Arc,        // a swing: reach Size, half-angle Arc
    Ring,       // a ring growing to Size (or to Extents as an ellipse)
    Puff,       // a few dust lobes spreading from the ground
    Streak,     // a line of length Size along the mark's rotation
    Zone,       // a telegraphed lane: Size long, Extents.Y half-wide — or a ring when Size is 0
    Field,      // a box of half-extents Extents, alive with ribbons
    Aura,       // a status worn by a body of radius Size; Variant says which
    Bits,       // Count small pieces on ballistic paths, drawn (stepped media)
    Glint,      // a four-point glint
    Sheet,      // an authored flipbook that replaces a procedural effect
    Warm,       // one invisible triangle: compiles the pipeline at arena load
}

/// <summary>What a burst of small pieces does.</summary>
public enum VfxBits : byte
{
    Spray,      // flung along a blow, then falls
    Fall,       // debris: out a little, then down
    Rise,       // motes that drift up (heal, embers, bubbles)
    Dust,       // slow lobes hugging the ground
}

/// <summary>How light the canvas behind an effect is — the contrast guard (R2 §4.5): each
/// medium says its colours three times, and lays its own ground under a mark the canvas
/// would swallow.</summary>
public enum VfxCanvas : byte { Dark, Mid, Light }

[System.Flags]
public enum VfxFlags : ushort
{
    None = 0,
    Burning = 1 << 0,     // the owner is burning: fire joins the effect
    Killed = 1 << 1,      // this hit killed
    Reverse = 1 << 2,     // a ring that closes, a sweep that runs the other way
    Converge = 1 << 3,    // pieces travel inward
    Player = 1 << 4,      // it concerns the body the player wears
    Flourish = 1 << 5,    // the rare extra
    Exit = 1 << 6,        // leaving (a volume, the fight)
    Big = 1 << 7,         // a boss, a finisher
    Double = 1 << 8,      // a second layer (the third slash)
    Submerged = 1 << 9,   // under water: bubbles instead of dust
}

/// <summary>Five colours a mark is made of. <see cref="Ground"/> is the medium's own ground
/// — paper, the black board — laid under a mark where the arena's canvas would swallow it
/// (the contrast guard, R2 §4.5); alpha 0 when the medium needs none.</summary>
public struct VfxPalette
{
    public Color Main;      // the body of the mark
    public Color Alt;       // its second tone (shadow face, wet rim, second ink)
    public Color Accent;    // the highlight
    public Color Line;      // the medium's line (ink, wire, key block)
    public Color Ground;    // the medium's ground

    public VfxPalette(Color main, Color alt, Color accent, Color line, Color ground = default)
    {
        Main = main; Alt = alt; Accent = accent; Line = line; Ground = ground;
    }
}

/// <summary>An owner's look: which medium, and its own three colours inside that medium's
/// palette rule. A world code is a look with no colours of its own.</summary>
public sealed class VfxLook
{
    public string Style { get; init; }
    public bool HasColours { get; init; }
    public Color Main { get; init; }
    public Color Alt { get; init; }
    public Color Accent { get; init; }
    /// <summary>True while the design owner has not confirmed the medium (Feedback.gdd §8).</summary>
    public bool Provisional { get; init; }
}

/// <summary>Where an effect's size comes from. The effect and the hitbox are one number.</summary>
public enum VfxSizeFrom : byte
{
    Fixed,          // Size px
    CueSize,        // the cue's reach / radius × Scale
    CueExtents,     // the cue's box × Scale
    Magnitude,      // mapped from the cue's magnitude: MagLo..MagHi → SizeMin..SizeMax
    BodyRadius,     // the source body's radius × Scale
    Weight,         // a hit: by its weight tier (VfxTable.WeightSize) × Scale
}

/// <summary>A change to an effect when a condition holds — the owner's state, the pace of
/// the fight, the combo, the boss's phase. Every set condition must hold.</summary>
public sealed class VfxModifier
{
    // ── when ──
    public FeedbackState State { get; init; }           // any of these on the source body
    public string Tag { get; init; }                    // the cue's tag
    public int MinCombo { get; init; }
    public float MinIntensity { get; init; }            // combat intensity 0–1
    public int MinBossPhase { get; init; }
    public bool Killed { get; init; }
    public HitWeight MinWeight { get; init; }
    public int Stage { get; init; }                     // the cue's variant (combo stage, wave)
    // ── then ──
    public float Count { get; init; } = 1f;
    public float Scale { get; init; } = 1f;
    public float Life { get; init; } = 1f;
    public float Speed { get; init; } = 1f;
    public VfxElement? Element { get; init; }           // repaint in this element
    public VfxFlags Flags { get; init; }
    public VfxDef Add { get; init; }                    // one more layer
}

/// <summary>
/// One row of the VFX table: what a cue looks like. Pure data; the style it resolves to
/// decides how each archetype is drawn.
/// </summary>
public sealed class VfxDef
{
    /// <summary>Effect id (snake_case). An authored sheet at
    /// <c>res://Assets/Vfx/&lt;style&gt;/&lt;id&gt;.png</c> replaces the procedural effect.</summary>
    public string Id { get; init; }
    public VfxArchetype Archetype { get; init; }
    /// <summary>A medium by id, or null for <see cref="StyleFrom"/>.</summary>
    public string Style { get; init; }
    public VfxStyleFrom StyleFrom { get; init; } = VfxStyleFrom.Owner;
    /// <summary>The owner's own colours when it has a look; else (or when false) the element.</summary>
    public bool OwnerColours { get; init; } = true;
    public VfxElement Element { get; init; } = VfxElement.Impact;

    public VfxSizeFrom SizeFrom { get; init; } = VfxSizeFrom.Fixed;
    /// <summary>Px, when the size is Fixed.</summary>
    public float Size { get; init; } = 24f;
    /// <summary>A multiple of the cue's own number, for every other size rule.</summary>
    public float Scale { get; init; } = 1f;
    public float SizeMin { get; init; } = 8f;
    public float SizeMax { get; init; } = 64f;
    public float MagLo { get; init; }
    public float MagHi { get; init; } = 1f;

    /// <summary>Shape variants drawn from a shuffle-bag: never the same twice running.</summary>
    public int Variants { get; init; } = 3;
    public float RotJitterDeg { get; init; } = 8f;
    public float ScaleJitter { get; init; } = 0.10f;
    public float TimeJitter { get; init; } = 0.10f;
    public float HueJitter { get; init; } = 0.18f;      // how far Main may lean to Alt
    public float Count { get; init; } = 1f;
    public float Life { get; init; } = 1f;

    public VfxPriority Priority { get; init; } = VfxPriority.Confirm;
    /// <summary>Seconds before the same source may play this row again (0 = always).</summary>
    public float Cooldown { get; init; }
    /// <summary>Ignored when the cue's magnitude is under this.</summary>
    public float MinMagnitude { get; init; }
    /// <summary>The effect stays on the body that raised it.</summary>
    public bool Follow { get; init; }
    /// <summary>Where on the body: 0 its centre, +1 its feet, −1 its head.</summary>
    public float Anchor { get; init; }
    public VfxFlags Flags { get; init; }
    /// <summary>One chance in this many of the rare flourish (0 = never).</summary>
    public int FlourishOneIn { get; init; }
    /// <summary>Critical when it concerns the player's side: their own hurt is never culled.</summary>
    public bool CriticalForPlayer { get; init; }
    /// <summary>The stage the style is told, instead of the cue's variant (a status's index).</summary>
    public int Stage { get; init; } = -1;

    public VfxModifier[] Modifiers { get; init; }
    /// <summary>Other rows played with the same cue (a jump is dust in the arena's medium
    /// and a launch streak in the body's).</summary>
    public VfxDef[] Layers { get; init; }
    /// <summary>Rows chosen by the cue's tag instead of this one ("water" → a splash).</summary>
    public (string Tag, VfxDef Def)[] ByTag { get; init; }

    // ── runtime, not data ──
    internal byte[] Bag;                 // the variant shuffle-bag
    internal int BagLeft;
    internal int LastVariant = -1;
    internal readonly ulong[] CooldownSource = new ulong[4];
    internal readonly double[] CooldownUntil = new double[4];
    internal int CooldownNext;
}

/// <summary>Everything a style needs to compose one effect. Passed by reference; never stored.</summary>
public struct VfxCtx
{
    public VfxDef Def;
    public VfxStyle Style;
    public VfxPalette P;
    public Vector2 Pos;
    public Vector2 Dir;              // unit, or zero
    public float Size;               // px, by the row's size rule
    public Vector2 Extents;          // px half-extents, or zero
    public float Arc;                // a cone's half-angle in radians, or 0
    public float Magnitude;
    public int Stage;                // the cue's variant
    public int Variant;              // the shape variant drawn from the bag
    public float Power;              // 0 graze … 1 massive
    public float CountMul, ScaleMul, LifeMul, SpeedMul;
    public VfxFlags Flags;
    public VfxPriority Priority;
    public Node2D Follow;
    public int LoopId;               // 0 = a one-shot
    public uint Seed;
    public VfxCanvas Canvas;         // how light the canvas behind it is
    public float BodyRadius;
}

/// <summary>The state of one pooled mark: what it draws.</summary>
public struct VfxMarkState
{
    public VfxMarkKind Kind;
    public VfxPalette P;
    public float Size;
    public Vector2 Extents;
    public float Arc;
    public float Rot;                // direction, for a medium that keeps its grid (the node is not turned)
    public float Life;               // seconds; a loop's period when looping
    public float Param;              // kind-specific: a tell's seconds, a streak's width
    public float Speed;              // bits: launch speed px/s
    public float Gravity;            // bits: px/s²
    public float Spread;             // bits: cone in radians
    public Vector2 Dir;              // bits: unit direction (local)
    public int Count;
    public int Variant;
    public int Stage;
    public uint Seed;
    public VfxFlags Flags;
    public Texture2D Sheet;          // Sheet: the authored strip
    public int Frames;
}

/// <summary>The budget (Docs/VFX.gdd §7). Our own targets — measured, not sourced.</summary>
public static class VfxBudget
{
    /// <summary>Pooled mark nodes: the hard ceiling of drawn effects alive.</summary>
    public const int Marks = 72;
    /// <summary>Pooled one-shot emitters: the hard ceiling of bursts alive.</summary>
    public const int Bursts = 24;
    /// <summary>Particles in one burst.</summary>
    public const int ParticlesPerBurst = 32;
    /// <summary>Live particles, every emitter, ambience included.</summary>
    public const int Particles = 512;
    /// <summary>Ambient emitters per arena.</summary>
    public const int Ambient = 2;
    /// <summary>Loops alive (statuses, telegraphs, fields).</summary>
    public const int Loops = 24;
    /// <summary>Effects that are not Critical started in one frame.</summary>
    public const int SpawnsPerFrame = 6;

    // Where each priority stops being served (live marks / bursts / particles).
    public const int CosmeticMarks = 36, ConfirmMarks = 56;
    public const int CosmeticBursts = 10, ConfirmBursts = 18;
    public const int CosmeticParticles = 220, ConfirmParticles = 400;
    /// <summary>Off-screen effects that are not Critical are not started; this is the margin.</summary>
    public const float OffscreenMarginPx = 160f;
}

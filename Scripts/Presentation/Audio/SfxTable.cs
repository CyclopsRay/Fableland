using System;
using System.Collections.Generic;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

/// <summary>
/// The sound table (<c>Docs/Audio.gdd</c> §3, §6): one row per cue of the vocabulary in
/// <c>Docs/Feedback.gdd</c> §6, keyed (owner, action) and resolved owner → class → <c>any</c>
/// like every director's table. A row is a recipe written in the six primitives of a
/// <see cref="Medium"/>, so the same row is a different sound in every world — which is how a
/// kit that has no rows of its own already sounds like its home medium.
///
/// Three tables:
///   <see cref="Cues"/>     — one-shots and loops, by the cue's (owner, action);
///   <see cref="Attacks"/>  — what struck, by the hit's (attack owner, attack action);
///   and, inside <see cref="Cues"/>, the rows the director posts itself (<c>hit.taken</c>,
///   <c>hit.heavy</c>, <c>combo.note</c>, <c>combo.resolve</c>, <c>heartbeat</c>, <c>ambience</c>).
///
/// PRESENTATION data: nothing here can change a fight. Every level and length is a starting
/// value judged on spectrogram sheets — nobody has heard it.
/// </summary>
public static class SfxTable
{
    public static readonly CueTable<SfxDef> Cues = new();
    public static readonly CueTable<SfxDef> Attacks = new();
    /// <summary>Every row, in table order — for warming, dumping and checks.</summary>
    public static readonly List<SfxDef> All = new();

    // Actions the director posts itself (constants, so nothing is built per event).
    public const string HitTaken = "hit.taken";
    public const string HitHeavy = "hit.heavy";
    public const string ComboNote = "combo.note";
    public const string ComboResolve = "combo.resolve";
    public const string Heartbeat = "heartbeat";
    public const string Ambience = "ambience";

    private const string Any = CueOwners.Any, Fighter = CueOwners.Fighter, Foe = CueOwners.Foe, Boss = CueOwners.Boss,
        Objective = CueOwners.Objective, Projectile = CueOwners.Projectile, World = CueOwners.World,
        Hazard = CueOwners.Hazard, Flow = CueOwners.Flow, Ui = CueOwners.Ui;

    /// <summary>The semitone above which a ladder note is rendered: the fifth, so the climb
    /// is a shift of at most a fifth either way.</summary>
    public const int LadderCentre = 7;

    /// <summary>Each character's motif, in semitones from the tonic, in the character's own
    /// mode (<c>Docs/Audio.gdd</c> §6) — heard in <c>ult.ready</c> and as a boss's layer.</summary>
    public static readonly Dictionary<string, int[]> Motifs = new(StringComparer.Ordinal)
    {
        ["pomegraknight"] = new[] { 7, 12, 16 },        // 5 – 1′ – 3′, a rising fanfare
        ["cleopastar"] = new[] { 0, 1, 5, 4 },          // 1 – ♭2 – 4 – 3
        ["pixolotl"] = new[] { 0, 7, 12, 7, 3 },        // 1 – 5 – 1′ – 5, then ♭3
        ["pumpking"] = new[] { 0, 6, -12 },             // 1 – ♯4 – 1 low
        ["sifu_pangda"] = new[] { 7, 9, 12 },           // 5 – 6 – 1′
        ["crab"] = new[] { 0, 0, 7 },
        ["boss_crab"] = new[] { 0, 0, 7 },
        ["seagull"] = new[] { 12, 7 },
    };

    private static readonly int[] DefaultMotif = { 0, 7, 12 };

    /// <summary>An owner that is another owner at a different size: its rows are the other's,
    /// played lower. The stand-in boss crab is a crab, a fourth down.</summary>
    private static readonly Dictionary<string, (string Rows, float Semis)> Alias = new(StringComparer.Ordinal)
    {
        ["boss_crab"] = ("crab", -5f),
    };

    /// <summary>Whose rows an owner uses (itself, unless it is an alias).</summary>
    public static string RowsOf(string owner) => owner != null && Alias.TryGetValue(owner, out var alias) ? alias.Rows : owner;
    /// <summary>Semitones an alias is moved by (0 for anyone else).</summary>
    public static float SizeSt(string owner) => owner != null && Alias.TryGetValue(owner, out var alias) ? alias.Semis : 0f;

    /// <summary>A cue's row: the owner's (or its alias's), then its class's, then <c>any</c>'s.</summary>
    public static SfxDef ForCue(string owner, string ownerClass, string action) => Cues.Resolve(RowsOf(owner), ownerClass, action);
    /// <summary>A hit's row: the named attack, else the same owner's unnamed one.</summary>
    public static SfxDef ForAttack(string owner, string ownerClass, string action) =>
        Attacks.Resolve(RowsOf(owner), ownerClass, action) ?? Attacks.Resolve(RowsOf(owner), ownerClass, "");
    public static int[] MotifOf(string owner) => owner != null && Motifs.TryGetValue(owner, out int[] m) ? m : DefaultMotif;

    private static readonly Mode[] SeedWood = { new(1f, 0.05f, 1f), new(2.7f, 0.03f, 0.4f) };
    private static readonly Mode[] Castanet = { new(1f, 0.03f, 1f), new(2.2f, 0.02f, 0.6f), new(4.3f, 0.012f, 0.3f) };
    private static readonly Mode[] Shell = { new(1f, 0.05f, 1f), new(1.9f, 0.03f, 0.6f), new(3.6f, 0.02f, 0.3f) };

    static SfxTable()
    {
        Bodies();
        Hits();
        Pomegraknight();
        PumpKing();
        BeachFoes();
        WorldAndHazards();
        FlowAndUi();
    }

    /// <summary>True for a row any owner may fall back to (a class or <c>any</c>).</summary>
    public static bool IsClassRow(SfxDef def) =>
        def.Owner is Any or Fighter or Foe or Boss or Objective or Projectile;

    /// <summary>The media a row can be heard in: its fixed one; every realm, for a realm's
    /// row or a class row; its owner's home, for a character's own.</summary>
    public static IEnumerable<Medium> MediaOf(SfxDef def)
    {
        if (def.From == MediumFrom.Fixed) { yield return Medium.Get(def.FixedMedium) ?? Medium.Home; yield break; }
        Medium home = def.From == MediumFrom.Owner && !IsClassRow(def) ? Medium.HomeFor(def.Owner) : null;
        if (home != null) { yield return home; yield break; }
        foreach (Medium medium in Medium.All) yield return medium;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────
    private static SfxDef Row(string owner, string action, float body, SfxRecipe recipe)
    {
        var def = new SfxDef(owner, action) { Body = body, Recipe = recipe };
        Cues.Add(owner, action, def);
        All.Add(def);
        return def;
    }

    private static SfxDef Attack(string owner, string action, float body, SfxRecipe recipe)
    {
        var def = new SfxDef(owner, action, action.Length > 0 ? owner + ".hit." + action : owner + ".hit")
        {
            Body = body, Recipe = recipe, Priority = 75, MaxVoices = 4, Cooldown = 0.03f, PeakDb = -6f,
            Variants = 3, PitchJitter = 1f, VolumeJitter = 1.5f,
        };
        Attacks.Add(owner, action, def);
        All.Add(def);
        return def;
    }

    /// <summary>A row that is only ever another row's burst (a volley in one sound).</summary>
    private static SfxDef Burst(string id, float body, SfxRecipe recipe)
    {
        var def = new SfxDef(Any, id, id) { Body = body, Recipe = recipe, Variants = 1, MaxVoices = 1, Cooldown = 0.1f, Priority = 55 };
        All.Add(def);
        return def;
    }

    /// <summary>The tonic, moved by semitones — for the open intervals (fifth, octave) a
    /// character's own sounds keep to, so they sit in any realm's key.</summary>
    private static float T(RenderCtx c, float semis = 0f) => c.Tonic + semis;

    /// <summary>Render a layer on its own, shape it with an envelope, mix it in.</summary>
    private static void Shaped(RenderCtx c, float at, float dur, Env env, float gain, Action<RenderCtx> inner)
    {
        var sub = new RenderCtx
        {
            Buf = new float[c.S(dur) + 2], Sr = c.Sr, Rng = c.Rng.Fork(3), Medium = c.Medium,
            Tonic = c.Tonic, Scale = c.Scale, Motif = c.Motif, Body = dur, Size = c.Size,
        };
        inner(sub);
        c.Bright |= sub.Bright;
        c.Low |= sub.Low;
        int start = c.S(at);
        float inv = 1f / c.Sr;
        for (int i = 0; i < sub.Buf.Length && start + i < c.Buf.Length; i++)
            c.Buf[start + i] += sub.Buf[i] * env.At(i * inv) * gain;
    }

    /// <summary>A burst of one of the medium's textures: a lick of fire, a splash, a rustle.</summary>
    private static void Puff(RenderCtx c, float at, float dur, Tex tex, Env env, float gain) =>
        Shaped(c, at, dur, env, gain, s => s.Medium.Texture(s, 0f, dur, tex, 1f));

    /// <summary>The realm's motif as a run of notes.</summary>
    private static void MotifRun(RenderCtx c, float at, float step, float gain, int octave = 0, bool reverse = false, int count = 0)
    {
        int n = count > 0 ? Math.Min(count, c.Motif.Length) : c.Motif.Length;
        for (int i = 0; i < n; i++)
        {
            int degree = c.Motif[reverse ? n - 1 - i : i];
            c.Medium.Tone(c, at + i * step, c.Deg(degree) + 12 * octave, step * 1.5f, gain, i == n - 1 ? ToneKind.Accent : ToneKind.Short);
        }
    }

    /// <summary>What a realm's air is made of.</summary>
    private static Tex AmbienceOf(Medium m) => m.Id switch
    {
        "VK" or "PL" => Tex.Water,
        "HC" or "VOID" => Tex.Rumble,
        _ => Tex.Air,
    };

    // ── Bodies: every fighter, foe, boss and objective (Feedback.gdd §6.2) ──────────
    private static void Bodies()
    {
        Row(Any, "spawn", 0.36f, (c, _, _) =>
        {
            c.Medium.Sweep(c, 0f, 0.2f, T(c, -5f), T(c, 7f), 0.6f);
            c.Medium.Tone(c, 0.18f, T(c, 12f), 0.2f, 0.8f);
        }).Vary(1, 0f, 0.5f).Voices(3, 0.08f, 40).Level(-4f).Keyed();

        Row(Foe, "spawn", 0.24f, (c, _, tag) =>
        {
            if (tag == 1)       // born out of another foe's death: smaller, quicker
            {
                c.Medium.Tick(c, 0f, 0.8f, 0.7f);
                c.Medium.Whoosh(c, 0f, 0.12f, 0.5f, 0.9f, 0.5f);
                return;
            }
            c.Medium.Thump(c, 0f, 0.3f, 0.8f);
            c.Medium.Tick(c, 0.04f, 0.3f, 0.6f);
            c.Medium.Whoosh(c, 0f, 0.18f, 0.2f, 0.5f, 0.4f);
        }).Tagged("born").Vary(2, 1.5f, 2f).Voices(3, 0.06f, 30).Level(-8f, -12f);

        Row(Boss, "spawn", 0.7f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 1f, 1f);
            c.Medium.Sweep(c, 0f, 0.5f, T(c, -12f), T(c, -5f), 0.6f);
            c.Medium.Tone(c, 0.35f, T(c, -12f), 0.3f, 0.8f, ToneKind.Low);
        }).Vary(1, 0f, 0.5f).Voices(1, 0.3f, 85).Level(0f, -6f).Keyed();

        SfxRecipe jump = (c, _, tag) =>
        {
            switch (tag)
            {
                case 1:     // a jump in the air
                    c.Medium.Whoosh(c, 0f, 0.12f, 0.5f, 0.95f, 0.9f);
                    c.Medium.Tick(c, 0f, 0.7f, 0.35f);
                    break;
                case 2:     // a stroke in water or leaves
                    Puff(c, 0f, 0.14f, Tex.Water, Env.Swell(0.02f, 0.03f, 0.09f), 0.9f);
                    break;
                default:
                    c.Medium.Whoosh(c, 0f, 0.13f, 0.3f, 0.75f, 0.9f);
                    c.Medium.Tick(c, 0f, 0.2f, 0.25f);
                    break;
            }
        };
        // One row for every body: whoever is not the player is simply quieter.
        Row(Any, "jump", 0.15f, jump).Tagged("air", "swim").Vary(2, 1f, 1.5f).Voices(3, 0.06f, 30).Level(-6f, -14f).Others(-6f);

        SfxRecipe land = (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 0.5f, 1f);
            c.Medium.Whoosh(c, 0f, 0.07f, 0.5f, 0.2f, 0.3f);
        };
        Row(Any, "land", 0.2f, land).Vary(2, 1.5f, 2.5f).Voices(3, 0.06f, 30).Level(-5f, -14f).ByMagnitude(900f).Others(-7f)
            .When(FeedbackState.Submerged, -3f, -8f);

        Row(Any, "death", 0.78f, (c, _, _) =>
        {
            c.Medium.Sweep(c, 0f, 0.45f, T(c, 7f), T(c, -5f), 0.7f);
            c.Medium.Thump(c, 0.4f, 0.8f, 0.9f);
            c.Medium.Tone(c, 0.42f, T(c, -12f), 0.35f, 0.6f, ToneKind.Low);
        }).Vary(1, 0f, 0.5f).Voices(1, 0.2f, 85).Level(0f, -6f).Keyed();

        Row(Foe, "death", 0.3f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.6f, 0.8f);
            c.Medium.Whoosh(c, 0.01f, 0.2f, 0.7f, 0.2f, 0.7f);
            c.Medium.Thump(c, 0.03f, 0.3f, 0.5f);
        }).Vary(3, 1.5f, 2f).Voices(3, 0.04f, 50).Level(-3f).When(BoardFlag.Boss, 0f, -3f);

        Row(Boss, "death", 0.95f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 1f, 1f);
            c.Medium.Sweep(c, 0f, 0.8f, T(c), T(c, -12f), 0.7f);
            for (int i = 0; i < 3; i++) c.Medium.Tick(c, 0.1f + 0.2f * i, 0.8f - 0.2f * i, 0.5f);
        }).Vary(1, 0f, 0f).Voices(1, 0.5f, 95).Level(0f, -4f).Keyed();

        Row(Objective, "death", 0.45f, (c, _, _) =>
        {
            float[] when = { 0f, 0.05f, 0.11f, 0.19f };
            for (int i = 0; i < when.Length; i++) c.Medium.Tick(c, when[i] * c.Rng.Range(0.9f, 1.1f), 0.8f - 0.2f * i, 0.6f);
            c.Medium.Thump(c, 0.05f, 0.7f, 0.9f);
            c.Medium.Whoosh(c, 0f, 0.3f, 0.4f, 0.1f, 0.5f);
        }).OfRealm().Vary(1, 1f, 1f).Voices(2, 0.1f, 70).Level(-1f, -6f);

        Row(Any, "respawn", 0.52f, (c, _, _) =>
        {
            c.Medium.Sweep(c, 0f, 0.3f, T(c, -12f), T(c), 0.6f);
            c.Medium.Tone(c, 0.28f, T(c), 0.2f, 0.7f);
            c.Medium.Tone(c, 0.36f, T(c, 7f), 0.2f, 0.6f);
        }).Vary(1, 0f, 0.5f).Voices(1, 0.3f, 80).Level(-3f).Keyed();

        Row(Any, "heal", 0.34f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, T(c), 0.2f, 0.6f);
            c.Medium.Tone(c, 0.12f, T(c, 7f), 0.2f, 0.7f);
        }).Vary(1, 0f, 0.5f).Voices(1, 0.2f, 60).Level(-8f, -12f).Keyed();

        Row(Any, "hurt.tick", 0.1f, (c, _, tag) =>
        {
            switch (tag)
            {
                case 1:             // burn, on_fire
                    Puff(c, 0f, 0.09f, Tex.Fire, Env.Perc(0.006f, 0.08f), 0.9f);
                    c.Medium.Tick(c, 0f, 0.3f, 0.3f);
                    break;
                case 2:             // frozen
                    Puff(c, 0f, 0.09f, Tex.Ice, Env.Perc(0.004f, 0.08f), 0.9f);
                    c.Medium.Tick(c, 0f, 0.9f, 0.3f);
                    break;
                default:
                    c.Medium.Tick(c, 0f, 0.4f, 0.5f);
                    c.Medium.Thump(c, 0f, 0.15f, 0.4f);
                    break;
            }
        }).Tagged("burn|on_fire", "frozen").OfRealm().Vary(2, 1.5f, 1.5f).Voices(2, 0.12f, 35).Level(-6f, -12f).Others(-6f);

        Row(Any, "ult.ready", 0.62f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, T(c), 0.2f, 0.6f);
            c.Medium.Tone(c, 0.1f, T(c, 7f), 0.2f, 0.7f);
            c.Medium.Tone(c, 0.2f, T(c, 12f), 0.4f, 0.8f, ToneKind.Accent);
        }).Vary(1, 0f, 0f).Voices(1, 1f, 80).Level(-3f, -8f).Keyed();

        Row(Any, "switch.out", 0.22f, (c, _, _) =>
        {
            c.Medium.Whoosh(c, 0f, 0.18f, 0.7f, 0.2f, 0.8f);
            c.Medium.Tick(c, 0f, 0.4f, 0.4f);
        }).Vary(1, 0.5f, 1f).Voices(1, 0.1f, 65).Level(-6f, -12f);

        Row(Any, "switch.in", 0.3f, (c, _, _) =>
        {
            c.Medium.Whoosh(c, 0f, 0.16f, 0.2f, 0.8f, 0.8f);
            c.Medium.Tone(c, 0.12f, T(c, 12f), 0.2f, 0.7f);
        }).Vary(1, 0f, 0.5f).Voices(1, 0.1f, 70).Level(-4f, -10f).Keyed();

        Row(Foe, "aggro", 0.22f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.7f, 0.7f);
            c.Medium.Tick(c, 0.07f, 0.9f, 0.7f);
            c.Medium.Sweep(c, 0.02f, 0.16f, T(c, -12f), T(c, -5f), 0.5f);
        }).Vary(2, 1f, 1.5f).Voices(2, 0.15f, 55).Level(-8f, -12f);

        Row(Foe, "evolve", 0.5f, (c, _, _) =>
        {
            c.Medium.Sweep(c, 0f, 0.35f, T(c, -12f), T(c, 7f), 0.7f);
            c.Medium.Thump(c, 0.3f, 0.7f, 0.9f);
            c.Medium.Tone(c, 0.32f, T(c), 0.2f, 0.6f, ToneKind.Accent);
        }).Vary(1, 0f, 1f).Voices(2, 0.2f, 60).Level(-5f, -9f).Keyed();

        Row(Objective, "hurt", 0.24f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, T(c, 12f), 0.15f, 0.7f, ToneKind.Accent);
            c.Medium.Tone(c, 0.09f, T(c, 13f), 0.12f, 0.5f);
            c.Medium.Tick(c, 0f, 0.8f, 0.5f);
        }).OfRealm().Vary(1, 0f, 0.5f).Voices(1, 0.3f, 75).Level(-5f, -9f).Keyed();

        // Status loops: the status is the realm's doing, so the realm's medium plays it.
        Row(Any, "status.burning", 1f, (c, _, _) => c.Medium.Texture(c, 0f, c.Body, Tex.Fire, 1f))
            .OfRealm().Looping().Voices(2, 0f, 40).Level(-14f, -12f).Others(-5f);
        Row(Any, "status.on_fire", 1f, (c, _, _) =>
        {
            c.Medium.Texture(c, 0f, c.Body, Tex.Fire, 1f);
            c.Medium.Texture(c, 0f, c.Body, Tex.Buzz, 0.25f);
        }).OfRealm().Looping().Voices(2, 0f, 40).Level(-14f, -12f).Others(-5f);
        Row(Any, "status.frozen", 1f, (c, _, _) => c.Medium.Texture(c, 0f, c.Body, Tex.Ice, 1f))
            .OfRealm().Looping().Voices(2, 0f, 40).Level(-15f, -12f).Others(-5f);
        Row(Any, "status.trapped", 1.0f, (c, _, _) => c.Medium.Texture(c, 0f, c.Body, Tex.Buzz, 1f))
            .OfRealm().Looping().Voices(2, 0f, 45).Level(-14f, -12f).Others(-5f);
        Row(Any, "status.stunned", 1f, (c, _, _) => c.Medium.Texture(c, 0f, c.Body, Tex.Daze, 1f))
            .OfRealm().Looping().Voices(2, 0f, 45).Level(-15f, -12f).Others(-5f);

        Row(Foe, "telegraph", 0.5f, (c, _, _) =>
        {
            c.Medium.Texture(c, 0f, c.Body, Tex.Buzz, 0.5f);
            for (float at = 0f; at < c.Body; at += 0.5f) c.Medium.Sweep(c, at, 0.45f, T(c), T(c, 7f), 0.6f);
        }).Looping(0.03f, 0.08f).Voices(2, 0f, 90).Level(-8f, -9f);
    }

    // ── Hits: what struck, and what was struck (Feedback.gdd §6.1) ──────────────────
    private static void Hits()
    {
        // The attack of a kit that names nothing yet — and the fallback of every named one.
        Attack(Any, "", 0.16f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.7f, 0.8f);
            c.Medium.Thump(c, 0.002f, 0.35f, 0.8f);
            c.Medium.Whoosh(c, 0f, 0.06f, 0.8f, 0.5f, 0.35f);
        });

        Attack(Foe, "contact", 0.16f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 0.45f, 0.9f);
            c.Medium.Tick(c, 0f, 0.4f, 0.6f);
        }).Vary(3, 1.5f, 2f);

        Attack(Hazard, "tsunami", 0.9f, (c, _, _) =>
        {
            Puff(c, 0f, 0.85f, Tex.Water, Env.Swell(0.3f, 0.1f, 0.45f), 0.9f);
            c.Medium.Thump(c, 0.3f, 1f, 1f);
            c.Medium.Whoosh(c, 0f, 0.6f, 0.2f, 0.7f, 0.8f);
        }).OfRealm().Vary(1, 0.5f, 1f).Voices(1, 0.3f, 90).Level(0f, -4f);

        // The extra weight under a Heavy or Massive blow.
        Row(Any, HitHeavy, 0.3f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 1f, 1f);
            c.Medium.Whoosh(c, 0f, 0.12f, 0.3f, 0.1f, 0.4f);
        }).Vary(2, 1f, 1f).Voices(2, 0.08f, 78).Level(-2f, -6f);

        // The victim's reaction, in the victim's own medium.
        Row(Fighter, HitTaken, 0.22f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 0.5f, 0.9f);
            c.Medium.Tone(c, 0.005f, T(c, -5f), 0.12f, 0.5f);
            c.Medium.Whoosh(c, 0f, 0.08f, 0.6f, 0.3f, 0.3f);
        }).Vary(2, 0f, 1f).Voices(1, 0.12f, 85).Level(-2f, -6f).Keyed().When(FeedbackState.LowHp, -2f, 2f);

        Row(Foe, HitTaken, 0.14f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.5f, 0.7f);
            c.Medium.Thump(c, 0f, 0.25f, 0.6f);
        }).Vary(3, 1.5f, 2f).Voices(3, 0.03f, 50).Level(-6f, -9f);

        Row(Boss, HitTaken, 0.25f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 0.8f, 1f);
            c.Medium.Tick(c, 0f, 0.3f, 0.6f);
        }).Vary(3, 1f, 1.5f).Voices(2, 0.05f, 60).Level(-4f, -8f);

        Row(Objective, HitTaken, 0.16f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 0.4f, 0.8f);
            c.Medium.Tick(c, 0f, 0.2f, 0.7f);
            c.Medium.Tick(c, 0.03f, 0.5f, 0.3f);
        }).OfRealm().Vary(3, 1.5f, 2f).Voices(2, 0.04f, 45).Level(-6f, -9f);

        Row(Any, HitTaken, 0.14f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.4f, 0.6f);
            c.Medium.Thump(c, 0f, 0.3f, 0.6f);
        }).Vary(3, 1.5f, 2f).Voices(2, 0.04f, 40).Level(-8f, -9f);

        // Actions and the music: a hit is a note of the realm's scale, climbing with the combo;
        // a kill is the tonic.
        Row(Any, ComboNote, 0.3f, (c, _, _) => c.Medium.Tone(c, 0f, T(c, LadderCentre), 0.25f, 1f))
            .Vary(2, 0f, 0.5f).Voices(3, 0.05f, 55).Level(-10f, -9f).Laddered();

        Row(Any, ComboResolve, 0.5f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, T(c), 0.4f, 0.8f, ToneKind.Accent);
            c.Medium.Tone(c, 0f, T(c, 12f), 0.3f, 0.5f);
        }).Vary(2, 0f, 0.5f).Voices(2, 0.08f, 65).Level(-7f, -9f).Keyed();
    }

    // ── Pomegraknight — the reference kit (Feedback.gdd §6.3), in watercolour ───────
    private static void Pomegraknight()
    {
        const string pome = "pomegraknight";
        int[] climb = { 0, 2, 3 };      // degrees 1-3-5 of her pentatonic

        // Burning (her passive): a fizz of fire laid over whatever she does while it is on.
        SfxDef fizz = Row(pome, "burn.fizz", 0.2f, (c, _, _) => Puff(c, 0.005f, 0.19f, Tex.Fire, Env.Perc(0.012f, 0.16f), 1f))
            .Vary(2, 1f, 1f).Voices(2, 0.03f, 60).Level(-7f, -12f);

        // A slash: a wash of air and a blade's ring; the three stages climb, the third lands
        // on a mallet. Burning (her passive) adds the fizz.
        Row(pome, "ba.swing", 0.2f, (c, stage, tag) =>
        {
            c.Medium.Whoosh(c, 0f, 0.09f + 0.025f * stage, 0.25f, 0.9f, 0.9f);
            float ring = Dsp.MidiToHz(c.Deg(climb[stage]) + 24f);
            Gen.Fm(c.Buf, c.Sr, c.S(0.02f), c.S(0.13f), ring, 3.5f, 2.5f, 0.05f, Env.Perc(0.008f, 0.10f), 0.3f);
            if (stage == 2) c.Medium.Tone(c, 0.03f, c.Deg(3) + 12f, 0.2f, 0.5f, ToneKind.Accent);
        }).Staged(3).Layered("burning", fizz).Vary(2, 0.7f, 1f).Voices(2, 0.04f, 70).Level(-3f, -9f).Keyed();

        Attack(pome, "ba", 0.18f, (c, stage, _) =>
        {
            c.Medium.Thump(c, 0f, 0.3f + 0.2f * stage, 0.8f);
            c.Medium.Tick(c, 0f, 0.8f, 0.6f);
            Gen.Fm(c.Buf, c.Sr, c.S(0.002f), c.S(0.07f), Dsp.MidiToHz(c.Deg(climb[stage]) + 24f), 3.5f, 3f, 0.03f, Env.Perc(0.008f, 0.06f), 0.25f);
            if (stage == 2) c.Medium.Tone(c, 0f, c.Deg(0) + 12f, 0.2f, 0.4f, ToneKind.Accent);
        }).Staged(3).Vary(2, 0.7f, 1.5f).Keyed();

        // Blush: a fire whoosh and a warm swell.
        Row(pome, "s1.cast", 0.7f, (c, _, _) =>
        {
            c.Medium.Whoosh(c, 0f, 0.45f, 0.2f, 0.8f, 0.8f);
            Puff(c, 0.05f, 0.6f, Tex.Fire, Env.Swell(0.15f, 0.2f, 0.25f), 0.6f);
            c.Medium.Sweep(c, 0f, 0.5f, T(c, -12f), T(c), 0.5f);
        }).Vary(1, 0f, 0.5f).Voices(1, 0.3f, 80).Level(-1f, -6f).Keyed();

        // The Fire Tornado: a rising spiral of air, hatched, over a low roll.
        Row(pome, "s1.tornado", 1.6f, (c, _, _) =>
        {
            float[] spiral = new float[c.Buf.Length];
            for (float at = 0f; at < c.Body; at += 0.4f)
                Gen.Noise(spiral, c.Sr, c.S(at), c.S(0.46f), Tint.Pink, Band.Mid, 500f, 2600f, 1.6f, Env.Swell(0.08f, 0.26f, 0.12f), 1f, ref c.Rng, 12f, 0.5f);
            float peak = Finish.Peak(spiral);
            for (int i = 0; i < spiral.Length; i++) c.Buf[i] += spiral[i] / MathF.Max(peak, 1e-6f) * 0.8f;
            for (float at = 0f; at < c.Body; at += 0.0625f) c.Medium.Thump(c, at, 0.3f, 0.3f);
        }).Looping(0.1f, 0.25f).Voices(1, 0f, 72).Level(-8f, -9f);

        Row(pome, "s1.tornado.tick", 0.12f, (c, _, _) =>
        {
            Puff(c, 0f, 0.1f, Tex.Fire, Env.Perc(0.008f, 0.08f), 0.7f);
            c.Medium.Tick(c, 0f, 0.5f, 0.4f);
            c.Medium.Tone(c, 0f, T(c, 12f), 0.1f, 0.35f);
        }).Vary(2, 0f, 1f).Voices(2, 0.05f, 45).Level(-6f, -10f).Keyed(0, 1);      // each tick a step up the realm's scale

        Attack(pome, "s1.tornado", 0.1f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.6f, 0.6f);
            c.Medium.Thump(c, 0f, 0.15f, 0.5f);
        }).Vary(2, 1.5f, 1.5f).Voices(2, 0.06f, 40).Level(-8f, -10f);

        Row(pome, "s2.cast", 0.3f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.3f, 0.8f);
            c.Medium.Tick(c, 0.06f, 0.6f, 0.7f);
            c.Medium.Sweep(c, 0.05f, 0.18f, T(c), T(c, 12f), 0.5f);
            c.Medium.Whoosh(c, 0.05f, 0.2f, 0.3f, 0.8f, 0.5f);
        }).Layered("burning", fizz).Vary(1, 0f, 0.5f).Voices(1, 0.1f, 80).Level(-3f, -8f).Keyed();

        // A wave of seeds is ONE sound: six, eight or ten woodblock pops in a pentatonic rattle.
        Row(pome, "s2.wave", 0.3f, (c, stage, tag) =>
        {
            int seeds = 6 + 2 * stage;
            for (int i = 0; i < seeds; i++)
            {
                float at = i * (0.17f / seeds) + c.Rng.Range(0f, 0.006f);
                float hz = Dsp.MidiToHz(c.Deg(c.Rng.Int(5) + (i & 1) * 2) + 12f);
                Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.1f), hz, SeedWood, 0.5f, 0.006f);
            }
            c.Medium.Whoosh(c, 0f, 0.2f, 0.4f, 0.9f, 0.4f);
        }).Staged(3).Layered("burning", fizz).Vary(1, 0f, 1f).Voices(2, 0.08f, 60).Level(-3f, -8f).Keyed();

        SfxDef seedBurst = Burst("pomegraknight.hit.s2.seed.burst", 0.22f, (c, _, _) =>
        {
            for (int i = 0; i < 6; i++)
            {
                float at = i * 0.03f + c.Rng.Range(0f, 0.008f);
                Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.1f), Dsp.MidiToHz(c.Deg(c.Rng.Int(5)) + 12f), SeedWood, 0.6f, 0.005f);
                c.Medium.Tick(c, at, 0.6f, 0.3f);
            }
            c.Medium.Thump(c, 0f, 0.2f, 0.5f);
        }).Keyed();

        Attack(pome, "s2.seed", 0.1f, (c, _, _) =>
        {
            Gen.Modal(c.Buf, c.Sr, 0, c.S(0.1f), Dsp.MidiToHz(c.Deg(c.Rng.Int(5)) + 12f), SeedWood, 0.7f, 0.005f);
            c.Medium.Tick(c, 0f, 0.6f, 0.4f);
            c.Medium.Thump(c, 0f, 0.1f, 0.4f);
        }).Vary(3, 0f, 2f).Voices(3, 0.03f, 45).Level(-5f, -10f).Keyed().Bursting(4, seedBurst);

        SfxDef expireBurst = Burst("pomegraknight.s2.seed.expire.burst", 0.2f, (c, _, _) =>
        {
            for (int i = 0; i < 5; i++) c.Medium.Tick(c, i * 0.035f + c.Rng.Range(0f, 0.01f), c.Rng.Range(0.2f, 0.5f), 0.6f);
        }).Level(-12f, -12f);

        // A seed that dies on terrain: class `projectile`, no body — the quietest thing she makes.
        Row(pome, "s2.seed.expire", 0.07f, (c, _, tag) =>
        {
            c.Medium.Tick(c, 0f, 0.3f, 0.7f);
        }).Vary(2, 2f, 2f).Voices(2, 0.05f, 25).Level(-12f, -12f).Bursting(4, expireBurst);

        Row(pome, "ult.ready", 0.7f, (c, _, _) =>
        {
            int[] motif = MotifOf(pome);
            for (int i = 0; i < motif.Length; i++)
                c.Medium.Tone(c, i * 0.12f, T(c, motif[i]), 0.3f, 0.7f, i == motif.Length - 1 ? ToneKind.Accent : ToneKind.Short);
        }).Vary(1, 0f, 0f).Voices(1, 1f, 80).Level(-3f, -8f).Keyed();
    }

    // ── PumpKing's head (Feedback.gdd §6.3), in scratchboard ────────────────────────
    private static void PumpKing()
    {
        const string pump = "pumpking";

        Row(pump, "ba.explode", 0.5f, (c, _, tag) =>
        {
            if (tag == 1)       // a dud: the head only pops
            {
                c.Medium.Tick(c, 0f, 0.5f, 0.7f);
                c.Medium.Whoosh(c, 0f, 0.1f, 0.6f, 0.4f, 0.4f);
                return;
            }
            c.Medium.Thump(c, 0f, 1f, 1f);                              // the hollow gourd
            c.Medium.Whoosh(c, 0f, 0.3f, 0.9f, 0.3f, 0.9f);             // the blast, scratched
            for (int i = 0; i < 4; i++)                                 // and the wet splat
                Gen.Osc(c.Buf, c.Sr, c.S(0.03f + 0.04f * i + c.Rng.Range(0f, 0.01f)), c.S(0.08f), Wave.Sine,
                    c.Rng.Range(700f, 1000f), 220f, Env.Perc(0.002f, 0.07f), 0.3f);
            c.Medium.Tone(c, 0f, T(c), 0.3f, 0.4f, ToneKind.Accent);    // red: the tritone stab
        }).Tagged("dud").Vary(2, 0.5f, 1f).Voices(2, 0.1f, 85).Level(0f, -4f).Keyed();

        Attack(pump, "ba.head", 0.2f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 0.7f, 1f);
            c.Medium.Tick(c, 0f, 0.3f, 0.5f);
        }).Vary(3, 1f, 1.5f);

        Attack(pump, "ba.explode", 0.2f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 0.9f, 0.9f);
            for (int i = 0; i < 2; i++)
                Gen.Osc(c.Buf, c.Sr, c.S(0.01f + 0.04f * i), c.S(0.08f), Wave.Sine, c.Rng.Range(700f, 1000f), 220f, Env.Perc(0.002f, 0.07f), 0.3f);
        }).Vary(2, 1f, 1.5f).Voices(2, 0.05f, 70);

        Row(pump, "ult.ready", 0.8f, (c, _, _) =>
        {
            int[] motif = MotifOf(pump);
            for (int i = 0; i < motif.Length; i++)
                c.Medium.Tone(c, i * 0.16f, T(c, motif[i]), 0.35f, 0.7f, i == 1 ? ToneKind.Accent : ToneKind.Short);
        }).Vary(1, 0f, 0f).Voices(1, 1f, 80).Level(-3f, -8f).Keyed();
    }

    // ── The beach's foes: crab, seagull, and the stand-in boss crab, in watercolour ─
    private static void BeachFoes()
    {
        foreach ((string id, float size) in new[] { ("crab", 1f) })
        {
            Attack(id, "contact", 0.16f, (c, _, _) =>
            {
                float scale = 1f / MathF.Sqrt(c.Size);
                Gen.Modal(c.Buf, c.Sr, 0, c.S(0.08f), c.Rng.Jitter(1800f, 0.05f) * scale, Castanet, 0.7f, 0.004f);
                Gen.Modal(c.Buf, c.Sr, c.S(0.035f), c.S(0.08f), c.Rng.Jitter(2100f, 0.05f) * scale, Castanet, 0.6f, 0.004f);
                c.Medium.Thump(c, 0.03f, 0.3f, 0.5f);
            }).Sized(size).Vary(3, 1f, 1.5f);

            Row(id, HitTaken, 0.12f, (c, _, _) =>
            {
                Gen.Modal(c.Buf, c.Sr, 0, c.S(0.1f), c.Rng.Jitter(700f, 0.06f) / MathF.Sqrt(c.Size), Shell, 0.8f, 0.004f);
                c.Medium.Tick(c, 0f, 0.5f, 0.4f);
            }).Sized(size).Vary(3, 1.5f, 2f).Voices(3, 0.03f, 50).Level(-6f, -9f);

            Row(id, "aggro", 0.25f, (c, _, _) =>
            {
                for (int i = 0; i < 5; i++) c.Medium.Tick(c, i * 0.04f + c.Rng.Range(0f, 0.008f), (i & 1) == 0 ? 0.3f : 0.6f, 0.6f);
            }).Sized(size).Vary(3, 1f, 1.5f).Voices(2, 0.15f, 55).Level(-8f, -12f);

            Row(id, "death", 0.35f, (c, _, _) =>
            {
                float scale = 1f / MathF.Sqrt(c.Size);
                Gen.Modal(c.Buf, c.Sr, 0, c.S(0.12f), 500f * scale, Shell, 0.9f, 0.004f);
                c.Medium.Tick(c, 0.01f, 0.7f, 0.5f);
                Gen.Osc(c.Buf, c.Sr, c.S(0.12f), c.S(0.1f), Wave.Sine, 400f * scale, 900f * scale, Env.Perc(0.008f, 0.08f), 0.4f);
                c.Medium.Whoosh(c, 0.02f, 0.2f, 0.6f, 0.2f, 0.4f);
            }).Sized(size).Vary(3, 1.5f, 2f).Voices(3, 0.04f, size > 1f ? 85 : 50).Level(size > 1f ? 0f : -3f);
        }

        const string gull = "seagull";
        Row(gull, "aggro", 0.3f, (c, _, _) =>
        {
            float cry = c.Rng.Jitter(1400f, 0.05f), fall = c.Rng.Jitter(0.5f, 0.1f);
            Gen.Fm(c.Buf, c.Sr, 0, c.S(0.24f), cry, 1.5f, 3f, 0.3f, Env.Swell(0.012f, 0.1f, 0.12f), 0.6f, cry * fall, 1f);
            Gen.Fm(c.Buf, c.Sr, 0, c.S(0.24f), cry * 2f, 1.5f, 2f, 0.3f, Env.Swell(0.012f, 0.08f, 0.1f), 0.2f, cry * 2f * fall, 0.5f);
        }).Vary(3, 1f, 1.5f).Voices(2, 0.15f, 55).Level(-8f, -12f);

        // The dash telegraph: a whistle that keeps rising — the one sound that must be heard.
        Row(gull, "telegraph", 0.5f, (c, _, _) =>
        {
            for (float at = 0f; at < c.Body; at += 0.5f)
                Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(0.48f), Wave.Sine, 900f, 2200f, Env.Swell(0.03f, 0.37f, 0.08f), 0.7f, vib: new Vib(9f, 0.15f), phase: c.Rng.Next());
        }).Looping(0.03f, 0.08f).Voices(2, 0f, 90).Level(-7f, -9f);

        Row(gull, "death", 0.4f, (c, _, _) =>
        {
            Gen.Fm(c.Buf, c.Sr, 0, c.S(0.2f), c.Rng.Jitter(1200f, 0.05f), 1.5f, 3f, 0.2f, Env.Swell(0.012f, 0.06f, 0.12f), 0.6f, c.Rng.Jitter(400f, 0.1f), 1f);
            c.Medium.Whoosh(c, 0.1f, 0.25f, 0.6f, 0.2f, 0.5f);
        }).Vary(3, 1.5f, 2f).Voices(3, 0.04f, 50).Level(-3f);

        Row(gull, HitTaken, 0.14f, (c, _, _) =>
        {
            Gen.Fm(c.Buf, c.Sr, 0, c.S(0.09f), c.Rng.Jitter(1500f, 0.05f), 1.5f, 2.5f, 0.1f, Env.Swell(0.008f, 0.03f, 0.05f), 0.6f, c.Rng.Jitter(1100f, 0.06f), 0.8f);
            c.Medium.Tick(c, 0f, 0.5f, 0.4f);
        }).Vary(3, 1.5f, 2f).Voices(3, 0.03f, 50).Level(-6f, -9f);

        Attack(gull, "contact", 0.14f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.9f, 0.8f);
            c.Medium.Thump(c, 0f, 0.2f, 0.5f);
        }).Vary(3, 1.5f, 2f);

        Attack(gull, "s2.dash", 0.25f, (c, _, _) =>
        {
            c.Medium.Whoosh(c, 0f, 0.18f, 0.9f, 0.3f, 0.8f);
            c.Medium.Thump(c, 0.1f, 0.6f, 0.9f);
            c.Medium.Tick(c, 0.1f, 0.9f, 0.6f);
        }).Vary(3, 1f, 1.5f).Voices(2, 0.05f, 80);

        Attack(gull, "s1.poop", 0.2f, (c, _, _) =>
        {
            Puff(c, 0f, 0.12f, Tex.Water, Env.Perc(0.008f, 0.09f), 0.8f);
            Gen.Osc(c.Buf, c.Sr, 0, c.S(0.12f), Wave.Sine, 500f, 150f, Env.Perc(0.008f, 0.1f), 0.6f);
        }).Vary(3, 1.5f, 2f).Voices(2, 0.05f, 60);
    }

    // ── The world and its hazards (Feedback.gdd §6.4): always the realm's medium ────
    private static void WorldAndHazards()
    {
        Row(World, "soft.enter", 0.4f, (c, _, tag) =>
        {
            switch (tag)
            {
                case 1:     // water: a splash, a body going under, bubbles
                    Puff(c, 0f, 0.3f, Tex.Water, Env.Perc(0.01f, 0.25f), 0.9f);
                    c.Medium.Thump(c, 0f, 0.4f, 0.5f);
                    for (int i = 0; i < 3; i++) c.Medium.Tick(c, 0.08f + 0.07f * i, 0.2f + 0.2f * i, 0.35f);
                    break;
                case 2:     // foliage: a rustle in three strokes
                    for (int i = 0; i < 3; i++) Puff(c, 0.06f * i, 0.12f, Tex.Air, Env.PressLift(0.11f, 0.2f, 1.5f), 0.7f - 0.15f * i);
                    c.Medium.Tick(c, 0.02f, 0.5f, 0.3f);
                    break;
                default:    // a kit's own volume, a cloud, anything unnamed: a puff
                    c.Medium.Whoosh(c, 0f, 0.2f, 0.3f, 0.5f, 0.6f);
                    break;
            }
        }).Tagged("water", "foliage").OfRealm().Vary(2, 1.5f, 2f).Voices(3, 0.08f, 35).Level(-5f, -10f).ByMagnitude(600f).Others(-6f);

        Row(World, "soft.exit", 0.3f, (c, _, tag) =>
        {
            switch (tag)
            {
                case 1:
                    Puff(c, 0f, 0.2f, Tex.Water, Env.Perc(0.01f, 0.15f), 0.6f);
                    c.Medium.Whoosh(c, 0f, 0.15f, 0.4f, 0.8f, 0.5f);
                    for (int i = 0; i < 2; i++) c.Medium.Tick(c, 0.12f + 0.08f * i, 0.7f, 0.3f);
                    break;
                case 2:
                    for (int i = 0; i < 2; i++) Puff(c, 0.07f * i, 0.12f, Tex.Air, Env.PressLift(0.11f, 0.2f, 1.5f), 0.6f - 0.2f * i);
                    break;
                default:
                    c.Medium.Whoosh(c, 0f, 0.15f, 0.5f, 0.3f, 0.5f);
                    break;
            }
        }).Tagged("water", "foliage").OfRealm().Vary(2, 1.5f, 2f).Voices(3, 0.08f, 30).Level(-8f, -10f).ByMagnitude(600f).Others(-6f);

        // A hazard's tick: the same hazard is a different thing in every realm — a Dungeon
        // spike is struck iron, a Forgotten City spike is three chip notes.
        Row(Hazard, "tick", 0.2f, (c, _, tag) =>
        {
            switch (tag)
            {
                case 1:     // fire
                    Puff(c, 0f, 0.14f, Tex.Fire, Env.Perc(0.005f, 0.12f), 0.9f);
                    c.Medium.Tick(c, 0f, 0.4f, 0.3f);
                    break;
                case 2:     // freeze: a shimmer and a crack
                    Puff(c, 0f, 0.16f, Tex.Ice, Env.Perc(0.004f, 0.14f), 0.8f);
                    c.Medium.Tick(c, 0f, 1f, 0.6f);
                    c.Medium.Tick(c, 0.03f, 0.8f, 0.4f);
                    break;
                case 3:     // lava: a low bubble
                    Puff(c, 0f, 0.18f, Tex.Rumble, Env.Swell(0.02f, 0.06f, 0.1f), 0.9f);
                    c.Medium.Thump(c, 0f, 0.5f, 0.6f);
                    break;
                case 4:     // spike
                    c.Medium.Tick(c, 0f, 1f, 0.8f);
                    c.Medium.Tone(c, 0f, c.Deg(0) + 19f, 0.12f, 0.5f);
                    c.Medium.Whoosh(c, 0f, 0.05f, 0.9f, 1f, 0.3f);
                    break;
                case 5:     // poop
                    Puff(c, 0f, 0.12f, Tex.Water, Env.Perc(0.008f, 0.09f), 0.8f);
                    c.Medium.Thump(c, 0f, 0.2f, 0.5f);
                    break;
                default:
                    c.Medium.Thump(c, 0f, 0.4f, 0.8f);
                    c.Medium.Tick(c, 0f, 0.2f, 0.4f);
                    break;
            }
        }).Tagged("fire", "freeze", "lava", "spike", "poop", "damage").OfRealm().Vary(2, 1.5f, 1.5f).Voices(2, 0.1f, 40).Level(-5f, -10f).Others(-5f);

        // The realm's air, on the Ambience bus.
        Row(World, Ambience, 2.4f, (c, _, _) => c.Medium.Texture(c, 0f, c.Body, AmbienceOf(c.Medium), 1f))
            .OfRealm().Looping(1.2f, 1.2f).On(AudioBusId.Ambience).Voices(1, 0f, 10).Level(-6f, -14f);

        // Low HP: a pulse under everything, in the realm's own low voice.
        Row(Any, Heartbeat, 1f, (c, _, _) =>
        {
            for (float at = 0f; at < c.Body; at += 1f)
            {
                c.Medium.Thump(c, at, 0.6f, 0.9f);
                c.Medium.Thump(c, at + 0.22f, 0.4f, 0.6f);
            }
        }).OfRealm().Looping(0.3f, 0.6f).Voices(1, 0f, 88).Level(-9f, -9f);
    }

    // ── The flow of a mission and of a run, the screens, the UI ─────────────────────
    private static void FlowAndUi()
    {
        Row(Flow, "arena.enter", 0.55f, (c, _, _) =>
        {
            c.Medium.Whoosh(c, 0f, 0.35f, 0.2f, 0.7f, 0.5f);
            c.Medium.Tone(c, 0.25f, c.Deg(0), 0.3f, 0.6f, ToneKind.Long);
        }).OfRealm().Vary(1, 0f, 0.5f).Voices(1, 0.5f, 70).Level(-8f, -9f);

        // 3 · 2 · 1 climb the realm's scale; GO is the octave.
        Row(Flow, "countdown.tick", 0.2f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.5f, 0.6f);
            c.Medium.Tone(c, 0f, c.Deg(0) + 12f, 0.15f, 0.7f);
        }).OfRealm().On(AudioBusId.Ui).Vary(1, 0f, 0f).Voices(2, 0.2f, 85).Level(-4f, -9f).Keyed(3, -1);

        Row(Flow, "countdown.go", 0.36f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, c.Deg(0) + 12f, 0.3f, 0.9f, ToneKind.Accent);
            c.Medium.Thump(c, 0f, 0.5f, 0.7f);
            c.Medium.Whoosh(c, 0f, 0.2f, 0.4f, 0.9f, 0.4f);
        }).OfRealm().On(AudioBusId.Ui).Vary(1, 0f, 0f).Voices(1, 0.3f, 88).Level(-2f, -8f).Keyed(4);

        Row(Flow, "mission.start", 0.8f, (c, _, _) => MotifRun(c, 0f, 0.14f, 0.8f, 0, false, 3))
            .OfRealm().On(AudioBusId.Music).Vary(1, 0f, 0f).Voices(1, 0.5f, 90).Level(0f, -8f).Quantized(Quantize.Grid);

        Row(Flow, "mission.success", 1.7f, (c, _, _) =>
        {
            MotifRun(c, 0f, 0.16f, 0.8f, 1);
            float end = c.Motif.Length * 0.16f;
            c.Medium.Tone(c, end, c.Deg(0) + 12f, 0.7f, 0.8f, ToneKind.Long);
            c.Medium.Tone(c, end, c.Deg(0), 0.7f, 0.6f, ToneKind.Low);
            c.Medium.Tick(c, end, 0.9f, 0.4f);
        }).OfRealm().On(AudioBusId.Music).Vary(1, 0f, 0f).Voices(1, 1f, 95).Level(0f, -6f).Quantized(Quantize.Grid);

        Row(Flow, "mission.fail", 1.3f, (c, _, _) =>
        {
            int n = Math.Min(3, c.Motif.Length);
            for (int i = 0; i < n; i++)
                c.Medium.Tone(c, i * 0.2f, c.Deg(c.Motif[n - 1 - i]) - (i == n - 1 ? 12f : 0f), 0.3f, 0.7f, i == n - 1 ? ToneKind.Low : ToneKind.Short);
            c.Medium.Tone(c, n * 0.2f, c.Deg(-1), 0.4f, 0.6f, ToneKind.Low);      // it does not resolve
            c.Medium.Thump(c, n * 0.2f, 0.8f, 0.8f);
        }).OfRealm().On(AudioBusId.Music).Vary(1, 0f, 0f).Voices(1, 1f, 95).Level(0f, -6f).Quantized(Quantize.Grid);

        Row(Flow, "boss.intro", 1.75f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 1f, 1f);
            c.Medium.Sweep(c, 0f, 0.9f, c.Deg(0) - 12f, c.Deg(0) - 6f, 0.6f);
            c.Medium.Tone(c, 0.9f, c.Deg(0) - 12f, 0.8f, 0.8f, ToneKind.Low);
            c.Medium.Tone(c, 1.05f, c.Deg(1) - 12f, 0.6f, 0.5f, ToneKind.Low);
            c.Medium.Thump(c, 0.9f, 0.8f, 0.8f);
        }).OfRealm().On(AudioBusId.Music).Vary(1, 0f, 0f).Voices(1, 1f, 96).Level(0f, -5f).Quantized(Quantize.Bar);

        Row(Flow, "boss.defeated", 1.9f, (c, _, _) =>
        {
            c.Medium.Thump(c, 0f, 1f, 1f);
            MotifRun(c, 0.1f, 0.1f, 0.8f, 1);
            float end = 0.1f + c.Motif.Length * 0.1f;
            c.Medium.Tone(c, end, c.Deg(0) + 12f, 1.2f, 0.9f, ToneKind.Long);
            c.Medium.Tone(c, end, c.Deg(0), 1.2f, 0.7f, ToneKind.Low);
            for (int i = 0; i < 4; i++) c.Medium.Tick(c, end + 0.12f * i, 1f - 0.15f * i, 0.4f);
            c.Medium.Thump(c, end, 0.9f, 0.9f);
        }).OfRealm().On(AudioBusId.Music).Vary(1, 0f, 0f).Voices(1, 1f, 99).Level(0f, -4f);

        Row(Flow, "player.down", 1.3f, (c, _, _) =>
        {
            c.Medium.Sweep(c, 0f, 0.8f, c.Deg(0) + 7f, c.Deg(0) - 12f, 0.7f);
            c.Medium.Thump(c, 0.7f, 1f, 0.9f);
            c.Medium.Tone(c, 0.75f, c.Deg(0) - 12f, 0.5f, 0.7f, ToneKind.Low);
        }).OfRealm().On(AudioBusId.Music).Vary(1, 0f, 0f).Voices(1, 1f, 97).Level(0f, -5f);

        Row(Flow, "run.over", 2.4f, (c, _, tag) =>
        {
            if (tag == 1)       // victory: the motif twice, the second time higher, and home
            {
                MotifRun(c, 0f, 0.16f, 0.8f);
                float half = c.Motif.Length * 0.16f;
                MotifRun(c, half, 0.16f, 0.8f, 1);
                c.Medium.Tone(c, 2f * half, c.Deg(0) + 12f, 0.9f, 0.9f, ToneKind.Long);
                c.Medium.Tone(c, 2f * half, c.Deg(0), 0.9f, 0.7f, ToneKind.Low);
                return;
            }
            // A dirge: three slow steps down to the low tonic.
            for (int i = 0; i < 3; i++)
                c.Medium.Tone(c, i * 0.45f, c.Deg(2 - i) - (i == 2 ? 12f : 0f), 0.6f, 0.7f, i == 2 ? ToneKind.Low : ToneKind.Long);
            c.Medium.Thump(c, 0.9f, 1f, 0.8f);
            if (tag == 2) c.Medium.Sweep(c, 0.9f, 1.2f, c.Deg(0), c.Deg(0) - 24f, 0.5f);   // the ground goes
        }).Tagged("victory", "void_devoured").OfRealm().On(AudioBusId.Music).Lasting((_, tag) => tag == 1 ? 2.4f : tag == 2 ? 2.1f : 1.6f)
          .Vary(1, 0f, 0f).Voices(1, 1f, 94).Level(0f, -5f);

        // The mission's clock running out: a tick at 30, 20 and 10 s, then every second from 5.
        Row(Flow, "mission.time", 0.16f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.9f, 0.7f);
            c.Medium.Tone(c, 0f, c.Deg(0) + 12f, 0.1f, 0.5f);
        }).OfRealm().On(AudioBusId.Ui).Vary(1, 0f, 0f).Voices(1, 0.3f, 86).Level(-6f, -10f).Keyed();

        // ── The screens between fights: the storybook itself, in its home medium ────
        const string home = "VK";
        SfxRecipe Page(int degree, ToneKind kind = ToneKind.Short) => (c, _, _) =>
        {
            c.Medium.Whoosh(c, 0f, 0.3f, 0.6f, 0.3f, 0.5f);
            c.Medium.Tone(c, 0.18f, c.Deg(degree), 0.3f, 0.6f, kind);
        };
        Row(Flow, "menu.enter", 0.5f, Page(0, ToneKind.Long)).OfMedium(home).Vary(1, 0f, 0f).Voices(1, 0.3f, 60).Level(-8f, -10f);
        Row(Flow, "map.enter", 0.5f, Page(1)).OfMedium(home).Vary(1, 0f, 0.5f).Voices(1, 0.3f, 60).Level(-8f, -10f);
        Row(Flow, "shelter.enter", 0.5f, Page(2, ToneKind.Long)).OfMedium(home).Vary(1, 0f, 0.5f).Voices(1, 0.3f, 60).Level(-8f, -10f);
        Row(Flow, "event.enter", 0.5f, Page(3)).OfMedium(home).Vary(1, 0f, 0.5f).Voices(1, 0.3f, 60).Level(-8f, -10f);
        Row(Flow, "runover.enter", 0.6f, (c, _, tag) =>
        {
            c.Medium.Whoosh(c, 0f, 0.3f, 0.6f, 0.3f, 0.5f);
            if (tag == 1) c.Medium.Tone(c, 0.18f, c.Deg(0) + 12f, 0.4f, 0.7f, ToneKind.Accent);
            else c.Medium.Tone(c, 0.18f, c.Deg(0) - 12f, 0.4f, 0.6f, ToneKind.Low);
        }).Tagged("victory").OfMedium(home).Vary(1, 0f, 0f).Voices(1, 0.3f, 60).Level(-8f, -10f);

        Row(Flow, "map.travel", 0.3f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.3f, 0.6f);
            c.Medium.Whoosh(c, 0.02f, 0.18f, 0.4f, 0.6f, 0.5f);
            c.Medium.Tick(c, 0.2f, 0.5f, 0.7f);
        }).OfMedium(home).Vary(2, 1f, 1f).Voices(1, 0.08f, 55).Level(-8f, -12f);

        Row(Flow, "day.end", 1.2f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, c.Deg(0) + 12f, 0.6f, 0.7f, ToneKind.Long);
            c.Medium.Tone(c, 0.3f, c.Deg(3), 0.6f, 0.7f, ToneKind.Long);
            c.Medium.Whoosh(c, 0.5f, 0.5f, 0.6f, 0.2f, 0.3f);
        }).OfMedium(home).On(AudioBusId.Music).Vary(1, 0f, 0f).Voices(1, 0.5f, 90).Level(0f, -7f).Quantized(Quantize.Beat);

        // The VOID devouring: the VOID's own voice — it swells, is cut, and what it ate falls.
        Row(Flow, "void.devour", 2.2f, (c, _, tag) =>
        {
            c.Medium.Whoosh(c, 0f, 1.2f, 0.9f, 0.2f, 0.9f);
            c.Medium.Thump(c, 1.2f, 1f, 1f);
            c.Medium.Sweep(c, 1.2f, 0.9f, T(c, 12f), T(c), 0.6f);
            if (tag == 1)       // the singularity: the whole outer ring at once
            {
                c.Medium.Thump(c, 1.5f, 1f, 0.8f);
                c.Medium.Tone(c, 1.2f, T(c, 12f), 0.9f, 0.6f, ToneKind.Accent);
            }
        }).Tagged("singularity").OfMedium("VOID").Vary(1, 0f, 0.5f).Voices(1, 0.5f, 97).Level(0f, -4f).Ducking(-9f, 2.2f);

        // ── The UI (the storybook's own hand) ───────────────────────────────────────
        Row(Ui, "hover", 0.05f, (c, _, _) => c.Medium.Tick(c, 0f, 0.6f, 0.5f))
            .OfMedium(home).On(AudioBusId.Ui).Vary(2, 0.25f, 0.5f).Voices(2, 0.04f, 60).Level(-10f, -16f);
        Row(Ui, "press", 0.14f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.4f, 0.7f);
            c.Medium.Tone(c, 0f, c.Deg(0) + 12f, 0.1f, 0.5f);
        }).OfMedium(home).On(AudioBusId.Ui).Vary(2, 0.25f, 0.5f).Voices(2, 0.05f, 80).Level(-3f, -12f);
        Row(Ui, "back", 0.16f, (c, _, _) =>
        {
            c.Medium.Tick(c, 0f, 0.3f, 0.6f);
            c.Medium.Tone(c, 0f, c.Deg(0) + 7f, 0.1f, 0.45f);
            c.Medium.Tone(c, 0.05f, c.Deg(0), 0.1f, 0.4f);
        }).OfMedium(home).On(AudioBusId.Ui).Vary(2, 0.25f, 0.5f).Voices(2, 0.05f, 80).Level(-3f, -12f);
        Row(Ui, "pause.open", 0.3f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, c.Deg(0) + 12f, 0.15f, 0.6f);
            c.Medium.Tone(c, 0.09f, c.Deg(0) + 7f, 0.15f, 0.5f);
        }).OfMedium(home).On(AudioBusId.Ui).Vary(1, 0f, 0f).Voices(1, 0.1f, 85).Level(-3f, -12f);
        Row(Ui, "pause.close", 0.3f, (c, _, _) =>
        {
            c.Medium.Tone(c, 0f, c.Deg(0) + 7f, 0.15f, 0.5f);
            c.Medium.Tone(c, 0.09f, c.Deg(0) + 12f, 0.15f, 0.6f);
        }).OfMedium(home).On(AudioBusId.Ui).Vary(1, 0f, 0f).Voices(1, 0.1f, 85).Level(-3f, -12f);
    }
}

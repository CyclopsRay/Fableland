using System;
using System.Collections.Generic;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

/// <summary>What a recipe draws on while it renders one sound: the buffer, the medium whose
/// rules apply, that medium's home key, and the random source that makes this variant differ
/// from its siblings. Pure C# — it runs on a worker thread.</summary>
public sealed class RenderCtx
{
    public float[] Buf;
    public int Sr;
    public SynthRng Rng;
    public Medium Medium;
    /// <summary>MIDI note of the home tonic, and the home scale as semitones above it.</summary>
    public int Tonic;
    public int[] Scale;
    /// <summary>The home realm's motif, as scale degrees.</summary>
    public int[] Motif;
    /// <summary>Seconds of the buffer the recipe itself may fill; the rest is the medium's tail.</summary>
    public float Body;
    /// <summary>How big the thing making the sound is (1 = a fighter). Bigger is lower.</summary>
    public float Size = 1f;
    /// <summary>The recipe is fire: a medium that darkens everything leaves this one bright.</summary>
    public bool Bright;
    /// <summary>The recipe has a low body the medium's thinning must spare (a thump).</summary>
    public bool Low;

    public int S(float seconds) => (int)(seconds * Sr);
    /// <summary>MIDI note of a degree of the home scale (0 = the tonic).</summary>
    public float Deg(int degree) => Tonic + Dsp.ScaleSemis(Scale, degree);
}

/// <summary>What a sustained texture is of — each medium answers in its own material.</summary>
public enum Tex { Fire, Ice, Air, Water, Rumble, Buzz, Daze }

/// <summary>How a note is played: short, held, accented, or as a bass.</summary>
public enum ToneKind { Short, Long, Accent, Low }

/// <summary>
/// A realm is a synthesis medium, not just a scale (<c>Docs/Audio.gdd</c> §5). Each art
/// medium — watercolour, poster, ink, lit realism, linocut, gongbi, scratchboard, pixel art,
/// cloisonné, the VOID's black — fixes which generators may sound, how a note begins and
/// ends, and how much space surrounds it. Every sound in the game is built from the six
/// primitives below, so a jump, a hit or a hazard is the same EVENT in every world and a
/// different SOUND in each; a character carries its home medium wherever it fights.
///
/// A medium also has a home key (tonic + scale): the pitch its tonal sounds are rendered at,
/// and its realm's music is written in.
/// </summary>
public abstract class Medium
{
    /// <summary>Realm code: VK, SL, BM, HC, TD, PL, KH, TF, CC, VOID.</summary>
    public string Id { get; protected init; }
    /// <summary>The art medium, as the style bible names it.</summary>
    public string Name { get; protected init; }
    /// <summary>The sound-making rule, in a line (the GDD's table is generated from these).</summary>
    public string Rule { get; protected init; }
    public string Forbidden { get; protected init; }
    public string Space { get; protected init; }
    public int Index { get; private set; }
    public int SampleRate { get; protected init; } = 22050;
    /// <summary>Seconds of buffer every sound gets after its body, for echoes and room.</summary>
    public float Tail { get; protected init; } = 0.25f;
    public int Tonic { get; protected init; }
    public int[] Scale { get; protected init; }
    /// <summary>The realm's motif, as degrees of its scale — its stingers and its lead are
    /// made of it.</summary>
    public int[] Motif { get; protected init; }
    /// <summary>The shortest attack this medium allows, seconds (0 = an instant edge).</summary>
    public float MinAttack { get; protected init; }

    // ── The six primitives ──────────────────────────────────────────────────────────
    /// <summary>Something moving through the air. <paramref name="from"/> / <paramref name="to"/>
    /// are heights 0 (low, dark) … 1 (high, bright).</summary>
    public abstract void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain);
    /// <summary>A body landing or being struck. <paramref name="weight"/> 0…1.</summary>
    public abstract void Thump(RenderCtx c, float at, float weight, float gain);
    /// <summary>The smallest transient. <paramref name="bright"/> 0…1.</summary>
    public abstract void Tick(RenderCtx c, float at, float bright, float gain);
    /// <summary>A note of the medium's melodic voice.</summary>
    public abstract void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short);
    /// <summary>A sustained material (fire, ice, air…); stationary, so it can be looped.</summary>
    public abstract void Texture(RenderCtx c, float at, float dur, Tex tex, float gain);
    /// <summary>A pitched gesture between two notes — a glide where the medium allows one, a
    /// stepped run where it forbids them.</summary>
    public abstract void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain);

    // ── Music instruments (default to the primitives) ───────────────────────────────
    public virtual void Pad(RenderCtx c, float at, float midi, float dur, float gain) => Tone(c, at, midi, dur, gain, ToneKind.Long);
    public virtual void Bass(RenderCtx c, float at, float midi, float dur, float gain) => Tone(c, at, midi, dur, gain, ToneKind.Low);

    /// <summary>The linear half of the medium's finish: band limits, echo, room. It may ring
    /// into the tail; a loop is folded afterwards.</summary>
    public abstract void Room(RenderCtx c);
    /// <summary>The memoryless half (bit depth, clipping); runs after a loop is folded.</summary>
    public virtual void Crush(RenderCtx c) { }

    // ── Helpers for the media ───────────────────────────────────────────────────────
    protected static float[] Temp(RenderCtx c, float seconds) => new float[Math.Max(8, c.S(seconds) + 2)];

    protected static void Mix(RenderCtx c, float at, float[] temp, float gain)
    {
        int start = c.S(at);
        int n = Math.Min(temp.Length, c.Buf.Length - start);
        for (int i = Math.Max(0, -start); i < n; i++) c.Buf[start + i] += temp[i] * gain;
    }

    /// <summary>Mix a layer in at a known level: it is scaled to a peak of 1 first, so a
    /// recipe's balance does not depend on how loud a filter happened to leave its noise.</summary>
    protected static void MixNormalized(RenderCtx c, float at, float[] temp, float gain)
    {
        float peak = Finish.Peak(temp);
        if (peak < 1e-6f) return;
        Mix(c, at, temp, gain / peak);
    }

    /// <summary>Call <paramref name="place"/> at irregular moments across <paramref name="dur"/>
    /// seconds, <paramref name="perSecond"/> on average — drips, embers, knocks.</summary>
    protected static void Scatter(RenderCtx c, float dur, float perSecond, Action<float> place)
    {
        float t = c.Rng.Range(0f, MathF.Min(1f / perSecond, dur * 0.2f));
        while (t < dur)
        {
            place(t);
            t += c.Rng.Range(0.4f, 1.6f) / perSecond;
        }
    }

    /// <summary>A factor near 1 (±<paramref name="share"/>) drawn from the variant's seed: how one
    /// variant's decay, colour or length differs from its siblings' — never its pitch.</summary>
    protected static float Mut(RenderCtx c, float share) => c.Rng.Jitter(1f, share);

    /// <summary>A few cents either way: too little to hear as pitch, enough that two
    /// variants of a struck note drift apart and are never the same waveform.</summary>
    protected static float Cents(RenderCtx c, float cents = 4f) => Dsp.Ratio(c.Rng.Signed() * cents * 0.01f);

    /// <summary>Linear ramp down over the last <paramref name="seconds"/> of a layer: a hard
    /// gate that is still not a click.</summary>
    protected static void GateEnd(float[] temp, int sr, float seconds)
    {
        int n = Math.Min(temp.Length, Math.Max(1, (int)(seconds * sr)));
        for (int i = 0; i < n; i++) temp[temp.Length - 1 - i] *= i / (float)n;
    }

    /// <summary>A height 0…1 as a frequency: <paramref name="lowHz"/> at 0, rising
    /// <paramref name="octaves"/> octaves by 1.</summary>
    protected static float Height(float x, float lowHz, float octaves) => lowHz * MathF.Pow(2f, Dsp.Clamp01(x) * octaves);

    /// <summary>The scale note nearest a pitch — for media that move in steps.</summary>
    protected static float Snap(RenderCtx c, float midi)
    {
        float best = midi;
        float bestDistance = float.MaxValue;
        for (int degree = -14; degree <= 28; degree++)
        {
            float note = c.Deg(degree);
            float distance = MathF.Abs(note - midi);
            if (distance < bestDistance) { bestDistance = distance; best = note; }
        }
        return best;
    }

    // ── The registry ────────────────────────────────────────────────────────────────
    public static readonly Medium[] All =
    {
        new Watercolour(), new Poster(), new Ink(), new LitRealism(), new Linocut(),
        new Gongbi(), new Scratchboard(), new PixelArt(), new Cloisonne(), new VoidBlack(),
    };

    private static readonly Dictionary<string, Medium> ById = Build();
    public static int Count => All.Length;

    private static Dictionary<string, Medium> Build()
    {
        var map = new Dictionary<string, Medium>(StringComparer.Ordinal);
        for (int i = 0; i < All.Length; i++)
        {
            All[i].Index = i;
            map[All[i].Id] = All[i];
        }
        return map;
    }

    /// <summary>The medium with this realm code, or null.</summary>
    public static Medium Get(string id) => id != null && ById.TryGetValue(id, out Medium m) ? m : null;

    /// <summary>Home, when a realm says nothing about itself: the beach's watercolour.</summary>
    public static Medium Home => All[0];
    public static Medium Void => All[^1];

    // ── Whose sound is whose ────────────────────────────────────────────────────────
    /// <summary>
    /// Home worlds (<c>Docs/Feedback.gdd</c> §8, <c>Docs/Audio.gdd</c> §6): a character keeps
    /// its home medium in every arena. Cleopastar → SL and Sifu Pangda → BM are PROVISIONAL —
    /// the owner has not confirmed either. An owner that is not listed sounds like the realm
    /// it is met in.
    /// </summary>
    private static readonly Dictionary<string, string> HomeOf = new(StringComparer.Ordinal)
    {
        ["pomegraknight"] = "VK", ["crab"] = "VK", ["seagull"] = "VK", ["boss_crab"] = "VK",
        ["cleopastar"] = "SL",
        ["sifu_pangda"] = "BM",
        ["pumpking"] = "KH",
        ["pixolotl"] = "TF",
    };

    private static readonly HashSet<string> FoeIds = new(StringComparer.Ordinal) { "crab", "seagull", "boss_crab" };

    /// <summary>The class a known owner's cues fall back to: a foe, or a fighter.</summary>
    public static string ClassFor(string owner) => owner != null && FoeIds.Contains(owner) ? CueOwners.Foe : CueOwners.Fighter;

    /// <summary>Every owner that has a home medium, for warming and for checks.</summary>
    public static IEnumerable<string> KnownOwners => HomeOf.Keys;

    /// <summary>The home medium of a content id, or null when it has none of its own.</summary>
    public static Medium HomeFor(string owner) =>
        owner != null && HomeOf.TryGetValue(owner, out string id) ? Get(id) : null;
}

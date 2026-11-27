using System;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

public enum AudioBusId : byte { Sfx, Ui, Ambience, Music }

/// <summary>Whose medium renders a row: the owner's home world (a body's own sounds), the
/// realm being fought in (hazards, the world, the flow of a mission), or one fixed medium
/// (the storybook's UI, the VOID's own voice).</summary>
public enum MediumFrom : byte { Owner, Realm, Fixed }

/// <summary>How a row's pitch is chosen when it plays: free (jitter only), transposed to the
/// realm's key on a scale degree, or climbing the realm's scale with the combo.</summary>
public enum PitchMode : byte { Free, Key, Ladder }

public enum StealMode : byte { Oldest, None }

/// <summary>When a stinger may start: at once, on the next subdivision of the beat that is
/// near enough, on the next beat, or on the next bar (each within its own waiting limit).</summary>
public enum Quantize : byte { None, Grid, Beat, Bar }

/// <summary>What the context blackboard can switch on a row (<c>Docs/Audio.gdd</c> §8).</summary>
[Flags]
public enum BoardFlag
{
    None = 0,
    Submerged = 1 << 0,     // the player's body is under water
    LowHp = 1 << 1,         // the player's body is at or under the low-HP share
    Boss = 1 << 2,          // a boss is on the field
    Urgent = 1 << 3,        // the mission's clock is nearly out
    Heated = 1 << 4,        // combat intensity is high
}

/// <summary>A state modifier: when the body that raised the cue is in any of
/// <see cref="State"/>, or the blackboard has any of <see cref="Board"/>, the sound is
/// re-pitched and re-levelled. Costs nothing — pitch and volume are playback parameters.</summary>
public sealed class StateMod
{
    public FeedbackState State;
    public BoardFlag Board;
    public float PitchSt, VolumeDb;
}

/// <summary>Renders one case of a sound into <c>c.Buf</c>. <paramref name="stage"/> is the
/// cue's variant made zero-based (combo stage, wave) when the row is staged; <paramref name="tagCase"/>
/// is 0 for no tag or an unknown one, else 1 + the index of the tag in the row's list.</summary>
public delegate void SfxRecipe(RenderCtx c, int stage, int tagCase);

/// <summary>
/// One row of the sound table (<c>Docs/Audio.gdd</c> §3): what a cue sounds like and how it
/// behaves among other sounds. A row is DATA — a recipe and a handful of numbers — and is
/// resolved exactly as every director resolves a cue: owner → class → <c>any</c>.
/// </summary>
public sealed class SfxDef
{
    public readonly string Owner, Action;
    /// <summary>The event id: <c>owner.action</c> — also the file name that overrides it.</summary>
    public readonly string Id;

    public SfxDef(string owner, string action, string id = null)
    {
        Owner = owner;
        Action = action;
        Id = id ?? owner + "." + action;
    }

    // ── What it is ──────────────────────────────────────────────────────────────────
    public SfxRecipe Recipe;
    /// <summary>Seconds the recipe fills, before the medium's tail; a loop's length.</summary>
    public float Body = 0.2f;
    /// <summary>Per-case length, when stages differ (a longer fanfare for a victory).</summary>
    public Func<int, int, float> BodyFor;
    public MediumFrom From = MediumFrom.Owner;
    public string FixedMedium;
    /// <summary>How big the thing is (the stand-in boss crab is a big crab).</summary>
    public float Size = 1f;
    /// <summary>Stages rendered separately (cue variant 1…n); 0 = the variant is not a stage.</summary>
    public int Stages;
    /// <summary>Tags rendered separately; any other tag plays case 0.</summary>
    public string[] Tags = Array.Empty<string>();
    /// <summary>Seed-mutated siblings per case, 1–4.</summary>
    public int Variants = 3;
    public bool Loop;

    // ── How it plays ────────────────────────────────────────────────────────────────
    public AudioBusId Bus = AudioBusId.Sfx;
    /// <summary>Peak the render is normalised to, dBFS, and the row's level on its bus.</summary>
    public float PeakDb = -9f, VolumeDb;
    public float PitchJitter = 0.7f, VolumeJitter = 1f;
    public PitchMode Pitch = PitchMode.Free;
    /// <summary>For <see cref="PitchMode.Key"/>: the realm-scale degree the sound is moved
    /// to, plus this many degrees per cue variant above 1.</summary>
    public int Degree, DegreePerVariant;
    /// <summary>The cue magnitude that plays at the row's level; each doubling adds
    /// <see cref="MagnitudeDb"/>, within −<see cref="MagnitudeRange"/> … +3 dB. 0 = ignored.</summary>
    public float MagnitudeRef, MagnitudeDb = 4f, MagnitudeRange = 9f;
    /// <summary>Extra level when the cue is not about the body the player wears.</summary>
    public float OthersDb;
    public StateMod[] Mods = Array.Empty<StateMod>();
    public float FadeIn = 0.04f, FadeOut = 0.15f;
    public Quantize Quantize = Quantize.None;
    /// <summary>Duck the music by this much for this long while the sound plays.</summary>
    public float DuckDb, DuckSeconds;

    // ── How it behaves among others ─────────────────────────────────────────────────
    /// <summary>0–100. 90 and above is never stolen by a lower priority.</summary>
    public int Priority = 50;
    public int MaxVoices = 3;
    public float Cooldown = 0.04f;
    public StealMode Steal = StealMode.Oldest;
    /// <summary>Volley coalescing: once this many posts arrive inside <see cref="BurstWindow"/>
    /// seconds, <see cref="BurstDef"/> plays once and the rest are dropped.</summary>
    public int BurstCount;
    public float BurstWindow = 0.06f;
    public SfxDef BurstDef;
    /// <summary>A layer added when the cue carries <see cref="LayerTag"/> (her slash, burning,
    /// is her slash plus a fizz of fire — one small sound instead of a second copy of every stage).</summary>
    public string LayerTag;
    public SfxDef Layer;

    // ── Runtime, owned by the director's cache and voice manager ────────────────────
    internal readonly SfxSet[] Sets = new SfxSet[Medium.Count];
    internal double LastPostAt = double.NegativeInfinity;
    internal double BurstStart = double.NegativeInfinity;
    internal int BurstSeen;
    internal bool BurstFired;
    internal int LiveVoices;

    private string[][] _tagAlternatives = Array.Empty<string[]>();

    public int TagCase(string tag)
    {
        if (tag == null) return 0;
        for (int i = 0; i < _tagAlternatives.Length; i++)
        {
            string[] alternatives = _tagAlternatives[i];
            for (int j = 0; j < alternatives.Length; j++)
                if (alternatives[j] == tag) return i + 1;
        }
        return 0;
    }

    public int StageCount => Math.Max(1, Stages);
    public int CaseCount => StageCount * (Tags.Length + 1);
    public int CaseIndex(int stage, int tagCase) => Math.Clamp(stage, 0, StageCount - 1) * (Tags.Length + 1) + tagCase;

    /// <summary>The stage a cue variant picks (1-based variant → 0-based stage).</summary>
    public int StageOf(int variant) => Stages > 0 ? Math.Clamp(variant, 1, Stages) - 1 : 0;

    /// <summary>The id of one case — what a dump file and an override file are called.</summary>
    public string CaseId(int stage, int tagCase)
    {
        string id = Id;
        if (Stages > 0) id += "." + (stage + 1);
        if (tagCase > 0) id += "." + Tags[tagCase - 1];
        return id;
    }

    // ── Fluent setup, so a row reads as one line in the table ───────────────────────
    public SfxDef Vary(int variants, float pitchSt = 0.7f, float volumeDb = 1f) { Variants = variants; PitchJitter = pitchSt; VolumeJitter = volumeDb; return this; }
    public SfxDef Voices(int max, float cooldown, int priority) { MaxVoices = max; Cooldown = cooldown; Priority = priority; return this; }
    public SfxDef Level(float volumeDb, float peakDb = -9f) { VolumeDb = volumeDb; PeakDb = peakDb; return this; }
    public SfxDef Staged(int stages) { Stages = stages; return this; }
    /// <summary>The tags rendered as cases of their own. "burn|on_fire" is one case two tags share.</summary>
    public SfxDef Tagged(params string[] tags)
    {
        _tagAlternatives = new string[tags.Length][];
        Tags = new string[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            _tagAlternatives[i] = tags[i].Split('|');
            Tags[i] = _tagAlternatives[i][0];
        }
        return this;
    }
    public SfxDef Layered(string tag, SfxDef layer) { LayerTag = tag; Layer = layer; return this; }
    public SfxDef Keyed(int degree = 0, int perVariant = 0) { Pitch = PitchMode.Key; Degree = degree; DegreePerVariant = perVariant; if (PitchJitter > 0.15f) PitchJitter = 0.15f; return this; }
    public SfxDef Laddered() { Pitch = PitchMode.Ladder; PitchJitter = 0f; return this; }
    public SfxDef OfRealm() { From = MediumFrom.Realm; return this; }
    public SfxDef OfMedium(string id) { From = MediumFrom.Fixed; FixedMedium = id; return this; }
    public SfxDef On(AudioBusId bus) { Bus = bus; return this; }
    public SfxDef Looping(float fadeIn = 0.08f, float fadeOut = 0.2f) { Loop = true; FadeIn = fadeIn; FadeOut = fadeOut; Variants = 1; PitchJitter = 0f; VolumeJitter = 0f; Cooldown = 0f; return this; }
    public SfxDef ByMagnitude(float reference, float dbPerDoubling = 4f, float range = 9f) { MagnitudeRef = reference; MagnitudeDb = dbPerDoubling; MagnitudeRange = range; return this; }
    public SfxDef Others(float db) { OthersDb = db; return this; }
    public SfxDef Sized(float size) { Size = size; return this; }
    public SfxDef Lasting(Func<int, int, float> bodyFor) { BodyFor = bodyFor; return this; }
    public SfxDef Quantized(Quantize q) { Quantize = q; return this; }
    public SfxDef Ducking(float db, float seconds) { DuckDb = db; DuckSeconds = seconds; return this; }
    public SfxDef Bursting(int count, SfxDef burst, float window = 0.06f) { BurstCount = count; BurstDef = burst; BurstWindow = window; return this; }

    public SfxDef When(FeedbackState state, float pitchSt, float volumeDb) => AddMod(new StateMod { State = state, PitchSt = pitchSt, VolumeDb = volumeDb });
    public SfxDef When(BoardFlag board, float pitchSt, float volumeDb) => AddMod(new StateMod { Board = board, PitchSt = pitchSt, VolumeDb = volumeDb });

    private SfxDef AddMod(StateMod mod)
    {
        var mods = new StateMod[Mods.Length + 1];
        Mods.CopyTo(mods, 0);
        mods[^1] = mod;
        Mods = mods;
        return this;
    }

    public override string ToString() => Id;
}

/// <summary>One rendered variant: 16-bit mono PCM and, once the main thread has wrapped it,
/// the stream that plays it (kept as <c>object</c> so this file stays free of engine types).</summary>
public sealed class SfxClip
{
    private byte[] _pcm;
    private int _bytes;

    public byte[] Pcm
    {
        get => _pcm;
        set { _pcm = value; if (value != null) _bytes = value.Length; }
    }

    public int SampleRate;
    public float Seconds;
    public bool Loop;
    /// <summary>True when a file replaced the recipe.</summary>
    public bool FromFile;
    /// <summary>Peak of the render BEFORE it was normalised, dBFS: a recipe that made next
    /// to nothing would otherwise be amplified into its own noise floor and pass for a sound.</summary>
    public float RawPeakDb;
    public object Stream;
    /// <summary>Bytes of PCM the engine holds for this clip.</summary>
    public int Bytes => _bytes;

    /// <summary>Drop the managed copy once the engine has its own (the game does; a check
    /// that wants to measure the sound keeps it).</summary>
    public void ReleasePcm() => _pcm = null;
}

/// <summary>Everything one row sounds like in one medium: its cases, each a small bag of
/// variants drawn without repeats.</summary>
public sealed class SfxSet
{
    public readonly SfxClip[][] Cases;
    private readonly int[] _last;
    private readonly int[][] _bag;
    private readonly int[] _left;

    public SfxSet(SfxClip[][] cases)
    {
        Cases = cases;
        _last = new int[cases.Length];
        _bag = new int[cases.Length][];
        _left = new int[cases.Length];
        for (int i = 0; i < cases.Length; i++)
        {
            _last[i] = -1;
            _bag[i] = new int[cases[i].Length];
        }
    }

    public int Bytes
    {
        get
        {
            int total = 0;
            foreach (SfxClip[] clips in Cases)
                foreach (SfxClip clip in clips) total += clip.Bytes;
            return total;
        }
    }

    /// <summary>The next variant of a case: a shuffled bag, refilled when empty, that never
    /// deals the clip it dealt last. Allocates nothing.</summary>
    public SfxClip Next(int caseIndex, ref SynthRng rng) => Cases[caseIndex][NextIndex(caseIndex, ref rng)];

    public int NextIndex(int caseIndex, ref SynthRng rng)
    {
        int n = Cases[caseIndex].Length;
        if (n <= 1) return 0;
        int[] bag = _bag[caseIndex];
        if (_left[caseIndex] == 0)
        {
            for (int i = 0; i < n; i++) bag[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Int(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            // The bag is dealt from its end: keep the last-played clip away from it.
            if (bag[n - 1] == _last[caseIndex]) (bag[n - 1], bag[0]) = (bag[0], bag[n - 1]);
            _left[caseIndex] = n;
        }
        int pick = bag[--_left[caseIndex]];
        _last[caseIndex] = pick;
        return pick;
    }
}

/// <summary>Turns a row into PCM. Pure and deterministic: the same row, medium, case, variant
/// and seed give the same bytes on every machine, on any thread.</summary>
public static class SfxRender
{
    /// <summary>The stretch of a loop that is cross-faded over its start.</summary>
    public const float LoopFade = 0.25f;
    /// <summary>A one-shot's tail is cut where it has fallen this far under its peak —
    /// memory is the reason; under a fight nothing down there is heard.</summary>
    public const float TrimBelowDb = -42f;

    public static ulong SeedFor(SfxDef def, Medium medium, int stage, int tagCase, int variant, ulong salt) =>
        SynthRng.Hash(def.Id) ^ (SynthRng.Hash(medium.Id) * 31UL) ^ ((ulong)stage << 20) ^ ((ulong)tagCase << 28)
        ^ ((ulong)variant * 0x9E3779B97F4A7C15UL) ^ salt;

    public static SfxClip Render(SfxDef def, Medium medium, int stage, int tagCase, int variant, ulong salt = 0)
    {
        int sr = medium.SampleRate;
        float body = def.BodyFor != null ? def.BodyFor(stage, tagCase) : def.Body;
        float fade = def.Loop ? MathF.Min(LoopFade, body * 0.5f) : 0f;
        float total = body + fade + medium.Tail;
        var c = new RenderCtx
        {
            Buf = new float[(int)(total * sr) + 8],
            Sr = sr,
            Rng = new SynthRng(SeedFor(def, medium, stage, tagCase, variant, salt)),
            Medium = medium,
            Tonic = medium.Tonic,
            Scale = medium.Scale,
            Motif = medium.Motif,
            Body = def.Loop ? total : body,
            Size = def.Size,
        };
        def.Recipe(c, stage, tagCase);
        medium.Room(c);
        BlockDc(c.Buf, sr);
        if (def.Loop)
        {
            int loopLen = (int)(body * sr);
            c.Buf = Finish.CrossfadeLoop(c.Buf, loopLen, (int)(fade * sr));
            medium.Crush(c);
            Finish.Scrub(c.Buf);
            Finish.RemoveDc(c.Buf);
        }
        else
        {
            medium.Crush(c);
            Finish.Scrub(c.Buf);
            c.Buf = Finish.TrimTail(c.Buf, sr, TrimBelowDb);
            Finish.Fade(c.Buf, sr, 0.0006f, MathF.Min(0.03f, c.Buf.Length / (float)sr * 0.25f));
        }
        float rawPeak = Dsp.LinToDb(Finish.Peak(c.Buf));
        Finish.Normalize(c.Buf, def.PeakDb);
        var dither = new SynthRng(c.Rng.NextU64());
        return new SfxClip
        {
            RawPeakDb = rawPeak,
            Pcm = Finish.Pack16(c.Buf, ref dither),
            SampleRate = sr,
            Seconds = c.Buf.Length / (float)sr,
            Loop = def.Loop,
        };
    }

    /// <summary>A 12 Hz one-pole high-pass: removes an offset without shifting silence.</summary>
    public static void BlockDc(float[] buf, int sr)
    {
        float r = 1f - Dsp.Tau * 12f / sr;
        float x1 = 0f, y1 = 0f;
        for (int i = 0; i < buf.Length; i++)
        {
            float x = buf[i];
            float y = x - x1 + r * y1;
            x1 = x;
            y1 = y;
            buf[i] = y;
        }
    }
}

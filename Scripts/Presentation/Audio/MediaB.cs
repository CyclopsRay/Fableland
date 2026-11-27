using System;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

// The other five media (Docs/Audio.gdd §5).

/// <summary>PL — gongbi: an even iron-wire line and colour graded without a visible stroke.
/// Perfectly even tone and pulse, smooth raised-cosine dynamics, ornaments placed exactly,
/// never a bend — and all of it heard under water. No pitch scoops, no rough attacks.</summary>
public sealed class Gongbi : Medium
{
    private static readonly Mode[] Chime = { new(1f, 1.2f, 1f), new(2f, 0.8f, 0.4f), new(2.4f, 0.5f, 0.25f), new(3f, 0.3f, 0.15f) };
    private static readonly float[] Even = { 1f, 0.4f, 0.2f };

    public Gongbi()
    {
        Id = "PL";
        Name = "gongbi (fine-line painting)";
        Rule = "even line, graded colour: bronze chimes and pure tones with smooth 10 ms+ edges, stepped ornaments in exact time, all under water";
        Forbidden = "pitch bends and scoops, rubato, a rough attack";
        Space = "under water: low-passed, chorused, a soft room";
        Tonic = 55;                                  // G3
        Scale = new[] { 0, 2, 4, 6, 7, 9, 11 };      // Lydian (yayue heptatonic)
        Motif = new[] { 0, 2, 3, 4 };
        SampleRate = 11025;                          // under water: low-passed at 2.2 kHz
        Tail = 0.45f;
        MinAttack = 0.010f;
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        float[] t = Temp(c, dur);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, Height(from, 300f, 2f), Height(to, 300f, 2f), 0.9f,
            Env.Swell(dur * 0.4f, dur * 0.1f, dur * 0.5f), 1f, ref c.Rng);
        MixNormalized(c, at, t, gain * 0.7f);
        // Bubbles, placed exactly: three rising steps.
        for (int i = 0; i < 3; i++)
        {
            float hz = (from <= to ? 600f + 200f * i : 1000f - 200f * i) / MathF.Sqrt(c.Size);
            Gen.Osc(c.Buf, c.Sr, c.S(at + dur * (0.2f + 0.25f * i)), c.S(0.05f), Wave.Sine, hz, hz, Env.Swell(0.010f, 0.008f, 0.03f), gain * 0.22f);
        }
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        float f = (120f - 35f * weight) / MathF.Sqrt(c.Size) * Mut(c, 0.03f);
        var env = Env.Swell(0.012f, 0.02f, (0.16f + 0.2f * weight) * Mut(c, 0.15f));
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(env.Length), Wave.Sine, f, f, env, gain * 0.9f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(env.Length), Wave.Sine, f * 2f, f * 2f, env, gain * 0.25f);
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain)
    {
        float hz = 1568f * (1f + 0.4f * bright) / MathF.Sqrt(c.Size) * Mut(c, 0.02f);       // the "pearl"
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(0.14f), Wave.Sine, hz, hz, Env.Perc(0.010f, 0.12f), gain * 0.6f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(0.08f), Wave.Sine, hz * 2f, hz * 2f, Env.Perc(0.010f, 0.06f), gain * 0.18f);
    }

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float hz = Dsp.MidiToHz(midi) * Cents(c, 3f);
        int at0 = c.S(at);
        float ring = Mut(c, 0.15f);
        switch (kind)
        {
            case ToneKind.Long:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(dur + 0.5f), hz, Chime, gain * 0.5f, 0.012f, MathF.Max(0.6f, dur) * ring);
                var breath = Env.Swell(0.06f, dur * 0.5f, dur * 0.4f);
                Gen.Additive(c.Buf, c.Sr, at0, c.S(dur), hz, Even, breath, gain * 0.2f, ref c.Rng);       // the xiao under it
                break;
            case ToneKind.Accent:
                // An exact ornament: a grace pearl one scale step up, 50 ms early.
                Tick(c, MathF.Max(0f, at - 0.05f), 0.3f, gain * 0.5f);
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.9f), hz, Chime, gain * 0.6f, 0.012f, 0.6f * ring);
                break;
            case ToneKind.Low:
                Gen.Additive(c.Buf, c.Sr, at0, c.S(dur), hz, Even, Env.Swell(0.03f, dur * 0.55f, dur * 0.4f), gain * 0.6f, ref c.Rng);
                break;
            default:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.6f), hz, Chime, gain * 0.6f, 0.012f, 0.35f * ring);
                Gen.Osc(c.Buf, c.Sr, at0, c.S(0.2f), Wave.Sine, hz * 2f, hz * 2f, Env.Perc(0.012f, 0.18f * ring), gain * 0.08f * Mut(c, 0.5f));
                break;
        }
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        var flat = Env.Gate(0f, dur + 1f, 0f);
        switch (tex)
        {
            case Tex.Fire:              // under water, fire is steam
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, 1500f, 1500f, 1f, flat, 1f, ref c.Rng, 3f, 0.5f);
                Bubbles(t, c.Sr, 6f, 700f, 0.4f);
                break;
            case Tex.Ice:
                Gen.Additive(t, c.Sr, 0, t.Length, 1975f, Even, flat, 0.6f, ref c.Rng);
                Fx.Tremolo(t, c.Sr, 4f, 0.5f);
                break;
            case Tex.Air:
            case Tex.Water:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Low, 500f, 500f, 0.7f, flat, 1f, ref c.Rng);
                Bubbles(t, c.Sr, 4f, 520f, 0.5f);
                break;
            case Tex.Rumble:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 55f, 55f, flat, 0.8f);
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, 200f, 200f, 0.7f, flat, 0.5f, ref c.Rng);
                break;
            case Tex.Buzz:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 180f, 180f, flat, 1f);
                Fx.Tremolo(t, c.Sr, 8f, 0.8f);
                break;
            default:
                float hz = Dsp.MidiToHz(c.Deg(0) + 12f), up = Dsp.MidiToHz(c.Deg(1) + 12f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz, hz, flat, 0.6f);
                Fx.Tremolo(t, c.Sr, 1.5f, 1f);
                float[] other = Temp(c, dur);
                Gen.Osc(other, c.Sr, 0, other.Length, Wave.Sine, up, up, flat, 0.6f, phase: 0.25f);
                for (int i = 0; i < other.Length; i++)
                    other[i] *= 0.5f - 0.5f * MathF.Sin(Dsp.Tau * 1.5f * i / c.Sr);
                for (int i = 0; i < t.Length && i < other.Length; i++) t[i] += other[i];
                break;
        }
        MixNormalized(c, at, t, gain * 0.55f);
    }

    /// <summary>Bubbles at a perfectly even rate — this medium has no irregular pulse.</summary>
    private static void Bubbles(float[] t, int sr, float perSecond, float hz, float gain)
    {
        int every = (int)(sr / perSecond);
        for (int start = every / 2, k = 0; start < t.Length; start += every, k++)
        {
            float f = hz * (1f + 0.25f * (k % 3));
            Gen.Osc(t, sr, start, (int)(0.05f * sr), Wave.Sine, f, f, Env.Swell(0.010f, 0.008f, 0.03f), gain);
        }
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        // A run of scale steps in exactly even time, each a short chime.
        int n = Math.Clamp((int)(dur / 0.055f), 3, 8);
        for (int i = 0; i < n; i++)
        {
            float hz = Dsp.MidiToHz(Snap(c, Dsp.Lerp(fromMidi, toMidi, i / (float)(n - 1))));
            Gen.Modal(c.Buf, c.Sr, c.S(at + i * dur / n), c.S(0.3f), hz, Chime, gain * 0.4f * Mut(c, 0.2f), 0.010f, 0.2f * Mut(c, 0.2f));
        }
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain) =>
        Gen.Additive(c.Buf, c.Sr, c.S(at), c.S(dur), Dsp.MidiToHz(midi), Even, Env.Swell(dur * 0.35f, dur * 0.3f, dur * 0.35f), gain * 0.4f, ref c.Rng, 3f);

    public override void Room(RenderCtx c)
    {
        Fx.LowPass(c.Buf, c.Sr, 2200f, 2);
        Fx.Chorus(c.Buf, c.Sr, 12f, 3f, 0.6f, 0.35f);
        Fx.Reverb(c.Buf, c.Sr, 0.9f, 0.5f, 0.4f);
    }
}

/// <summary>KH — scratchboard: light scratched out of black. Thin, wiry, high-passed sounds
/// cut out of silence, with tremolo as the hatching; the line is never tidy. A theremin for
/// the cold blue, a music box for the yellow, a tritone stab for the red. No warm bass, no
/// lush reverb.</summary>
public sealed class Scratchboard : Medium
{
    private static readonly Mode[] Hollow = { new(1f, 0.11f, 1f), new(1.52f, 0.07f, 0.6f), new(2.3f, 0.05f, 0.3f) };
    private static readonly Mode[] Bone = { new(1f, 0.05f, 1f), new(2.83f, 0.03f, 0.5f), new(5.1f, 0.02f, 0.25f) };
    private static readonly Mode[] MusicBox = { new(1f, 0.5f, 1f), new(4f, 0.2f, 0.3f), new(6.3f, 0.1f, 0.12f) };

    public Scratchboard()
    {
        Id = "KH";
        Name = "scratchboard";
        Rule = "light scratched out of black: thin, wiry, high-passed; tremolo as hatching; theremin, music box, a tritone stab; an untidy line";
        Forbidden = "warm bass pads, lush reverb, a tidy even line";
        Space = "dry, a short plate";
        Tonic = 62;                                  // D4
        Scale = new[] { 0, 2, 3, 6, 7, 8, 11 };      // Hungarian minor
        Motif = new[] { 0, 3, 4, 2 };
        Tail = 0.18f;
        MinAttack = 0f;
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        float[] t = Temp(c, dur + 0.03f);
        // Two scratches, never quite together.
        for (int line = 0; line < 2; line++)
        {
            float late = line * c.Rng.Range(0.008f, 0.02f);
            Gen.Noise(t, c.Sr, c.S(late), c.S(dur), Tint.White, Band.Mid, Height(from, 1800f, 1.8f) * (1f + 0.2f * line),
                Height(to, 1800f, 1.8f) * (1f + 0.2f * line), 3f, Env.PressLift(dur, 0.2f, 1.6f), 1f - 0.4f * line, ref c.Rng,
                c.Rng.Range(38f, 55f), 0.85f);
        }
        MixNormalized(c, at, t, gain * 0.7f);
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        c.Low = true;       // the one low thing this medium owns: a hollow gourd
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.3f), (170f - 50f * weight) / MathF.Sqrt(c.Size), Hollow, gain * 0.9f, 0f, 0.8f + 0.7f * weight);
        Gen.Noise(c.Buf, c.Sr, c.S(at), c.S(0.02f), Tint.White, Band.High, 3000f, 3000f, 0.7f, Env.Perc(0.0005f, 0.015f), gain * 0.2f, ref c.Rng);
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain) =>
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.1f), c.Rng.Jitter(1700f + 1300f * bright, 0.06f) / MathF.Sqrt(c.Size), Bone, gain * 0.8f);

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float hz = Dsp.MidiToHz(midi) * Cents(c, 6f);       // an untidy line is never quite in tune
        int at0 = c.S(at);
        switch (kind)
        {
            case ToneKind.Long:         // the theremin: a nervous line
                var vib = new Vib(c.Rng.Range(6f, 7f), 0.5f, 0.05f);
                var env = Env.Swell(0.05f, dur * 0.55f, dur * 0.4f);
                Gen.Osc(c.Buf, c.Sr, at0, c.S(dur), Wave.Sine, hz * 2f * c.Rng.Range(0.992f, 1.008f), hz * 2f, env, gain * 0.5f, vib: vib);
                Gen.Osc(c.Buf, c.Sr, at0, c.S(dur), Wave.Sine, hz * 4f, hz * 4f, env, gain * 0.1f, vib: vib);
                break;
            case ToneKind.Accent:       // the red: a tritone stab
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.5f), hz * 2f, MusicBox, gain * 0.55f);
                Gen.Modal(c.Buf, c.Sr, at0 + c.S(c.Rng.Range(0.004f, 0.012f)), c.S(0.5f), hz * 2f * Dsp.Ratio(6f), MusicBox, gain * 0.45f);
                break;
            case ToneKind.Low:          // a harpsichord string: thin even when it is low
                Gen.Pluck(c.Buf, c.Sr, at0, c.S(0.45f), hz, 1f, 0.35f, 0.06f, gain * 0.7f, ref c.Rng);
                break;
            default:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.5f), hz * 2f, MusicBox, gain * 0.65f, 0f, Mut(c, 0.2f));
                Gen.Noise(c.Buf, c.Sr, at0, c.S(0.01f), Tint.White, Band.High, 3000f, 3000f, 0.7f, Env.Perc(0.0005f, 0.008f), gain * 0.06f, ref c.Rng);   // the comb's pluck
                break;
        }
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        var flat = Env.Gate(0f, dur + 1f, 0f);
        switch (tex)
        {
            case Tex.Fire:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 2800f, 2800f, 1.4f, flat, 1f, ref c.Rng, 31f, 0.8f);
                Fx.Tremolo(t, c.Sr, 47f, 0.6f);
                Gen.Crackle(t, c.Sr, 0, t.Length, 35f, 0.7f, ref c.Rng, 3800f);
                break;
            case Tex.Ice:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 3136f, 3136f, flat, 0.7f);
                Fx.Tremolo(t, c.Sr, 23f, 0.8f);
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 4500f, 4500f, 2f, flat, 0.08f, ref c.Rng);
                break;
            case Tex.Air:
            case Tex.Water:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 1800f, 1800f, 4f, flat, 1f, ref c.Rng, 0.9f, 0.6f);
                break;
            case Tex.Rumble:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 500f, 500f, 2f, flat, 1f, ref c.Rng, 12f, 0.8f);
                break;
            case Tex.Buzz:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 880f, 880f, flat, 1f, vib: new Vib(6.5f, 0.4f));
                Fx.Tremolo(t, c.Sr, 18f, 0.85f);
                break;
            default:
                float hz = Dsp.MidiToHz(c.Deg(0) + 12f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz, hz, flat, 1f, vib: new Vib(3f, 1f));
                break;
        }
        MixNormalized(c, at, t, gain * 0.5f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        // A theremin wail that overshoots where it is going.
        float f0 = Dsp.MidiToHz(fromMidi + 12f), f1 = Dsp.MidiToHz(toMidi + 12f + (toMidi >= fromMidi ? 0.6f : -0.6f));
        var vib = new Vib(6.5f, 0.6f, dur * 0.2f);
        var env = Env.Swell(dur * 0.15f, dur * 0.5f, dur * 0.35f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(dur), Wave.Sine, f0, f1, env, gain * 0.5f, vib: vib);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(dur), Wave.Sine, f0 * 2f, f1 * 2f, env, gain * 0.12f, vib: vib);
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain)
    {
        Tone(c, at, midi, dur, gain * 0.5f, ToneKind.Long);
        float[] wind = Temp(c, dur);
        Gen.Noise(wind, c.Sr, 0, wind.Length, Tint.White, Band.Mid, 1800f, 2200f, 4f, Env.Swell(dur * 0.4f, dur * 0.2f, dur * 0.4f), 1f, ref c.Rng, 0.7f, 0.5f);
        MixNormalized(c, at, wind, gain * 0.12f);
    }

    public override void Bass(RenderCtx c, float at, float midi, float dur, float gain) => Tone(c, at, midi, dur, gain, ToneKind.Low);

    public override void Room(RenderCtx c)
    {
        Fx.HighPass(c.Buf, c.Sr, c.Low ? 110f : 400f, 2);
        Fx.Reverb(c.Buf, c.Sr, 0.3f, 0.2f, 0.2f);
    }
}

/// <summary>TF — pixel art: a hard grid and a small fixed palette. Four chip channels —
/// pulse, triangle, shift-register noise — whose pitch moves only in semitone steps and whose
/// volume has sixteen levels, both changing sixty times a second; dither is two notes or two
/// levels taking turns. No reverb, no glide, no soft edge.</summary>
public sealed class PixelArt : Medium
{
    private const float Frame = 1f / 60f;

    public PixelArt()
    {
        Id = "TF";
        Name = "pixel art";
        Rule = "hard grid, fixed palette: pulse, triangle and shift-register noise; semitone steps, 16 volume levels, a 60 Hz tick; dither = alternation";
        Forbidden = "reverb, smooth glides, soft edges";
        Space = "none; one eighth-note ping";
        Tonic = 57;                                  // A3
        Scale = new[] { 0, 2, 3, 5, 7, 8, 11 };      // harmonic minor
        Motif = new[] { 0, 4, 7, 4, 2 };
        Tail = 0.32f;
        MinAttack = 0f;
    }

    private static float Semitone(float midi) => Dsp.MidiToHz(MathF.Round(midi));

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain) =>
        Gen.Lfsr(c.Buf, c.Sr, c.S(at), c.S(dur), Height(from, 900f, 4f), Height(to, 900f, 4f),
            Env.Perc(0f, dur).Stepped(16, Frame), gain * 0.45f, false, Frame * 2f, (int)(c.Rng.NextU64() & 0x7FFF));

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        // A triangle kick: the pitch falls a minor third every frame, never in between.
        float baseHz = Semitone(Dsp.LinToDb((220f - 90f * weight) / MathF.Sqrt(c.Size) / 440f) / 20f * 39.863f + 69f + c.Rng.Int(3) - 1);
        int frames = 4 + (int)(6f * weight) + c.Rng.Int(2);
        for (int k = 0; k < frames; k++)
        {
            float hz = baseHz * Dsp.Ratio(-3f * k);
            if (hz < 28f) break;
            float level = MathF.Round((1f - k / (float)frames) * 16f) / 16f;
            Gen.Osc(c.Buf, c.Sr, c.S(at + k * Frame), c.S(Frame), Wave.Triangle, hz, hz, Env.Gate(0.0005f, Frame - 0.001f, 0.0005f), gain * 0.9f * level);
        }
        Gen.Lfsr(c.Buf, c.Sr, c.S(at), c.S(Frame), 9000f, 0f, Env.Gate(0f, Frame, 0f), gain * 0.25f, true, 1f / 60f, (int)(c.Rng.NextU64() & 0x7FFF));
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain)
    {
        float hz = Semitone(81f + 12f * bright - 6f * MathF.Log2(c.Size) + c.Rng.Int(3) - 1);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(Frame * 2f), Wave.Pulse, hz, hz, Env.Perc(0f, Frame * 2f).Stepped(16, Frame), gain * 0.5f, c.Rng.Chance(0.5f) ? 0.125f : 0.25f);
    }

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float hz = Semitone(midi);
        switch (kind)
        {
            case ToneKind.Accent:
            {
                // Dither: the note and its fifth take turns, a frame each.
                float len = 0.24f;
                int frames = (int)(len / Frame) + c.Rng.Int(3);
                int lead = c.Rng.Int(2);
                for (int k = 0; k < frames; k++)
                {
                    float f = ((k + lead) & 1) == 0 ? hz : Semitone(midi + 7f);
                    float level = MathF.Round((1f - k / (float)frames) * 16f) / 16f;
                    Gen.Osc(c.Buf, c.Sr, c.S(at + k * Frame), c.S(Frame), Wave.Pulse, f, f, Env.Gate(0f, Frame, 0f), gain * 0.42f * level, 0.25f,
                        phase: f * k * Frame);
                }
                break;
            }
            case ToneKind.Low:
                // The triangle channel has no volume register: it is on or off.
                Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(dur), Wave.Triangle, hz, hz, Env.Gate(0.0005f, MathF.Max(Frame, dur - 0.002f), 0.0005f), gain * 0.7f);
                // Some variants open with one frame an octave up: a tracker's bass articulation.
                if (c.Rng.Chance(0.5f)) Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(Frame), Wave.Pulse, hz * 2f, hz * 2f, Env.Gate(0f, Frame, 0f), gain * 0.2f, 0.25f);
                break;
            default:
            {
                var p = SfxrParams.Default;
                p.Wave = SfxrParams.Shape.Square;
                p.BaseFreq = SfxrParams.FreqFor(hz);
                // A chip's siblings differ in duty, not in room: 50 or 35 % held, 25 or 12.5 % struck.
                p.Duty = kind == ToneKind.Long ? (c.Rng.Chance(0.5f) ? 0f : 0.3f) : (c.Rng.Chance(0.5f) ? 0.5f : 0.75f);
                p.Sustain = MathF.Sqrt((kind == ToneKind.Long ? dur * 0.5f : 0.04f) * 0.441f);
                p.Decay = MathF.Sqrt((kind == ToneKind.Long ? dur * 0.5f : 0.16f) * 0.441f);
                p.Punch = 0.3f;
                p.VolumeSteps = 16;
                p.TickHz = 60f;
                // A sibling in everything but pitch: a chip's notes are exact.
                SfxrParams sibling = p.Mutate(ref c.Rng, 0.02f);
                sibling.BaseFreq = p.BaseFreq;
                Sfxr.Render(c.Buf, c.Sr, c.S(at), sibling, gain * 0.55f, ref c.Rng);
                break;
            }
        }
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        int frames = (int)(dur / Frame) + 1;
        int turn = c.Rng.Int(12);                              // where in its pattern this variant starts
        int seed = (int)(c.Rng.NextU64() & 0x3FFF);
        float duty = c.Rng.Chance(0.5f) ? 0.125f : 0.25f;
        for (int k = 0; k < frames; k++)
        {
            int start = (int)(k * Frame * c.Sr), n = (int)(Frame * c.Sr) + 1;
            var hold = Env.Gate(0f, Frame, 0f);
            int q = k + turn;
            bool odd = (q & 1) == 1;
            switch (tex)
            {
                case Tex.Fire:
                    Gen.Lfsr(t, c.Sr, start, n, odd ? 6000f : 3500f, 0f, hold, odd ? 0.5f : 0.3125f, false, Frame, seed + k * 7);
                    break;
                case Tex.Ice:
                    Gen.Osc(t, c.Sr, start, n, Wave.Pulse, odd ? 3520f : 1760f, 0f, hold, 0.4f, duty);
                    break;
                case Tex.Air:
                    Gen.Lfsr(t, c.Sr, start, n, 1200f, 0f, hold, odd ? 0.25f : 0.1875f, false, Frame, seed + k * 7);
                    break;
                case Tex.Water:
                    Gen.Osc(t, c.Sr, start, n, Wave.Triangle, Semitone(69f - (q % 6) * 2f), 0f, hold, q % 6 < 4 ? 0.6f : 0f);
                    Gen.Lfsr(t, c.Sr, start, n, 2400f, 0f, hold, q % 6 == 0 ? 0.2f : 0f, false, Frame, seed + k * 7);
                    break;
                case Tex.Rumble:
                    Gen.Lfsr(t, c.Sr, start, n, 420f, 0f, hold, odd ? 0.4f : 0.3f, true, Frame, seed + k * 7);
                    Gen.Osc(t, c.Sr, start, n, Wave.Triangle, 55f, 0f, hold, 0.5f, phase: 55f * k * Frame);
                    break;
                case Tex.Buzz:
                    Gen.Osc(t, c.Sr, start, n, Wave.Pulse, 110f, 0f, hold, odd ? 0.5f : 0.25f, duty * 2f, phase: 110f * k * Frame);
                    break;
                default:
                    float f = (q / 8) % 2 == 0 ? Semitone(c.Deg(0) + 12f) : Semitone(c.Deg(0) + 13f);
                    Gen.Osc(t, c.Sr, start, n, Wave.Pulse, f, 0f, hold, 0.4f, duty, phase: f * k * Frame);
                    break;
            }
        }
        MixNormalized(c, at, t, gain * 0.4f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        // An arpeggio run: two frames a note, each note a whole semitone, never a glide.
        int n = Math.Max(2, (int)(dur / (Frame * 2f)));
        float duty = c.Rng.Chance(0.5f) ? 0.25f : 0.125f;
        for (int k = 0; k < n; k++)
        {
            float hz = Semitone(Dsp.Lerp(fromMidi, toMidi, k / (float)(n - 1)));
            float level = MathF.Round(Dsp.Lerp(0.6f, 1f, k / (float)(n - 1)) * 16f) / 16f;
            Gen.Osc(c.Buf, c.Sr, c.S(at + k * Frame * 2f), c.S(Frame * 2f), Wave.Pulse, hz, hz,
                Env.Gate(0f, Frame * 2f - 0.001f, 0.001f), gain * 0.42f * level, duty);
        }
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain) => Tone(c, at, midi, dur, gain * 0.6f, ToneKind.Low);

    public override void Room(RenderCtx c)
    {
        Fx.HighPass(c.Buf, c.Sr, 30f);
        Fx.Echo(c.Buf, c.Sr, 60f / 126f * 0.5f, 0f, 0.25f);
    }
}

/// <summary>CC — cloisonné enamel: every colour enclosed by gold wire. Every note opens with
/// a metallic tick — the wire — around a pure tone; intervals are open fourths, fifths and
/// octaves; the room is bright and short. Nothing unpitched and muddy, nothing low, and never
/// a note without its edge.</summary>
public sealed class Cloisonne : Medium
{
    private static readonly Mode[] WireModes = { new(1f, 0.03f, 1f), new(1.47f, 0.025f, 0.7f), new(2.09f, 0.02f, 0.5f), new(2.56f, 0.015f, 0.35f) };
    private static readonly Mode[] Cymbal = { new(1f, 0.22f, 1f), new(2.76f, 0.16f, 0.5f), new(5.4f, 0.1f, 0.3f) };
    private static readonly float[] Reed = { 1f, 0f, 0.5f, 0f, 0.25f, 0f, 0.12f };

    public Cloisonne()
    {
        Id = "CC";
        Name = "cloisonné enamel";
        Rule = "every colour inside gold wire: each note opens with a metallic tick around a pure tone; open fourths, fifths, octaves; high register";
        Forbidden = "unpitched mud, low drones, a note without its bell edge";
        Space = "bright and short";
        Tonic = 72;                                  // C5
        Scale = new[] { 0, 2, 4, 7, 9 };             // gong pentatonic, high
        Motif = new[] { 0, 3, 5, 3 };
        Tail = 0.3f;
        MinAttack = 0f;
    }

    /// <summary>The gold wire: a few milliseconds of struck metal.</summary>
    private static void Wire(RenderCtx c, float at, float gain) =>
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.05f), c.Rng.Jitter(3800f, 0.08f), WireModes, gain * 0.5f);

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        Wire(c, at, gain * 0.7f);
        float[] t = Temp(c, dur);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, Height(from, 523f, 2f), Height(to, 523f, 2f), 6f,
            Env.Swell(dur * 0.25f, dur * 0.25f, dur * 0.5f), 1f, ref c.Rng);
        MixNormalized(c, at, t, gain * 0.55f);
        Wire(c, at + dur * 0.92f, gain * 0.35f);         // and the wire closes the shape
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        Wire(c, at, gain * 0.6f);
        float f = MathF.Max(130f, (196f - 50f * weight) / MathF.Sqrt(c.Size));
        var env = Env.Perc(0.003f, 0.12f + 0.1f * weight);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(env.Length), Wave.Sine, f, f, env, gain * 0.8f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(env.Length), Wave.Sine, f * 1.5f, f * 1.5f, env, gain * 0.25f);
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain) =>
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.3f), c.Rng.Jitter(2600f + 1400f * bright, 0.03f) / MathF.Sqrt(c.Size), Cymbal, gain * 0.6f, 0f, 0.6f);

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float hz = MathF.Max(130f, Dsp.MidiToHz(midi)) * Cents(c, 3f);
        Wire(c, at, gain * 0.6f * Mut(c, 0.3f));
        var env = Env.Perc(0.002f, kind == ToneKind.Long || kind == ToneKind.Low ? MathF.Max(0.3f, dur) : 0.28f);
        int at0 = c.S(at), n = c.S(env.Length);
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, hz, hz, env, gain * 0.55f);
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, hz * 2f, hz * 2f, env, gain * 0.16f);
        if (kind == ToneKind.Accent || kind == ToneKind.Low)
            Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, hz * 1.5f, hz * 1.5f, env, gain * 0.2f);     // the open fifth
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        var flat = Env.Gate(0f, dur + 1f, 0f);
        var ctx = new RenderCtx { Buf = t, Sr = c.Sr, Rng = c.Rng.Fork(13), Medium = this, Tonic = c.Tonic, Scale = c.Scale };
        float hz = Dsp.MidiToHz(c.Deg(0));
        switch (tex)
        {
            case Tex.Fire:
                Gen.Additive(t, c.Sr, 0, t.Length, hz, Reed, flat, 0.3f, ref c.Rng, 4f);
                Gen.Additive(t, c.Sr, 0, t.Length, hz * 4f / 3f, Reed, flat, 0.3f, ref c.Rng, 4f);
                Fx.Tremolo(t, c.Sr, 7f, 0.6f);
                Scatter(ctx, dur, 9f, when => Wire(ctx, when, 0.9f));
                break;
            case Tex.Ice:
                Scatter(ctx, dur, 10f, when => Gen.Modal(t, ctx.Sr, ctx.S(when), ctx.S(0.25f), ctx.Rng.Range(3000f, 4500f), Cymbal, 0.5f, 0f, 0.5f));
                break;
            case Tex.Air:
            case Tex.Water:
                Gen.Additive(t, c.Sr, 0, t.Length, hz * 0.5f, Reed, flat, 0.35f, ref c.Rng, 5f);
                Gen.Additive(t, c.Sr, 0, t.Length, hz * 2f / 3f, Reed, flat, 0.3f, ref c.Rng, 5f);
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 3000f, 3000f, 2f, flat, 0.25f, ref c.Rng);
                break;
            case Tex.Rumble:
                Scatter(ctx, dur, 11f, when =>
                {
                    Gen.Osc(t, ctx.Sr, ctx.S(when), ctx.S(0.1f), Wave.Sine, 150f, 150f, Env.Perc(0.003f, 0.09f), 0.8f);
                    Wire(ctx, when, 0.4f);
                });
                break;
            case Tex.Buzz:
                Gen.Additive(t, c.Sr, 0, t.Length, 392f, Reed, flat, 1f, ref c.Rng, 3f);
                Fx.Tremolo(t, c.Sr, 12f, 0.8f);
                break;
            default:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz, hz, flat, 0.6f);
                Fx.Tremolo(t, c.Sr, 1.5f, 1f);
                float[] other = Temp(c, dur);
                Gen.Osc(other, c.Sr, 0, other.Length, Wave.Sine, hz * 4f / 3f, hz * 4f / 3f, flat, 0.6f);
                for (int i = 0; i < other.Length; i++) other[i] *= 0.5f - 0.5f * MathF.Sin(Dsp.Tau * 1.5f * i / c.Sr);
                for (int i = 0; i < t.Length && i < other.Length; i++) t[i] += other[i];
                Scatter(ctx, dur, 3f, when => Wire(ctx, when, 0.6f));
                break;
        }
        MixNormalized(c, at, t, gain * 0.5f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        // A glissando across the strings: scale steps, each with its wire.
        int n = Math.Clamp((int)(dur / 0.045f), 3, 9);
        for (int i = 0; i < n; i++)
        {
            float hz = MathF.Max(130f, Dsp.MidiToHz(Snap(c, Dsp.Lerp(fromMidi, toMidi, i / (float)(n - 1)))));
            float when = at + i * dur / n;
            Wire(c, when, gain * 0.35f);
            Gen.Osc(c.Buf, c.Sr, c.S(when), c.S(0.16f), Wave.Sine, hz, hz, Env.Perc(0.002f, 0.14f), gain * 0.4f);
        }
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain)
    {
        // The sheng: a cluster of stacked fourths, breathing.
        float hz = MathF.Max(130f, Dsp.MidiToHz(midi));
        var env = Env.Swell(dur * 0.3f, dur * 0.35f, dur * 0.35f);
        Wire(c, at, gain * 0.25f);
        Gen.Additive(c.Buf, c.Sr, c.S(at), c.S(dur), hz, Reed, env, gain * 0.22f, ref c.Rng, 4f);
        Gen.Additive(c.Buf, c.Sr, c.S(at), c.S(dur), hz * 4f / 3f, Reed, env, gain * 0.18f, ref c.Rng, 4f);
        Gen.Additive(c.Buf, c.Sr, c.S(at), c.S(dur), hz * 16f / 9f, Reed, env, gain * 0.12f, ref c.Rng, 4f);
    }

    public override void Room(RenderCtx c)
    {
        Fx.HighPass(c.Buf, c.Sr, 120f, 2);
        Fx.Reverb(c.Buf, c.Sr, 0.5f, 0.1f, 0.35f);
    }
}

/// <summary>VOID — black: subtraction. Nothing is added here; sounds lose their top end,
/// their bit depth and their pitch, sag downward, and swell in reverse before they are cut.
/// A sub drone with octave partials (so a small speaker still has something to play), a
/// near-endless room. No pulse, no melody of its own.</summary>
public sealed class VoidBlack : Medium
{
    private static readonly float[] Octaves = { 0.45f, 1f, 0f, 0.8f, 0f, 0f, 0f, 0.5f };

    public VoidBlack()
    {
        Id = "VOID";
        Name = "the VOID's black";
        Rule = "subtraction: band-limited to 800 Hz, seven bits, pitches that sag, swells that are cut off; a sub drone with octave partials";
        Forbidden = "pulse, melody, brightness";
        Space = "near-endless and dark";
        Tonic = 38;                                  // D2 (the drone sits an octave under, D1)
        Scale = new[] { 0, 1, 6, 7 };                // no scale: tonic, its neighbour, the tritone
        Motif = new[] { 0, 1 };
        SampleRate = 11025;
        Tail = 1.1f;
        MinAttack = 0.004f;
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        // Sucked inward: it grows, and is cut.
        float[] t = Temp(c, dur);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, Height(MathF.Max(from, to), 200f, 1.5f), Height(MathF.Min(from, to), 200f, 1.5f),
            0.9f, Env.Gate(0f, dur + 1f, 0f), 1f, ref c.Rng);
        for (int i = 0; i < t.Length; i++) t[i] *= MathF.Pow(i / (float)t.Length, 2.5f);
        GateEnd(t, c.Sr, 0.004f);
        MixNormalized(c, at, t, gain * 0.8f);
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain) =>
        Gen.Additive(c.Buf, c.Sr, c.S(at), c.S(0.6f + 0.5f * weight), 36.7f / MathF.Sqrt(c.Size), Octaves,
            Env.Perc(0.004f, 0.5f + 0.5f * weight), gain * 0.6f, ref c.Rng);

    public override void Tick(RenderCtx c, float at, float bright, float gain)
    {
        float[] t = Temp(c, 0.04f);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Low, 500f + 300f * bright, 300f, 0.7f, Env.Perc(0.002f, 0.012f), 1f, ref c.Rng);
        MixNormalized(c, at, t, gain * 0.5f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(0.04f), Wave.Sine, 147f / MathF.Sqrt(c.Size), 140f / MathF.Sqrt(c.Size), Env.Perc(0.004f, 0.03f), gain * 0.5f);
    }

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        // A cluster that beats against itself and sags: pitch is being taken away too.
        float hz = Dsp.MidiToHz(midi);
        float len = kind == ToneKind.Short ? 0.4f : kind == ToneKind.Accent ? 0.6f : MathF.Max(0.4f, dur);
        var env = Env.Swell(MathF.Min(0.04f, len * 0.2f), len * 0.3f, len * 0.65f);
        float sag = Dsp.Ratio(-0.3f * Mut(c, 0.3f));
        float up = 1f + 0.012f * Mut(c, 0.4f), down = 1f - 0.015f * Mut(c, 0.4f);
        int at0 = c.S(at), n = c.S(len);
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, hz, hz * sag, env, gain * 0.4f);
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, hz * up, hz * up * sag, env, gain * 0.3f, phase: c.Rng.Next());
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, hz * down, hz * down * sag, env, gain * 0.3f, phase: c.Rng.Next());
        if (kind == ToneKind.Accent) Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, hz * Dsp.Ratio(6f), hz * Dsp.Ratio(6f) * sag, env, gain * 0.25f);
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        var flat = Env.Gate(0f, dur + 1f, 0f);
        Gen.Additive(t, c.Sr, 0, t.Length, 36.7f, Octaves, flat, 0.5f, ref c.Rng, 8f);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, 300f, 300f, 0.7f, flat, 0.6f, ref c.Rng, 0.3f, 0.6f);
        switch (tex)
        {
            case Tex.Fire: Gen.Crackle(t, c.Sr, 0, t.Length, 14f, 0.6f, ref c.Rng, 700f); break;
            case Tex.Ice:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 587f, 587f, flat, 0.12f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 593f, 593f, flat, 0.12f);
                break;
            case Tex.Buzz: Fx.Tremolo(t, c.Sr, 9f, 0.6f); break;
            case Tex.Daze:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 110f, 110f, flat, 0.3f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 113f, 113f, flat, 0.3f);
                break;
        }
        MixNormalized(c, at, t, gain * 0.6f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        // Always downward: things fall into it.
        float top = Dsp.MidiToHz(MathF.Max(fromMidi, toMidi)), bottom = Dsp.MidiToHz(MathF.Min(fromMidi, toMidi) - 5f);
        var env = Env.Swell(dur * 0.2f, dur * 0.3f, dur * 0.5f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(dur), Wave.Sine, top, bottom, env, gain * 0.45f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(dur), Wave.Sine, top * (1f + 0.015f * Mut(c, 0.4f)), bottom * 1.015f, env, gain * 0.35f, phase: c.Rng.Next());
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain)
    {
        var env = Env.Swell(dur * 0.4f, dur * 0.2f, dur * 0.4f);
        Gen.Additive(c.Buf, c.Sr, c.S(at), c.S(dur), Dsp.MidiToHz(midi - 12f), Octaves, env, gain * 0.5f, ref c.Rng, 10f);
    }

    public override void Room(RenderCtx c)
    {
        Fx.LowPass(c.Buf, c.Sr, 800f, 2);
        Fx.Reverb(c.Buf, c.Sr, 2.2f, 0.7f, 0.6f, 0.02f);
    }

    public override void Crush(RenderCtx c) => Fx.BitCrush(c.Buf, 7);
}

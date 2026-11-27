using System;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

// The first five media (Docs/Audio.gdd §5). Each class is one art medium's sound-making
// rule, written as the six primitives every recipe is built from. The numbers are reasoned
// from the style bible and the research digest (R1 §4.7) and judged on spectrogram sheets —
// nobody has heard them yet.

/// <summary>VK — storybook watercolour: soft edges and bleeding washes. Every attack is at
/// least 8 ms, pitched voices are doubled a few cents apart, and a small bright room blurs
/// the tail. No hard square waves, no clipping, nothing dry and gated.</summary>
public sealed class Watercolour : Medium
{
    private static readonly Mode[] Marimba = { new(1f, 0.55f, 1f), new(3.93f, 0.16f, 0.42f), new(9.2f, 0.07f, 0.16f) };
    private static readonly Mode[] Pan = { new(1f, 0.9f, 1f), new(2f, 0.6f, 0.5f), new(3.01f, 0.35f, 0.22f), new(4.03f, 0.2f, 0.1f) };
    private static readonly Mode[] Block = { new(1f, 0.07f, 1f), new(2.7f, 0.04f, 0.45f), new(5.2f, 0.02f, 0.16f) };

    public Watercolour()
    {
        Id = "VK";
        Name = "storybook watercolour";
        Rule = "soft edges and bleeding washes: attacks of 8 ms or more, mallets and steel pan doubled a few cents apart, pink-noise washes";
        Forbidden = "hard square waves, clipping, dry gated sounds";
        Space = "small bright room";
        Tonic = 62;                                  // D4
        Scale = new[] { 0, 2, 4, 7, 9 };             // gong (major) pentatonic
        Motif = new[] { 0, 2, 3, 2, 4 };
        SampleRate = 16000;                          // under 1 % of its energy is above 5 kHz
        Tail = 0.28f;
        MinAttack = 0.008f;
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        float[] t = Temp(c, dur);
        float attack = MathF.Max(0.012f, dur * 0.3f);
        var stroke = Env.Swell(attack, dur * 0.15f, MathF.Max(0.01f, dur - attack - dur * 0.15f));
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, Height(from, 500f, 3.3f), Height(to, 500f, 3.3f), 1.2f, stroke, 1f, ref c.Rng);
        // The wash that bleeds under the stroke: an octave lower and slower.
        var wash = Env.Swell(dur * 0.45f, 0f, dur * 0.55f);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, Height(from, 250f, 3.3f), Height(to, 250f, 3.3f), 0.8f, wash, 0.6f, ref c.Rng);
        MixNormalized(c, at, t, gain * 0.8f);
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        float f = (150f - 70f * weight) / MathF.Sqrt(c.Size) * Mut(c, 0.04f);
        float len = (0.1f + 0.22f * weight) * Mut(c, 0.12f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(len + 0.03f), Wave.Sine, f, f * 0.5f, Env.Perc(0.010f, len), gain * 0.9f);
        float[] sand = Temp(c, 0.09f);
        Gen.Noise(sand, c.Sr, 0, sand.Length, Tint.Pink, Band.Low, 700f, 400f, 0.7f, Env.Perc(0.012f, 0.06f), 1f, ref c.Rng);
        MixNormalized(c, at, sand, gain * 0.28f);
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain) =>
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.12f), c.Rng.Jitter(900f + 900f * bright, 0.04f) / MathF.Sqrt(c.Size), Block, gain * 0.8f, 0.008f);

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float hz = Dsp.MidiToHz(midi) * Cents(c);
        int at0 = c.S(at);
        float ring = Mut(c, 0.15f), bleed = Mut(c, 0.3f);
        switch (kind)
        {
            case ToneKind.Long:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(dur + 0.4f), hz, Pan, gain * 0.6f, 0.012f, MathF.Max(0.5f, dur / 0.7f) * ring);
                Gen.Fm(c.Buf, c.Sr, at0, c.S(dur), hz, 2f, 1.2f, dur * 0.5f, Env.Perc(0.012f, dur), gain * 0.22f);
                break;
            case ToneKind.Accent:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.6f), hz, Marimba, gain * 0.7f, 0.008f, ring);
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.7f), hz * 1.003f, Pan, gain * 0.4f * bleed, 0.010f, 0.6f);
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.3f), hz * 2f, Marimba, gain * 0.2f, 0.008f, 0.5f);
                break;
            case ToneKind.Low:
                Gen.Osc(c.Buf, c.Sr, at0, c.S(dur), Wave.Triangle, hz, hz, Env.Swell(0.02f, dur * 0.4f, dur * 0.55f), gain * 0.6f);
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.4f), hz * 2f, Marimba, gain * 0.25f * bleed, 0.010f, ring);
                break;
            default:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.6f), hz, Marimba, gain * 0.7f, 0.008f, ring);
                Gen.Modal(c.Buf, c.Sr, at0 + c.S(c.Rng.Range(0f, 0.004f)), c.S(0.6f), hz * 1.004f, Marimba, gain * 0.35f * bleed, 0.008f);   // the bleed
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
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, 1800f, 1800f, 0.8f, flat, 1f, ref c.Rng, 9f, 0.5f);
                Gen.Crackle(t, c.Sr, 0, t.Length, 28f, 0.5f, ref c.Rng, 2500f);
                break;
            case Tex.Ice:
                Gen.Additive(t, c.Sr, 0, t.Length, 2200f, stackalloc float[] { 1f, 0f, 0.4f }, flat, 0.5f, ref c.Rng, 12f);
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.High, 5000f, 5000f, 0.7f, flat, 0.06f, ref c.Rng);
                Fx.Tremolo(t, c.Sr, 5f, 0.4f);
                break;
            case Tex.Air:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Low, 900f, 900f, 0.7f, flat, 1f, ref c.Rng, 0.8f, 0.4f);
                break;
            case Tex.Water:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, 600f, 600f, 0.9f, flat, 1f, ref c.Rng, 0.7f, 0.6f);
                break;
            case Tex.Rumble:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, 160f, 160f, 0.7f, flat, 1f, ref c.Rng);
                break;
            case Tex.Buzz:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Triangle, 110f, 110f, flat, 1f);
                Fx.Tremolo(t, c.Sr, 14f, 0.7f);
                break;
            default:
                float hz = Dsp.MidiToHz(c.Deg(0));
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz, hz, flat, 0.5f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz + 3f, hz + 3f, flat, 0.5f);
                break;
        }
        MixNormalized(c, at, t, gain * 0.6f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        float f0 = Dsp.MidiToHz(fromMidi), f1 = Dsp.MidiToHz(toMidi);
        float rise = Mut(c, 0.25f);
        var env = Env.Swell(MathF.Max(0.015f, dur * 0.2f * rise), dur * 0.4f, dur * (0.6f - 0.2f * rise));
        int at0 = c.S(at), n = c.S(dur);
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, f0, f1, env, gain * 0.5f);
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Sine, f0 * 1.006f, f1 * 1.006f, env, gain * 0.3f * Mut(c, 0.3f), phase: c.Rng.Next());
        Gen.Osc(c.Buf, c.Sr, at0, n, Wave.Triangle, f0 * 2f, f1 * 2f, env, gain * 0.1f * Mut(c, 0.4f));
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain) =>
        Gen.Additive(c.Buf, c.Sr, c.S(at), c.S(dur), Dsp.MidiToHz(midi), stackalloc float[] { 1f, 0.5f, 0.25f, 0.12f },
            Env.Swell(dur * 0.3f, dur * 0.3f, dur * 0.4f), gain * 0.35f, ref c.Rng, 6f);

    public override void Room(RenderCtx c)
    {
        Fx.LowPass(c.Buf, c.Sr, 9000f);
        Fx.Reverb(c.Buf, c.Sr, 0.45f, 0.35f, 0.3f);
    }
}

/// <summary>SL — flat poster: flat planes and a hard terminator. Only pure sines and
/// triangles, levels in terraces, no vibrato, hard gates, never more than three voices; then
/// a huge empty hall with a long echo. No noise of any kind.</summary>
public sealed class Poster : Medium
{
    private static readonly float[] OnePlane = { 1f };

    public Poster()
    {
        Id = "SL";
        Name = "flat poster";
        Rule = "flat planes, hard terminator: pure sines and triangles only, terraced levels, hard gates, no vibrato, three voices at most";
        Forbidden = "noise textures, vibrato, busy rhythm, clutter";
        Space = "huge hall, long echo";
        Tonic = 57;                                  // A3
        Scale = new[] { 0, 1, 4, 7 };                // Phrygian dominant, sparse
        Motif = new[] { 0, 3, 2, 1 };
        SampleRate = 16000;                          // pure tones under 5 kHz: nothing is lost
        Tail = 0.6f;
        MinAttack = 0.003f;
    }

    /// <summary>A flat plane of tone: a row of hard-edged plateaus. The 3 ms edges overlap
    /// their neighbours', so a terrace is a step in level and not a click.</summary>
    private static void Plane(RenderCtx c, float at, float hz, float dur, float gain, Wave wave, ReadOnlySpan<float> levels)
    {
        float seg = dur / levels.Length;
        for (int i = 0; i < levels.Length; i++)
        {
            var env = Env.Gate(0.003f, MathF.Max(0.001f, seg - 0.003f), 0.003f);
            Gen.Osc(c.Buf, c.Sr, c.S(at + i * seg), c.S(seg + 0.004f), wave, hz, hz, env, gain * levels[i], phase: hz * (i * seg));
        }
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        // Two flat planes of pitch, not a sweep.
        float split = dur * 0.5f * Mut(c, 0.2f);
        Plane(c, at, Height(from, 220f, 3f), split, gain * 0.6f, Wave.Sine, stackalloc float[] { 1f });
        Plane(c, at + split, Height(to, 220f, 3f), dur - split, gain * 0.6f, Wave.Sine, stackalloc float[] { 0.6f * Mut(c, 0.25f) });
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        float f = (110f - 45f * weight) / MathF.Sqrt(c.Size) * Mut(c, 0.03f);
        Plane(c, at, f, (0.06f + 0.12f * weight) * Mut(c, 0.15f), gain * 0.9f, Wave.Sine, stackalloc float[] { 1f, 0.5f * Mut(c, 0.3f) });
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain) =>
        Plane(c, at, c.Rng.Jitter(1760f * (1f + bright), 0.01f) / MathF.Sqrt(c.Size), 0.016f, gain * 0.6f, Wave.Sine, stackalloc float[] { 1f });

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float hz = Dsp.MidiToHz(midi) * Cents(c, 3f);
        float edge = Mut(c, 0.4f), hold = Mut(c, 0.12f);
        switch (kind)
        {
            case ToneKind.Long:
                Plane(c, at, hz, dur, gain * 0.6f, Wave.Sine, stackalloc float[] { 1f, 0.7f, 0.5f * hold, 0.35f, 0.25f * hold });
                Plane(c, at, hz * 3f, dur * 0.3f * hold, gain * 0.12f * edge, Wave.Sine, stackalloc float[] { 1f, 0.5f });
                break;
            case ToneKind.Accent:
                Plane(c, at, hz, 0.24f * hold, gain * 0.6f, Wave.Sine, stackalloc float[] { 1f, 0.5f, 0.25f });
                Plane(c, at, hz * 2f, 0.24f * hold, gain * 0.3f * edge, Wave.Sine, stackalloc float[] { 1f, 0.5f });
                Plane(c, at, hz * 3f, 0.08f, gain * 0.15f * edge, Wave.Sine, stackalloc float[] { 1f });
                break;
            case ToneKind.Low:
                Plane(c, at, hz, dur, gain * 0.7f, Wave.Triangle, stackalloc float[] { 1f, 0.6f * hold });
                break;
            default:
                Plane(c, at, hz, 0.22f * hold, gain * 0.6f, Wave.Sine, stackalloc float[] { 1f, 0.5f, 0.25f });
                Plane(c, at, hz * 3f, 0.08f * hold, gain * 0.15f * edge, Wave.Sine, stackalloc float[] { 1f });   // the glass edge
                break;
        }
    }

    /// <summary>Two pure tones taking turns under a square chop — this medium's only texture.</summary>
    private static void Alternate(float[] t, int sr, float f1, float f2, float chopHz, Wave wave, ref SynthRng rng)
    {
        double p1 = rng.Next(), p2 = rng.Next();
        float turn = rng.Next();
        float smooth = 0f, coef = OnePole.Coef(250f, sr);
        for (int i = 0; i < t.Length; i++)
        {
            float target = MathF.Sin(Dsp.Tau * (chopHz * i / sr + turn)) >= 0f ? 1f : 0f;
            smooth += coef * (target - smooth);
            float a = wave == Wave.Sine ? MathF.Sin(Dsp.Tau * (float)p1) : 4f * MathF.Abs((float)p1 - 0.5f) - 1f;
            float b = wave == Wave.Sine ? MathF.Sin(Dsp.Tau * (float)p2) : 4f * MathF.Abs((float)p2 - 0.5f) - 1f;
            t[i] += a * smooth + b * (1f - smooth) * (f2 > 0f ? 1f : 0f);
            p1 += f1 / sr;
            p2 += f2 / sr;
            if (p1 >= 1.0) p1 -= 1.0;
            if (p2 >= 1.0) p2 -= 1.0;
        }
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        switch (tex)
        {
            case Tex.Fire: Alternate(t, c.Sr, 1568f, 1661f, 12f, Wave.Sine, ref c.Rng); break;
            case Tex.Ice: Alternate(t, c.Sr, 2637f, 3951f, 6f, Wave.Sine, ref c.Rng); break;
            case Tex.Air: Alternate(t, c.Sr, 110f, 0f, 3f, Wave.Triangle, ref c.Rng); break;
            case Tex.Water: Alternate(t, c.Sr, 330f, 440f, 4f, Wave.Sine, ref c.Rng); break;
            case Tex.Rumble: Alternate(t, c.Sr, 65.4f, 98f, 5f, Wave.Sine, ref c.Rng); break;
            case Tex.Buzz: Alternate(t, c.Sr, 220f, 0f, 16f, Wave.Triangle, ref c.Rng); break;
            default: Alternate(t, c.Sr, 440f, 466.2f, 2.5f, Wave.Sine, ref c.Rng); break;
        }
        MixNormalized(c, at, t, gain * 0.5f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        int n = Math.Clamp((int)MathF.Round(dur / 0.07f), 2, 5);
        for (int i = 0; i < n; i++)
        {
            float midi = Snap(c, Dsp.Lerp(fromMidi, toMidi, i / (float)(n - 1)));
            Plane(c, at + i * dur / n, Dsp.MidiToHz(midi), dur / n * Mut(c, 0.12f), gain * 0.6f * Mut(c, 0.2f), Wave.Sine, OnePlane);
        }
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain)
    {
        float hz = Dsp.MidiToHz(midi);
        Plane(c, at, hz, dur, gain * 0.3f, Wave.Sine, stackalloc float[] { 0.6f, 1f, 0.6f });
        Plane(c, at, hz * 1.5f, dur, gain * 0.25f, Wave.Sine, stackalloc float[] { 0.5f, 1f, 0.5f });
        Plane(c, at, hz * 2f, dur, gain * 0.2f, Wave.Sine, stackalloc float[] { 1f, 0.5f, 1f });
    }

    public override void Room(RenderCtx c)
    {
        Fx.HighPass(c.Buf, c.Sr, 45f);
        Fx.Echo(c.Buf, c.Sr, 0.22f, 0.22f, 0.3f);
        Fx.Reverb(c.Buf, c.Sr, 0.8f, 0.25f, 0.3f);
    }
}

/// <summary>BM — freehand ink: one stroke, pressed and lifted, and bare paper around it.
/// Every note is a gesture — a pluck that bends into pitch, a vibrato that dies away, a
/// breath — played alone over nothing. Dry, with a single echo. No pads, no chords, nothing
/// warm, nothing machine-regular.</summary>
public sealed class Ink : Medium
{
    private static readonly Mode[] Muyu = { new(1f, 0.09f, 1f), new(2.4f, 0.05f, 0.5f), new(4.1f, 0.03f, 0.2f) };
    private static readonly Mode[] Bangu = { new(1f, 0.03f, 1f), new(1.8f, 0.02f, 0.6f), new(3.3f, 0.012f, 0.3f) };

    public Ink()
    {
        Id = "BM";
        Name = "freehand ink (xieyi)";
        Rule = "one pressed-and-lifted stroke: plucks that bend into pitch, vibrato that dies away, breath, wood knocks; solo lines and rests";
        Forbidden = "pads, chords, warm timbres, machine-regular rhythm";
        Space = "dry, one 280 ms echo";
        Tonic = 64;                                  // E4
        Scale = new[] { 0, 3, 5, 7, 10 };            // yu (minor) pentatonic
        Motif = new[] { 0, 2, 1, 3 };
        Tail = 0.36f;
        MinAttack = 0f;
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        float[] t = Temp(c, dur);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, Height(from, 900f, 2.3f), Height(to, 900f, 2.3f), 2.5f,
            Env.PressLift(dur, 0.15f, 2.2f), 1f, ref c.Rng);
        MixNormalized(c, at, t, gain * 0.7f);
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        float f = (420f - 180f * weight) / MathF.Sqrt(c.Size);
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.25f), f, Muyu, gain * 0.9f, 0f, 0.8f + 0.6f * weight);
        Gen.Noise(c.Buf, c.Sr, c.S(at), c.S(0.02f), Tint.White, Band.High, 2500f, 2500f, 0.7f, Env.Perc(0.0005f, 0.012f), gain * 0.12f, ref c.Rng);
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain) =>
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.08f), c.Rng.Jitter(1400f + 1200f * bright, 0.04f) / MathF.Sqrt(c.Size), Bangu, gain * 0.8f);

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float t60 = kind switch { ToneKind.Short => 0.5f, ToneKind.Long => MathF.Max(0.8f, dur * 1.2f), _ => 0.9f };
        float hz = Dsp.MidiToHz(kind == ToneKind.Low ? midi - 12f : midi);
        float bright = kind == ToneKind.Low ? 0.6f : kind == ToneKind.Accent ? 1f : 0.85f;
        float bend = kind == ToneKind.Accent ? -2f : kind == ToneKind.Short ? -0.7f : -1.2f;
        var vib = new Vib(5.5f, kind == ToneKind.Short ? 0f : 0.35f, 0.12f, 0.35f);
        Gen.Pluck(c.Buf, c.Sr, c.S(at), c.S(t60 * 1.1f + 0.05f), hz, bright, t60, 0.18f, gain * 0.8f, ref c.Rng, bend, 0.07f, vib);
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        var flat = Env.Gate(0f, dur + 1f, 0f);
        var ctx = new RenderCtx { Buf = t, Sr = c.Sr, Rng = c.Rng.Fork(7), Medium = this, Tonic = c.Tonic, Scale = c.Scale };
        switch (tex)
        {
            case Tex.Fire:
                Gen.Crackle(t, c.Sr, 0, t.Length, 18f, 0.8f, ref c.Rng, 2200f);
                Scatter(ctx, dur, 5f, when => Gen.Noise(t, ctx.Sr, ctx.S(when), ctx.S(0.08f), Tint.White, Band.Mid, 2400f, 2000f, 1.5f,
                    Env.PressLift(0.08f), 0.3f, ref ctx.Rng));
                break;
            case Tex.Ice:
                Scatter(ctx, dur, 5f, when =>
                {
                    float hz = ctx.Rng.Range(2500f, 4000f);
                    Gen.Osc(t, ctx.Sr, ctx.S(when), ctx.S(0.16f), Wave.Sine, hz, hz, Env.Perc(0.001f, 0.15f), 0.6f);
                });
                break;
            case Tex.Air:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 2500f, 2500f, 0.7f, flat, 1f, ref c.Rng);
                break;
            case Tex.Water:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 900f, 900f, 0.8f, flat, 1f, ref c.Rng, 0.6f, 0.3f);
                break;
            case Tex.Rumble:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, 220f, 220f, 0.7f, flat, 0.25f, ref c.Rng);
                Scatter(ctx, dur, 4f, when => Gen.Modal(t, ctx.Sr, ctx.S(when), ctx.S(0.2f), ctx.Rng.Range(180f, 240f), Muyu, 0.8f));
                break;
            case Tex.Buzz:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 700f, 700f, 3f, flat, 1f, ref c.Rng, 11f, 0.8f);
                break;
            default:
                float hz2 = Dsp.MidiToHz(c.Deg(0) + 12f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz2, hz2, flat, 0.6f, vib: new Vib(3f, 0.8f));
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, hz2 * 2f, hz2 * 2f, 4f, flat, 0.15f, ref c.Rng);
                break;
        }
        MixNormalized(c, at, t, gain * 0.5f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        float f0 = Dsp.MidiToHz(fromMidi), f1 = Dsp.MidiToHz(toMidi);
        var env = Env.PressLift(dur, 0.2f, 1.5f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(dur), Wave.Sine, f0, f1, env, gain * 0.5f, vib: new Vib(7f, 0.3f, dur * 0.6f));
        float[] breath = Temp(c, dur);
        Gen.Noise(breath, c.Sr, 0, breath.Length, Tint.White, Band.Mid, f0 * 2f, f1 * 2f, 4f, env, 1f, ref c.Rng);
        MixNormalized(c, at, breath, gain * 0.22f);
    }

    /// <summary>Bare paper: the bed is almost nothing — a faint breath at the pitch.</summary>
    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain)
    {
        float hz = Dsp.MidiToHz(midi);
        float[] t = Temp(c, dur);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, hz * 2f, hz * 2f, 6f, Env.PressLift(dur, 0.3f, 1.2f), 1f, ref c.Rng);
        MixNormalized(c, at, t, gain * 0.12f);
        Gen.Osc(c.Buf, c.Sr, c.S(at), c.S(dur), Wave.Sine, hz, hz, Env.PressLift(dur, 0.3f, 1.2f), gain * 0.1f);
    }

    public override void Room(RenderCtx c)
    {
        Fx.HighPass(c.Buf, c.Sr, 180f);
        Fx.Echo(c.Buf, c.Sr, 0.28f, 0f, 0.2f);
        Fx.Reverb(c.Buf, c.Sr, 0.25f, 0.3f, 0.12f);
    }
}

/// <summary>HC — lit realism: weight, relief, and one saturated colour. Physical models —
/// drum skins, iron, stone — with real low end in a long stone room; the whole mix is dark,
/// and only fire is allowed to be bright. No chip waves, no bright leads.</summary>
public sealed class LitRealism : Medium
{
    private static readonly Mode[] Drum = { new(1f, 0.5f, 1f), new(1.59f, 0.3f, 0.6f), new(2.14f, 0.2f, 0.4f), new(2.65f, 0.12f, 0.25f) };
    private static readonly Mode[] Iron = { new(1f, 0.25f, 1f), new(2.76f, 0.18f, 0.6f), new(5.4f, 0.1f, 0.35f), new(8.93f, 0.05f, 0.2f) };
    private static readonly Mode[] Bell = { new(1f, 1.4f, 1f), new(2f, 1f, 0.5f), new(2.76f, 0.8f, 0.45f), new(4.07f, 0.5f, 0.25f), new(5.4f, 0.3f, 0.15f) };

    public LitRealism()
    {
        Id = "HC";
        Name = "lit realism";
        Rule = "weight and relief: struck skins, iron and stone as physical models, real low end, everything dark except fire";
        Forbidden = "chip waves, bright leads, cheerful major";
        Space = "stone cave, long and damped";
        Tonic = 50;                                  // D3
        Scale = new[] { 0, 2, 3, 5, 6, 7, 8, 10 };   // Aeolian with the tritone
        Motif = new[] { 0, 2, 1, 4 };
        SampleRate = 16000;                          // dark by rule: low-passed at 3.5 kHz
        Tail = 0.5f;
        MinAttack = 0f;
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        float[] t = Temp(c, dur);
        var env = Env.Swell(MathF.Max(0.03f, dur * 0.3f), dur * 0.2f, dur * 0.5f);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, Height(from, 220f, 3f), Height(to, 220f, 3f), 0.9f, env, 1f, ref c.Rng);
        Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, Height(from, 400f, 3f), Height(to, 400f, 3f), 1f, env, 0.6f, ref c.Rng);
        MixNormalized(c, at, t, gain * 0.8f);
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        float f = (95f - 25f * weight) / MathF.Sqrt(c.Size) * Mut(c, 0.04f);
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.7f), f, Drum, gain, 0f, (0.4f + 0.6f * weight) * Mut(c, 0.12f));
        float[] stone = Temp(c, 0.09f);
        Gen.Noise(stone, c.Sr, 0, stone.Length, Tint.Brown, Band.Low, 900f, 300f, 0.7f, Env.Perc(0.002f, 0.07f), 1f, ref c.Rng);
        MixNormalized(c, at, stone, gain * 0.4f);
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain) =>
        Gen.Modal(c.Buf, c.Sr, c.S(at), c.S(0.3f), c.Rng.Jitter(700f + 500f * bright, 0.05f) / MathF.Sqrt(c.Size), Iron, gain * 0.7f, 0f, 0.5f);

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float hz = Dsp.MidiToHz(midi) * Cents(c, 5f);
        int at0 = c.S(at);
        switch (kind)
        {
            case ToneKind.Long:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(dur + 0.6f), hz, Bell, gain * 0.6f, 0f, MathF.Max(0.6f, dur / 1.2f) * Mut(c, 0.12f));
                break;
            case ToneKind.Accent:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.9f), hz, Bell, gain * 0.6f, 0f, 0.5f * Mut(c, 0.15f));
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.2f), hz * 4f, Iron, gain * 0.25f, 0f, 0.4f);
                break;
            case ToneKind.Low:
                float[] saw = Temp(c, dur);
                Gen.Osc(saw, c.Sr, 0, saw.Length, Wave.Saw, hz, hz, Env.Swell(0.04f, dur * 0.5f, dur * 0.45f), 1f);
                Fx.LowPass(saw, c.Sr, 400f, 2);
                Mix(c, at, saw, gain * 0.8f);
                break;
            default:
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.6f), hz, Bell, gain * 0.6f, 0f, 0.3f * Mut(c, 0.2f));
                Gen.Modal(c.Buf, c.Sr, at0, c.S(0.1f), hz * 4f, Iron, gain * 0.1f * Mut(c, 0.5f), 0f, 0.3f);   // the hammer on it
                break;
        }
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        var flat = Env.Gate(0f, dur + 1f, 0f);
        var ctx = new RenderCtx { Buf = t, Sr = c.Sr, Rng = c.Rng.Fork(11), Medium = this, Tonic = c.Tonic, Scale = c.Scale };
        switch (tex)
        {
            case Tex.Fire:
                c.Bright = true;
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Pink, Band.Mid, 2200f, 2200f, 0.8f, flat, 0.5f, ref c.Rng, 7f, 0.4f);
                Gen.Crackle(t, c.Sr, 0, t.Length, 45f, 0.9f, ref c.Rng, 3500f);
                break;
            case Tex.Ice:
                c.Bright = true;
                Scatter(ctx, dur, 6f, when => Gen.Modal(t, ctx.Sr, ctx.S(when), ctx.S(0.25f), ctx.Rng.Range(2500f, 4000f), Iron, 0.5f, 0f, 0.4f));
                break;
            case Tex.Air:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, 300f, 300f, 0.7f, flat, 1f, ref c.Rng, 0.4f, 0.5f);
                break;
            case Tex.Water:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, 600f, 600f, 0.7f, flat, 0.5f, ref c.Rng, 1.3f, 0.5f);
                Scatter(ctx, dur, 5f, when => Gen.Osc(t, ctx.Sr, ctx.S(when), ctx.S(0.07f), Wave.Sine, 900f, 1400f, Env.Perc(0.002f, 0.06f), 0.7f));
                break;
            case Tex.Rumble:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.Brown, Band.Low, 120f, 120f, 0.7f, flat, 1f, ref c.Rng);
                Scatter(ctx, dur, 5f, when => Gen.Osc(t, ctx.Sr, ctx.S(when), ctx.S(0.14f), Wave.Sine, 60f, 95f, Env.Perc(0.01f, 0.12f), 0.5f));
                break;
            case Tex.Buzz:
                Gen.Crackle(t, c.Sr, 0, t.Length, 120f, 1f, ref c.Rng, 4000f);
                Fx.Resonate(t, c.Sr, 900f, 6f, 3f);
                break;
            default:
                float hz = Dsp.MidiToHz(c.Deg(0));
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz, hz, flat, 0.5f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, hz + 2.5f, hz + 2.5f, flat, 0.5f);
                break;
        }
        MixNormalized(c, at, t, gain * 0.6f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        float[] t = Temp(c, dur);
        Gen.Osc(t, c.Sr, 0, t.Length, Wave.Saw, Dsp.MidiToHz(fromMidi - 12f), Dsp.MidiToHz(toMidi - 12f),
            Env.Swell(dur * 0.3f, dur * 0.3f, dur * 0.4f), 1f);
        Fx.LowPass(t, c.Sr, 500f, 2);
        Mix(c, at, t, gain * 0.8f);
    }

    public override void Pad(RenderCtx c, float at, float midi, float dur, float gain) => Tone(c, at, midi, dur, gain, ToneKind.Low);

    public override void Room(RenderCtx c)
    {
        Fx.LowPass(c.Buf, c.Sr, c.Bright ? 7000f : 3500f, 2);
        Fx.Reverb(c.Buf, c.Sr, 1.0f, 0.6f, 0.28f, 0.012f);
    }
}

/// <summary>TD — linocut: a black key block and three flat inks printed out of register.
/// Notes are hard-gated and level moves in terraces; every mark is printed twice, the second
/// impression 10–25 ms late; shellac crackle lies over it. Dry, and band-limited like an old
/// record. No reverb, no filter sweeps, no crescendos.</summary>
public sealed class Linocut : Medium
{
    private static readonly Mode[] Clop = { new(1f, 0.06f, 1f), new(2.6f, 0.035f, 0.5f) };
    private static readonly Mode[] Knock = { new(1f, 0.025f, 1f), new(3.1f, 0.015f, 0.5f) };

    public Linocut()
    {
        Id = "TD";
        Name = "linocut";
        Rule = "a key block and flat inks out of register: hard gates, terraced levels, every mark printed twice 10–25 ms apart, crackle";
        Forbidden = "reverb washes, filter sweeps, crescendos, a fourth instrument";
        Space = "dry, band-passed 300–3500 Hz";
        Tonic = 57;                                  // A3
        Scale = new[] { 0, 3, 5, 7, 10 };            // minor pentatonic (the melody's share of Dorian)
        Motif = new[] { 0, 1, 2, 4, 3 };
        SampleRate = 11025;                          // band-passed at 3.5 kHz, like the record it imitates
        Tail = 0.06f;
        MinAttack = 0f;
    }

    /// <summary>Print a layer twice: once in register, once late and fainter.</summary>
    private static void Print(RenderCtx c, float at, float[] block, float gain, float second = 0.4f)
    {
        Mix(c, at, block, gain);
        Mix(c, at + c.Rng.Range(0.010f, 0.024f), block, gain * second);
    }

    /// <summary>Level in flat steps, each held for <paramref name="step"/> seconds.</summary>
    private static void Terrace(float[] t, int sr, float step, ReadOnlySpan<float> levels)
    {
        float smooth = levels[0], coef = OnePole.Coef(300f, sr);
        for (int i = 0; i < t.Length; i++)
        {
            float target = levels[(int)(i / (step * sr)) % levels.Length];
            smooth += coef * (target - smooth);
            t[i] *= smooth;
        }
    }

    public override void Whoosh(RenderCtx c, float at, float dur, float from, float to, float gain)
    {
        // A gouge, not a sweep: two or three chunks, each at one fixed band, with a skip between.
        int n = dur > 0.15f ? 3 : 2;
        float[] t = Temp(c, dur);
        float chunk = dur / n;
        for (int i = 0; i < n; i++)
        {
            float x = i / (float)(n - 1);
            float level = from <= to ? Dsp.Lerp(0.45f, 1f, x) : Dsp.Lerp(1f, 0.45f, x);
            float hz = Height(Dsp.Lerp(from, to, x), 700f, 2f);
            float skip = c.Rng.Range(0.012f, 0.02f);
            Gen.Noise(t, c.Sr, c.S(i * chunk), c.S(chunk), Tint.White, Band.Mid, hz, hz, 1.6f,
                Env.Gate(0.001f, MathF.Max(0.004f, chunk - skip - 0.003f), 0.002f), level, ref c.Rng);
        }
        MixNormalized(c, at, t, gain * 0.75f);
    }

    public override void Thump(RenderCtx c, float at, float weight, float gain)
    {
        float[] block = Temp(c, 0.07f + 0.05f * weight);
        Gen.Modal(block, c.Sr, 0, block.Length, (190f - 60f * weight) / MathF.Sqrt(c.Size), Clop, 1f, 0f, 1f + weight);
        GateEnd(block, c.Sr, 0.003f);
        Print(c, at, block, gain * 0.9f, 0.45f);
    }

    public override void Tick(RenderCtx c, float at, float bright, float gain)
    {
        float[] block = Temp(c, 0.04f);
        Gen.Modal(block, c.Sr, 0, block.Length, c.Rng.Jitter(1300f + 900f * bright, 0.04f) / MathF.Sqrt(c.Size), Knock, 1f);
        GateEnd(block, c.Sr, 0.003f);
        Print(c, at, block, gain * 0.8f);
    }

    public override void Tone(RenderCtx c, float at, float midi, float dur, float gain, ToneKind kind = ToneKind.Short)
    {
        float len = kind == ToneKind.Short ? 0.13f : kind == ToneKind.Accent ? 0.2f : dur;
        float hz = Dsp.MidiToHz(kind == ToneKind.Low ? midi - 12f : midi);
        float[] block = Temp(c, len);
        Gen.Pluck(block, c.Sr, 0, block.Length, hz, kind == ToneKind.Low ? 0.7f : 0.95f, 0.9f, 0.08f, 1f, ref c.Rng);
        if (kind == ToneKind.Accent)
        {
            // The whistle on top: one flat ink, two terraces.
            Gen.Osc(block, c.Sr, 0, c.S(len * 0.5f), Wave.Sine, hz * 2f, hz * 2f, Env.Gate(0.002f, len * 0.5f - 0.005f, 0.002f), 0.45f);
            Gen.Osc(block, c.Sr, c.S(len * 0.5f), c.S(len * 0.5f), Wave.Sine, hz * 2f, hz * 2f, Env.Gate(0.002f, len * 0.5f - 0.008f, 0.002f), 0.25f,
                phase: hz * 2f * len * 0.5f);
        }
        GateEnd(block, c.Sr, 0.003f);
        Print(c, at, block, gain * 0.75f, 0.35f);
    }

    public override void Texture(RenderCtx c, float at, float dur, Tex tex, float gain)
    {
        float[] t = Temp(c, dur);
        var flat = Env.Gate(0f, dur + 1f, 0f);
        switch (tex)
        {
            case Tex.Fire:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 1600f, 1600f, 1.2f, flat, 1f, ref c.Rng);
                Terrace(t, c.Sr, 0.08f, stackalloc float[] { 1f, 0.5f, 0.8f, 0.35f });
                Gen.Crackle(t, c.Sr, 0, t.Length, 60f, 0.8f, ref c.Rng, 3200f);
                break;
            case Tex.Ice:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, 2600f, 2600f, flat, 0.6f);
                Terrace(t, c.Sr, 0.12f, stackalloc float[] { 0.6f, 0f, 0.9f, 0f });
                Gen.Crackle(t, c.Sr, 0, t.Length, 20f, 0.8f, ref c.Rng, 3400f);
                break;
            case Tex.Air:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 700f, 700f, 1.4f, flat, 1f, ref c.Rng);
                Terrace(t, c.Sr, 0.3f, stackalloc float[] { 0.5f, 0.8f, 0.6f, 1f });
                break;
            case Tex.Water:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 500f, 500f, 1.2f, flat, 1f, ref c.Rng);
                Terrace(t, c.Sr, 0.2f, stackalloc float[] { 0.7f, 1f, 0.5f });
                break;
            case Tex.Rumble:
                Gen.Noise(t, c.Sr, 0, t.Length, Tint.White, Band.Mid, 320f, 320f, 1.5f, flat, 1f, ref c.Rng);
                Terrace(t, c.Sr, 0.25f, stackalloc float[] { 1f, 0.6f, 0.85f, 0.5f });
                break;
            case Tex.Buzz:
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Pulse, 220f, 220f, flat, 1f, 0.25f);
                Terrace(t, c.Sr, 0.033f, stackalloc float[] { 1f, 0f });
                break;
            default:
                float a = Dsp.MidiToHz(c.Deg(0) + 12f), b = Dsp.MidiToHz(c.Deg(1) + 12f);
                Gen.Osc(t, c.Sr, 0, t.Length, Wave.Sine, a, a, flat, 1f);
                Terrace(t, c.Sr, 0.2f, stackalloc float[] { 1f, 0f });
                float[] other = Temp(c, dur);
                Gen.Osc(other, c.Sr, 0, other.Length, Wave.Sine, b, b, flat, 1f);
                Terrace(other, c.Sr, 0.2f, stackalloc float[] { 0f, 1f });
                for (int i = 0; i < t.Length && i < other.Length; i++) t[i] += other[i];
                break;
        }
        MixNormalized(c, at, t, gain * 0.55f);
    }

    public override void Sweep(RenderCtx c, float at, float dur, float fromMidi, float toMidi, float gain)
    {
        // Three fixed whistle notes, each a flat block of ink.
        const int n = 3;
        float step = dur / n;
        for (int i = 0; i < n; i++)
        {
            float hz = Dsp.MidiToHz(Snap(c, Dsp.Lerp(fromMidi, toMidi, i / (float)(n - 1))) + 12f);
            float[] block = Temp(c, step);
            Gen.Osc(block, c.Sr, 0, block.Length, Wave.Sine, hz, hz, Env.Gate(0.002f, MathF.Max(0.004f, step - 0.016f), 0.002f), 1f);
            Print(c, at + i * step, block, gain * Dsp.Lerp(0.35f, 0.6f, i / (float)(n - 1)), 0.35f);
        }
    }

    public override void Room(RenderCtx c)
    {
        Gen.Crackle(c.Buf, c.Sr, 0, c.S(c.Body), 22f, 0.03f, ref c.Rng, 5000f);
        Fx.HighPass(c.Buf, c.Sr, 300f, 2);
        Fx.LowPass(c.Buf, c.Sr, 3500f, 2);
    }
}

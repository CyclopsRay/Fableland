using System;

namespace Fableland.Audio.Synth;

/// <summary>
/// The synthesis toolkit's foundations (<c>Docs/Audio.gdd</c> §4): a deterministic random
/// source, unit helpers, envelopes and filters. PURE C# — no Godot type appears anywhere
/// under <c>Synth/</c>, so every sound in the game can be rendered on a worker thread and
/// measured in a headless check. Everything is mono, <c>float</c>, in the range −1…1.
/// </summary>
public struct SynthRng
{
    private ulong _state;

    public SynthRng(ulong seed)
    {
        _state = seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
    }

    /// <summary>splitmix64 — small, fast, and the same on every machine.</summary>
    public ulong NextU64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>0 ≤ x &lt; 1.</summary>
    public float Next() => (NextU64() >> 40) * (1f / 16777216f);
    /// <summary>−1 ≤ x &lt; 1.</summary>
    public float Signed() => Next() * 2f - 1f;
    public float Range(float a, float b) => a + (b - a) * Next();
    public int Int(int n) => n <= 1 ? 0 : (int)(NextU64() % (ulong)n);
    public bool Chance(float p) => Next() < p;
    /// <summary>The value, moved by up to ±<paramref name="share"/> of itself — how a
    /// variant differs from its siblings.</summary>
    public float Jitter(float value, float share) => value * (1f + share * Signed());
    public SynthRng Fork(ulong salt) => new(NextU64() ^ (salt * 0xA24BAED4963EE407UL));

    /// <summary>FNV-1a, 64 bit: the same string gives the same seed on every run.</summary>
    public static ulong Hash(string text)
    {
        ulong h = 0xCBF29CE484222325UL;
        if (text != null)
            for (int i = 0; i < text.Length; i++) { h ^= text[i]; h *= 0x100000001B3UL; }
        return h;
    }
}

public static class Dsp
{
    public const float Tau = 6.2831855f;
    public const float Pi = 3.1415927f;

    public static float MidiToHz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);
    public static float Ratio(float semitones) => MathF.Pow(2f, semitones / 12f);
    public static float DbToLin(float db) => MathF.Pow(10f, db / 20f);
    public static float LinToDb(float lin) => lin <= 1e-9f ? -180f : 20f * MathF.Log10(lin);
    public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Semitones above the tonic for a scale degree; degrees past the scale's end
    /// climb an octave, negative ones fall one.</summary>
    public static int ScaleSemis(int[] scale, int degree)
    {
        if (scale == null || scale.Length == 0) return degree * 2;
        int n = scale.Length;
        int octave = degree >= 0 ? degree / n : -((-degree + n - 1) / n);
        int index = degree - octave * n;
        return scale[index] + 12 * octave;
    }

    /// <summary>The smallest transposition, in semitones, that moves one tonic onto another
    /// (−6…+6): a sound keeps its register when it changes key.</summary>
    public static int NearestShift(int fromTonic, int toTonic)
    {
        int d = ((toTonic - fromTonic) % 12 + 12) % 12;
        return d > 6 ? d - 12 : d;
    }
}

/// <summary>A note's loudness over time. Four shapes cover every medium: a struck sound
/// (<see cref="Perc"/>), a hard gate, a smooth swell, and the ink stroke that is pressed and
/// lifted. <see cref="Stepped"/> quantises level and time — a chip's volume register.</summary>
public struct Env
{
    public enum Shape : byte { Perc, Gate, Swell, PressLift }

    public Shape Kind;
    public float A, B, C;
    public int Levels;
    public float Step;

    /// <summary>Rise over <paramref name="attack"/> s, then fall 60 dB in <paramref name="t60"/> s.</summary>
    public static Env Perc(float attack, float t60) => new() { Kind = Shape.Perc, A = attack, B = MathF.Max(0.001f, t60) };
    /// <summary>A flat plateau with straight ramps: the hard gate.</summary>
    public static Env Gate(float attack, float hold, float release) => new() { Kind = Shape.Gate, A = attack, B = hold, C = release };
    /// <summary>A plateau with raised-cosine ramps: no corner anywhere.</summary>
    public static Env Swell(float attack, float hold, float release) => new() { Kind = Shape.Swell, A = attack, B = hold, C = release };
    /// <summary>Pressed and lifted: up to full at <paramref name="peakAt"/> of the length,
    /// then a long taper.</summary>
    public static Env PressLift(float length, float peakAt = 0.15f, float power = 2f) =>
        new() { Kind = Shape.PressLift, A = MathF.Max(0.001f, length), B = Dsp.Clamp01(peakAt), C = power };

    /// <summary>Quantise to <paramref name="levels"/> volume steps, changing only every
    /// <paramref name="step"/> seconds.</summary>
    public Env Stepped(int levels, float step)
    {
        Levels = levels;
        Step = step;
        return this;
    }

    /// <summary>Seconds until the envelope is silent (60 dB down for a struck sound).</summary>
    public float Length => Kind switch
    {
        Shape.Perc => A + B,
        Shape.PressLift => A,
        _ => A + B + C,
    };

    public float At(float t)
    {
        if (t < 0f) return 0f;
        if (Step > 0f) t = MathF.Floor(t / Step) * Step;
        float v;
        switch (Kind)
        {
            case Shape.Perc:
                v = t < A ? 0.5f - 0.5f * MathF.Cos(Dsp.Pi * t / A) : MathF.Exp(-6.9078f * (t - A) / B);
                break;
            case Shape.Gate:
                if (t < A) v = t / A;
                else if (t < A + B) v = 1f;
                else if (t < A + B + C) v = 1f - (t - A - B) / C;
                else v = 0f;
                break;
            case Shape.Swell:
                if (t < A) v = 0.5f - 0.5f * MathF.Cos(Dsp.Pi * t / A);
                else if (t < A + B) v = 1f;
                else if (t < A + B + C) v = 0.5f + 0.5f * MathF.Cos(Dsp.Pi * (t - A - B) / C);
                else v = 0f;
                break;
            default:
                float x = t / A;
                if (x >= 1f) v = 0f;
                else if (x < B) v = MathF.Sin(0.5f * Dsp.Pi * x / B);
                else v = MathF.Pow(1f - (x - B) / (1f - B), C);
                break;
        }
        if (Levels > 0) v = MathF.Round(v * Levels) / Levels;
        return v;
    }
}

/// <summary>Vibrato: rate, depth in semitones, a delay before it starts, and — for the ink
/// medium, whose vibrato dies away — the seconds its depth takes to fall to a third.</summary>
public struct Vib
{
    public float Hz, Semis, Delay, Fade;

    public Vib(float hz, float semis, float delay = 0f, float fade = 0f)
    {
        Hz = hz;
        Semis = semis;
        Delay = delay;
        Fade = fade;
    }

    /// <summary>Frequency multiplier at <paramref name="t"/> seconds.</summary>
    public float At(float t)
    {
        if (Semis == 0f || t < Delay) return 1f;
        float u = t - Delay;
        float depth = Fade > 0f ? Semis * MathF.Exp(-u / Fade) : Semis;
        return 1f + 0.0577623f * depth * MathF.Sin(Dsp.Tau * Hz * u);
    }
}

/// <summary>One pole: 6 dB an octave, the cheapest way to darken or thin a sound.</summary>
public struct OnePole
{
    private float _z;

    public static float Coef(float hz, int sr) => 1f - MathF.Exp(-Dsp.Tau * MathF.Min(hz, 0.49f * sr) / sr);
    public float Low(float x, float coef) => _z += coef * (x - _z);
    public float High(float x, float coef) => x - (_z += coef * (x - _z));
}

/// <summary>A state-variable filter in the trapezoidal form: 12 dB an octave, stable while
/// its cutoff moves, with low, band and high outputs from one state.</summary>
public struct Svf
{
    private float _ic1, _ic2, _a1, _a2, _a3, _k;
    public float LowOut, BandOut, HighOut;

    public void Set(float hz, float q, int sr)
    {
        float g = MathF.Tan(Dsp.Pi * Math.Clamp(hz, 10f, 0.46f * sr) / sr);
        _k = 1f / MathF.Max(0.3f, q);
        _a1 = 1f / (1f + g * (g + _k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    public void Tick(float x)
    {
        float v3 = x - _ic2;
        float v1 = _a1 * _ic1 + _a2 * v3;
        float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2f * v1 - _ic1;
        _ic2 = 2f * v2 - _ic2;
        LowOut = v2;
        BandOut = v1;
        HighOut = x - _k * v1 - v2;
    }
}

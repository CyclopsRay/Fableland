using System;

namespace Fableland.Audio.Synth;

public enum Wave : byte { Sine, Triangle, Saw, Pulse }
public enum Tint : byte { White, Pink, Brown }
public enum Band : byte { Off, Low, Mid, High }

/// <summary>One resonance of a struck object: a partial at <c>Ratio</c> × the base pitch,
/// ringing for <c>T60</c> seconds at relative level <c>Gain</c>.</summary>
public readonly struct Mode
{
    public readonly float Ratio, T60, Gain;
    public Mode(float ratio, float t60, float gain) { Ratio = ratio; T60 = t60; Gain = gain; }
}

/// <summary>
/// The sound generators (<c>Docs/Audio.gdd</c> §4.1). Each ADDS into <paramref name="dst"/>
/// starting at sample <c>at</c> for <c>n</c> samples, clipped to the buffer, so a recipe is a
/// list of calls that layer onto one another. All are deterministic: randomness comes only
/// from the <see cref="SynthRng"/> handed in.
/// </summary>
public static class Gen
{
    private static float PolyBlep(float t, float dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1f; }
        if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
        return 0f;
    }

    /// <summary>An oscillator gliding from <paramref name="f0"/> to <paramref name="f1"/> Hz
    /// (exponentially). Saw and pulse are band-limited (PolyBLEP): their harmonics stop at
    /// Nyquist instead of folding back as a buzz.</summary>
    public static void Osc(float[] dst, int sr, int at, int n, Wave wave, float f0, float f1, in Env env, float gain,
                           float duty = 0.5f, in Vib vib = default, float phase = 0f)
    {
        if (n <= 0 || f0 <= 0f) return;
        int end = Math.Min(dst.Length, at + n);
        double ph = phase - Math.Floor(phase);
        double f = f0;
        bool glide = f1 > 0f && f1 != f0;
        double ratio = glide ? Math.Pow(f1 / (double)f0, 1.0 / n) : 1.0;
        float invSr = 1f / sr;
        float top = 0.45f * sr;
        for (int i = Math.Max(0, at), k = i - at; i < end; i++, k++)
        {
            float t = k * invSr;
            float fr = (float)f * vib.At(t);
            if (fr > top) fr = top;
            float dt = fr * invSr;
            float p = (float)ph;
            float y;
            switch (wave)
            {
                case Wave.Sine: y = MathF.Sin(Dsp.Tau * p); break;
                case Wave.Triangle: y = 4f * MathF.Abs(p - 0.5f) - 1f; break;
                case Wave.Saw: y = 2f * p - 1f - PolyBlep(p, dt); break;
                default:
                    float q = p + 1f - duty;
                    if (q >= 1f) q -= 1f;
                    y = (p < duty ? 1f : -1f) + PolyBlep(p, dt) - PolyBlep(q, dt);
                    break;
            }
            dst[i] += y * env.At(t) * gain;
            ph += dt;
            if (ph >= 1.0) ph -= 1.0;
            if (glide) f *= ratio;
        }
    }

    /// <summary>Two-operator FM: a carrier whose phase is pushed by a modulator at
    /// <paramref name="ratio"/> × its pitch. The index falls from <paramref name="index"/>
    /// over <paramref name="indexT60"/> seconds, so the sound starts bright and ends pure —
    /// bells, glass, a blade's ring.</summary>
    public static void Fm(float[] dst, int sr, int at, int n, float freq, float ratio, float index, float indexT60,
                          in Env env, float gain, float freqEnd = 0f, float indexFloor = 0f)
    {
        if (n <= 0 || freq <= 0f) return;
        int end = Math.Min(dst.Length, at + n);
        double pc = 0, pm = 0;
        double f = freq;
        bool glide = freqEnd > 0f && freqEnd != freq;
        double step = glide ? Math.Pow(freqEnd / (double)freq, 1.0 / n) : 1.0;
        float invSr = 1f / sr;
        float decay = indexT60 > 0f ? 6.9078f / indexT60 : 0f;
        // Keep the modulator's sidebands under Nyquist: the index is cut where they would fold.
        float safe = MathF.Max(0f, (0.45f * sr / MathF.Max(1f, freq * MathF.Max(1f, ratio))) - 1f);
        for (int i = Math.Max(0, at), k = i - at; i < end; i++, k++)
        {
            float t = k * invSr;
            float ix = indexFloor + (index - indexFloor) * (decay > 0f ? MathF.Exp(-decay * t) : 1f);
            if (ix > safe) ix = safe;
            float y = MathF.Sin(Dsp.Tau * (float)pc + ix * MathF.Sin(Dsp.Tau * (float)pm));
            dst[i] += y * env.At(t) * gain;
            double df = f * invSr;
            pc += df;
            pm += df * ratio;
            if (pc >= 1.0) pc -= 1.0;
            if (pm >= 1.0) pm -= 1.0;
            if (glide) f *= step;
        }
    }

    /// <summary>A struck object as a handful of decaying partials (a bar, a block, a bowl, an
    /// anvil). Each partial is a two-pole resonator, so a long ring costs two multiplies a
    /// sample. Partials above Nyquist are left out.</summary>
    public static void Modal(float[] dst, int sr, int at, int n, float freq, ReadOnlySpan<Mode> modes, float gain,
                             float attack = 0f, float ringScale = 1f)
    {
        if (n <= 0 || freq <= 0f) return;
        int start = Math.Max(0, at);
        int end = Math.Min(dst.Length, at + n);
        int attackN = (int)(attack * sr);
        for (int m = 0; m < modes.Length; m++)
        {
            float f = freq * modes[m].Ratio;
            if (f >= 0.45f * sr || f < 15f) continue;
            double w = Dsp.Tau * f / sr;
            double r = Math.Pow(10.0, -3.0 / (Math.Max(0.004, modes[m].T60 * ringScale) * sr));
            double c = 2.0 * r * Math.Cos(w), r2 = r * r;
            double y2 = 0.0;                                         // y[0]
            double y1 = gain * modes[m].Gain * r * Math.Sin(w);      // y[1]
            for (int i = at, k = 0; i < end; i++, k++)
            {
                double y;
                if (k == 0) y = 0.0;
                else if (k == 1) y = y1;
                else { y = c * y1 - r2 * y2; y2 = y1; y1 = y; }
                if (i < start) continue;
                dst[i] += (float)(k < attackN ? y * (0.5 - 0.5 * Math.Cos(Math.PI * k / attackN)) : y);
            }
        }
    }

    /// <summary>
    /// A plucked string (Karplus–Strong): a burst of noise circulating in a delay line one
    /// period long, losing its top end each time round. <paramref name="bright"/> 0…1 sets
    /// how much top survives; <paramref name="pick"/> 0…0.5 where the string is plucked;
    /// <paramref name="bendSemis"/> starts the note off pitch and lets it settle over
    /// <paramref name="bendTime"/> — the pressed string of a guzheng.
    /// </summary>
    public static void Pluck(float[] dst, int sr, int at, int n, float freq, float bright, float t60, float pick,
                             float gain, ref SynthRng rng, float bendSemis = 0f, float bendTime = 0f,
                             in Vib vib = default, float attack = 0f)
    {
        if (n <= 0 || freq <= 20f) return;
        float a = 0.35f + 0.65f * Dsp.Clamp01(bright);            // loop low-pass coefficient
        float filterDelay = (1f - a) / a;                         // its phase delay, in samples
        float lowest = freq * Dsp.Ratio(-MathF.Abs(bendSemis) - MathF.Abs(vib.Semis) - 0.5f);
        int size = (int)(sr / lowest) + 8;
        Span<float> line = size <= 4096 ? stackalloc float[size] : new float[size];
        float period0 = sr / freq;
        int exc = Math.Min(size - 2, Math.Max(2, (int)period0));

        // The pluck itself: one period of noise, softened, with the notch a pick position leaves.
        float lp = 0f, mean = 0f;
        float soft = 0.25f + 0.75f * Dsp.Clamp01(bright);
        for (int i = 0; i < exc; i++)
        {
            lp += soft * (rng.Signed() - lp);
            line[i] = lp;
            mean += lp;
        }
        mean /= exc;
        int comb = (int)(Math.Clamp(pick, 0f, 0.5f) * exc);
        float peak = 1e-6f;
        Span<float> shaped = exc <= 4096 ? stackalloc float[exc] : new float[exc];
        for (int i = 0; i < exc; i++)
        {
            float x = line[i] - mean;
            if (comb > 0) x -= 0.9f * (line[(i - comb + exc) % exc] - mean);
            shaped[i] = x;
            peak = MathF.Max(peak, MathF.Abs(x));
        }
        for (int i = 0; i < exc; i++) line[i] = shaped[i] / peak;
        for (int i = exc; i < size; i++) line[i] = 0f;

        float loss = MathF.Pow(10f, -3f / (MathF.Max(0.02f, t60) * freq));   // per trip round the loop
        int write = exc;
        float z = 0f;
        int start = Math.Max(0, at);
        int end = Math.Min(dst.Length, at + n);
        float invSr = 1f / sr;
        int attackN = (int)(attack * sr);
        for (int i = at, k = 0; i < end; i++, k++)
        {
            float t = k * invSr;
            float bend = bendSemis != 0f && t < bendTime ? bendSemis * (1f - t / bendTime) * (1f - t / bendTime) : 0f;
            float period = sr / (freq * Dsp.Ratio(bend) * vib.At(t)) - filterDelay;
            if (period < 2f) period = 2f;
            if (period > size - 2) period = size - 2;
            float read = write - period;
            if (read < 0f) read += size;
            int i0 = (int)read;
            float frac = read - i0;
            float s0 = line[i0 % size], s1 = line[(i0 + 1) % size];
            float y = s0 + (s1 - s0) * frac;
            z += a * (y - z);
            line[write] = z * loss;
            write++;
            if (write >= size) write = 0;
            if (i < start) continue;
            dst[i] += (k < attackN ? y * (0.5f - 0.5f * MathF.Cos(Dsp.Pi * k / attackN)) : y) * gain;
        }
    }

    /// <summary>Tinted noise through a filter whose cutoff sweeps from <paramref name="f0"/>
    /// to <paramref name="f1"/>: wind, breath, surf, a blade through air. A tremolo
    /// (<paramref name="tremHz"/>, depth 0…1) hatches it.</summary>
    public static void Noise(float[] dst, int sr, int at, int n, Tint tint, Band band, float f0, float f1, float q,
                             in Env env, float gain, ref SynthRng rng, float tremHz = 0f, float tremDepth = 0f)
    {
        if (n <= 0) return;
        int end = Math.Min(dst.Length, at + n);
        var filter = new Svf();
        float b0 = 0f, b1 = 0f, b2 = 0f, brown = 0f;
        float invSr = 1f / sr;
        bool sweep = f1 > 0f && f1 != f0;
        double ratio = sweep ? Math.Pow(f1 / (double)MathF.Max(1f, f0), 1.0 / n) : 1.0;
        double f = MathF.Max(1f, f0);
        for (int i = Math.Max(0, at), k = i - at; i < end; i++, k++)
        {
            float white = rng.Signed();
            float x;
            switch (tint)
            {
                case Tint.Pink:
                    b0 = 0.99765f * b0 + white * 0.0990460f;
                    b1 = 0.96300f * b1 + white * 0.2965164f;
                    b2 = 0.57000f * b2 + white * 1.0526913f;
                    x = (b0 + b1 + b2 + white * 0.1848f) * 0.22f;
                    break;
                case Tint.Brown:
                    brown = (brown + 0.02f * white) / 1.02f;
                    x = brown * 3.5f;
                    break;
                default: x = white; break;
            }
            if (band != Band.Off)
            {
                if ((k & 15) == 0) filter.Set((float)f, q, sr);
                filter.Tick(x);
                x = band == Band.Low ? filter.LowOut : band == Band.Mid ? filter.BandOut : filter.HighOut;
            }
            float t = k * invSr;
            float level = env.At(t) * gain;
            if (tremDepth > 0f) level *= 1f - tremDepth * (0.5f + 0.5f * MathF.Sin(Dsp.Tau * tremHz * t));
            dst[i] += x * level;
            if (sweep) f *= ratio;
        }
    }

    /// <summary>
    /// Chip noise: a 15-bit shift register clocked at <paramref name="clockHz"/> — the noise
    /// channel of an 8-bit console. <paramref name="shortMode"/> gives the metallic 93-step
    /// loop. The clock changes only in steps (<paramref name="stepSec"/>), never as a glide.
    /// </summary>
    public static void Lfsr(float[] dst, int sr, int at, int n, float clockHz, float clockEndHz, in Env env, float gain,
                            bool shortMode = false, float stepSec = 1f / 60f, int seed = 1)
    {
        if (n <= 0) return;
        int end = Math.Min(dst.Length, at + n);
        int reg = (seed & 0x7FFF) | 1;
        float acc = 0f;
        float outV = 1f;
        float invSr = 1f / sr;
        float total = n * invSr;
        for (int i = Math.Max(0, at), k = i - at; i < end; i++, k++)
        {
            float t = k * invSr;
            float tq = stepSec > 0f ? MathF.Floor(t / stepSec) * stepSec : t;
            float clock = clockEndHz > 0f ? clockHz * MathF.Pow(clockEndHz / clockHz, tq / total) : clockHz;
            acc += clock * invSr;
            float sum = 0f;
            int steps = 0;
            while (acc >= 1f)
            {
                acc -= 1f;
                int bit = (reg ^ (reg >> (shortMode ? 6 : 1))) & 1;
                reg = (reg >> 1) | (bit << 14);
                outV = (reg & 1) != 0 ? 1f : -1f;
                sum += outV;
                steps++;
            }
            // A clock above the sample rate is averaged, which is what an analogue output does.
            float y = steps > 1 ? sum / steps : outV;
            dst[i] += y * env.At(t) * gain;
        }
    }

    /// <summary>A stack of harmonics at fixed levels — organ pipes, a sheng, a pad, a held
    /// voice. Each partial drifts a little off pitch (<paramref name="detuneCents"/>) so the
    /// stack breathes instead of fusing into one buzz.</summary>
    public static void Additive(float[] dst, int sr, int at, int n, float freq, ReadOnlySpan<float> partials, in Env env,
                                float gain, ref SynthRng rng, float detuneCents = 0f)
    {
        if (n <= 0 || freq <= 0f || partials.Length == 0) return;
        int count = Math.Min(partials.Length, 24);
        Span<double> y1 = stackalloc double[count];
        Span<double> y2 = stackalloc double[count];
        Span<double> coef = stackalloc double[count];
        for (int p = 0; p < count; p++)
        {
            float f = freq * (p + 1) * Dsp.Ratio(detuneCents * 0.01f * rng.Signed());
            if (f >= 0.45f * sr || partials[p] == 0f) { coef[p] = double.NaN; continue; }
            double w = Dsp.Tau * f / sr;
            double phase = Dsp.Tau * rng.Next();
            coef[p] = 2.0 * Math.Cos(w);
            y2[p] = partials[p] * Math.Sin(phase - w);
            y1[p] = partials[p] * Math.Sin(phase);
        }
        int start = Math.Max(0, at);
        int end = Math.Min(dst.Length, at + n);
        float invSr = 1f / sr;
        for (int i = at, k = 0; i < end; i++, k++)
        {
            double sum = 0.0;
            for (int p = 0; p < count; p++)
            {
                if (double.IsNaN(coef[p])) continue;
                sum += y1[p];
                double y = coef[p] * y1[p] - y2[p];
                y2[p] = y1[p];
                y1[p] = y;
            }
            if (i >= start) dst[i] += (float)sum * env.At(k * invSr) * gain;
        }
    }

    /// <summary>Sparse clicks at random moments and levels: shellac crackle, an ember, a
    /// gouge mark. <paramref name="perSecond"/> is the average rate.</summary>
    public static void Crackle(float[] dst, int sr, int at, int n, float perSecond, float gain, ref SynthRng rng,
                               float clickHz = 3000f)
    {
        if (n <= 0) return;
        int end = Math.Min(dst.Length, at + n);
        float chance = perSecond / sr;
        float coef = OnePole.Coef(clickHz, sr);
        var pole = new OnePole();
        for (int i = Math.Max(0, at); i < end; i++)
        {
            float x = rng.Next() < chance ? rng.Signed() * (0.4f + 0.6f * rng.Next()) : 0f;
            dst[i] += pole.Low(x * 4f, coef) * gain;
        }
    }
}

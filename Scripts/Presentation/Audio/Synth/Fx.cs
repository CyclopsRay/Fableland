using System;

namespace Fableland.Audio.Synth;

/// <summary>
/// Offline effects (<c>Docs/Audio.gdd</c> §4.1) — everything a medium does to a sound after
/// its generators have run. All work in place on a whole buffer, at load, never while the
/// game plays. Two families, and the order matters for loops:
///   • LINEAR (filters, echo, reverb, chorus): these may ring past the end of what they are
///     given, so a loop is processed with its tail and the tail folded back afterwards.
///   • MEMORYLESS (soft clip, bit crush, rate reduce): applied after folding.
/// </summary>
public static class Fx
{
    public static void Gain(float[] buf, float gain)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] *= gain;
    }

    public static void LowPass(float[] buf, int sr, float hz, int passes = 1, float q = 0.707f)
    {
        for (int p = 0; p < passes; p++)
        {
            var f = new Svf();
            f.Set(hz, q, sr);
            for (int i = 0; i < buf.Length; i++) { f.Tick(buf[i]); buf[i] = f.LowOut; }
        }
    }

    public static void HighPass(float[] buf, int sr, float hz, int passes = 1, float q = 0.707f)
    {
        for (int p = 0; p < passes; p++)
        {
            var f = new Svf();
            f.Set(hz, q, sr);
            for (int i = 0; i < buf.Length; i++) { f.Tick(buf[i]); buf[i] = f.HighOut; }
        }
    }

    /// <summary>A resonant peak laid over the sound (a formant, a hollow body).</summary>
    public static void Resonate(float[] buf, int sr, float hz, float q, float amount)
    {
        var f = new Svf();
        f.Set(hz, q, sr);
        for (int i = 0; i < buf.Length; i++) { f.Tick(buf[i]); buf[i] += f.BandOut * amount; }
    }

    /// <summary>Echoes: a delay with feedback, its repeats darkened by <paramref name="dampHz"/>.</summary>
    public static void Echo(float[] buf, int sr, float delaySec, float feedback, float wet, float dampHz = 0f)
    {
        int d = Math.Max(1, (int)(delaySec * sr));
        float[] line = new float[d];
        float coef = dampHz > 0f ? OnePole.Coef(dampHz, sr) : 1f;
        var pole = new OnePole();
        int w = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            float delayed = line[w];
            float fed = dampHz > 0f ? pole.Low(delayed, coef) : delayed;
            line[w] = buf[i] + fed * feedback;
            buf[i] += delayed * wet;
            w++;
            if (w >= d) w = 0;
        }
    }

    private static readonly int[] CombLengths = { 1116, 1188, 1277, 1356, 1422, 1491 };
    private static readonly int[] AllpassLengths = { 556, 441, 341 };

    /// <summary>A small Schroeder reverb (six damped combs into three all-passes):
    /// <paramref name="t60"/> is how long the room rings, <paramref name="damping"/> 0…1 how
    /// fast its top end dies, <paramref name="wet"/> how much of it is heard.</summary>
    public static void Reverb(float[] buf, int sr, float t60, float damping, float wet, float preDelaySec = 0f)
    {
        if (wet <= 0f || buf.Length == 0) return;
        float scale = sr / 44100f;
        float[] room = new float[buf.Length];
        int pre = (int)(preDelaySec * sr);
        for (int c = 0; c < CombLengths.Length; c++)
        {
            int len = Math.Max(8, (int)(CombLengths[c] * scale));
            float[] line = new float[len];
            float fb = MathF.Pow(10f, -3f * len / (MathF.Max(0.05f, t60) * sr));
            float filt = 0f;
            int w = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float x = i >= pre ? buf[i - pre] : 0f;
                float y = line[w];
                filt = y * (1f - damping) + filt * damping;
                line[w] = x + filt * fb;
                room[i] += y;
                w++;
                if (w >= len) w = 0;
            }
        }
        for (int a = 0; a < AllpassLengths.Length; a++)
        {
            int len = Math.Max(4, (int)(AllpassLengths[a] * scale));
            float[] line = new float[len];
            int w = 0;
            for (int i = 0; i < room.Length; i++)
            {
                float delayed = line[w];
                float x = room[i];
                line[w] = x + delayed * 0.5f;
                room[i] = delayed - x * 0.5f;
                w++;
                if (w >= len) w = 0;
            }
        }
        float mix = wet / CombLengths.Length;
        for (int i = 0; i < buf.Length; i++) buf[i] += room[i] * mix;
    }

    /// <summary>A second, slowly wandering copy: the shimmer of a thing seen through water.</summary>
    public static void Chorus(float[] buf, int sr, float delayMs, float depthMs, float rateHz, float wet)
    {
        int size = (int)((delayMs + depthMs + 2f) * 0.001f * sr) + 4;
        float[] line = new float[size];
        int w = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            line[w] = buf[i];
            float d = (delayMs + depthMs * MathF.Sin(Dsp.Tau * rateHz * i / sr)) * 0.001f * sr;
            float read = w - d;
            if (read < 0f) read += size;
            int i0 = (int)read;
            float frac = read - i0;
            float y = line[i0 % size] * (1f - frac) + line[(i0 + 1) % size] * frac;
            buf[i] += y * wet;
            w++;
            if (w >= size) w = 0;
        }
    }

    /// <summary>Amplitude wobble; <paramref name="square"/> makes it a chopper.</summary>
    public static void Tremolo(float[] buf, int sr, float hz, float depth, bool square = false)
    {
        for (int i = 0; i < buf.Length; i++)
        {
            float s = MathF.Sin(Dsp.Tau * hz * i / sr);
            float m = square ? (s >= 0f ? 1f : 0f) : 0.5f + 0.5f * s;
            buf[i] *= 1f - depth * (1f - m);
        }
    }

    public static void SoftClip(float[] buf, float drive)
    {
        float norm = 1f / MathF.Tanh(drive);
        for (int i = 0; i < buf.Length; i++) buf[i] = MathF.Tanh(buf[i] * drive) * norm;
    }

    /// <summary>Fewer bits: the sound of bit depth being taken away.</summary>
    public static void BitCrush(float[] buf, int bits)
    {
        float steps = (1 << Math.Clamp(bits, 2, 15)) * 0.5f;
        for (int i = 0; i < buf.Length; i++) buf[i] = MathF.Round(buf[i] * steps) / steps;
    }

    /// <summary>Hold every sample for <paramref name="hold"/> samples: bandwidth taken away.</summary>
    public static void RateReduce(float[] buf, int hold)
    {
        if (hold <= 1) return;
        float held = 0f;
        for (int i = 0; i < buf.Length; i++)
        {
            if (i % hold == 0) held = buf[i];
            buf[i] = held;
        }
    }
}

/// <summary>The last steps every sound goes through before it becomes PCM
/// (<c>Docs/Audio.gdd</c> §4.3): no DC, no click at either end, a continuous seam for a loop,
/// a known peak, 16 bits.</summary>
public static class Finish
{
    /// <summary>Replace anything that is not a number. Returns how many samples it had to.</summary>
    public static int Scrub(float[] buf)
    {
        int bad = 0;
        for (int i = 0; i < buf.Length; i++)
            if (float.IsNaN(buf[i]) || float.IsInfinity(buf[i])) { buf[i] = 0f; bad++; }
        return bad;
    }

    public static void RemoveDc(float[] buf)
    {
        if (buf.Length == 0) return;
        double sum = 0;
        for (int i = 0; i < buf.Length; i++) sum += buf[i];
        float mean = (float)(sum / buf.Length);
        for (int i = 0; i < buf.Length; i++) buf[i] -= mean;
    }

    public static float Peak(float[] buf)
    {
        float peak = 0f;
        for (int i = 0; i < buf.Length; i++) peak = MathF.Max(peak, MathF.Abs(buf[i]));
        return peak;
    }

    /// <summary>Raised-cosine fades at both ends.</summary>
    public static void Fade(float[] buf, int sr, float inSec, float outSec)
    {
        int a = Math.Min(buf.Length / 2, (int)(inSec * sr));
        int b = Math.Min(buf.Length / 2, (int)(outSec * sr));
        for (int i = 0; i < a; i++) buf[i] *= 0.5f - 0.5f * MathF.Cos(Dsp.Pi * i / a);
        for (int i = 0; i < b; i++) buf[buf.Length - 1 - i] *= 0.5f - 0.5f * MathF.Cos(Dsp.Pi * i / b);
    }

    /// <summary>Cut the silence off the end (anything <paramref name="belowDb"/> under the
    /// peak), keeping <paramref name="keepSec"/> for the fade. Memory is the point.</summary>
    public static float[] TrimTail(float[] buf, int sr, float belowDb = -60f, float keepSec = 0.012f)
    {
        float floor = Peak(buf) * Dsp.DbToLin(belowDb);
        int last = buf.Length - 1;
        while (last > 0 && MathF.Abs(buf[last]) <= floor) last--;
        int length = Math.Min(buf.Length, last + 1 + (int)(keepSec * sr));
        if (length >= buf.Length) return buf;
        float[] trimmed = new float[Math.Max(length, 16)];
        Array.Copy(buf, trimmed, Math.Min(buf.Length, trimmed.Length));
        return trimmed;
    }

    /// <summary>A loop out of something PERIODIC (notes on a bar grid): everything past the
    /// loop's end — ringing notes, echoes, reverb — is added back onto the start, where it
    /// would have been if the loop had already gone round once. The seam is exact.</summary>
    public static float[] Fold(float[] buf, int loopLen)
    {
        float[] loop = new float[loopLen];
        for (int i = 0; i < buf.Length; i++) loop[i % loopLen] += buf[i];
        return loop;
    }

    /// <summary>A loop out of something STATIONARY (a texture): the stretch after the loop's
    /// end is cross-faded, at equal power, over the start.</summary>
    public static float[] CrossfadeLoop(float[] buf, int loopLen, int fadeLen)
    {
        fadeLen = Math.Min(fadeLen, Math.Min(loopLen, buf.Length - loopLen));
        float[] loop = new float[loopLen];
        Array.Copy(buf, loop, loopLen);
        for (int i = 0; i < fadeLen; i++)
        {
            float x = (i + 0.5f) / fadeLen;
            float fadeIn = MathF.Sin(0.5f * Dsp.Pi * x), fadeOut = MathF.Cos(0.5f * Dsp.Pi * x);
            loop[i] = buf[i] * fadeIn + buf[loopLen + i] * fadeOut;
        }
        return loop;
    }

    public static void Normalize(float[] buf, float peakDb)
    {
        float peak = Peak(buf);
        if (peak < 1e-7f) return;
        Fx.Gain(buf, Dsp.DbToLin(peakDb) / peak);
    }

    /// <summary>16-bit little-endian PCM, with triangular dither so a quiet tail fades into
    /// noise instead of into steps.</summary>
    public static byte[] Pack16(float[] buf, ref SynthRng rng, bool dither = true)
    {
        byte[] pcm = new byte[buf.Length * 2];
        for (int i = 0; i < buf.Length; i++)
        {
            float x = buf[i] * 32767f;
            if (dither) x += rng.Next() - rng.Next();
            int v = (int)MathF.Round(x);
            if (v > 32767) v = 32767;
            if (v < -32768) v = -32768;
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return pcm;
    }

    public static float[] Unpack16(byte[] pcm)
    {
        float[] buf = new float[pcm.Length / 2];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
        return buf;
    }
}

using System;
using System.IO;

namespace Fableland.Audio.Synth;

/// <summary>
/// Measuring a sound without hearing it (<c>Docs/Audio.gdd</c> §12). Nobody could listen
/// while the audio layer was built, so every claim about a rendered sound — it is not silent,
/// it does not clip, it has no DC, it does not click, its loop is continuous, it is not harsh
/// or boomy — is a number from here, asserted by <c>AudioCheck</c>.
/// </summary>
public struct SoundStats
{
    public int Samples;
    public float Seconds;
    /// <summary>Largest absolute sample, 0…1.</summary>
    public float Peak;
    public float Rms;
    /// <summary>Mean of the signal — an offset that eats headroom and thumps when a voice stops.</summary>
    public float Dc;
    /// <summary>The first and the last sample: the step a voice makes out of and into silence.</summary>
    public float FirstSample, LastSample;
    /// <summary>Peak of the last 2 ms: a one-shot must have died away by its end.</summary>
    public float TailPeak;
    /// <summary>The largest sample-to-sample step anywhere, and the step across the loop's seam.</summary>
    public float MaxStep, SeamStep;
    /// <summary>Samples at or beyond full scale.</summary>
    public int Clipped;
    /// <summary>Where the spectrum's weight sits, Hz.</summary>
    public float Centroid;
    /// <summary>Share of the energy under 120 Hz (boom), above 5 kHz and above 8 kHz (harshness).</summary>
    public float Below120, Above5k, Above8k;
    /// <summary>Seconds until the envelope first reaches half its peak.</summary>
    public float AttackSeconds;
}

public static class Analysis
{
    public static SoundStats Measure(float[] x, int sr)
    {
        var s = new SoundStats { Samples = x.Length, Seconds = x.Length / (float)sr };
        if (x.Length == 0) return s;
        double sum = 0, sq = 0;
        for (int i = 0; i < x.Length; i++)
        {
            float v = x[i];
            float a = MathF.Abs(v);
            sum += v;
            sq += v * v;
            if (a > s.Peak) s.Peak = a;
            if (a >= 0.9999f) s.Clipped++;
            if (i > 0)
            {
                float step = MathF.Abs(v - x[i - 1]);
                if (step > s.MaxStep) s.MaxStep = step;
            }
        }
        s.Dc = (float)(sum / x.Length);
        s.Rms = (float)Math.Sqrt(sq / x.Length);
        s.FirstSample = x[0];
        s.LastSample = x[^1];
        s.SeamStep = MathF.Abs(x[0] - x[^1]);
        int tail = Math.Max(1, (int)(0.002f * sr));
        for (int i = Math.Max(0, x.Length - tail); i < x.Length; i++) s.TailPeak = MathF.Max(s.TailPeak, MathF.Abs(x[i]));
        for (int i = 0; i < x.Length; i++)
            if (MathF.Abs(x[i]) >= 0.5f * s.Peak) { s.AttackSeconds = i / (float)sr; break; }
        Spectrum(x, sr, ref s);
        return s;
    }

    public static SoundStats Measure(byte[] pcm16, int sr) => Measure(Finish.Unpack16(pcm16), sr);

    /// <summary>The 99.9th-percentile sample-to-sample step: what "an ordinary step" is for
    /// this sound, which a loop's seam must not exceed.</summary>
    public static float TypicalStep(float[] x)
    {
        if (x.Length < 3) return 0f;
        float[] steps = new float[x.Length - 1];
        for (int i = 1; i < x.Length; i++) steps[i - 1] = MathF.Abs(x[i] - x[i - 1]);
        Array.Sort(steps);
        return steps[Math.Min(steps.Length - 1, (int)(steps.Length * 0.999f))];
    }

    /// <summary>Average power spectrum (Hann, 2048 points, half-overlapped), reduced to a
    /// centroid and three band shares.</summary>
    private static void Spectrum(float[] x, int sr, ref SoundStats s)
    {
        const int n = 2048;
        double[] power = new double[n / 2];
        float[] re = new float[n], im = new float[n];
        int frames = 0;
        for (int start = 0; start < Math.Max(1, x.Length - n / 2); start += n / 2)
        {
            for (int i = 0; i < n; i++)
            {
                int k = start + i;
                float w = 0.5f - 0.5f * MathF.Cos(Dsp.Tau * i / n);
                re[i] = k < x.Length ? x[k] * w : 0f;
                im[i] = 0f;
            }
            Fft(re, im);
            for (int b = 1; b < n / 2; b++) power[b] += re[b] * re[b] + im[b] * im[b];
            frames++;
            if (frames >= 64) break;
        }
        double total = 0, weighted = 0, low = 0, high5 = 0, high8 = 0;
        for (int b = 1; b < n / 2; b++)
        {
            double hz = b * (double)sr / n;
            total += power[b];
            weighted += power[b] * hz;
            if (hz < 120) low += power[b];
            if (hz >= 5000) high5 += power[b];
            if (hz >= 8000) high8 += power[b];
        }
        if (total <= 0) return;
        s.Centroid = (float)(weighted / total);
        s.Below120 = (float)(low / total);
        s.Above5k = (float)(high5 / total);
        s.Above8k = (float)(high8 / total);
    }

    /// <summary>In-place radix-2 FFT; the length must be a power of two.</summary>
    public static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1f, ci = 0f;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    float next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }
}

/// <summary>Plain 16-bit mono WAV files, written and read with <c>System.IO</c> — the dump a
/// person plays and the inspection tool draws (<c>Tools/audio/inspect.py</c>).</summary>
public static class WavFile
{
    public static void Write(string path, byte[] pcm16, int sampleRate)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);
        w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        w.Write(36 + pcm16.Length);
        w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        w.Write(16);
        w.Write((short)1);               // PCM
        w.Write((short)1);               // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        w.Write(pcm16.Length);
        w.Write(pcm16);
    }
}

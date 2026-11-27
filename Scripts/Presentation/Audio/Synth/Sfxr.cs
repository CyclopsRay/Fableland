using System;

namespace Fableland.Audio.Synth;

/// <summary>
/// An sfxr-style sound: one oscillator described by two dozen numbers, most of them 0…1 —
/// the model behind a generation of retro game effects (parameter names follow jsfxr, which
/// the research digest read: R1 §2.4, source X1). It is the pixel-art medium's voice.
///
/// Two additions make it obey that medium's rules (<c>Docs/Audio.gdd</c> §5, TF):
/// <see cref="VolumeSteps"/> quantises the envelope to a chip's volume register, and
/// <see cref="TickHz"/> lets pitch and duty change only on a tick grid — so a "slide" becomes
/// a staircase and never a glide.
/// </summary>
public struct SfxrParams
{
    public enum Shape : byte { Square, Saw, Sine, Noise }

    public Shape Wave;
    public float BaseFreq, FreqLimit, FreqRamp, FreqDeltaRamp;
    public float Duty, DutyRamp;
    public float VibStrength, VibSpeed;
    public float Attack, Sustain, Decay, Punch;
    public float LpfFreq, LpfRamp, LpfResonance;
    public float HpfFreq, HpfRamp;
    public float PhaserOffset, PhaserRamp;
    public float RepeatSpeed;
    public float ArpSpeed, ArpMod;
    /// <summary>0 = a smooth envelope; 16 = a four-bit volume register.</summary>
    public int VolumeSteps;
    /// <summary>0 = continuous; 60 = pitch and duty move once a video frame.</summary>
    public float TickHz;

    public static SfxrParams Default => new()
    {
        Wave = Shape.Square, BaseFreq = 0.3f, Sustain = 0.3f, Decay = 0.4f, LpfFreq = 1f,
    };

    /// <summary>The sfxr frequency number for a pitch in Hz (the model's own unit is a
    /// period in 1/352800 s).</summary>
    public static float FreqFor(float hz) => MathF.Sqrt(MathF.Max(0f, hz / 3528f - 0.001f));

    /// <summary>The semitone jump an arpeggio makes, as the model's <c>ArpMod</c>.</summary>
    public static float ArpFor(float semitones)
    {
        float mult = Dsp.Ratio(-semitones);                 // the period is multiplied
        return mult <= 1f ? MathF.Sqrt((1f - mult) / 0.9f) : -MathF.Sqrt((mult - 1f) / 10f);
    }

    /// <summary>A sibling: every continuous parameter nudged by up to ±<paramref name="amount"/>.
    /// This is how one recipe becomes several variants that are the same sound and not the
    /// same waveform.</summary>
    public SfxrParams Mutate(ref SynthRng rng, float amount = 0.05f)
    {
        SfxrParams p = this;
        p.BaseFreq = Nudge(p.BaseFreq, ref rng, amount * 0.4f);
        p.FreqRamp = Nudge(p.FreqRamp, ref rng, amount);
        p.Duty = Nudge(p.Duty, ref rng, amount);
        p.DutyRamp = Nudge(p.DutyRamp, ref rng, amount);
        p.Sustain = Nudge(p.Sustain, ref rng, amount);
        p.Decay = Nudge(p.Decay, ref rng, amount);
        p.Punch = Nudge(p.Punch, ref rng, amount);
        p.LpfFreq = Dsp.Clamp01(Nudge(p.LpfFreq, ref rng, amount));
        p.HpfFreq = Dsp.Clamp01(Nudge(p.HpfFreq, ref rng, amount));
        p.PhaserOffset = Nudge(p.PhaserOffset, ref rng, amount);
        return p;
    }

    private static float Nudge(float v, ref SynthRng rng, float amount) =>
        v == 0f ? 0f : Math.Clamp(v + rng.Signed() * amount, -1f, 1f);
}

public static class Sfxr
{
    private const int Internal = 352800;     // 44.1 kHz × 8: the model's own clock

    /// <summary>Seconds the sound lasts (its three envelope stages).</summary>
    public static float Length(in SfxrParams p) =>
        ((int)(p.Attack * p.Attack * 100000f) + (int)(p.Sustain * p.Sustain * 100000f) + (int)(p.Decay * p.Decay * 100000f)) / 44100f;

    /// <summary>Render the sound, added into <paramref name="dst"/> from sample
    /// <paramref name="at"/>. Returns the samples written.</summary>
    public static int Render(float[] dst, int sr, int at, in SfxrParams p, float gain, ref SynthRng rng)
    {
        double period = 100.0 / (p.BaseFreq * p.BaseFreq + 0.001);
        double maxPeriod = 100.0 / (p.FreqLimit * p.FreqLimit + 0.001);
        double slide = 1.0 - Math.Pow(p.FreqRamp, 3.0) * 0.01;
        double deltaSlide = -Math.Pow(p.FreqDeltaRamp, 3.0) * 0.000001;
        float duty = 0.5f - p.Duty * 0.5f;
        float dutySlide = -p.DutyRamp * 0.00005f;
        double arpMod = p.ArpMod >= 0f ? 1.0 - p.ArpMod * p.ArpMod * 0.9 : 1.0 + p.ArpMod * p.ArpMod * 10.0;
        int arpLimit = p.ArpSpeed >= 1f ? 0 : (int)(Math.Pow(1.0 - p.ArpSpeed, 2.0) * 20000 + 32);
        int arpTime = 0;

        float fltp = 0f, fltdp = 0f, fltphp = 0f;
        float fltw = MathF.Pow(p.LpfFreq, 3f) * 0.1f;
        float fltwD = 1f + p.LpfRamp * 0.0001f;
        float fltdmp = MathF.Min(0.8f, 5f / (1f + p.LpfResonance * p.LpfResonance * 20f) * (0.01f + fltw));
        float flthp = p.HpfFreq * p.HpfFreq * 0.1f;
        float flthpD = 1f + p.HpfRamp * 0.0003f;
        bool lowPass = p.LpfFreq < 1f;

        float vibPhase = 0f;
        float vibSpeed = p.VibSpeed * p.VibSpeed * 0.01f;
        float vibAmp = p.VibStrength * 0.5f;

        int[] envLength =
        {
            (int)(p.Attack * p.Attack * 100000f), (int)(p.Sustain * p.Sustain * 100000f), (int)(p.Decay * p.Decay * 100000f),
        };
        int envStage = 0, envTime = 0;
        float envVol = 0f;

        float fphase = p.PhaserOffset * p.PhaserOffset * 1020f * (p.PhaserOffset < 0f ? -1f : 1f);
        float fdphase = p.PhaserRamp * p.PhaserRamp * (p.PhaserRamp < 0f ? -1f : 1f);
        Span<float> phaser = stackalloc float[1024];
        phaser.Clear();
        int ipp = 0;
        Span<float> noise = stackalloc float[32];
        for (int i = 0; i < 32; i++) noise[i] = rng.Signed();

        int repLimit = p.RepeatSpeed <= 0f ? 0 : (int)(Math.Pow(1.0 - p.RepeatSpeed, 2.0) * 20000 + 32);
        int repTime = 0;
        double startPeriod = period, startSlide = slide;
        float startDuty = duty;

        int tickEvery = p.TickHz > 0f ? Math.Max(1, (int)(44100f / p.TickHz)) : 1;
        int heldPeriod = Math.Max(8, (int)period);
        float heldDuty = duty;
        float heldVol = 0f;

        int phase = 0;
        int written = 0;
        int outIndex = at;
        long acc = 0;
        float sum = 0f;
        int summed = 0;
        bool playing = true;
        for (int step = 0; playing; step++)
        {
            repTime++;
            if (repLimit != 0 && repTime >= repLimit)
            {
                repTime = 0;
                period = startPeriod;
                slide = startSlide;
                duty = startDuty;
                arpTime = 0;
                arpLimit = p.ArpSpeed >= 1f ? 0 : (int)(Math.Pow(1.0 - p.ArpSpeed, 2.0) * 20000 + 32);
            }
            arpTime++;
            if (arpLimit != 0 && arpTime >= arpLimit) { arpLimit = 0; period *= arpMod; }
            slide += deltaSlide;
            period *= slide;
            if (period > maxPeriod)
            {
                period = maxPeriod;
                if (p.FreqLimit > 0f) playing = false;
            }
            double rperiod = period;
            if (vibAmp > 0f)
            {
                vibPhase += vibSpeed;
                rperiod = period * (1.0 + Math.Sin(vibPhase) * vibAmp);
            }
            duty = Math.Clamp(duty + dutySlide, 0f, 0.5f);

            envTime++;
            if (envTime > envLength[envStage])
            {
                envTime = 0;
                do { envStage++; } while (envStage < 3 && envLength[envStage] == 0);
                if (envStage >= 3) break;
            }
            if (envStage == 0) envVol = (float)envTime / envLength[0];
            else if (envStage == 1) envVol = 1f + (1f - (float)envTime / envLength[1]) * 2f * p.Punch;
            else envVol = 1f - (float)envTime / envLength[2];

            // A chip changes its registers on a tick, not continuously.
            if (step % tickEvery == 0)
            {
                heldPeriod = Math.Max(8, (int)rperiod);
                heldDuty = duty;
                heldVol = p.VolumeSteps > 0 ? MathF.Round(MathF.Min(envVol, 1f) * p.VolumeSteps) / p.VolumeSteps : envVol;
            }
            int iperiod = p.TickHz > 0f ? heldPeriod : Math.Max(8, (int)rperiod);
            float useDuty = p.TickHz > 0f ? heldDuty : duty;
            float useVol = p.TickHz > 0f || p.VolumeSteps > 0 ? heldVol : envVol;

            fphase += fdphase;
            int iphase = Math.Min(1023, Math.Abs((int)fphase));
            if (flthpD != 0f) flthp = Math.Clamp(flthp * flthpD, 0.00001f, 0.1f);

            for (int si = 0; si < 8; si++)
            {
                phase++;
                if (phase >= iperiod)
                {
                    phase %= iperiod;
                    if (p.Wave == SfxrParams.Shape.Noise)
                        for (int i = 0; i < 32; i++) noise[i] = rng.Signed();
                }
                float fp = (float)phase / iperiod;
                float sample = p.Wave switch
                {
                    SfxrParams.Shape.Square => fp < useDuty ? 0.5f : -0.5f,
                    SfxrParams.Shape.Saw => 1f - fp * 2f,
                    SfxrParams.Shape.Sine => MathF.Sin(fp * Dsp.Tau),
                    _ => noise[phase * 32 / iperiod],
                };
                float pp = fltp;
                fltw = Math.Clamp(fltw * fltwD, 0f, 0.1f);
                if (lowPass)
                {
                    fltdp += (sample - fltp) * fltw;
                    fltdp -= fltdp * fltdmp;
                }
                else
                {
                    fltp = sample;
                    fltdp = 0f;
                }
                fltp += fltdp;
                fltphp += fltp - pp;
                fltphp -= fltphp * flthp;
                sample = fltphp;
                phaser[ipp & 1023] = sample;
                sample += phaser[(ipp - iphase + 1024) & 1023];
                ipp = (ipp + 1) & 1023;

                // Average the model's 352.8 kHz stream down to the output rate.
                sum += sample * useVol;
                summed++;
                acc += sr;
                if (acc >= Internal)
                {
                    acc -= Internal;
                    if (outIndex >= 0 && outIndex < dst.Length) dst[outIndex] += sum / summed * gain;
                    outIndex++;
                    written++;
                    sum = 0f;
                    summed = 0;
                }
            }
            if (step > 44100 * 6) break;      // six seconds: nothing in this game is longer
        }
        return written;
    }
}

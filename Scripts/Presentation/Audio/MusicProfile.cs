using System;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

public enum StemRole : byte { Bed, Pulse, Drums, Lead }

/// <summary>One looping layer of a realm's music: how many bars it is, what plays in it, and
/// between which intensities it fades in.</summary>
public sealed class StemDef
{
    public string Name;
    public StemRole Role;
    /// <summary>Loop length in bars. Stems of one profile have DIFFERENT lengths, so the
    /// whole only repeats after their least common multiple.</summary>
    public int Bars;
    public float GainDb;
    /// <summary>Silent at or below <see cref="InAt"/>, full from <see cref="FullAt"/>.</summary>
    public float InAt, FullAt;
    /// <summary>What the stem is, for the GDD.</summary>
    public string Adds;
    public Action<MusicCtx> Gen;
}

/// <summary>
/// A realm's music as data (<c>Docs/Audio.gdd</c> §7): a key, a tempo, a metre, the medium
/// whose instruments play it, a motif, and three or four stems made by short pattern
/// generators. It is bounced with a seed into looping PCM at load; nothing is stored on disk.
/// </summary>
public sealed class MusicProfile
{
    public string Id, Title, Metre;
    public Medium Medium;
    public int Tonic;
    public int[] Scale;
    public int[] Motif;
    public float Bpm;
    public int BeatsPerBar = 4;
    public int StepsPerBeat = 2;
    /// <summary>How late an off-beat step is played, as a share of a step.</summary>
    public float Swing;
    /// <summary>Seconds of human looseness added to every note (a medium that forbids
    /// machine-regular rhythm).</summary>
    public float Loose;
    /// <summary>The intensity a screen holds when no fight drives it; negative = a fight does.</summary>
    public float RestIntensity = -1f;
    public StemDef[] Stems;

    public int SampleRate => Medium.SampleRate;
    /// <summary>A beat is a whole number of samples: the clock and every stem agree exactly.</summary>
    public int SamplesPerBeat => (int)MathF.Round(SampleRate * 60f / Bpm);
    public double SecondsPerBeat => SamplesPerBeat / (double)SampleRate;
    public double SecondsPerBar => SecondsPerBeat * BeatsPerBar;

    public int TotalBars
    {
        get
        {
            int bars = 0;
            foreach (StemDef stem in Stems) bars += stem.Bars;
            return bars;
        }
    }

    /// <summary>Bars until every stem is back at its start together.</summary>
    public int RepeatBars
    {
        get
        {
            long lcm = 1;
            foreach (StemDef stem in Stems) lcm = lcm / Gcd(lcm, stem.Bars) * stem.Bars;
            return (int)Math.Min(lcm, int.MaxValue);
        }
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
}

/// <summary>What a pattern generator writes into: the stem's buffer, on the profile's grid.</summary>
public sealed class MusicCtx
{
    public const int Rest = int.MinValue;

    public RenderCtx R;
    public MusicProfile P;
    public int Bars;
    public int LoopSamples;
    public Medium M => R.Medium;
    public int StepsPerBar => P.BeatsPerBar * P.StepsPerBeat;
    public float StepSeconds => (float)(P.SecondsPerBeat / P.StepsPerBeat);

    public float Beats(float beats) => (float)(beats * P.SecondsPerBeat);

    /// <summary>Seconds from the loop's start of a step of a bar, with the profile's swing
    /// and looseness.</summary>
    public float At(int bar, int step)
    {
        float t = (bar * StepsPerBar + step) * StepSeconds;
        if (P.Swing > 0f && (step & 1) == 1) t += P.Swing * StepSeconds;
        if (P.Loose > 0f) t += R.Rng.Range(0f, P.Loose);
        return t;
    }

    public float AtBeat(int bar, float beat)
    {
        float t = (float)((bar * P.BeatsPerBar + beat) * P.SecondsPerBeat);
        if (P.Loose > 0f) t += R.Rng.Range(0f, P.Loose);
        return t;
    }

    /// <summary>MIDI note of a scale degree of the profile's key.</summary>
    public float Deg(int degree, int octave = 0) => P.Tonic + Dsp.ScaleSemis(P.Scale, degree) + 12 * octave;

    // ── The pattern generators ──────────────────────────────────────────────────────
    /// <summary>A bed of held notes: each chord (scale degrees) lasts <paramref name="barsEach"/>
    /// bars and overlaps the next, so the bed never stops.</summary>
    public void Bed(int[][] chords, int barsEach, int octave, float gain)
    {
        for (int bar = 0, k = 0; bar < Bars; bar += barsEach, k++)
        {
            float at = (float)(bar * P.SecondsPerBar);
            float dur = (float)(barsEach * P.SecondsPerBar) * 1.5f;
            foreach (int degree in chords[k % chords.Length]) M.Pad(R, at, Deg(degree, octave), dur, gain);
        }
    }

    /// <summary>One of the medium's textures, the whole length of the stem, looped by
    /// cross-fading its own overrun over its start.</summary>
    public void TextureLoop(Tex tex, float gain)
    {
        const float fade = 0.5f;
        int fadeN = R.S(fade);
        float seconds = (LoopSamples + fadeN) / (float)R.Sr;
        var sub = new RenderCtx
        {
            Buf = new float[LoopSamples + fadeN + 4], Sr = R.Sr, Rng = R.Rng.Fork(17), Medium = R.Medium,
            Tonic = R.Tonic, Scale = R.Scale, Motif = R.Motif, Body = seconds,
        };
        sub.Medium.Texture(sub, 0f, seconds, tex, 1f);
        float[] loop = Finish.CrossfadeLoop(sub.Buf, LoopSamples, fadeN);
        for (int i = 0; i < LoopSamples; i++) R.Buf[i] += loop[i] * gain;
    }

    /// <summary>An ostinato: one cell a bar, a cell being a scale degree per step
    /// (<see cref="Rest"/> = silence). <paramref name="vary"/> is the chance a bar takes a
    /// cell out of turn.</summary>
    public void Pulse(int[][] cells, int octave, ToneKind kind, float gain, float durSteps = 1f, bool bass = false, float vary = 0.2f)
    {
        for (int bar = 0; bar < Bars; bar++)
        {
            int[] cell = cells[vary > 0f && R.Rng.Chance(vary) ? R.Rng.Int(cells.Length) : bar % cells.Length];
            for (int step = 0; step < StepsPerBar; step++)
            {
                int degree = cell[step % cell.Length];
                if (degree == Rest) continue;
                float accent = step == 0 ? 1f : step % P.StepsPerBeat == 0 ? 0.85f : 0.7f;
                float at = At(bar, step), dur = durSteps * StepSeconds;
                if (bass) M.Bass(R, at, Deg(degree, octave), dur, gain * accent);
                else M.Tone(R, at, Deg(degree, octave), dur, gain * accent, kind);
            }
        }
    }

    /// <summary>Percussion from step masks — <c>x</c> a hit, <c>X</c> an accent, anything else
    /// a rest; a mask repeats across the stem. <paramref name="fill"/> is the chance the last
    /// bar ends in a run; <paramref name="drop"/> the chance any hit is left out.</summary>
    public void Drums(string low, string mid, string high, float gainLow, float gainMid, float gainHigh, float fill = 0f, float drop = 0f)
    {
        for (int bar = 0; bar < Bars; bar++)
        {
            for (int step = 0; step < StepsPerBar; step++)
            {
                int index = bar * StepsPerBar + step;
                Hit(low, index, bar, step, 0, gainLow, drop);
                Hit(mid, index, bar, step, 1, gainMid, drop);
                Hit(high, index, bar, step, 2, gainHigh, drop);
            }
        }
        if (fill > 0f && R.Rng.Chance(fill))
            for (int step = StepsPerBar - P.StepsPerBeat; step < StepsPerBar; step++)
                M.Tick(R, At(Bars - 1, step), 0.3f, gainMid > 0f ? gainMid * 0.8f : gainHigh * 0.8f);
    }

    private void Hit(string mask, int index, int bar, int step, int voice, float gain, float drop)
    {
        if (string.IsNullOrEmpty(mask) || gain <= 0f) return;
        char mark = mask[index % mask.Length];
        if (mark != 'x' && mark != 'X') return;
        if (drop > 0f && R.Rng.Chance(drop)) return;
        float at = At(bar, step);
        float level = gain * (mark == 'X' ? 1f : 0.8f);
        switch (voice)
        {
            case 0: M.Thump(R, at, mark == 'X' ? 0.9f : 0.6f, level); break;
            case 1: M.Tick(R, at, 0.3f, level); break;
            default: M.Tick(R, at, 0.9f, level); break;
        }
    }

    /// <summary>
    /// The lead: the motif in phrases — as written, moved a step, reversed, cut short, or one
    /// held note — with rests between. <paramref name="rest"/> is the chance a phrase is
    /// silence (the ink medium asks for half). <paramref name="gestures"/> fills some of the
    /// rests with a glide (or the medium's stepped run).
    /// </summary>
    public void Lead(int octave, float rest, float gain, int phraseBars = 2, float noteBeats = 1f,
                     ToneKind moving = ToneKind.Short, bool gestures = false)
    {
        int[] motif = P.Motif;
        float phraseBeats = phraseBars * P.BeatsPerBar;
        bool played = false;
        for (int bar = 0; bar < Bars; bar += phraseBars)
        {
            // A lead may rest, but a stem that is ALL rests is a silent stem: the last phrase
            // plays if none has (found by the check — the shelter's lead, seed 1).
            bool lastChance = !played && bar + phraseBars >= Bars;
            if (!lastChance && R.Rng.Chance(rest))
            {
                if (gestures && R.Rng.Chance(0.5f))
                    M.Sweep(R, AtBeat(bar, 1f), Beats(MathF.Min(1f, noteBeats)), Deg(motif[0], octave), Deg(motif[^1], octave), gain * 0.6f);
                continue;
            }
            played = true;
            int form = R.Rng.Int(5);
            int shift = form == 1 ? (R.Rng.Chance(0.5f) ? 1 : -1) : 0;
            int count = form == 3 ? Math.Max(2, motif.Length - 2) : form == 4 ? 1 : motif.Length;
            float beat = R.Rng.Int(2) * 0.5f;
            for (int i = 0; i < count && beat < phraseBeats - 0.5f; i++)
            {
                int degree = (form == 2 ? motif[motif.Length - 1 - i] : motif[i]) + shift;
                bool last = i == count - 1;
                float length = last ? MathF.Min(phraseBeats - beat, noteBeats * 3f) : noteBeats;
                M.Tone(R, AtBeat(bar, beat), Deg(degree, octave), Beats(length) * 0.95f, gain, last ? ToneKind.Long : moving);
                beat += noteBeats;
            }
        }
    }

    /// <summary>Call <paramref name="place"/> at <paramref name="count"/> irregular moments
    /// across the stem (start time in seconds).</summary>
    public void Sprinkle(int count, Action<float> place)
    {
        float loop = LoopSamples / (float)R.Sr;
        for (int i = 0; i < count; i++) place((i + R.Rng.Range(0.1f, 0.9f)) * loop / count);
    }
}

/// <summary>A profile bounced with a seed: its stems as looping PCM.</summary>
public sealed class MusicSet
{
    public MusicProfile Profile;
    public SfxClip[] Stems;
    public ulong Seed;
    public int Bytes
    {
        get
        {
            int total = 0;
            foreach (SfxClip stem in Stems) total += stem.Bytes;
            return total;
        }
    }
}

/// <summary>Turns a profile into stems (<c>Docs/Audio.gdd</c> §7.1). Pure and deterministic;
/// runs on a worker thread.</summary>
public static class MusicBounce
{
    /// <summary>Room after a loop's end for ringing notes and reverb, folded back in.</summary>
    public const float TailSeconds = 2.5f;
    public const float StemPeakDb = -9f;

    public static MusicSet Bounce(MusicProfile profile, ulong seed)
    {
        var set = new MusicSet { Profile = profile, Seed = seed, Stems = new SfxClip[profile.Stems.Length] };
        for (int i = 0; i < profile.Stems.Length; i++) set.Stems[i] = BounceStem(profile, profile.Stems[i], seed);
        return set;
    }

    public static SfxClip BounceStem(MusicProfile profile, StemDef stem, ulong seed, Medium medium = null)
    {
        medium ??= profile.Medium;
        int sr = medium.SampleRate;
        double loopSeconds = stem.Bars * profile.BeatsPerBar * profile.SecondsPerBeat;
        // In the profile's own medium a bar is a whole number of samples; a guest medium at
        // another rate rounds to the nearest one (under 50 µs a loop).
        int loop = sr == profile.SampleRate ? stem.Bars * profile.BeatsPerBar * profile.SamplesPerBeat : (int)Math.Round(loopSeconds * sr);
        var c = new RenderCtx
        {
            Buf = new float[loop + (int)(TailSeconds * sr)],
            Sr = sr,
            Rng = new SynthRng(seed ^ SynthRng.Hash(profile.Id) ^ (SynthRng.Hash(stem.Name) * 7UL)),
            Medium = medium,
            Tonic = profile.Tonic,
            Scale = profile.Scale,
            Motif = profile.Motif,
            Body = (float)loopSeconds,
        };
        stem.Gen(new MusicCtx { R = c, P = profile, Bars = stem.Bars, LoopSamples = loop });
        medium.Room(c);
        SfxRender.BlockDc(c.Buf, sr);
        c.Buf = Finish.Fold(c.Buf, loop);
        medium.Crush(c);
        Finish.Scrub(c.Buf);
        Finish.RemoveDc(c.Buf);
        Finish.Normalize(c.Buf, StemPeakDb);
        var dither = new SynthRng(c.Rng.NextU64());
        return new SfxClip { Pcm = Finish.Pack16(c.Buf, ref dither), SampleRate = sr, Seconds = loop / (float)sr, Loop = true };
    }

    /// <summary>A boss's motif as a two-bar ostinato in the boss character's HOME medium, at
    /// the realm's tonic — a boss borrowed from another world audibly invades the music.</summary>
    public static SfxClip BossLayer(MusicProfile profile, string bossOwner, ulong seed)
    {
        Medium medium = Medium.HomeFor(bossOwner) ?? profile.Medium;
        int[] motif = SfxTable.MotifOf(bossOwner);
        var stem = new StemDef
        {
            Name = "boss." + bossOwner, Role = StemRole.Lead, Bars = 2, GainDb = -4f,
            Gen = m =>
            {
                for (int bar = 0; bar < m.Bars; bar++)
                {
                    m.M.Thump(m.R, m.AtBeat(bar, 0f), 0.8f, 0.6f);
                    for (int i = 0; i < motif.Length && i < profile.BeatsPerBar * 2; i++)
                        m.M.Tone(m.R, m.AtBeat(bar, i * 0.5f), profile.Tonic - 12 + motif[i], m.Beats(0.5f), 0.8f,
                            i == motif.Length - 1 ? ToneKind.Accent : ToneKind.Short);
                }
            },
        };
        return BounceStem(profile, stem, seed, medium);
    }

    /// <summary>A stem's level at an intensity, dB (−80 = muted). Above 0.8 the mix tilts
    /// toward danger: lead and drums up, bed down.</summary>
    public static float StemDb(StemDef stem, float intensity)
    {
        float g = stem.FullAt <= stem.InAt
            ? (intensity >= stem.InAt ? 1f : 0f)
            : Dsp.Clamp01((intensity - stem.InAt) / (stem.FullAt - stem.InAt));
        if (g <= 0f) return -80f;
        float db = stem.GainDb - 24f * (1f - g) * (1f - g);
        if (intensity > 0.8f)
        {
            float danger = (intensity - 0.8f) / 0.2f;
            db += stem.Role switch { StemRole.Lead => 2f * danger, StemRole.Drums => 2f * danger, StemRole.Bed => -2f * danger, _ => 0f };
        }
        return db;
    }

    /// <summary>What the stems sound like together at an intensity, mixed offline exactly as
    /// the synchronized player mixes them — for the dump, the sheets and the check.</summary>
    public static float[] Mixdown(MusicSet set, float intensity, float seconds)
    {
        int sr = set.Profile.SampleRate;
        float[] mix = new float[(int)(seconds * sr)];
        for (int s = 0; s < set.Stems.Length; s++)
        {
            float db = StemDb(set.Profile.Stems[s], intensity);
            if (db <= -60f) continue;
            float gain = Dsp.DbToLin(db);
            float[] stem = Finish.Unpack16(set.Stems[s].Pcm);
            for (int i = 0; i < mix.Length; i++) mix[i] += stem[i % stem.Length] * gain;
        }
        return mix;
    }
}

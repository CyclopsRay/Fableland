using System;
using System.Collections.Generic;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

/// <summary>
/// Boot validation for the audio tables (T10 §5), and the pure-logic self-tests the audio
/// check absorbs. Nothing here needs the engine: a table that is wrong is reported before a
/// sound is made.
/// </summary>
public static class AudioSelfTest
{
    /// <summary>The sound table and the music profiles are well-formed.</summary>
    public static List<string> Table()
    {
        var failures = new List<string>();
        var ids = new HashSet<string>();
        foreach (SfxDef def in SfxTable.All)
        {
            if (!ids.Add(def.Id)) failures.Add($"{def.Id}: two rows share this id");
            if (def.Recipe == null) failures.Add($"{def.Id}: no recipe");
            if (def.Variants < 1 || def.Variants > 4) failures.Add($"{def.Id}: {def.Variants} variants (1–4)");
            if (def.Body <= 0f) failures.Add($"{def.Id}: no length");
            if (def.MaxVoices < 1) failures.Add($"{def.Id}: a voice cap under 1 can never play");
            if (def.Priority < 0 || def.Priority > 100) failures.Add($"{def.Id}: priority {def.Priority} (0–100)");
            if (def.PeakDb > -1f) failures.Add($"{def.Id}: normalised above −1 dBFS");
            if (def.From == MediumFrom.Fixed && Medium.Get(def.FixedMedium) == null) failures.Add($"{def.Id}: fixed to unknown medium '{def.FixedMedium}'");
            if (def.Loop && def.Variants != 1) failures.Add($"{def.Id}: a loop has one variant");
            if (def.Loop && def.Quantize != Quantize.None) failures.Add($"{def.Id}: a loop cannot be quantized");
            if ((def.BurstCount > 0) != (def.BurstDef != null)) failures.Add($"{def.Id}: a burst count and a burst row go together");
            if (def.Pitch != PitchMode.Free && def.PitchJitter > 0.2f) failures.Add($"{def.Id}: a tonal row detuned by {def.PitchJitter} st");
            for (int i = 0; i < def.Tags.Length; i++)
                for (int j = i + 1; j < def.Tags.Length; j++)
                    if (def.Tags[i] == def.Tags[j]) failures.Add($"{def.Id}: tag '{def.Tags[i]}' twice");
        }
        foreach (string action in new[] { SfxTable.HitTaken, SfxTable.HitHeavy, SfxTable.ComboNote, SfxTable.ComboResolve, SfxTable.Heartbeat })
            if (SfxTable.Cues.Resolve(CueOwners.Any, null, action) == null) failures.Add($"no any/{action} row");
        if (SfxTable.Attacks.Resolve(CueOwners.Any, null, "") == null) failures.Add("no attack row for an unnamed hit");

        foreach (MusicProfile p in MusicProfiles.All)
        {
            if (p.Stems.Length < 3 || p.Stems.Length > 4) failures.Add($"music {p.Id}: {p.Stems.Length} stems (3–4)");
            var lengths = new HashSet<int>();
            foreach (StemDef stem in p.Stems)
                if (!lengths.Add(stem.Bars)) failures.Add($"music {p.Id}: two stems of {stem.Bars} bars — unequal lengths are the point");
            double seconds = p.TotalBars * p.SecondsPerBar;
            if (seconds * p.SampleRate * 2 > 3 * 1024 * 1024) failures.Add($"music {p.Id}: {seconds:0} s of stems is over the 3 MB music budget");
            if (p.RepeatBars < 2 * p.TotalBars / p.Stems.Length) failures.Add($"music {p.Id}: repeats after only {p.RepeatBars} bars");
            foreach (int degree in p.Motif)
                if (degree < -7 || degree > 14) failures.Add($"music {p.Id}: motif degree {degree}");
        }
        foreach (Medium m in Medium.All)
            if (MusicProfiles.Get(m.Id) == null) failures.Add($"medium {m.Id} has no music profile");
        foreach (string screen in new[] { MusicProfiles.Menu, MusicProfiles.Map, MusicProfiles.Shelter, MusicProfiles.Event })
            if (MusicProfiles.Get(screen) == null) failures.Add($"no music for the {screen} screen");
        return failures;
    }

    /// <summary>A backend that only counts — the voice manager's rules, without an engine.</summary>
    public sealed class FakeVoices : IVoiceBackend
    {
        public int Plays, Stops, VolumeSets;
        public long Next = 1;
        public bool Refuse;
        public readonly List<long> Stopped = new();
        public long Play(AudioBusId bus, SfxClip clip, float volumeDb, float pitchScale) { if (Refuse) return -1; Plays++; return Next++; }
        public void Stop(AudioBusId bus, long handle) { Stops++; Stopped.Add(handle); }
        public void SetVolume(AudioBusId bus, long handle, float volumeDb) => VolumeSets++;
    }

    private static SfxDef Probe(string name, int priority, int max, float cooldown, AudioBusId bus = AudioBusId.Sfx) =>
        new("test", name) { Priority = priority, MaxVoices = max, Cooldown = cooldown, Bus = bus, Recipe = (_, _, _) => { } };

    /// <summary>Cooldown, per-row cap and stealing, the bus cap and priority, volleys, loops.</summary>
    public static List<string> VoiceRules()
    {
        var failures = new List<string>();
        var clip = new SfxClip { Seconds = 1f };
        var fake = new FakeVoices();
        var voices = new VoiceManager(fake, sfx: 4, ui: 2, ambience: 2, music: 2);

        // Cooldown.
        SfxDef a = Probe("a", 50, 8, 0.1f);
        if (voices.Post(a, clip, 0f, 1f, 0.00) != PostResult.Played) failures.Add("a first post did not play");
        if (voices.Post(a, clip, 0f, 1f, 0.05) != PostResult.Cooldown) failures.Add("a post inside the cooldown played");
        if (voices.Post(a, clip, 0f, 1f, 0.11) != PostResult.Played) failures.Add("a post after the cooldown did not play");

        // The row's own cap steals its own oldest voice.
        voices.StopAll();
        fake.Stopped.Clear();
        SfxDef b = Probe("b", 50, 2, 0f);
        voices.Post(b, clip, 0f, 1f, 1.0);
        long first = fake.Next - 1;
        voices.Post(b, clip, 0f, 1f, 1.1);
        voices.Post(b, clip, 0f, 1f, 1.2);
        if (b.LiveVoices != 2) failures.Add($"a row capped at 2 has {b.LiveVoices} voices");
        if (fake.Stopped.Count != 1 || fake.Stopped[0] != first) failures.Add("the row did not steal its own oldest voice");
        SfxDef keep = Probe("keep", 50, 1, 0f);
        keep.Steal = StealMode.None;
        voices.Post(keep, clip, 0f, 1f, 1.0);
        if (voices.Post(keep, clip, 0f, 1f, 1.1) != PostResult.Capped) failures.Add("a row that does not steal played over its cap");

        // The bus cap: the lowest priority yields; a lower newcomer is dropped; ≥ 90 is safe.
        voices.StopAll();
        SfxDef low = Probe("low", 20, 8, 0f), mid = Probe("mid", 50, 8, 0f), high = Probe("high", 95, 8, 0f), lower = Probe("lower", 10, 8, 0f);
        voices.Post(low, clip, 0f, 1f, 2.0);
        voices.Post(mid, clip, 0f, 1f, 2.0);
        voices.Post(mid, clip, 0f, 1f, 2.0);
        voices.Post(high, clip, 0f, 1f, 2.0);
        if (voices.LiveOn(AudioBusId.Sfx) != 4) failures.Add("the bus did not fill to its cap of 4");
        if (voices.Post(lower, clip, 0f, 1f, 2.1) != PostResult.Capped) failures.Add("a newcomer lower than everything on a full bus played");
        if (voices.Post(mid, clip, 0f, 1f, 2.1) != PostResult.Played || low.LiveVoices != 0) failures.Add("a full bus did not give up its lowest priority");
        voices.Post(high, clip, 0f, 1f, 2.2);
        voices.Post(high, clip, 0f, 1f, 2.2);
        voices.Post(high, clip, 0f, 1f, 2.2);
        if (voices.Post(mid, clip, 0f, 1f, 2.3) != PostResult.Capped || high.LiveVoices != 4) failures.Add("a voice of priority 90+ was stolen by a lower one");
        if (voices.LiveOn(AudioBusId.Sfx) > 4) failures.Add("the bus went over its cap");

        // Voices end by themselves — and the engine is told about each one: its voice ends in
        // ITS time, and a slot it still holds is a sound refused later.
        int retiring = voices.LiveVoices, stopsBefore = fake.Stops;
        voices.Update(10.0, 0.016f);
        if (voices.LiveVoices != 0) failures.Add($"{voices.LiveVoices} one-shots still counted nine seconds after they ended");
        if (fake.Stops - stopsBefore != retiring) failures.Add($"{retiring} voices ended by themselves and the engine was told to stop {fake.Stops - stopsBefore} of them");

        // A volley: the fourth post inside the window is the burst, the rest are dropped.
        SfxDef burst = Probe("burst", 55, 1, 0f);
        SfxDef seed = Probe("seed", 45, 3, 0.03f);
        seed.Bursting(4, burst);
        int played = 0, bursts = 0, dropped = 0;
        for (int i = 0; i < 24; i++)
        {
            PostResult r = voices.Post(seed, clip, 0f, 1f, 20.0 + i * 0.002);
            if (r == PostResult.Played) played++;
            else if (r == PostResult.Burst) bursts++;
            else dropped++;
        }
        if (bursts != 1) failures.Add($"24 seed hits in 48 ms made {bursts} bursts, expected 1");
        if (played > 2) failures.Add($"24 seed hits in 48 ms played {played} single pops as well (cooldown 30 ms allows 1–2)");
        if (voices.Post(seed, clip, 0f, 1f, 21.0) != PostResult.Played) failures.Add("a seed hit a second after a volley did not play");

        // Loops are requests: five bodies, a cap of two, two heard; stop one and a third is.
        voices.StopAll();
        SfxDef burn = Probe("burn", 40, 2, 0f);
        burn.Looping();
        burn.MaxVoices = 2;
        burn.Sets[0] = new SfxSet(new[] { new[] { new SfxClip { Seconds = 1f, Loop = true } } });
        var bodies = new object[5];
        for (int i = 0; i < 5; i++) voices.StartLoop(burn, bodies[i] = new object(), 0, 0, -10f, 1f, 30.0);
        voices.StartLoop(burn, bodies[0], 0, 0, -10f, 1f, 30.0);
        voices.Update(30.02, 0.016f);
        if (voices.LoopRequests != 5) failures.Add($"5 burning bodies are {voices.LoopRequests} loop requests");
        if (voices.LoopsSounding != 2) failures.Add($"a loop row capped at 2 sounds {voices.LoopsSounding}");
        voices.StopLoop(burn, bodies[0]);
        for (int i = 0; i < 40; i++) voices.Update(30.04 + i * 0.016, 0.016f);
        if (voices.LoopRequests != 4 || voices.LoopsSounding != 2) failures.Add($"after one of five stopped: {voices.LoopRequests} requests, {voices.LoopsSounding} sounding (expected 4 and 2)");
        voices.Update(31.0, 0.016f, source => !ReferenceEquals(source, bodies[1]) && !ReferenceEquals(source, bodies[2]));
        voices.Update(31.02, 0.016f);
        if (voices.LoopRequests != 2) failures.Add("a loop whose source left the tree was not ended");
        voices.StopLoops();
        for (int i = 0; i < 40; i++) voices.Update(31.1 + i * 0.016, 0.016f);
        if (voices.LiveVoices != 0 || burn.LiveVoices != 0) failures.Add("stopped loops still hold voices");

        // The engine refusing a voice is counted, not fatal.
        fake.Refuse = true;
        if (voices.Post(Probe("r", 50, 2, 0f), clip, 0f, 1f, 40.0) != PostResult.Refused || voices.LiveVoices != 0) failures.Add("a refused voice was counted as playing");
        return failures;
    }

    /// <summary>The clock: evenly spaced beats, bars on the first of them, stingers on the grid.</summary>
    public static List<string> ClockRules()
    {
        var failures = new List<string>();
        var clock = new MusicClock();
        var beats = new List<double>();
        var bars = new List<int>();
        double now = 0;
        clock.BeatHit += _ => beats.Add(now);
        clock.BarHit += bar => bars.Add(bar);
        const double spb = 11025.0 / 22050.0 * 1.154;      // an awkward tempo
        clock.Start(1.0, spb, 3);
        const double frame = 1.0 / 60.0;
        for (int i = 0; i < 60 * 30; i++) { now = i * frame; clock.Advance(now); }
        if (beats.Count < 40) failures.Add($"only {beats.Count} beats in 29 s");
        double worst = 0;
        for (int i = 0; i < beats.Count; i++) worst = Math.Max(worst, Math.Abs(beats[i] - clock.TimeOfBeat(i)));
        if (worst > frame + 1e-9) failures.Add($"a beat was announced {worst * 1000:0.0} ms from its time (one frame is {frame * 1000:0.0})");
        for (int i = 1; i < beats.Count; i++)
            if (Math.Abs(beats[i] - beats[i - 1] - spb) > frame + 1e-9) failures.Add($"beats {i - 1} and {i} are {(beats[i] - beats[i - 1]) * 1000:0} ms apart, not {spb * 1000:0}");
        if (bars.Count != (beats.Count + 2) / 3) failures.Add($"{bars.Count} bars for {beats.Count} beats of three");

        // Quantizing: never earlier than asked, never later than the wait allows, always on the grid.
        var rng = new SynthRng(7);
        foreach (Quantize mode in new[] { Quantize.Grid, Quantize.Beat, Quantize.Bar })
        {
            for (int i = 0; i < 400; i++)
            {
                double asked = 1.0 + rng.Next() * 20.0;
                double at = clock.Quantized(asked, mode);
                double grid = (at - clock.T0) / (spb / 4.0);
                if (at < asked - 1e-9) failures.Add($"{mode}: quantized into the past");
                if (Math.Abs(grid - Math.Round(grid)) > 1e-6) failures.Add($"{mode}: {at:0.000} is not on a quarter-beat");
                double limit = mode == Quantize.Grid ? Math.Max(MusicClock.GridWait, spb / 4.0) : mode == Quantize.Beat ? MusicClock.BeatWait : MusicClock.BarWait;
                if (at - asked > limit + 1e-6) failures.Add($"{mode}: waited {(at - asked) * 1000:0} ms (limit {limit * 1000:0})");
                if (failures.Count > 12) return failures;
            }
        }
        if (clock.Quantized(0.2, Quantize.Bar) != 0.2) failures.Add("a stinger before the music starts was held back");
        clock.Stop();
        if (clock.Quantized(5.0, Quantize.Bar) != 5.0) failures.Add("a stinger with no music playing was held back");
        return failures;
    }

    /// <summary>The combo ladder climbs and never leaves a realm's scale.</summary>
    public static List<string> LadderRules()
    {
        var failures = new List<string>();
        foreach (MusicProfile p in MusicProfiles.All)
        {
            int previous = -1;
            bool climbed = false;
            for (int step = 1; step <= 40; step++)
            {
                int semis = AudioBlackboard.LadderSemis(p.Scale, step);
                int pitchClass = ((semis % 12) + 12) % 12;
                if (Array.IndexOf(p.Scale, pitchClass) < 0) failures.Add($"{p.Id}: hit {step} plays +{semis} st, which is not in the scale");
                if (semis < 0 || semis > 14) failures.Add($"{p.Id}: hit {step} plays +{semis} st, outside the instrument's ninth");
                if (semis > previous) climbed = true;
                previous = semis;
            }
            if (AudioBlackboard.LadderSemis(p.Scale, 1) != 0) failures.Add($"{p.Id}: the first hit is not the tonic");
            if (!climbed) failures.Add($"{p.Id}: the ladder never climbs");
        }
        var board = new AudioBlackboard();
        if (board.PlayerHit(10f, 1.00) != 1 || board.PlayerHit(10f, 1.30) != 2) failures.Add("two hits 0.3 s apart are not steps 1 and 2");
        if (board.PlayerHit(10f, 1.31) != 0) failures.Add("a hit 10 ms after another is its own step (a volley must be one)");
        if (board.PlayerHit(10f, 1.60) != 3) failures.Add("the combo did not continue");
        board.Resolve();
        if (board.PlayerHit(10f, 1.90) != 1) failures.Add("a kill did not restart the ladder");
        if (board.PlayerHit(10f, 9.00) != 1) failures.Add("a hit long after the last did not restart the ladder");
        board.PlayerHit(10f, 9.3);
        board.PlayerHurt(20f);
        if (board.Combo != 0) failures.Add("being hit did not drop the combo");
        return failures;
    }
}

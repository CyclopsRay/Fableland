using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Godot;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

/// <summary>
/// The bus layout, built in code (<c>Docs/Audio.gdd</c> §9): Master ← Music, SFX, Ambience,
/// UI; a hard limiter on Master; a low-pass slot on Music, SFX and Ambience for the mix
/// states. Idempotent — there is no bus layout file to drift from it. Levels are designed,
/// not heard: Music −8 dB, SFX −3, Ambience −14, UI −6, each then scaled by the player's
/// volume setting.
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master", Music = "Music", Sfx = "SFX", Ambience = "Ambience", Ui = "UI";
    public const float MusicDb = -8f, SfxDb = -3f, AmbienceDb = -14f, UiDb = -6f;
    /// <summary>A cutoff at or above this is "no filter": the effect is switched off.</summary>
    public const float OpenHz = 18000f;

    private static readonly StringName[] Names = { Sfx, Ui, Ambience, Music };
    private static readonly int[] Index = new int[4];
    private static readonly AudioEffectLowPassFilter[] LowPass = new AudioEffectLowPassFilter[4];
    private static readonly float[] LastDb = { float.NaN, float.NaN, float.NaN, float.NaN };
    private static readonly float[] LastHz = { float.NaN, float.NaN, float.NaN, float.NaN };
    private static float _lastMasterDb = float.NaN;
    public static bool Built { get; private set; }

    public static StringName NameOf(AudioBusId bus) => Names[(int)bus];
    public static int IndexOf(AudioBusId bus) => Index[(int)bus];

    public static void Ensure()
    {
        if (Built) return;
        foreach (AudioBusId bus in new[] { AudioBusId.Music, AudioBusId.Sfx, AudioBusId.Ambience, AudioBusId.Ui })
        {
            StringName name = Names[(int)bus];
            int index = AudioServer.GetBusIndex(name);
            if (index < 0)
            {
                AudioServer.AddBus();
                index = AudioServer.BusCount - 1;
                AudioServer.SetBusName(index, name);
            }
            AudioServer.SetBusSend(index, Master);
            Index[(int)bus] = index;
            if (bus == AudioBusId.Ui) continue;            // the UI is never filtered
            var filter = new AudioEffectLowPassFilter { CutoffHz = 20000f, Resonance = 0.5f };
            AudioServer.AddBusEffect(index, filter, 0);
            AudioServer.SetBusEffectEnabled(index, 0, false);
            LowPass[(int)bus] = filter;
        }
        bool limited = false;
        for (int i = 0; i < AudioServer.GetBusEffectCount(0); i++)
            if (AudioServer.GetBusEffect(0, i) is AudioEffectHardLimiter) limited = true;
        if (!limited) AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -0.3f });
        Built = true;
    }

    /// <summary>Volume setting 0…1 → dB; 0 is a mute.</summary>
    public static float SettingDb(float setting) => setting <= 0.0005f ? -80f : Dsp.LinToDb(setting);

    /// <summary>The level a bus is at with no mix state on it: its designed level and the
    /// player's setting.</summary>
    public static float RestDb(AudioBusId bus) => bus switch
    {
        AudioBusId.Music => MusicDb + SettingDb(FeedbackSettings.MusicVolume),
        AudioBusId.Sfx => SfxDb + SettingDb(FeedbackSettings.SfxVolume),
        AudioBusId.Ambience => AmbienceDb + SettingDb(FeedbackSettings.SfxVolume),
        _ => UiDb + SettingDb(FeedbackSettings.UiVolume),
    };

    /// <summary>Put a bus at its rest level plus a mix-state offset, behind a low-pass.
    /// Only touches the engine when something changed.</summary>
    public static void Apply(AudioBusId bus, float offsetDb, float cutoffHz)
    {
        if (!Built) return;
        int b = (int)bus;
        float db = Math.Max(-80f, RestDb(bus) + offsetDb);
        if (float.IsNaN(LastDb[b]) || Math.Abs(db - LastDb[b]) > 0.02f)
        {
            AudioServer.SetBusVolumeDb(Index[b], db);
            LastDb[b] = db;
        }
        AudioEffectLowPassFilter filter = LowPass[b];
        if (filter == null) return;
        if (float.IsNaN(LastHz[b]) || Math.Abs(cutoffHz - LastHz[b]) > LastHz[b] * 0.01f)
        {
            bool open = cutoffHz >= OpenHz;
            if (!open) filter.CutoffHz = cutoffHz;
            if (float.IsNaN(LastHz[b]) || open != (LastHz[b] >= OpenHz)) AudioServer.SetBusEffectEnabled(Index[b], 0, !open);
            LastHz[b] = cutoffHz;
        }
    }

    public static void ApplyMaster()
    {
        if (!Built) return;
        float db = SettingDb(FeedbackSettings.MasterVolume);
        if (!float.IsNaN(_lastMasterDb) && Math.Abs(db - _lastMasterDb) <= 0.02f) return;
        AudioServer.SetBusVolumeDb(0, db);
        _lastMasterDb = db;
    }

    public static float VolumeDb(AudioBusId bus) => AudioServer.GetBusVolumeDb(Index[(int)bus]);
    public static float CutoffHz(AudioBusId bus) =>
        LowPass[(int)bus] != null && AudioServer.IsBusEffectEnabled(Index[(int)bus], 0) ? LowPass[(int)bus].CutoffHz : 20000f;
}

/// <summary>The engine's voices: one polyphonic player per bus (the per-voice bus argument
/// is reported ignored — R1 §5.5 — so a bus is a player), each a little larger than the
/// voice manager's cap so a voice that our end-time estimate freed a moment early still has
/// somewhere to go.</summary>
public sealed class GodotVoices : IVoiceBackend
{
    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[4];
    private readonly AudioStreamPlaybackPolyphonic[] _playbacks = new AudioStreamPlaybackPolyphonic[4];

    public GodotVoices(Node parent)
    {
        foreach (AudioBusId bus in new[] { AudioBusId.Sfx, AudioBusId.Ui, AudioBusId.Ambience, AudioBusId.Music })
        {
            var player = new AudioStreamPlayer
            {
                Name = "Voices" + bus,
                Bus = AudioBuses.NameOf(bus),
                // Twice the cap: a voice we stop is only free after the engine's next mix, so a
                // frame that steals many needs somewhere to put the newcomers meanwhile.
                Stream = new AudioStreamPolyphonic { Polyphony = VoiceManager.DefaultCap(bus) * 2 },
                // A paused tree holds the fight's sounds; the UI and the music keep going.
                ProcessMode = bus == AudioBusId.Sfx || bus == AudioBusId.Ambience ? Node.ProcessModeEnum.Pausable : Node.ProcessModeEnum.Always,
            };
            parent.AddChild(player);
            _players[(int)bus] = player;
        }
    }

    private AudioStreamPlaybackPolyphonic Playback(AudioBusId bus)
    {
        int b = (int)bus;
        if (_playbacks[b] == null || !_players[b].Playing)
        {
            // The playback exists only after Stream is set AND Play() was called (R1 §5.4).
            _players[b].Play();
            _playbacks[b] = _players[b].GetStreamPlayback() as AudioStreamPlaybackPolyphonic;
        }
        return _playbacks[b];
    }

    public long Play(AudioBusId bus, SfxClip clip, float volumeDb, float pitchScale)
    {
        if (clip.Stream is not AudioStream stream) return -1;
        AudioStreamPlaybackPolyphonic playback = Playback(bus);
        if (playback == null) return -1;
        // The bus name is passed explicitly: the default would build a StringName per call.
        return playback.PlayStream(stream, 0f, volumeDb, pitchScale, AudioServer.PlaybackType.Default, AudioBuses.NameOf(bus));
    }

    public void Stop(AudioBusId bus, long handle) => _playbacks[(int)bus]?.StopStream(handle);
    public void SetVolume(AudioBusId bus, long handle, float volumeDb) => _playbacks[(int)bus]?.SetStreamVolume(handle, volumeDb);
    public bool IsPlaying(AudioBusId bus, long handle) => _playbacks[(int)bus] != null && _playbacks[(int)bus].IsStreamPlaying(handle);
}

/// <summary>
/// A file wins over a recipe (<c>Docs/Audio.gdd</c> §10). If <c>res://Assets/Audio/&lt;event
/// id&gt;.wav</c> (or <c>.ogg</c>) exists it replaces the synthesis of that event, with no
/// code change — the upgrade path to recorded or commissioned sound. A medium-specific file
/// (<c>&lt;id&gt;@&lt;MEDIUM&gt;.wav</c>) wins over a general one; <c>_2</c> … <c>_4</c> add variants.
/// </summary>
public static class AudioFiles
{
    public const string DefaultRoot = "res://Assets/Audio";
    /// <summary>Where override files are looked for. A check points it at a scratch folder.</summary>
    public static string Root = DefaultRoot;

    private static readonly string[] Extensions = { ".wav", ".ogg" };

    /// <summary>The files that override one case of an event in one medium, as clips (empty
    /// when there are none).</summary>
    public static List<SfxClip> Find(string caseId, Medium medium, bool loop)
    {
        var found = new List<SfxClip>();
        foreach (string stem in new[] { caseId + "@" + medium.Id, caseId })
        {
            for (int n = 1; n <= 4; n++)
            {
                AudioStream stream = Load(Root.TrimEnd('/') + "/" + stem + (n == 1 ? "" : "_" + n), loop);
                if (stream == null) break;
                found.Add(new SfxClip { Stream = stream, FromFile = true, Loop = loop, Seconds = (float)stream.GetLength(), SampleRate = 0 });
            }
            if (found.Count > 0) break;
        }
        return found;
    }

    private static AudioStream Load(string pathNoExtension, bool loop)
    {
        foreach (string ext in Extensions)
        {
            string path = pathNoExtension + ext;
            AudioStream stream = null;
            if (path.StartsWith("res://") && ResourceLoader.Exists(path)) stream = ResourceLoader.Load<AudioStream>(path);
            else if (FileAccess.FileExists(path))
                stream = ext == ".wav" ? AudioStreamWav.LoadFromFile(path) : AudioStreamOggVorbis.LoadFromFile(path);
            if (stream == null) continue;
            if (loop)
            {
                if (stream is AudioStreamWav wav && wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
                {
                    // A looping WAV with no loop end has an empty loop (R1 §5.1): give it one.
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                    wav.LoopBegin = 0;
                    wav.LoopEnd = (int)Math.Round(wav.GetLength() * wav.MixRate);
                }
                else if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
            }
            return stream;
        }
        return null;
    }
}

/// <summary>
/// The sound cache (<c>Docs/Audio.gdd</c> §11). Sounds are rendered on ONE worker thread
/// (pure C#) and wrapped as <c>AudioStreamWAV</c> on the main thread. Three kinds of bank:
/// the common one (UI, the storybook's screens, the VOID's voice), one per character — its
/// own rows plus the class rows in its home medium — and the realm's. The realm's is evicted
/// when the realm changes; the others stay.
/// </summary>
public sealed class AudioCache
{
    /// <summary>The steady-state budget for PCM (sound effects + music), bytes.</summary>
    public const int BudgetBytes = 8 * 1024 * 1024;

    private sealed class Job
    {
        public SfxDef Def;
        public Medium Medium;
        public SfxClip[][] Cases;
        public MusicProfile Profile;
        public string BossOwner;
        public ulong Seed;
        public int Serial;
        public MusicSet Music;
        public SfxClip Layer;
    }

    private readonly BlockingCollection<Job> _todo = new();
    private readonly ConcurrentQueue<Job> _done = new();
    private readonly HashSet<(SfxDef, int)> _requested = new();
    private readonly Thread _worker;
    private int _pending;
    private long _renderTicks;

    /// <summary>Keep the managed PCM after wrapping (checks and the dump measure it).</summary>
    public bool KeepPcm;
    public int Pending => Volatile.Read(ref _pending);
    public double RenderSeconds => Interlocked.Read(ref _renderTicks) / (double)Stopwatch.Frequency;
    public int ClipsRendered { get; private set; }
    public int FilesUsed { get; private set; }

    /// <summary>Called on the main thread when a bounce is ready.</summary>
    public Action<MusicSet, int> MusicReady;
    public Action<SfxClip, int> LayerReady;

    public AudioCache()
    {
        _worker = new Thread(Work) { IsBackground = true, Name = "audio-synth", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    public void Shutdown() => _todo.CompleteAdding();

    private void Work()
    {
        try
        {
            foreach (Job job in _todo.GetConsumingEnumerable())
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    if (job.Def != null) RenderCases(job.Def, job.Medium, job.Cases);
                    else if (job.BossOwner != null) job.Layer = MusicBounce.BossLayer(job.Profile, job.BossOwner, job.Seed);
                    else job.Music = MusicBounce.Bounce(job.Profile, job.Seed);
                }
                catch (Exception e)
                {
                    // A recipe that throws costs its sound, never the game (T10 §5).
                    GD.PushError($"[AudioCache] rendering {job.Def?.Id ?? job.Profile?.Id} failed: {e}");
                }
                Interlocked.Add(ref _renderTicks, Stopwatch.GetTimestamp() - start);
                _done.Enqueue(job);
            }
        }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    /// <summary>Fill every case that has no file with its seed-mutated variants.</summary>
    public static void RenderCases(SfxDef def, Medium medium, SfxClip[][] cases)
    {
        int tags = def.Tags.Length + 1;
        for (int index = 0; index < cases.Length; index++)
        {
            if (cases[index] != null) continue;
            int stage = index / tags, tagCase = index % tags;
            var clips = new SfxClip[Math.Clamp(def.Variants, 1, 4)];
            for (int v = 0; v < clips.Length; v++) clips[v] = SfxRender.Render(def, medium, stage, tagCase, v);
            cases[index] = clips;
        }
    }

    // ── Requests (main thread) ──────────────────────────────────────────────────────
    private SfxClip[][] FileCases(SfxDef def, Medium medium)
    {
        var cases = new SfxClip[def.CaseCount][];
        int tags = def.Tags.Length + 1;
        for (int index = 0; index < cases.Length; index++)
        {
            List<SfxClip> files = AudioFiles.Find(def.CaseId(index / tags, index % tags), medium, def.Loop);
            if (files.Count == 0) continue;
            cases[index] = files.ToArray();
            FilesUsed += files.Count;
        }
        return cases;
    }

    /// <summary>Ask for a row's sounds in a medium. Returns at once; they arrive on a later frame.</summary>
    public void Request(SfxDef def, Medium medium)
    {
        if (def.Sets[medium.Index] != null || !_requested.Add((def, medium.Index))) return;
        Interlocked.Increment(ref _pending);
        _todo.Add(new Job { Def = def, Medium = medium, Cases = FileCases(def, medium) });
    }

    /// <summary>Render a row's sounds in a medium on the calling thread (checks, the dump).</summary>
    public SfxSet RenderNow(SfxDef def, Medium medium)
    {
        if (def.Sets[medium.Index] != null) return def.Sets[medium.Index];
        _requested.Add((def, medium.Index));
        SfxClip[][] cases = FileCases(def, medium);
        long start = Stopwatch.GetTimestamp();
        RenderCases(def, medium, cases);
        Interlocked.Add(ref _renderTicks, Stopwatch.GetTimestamp() - start);
        Install(def, medium, cases);
        return def.Sets[medium.Index];
    }

    public void RequestMusic(MusicProfile profile, ulong seed, int serial)
    {
        Interlocked.Increment(ref _pending);
        _todo.Add(new Job { Profile = profile, Seed = seed, Serial = serial });
    }

    public void RequestBossLayer(MusicProfile profile, string bossOwner, ulong seed, int serial)
    {
        Interlocked.Increment(ref _pending);
        _todo.Add(new Job { Profile = profile, BossOwner = bossOwner, Seed = seed, Serial = serial });
    }

    /// <summary>Install what the worker finished. Main thread, every frame.</summary>
    public void Pump()
    {
        while (_done.TryDequeue(out Job job))
        {
            Interlocked.Decrement(ref _pending);
            if (job.Def != null)
            {
                if (job.Cases != null && Array.TrueForAll(job.Cases, c => c != null)) Install(job.Def, job.Medium, job.Cases);
            }
            else if (job.Layer != null)
            {
                Wrap(job.Layer);
                LayerReady?.Invoke(job.Layer, job.Serial);
            }
            else if (job.Music != null)
            {
                foreach (SfxClip stem in job.Music.Stems) Wrap(stem);
                MusicReady?.Invoke(job.Music, job.Serial);
            }
        }
    }

    private void Install(SfxDef def, Medium medium, SfxClip[][] cases)
    {
        foreach (SfxClip[] clips in cases)
            foreach (SfxClip clip in clips)
            {
                if (clip.FromFile) continue;
                Wrap(clip);
                ClipsRendered++;
            }
        def.Sets[medium.Index] = new SfxSet(cases);
    }

    /// <summary>PCM → a stream the engine can play. A looping runtime WAV must be told
    /// where its loop ends (R1 §5.1).</summary>
    public void Wrap(SfxClip clip)
    {
        if (clip.Stream != null || clip.Pcm == null) return;
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = clip.SampleRate,
            Stereo = false,
            Data = clip.Pcm,
        };
        if (clip.Loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = clip.Pcm.Length / 2;
        }
        clip.Stream = wav;
        if (!KeepPcm) clip.ReleasePcm();
    }

    // ── Banks ───────────────────────────────────────────────────────────────────────
    private static bool IsClassRow(SfxDef def) =>
        def.Owner is CueOwners.Any or CueOwners.Fighter or CueOwners.Foe or CueOwners.Boss or CueOwners.Objective or CueOwners.Projectile;

    /// <summary>Rows in one fixed medium: the UI, the screens, the VOID's voice.</summary>
    public void WarmCommon(bool now = false)
    {
        foreach (SfxDef def in SfxTable.All)
            if (def.From == MediumFrom.Fixed) Get(def, Medium.Get(def.FixedMedium) ?? Medium.Home, now);
    }

    /// <summary>A character: every row its cues and its hits resolve to — its own, then its
    /// class's, then <c>any</c>'s — in its home medium. Nothing it cannot raise is made.</summary>
    public void WarmOwner(string owner, Medium fallback, bool now = false)
    {
        Medium medium = Medium.HomeFor(owner) ?? fallback;
        string ownerClass = Medium.ClassFor(owner);
        foreach ((string _, string action) in SfxTable.Cues.Keys)
        {
            Consider(SfxTable.ForCue(owner, ownerClass, action), medium, now);
            Consider(SfxTable.ForCue(owner, CueOwners.Projectile, action), medium, now);
        }
        foreach ((string _, string action) in SfxTable.Attacks.Keys)
            Consider(SfxTable.ForAttack(owner, ownerClass, action), medium, now);
    }

    private void Consider(SfxDef def, Medium medium, bool now)
    {
        if (def == null || def.From != MediumFrom.Owner) return;
        Get(def, medium, now);
        if (def.BurstDef != null) Get(def.BurstDef, medium, now);
        if (def.Layer != null) Get(def.Layer, medium, now);
    }

    /// <summary>A realm: its hazards, its world, its statuses, its flow. A body that has no
    /// home of its own gets its class's rows in this medium the first time it speaks.</summary>
    public void WarmRealm(Medium medium, bool now = false)
    {
        foreach (SfxDef def in SfxTable.All)
            if (def.From == MediumFrom.Realm) Get(def, medium, now);
    }

    private void Get(SfxDef def, Medium medium, bool now)
    {
        if (now) RenderNow(def, medium);
        else Request(def, medium);
    }

    /// <summary>Forget a realm's sounds: its own rows, and the class rows unless a known
    /// character lives in that medium.</summary>
    public void EvictRealm(Medium medium)
    {
        bool inhabited = false;
        foreach (string owner in Medium.KnownOwners) inhabited |= Medium.HomeFor(owner) == medium;
        foreach (SfxDef def in SfxTable.All)
        {
            bool realmRow = def.From == MediumFrom.Realm;
            if (!realmRow && (inhabited || def.From != MediumFrom.Owner || !IsClassRow(def))) continue;
            if (def.Sets[medium.Index] == null) continue;
            def.Sets[medium.Index] = null;
            _requested.Remove((def, medium.Index));
        }
    }

    /// <summary>Forget everything (a check starting from nothing).</summary>
    public void Clear()
    {
        foreach (SfxDef def in SfxTable.All)
            for (int m = 0; m < def.Sets.Length; m++) def.Sets[m] = null;
        _requested.Clear();
    }

    /// <summary>Bytes of sound-effect PCM loaded, optionally for one medium.</summary>
    public static int LoadedBytes(Medium only = null)
    {
        int total = 0;
        foreach (SfxDef def in SfxTable.All)
            for (int m = 0; m < def.Sets.Length; m++)
                if (def.Sets[m] != null && (only == null || only.Index == m)) total += def.Sets[m].Bytes;
        return total;
    }
}

/// <summary>
/// The music engine (<c>Docs/Audio.gdd</c> §7): a realm's stems in one synchronized stream,
/// faded by intensity; a second player to cross-fade to the next realm or screen; a third
/// for a boss's motif; and the clock.
///
/// Engine facts it is built around, each verified against the 4.7 source by the research
/// digest (R1 §5) or by running: stems of a synchronized stream loop independently, each at
/// its own length; a stem's volume is applied with no ramp, so it is moved in small steps
/// and muted below −40 dB; a stream is never changed while it plays — a new one is built and
/// cross-faded in.
/// </summary>
public sealed class MusicEngine
{
    public const float StemDbPerSecond = 24f;
    public const float MuteBelowDb = -40f;
    public const float CrossfadeSeconds = 0.8f;

    private sealed class Deck
    {
        public AudioStreamPlayer Player;
        public AudioStreamSynchronized Stream;
        public MusicSet Set;
        public readonly float[] StemDb = new float[8];
        public float FadeDb = -80f, FadeTarget = -80f, FadePerSecond = 100f;
    }

    private readonly Deck[] _decks = { new(), new() };
    private int _live = -1;
    private readonly AudioStreamPlayer _overlay;
    private float _overlayDb = -80f, _overlayTarget = -80f;
    private SfxClip _overlayPending;
    private double _overlayAt;
    private float _duckDb, _duckLeft;

    public readonly MusicClock Clock = new();
    public MusicSet Current => _live >= 0 ? _decks[_live].Set : null;
    public bool Playing => _live >= 0 && _decks[_live].Player.Playing;
    public bool OverlayPlaying => _overlay.Playing;
    public float OverlayDb => _overlayDb;
    /// <summary>The lead the audio has over a call made now: time to the next mix plus the
    /// output latency (cached — the engine calls reading it expensive).</summary>
    public double Lead => NoLead ? 0.0 : AudioServer.GetTimeToNextMix() + _latency;
    /// <summary>Set when the director runs on a made-up clock: there is no sound card to lead.</summary>
    public bool NoLead;
    private readonly double _latency;

    public MusicEngine(Node parent)
    {
        _latency = AudioServer.GetOutputLatency();
        for (int i = 0; i < _decks.Length; i++)
        {
            _decks[i].Player = new AudioStreamPlayer { Name = "Music" + (char)('A' + i), Bus = AudioBuses.Music, ProcessMode = Node.ProcessModeEnum.Always, VolumeDb = -80f };
            parent.AddChild(_decks[i].Player);
        }
        _overlay = new AudioStreamPlayer { Name = "MusicBossLayer", Bus = AudioBuses.Music, ProcessMode = Node.ProcessModeEnum.Always, VolumeDb = -80f };
        parent.AddChild(_overlay);
    }

    /// <summary>The level of one stem of the live deck, as last sent to the engine.</summary>
    public float StemDb(int stem) => _live >= 0 ? _decks[_live].StemDb[stem] : -80f;
    public float DeckDb => _live >= 0 ? _decks[_live].FadeDb : -80f;

    /// <summary>Start a bounced set, cross-fading from whatever plays. The clock is
    /// re-anchored to it.</summary>
    public void Play(MusicSet set, float intensity, double now)
    {
        int next = _live == 0 ? 1 : 0;
        Deck deck = _decks[next];
        var stream = new AudioStreamSynchronized { StreamCount = set.Stems.Length };
        for (int i = 0; i < set.Stems.Length; i++)
        {
            stream.SetSyncStream(i, (AudioStream)set.Stems[i].Stream);
            float db = MusicBounce.StemDb(set.Profile.Stems[i], intensity);
            deck.StemDb[i] = db < MuteBelowDb ? -80f : db;
            stream.SetSyncStreamVolume(i, deck.StemDb[i]);
        }
        deck.Stream = stream;
        deck.Set = set;
        deck.Player.Stream = stream;
        deck.FadeDb = -40f;
        deck.FadeTarget = 0f;
        deck.FadePerSecond = 40f / CrossfadeSeconds;
        deck.Player.VolumeDb = deck.FadeDb;
        deck.Player.Play();
        // Beat 0 is HEARD after the next mix and the output latency (R1 §5.8).
        Clock.Start(now + Lead, set.Profile.SecondsPerBeat, set.Profile.BeatsPerBar);
        if (_live >= 0) FadeOut(_decks[_live], CrossfadeSeconds);
        _live = next;
        StopOverlay(0.3f);
    }

    /// <summary>Fade the music out and stop the clock.</summary>
    public void Stop(float seconds)
    {
        if (_live >= 0) FadeOut(_decks[_live], seconds);
        _live = -1;
        Clock.Stop();
        StopOverlay(seconds);
    }

    private static void FadeOut(Deck deck, float seconds)
    {
        deck.FadeTarget = -80f;
        deck.FadePerSecond = Math.Max(1f, (deck.FadeDb + 60f) / Math.Max(0.05f, seconds));
    }

    /// <summary>A boss's layer: it enters on the next bar, so its ostinato sits on the grid.</summary>
    public void StartOverlay(SfxClip layer, double now)
    {
        if (layer?.Stream == null || !Clock.Running) return;
        _overlayPending = layer;
        _overlayAt = Clock.NextBoundary(now, -1);
    }

    public void StopOverlay(float seconds)
    {
        _overlayPending = null;
        _overlayTarget = -80f;
    }

    /// <summary>Pull the music down for a moment (a stinger that must be heard).</summary>
    public void Duck(float db, float seconds)
    {
        _duckDb = Math.Min(_duckDb, db);
        _duckLeft = Math.Max(_duckLeft, seconds);
    }

    public void Update(double now, float dt, float intensity)
    {
        if (_duckLeft > 0f) _duckLeft -= dt;
        float duck = _duckLeft > 0f ? _duckDb : 0f;
        if (_duckLeft <= 0f) _duckDb = 0f;

        for (int d = 0; d < _decks.Length; d++)
        {
            Deck deck = _decks[d];
            if (deck.Set == null) continue;
            if (deck.FadeDb != deck.FadeTarget)
            {
                float step = deck.FadePerSecond * dt;
                deck.FadeDb = deck.FadeDb < deck.FadeTarget ? Math.Min(deck.FadeTarget, deck.FadeDb + step) : Math.Max(deck.FadeTarget, deck.FadeDb - step);
            }
            float want = deck.FadeDb + (d == _live ? duck : 0f);
            if (Math.Abs(deck.Player.VolumeDb - want) > 0.05f)
                deck.Player.VolumeDb = deck.Player.VolumeDb + Math.Clamp(want - deck.Player.VolumeDb, -90f * dt, 90f * dt);
            if (d != _live && deck.FadeDb <= -59f)
            {
                deck.Player.Stop();
                deck.Player.Stream = null;
                deck.Stream = null;
                deck.Set = null;
                continue;
            }
            if (d != _live) continue;
            // Stem levels follow intensity in steps the ear does not hear as steps.
            StemDef[] stems = deck.Set.Profile.Stems;
            for (int i = 0; i < stems.Length; i++)
            {
                float target = MusicBounce.StemDb(stems[i], intensity);
                if (target < MuteBelowDb) target = -80f;
                float have = deck.StemDb[i];
                if (have == target) continue;
                float next;
                if (target > have) next = have < MuteBelowDb ? MuteBelowDb : Math.Min(target, have + StemDbPerSecond * dt);
                else next = have - StemDbPerSecond * dt < Math.Max(target, MuteBelowDb) ? target : have - StemDbPerSecond * dt;
                deck.StemDb[i] = next;
                deck.Stream.SetSyncStreamVolume(i, next);
            }
        }

        if (_overlayPending != null && now + Lead >= _overlayAt)
        {
            _overlay.Stream = (AudioStream)_overlayPending.Stream;
            _overlayDb = -30f;
            _overlayTarget = -4f;
            _overlay.VolumeDb = _overlayDb;
            _overlay.Play();
            _overlayPending = null;
        }
        if (_overlayDb != _overlayTarget)
        {
            float step = 40f * dt;
            _overlayDb = _overlayDb < _overlayTarget ? Math.Min(_overlayTarget, _overlayDb + step) : Math.Max(_overlayTarget, _overlayDb - step);
            _overlay.VolumeDb = _overlayDb + duck;
            if (_overlayDb <= -79f && _overlay.Playing) _overlay.Stop();
        }

        Clock.Advance(now);
    }
}

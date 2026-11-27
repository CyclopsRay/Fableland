using System;

namespace Fableland.Audio;

/// <summary>What actually makes sound — the engine's polyphonic players in the game, a
/// recorder in a check. Handles are opaque; −1 means "no voice was free".</summary>
public interface IVoiceBackend
{
    long Play(AudioBusId bus, SfxClip clip, float volumeDb, float pitchScale);
    void Stop(AudioBusId bus, long handle);
    void SetVolume(AudioBusId bus, long handle, float volumeDb);
}

public enum PostResult : byte
{
    Played,
    /// <summary>Inside the row's cooldown.</summary>
    Cooldown,
    /// <summary>The row or the bus was full and nothing could be stolen for it.</summary>
    Capped,
    /// <summary>The volley threshold was just crossed: the caller plays the row's burst once.</summary>
    Burst,
    /// <summary>Part of a volley that has already had its burst.</summary>
    BurstDropped,
    /// <summary>The engine had no voice (its polyphony is the last line of defence).</summary>
    Refused,
}

/// <summary>
/// The voice manager (<c>Docs/Audio.gdd</c> §3.3). The engine's polyphonic stream neither
/// limits how often a sound plays nor steals a voice when it is full — it just returns −1
/// (R1 §5.5, verified by running: 6 posts into a polyphony of 4 give 4 handles and two −1) —
/// so every rule that keeps a fight from turning into noise lives here:
///
///   1. a volley is one sound (burst coalescing);
///   2. a row cannot retrigger inside its cooldown;
///   3. a row has its own voice cap, and steals its own oldest voice;
///   4. a bus has a cap, and the lowest priority on it yields — unless the newcomer is lower
///      still, and never a voice of priority 90 or more to anything below it;
///   5. loops are requests that outlive voices: five burning bodies and a cap of two means
///      two are heard, and when one stops the next takes its place.
///
/// Pure logic on a clock the caller supplies; allocates nothing after construction.
/// </summary>
public sealed class VoiceManager
{
    public const int NeverStolen = 90;

    private struct Voice
    {
        public bool Active;
        public long Handle;
        public SfxDef Def;
        public AudioBusId Bus;
        public double StartedAt, EndsAt;
        public float VolumeDb, TargetDb, FadePerSecond;
        public int Priority;
        public bool Loop, Stopping;
        public int Request;
    }

    private struct LoopRequest
    {
        public bool Active;
        public SfxDef Def;
        public object Source;
        public int MediumIndex, CaseIndex;
        public float VolumeDb, Pitch;
        public int Voice;        // slot of the voice sounding it, or −1
        public double Since;
    }

    private readonly IVoiceBackend _backend;
    private readonly Voice[] _voices;
    private readonly LoopRequest[] _loops;
    private readonly int[] _busCap = new int[4];
    private readonly int[] _busLive = new int[4];

    // Counters, for checks and the debug page.
    public int Played, Stolen, DroppedCooldown, DroppedCap, DroppedBurst, Bursts, Refused;
    public int LiveVoices { get; private set; }
    public int PeakVoices { get; private set; }

    /// <summary>The voice budget (<c>Docs/Audio.gdd</c> §11): 24 effects, 4 UI, 4 ambience,
    /// 8 stingers and notes — 40 voices at the very most, usually under a dozen.</summary>
    public static int DefaultCap(AudioBusId bus) => bus switch
    {
        AudioBusId.Sfx => 24,
        AudioBusId.Music => 8,
        _ => 4,
    };

    public VoiceManager(IVoiceBackend backend, int sfx = 24, int ui = 4, int ambience = 4, int music = 8, int loops = 32)
    {
        _backend = backend;
        _busCap[(int)AudioBusId.Sfx] = sfx;
        _busCap[(int)AudioBusId.Ui] = ui;
        _busCap[(int)AudioBusId.Ambience] = ambience;
        _busCap[(int)AudioBusId.Music] = music;
        _voices = new Voice[sfx + ui + ambience + music];
        _loops = new LoopRequest[loops];
    }

    public int CapOf(AudioBusId bus) => _busCap[(int)bus];
    public int LiveOn(AudioBusId bus) => _busLive[(int)bus];

    public int LoopRequests
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _loops.Length; i++) if (_loops[i].Active) n++;
            return n;
        }
    }

    public int LoopsSounding
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _loops.Length; i++) if (_loops[i].Active && _loops[i].Voice >= 0) n++;
            return n;
        }
    }

    // ── One-shots ───────────────────────────────────────────────────────────────────
    public PostResult Post(SfxDef def, SfxClip clip, float volumeDb, float pitch, double now)
    {
        // 1. A volley is counted before anything else: most of its posts fall inside the
        //    cooldown, and they are exactly what must be counted.
        if (def.BurstCount > 0)
        {
            if (now - def.BurstStart > def.BurstWindow)
            {
                def.BurstStart = now;
                def.BurstSeen = 0;
                def.BurstFired = false;
            }
            def.BurstSeen++;
            if (def.BurstSeen >= def.BurstCount)
            {
                if (def.BurstFired) { DroppedBurst++; return PostResult.BurstDropped; }
                def.BurstFired = true;
                Bursts++;
                return PostResult.Burst;
            }
        }

        // 2. Cooldown.
        if (now - def.LastPostAt < def.Cooldown) { DroppedCooldown++; return PostResult.Cooldown; }

        // 3. The row's own cap.
        if (def.LiveVoices >= def.MaxVoices)
        {
            int oldest = def.Steal == StealMode.Oldest ? OldestOf(def) : -1;
            if (oldest < 0) { DroppedCap++; return PostResult.Capped; }
            Steal(oldest);
        }

        // 4. The bus's cap.
        int bus = (int)def.Bus;
        if (_busLive[bus] >= _busCap[bus])
        {
            int victim = WeakestOn(def.Bus);
            if (victim < 0 || _voices[victim].Priority > def.Priority
                || (_voices[victim].Priority >= NeverStolen && def.Priority < _voices[victim].Priority))
            {
                DroppedCap++;
                return PostResult.Capped;
            }
            Steal(victim);
        }

        int slot = FreeSlot();
        if (slot < 0) { DroppedCap++; return PostResult.Capped; }
        long handle = _backend.Play(def.Bus, clip, volumeDb, pitch);
        if (handle < 0) { Refused++; return PostResult.Refused; }

        def.LastPostAt = now;
        ref Voice v = ref _voices[slot];
        v = new Voice
        {
            Active = true, Handle = handle, Def = def, Bus = def.Bus, StartedAt = now,
            EndsAt = now + clip.Seconds / Math.Max(0.05f, pitch) + 0.03,
            VolumeDb = volumeDb, TargetDb = volumeDb, Priority = def.Priority, Request = -1,
        };
        Count(def, +1);
        Played++;
        return PostResult.Played;
    }

    // ── Loops ───────────────────────────────────────────────────────────────────────
    /// <summary>Ask for a loop. The same source + row asked twice is one request.</summary>
    public void StartLoop(SfxDef def, object source, int mediumIndex, int caseIndex, float volumeDb, float pitch, double now)
    {
        int free = -1;
        for (int i = 0; i < _loops.Length; i++)
        {
            if (_loops[i].Active)
            {
                if (ReferenceEquals(_loops[i].Def, def) && ReferenceEquals(_loops[i].Source, source)) return;
            }
            else if (free < 0) free = i;
        }
        if (free < 0) return;
        _loops[free] = new LoopRequest
        {
            Active = true, Def = def, Source = source, MediumIndex = mediumIndex, CaseIndex = caseIndex,
            VolumeDb = volumeDb, Pitch = pitch, Voice = -1, Since = now,
        };
    }

    public void StopLoop(SfxDef def, object source)
    {
        for (int i = 0; i < _loops.Length; i++)
            if (_loops[i].Active && ReferenceEquals(_loops[i].Def, def) && ReferenceEquals(_loops[i].Source, source))
                EndRequest(i);
    }

    /// <summary>End every loop a bus carries (a scene change), or every loop there is.</summary>
    public void StopLoops(AudioBusId? bus = null)
    {
        for (int i = 0; i < _loops.Length; i++)
            if (_loops[i].Active && (bus == null || _loops[i].Def.Bus == bus)) EndRequest(i);
    }

    public bool IsLooping(SfxDef def, object source)
    {
        for (int i = 0; i < _loops.Length; i++)
            if (_loops[i].Active && ReferenceEquals(_loops[i].Def, def) && ReferenceEquals(_loops[i].Source, source)) return true;
        return false;
    }

    private void EndRequest(int index)
    {
        int voice = _loops[index].Voice;
        _loops[index] = default;
        _loops[index].Voice = -1;
        if (voice < 0 || !_voices[voice].Active) return;
        ref Voice v = ref _voices[voice];
        v.Stopping = true;
        v.Request = -1;
        v.TargetDb = -60f;
        v.FadePerSecond = (v.VolumeDb + 60f) / Math.Max(0.02f, v.Def.FadeOut);
    }

    // ── Every frame ─────────────────────────────────────────────────────────────────
    /// <summary>Free the voices that have ended, step the fades, drop the loops whose source
    /// is gone, and let waiting loops take free voices.</summary>
    public void Update(double now, float dt, Func<object, bool> sourceAlive = null)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            ref Voice v = ref _voices[i];
            if (!v.Active) continue;
            if (!v.Loop && now >= v.EndsAt)
            {
                // Tell the engine too: its voice ends in ITS time, and a slot it still holds is
                // a slot the next sound is refused (found by the check running a fast clock).
                _backend.Stop(v.Bus, v.Handle);
                Release(i);
                continue;
            }
            if (v.VolumeDb != v.TargetDb)
            {
                float step = v.FadePerSecond * dt;
                v.VolumeDb = v.VolumeDb < v.TargetDb ? Math.Min(v.TargetDb, v.VolumeDb + step) : Math.Max(v.TargetDb, v.VolumeDb - step);
                _backend.SetVolume(v.Bus, v.Handle, v.VolumeDb);
            }
            if (v.Stopping && v.VolumeDb <= -59.5f)
            {
                _backend.Stop(v.Bus, v.Handle);
                Release(i);
            }
        }

        for (int i = 0; i < _loops.Length; i++)
        {
            if (!_loops[i].Active) continue;
            if (sourceAlive != null && _loops[i].Source != null && !ReferenceEquals(_loops[i].Source, _loops[i].Def)
                && !sourceAlive(_loops[i].Source))
            {
                EndRequest(i);
                continue;
            }
            if (_loops[i].Voice < 0) TrySound(i, now);
        }
    }

    private void TrySound(int index, double now)
    {
        ref LoopRequest r = ref _loops[index];
        SfxDef def = r.Def;
        SfxSet set = def.Sets[r.MediumIndex];
        if (set == null) return;                               // its sound is still being made
        if (def.LiveVoices >= def.MaxVoices) return;           // the row is full: wait
        int bus = (int)def.Bus;
        if (_busLive[bus] >= _busCap[bus])
        {
            int victim = WeakestOn(def.Bus);
            if (victim < 0 || _voices[victim].Priority >= def.Priority) return;
            Steal(victim);
        }
        int slot = FreeSlot();
        if (slot < 0) return;
        SfxClip clip = set.Cases[Math.Min(r.CaseIndex, set.Cases.Length - 1)][0];
        float from = r.VolumeDb - 40f;
        long handle = _backend.Play(def.Bus, clip, from, r.Pitch);
        if (handle < 0) { Refused++; return; }
        _voices[slot] = new Voice
        {
            Active = true, Handle = handle, Def = def, Bus = def.Bus, StartedAt = now, EndsAt = double.PositiveInfinity,
            VolumeDb = from, TargetDb = r.VolumeDb, FadePerSecond = 40f / Math.Max(0.02f, def.FadeIn),
            Priority = def.Priority, Loop = true, Request = index,
        };
        r.Voice = slot;
        Count(def, +1);
        Played++;
    }

    // ── Bookkeeping ─────────────────────────────────────────────────────────────────
    private int FreeSlot()
    {
        for (int i = 0; i < _voices.Length; i++) if (!_voices[i].Active) return i;
        return -1;
    }

    private int OldestOf(SfxDef def)
    {
        int best = -1;
        for (int i = 0; i < _voices.Length; i++)
            if (_voices[i].Active && ReferenceEquals(_voices[i].Def, def) && (best < 0 || _voices[i].StartedAt < _voices[best].StartedAt)) best = i;
        return best;
    }

    /// <summary>The voice a bus would give up first: lowest priority, then quietest, then oldest.</summary>
    private int WeakestOn(AudioBusId bus)
    {
        int best = -1;
        for (int i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active || _voices[i].Bus != bus) continue;
            if (best < 0) { best = i; continue; }
            ref Voice a = ref _voices[i];
            ref Voice b = ref _voices[best];
            if (a.Priority < b.Priority
                || (a.Priority == b.Priority && (a.VolumeDb < b.VolumeDb - 0.01f
                    || (Math.Abs(a.VolumeDb - b.VolumeDb) <= 0.01f && a.StartedAt < b.StartedAt)))) best = i;
        }
        return best;
    }

    private void Steal(int slot)
    {
        _backend.Stop(_voices[slot].Bus, _voices[slot].Handle);
        Stolen++;
        Release(slot);
    }

    private void Release(int slot)
    {
        ref Voice v = ref _voices[slot];
        if (v.Request >= 0 && _loops[v.Request].Active && _loops[v.Request].Voice == slot) _loops[v.Request].Voice = -1;
        Count(v.Def, -1);
        v = default;
    }

    private void Count(SfxDef def, int delta)
    {
        def.LiveVoices += delta;
        _busLive[(int)def.Bus] += delta;
        LiveVoices += delta;
        if (LiveVoices > PeakVoices) PeakVoices = LiveVoices;
    }

    /// <summary>Silence everything now (leaving a scene, or a check between stages).</summary>
    public void StopAll()
    {
        for (int i = 0; i < _loops.Length; i++) { _loops[i] = default; _loops[i].Voice = -1; }
        for (int i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active) continue;
            _backend.Stop(_voices[i].Bus, _voices[i].Handle);
            Release(i);
        }
    }
}

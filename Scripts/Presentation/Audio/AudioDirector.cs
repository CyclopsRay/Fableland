using System;
using System.Collections.Generic;
using Godot;
using Fableland.Audio.Synth;
using Fableland.Data;

namespace Fableland.Audio;

/// <summary>Mix states (<c>Docs/Audio.gdd</c> §9): snapshots of bus level and low-pass that
/// stack — the lowest cutoff wins, the level offsets add.</summary>
[Flags]
public enum MixState
{
    None = 0,
    Paused = 1 << 0,
    Underwater = 1 << 1,
    LowHp = 1 << 2,
    Void = 1 << 3,
    Defeat = 1 << 4,
}

/// <summary>
/// The audio director (<c>Docs/Audio.gdd</c>) — a PRESENTATION autoload, the third of the
/// directors gameplay never names (feel, VFX, audio). It listens to the cue bus and turns
/// every cue into sound through <see cref="SfxTable"/>; it keeps the context blackboard; it
/// runs the music. It never calls gameplay and gameplay never calls it; nothing here can
/// change a fight; its randomness is its own.
///
/// There are no audio assets. Every sound and every piece of music is synthesised from data
/// at load, off the main thread, and cached per realm and per character
/// (<see cref="AudioCache"/>). A file named after an event replaces its recipe
/// (<see cref="AudioFiles"/>).
///
/// Justification as an autoload (T10 §1): like the feel director, it must outlive every
/// scene — the music plays across scene changes and the cue bus it listens to is static.
/// It is dormant in a headless run (the checks play fights at 8× and must not be given
/// sound to make); <see cref="Activate"/> wakes it for the audio check and the dump.
/// Raising a cue allocates nothing here (T40 §2).
/// </summary>
public partial class AudioDirector : Node
{
    public static AudioDirector Instance { get; private set; }

    public bool Active { get; private set; }
    public readonly AudioBlackboard Board = new();
    public VoiceManager Voices { get; private set; }
    public AudioCache Cache { get; private set; }
    public MusicEngine Music { get; private set; }
    public GodotVoices Backend { get; private set; }
    public MusicClock Clock => Music?.Clock;
    /// <summary>The medium of the realm being played in (home, between fights).</summary>
    public Medium Realm { get; private set; } = Medium.Home;
    public MusicProfile Profile { get; private set; }
    public MixState Mix { get; private set; }

    // ── Dials for the audition page and the checks ──────────────────────────────────
    /// <summary>Play every row that is not fixed to a medium in this one.</summary>
    public Medium ForceMedium;
    /// <summary>Hold the music's intensity here; negative = follow the game.</summary>
    public float IntensityOverride = -1f;
    /// <summary>Mix states switched on by hand.</summary>
    public MixState ForcedMix;
    /// <summary>A clock to run on instead of the system's (a check stepping time).</summary>
    public Func<double> TimeSource
    {
        get => _timeSource;
        set { _timeSource = value; if (Music != null) Music.NoLead = value != null; }
    }

    private Func<double> _timeSource;

    // ── Counters ────────────────────────────────────────────────────────────────────
    public int CuesHeard, HitsHeard, Unresolved, LatePlayed, LateDropped, StingersHeld, StingersSkipped;
    /// <summary>The moment the last quantized stinger was (or will be) posted, and the
    /// moment on the music's clock it was aimed at.</summary>
    public double LastStingerPostAt, LastStingerHeardAt;

    public float Intensity => IntensityOverride >= 0f ? IntensityOverride : Board.Intensity;
    public int KeyTonic => _keyTonic;
    public int[] KeyScale => _keyScale;

    /// <summary>Seconds a post waits for its sound to finish rendering before it is dropped.</summary>
    public const double LateSeconds = 0.3;
    private static readonly float[] WeightDb = { 0f, -8f, -2f, 0f, 1.5f, 3f };     // by (int)HitWeight
    private static readonly Func<object, bool> SourceAlive =
        source => source is not GodotObject o || (GodotObject.IsInstanceValid(o) && (o is not Node n || n.IsInsideTree()));

    private struct Pending
    {
        public bool Active, WaitForSet;
        public SfxDef Def;
        public int MediumIndex, CaseIndex;
        public float Db, Pitch;
        public double FireAt, Expires;
    }

    private readonly Pending[] _pending = new Pending[16];
    private SynthRng _rng = new((ulong)System.Diagnostics.Stopwatch.GetTimestamp());
    private Node2D _playerBody;
    private double _lastNow, _pollAt, _stingerUntil, _defeatUntil;
    private int _stingerPriority;
    private int _keyTonic = Medium.Home.Tonic;
    private int[] _keyScale = Medium.Home.Scale;
    private int _musicSerial, _bounces;
    private SfxClip _layerWaiting;
    private bool _paused, _inArena, _heartbeat;
    private float _musicHz = 20000f, _sfxHz = 20000f, _musicDb, _sfxDb;
    private SfxDef _ambience, _heart, _comboNote, _comboResolve, _hitHeavy;

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;      // the music and the UI play through a pause
        // Paired with _ExitTree — the bus is static and outlives everything (T10 §1).
        CueBus.Raised += OnCue;
        CueBus.HitLanded += OnHit;
        CueBus.LoopStarted += OnLoopStart;
        CueBus.LoopStopped += OnLoopStop;
    }

    public override void _ExitTree()
    {
        CueBus.Raised -= OnCue;
        CueBus.HitLanded -= OnHit;
        CueBus.LoopStarted -= OnLoopStart;
        CueBus.LoopStopped -= OnLoopStop;
        Cache?.Shutdown();
        if (_trace != null)
        {
            var keys = new System.Collections.Generic.List<string>(_trace.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys) GD.Print($"[AudioDirector] heard {_trace[key],5} × {key}");
            GD.Print($"[AudioDirector] {CuesHeard} cues and {HitsHeard} hits heard · {Unresolved} resolved to no sound · {Voices.Played} voices played, " +
                     $"{Voices.Stolen} stolen, {Voices.Refused} refused, {Voices.PeakVoices} at once at most · {LatePlayed} played late, {LateDropped} dropped late · " +
                     $"{Cache.ClipsRendered} clips rendered in {Cache.RenderSeconds:0.00} s of worker time");
        }
        if (Instance == this) Instance = null;
    }

    /// <summary>Environment variable that wakes the director where it would stay dormant:
    /// <c>on</c> makes sound in a headless run (a rehearsal of a run with the audio layer
    /// working), <c>trace</c> also prints what it heard when the game quits.</summary>
    public const string EnvVar = "FABLELAND_AUDIO";
    private System.Collections.Generic.Dictionary<string, int> _trace;

    public override void _Ready()
    {
        SetProcess(false);
        string asked = OS.GetEnvironment(EnvVar);
        bool forced = asked == "on" || asked == "trace";
        // No sound card, nobody to hear: stay dormant. (Verified by running: a headless run
        // reports the display "headless" and the audio driver "Dummy".)
        if (!forced && (DisplayServer.GetName() == "headless" || AudioServer.GetDriverName() == "Dummy")) return;
        Activate();
        if (asked == "trace") _trace = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
        Cache.WarmCommon();
        foreach (string owner in Medium.KnownOwners) Cache.WarmOwner(owner, Medium.Home);
    }

    /// <summary>Build the buses, the players and the cache, and start listening for real.
    /// Idempotent. <paramref name="keepPcm"/> keeps every rendered sound's samples in managed
    /// memory so a check can measure them.</summary>
    public void Activate(bool keepPcm = false)
    {
        if (Active) { if (keepPcm) Cache.KeepPcm = true; return; }
        AudioBuses.Ensure();
        Backend = new GodotVoices(this);
        Voices = new VoiceManager(Backend);
        Cache = new AudioCache { KeepPcm = keepPcm, MusicReady = OnMusicReady, LayerReady = OnLayerReady };
        Music = new MusicEngine(this) { NoLead = _timeSource != null };
        _ambience = SfxTable.Cues.Resolve(CueOwners.World, null, SfxTable.Ambience);
        _heart = SfxTable.Cues.Resolve(CueOwners.Any, null, SfxTable.Heartbeat);
        _comboNote = SfxTable.Cues.Resolve(CueOwners.Any, null, SfxTable.ComboNote);
        _comboResolve = SfxTable.Cues.Resolve(CueOwners.Any, null, SfxTable.ComboResolve);
        _hitHeavy = SfxTable.Cues.Resolve(CueOwners.Any, null, SfxTable.HitHeavy);
        _lastNow = Now();
        Active = true;
        SetProcess(true);
        if (OS.IsDebugBuild())
            foreach (string problem in AudioSelfTest.Table()) GD.PushError("[SfxTable] " + problem);
    }

    /// <summary>Real seconds: sound is paced for the ear, whatever the engine's time scale.</summary>
    public double Now() => _timeSource != null ? _timeSource() : Time.GetTicksUsec() * 1e-6;

    // ── Every frame ─────────────────────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        if (!Active) return;
        double now = Now();
        float dt = (float)Math.Clamp(now - _lastNow, 0.0, 0.1);
        _lastNow = now;

        Cache.Pump();
        Voices.Update(now, dt, SourceAlive);
        if (now >= _pollAt) PollPlayer(now);
        if (Board.State == MusicState.Combat || Board.State == MusicState.Boss) Board.Update(dt);
        Music.Update(now, dt, Intensity);
        FireDue(now);
        UpdateMix(now, dt);
    }

    /// <summary>What the bus cannot say in a cue: whether the body the player wears is low.
    /// Read a few times a second through the narrow view presentation is given of a body.</summary>
    private void PollPlayer(double now)
    {
        _pollAt = now + 0.12;
        if (_playerBody == null) return;
        if (!GodotObject.IsInstanceValid(_playerBody) || !_playerBody.IsInsideTree()) { _playerBody = null; return; }
        if (_playerBody is not IFeedbackBody body) return;
        FeedbackState state = body.FeedbackState;
        Board.PlayerHp = (state & FeedbackState.Dead) != 0 ? 0f : (state & FeedbackState.LowHp) != 0 ? AudioBlackboard.LowHpShare : 1f;
    }

    // ── Cues ────────────────────────────────────────────────────────────────────────
    private void OnCue(Cue cue)
    {
        if (!Active) return;
        CuesHeard++;
        if (_trace != null && (cue.Owner == CueOwners.Flow || cue.Owner == CueOwners.Ui))
        {
            // Tracing only (it allocates): which screen and flow cues a run really raised.
            string key = cue.Owner + "/" + cue.Action + (string.IsNullOrEmpty(cue.Tag) ? "" : ":" + cue.Tag);
            _trace[key] = _trace.GetValueOrDefault(key) + 1;
        }
        double now = Now();
        Hear(cue, now);
        SfxDef def = SfxTable.ForCue(cue.Owner, cue.OwnerClass, cue.Action);
        if (def == null) { Unresolved++; return; }
        if (def.Loop) return;
        FeedbackState state = cue.Source is IFeedbackBody body ? body.FeedbackState : FeedbackState.None;
        bool players = IsPlayers(cue);
        Play(def, cue.Owner, cue.Variant, cue.Tag, cue.Magnitude, players, state, now, 0f, SfxTable.SizeSt(cue.Owner));
        // A layer the cue's tag asks for: her slash, burning, is her slash and a fizz of fire.
        if (def.Layer != null && cue.Tag != null && cue.Tag == def.LayerTag)
            Play(def.Layer, cue.Owner, 0, null, 0f, players, FeedbackState.None, now, 0f, 0f);
    }

    private bool IsPlayers(in Cue cue) =>
        (cue.Source != null && ReferenceEquals(cue.Source, _playerBody))
        || (cue.Side == CombatSide.Player && (cue.Owner == CueOwners.Hazard || cue.Owner == CueOwners.Flow || cue.Owner == CueOwners.Ui));

    /// <summary>Keep the blackboard, and let the flow of the game move the music.</summary>
    private void Hear(in Cue cue, double now)
    {
        string action = cue.Action;
        if (cue.OwnerClass == CueOwners.Fighter && cue.Side == CombatSide.Player && cue.Source != null)
        {
            _playerBody = cue.Source;
            Board.PlayerOwner = cue.Owner;
            if (action == "spawn" || action == "respawn" || action == "switch.in") Board.PlayerHp = 1f;
            else if (action == "death") Board.PlayerHp = 0f;
        }
        else if (cue.OwnerClass == CueOwners.Foe)
        {
            if (action == "spawn") Board.Foes++;
            else if (action == "death") Board.Foes = Math.Max(0, Board.Foes - 1);
        }

        if (cue.Owner == CueOwners.Flow) HearFlow(cue, now);
        else if (cue.Owner == CueOwners.World)
        {
            if (cue.Tag == "water" && cue.Source != null && ReferenceEquals(cue.Source, _playerBody))
                Board.Submerged = action == "soft.enter";
        }
        else if (cue.Owner == CueOwners.Ui)
        {
            if (action == "pause.open") _paused = true;
            else if (action == "pause.close") _paused = false;
        }
    }

    private void HearFlow(in Cue cue, double now)
    {
        switch (cue.Action)
        {
            case "arena.enter":
            {
                MusicProfile profile = MusicProfiles.ForArena(cue.Tag, cue.Variant);
                EnterRealm(profile.Medium);
                Board.EnterArena(profile.Id, cue.Variant);
                Board.State = MusicState.Combat;
                _inArena = true;
                _paused = false;
                _defeatUntil = 0;
                Voices.StopLoops();
                SetProfile(profile);
                if (_ambience != null) Voices.StartLoop(_ambience, _ambience, Realm.Index, 0, _ambience.VolumeDb, 1f, now);
                break;
            }
            case "mission.start":
                Board.Mission = cue.Tag ?? "";
                Board.Fighting = true;
                break;
            case "mission.time":
                Board.MissionTimeLeft = cue.Variant;
                break;
            case "boss.intro":
                Board.Boss = true;
                Board.BossId = cue.Tag ?? "";
                Board.State = MusicState.Boss;
                if (Profile != null) Cache.RequestBossLayer(Profile, Board.BossId, NextSeed(Profile.Id), _musicSerial);
                break;
            case "boss.defeated":
                Board.Boss = false;
                EndFight(MusicState.Victory, 0.5f);
                break;
            case "mission.success":
                EndFight(MusicState.Victory, 0.5f);
                break;
            case "mission.fail":
                EndFight(MusicState.Defeat, 0.8f);
                break;
            case "player.down":
                EndFight(MusicState.Defeat, 1.5f);
                _defeatUntil = now + 1.6;      // the music sinks under a closing filter
                break;
            case "menu.enter": EnterScreen(MusicState.Menu, MusicProfiles.Get(MusicProfiles.Menu)); break;
            case "map.enter":
                Board.Void = cue.Tag == "VOID" ? 1f : Math.Clamp((cue.Variant - 1) / 44f, 0f, 1f);
                EnterScreen(MusicState.Overworld, cue.Tag == "VOID" ? MusicProfiles.Get("VOID") : MusicProfiles.Get(MusicProfiles.Map));
                break;
            case "shelter.enter": EnterScreen(MusicState.Shelter, MusicProfiles.Get(MusicProfiles.Shelter)); break;
            case "event.enter": EnterScreen(MusicState.Event, MusicProfiles.Get(MusicProfiles.Event)); break;
            case "runover.enter":
                EnterScreen(cue.Tag == "victory" ? MusicState.Victory : MusicState.Defeat, null);
                break;
        }
    }

    private void EndFight(MusicState state, float fadeSeconds)
    {
        if (Board.State != MusicState.Combat && Board.State != MusicState.Boss) return;
        Board.State = state;
        Board.Fighting = false;
        _musicSerial++;                     // a bounce still on its way is no longer wanted
        Music.Stop(fadeSeconds);
        Profile = null;
    }

    /// <summary>A screen between fights: the arena's loops and mix states end, the storybook
    /// is the realm again, and the screen's own music plays at its resting intensity.</summary>
    private void EnterScreen(MusicState state, MusicProfile profile)
    {
        _inArena = false;
        _paused = false;
        _defeatUntil = 0;
        _playerBody = null;
        Board.State = state;
        Board.Fighting = false;
        Board.Boss = false;
        Board.Submerged = false;
        Board.PlayerHp = 1f;
        Board.MissionTimeLeft = -1f;
        Voices.StopLoops();
        if (profile != null && profile.RestIntensity >= 0f)
            Board.SetIntensity(Math.Min(1f, profile.RestIntensity + (state == MusicState.Overworld ? 0.3f * Board.Void : 0f)));
        else if (profile != null) Board.SetIntensity(0.25f);
        SetProfile(profile);
    }

    private void EnterRealm(Medium medium)
    {
        if (medium != Realm)
        {
            Cache.EvictRealm(Realm);
            Realm = medium;
        }
        Cache.WarmRealm(medium);
    }

    private ulong NextSeed(string id) => SynthRng.Hash(id) ^ ((ulong)(++_bounces) * 0x9E3779B97F4A7C15UL) ^ _rng.NextU64();

    /// <summary>Change what the music is. The key changes at once (sounds land in the new
    /// key from this moment); the stems follow as soon as they are bounced.</summary>
    public void SetProfile(MusicProfile profile, ulong? seed = null)
    {
        if (profile == null)
        {
            _musicSerial++;
            Profile = null;
            Music.Stop(0.8f);
            return;
        }
        _keyTonic = profile.Tonic;
        _keyScale = profile.Scale;
        if (Profile == profile && seed == null && (Music.Current?.Profile == profile || Cache.Pending > 0)) return;
        Profile = profile;
        _layerWaiting = null;
        Cache.RequestMusic(profile, seed ?? NextSeed(profile.Id), ++_musicSerial);
    }

    private void OnMusicReady(MusicSet set, int serial)
    {
        if (serial != _musicSerial || !Active) return;
        double now = Now();
        Music.Play(set, Intensity, now);
        if (_layerWaiting != null) { Music.StartOverlay(_layerWaiting, now); _layerWaiting = null; }
    }

    private void OnLayerReady(SfxClip layer, int serial)
    {
        if (serial != _musicSerial || !Board.Boss) return;
        if (Music.Clock.Running && Music.Current?.Profile == Profile) Music.StartOverlay(layer, Now());
        else _layerWaiting = layer;
    }

    // ── Hits: what struck, what was struck, and the note it plays ───────────────────
    private void OnHit(HitCue hit)
    {
        if (!Active) return;
        HitsHeard++;
        double now = Now();
        IFeedbackBody victim = hit.VictimBody;
        bool onPlayer = hit.Victim != null && ReferenceEquals(hit.Victim, _playerBody);
        bool byPlayerSide = victim != null && victim.Side == CombatSide.Foe;
        FeedbackState state = victim != null ? victim.FeedbackState : FeedbackState.None;
        float weightDb = WeightDb[Math.Clamp((int)hit.Weight, 0, WeightDb.Length - 1)];

        // What struck — in the attacker's medium. An unnamed hit on a foe is the player's.
        SfxDef attack = SfxTable.ForAttack(hit.AttackOwner, hit.AttackClass, hit.AttackAction);
        string attackOwner = hit.AttackOwner;
        if (attackOwner == CueOwners.Any && byPlayerSide) attackOwner = Board.PlayerOwner;
        // A heavier blow is louder and a little lower.
        float weightSt = hit.Weight > HitWeight.Medium ? -0.4f * ((int)hit.Weight - (int)HitWeight.Medium) : 0f;
        if (attack != null) Play(attack, attackOwner, hit.AttackVariant, null, 0f, true, FeedbackState.None, now, weightDb, weightSt + SfxTable.SizeSt(attackOwner));
        if (hit.Weight >= HitWeight.Heavy && _hitHeavy != null)
            Play(_hitHeavy, attackOwner, 0, null, 0f, true, FeedbackState.None, now, hit.Weight == HitWeight.Massive ? 2f : 0f, 0f);

        // What was struck — in the victim's.
        if (hit.Weight > HitWeight.Graze)
        {
            SfxDef taken = SfxTable.ForCue(hit.VictimOwner, hit.VictimClass, SfxTable.HitTaken);
            if (taken != null) Play(taken, hit.VictimOwner, 0, null, 0f, onPlayer, state, now, weightDb * 0.5f, SfxTable.SizeSt(hit.VictimOwner));
        }

        // And the music: the player's hits climb the realm's scale; a kill is the tonic.
        if (onPlayer) Board.PlayerHurt(hit.Damage);
        else if (byPlayerSide)
        {
            int step = Board.PlayerHit(hit.Damage, now);
            if (hit.Killed)
            {
                Board.Resolve();
                if (_comboResolve != null) Play(_comboResolve, Board.PlayerOwner, 0, null, 0f, true, FeedbackState.None, now, 0f, 0f);
            }
            else if (step > 0 && _comboNote != null)
            {
                Play(_comboNote, Board.PlayerOwner, 0, null, 0f, true, FeedbackState.None, now, 0f, 0f, step);
            }
        }
    }

    // ── Loops ───────────────────────────────────────────────────────────────────────
    private void OnLoopStart(Cue cue)
    {
        if (!Active) return;
        SfxDef def = SfxTable.ForCue(cue.Owner, cue.OwnerClass, cue.Action);
        if (def == null) { Unresolved++; return; }
        if (!def.Loop) return;
        Medium medium = MediumFor(def, cue.Owner);
        if (def.Sets[medium.Index] == null) Cache.Request(def, medium);
        float db = def.VolumeDb + (IsPlayers(cue) ? 0f : def.OthersDb);
        Voices.StartLoop(def, (object)cue.Source ?? def, medium.Index, def.CaseIndex(def.StageOf(cue.Variant), def.TagCase(cue.Tag)), db, 1f, Now());
    }

    private void OnLoopStop(Cue cue)
    {
        if (!Active) return;
        SfxDef def = SfxTable.ForCue(cue.Owner, cue.OwnerClass, cue.Action);
        if (def == null || !def.Loop) return;
        Voices.StopLoop(def, (object)cue.Source ?? def);
    }

    // ── Playing a row ───────────────────────────────────────────────────────────────
    /// <summary>Whose medium renders this row for this owner.</summary>
    public Medium MediumFor(SfxDef def, string owner)
    {
        if (def.From == MediumFrom.Fixed) return Medium.Get(def.FixedMedium) ?? Medium.Home;
        if (ForceMedium != null) return ForceMedium;
        return def.From == MediumFrom.Realm ? Realm : Medium.HomeFor(owner) ?? Realm;
    }

    /// <summary>The semitones a row is moved by when it plays: jitter, state, and — for a
    /// tonal row — the realm's key, a scale degree, or the combo's ladder.</summary>
    public float PitchFor(SfxDef def, Medium medium, int variant, int ladderStep, float extraSt)
    {
        float st = extraSt;
        if (def.Pitch == PitchMode.Key)
            st += Dsp.NearestShift(medium.Tonic, _keyTonic) + Dsp.ScaleSemis(_keyScale, def.Degree + def.DegreePerVariant * (Math.Max(1, variant) - 1));
        else if (def.Pitch == PitchMode.Ladder)
            st += Dsp.NearestShift(medium.Tonic, _keyTonic) + AudioBlackboard.LadderSemis(_keyScale, ladderStep) - SfxTable.LadderCentre;
        return st;
    }

    private void Play(SfxDef def, string owner, int variant, string tag, float magnitude, bool players, FeedbackState state,
                      double now, float extraDb, float extraSt, int ladderStep = 0)
    {
        Medium medium = MediumFor(def, owner);
        float db = def.VolumeDb + extraDb;
        float st = PitchFor(def, medium, variant, ladderStep, extraSt);
        if (def.VolumeJitter > 0f) db += _rng.Signed() * def.VolumeJitter;
        if (def.PitchJitter > 0f) st += _rng.Signed() * def.PitchJitter;
        if (def.MagnitudeRef > 0f && magnitude > 0f)
            db += Math.Clamp(def.MagnitudeDb * MathF.Log2(magnitude / def.MagnitudeRef), -def.MagnitudeRange, 3f);
        if (!players) db += def.OthersDb;
        if (def.Mods.Length > 0)
        {
            BoardFlag flags = Board.Flags;
            for (int i = 0; i < def.Mods.Length; i++)
            {
                StateMod mod = def.Mods[i];
                if ((state & mod.State) == 0 && (flags & mod.Board) == 0) continue;
                db += mod.VolumeDb;
                st += mod.PitchSt;
            }
        }
        Post(def, medium.Index, def.CaseIndex(def.StageOf(variant), def.TagCase(tag)), db, Dsp.Ratio(st), now);
    }

    private void Post(SfxDef def, int mediumIndex, int caseIndex, float db, float pitch, double now)
    {
        SfxSet set = def.Sets[mediumIndex];
        if (set == null)
        {
            // Its sound is still being made: ask, and try again for a moment.
            Cache.Request(def, Medium.All[mediumIndex]);
            Hold(def, mediumIndex, caseIndex, db, pitch, now, now + LateSeconds, true);
            return;
        }
        if (def.Quantize != Quantize.None && Music.Clock.Running)
        {
            // Aim at the music's grid in HEARD time, and post that much earlier.
            double lead = Music.Lead;
            double heard = Music.Clock.Quantized(now + lead, def.Quantize);
            double postAt = heard - lead;
            LastStingerHeardAt = heard;
            LastStingerPostAt = postAt;
            if (postAt - now > 0.008)
            {
                if (!Gate(def, set, caseIndex, postAt)) return;
                StingersHeld++;
                Hold(def, mediumIndex, caseIndex, db, pitch, postAt, postAt + 1.0, false);
                return;
            }
        }
        if (!Gate(def, set, caseIndex, now)) return;
        Fire(def, set, mediumIndex, caseIndex, db, pitch, now);
    }

    /// <summary>One stinger at a time on the music bus: while one sounds, a lesser one is
    /// not played over it (a boss's fall is not followed by "mission success").</summary>
    private bool Gate(SfxDef def, SfxSet set, int caseIndex, double at)
    {
        if (def.Bus != AudioBusId.Music) return true;
        if (at < _stingerUntil && def.Priority < _stingerPriority) { StingersSkipped++; return false; }
        _stingerUntil = at + set.Cases[Math.Min(caseIndex, set.Cases.Length - 1)][0].Seconds;
        _stingerPriority = def.Priority;
        return true;
    }

    private void Fire(SfxDef def, SfxSet set, int mediumIndex, int caseIndex, float db, float pitch, double now)
    {
        caseIndex = Math.Min(caseIndex, set.Cases.Length - 1);
        PostResult result = Voices.Post(def, set.Next(caseIndex, ref _rng), db, pitch, now);
        if (result == PostResult.Burst && def.BurstDef != null) Post(def.BurstDef, mediumIndex, 0, db, pitch, now);
        else if (result == PostResult.Played && def.DuckSeconds > 0f) Music.Duck(def.DuckDb, def.DuckSeconds);
    }

    private void Hold(SfxDef def, int mediumIndex, int caseIndex, float db, float pitch, double fireAt, double expires, bool waitForSet)
    {
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i].Active) continue;
            _pending[i] = new Pending
            {
                Active = true, WaitForSet = waitForSet, Def = def, MediumIndex = mediumIndex, CaseIndex = caseIndex,
                Db = db, Pitch = pitch, FireAt = fireAt, Expires = expires,
            };
            return;
        }
        LateDropped++;
    }

    private void FireDue(double now)
    {
        for (int i = 0; i < _pending.Length; i++)
        {
            if (!_pending[i].Active) continue;
            Pending p = _pending[i];
            SfxSet set = p.Def.Sets[p.MediumIndex];
            if (now > p.Expires) { _pending[i].Active = false; LateDropped++; continue; }
            if (set == null || now < p.FireAt) continue;
            _pending[i].Active = false;
            if (p.WaitForSet)
            {
                LatePlayed++;
                Post(p.Def, p.MediumIndex, p.CaseIndex, p.Db, p.Pitch, now);
            }
            else Fire(p.Def, set, p.MediumIndex, p.CaseIndex, p.Db, p.Pitch, now);
        }
    }

    // ── The audition page's direct line (Docs/Audio.gdd §13) ────────────────────────
    /// <summary>Play one variant of one case of a row exactly as rendered: made now if it
    /// has not been, no jitter, no cooldown.</summary>
    public bool Audition(SfxDef def, Medium medium, int caseIndex, int variant)
    {
        if (!Active || def == null || def.Loop) return false;
        SfxSet set = Cache.RenderNow(def, medium);
        SfxClip[] clips = set.Cases[Math.Clamp(caseIndex, 0, set.Cases.Length - 1)];
        def.LastPostAt = double.NegativeInfinity;
        float st = PitchFor(def, medium, 1, 1, 0f);
        return Voices.Post(def, clips[Math.Clamp(variant, 0, clips.Length - 1)], def.VolumeDb, Dsp.Ratio(st), Now()) == PostResult.Played;
    }

    /// <summary>Start a loop row, or stop it if it is sounding.</summary>
    public void AuditionLoop(SfxDef def, Medium medium, int caseIndex)
    {
        if (!Active || def == null || !def.Loop) return;
        if (Voices.IsLooping(def, def)) { Voices.StopLoop(def, def); return; }
        Cache.RenderNow(def, medium);
        Voices.StartLoop(def, def, medium.Index, caseIndex, def.VolumeDb, 1f, Now());
    }

    // ── Mix states ──────────────────────────────────────────────────────────────────
    private void UpdateMix(double now, float dt)
    {
        MixState want = ForcedMix;
        if (_paused) want |= MixState.Paused;
        if (_inArena && Board.Submerged) want |= MixState.Underwater;
        if (_inArena && (Board.Flags & BoardFlag.LowHp) != 0) want |= MixState.LowHp;
        if (Board.State == MusicState.Overworld && Board.Void > 0.02f) want |= MixState.Void;
        if (now < _defeatUntil) want |= MixState.Defeat;
        Mix = want;

        bool heart = (want & MixState.LowHp) != 0;
        if (heart != _heartbeat && _heart != null)
        {
            _heartbeat = heart;
            if (heart)
            {
                if (_heart.Sets[Realm.Index] == null) Cache.Request(_heart, Realm);
                Voices.StartLoop(_heart, _heart, Realm.Index, 0, _heart.VolumeDb, 1f, now);
            }
            else Voices.StopLoop(_heart, _heart);
        }

        float musicHz = 20000f, sfxHz = 20000f, musicDb = 0f, sfxDb = 0f;
        if ((want & MixState.Paused) != 0) { musicHz = Math.Min(musicHz, 800f); musicDb -= 6f; }
        if ((want & MixState.Underwater) != 0) { musicHz = Math.Min(musicHz, 1800f); sfxHz = Math.Min(sfxHz, 1200f); sfxDb -= 2f; }
        if ((want & MixState.LowHp) != 0) musicHz = Math.Min(musicHz, 3000f);
        if ((want & MixState.Void) != 0) musicHz = Math.Min(musicHz, VoidCutoff(ForcedMix.HasFlag(MixState.Void) && Board.Void < 0.02f ? 1f : Board.Void));
        if ((want & MixState.Defeat) != 0) musicHz = Math.Min(musicHz, 600f);

        // Cutoffs glide in octaves (a quarter second), levels in dB.
        float k = Math.Min(1f, dt / 0.25f);
        _musicHz *= MathF.Pow(musicHz / _musicHz, k);
        _sfxHz *= MathF.Pow(sfxHz / _sfxHz, k);
        _musicDb += Math.Clamp(musicDb - _musicDb, -30f * dt, 30f * dt);
        _sfxDb += Math.Clamp(sfxDb - _sfxDb, -30f * dt, 30f * dt);
        AudioBuses.ApplyMaster();
        AudioBuses.Apply(AudioBusId.Music, _musicDb, _musicHz);
        AudioBuses.Apply(AudioBusId.Sfx, _sfxDb, _sfxHz);
        AudioBuses.Apply(AudioBusId.Ambience, _sfxDb, _sfxHz);
        AudioBuses.Apply(AudioBusId.Ui, 0f, 20000f);
    }

    /// <summary>The music's low-pass as the VOID closes in: open on day 1, 1.2 kHz at the end.</summary>
    public static float VoidCutoff(float share) => 20000f * MathF.Pow(1200f / 20000f, Math.Clamp(share, 0f, 1f));

    /// <summary>Silence and forget: every voice, every pending post, the music (a check
    /// between stages, the audition page changing realm).</summary>
    public void Hush()
    {
        if (!Active) return;
        Voices.StopAll();
        for (int i = 0; i < _pending.Length; i++) _pending[i].Active = false;
        _stingerUntil = 0;
        _heartbeat = false;
        _paused = false;
        _defeatUntil = 0;
    }
}

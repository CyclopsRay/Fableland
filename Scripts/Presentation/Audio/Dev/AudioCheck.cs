namespace Fableland.Audio.Dev;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Fableland.Audio.Synth;
using Fableland.Data;

/// <summary>
/// Headless check for the audio layer (40-QA.md §2, Audio.gdd §12) — proof without ears:
///
///     Godot --headless --path . res://Scenes/Feedback/AudioCheck.tscn      # exit 0 = clean
///
/// 1. Dormant: in a headless run the director builds nothing and hears nothing until asked.
/// 2. Logic: the tables, the voice manager's rules, the clock, the combo ladder.
/// 3. The vocabulary: every cue of Feedback.gdd §6 (and the screen cues) resolves to a row
///    for every owner class, and every row renders in every medium it can be heard in —
///    not silent, not clipped, no DC, no click at either end, a continuous seam for a loop.
/// 4. Determinism: the same row, medium, variant and seed give the same bytes on any thread.
/// 5. Music: every profile's stems are whole bars, of unequal lengths, seamless, and mix
///    without clipping at any intensity; a boss's layer is in the boss's home medium.
/// 6. Memory: the budget holds with a realm and five protagonists loaded.
/// 7. The engine, for real (the headless driver still mixes): buses, settings, voices,
///    cooldowns, volleys, loops, layered hits, the ladder in key, music and its clock,
///    stingers on the grid, intensity moving stems in small steps, a boss's layer on the
///    bar, mix states, screens, realm eviction.
/// 8. A file overrides a recipe; the real menu, pause menu and run-over screen raise their cues.
/// 9. Raising cues allocates nothing.
///
/// It measures rendered samples and compares against what the tables say: retuning a sound
/// does not break it, breaking a mechanism does. It cannot LISTEN — whether any of it is
/// pleasant is a person's job (Audio.gdd §13). Measured numbers print as `audio` lines.
/// </summary>
public partial class AudioCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;
    private AudioDirector _d;
    private double _fake = 100.0;
    private const double Frame = 1.0 / 60.0;

    private static readonly string[] Protagonists = { "pomegraknight", "cleopastar", "pixolotl", "pumpking", "sifu_pangda" };
    private static readonly string[] BeachFoes = { "crab", "seagull", "boss_crab" };

    public override async void _Ready()
    {
        try
        {
            _d = AudioDirector.Instance;
            Expect(_d != null, "the audio director is not an autoload");
            if (_d == null) throw new InvalidOperationException("no AudioDirector");
            Dormant();
            Logic();
            Vocabulary();
            Determinism();
            Music();
            _d.Activate(keepPcm: true);
            _d.TimeSource = () => _fake;
            Buses();
            Memory();
            await TheEngine();
            FileOverride();
            await Screens();
            await Allocation();
        }
        catch (Exception e)
        {
            _problems.Add("audio check threw: " + e);
        }
        finally
        {
            FeedbackSettings.ResetToDefaults();
            AudioFiles.Root = AudioFiles.DefaultRoot;
            CueBus.Enabled = true;
            GetTree().Paused = false;
        }

        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0
            ? $"AudioCheck: OK ({_checks} checks)"
            : $"AudioCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- helpers
    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private void Absorb(string suite, List<string> failures)
    {
        _checks++;
        foreach (string failure in failures) _problems.Add(suite + ": " + failure);
    }

    /// <summary>Advance the made-up clock a frame at a time and run the director on it.</summary>
    private void Step(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            _fake += Frame;
            _d._Process(Frame);
        }
    }

    /// <summary>Wait (real frames) for the synthesis thread, then install what it made.</summary>
    private async Task Settle()
    {
        for (int i = 0; i < 1200 && _d.Cache.Pending > 0; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Step();
    }

    private AudioCheckBody Body(string id, string cls, CombatSide side)
    {
        var body = new AudioCheckBody { Id = id, Class = cls, BodySide = side, Position = new Vector2(100f, 100f) };
        AddChild(body);
        return body;
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.00") + " MB";

    // ---------------------------------------------------------------- 1. dormant
    private void Dormant()
    {
        Expect(!_d.Active, "headless: the director must stay dormant until asked — a check running fights at 8× must not be given sound to make");
        int buses = AudioServer.BusCount;
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, variant: 2, tag: "VK");
        CueBus.EmitAt(CueOwners.Ui, "press", Vector2.Zero);
        Expect(_d.CuesHeard == 0 && AudioServer.BusCount == buses && _d.Cache == null,
            "dormant: a cue built a bus, a cache or was counted");
    }

    // ---------------------------------------------------------------- 2. logic
    private void Logic()
    {
        Absorb("tables", AudioSelfTest.Table());
        Absorb("voices", AudioSelfTest.VoiceRules());
        Absorb("clock", AudioSelfTest.ClockRules());
        Absorb("ladder", AudioSelfTest.LadderRules());
        Expect(Medium.Count == 10, $"{Medium.Count} media, expected ten");
        foreach (string owner in Protagonists)
            Expect(Medium.HomeFor(owner) != null, $"{owner} has no home medium");
        Expect(Medium.HomeFor("pomegraknight").Id == "VK" && Medium.HomeFor("pumpking").Id == "KH" && Medium.HomeFor("pixolotl").Id == "TF"
               && Medium.HomeFor("cleopastar").Id == "SL" && Medium.HomeFor("sifu_pangda").Id == "BM" && Medium.HomeFor("crab").Id == "VK",
            "a character's home medium is not the one the GDD names");
    }

    // ---------------------------------------------------------------- 3. the vocabulary
    private readonly struct Word
    {
        public readonly string Owner, Class, Action, Tag;
        public readonly int Variant;
        public readonly bool Loop;
        public Word(string owner, string cls, string action, int variant = 0, string tag = null, bool loop = false)
        { Owner = owner; Class = cls; Action = action; Variant = variant; Tag = tag; Loop = loop; }
    }

    /// <summary>Feedback.gdd §6, plus the screen and UI cues of Audio.gdd §2.</summary>
    private static List<Word> Words()
    {
        var words = new List<Word>();
        var bodies = new List<(string, string)>();
        foreach (string p in Protagonists) bodies.Add((p, CueOwners.Fighter));
        bodies.Add(("pumpking", CueOwners.Boss));
        foreach (string f in BeachFoes) bodies.Add((f, CueOwners.Foe));
        bodies.Add(("a_new_fighter", CueOwners.Fighter));
        bodies.Add(("a_new_foe", CueOwners.Foe));
        bodies.Add(("a_new_boss", CueOwners.Boss));
        foreach ((string owner, string cls) in bodies)
        {
            words.Add(new Word(owner, cls, "spawn", cls == CueOwners.Foe ? 3 : 0));
            if (cls == CueOwners.Foe) words.Add(new Word(owner, cls, "spawn", 3, "born"));
            words.Add(new Word(owner, cls, "jump", 1));
            words.Add(new Word(owner, cls, "jump", 2, "air"));
            words.Add(new Word(owner, cls, "jump", 1, "swim"));
            words.Add(new Word(owner, cls, "land"));
            words.Add(new Word(owner, cls, "death"));
            words.Add(new Word(owner, cls, "heal"));
            foreach (string tag in new[] { "hazard", "burn", "on_fire", "frozen" }) words.Add(new Word(owner, cls, "hurt.tick", 0, tag));
            foreach (string status in new[] { "burning", "on_fire", "frozen", "trapped", "stunned" })
                words.Add(new Word(owner, cls, "status." + status, 0, null, true));
            words.Add(new Word(owner, cls, SfxTable.HitTaken));
            if (cls == CueOwners.Foe)
            {
                words.Add(new Word(owner, cls, "aggro"));
                words.Add(new Word(owner, cls, "evolve", 4));
                words.Add(new Word(owner, cls, "telegraph", 0, null, true));
            }
            else
            {
                words.Add(new Word(owner, cls, "respawn"));
                words.Add(new Word(owner, cls, "ult.ready"));
                words.Add(new Word(owner, cls, "switch.out"));
                words.Add(new Word(owner, cls, "switch.in"));
            }
        }
        words.Add(new Word("destroy_objective", CueOwners.Objective, "death"));
        words.Add(new Word("destroy_objective", CueOwners.Objective, SfxTable.HitTaken));
        words.Add(new Word("protect_core", CueOwners.Objective, "death"));
        words.Add(new Word("protect_core", CueOwners.Objective, "hurt"));

        const string pome = "pomegraknight";
        for (int stage = 1; stage <= 3; stage++)
        {
            words.Add(new Word(pome, CueOwners.Fighter, "ba.swing", stage));
            words.Add(new Word(pome, CueOwners.Fighter, "ba.swing", stage, "burning"));
            words.Add(new Word(pome, CueOwners.Fighter, "s2.wave", stage));
            words.Add(new Word(pome, CueOwners.Fighter, "s2.wave", stage, "burning"));
        }
        words.Add(new Word(pome, CueOwners.Fighter, "s1.cast"));
        words.Add(new Word(pome, CueOwners.Fighter, "s1.tornado", 0, null, true));
        for (int tick = 1; tick <= 5; tick++) words.Add(new Word(pome, CueOwners.Fighter, "s1.tornado.tick", tick));
        words.Add(new Word(pome, CueOwners.Fighter, "s2.cast"));
        words.Add(new Word(pome, CueOwners.Fighter, "s2.cast", 0, "burning"));
        words.Add(new Word(pome, CueOwners.Projectile, "s2.seed.expire"));
        words.Add(new Word(pome, CueOwners.Projectile, "s2.seed.expire", 0, "burning"));
        words.Add(new Word("pumpking", CueOwners.Projectile, "ba.explode"));
        words.Add(new Word("pumpking", CueOwners.Projectile, "ba.explode", 0, "dud"));

        foreach (string medium in new[] { "water", "foliage", "kit", "cloud" })
        {
            words.Add(new Word(CueOwners.World, null, "soft.enter", 0, medium));
            words.Add(new Word(CueOwners.World, null, "soft.exit", 0, medium));
        }
        foreach (string kind in new[] { "fire", "freeze", "lava", "spike", "poop", "damage" })
            words.Add(new Word(CueOwners.Hazard, null, "tick", 0, kind));

        words.Add(new Word(CueOwners.Flow, null, "arena.enter", 2, "VK"));
        for (int n = 3; n >= 1; n--) words.Add(new Word(CueOwners.Flow, null, "countdown.tick", n));
        words.Add(new Word(CueOwners.Flow, null, "countdown.go"));
        foreach (string mission in new[] { "collection", "protect", "destroy", "slaughter", "boss" })
        {
            words.Add(new Word(CueOwners.Flow, null, "mission.start", 2, mission));
            words.Add(new Word(CueOwners.Flow, null, "mission.success", 2, mission));
            words.Add(new Word(CueOwners.Flow, null, "mission.fail", 2, mission));
        }
        words.Add(new Word(CueOwners.Flow, null, "boss.intro", 4, "pumpking"));
        words.Add(new Word(CueOwners.Flow, null, "boss.defeated", 4, "pumpking"));
        words.Add(new Word(CueOwners.Flow, null, "player.down"));
        foreach (string end in new[] { "death", "boss_timer", "victory", "void_devoured" })
        {
            words.Add(new Word(CueOwners.Flow, null, "run.over", 0, end));
            words.Add(new Word(CueOwners.Flow, null, "runover.enter", 0, end));
        }
        // The cues this layer added (Audio.gdd §2).
        words.Add(new Word(CueOwners.Flow, null, "mission.time", 10));
        foreach (string screen in new[] { "menu.enter", "map.enter", "shelter.enter", "event.enter", "map.travel", "day.end", "void.devour" })
            words.Add(new Word(CueOwners.Flow, null, screen, 1));
        words.Add(new Word(CueOwners.Flow, null, "void.devour", 1, "singularity"));
        foreach (string ui in new[] { "hover", "press", "back", "pause.open", "pause.close" })
            words.Add(new Word(CueOwners.Ui, null, ui));
        return words;
    }

    private readonly HashSet<(SfxDef, int)> _judged = new();
    private int _clips;
    private long _clipBytes;
    private float _quietestRaw = 0f;
    private string _quietestRawWho = "";

    private void Vocabulary()
    {
        var clock = Stopwatch.StartNew();
        int resolved = 0;
        foreach (Word w in Words())
        {
            SfxDef def = SfxTable.ForCue(w.Owner, w.Class, w.Action);
            Expect(def != null, $"vocabulary: {w.Owner} ({w.Class ?? "system"}) / {w.Action} resolves to no sound");
            if (def == null) continue;
            resolved++;
            if (w.Loop != def.Loop) Expect(false, $"vocabulary: {w.Owner}/{w.Action} is {(w.Loop ? "a loop" : "a one-shot")} on the bus and {(def.Loop ? "a loop" : "a one-shot")} in the table");
            foreach (Medium medium in MediaFor(def, w.Owner)) Judge(def, medium);
        }

        // Hits: every attack name in use, an unwired kit, a kit that names an attack with no row.
        var attacks = new (string Owner, string Class, string Action)[]
        {
            ("pomegraknight", CueOwners.Fighter, "ba"), ("pomegraknight", CueOwners.Fighter, "s1.tornado"), ("pomegraknight", CueOwners.Projectile, "s2.seed"),
            ("pumpking", CueOwners.Projectile, "ba.head"), ("pumpking", CueOwners.Projectile, "ba.explode"),
            ("crab", CueOwners.Foe, "contact"), ("seagull", CueOwners.Foe, "contact"), ("boss_crab", CueOwners.Foe, "contact"), ("a_new_foe", CueOwners.Foe, "contact"),
            ("seagull", CueOwners.Foe, "s2.dash"), ("seagull", CueOwners.Projectile, "s1.poop"), (CueOwners.Hazard, CueOwners.Projectile, "tsunami"),
            (CueOwners.Any, null, ""), ("cleopastar", CueOwners.Fighter, "ba.star"), ("sifu_pangda", CueOwners.Fighter, "palm"),
        };
        foreach ((string owner, string cls, string action) in attacks)
        {
            SfxDef def = SfxTable.ForAttack(owner, cls, action);
            Expect(def != null, $"vocabulary: a hit by {owner}/{action} resolves to no sound");
            if (def == null) continue;
            resolved++;
            foreach (Medium medium in MediaFor(def, owner)) Judge(def, medium);
        }
        foreach (string own in new[] { SfxTable.HitHeavy, SfxTable.ComboNote, SfxTable.ComboResolve, SfxTable.Heartbeat })
            foreach (Medium medium in Medium.All) Judge(SfxTable.Cues.Resolve(CueOwners.Any, null, own), medium);
        foreach (Medium medium in Medium.All) Judge(SfxTable.Cues.Resolve(CueOwners.World, null, SfxTable.Ambience), medium);
        // …and every row the table holds, whether or not a word above reached it.
        foreach (SfxDef def in SfxTable.All)
            foreach (Medium medium in SfxTable.MediaOf(def)) Judge(def, medium);

        GD.Print($"  audio    vocabulary     {resolved} cues and hits resolve; {_judged.Count} row × medium sets rendered and measured — {_clips} clips, " +
                 $"{Mb(_clipBytes)} of PCM across all ten media, in {clock.Elapsed.TotalSeconds:0.0} s · the faintest recipe was {_quietestRaw:0} dBFS before normalising ({_quietestRawWho})");
    }

    /// <summary>The media a row must be heard in for this owner: every one of them for a
    /// class or realm row, the home medium for a character's own.</summary>
    private static IEnumerable<Medium> MediaFor(SfxDef def, string owner)
    {
        if (def.From == MediumFrom.Owner && SfxTable.IsClassRow(def)) return Medium.All;
        return SfxTable.MediaOf(def);
    }

    /// <summary>Render every case and variant of a row in a medium, and measure each.</summary>
    private void Judge(SfxDef def, Medium medium)
    {
        if (def == null || !_judged.Add((def, medium.Index))) return;
        var cases = new SfxClip[def.CaseCount][];
        AudioCache.RenderCases(def, medium, cases);
        var bad = new List<string>();
        int tags = def.Tags.Length + 1;
        for (int c = 0; c < cases.Length; c++)
        {
            for (int v = 0; v < cases[c].Length; v++)
            {
                SfxClip clip = cases[c][v];
                Measure(clip, def.PeakDb, $"{def.CaseId(c / tags, c % tags)} v{v + 1}", bad);
                _clips++;
                _clipBytes += clip.Bytes;
                if (clip.RawPeakDb < _quietestRaw) { _quietestRaw = clip.RawPeakDb; _quietestRawWho = $"{def.Id}@{medium.Id}"; }
            }
        }
        _checks++;
        for (int i = 0; i < bad.Count && i < 4; i++) _problems.Add($"sound {def.Id}@{medium.Id}: {bad[i]}");
        if (bad.Count > 4) _problems.Add($"sound {def.Id}@{medium.Id}: … and {bad.Count - 4} more");
    }

    private static void Measure(SfxClip clip, float peakDb, string label, List<string> bad, float longest = 4.5f)
    {
        float[] x = Finish.Unpack16(clip.Pcm);
        SoundStats s = Analysis.Measure(x, clip.SampleRate);
        if (clip.RawPeakDb < -45f) bad.Add($"{label}: the recipe made next to nothing ({clip.RawPeakDb:0} dBFS before normalising)");
        if (s.Peak < 0.02f || s.Rms < 0.001f) bad.Add($"{label}: silent (peak {Dsp.LinToDb(s.Peak):0} dBFS)");
        if (s.Clipped > 0) bad.Add($"{label}: {s.Clipped} clipped samples");
        if (s.Peak > Dsp.DbToLin(peakDb) * 1.04f) bad.Add($"{label}: peaks at {Dsp.LinToDb(s.Peak):0.0} dBFS, over its {peakDb:0} dB target");
        if (MathF.Abs(s.Dc) > 0.01f) bad.Add($"{label}: DC offset {s.Dc:0.000}");
        if (s.Seconds < 0.02f || s.Seconds > longest) bad.Add($"{label}: {s.Seconds:0.00} s long");
        if (clip.Loop)
        {
            float typical = Analysis.TypicalStep(x);
            if (s.SeamStep > typical * 1.5f + 0.002f) bad.Add($"{label}: the loop's seam steps {s.SeamStep:0.000}, an ordinary step is {typical:0.000}");
        }
        else
        {
            if (MathF.Abs(s.FirstSample) > 0.012f) bad.Add($"{label}: starts with a click ({s.FirstSample:0.000})");
            if (s.TailPeak > 0.012f) bad.Add($"{label}: ends with a click ({s.TailPeak:0.000} in its last 2 ms)");
        }
    }

    // ---------------------------------------------------------------- 4. determinism
    private void Determinism()
    {
        SfxDef swing = SfxTable.Cues.Resolve("pomegraknight", CueOwners.Fighter, "ba.swing");
        SfxDef land = SfxTable.Cues.Resolve("any_body", CueOwners.Fighter, "land");
        Medium vk = Medium.Get("VK"), tf = Medium.Get("TF");
        byte[] a = SfxRender.Render(swing, vk, 1, 0, 0).Pcm;
        byte[] b = SfxRender.Render(swing, vk, 1, 0, 0).Pcm;
        byte[] onThread = null;
        var thread = new System.Threading.Thread(() => onThread = SfxRender.Render(swing, vk, 1, 0, 0).Pcm);
        thread.Start();
        thread.Join();
        Expect(Same(a, b), "determinism: the same row, medium, stage and variant rendered twice gave different bytes");
        Expect(Same(a, onThread), "determinism: a render on another thread gave different bytes");
        Expect(!Same(a, SfxRender.Render(swing, vk, 1, 0, 1).Pcm), "determinism: two variants of a slash are the same bytes — variants must be siblings, not copies");
        Expect(!Same(a, SfxRender.Render(swing, vk, 2, 0, 0).Pcm), "determinism: two stages of a slash are the same bytes");
        Expect(!Same(a, SfxRender.Render(swing, vk, 1, 1, 0).Pcm), "determinism: the burning slash is the same bytes as the plain one");
        Expect(!Same(SfxRender.Render(land, vk, 0, 0, 0).Pcm, SfxRender.Render(land, tf, 0, 0, 0).Pcm), "determinism: a landing is the same bytes in watercolour and in pixel art");

        MusicProfile profile = MusicProfiles.Get("KH");
        MusicSet one = MusicBounce.Bounce(profile, 7), again = MusicBounce.Bounce(profile, 7), other = MusicBounce.Bounce(profile, 8);
        bool same = true, differs = false;
        for (int i = 0; i < one.Stems.Length; i++)
        {
            same &= Same(one.Stems[i].Pcm, again.Stems[i].Pcm);
            differs |= !Same(one.Stems[i].Pcm, other.Stems[i].Pcm);
        }
        Expect(same, "determinism: a profile bounced twice with one seed gave different stems");
        Expect(differs, "determinism: a profile bounced with two seeds gave the same stems — every fight would hear the same music");
    }

    private static bool Same(byte[] a, byte[] b) => a != null && b != null && a.AsSpan().SequenceEqual(b);

    // ---------------------------------------------------------------- 5. music
    // The budgets (Audio.gdd §11), bytes of 16-bit mono PCM.
    private const int MusicBudget = 2150 * 1024, CommonBudget = 512 * 1024, ProtagonistBudget = 1024 * 1024,
        ProtagonistsBudget = 3380 * 1024, FoesBudget = 615 * 1024, RealmBudget = 1945 * 1024;

    private long _worstMusicBytes;
    private string _worstMusic = "";

    private void Music()
    {
        double bounceSeconds = 0;
        foreach (MusicProfile p in MusicProfiles.All)
        {
            var clock = Stopwatch.StartNew();
            MusicSet set = MusicBounce.Bounce(p, 1);
            bounceSeconds += clock.Elapsed.TotalSeconds;
            if (p.RestIntensity < 0f && set.Bytes > _worstMusicBytes) { _worstMusicBytes = set.Bytes; _worstMusic = p.Id; }
            Expect(set.Bytes <= MusicBudget, $"music {p.Id}: {Mb(set.Bytes)} of stems, over the {Mb(MusicBudget)} music budget");
            var bad = new List<string>();
            for (int s = 0; s < set.Stems.Length; s++)
            {
                SfxClip stem = set.Stems[s];
                int frames = stem.Pcm.Length / 2;
                int wanted = p.Stems[s].Bars * p.BeatsPerBar * p.SamplesPerBeat;
                if (frames != wanted) bad.Add($"stem {p.Stems[s].Name} is {frames} samples, {p.Stems[s].Bars} bars are {wanted}");
                Measure(stem, MusicBounce.StemPeakDb, "stem " + p.Stems[s].Name, bad, 40f);
            }
            // Intensity: a stem below its threshold is silent, above it is full; the mix never clips.
            foreach (StemDef stem in p.Stems)
            {
                if (stem.InAt > 0f && MusicBounce.StemDb(stem, stem.InAt * 0.5f) > -60f) bad.Add($"stem {stem.Name} is heard below its threshold");
                if (MathF.Abs(MusicBounce.StemDb(stem, MathF.Min(0.8f, MathF.Max(stem.FullAt, 0.01f))) - stem.GainDb) > 0.01f) bad.Add($"stem {stem.Name} is not at its level once fully in");
            }
            float previousRms = 0f;
            foreach (float intensity in new[] { 0.1f, 0.5f, 1f })
            {
                SoundStats mix = Analysis.Measure(MusicBounce.Mixdown(set, intensity, 20f), p.SampleRate);
                if (mix.Peak > 0.89f) bad.Add($"the mix at intensity {intensity:0.0} peaks at {Dsp.LinToDb(mix.Peak):0.0} dBFS (limit −1)");
                if (mix.Rms < 0.004f) bad.Add($"the mix at intensity {intensity:0.0} is all but silent ({Dsp.LinToDb(mix.Rms):0} dB rms)");
                if (mix.Rms < previousRms * 0.7f) bad.Add($"the mix gets quieter as intensity rises to {intensity:0.0}");
                previousRms = mix.Rms;
            }
            _checks++;
            foreach (string problem in bad) _problems.Add($"music {p.Id}: {problem}");
        }

        // A boss's layer: two bars of the realm's tempo, in the boss's own medium.
        MusicProfile sl = MusicProfiles.Get("SL");
        SfxClip layer = MusicBounce.BossLayer(sl, "pumpking", 3);
        var layerBad = new List<string>();
        Measure(layer, MusicBounce.StemPeakDb, "boss layer", layerBad, 40f);
        Absorb("music: a boss's layer", layerBad);
        Expect(layer.SampleRate == Medium.HomeFor("pumpking").SampleRate
               && Math.Abs(layer.Pcm.Length / 2 / (double)layer.SampleRate - 2 * sl.SecondsPerBar) <= 1.0 / layer.SampleRate,
            "music: PumpKing's layer in Starland is not two of Starland's bars, in his own medium");
        Expect(!Same(layer.Pcm, MusicBounce.BossLayer(sl, "pomegraknight", 3).Pcm), "music: two bosses have the same layer");
        Expect(MusicProfiles.ForArena("SL", 2).Id == "SL" && MusicProfiles.ForArena("", 2).Id == "VK" && MusicProfiles.ForArena("", 5).Id == "VOID"
               && MusicProfiles.ForArena("CC", 3).Id == "CC" && MusicProfiles.ForArena("menu", 1).Id == "VK",
            "music: an arena's world does not pick the right profile (a nameless arena is home, or the VOID from level 5)");
        GD.Print($"  audio    music          {MusicProfiles.All.Count} profiles bounced in {bounceSeconds * 1000:0} ms in all ({bounceSeconds * 1000 / MusicProfiles.All.Count:0} ms each) · " +
                 $"the largest realm set is {_worstMusic} at {Mb(_worstMusicBytes)}");
    }

    // ---------------------------------------------------------------- engine: buses
    private void Buses()
    {
        foreach (string bus in new[] { AudioBuses.Music, AudioBuses.Sfx, AudioBuses.Ambience, AudioBuses.Ui })
        {
            int index = AudioServer.GetBusIndex(bus);
            Expect(index > 0, $"buses: no '{bus}' bus");
            if (index > 0) Expect(AudioServer.GetBusSend(index) == AudioBuses.Master, $"buses: '{bus}' does not send to Master");
        }
        bool limiter = false;
        for (int i = 0; i < AudioServer.GetBusEffectCount(0); i++) limiter |= AudioServer.GetBusEffect(0, i) is AudioEffectHardLimiter;
        Expect(limiter, "buses: no hard limiter on Master");
        Expect(AudioServer.GetBusEffect(AudioServer.GetBusIndex(AudioBuses.Music), 0) is AudioEffectLowPassFilter
               && AudioServer.GetBusEffect(AudioServer.GetBusIndex(AudioBuses.Sfx), 0) is AudioEffectLowPassFilter,
            "buses: Music and SFX have no low-pass slot");
        Expect(AudioServer.GetBusEffectCount(AudioServer.GetBusIndex(AudioBuses.Ui)) == 0, "buses: the UI bus is filtered");
        int count = AudioServer.BusCount;
        AudioBuses.Ensure();
        Expect(AudioServer.BusCount == count, "buses: building them twice added buses");

        // Settings scale the buses; 0 mutes.
        FeedbackSettings.ResetToDefaults();
        Step(30);
        Expect(Near(AudioBuses.VolumeDb(AudioBusId.Music), AudioBuses.MusicDb + Dsp.LinToDb(0.8f), 0.2f), $"buses: Music is at {AudioBuses.VolumeDb(AudioBusId.Music):0.0} dB with the default setting");
        Expect(Near(AudioBuses.VolumeDb(AudioBusId.Sfx), AudioBuses.SfxDb, 0.2f) && Near(AudioBuses.VolumeDb(AudioBusId.Ui), AudioBuses.UiDb + Dsp.LinToDb(0.9f), 0.2f),
            "buses: SFX or UI is not at its designed level with the default settings");
        FeedbackSettings.MusicVolume = 0.5f;
        FeedbackSettings.SfxVolume = 0.25f;
        FeedbackSettings.MasterVolume = 0.5f;
        Step(60);
        Expect(Near(AudioBuses.VolumeDb(AudioBusId.Music), AudioBuses.MusicDb - 6.02f, 0.2f), $"buses: Music at half volume is {AudioBuses.VolumeDb(AudioBusId.Music):0.0} dB");
        Expect(Near(AudioBuses.VolumeDb(AudioBusId.Sfx), AudioBuses.SfxDb - 12.04f, 0.2f) && Near(AudioBuses.VolumeDb(AudioBusId.Ambience), AudioBuses.AmbienceDb - 12.04f, 0.2f),
            "buses: the SFX setting does not scale SFX and Ambience");
        Expect(Near(AudioServer.GetBusVolumeDb(0), -6.02f, 0.2f), "buses: the master setting does not scale Master");
        FeedbackSettings.MusicVolume = 0f;
        Step(10);
        Expect(AudioBuses.VolumeDb(AudioBusId.Music) <= -79f, "buses: Music at volume 0 is not muted");
        FeedbackSettings.ResetToDefaults();
        Step(10);
    }

    private static bool Near(float a, float b, float within) => MathF.Abs(a - b) <= within;

    // ---------------------------------------------------------------- 6. memory
    private void Memory()
    {
        _d.Cache.Clear();
        var clock = Stopwatch.StartNew();
        _d.Cache.WarmCommon(true);
        long common = AudioCache.LoadedBytes();
        double commonMs = clock.Elapsed.TotalMilliseconds;
        Expect(common > 0 && common <= CommonBudget, $"memory: the common bank (UI, screens, the VOID's voice) is {Mb(common)} — budget {Mb(CommonBudget)}");

        long before = common, characters = 0, worstCharacter = 0;
        string report = "";
        clock.Restart();
        foreach (string owner in Protagonists)
        {
            _d.Cache.WarmOwner(owner, Medium.Home, true);
            long now = AudioCache.LoadedBytes();
            long cost = now - before;
            before = now;
            characters += cost;
            worstCharacter = Math.Max(worstCharacter, cost);
            report += $" {owner} {cost / 1024} KB ·";
            Expect(cost > 0 && cost <= ProtagonistBudget, $"memory: {owner} costs {Mb(cost)} — budget {Mb(ProtagonistBudget)} a protagonist");
        }
        Expect(characters <= ProtagonistsBudget, $"memory: the five protagonists cost {Mb(characters)} together — budget {Mb(ProtagonistsBudget)}");
        long foes = 0;
        foreach (string owner in BeachFoes)
        {
            _d.Cache.WarmOwner(owner, Medium.Home, true);
            long now = AudioCache.LoadedBytes();
            foes += now - before;
            before = now;
        }
        double charactersMs = clock.Elapsed.TotalMilliseconds;
        Expect(foes <= FoesBudget, $"memory: the beach's three foes cost {Mb(foes)} — budget {Mb(FoesBudget)}");

        long worstRealm = 0;
        string worstRealmId = "";
        double realmMs = 0;
        foreach (Medium medium in Medium.All)
        {
            clock.Restart();
            _d.Cache.WarmRealm(medium, true);
            realmMs = Math.Max(realmMs, clock.Elapsed.TotalMilliseconds);
            long cost = AudioCache.LoadedBytes() - before;
            if (cost > worstRealm) { worstRealm = cost; worstRealmId = medium.Id; }
            Expect(cost > 0 && cost <= RealmBudget, $"memory: the {medium.Id} realm bank is {Mb(cost)} — budget {Mb(RealmBudget)}");
            // Leaving a realm takes its bank with it.
            _d.Cache.EvictRealm(medium);
            Expect(AudioCache.LoadedBytes() == before, $"memory: leaving {medium.Id} did not give its memory back");
        }
        long total = before + worstRealm + _worstMusicBytes;
        Expect(total <= AudioCache.BudgetBytes,
            $"memory: common + five protagonists + the beach's foes + the largest realm ({worstRealmId}) + the largest music ({_worstMusic}) is {Mb(total)} — budget {Mb(AudioCache.BudgetBytes)}");
        GD.Print($"  audio    memory         common {common / 1024} KB ({commonMs:0} ms) ·{report} beach foes {foes / 1024} KB ({charactersMs:0} ms for all eight) · " +
                 $"largest realm bank {worstRealmId} {worstRealm / 1024} KB (at most {realmMs:0} ms to render one) · largest music {_worstMusic} {_worstMusicBytes / 1024} KB · " +
                 $"TOTAL {Mb(total)} of {Mb(AudioCache.BudgetBytes)}");
        _memoryTotal = total;
    }

    private long _memoryTotal;

    // ---------------------------------------------------------------- 7. the engine
    private async Task TheEngine()
    {
        Medium vk = Medium.Get("VK"), sl = Medium.Get("SL");
        SfxDef jump = SfxTable.Cues.Resolve("pomegraknight", CueOwners.Fighter, "jump");
        SfxDef spawn = SfxTable.Cues.Resolve("pomegraknight", CueOwners.Fighter, "spawn");
        SfxDef hazard = SfxTable.Cues.Resolve(CueOwners.Hazard, null, "tick");
        SfxDef note = SfxTable.Cues.Resolve(CueOwners.Any, null, SfxTable.ComboNote);
        SfxDef heart = SfxTable.Cues.Resolve(CueOwners.Any, null, SfxTable.Heartbeat);
        SfxDef burning = SfxTable.Cues.Resolve(CueOwners.Any, null, "status.burning");
        SfxDef unnamed = SfxTable.Attacks.Resolve(CueOwners.Any, null, "");

        // A realm: its medium, its key, its bank, its music.
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, variant: 2, magnitude: 0.4f, tag: "SL");
        Expect(_d.Realm == sl && _d.KeyTonic == sl.Tonic && _d.Board.State == MusicState.Combat, "engine: entering a Starland arena did not make Starland the realm and A the key");
        await Settle();
        Step(3);
        Expect(hazard.Sets[sl.Index] != null && burning.Sets[sl.Index] != null, "engine: the realm's bank was not warmed on arena.enter");
        Expect(_d.Music.Playing && _d.Clock.Running && _d.Music.Current?.Profile.Id == "SL", "engine: the realm's music is not playing after its stems were bounced");
        Expect(_d.Voices.IsLooping(SfxTable.Cues.Resolve(CueOwners.World, null, SfxTable.Ambience), SfxTable.Cues.Resolve(CueOwners.World, null, SfxTable.Ambience)),
            "engine: the realm's ambience loop was not started");

        // A body's cue plays, in its home medium, and not twice inside its cooldown.
        AudioCheckBody pome = Body("pomegraknight", CueOwners.Fighter, CombatSide.Player);
        AudioCheckBody crab = Body("crab", CueOwners.Foe, CombatSide.Foe);
        Step(30);
        int played = _d.Voices.Played, cooled = _d.Voices.DroppedCooldown;
        CueBus.Emit(pome, "jump", variant: 1, magnitude: 1024f);
        Expect(_d.Voices.Played == played + 1, "engine: a jump cue did not start a voice");
        CueBus.Emit(pome, "jump", variant: 1, magnitude: 1024f);
        Expect(_d.Voices.Played == played + 1 && _d.Voices.DroppedCooldown == cooled + 1, "engine: a second jump in the same instant was not held back by its cooldown");
        Expect(_d.Board.PlayerOwner == "pomegraknight", "engine: the blackboard did not learn who the player wears");
        Expect(_d.MediumFor(jump, "pomegraknight").Id == "VK" && _d.MediumFor(jump, "cleopastar").Id == "SL" && _d.MediumFor(jump, "pixolotl").Id == "TF"
               && _d.MediumFor(jump, "pumpking").Id == "KH" && _d.MediumFor(jump, "sifu_pangda").Id == "BM",
            "engine: a kit with no rows of its own does not play in its home medium");
        Expect(_d.MediumFor(jump, "a_stranger") == sl, "engine: a body with no home does not sound like the realm it is met in");
        Expect(_d.MediumFor(hazard, CueOwners.Hazard) == sl, "engine: a hazard does not sound like the realm");
        Expect(_d.MediumFor(SfxTable.Cues.Resolve(CueOwners.Ui, null, "press"), CueOwners.Ui).Id == "VK", "engine: the UI is not in the storybook's medium");

        // In key: her watercolour D is moved to Starland's A, by the nearest way.
        Expect(Near(_d.PitchFor(spawn, vk, 0, 0, 0f), Dsp.NearestShift(vk.Tonic, sl.Tonic), 0.001f) && Dsp.NearestShift(vk.Tonic, sl.Tonic) == -5,
            $"engine: a keyed sound of hers is moved {_d.PitchFor(spawn, vk, 0, 0, 0f)} st in Starland, expected −5 (D down to A)");
        bool inScale = true;
        for (int step = 1; step <= 24; step++)
        {
            int semis = (int)MathF.Round(_d.PitchFor(note, vk, 0, step, 0f)) + SfxTable.LadderCentre - Dsp.NearestShift(vk.Tonic, sl.Tonic);
            inScale &= Array.IndexOf(sl.Scale, ((semis % 12) + 12) % 12) >= 0 && MathF.Abs(_d.PitchFor(note, vk, 0, step, 0f)) <= 12.01f;
        }
        Expect(inScale, "engine: a combo's notes leave Starland's scale, or move the sample by more than an octave");

        // Hits are layered: what struck, what was struck, and the note — and a kill resolves.
        var slash = new HitInfo(15f, new Vector2(40f, 0f)).From(pome, "ba", 2);
        Step(30);
        played = _d.Voices.Played;
        CueBus.EmitHit(new HitCue(crab, slash, HitWeight.Medium, 15f, slash.Knockback, pome.Position, false, 0.05f));
        Expect(_d.Voices.Played == played + 3 && _d.Board.Combo == 1, $"engine: a slash that landed started {_d.Voices.Played - played} voices (attack + reaction + note = 3) and the combo is {_d.Board.Combo}");
        Step(20);
        played = _d.Voices.Played;
        CueBus.EmitHit(new HitCue(crab, slash, HitWeight.Heavy, 70f, slash.Knockback, pome.Position, false, 0.1f));
        Expect(_d.Voices.Played == played + 4 && _d.Board.Combo == 2, $"engine: a heavy blow started {_d.Voices.Played - played} voices (4 with its weight) and the combo is {_d.Board.Combo}");
        Step(20);
        played = _d.Voices.Played;
        CueBus.EmitHit(new HitCue(crab, slash, HitWeight.Medium, 30f, slash.Knockback, pome.Position, true, 0f));
        Expect(_d.Voices.Played == played + 3 && _d.Board.Combo == 0, $"engine: the killing blow started {_d.Voices.Played - played} voices (attack + reaction + resolution) and left the combo at {_d.Board.Combo}");
        Step(20);
        played = _d.Voices.Played;
        var claw = new HitInfo(18f, new Vector2(-40f, 0f)).From(crab, "contact");
        CueBus.EmitHit(new HitCue(pome, claw, HitWeight.Light, 18f, claw.Knockback, crab.Position, false, 0.05f));
        Expect(_d.Voices.Played == played + 2, $"engine: the player being hit started {_d.Voices.Played - played} voices (the claw + her reaction = 2)");
        Expect(_d.Board.Heat > 0f, "engine: hits did not heat the blackboard");

        // An unwired kit's hits are in ITS medium, because the blackboard knows who the player wears.
        AudioCheckBody cleo = Body("cleopastar", CueOwners.Fighter, CombatSide.Player);
        CueBus.Emit(cleo, "switch.in");
        Expect(_d.Board.PlayerOwner == "cleopastar" && _d.MediumFor(unnamed, _d.Board.PlayerOwner) == sl, "engine: an unnamed hit by Cleopastar is not in her poster medium");
        CueBus.Emit(pome, "switch.in");

        // A volley is one sound.
        Step(30);
        int bursts = _d.Voices.Bursts;
        played = _d.Voices.Played;
        var seed = new HitInfo(8f).FromRanged("pomegraknight", "s2.seed");
        for (int i = 0; i < 24; i++)
        {
            CueBus.EmitHit(new HitCue(crab, seed, HitWeight.Light, 8f, Vector2.Zero, pome.Position, false, 0f));
            _fake += 0.002;
        }
        Expect(_d.Voices.Bursts == bursts + 1, $"engine: 24 seed hits in 48 ms made {_d.Voices.Bursts - bursts} bursts, expected 1");
        Expect(_d.Voices.Played - played <= 12, $"engine: 24 seed hits started {_d.Voices.Played - played} voices");
        Expect(_d.Voices.LiveOn(AudioBusId.Sfx) <= _d.Voices.CapOf(AudioBusId.Sfx), "engine: the SFX bus is over its voice cap");

        // Loops: requests that outlive voices, ended with their source.
        Step(60);
        var burners = new[] { Body("crab", CueOwners.Foe, CombatSide.Foe), Body("crab", CueOwners.Foe, CombatSide.Foe), Body("seagull", CueOwners.Foe, CombatSide.Foe) };
        int requests = _d.Voices.LoopRequests;
        foreach (AudioCheckBody b in burners) CueBus.Start(b, "status.burning");
        Step(5);
        Expect(_d.Voices.LoopRequests == requests + 3, "engine: three burning bodies are not three loop requests");
        Expect(burning.LiveVoices == 2, $"engine: a loop row capped at 2 sounds {burning.LiveVoices}");
        burners[0].Free();
        Step(30);
        Expect(_d.Voices.LoopRequests == requests + 2 && burning.LiveVoices == 2, "engine: a loop whose source was freed was not ended, or the waiting one did not take its place");
        CueBus.Stop(burners[1], "status.burning");
        CueBus.Stop(burners[2], "status.burning");
        Step(40);
        Expect(_d.Voices.LoopRequests == requests && burning.LiveVoices == 0, "engine: stopped loops still sound");
        SfxDef tornado = SfxTable.Cues.Resolve("pomegraknight", CueOwners.Fighter, "s1.tornado");
        CueBus.Start(pome, "s1.tornado", size: 128f);
        Step(5);
        Expect(tornado.LiveVoices == 1, "engine: the Fire Tornado's loop did not start");
        CueBus.Stop(pome, "s1.tornado");
        Step(40);
        Expect(tornado.LiveVoices == 0, "engine: the Fire Tornado's loop did not stop");

        // A stinger lands on the music's grid.
        Step(90);
        MusicClock clock = _d.Clock;
        int held = _d.StingersHeld;
        played = _d.Voices.Played;
        double asked = _fake;
        CueBus.EmitAt(CueOwners.Flow, "mission.start", Vector2.Zero, variant: 2, tag: "collection");
        double quarter = clock.SecondsPerBeat / 4.0;
        double grid = (_d.LastStingerHeardAt - clock.T0) / quarter;
        Expect(_d.Board.Fighting && _d.Board.Mission == "collection", "engine: mission.start did not reach the blackboard");
        Expect(Math.Abs(grid - Math.Round(grid)) < 1e-6 && _d.LastStingerHeardAt >= asked - 1e-9 && _d.LastStingerHeardAt - asked <= Math.Max(MusicClock.GridWait, quarter) + 1e-6,
            $"engine: the mission-start stinger was aimed at {_d.LastStingerHeardAt - asked:0.000} s later, {grid:0.00} quarter-beats into the music — not on the grid, or too late");
        double firedAt = -1;
        for (int i = 0; i < 60 && firedAt < 0; i++)
        {
            Step();
            if (_d.Voices.Played > played) firedAt = _fake;
        }
        Expect(_d.StingersHeld == held + (firedAt - asked > Frame * 1.5 ? 1 : 0) || _d.StingersHeld == held + 1, "engine: the stinger was not held for the grid");
        Expect(firedAt >= 0 && Math.Abs(firedAt - _d.LastStingerHeardAt) <= Frame + 1e-9,
            $"engine: the stinger sounded {(firedAt - _d.LastStingerHeardAt) * 1000:0.0} ms from the grid line it was aimed at (a frame is {Frame * 1000:0.0})");

        // Intensity moves the stems, in steps too small to hear as steps.
        MusicProfile profile = _d.Music.Current.Profile;
        int pulse = Array.FindIndex(profile.Stems, s => s.Role == StemRole.Pulse), drums = Array.FindIndex(profile.Stems, s => s.Role == StemRole.Drums);
        _d.IntensityOverride = 0f;
        Step(300);
        Expect(_d.Music.StemDb(pulse) <= -79f && _d.Music.StemDb(drums) <= -79f && _d.Music.StemDb(0) > -30f, "engine: at intensity 0 the pulse and the drums are not muted under the bed");
        _d.IntensityOverride = 1f;
        float worstStep = 0f, last = _d.Music.StemDb(drums);
        for (int i = 0; i < 400; i++)
        {
            Step();
            float now = _d.Music.StemDb(drums);
            // The one jump that is designed: out of mute, to the level where a stem becomes audible.
            if (last > -79f || now > MusicEngine.MuteBelowDb + 0.01f) worstStep = MathF.Max(worstStep, MathF.Abs(now - last));
            last = now;
        }
        Expect(MathF.Abs(_d.Music.StemDb(drums) - MusicBounce.StemDb(profile.Stems[drums], 1f)) < 0.01f, "engine: at intensity 1 the drums are not at their level");
        Expect(worstStep <= 0.5f, $"engine: a stem's level jumped {worstStep:0.00} dB in one frame (0.5 is the limit: the engine does not ramp it)");
        _d.IntensityOverride = -1f;

        // A boss: the blackboard, the floor under intensity, and its layer entering on a bar.
        CueBus.EmitAt(CueOwners.Flow, "boss.intro", Vector2.Zero, null, CombatSide.Foe, variant: 4, tag: "pumpking");
        Expect(_d.Board.Boss && _d.Board.BossId == "pumpking" && _d.Board.State == MusicState.Boss, "engine: boss.intro did not reach the blackboard");
        await Settle();
        bool layered = false;
        double barAt = clock.NextBoundary(_fake, -1);
        for (int i = 0; i < 600 && !layered; i++)
        {
            Step();
            layered = _d.Music.OverlayPlaying;
        }
        Expect(layered && Math.Abs(_fake - barAt) <= Frame + 1e-9, $"engine: the boss's layer did not enter on the bar (it entered {(_fake - barAt) * 1000:0} ms from it)");
        Step(600);
        Expect(_d.Board.Intensity >= AudioBlackboard.BossFloor - 0.05f, $"engine: with a boss on the field intensity settled at {_d.Board.Intensity:0.00}, under its floor of {AudioBlackboard.BossFloor}");

        // Mix states.
        CueBus.EmitAt(CueOwners.Ui, "pause.open", Vector2.Zero);
        Step(120);
        Expect(_d.Mix.HasFlag(MixState.Paused) && Near(AudioBuses.CutoffHz(AudioBusId.Music), 800f, 60f) && Near(AudioBuses.VolumeDb(AudioBusId.Music), AudioBuses.RestDb(AudioBusId.Music) - 6f, 0.3f),
            $"engine: paused, the music is at {AudioBuses.CutoffHz(AudioBusId.Music):0} Hz and {AudioBuses.VolumeDb(AudioBusId.Music) - AudioBuses.RestDb(AudioBusId.Music):0.0} dB (800 Hz, −6 dB)");
        CueBus.EmitAt(CueOwners.Ui, "pause.close", Vector2.Zero);
        Step(240);
        Expect(!_d.Mix.HasFlag(MixState.Paused) && AudioBuses.CutoffHz(AudioBusId.Music) >= AudioBuses.OpenHz, "engine: unpaused, the music's low-pass is still closed");
        CueBus.EmitAt(CueOwners.World, "soft.enter", pome.Position, pome, magnitude: 300f, tag: "water");
        Step(120);
        Expect(_d.Board.Submerged && _d.Mix.HasFlag(MixState.Underwater) && Near(AudioBuses.CutoffHz(AudioBusId.Sfx), 1200f, 90f), $"engine: under water, SFX are at {AudioBuses.CutoffHz(AudioBusId.Sfx):0} Hz (1200)");
        CueBus.EmitAt(CueOwners.World, "soft.enter", crab.Position, crab, CombatSide.Foe, magnitude: 300f, tag: "water");
        CueBus.EmitAt(CueOwners.World, "soft.exit", pome.Position, pome, magnitude: 300f, tag: "water");
        Step(240);
        Expect(!_d.Board.Submerged && AudioBuses.CutoffHz(AudioBusId.Sfx) >= AudioBuses.OpenHz, "engine: out of the water (a foe still in it), SFX are still muffled");
        pome.State = FeedbackState.LowHp;
        Step(60);
        Expect(_d.Mix.HasFlag(MixState.LowHp) && _d.Voices.IsLooping(heart, heart) && Near(_d.Board.PlayerHp, AudioBlackboard.LowHpShare, 0.001f), "engine: at low HP there is no low-HP mix state or no heartbeat");
        pome.State = FeedbackState.None;
        Step(60);
        Expect(!_d.Mix.HasFlag(MixState.LowHp) && !_d.Voices.IsLooping(heart, heart), "engine: healed, the heartbeat goes on");

        // A fight ends: one stinger at a time, the music leaves.
        int skipped = _d.StingersSkipped;
        CueBus.EmitAt(CueOwners.Flow, "boss.defeated", Vector2.Zero, null, CombatSide.Foe, variant: 4, tag: "pumpking");
        CueBus.EmitAt(CueOwners.Flow, "mission.success", Vector2.Zero, variant: 4, tag: "boss");
        CueBus.EmitAt(CueOwners.Flow, "run.over", Vector2.Zero, tag: "victory");
        Expect(_d.StingersSkipped == skipped + 2 && _d.Board.State == MusicState.Victory && !_d.Board.Boss, "engine: a boss's fall was followed by lesser fanfares over it");
        Step(180);
        Expect(!_d.Clock.Running && !_d.Music.Playing, "engine: the music is still playing after the fight was won");

        // Screens: their own music and key; the VOID closing in on the map; the realm evicted on the next arena.
        CueBus.EmitAt(CueOwners.Flow, "map.enter", Vector2.Zero, variant: 23, tag: "SL");
        Expect(_d.Board.State == MusicState.Overworld && _d.Profile?.Id == MusicProfiles.Map && _d.KeyTonic == MusicProfiles.Get(MusicProfiles.Map).Tonic && Near(_d.Board.Void, 0.5f, 0.01f),
            "engine: the map screen did not ask for the overworld's music and key, or day 23 is not half-way to the VOID");
        await Settle();
        Step(240);
        Expect(_d.Music.Current?.Profile.Id == MusicProfiles.Map && _d.Mix.HasFlag(MixState.Void) && Near(AudioBuses.CutoffHz(AudioBusId.Music), AudioDirector.VoidCutoff(0.5f), 300f),
            $"engine: on day 23 the map's music is at {AudioBuses.CutoffHz(AudioBusId.Music):0} Hz, expected {AudioDirector.VoidCutoff(0.5f):0}");
        CueBus.EmitAt(CueOwners.Flow, "menu.enter", Vector2.Zero);
        Expect(_d.Profile?.Id == MusicProfiles.Menu && _d.Board.State == MusicState.Menu, "engine: the menu did not ask for its music");
        CueBus.EmitAt(CueOwners.Flow, "map.enter", Vector2.Zero, variant: 40, tag: "VOID");
        Expect(_d.Profile?.Id == "VOID" && _d.Board.Void >= 0.999f, "engine: a map inside the VOID does not play the VOID");
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, variant: 3, tag: "HC");
        Expect(_d.Realm.Id == "HC" && hazard.Sets[sl.Index] == null, "engine: entering the Hollow Castle did not evict Starland's bank");
        await Settle();
        CueBus.EmitAt(CueOwners.Flow, "runover.enter", Vector2.Zero, tag: "death");
        Step(120);
        Expect(_d.Profile == null && !_d.Clock.Running, "engine: the run-over screen has music");
        GD.Print($"  audio    the engine     voices: {_d.Voices.Played} played, {_d.Voices.Stolen} stolen, {_d.Voices.DroppedCooldown} held by cooldown, {_d.Voices.Bursts} volleys coalesced, " +
                 $"{_d.Voices.Refused} refused by the engine, peak {_d.Voices.PeakVoices} at once · a stinger lands within {Frame * 1000:0.0} ms of its grid line · a stem moves {worstStep:0.00} dB a frame at most");
        _d.Hush();
    }

    // ---------------------------------------------------------------- 8. a file wins
    private void FileOverride()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fableland-audiocheck-" + System.Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(dir);
            WavFile.Write(Path.Combine(dir, "any.jump.wav"), Tone(0.25f), 22050);
            WavFile.Write(Path.Combine(dir, "any.jump_2.wav"), Tone(0.30f), 22050);
            WavFile.Write(Path.Combine(dir, "any.jump@SL.wav"), Tone(0.40f), 22050);
            WavFile.Write(Path.Combine(dir, "any.status.burning.wav"), Tone(0.50f), 22050);
            AudioFiles.Root = dir;
            SfxDef jump = SfxTable.Cues.Resolve("pomegraknight", CueOwners.Fighter, "jump");
            SfxDef burning = SfxTable.Cues.Resolve(CueOwners.Any, null, "status.burning");
            SfxDef land = SfxTable.Cues.Resolve("pomegraknight", CueOwners.Fighter, "land");
            _d.Cache.Clear();
            SfxSet vk = _d.Cache.RenderNow(jump, Medium.Get("VK"));
            SfxSet sl = _d.Cache.RenderNow(jump, Medium.Get("SL"));
            SfxSet fire = _d.Cache.RenderNow(burning, Medium.Get("VK"));
            SfxSet plain = _d.Cache.RenderNow(land, Medium.Get("VK"));
            Expect(vk.Cases[0].Length == 2 && vk.Cases[0][0].FromFile && Near(vk.Cases[0][0].Seconds, 0.25f, 0.01f) && Near(vk.Cases[0][1].Seconds, 0.30f, 0.01f),
                "file override: any.jump.wav and any.jump_2.wav did not become the jump's two variants");
            Expect(!vk.Cases[1][0].FromFile && !vk.Cases[2][0].FromFile, "file override: a file for the plain jump also replaced the air and swim jumps");
            Expect(sl.Cases[0].Length == 1 && Near(sl.Cases[0][0].Seconds, 0.40f, 0.01f), "file override: a medium's own file (any.jump@SL.wav) did not win over the general one");
            Expect(fire.Cases[0][0].FromFile && fire.Cases[0][0].Stream is AudioStreamWav wav && wav.LoopMode == AudioStreamWav.LoopModeEnum.Forward && wav.LoopEnd > 0,
                "file override: a file that replaces a loop was not made to loop (a looping WAV needs its loop end)");
            Expect(!plain.Cases[0][0].FromFile, "file override: a row with no file was replaced");
            AudioCheckBody body = Body("pomegraknight", CueOwners.Fighter, CombatSide.Player);
            Step(30);
            int played = _d.Voices.Played;
            CueBus.Emit(body, "jump", variant: 1);
            Expect(_d.Voices.Played == played + 1, "file override: the file did not play through the same voice path");
        }
        finally
        {
            AudioFiles.Root = AudioFiles.DefaultRoot;
            _d.Cache.Clear();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static byte[] Tone(float seconds)
    {
        float[] x = new float[(int)(seconds * 22050)];
        Gen.Osc(x, 22050, 0, x.Length, Wave.Sine, 440f, 440f, Env.Swell(0.01f, seconds - 0.03f, 0.02f), 0.5f);
        var rng = new SynthRng(1);
        return Finish.Pack16(x, ref rng);
    }

    // ---------------------------------------------------------------- the screens' cues
    /// <summary>The real menu, pause menu and run-over screen raise their cues, and every
    /// button raises hover / press / back. (The map, shelter, event and day-end cues are
    /// raised in the run check's real runs; `FABLELAND_AUDIO=trace` prints them.)</summary>
    private async Task Screens()
    {
        var heard = new List<string>();
        void Listen(Cue cue)
        {
            if (cue.Owner == CueOwners.Flow || cue.Owner == CueOwners.Ui) heard.Add(cue.Owner + "/" + cue.Action + (string.IsNullOrEmpty(cue.Tag) ? "" : ":" + cue.Tag));
        }
        CueBus.Raised += Listen;
        try
        {
            Node menu = GD.Load<PackedScene>("res://Scenes/Menu.tscn").Instantiate();
            AddChild(menu);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Expect(heard.Contains("flow/menu.enter"), "screens: the title menu did not raise flow/menu.enter");
            Expect(_d.Profile?.Id == MusicProfiles.Menu, "screens: the title menu did not ask for the menu's music");
            var back = menu.GetNode<Button>("Center/SlotPanel/BackButton");
            var start = menu.GetNode<Button>("Center/VBox/StartButton");
            start.EmitSignal(Control.SignalName.MouseEntered);
            start.EmitSignal(BaseButton.SignalName.Pressed);
            back.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(heard.Contains("ui/hover") && heard.Contains("ui/press") && heard.Contains("ui/back"),
                $"screens: the menu's buttons raised [{string.Join(", ", heard)}] — expected ui/hover, ui/press and ui/back");
            menu.Free();

            heard.Clear();
            Fableland.UI.PauseMenu.Open(this, () => true);
            Expect(heard.Contains("ui/pause.open") && GetTree().Paused, "screens: opening the pause menu did not raise ui/pause.open");
            Step(60);
            Expect(_d.Mix.HasFlag(MixState.Paused), "screens: the real pause menu did not put the mix in its paused state");
            Button resume = null;
            foreach (Node node in FindChildren("*", "Button", true, false))
                if (node is Button candidate && candidate.Text == "Continue") resume = candidate;
            Expect(resume != null, "screens: the pause menu has no Continue button to press");
            resume?.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(heard.Contains("ui/pause.close") && !GetTree().Paused, "screens: closing the pause menu did not raise ui/pause.close");
            GetTree().Paused = false;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            heard.Clear();
            Node over = GD.Load<PackedScene>("res://Scenes/RunOver.tscn").Instantiate();
            AddChild(over);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Expect(heard.Exists(cue => cue.StartsWith("flow/runover.enter:")), "screens: the run-over screen did not raise flow/runover.enter with how the run ended");
            over.Free();
        }
        finally
        {
            CueBus.Raised -= Listen;
            GetTree().Paused = false;
        }
        _d.Hush();
    }

    // ---------------------------------------------------------------- 9. no garbage
    /// <summary>Real time has to pass for the engine to mix: a voice it was told to stop is
    /// only free once its mix thread has run (found by this check — thousands of voices
    /// started on a made-up clock were refused, 32 at a time).</summary>
    private async Task Breathe()
    {
        ulong start = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - start < 70) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Allocation()
    {
        // Everything a fight raises, with the director awake and every sound it needs loaded.
        CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, variant: 2, tag: "VK");
        _d.Cache.WarmRealm(Medium.Get("VK"), true);
        foreach (string owner in new[] { "pomegraknight", "crab" }) _d.Cache.WarmOwner(owner, Medium.Home, true);
        AudioCheckBody pome = Body("pomegraknight", CueOwners.Fighter, CombatSide.Player);
        AudioCheckBody crab = Body("crab", CueOwners.Foe, CombatSide.Foe);
        var slash = new HitInfo(15f, new Vector2(40f, 0f)).From(pome, "ba", 2);
        var claw = new HitInfo(18f, new Vector2(-40f, 0f)).From(crab, "contact");
        CueBus.EmitAt(CueOwners.Flow, "mission.start", Vector2.Zero, variant: 2, tag: "slaughter");
        for (int i = 0; i < 25; i++)
        {
            Round(pome, crab, slash, claw);
            Round(pome, crab, slash, claw);
            await Breathe();
        }
        int before = _d.Voices.Played, refusedBefore = _d.Voices.Refused;
        long allocated = 0, cueTicks = 0;
        const int rounds = 200;
        for (int i = 0; i < rounds / 2; i++)
        {
            // Measured only while the cues are raised; the wait for the engine is outside.
            long start = GC.GetAllocatedBytesForCurrentThread();
            long began = Stopwatch.GetTimestamp();
            Round(pome, crab, slash, claw);
            Round(pome, crab, slash, claw);
            cueTicks += Stopwatch.GetTimestamp() - began;
            allocated += GC.GetAllocatedBytesForCurrentThread() - start;
            await Breathe();
        }
        int voices = _d.Voices.Played - before;

        // CPU: a cue (lookup, medium, pitch, voice rules, the engine call), and a frame of the
        // director with music playing, loops sounding and voices live.
        double microsPerCue = cueTicks * 1e6 / Stopwatch.Frequency / (rounds * 9);
        await Settle();
        CueBus.Start(pome, "s1.tornado", size: 128f);
        CueBus.Start(crab, "status.burning");
        for (int i = 0; i < 6; i++) CueBus.Emit(pome, "ba.swing", variant: 1 + i % 3);
        Step(30);
        long frameStart = Stopwatch.GetTimestamp();
        const int frames = 3000;
        Step(frames);
        double microsPerFrame = (Stopwatch.GetTimestamp() - frameStart) * 1e6 / Stopwatch.Frequency / frames;
        CueBus.Stop(pome, "s1.tornado");
        CueBus.Stop(crab, "status.burning");
        Expect(_d.Music.Playing, "cpu: the frame was measured with no music playing");
        Expect(microsPerFrame < 100.0, $"cpu: a frame of the audio director costs {microsPerFrame:0.0} µs — budget 100 (0.1 ms)");
        Expect(microsPerCue < 100.0, $"cpu: a cue costs {microsPerCue:0.0} µs on average — budget 100");
        Expect(voices > rounds * 4, $"allocation: only {voices} voices started in {rounds} rounds — the measurement would prove nothing");
        Expect(allocated == 0, $"allocation: raising {rounds * 9} cues with the audio director awake ({voices} voices started) allocated {allocated} bytes ({allocated / (float)(rounds * 9):0.0} a cue); steady state must allocate nothing");
        Expect(_d.Unresolved == 0, $"allocation: {_d.Unresolved} cues heard during the check resolved to no sound");
        GD.Print($"  audio    the bus        {rounds * 9} cues and hits raised with the audio director awake — {voices} voices really started in the engine ({_d.Voices.Refused - refusedBefore} more refused by it under this load of some 280 a second, and dropped), {rounds} frames of bookkeeping: {allocated} bytes allocated");
        GD.Print($"  audio    cpu            a cue costs {microsPerCue:0.0} µs on average (lookup, pitch, voice rules and the engine's call; a hit is up to four voices) · " +
                 $"a frame of the director costs {microsPerFrame:0.0} µs with music, two loops and voices live");
        GD.Print($"  audio    budget         {Mb(_memoryTotal)} of PCM with a realm, its music, five protagonists and three foes loaded (budget {Mb(AudioCache.BudgetBytes)}) · voices capped at " +
                 $"{_d.Voices.CapOf(AudioBusId.Sfx)} SFX + {_d.Voices.CapOf(AudioBusId.Ui)} UI + {_d.Voices.CapOf(AudioBusId.Ambience)} ambience + {_d.Voices.CapOf(AudioBusId.Music)} stingers");
        _d.Hush();
    }

    /// <summary>Nine cues and one frame, a tenth of a second apart so cooldowns pass and voices really start.</summary>
    private void Round(AudioCheckBody pome, AudioCheckBody crab, in HitInfo slash, in HitInfo claw)
    {
        _fake += 0.1;
        CueBus.Emit(pome, "ba.swing", variant: 2, size: 130f, magnitude: 15f, direction: Vector2.Right, tag: "burning");
        CueBus.Emit(pome, "jump", variant: 1, magnitude: 1024f);
        CueBus.Emit(crab, "hurt.tick", magnitude: 3f, tag: "burn");
        CueBus.Start(pome, "s1.tornado", size: 128f, extents: new Vector2(128f, 40f));
        CueBus.Stop(pome, "s1.tornado");
        CueBus.EmitAt(CueOwners.World, "soft.enter", crab.Position, crab, CombatSide.Foe, magnitude: 300f, tag: "foliage");
        CueBus.EmitAt(CueOwners.Hazard, "tick", pome.Position, null, CombatSide.Player, magnitude: 5f, tag: "fire");
        CueBus.EmitHit(new HitCue(crab, slash, HitWeight.Light, 15f, slash.Knockback, pome.Position, false, 0.044f));
        CueBus.EmitHit(new HitCue(pome, claw, HitWeight.Heavy, 80f, claw.Knockback, crab.Position, false, 0.1f));
        _d._Process(Frame);
    }
}

namespace Fableland.Audio.Dev;

using System;
using System.Collections.Generic;
using Godot;
using Fableland.Audio.Synth;
using Fableland.Data;

/// <summary>
/// The audition page (<c>Docs/Audio.gdd</c> §13): the owner's way to LISTEN to what was built
/// without ears. Run <c>res://Scenes/Dev/AudioAudition.tscn</c> (F6 on the scene):
///
///   • every sound event, every case and every variant — in its own medium or forced into
///     any of the ten, so the same event can be heard in every world;
///   • the music of any realm or screen, with the intensity dragged by hand;
///   • the mix states, the stingers, a boss's layer, the combo ladder and its resolution;
///   • the volume settings (held in memory only — nothing is saved).
///
/// It drives the real director through the real cue bus, so what is heard here is what the
/// game plays. Presentation-only: it touches no gameplay and no save.
/// </summary>
public partial class AudioAudition : Control
{
    private AudioDirector _d;
    private ItemList _events;
    private LineEdit _filter;
    private OptionButton _medium, _place;
    private HSlider _intensity;
    private CheckBox _follow;
    private Label _status, _detail;
    private HBoxContainer _variants;
    private readonly List<(SfxDef Def, int Case)> _rows = new();
    private readonly List<(SfxDef Def, int Case)> _shown = new();
    private AudioCheckBody _player, _foe;
    private int _beat = -1, _bar = -1;

    private static readonly string[] Places = { "VK", "SL", "BM", "HC", "TD", "PL", "KH", "TF", "CC", "VOID", "menu", "map", "shelter", "event", "(silence)" };

    public override void _Ready()
    {
        _d = AudioDirector.Instance;
        if (_d == null) { AddChild(new Label { Text = "AudioDirector is not an autoload." }); return; }
        _d.Activate();
        _d.Cache.WarmCommon();
        foreach (string owner in Medium.KnownOwners) _d.Cache.WarmOwner(owner, Medium.Home);
        _d.Clock.BeatHit += OnBeat;
        _d.Clock.BarHit += OnBar;

        _player = new AudioCheckBody { Id = "pomegraknight", Class = CueOwners.Fighter, BodySide = CombatSide.Player };
        _foe = new AudioCheckBody { Id = "crab", Class = CueOwners.Foe, BodySide = CombatSide.Foe };
        AddChild(_player);
        AddChild(_foe);

        foreach (SfxDef def in SfxTable.All)
            for (int c = 0; c < def.CaseCount; c++) _rows.Add((def, c));

        SetAnchorsPreset(LayoutPreset.FullRect);
        var split = new HSplitContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(split);

        // ── Left: every event ──
        var left = new VBoxContainer();
        split.AddChild(left);
        _filter = new LineEdit { PlaceholderText = "filter: pomegraknight, hit, flow, loop…" };
        _filter.TextChanged += _ => Fill();
        left.AddChild(_filter);
        _events = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(400f, 0f) };
        _events.ItemSelected += index => Select((int)index);
        _events.ItemActivated += index => { Select((int)index); Play(-1); };
        left.AddChild(_events);

        // ── Right: the dials ──
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 8);
        split.AddChild(right);
        _detail = new Label { Text = "Pick an event. Double-click, or press a variant.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_detail);
        _variants = new HBoxContainer();
        right.AddChild(_variants);

        _medium = new OptionButton();
        _medium.AddItem("medium: its own");
        foreach (Medium m in Medium.All) _medium.AddItem($"force {m.Id} — {m.Name}");
        _medium.ItemSelected += index => _d.ForceMedium = index == 0 ? null : Medium.All[(int)index - 1];
        right.AddChild(_medium);

        right.AddChild(new HSeparator());
        _place = new OptionButton();
        foreach (string place in Places) _place.AddItem("where: " + place);
        _place.ItemSelected += index => Go(Places[(int)index]);
        right.AddChild(_place);

        var intensityRow = new HBoxContainer();
        intensityRow.AddChild(new Label { Text = "intensity" });
        _intensity = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 0.5, CustomMinimumSize = new Vector2(260f, 0f) };
        _intensity.ValueChanged += value => { if (!_follow.ButtonPressed) _d.IntensityOverride = (float)value; };
        intensityRow.AddChild(_intensity);
        _follow = new CheckBox { Text = "follow the game", ButtonPressed = false };
        _follow.Toggled += on => _d.IntensityOverride = on ? -1f : (float)_intensity.Value;
        intensityRow.AddChild(_follow);
        right.AddChild(intensityRow);
        _d.IntensityOverride = 0.5f;

        var stingers = new HFlowContainer();
        foreach (string action in new[] { "countdown.tick", "countdown.go", "mission.start", "mission.success", "mission.fail", "boss.intro", "boss.defeated", "player.down", "run.over", "day.end", "void.devour" })
        {
            string cue = action;
            stingers.AddChild(Button(cue, () => CueBus.EmitAt(CueOwners.Flow, cue, Vector2.Zero, null, CombatSide.Player, variant: 2,
                tag: cue == "boss.intro" || cue == "boss.defeated" ? "pumpking" : cue == "run.over" ? "victory" : "collection")));
        }
        right.AddChild(stingers);

        var actions = new HFlowContainer();
        actions.AddChild(Button("hit (climbs the scale)", () => Hit(false)));
        actions.AddChild(Button("kill (resolves)", () => Hit(true)));
        actions.AddChild(Button("heavy hit", () => Hit(false, HitWeight.Heavy)));
        actions.AddChild(Button("player hit", HurtPlayer));
        actions.AddChild(Button("seed volley ×24", Volley));
        actions.AddChild(Button("silence", () => _d.Hush()));
        right.AddChild(actions);

        right.AddChild(new HSeparator());
        var mix = new HFlowContainer();
        foreach (MixState state in new[] { MixState.Paused, MixState.Underwater, MixState.LowHp, MixState.Void, MixState.Defeat })
        {
            MixState flag = state;
            var box = new CheckBox { Text = flag.ToString() };
            box.Toggled += on => _d.ForcedMix = on ? _d.ForcedMix | flag : _d.ForcedMix & ~flag;
            mix.AddChild(box);
        }
        right.AddChild(mix);

        var volumes = new GridContainer { Columns = 2 };
        Volume(volumes, "master", FeedbackSettings.MasterVolume, v => FeedbackSettings.MasterVolume = v);
        Volume(volumes, "music", FeedbackSettings.MusicVolume, v => FeedbackSettings.MusicVolume = v);
        Volume(volumes, "sfx", FeedbackSettings.SfxVolume, v => FeedbackSettings.SfxVolume = v);
        Volume(volumes, "ui", FeedbackSettings.UiVolume, v => FeedbackSettings.UiVolume = v);
        right.AddChild(volumes);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_status);

        Fill();
        Go("VK");
    }

    public override void _ExitTree()
    {
        if (_d == null || !_d.Active) return;
        // Paired with _Ready: the director outlives this page (T10 §1).
        _d.Clock.BeatHit -= OnBeat;
        _d.Clock.BarHit -= OnBar;
        _d.ForceMedium = null;
        _d.IntensityOverride = -1f;
        _d.ForcedMix = MixState.None;
        _d.Hush();
        FeedbackSettings.ResetToDefaults();
    }

    private void OnBeat(int beat) => _beat = beat;
    private void OnBar(int bar) => _bar = bar;

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        return button;
    }

    private static void Volume(Container into, string name, float value, Action<float> set)
    {
        into.AddChild(new Label { Text = name });
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = value, CustomMinimumSize = new Vector2(220f, 0f) };
        slider.ValueChanged += v => set((float)v);
        into.AddChild(slider);
    }

    private void Fill()
    {
        _events.Clear();
        _shown.Clear();
        string filter = _filter.Text.Trim().ToLowerInvariant();
        foreach ((SfxDef def, int c) in _rows)
        {
            int tags = def.Tags.Length + 1;
            string label = def.CaseId(c / tags, c % tags) + (def.Loop ? "   (loop)" : "") + "   · " + def.Bus;
            if (filter.Length > 0 && !label.ToLowerInvariant().Contains(filter)) continue;
            _events.AddItem(label);
            _shown.Add((def, c));
        }
    }

    private int _selected = -1;

    private void Select(int index)
    {
        _selected = index;
        foreach (Node child in _variants.GetChildren()) child.QueueFree();
        if (index < 0 || index >= _shown.Count) return;
        (SfxDef def, int c) = _shown[index];
        Medium medium = _d.MediumFor(def, def.Owner);
        _detail.Text = $"{def.Id} — medium {medium.Id} ({medium.Name}) · {def.Variants} variant(s) · priority {def.Priority} · at most {def.MaxVoices} at once · " +
                       $"cooldown {def.Cooldown * 1000:0} ms · pitch {def.Pitch} · peak {def.PeakDb:0} dB";
        if (def.Loop) _variants.AddChild(Button("start / stop the loop", () => _d.AuditionLoop(def, _d.MediumFor(def, def.Owner), c)));
        else
            for (int v = 0; v < Math.Clamp(def.Variants, 1, 4); v++)
            {
                int variant = v;
                _variants.AddChild(Button("variant " + (v + 1), () => Play(variant)));
            }
    }

    private int _next;

    private void Play(int variant)
    {
        if (_selected < 0 || _selected >= _shown.Count) return;
        (SfxDef def, int c) = _shown[_selected];
        Medium medium = _d.MediumFor(def, def.Owner);
        if (def.Loop) { _d.AuditionLoop(def, medium, c); return; }
        _d.Audition(def, medium, c, variant >= 0 ? variant : _next++ % Math.Clamp(def.Variants, 1, 4));
    }

    /// <summary>Go somewhere: an arena of a realm, or a screen — through the same cues the game raises.</summary>
    private void Go(string place)
    {
        switch (place)
        {
            case "menu": CueBus.EmitAt(CueOwners.Flow, "menu.enter", Vector2.Zero); break;
            case "map": CueBus.EmitAt(CueOwners.Flow, "map.enter", Vector2.Zero, variant: 1, tag: "VK"); break;
            case "shelter": CueBus.EmitAt(CueOwners.Flow, "shelter.enter", Vector2.Zero, tag: "hub"); break;
            case "event": CueBus.EmitAt(CueOwners.Flow, "event.enter", Vector2.Zero); break;
            case "(silence)": CueBus.EmitAt(CueOwners.Flow, "runover.enter", Vector2.Zero, tag: "death"); break;
            default:
                CueBus.EmitAt(CueOwners.Flow, "arena.enter", Vector2.Zero, variant: place == "VOID" ? 5 : 2, magnitude: 0.5f, tag: place == "VOID" ? "" : place);
                CueBus.Emit(_player, "spawn");
                CueBus.EmitAt(CueOwners.Flow, "mission.start", Vector2.Zero, variant: 2, tag: "slaughter");
                break;
        }
    }

    private void Hit(bool kill, HitWeight weight = HitWeight.Medium)
    {
        var slash = new HitInfo(weight == HitWeight.Heavy ? 70f : 15f, new Vector2(40f, 0f)).From(_player, "ba", 1 + _next++ % 3);
        CueBus.EmitHit(new HitCue(_foe, slash, weight, slash.Damage, slash.Knockback, _player.Position, kill, 0.05f));
    }

    private void HurtPlayer()
    {
        var claw = new HitInfo(18f, new Vector2(-40f, 0f)).From(_foe, "contact");
        CueBus.EmitHit(new HitCue(_player, claw, HitWeight.Light, 18f, claw.Knockback, _foe.Position, false, 0.05f));
    }

    private void Volley()
    {
        CueBus.Emit(_player, "s2.wave", variant: 2, magnitude: 8f);
        var seed = new HitInfo(8f).FromRanged("pomegraknight", "s2.seed");
        for (int i = 0; i < 24; i++) CueBus.EmitHit(new HitCue(_foe, seed, HitWeight.Light, 8f, Vector2.Zero, _player.Position, false, 0f));
    }

    public override void _Process(double delta)
    {
        if (_d == null || !_d.Active || _status == null) return;
        if (_follow.ButtonPressed) _intensity.SetValueNoSignal(_d.Board.Intensity);
        MusicProfile profile = _d.Music.Current?.Profile;
        _status.Text =
            $"realm {_d.Realm.Id} ({_d.Realm.Name}) · key: tonic {_d.KeyTonic} · music: {(profile != null ? profile.Id + " — " + profile.Title + ", " + profile.Bpm + " BPM, " + profile.Metre : "none")}\n" +
            $"beat {_beat} · bar {_bar} · intensity {_d.Intensity:0.00} · combo {_d.Board.Combo} · mix {_d.Mix}\n" +
            $"voices {_d.Voices.LiveVoices} (peak {_d.Voices.PeakVoices}) · played {_d.Voices.Played} · stolen {_d.Voices.Stolen} · held by cooldown {_d.Voices.DroppedCooldown} · volleys {_d.Voices.Bursts}\n" +
            $"sound effects loaded {AudioCache.LoadedBytes() / 1024} KB · music {(_d.Music.Current?.Bytes ?? 0) / 1024} KB · rendering {_d.Cache.Pending} · {_d.Cache.ClipsRendered} clips in {_d.Cache.RenderSeconds:0.00} s";
    }
}

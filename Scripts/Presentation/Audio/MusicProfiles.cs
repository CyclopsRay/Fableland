using System;
using System.Collections.Generic;
using Fableland.Audio.Synth;

namespace Fableland.Audio;

/// <summary>
/// Every piece of music in the game, as data (<c>Docs/Audio.gdd</c> §7.4): the eight pool
/// worlds, Cloud Court, the VOID, and the four screens between fights. Keys stay in one
/// family (D, A, E, G, C) so a character's motif and a crossfade between two screens share
/// tones. Stems of one profile have unequal bar counts — the whole repeats only after their
/// least common multiple. Harmony never leaves the tonic, so any alignment is consonant.
///
/// Starting values from the research digest (R1 §4.7), judged on spectrogram sheets and
/// never heard.
/// </summary>
public static class MusicProfiles
{
    private const int X = MusicCtx.Rest;
    private static readonly Dictionary<string, MusicProfile> ById = new(StringComparer.Ordinal);
    public static readonly List<MusicProfile> All = new();

    public const string Menu = "menu", Map = "map", Shelter = "shelter", Event = "event";

    /// <summary>The profile with this id (a realm code or a screen name), or null.</summary>
    public static MusicProfile Get(string id) => id != null && ById.TryGetValue(id, out MusicProfile p) ? p : null;

    private static StemDef Stem(string name, StemRole role, int bars, float gainDb, float inAt, float fullAt, string adds, Action<MusicCtx> gen) =>
        new() { Name = name, Role = role, Bars = bars, GainDb = gainDb, InAt = inAt, FullAt = fullAt, Adds = adds, Gen = gen };

    private static StemDef Bed(int bars, string adds, Action<MusicCtx> gen) => Stem("bed", StemRole.Bed, bars, -6f, 0f, 0f, adds, gen);
    private static StemDef Pulse(int bars, string adds, Action<MusicCtx> gen) => Stem("pulse", StemRole.Pulse, bars, -5f, 0.2f, 0.45f, adds, gen);
    private static StemDef Drums(int bars, string adds, Action<MusicCtx> gen) => Stem("drums", StemRole.Drums, bars, -4f, 0.5f, 0.75f, adds, gen);
    private static StemDef Lead(int bars, string adds, Action<MusicCtx> gen) => Stem("lead", StemRole.Lead, bars, -3f, 0f, 0.1f, adds, gen);

    private static MusicProfile Add(string id, string title, string mediumId, float bpm, int beatsPerBar, int stepsPerBeat, string metre,
                                    params StemDef[] stems)
    {
        Medium medium = Medium.Get(mediumId);
        var profile = new MusicProfile
        {
            Id = id, Title = title, Medium = medium, Tonic = medium.Tonic, Scale = medium.Scale, Motif = medium.Motif,
            Bpm = bpm, BeatsPerBar = beatsPerBar, StepsPerBeat = stepsPerBeat, Metre = metre, Stems = stems,
        };
        ById[id] = profile;
        All.Add(profile);
        return profile;
    }

    /// <summary>Move a whole profile's level: the realms were measured (mixdown RMS) and
    /// brought within a few dB of one another, so a slider means the same in each.</summary>
    private static MusicProfile Trim(this MusicProfile profile, float db)
    {
        foreach (StemDef stem in profile.Stems) stem.GainDb += db;
        return profile;
    }

    static MusicProfiles()
    {
        // ── VK · the Beach · watercolour · D gong pentatonic · 104, swung ───────────
        Add("VK", "The Beach", "VK", 104f, 4, 2, "4/4, swung",
            Bed(3, "a soft pad on D and A, and surf", m =>
            {
                m.Bed(new[] { new[] { 0, 3 }, new[] { 0, 4 } }, 2, -1, 0.8f);
                m.TextureLoop(Tex.Water, 0.22f);
            }),
            Pulse(5, "marimba ostinato", m => m.Pulse(new[] { new[] { 0, 2, 3, 2, 4, 2, 3, 2 }, new[] { 0, 3, 4, 3, 5, 3, 4, 3 } }, 0, ToneKind.Short, 0.8f)),
            Drums(7, "shaker and a low conga", m => m.Drums("x.....x.", "..x...x.", ".x.x.x.x", 0.8f, 0.5f, 0.3f, 0.5f)),
            Lead(4, "steel pan: the motif", m => m.Lead(1, 0.35f, 0.8f))).Swing = 0.12f;

        // ── SL · Starland · flat poster · A Phrygian dominant, sparse · 56 ──────────
        Add("SL", "Starland", "SL", 56f, 4, 2, "four slow beats, free",
            Bed(3, "a sub drone and its fifth, in terraces", m => m.Bed(new[] { new[] { 0 } }, 3, -1, 0.9f)),
            Pulse(4, "one doum every two bars, a sistrum answer", m => m.Drums("X...............", "", "........x.x.x...", 0.9f, 0f, 0.4f)),
            Drums(2, "a half-time maqsum", m => m.Drums("x...x...", ".x.x..x.", "", 0.7f, 0.45f, 0f)),
            Lead(5, "a glass bell, a distant call", m => m.Lead(1, 0.5f, 0.8f, 1, 2f, ToneKind.Long))).Trim(-3f);

        // ── BM · the Banboo Maze · ink · E yu pentatonic · 72, loose ────────────────
        Add("BM", "The Banboo Maze", "BM", 72f, 4, 2, "4/4, loose",
            Bed(2, "bare paper: a breath at the tonic", m => m.Bed(new[] { new[] { 0 } }, 2, -1, 0.9f)),
            Pulse(3, "muyu knocks, some left out", m => m.Drums("x...x...", "", "", 0.7f, 0f, 0f, 0f, 0.25f)),
            Drums(4, "bangu rolls that accelerate", m =>
            {
                m.Sprinkle(3, at =>
                {
                    int hits = 6 + m.R.Rng.Int(4);
                    float gap = 0.22f, t = at;
                    for (int i = 0; i < hits; i++)
                    {
                        m.M.Tick(m.R, t, 0.4f + 0.05f * i, 0.4f + 0.06f * i);
                        t += gap;
                        gap *= 0.8f;
                    }
                    m.M.Thump(m.R, t, 0.5f, 0.7f);
                });
            }),
            Lead(5, "guzheng gestures, half of them rests; a dizi answers", m => m.Lead(0, 0.5f, 0.85f, 2, 1.5f, ToneKind.Long, true))).Trim(2f).Loose = 0.02f;

        // ── HC · the Hollow Castle · lit realism · D Aeolian + tritone · 80, 6/8 ────
        Add("HC", "The Hollow Castle", "HC", 80f, 2, 3, "6/8",
            Bed(4, "a low saw drone under 400 Hz, lava", m =>
            {
                m.Bed(new[] { new[] { 0 } }, 3, -1, 0.8f);
                m.TextureLoop(Tex.Rumble, 0.3f);
            }),
            Pulse(7, "anvil and chain", m => m.Drums("", "x.....x..x..", "...x........", 0f, 0.7f, 0.4f)),
            Drums(10, "low drums", m => m.Drums("X..x.x", "", "", 0.9f, 0f, 0f, 0.4f)),
            Lead(9, "an iron bell: the motif, slowly, through its tritone", m => m.Lead(0, 0.3f, 0.7f, 2, 1f, ToneKind.Long)));

        // ── TD · the Deserted · linocut · A minor pentatonic · 92, gallop ───────────
        Add("TD", "The Deserted", "TD", 92f, 4, 4, "4/4, a gallop",
            Bed(4, "wind in terraces, crackle", m => m.TextureLoop(Tex.Air, 0.6f)),
            Pulse(6, "twang guitar, root and fifth", m =>
            {
                m.Pulse(new[] { new[] { 0, X, X, X, X, X, X, X, 3, X, X, X, X, X, X, X } }, -1, ToneKind.Low, 0.9f, 3f, true, 0f);
                m.Pulse(new[] { new[] { X, X, 0, 0, X, X, 3, 3, X, X, 0, 0, X, X, 3, 2 } }, 0, ToneKind.Short, 0.5f, 1f, false, 0f);
            }),
            Drums(7, "clip-clop blocks, a tambourine", m => m.Drums("x.xxx.xxx.xxx.xx", "", "....x.......x...", 0.6f, 0f, 0.5f)),
            Lead(5, "a whistle over the twang", m => m.Lead(1, 0.4f, 0.7f, 2, 1f, ToneKind.Accent))).Trim(2f);

        // ── PL · the Palace of LOOING · gongbi · G Lydian · 63, strict ──────────────
        Add("PL", "The Palace of LOOING", "PL", 63f, 4, 2, "4/4, strict",
            Bed(2, "a deep pad, bubbles at an even rate", m =>
            {
                m.Bed(new[] { new[] { 0, 4 } }, 2, -1, 0.8f);
                m.TextureLoop(Tex.Water, 0.25f);
            }),
            Pulse(3, "bronze-chime ostinato", m => m.Pulse(new[] { new[] { 0, 4, 2, 4, 3, 4, 2, 4 } }, 0, ToneKind.Short, 0.75f, 1f, false, 0f)),
            Drums(4, "a frame drum, small bells", m => m.Drums("x...x...", "", "...x...x", 0.7f, 0f, 0.4f)),
            Lead(5, "a xiao: the motif with its exact grace note", m => m.Lead(1, 0.3f, 0.75f, 1, 1f, ToneKind.Long)));

        // ── KH · the Kingdom of Horror · scratchboard · D Hungarian minor · 132, 3/4 ─
        Add("KH", "The Kingdom of Horror", "KH", 132f, 3, 2, "3/4, a waltz",
            Bed(5, "a thin theremin and wind", m => m.Bed(new[] { new[] { 0 }, new[] { 4 } }, 4, 0, 0.6f)),
            Pulse(12, "oom-pah-pah on plucked strings", m =>
            {
                m.Pulse(new[] { new[] { 0, X, X, X, X, X }, new[] { 4, X, X, X, X, X } }, -1, ToneKind.Low, 0.9f, 2f, true, 0f);
                m.Pulse(new[] { new[] { X, X, 2, X, 2, X }, new[] { X, X, 4, X, 4, X } }, 0, ToneKind.Low, 0.5f, 1f, false, 0f);
            }),
            Drums(9, "bones and a hollow thump", m => m.Drums("x.....", "..x.x.", "", 0.6f, 0.5f, 0f, 0.5f)),
            Lead(8, "a music box, ending on the theremin", m => m.Lead(1, 0.35f, 0.75f, 2, 1f)));

        // ── TF · the Forgotten · pixel art · A harmonic minor · 126, 6/8 over 3/4 ───
        Add("TF", "The Forgotten", "TF", 126f, 3, 2, "6/8 against 3/4",
            Bed(8, "a triangle bass in three, swamp noise", m =>
            {
                m.Pulse(new[] { new[] { 0, X, 0, X, 4, X }, new[] { 0, X, 0, X, 3, X } }, -1, ToneKind.Low, 0.9f, 1.8f, true, 0f);
                m.TextureLoop(Tex.Air, 0.12f);
            }),
            Pulse(10, "pulse arpeggios in two groups of three", m =>
                m.Pulse(new[] { new[] { 0, 2, 4, 0, 2, 4 }, new[] { 0, 2, 5, 0, 2, 5 }, new[] { 0, 3, 4, 0, 3, 4 } }, 1, ToneKind.Short, 0.6f, 0.9f)),
            Drums(9, "a shift-register kit", m => m.Drums("x.....", "...x..", "x.xx.x", 0.8f, 0.4f, 0.25f, 0.5f)),
            Lead(5, "a pulse lead dithered with its fifth; arpeggio runs in the rests", m => m.Lead(1, 0.3f, 0.7f, 2, 0.5f, ToneKind.Accent, true))).Trim(-3f);

        // ── CC · Cloud Court · cloisonné · C gong pentatonic, high · 96 ─────────────
        Add("CC", "Cloud Court", "CC", 96f, 4, 2, "4/4",
            Bed(3, "a sheng cluster of stacked fourths", m => m.Bed(new[] { new[] { 0 } }, 2, -1, 0.7f)),
            Pulse(5, "yunluo ostinato in fifths and octaves", m =>
                m.Pulse(new[] { new[] { 0, 3, 5, 3, 0, 3, 5, 8 }, new[] { 0, 3, 5, 3, 5, 3, 0, X } }, -1, ToneKind.Short, 0.6f)),
            Drums(6, "finger cymbals, a small drum", m => m.Drums("x.....x.", "", "..x...x.", 0.5f, 0f, 0.45f)),
            Lead(4, "a high flute, glissandi across the strings", m => m.Lead(0, 0.35f, 0.7f, 2, 1f, ToneKind.Long, true)));

        // ── VOID · zone 6 · black · D, no scale · no pulse ──────────────────────────
        Add("VOID", "The VOID", "VOID", 60f, 4, 2, "none (a grid nobody hears)",
            Bed(3, "a sub drone with octave partials, rumble", m =>
            {
                m.Bed(new[] { new[] { 0 } }, 3, 0, 0.9f);
                m.TextureLoop(Tex.Rumble, 0.4f);
            }),
            Stem("cluster", StemRole.Pulse, 5, -5f, 0.3f, 0.6f, "a detuned cluster that sags", m =>
                m.Sprinkle(4, at => m.M.Tone(m.R, at, m.Deg(m.R.Rng.Int(4), 1), m.R.Rng.Range(4f, 7f), 0.8f, ToneKind.Long))),
            Stem("fragments", StemRole.Lead, 7, -3f, 0f, 0.1f, "fragments of eaten realms' motifs, an octave down", m =>
                m.Sprinkle(3, at =>
                {
                    Medium eaten = Medium.All[m.R.Rng.Int(Medium.Count - 1)];
                    int notes = 2 + m.R.Rng.Int(2);
                    for (int i = 0; i < notes && i < eaten.Motif.Length; i++)
                        m.M.Tone(m.R, at + i * 1.4f, eaten.Tonic - 12 + Dsp.ScaleSemis(eaten.Scale, eaten.Motif[i]), 2.2f, 0.7f, ToneKind.Long);
                })));

        // ── The screens between fights: the storybook itself, in watercolour ────────
        MusicProfile menu = Add(Menu, "Title menu", "VK", 84f, 4, 2, "4/4",
            Bed(4, "a pad on D and A, surf", m =>
            {
                m.Bed(new[] { new[] { 0, 3 }, new[] { 0, 4 } }, 2, -1, 0.8f);
                m.TextureLoop(Tex.Water, 0.15f);
            }),
            Stem("pulse", StemRole.Pulse, 6, -6f, 0.2f, 0.4f, "a gentle marimba", m =>
                m.Pulse(new[] { new[] { 0, X, 3, X, 2, X, 3, X }, new[] { 0, X, 4, X, 3, X, 2, X } }, 0, ToneKind.Short, 0.6f)),
            Lead(5, "steel pan: the theme", m => m.Lead(1, 0.25f, 0.8f)));
        menu.Swing = 0.08f;
        menu.RestIntensity = 0.5f;

        MusicProfile map = Add(Map, "The overworld", "VK", 76f, 4, 2, "4/4, walking",
            Bed(3, "a pad on G and D", m =>
            {
                m.Bed(new[] { new[] { 0, 3 } }, 3, 0, 0.7f);
                m.TextureLoop(Tex.Air, 0.12f);
            }),
            Stem("pulse", StemRole.Pulse, 4, -6f, 0.2f, 0.4f, "a walking marimba", m =>
                m.Pulse(new[] { new[] { 0, X, 2, X, 3, X, 2, X } }, 0, ToneKind.Short, 0.55f)),
            Lead(5, "steel pan, sparse", m => m.Lead(1, 0.45f, 0.7f)));
        map.Tonic = 55;                                      // G3
        map.Motif = new[] { 3, 2, 0, 2 };
        map.RestIntensity = 0.35f;

        MusicProfile shelter = Add(Shelter, "A shelter", "VK", 60f, 4, 2, "4/4, still",
            Bed(2, "a warm pad", m => m.Bed(new[] { new[] { 0, 3 }, new[] { 0, 2 } }, 1, -1, 0.8f)),
            Stem("pulse", StemRole.Pulse, 3, -7f, 0.2f, 0.4f, "a low mallet, two notes a bar", m =>
                m.Pulse(new[] { new[] { 0, X, X, X, 3, X, X, X } }, 0, ToneKind.Low, 0.5f, 3f, false, 0f)),
            Lead(4, "a lullaby on the pan, half rests", m => m.Lead(1, 0.5f, 0.65f, 2, 2f, ToneKind.Long)));
        shelter.Tonic = 60;                                  // C4
        shelter.RestIntensity = 0.3f;

        MusicProfile ev = Add(Event, "A “?” node", "VK", 92f, 4, 2, "4/4",
            Bed(3, "a suspended pad", m => m.Bed(new[] { new[] { 0, 2 } }, 3, -1, 0.7f)),
            Stem("pulse", StemRole.Pulse, 4, -6f, 0.2f, 0.4f, "plucked ticks", m =>
            {
                m.Pulse(new[] { new[] { 0, X, 3, X, X, 2, X, X } }, 0, ToneKind.Short, 0.5f);
                m.Drums("", "", "..x...x.", 0f, 0f, 0.3f);
            }),
            Lead(5, "a question on the pan", m => m.Lead(1, 0.4f, 0.7f)));
        ev.Tonic = 64;                                       // E4
        ev.Scale = new[] { 0, 2, 5, 7, 10 };                 // suspended: it does not say major or minor
        ev.Motif = new[] { 0, 2, 1, 3 };
        ev.RestIntensity = 0.45f;
    }

    /// <summary>The profile for a realm code as an arena names it; an arena that names no
    /// world is the VOID from node level 5 up, and home otherwise.</summary>
    public static MusicProfile ForArena(string world, int nodeLevel)
    {
        MusicProfile named = Get(world);
        if (named != null && named.RestIntensity < 0f) return named;
        return nodeLevel >= 5 ? Get("VOID") : Get("VK");
    }
}

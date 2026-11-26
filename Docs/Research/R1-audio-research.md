# R1 — Audio research digest

Input for the Audio GDD and tonight's implementation. Written 2026-11-25 against
`Godot.NET.Sdk/4.7.0` (the upstream `4.7` branch head is 4.7.3-rc — [verified: G23]).

> **Research-tool status — read first.** `WebSearch` and `WebFetch` were loaded but **failed on
> every call** (their helper model is not available on this machine's model routing), so this
> session had **no search engine**. I fell back to plain HTTPS reads of URLs I already knew and
> read the raw text myself, inside the fetch budget the coordinator set. The result is uneven
> and you should read it that way:
> - **Godot facts are verified in depth** — 4.7-branch class XML, engine C++ source, docs
>   source, an open-issue search, and the GodotSharp 4.7.0 API installed on this machine.
> - **Shipped-game practice is verified only where a stable URL answered** — four Wikipedia
>   articles, the jsfxr source, one Unity manual page. FMOD's docs returned only a JavaScript
>   shell (no content). No GDC talk, dev blog or postmortem was reached.
> - **Everything else is from my own knowledge** and is labelled.
>
> **Marks.** `[verified: Kn]` = I fetched and read source `Kn` this session (key → URL in §6).
> `[from memory]` = not fetched; confidence is given (hi / mod / low) where it matters.
> **Proposal** = my design recommendation, not a sourced fact. No quotation here is invented.
> A number without a `verified` mark is a convention or an estimate and says so.

---

## 1. Verdict in ten lines

1. One `AudioManager` autoload; five buses built in code (`Master / Music / SFX / Ambience / UI`),
   a hard limiter on Master, a low-pass slot on Music and on SFX.
2. Every sound is a **named event** resolved through a fallback chain
   (`event@realm → event → generic`). An event's source is a synthesis **recipe** tonight and a
   **file** later; gameplay code never names an asset.
3. Tonight everything is **synthesised in C# at load time** into 16-bit mono `AudioStreamWav`
   (22.05 kHz default), cached per realm and per character. Nothing on disk, no import step.
4. SFX: 2–4 seed-mutated variants per event, shuffle-no-repeat, small pitch/volume jitter,
   transient/body/tail layering; played through one `AudioStreamPolyphonic` per bus with our
   own instance caps, cooldowns and priority stealing (the engine has none for this path).
5. Music: a per-realm `MusicProfile` (tonic, scale, tempo, palette, medium rule) is bounced,
   seeded, into 3–4 looping stems of **unequal bar lengths** inside one
   `AudioStreamSynchronized`; an `intensity` parameter fades stems; explore/combat/boss are states.
6. We own the clock (system time + mix/latency compensation). It publishes beat/bar events,
   quantises stingers, and drives a sparse live note layer so actions can sound in the realm's key.
7. Style rule: **a realm is a synthesis medium, not a scale** — each world fixes allowed
   generators, envelopes, space and a forbidden list (§4.7). Characters keep their home-world
   timbre everywhere and are pitched to the current realm's tonic.
8. Mix states are snapshots (pause, underwater, low HP, black hole, VOID): tweened bus gain +
   low-pass cutoff.
9. Nobody can listen tonight, so ship a headless **AudioAudit** (peak, level window, DC, edge
   clicks, loop seams, memory) and a WAV-dump tool — the audio twin of `TileSetAudit`.
10. Upgrade path: drop a file named after the event id → it replaces the recipe; commissioned
    music arrives as Ogg stems carrying BPM metadata → move states to `AudioStreamInteractive`
    (which only beat-syncs streams that report BPM — §2.5); then sidechain, positional players,
    real-time generator voices.

---

## 2. Findings

### 2.1 Adaptive / interactive music as shipped

| # | Technique | Who shipped it | Mechanism → what drives it | Authoring cost | Source | Fableland |
|---|---|---|---|---|---|---|
| 1 | Tempo as threat | Space Invaders (Taito, 1978) | a four-note ostinato speeds up as the aliens descend → threat proximity | none | [verified: W1] | **Adapt** — bounced stems have fixed tempo; use re-bounce at a higher BPM for boss phases later |
| 2 | Vertical layering ("vertical orchestration") | Dead Space 2 (2011): the music "appears to be arranged into four layers", each a stereo track for one level of "fear", mixed from game variables such as distance to enemies [verified: W1]. Super Mario 64 Dire Dire Docks, Yoshi's bongos, Banjo-Kazooie [from memory, hi] | synchronised stems; layers are "generally faded in and out" [W1] → a continuous parameter | N stems that must work in every combination | W1 | **Adopt** — the core of tonight's music (`intensity` → stem gains) |
| 3 | Horizontal re-sequencing | iMUSE, LucasArts, first used in Monkey Island 2 (1991) [verified: W1]. Variants named by W1: crossfade, **phrase branching** (switch when the phrase ends), dedicated **bridge** transitions | switch segment at a musically legal point → game state | transition matrix grows with states²; bridges are extra music | W1 | **Adapt** — seven states, our own conductor, no bridges; stingers cover seams |
| 4 | Stingers | middleware concept: short one-shot fired on the next beat/bar/cue | game event → quantised one-shot | cheap | [from memory, hi] | **Adopt** |
| 5 | Modular riff sequencing | DOOM (id Software, 2016; Mick Gordon — GDC 2017 talk "DOOM: Behind the Music") | music written as short interchangeable pieces grouped by combat intensity; the engine picks the next piece, so a fight does not replay one fixed order → combat state | high (composing for interchangeability) | [from memory, mod] | **Adapt** — generative phrase cells per layer are the cheap version |
| 6 | Alternate arrangement at the same position | FTL (2012): explore/battle versions crossfade [from memory, hi]. NieR:Automata (2017): quiet/full/vocal versions and an 8-bit version for hacking, all aligned [from memory, mod]. Super Mario Odyssey 8-bit pipes [from memory, hi]. Transistor Turn(): low-pass + hummed layer [from memory, hi] | crossfade between synchronised renditions → game state | every piece × N arrangements | memory | **Adopt** — nearly free for us: re-bounce the same patterns with another palette (a chip rendition of any realm for Pixolotl's SOBRECARGA) |
| 7 | Procedural percussion | Ape Out (Gabe Cuzzillo; music system by Maxi Boch; 2019) | "draws from a bank of thousands of recorded individual drum sounds … and combines them according to player movement"; intensity rises with more enemies, a cymbal crashes on each kill, volume follows speed and kill count, and the on-screen location selects the drum/cymbal at that place on a real kit; each chapter is a different percussion style | a big one-shot bank + rules; no composed tracks | [verified: W2] | **Adapt** — "kill = crash" and "screen position = instrument" cost nothing with a synth kit; put them in the live note layer |
| 8 | Embedded generative engine | Spore (Maxis, 2008): an embedded Pure Data patch generates music from game phase, creature-editor actions and session length [verified: W1]. No Man's Sky (2016, 65daysofstatic + Paul Weir): recorded fragments recombined by context [from memory, mod]. Mini Metro (2015, Disasterpeace) [from memory, mod] | rules + material → game phase/context | system building; hard to art-direct | W1 / memory | **Adapt** — a seeded phrase grammar per realm |
| 9 | Sparse fragments and silence | Zelda: Breath of the Wild field piano [from memory, hi]. Minecraft: pieces separated by long silences [from memory, hi]. Untitled Goose Game: Debussy cut into hundreds of fragments at two energy levels [from memory, mod] | timers / activity | cheap | memory | **Adopt** for explore and cleared states (lead rests ≥ 50%) |
| 10 | Actions snap to the beat | Hi-Fi Rush (Tango Gameworks, 2023) | "Attacking on rhythm is not required, as actions automatically sync up with the music"; on-beat presses earn more damage; hero, enemies and environment move to the beat; each division is built on one musical style | very high — every animation is authored in beats | [verified: W3] | **Skip** as a rule; keep the hook (`Beat` event) |
| 11 | SFX delayed onto the beat | Rez, Extase | sound effects are synchronised with the background music: "This is done by delaying playback of the sound effects after they're triggered by the player" | cheap | [verified: W1] | **Adapt** — delay only a musical tail, never the hit transient (a fighter must answer the input) |
| 12 | The player builds the score | Sound Shapes: collected coins *are* the notes; the track intensifies as you collect [verified: W1]. Peggle: each peg in a shot plays the next note up a scale [from memory, hi]. Killer Instinct (2013) combo layers [from memory, mod] | success count → pitch / layer | cheap with a scale table | W1 / memory | **Adopt** — combo ladder: hit *n* plays scale degree *n* |
| 13 | A meter unlocks layers | Metal: Hellsinger (2022): the Fury multiplier adds layers, vocals at the top [from memory, hi]. Crypt of the NecroDancer (2015): move on the beat; the shopkeeper sings a proximity layer [from memory, hi]. BPM: Bullets Per Minute (2020) [from memory, hi] | performance meter → stem gain | stems | memory | **Adapt** — ult charge or combo meter → lead layer |
| 14 | One theme, re-voiced per place | Banjo-Kazooie (1998): channels of one sequence fade per sub-area [from memory, hi]. Hollow Knight (2017) [from memory, mod] | location → instrumentation | one composition × N palettes | memory | **Adopt** — one Fableland motif stored as scale degrees and voiced by every realm; five character motifs likewise |
| 15 | Middleware model | Wwise: segments, playlist and switch containers, transition rules (leave at next beat/bar/cue; enter at start/same time), stingers. FMOD: timeline, transition regions, parameters, snapshots | — | vocabulary | [from memory, hi] (FMOD docs unreachable — F1) | **Adopt as vocabulary** (§4.2) |
| 16 | Engine-native equivalents | Godot `AudioStreamInteractive` / `Synchronized` / `Playlist` | see §2.5 | — | [verified: G1–G3, G15, G16] | Synchronized **Adopt**; Interactive and Playlist **later** |

Sources disagree: the brief and older credits attribute Ape Out's drums to Matt Boch; Wikipedia
today credits Maxi Boch [W2]. I could not check DOOM, NieR, Hades or BOTW against a primary source.

### 2.2 How roguelikes and long-session games avoid music fatigue

| Game | What it does | Confidence | Lesson |
|---|---|---|---|
| Hades (Supergiant, 2020; Darren Korb; FMOD) | a track carries across chambers; the arrangement thins when a chamber is cleared and returns on the next encounter; bosses have their own themes | [from memory, mod] | combat/cleared as a **layer change, not a track change** |
| FTL (2012; Ben Prunty) | every piece exists as explore and battle versions of the same length, crossfaded | [from memory, hi] | two aligned arrangements double perceived variety |
| The Binding of Isaac: Rebirth (2014; Ridiculon) | layered tracks; extra layers enter in fights | [from memory, mod] | layers follow room intensity |
| Enter the Gungeon (2016; doseone) | one theme per floor with a calmer cleared-room rendition | [from memory, mod-low] | same |
| Slay the Spire (2019; Clark Aboud) | several battle tracks per act drawn at random; separate elite and boss themes; ambient map | [from memory, mod] | a small pool beats one loop |
| Risk of Rain 2 (2020; Chris Christodoulou) | long through-composed stage tracks; separate teleporter/boss music | [from memory, mod] | loop length is itself an anti-fatigue tool |
| Dead Cells (2018; Yoann Laulan) | one long loop per biome; variety comes from the route | [from memory, mod-low] | world variety carries the load — Fableland has eight worlds |
| Spelunky 2 (2020; Eirik Suhrke) | several variants per area, chosen per level | [from memory, mod] | re-roll the arrangement on every entry |
| Minecraft (C418) | music arrives at random intervals with long gaps | [from memory, hi] | **silence is content** |
| Ape Out | gameplay music is assembled from single drum hits by rules, not played from a track (one licensed song closes the game) | [verified: W2] | rules over loops |
| Vampire Survivors | one track per stage, player-selectable | [from memory, low] | give the player the dial |

Distilled rules — practice, not a single source:
1. Never loop one fixed arrangement: seed the bounce per node, so every arena hears a new take.
2. Use loops whose lengths do not divide each other (Eno's *Music for Airports* tape loops —
   [from memory, hi]); stems of 4, 6 and 7 bars repeat exactly only every 84 bars.
3. Leave the percussion and the lead out when nothing is happening.
4. Change arrangement on state (explore / combat / cleared / boss); spend novelty on bosses.
5. Make the top line non-repeating (phrase pool + rests) over a repeating bed.
6. Tie something to the player's own actions (rows 7, 12): self-made variation does not tire.
7. Ship Music and SFX sliders and a mute from day one.

### 2.3 SFX variation and mix management

| Technique | Convention / numbers | Source | Fableland |
|---|---|---|---|
| Random container | pick one of N; modes random / shuffle / sequence; "avoid the last *k*"; weights | Wwise random container, FMOD multi-instrument [from memory, hi] | **Adopt** — implement in the manager |
| Engine-native version | `AudioStreamRandomizer`: modes `RANDOM_NO_REPEATS` (default), `RANDOM`, `SEQUENTIAL`; per-stream weight; `random_pitch` is a **ratio** (1.0 = none, 2.0 = half…double) linked to `random_pitch_semitones` (0 = none); `random_volume_offset_db` is ± (3.0 → −3…+3 dB) | [verified: G4] | **Adapt** — copy its parameter names; we select ourselves because we also need scale-quantised pitch and voice limits. Still useful later as the *file* behind an event (a designer-made `.tres`) |
| Pitch/volume jitter | frequent impacts and footsteps: ±1–2 semitones, ±1–3 dB; weapons and signature moves: ≤ ±0.5–1 st; tonal or musical sounds: ≤ ±20–50 cents or quantised to the scale; 3–8 variants for anything heard every second; never the same variant twice running | [from memory — practitioner convention; tutorials range from ±5% to ±2 st, no authority] | **Adopt**, table in §4.3. `pitch_scale = 2^(st/12)`; ±1 st = ×0.944…1.059 |
| Layered one-shots | transient + body + tail picked independently: 3 × 3 × 3 assets = 27 results | [from memory, hi] | **Adopt** — in synthesis each layer is one generator |
| Per-event concurrency | Wwise "limit sound instances" (per object/global; kill or virtualise; discard oldest/newest on ties). FMOD max instances + stealing (oldest, quietest, virtualise, furthest, none) + cooldown | [from memory, hi] (F1 unreachable) | **Adopt** — `MaxInstances`, `Steal`, `MinInterval` per event |
| What Godot gives | `AudioStreamPlayer.max_polyphony` (default 1): extra `play()` calls "cut off the oldest sounds". `AudioStreamPlaybackPolyphonic.play_stream` returns `INVALID_ID` when `polyphony` (default 32) voices are busy — **no stealing** | [verified: G8, G5] | we must write the voice manager |
| Real vs virtual voices | Unity: Max Real Voices = how many play at once, "At every frame, the loudest voice is picked"; virtual voices are tracked but silent | [verified: X2]; defaults 32 real / 512 virtual are [from memory] | **Adapt** — cap 24 SFX voices; drop or steal by priority, then loudness |
| Priority | telegraphs and player feedback outrank ambience; a gameplay tell is never stolen | [from memory, hi] | **Adopt** — matches `50-ART-AUDIO.md` §3 ("the telegraph is a gameplay promise") |
| Ducking / sidechain | Godot's compressor takes another bus as detector: "common in video game mixing to decrease the volume of music and SFX while voices are being heard"; `attack_us` 20–2000 µs, `release_ms` 20–2000 (default 250), `ratio` 1–48 (default 4), `threshold` −60…0 dB | [verified: G12] | **Adapt** — scripted duck tonight (deterministic, debuggable); sidechain later |
| HDR audio | the loudest sound sets a moving window; sounds below it are culled (DICE Frostbite; a Wwise bus feature) | [from memory, mod] | **Skip** — too few sources to need it |
| Importance system | Overwatch (GDC 2016, "The Elusive Goal: Play by Sound"): rank threats, mix by rank | [from memory, mod] | **Skip**; the `Priority` field is the hook |
| Low-pass on state | pause, underwater and low-HP muffling by sweeping a bus low-pass | [from memory, hi]. Godot filter: `cutoff_hz` 1–20500 (default 2000), slopes 6/12/18/24 dB, `resonance` 0–1 [verified: G12]. `Area2D.AudioBusOverride` exists and the C# docs give the water example for `AudioStreamPlayer2D.AreaMask` [verified: G22] | **Adopt** as snapshots (§4.5) |
| Loudness target | Sony ASWG-R001: −24 LKFS home console, −18 LKFS portable; mobile titles are usually mastered hotter | [from memory, mod-hi; not fetched] | **Adapt** — no LUFS meter tonight: normalise each generated sound to a per-category peak, keep the Master limiter |
| Safety limiter | `AudioEffectHardLimiter`: `ceiling_db` −0.3 default ("prevents potential inter-sample peaks"), `pre_gain_db`, `release` 0.1 s; recommended on Master. The older `AudioEffectLimiter` is marked deprecated in its own docs | [verified: G12] | **Adopt** HardLimiter on Master |

Vocabulary to copy (Wwise/FMOD terms → ours):

| Middleware term | Meaning | Fableland equivalent |
|---|---|---|
| Event | named request; the mix decides what plays | `Audio.Post(id, …)` |
| Game parameter / RTPC | continuous value mapped by curves | `Audio.SetParam(Intensity, 0.7f)` |
| State | global, exclusive mode | `Audio.SetState(MusicState.Combat)` |
| Switch | per-emitter variant choice | the `@realm` suffix in event resolution |
| Snapshot | a mixer preset blended in and out | `Audio.PushSnapshot(Underwater)` |
| Stinger | quantised musical one-shot | `Audio.Stinger(id, Quantize.Bar)` |
| Random container | variant pool | `SoundEventDef.Variants` + `Select` |

### 2.4 Procedural / synthesised game audio

**The sfxr family.** sfxr (DrPetter, written for Ludum Dare in 2007), bfxr (increpare — more
waveforms, parameter locking, a mixer), jsfxr (browser port), ChipTone (SFB Games), rFXGen
(raylib) [history from memory, hi]. The parameter model below is read from the jsfxr source
[verified: X1]; all parameters are normalised to 0…1, or −1…1 where signed.

| Group | Parameters | Mapping in the source |
|---|---|---|
| Wave | `SQUARE, SAWTOOTH, SINE, NOISE`; rendered with 8× oversampling | noise is a 32-sample table refreshed every period |
| Envelope | attack, sustain, **punch**, decay | each length = p² × 100 000 samples (≤ 2.27 s at 44.1 kHz); in sustain the gain is `1 + (1 − t)·2·punch` |
| Pitch | base_freq, freq_limit, freq_ramp (slide), freq_dramp (delta slide) | Hz = 8 × 44100 × (p² + 0.001) / 100, so p = 0.3 ≈ 321 Hz |
| Vibrato | strength, speed | sine on the period |
| "Change" (arpeggio) | arp_mod, arp_speed | one pitch jump after a delay — the coin "di-DING" |
| Duty | duty, duty_ramp | square wave only |
| Repeat | repeat_speed | restarts slide and arpeggio periodically |
| Flanger | pha_offset, pha_ramp | 1024-sample delay buffer |
| Filters | lpf_freq, lpf_ramp, lpf_resonance; hpf_freq, hpf_ramp | lpf w = p³ × 0.1; hpf = p² × 0.1 |
| Output | sound_vol 0.5, sample_rate 44100, sample_size 8 | — |
| Presets | pickupCoin, laserShoot, explosion, powerUp, hitHurt, jump, blipSelect, synth, tone, click, random, **mutate** | a preset is a set of randomised ranges; `mutate` nudges parameters by up to ±0.05, each with probability ½ |

→ **Adopt** the model as one generator (`Sfxr`), and adopt `mutate` as the variant maker: an
event stores one recipe and the loader derives its variants from seeds.

| Method | How it works | Cost / numbers | Source | Fableland |
|---|---|---|---|---|
| Karplus–Strong pluck | a noise burst of L samples circulates in an L-sample delay line through a low-pass (the original averages two adjacent samples); delay D = Fs / F0; fractional delay by linear interpolation for tuning; the demo there uses loop gain 0.98; "Increasing the period sharply after the transient input produces drum-like sounds" | one add and one multiply per sample | [verified: W4] | **Adopt** — guzheng, pipa, oud, ukulele, twang guitar, harpsichord, honky-tonk |
| 2-operator FM | a sine modulates a sine's phase; ratio sets harmonicity, index sets brightness; decaying index = struck sound; inharmonic ratios (Chowning's bell uses carrier:modulator 1:1.4) give bells and gongs | two sines per sample | [from memory, hi] | **Adopt** — bells, celesta, gongs, glass, steel-pan-like |
| Modal bank | a sum of ≤ 8 exponentially decaying sines at measured mode ratios. Free bar 1 : 2.756 : 5.404 : 8.933 (computed from the beam equation); a tuned marimba bar ≈ 1 : 4 : 10; xylophone ≈ 1 : 3 : 6; church-bell partials 0.5 : 1 : 1.2 : 1.5 : 2 | ≤ 8 sines per sample | [from memory, hi] | **Adopt** — marimba, woodblock, log drum, bianzhong, bowl, pumpkin, bottle |
| Filtered noise | noise through a swept band-pass with an envelope; slow random-walk centre for wind; 0.1–0.2 Hz amplitude LFO for surf; impulses for crackle | a few operations per sample | [from memory, hi] | **Adopt** — slashes, wind, surf, fire, breath |
| Chirps | a sine with a frequency sweep and decay: bubbles, drops, kick drums (150 → 50 Hz) | one sine per sample | [from memory, hi] | **Adopt** |
| Tracker music | pattern data plus tiny instrument samples (MOD/XM lineage); PICO-8: 4 channels, 8 waveforms, 64 SFX of 32 notes | whole soundtracks in tens to hundreds of KB | [from memory, hi] | **Adapt** — our stems are a tracker bounced at load |
| Real-time DSP graph | Spore's embedded Pure Data | a runtime of its own | [verified: W1] | **Skip** — C# cannot override the engine's mix callback (§2.5) |

Who shipped synthesised or generative audio, and the lesson: Ape Out (rules over loops — W2);
Spore (generative needs strong constraints to stay musical — W1 for the fact, the lesson is
mine); PICO-8 games and the sfxr jam tradition (a narrow, consistent palette reads as *style*
rather than as cheapness — [from memory]); No Man's Sky's procedurally voiced creatures [from
memory, mod]. The transferable lesson for a team that cannot listen tonight: constrain the
material (one scale, one tonic, few generators per realm) so no combination can be ugly.

Scales as semitone sets [from memory, hi — music theory]:

| Name | Semitones | Use |
|---|---|---|
| gong 宫 (major pentatonic) | 0 2 4 7 9 | Beach, Cloud Court |
| shang 商 | 0 2 5 7 10 | alternative Chinese colour |
| zhi 徵 | 0 2 5 7 9 | alternative |
| yu 羽 (minor pentatonic) | 0 3 5 7 10 | Wulin; Ghost Town melody |
| yayue heptatonic (= Lydian) | 0 2 4 6 7 9 11 | Loong's Nest (court scale: pentatonic + two *bian* tones) |
| Phrygian dominant (Hijaz-like) | 0 1 4 5 7 8 10 | Starfield, Cleopastar |
| double harmonic | 0 1 4 5 7 8 11 | Cleopastar's ultimate |
| Dorian | 0 2 3 5 7 9 10 | Ghost Town harmony |
| Aeolian / harmonic minor | 0 2 3 5 7 8 10 / …11 | Dungeon; Forgotten City |
| Hungarian minor | 0 2 3 6 7 8 11 | Dark Wood |

Cheap recipes for culturally flavoured timbres — **proposals from sound-design practice**
[from memory]; every value is a starting point to be tuned by ear:

| Target | Recipe |
|---|---|
| Guzheng / pipa | KS pluck, excitation low-passed at 4–6 kHz, decay 1.2–2.5 s; **pitch bend after the attack** (+2–3 st settling in 80–200 ms) and delayed 5–6 Hz vibrato; pipa tremolo = re-pluck at 12–16 Hz; glissando = 5–8 plucks 30–45 ms apart down the scale |
| Dizi / xiao | sine + weak 2nd/3rd harmonic, band-passed breath noise (1.5–4 kHz) at −18 dB, attack 40–80 ms opened by a +2 st grace flick, vibrato fading in after 250 ms; dizi adds a buzz (soft-clipped copy, high-passed 2 kHz) |
| Bianzhong / yunluo / bells | modal partials ≈ 1 : 2.0 : 2.4–3.0 : 4.2 : 5.4 with decays 0.3–2.5 s; or FM 1:1.4 with a decaying index |
| Opera gong | inharmonic FM/modal + **pitch glide** (small gong up, large gong down, 3–8% over 300 ms) + a noise crash |
| Woodblock / muyu / bangu | two modes (1 : 2.6), decay 40–90 ms, plus a click |
| Oud / qanun | KS, darker excitation (2.5 kHz), decay 0.6–1.2 s, tremolo picking 10–12 Hz, grace notes from the ♭2 |
| Ney | xiao with more breath (−12 dB) and slow scoops into notes |
| Darbuka | *doum*: sine chirp 140 → 70 Hz, 180 ms; *tek*: three modes at 0.9–2.5 kHz + a high-passed tick, 40 ms; pattern maqsum `D T – T D – T –` |
| Sistrum / finger cymbals | three 30 ms bursts of noise high-passed at 6 kHz; cymbals: FM 1:1.48 around 3–5 kHz, decay 0.6 s |
| Chiptune | pulse at 12.5/25/50% duty, triangle bass, LFSR noise drums, 50–60 Hz arpeggios instead of chords, 16 volume steps, no reverb |
| Clay flute | sine/triangle + breath, 15 ms attack, calls that end on a falling minor third |
| Huehuetl / teponaztli | chirp tom 110 → 80 Hz, 250 ms; log drum = two modal pitches a fourth apart, decay 150 ms |
| Organ | additive harmonics 1, 2, 3, 4; 30 ms attack with a 20 ms noise chiff; 6 Hz tremulant |
| Theremin | sine + a little 2nd harmonic, 80–150 ms portamento, vibrato 5.5–6.5 Hz ±35 cents |
| Music box / celesta | FM ratio 1:3.5–1:4, index 1.5 → 0, decay 0.8 s, high register |
| Twang guitar | KS, bright excitation (7 kHz), comb for a bridge pick, **tremolo-arm dip** −30 cents recovering in 120 ms, 5 Hz amplitude tremolo, one 110 ms slap echo |
| Harmonica | pulse 30% + saw through a band-pass (1.2–2.5 kHz), bend in from −1 st over 90 ms, ±8-cent chorus |
| Whistle | sine + breath, 5 Hz vibrato, slides between notes |
| Honky-tonk piano | two KS strings ±12 cents + hammer click, decay 0.9 s |
| Marimba | modal 1 : 4 : 10, decays 400/120/50 ms, soft low-passed mallet click |
| Steel pan | additive 1 : 2 : 3 (+4), upper partials entering 10–20 ms late, 15-cent pitch drop over 60 ms, decay 0.7 s |
| Ukulele | KS, dull excitation, decay 0.5–0.9 s; strum = four strings 12–25 ms apart |
| Shaker / surf / wind | 35 ms bursts high-passed at 5 kHz; surf = noise low-passed 400 → 2500 Hz by a 0.12 Hz LFO; wind = band-pass 300–1200 Hz on a random walk |
| Bubbles | sine chirp f × (1 → 1.6) in 40–90 ms with exponential decay, clustered 20–120 ms apart |
| Glass / star | FM 1:2.76, or two sines a fifth apart at 2–5 kHz, decay 1.5 s + three echo taps |
| Singing bowl | modal 1 : 2.71 : 5.2, each partial doubled ±0.4 Hz so it beats, decay 4 s |
| Fire / lava | low-passed brown noise + 6–20 crackle impulses a second (band-pass 1–4 kHz, 2–8 ms); lava = brown noise under 200 Hz + bubble chirps 60 → 120 Hz |

### 2.5 Godot 4.7 specifics

All rows are **[verified]** against the 4.7 branch unless marked. C# names were checked in the
GodotSharp 4.7.0 package on this machine [G22].

| Class | Verified facts | Source | Verdict |
|---|---|---|---|
| `AudioStreamWAV` (C# `AudioStreamWav`) | "can also be used to store dynamically-generated PCM audio data". Properties: `data` (bytes), `format` (`FORMAT_8_BITS` = signed 8-bit, `FORMAT_16_BITS`, `FORMAT_IMA_ADPCM`, `FORMAT_QOA` = "data from a full QOA file"), `mix_rate` (44100), `stereo` (false), `loop_mode` (disabled / forward / pingpong / backward), `loop_begin`, `loop_end` in sample frames. Static `load_from_buffer(bytes, options)` and `load_from_file(path, options)` parse a WAV container at runtime (options = importer keys such as `force/max_rate`). `save_to_wav(path)` writes a file but not for ADPCM/QOA. C# enums: `FormatEnum.Format16Bits`, `LoopModeEnum.Forward` | G7, G22 | **Adopt** — the core of tonight |
| — from source | with looping on, the mixer wraps at `loop_end` as given: **`loop_end = 0` is an empty loop**, there is no "end of data" default at runtime. `set_data` takes the audio server lock. IMA ADPCM cannot seek and forces forward loops. `is_monophonic()` is false, so one stream can play many times at once | G14 | set `LoopEnd` yourself; build at load, not in combat |
| `AudioStreamPolyphonic` | `polyphony` default 32. `play_stream(stream, from_offset = 0, volume_db = 0, pitch_scale = 1.0, playback_type, bus)` → integer id, or `INVALID_ID` (−1) when every voice is busy; `set_stream_volume`, `set_stream_pitch_scale`, `stop_stream`, `is_stream_playing`. The playback object exists only after the player has the stream **and** is playing. C# ids are `long` | G5, G8, G22 | **Adopt** for SFX and live notes |
| `AudioStreamSynchronized` | up to 32 sub-streams that "begin at exactly the same time"; ends when the last ends; "If one of the sub-streams loops, then playback will continue"; per-stream `set_sync_stream_volume(i, db)` | G2 | **Adopt** for stems |
| — from source | each sub-playback is mixed on its own (so unequal loops drift against each other by design); the volume is read once per mix block and applied **without a ramp** | G16 | fade in small steps |
| `AudioStreamInteractive` | clips + a transition table: `add_transition(from, to, from_time, to_time, fade_mode, fade_beats, use_filler_clip, filler_clip, hold_previous)`; from: immediate / next beat / next bar / end; to: same position / start / previous position; fades: disabled / in / out / cross / automatic; `CLIP_ANY = −1`; per-clip auto-advance. Playback: `switch_to_clip(index)`, `switch_to_clip_by_name(name)`, `get_current_clip_index()` | G1, G22 | **Later** |
| — from source | beat/bar timing is used **only if the source clip reports a BPM** (`if (from_state.stream->get_bpm())`); otherwise the transition is immediate and `fade_beats` is taken as **seconds**. The base `AudioStream::get_bpm()` returns 0 unless a script implements `_get_bpm`. Only `AudioStreamOggVorbis` and `AudioStreamMP3` expose `Bpm / BeatCount / BarBeats`; `AudioStreamWav` does not. `get_playback_position()` returns 0.0 for interactive playback | G15, G17, G22, G8 | runtime WAV clips get no quantisation |
| `AudioStreamPlaylist` | up to 64 streams, `shuffle`, `loop` (true), `fade_time` 0.3 s ("Streams are expected to have an extra bit of audio after the end to help with fading") | G3 | **Later** — for recorded track pools |
| `AudioStreamRandomizer` | see §2.3 | G4 | **Adapt** |
| `AudioStreamGenerator` | `buffer_length` 0.5 s, `mix_rate` 44100, `mix_rate_mode` default custom; "best used from C# or from a compiled language via GDExtension"; `push_buffer` is "usually more efficient than `push_frame` in C#". C# has `PushBuffer(Vector2[])` and `PushBuffer(ReadOnlySpan<Vector2>)` | G6, G22 | **Skip tonight** |
| — from source | the ring buffer is rounded to a power of two (`nearest_shift(rate × length)`); `push_buffer` returns false and writes **nothing** if the whole array does not fit; an underrun is filled with zeros and counted in `get_skips()`; `clear_buffer()` errors while playing; the stream is monophonic | G13 | whatever you keep queued *is* your latency |
| Custom streams | `AudioStreamPlayback._mix`: "It is not useful to override this method in GDScript or C#. Only GDExtension can take advantage of it" — and the C# class has no `_Mix` to override | G10, G22 | no C# DSP in the mix thread |
| `AudioStreamPlayer` | `max_polyphony` 1 (further `play()` calls cut the oldest); setting `stream` stops everything; an unknown `bus` name silently falls back to Master; `stream_paused` flips automatically when the node is paused; `get_stream_playback()` "fails and returns an empty playback" if nothing is playing | G8 | see §5 |
| `AudioServer` | `get_time_since_last_mix()`, `get_time_to_next_mix()`, `get_output_latency()` ("can be expensive; it is not recommended to call … every frame"), `get_mix_rate()`; `add_bus`, `set_bus_name`, `set_bus_send`, `add_bus_effect`, `set_bus_effect_enabled`, `set_bus_volume_db`, `get_bus_index` (−1 if absent), `set_bus_layout`; `lock()`/`unlock()`; `playback_speed_scale` is independent of `Engine.time_scale` | G9, G22 | — |
| Timing | Method A (recommended for pieces of a few minutes): system clock from `play()` minus `get_time_to_next_mix() + get_output_latency()`; it "will slowly drift". Method B (any length): `get_playback_position() + get_time_since_last_mix() − get_output_latency()`, jittery — discard values that go backwards. *The page is flagged `article_outdated` in its own source* | G18 | **Adopt** A; re-anchor at every music start |
| Project settings | `audio/driver/mix_rate` 44100 — "On iOS and macOS, mixing rate is determined by audio driver, this value is ignored"; `output_latency` 15 ms ("ignored on Android"; web 50); `audio/buses/default_bus_layout` = `res://default_bus_layout.tres`; buses switch off below −60 dB for 2 s, saving their effects' CPU; `2d_panning_strength` 0.5; the web default playback type is Sample, where "`AudioEffect`s are not supported" | G11, G9 | — |
| Effects | compressor with `sidechain`; hard limiter; filters; reverb (`room_size` 0.8, `damping` 0.5, `wet` 0.5, `dry` 1, `predelay_msec` 150 within 20–500, `spread`, `hipass`); delay with two taps + feedback; `AudioEffectDistortion.ModeEnum.Lofi`, `AudioEffectChorus`, `AudioEffectPanner.Pan` exist | G12, G22 | — |
| Formats | "WAV … lightweight to play back on the CPU (hundreds of simultaneous voices in this format are fine)"; Ogg Vorbis needs "significantly more processing power"; QOA is the default WAV import compression; a looping stream never emits `finished` | G19 | WAV for SFX, Ogg for future music |

Open engine issues that touch this design — titles from an issue search today, bodies not read
[verified: G21]:

| Area | Open issues |
|---|---|
| Interactive | #110216 "AudioStreamInteractive can't loop clips" (confirmed) · #94538 "Pop and mistiming in seamless transitions" · #117855 "'no fade' transitions leave prior clip running" · #99384 "breaks when modified while already playing" · #119043 `RETURN_TO_HOLD` / `hold_previous` · #105810 `pitch_scale` "does not work" for Interactive and Playlist · #112480 crash on close when extended from script |
| WAV + interactive | #108184 "WAV AudioStream importer lacks BPM support" · #118366 "`AudioStreamWAV` missing `has_loop()` override" |
| Synchronized | #106900 "audible clicks/pops when changing a stream volume" · #101400 "stops playback if 'set_sync_stream' is called while playing" · #100570 "Stream sync play and seek is setting wrong position on certain cases" (regression) |
| Playlist | #95879 "Fading between audio streams is not working" · #103386 `stream_count` not updated · #104527 "ignores pitch_scale" (confirmed) · #109113 wrong advance time when `playback_speed_scale != 1.0` |
| Polyphonic | #118498 "`play_stream` bus argument is ignored" · #111977 changing `polyphony` "doesn't affect existing" playbacks (confirmed) · #123398 `get_playback_position()` always 0.0 · #75089 player does not emit `finished` |
| Generator | #92349 "Crashes with AudioStreamGenerator or c# Theme when game exits" · #46490 "AudioStreamGeneratorPlayback delay" · #76625 Movie Maker does not record procedural audio |
| Output | #124104 "Audio output is floor-truncated to 16 bits without dither" · #109728 web: composite streams + pause → wasm error |

Sources disagree: the docs give `play_stream` a `bus` argument while #118498 says it is ignored;
the class reference presents beat/bar transitions as general while the source applies them only
to BPM-reporting streams; `AudioEffectLimiter`'s description recommends it on Master while one
of its own properties calls the effect deprecated.

### 2.6 Budgets

| Fact | Number | Source |
|---|---|---|
| WAV 16-bit 44 kHz mono | 88 KB per second | [verified: G19] |
| IMA ADPCM / QOA, 44 kHz mono | 22 KB / 17 KB per second | [verified: G19] |
| Ogg Vorbis 96 / 128 kb/s stereo | 12 KB / 16 KB per second | [verified: G19] |
| ⇒ 16-bit **22.05 kHz mono** | **44 KB per second** (bandwidth to 11 kHz) | computed |
| Guidance | "no audible benefit" to 24-bit or above 48 kHz; mono for most SFX; voices at 22 kHz; use bus reverb instead of baking tails | [verified: G19] |
| Voice conventions | Unity defaults 32 real / 512 virtual voices | [from memory] (concept verified: X2) |
| Engine voices | polyphonic default 32 per stream; I found no global voice cap in the class docs I read | [verified: G5]; the absence of a cap is **unverified** |

I have **no verified source** for typical indie or mobile audio budgets; §4.9 gives targets
derived from the figures above, to be measured rather than trusted.

---

## 3. Requirements map

| Requirement | Mechanisms that satisfy it |
|---|---|
| **a) Low resource use** | zero audio on disk tonight (recipes are code) · 22.05 kHz mono 16-bit PCM, 44 KB/s · per-realm and per-character caching with unload on realm change · stems of 4–7 bars, not minutes · WAV voices are the cheapest thing the engine mixes (G19) · hard caps: 24 SFX voices, per-event instances, cooldowns · burst coalescing for 24-seed and 18-bubble volleys · one shared loop per hazard *type* · mono sources, stereo from bus effects · no per-frame allocation, no synthesis during combat (T40 §2) |
| **b) No repetition fatigue** | seed-mutated variants + shuffle-no-repeat + jitter · layer recombination · combo ladder (pitch climbs the scale, resets on drop) · music re-seeded per node · unequal loop lengths · rests and silence in calm states · arrangement follows intensity and state · boss leitmotif layer as a novelty spike · eight worlds = eight sound worlds · sliders and mute |
| **c) Matches the world's style** | medium → synthesis rule table with a forbidden list (§4.7), the audio twin of RealmStyleBible §0 · one tonic and scale per realm · per-realm "space" (reverb/filter preset) · emitter-origin rule: characters and foes carry their *home* palette, tiles and hazards carry the realm's · `@realm` overrides for the few sounds that should change with the place |
| **d) Editable and extensible** | events, parameters, states, snapshots as the only gameplay-facing API · sound and music definitions are data tables (same pattern as `Scripts/Data/*Table.cs`) · recipe **or** file behind each id (drop-in replacement, no code change) · `intensity`, `hp`, `void`, `local_time` parameters already defined though few are wired tonight · `Beat` / `Bar` events out, `Stinger` / `Note` in · character motifs stored as scale degrees so they follow any key · later migration to `AudioStreamInteractive` touches only the music player |

---

## 4. Recommended design for Fableland (proposal)

### 4.1 Event names and resolution

`<category>:<owner>.<action>[.<detail>]`, lower-case; the category prefix keeps the old
`50-ART-AUDIO.md` §7 style (`"sfx:hit_melee"`).

| Category | Examples |
|---|---|
| `sfx:` characters | `sfx:pome.ba.slash1` … `slash3`, `sfx:pome.ba.hit`, `sfx:pome.blush.on`, `sfx:pome.seed.burst`, `sfx:cleo.ba.star_shot`, `sfx:cleo.blackhole.open`, `sfx:pixo.bubble.pop`, `sfx:pixo.rewind.loop`, `sfx:pump.head.bowl`, `sfx:pump.soul.enter`, `sfx:pangda.palm.hit`, `sfx:pangda.bottle.throw` |
| `sfx:` generic fallbacks | `sfx:char.jump`, `sfx:char.land`, `sfx:char.hurt`, `sfx:char.death`, `sfx:char.switch`, `sfx:char.reload`, `sfx:hit.melee`, `sfx:hit.projectile` |
| `sfx:` foes and world | `sfx:foe.crab.claw`, `sfx:foe.seagull.dash_telegraph`, `sfx:hazard.fire.loop`, `sfx:hazard.tsunami.warning`, `sfx:vol.water.enter`, `sfx:vol.foliage.rustle`, `sfx:pickup.core`, `sfx:mission.core_hurt` |
| `ui:` | `ui:click`, `ui:hover`, `ui:confirm`, `ui:back`, `ui:error`, `ui:page` |
| `mus:` | `mus:realm.VK` … `mus:realm.VOID` (profiles), `mus:map`, `mus:shelter` |
| `sting:` | `sting:combat_start`, `sting:clear`, `sting:victory`, `sting:defeat`, `sting:boss_intro`, `sting:day_end`, `sting:void_devour`, `sting:ult.cleo` |
| `amb:` | `amb:realm.HC` |

Resolution order for `sfx:pome.ba.hit` posted in Wulin: `sfx:pome.ba.hit@BM` →
`sfx:pome.ba.hit` → `sfx:char.ba.hit` → `sfx:hit.melee` → silent, logged once. This lets the MVP
ship about twenty generic events and still answer every call. Cache `AudioId` handles in static
fields; do not hash strings per call.

**File override.** At boot, for each id look for
`res://Assets/Audio/<category>/<id with dots as slashes>[.<n>].(wav|ogg|tres)` — for example
`Assets/Audio/sfx/pome/ba/slash1.2.wav`. Files found replace the recipe's variants; ranges,
limits and bus stay in the table. `AudioStreamWav.LoadFromFile` and
`AudioStreamOggVorbis.LoadFromFile` exist for un-imported files [verified: G22].

### 4.2 Gameplay-facing API

```csharp
public static class Audio            // static facade over AudioManager.Instance; null-tolerant; no-op when headless
{
    public static void Post(AudioId id, Node2D at = null, float gainDb = 0f, float pitchSt = 0f);
    public static long PostLoop(AudioId id, Node2D at = null);
    public static void StopLoop(long handle, float fadeSec = 0.15f);
    public static void SetParam(AudioParam p, float value);   // Intensity, Hp, Void, UltCharge, LocalTime
    public static void SetState(MusicState s);                // Map, Shelter, Explore, Combat, Boss, Victory, Defeat
    public static void SetRealm(string abbr, int seed);       // loads palette + bounces stems off-thread
    public static void PushSnapshot(Snapshot s);  public static void PopSnapshot(Snapshot s);
    public static void Stinger(AudioId id, Quantize q = Quantize.Beat);
    public static void Note(AudioId instrument, int degree, int octave = 0);   // degree of the current realm scale
    public static event Action<int> Beat;  public static event Action<int> Bar;
}
```

Scene nodes that subscribe to `Beat`/`Bar` must unsubscribe in `_ExitTree` (KNOWLEDGE v0.5.4).

### 4.3 Sound event data model

```csharp
public sealed class SoundEventDef
{
    public string Id;                       // "sfx:pome.ba.slash1"
    public string Bus = "SFX";              // SFX | UI | Music | Ambience
    public SoundSource[] Layers;            // 1–3 layers (transient/body/tail); each a Recipe or a File
    public int Variants = 3;                // seeds derived per layer via mutate(±5%)
    public SelectMode Select = SelectMode.ShuffleNoRepeat;   // Random | Sequence | ShuffleNoRepeat
    public float VolumeDb, VolumeRandDb = 1.5f;
    public float PitchSt,  PitchRandSt  = 1.0f;
    public PitchMode Pitch = PitchMode.Free;                 // Free | ScaleDegree | ComboLadder
    public int   MaxInstances = 3;  public StealMode Steal = StealMode.Oldest;   // None | Oldest | Quietest
    public float MinIntervalSec = 0.04f;
    public int   Priority = 50;             // 0–100
    public float DuckMusicDb, DuckSec;      // scripted duck, e.g. −5 dB for 0.4 s on an ultimate
    public bool  Loop;  public float FadeInSec, FadeOutSec;
    public float SampleRate = 22050;        // 32000–44100 for glassy, bell-like sounds
    public string Palette;                  // world abbr of the emitter's origin
}
```

Starting values per category — proposal; the ranges follow the convention in §2.3:

| Category | Variants | Pitch ± st | Vol ± dB | Max inst. | Min interval | Priority | Peak target |
|---|---|---|---|---|---|---|---|
| UI | 1–2 | 0.25 | 0.5 | 2 | 50 ms | 80 | −12 dBFS |
| Player BA swing | 3 | 0.7 | 1 | 2 | 40 ms | 70 | −9 |
| Hit confirm | 4 | 1.0 or ladder | 1.5 | 4 | 30 ms | 75 | −6 |
| Player hurt | 3 | 0.5 | 1 | 1 | 120 ms | 85 | −6 |
| Skill cast | 2 | 0.3 | 0.5 | 2 | 100 ms | 80 | −6 |
| Ultimate | 1, layered | 0 | 0 | 1 | — | 95, never stolen | −3 |
| Projectile fire (seeds, bubbles) | 4 | 1.5 | 2 | 3 | 30 ms | 55 | −10 |
| Projectile impact | 4 | 2.0 | 2 | 3 | 30 ms | 45 | −10 |
| Foe telegraph | 2 | 0.3 | 0 | 2 | 80 ms | 90, never stolen | −6 |
| Foe hurt / death | 3 | 1.5 | 2 | 3 | 40 ms | 50 | −9 |
| Land / step | 4 | 1.5 | 2.5 | 2 | 60 ms | 30 | −14 |
| Hazard loop | 1 | slow drift | — | 1 per type | — | 40 | −16 |
| Ambience bed | 1–2 | — | — | 2 | — | 10 | −20 |

Voice manager rules: (1) cooldown check; (2) per-event cap, apply `Steal`; (3) global cap of
24 — steal the lowest priority, then the quietest, and drop the new sound if it is itself the
lowest; (4) **burst coalescing** — more than six posts of one event within 30 ms become a
single dedicated `*.burst` sound (Pome seeds fire 24 at once, Pixolotl keeps 18 bubbles alive —
T40 §1); (5) one loop instance per hazard type, gain set by the nearest instance.

Randomness for variation must not consume gameplay `DetRandom` streams (KNOWLEDGE,
determinism). Use a private generator; where repeatability is wanted (music per node), derive
it with `Rng.Sub("audio")`.

### 4.4 Music model

```csharp
public sealed class MusicProfile
{
    public string Realm;  public int TonicMidi;  public int[] Scale;       // semitone set
    public float Bpm;  public int BeatsPerBar;  public float Swing;         // 0…0.33
    public InstrumentDef[] Palette;                                         // generator recipes
    public LayerDef[] Layers;                                               // bed, pulse, drums, lead
    public StingerDef[] Stingers;  public PhraseGrammar Lead;               // motif cells, rest probability, register
    public string Space;  public float MusicLowpassHz;                      // reverb preset; 0 = off
}
public sealed class LayerDef { public string Name; public int Bars; public PatternGen Gen; public string Instrument;
                               public float BaseDb; public float FadeInAt, FullAt; /* intensity thresholds */ }
```

| Piece | Rule |
|---|---|
| Stems | bounced at realm load on a worker thread (pure C# arrays), then wrapped as looping `AudioStreamWav` on the main thread and placed in one `AudioStreamSynchronized`. Bar counts such as 4 / 6 / 7 so the composite repeats only every 84 bars. Harmony stays on one tonal centre (drone or pedal) so any alignment is consonant. Render tails past the loop end and fold them back onto the start: seamless loops |
| Intensity (0…1) | 0–0.2 bed + sparse lead · 0.2–0.5 + pulse · 0.5–0.8 + drums · 0.8–1 + danger layer / lead up an octave. Source: `0.3·aggro foes (÷4, clamped) + 0.3·recent damage dealt and taken + 0.2·(1 − HP%) + 0.2·mission urgency`, attack 0.4 s, release 4 s, hysteresis 0.05 |
| States | Map, Shelter, Event (bed only, different registers) · Explore · Combat · Boss · Victory · Defeat · VOID |
| Transitions (our own table) | Explore → Combat: on the next bar, stinger on the bar · Combat → cleared: 2 s grace, drums out over two bars · any → Boss: immediate, `boss_intro` stinger covers the seam · → Victory / Defeat: stems out in 0.3 s (defeat adds a 1.5 s low-pass sweep), then the stinger · Arena ⇄ Map: 0.8 s crossfade under the scene fade (the manager is an autoload, so music survives scene changes) |
| Live note layer | a polyphonic player on the Music bus: lead fragments from the phrase grammar with a rest probability, quantised to the conductor's grid; a frame of jitter is harmless for sparse, soft-attack notes. The rhythm section lives in the stems and is sample-accurate |
| Stingers | one-shots on the Music bus fired at the next beat or bar |
| Actions → music | `ComboLadder` (hit *n* = scale degree *n*, reset when the combo drops) · kills fire the realm's "crash" (Ape Out's rule) · ult = duck stems 6 dB and play the character motif in the character's own mode at the realm tonic · `UltCharge` → lead gain · screen x → choice of percussion voice |
| Music → game | `Beat` / `Bar` events for VFX pulses and, later, telegraph timing |
| Boss rule | realm stems + **the boss character's motif as an ostinato in that character's home timbre** — a boss borrowed from another world audibly invades |
| VOID parameter (0…1 over the 45 days) | lowers the Music low-pass 20 kHz → 1.2 kHz, raises a sub drone, removes layers, adds `Lofi` distortion; eaten realms lose their motif on the map screen |
| One tonic family | every realm's tonic is D, A, E, G or C (§4.7) so motifs transpose by fourths and fifths and map crossfades share tones |

### 4.5 Bus layout and snapshots

```
Master   [HardLimiter  ceiling −0.3 dB]
 ├─ Music     [LowPassFilter (off)] [Reverb — realm preset]      players: MusicStems (Synchronized), MusicNotes (Polyphonic)
 ├─ SFX       [LowPassFilter (off)] [Reverb — realm preset]      players: Sfx (Polyphonic 24)
 ├─ Ambience                                                     players: Amb (Polyphonic 4)
 └─ UI        (never filtered; its player keeps running while paused)
```

Built in code at boot, idempotently (`GetBusIndex(name) < 0` → `AddBus`, `SetBusName`,
`SetBusSend`, `AddBusEffect`) — no hand-written `.tres`. Starting gains, **untested by ear**:
Music −8 dB, SFX −3, Ambience −14, UI −6.

| Snapshot | Music | SFX | Trigger |
|---|---|---|---|
| Paused | low-pass 800 Hz, −6 dB | player paused with the tree | pause menu |
| Underwater | low-pass 1.8 kHz | low-pass 1.2 kHz | camera target inside a water `SoftVolume`; Loong's Nest keeps it on permanently |
| Low HP | low-pass 3 kHz | — , plus a heartbeat loop | HP < 25% |
| Black hole | −8 dB, low-pass 1.5 kHz | −3 dB except the black hole | Cleopastar's Apep's Maw |
| VOID | cutoff from the `Void` parameter | — | day clock |

A snapshot is a tween of bus gain and filter cutoff over 0.15–0.6 s; stack them by taking the
lowest cutoff and the sum of gains.

### 4.6 Synthesis toolkit

| # | Generator | Used for |
|---|---|---|
| 1 | `Osc` — sine, triangle, band-limited saw and pulse (PolyBLEP or additive: naive saws alias at 22 kHz) | everything tonal |
| 2 | `Noise` — white, pink, brown, LFSR | breath, wind, drums, chip noise |
| 3 | `Env` — ADSR, exponential decay, sfxr punch | all |
| 4 | `Filter` — one-pole LP/HP, state-variable LP/BP/HP with resonance | whooshes, formants, "underwater" |
| 5 | `KarplusStrong` — damping, brightness, pick position, fractional delay, pitch bend | plucked strings |
| 6 | `Fm2` — ratio, index, index envelope | bells, gongs, glass, celesta |
| 7 | `Modal` — up to 8 modes (ratio, decay, gain) | bars, blocks, bowls, bottles, pumpkin |
| 8 | `Additive` — drawbar partials + formant filter | organ, sheng cluster, pads, choir |
| 9 | `Sfxr` — the full parameter model + presets + `mutate` | UI, pickups, chip palette |
| 10 | `Whoosh` — noise → swept band-pass + envelope | slashes, dashes, wind, fire |
| 11 | `Chirp` — swept sine + decay | bubbles, drops, kicks, toms |
| 12 | `DrumKit` — compositions of 2, 7, 10, 11 | per-realm percussion |
| 13 | `Fx` (offline) — soft clip, bit-crush / rate-reduce, comb / flanger, feedback delay, two-voice detune, tremolo / vibrato LFO | medium rules |
| 14 | `Finish` — DC removal, 3–5 ms edge fades, loop-seam crossfade, peak normalise to the category target, TPDF dither, 16-bit pack | every output |
| 15 | `Bounce` — pattern (degrees, velocity, gate, swing) × instrument → PCM at exact sample positions, with tail fold-back | stems, stingers |

Write scratch buffers once per load and reuse them (arrays above 85 KB land on the large-object
heap); never synthesise inside `_Process` or `_PhysicsProcess`.

### 4.7 Per-world palette — a realm is a synthesis medium (proposal)

The Style Bible defines a medium by "what a mark is, what is forbidden". The audio rule mirrors it.

| World → realm | Medium → sound-making rule | Tonic, scale | Tempo, metre | Bed · pulse · drums · lead | Space | Forbidden |
|---|---|---|---|---|---|---|
| **VK** Beach | *storybook watercolour*: soft edges and bleeding washes → attacks ≥ 8–15 ms, gentle detune, wet blur | D, gong pentatonic (+♯4 as colour) | 104, 4/4, swing 0.12 | surf noise + ukulele strum · marimba ostinato · shaker + low conga · steel pan | small bright room, wet 0.15 | hard square waves, clipping, dry gated sounds |
| **SL** Starfield | *flat poster*: flat planes, hard terminator, no ornament → pure sines and triangles, terraced levels, **no vibrato**, hard gates, ≤ 3 voices at once | A, Phrygian dominant, sparse (0 1 4 7) | 56, free | sub drone + fifth · one *doum* every two bars + sistrum · half-time maqsum in combat only · glass/star bell, a distant ney call every 4–8 bars | huge hall, wet 0.45, room 0.95, long delay | noise textures, busy rhythm, vibrato, clutter |
| **BM** Wulin | *xieyi ink*: one pressed-and-lifted stroke; bare paper is the picture → every note is a gesture (pluck + bend + fading vibrato), ≥ 50% rests, solo lines over at most a drone | E, yu pentatonic | 72, 4/4, loose | faint waterfall noise or nothing · muyu knocks · accelerating bangu rolls in combat · guzheng gestures, dizi answers | dry, wet 0.08, one 280 ms echo | pads, chords, machine-regular rhythm, warm timbres |
| **HC** Dungeon | *lit realism*: weight, relief, one saturated colour (fire) → physical models, low end, long stone reverb; the whole mix is dark and only fire and lava are bright | D (low), Aeolian + tritone | 80, 6/8 | low drone (saw under 400 Hz) + lava rumble · anvil and chain · low drums · choir "ooh" in minor seconds | cave, wet 0.35, damping 0.6; Music low-pass 3.5 kHz | chip waves, cheerful major, bright leads |
| **TD** Ghost Town | *linocut*: a black key block + three flat inks printed out of register, gouge marks, ink skips → **at most three instruments + percussion**, hard-gated notes, terraced levels, 10–25 ms deliberate mis-registration between parts, crackle | A, Dorian (melody in minor pentatonic) | 92, 4/4, gallop | wind + shellac crackle · twang-guitar root–fifth · clip-clop blocks, tambourine, whip · whistle or harmonica | dry; band-pass 300–3500 Hz on Music | reverb washes, filter sweeps, crescendos, a fourth instrument |
| **PL** Loong's Nest | *gongbi*: even iron-wire line, smoothly graded colour, detail drawn one by one, no visible stroke → perfectly even pulse and tone, smooth graded dynamics, exact ornaments, **no bends**; all under water | G, yayue heptatonic (Lydian), pentatonic core | 63, 4/4, strict | deep pad + bubbles · bianzhong ostinato · frame drum + small bells (combat) · xiao and guzheng harmonics, single "pearl" bell accents | low-pass 2.2 kHz + chorus, wet 0.3 | pitch scoops, rubato, warm timbres, a rough attack |
| **KH** Dark Wood | *scratchboard*: light scratched out of black → thin, wiry, high-passed sounds scratched out of silence; tremolo as hatching; colour is a code — blue = cold sine/theremin, yellow = music box, red = tritone stab | D, Hungarian minor + chromatic steps | 132, 3/4 waltz | thin theremin and wind (high-passed 400 Hz) · plucked oom-pah-pah, harpsichord · bones xylophone + low thump · music box | dry, short plate, wet 0.12 | warm bass pads, lush reverb, a tidy even line |
| **TF** Forgotten City | *pixel art*: hard grid, small fixed palette, ordered dither only → four chip channels, semitone-quantised pitch (arpeggios, never glides), 16 volume steps, strict 16th grid; dither = fast alternation between two notes or levels; neon = one thin bright lead, rare | A, harmonic minor; flute calls in minor pentatonic | 126, 6/8 against 3/4 | triangle bass + swamp noise · pulse arpeggios · LFSR kit + huehuetl · pulse lead in thirds, clay-flute calls | none; 1/8 ping delay | reverb, smooth glides, anti-aliased softness |
| **CC** Cloud Court | *cloisonné*: every colour enclosed by gold wire → every note opens with a metallic "wire" transient (bell tick) around a pure tone; open fourths, fifths and octaves | C, gong pentatonic, high register | 96, 4/4 | sheng cluster (stacked fourths) + air · yunluo ostinato · finger cymbals, small drum · high dizi, guzheng glissandi | bright and short, wet 0.2, damping 0.1 | unpitched mud, low drones, a note without its bell edge |
| **VOID** / zone 6 | *black*: subtraction → the mix loses layers, bandwidth and bit depth | D, no scale | none | sub drone D1 36.7 Hz **with octave partials** (phone speakers cannot play 36 Hz) + detuned cluster · none · none · fragments of eaten realms' motifs an octave down | near-endless reverb, low-pass 800 Hz | pulse, melody of its own |

### 4.8 Signature sounds (proposal)

Home worlds: Pomegraknight → VK, Pixolotl → TF, PumpKing → KH are given. **Cleopastar → SL and
Sifu Pangda → BM are my inference** from the realm descriptions — confirm before the GDD
depends on it.

| Protagonist | Timbre (home medium) | Basic attack | Skills | Ultimate / signature | Music hook | Motif (scale degrees) |
|---|---|---|---|---|---|---|
| **Pomegraknight** | watercolour: mallets, steel pan, soft brass | slash = noise whoosh (band-pass 0.8 → 4 kHz, 90–140 ms) + short FM "shing"; the three stages climb degrees 1-3-5 and the third adds a marimba accent | seeds = dry woodblock pops in a fast pentatonic rattle, one `burst` sound for the volley; Blush = fire whoosh + warm swell | Fire Tornado = rising, tremolo'd noise spiral over a low drum roll | combo ladder on hits; Blush raises `Intensity` by 0.15 while active | 5 – 1′ – 3′, rising fanfare |
| **Cleopastar** | flat poster: pure tones, glass, no vibrato | star shot = FM glass ping + two echo taps, pitched on Phrygian-dominant degrees | Nut's Decree spikes = crystalline tick + falling gliss; Apep's Maw = reversed swell into a falling sub, noise through a closing low-pass | Constellation Reign = sustained double-harmonic cluster + sistrum | black hole pushes the `BlackHole` snapshot — the music is pulled under while it exists | 1 – ♭2 – 4 – 3 |
| **Pixolotl** | pixel: pulse waves, bit-crush | bubble = square blip with an upward slide; pop = noise tick | RANIBOBER rewind = descending arpeggio played backwards + tape-stop slide; SOBRECARGA = arpeggio rate ×2, one octave up; ghosts = detuned pulse "ooo" with slow vibrato | — | local time rate → pitch of its own sounds (`pitch = rate^0.5`, clamped 0.5–2); SOBRECARGA switches the lead layer to the chip rendition | 1 – 5 – 1′ – 5, then ♭3 |
| **PumpKing** | scratchboard: thin, wiry, organ and theremin | head bowl = hollow modal thump (120–200 Hz) + a roll loop whose amplitude wobble follows roll speed; detonate = noise blast + wet splat | Pump Up = rising sine sweep + rubber squeak; the larger the head stage, the *lower* its pitch | Soul Form = theremin wail over an organ cluster | Soul Form removes the drums and adds the choir layer | 1 – ♯4 – 1 low; laugh ♭6 – 5 – ♯4 – 4 |
| **Sifu Pangda** | xieyi: gestures, wood, bends, rests | palms = dry woodblock clack + low thump; crane = dizi flutter upward | tiger kick = gong hit with a bend; bottle = ceramic FM clink + low bubble glugs; Chi shield = singing-bowl hum with beating partials | 归一 = every layer collapses to one guzheng note and a big gong, **then silence** — a stinger whose payload is the rest | drinking detunes and swings its own sounds slightly; the ult mutes the stems for one bar | 5 – 6 – 1′ with a bend into 1′, then one muyu knock |

Foes, hazards and volumes: crab = castanet claw clicks and soft scuttle ticks (VK wood);
seagull = descending squawk, splat, and a **rising-whistle dash telegraph** (priority 90).
Fire = crackle loop; freeze = glass shimmer + crack; lava = low bubbling; spikes = metal
"shink"; tsunami = warning stinger, then a rising rumble on the realm's low instrument. Water
enter/exit = splash + the Underwater snapshot; foliage = rustle noise; cloud = airy puff and a
light low-pass. Each is rendered with the realm's medium rule, so a Dungeon spike is a real
iron ring and a Forgotten City spike is a three-step chip arpeggio.

### 4.9 Budgets (targets to measure — derived, not sourced)

| Budget | Target | Basis |
|---|---|---|
| Disk | 0 MB tonight; ≤ 35 MB when music is recorded | e.g. 10 realms × 3 stems × 90 s × 12 KB/s (Ogg 96 kb/s, G19) ≈ 32 MB |
| Memory, steady | ≤ 8 MB PCM: music stems ≤ 3 MB (17 bars at 72 BPM ≈ 57 s × 44 KB/s ≈ 2.5 MB) · realm foes, hazards, ambience ≤ 1.5 MB · party ≤ 0.6 MB per protagonist · common + UI ≤ 0.7 MB | 44 KB/s at 22.05 kHz mono |
| Memory, peak | ≤ 12 MB during a realm crossfade | two realms alive briefly |
| Voices | SFX 24 · music notes 12 · ambience 4 · UI 4 · stems 4 → at most 48 sources, typically under 12 | WAV voices are cheap (G19) |
| CPU per frame | `AudioManager` ≤ 0.1 ms and zero allocations; a `Post` is one table lookup plus one `PlayStream` | T40 §1–2 |
| CPU at load | ≤ 150 ms of synthesis per realm, on a worker thread, under the scene fade | **estimate** — assumes 50–150 ns per output sample; measure it |
| Sample format | 16-bit mono; 22.05 kHz default; 32–44.1 kHz only for glass and bells; 11.025 kHz allowed for dark drones | G19 guidance |

### 4.10 Verifying without ears

Nobody can audition tonight. Add a headless `AudioAudit` in the spirit of `TileSetAudit`:
every event resolves to a source; no NaN or infinity; peak within ±1 dB of its category
target and never above −1 dBFS; RMS inside a per-category window; DC offset below 1%; first
and last 2 ms under −40 dBFS for one-shots; loop seam step no larger than a typical
sample-to-sample step; duration within bounds; per-realm and total bytes inside §4.9. Add a
dev command that dumps any event or stem with `SaveToWav` [verified: G7] so the owner — or a
spectrogram drawn with the existing Pillow/numpy toolchain — can inspect it. Ship with a mute
toggle and conservative gains.

### 4.11 Cut line

**Tonight:** manager + buses + limiter; event table with fallback; generators 1–5, 7, 9–11,
14, 15; the minimum set from `50-ART-AUDIO.md` §7 (hit, hurt, death, jump, land, per-skill
casts, pickup, UI, day-end sting, devour rumble); voice manager; stems + intensity + four
states for two or three realms; pause snapshot; AudioAudit.
**Defer:** positional panning and attenuation; sidechain compressor; `AudioStreamInteractive`;
`AudioStreamGenerator` voices; the live note layer beyond the combo ladder; reversed-music
rewind; per-surface footsteps; vocals; web export; a LUFS meter; Ogg/QOA asset pipeline.

---

## 5. Godot 4.7 implementation notes and traps

Verified (source keys in §6):

1. **Set `LoopEnd` on every looping runtime WAV.** With looping on and `loop_end = 0` the loop
   region is empty [G14]. Use `LoopEnd = frameCount`.
   ```csharp
   var wav = new AudioStreamWav {
       Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Stereo = false,
       Data = pcm,                                        // signed 16-bit little-endian, mono
       LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = pcm.Length / 2 };
   ```
2. 8-bit data is **signed**; QOA wants a whole QOA file (no runtime encoder is exposed); IMA
   ADPCM cannot seek. Use 16-bit PCM at runtime [G7, G14].
3. `Data` assignment takes the audio lock [G14]; C# also marshals packed arrays by copy in
   both directions (from memory): assign once at load, never read it back per frame.
4. The polyphonic playback exists only after `Stream` is set **and** `Play()` was called, and
   `GetStreamPlayback()` fails when nothing is playing [G5, G8].
   ```csharp
   _sfx = new AudioStreamPlayer { Bus = "SFX", Stream = new AudioStreamPolyphonic { Polyphony = 24 } };
   AddChild(_sfx); _sfx.Play();
   _poly = (AudioStreamPlaybackPolyphonic)_sfx.GetStreamPlayback();
   long id = _poly.PlayStream(wav, 0f, volumeDb, pitchScale);   // −1 when every voice is busy
   ```
5. `PlayStream` returns −1 instead of stealing [G5]; set `Polyphony` before `Play()` (#111977);
   the `bus` argument is reported ignored (#118498) → **one player per bus**; the player's
   `finished` signal does not fire for polyphonic voices (#75089) → poll `IsStreamPlaying(id)`
   or keep your own end times [G21].
6. `AudioStreamSynchronized` applies volume without a ramp [G16] and has an open click report
   (#106900): move a stem's gain by at most about 0.5 dB per frame and mute below −40 dB. Never
   call `SetSyncStream` while playing (#101400) — build a new stream and crossfade two players.
7. `AudioStreamInteractive` gives runtime WAV clips no beat/bar timing: without BPM the switch
   is immediate and `fade_beats` means seconds [G15]; WAV has no BPM (#108184) and no
   `has_loop()` override (#118366); open bugs cover looping clips, pops and edits while playing
   [G21]. Use it later with Ogg stems (`Bpm`, `BeatCount`, `BarBeats` exist [G22]) and re-check
   those issues then.
8. `GetPlaybackPosition()` is 0.0 for interactive streams [G8, G15] and reported 0.0 for
   polyphonic ones (#123398): **own the clock**.
   ```csharp
   _latency = AudioServer.GetOutputLatency();             // cache: the docs call it expensive
   _t0 = Time.GetTicksUsec() / 1e6 + AudioServer.GetTimeToNextMix() + _latency;   // right before Play()
   double songT = Time.GetTicksUsec() / 1e6 - _t0;        // < 0 until the first sample is audible
   ```
   The docs note this clock drifts slowly against the sound card [G18]; re-anchor at every
   music start (each arena entry).
9. C# cannot implement the mix callback (`_mix` is GDExtension-only, and the C# class has no
   `_Mix`) [G10, G22]. The only real-time DSP route from C# is `AudioStreamGenerator`.
10. Generator rules, if it is used later: the buffer is rounded to a power of two; a
    `PushBuffer` that does not fit writes nothing; an underrun is silence plus a skip count;
    `ClearBuffer` errors while playing [G13]. Push to a fixed lead instead of filling
    `GetFramesAvailable()`, or the fill level becomes your latency (#46490). There is an open
    C# crash-on-exit report (#92349) [G21].
11. Pausing the tree pauses any player that is not `ProcessModeEnum.Always` [G8]: UI and Music
    players `Always`, SFX player `Pausable`.
12. Never assume the output rate: on macOS and iOS the project `mix_rate` is ignored [G11].
    Always set `MixRate` on each generated stream; the engine resamples.
13. An unknown bus name falls back to Master silently [G8]: assert every bus name at boot.
14. Looping streams never emit `finished` [G19].
15. Bus effects cost CPU only while the bus carries signal above −60 dB (2 s hold) [G11].
16. Web export changes the rules (sample playback by default, no effects; "Only static streams
    can be sampled"; #109728) [G11, G9, G10, G21]. Not a target now; flag it if it becomes one.
17. `AudioEffectLimiter` is deprecated; use `AudioEffectHardLimiter` [G12].
18. Output is reported truncated to 16 bits without dither (#124104): do not rely on very long,
    very quiet tails [G21].

Project-specific (from the repo, not the web):

19. Headless checks run with a sped-up `Engine.TimeScale`. Make `Post` a no-op and skip
    synthesis when headless (`AudioServer.GetDriverName()` and `DisplayServer.GetName()` exist
    [G22]; the exact strings they return in headless mode are **unverified**), and never time
    music with game delta.
20. Autoload C# events leak scene nodes unless unsubscribed (KNOWLEDGE v0.5.4); prefer direct
    static calls into `Audio` from gameplay.
21. Audio randomness must not touch gameplay `DetRandom` streams.
22. `T10-MODULE-CONTRACTS.md` already lists `AudioManager` as a planned autoload; set the
    static `Instance` in `_EnterTree` as the other autoloads do.

Not verified — treat as hypotheses:

- Whether several `AudioStreamPlayer.Play()` calls in one frame, or inside
  `AudioServer.Lock()`/`Unlock()`, start in the same mix block (the alternative to Synchronized).
- Whether player `VolumeDb` changes are ramped by the mixer (I believe so; not read this session).
- Whether a C# subclass of `AudioStreamWav` overriding `_GetBpm()` / `_GetBeatCount()` /
  `_GetBarBeats()` would make runtime clips beat-aware for `AudioStreamInteractive`. The base
  class calls those virtuals [G17], but I did not check whether `AudioStreamWAV` overrides them.
- Whether `PitchScale` works on a player holding `AudioStreamSynchronized` (it is reported
  broken for Interactive and Playlist).
- The mix block size and therefore the true start jitter of a `PlayStream` call.
- That a bus may only send to a bus created before it (from memory).
- `AudioStreamInteractive`'s clip limit (I recall 63; not read).
- Any behaviour on Android or iOS; nothing here was run, built or heard.

---

## 6. Source list

Fetched and read this session:

| Key | URL | What it gave |
|---|---|---|
| G1 | https://raw.githubusercontent.com/godotengine/godot/4.7/modules/interactive_music/doc_classes/AudioStreamInteractive.xml (+ `AudioStreamPlaybackInteractive.xml`) | class reference source: transitions, enums, clip switching |
| G2 | …/godot/4.7/modules/interactive_music/doc_classes/AudioStreamSynchronized.xml | synchronised sub-streams, 32 max |
| G3 | …/godot/4.7/modules/interactive_music/doc_classes/AudioStreamPlaylist.xml | playlist, shuffle, fade, 64 max |
| G4 | https://raw.githubusercontent.com/godotengine/godot/4.7/doc/classes/AudioStreamRandomizer.xml | modes, pitch/volume randomisation semantics |
| G5 | …/godot/4.7/doc/classes/AudioStreamPolyphonic.xml, AudioStreamPlaybackPolyphonic.xml | polyphony, `play_stream`, `INVALID_ID` |
| G6 | …/godot/4.7/doc/classes/AudioStreamGenerator.xml, AudioStreamGeneratorPlayback.xml | generator properties, C#/GDScript notes |
| G7 | …/godot/4.7/doc/classes/AudioStreamWAV.xml | formats, loop fields, runtime load/save |
| G8 | …/godot/4.7/doc/classes/AudioStreamPlayer.xml | polyphony, pause, playback position notes |
| G9 | …/godot/4.7/doc/classes/AudioServer.xml | timing, bus API, playback types |
| G10 | …/godot/4.7/doc/classes/AudioStream.xml, AudioStreamPlayback.xml, AudioStreamPlaybackResampled.xml | virtuals; `_mix` is GDExtension-only |
| G11 | …/godot/4.7/doc/classes/ProjectSettings.xml (`audio/*`) | mix rate, latency, bus layout path, web defaults |
| G12 | …/godot/4.7/doc/classes/AudioEffectCompressor.xml, AudioEffectHardLimiter.xml, AudioEffectLimiter.xml, AudioEffectFilter.xml, AudioEffectLowPassFilter.xml, AudioEffectReverb.xml, AudioEffectDelay.xml | effect parameters and ranges |
| G13 | https://raw.githubusercontent.com/godotengine/godot/4.7/servers/audio/effects/audio_stream_generator.cpp | ring buffer, underrun, push behaviour |
| G14 | https://raw.githubusercontent.com/godotengine/godot/4.7/scene/resources/audio_stream_wav.cpp | loop handling, data lock, ADPCM limits |
| G15 | https://raw.githubusercontent.com/godotengine/godot/4.7/modules/interactive_music/audio_stream_interactive.cpp | BPM-conditional transitions |
| G16 | https://raw.githubusercontent.com/godotengine/godot/4.7/modules/interactive_music/audio_stream_synchronized.cpp | per-block volume, independent sub-mixes |
| G17 | https://raw.githubusercontent.com/godotengine/godot/4.7/servers/audio/audio_stream.cpp | base `get_bpm` and friends |
| G18 | https://raw.githubusercontent.com/godotengine/godot-docs/4.7/tutorials/audio/sync_with_audio.rst | timing recipes (page flagged outdated) |
| G19 | https://raw.githubusercontent.com/godotengine/godot-docs/4.7/tutorials/assets_pipeline/importing_audio_samples.rst | format sizes, CPU guidance, best practices |
| G20 | https://raw.githubusercontent.com/godotengine/godot-docs/4.7/tutorials/audio/audio_streams.rst | players, playback type, Area bus redirection |
| G21 | https://api.github.com/search/issues — two queries on `repo:godotengine/godot is:issue is:open` for the stream classes; each issue is at https://github.com/godotengine/godot/issues/<number> | open-issue titles (bodies not read) |
| G22 | local file `~/.nuget/packages/godotsharp/4.7.0/lib/net8.0/GodotSharp.xml` | exact C# names and overloads on this machine |
| G23 | https://raw.githubusercontent.com/godotengine/godot/4.7/version.py | branch is 4.7.3-rc |
| W1 | https://en.wikipedia.org/wiki/Adaptive_music | techniques, early examples, iMUSE, Dead Space 2, Spore, Rez, Sound Shapes |
| W2 | https://en.wikipedia.org/wiki/Ape_Out | the procedural percussion system |
| W3 | https://en.wikipedia.org/wiki/Hi-Fi_Rush | automatic beat sync, style per division |
| W4 | https://en.wikipedia.org/wiki/Karplus%E2%80%93Strong_string_synthesis | the algorithm and its tuning |
| X1 | https://raw.githubusercontent.com/chr15m/jsfxr/master/sfxr.js | the sfxr parameter model, presets, `mutate` |
| X2 | https://docs.unity3d.com/Manual/class-AudioManager.html | real and virtual voice limits |

Wikipedia articles were read through the site's plain-text API; they are secondary sources.

Tried and failed:

| Key | URL | Result |
|---|---|---|
| F1 | https://www.fmod.com/docs/2.02/studio/event-macro-controls-reference.html | returned only a JavaScript application shell; FMOD terms above are from memory |
| — | `WebSearch`, `WebFetch` | every call failed with a model-routing error; no search was possible |

Named but **not fetched** (pointers for whoever can check them): GDC talks "DOOM: Behind the
Music" (Mick Gordon, 2017), "Procedural Music in SPORE" (2008), "The Sound of No Man's Sky"
(Paul Weir, 2017), "Serialism & Sonification in Mini Metro" (2018), "The Elusive Goal: Play by
Sound" (Overwatch, 2016); PlatinumGames' blog on NieR:Automata's music implementation; Sony's
ASWG-R001 loudness recommendation; Wwise and FMOD manuals; DrPetter's sfxr page; any interview
with the composers of Hades, Dead Cells, Slay the Spire, Risk of Rain 2, Isaac or Gungeon.
Every claim about those works in §2.1–2.3 rests on memory alone.

# Glory of Fableland — Unity → Godot Migration Guide

> **Audience:** Future AI agents (Claude Code, etc.) executing this migration step by step.
> **Principle:** Build a working prototype first (map + one character), then expand.
> **Reference:** Read `CLAUDE.md` for the Unity project architecture before starting.

---

## 0. Prototype 0 — DELIVERED & PLAYABLE ✅ (2026-07-03)

### Changelog
- **0.11.8** — **A hand-off for the next session.** Docs only. `HANDOFF.md` at the repo root replaces reading the previous session. It says where the work is: this branch; `main` at v0.11.0; the owner's editor on an old worktree; full-size concept originals only on disk. It gives one line per version from v0.11.1 to v0.11.7 with what to read before touching each, and what is not done in priority order with the `KNOWLEDGE.md` caveats to read for each. It also lists what waits on the owner and how to work on this machine (model routing, usage limits, Python, scratch `HOME`, integrating parallel branches). Nothing in the game changed.
- **0.11.7** — **The game has sound: an audio director, a synthesis toolkit, music for every
  world.** Design, the ten media, every rule and the budgets: `Docs/Audio.gdd`; research
  behind it: `Docs/Research/R1-audio-research.md`. **Nobody has heard it yet** — there are no
  audio assets and none could be acquired, so every sound and every piece of music is
  synthesised from data at load, and all of it was proved by measurement (a headless check)
  and by looking at spectrograms; the listening pass (`50-ART-AUDIO.md` §7) is owed.
  **What a player should notice:** everything makes a sound, and the sound belongs to a
  world. A jump, a landing, a hit, a death, a hazard are each one event and ten sounds —
  soft mallets and washes of air on the beach, pure tones in a huge hall in Starland, plucks
  that bend into pitch with a single echo in the Banboo Maze, struck iron in a cave in the
  Hollow Castle, hard-gated twang printed twice like a worn record in the Deserted, chimes
  under water in the Palace, scratched and hatched noise in the Kingdom of Horror, chip
  pulses on a 60 Hz tick in the Forgotten, a metallic tick around every note in Cloud Court,
  and sound with its top, its bits and its pitch taken away in the VOID. A character carries
  its home sound into every arena. Each world has its own music — key, tempo, metre, motif —
  whose layers come in as a fight heats up; a boss adds its own motif in its own world's
  sound; hits climb the realm's scale as a combo builds and a kill resolves it; stingers
  land on the beat. The menu, the map, shelters and events have music; the map's loses its
  top end as the VOID closes in. Pausing, going under water and low HP each change the mix.
  **The contract:** `AudioDirector` is a presentation autoload on the cue bus (T10 §4b) —
  no gameplay file names a sound. A sound is a row in `SfxTable` keyed (owner, action),
  resolved owner → class → `any`, written in the six primitives of a `Medium`. A file named
  after an event (`res://Assets/Audio/<event id>.wav`) replaces its recipe with no code
  change. New cues for the screens between fights (`Audio.gdd` §2): `flow`/`menu.enter`,
  `map.enter`, `map.travel`, `shelter.enter`, `event.enter`, `runover.enter`, `day.end`,
  `void.devour`, `mission.time`, `run.over` tagged `void_devoured`; `ui`/`hover`, `press`,
  `back`, `pause.open`, `pause.close` (`Scripts/UI/UiCues.cs` wires every button).
  **Budgets, measured:** 0 bytes on disk; 7.6 MB of PCM with a realm, its music, all five
  protagonists and the beach's foes loaded (budget 8); at most 40 voices; 0 bytes allocated
  per cue; 3.6 µs a cue, 1.2 µs a frame; synthesis on one worker thread (a realm's sounds in
  under 0.2 s, its music in about 0.1 s).
  **Verification:** `Scenes/Feedback/AudioCheck.tscn` — 1098 checks, about 20 s
  (`40-QA.md` §(2)), mutation-tested with twenty-six breakages (24 caught at once, the two
  survivors given assertions); `Scenes/Dev/AudioDump.tscn` + `Tools/audio/inspect.py` draw the sound;
  `Scenes/Dev/AudioAudition.tscn` is the page to listen with. `FABLELAND_AUDIO=trace` wakes
  the director in a headless run: the run check stays green with sound being made.
  **Not done:** the other four kits' own sounds (their abilities raise no cues yet);
  positional audio; an options screen; saving the volume settings; music as recorded stems.
  **Also archived here:** `Docs/Research/AUDIT-v0.11.1.md`, an audit of the GDDs against the
  code at v0.11.5 — a 161-row work order of gameplay moments for the effects and sound wiring
  of the other four kits, the foes, hazards, items and missions; the twenty gaps a player would
  hit; the TBD registry reviewed; seventeen number mismatches.
  *(Merged onto 0.11.6 from the audio branch's five commits after its own copy of the 0.11.2
  base, by cherry-pick, so 0.11.2–0.11.6 kept their text. From the hand-off: seven caveats in
  `KNOWLEDGE.md`; `AudioDirector` in `T10` §1 / §4b, `T40` §1 and the `T00` diagram; the new
  cue rows in `Feedback.gdd` §6.4; TBDs #56–#61, with its options-screen and provisional-media
  rows folded into #40 and #48 and its "Cloud Court has no world" row dropped as untrue since
  0.11.3; `Docs/Audio/sheets/` has a `.gdignore`. On the merged tree: `dotnet build` 0 / 0;
  AudioCheck 1098, VfxCheck 1298, FeedbackCheck 249, map content 1170, boss 109, run 1328; the
  run check with `FABLELAND_AUDIO=trace`: 996 cues, 0 without a sound, 527 voices, 129 stolen,
  0 refused, at most 17 at once, 100 played late and 38 dropped late while banks rendered;
  `AudioAudition.tscn` and `VfxGallery.tscn` start headless without script errors. Still
  open, untouched here: an arena's `arena.enter` names the map's world, not its realm, and a
  zone-6 arena reports its untreated lightness — TBD #49 and #50.)*
- **0.11.6** — **Effects: a VFX director that draws every cue in the art medium of whoever
  caused it.** Design, the ten media, every row and the budget: `Docs/VFX.gdd`. It was
  **looked at** — every archetype in every medium, and a staged fight on a mid-tone, a pale
  and a dark map, captured in the real renderer and iterated four times
  (`Docs/Art/Vfx/captures/`); **nobody has watched it move at play speed**.
  **What a player should notice:** Pomegraknight's slash is a ruby watercolour crescent that
  sits exactly on her cone (double on the third hit, orange with flame motes while she
  burns); hits throw a small juice splat and droplets, bigger by weight; Blush blooms; the
  Fire Tornado is two flame ribbons on its real box with a pulse per tick; seeds leave a fan
  and a pop where they die; PumpKing's head goes off in yellow scratches to its true radius.
  Every body has dust when it jumps and lands, a pop when it arrives, a burst when it dies,
  a loop while it burns, freezes, is trapped or stunned; a foe's tell is drawn on its real
  path; water splashes and leaves rustle; each realm has its own air. And each of those is
  drawn differently in each world — watercolour wash, flat poster planes, ink strokes, lit
  sparks and smoke, linocut stamps, gongbi line and graded fill, scratches out of black,
  pixels, gold-wired enamel, and the VOID's erasure.
  **The contract:** `VfxDirector` is a presentation autoload (one line in `project.godot`)
  that subscribes to `CueBus` and resolves each cue in `VfxTable` — owner → class → `any` —
  to a `VfxDef`: an **archetype** (20 of them) rendered by a **style** (one per medium, ten).
  A style is five primitives (mass, brush, hoop, chip, tone), a palette rule for dark, mid
  and pale canvases, a timing rule and its particle recipes. Gameplay files changed by two
  additive lines and one visual fix: `ba.swing` carries its cone (`CueShapes.Cone`),
  `arena.enter` carries the arena as its source, and the i-frame blink waits for the hit
  flash.
  **Cheap:** 72 pooled marks, 24 pooled emitters and 2 ambient emitters for the whole game,
  created once; a mark is one canvas command; 0 bytes allocated per cue, per frame and per
  draw; a three-priority budget that never drops a telegraph, a status or the player's own
  hurt. Measured in the staged fights: peak 13–19 marks, 9–13 emitters, 49–97 particles.
  **Verified:** `Scenes/Feedback/VfxCheck.tscn` (headless) and `Scenes/Dev/VfxGallery.tscn`
  (windowed, by eye); the feedback, map, boss and run checks unchanged.
  **Not done:** the four other kits' own effects (they get their home medium's generic
  ones), decals and trails, an options screen, audio — `Docs/VFX.gdd` §9.
  (Merged onto 0.11.5. The branch opened with v0.11.2 re-applied as a commit of its own —
  the same tree under another history — so a plain squash merge conflicted in eleven files
  over text both sides already shared. The six VFX commits were applied on their own
  instead, with no conflict; the result differs from the branch in exactly the files
  0.11.3–0.11.5 changed. On the merged tree: `dotnet build`, 0 errors/warnings; VFX check
  1298, feedback check 249, content check 1170, boss check 109, run check 1328;
  `VfxGallery.tscn` started headless loads and runs without a script error — headless
  draws nothing, so the effects were not looked at again here. **Two things the merge
  brought together that no one has looked at, and no check fails on:** a zone-6 fight
  (0.11.3) is drawn VOID-touched, at about half its backdrop's lightness, while
  `arena.enter` still reports the lightness from before the drain and the world whose map
  it wears — so effect palettes and the hit flash's dark-or-light choice are made for a
  lighter canvas than the one on screen; and the Cloud Court map announces `PL` since
  0.11.3, so its arena effects are gongbi and the enamel style (`CC`) is reached by no
  arena. Both are registered, TBD #49 and #50, and left for a decision. From the hand-off:
  the Tech paragraphs are in `T00-INDEX.md`, `T10` §1 / §4b and `T40` §1 / §3 / §4, the
  mutation list in `40-QA.md` §(2), the TBDs #48–#55; `Docs/Art/Vfx/captures/` has a
  `.gdignore`, and the check's two scripts got their `.uid` files.)
- **0.11.5** — **Style exploration and research, archived.** Documents and pictures only:
  **nothing here changes a shipped asset**, and no code changed. `Docs/Art/StyleExploration/`
  holds AI concept images made in Tripo Studio (GPT Image 2.5, Midjourney v8.2) for the
  owner to choose art directions from: **67 arena concepts over nine worlds and 11 overworld
  concepts** (`img/`, 78 previews; prompts in `README.md` / `prompts.json`, provenance in
  `manifest.json`), a render of what each world ships today for comparison (`ref/`, 9), the
  art-direction review with a recommendation per world (`REVIEW.md`, `review.json`), the
  pick gallery page (`index.html`, built by `Tools/style_exploration/build_gallery.py`; also
  published as a private artifact), and the 16 kit sheets the overworld page kits of 0.11.4
  were cut from (`kit/`). `Tools/style_exploration/fetch_tripo_images.py` is how the images
  were saved. `Docs/Research/` holds the two research digests behind the feedback / VFX
  and audio designs (`R2-vfx-feel-research.md`, `R1-audio-research.md`). **The full-size
  originals are not in git** (`_originals/`, 94 PNGs, 465 MB, git-ignored; they exist only
  as untracked files in the worktree that made them) — the 16 kit sheets among them are
  what `Tools/overworld_art/build_kits.py` reads, so the page kits cannot be rebuilt from a
  clone alone. To cut the weight the 78 previews in `img/` were re-encoded in place, names
  unchanged: longest side 1280 px (wide) or 900 px (square), progressive JPEG, quality 70 —
  25.3 MiB → 11.7 MiB (26.5 → 12.3 MB). Quality 80 came to 14.6 MiB; measured against the
  previews as made, 70 at this size keeps more than 80 at a smaller one. The fetch tool
  still writes 1600 / 1100 px at quality 86, so a re-run writes larger files. `ref/` and
  `kit/img/` are as made. A
  `.gdignore` keeps the folder out of Godot's import (KNOWLEDGE v0.11.4).
  (Verification: all 103 images decode; every file the gallery page and the two manifests
  name exists; `ref/`, `kit/img/` and the text files are byte-identical to the copies they
  came from; `dotnet build`, 0 errors/warnings; the map content check, 1170 — and the
  feedback, boss and run checks, 249 / 109 / 1328, as in 0.11.4.)
- **0.11.4** — **The overworld as torn storybook pages (a third map view; demo).** The
  owner's question: the main map is random, yet every region must keep its own style — how
  do we make art so any generated map *feels like a map* while each region keeps its style?
  The answer built here splits the picture in two. **What makes it a map is code**,
  identical on every run and computed from the generated shapes: a desk of sea-coloured
  paper, rows of cut-paper waves, ripple lines off the coast, the shallows, a compass rose,
  a thin frame — and the torn edge, deckle and shadow of every page, which
  `PaperCut.Tear` makes from *any* outline and a seed-derived stream. **What makes it a
  realm is a page kit**: one ground and nine map symbols in that world's medium, one of
  them the seat that stands at the LV4 city (`Assets/Sprites/Map/Overworld/<W>/`, 8 kits).
  So the island is five pages torn from five storybooks; the tears between them are the
  rivers (deep blue paper shows through); **the VOID is a hole burnt through all of them**,
  and a devoured city's field is charred paper — the page darkened so its medium ghosts
  through, cracked char over it, a bitten and glowing frontier where it meets living
  ground. Mist is tracing paper: it hides nodes and roads, not which book lies there.
  Cities are pins — a cream sticker in the node kind's shape (circle, diamond, triangle,
  square, ring) with an enamel head in the node's colour — roads are dotted ink, a Bridge
  of Eidolon is a violet thread; names are the game's own text on slips of paper, and no
  lettering is drawn into the art. **In the game** the map's View button now cycles *atlas
  → schematic → pages* (still opens on the atlas); under the tilted, rotated boss-up
  camera the pages turn and foreshorten while symbols and pins stay upright (captured flat
  and boss-up; heading-up is the same projection and was not photographed). **To look at it on any seed and
  any day:** `res://Scenes/Dev/OverworldArtDemo.tscn` (seed field, New seed, a day slider
  through the 45-day clock, pan / zoom, toggles for symbols, torn edges, mist, labels and
  the atlas side by side; `-- --shots=DIR [--map]` photographs it and quits). **The kits
  are AI-made placeholders** (GPT Image 2.5 in Tripo Studio, sixteen prompts from two
  templates — `Tools/overworld_art/kit_prompts.json`; `build_kits.py --prompts` prints
  them) processed by `Tools/overworld_art/build_kits.py`: a chroma-difference key that
  keeps the ink wash's mist soft, despill by palette, split, trim, a measured standing
  point per symbol, 8-bit palettes for eight of nine stamps. The shared furniture is drawn
  by code (`shared.py`). Deterministic — two runs give identical bytes. **Added weight:
  95 files, 5.09 MB.** Measured (dev Mac, Mobile renderer, 1600 × 900, MSAA 4×): 155 – 245 ms
  to build an island's pages once per generation (≈ 78 000 vertices, 44 – 56 symbols),
  2 – 7 ms per state change, and per frame two transforms and two modulates. The generator
  is untouched and draws the numbers it drew before (the view's streams are
  `seed + ":pages:…"`): the map content check (1062) and the run check (891) pass — counted
  on 0.11.1, where this was written; the merged tree's numbers are at the end — and the
  build is 0 warnings. Method, kit specification, recipe, placement rules, cost and
  weaknesses: **`Docs/Art/OverworldStyle.md`**; `MapGDD.md` §11a. Not done, by decision or
  for lack of one: the burn is redrawn between days, not animated; the Fields overlay is
  not drawn in this view; at the overview the tear is subtle on pale pages, so it reads
  "illustrated map" before "torn pages"; symbols are small because ~25 nodes a realm win;
  the ground is `ground.jpg` and eight of nine stamps are palette PNGs (the brief said
  PNG throughout — 1024 px PNG grounds are 15 MB); the kits are first results, unedited;
  the view asks for MSAA 2D ×4 while shown and has not been tried without it on a weak
  GPU; frame time was not captured. **Owner's calls:** whether pages becomes the default
  view; whether the kits are kept, regenerated or painted; whether the seat may cover its
  own approach roads to be larger.
  (Merged onto 0.11.3 — written from 0.11.1 — with no conflict; `MapGDD.md` and
  `50-ART-AUDIO.md` were touched by both and merged by git. On the merged tree:
  `dotnet build`, 0 errors/warnings; feedback check 249, content check 1170, boss check 109,
  run check 1328 — the same as 0.11.3, so nothing the checks watch moved; and
  `OverworldArtDemo.tscn` started headless loads without a script error and builds seed
  `STORYBK1` (56 symbols, 78 327 vertices). Headless draws nothing: the view itself was not
  looked at again here. `40-QA.md` §(2) gained the line on the demo's capture mode.)
- **0.11.3** — **Every shipped combat map can be reached by a run.** Two gaps were known
  after 0.11.1; a third turned up while closing them. Both decisions below are
  **provisional** — made on the owner's standing instruction to decide now and discuss
  rework later, and logged as such (`MapGDD.md` §10, style bible Decisions log, TBD #26 and
  #33). (1) **Cloud Court is the sky district of the Palace of LOOING.** The dragon's palace
  is under the sea (Loong's Nest); its court is above the clouds. A Palace node on **high
  ground** may be dealt `cloud_court_heavens` beside that world's two other high-ground
  candidates — one node in three, with today's maps — and a node on sea level or low ground
  never is. It is not a ninth world: the pool, every seed and every save are as they were.
  The map's tags changed in its generator (`build_maps.py`: world `CC` → `PL`, terrain `*` →
  `high-ground`) and it was regenerated; the other 40 generated maps came out
  byte-identical. The kit keeps its tile set (`pl`) and its medium, so the Palace is the one
  world with **two realm sets**: `TileSets.All` lists both under `PL`, the first being the
  world's primary realm, and `MapRealm.Of` says which of the two a map belongs to (what it
  is built from; for an empty map its canvas colour; else the primary). The map browser's
  chip and badge, the palette a map opens on and the palette's group titles ask it — before
  that, a `PL`-tagged Cloud Court map was filed under Loong's Nest. (2) **Zone 6 wears the
  worlds it has swallowed.** Until the VOID has maps of its own, each of its five LV5 nodes
  is dealt an authored map of one of **this run's five worlds**, matched to the node's
  mission, and the LV6 core one of their boss arenas — `CombatMapCatalog.SelectForVoid`. The
  world is drawn first (each of the five as likely as another, however many maps it ships),
  then the map, from a stream of its own (`seed:void-map:nodeId`): the same map every time
  the node is entered, and nothing taken from the streams that build the overworld, roll
  missions or run the arena. Band and hardship level are not consulted (the VOID has no
  altitude, and no map is authored for LV5 or LV6). The handshake carries
  `AdventureContext.VoidTouched`, and the arena draws the map **VOID-touched**: one shader —
  the project's first — keeps a picture's light and 22% of its colour and shades it toward
  violet, on the backdrop and every piece of the map's art and on nothing else. Fighters,
  foes, projectiles, pickups, hazard sprites, telegraphs and the HUD keep their colour. No
  new art. The legacy arena is left for a node nothing matches (none can, with the shipped
  maps) and for `Arena.tscn` run directly. Unchanged in zone 6: foe levels 7 and 8, timers,
  rewards, and the core's boss — still the stand-in crab, now staged on a perch of a boss
  arena. (3) **Found: Crownwave Colosseum (`seashore_5`) has been dealt by no node since
  0.11.0.** It is tagged LV4 without the `boss` goal; every outer LV4 node is a boss fight,
  and 0.11.0 gave VanillaKindom a boss arena, which is preferred. Zone 6 ignores the level
  tag, so a run reaches it there now, and only there — whether it should be retagged is the
  owner's (TBD #26's row says so). **Tried when this was merged, and reverted:** `boss`
  added to its goals, so that VanillaKindom's LV4 boss fight could be dealt it beside the
  beach boss arena. The content check passed with it (1171 checks; `reach` then counted all
  45 maps as dealt in an outer world), and so did the feedback and boss checks; the run
  check failed, 1 of 1337: `maps: seashore_5.json (Boss): a boss arena needs two perches
  (Level Goal markers); it has 1`. The map ships as it was — it needs a second perch
  authored within about 20 m of the first before it can serve a boss.
  **Checks.** *No orphan map* (content check): every shipped
  map must be dealt to some node a run can produce, asked through the run's own two
  selection rules; a map tagged for a world outside the pool **fails** — it used to be a
  printed note, which is how Cloud Court stayed unreachable for two versions. The run check
  pins what the pool of eight generates for eight seeds (recorded on 0.11.1, before any of
  this); asks what zone 6 is dealt for twelve seeds — the same map twice, the second time
  with the run's worlds listed in reverse, always a map of one of that run's five worlds for
  the node's mission; enters Cloud Court through a real run's own high-ground Palace node
  with nothing written onto it (seed `COURT3`, `PL-2-6`); walks all five LV5 nodes and the
  core of seed `WALKVOID` — each on an authored map of a run world, VOID-touched, its first
  foe out of the map's own nests, played to its goal, the core to victory; stands the crab
  on a perch of each of the eight boss arenas; and asks the engine whether the shader
  compiles. **Saves.** The format is unchanged — version 4, no new field — and a save made
  before this loads and plays. Which map a node opens is not saved; it is worked out from
  the seed and the node on entry, as it always was. So two things differ for a run already
  in progress: a save taken *inside* a zone-6 fight resumes that node (a fresh encounter, as
  before) on the authored map this build deals it, where the older build opened the legacy
  arena; and in the Palace of LOOING an uncompleted high-ground node may open a different
  map than it did yesterday, because it now draws one of three where it drew one of two —
  the same goes for a save taken inside such a fight. Nothing else moves. Not done, by
  decision or for lack of one: the VOID has no kit, medium or maps of its own (TBD #33) and
  its look is a drained picture and nothing more; the treatment was looked at in the real
  renderer on four arenas of one seed (Dungeon, Ghost Town twice, Beach) — not on all eight
  realms, not on Loong's Nest's sea, not on Cloud Court; the stand-in crab stands on perches
  built for PumpKing and nobody fought it there; no zone-6 fight was played by hand; and a
  local Edit Copy still beats a shipped map's tags — this Mac's `user://maps` has a copy of
  Cloud Court from 15 November tagged `PL` / every band, so here it is dealt on all three
  bands until that copy is deleted.
  (Verification: `dotnet build`, 0 errors/warnings; the map content check, 1170 checks, up
  from 1062; the boss check, 109, unchanged; the run check, 1328, up from 891 — logs clean
  but for the sixteen `Command or Control autoremapping` lines `project.godot` prints at
  every start; the two zone-6 `using legacy Arena` warnings are gone. Ten mutation runs,
  sources restored and rebuilt after each, the working tree compared byte for byte
  afterwards. Nine failed a check by name: Cloud Court put back on world `CC` (`orphan map`),
  tagged for every band, zone 6 honouring a hardship level, zone 6 drawing from the whole
  pool, its choice depending on the order of the worlds, a draw for it taken from the
  missions' stream at map generation (seven of the eight pinned seeds moved, and five of
  the six older ones — seed `DEBUG` moved in neither table), zone 6 dealt nothing as before,
  the treatment draining hazard sprites, the arena never applying it. The tenth — the
  shader replaced by nonsense — **survived**, 1327 of 1327: the walk counted which nodes
  carry the material, not whether it draws. The headless renderer does parse shaders, so the
  walk now asks (`VoidTreatment.ShaderCompiles()`), and the same breakage fails by name.
  Both map generators re-run: only `cloud_court_heavens.json` differs, by its three header
  lines. Every Godot run after the first import used a scratch `HOME`; the real `saves/` and
  `maps/` folders in `user://` were compared before and after: untouched.)
  (Merged onto 0.11.2 — written from 0.11.1 — with no conflict; `GameManager.cs` and
  `40-QA.md` were touched by both and merged by git, each side's part read back. On the
  merged tree: `dotnet build`, 0 errors/warnings; feedback check 249, content check 1170,
  boss check 109, run check 1328. Twelve open questions from 0.11.2's hit feedback were
  registered as TBD #36–#47 in `10-DESIGN.md` §6.)
- **0.11.2** — **Hit feedback has a foundation: a cue bus, hit-stop, and a feel director.**
  Design, the tier table, every rule and the full cue vocabulary: `Docs/Feedback.gdd`;
  research behind the numbers: `Docs/Research/R2-vfx-feel-research.md`. **Nobody has watched
  it yet** — every number was reasoned from the research and from pixels at 32 px/m and
  proved by a headless check, which proves mechanisms, not looks; the verify-by-eye pass
  (`50-ART-AUDIO.md` §6) is owed.
  **What a player should notice:** a hit that lands makes both fighters hold still for a
  few frames (a 15-damage slash: the victim 44 ms, Pomegraknight 31 ms; a 30-damage one
  68 ms; the player being hit at least 50 ms), the victim flashes as a white silhouette — ink
  on a pale arena — instead of going pink, the camera is kicked along the blow, and shake is
  now small for small hits and capped by the weight of the hit instead of a flat 0.35 each;
  heavy hits roll the view a hair and punch the zoom 2–4 %; a boss falling and the player
  going down are beats of their own. Foes no longer blink when struck.
  **The contract** (T10 §4b): gameplay raises cues on `CueBus` — `Emit`, `Start` / `Stop`,
  one `HitCue` per hit from the two hit intakes — keyed by two constant strings (owner,
  action), and never names an effect, a sound or a director. Directors are presentation
  autoloads that listen and look cues up in their own `CueTable` (owner → class → `any`);
  `FeelDirector` is the first, VFX and audio are next on the same bus with no gameplay edits.
  `HitInfo` gained feedback metadata damage math never reads (`.From(this, "ba", stage)`,
  `.FromRanged(owner, "s2.seed")`); both body classes are `IFeedbackBody` with a snake_case
  `FeedbackId`. **Hit-stop is gameplay** (it moves bodies in time): per body, never
  `Engine.TimeScale`; it holds motion and animation while **every clock keeps running and
  abilities are still read**, so no DPS, cooldown or check timing moved; victims are held by
  the intake, a melee attacker once per swing, a ranged hit never holds its owner, ticks hold
  nobody; stops take the max, re-arm after 0.10 s and are budgeted so a stream of hits holds
  a body about 15 % of the time at most; `FeedbackSettings.HitStop` scales it and 0 switches
  it off. Sifu Pangda's dive and PumpKing's Soul Form hold with the body; PumpKing's head
  and Cleopastar's star are exempt.
  **Cues raised** (Feedback.gdd §6): spawn, jump, land, death, respawn, heal, `hurt.tick`
  (hazard and DoT damage merged to one per kind per 0.35 s), ult.ready, switch.in / out, five
  status loops, a foe's aggro / evolve / telegraph loop, soft-volume enter / exit (once per
  sea, not per tile), hazard ticks by kind, and the arena's flow — arena.enter, the
  countdown, mission.start / success / fail, boss.intro / defeated, player.down, run.over.
  **Pomegraknight is wired in full as the reference kit**; the other four still hit as
  `any` (victims are held by damage, attackers are not) and Pixolotl and Sifu Pangda still
  shake the camera by hand until they are wired (`20-ENGINEERING.md` §4 has the recipe).
  The inline feedback is gone from the intakes, PumpKing's head and the hazards.
  **Photosensitivity limits hold whatever the settings** (WCAG 2.3.1 / Xbox XAG 118): no
  full-screen flash, no saturated red, six bodies flashing at once at most, three large
  flashes a second at most. Comfort settings exist (`FeedbackSettings`: shake, zoom punch,
  flash, hit-stop) but nothing writes or saves them yet.
  **Verified:** `dotnet build` clean; a new headless check,
  `res://Scenes/Feedback/FeedbackCheck.tscn` (249 checks, ~20 s, `40-QA.md`), on real
  bodies and through the real Arena, mutation-tested (44 deliberate breakages, each failing
  by name — three of them only after the check was tightened); raising 14 000 cues with the
  director acting on each allocates 0 bytes. The three existing checks stay green (content 1062, boss
  109, run 891) with hit-stop on; **one boss-check line changed**: its poise probe read a
  boss's velocity the tick after a hit, and now waits out the stop first — same bound.
  **Fixed on the way:** a Pome seed that touched two bodies in one physics frame resolved
  twice (`QueueFree` is deferred; the check counted 25 endings for 24 seeds). **Noted, not
  changed:** a dashing seagull usually lands its contact hit a tick before its dash hit and
  the i-frames eat the dash. **Deferred:** world slow-motion, the boss-kill freeze, holding a
  killed foe on its flash frame, sprite shake and squash, particles, audio, rumble, pooled
  damage numbers, the options screen. **Provisional, not confirmed by the owner:**
  Cleopastar's effect style follows Starland and Sifu Pangda's the Banboo Maze. Eight
  caveats in `KNOWLEDGE.md` (v0.11.2), among them: never launch Godot from the Anaconda
  python — an x86_64 parent starts it under Rosetta and the checks hang.
- **0.11.1** — **A fighter swims in every soft volume.** *(This is the 0.10.10 work: written
  in another session's worktree from 0.10.9, never committed on its own, and merged here
  onto 0.11.0. What the merge had to change is at the end of the entry.)* The owner's rule:
  inside a soft volume the player moves freely, as if in space, and gravity stays as a drop
  — velocity is the fighter's own swimming plus the drop. **Up / Down (`W` / `S`, `↑` / `↓`)
  now steer exactly as Left / Right do** inside any soft volume — sea, swamp, bamboo, pine,
  cloud, vines, chains, fog — and gravity there is replaced by the volume's drop: its
  `SinkSpeed` in water, a tenth of top speed elsewhere. Measured on the real character: in
  the sea 5.5 m/s across while sinking 3, **up 2.5 m/s, down 8.5 m/s**; in the swamp up
  2 m/s; in a bamboo a fighter floats down at 1 m/s and climbs at 8.6. **Jumping inside a
  volume stays**: Space is a stroke wherever the fighter is (3 m in the sea, 5.9 m in a
  bamboo), as often as the universal 0.3 s jump cooldown allows, on top of the swimming —
  so a player chooses between holding Up and jumping. With that cooldown tapping is the
  faster way up: 10 m/s against 2.5 in the sea, 19.4 against 8.6 in a bamboo (10.7 with
  both in the sea). From a surface a jump clears the sea by 2.1 m, the swamp by 1.0 m, a
  bamboo's top by 5.1 m. Up stops pushing once the head is out, so a swimmer rests with its
  head at the surface. This replaces v0.10.9's dive (Down *replaced* the sink). **Foes do
  not swim** — they fall through foliage, slowed, and sink in water, as before — and
  Cleopastar's star keeps its own rule (`SoftVolume.Swimmable` off). `move_up` is
  registered by the game (`InputDefaults.cs`), not in `project.godot`. **Fixed:** a stream
  laid as several tiles (a bubble column, a waterfall) pushed twice as hard where a body
  crossed a joint — 11 m/s in a 4 m/s column; an identical carry now counts once.
  **Retuned:** kelp 0.3 → 0.4 (in the sea its swimming speed exactly cancelled the sink, so
  nobody could swim up through it). Engine: `SoftVolumeSet.SwimVertical` / `Swim` /
  `Covers` / `PredictSwimSpeed`; the passive rule (`Resist`) is unchanged to the digit. The
  content check asks every medium in the registry whether it can be swum up in, and the
  probe measures swimming on both axes, a jump inside a volume and that its cooldown holds
  there, tapping Jump against holding Up, resting at and leaving a surface, the joint of
  two stacked tiles, a foe, kelp in the sea, and getting out of the shipped canals.
  **What merging it onto 0.11.0 changed.** (1) *Swimming takes sides.* The work read
  `Input` for Up and Down inside `CharacterController`; since 0.10.11 a BOSS is that class
  on a brain's pad, so as written the player's W and S swam the boss — and the three-way
  merge was clean at those lines. Both reads go through `Controls`
  (`ControlAction.MoveUp` / `MoveDown`); `PlayerControls`, the one reader of the InputMap,
  now registers `move_up` itself and lost its guard for a missing action. So **a boss
  swims too, on its own pad** — BOSS PumpKing 1.4 m/s up and 7.4 down in the sea — and
  "foes do not swim" means `BaseFoe`. No rule of his brain uses it: in a volume he walks,
  jumps and floats (TBD #34). The boss check has a stage for it — one sea, the player and
  a boss in it; each side's Up and Down must swim that side and leave the other sinking at
  the drop. (2) *A 0.11.0 map broke a swimming rule.* The Forgotten City low-ground canal
  was authored for the old water, its banks 2 m over the surface, left by "a second jump";
  a jump at the surface of swamp water clears 1 m. Tapping Jump still got out (0.75 s,
  measured); swimming to the bank and jumping did not. The canal is now flush with its
  banks — `Maps/forgotten_low_ground.json` regenerated, its pit one row shallower — and
  the probe swims out of every listed pool and refuses a shipped map with swamp water that
  is not listed (`ShippedPools`). (3) *Bookkeeping.* The swimming work's TBD #28 and #29
  collided with 0.11.0's and are **#34 and #35**; its "v0.10.10" tags read v0.11.1 in the
  docs and code, the first version of this line that swims. Nothing was taken from its
  `project.godot`: that diff is an editor re-save (feature list, section order, event
  fields) and the feature needs none of it. Not done, by decision or for lack of one: the
  numbers are measured, not balanced (TBD #27, #35) — 48 m of sea is 19 s up and 6 s down
  by holding a direction, and foliage practically holds a fighter (drop 1 m/s); **they are
  a 10 m/s fighter's** — by the model PumpKing (8 m/s) rises 1.4 m/s in the sea and 0.2
  through kelp there, Sifu Pangda (6.5) 0.6 and sinks in that kelp, and a PumpKing slowed
  to 4.8 m/s by three pumps sinks while holding Up; a jump stroke still climbs for all of
  them, and the "can be swum up in" check asks only the 10 m/s fighter; a boss's legs do
  not swim; whether foes should float; no real fight was played, and routes in the older
  maps were not re-walked.
  (Verification: `dotnet build`, 0 errors/warnings; the map content check, 1062
  checks; the boss check, 109 (13 of them the new stage); the run check, 891 — logs clean but for the two
  expected zone-6 lines and sixteen `Command or Control autoremapping` lines that
  `project.godot` already printed at every start before this change. Seven mutation runs
  for the merge, each failing a check by name, sources restored and rebuilt after each:
  both swim reads put back on `Input` — four boss-check lines, while the content check
  stayed green, which is why the stage exists — each read alone, swimming made
  Player-side only, `move_up` no longer registered (again invisible to the content check,
  whose probe registers what it presses), the 0.11.0 canal put back, and its pool left
  off the list. Both map generators re-run: the 40 maps they write are byte-identical but
  for the canal's, which was looked at in the offline preview only. The swimming work's
  own fifteen mutation runs were made next door on 0.10.9 and were not repeated here.
  The real `saves/` and `maps/` folders in `user://` were compared before and after: untouched.)
- **0.11.0** — **The realm pool is arranged: eight worlds, a map for every altitude band and
  a boss in each.** *(Committed together with 0.10.11, below, which had not been committed on
  its own.)* **How many worlds there are:** six were in the overworld pool (SL, HC, VK, TD,
  PL, BM); two more had a full combat realm — art, tile kit, maps — that no run could reach
  (the Kingdom of Horror, The Forgotten); one kit has no world (Cloud Court). **The pool is now
  the eight worlds whose realm is built**: KH and TF joined, in danger red and canal jade on
  the map. A run still draws five, home first. **Every pool world ships its own set of maps**:
  one authored for sea level, one for low ground, one for high ground, and a boss arena — 31
  new maps (`Maps/<realm>_sea_level` / `_low_ground` / `_high_ground` / `_boss_arena`),
  generated by `Tools/realm_maps/build_band_maps.py` from one data row per realm and four
  shapes. They are **plain test maps, as asked** ("very easy for now, just so when we're
  testing we have a valid map to enter"): a flat field with ledges; a basin holding what the
  realm keeps in its low places (a lava pool under a catwalk, a canal, mist and bamboo, kelp
  and a bubble lift…); a mesa of three terraces — the Dark Wood's climbed by its slope; and
  an arena with a tower and a ledge for the boss's two perches. The showcase maps are
  untouched and still serve every band. **PumpKing is every realm's boss for now**
  (`BossTable.StandIn`): each 4-1 fields him on that realm's boss arena — his own castle in
  the Kingdom of Horror — until a realm gets a row of its own; he recruits once and after
  that just falls. **The boss's attributes were examined against the foe design** (`FOES.gdd`
  §8.2, a table): HP and damage follow the day exactly — **600 / 720 / 900 / 1200 / 1800 /
  1800 HP** and ×0.60 / 0.72 / 0.90 / 0.90 / 0.90 / 1.20 damage on the six day bands, read
  from the boss and from its bar inside real runs — and nothing of the player's own PumpKing
  leaks into him. Two foe rules do *not* apply to a boss, on purpose, and are the owner's to
  decide (TBD #31): the day's speed multiplier, and the 25-second evolution (+1 level and a
  30% heal — up to +540 HP into a fight whose timer is permadeath; the old crab stand-in did
  evolve). **Old saves keep their worlds.** A save stores the seed, not the map, and the
  seed's shuffle is over the pool — so growing the pool would have moved every saved run to
  other realms. A run now records the pool it was generated from (save version 4), and a save
  from before is given the six worlds of its day: the three real slots on the dev machine
  were tested as copies and each wakes up on its own map, standing where it was. New runs do
  draw different realms for the same seed than before; that is the cost of growing the pool,
  paid once (`WorldDef.Pool` is append-only from here). **A run check** —
  `Scenes/Run/RunWalkCheck.tscn` — plays real runs headless: enters all 45 shipped maps,
  walks every world (each altitude band, a hub, an event, the capital on its own day band),
  ends days, and finishes a run in zone 6; it works in a scratch save folder and deals
  shipped maps only (`SaveGameService.DirectoryOverride`, `FABLELAND_SAVE_DIR`,
  `CombatMapCatalog.UserMapsEnabled`). The content check now requires each pool world's own
  map per band and a boss arena. Not done, by decision or for lack of one: the band maps and
  seven boss arenas are test maps, not designs (TBD #32) — outside the Dark Wood there is no
  slope, so his head has no hill to run; the other seven realms' own bosses do not exist
  (TBD #2); zone 6 still opens the legacy arena (TBD #33); whether PumpKing and Pixolotl
  become starters in their worlds is open (TBD #20, #24); the swimming work (0.10.10) was
  still in its own worktree when this was committed — it is merged in **0.11.1**, above, with
  its `Input` reads routed through `Controls`; no person has played any of this.
  (Verification: `dotnet build`, 0 errors/warnings; the run check, 891 checks in 41 s; the
  boss check, 96; the map content check, 939 — each twice on the final code, logs clean but
  for the two expected zone-6 lines; twelve mutation runs for this change, every one failing
  a check by name — after one runner bug was found in them: it did not rebuild after
  restoring a source file, so two data mutations first ran against the previous mutation's
  assembly; every showcase map regenerating byte-identical; the overworld photographed with
  both new worlds; twelve of the new arenas photographed in the engine, where five art-less
  beach tiles showed up as flat squares and were replaced; and two boss fights photographed
  inside real runs — The Forgotten on day 11, bar reading 720 / 720, and the Kingdom of
  Horror on day 36, 1800 / 1800.)
- **0.10.11** — **Boss fights: PumpKing, on his castle's balcony, bowling his head down a
  slope.** *(0.10.10 was taken by the swimming work, then uncommitted in another worktree;
  it reached this line as 0.11.1 — see the end of this entry.)*
  At a realm's 4-1 its protagonist is the BOSS, and beating it recruits it. **A boss is the
  protagonist's own class on the other side** — `BossPumpKing : PumpKing` — so it fights with
  the kit the player will recruit; what differs (HP, damage, poise, magazine, cooldowns, a
  held item) is one row in the new `BossTable`. To make that possible every fighter now has a
  **side** and attacks ask who is hostile to theirs (`CombatSide`, `ICombatTarget`,
  `CombatTargets` — all five kits, their projectiles and the item skills were moved onto it),
  and kits read their input from **`Controls`** instead of the keyboard, so a brain can press
  the same buttons. The brain is **rule-based**, as asked: per boss a priority list of "when
  this, do that" for his legs and one for his hands, decided every 0.15 s through a virtual
  pad (`BossBrain`, `BossRuleSet`); anything else that can fill the pad can replace it later.
  BOSS PumpKing holds his perch, pumps more shell the farther you are, bowls, sets his head
  off beside you (or calls it back when it has passed you), and when you have been in his
  face for 1.2 s leaves in Soul Form for his other perch. **His arena takes his side**:
  `Maps/horror_pumpkings_castle.json` — a strip of wood, one unbroken slope 36 m along and
  18 m up, the forecourt, the gate (shut), the balcony he comes out onto, the keep. On level
  ground his head carries 7.7 m and goes off; on the slope it does not stop, and from the
  balcony his first head runs 51 m to a player at the foot. The map format gained a `boss`
  goal (dealt to a world's LV4 Boss mission before its other maps, never to anything else)
  and the map's Level Goal markers are the boss's perches. **Slopes are new terrain**: a
  slope block is a Ground tile whose top is a ramp (`ground.kh_slope`, one cell up in two
  along, `RealmTiles.Slope`), a fighter keeps its feet on one (`Units.FloorSnapPx`), climbs
  it at 8 m/s where it runs 10, and comes down at 10. Seven new Dark Wood sprites: the slope,
  a balcony and its balustrade, the gate, the lit balcony door, battlements, a pennant.
  **Fixed on the way:** PumpKing's head never rolled — a head resting on a floor lost 15% of
  its speed every physics tick and stopped within 0.7 m (it now bounces *or* rolls: 6 m at
  the friction his GDD always gave, capped at 6 m/s across even downhill, and it keeps
  rolling over a change of slope); and Pixolotl's bubble pool was 18 bubbles that never
  entered the tree whenever she was the body authored in the Arena scene (`AddChild` to a
  busy parent from `_Ready`), so her attack fired nothing there. `RunState.GrantProtagonist`
  no longer adds a protagonist twice. Debug: key 4's page ends with **Boss fights** (from the
  title screen), `Scenes/Bosses/PumpKingBossFight.tscn` runs with F6, the Arena has
  `DebugMission` / `DebugNodeLevel` / `DebugBossId` knobs, and with debug on the mission line
  shows what the brain is doing. Not done, by decision or for lack of one: **the Kingdom of
  Horror is still not in the overworld pool**, so a run cannot reach this fight — it is wired
  (`AdventureContext.BossId`) for the day it is; every `BossTable` number and every rule
  threshold is a first pass (the owner postponed boss design); the other four kits'
  projectiles still fight for the player only (`NOTE(boss)`); the prototype roster already
  owns PumpKing, so recruiting him changes nothing yet; he wears a placeholder red mark
  because a headless black coat vanishes against black stone; no human has played the fight.
  (Verification: `dotnet build`, 0 errors/warnings; the new headless boss check — 92 checks:
  pad and scheduler logic, sides, the head on flat and slope, a fighter on the slope, each of
  his rules in the one situation it exists for, and the whole fight through the real Arena on
  the dealt map — passing on every one of a dozen runs; twenty-nine mutation runs against the
  final check, twenty-eight failing it by name and one shown to change no behaviour — the
  first round had broken only two of his seven rules, and breaking the other five found two
  ("hold perch", "call back") that nothing checked and that nobody had yet watched working
  (both do) and a third caught only by accident, so each rule now has a stage of its own;
  the map content check, 500 checks, water numbers unchanged; every painter and
  every map regenerated with only the new files differing; the fight driven end to end in
  the real renderer from the title screen through the debug page — staged, climbed, the
  balcony reached, Soul Form flown to the limb — and its frames reviewed, which is where a
  balcony the same tone as the wall behind it and a balustrade hiding the boss's legs were
  found and fixed; Menu, Arena and the boss scene booting headless with no new errors.)
  **0.10.10** was, at the time, an uncommitted change in another session's worktree
  (`game-map-editor-ui-b1ced6`: a fighter swims in every soft volume). It touched
  `CharacterController._PhysicsProcess` too, reading `Input` directly for Up and Down; the
  note left here said that when the two met those reads had to go through `Controls`
  (`ControlAction.MoveUp` / `MoveDown`) or the player's keys would swim the boss. They met in
  **0.11.1**, which did exactly that and has a check for it — `KNOWLEDGE.md` has the caveat.
- **0.10.9** — **Three new realms, Wulin repainted as ink painting, and water.**
  *Wulin (Banboo Maze)* is repainted as freehand ink (写意): calligraphic strokes that swell
  and taper, splashed wet ink, and most of the sheet left bare (留白), in black, white, cyan
  and ink green with no warm colour. The background is now a painting with a subject — one
  towering peak with pines, a slender second, a thread of far waterfall, a village in mist, a
  boat — instead of a wall of bamboo; the moon is unpainted paper and the inscription 山水 is
  brush-written. New pieces: a stone bridge, a climbable pine, and a **waterfall** that
  carries whoever enters it down at 14 m/s and cannot be climbed, so the head of the fall is
  worth holding. *Loong's Nest (龙宫)* is the Palace of LOOING's realm: a palace under the
  sea in gongbi (fine line, graded purple / cyan / blue), set `ln`, 17 tiles, map "The Sunken
  Court". The whole map is water, with hidden currents that drag a swimmer toward urchin
  beds and bubble columns that lift. *The Dark Wood* is the Kingdom of Horror (PumpKing's
  home) in scratchboard — blue, red, yellow; set `kh`, 18 tiles, map "The Hanging Wood":
  bramble, fog, bear traps, wisps, and a castle. *The Forgotten City* is The Forgotten
  (Pixolotl's home) in pixel art — a Teotihuacan city everyone takes for a ruin, lit by thin
  neon; swamp at the bottom, pyramid above; set `tf`, 18 tiles, map "Canals of the Sun".
  **Water** is a SoftVolume with three new numbers: `GravityScale` (the share of gravity
  left — buoyancy and gravity are simply added), `SinkSpeed` and `Drag`; a **current** is a
  SoftVolume that only carries (`Flow`, `IsMedium` off). Measured on the real character: in
  the sea a 10 m/s fighter swims 5.5 m/s, sinks 3 m/s, a jump is a 3.4 m stroke that can be
  repeated (an air jump is 8.3 m), a current carries 4 m/s, bubbles lift 4 m/s; swamp water
  is waded at 4 m/s. Holding **Down** in water swims down. Engine: `SoftVolumeSet` — a body
  can be in several volumes at once and they add up (it used to remember one, and leaving an
  inner volume forgot the outer); `MapCollision.CreateSoftVolume` shared by both map
  builders; **the arena now builds a Farview layer's SoftVolumes** (only ▶ Play did), which
  is how a sea is laid without using battlefield cells; `SoftVolumeTuning` + registry
  validation; realm chips for realms without a pool world. Tools: three painters,
  `audit_edges.py` (sprites cut by their own canvas), `mapkit` `collision=`. The content
  check gains soft-volume parity (arena vs Playtest) and a **physics probe** that runs the
  real character through every shipped water kind and through the shipped Loong's Nest map
  built as a run builds it. Not done, by decision or for lack of one: the Kingdom of Horror
  and The Forgotten are **not in the overworld pool** — their maps play from the map editor
  (adding a world changes what every seed generates); Cloud Court, v0.10.6's proposal for
  the Palace, is kept with no world (retagged `CC`); destroyable trees are not built; the
  water numbers are measured, not balanced; no real fight was played on the new maps.
  (Verification: `dotnet build`, 0 errors/warnings; headless content check, 482 checks,
  twice; ten mutation runs — each breaking one water mechanism: buoyancy, dive, carry,
  drag, sink cap, set membership, foe resistance, flip, and each builder's Farview volumes —
  each failing the check by name; all eight painters and all realm maps regenerated
  byte-identical; the four new or repainted maps and the map browser screenshotted through
  the real renderer and reviewed, which is where a dozen clipped sprites and a sea surface
  drawn a row above its water were found and fixed; headless boot of the main scene clean.)
- **0.10.8** — **Map editor: props are no longer stuck on the 2 m grid.** A placed tile can
  carry a sub-cell **nudge** — `SubX` / `SubY`, whole quarter cells (16 px = 0.5 m), −3..3.
  Select tiles and press **Alt + arrow** to slide them; a fourth step carries into the next
  cell, a step into an occupied cell or past the layer edge is refused (a nudge never
  overwrites), each press is one undo step, and the status bar reads the offset. The integer
  anchor still owns occupancy, erase and selection; the nudge moves art, effect area
  (collider / SoftVolume / hazard) and marker point together, so a nudged bench is stood on
  exactly where it is drawn — in the editor canvas, ▶ Play, a real run and the offline
  preview. Ground, autotiled fills and rule zones stay on the grid. Move, copy/paste and
  Flip H keep the nudge. A nudge of 0 is not written, so existing map files are unchanged and
  load as before. Engine: one placement function (`TilePlacement`) replaces the cell-to-pixel
  arithmetic that five builders and two Python tools each carried; `TileNudge` plans a step;
  the Python map kit and preview understand `nudge=`. Out of scope, by decision: moving map
  pieces, several tiles per cell, free pixel placement. (Verification: `dotnet build`, 0
  errors/warnings; headless content check, 315 checks, including a new arena-vs-Playtest
  nudge parity check; nine mutation runs, each breaking one nudge site and each failing the
  check by name; the editor driven end to end against a scratch save folder — select, nudge,
  carry, blocked, undo/redo, copy/paste, move, flip, save, reload, ▶ Play — 24 assertions;
  the nudged pieces located at the same world position in the editor canvas, the Playtest,
  the arena runtime and the offline preview. Regenerating the five realm maps with the
  updated kit leaves `Maps/` byte-identical.)
- **0.10.7** — **The realm maps actually reach the player, and the map browser is rebuilt.**
  Three separate reasons the v0.10.6 maps were not seen. (1) A run's map selection treated
  every file in `user://maps` as playable: an empty or half-built draft — which answers to
  every world and level until its author narrows it — was dealt to fights, usually ahead of
  the real map. Selection now only deals maps that pass `MapReadiness` (ground, one character
  spawn, an enemy spawn, a goal marker), and an unfinished copy of a built-in map falls back
  to the built-in. (2) The five realm maps were tagged `sea-level`, so two combat nodes in
  three in those worlds opened the legacy arena; they now serve every terrain band (`*`, the
  map kit's default), and the editor's Map info dialog can express `*` instead of silently
  narrowing it. (3) The browser listed only the user's own maps; the built-in maps, where the
  realm maps live, were never shown. **Map browser:** always four cards to a row and never
  wider than the window; each card is a live picture of the map with a bold name, small
  details and realm / level / status badges; a realm strip (the realm selector, one coloured
  chip per realm with counts), a search box, and filters for level, goal, terrain, source and
  sort; unfinished maps are hidden behind a toggle and can be archived (never deleted) with
  Clean up; built-in maps are listed and edited through a same-id copy; Create asks for a
  realm. Card pictures are framed on what is built, not on the whole grid, so a sparse beach
  map shows its floor and towers instead of sky. **Checks:** the headless content check now
  fails a shipped map that is not playable and a world with a kind of combat node no shipped
  map serves, and self-tests cover selection and picture framing. (Verification: `dotnet
  build`, 0 errors/warnings; headless content check, 293 checks, coverage 39/39 for all six
  worlds, plus two mutation runs proving the new checks fail on the old behaviour; the browser
  screenshotted in-engine at three window shapes; create / edit copy / clean up exercised end
  to end against a scratch save folder.)
- **0.10.6** — **Realm tile sets: every world gets its own art and its own kind of arena.**
  Five new combat-map tile sets, each in a different medium, with one built-in map apiece:
  **Starfield** (Starland — flat minimalist poster; vast and empty, colossal stone monuments;
  long range), **Wulin** (Banboo Maze — Chinese ink-wash; vertical, every bamboo a soft volume;
  assassins), **Dungeon** (HollowCastle — lit realism; crowded, lava, spikes, chains to climb),
  **Ghost Town** (TheDeserted — linocut print; two-storey duelling street) and **Cloud Court**
  (Palace of LOOING — cloisonné enamel; aerial, cloud soft volumes). 100 new tile kinds, painted
  by deterministic scripts (`Tools/realm_art/`); maps written as code (`Tools/realm_maps/`).
  Engine: realm tile sets with a palette **Realm** filter; one shared resolver for tile art and
  code-painted strip ground atlases that now draw in play; the Playtest and the arena build maps
  from shared parts, so real runs gain **parallax**, Closeview finally draws in front of the
  fighters, and prop sprites sit where the editor shows them; hazards are bound to scenes by
  data and touching lava tiles merge into one hazard; the arena backdrop covers large maps.
  New dev tools: a headless map content check and an in-engine map screenshot scene. Art
  direction: `Docs/Art/RealmStyleBible.md`. The beach keeps its look: its ground still draws
  as a seamless fill in play, and the three pieces on its parallax sky layer were re-placed
  (`Maps/seashore.json`) so the sun and clouds sit where they did now that real runs honour
  that layer's 0.55 parallax. (Verification:
  `dotnet build`, 0 errors/warnings; headless Godot content check, 224 checks over 127 tile
  kinds and 6 maps; all six maps rendered in-engine and reviewed.)
- **0.10.4** — **Pixolotl click boost and universal combat opening.** SOBRECARGA is now a
  click-activated 1.5× local-time burst lasting ten generated frames or three real seconds.
  Bubble collision and the fallback visual now match the authored 2 m diameter. The Arena owns
  a visible three-second opening countdown that freezes mission timers and gates ambient spawns,
  mission waves, and bosses alike. Debug character selection now closes its roster overlay so it
  cannot retain mouse focus over the newly selected body's BA. Old-project standalone Pixolotl
  bubble/ghost art is absent from this checkout; current fallback visuals remain in use pending
  those source assets.
- **0.10.3** — **Pixolotl motion and debug-swap fixes.** Shift now freezes all physical
  movement—including gravity, knockback, and carried momentum—between its authored rewind
  frame jumps and through recovery, while preserving the chosen frame velocity for release.
  E now uses robust held-key detection and adds a cyan active-time treatment. Debug body swaps
  retain only HP ratio and reset transient combat state, so a newly selected character can BA
  immediately rather than inheriting another body's reload or attack interval.
- **0.10.2** — **Pixolotl default-character integration.** New runs begin with Pixolotl
  leading the three-member active team (Pixolotl, Pomegraknight, PumpKing), while
  Cleopastar remains available on the bench. The direct Arena scene now also authors
  Pixolotl as its no-run body. Tab switching and held-item activation respect her skill
  commitment: E allows only movement and Branquias; Shift and its recovery allow neither.
- **0.10.1** — **Pixolotl temporal-control migration.** Pixolotl is playable through the
  debug protagonist roster with her 150 HP, 10 m/s route-building kit: pooled six-angle
  bubbles travel for 1.5 local-time seconds, rise, persist through foes once each, and
  live for 10 local-time seconds. Her E hold runs herself and all bubbles at 1.5× local
  time until release, ten generated frames, or 2.5 real seconds; her held Shift rewinds
  through the eight-frame path, restores historical velocity, reverses bubbles, and
  finishes with a 3×3 m Trapped control field plus vulnerable recovery. Added the generic
  local-time simulation contract (including debug self-tests) and canonical Trapped root
  support for players and foes, ready for future bullet-time content. (Verification:
  `dotnet build`, 0 errors/warnings; static scene/resource checks.)
- **0.10.0** — **Wonder-item prototype and Eidolon access.** Held items now persist unique
  instances and independent day/second cooldowns. FanChen's Heart, Yukai's Rope, and The
  Forgotten Kashaya, Pome's Bravery, and Pome's Seed have live combat passives and **F**-activated
  skills where applicable, delivered through Protect, Destroy, outer-BOSS, and Abandoned Cart
  rewards. Team Build now displays a stable-order 64 px icon backpack with hover labels. Pome's
  Bravery grants 60 OnFire stacks then converts in place to Seed; planting is deferred with the
  plantation system. TwistedReality is a possession: a BOSS death or
  fatal BOSS timer consumes it to restore the start of the current day. Its 4-day, no-stamina
  Bridge of Eidolon skill builds two violet legs and a midpoint Eidolon Shelter; Rest or Sharpen
  spends that Shelter's Blessing to cross. Generated LV4→LV5/`XX-S` passages are removed, so an
  LV4-origin Eidolon bridge is the only Zone-6 route. (Verification: `dotnet build`, 0 errors/
  warnings; static map/save/scene checks.)
- **0.9.0** — **Run slots, safe checkpoints, and pause.** Play now opens three persistent
  save slots: empty starts a run, occupied continues it. Versioned atomic snapshots retain the
  seed, map deltas (VOID/devour + reality bridges), node state, party/build/base stats, backpack,
  and counters while preserving unknown future fields. Esc in Map or Arena opens Continue,
  Settings, and Save & Quit. Battle checkpoints preserve the party and restart the same
  deterministic unfinished node rather than serializing live combat. Non-terminal goal/time
  resolution and Finish the Day auto-save; death/victory erase the slot. (Verification: `dotnet
  build`, 0 errors/warnings; static scene/input checks.)
- **0.8.1** — **Realm fields, one-way threshold, and city-based VOID pressure.** Every outer
  realm now rolls 3–5 Transportation Hubs and 4–6 degree-two Event nodes, with every new
  road/spoke rejected if it would cross or cover an existing path. Capitals sit beyond a central
  buffer, leaving an `XX-S` **Shelter** between each LV4 and LV5; this special Shelter crosses
  the singularity one-way and, like all current Hubs, only provides Rest, both Sharpen actions,
  and Team Build. Each outer city owns a deterministic clipped control field visible through the
  new Fields overlay. VOID devour now consumes the field: farthest half/all LV1 on days 10/20,
  farthest half/all LV2 on 30/40, all LV3 on 43, and all remaining outer cities on 45. Function
  nodes require a strict majority of their connected cities to have fallen. Reality bridges break
  if either endpoint is devoured and release their surviving endpoint for later reuse. (Verified:
  `dotnet build` and 120 deterministic seed maps for topology, field, devour, Shelter, and bridge
  invariants.)
- **0.8.0** — **Outer-world map reconstruction.** The five selected worlds receive seeded angular
  placement shares that sum to exactly 360°, but form five independent soft-edged flower petals:
  narrow beside the VOID and broad at asymmetric outer tips, with real sea gaps instead of
  adjoining wedges. Fixed-count combat nodes scatter first, then rank by
  VOID distance as LV4, LV3, `2-B`, `2-A`, `1-B`, `1-A`; every outer-world node reaches its inner
  `4-1` landmark through a short Euclidean spanning tree plus loops. The two final routes are
  always `LV3 → ? → LV4` and `LV3 → Shelter → LV4`, with their LV3 assignment seeded. Each world
  additionally rolls 1–3 Shelters and 1–3 Question Marks. A new global distance-sorted bridge
  pass selects 7–9 inter-world links (maximum two per realm pair), preserves a connected realm
  graph, and may insert a midpoint Shelter/Question function node. The former Voronoi territories
  and gameplay-looking barriers are removed: the atlas now draws triangulated coastlines, flat
  height tint/contours, and roads only, while the central VOID retains its existing generation.
  Map controls now use left-drag pan, right-drag player-pivot rotation, and the existing wheel
  zoom. Legacy combat-map terrain values `high`/`lowground` migrate to the canonical
  `high-ground`/`low-ground`. (Verification: `dotnet build`, 0 errors/warnings; 100 seeded maps
  checked for deterministic generation, placement-share totals, separated coasts, concave-coast
  triangulation, distance-ranked levels, local-road length, boss reachability, bridge rules,
  function routes/counts, and terrain labels.)
- **0.7.0** — **Authored combat maps are live.** Combat-node entry now deterministically selects
  a map by its authored world(s), hardship level(s), goal, and terrain instead of always using
  the fixed Arena. Map documents are v2 (v1's singular world field migrates safely): empty
  world/hardship filters mean all, goals default to claim/Collection, and terrain defaults to
  sea-level (high and lowground are ready for future overworld terrain generation). The new
  **Seashore** map is a real vertical slice for Pomegraknight's VanillaKindom (VK), levels 1–2,
  claim, sea-level: its character and respawn points, four Wonder Core goal points, map collision,
  and enemy nests load into the normal mission loop. Seashore mixes crabs/seagulls 50/50; crab
  nests are authored at rows ≤4, seagull nests at rows ≥5, and periodic spawning is now one foe
  every 4 seconds. Goal tiles are mission-relative: claim→Wonder Core points,
  protect→Condensed Core, destroy→enemy objectives, slaughter→unused. The map editor now exposes
  these attributes and spawn rules in **Map info**. (Verification: static checks + `dotnet build`.)
- **0.6.18** — **Map Creation playtest and environmental traversal.** The editor can now
  instantiate its current map with Pomegraknight, authored runtime collision, camera-anchored
  parallax, looping farview art, and deterministic opt-in Farview SoftVolumes. Effect-area
  painting now spans each complete multi-cell footprint and mirrors with Flip H. Platforms use a
  full-footprint fallback; authored narrow surfaces live solely in the global effect painter.
  SoftVolumes are enterable Area2D fields that preserve input, gravity, and jump launches while
  applying gradual resistance; clouds use a 0.3 stagnation index and palms use 0.1. The beach
  tsunami event darkens map visuals, adds visual wind/sway, and applies the wave's actor wind and
  hit payload. Scripts have been reorganized into the documented Foundation, Gameplay,
  Protagonists, Hazard, Orchestration, and UI modules. (Verification: `dotnet build`, 0 errors
  and 0 warnings.)
- **0.6.7** — **Map-creation rework (combat-map builder), per the new `Docs/MapCreation.gdd`.**
  Complete demolition + rebuild of `Scripts/MapCreation/` + `Scenes/MapCreation/` (built per
  `IMPLEMENTATION_REPORT.md` §11; main build landed in commit 98f4787, this version adds the
  review fixes + ship checklist). **Data layer** (`Scripts/MapCreation/Data/`, zero Godot,
  headless-testable): MapDocument/layers/sparse anchor-only tiles as STJ **properties** (fixes
  the "every save was `{}`" fields bug, with a `RoundTripSelfTest` boot guard), GUID file
  identity at `user://maps/<guid>.json` (fixes the duplicate-name overwrite bug; no
  `_index.json`), versioned+atomic `MapJson`, code-defined `TileRegistry` (18 starter defs
  incl. a 2×1 cloud, bonfire/freeze-pit hazards, spawns/goal/character, decorations), and a
  pure deterministic `RuleResolver` (rule tiles → flood-filled zones → per-zone
  `DetRandom.Sub` child streams, GDD §6). **Browser**: card grid (name/world/WxH/date),
  Create/Open/Rename/Duplicate/Delete. **Editor**: dedicated `GridView` child does ALL world
  drawing (fixes the draw-behind-children occlusion bug), manual pan/zoom (no Camera2D,
  wheel-to-cursor 0.25–4×), grid with 8-cell majors + LOD fade, layer-focus rendering (current
  layer 100% + sketchline, others 35% never 0), 8 tools (Paint/Erase/Rect/Marquee/Lasso/Move/
  Eyedropper/Bucket) with red-ghost rejection + whole-tile erase, 200-deep command stack (one
  stroke = one undo), full §7.3 shortcut set via InputMap (Cmd/Ctrl per-platform), layer panel
  enforcing the farview collision-only-at-parallax-(1,1)/loop-never policy (GDD §3), palette
  by category, canvas color + battlefield-bounds properties, and an in-editor "Preview
  generation" button (scratch-seed rule resolution, Esc clears). Runtime arena instantiation
  is deliberately NOT included (GDD §9 is contract-only, next milestone). Review fixes:
  Ctrl+V paste unreachable behind the bare-V tool shortcut (action-dispatch ordering), numpad
  zoom-out keycode, save-path IO guarding, id-less JSON rejected as not-a-map, stale selection
  refs on overwrite. *(0.6.1–0.6.6 shipped without changelog lines — see
  `IMPLEMENTATION_REPORT.md` §10 for that span. Static checks only — no toolchain this
  session.)*
- **0.6.0** — **First Team Build menu + wonder-item stub; PumpKing now testable; debug mode
  upgraded.** Three landings, all deliberately scoped:
  - **(a) PumpKing is playable/testable.** The character migrated in 0.5.3 is now actually
    reachable in the Arena via the 0.5.4 debug protagonist swap (key **4**) — no change to real
    progression (still Pomegraknight-only).
  - **(b) Debug mode upgraded.** On top of 0.5.4's key-4 protagonist page + `Docs/DEBUG_MODE.md`
    manual, the new shelter Team Build menu gains a **debug-mode visibility bypass**: with debug
    ON it also lists PumpKing and all 13 wonder items (tagged `[DBG]`) for assignment, without
    touching `RunState.Owned`/`ActiveBuild` or writing the catalog into the real backpack.
  - **(c) Minimal Team Build + item catalog stub.** New `Scripts/Items/ItemCatalog.cs` (id +
    display name for 13 items), one `ProtagonistState.HeldItemDefId` held slot, `RunState.
    HoldItem/UnholdItem` bookkeeping, and a **Team Build** menu at the shelter
    (`Scripts/Nodes/ShelterController.cs`) to assign a held item to a protagonist (a free
    management action, works with debug OFF too). Held-item provenance (`HeldItemFromBackpack`)
    keeps the debug bypass from ever materializing an item into the real economy.
  - **NOT in this release (still future work — `Docs/ITEMS.gdd` / T30 §4):** the full wonder-items
    economy — passives, item skills, day- and second-based cooldowns, perish/convert/plant
    lifecycle, Possession/Eternal tags, RolledStats, day-end item hooks, benching auto-return.
    Wonder items are inert id+name tokens for now. (Static checks only — no toolchain this
    session.)
- **0.5.4** — **Debug protagonist selector + debug manual.** With debug mode ON, key **4**
  opens a protagonist page on the `DebugManager` autoload (reachable from any scene, like the
  key-5 log viewer) listing every implemented protagonist from the new debug-layer
  `Scripts/Debug/ProtagonistRoster.cs` (Pomegraknight, PumpKing). Selecting one swaps the
  controlled body in-place in the Arena (position/signals/HUD/camera/HP-hydration re-wired via
  the normal `SetupPlayer` path) or queues the choice for the next Arena entry elsewhere —
  never touching `RunState.Owned`/`ActiveBuild` (real-run progression unaffected; gate is
  `DebugManager.Enabled`). New input action `debug_protagonist_page` (key 4). Fixes: the
  `SkipRequested` autoload-event subscriber leak (`GameManager._ExitTree` now unsubscribes) and
  a review-caught `_spawnPoint` mis-anchor on runtime-instanced bodies (new
  `CharacterController.SetSpawnPoint`). New **`Docs/DEBUG_MODE.md`** documents all debug
  tooling (DBG/SKIP/keys 4+5/F9/R/`DebugFoeLevel`/`DebugDay`), linked from 40-QA §1. (Static
  checks only — no toolchain this session.)
- **0.5.3** — PumpKing character migration: body + detachable-head sub-scene
  (`PumpKingHead.tscn`), full animation set, Soul free-flight kit (commit 8a3b73f).
- **0.5.2** — Pomegraknight sprite migration + animation automata (Unity sheets →
  AtlasTexture clips; commit 15e141e).
- **0.5.1** — Debug mode: DBG toggle, SKIP, live damage log + key-5 viewer, node jumping
  (`Scripts/Debug/`; commit b065886). *(0.5.1–0.5.3 lines back-filled at 0.5.4 — they
  originally shipped without changelog entries.)*
- **0.5.0** — **The run loop is playable end-to-end** (lands both roadmap milestones:
  v0.4.0 foe system + v0.5.0 node content; built per `IMPLEMENTATION_REPORT.md`, phases
  reviewed individually). **Foes** (`Scripts/Foes/`): BaseFoe FSM (patrol/sight/aggro),
  CrabFoe (Soft Shell, jump, spawn-on-death), SeagullFoe (patrol height, dive, Poop
  projectile/hazard), 8-level day-based scaling + evolution (`FoeStats`); old
  `Enemy.cs/.tscn` deleted. **Run layer** (`Scripts/Run/`): `RunState` autoload owns all
  run truth (day/stamina/visited/devour/party/cores/items), `BeginAdventure → scene →
  ReportGoal → EndDay` handshake, ordered `DayEndPipeline` (T30 §5), run-over/victory
  screen; `MapController` reduced to a view. **Missions** (`Scripts/Missions/`):
  Collection/Protect/Destroy/Slaughter rolled at mapgen (60:15:10:10) + structural Boss
  (LV4/LV6, placeholder scaled crab, 240/360 s permadeath timer), `GameManager` rebuilt
  as the arena integrator (procedural `ArenaBuilder` platforms/hazards, deterministic
  `FoeSpawner`, mission HUD with timer/progress/secondary bar/reward choice/Finish-the-
  Day, player HP carried through `ProtagonistState`). **Nodes**: Shelter Blessing
  actions (Rest / Sharpen ±10 ATK/DEF), generic `?`-EventRunner over authored
  `EventDef` verb data (`Scripts/Data/`), day-end summary toast on the map. WonderPage →
  wonder-core pickup. F9 = QA force-complete cheat (debug builds). All randomness
  through per-subsystem `DetRandom` sub-streams. (Verification: static only — no
  toolchain this session; run `dotnet build` at the next opportunity.)
- **0.3.7** — *Docs-only design-sync commit* (no runtime code changed). Resolved the last
  batch of pre-implementation design questions (Q22–32) and swept the docs to match:
  **in-run character leveling removed** across all 5 character GDDs — HP is now a flat base,
  permanent growth is the additive HP/ATK/DEF pools (Rest excess → max-HP pp, Sharpen → +10
  ATK/DEF), and **WonderPages renamed *wonder cores***; **Pomegraknight's Burning speed buff
  corrected +30% → +20%** to match the base OnFire status (code was already +20%); canonized
  the **Trapped** status (root: no input move/jump/dash, knockback-immune, gravity still
  applies, skills castable) in Sifu Pangda's GDD; added a Unity-port/Units provenance header
  to each character GDD. Instructions: added a `Gameplay.gdd` row to the 10-DESIGN ownership
  map, extended the TBD registry (#21 Ult model, #22 landing effects, #23 per-type victory
  loot, #24 Pixolotl realm), and fixed stale `Train`/`Conquer` terms in T30 and IDEAS. The
  GDD suite + `Instructions/` + `Tech/` are now cross-referenced and decision-logged, ready
  to drive the v0.4.0 foe build. (Verification: static/prose review only — no toolchain.)
- **0.2.1** — Damage mitigation now flows through **defense only**: removed the flat
  `DamageTakenMult` "damage reduction" multiplier (was only used by Fire Tornado's 0.6x); Fire
  Tornado now grants +66.7 defense via the same aggregatable `SetDefenseSource`/
  `ClearDefenseSource` mechanism Frozen uses, so there's a single damage-taken lever
  (`100/(100+defense)`) everywhere. Fixed the player rendering as a square: the placeholder
  sprite (`player_placeholder.svg`) was a 64×64 square regardless of the 44-wide collision
  box; both are now 48×64 (3:4 w:h). Bonfire/frozen-pit test hazards resized to match.
- **0.2.0** — Jump coyote time (0.2s grace after leaving a surface before a jump is forfeited,
  so 1-jump characters can edge-jump but not air-jump). Bumped `GroundAccel`/`AirAccel` ~20% for
  snappier control. Fire Tornado reshaped into a wide-and-short rectangle with a slight knockback
  instead of a launch. Screen shake now fires on every hit taken (player) or dealt (foe), via a
  static `ShakeCamera2D.Instance` so non-owning scripts (Enemy) can trigger it. **Hazard system:**
  `Hazard` base (Area2D, box collision built from `BoxSize` so it always matches the telegraph) +
  **FireHazard** (bonfire — 20pt OnFire stack + slight knockback every 0.25s), **FreezeHazard**
  (frozen pit — 20pt Frozen stack every 0.25s), **DamageHazard** (flat 20 dmg every 0.25s, no
  debuff). **TsunamiHazard** — one-shot pyramid sweep, 35% max-HP damage, a big leftward shove
  (delta-v solved from a 15 m target displacement), and a fixed 1s no-control window; spawned by
  **TsunamiTrigger** (a walk-into button). New stackable, self-decaying `DecayingDebuff`
  (10%/0.2s, integer, rounds up) backs **OnFire** (+20% move accel/speed, +0.2 aggregatable
  damage-dealt bonus) and **Frozen** (−20% move, +20% damage resistance) on both
  `CharacterController` and `Enemy`. 3 hazards placed on the arena's left edge for testing.
- **0.1.6** — Movement/combat core reconstruction. Velocity split into `intentVel` (input/gravity/
  jump/field) + `externalVel` (decaying impulses) so knockback finally works on players too;
  horizontal now has momentum (accel/friction). Per-skill `HitInfo` = damage + delta-v knockback +
  gain-no window (default 0.005·dmg; frozen control + animation). SoftVolume damps external impulses
  (viscous). Enemy rebuilt on the same model.
- **0.1.5** — Melee now hits when the cone overlaps a foe's body (radius-aware), not only its
  center. Per-character jump count (`MaxJumps`; Pomegraknight = 1) refreshed on ground/platform/
  SoftVolume, with a universal 0.3 s jump cooldown. Test tree made bigger and moved far right.
- **0.1.4** — Docs: added `KNOWLEDGE.md` (conventions + running Godot/C# caveats) and
  `CLAUDE.md` (read-first pointers + mandatory workflow: log every bug fix as a caveat,
  bump version per commit).
- **0.1.3** — Build fixes: Godot 4 has no `Label2D` (damage numbers rebuilt as Node2D+Label);
  `PomeSeed.Gravity` → `FallGravity` (was hiding `Area2D.Gravity`).
- **0.1.2** — Two platform kinds. The thin one-way **platform** (land/cross/drop-through) now has
  a sibling **SoftVolume** (`SoftVolume.cs`) — the go-inside "tree" archetype: stand on its one-way
  top or press-down to sink in; inside, falling halts and up/down move like left/right, capped by a
  **stagnation index** (0.5·maxSpeed) with a constant **gravity index** drift (0.1·maxSpeed). Applies
  to players *and* foes. A placeholder tree is placed in the arena.
- **0.1.1** — Standard units model (`Units.cs`: 2 m player / 8 m jump / 1 s ground jump ⇒
  g = 2048 px/s², 32 px/m). Named collision layers (Player/Foes/Ground/Platform/Projectile/
  Hazard). One-way platforms with **press-down drop-through** (hold = fall further; ground stays
  solid). Pome seeds **linger** where they land. Attack feedback: **camera shake**, enemy
  **blink**, real **knockback bounce**. **Damage-number** popups (red damage / green heal, size
  scales 20→100). **Version stamp** top-left; patch auto-bumps +0.0.1 per commit.
- **0.1.0** — Initial playable slice + Pomegraknight on a `CharacterController` base.


A **self-contained, playable vertical slice** now ships in this repo. The goal here was
*playability*, not feature parity — it proves out the core loop (arena + character +
skills + enemies + collectibles + win/lose) end-to-end so the fuller port below has a
skeleton to grow into. It is intentionally simpler than the full architecture in §1+.

### How to run it
1. Install Godot **4.7 (.NET/Mono build)** and .NET 8+ SDK.
2. Open this folder as a Godot project (the editor imports the SVG placeholders on first open).
3. Press **F5** (main scene is `res://Scenes/Arena.tscn`).

### Controls
| Action | Key / Mouse |
|---|---|
| Move | `A` / `D` (or `←` / `→`) |
| Jump / double-jump | `Space` |
| Melee combo (3-stage 15/15/30, mag 3, reload) | `J` or **Left Mouse** |
| Blush → self-ignite + Fire Tornado charge | `Left Shift` |
| Pome Seed Eruption (3 waves of gravity seeds) | `E` |
| Drop through platform (hold = fall further) | `S` / `Down` |
| Swim inside a soft volume (bamboo, cloud, water…) | `W` / `S` (or `↑` / `↓`) with `A` / `D`; `Space` is a jump stroke there, on the jump cooldown |
| Restart after win/lose | `R` |

Affected-range lines are drawn on-screen: the yellow **BA cone** (brightens on each
swing), the green **seed-launch cone** during E, and the orange **Fire Tornado box**
while it spins. Toggle via `ShowDebugRanges` on the character.

### The loop
Move around the arena, **collect 5 WonderPages** (gold, bobbing) to win while **crab foes**
spawn and chase you. Melee them in front of you; use Blush for a burst. Contact with a foe
costs HP with brief i-frames + knockback. Dying spends a life (3 total) and respawns you;
0 lives = Game Over. Win or lose, press `R` to replay.

### What was built (files)
```
Scripts/Gameplay/Characters/CharacterController.cs  Base playable char: movement/double-jump, HP/lives, i-frames,
                                knockback, death+respawn, Burning status, move penalty,
                                MeleeCone helper, debug-range draw hook, animation hook
Scripts/Protagonists/Pomegraknight/Pomegraknight.cs  First character: 3-stage combo w/ magazine+reload,
                                Blush + Fire Tornado, Pome Seed Eruption, burn passive, range lines
Scripts/Protagonists/Pomegraknight/PomeSeed.cs  Gravity seed projectile; per-wave shared hit registry
Scripts/Foes/                  BaseFoe hierarchy, crab/seagull foes and their projectiles
Scripts/Orchestration/Arena/GameManager.cs  Match/mission orchestration and run handshake
Scripts/UI/Hud.cs              HP, mission, party-switch and reward UI
Scripts/Gameplay/Combat/DecayingDebuff.cs  Stackable self-decaying status points;
                                backs the OnFire/Frozen hazard statuses
Scripts/Hazard/Hazard.cs        Base for a stationary periodic hazard box (collision built from
                                BoxSize; per-body tick timer; dispatches to Character/Enemy)
Scripts/Hazard/{Fire,Freeze,Damage}Hazard.cs  Bonfire / frozen pit / plain damage hazard
Scripts/Hazard/TsunamiHazard.cs  Animated 16x8-cell moving sweep (35% max HP, shove, stun)
Scripts/Hazard/TsunamiTrigger.cs  1x1 trigger: storm fade + shake + wave + restore
Scenes/Arena.tscn               Ground + 3 platforms + walls, spawn markers, HUD, player,
                                3 test hazards (bonfire/frozen pit/tsunami button) on the left
Scenes/Pomegraknight.tscn       Player (CharacterBody2D + Camera2D + FirePoint + empty AnimationPlayer)
Scenes/{Enemy,WonderPage,PomeSeed,Hud}.tscn
Assets/Sprites/*                Placeholder and production art, grouped like Scripts/
```
**Animation is stubbed, not skipped:** `Pomegraknight.tscn` has an empty `AnimationPlayer`,
`CharacterController.UpdateAnimator()` is the drive point, and BA/seed damage is applied inline
with a `// NOTE(animation)` marker showing where to move it onto AnimationPlayer call-method tracks.

`Scenes/Demo.tscn` + `Scripts/Protagonists/Legacy/Player.cs` are the original throwaway skeleton — kept for now,
safe to delete once the prototype is confirmed.

### How this maps to the full plan below
| Prototype (current) | Full port target (§1+) |
|---|---|
| `CharacterController` base + `Pomegraknight` subclass ✅ | same shape; add Pixolotl/PumpKing/Cleopastar |
| Direct `Input.IsActionJustPressed` in base `HandleAbilities` | `InputRouter` → `InputState` struct → `ChannelSystem` dispatch |
| Combo damage applied inline (anim hook noted) | Animation-event-driven damage on AnimationPlayer tracks |
| `PomeSeed` instantiated per shot | object-pooled projectiles |
| `GameManager.cs` (spawn+score+lives) | `GameManager` + `FoeManager` + `DifficultyManager` split |
| `Enemy.cs` (one crab, + burn DoT) | `BaseFoe` hierarchy (Crab/Drift/Seagull/Bee/Snake/Tsunami) |
| Single camera | Split-screen via two `SubViewport`s |
| Collision layers: 1=world, 2=player, 4=enemy, 8=pickup, seeds mask 4 | (adopt same convention) |

### Deliberate cuts (deferred to the phases below)
Two-player/split-screen, the ChannelSystem aim/trajectory/area skill types, object pooling,
real animation (AnimationPlayer is stubbed; sprites are static), audio, difficulty scaling,
Tsunami/hazards, other characters (Pixolotl/PumpKing/Cleopastar), leveling/status-VFX,
damage numbers, and gamepad polish. The §9 API cheat-sheet and per-file porting steps below
remain the roadmap for adding these.

Pomegraknight uses pixel units (~32 px/m); its exported stats are tuned for feel, not
1:1 with the Unity metric values in `CLAUDE.md` — reconcile these during full tuning.

### Not yet verified at runtime
This machine has **no Godot/.NET toolchain installed**, so the prototype was verified
*statically* (brace/paren balance, every `.tscn` resource path resolves, every `GetNode`
path matches its scene). First editor open is the real smoke test — see §10 Phase 1 checklist.

---

## 1. High-Level Architecture (Godot Edition)

### 1.1 Language Choice: **C# (.NET)**

Use Godot with .NET/C# — the existing codebase is ~12,000 lines of clean C# with zero LINQ, zero async/await, and no exotic Unity-only APIs. Most game logic (math, state machines, data structures) ports directly with only API surface changes. GDScript would require a full rewrite; C# in Godot 4 uses the same .NET 8 runtime and is production-ready.

### 1.2 Unity → Godot Concept Map

| Unity | Godot |
|---|---|
| `GameObject` | `Node` (any type) |
| `MonoBehaviour` | `Node` script (C# class extending a node type) |
| `Prefab` | `PackedScene` (`.tscn` file) |
| `Scene` (`.unity`) | `PackedScene` (`.tscn`) |
| `[SerializeField] private` | `[Export] private` |
| `Start()` | `_Ready()` |
| `Update()` | `_Process(double delta)` |
| `FixedUpdate()` | `_PhysicsProcess(double delta)` |
| `Time.deltaTime` | `(float)delta` (parameter) |
| `Time.time` | `Time.GetTicksMsec() / 1000.0f` |
| `GetComponent<T>()` | `GetNode<T>("path")` or `@onready` |
| `Instantiate(prefab)` | `PackedScene.Instantiate()` or `ResourceLoader.Load<PackedScene>().Instantiate()` |
| `Destroy(gameObject)` | `QueueFree()` |
| `StartCoroutine(IEnumerator)` | `async Task` / `await ToSignal(...)` |
| `WaitForSeconds(n)` | `await ToSignal(GetTree().CreateTimer(n), "timeout")` |
| `Debug.Log()` | `GD.Print()` |
| `Rigidbody2D` | `RigidBody2D` or `CharacterBody2D` |
| `Physics2D.OverlapCircle` | `PhysicsShapeQueryParameters2D` + direct space queries |
| `Animator` + Animation Events | `AnimationPlayer` + `call_method` tracks |
| `InputActionAsset` / Input System | `Input` singleton + Input Map |
| `LayerMask` | Collision layers (bitwise) |
| `Camera.main` | `GetViewport().GetCamera2D()` |
| `ScriptableObject` | `Resource` |
| `TMP_Text` | `Label` / `RichTextLabel` |
| `SceneManager.LoadScene()` | `GetTree().ChangeSceneToFile()` |
| `InvokeRepeating()` | `Timer` node or `SceneTreeTimer` |
| `#if UNITY_EDITOR` / Gizmos | `#if TOOLS` / `_Draw()` |

### 1.3 Target Folder Structure (Godot Project)

For the `.cs` script from Unity, godot filetype or cs type are all accepted, based on the feasability.
Original project direction: `/Users/cyclops/GloryOfFableland`

```
res://
├── Characters/
│   ├── Base/
│   │   ├── CharacterController.cs
│   │   ├── PlayerAgent.cs
│   │   └── CharacterController.tscn   (base scene with shared nodes)
│   ├── Pomegraknight/
│   │   ├── Pomegraknight.cs
│   │   ├── Pomegraknight.tscn
│   │   ├── PomeSeed.cs
│   │   └── PomeSeed.tscn
│   ├── Cleopastar/
│   ├── Pixolotl/
│   └── PumpKing/
├── Foes/
│   ├── BaseFoe.cs
│   ├── FoeManager.cs
│   ├── CrabFoe/
│   ├── DriftFoe/
│   ├── SeagullFoe/
│   ├── BeeFoe/
│   ├── SnakeFoe/
│   └── Tsunami/
├── Projectiles/
│   ├── BaseProjectile.cs
│   ├── PixelBubble/
│   ├── PomoSeed/
│   ├── PoopProjectile/
│   ├── RollingBall/
│   └── CleopastarStar/
├── Gameplay/
│   ├── GameManager.cs
│   ├── DifficultyManager.cs
│   ├── CameraController.cs
│   ├── ChannelSystem.cs
│   ├── WonderPage.cs
│   └── WonderPageCollector.cs
├── Input/
│   ├── InputRouter.cs
│   └── InputState.cs
├── UI/
│   ├── PlayerHUDController.cs
│   ├── AnnouncementHUD.cs
│   ├── DamageNumberManager.cs
│   ├── EmotionTracker.cs
│   ├── CharacterBubble.cs
│   └── CharacterStatusVFX.cs
├── Collectable/
│   ├── HealthPickup.cs
│   ├── HeldItem.cs (Resource)
│   └── FastClawCollectible.cs
├── Maps/
│   └── MainArena.tscn
├── Assets/Sprites/   (copied from Unity Assets/Sprites/)
├── Audio/            (copied from Unity Assets/Audio/)
└── project.godot
```

### 1.4 Godot Node Hierarchy (MainArena.tscn)

```
MainArena (Node2D)
├── Camera2D (for fullscreen or one camera for split-screen)
├── TileMapLayer (ground / platforms)
├── HazardAreas (Node2D)
│   ├── FireHazard (Area2D)
│   └── FatalHazard (Area2D)
├── P1_Agent (Node2D)
│   ├── InputRouter.cs
│   ├── ChannelSystem.cs
│   ├── PlayerHUDController.cs
│   ├── EmotionTracker.cs
│   ├── CharacterStatusVFX.cs
│   ├── WonderPageCollector.cs
│   └── CharacterContainer (Node2D)  ← characters parented here
├── P2_Agent (Node2D)  (same structure)
├── GameManager (Node)
├── DifficultyManager (Node)
├── FoeManagers (Node2D)
│   ├── FoeManager_Crab (Node)
│   ├── FoeManager_Drift (Node)
│   └── ...
├── AnnouncementHUD (CanvasLayer)
├── DamageNumberManager (Node2D)
├── WonderPages (Node2D)
└── SpawnPoints (Node2D)
    ├── PlayerSpawns/
    └── PageSpawns/
```

### 1.5 Data Flow (unchanged from Unity)

```
Input (keyboard/mouse/gamepad)
  → InputRouter (one per player, outputs InputState struct)
    → CharacterController._Process() → HandleAbilities()
      → ChannelSystem.Activate(skillName, direction, magnitude)
        → Character subclass HandleBA/HandleSkill1/HandleSkill2/HandleSkillUlt()
          → spawns projectiles, applies damage, triggers effects
```

---

## 2. Setup — Godot Project Creation

### Step 2.1: Install Prerequisites
- Install Godot 4.4+ (with .NET support): `brew install godot-mono` (macOS) or download from godotengine.org
- Verify: `godot --version` and `dotnet --version` (needs .NET 8+)

### Step 2.2: Create Project
```bash
mkdir -p ~/GloryOfFableland-Godot
cd ~/GloryOfFableland-Godot
godot --editor &  # Creates new project via launcher, select .NET
```
- Project name: `Glory of Fableland`
- Renderer: **Compatibility** (2D game, no 3D needed)
- Version control: Git init immediately

### Step 2.3: Input Map Setup
Open `Project → Project Settings → Input Map` and add these actions:

```
move_left    → A key, Left arrow, Gamepad Left Stick Left
move_right   → D key, Right arrow, Gamepad Left Stick Right
move_up      → W key, Up arrow, Gamepad Left Stick Up
move_down    → S key, Down arrow, Gamepad Left Stick Down
jump         → Space, Gamepad Button South (A/Cross)

basic_attack → Left Mouse Button, Gamepad Right Trigger
skill1       → Left Shift, Gamepad Left Shoulder (L1)
skill2       → E key, Gamepad Right Shoulder (R1)
ult          → Q key, Gamepad Left Trigger (L2)
use_item     → F key, Gamepad Button East (B/Circle)
drop_item    → Right Mouse Button, Gamepad Button North (Y/Triangle)

aim_horizontal → Mouse motion X, Gamepad Right Stick X
aim_vertical   → Mouse motion Y, Gamepad Right Stick Y
```

### Step 2.4: Copy Assets
```bash
# From Unity project to Godot project
cp -r ~/GloryOfFableland/Assets/Sprites ~/GloryOfFableland-Godot/Assets/Sprites
cp -r ~/GloryOfFableland/Assets/Audio ~/GloryOfFableland-Godot/Audio
```

### Step 2.5: Create Folder Structure
Create all folders listed in §1.3. Commit.

---

## 3. Phase 1 — Prototype: Map + Camera + Input (Week 1, Days 1-3)

> **Goal:** A navigation-ready arena with a working camera and input system. No characters yet.

### Day 1: MainArena Map

#### Task 1.1: Create MainArena.tscn
- Root node: `Node2D` named "MainArena"
- Add `Camera2D` as child, enable `Current`, set zoom `(1, 1)`, limit to map bounds
- Add `TileMapLayer` node for the ground

#### Task 1.2: Build Arena Layout
- The arena is 80×50 world units (from Unity `CameraController.mapWidth/mapHeight`)
- Ground level at y=0, spanning x: 0 to 80
- Platforms at various heights for vertical gameplay
- Reference the Unity scene visually: open `Assets/Scenes/MainArena.unity` in Unity Editor to see the layout
- Paint ground tiles using a simple colored rectangle sprite as placeholder
- Add invisible `StaticBody2D` walls at arena edges (x=0, x=80) with `CollisionShape2D`

#### Task 1.3: Create Spawn Point Markers
- Create `Marker2D` nodes grouped under `SpawnPoints/PlayerSpawns/` (10 markers)
- Create `Marker2D` nodes grouped under `SpawnPoints/PageSpawns/` (5 markers)

### Day 2: Camera System

#### Task 2.1: Port CameraController.cs
```
Source: Assets/Scripts/Gameplay/CameraController.cs
Target: res://Gameplay/CameraController.cs
```

Key changes:
- No `Camera` component reference → use `Camera2D` node reference
- `Screen.width/height` → `DisplayServer.WindowGetSize()`
- `Vector3.Lerp` → `Position.Lerp()`
- `Time.deltaTime` → `(float)delta` in `_Process`
- `Mathf.Clamp` → `Mathf.Clamp`
- `OnDrawGizmos()` → `_Draw()` (Godot's debug draw)
- Split-screen: Use two `SubViewport` nodes instead of `Camera.rect`
  - For the prototype, implement single-camera first; split-screen comes later

```csharp
// Godot CameraController skeleton
using Godot;

public partial class CameraController : Node2D
{
    [Export] public bool IsSplitScreen = false;
    [Export] public float CameraWidth = 25f;
    [Export] public float MapWidth = 80f;
    [Export] public float MapHeight = 50f;
    [Export] public float SmoothSpeed = 5f;

    public Node2D Target1;
    public Node2D Target2;
    public Vector2 ShakeOffset;

    private Camera2D _camera1;
    private Camera2D _camera2;
    private float _cameraHeight;

    public override void _Ready()
    {
        _camera1 = GetNode<Camera2D>("Camera2D"); // or find
        _cameraHeight = CameraWidth / GetViewport().GetVisibleRect().Size.Aspect();
        // SetupCameras() equivalent
    }

    public override void _Process(double delta)
    {
        if (Target1 != null) UpdateCamera(_camera1, Target1, (float)delta);
        // Remove shake offset each frame (or decay it)
    }

    private void UpdateCamera(Camera2D cam, Node2D target, float delta)
    {
        Vector2 targetPos = target.GlobalPosition;
        Vector2 smoothed = cam.GlobalPosition.Lerp(targetPos, SmoothSpeed * delta);
        smoothed.X = Mathf.Clamp(smoothed.X, CameraWidth / 2f, MapWidth - CameraWidth / 2f);
        smoothed.Y = Mathf.Clamp(smoothed.Y, _cameraHeight / 2f, MapHeight - _cameraHeight / 2f);
        cam.GlobalPosition = smoothed + ShakeOffset;
    }

    public void SetTarget1(Node2D t) => Target1 = t;
    public void SetTarget2(Node2D t) => Target2 = t;
}
```

### Day 3: Input System

#### Task 3.1: Port InputState struct
```
Source: Assets/Input/InputRouter.cs (InputState struct)
Target: res://Input/InputState.cs
```
Direct port — the struct is pure data with no Unity dependencies:

```csharp
// res://Input/InputState.cs
using Godot;

public struct InputState
{
    // Movement
    public float MoveX;

    // Aiming
    public Vector2 AimDirection;
    public float AimMagnitude;

    // Jump
    public bool JumpDown;

    // Skills
    public bool BasicAttackHeld;
    public bool BasicAttackDown;
    public bool BasicAttackUp;
    public bool Skill1Down;
    public bool Skill2Down;
    public bool UltDown;
    public bool UseHeldItemDown;
    public bool DropWonderItemDown;
}
```

#### Task 3.2: Port InputRouter.cs

Key mapping (Unity → Godot Input):
- `Keyboard.current.aKey.isPressed` → `Input.IsKeyPressed(Key.A)`
- `Keyboard.current.spaceKey.wasPressedThisFrame` → `Input.IsActionJustPressed("jump")`
- `Mouse.current` position → `GetViewport().GetMousePosition()`
- `Camera.main.ScreenToWorldPoint()` → `GetViewport().GetCamera2D().GetScreenTransform().AffineInverse() * screenPos`
  - Or use `GetGlobalMousePosition()` directly
- `Gamepad.all` → `Input.GetConnectedJoypads()`
- `gp.leftStick.ReadValue()` → `Input.GetJoyAxis(deviceId, JoyAxis.LeftX)` / `JoyAxis.LeftY`
- `gp.rightStick.ReadValue()` → `Input.GetJoyAxis(deviceId, JoyAxis.RightX)` / `JoyAxis.RightY`
- `gp.rightTrigger.ReadValue()` → `Input.GetJoyAxis(deviceId, JoyAxis.RightTrigger)`
- `wasPressedThisFrame` → `Input.IsActionJustPressed()`
- `wasReleasedThisFrame` → `Input.IsActionJustReleased()`

```csharp
// res://Input/InputRouter.cs — skeleton
using Godot;

public partial class InputRouter : Node
{
    [Export] public int PlayerID = 1;
    [Export] public float MouseDeadZonePx = 10f;

    public Node2D CharacterTransform { get; set; }
    public InputState State { get; private set; }

    private bool _usingGamepad;
    private Camera2D _cam;

    public override void _Ready()
    {
        _cam = GetViewport().GetCamera2D();
    }

    public override void _Process(double delta)
    {
        DetectInputDevice();
        State = BuildState();
    }

    private void DetectInputDevice()
    {
        var joypads = Input.GetConnectedJoypads();
        int gpIndex = PlayerID - 1;
        _usingGamepad = gpIndex < joypads.Count;
    }

    private InputState BuildState()
    {
        var s = new InputState();
        if (_usingGamepad)
            FillFromGamepad(ref s);
        else
            FillFromKeyboardMouse(ref s);
        return s;
    }

    private void FillFromKeyboardMouse(ref InputState s)
    {
        s.MoveX = 0f;
        if (Input.IsActionPressed("move_left"))  s.MoveX -= 1f;
        if (Input.IsActionPressed("move_right")) s.MoveX += 1f;
        s.JumpDown = Input.IsActionJustPressed("jump");
        s.Skill1Down = Input.IsActionJustPressed("skill1");
        s.Skill2Down = Input.IsActionJustPressed("skill2");
        s.UltDown = Input.IsActionJustPressed("ult");
        s.UseHeldItemDown = Input.IsActionJustPressed("use_item");
        s.DropWonderItemDown = Input.IsActionJustPressed("drop_item");

        // Mouse aim
        s.BasicAttackHeld = Input.IsActionPressed("basic_attack");
        s.BasicAttackDown = Input.IsActionJustPressed("basic_attack");
        s.BasicAttackUp   = Input.IsActionJustReleased("basic_attack");

        Vector2 mouseWorld = GetGlobalMousePosition();
        Vector2 charWorld = CharacterTransform?.GlobalPosition ?? Vector2.Zero;
        Vector2 delta = mouseWorld - charWorld;
        if (delta.Length() > 0.05f)
        {
            s.AimDirection = delta.Normalized();
            s.AimMagnitude = 1f;
        }
        else
        {
            s.AimDirection = Vector2.Right;
            s.AimMagnitude = 0f;
        }
    }

    private void FillFromGamepad(ref InputState s)
    {
        int deviceId = PlayerID - 1;
        // Left stick movement
        float lsX = Input.GetJoyAxis(deviceId, JoyAxis.LeftX);
        s.MoveX = Mathf.Abs(lsX) > 0.15f ? lsX : 0f;

        s.JumpDown = Input.IsActionJustPressed("jump");

        // Right stick aim
        float rsX = Input.GetJoyAxis(deviceId, JoyAxis.RightX);
        float rsY = Input.GetJoyAxis(deviceId, JoyAxis.RightY);
        float rsMag = Mathf.Sqrt(rsX * rsX + rsY * rsY);
        if (rsMag > 0.15f)
        {
            s.AimDirection = new Vector2(rsX, rsY).Normalized();
            s.AimMagnitude = Mathf.Clamp(rsMag, 0f, 1f);
        }
        else
        {
            s.AimDirection = Vector2.Right;
            s.AimMagnitude = 0f;
        }

        s.BasicAttackHeld = Input.GetJoyAxis(deviceId, JoyAxis.RightTrigger) > 0.5f;
        s.BasicAttackDown = Input.IsActionJustPressed("basic_attack");
        s.BasicAttackUp   = Input.IsActionJustReleased("basic_attack");
        s.Skill1Down = Input.IsActionJustPressed("skill1");
        s.Skill2Down = Input.IsActionJustPressed("skill2");
        s.UltDown = Input.IsActionJustPressed("ult");
        s.UseHeldItemDown = Input.IsActionJustPressed("use_item");
    }
}
```

#### Task 3.3: Verify Input Loop
- Attach `InputRouter` to a test node in the scene
- Add temporary `GD.Print()` to log input state each frame
- Run the scene, verify keyboard and gamepad input both produce correct values

---

## 4. Phase 2 — Pomegraknight: The First Character (Week 1 Days 4-5 + Week 2 Days 1-5)

> **Goal:** Pomegraknight fully playable — movement, BA 3-hit combo, Blush, Pome Seed Eruption, Fire Tornado, HP/damage, death/respawn, HUD.

### Step 4.0: Understand What You're Porting

Before writing any code, read these Unity source files:
1. `CharacterController.cs` (1159 lines) — the base class
2. `Pomegraknight.cs` (594 lines) — the character implementation
3. `ChannelSystem.cs` (290 lines) — skill dispatch
4. `PlayerAgent.cs` (320 lines) — player slot manager
5. `BaseFoe.cs` (467 lines) — enemy base (needed for hit detection targets)
6. `PomeSeed.cs` (212 lines) — the projectile spawned by E

### Week 2 Day 1: ChannelSystem

#### Task 4.1: Port ChannelSystem.cs
```
Source: Assets/Scripts/Gameplay/ChannelSystem.cs
Target: res://Gameplay/ChannelSystem.cs
```

Changes:
- `MonoBehaviour` → `Node`
- `FindObjectsByType<CharacterController>` → `GetTree().GetNodesInGroup("Character")`
- `FindObjectsByType<BaseFoe>` → `GetTree().GetNodesInGroup("Foe")`
- `Mouse.current` → `Input.GetLastMouseVelocity()` for mouse detection
- `Camera.main.ScreenToWorldPoint()` → use `GetViewport().GetCamera2D().GetScreenTransform()`
- `Debug.Log` → `GD.Print`
- `SkillRegistration` class: `Action` → `System.Action`, `[Serializable]` → keep (C# is fine)

Godot-specific: Characters and foes will need to add themselves to groups:
```csharp
// In CharacterController._Ready():
AddToGroup("Character");

// In BaseFoe._Ready():
AddToGroup("Foe");
```

The `ChannelType` enum and resolution logic ports directly — the math (`Mathf.Atan2`, `Mathf.DeltaAngle`, `Mathf.Deg2Rad`) exists identically in Godot's `Mathf` class.

### Week 2 Day 2: CharacterController Base

#### Task 4.2: Port CharacterController.cs

This is the largest single file. Use `CharacterBody2D` as the base node type (it provides built-in `Velocity`, `MoveAndSlide()`, and ground detection).

```
Source: Assets/Scripts/Characters/BaseCharacter/CharacterController.cs (1159 lines)
Target: res://Characters/Base/CharacterController.cs
```

**Step-by-step porting strategy:**

**4.2.1: Class declaration and node type**
```csharp
// Unity
public class CharacterController : MonoBehaviour

// Godot — extend CharacterBody2D for built-in physics
public partial class CharacterController : CharacterBody2D
```

**4.2.2: Inner types** — Buff, BuffType, FiringSegment: direct port, no changes.

**4.2.3: Fields**
- `[Header("...")]` → `[ExportGroup("...")]`
- `[Tooltip("...")]` → `[Export(PropertyHint.MultilineText, "...")]` or just comment above
- `[SerializeField] private` → `[Export] private`
- `[HideInInspector]` → `// private field, no export`
- `Rigidbody2D rb` → remove — `CharacterBody2D` has built-in `Velocity`
- `Collider2D col` → remove — `CharacterBody2D` manages its own collider
- `SpriteRenderer spriteRenderer` → `Sprite2D` node (get via `@onready`)
- `Animator animator` → `AnimationPlayer` node (get via `@onready`)

**4.2.4: `_Ready()` (was `Start()`)**
```csharp
public override void _Ready()
{
    // Get node references
    _sprite = GetNode<Sprite2D>("Sprite2D");
    _animPlayer = GetNode<AnimationPlayer>("AnimationPlayer");
    _inputRouter = GetParent().GetNode<InputRouter>("InputRouter"); // lives on PlayerAgent
    _channelSystem = GetParent().GetNode<ChannelSystem>("ChannelSystem");

    // Init character-specific stats
    InitCharacter();

    // Derive jump physics
    _gravity = -(2f * jumpHeight) / (timeToApex * timeToApex);
    _jumpVelocity = (2f * jumpHeight) / timeToApex;

    jumpsRemaining = maxJumps;
    currentAmmo = magazineSize;

    RegisterSkillsInternal();
    RegisterSkills();
}
```

**4.2.5: `_Process(double delta)` (was `Update()`)**
```csharp
public override void _Process(double delta)
{
    float dt = (float)delta;
    if (controlsLocked) return;

    GetInput();
    UpdateStatusEffects(dt);
    UpdateBuffs(dt);
    HandleAbilities();
    UpdateFirePoint(dt);
    UpdateAmmo(dt);
    UpdateAnimator(dt);
}
```

**4.2.6: `_PhysicsProcess(double delta)` (was `FixedUpdate()`)**
```csharp
public override void _PhysicsProcess(double delta)
{
    float dt = (float)delta;
    if (controlsLocked) return;

    if (isFreeFlying)
    {
        HandleMovement();
        HandleFreeFlying();
        Velocity += externalForce;
        externalForce = Vector2.Zero;
        MoveAndSlide();
        return;
    }

    CheckGrounded();

    if (isGravityExempt)
        Velocity = new Vector2(Velocity.X, 0f);
    else if (isFloating)
        ApplyFloatingGravity(dt);
    else
        ApplyGravity(dt);

    HandleMovement();
    HandleJump();

    Velocity += externalForce;
    externalForce = Vector2.Zero;
    MoveAndSlide();
}
```

**4.2.7: Ground detection**
Unity uses `Physics2D.OverlapCircle(groundCheckPoint.position, radius, layerMask)`.
Godot `CharacterBody2D` has built-in `IsOnFloor()` → use it.

```csharp
void CheckGrounded()
{
    isGrounded = IsOnFloor();
    if (isGrounded)
    {
        lastGroundedTime = Time.GetTicksMsec() / 1000.0f;
        jumpsRemaining = maxJumps;
    }
}
```

**4.2.8: Gravity and jump**
Godot `CharacterBody2D.Velocity` replaces manual `rb.linearVelocity` manipulation:
```csharp
void ApplyGravity(float dt)
{
    if (IsOnFloor() && Velocity.Y <= 0f)
    {
        Velocity = new Vector2(Velocity.X, 0f);
        return;
    }
    float vy = Velocity.Y + _gravity * gravityMultiplier * dt;
    Velocity = new Vector2(Velocity.X, Mathf.Max(vy, maxFallSpeed));
}

void HandleJump()
{
    if (isStunned) return;

    float now = Time.GetTicksMsec() / 1000.0f;
    bool coyote = now - lastGroundedTime <= coyoteTime;
    bool buffered = now - lastJumpPressedTime <= jumpBufferTime;
    bool canJump = IsOnFloor() || (coyote && jumpsRemaining == maxJumps) || jumpsRemaining > 0;

    if (buffered && canJump)
    {
        Velocity = new Vector2(Velocity.X, _jumpVelocity);
        jumpsRemaining--;
        lastJumpPressedTime = -999f;
    }
}
```

**4.2.9: Movement**
```csharp
void HandleMovement()
{
    float speed = moveSpeed;
    if (isBurning) speed *= 1.3f;
    if (isIcy)     speed *= 0.6f;
    if (isStunned) speed  = 0f;

    float control = IsOnFloor() ? 1f : airControl;
    float targetVX = horizontalInput * speed * control;

    Velocity = new Vector2(targetVX, Velocity.Y);

    // Flip sprite
    if (horizontalInput > 0.01f) { facingRight = true;  _sprite.FlipH = false; }
    else if (horizontalInput < -0.01f) { facingRight = false; _sprite.FlipH = true; }
}
```

**4.2.10: Coroutines → async/await**
This is the most significant API change. Map all `IEnumerator` coroutines to `async void` or `async Task`:

| Unity | Godot |
|---|---|
| `StartCoroutine(Routine())` | `_ = Routine();` (fire-and-forget async void) |
| `StopCoroutine(nameof(Routine))` | Use `CancellationTokenSource` and `Cancel()` |
| `yield return new WaitForSeconds(n)` | `await ToSignal(GetTree().CreateTimer(n), "timeout")` |
| `yield return null` | `await ToSignal(GetTree(), "process_frame")` |
| `yield break` | `return;` |

Example — `FlashOnHit`:
```csharp
// Unity
IEnumerator FlashOnHit()
{
    spriteRenderer.color = Color.red;
    yield return new WaitForSeconds(0.12f);
    if (!isDead) spriteRenderer.color = original;
}

// Godot
async void FlashOnHit()
{
    _sprite.Modulate = Colors.Red;
    await ToSignal(GetTree().CreateTimer(0.12f), "timeout");
    if (!isDead) _sprite.Modulate = _originalColor;
}
```

Example — `TransformationProtection`:
```csharp
// Unity
IEnumerator TransformationProtection(float duration)
{
    controlsLocked = true;
    yield return new WaitForSeconds(duration);
    controlsLocked = false;
}

// Godot
async void TransformationProtection(float duration)
{
    controlsLocked = true;
    await ToSignal(GetTree().CreateTimer(duration), "timeout");
    controlsLocked = false;
}
```

**4.2.11: Hit detection (Physics2D → Godot queries)**
```csharp
// Unity: Physics2D.OverlapCircleAll(transform.position, range)
// Godot:
var spaceState = GetWorld2D().DirectSpaceState;
var query = new PhysicsShapeQueryParameters2D();
var circleShape = new CircleShape2D();
circleShape.Radius = range;
query.Shape = circleShape;
query.Transform = new Transform2D(0, GlobalPosition);
query.CollisionMask = /* layer mask for characters + foes */;
var results = spaceState.IntersectShape(query);
foreach (var result in results)
{
    Node2D body = (Node2D)result["collider"];
    // ...
}
```

**4.2.12: Animator → AnimationPlayer**
```csharp
// Unity
animator.SetBool("IsGrounded", isGrounded);
animator.SetFloat("Speed", speed);
animator.SetTrigger("Jump");

// Godot — use string names or StringName constants
_animPlayer.Set("parameters/conditions/IsGrounded", isGrounded);

// For triggers: use a method-call track in AnimationPlayer,
// or use a parameter system with AnimationTree
// Simpler approach for the prototype: just call methods directly
// instead of going through animation parameters. The animation
// parameters in Unity were used because Unity forces it.
// In Godot, you can call methods on the script directly.
```

**For the prototype, bypass animation-driven combat timing:**
Instead of Unity Animation Events driving `OnComboHit()`, use timer-based callbacks:
```csharp
// Pomegraknight combo: fire the hit after a timed delay instead of
// waiting for an animation event
async void TriggerComboStage(int stage)
{
    float hitDelay = stage switch
    {
        0 => 0.25f, // Slash1: hit at frame 4 of 6 (0.25s into 0.5s clip)
        1 => 0.00f, // Slash2: hit at frame 1 of 4 (0s into 0.333s clip)
        2 => 0.25f, // Slash3: hit at frame 4 of 5 (0.25s into 0.417s clip)
        _ => 0.25f
    };

    // Play animation
    _animPlayer.Play(k_SlashAnimNames[stage]);

    // Wait for the hit frame
    await ToSignal(GetTree().CreateTimer(hitDelay), "timeout");

    // Perform hit detection
    OnComboHit(stage);
}
```

### Week 2 Day 3: Pomegraknight Implementation

#### Task 4.3: Port Pomegraknight.cs

```
Source: Assets/Scripts/Characters/Pomegraknight/Pomegraknight.cs (594 lines)
Target: res://Characters/Pomegraknight/Pomegraknight.cs
```

**Key ports:**

**4.3.1: InitCharacter()** — direct port, just change `Debug.LogError` → `GD.PrintErr`

**4.3.2: HandleBA (3-hit combo)**
- Same logic: ammo acts as combo counter
- Animation triggers become `_animPlayer.Play("slash_1")` etc.
- `StopCoroutine`/`StartCoroutine` → `SwingPenaltyRoutine()` becomes async

**4.3.3: HandleSkill1 (Blush)**
- `ApplyBurning(blushDuration)` — ported from base class
- `animator.SetTrigger(ANIM_BLUSH)` → `_animPlayer.Play("blush")`
- Fire tornado window: same timer logic (`Time.time` → `Time.GetTicksMsec()/1000f`)

**4.3.4: HandleSkill2 (Pome Seed Eruption)**
- `SeedEruptionRoutine` becomes async
- `FireSeedWave(count)` spawns seeds — requires PomeSeed.tscn

**4.3.5: FireTornadoRoutine**
- `controlsLocked` logic identical
- `yield return new WaitForSeconds(tornadoTickInterval)` → `await ToSignal(GetTree().CreateTimer(interval), "timeout")`
- Hit detection: `Physics2D.OverlapBoxAll` → Godot `IntersectShape` with `RectangleShape2D`

**4.3.6: PerformSlashHitDetection**
- `Physics2D.OverlapCircleAll` → Godot `IntersectShape` with `CircleShape2D`
- Facing angle logic identical
- Get character/foe components via `GetNode<>()` or groups

### Week 2 Day 4: Pomegraknight.tscn Scene + PomeSeed

#### Task 4.4: Create Pomegraknight.tscn

```
Pomegraknight (CharacterBody2D)
├── Sprite2D
├── CollisionShape2D (CapsuleShape2D or RectangleShape2D)
├── AnimationPlayer
├── PomeSeedSpawnPoint (Marker2D)
└── FireVFX (GpuParticles2D, optional for prototype)
```

- Attach `Pomegraknight.cs` script
- Set collision layer/mask: layer 2 for "Characters", mask to include "Ground", "Foes", "Characters"
- Configure `CharacterBody2D` motion mode: `Grounded`

#### Task 4.5: Port PomeSeed.cs + Create PomeSeed.tscn

```
Source: Assets/Scripts/Projectiles/PomoSeed.cs (212 lines)
Target: res://Characters/Pomegraknight/PomeSeed.cs + PomeSeed.tscn
```

PomeSeed uses gravity-affected `Rigidbody2D` — in Godot, use `RigidBody2D`:
- `rb.linearVelocity` → `LinearVelocity`
- Wave hit tracking (`HashSet<int>`) — identical logic
- `Destroy(gameObject, lifetime)` → set `Timer` node for lifetime, then `QueueFree()`
- Contact detection: connect `body_entered` signal or override `_OnBodyEntered()`

PomeSeed.tscn:
```
PomeSeed (RigidBody2D)
├── Sprite2D
├── CollisionShape2D (CircleShape2D)
└── LifetimeTimer (Timer, OneShot, autostart with lifetime duration)
```

PomeSeed.cs uses `Init(launchVel, waveHits, lifetime, isBurning, burnDebuffDur, ownerCol)` for per-wave shared hit sets. Port directly — the `Physics2D.IgnoreCollision` call becomes setting collision mask to exclude the owner's layer temporarily.

#### Task 4.6: PomeSeed Burning Variant
- Duplicate PomeSeed.tscn as PomeSeedBurning.tscn
- Add orange/red tint to sprite
- On contact, call `target.ApplyBurning(burnDebuffDur)` — same logic from Unity

### Week 2 Day 5: PlayerAgent + HUD + Integration Test

#### Task 4.7: Port PlayerAgent.cs

```
Source: Assets/Scripts/Characters/BaseCharacter/PlayerAgent.cs (320 lines)
Target: res://Characters/Base/PlayerAgent.cs
```

Key changes:
- `Instantiate(characterPrefab, position, rotation, parent)` → `characterPrefabScene.Instantiate<Pomegraknight>()` then `AddChild()`
- `Destroy(oldObj)` → `oldObj.QueueFree()`
- `CharacterController playerHUD` / `emotionTracker` → direct node references via `GetNode<>()`
- Character roster: store `PackedScene[]` instead of `GameObject[]`
- `FindObjectOfType<CameraController>()` → `GetTree().GetFirstNodeInGroup("CameraController")` or export reference

#### Task 4.8: Port PlayerHUDController.cs (minimal version)

For the prototype, implement only:
- HP bar display
- Ammo/Glow display
- Cooldown indicators for Shift and E
- Lives display

```
Source: Assets/Scripts/UI/PlayerHUDController.cs (263 lines)
Target: res://UI/PlayerHUDController.cs
```

Godot UI approach:
```
PlayerHUDController (CanvasLayer)
├── HPBar (TextureProgressBar)
├── AmmoLabel (Label)
├── ShiftCooldown (TextureProgressBar)
├── ECooldown (TextureProgressBar)
├── LivesLabel (Label)
└── Mugshot (TextureRect)
```

Key API mappings:
- `TextMeshProUGUI.text` → `Label.Text`
- `Image.fillAmount` → `TextureProgressBar.Value` (normalized 0-1)
- `Mathf.Max(0f, cooldown - (Time.time - lastUseTime))` → identical math

#### Task 4.9: Integration Test
1. Wire `P1_Agent` node in MainArena with:
   - `InputRouter` (playerID=1)
   - `ChannelSystem`
   - `PlayerHUDController`
   - `PlayerAgent` with Pomegraknight `PackedScene` in the roster
2. Run the scene
3. Verify: movement (A/D), jump (Space), basic attack (LMB — triggers 3-hit combo), Blush (Shift), Pome Seed Eruption (E)
4. Verify: HP display updates on damage, cooldowns display correctly
5. Verify: character dies at 0 HP, respawns after delay

---

## 5. Phase 3 — Game Systems (Week 3, Days 1-3)

> **Goal:** Match flow, scoring, difficulty scaling, enemies start spawning.

### Week 3 Day 1: GameManager + DifficultyManager

#### Task 5.1: Port GameManager.cs

```
Source: Assets/Scripts/Gameplay/GameManager.cs (313 lines)
Target: res://Gameplay/GameManager.cs
```

Key changes:
- `SceneManager.LoadScene()` → `GetTree().ChangeSceneToFile("res://Maps/MainArena.tscn")`
- `Application.Quit()` → `GetTree().Quit()`
- `TextMeshProUGUI` references → `Label` node references
- `OnGUI()` debug overlay → `Label` node in a `CanvasLayer` or remove entirely
- `RespawnRoutine` coroutine → async method
- `Destroy(gameObject)` → `QueueFree()`
- Add to `Autoload` singleton: Project Settings → Autoload → `GameManager.cs`

#### Task 5.2: Port DifficultyManager.cs

```
Source: Assets/Scripts/Gameplay/DifficultyManager.cs (162 lines)
Target: res://Gameplay/DifficultyManager.cs
```

- `TMP_Text` → `Label`
- C# events (`System.Action`) — identical, no change needed
- Add to `Autoload`

### Week 3 Day 2: BaseFoe + One Enemy Type

#### Task 5.3: Port BaseFoe.cs

```
Source: Assets/Scripts/Foes/BaseFoe.cs (467 lines)
Target: res://Foes/BaseFoe.cs
```

Use `RigidBody2D` as base node type (foes need physics for knockback + velocity impacts):
- `Rigidbody2D` → `RigidBody2D` (properties map directly: `LinearVelocity`, `AngularVelocity`)
- `Collider2D` → `CollisionShape2D` child
- `SpriteRenderer` → `Sprite2D` child, `spriteRenderer.color` → `_sprite.Modulate`
- `StartCoroutine(FlashOnHit())` → async
- Contact damage: connect `body_entered` signal
- Evolution: `transform.localScale` → `Scale`, same multiplier math
- `Destroy(gameObject)` → `QueueFree()`
- `Random.value` → `GD.Randf()`
- `Random.Range(min, max)` → `GD.RandRange(min, max)`
- `Instantiate(dropPrefab, pos, rot)` → drop scene instantiation

#### Task 5.4: Port CrabFoe as First Enemy

```
Source: Assets/Scripts/Foes/CrabFoe.cs (217 lines)
Target: res://Foes/CrabFoe/CrabFoe.cs
```

CrabFoe is the simplest enemy — ground patrol with edge detection:
- Patrol: move horizontally, flip at walls/edges
- Edge detection: `Physics2D.Raycast` → Godot `RayCast2D` node or `IntersectRay`
- Wall detection: same approach

CrabFoe.tscn:
```
CrabFoe (RigidBody2D)
├── Sprite2D
├── CollisionShape2D
├── WallCheck (RayCast2D, forward direction)
└── EdgeCheck (RayCast2D, downward from edge)
```

#### Task 5.5: Port FoeManager.cs

```
Source: Assets/Scripts/Foes/FoeManager.cs (253 lines)
Target: res://Foes/FoeManager.cs
```

Key changes:
- `InvokeRepeating(nameof(Tick), 1f, 1f)` → `Timer` node with `WaitTime=1`, `OneShot=false`, connect `timeout` signal to `Tick()`
- `Instantiate(foePrefab, point.position, Quaternion.identity)` → scene instantiation
- `Random.Range(0, spawnPoints.Length)` → `GD.RandRange(0, spawnPoints.Length)`
- Evolution queue: identical logic
- Boss subscription: connect to `DifficultyManager.OnBossPhase` C# event

### Week 3 Day 3: WonderPages + Damage Numbers

#### Task 5.6: Port WonderPage.cs + WonderPageCollector.cs

- WonderPage: `CircleCollider2D` trigger → `Area2D` with `body_entered` signal
- Collector: same logic, callback on page touch

#### Task 5.7: Port DamageNumberManager.cs

- Floating text that rises and fades → `Label` + `Tween`
- Object pooling: reuse Label nodes instead of instantiate/destroy

---

## 6. Phase 4 — Remaining Characters (Week 3 Days 4-5 + Week 4 Days 1-3)

> **Goal:** Pixolotl, PumpKing, Cleopastar all playable.

### Week 3 Day 4-5: Pixolotl

```
Source: Assets/Scripts/Characters/Pixolotl/Pixolotl.cs (690 lines)
```

Pixolotl is a ranged mage. Key systems to port:
- **Burst fire 6 projectiles via animation events** → timer-based or `AnimationPlayer` call_method tracks
- **PixelBubble** projectile (376 lines — bezier curve flight, wall bounce 4 hits) → `RigidBody2D` + manual bezier math
- **SOBRECARGA self-buff** → uses the buff system from CharacterController
- **RANIBOBER time rewind** → store position/HP history in a `Queue<(Vector2 pos, float hp, float time)>`
- **PapalPicadoGhost** (182 lines) → after-image VFX, simpler in Godot with `Sprite2D` copies + `Tween` fade

### Week 4 Day 1-2: PumpKing

```
Source: Assets/Scripts/Characters/PumpKing/PumpKing.cs (673 lines)
Source: Assets/Scripts/Characters/PumpKing/PumpKingHead.cs (372 lines)
Source: Assets/Scripts/Projectiles/RollingBall.cs (394 lines)
```

PumpKing is the most physics-heavy character — three states: Normal, RollingBall, Ghost.
- **RollingBall** → `RigidBody2D` with physics bounce, manual/auto detonate
- **Ghost Form** → free-flight mode (`isFreeFlying` in base), invulnerable (`isInvincible`)
- **Head detach** → separate `RigidBody2D` scene that spawns on BA, bounces, can be detonated
- **Shield stacking** → uses shield system from CharacterController base

### Week 4 Day 3: Cleopastar

```
Source: Assets/Scripts/Characters/Cleopastar/Cleopastar.cs (537 lines)
Source: Assets/Scripts/Projectiles/CleopastarStar.cs (849 lines)
```

Cleopastar has the most complex projectile management:
- **Star system**: List of active stars, each with states (Stationary/Gliding, Normal/Blackhole, Frozen)
- **CleopastarStar** is the largest single file (849 lines) — a state machine for each star projectile
  - Stationary: hovers at position, 0.5 m/s fall
  - Gliding: accelerates 12→30 m/s over 1.5s
  - Blackhole: pull radius 6m, 15 m/s force, contact triggers 4-tick damage sequence
  - Star platform: Cleo landing consumes a stationary Normal star in an immediate explosion
- **Glow generation**: shared ammo system (5-capacity, 1 Glow/1.5 s reload)
- **Shift volley reduction**: repeated Normal Gliding impacts on one target deal -35%, then -70%
- **Ult**: deferred

---

## 7. Phase 5 — Remaining Enemies & Hazards (Week 4 Days 4-5)

> **Goal:** All enemy types and environmental hazards working.

### Week 4 Day 4: Air Enemies + Tsunami

#### Task 7.1: Port DriftFoe + SeagullFoe
- DriftFoe: zero-gravity aerial base, random horizontal drift with vertical bob
- SeagullFoe: dive-bombs players, fires slowing poop projectiles
- `PoopProjectile` (62 lines) — straight line, slow debuff on hit

#### Task 7.2: Port Tsunami System
Four files to port: `Tsunami.cs`, `TsunamiManager.cs`, `TsunamiTrigger.cs`, `TsunamiZone.cs`
- Full-screen environmental hazard
- Cinematics: sky darkening → `CanvasModulate` with color tween
- Camera shake: already in CameraController as `ShakeOffset`
- Percentage-based damage: identical math
- Tsunami wave visual: `Sprite2D` sliding across screen

### Week 4 Day 5: Fire Hazard + Fatal Hazard + Jump-Through Platforms

#### Task 7.3: Port Hazards
- `FireHazard.cs` (91 lines) — `Area2D` with `body_entered` → apply burning
- `FatalHazard.cs` (91 lines) — `Area2D` with `body_entered` → instant kill
- `JumpThroughPlatform.cs` (88 lines) — Godot has built-in `one_way_collision` on `CollisionShape2D`
- `SeedTrap.cs` (39 lines) — simple triggered trap

---

## 8. Phase 6 — UI & Polish (Week 5 Days 1-5)

> **Goal:** Complete HUD, announcements, emotion bubbles, boss UI, split-screen.

### Week 5 Day 1: Full HUD

#### Task 8.1: Complete PlayerHUDController
- HP bar with smooth drain animation (`Tween`)
- Ammo display with color coding (low ammo = red)
- Skill cooldown radial indicators (`TextureProgressBar` with radial fill mode)
- Lives counter with heart icons
- Character mugshot on transformation
- Level indicator

#### Task 8.2: Port AnnouncementHUD
- "Player X Wins!" / "Draw!" / "Boss Phase!" text
- Slide-in animation with `Tween`
- Auto-hide after delay

### Week 5 Day 2: Emotion Bubble + Status VFX

#### Task 8.3: Port EmotionTracker + CharacterBubble
- World-space bubble above character
- Emoji/sprites for different states (hit, kill, collect, death)
- Fade in/out with `Tween`

#### Task 8.4: Port CharacterStatusVFX
- Color cycle on invincibility → `Tween` looping on `Sprite2D.Modulate`
- Shield visual → overlay sprite
- Burning visual → `GpuParticles2D` flame effect
- Icy visual → blue tint + particles

### Week 5 Day 3-4: Split-Screen + Boss HUD

#### Task 8.5: Split-Screen Support
- Use two `SubViewportContainer` nodes, each with its own `Camera2D`
- Each camera follows one player
- UI layers per viewport

#### Task 8.6: Port BossHUDController
- Boss HP bar at top of screen
- Boss name + phase indicator
- Appears/disappears with `Tween`

### Week 5 Day 5: Final Testing Pass
- Run full match: P1 vs P2, all characters playable
- Verify all skills work, cooldowns display
- Verify enemies spawn, evolve, drop items
- Verify WonderPages collect, trigger transformations
- Verify Tsunami triggers correctly
- Verify death/respawn cycle
- Verify difficulty level progression
- Performance check: maintain 60 FPS with all systems active
- Compare game feel to Unity version, tune physics values

---

## 9. Quick-Reference: API Migration Cheat Sheet

### Time
| Unity | Godot |
|---|---|
| `Time.time` | `Time.GetTicksMsec() / 1000.0f` |
| `Time.deltaTime` | `(float)delta` parameter |
| `Time.fixedDeltaTime` | `(float)delta` in `_PhysicsProcess` |
| `WaitForSeconds(n)` | `await ToSignal(GetTree().CreateTimer(n), "timeout")` |

### Math
| Unity | Godot |
|---|---|
| `Mathf.Atan2(y, x)` | `Mathf.Atan2(y, x)` |
| `Mathf.Rad2Deg` | `Mathf.RadToDeg(radians)` |
| `Mathf.Deg2Rad` | `Mathf.DegToRad(degrees)` |
| `Mathf.DeltaAngle(a, b)` | `Mathf.AngleDifference(a, b)` |
| `Mathf.Clamp(v, min, max)` | `Mathf.Clamp(v, min, max)` |
| `Mathf.Max(a, b)` | `Mathf.Max(a, b)` |
| `Mathf.Min(a, b)` | `Mathf.Min(a, b)` |
| `Mathf.Approximately(a, b)` | `Mathf.IsEqualApprox(a, b)` |
| `Vector2.right` | `Vector2.Right` |
| `Vector3.zero` | `Vector2.Zero` (or `Vector3.Zero`) |
| `Vector3.Lerp(a, b, t)` | `a.Lerp(b, t)` |
| `Random.value` | `GD.Randf()` |
| `Random.Range(min, max)` | `GD.RandRange(min, max)` |

### Physics
| Unity | Godot |
|---|---|
| `rb.linearVelocity` | `LinearVelocity` (RigidBody2D) or `Velocity` (CharacterBody2D) |
| `rb.AddForce(f, ForceMode2D.Impulse)` | `ApplyCentralImpulse(f)` (RigidBody2D) |
| `rb.gravityScale = 0` | `GravityScale = 0` |
| `Physics2D.OverlapCircle(pos, r)` | `PhysicsShapeQueryParameters2D` + `DirectSpaceState.IntersectShape()` |
| `Physics2D.OverlapBox(pos, size, angle)` | Same as above with `RectangleShape2D` |
| `Physics2D.IgnoreCollision(a, b)` | `CollisionObject2D.AddCollisionExceptionWith()` |
| `OnTriggerEnter2D(Collider2D)` | Connect `body_entered` signal or override `_OnBodyEntered()` |
| `OnCollisionEnter2D(Collision2D)` | Connect `body_entered` signal on `RigidBody2D` |

### Nodes & Objects
| Unity | Godot |
|---|---|
| `Instantiate(prefab, pos, rot)` | `scene.Instantiate<T>()` + configure + `AddChild()` |
| `Destroy(gameObject)` | `QueueFree()` |
| `Destroy(gameObject, delay)` | `Timer` node + `QueueFree()` on timeout |
| `GetComponent<T>()` | `GetNode<T>("path")` |
| `FindObjectsByType<T>()` | `GetTree().GetNodesInGroup("groupName")` |
| `gameObject.SetActive(false)` | `Hide()` or `ProcessMode = ProcessModeEnum.Disabled` |
| `transform.position` | `GlobalPosition` |
| `transform.localPosition` | `Position` |
| `transform.localScale` | `Scale` |
| `transform.parent` | `GetParent()` or `GetParent<T>()` |

### Rendering
| Unity | Godot |
|---|---|
| `SpriteRenderer.sprite` | `Sprite2D.Texture` |
| `SpriteRenderer.color` | `Sprite2D.Modulate` |
| `SpriteRenderer.flipX` | `Sprite2D.FlipH` |
| `Camera.main` | `GetViewport().GetCamera2D()` |
| `Camera.orthographicSize` | `Camera2D.Zoom` (inverted) |
| `Screen.width` | `DisplayServer.WindowGetSize().X` |
| `ParticleSystem` | `GpuParticles2D` or `CpuParticles2D` |

### Input
| Unity | Godot |
|---|---|
| `Input.GetKey(KeyCode.A)` | `Input.IsKeyPressed(Key.A)` |
| `Input.GetKeyDown(KeyCode.Space)` | `Input.IsActionJustPressed("jump")` |
| `Input.GetMouseButton(0)` | `Input.IsActionPressed("basic_attack")` |
| `Input.GetMouseButtonDown(0)` | `Input.IsActionJustPressed("basic_attack")` |
| `Input.mousePosition` | `GetViewport().GetMousePosition()` |
| `Gamepad.all` | `Input.GetConnectedJoypads()` |
| `gp.leftStick.ReadValue()` | `Input.GetJoyAxis(id, JoyAxis.LeftX/Y)` |

### UI
| Unity | Godot |
|---|---|
| `TextMeshProUGUI` | `Label` or `RichTextLabel` |
| `TMP_Text.text` | `Label.Text` |
| `Image.fillAmount` | `TextureProgressBar.Value` (0-100) |
| `Canvas` | `CanvasLayer` |
| `RectTransform` | `Control` nodes (built-in anchors/margins) |

### Debug / Misc
| Unity | Godot |
|---|---|
| `Debug.Log(msg)` | `GD.Print(msg)` |
| `Debug.LogWarning(msg)` | `GD.PushWarning(msg)` |
| `Debug.LogError(msg)` | `GD.PrintErr(msg)` or `GD.PushError(msg)` |
| `OnDrawGizmos()` | `_Draw()` (or `[Tool]` + `_Process`) |
| `#if UNITY_EDITOR` | `#if TOOLS` |
| `[ContextMenu("Name")]` | Add to `_GetConfigurationWarnings()` or use `[Tool]` |
| `Singleton` pattern | `Autoload` in Project Settings |

---

## 10. Testing Checklist Per Phase

### Phase 1 (Map + Camera + Input)
- [ ] Camera follows player smoothly
- [ ] Camera clamps to map bounds
- [ ] Camera shake works
- [ ] Keyboard input: WASD movement, mouse aim
- [ ] Gamepad input: left stick move, right stick aim
- [ ] All skill buttons register correctly
- [ ] Aim direction points from character to cursor

### Phase 2 (Pomegraknight)
- [ ] Character spawns at spawn point
- [ ] A/D moves left/right, sprite flips
- [ ] Space jumps, double-jump works
- [ ] Coyote time and jump buffering feel right
- [ ] Gravity matches Unity feel
- [ ] BA: 3-stage combo triggers, damage applied
- [ ] BA: movement penalty active during swings
- [ ] Combo resets after full reload
- [ ] Shift: Blush applies burning, fire VFX appears
- [ ] Shift: burning passive boosts combo damage 1.5x
- [ ] Shift: Fire Tornado triggers on next BA within 3s
- [ ] Fire Tornado: AoE damage ticks, push force works
- [ ] Fire Tornado: 40% damage reduction active
- [ ] E: 3 waves of seeds launch in cone
- [ ] Seeds: gravity-affected, per-wave hit tracking
- [ ] Burning seeds: apply burning debuff on contact
- [ ] HP bar updates on damage taken
- [ ] Character dies at 0 HP, respawns after delay
- [ ] Cooldown indicators for Shift and E
- [ ] Ammo bar shows combo stages

### Phase 3 (Game Systems)
- [ ] Match timer counts down
- [ ] Score increments on kills and page collects
- [ ] WonderPage spawns, collects, respawns
- [ ] Difficulty score accumulates over time
- [ ] Difficulty level increases at thresholds
- [ ] FoeManager spawns enemies on tick
- [ ] CrabFoe patrols, detects edges, flips
- [ ] Enemies take damage, die, drop items
- [ ] Evolution: enemies grow after survival time
- [ ] Damage numbers float and fade

### Phase 4 (All Characters)
- [ ] Pixolotl: burst fire, SOBRECARGA, RANIBOBER
- [ ] PumpKing: rolling ball, ghost form, head detach
- [ ] Cleopastar: star placement, gliding, blackhole
- [ ] All characters can damage each other
- [ ] Character transformation on WonderPage collect

### Phase 5 (Enemies & Hazards)
- [ ] DriftFoe: aerial drift pattern
- [ ] SeagullFoe: dive-bomb attack, slow projectiles
- [ ] Tsunami: full sequence (sky darken, wave, damage, shake)
- [ ] Fire hazard: applies burning on contact
- [ ] Fatal hazard: instant kill on contact
- [ ] Jump-through platforms work

### Phase 6 (UI & Polish)
- [ ] HUD: HP, ammo, cooldowns, lives all display
- [ ] Announcements: match end, boss phase, eliminations
- [ ] Emotion bubbles: show above characters on events
- [ ] Status VFX: shield glow, burn particles, icy tint
- [ ] Split-screen: both players visible
- [ ] Boss HUD: appears/disappears correctly
- [ ] 60 FPS stable with all systems active

---

## 11. Known Gotchas & Tips

### 11.1 Physics Feel
- **Godot's `CharacterBody2D` feels different from Unity's manual `Rigidbody2D` gravity.** Expect to tune `_gravity`, `_jumpVelocity`, `moveSpeed`, and `airControl` by ~10-20% to match the original feel.
- **Godot's `IsOnFloor()` is stricter than Unity's `OverlapCircle` ground check.** If the character "sticks" to walls or misses ground detection, increase the `FloorMaxAngle` (default 45°) or adjust the collision shape.
- **`MoveAndSlide()` handles collisions automatically.** You don't need to manually resolve overlaps like in Unity.

### 11.2 Animation Timing
- **Unity Animation Events run at specific frames.** Godot's `AnimationPlayer` has `call_method` tracks that fire at specific timestamps. If the combat feel depends on frame-precise hit detection, use timer-based callbacks (as in §4.2.12) instead — they're more reliable and easier to tune.
- **Godot's `AnimationPlayer.Play()` resets the track if called again.** For combo strings, make sure each slash animation is a separate animation (slash_1, slash_2, slash_3) rather than trying to seek within one long clip.

### 11.3 Coroutine Pitfalls
- **`async void` methods can't be cancelled** unless you use `CancellationTokenSource`. For skills that can be interrupted (Fire Tornado on death), always check `isDead` in the async loop.
- **`await ToSignal(GetTree().CreateTimer(n), "timeout")` creates a new timer each time.** For high-frequency patterns like the Fire Tornado tick loop, prefer a single reuseable `Timer` node.

### 11.4 Object References
- **Unity's `GetComponent<T>()` searches the current GameObject.** Godot's `GetNode<T>("path")` requires the node path. Use `@onready var _sprite = GetNode<Sprite2D>("Sprite2D")` at the top of the class for compile-time-like safety.
- **Unity Inspector references survive prefab instantiation.** Godot `[Export]` references in `PackedScene` work the same way — assign them in the editor and they persist.

### 11.5 Singleton / Manager Access
- **Unity: `GameManager.Instance`** → Godot: Add `GameManager.cs` to Autoload, then access via `GetNode<GameManager>("/root/GameManager")` or use a static `Instance` property set in `_Ready()`.
- **Autoloads are initialized before any scene's `_Ready()`.** They're the right place for singletons.

### 11.6 Object Pooling
- **Unity: `Instantiate`/`Destroy` are expensive.** Godot has the same issue. For projectiles, damage numbers, and VFX: create a pool of inactive nodes, activate/deactivate instead of instantiate/free.
- **Simpler approach for the prototype:** just instantiate/free. Optimize with pooling only if you see frame drops.

---

## 12. When to Involve a Human

Stop and ask the user:
- When the Unity scene layout isn't clear from code alone (open `MainArena.unity` in Unity to see platform positions, spawn point coordinates, camera setup)
- When sprite sheet slicing is needed (Godot's `SpriteFrames` vs Unity's Sprite Editor)
- When animation clip durations/timing need to be extracted from Unity (read `.anim` files or inspect Animation window)
- When game feel differs significantly after porting (movement, combat timing, knockback distances)
- When deciding on Godot 4.4 vs 4.5 features (the guide targets 4.4 LTS)

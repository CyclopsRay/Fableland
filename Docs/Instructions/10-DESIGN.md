# Fableland — Game Design Department

How to write, extend, and maintain the design of Fableland. The design department's
deliverable is **a GDD section with numbers in tables and a decision recorded** — not
an idea in your head, not a comment in code.

---

## 1. GDD ownership map

Each document owns a layer. Cross-references are explicit (`NODES.gdd §5.3`-style).
When two docs could own a rule, the table below decides; the *other* doc links to it.

| Document | Owns |
|----------|------|
| `MapGDD.md` | Map generation: zones, worlds, levels, edges, function-node placement, seed, day/stamina/VOID schedule, mist, zone-6 topology, atlas render |
| `NODES.gdd` | What happens *inside* nodes: mission types & scaling, shelters (Blessed/Mundane, traders, jousting, Rest/Sharpen/Wish), ?-events, day cycle & **day-end resolution order**, team/party rules, run goal & fail states, run-performance tracking |
| `Gameplay.gdd` | Player-facing gameplay systems that span nodes: ATK/DEF/Vamp stat model, switch-protagonist & landing effects, jousting, trading, planting UI, universal time limits — links to `NODES.gdd` §4 for per-node semantics (which owns the mission/scaling tables) |
| `FOES.gdd` | Foe types, base stats, 8-level day scaling, sight/aggro FSM, evolution, skills, loot hooks, arena cap, bosses (TBD) |
| `ITEMS.gdd` | Wonder items: slots, acquisition, anatomy, both cooldown axes, tags (`Perishable/Convertible/Plantable/Possession/Eternal`), lifecycle archetypes, catalog |
| `<Character>.gdd` (one per playable) | Full kit: overview/HP, passive, BA/Shift/E/Ult, movement, leveling, design + implementation notes, image prompts |
| Plantation GDD (**to be written**) | Plantation slots, watering, growth, harvest, inheritance rolls |
| Meta-progression GDD (**to be written**) | What persists between runs (unlocks, account) |

**Rules of the format** (all existing docs follow these — keep them):
- Every number lives in a **table**, because tables become data tables (`30-DATA-AND-BALANCE.md`).
- Every doc ends with a **Decisions log**: what was decided, and *why*. When you change
  your mind later, you'll need the why.
- Unknowns are marked **TBD** loudly, never papered over. TBDs feed the registry (§6).
- Implementation notes live in the GDD (see Cleopastar's Blackhole-pull note) so the
  engineering hat doesn't rediscover constraints.
- Units are meters/seconds/days — never pixels (engineering converts via `Units.cs`).

## 2. Checklist — adding a playable character

Copy the structure of `Cleopastar.gdd` (the most complete). A character GDD is done when
it has:

1. **Overview** — race, role, realm/home world (must map to a world in `MapGDD.md` §2 or
   add one), HP by level, personality (drives animation + merch later).
2. **Visual design** — palette table, silhouette, accessories (or explicit TODO).
3. **Resource system** if non-standard (Glows, pump stacks, frames) — define generation
   rate, cap, loss conditions.
4. **Passive + BA + Shift + E (+ Ult)** — each with CD, numbers table, and a *design
   note* saying what tension it creates.
5. **Skill summary table** — one row per skill (this is the balance reviewer's view).
6. **Movement table** — speed, jumps, air control, any gravity modifiers.
7. **Leveling** — flat base HP (in-run character leveling was removed in v0.3.7; permanent
   growth is the additive HP/ATK/DEF pools, not a per-character level ladder).
8. **Design notes** — the character's win condition, weakness, and intended counterplay.
9. **Implementation notes** — which base-class systems it uses (ammo/magazine, shield,
   continuous-force sources…), any base-class changes needed (keep them minimal + non-breaking).
10. **Image generation prompts** — sprite sheet (frame list!) + poster.

Design constraints to respect:
- Kits build on the shared `CharacterController` systems (ammo/magazine, shield pool,
  statuses, externalVel). If a kit needs a new base system, that's a *base* feature
  first — spec it standalone (like the Blackhole-pull note in Cleopastar's GDD).
- Every character needs an answer to swarms and an exploitable weakness (Pomegraknight:
  Fire Tornado / freeze-slow; Pixolotl: bubble fan / fragility).
- HP band so far: 150 (fragile controller) — 300 (tank). Justify placement in notes.

## 3. Checklist — adding a foe

Extend `FOES.gdd`; one section per foe (§3/§4 are the templates):
1. Base stats table (HP/ATK/SPD, ranges, cooldowns, hit radius, damping).
2. Passive — the identity mechanic (Soft Shell's 60° cone, Flight's 1/3-accel momentum).
   Every foe passive should teach the player a *positional* lesson.
3. Patrol + Aggro behaviour, Sight shape + improved variant + check interval.
4. Skill 1 (unlocks foe-level 4) and Skill 2 (level 6), each with CD and numbers.
5. Confirm it obeys the shared systems: 8-level multiplier table (§2), evolution (§5),
   sight/aggro FSM (§6), loot hook (§7), arena cap (§11).
6. Add a Decisions-log entry for anything surprising.

## 4. Checklist — adding a wonder item

Extend `ITEMS.gdd` §7 (catalog) and §6.3 (tag matrix):
1. Fill the standard field table: Domain, Lifecycle, Passive, Skill, CD, Tags.
2. Pick the **lifecycle archetype** (§5). If none fits, you're inventing a new archetype
   — add it to §5 with its own rules first.
3. Choose the **cooldown axis** (day-based / second-based / single-use) deliberately —
   day-based CDs interact with day-end resolution (`NODES.gdd` §7.4).
4. Validate tag legality: `Eternal` × `Convertible` is forbidden; `Possession` means the
   passive works from the backpack; `Perishable` ticks *regardless of location*.
5. If `Plantable`: harvest time, fruit list, inheritance range (60–140% pattern).
6. Update the tag matrix row and, if the item touches other systems (traders, jousting,
   plantation), the §8 integration table.

## 5. Balance philosophy

- **Two-axis difficulty is sacred.** Node level sets objectives; day sets foe stats.
  Never let a mechanic collapse the axes (e.g., an item that reduces foe level would —
  prefer effects that manipulate *days* or *objectives* instead).
- **Budget math, not vibes.** Balance by explicit kill-budgets like Cleopastar's:
  "3 max-glide stars = 195 dmg, survives 200 HP; 4 = lethal." When adding damage
  sources, write the budget line in the GDD. Targets to design against:
  - Normal combat node: 1.5–2 min; boss: 2–3 min; full run ≤ ~90 min (`MapGDD.md` §8a).
  - Foe TTK should stay reasonable at ×5 HP (level 8) — that's why scaling is
    base-relative, not cumulative (FOES.gdd Decisions log).
- **Punish stalling softly, then hard.** Devour schedule → foe levels → zone-6 time
  scaling (anti-stagnation). New mechanics should push forward-motion, not camping.
- **Player survives non-boss failure.** Only combat death and boss-timer expiry kill
  (`NODES.gdd` §2). Don't add new instant-death conditions casually.
- **Stackable buffs use the single-shared-timer model:** one timer per buff instance;
  any application resets it; expiry removes one stack. Universal.
- **50%-uptime power windows** (Blush 5 s/10 s, SOBRECARGA 3.2 s/12.8 s ≈ 25%) — burst
  windows define character rhythm; pick uptime deliberately and write it down.

## 6. The TBD registry

Track every open design question here; resolve them via the change workflow. Current
open TBDs harvested from the GDDs (keep this list pruned as they close):

| # | TBD | Home |
|---|-----|------|
| 1 | Dark leader (final boss) name + full design | MapGDD §10, FOES §8 |
| 2 | Boss foes. *v0.10.11: the system is built — a boss is its protagonist's kit on the Foe side with its own table row, a rule-based brain and an arena made for it; BOSS PumpKing is the first (FOES §8).* Still open: **what makes a boss differ from its protagonist** (larger magazine, quicker cooldowns, a held item, … — every `BossTable` value is a first-pass placeholder); phases; the other realms' bosses; the LV6 final boss; whether a boss can be executed (Cleopastar's Blackhole under 10% HP currently can); whether BOSS PumpKing should let a closing head arrive instead of setting it off beside the player (a touch costs a standing player 48, a detonation 30 — FOES §8.3). *v0.11.0: PumpKing stands in as **every** realm's boss until each has its own — "the other realms' bosses" is now seven rows to add to `BossTable`* | FOES §8, NODES §4.5 |
| 3 | Question-mark node event content + probabilities | NODES §6.1 |
| 4 | Shelter **Wish** action | NODES §5.5 |
| 5 | Plantation GDD (slots, watering, growth) | ITEMS §4.3, NODES §5.2 |
| 6 | Meta-progression / persistence between runs | NODES §2.4 |
| 7 | LV5/LV6 content gated by run-performance | NODES §8 |
| 8 | Pixolotl Feather day-rewind undo scope | ITEMS §7.8 |
| 9 | Slaughter wave composition per world | NODES §4.3 |
| 10 | Zone-6 time-scaling curve (anti-stagnation) | MapGDD §8a |
| 11 | VOID trader goods list & prices | NODES §5.3, ITEMS §1.2 |
| 12 | Wonder-item row in FOES loot table; foes holding items | ITEMS §1.2/§8 |
| 13 | VOID PHANTOM contact boss fight | ITEMS §7.2 |
| 14 | Mission reward delivery mechanism | NODES §1.2 |
| 15 | Protect-core healability by ally-heal abilities | NODES §4.4 |
| 16 | Per-shelter plantation/jousting chances | NODES §5.2/§5.4 |
| 17 | Surprise shelter functions (VOID TRADER surprise, treasure box, teleport) | MapGDD §6 |
| 18 | Pixolotl & PumpKing & Pangda visual design + leveling curves | their GDDs |
| 19 | Sifu Pangda Ult model/CD and realm (Gui Lin not yet in world pool) | Sifu Pangda.gdd |
| 20 | ~~PumpKing realm "Kingdom of Horror" not in world pool.~~ **Resolved v0.11.0:** the Kingdom of Horror is in the pool (danger red on the map), with a map per altitude band and PumpKing's castle as its boss arena. Still open under this number: whether PumpKing becomes a *starter* with it as his home | PumpKing.gdd, MapGDD §2/§2.2, RealmStyleBible §7A |
| 21 | Ult resource/charge model + per-character Ult CDs (Pangda Ult CD TBD; foe Ult-charge loot orb) | Sifu Pangda.gdd, FOES §7 |
| 22 | Protagonist Landing Effects — the per-character switch-in effect (all 5 GDDs stub it TODO) | character GDDs, NODES §3.3 |
| 23 | Per-mission-type victory loot beyond the demo (Protect/Destroy/Slaughter reward tables) | NODES §4 |
| 24 | Pixolotl home world / realm. *v0.10.9: the owner named it **The Forgotten** (a Teotihuacan-style city everyone takes for a ruin, in fact highly developed; swamp at sea level, pyramids above); its combat realm — the Forgotten City, tile set `tf` — is built. **v0.11.0: The Forgotten is in the pool** (canal jade on the map), with a map per altitude band and a boss arena.* Still open: whether Pixolotl becomes a starter there, and whether "Not Found" stays as her origin story | MapGDD §2/§2.2, Pixolotl.gdd, RealmStyleBible §7B |
| 25 | Destroyable trees (and destructible scenery in general) — asked for in the Kingdom of Horror brief, not built; the realm's trees are decoration | RealmStyleBible §7A, MapCreation.gdd §2.2 |
| 26 | Cloud Court — **provisionally decided (v0.11.3, on the standing instruction to decide now and discuss rework later): the sky district of the Palace of LOOING.** A Palace node on high ground may be dealt its map beside that world's other high-ground maps (one in three today); no ninth world, no seed or save moved. To confirm or rework: does a sky district belong in an undersea world; how often it should turn up; a boss arena of its own. Before this it was a finished set and map no run could open (world code `CC`). Found on the way: **Crownwave Colosseum (`seashore_5`) is dealt by no outer node** — tagged LV4 without the `boss` goal, and VanillaKindom's LV4 has had its own boss arena since v0.11.0; today only zone 6 reaches it. Retag it LV3, make it a second VK boss arena, or leave it to the VOID. *v0.11.3: the second-boss-arena option was tried when the work was merged — `boss` added to its goals — and reverted. The content, boss and feedback checks passed with it; the run check failed 1 of 1337, `a boss arena needs two perches (Level Goal markers); it has 1`. So that option needs a second perch authored on the map (within about 20 m of the first) and is not a tag change; the map ships untouched and the question stays open* | RealmStyleBible §7C, §10.4, MapGDD §2.2 |
| 27 | Swim balance — every number is measured but untested in a fight. Sea: swim 5.5 m/s, drop 3 m/s, so up 2.5 / down 8.5 (48 m of sea: 19 s up, 6 s down; holding a height = holding Up over half the time); a surface jump clears 2.1 m (swamp 1.0 m); bubbles (7 m/s) can be swum down against at 1.5 m/s. Foliage / cloud: drop 1 m/s (`GravityIndex` 0.1, one scene default, not tunable per kind) — a fighter practically hangs. **Slower kits** (the numbers above are a 10 m/s fighter's; swimming speed is a share of top speed, the drop is absolute): PumpKing (8 m/s) rises 1.4 m/s in the sea — measured — and 0.2 through kelp there; Sifu Pangda (6.5 m/s) 0.6 m/s, and sinks in that kelp; a PumpKing slowed to 4.8 m/s by three pumps sinks in the sea and the swamp while holding Up (from the model). A jump stroke is about 2.9 m for all of them, so nobody is trapped — is holding Up meant to fail for the slow? *v0.11.1 (the swimming work, written as v0.10.10) replaced v0.10.9's dive with free swimming, on the owner's rule (own speed + drop) and for every soft volume; the jump stroke inside a volume stays* | RealmStyleBible §10, Gameplay.gdd A.2, MapCreation.gdd §2.2a |
| 28 | Boss presentation — BOSS PumpKing wears a placeholder red mark because his black coat vanishes against his castle's stone when his head is off; his boss art, entrance and defeat are undesigned (he fades) | FOES §8, RealmStyleBible §7A |
| 29 | The prototype roster owns every protagonist from the start, so recruiting a beaten boss changes nothing yet. When that policy ends, recruitable protagonists leave the starting roster (the reward path is in place) | NODES §4.5, FOES §8.6 |
| 30 | Slopes for other realms, and other grades — only the Dark Wood has a slope block, at 1:2 | MapCreation.gdd §2.2b |
| 31 | **Does a boss get faster with the day, and does it evolve?** Every foe does both (FOES §2 SPD multiplier; §5: not eliminated in 25 s → +1 level and a 30% heal), and the old crab stand-in did. BOSS PumpKing does neither — HP and damage follow the day, checked on every band. Applying evolution would give every boss a level and up to +540 HP 25 s into a fight whose timer is permadeath; applying speed needs boss legs that hold a perch at up to 2× | FOES §8.2 |
| 32 | The band maps and boss arenas of v0.11.0 are **plain test maps** (three shapes and one arena per realm, `build_band_maps.py`): each realm needs designed low-ground / sea-level / high-ground maps and a boss arena that takes its own boss's side, as PumpKing's castle does. Whether each realm's showcase map should then be narrowed from `*` to the band it is built for is part of that | MapGDD §2.2, RealmStyleBible §8, FOES §8.5 |
| 33 | Zone 6 has no combat maps of its own — **provisionally decided (v0.11.3, same instruction): it wears the worlds it has swallowed.** Each LV5 node is dealt a map of one of the run's five worlds for its mission, the LV6 core one of their boss arenas, chosen per node from the seed and drawn VOID-touched (drained and darkened; fighters, foes and hazards untouched). Still open: the VOID's own kit, medium and maps; whether the look should be more than a drained picture; whether an LV5 node should wear the realm it sits beside rather than a drawn one; the final boss, still the stand-in crab on a perch built for PumpKing | MapGDD §7, MapCreation.gdd §9, RealmStyleBible Decisions log |
| 34 | Foes in soft volumes — fighters swim, foes stay passive (they fall through foliage, slowed, and sink in water). Should foes float in foliage as fighters do, and should any foe swim? **And the boss:** a BOSS is a fighter, so since v0.11.1 it swims — from its own pad, never the player's keys — and floats in foliage; BOSS PumpKing's brain has no rule that holds Up or Down, so in a volume he only walks, jumps and floats. What should a boss's legs do in water? | Gameplay.gdd A.2, MapCreation.gdd §2.2a, FOES §8.5 |
| 35 | Jump cooldown inside a soft volume — jumping there stays (owner, in the swimming work) so a player chooses between holding Up and jumping, limited by the universal 0.3 s `JumpCooldown`. With it tapping Jump is the faster way up: 10 m/s against 2.5 m/s in the sea, 19.4 against 8.6 in a bamboo. Calculated: about 0.7 s inside volumes would make the two equal in both. Keep 0.3 s, or give volumes their own cooldown? | Gameplay.gdd A.2, MapCreation.gdd §2.2a |
| 36 | Hit feedback has never been watched — every number in `FeedbackTable` and `FeelTable` (hit weights, stop lengths, shake, kick, flash) was reasoned from the research and proven only headless. It needs a verify-by-eye tuning pass in real fights | Feedback.gdd §3–§5, §10 |
| 37 | Cleopastar's effect and sound style follows **Starland** (flat poster) — **provisional, the owner has not confirmed it**; her GDD says the Palace of LOOING | Feedback.gdd §8, §11 |
| 38 | Sifu Pangda's effect and sound style follows the **Banboo Maze** (freehand ink) — **provisional, the owner has not confirmed it**; his GDD says Gui Lin, which has no world yet (#19) | Feedback.gdd §8, §11 |
| 39 | Should a seagull's dash suppress its contact damage? Its 20-damage contact usually lands a tick before its 50-damage dash, and the player's i-frames then refuse the dash — so the dash seldom deals its own damage (noted in v0.11.2, not changed) | FOES §4 |
| 40 | An options screen and persistence for `FeedbackSettings` — the comfort settings and (v0.11.7) the four volumes exist (static, 0–1) and work; nothing shows, writes or saves them | Feedback.gdd §7, Audio.gdd §14 |
| 41 | Naming a soft volume's medium — a cloud cannot be told from foliage today (the two are tuned alike; `SoftVolume.MediumKind` is there to be named), so their cues cannot differ | Feedback.gdd §9 |
| 42 | `arena.enter` takes the first world a map lists, which is ambiguous for a map serving several worlds — whose style does such an arena wear? | Feedback.gdd §9 |
| 43 | The dark-flash threshold (backdrop lightness ≥ 0.80 — a guess until seen) and a per-world canvas review of which realms flash dark | Feedback.gdd §5.3, §11 |
| 44 | A death delay so a killed foe holds its flash frame — today a foe frees itself the frame it dies (`BaseFoe`) | Feedback.gdd §9 |
| 45 | Should hit-stop also freeze attack timing? Today every clock keeps running during a stop and abilities are still read, so no DPS or cooldown moves; Smash freezes attack timing with the body | Feedback.gdd §4, §11 |
| 46 | The hit-stop budget numbers — 0.18 s, refilling at 0.15 s per second; not from the research and never tuned in a fight | Feedback.gdd §4, §11 |
| 47 | A photosensitivity test on real footage (Harding-style) — the limits are reasoned, not measured | Feedback.gdd §5.4 |
| 48 | **Cleopastar's and Sifu Pangda's effect media are still provisional** (poster / ink — #37, #38); since v0.11.6 they are also two fields in `VfxTable.Looks`, and since v0.11.7 two entries in the audio home-world table (`Scripts/Presentation/Audio/Medium.cs`) — they decide how each looks and sounds, to change with Feedback.gdd §8 | VFX.gdd §4, Audio.gdd §6 |
| 49 | **The VOID style has no art bible, and no arena raises a VOID tag.** A zone-6 fight is fought on a map of one of the run's worlds, drawn VOID-touched (#33): its `arena.enter` still names that world, and reports the backdrop's lightness from before the drain — the picture is drawn at about half of it. So effects are in the borrowed world's medium, and their palette and the hit flash's dark-or-light choice are made for a canvas lighter than the one on screen. Should a VOID-touched arena announce the VOID's medium, and its drained lightness? | VFX.gdd §2, §9, MapGDD §7, Feedback.gdd §5.3 |
| 50 | **Cloud Court's effects are not enamel.** The enamel style is filed under `CC`, a code no arena raises: since v0.11.3 the Cloud Court map is a Palace of LOOING map (#26), so its arena announces `PL` and its dust, water, hazards and ambience are drawn in gongbi — Loong's Nest's medium — over cloisonné art. Should `arena.enter` name the map's realm (`MapRealm.Of`) and not its world? (see also #42) | VFX.gdd §4, RealmStyleBible §7C |
| 51 | The kits' own placeholder telegraphs (`_baTelegraph`'s cone outline and the debug ranges) are still drawn under the new slash — keep them as a debug view, or remove them now that the cone is shown? | VFX.gdd §11, `Docs/Art/Vfx/captures/` 06–08 |
| 52 | Tempo and density at play speed — every medium's `Tempo`, the hit sizes and the ambience scale were judged from stills; nobody has watched the effects move | VFX.gdd §11 |
| 53 | Shake on a Light hit is ≈ 1.5–2 px for 70 ms (`Decay` 3 /s) — invisible in a capture; is the kick enough on its own? | Feedback.gdd §3 |
| 54 | A cue for *how* a status ended (Blush's steam, a thaw's shatter) | VFX.gdd §9 |
| 55 | The map editor's Playtest raises no `arena.enter`, so its effects use the default medium | VFX.gdd §9 |
| 56 | **Listen to it.** Every level, every recipe and all the music are unheard — proven only by measurement and spectrograms; the audition page is `Scenes/Dev/AudioAudition.tscn` | Audio.gdd §13 |
| 57 | Starland allows pure tones only and the Palace is low-passed at 2.2 kHz: are their sounds distinct enough (a jump, a landing and a hit may all read as "a beep" or "a thud in water"), or do the rules need loosening for readability? | Audio.gdd §13 |
| 58 | Audio memory when the other four kits are wired (about +0.4 MB each against the 8 MB budget): hold the party of three instead of every character, or raise the budget | Audio.gdd §11, §14 |
| 59 | A real HP share for the music and the mix — the bus only says "low"; a cue or an `IFeedbackBody` member | Audio.gdd §8, §14 |
| 60 | Recorded or commissioned music: stems as Ogg with BPM metadata, and whether music states then move to `AudioStreamInteractive` | Audio.gdd §14 |
| 61 | The audio work's own calls, for the owner: the menu, map, shelters, events and UI sound in the beach's watercolour (the starting world); `flow`/`mission.time` is raised from the HUD clock; `UiCues` wires every `BaseButton` in the tree | Audio.gdd §2, §14 |

Numbering: #28–#33 were minted by v0.10.11 / v0.11.0 and #34–#35 by the swimming work, which
called them #28 and #29 in its own worktree before the two lines met (v0.11.1). #36–#47 are
the open questions v0.11.2's hit feedback left, registered here in v0.11.3. #48–#55 came with
the VFX director (v0.11.6); #49 and #50 were written before zone 6 was dealt maps and Cloud
Court joined the Palace (v0.11.3) and are reworded here for the tree they landed in. #56–#61
came with the audio director (v0.11.7); its hand-off also asked for an options-screen row and
a provisional-media row, which are #40 and #48 (amended there, not repeated), and for a "Cloud
Court has no world" row, untrue since v0.11.3 and left out (#50 covers what remains).

Note #19/#20/#24: **realm ↔ world-pool consistency** — every playable character must have
a home world in `MapGDD.md` §2 (home world = start zone). The Kingdom of Horror and The
Forgotten joined the pool in v0.11.0 (palette, maps, a boss); Gui Lin (#19) is the one left
without a world. Adding a world is append-only and changes what a *new* seed draws; a run
that already exists keeps the pool it was made from (`MapGDD.md` §2).

## 7. Writing for the other hats

- For **engineering**: numbers in tables, edge cases in prose, base-class impacts in
  Implementation Notes. Say what is *fixed* vs *tunable*.
- For **QA**: every mechanic should imply its test ("spawned crabs cannot spawn" is a
  test case verbatim). Write failure conditions explicitly.
- For **art**: personality + palette + frame lists are the art order form. A kit isn't
  animatable until the sprite-sheet prompt enumerates its frames.
- For **merch/marketing**: personality lines and poster prompts are brand assets.
  Names are IP — check pronunciation/meaning before they calcify (bilingual names like
  破竹 / RANIBOBER are a brand strength; keep the pattern).

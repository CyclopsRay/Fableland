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
| 2 | Boss foes: phases, arenas, kits (incl. LV4 boss-as-protagonist player kits) | FOES §8, NODES §4.5 |
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
| 20 | PumpKing realm "Kingdom of Horror" not in world pool | PumpKing.gdd, MapGDD §2 |
| 21 | Ult resource/charge model + per-character Ult CDs (Pangda Ult CD TBD; foe Ult-charge loot orb) | Sifu Pangda.gdd, FOES §7 |
| 22 | Protagonist Landing Effects — the per-character switch-in effect (all 5 GDDs stub it TODO) | character GDDs, NODES §3.3 |
| 23 | Per-mission-type victory loot beyond the demo (Protect/Destroy/Slaughter reward tables) | NODES §4 |
| 24 | Pixolotl home world / realm — her "Not Found" origin is not in the world pool, so she is a non-starter | MapGDD §2, Pixolotl.gdd |

Note #19/#20/#24: **realm ↔ world-pool consistency** is a design debt — every playable
character must have a home world in `MapGDD.md` §2 (home world = start zone). Either
add Gui Lin / Kingdom of Horror / a Pixolotl realm to the world pool or reassign realms
before those characters become starters.

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

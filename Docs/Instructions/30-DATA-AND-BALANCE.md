# Fableland — Data & Balance Department

The department that guarantees the user's core requirement: **everything — stats,
slots, caps, cooldowns, probabilities, schedules — can change**, at design time in
minutes and, where the design says so, *at runtime inside a run* (items modify party
caps, gravity, speed…). This doc defines where numbers live and how they flow.

---

## 1. The three tiers of a number

Every gameplay value belongs to exactly one tier. Decide the tier when you add it.

| Tier | What | Lives in | Changed by |
|------|------|----------|-----------|
| **Derived** | Physical model (g, jump speed, px/m, stun-per-damage) | `Units.cs` | Changing the three axioms (2 m player, 8 m jump, 1 s) — rare, global |
| **Balance** | Designer-tunable: base stats, CDs, damage, scaling tables, probabilities, schedules | `Scripts/Data/` tables (§2) | Editing the table; hot-tweaked via the tuning overlay (§4) |
| **Runtime-modified** | Anything a buff/item/event can change during a run: MoveSpeed, gravity mult, defense, party cap, held slots, damage mult… | **Base (from a Balance table) + modifier stack** (§3), read through a getter | Gameplay systems adding/removing modifier sources |

The classic failure is tier-confusion: a "constant" party cap of 3 becomes a bug the
day Weird Mushroom outcome (f) grants a 4th held slot. **If a GDD says "subject to
change by mechanisms or items," it is tier-3 from day one.**

## 2. Balance tables — architecture

Now (no toolchain guarantees, keep it compiling statically): plain C# static tables in
`Scripts/Data/`, one file per GDD domain, each entry commented with its GDD section:

```
Scripts/Data/
├── FoeTable.cs        # base stats per foe type + the 8-level mult table   (FOES §2–4)
├── MissionTable.cs    # per-node-level goals: cores/duration/objectives/
│                      #   waves, protect-core HP, mission-type weights     (NODES §4)
├── DayTable.cs        # devour schedule, stamina/day, NPC 5-day window,
│                      #   foe-level-by-day breakpoints                     (Map §8, FOES §2)
├── ItemTable.cs       # ItemDef catalog: tags, CDs, perish, plant data     (ITEMS §7)
├── CharacterTable.cs  # per-character kit numbers (HP curves, CDs, dmg)    (character GDDs)
├── EnvironmentEventDefs.cs # arena event phases, tints, wind, cooldowns    (Gameplay §A.6)
├── MapGenTable.cs     # node-count rolls, edge/bridge/function-node odds,
│                      #   shelter/? split, trader chance + min-3           (Map §4–6)
└── ShelterTable.cs    # Rest/Sharpen values, joust timer                   (NODES §5)
```

Rules:
- **Lookup by enum/id, single call path** (`FoeStats.ForLevel(n)` is the model —
  FOES §9 already specs it). Logic never embeds a table row.
- Tables are **pure data + pure functions** (no Godot types) — this is what makes them
  unit-testable headlessly (40-QA.md §2) and portable to JSON later.
- **Later migration** (when the editor toolchain is routine): move tables to
  `user-editable` JSON or `.tres` resources loaded at boot, keeping the same lookup
  API. Do not block on this — the API boundary is the investment, the storage is swappable.
- `[Export]` properties on scenes are for *placement/wiring* (spawn markers, scene
  refs), not balance. A number exported on 5 scenes is a bug waiting to desync.

## 3. The modifier stack (tier-3 spec)

Generalize the pattern already in `CharacterController` (`DamageDealtMultiplier`
dictionary-by-source, `_defenseBonuses`, `SetDefenseSource/ClearDefenseSource`):

```
EffectiveValue(stat) = clamp( (Base(stat) + Σ additive[source])
                              × Π multiplicative[source],
                              min(stat), max(stat) )
```

- **Keyed by source string** (`"Frozen"`, `"Blush"`, `"item:PomesBravery"`). Setting the
  same source twice replaces; clearing removes. Sources sum/compose — never overwrite a
  raw float another system also writes.
- **Additive vs multiplicative is per-effect, per the GDD.** Frozen −20% speed and
  OnFire +20% are multiplicative; Frozen +30 defense is additive into the defense pool.
- **Never mutate Base at runtime** except designed permanence (Sharpen's +10 ATK /
  +10 DEF, Rest's excess→max-HP percentage points, mushroom (e)/(f)) — model permanence
  as writes to the *run copy* of base in RunState, never to the table. These permanent
  pools are **additive** (max-HP pp: two full-HP Rests → ×1.10 then ×1.20, not 1.1²).
- **Caps/slots are stats too:** `PartyCap`, `HeldSlots(protagonist)`, `MaxJumps`,
  arena foe cap — all readable through the same getter pattern so items can touch them.
- Read through getters at use-time; don't cache effective values across frames unless
  profiled (cache invalidation bugs cost more than the multiply).

## 4. Runtime tuning overlay (build this early — target v0.4.x)

A debug-only panel (toggle key, e.g. F1) that lists Balance-table entries with sliders/
fields, applying live via a `BalanceOverrides` layer that shadows table lookups:

- Overrides persist to `user://balance_overrides.json`; a visible "MODIFIED" badge and
  a reset-all button prevent shipping-with-overrides accidents.
- Combined with the seed field, this is the balance lab: same seed + tweaked number =
  clean A/B. This tool pays for the whole data architecture.
- The existing debug affordances are the precedent and stay: seed field + dice, Rest
  button, Mist toggle, `ShowDebugRanges` telegraph lines.

## 5. Canonical tables to encode (harvest from GDDs at each milestone)

v0.4.0: foe base stats (Crab/Seagull), 8-level mult table (zone 6: LV5 → 7, core → 8),
sight shapes/intervals, evolution (25 s, +1 once, ratio-preserve + 30% heal), skill
numbers (spawn −2 rule, jump CD 5 s, poop 30/3 s, dash 50 dmg / 15 m @ 30 m/s / 10 s CD),
arena cap 6.
v0.5.0: mission weights (60:15:10:10 for LV1/2/3/5; LV4/LV6 structural BOSS) + per-level
scaling grid, protect-core HP, slaughter final-wave +1 (cap 8), victory-loot demo
(Slaughter +10 ATK|+10 DEF choice, Protect/Destroy random item, Collection 10 s grace),
Rest 30%/⅓-excess→max-HP pp, Sharpen +10 ATK | +10 DEF, joust 3 min (both sides ×2 HP),
trader 15%/min-3 (map-wide), NPC 5-day window, devour days (10/20/30/35/40/45), stamina 5.
v0.6.0: the full item catalog incl. mushroom outcome table (18×5+10%), perish/harvest
timers, inheritance ranges (60–140%).
v0.7.0: combat-map metadata (`worlds`, hardship levels, goals, terrain), map foe composition
weights, and optional foe-spawn row limits. Defaults are all worlds/all hardships/claim/
sea-level; the Seashore vertical slice is VK, hardship 1–2, claim, sea-level, 50/50 crab/seagull,
with crab rows ≤4 and seagull rows ≥5.
v0.8.0: outer-world angular placement shares (sum 360°), independent soft-coast petal framing,
distance-ranked level assignment, spatial-tree/loop counts, a 7–9 bridge budget with a two-bridge
per-world-pair cap, 50% bridge-function chance / 50:50 function type, per-world 1–3 Shelter +
1–3 Question-Mark rolls, the required seeded LV3→? / LV3→Shelter boss approaches, and altitude
thresholds. Canonical terrain values are now `low-ground`, `sea-level`, and `high-ground`; old map
JSON values `lowground` and `high` migrate on load.
v0.9.0: one-island coastline/no-islet constraint, a fixed pentagonal VOID target-area ratio,
five variable-width river corridors, equal level-1/level-2 total city counts with A/B sublevels,
realm-local road/loop budgets, altitude contour thresholds, and TwistedReality's one-stamina /
5-day cross-realm map skill. Each use creates a reality bridge and locks its endpoints while the
bridge remains. The old bridge budget, pair cap, and bridge-function rolls are retired.
v0.9.1: reserve an inner VOID buffer and an `XX-S` one-way Shelter between each LV4 and LV5;
per realm roll 3–5 Transportation Hubs and 4–6 Event nodes; reject road/hub spokes that would
cross or overlap a path; city-control field boundaries; and VOID days 10/20 (furthest half/all
LV1), 30/40 (furthest half/all LV2), 43 (all LV3), 45 (all remaining outer cities). Functional
nodes require a strict majority of connected cities to be devoured, and reality bridges break /
release the surviving endpoint when the VOID consumes either endpoint.
Characters: each kit's skill-summary table verbatim.
Arena events: tsunami warning/recovery/cooldown, storm tint, gust schedule, wind pulse,
and subsequent event definitions. Canvas colours stay hex data; Godot colours are made only
at the presentation edge.

## 6. Balance-testing method

- **Budget lines in GDDs** are the spec (Cleopastar's 3-star/4-star lethality line).
  When a number changes, recompute the budget lines it touches and update them.
- **TTK spot-checks per foe level:** base kit DPS vs foe HP at levels 1/4/8; combat
  should stay inside the 1.5–2 min node budget (Map §8a pacing).
- **Run-length telemetry (cheap version):** log day count, node counts, death cause to
  a local file at run end; eyeball after playtest sessions. The five run-performance
  counters (NODES §8) double as telemetry.
- **Seeded A/B:** fix a seed, play the change, compare notes. Record the seed in the
  playtest log (40-QA.md §5).
- **Change one axis at a time.** Node-level scaling and day-scaling multiply; tuning
  both simultaneously makes attribution impossible.

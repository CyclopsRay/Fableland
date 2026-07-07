# Fableland — Studio Instructions (Overview)

This folder is the **operating manual** for building Fableland. One person wears every
hat, so these docs are written as if each department were a separate team — because in
practice they are: *you-the-designer* hands work to *you-the-engineer*, who hands it to
*you-the-tester*. The documents are the interfaces between those hats. When a hat is
confused, the fix is almost always "update the doc the previous hat should have written."

| File | Department | One-line charter |
|------|-----------|------------------|
| `00-OVERVIEW.md` | Studio | The game, the roadmap, the golden rules, the change workflow |
| `10-DESIGN.md` | Game Design | GDD process, content-addition checklists, balance philosophy, TBD registry |
| `20-ENGINEERING.md` | Engineering | Architecture, core patterns, feature-addition recipes, data-driven rules |
| `30-DATA-AND-BALANCE.md` | Data & Balance | Where every tunable number lives, the modifier stack, runtime tuning |
| `40-QA.md` | QA / Testing | The testing pyramid, bug workflow, regression checklists, determinism tests |
| `50-ART-AUDIO.md` | Art, Animation & Audio | Placeholder policy, animation pipeline, sprite specs, audio plan |
| `60-PRODUCTION.md` | Production | Versioning, branching, milestones, definition-of-done, scope control |
| `70-MERCHANDISING.md` | Marketing & Merch | IP strategy, brand kit, community beats, merch pipeline |

Two sibling collections complete the manual:
- **`Docs/Tech/`** (`T00-INDEX.md` …) — the senior-developer rulebook: module
  dependency law, contracts, extensibility patterns, per-feature implementation
  blueprints, and performance budgets. `20-ENGINEERING.md` says what the architecture
  is; the T-docs say how to keep it healthy as it grows.
- **`Docs/IDEAS.md`** — the design idea ledger (explicitly non-canon): characters,
  worlds, items, and modes waiting to graduate into GDDs via the change workflow.

---

## 1. What Fableland is

A **2.5D arena-fighter roguelike** (Godot 4.7 / .NET C#). Every run: fused storybook
worlds form a disc-shaped overworld; the player crosses it toward the central **VOID**
while a 45-day clock devours the map from the rim inward, then fights the dark leader
at the core. Death is permadeath. Runs are fully **seeded** — an 8-char seed reproduces
the entire map.

Think of the game as **three stacked layers**, each with its own GDD:

```
RUN layer      — the overworld: zones, nodes, edges, days, stamina, VOID  → MapGDD.md
NODE layer     — what happens inside a node: missions, shelters, ? events → NODES.gdd
COMBAT layer   — the arena: characters, foes, items, hazards              → character GDDs,
                                                                            FOES.gdd, ITEMS.gdd
```

Two independent difficulty axes multiply (FOES.gdd §1): the **node level** (1–6, chosen
by the player on the map) sets objective complexity; the **foe level** (1–8, set purely
by the elapsed day) sets enemy stats/skills. This is the game's central tension knob —
protect it in every design and code decision.

## 2. Current state and roadmap

Shipped (v0.3.7, branch `prototype-0-playable`):
- Arena vertical slice with **Pomegraknight** (full kit), crab foe, hazards, statuses,
  damage/knockback/stun model, HUD (`Migration.md` §0).
- Full **map layer**: seeded generation, zones/levels/edges/function nodes, day/stamina,
  VOID devour, mist, zone-6 singularity, schematic + rendered atlas views.
- **Design + engineering law is now spec-complete on paper**: the GDD suite
  (Map/NODES/FOES/ITEMS/Gameplay + 5 character GDDs), this studio manual (`Instructions/`),
  and the technical rulebook (`Tech/`) are cross-referenced and decision-logged, ready to
  drive the v0.4.0 build. (Docs only — no new runtime code since v0.3.5.)

Planned landings (the version numbers are already reserved in the GDDs — keep them):

| Version | Milestone | GDD |
|---------|-----------|-----|
| **v0.4.0** | Foe system: `Scripts/Foes/` refactor, Crab + Seagull, 8-level scaling, sight/aggro FSM, evolution | `FOES.gdd` |
| **v0.5.0** | Node content: missions (Collection/Protect/Destroy/Slaughter/Boss), shelters, day cycle & day-end resolution, team/build | `NODES.gdd` |
| **v0.6.0** | Wonder items: slots, tags, cooldowns, lifecycle, catalog | `ITEMS.gdd` |
| later | Plantation GDD + system, boss kits, ?-node events, LV5/6 content, meta-progression, more characters (Cleopastar, Pixolotl, PumpKing, Sifu Pangda) | various |

Rule of thumb for ordering: **each milestone must be playable and testable on its own**
before the next starts (the same vertical-slice discipline that produced prototype 0).

## 3. The golden rules (cross-department invariants)

These override convenience. Every department doc assumes them.

1. **GDD first.** No mechanic exists in code that isn't written in a GDD, and no number
   in a GDD is contradicted by code. When they drift, fixing the drift is a bug-priority
   task. Every GDD keeps a **Decisions log** (see the existing ones — keep the format).
2. **Determinism.** All gameplay randomness flows through `DetRandom` seeded from the
   run seed. Never `System.Random` or Godot's global RNG for anything that affects a
   run. This is what makes bugs reproducible and balance testable.
3. **Data-driven numbers.** Any value a designer might want to change — stats, slots,
   caps, cooldowns, probabilities, schedules — is a *looked-up datum*, never a literal
   in logic, and never a constant when the design says it can be modified in-game
   (party cap, held slots, gravity, move speed are all modifiable by items!). See
   `30-DATA-AND-BALANCE.md`.
4. **Units are derived.** 32 px/m, 2 m player, 8 m/1 s jump ⇒ g = 2048 px/s². Everything
   comes from `Units.cs`. GDDs speak meters/seconds; code converts once, at the edge.
5. **Versioning trio.** Every commit bumps the patch and keeps `Scripts/GameVersion.cs`,
   root `VERSION`, and the HUD `VersionLabel` in sync. Minor version = GDD milestone.
6. **Every bug becomes a caveat.** After fixing any bug, append symptom → rule → why to
   `KNOWLEDGE.md`, tagged with the version. Read it before writing code. This file is
   the studio's institutional memory — the thing a real team would carry in senior
   engineers' heads.
7. **Aggregatable modifiers, keyed by source.** Buffs/debuffs/defense/damage multipliers
   are dictionaries keyed by source string that sum — never a single float that gets
   overwritten (the existing `DamageDealtMultiplier` / `_defenseBonuses` pattern).
   The **single-timer stackable buff** model (Cleopastar's StarSicking) is the universal
   pattern for stacking buffs: one timer per buff instance, not per stack.

## 4. The change workflow (any feature, any size)

```
1. DESIGN      Write/update the GDD section. Numbers go in tables. Record the decision
               and its "why" in the Decisions log. Mark unknowns TBD explicitly.
2. DATA        Add/adjust the entries in the data tables (30-DATA-AND-BALANCE.md).
3. ENGINEER    Implement against the pattern recipes (20-ENGINEERING.md). Read
               KNOWLEDGE.md first. Scene + script changes stay in lockstep.
4. VERIFY      Static checks always; real build + headless tests when a toolchain is
               available; play the affected loop with a fixed seed (40-QA.md).
5. RECORD      Bug found? Fix → KNOWLEDGE.md caveat. Design changed during
               implementation? Back-propagate to the GDD before committing.
6. SHIP        Bump version trio, changelog line in Migration.md §0, commit.
```

Never skip step 1 for "small" mechanics and never skip step 5 for "obvious" fixes —
those two shortcuts are how solo projects rot.

## 5. Advice from the architect (things I want you to know)

- **Your GDDs are unusually good.** They have numbers, decisions logs, and integration
  tables. The single biggest risk is *not keeping them true* as code evolves. Budget
  10 minutes of doc-sync into every task; it repays itself within weeks.
- **The seed is a superpower.** Determinism gives you free repro for bugs, free A/B for
  balance, and (later) free marketing (daily-seed challenges). Guard it jealously —
  one stray `GD.Randf()` in a gameplay path silently poisons all three.
- **Beware the flagged scope bombs.** The GDDs already flag them: boss-as-protagonist
  (every LV4 boss = a boss kit **plus** a player kit), Pixolotl's day-rewind undo scope,
  ?-node event content, plantation. Treat each as a milestone, not a task.
- **Playable beats complete.** Prototype 0 worked because it cut everything except the
  loop. Apply the same knife to each milestone: v0.5.0 needs *one* mission type fully
  fun before all five exist.
- **When you're stuck between hats, write it down as a handoff.** "Design hasn't
  specified X" → add a TBD to the registry (10-DESIGN.md §6) and pick the safest
  default, noting it in the Decisions log. Don't stall; don't silently invent.
- **Merchandising starts now, cheaply.** Every character GDD already contains poster
  and sprite-sheet prompts. Keep generating and archiving key art as characters land —
  a launch-ready press kit is a byproduct, not a project.

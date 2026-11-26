# Fableland — Engineering Department

How to build and extend the code. **Read `KNOWLEDGE.md` before every session** — it is
the caveat list that keeps you from re-fixing old bugs; this doc is the architecture
and the recipes. When the two overlap, KNOWLEDGE.md wins on Godot/C# specifics.

---

## 1. Architecture — current and target

```
Scripts/
├── Foundation/              # Units.cs + GameVersion.cs + InputDefaults.cs (the one input
│                            #   action the game registers itself: move_up)
├── Gameplay/
│   ├── Characters/          # CharacterController (a fighter, on either side) + CharacterControls
│   │                        #   (ControlAction, PlayerControls, VirtualPad)
│   ├── Combat/              # HitInfo, DecayingDebuff, CombatTarget (sides, ICombatTarget,
│   │                        #   CombatTargets) — universal combat contracts
│   └── World/               # SoftVolume + WonderCorePickup shared world gameplay
├── Protagonists/
│   ├── Pomegraknight/       # Pomegraknight + PomeSeed
│   ├── PumpKing/            # PumpKing + PumpKingHead
│   └── Legacy/              # throwaway prototype Player, isolated until deletion
├── Bosses/                  # v0.10.11 — BossBrain (rules, scheduler, senses), BossFighter
│   │                        #   (what every boss shares), one class per boss
│   ├── PumpKing/            # BossPumpKing : PumpKing
│   └── Dev/                 # BossFightCheck (headless, 40-QA.md)
├── Data/                    # balance tables: CharacterTable, BossTable, …
├── Hazard/                  # Hazard base + Fire/Freeze/Damage/Tsunami + trigger
├── Orchestration/Arena/     # GameManager scene/run/mission integrator
├── UI/                      # Hud, HpBlockBar, DamageNumberManager, ShakeCamera2D
├── Map/                     # DetRandom, MapGenerator, MapData (WorldDef.Pool — append-only),
│                            #   MapController(+Atlas), MapRenderModel — the seeded overworld
├── Run/Dev/                 # v0.11.0 — RunWalkCheck (headless: plays real runs, 40-QA.md)
└── Menu/MenuController.cs

PLANNED (create at each milestone — names already referenced by GDDs):
├── Foes/                    # v0.4.0 — BaseFoe, CrabFoe, SeagullFoe, FoeStats  (FOES.gdd §9)
├── Nodes/                   # v0.5.0 — missions, shelters, day cycle          (NODES.gdd)
├── Items/                   # v0.6.0 — item defs, inventory, tags, cooldowns  (ITEMS.gdd)
├── Run/                     # v0.5.0 — RunState autoload (see §3)
└── Data/                    # balance tables (see 30-DATA-AND-BALANCE.md)
```

Don'ts (from `CLAUDE.md`, enforced): don't rename/move scripts or scene nodes without
updating `.tscn` `ext_resource` paths + `GetNode`/NodePath refs + exported scene
assignments; don't shadow inherited members; never commit the PAT.

## 2. Core patterns (use these; don't invent parallels)

- **Velocity model:** `Velocity = intentVel + externalVel`. Intent = input/gravity/jump/
  SoftVolume field with momentum (accel/friction); external = impulses via
  `AddImpulse(Δv)` decaying at `ExternalDamping`. Continuous forces (pulls, tornado,
  Blackhole, Pangda's Ult) = per-tick impulses. Never write `Velocity` directly from a
  skill.
- **Sides and targets (v0.10.11):** a fighter is on a `CombatSide`; an attack hits
  `CombatTargets.HostileTo(GetTree(), Side)` and talks to `ICombatTarget`. Never cast to
  `BaseFoe`, never name a group in attack code, and give a projectile the side of whoever
  launched it. (T10 §4a.)
- **Controls (v0.10.11):** a kit reads `Controls.Held / JustPressed / JustReleased(ControlAction…)`
  and `Controls.AimPoint(this)` — never `Input`, never `GetGlobalMousePosition()`. That is
  what lets a boss brain press the same buttons. The base class is under the same rule:
  swimming's Up and Down are `ControlAction.MoveUp` / `MoveDown` (v0.11.1) — written as
  `Input` reads they swam the boss on the player's keys, and the boss check has a stage
  for it. A new button is a `ControlAction`, a name in `PlayerControls`, and — if
  `project.godot` does not define the action — a line in `InputDefaults`.
- **Hits:** author per skill as `HitInfo { Damage, Knockback, Stun }`. `Stun < 0` ⇒
  default gain-no window `Units.StunPerDamage · Damage`. Melee/AoE checks are
  **radius-aware** (target circle overlap, not center point — KNOWLEDGE caveat v0.1.5).
- **Damage taken:** single lever — defense: `mult = 100/(100+defense)`, defense being an
  aggregatable pool. Don't add flat damage-reduction multipliers (removed in v0.2.1).
- **Statuses:** `DecayingDebuff` (integer points, cap 99, 10%/0.2 s decay, ceil) backs
  OnFire/Frozen. New stackable buffs follow the **single-shared-timer** model: timer
  on the buff instance, not per stack.
- **Modifiers:** dictionaries keyed by source string that aggregate (sum), e.g.
  `SetDefenseSource("Frozen", 30)` / `ClearDefenseSource`. Every new buff-like effect
  uses this. Generalized spec in `30-DATA-AND-BALANCE.md` §3.
- **Hazards:** stationary `Area2D`, collision box built in code from `BoxSize` (shape
  can't drift from telegraph), tick every 0.25 s via `Deliver(...)`; ticks deliberately
  **bypass** the 0.8 s hit i-frame; one-shots (Tsunami) go through `TakeHit` instead.
- **Ammo/magazine:** all BA resources use the shared `AmmoController` contract in
  `Gameplay.gdd` §A.2.1. Migrate Pomegraknight and PumpKing's local counters before
  adding Cleopastar; never build a character-local ammo/reload timer.
- **Autoload singletons:** register using the script's real module path, set static
  `Instance` in `_EnterTree`; they survive `ReloadCurrentScene`.
- **Determinism:** all gameplay RNG through `DetRandom` derived from the run seed (use
  suffixed sub-seeds like `seed+"R"` per subsystem so adding a consumer doesn't shift
  another's stream). UI-only randomness may use anything.
- **Spawning:** `AddChild` runs `_Ready` synchronously — call `Init(...)` after
  `AddChild`, and don't depend on Init'd fields inside `_Ready`.

## 3. RunState — the spine of v0.5.0+ (design it once, well)

Everything that outlives a scene belongs in one autoload, `Run/RunState.cs`:

- Seed + `DetRandom` root; day counter (and the zone-6 `???` latch); stamina.
- Map graph reference + per-node state (visited / completed / devoured / Blessed).
- Party: owned protagonists, active build (≤ cap), per-protagonist HP carried between
  fights; **caps are queries, not constants** (`PartyCap()` = base 3 + modifiers —
  Weird Mushroom outcome (f) raises held slots permanently).
- Inventory: held items per protagonist + backpack; item instance state (CD remaining,
  perish remaining, tags — instances, because fruits roll inherited stats).
- NPC windows: per-shelter trader/wanderer first-entry day (5-day departures).
- Plantations: per-shelter slots, plant, days grown, watered state.
- Run-performance counters (NODES §8): nodes traversed, worlds visited, goals
  succeeded, protagonists collected, items collected.

**Day-end resolution is an ordered pipeline** — implement it as an explicit ordered
list of steps, exactly `NODES.gdd` §7.4: day++ → VOID devour → held-item CD tick →
perish tick (held AND backpack) → NPC departures → stamina refresh. New systems that
need a daily tick register a step here — never a second "on day end" code path.

Scene flow: `Menu → Map (Exploration) ⇄ Arena/Shelter/Event (Adventure)`. The map
already runs standalone; when wiring Adventure scenes, pass only node id + RunState —
the arena derives mission from node level and foe level from day (FOES §10).

## 4. Feature-addition recipes

**New character** — 1) GDD complete per `10-DESIGN.md` §2. 2) Subclass
`CharacterController`; override `InitCharacter`, `HandleBA/HandleSkill1/2/Ult`,
`DrawDebug`, later `UpdateAnimator`. Read input only through `Controls` and find targets
only through `CombatTargets` (core patterns, above) — the character may be a boss one day. 3) New base-class needs (e.g. a continuous-force source)
go in as minimal, non-breaking, documented additions. 4) Scene: CharacterBody2D +
Camera2D + FirePoint + empty AnimationPlayer, collision 48-wide×64 class shape, layers
per `Units.cs`. 5) Projectiles = own scenes/scripts; guard 0-lifetime first-tick.
6) Damage applied inline with `// NOTE(animation)` markers for later event tracks.

**New boss (v0.10.11+)** — a boss is its protagonist's class on the Foe side (FOES §8,
T30 §9): a `BossTable` row; `class BossY : Y, IBossFighter` (`Side => Foe`, `BeginFight`,
a rule list — and nothing else: differences in the *kit* are `protected virtual`s on `Y`);
`Scenes/Bosses/BossY.tscn` inheriting `Y.tscn`; make `Y`'s projectiles take their side from
the launcher; a `boss` map with perches; a stage in `BossFightCheck`, and one scenario per
rule of its brain in the check's rules probe. Run the check, then its mutation habit: break
the new mechanism — and every rule, not a sample — and see each fail by name.

**New foe (v0.4.0+)** — subclass `BaseFoe` (FOES §9): override `InitFoe`,
`UpdatePatrol/UpdateAggro`, `Skill1/Skill2` (base gates by level via `FoeStats`),
`OnDeath` (loot hook; Crab's spawn-on-death lives here), `DrawSight`. Stats go in the
data tables, not the subclass. Respect: cap 6 (spawn-on-death ignores it), evolution
at 25 s (+1 level once, heal 30% new max), spawned crabs can't spawn.

**New wonder item (v0.6.0+)** — items are **data + small behavior class**:
an `ItemDef` (id, domain, tags, CD type+value, passive modifier list, skill ref,
conversion target, perish days, plant data) and, only if the skill is bespoke (DD's
bug, Yukai's rope), a skill class. Passives apply/remove via the modifier-by-source
system on equip/unequip (`Possession` ⇒ apply while in backpack too). Conversion =
replace instance in place (slot preserved). Enforce `Eternal`×`Convertible` exclusion
in the loader with a validation error.

**New hazard** — subclass `Hazard`, set `BoxSize`/`TickInterval`, override the deliver
payload (`Deliver` raises the hazard's `tick` cue for you) and `DefaultCueKind`; a scene that
reuses a script names itself with `CueKind` ("lava", "spike"). One-shots opt into `TakeHit`
explicitly, as a ranged hit: `.FromRanged(CueOwners.Hazard, "tsunami")`.

**Give an attack its feedback (v0.11.2)** — `Docs/Feedback.gdd`; copy
`Pomegraknight.cs`, the reference kit. Gameplay never shakes, tints or plays anything:

1. **Name the hit.** A blow of the body: `new HitInfo(dmg, knock).From(this, "ba", stage)`.
   A projectile, a field or a tick: `.FromRanged(owner, "s2.seed")` — or
   `.FromRanged(_cueOwner, "s2.seed", ownerBody)` from a projectile that keeps its owner's
   `FeedbackId` as a string, because it may outlive the body. Name a weight
   (`HitWeight.Light`) only when the hit must stay in a tier whatever buffs do to it (ticks).
2. **Hold the attacker, once per swing — melee only.** `MeleeCone(..., cueAction: "ba",
   cueVariant: stage)` does it. A kit with its own shape uses the same helper:
   `var swing = new SwingStop();` → `swing.Landed(target, dealt)` per victim →
   `ApplyHitStop(swing.AttackerStop());` after the loop. A foe's own blow goes through
   `BaseFoe.Strike`. Never for a ranged hit.
3. **Raise the cues**, on the line where the moment happens, with the attack's own numbers:
   `CueBus.Emit(this, "ba.swing", variant: stage, size: reach, direction: FacingDirection,
   tag: IsBurning ? "burning" : null)`. Something that lasts is `CueBus.Start` …
   `CueBus.Stop` (and `Stop` in `_ExitTree` if it may still be running). A volley is **one**
   cue with a count. Constant strings only — never build one.
4. **A kit that moves the body itself** holds for hit-stop: after `base._PhysicsProcess`,
   `if (HitStopHeld) return;`; if it bypasses the base tick, `if (HoldForHitStop(delta))
   return;` instead.
5. **Override `FeedbackId`** with the kit's snake_case content id.
6. **Remove what the kit did by hand** — `ShakeCamera(...)`, a tint, a blink — and give the
   feel director a row in `FeelTable.ByCue` only if the moment needs a beat of its own.
7. Add the cues to `Docs/Feedback.gdd` §6 and a probe to `FeedbackCheck`; break the wiring
   once and watch it fail by name.

**New mission type (v0.5.0+)** — a `Mission` strategy object the arena GameManager
hosts: `Setup(nodeLevel)`, `Tick`, `IsComplete/IsFailed`, `GrantReward`. Mission choice
is rolled **at map-generation time** from `DetRandom` and stored on the node.

**New world** — add it to the pool in `MapGenerator`, give it an atlas land palette, and ensure
its seeded sector, altitude field, boss-reachability tree, required LV3 function routes, and
extra function-node counts pass the map invariants (40-QA.md §4).

## 5. Data-driven rules (the engineering half)

- No gameplay literal in logic. `[Export]` knobs for per-scene placement; `Data/`
  tables for balance (see `30-DATA-AND-BALANCE.md` for the lookup architecture).
- Anything the design marks modifiable-in-game (speeds, gravity, caps, slots, CDs) must
  be **base value + modifier stack**, read through a getter, every frame it matters.
  Gravity is already per-character (`Pixolotl 0.6× under SOBRECARGA`, Feather −20%) —
  multiplicative sources compose, so route them through the modifier stack too.
- Tables carry GDD section references in comments so drift is greppable.

## 6. Verification duties (engineering's share)

Before any commit: brace/paren balance, `.tscn` resource paths resolve, `GetNode`
paths match scenes, no inherited-member shadowing, version trio synced. When a
toolchain exists: `dotnet build` + headless test run (40-QA.md §2). After any bug fix:
KNOWLEDGE.md caveat, no exceptions. Static checks miss type/API-existence errors —
say so honestly in commit messages when a real build wasn't possible.

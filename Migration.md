# Glory of Fableland — Unity → Godot Migration Guide

> **Audience:** Future AI agents (Claude Code, etc.) executing this migration step by step.
> **Principle:** Build a working prototype first (map + one character), then expand.
> **Reference:** Read `CLAUDE.md` for the Unity project architecture before starting.

---

## 0. Prototype 0 — DELIVERED & PLAYABLE ✅ (2026-07-03)

### Changelog
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

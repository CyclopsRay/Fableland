# IMPLEMENTATION_REPORT.md — Fableland full-project build

> Orchestration log for implementing the GDD suite (v0.4.0 foes → v0.5.0 node content →
> v0.6.0 items) on top of prototype 0 (v0.3.7). Maintained by the orchestrating session;
> updated after every phase so a fresh session can resume from here. Companion docs:
> `KNOWLEDGE.md` (caveats), `Docs/Tech/T30-FEATURE-BLUEPRINTS.md` (blueprints this build
> follows).

**Status: v0.6.0 (Wonder Items) IN PROGRESS on branch `ver0.6.0-items` — see §7. v0.5.0/v0.5.1 landed and merged to main (04b8843).**
**v0.4.0–v0.5.0 record:** started 2026-07-07 from v0.3.7 (8315b4f); §§1–6 below are the closed v0.5.0 log.

---

## 1. Build strategy

Prototype-first, per the user's instruction: make the whole run loop *run* end-to-end
with placeholder content, log open questions instead of stalling. Milestone versions
follow the roadmap (00-OVERVIEW §2): **v0.4.0 = foe system**, **v0.5.0 = node content**.
Items (v0.6.0) are stubbed (wonder-core currency + placeholder item tokens only).

Phases (each = one sub-agent brief; orchestrator reviews all code):

| Phase | Version | Scope | Agent | Status |
|-------|---------|-------|-------|--------|
| 1 | v0.4.0 | `Scripts/Foes/`: BaseFoe, CrabFoe, SeagullFoe, FoeStats, Poop, evolution, sight/aggro FSM | opus | REVIEWED, APPROVED |
| 2 | v0.5.0-pre | `Scripts/Run/RunState.cs` autoload, mission-type roll at mapgen, MapController → RunState refactor, scene handshake (incl. Shelter/Event/RunOver placeholder scenes + controllers) | opus | REVIEWED, APPROVED (3 fixes) |
| 3 | v0.5.0-pre | Missions (Collection/Protect/Destroy/Slaughter/Boss) + arena GameManager rebuild + mission HUD + Map/Finish-Day buttons | opus | REVIEWED, APPROVED (4 fixes) |
| 4 | v0.5.0 | Shelter scene (Blessing actions), ?-node EventRunner, day-end pipeline UI, run-over/victory flow | sonnet/opus | REVIEWED, APPROVED (core in Phase 2; residuals 2026-07-08) |
| 5 | — | Orchestrator review, static verification, doc-sync, version bumps, commits | orchestrator | DONE except commit (not authorized this session) |

### Integration contract (locked before any code)

Per `T10 §3` / `T30 §1`, the single seam between layers:

```
MapController ─▶ RunState.BeginAdventure(nodeId)   // snapshots level/mission/day; swaps scene
Arena/Shelter/Event scene ◀── reads RunState.CurrentAdventure (null ⇒ debug defaults, F5 keeps working)
scene ─▶ RunState.ReportGoal(success, rewards)
"Finish the Day" ─▶ RunState.EndDay()              // ordered pipeline (T30 §5) → back to Map scene
```

Day/stamina/visited/devour state MOVES out of `MapController` into `RunState`.
MapController keeps rendering + movement rules, reading/writing through RunState.

---

## 2. Prototype simplifications (user-approved or logged for decision)

Explicitly sanctioned by the user ("build a random map, put some random foes,
the point is to make the game run"):

- **S1. One combat arena template for all combat nodes.** A procedurally-varied arena
  (ground + a few platforms + optional SoftVolume/hazards placed via the node's
  DetRandom sub-seed) — no per-world/per-style arenas yet.
- **S2. Foe composition is a random crab/seagull mix** scaled by day-based foe level;
  no per-world composition tables yet (FOES.gdd says wave composition TBD anyway).

Decided by orchestrator to keep the prototype moving (review when convenient):

- **S3. Boss fights use a placeholder boss** — a scaled-up Crab (big HP, level-scaled)
  since FOES.gdd §8 is TBD. LV4 win grants nothing recruitable yet (see Q2), LV6 win
  = run victory screen.
- **S4. Items stubbed:** wonder cores = an int on RunState; "1 random wonder item"
  rewards grant a placeholder `ItemInstance{DefId="placeholder"}` counted in inventory.
  No passives, slots, or trading yet (v0.6.0).
- **S5. Party of one.** Only Pomegraknight exists, so the switch-protagonist system
  (Tab) is scaffolded but inert per NODES §3.3 ("1 protagonist = Tab unavailable").
  HP persistence across fights goes through `ProtagonistState` as designed.
- **S6. Shelter = menu UI** (Gameplay.gdd B says menu is fine for now): Rest /
  Sharpen Weapon / Sharpen Armor + free actions stubbed. Traders, jousting,
  plantation, teleport are deferred with visible "TBD" greyed buttons.
- **S7. ?-nodes run a minimal EventRunner** with 2–3 placeholder events (text +
  choices → grant cores / small heal / nothing) proving the verb pipeline (T30 §7).
- **S8. Protect core is not healable** (GDD marks healing-the-core TBD).
- **S9. No save/load yet** (T30 §6) — a run lives and dies in-process. Logged as
  next milestone after v0.5.0.

## 3. Open questions for the user (decide later; prototype picked a default)

- **Q1.** Combat-node arena styles per world/level — sample maps needed. Prototype
  uses S1 random template.
- **Q2.** LV4 boss reward is "recruit boss as protagonist" but no boss player-kits
  exist. Prototype: LV4 win just marks node conquered + grants a placeholder item.
- **Q3.** Reward delivery mechanism for combat (NODES §1.2 "delivery TBD"): prototype
  applies rewards instantly on goal achievement, with a HUD toast.
- **Q4.** Collection: exact core spawn cadence/despawn timing not specified. Prototype:
  1 core on field at a time per required count pacing — spawn on collect, 12 s despawn,
  respawn elsewhere (all from mission DetRandom). Tune later.
- **Q5.** Wave composition per world (FOES TBD): prototype rolls each wave as
  60% crab / 40% seagull per slot, count = 3 + nodeLevel.
- **Q6.** "On failure player returns to previous position with 0 stamina" — prototype
  implements as: return to the node moved *from*; if that node was devoured meanwhile,
  run over (VOID rule).
- **Q7.** In-void devour semantics: outer ring already consumed; EndDay still steps
  the devour table by hidden day (moot in practice). Confirm this matches intent.
- **Q8.** Destroy-mission objectives: stats not specified. Prototype: stationary
  targets, HP 60 × node multiplier, placed via DetRandom on platforms/ground.
- **Q9.** NODES §1.1 wants a Map button during Adventure (pause + view-only map). The
  map is a separate scene, so an in-combat map overlay needs a SubViewport or a
  rendered snapshot — deferred; prototype omits the in-combat Map button (Finish the
  Day gating is implemented). Decide desired UX later. *(Extended 2026-07-08 per Phase 4
  review: the same deferral covers Event.tscn — NODES §1.2 implies view-only map access
  from "?" nodes too; Shelter keeps its free "Back to Map" since leaving a shelter is
  legal movement, not a map view.)*
- **Q10.** Protect-mission foe aggro on the core is "detailed AI TBD" in NODES §4.4.
  Prototype: foes chase the player as usual, but any foe within contact range of the
  core damages it on its contact cooldown (crabs), so ignoring foes still loses the core.

## 4. Phase log

### Phase 0 — survey (done, 2026-07-07)
- Read: CLAUDE/KNOWLEDGE/Migration §0, NODES/FOES/Gameplay GDDs, T00/T10/T30,
  00-OVERVIEW, MapData/MapController(核心部分)/GameManager/Arena.tscn/project.godot.
- Findings driving the plan:
  - `MapController` currently owns day/stamina/visited/devour (lines ~25–50, 173–235)
    — must be extracted into RunState without breaking the atlas renderer, which reads
    the same state.
  - `MapNode` has no mission-type field; missions must be rolled in `MapGenerator`
    (deterministic, from the map seed) and stored on the node.
  - Existing `GameManager` uses `GD.Randi()` — violates the determinism golden rule;
    the rebuild must route all arena randomness through a DetRandom sub-seed.
  - Only autoload today is `DamageNumberManager`; RunState joins it (T10 caps the list).
  - `Enemy.cs`/`Enemy.tscn` are the to-be-replaced foe layer; `WonderPage.cs` becomes
    the wonder-core pickup (rename per v0.3.7 decision).

### Resume audit — 2026-07-08 (fresh orchestrator session)

Working tree inspected against the report (nothing committed since 8315b4f). Findings:

**Phase 3 is PARTIAL.** Written and coherent (brief-aware doc comments citing S1–S8,
C1–C3, Q4/Q5/Q8/Q10, NODES §§ — clearly an engineer pass whose session ended before
review/logging):
- `Scripts/Missions/`: Mission.cs (strategy base + HUD read-props + FatalTimeout/
  IsFinalBoss/NeedsRewardChoice hooks), MissionTable.cs (pure NODES §4.2 data),
  CollectionMission, ProtectMission, DestroyMission, SlaughterMission, ProtectCore,
  DestroyObjective (BaseFoe subclass, CanEvolve=false), BossCrab (CrabFoe ×8 HP, ×2 scale),
  ArenaBuilder (S1 procedural platforms/hazards), FoeSpawner (C1 aerial offset, C2 seeded
  Rng, C3 BabyCrabScene injection — all honored *in the spawner*).
- Scenes: `Scenes/Missions/{BossCrab,DestroyObjective,ProtectCore}.tscn`,
  `Scenes/WonderCorePickup.tscn` + `Scripts/WonderCorePickup.cs` (WonderPage successor).
- Player-side hooks already landed: `CharacterController.HydrateRun(...)` + `HpRatio`.

**Phase 3 gaps (the integrator layer is missing entirely):**
- G1. `GameManager.cs` is still the Phase 1 version (its own comment says "Phase 3
  replaces this"). Missions reference a GameManager API that does not exist:
  `FoeLevel`, `Spawner`, `Entities` (public), `RandomFoeSpawn(DetRandom, bool aerial)`,
  `RandomPlacementPoint(DetRandom)`, `WonderCorePickupScene`, `ProtectCoreScene`,
  `DestroyObjectiveScene`. **The project cannot compile as-is.**
- G2. No `BossMission.cs` — Mission.cs's FatalTimeout/IsFinalBoss hooks have no
  implementor; BossCrab.tscn is never instantiated by anything.
- G3. `Arena.tscn` still wires `WonderPageScene = WonderPage.tscn`; none of the new
  PackedScene exports are wired; GameManager still uses `GD.Randf/Randi` (C2 unmet at
  the arena level).
- G4. Mission HUD not built: `Hud.cs`/`Hud.tscn` untouched (no mission/progress label,
  timer, secondary bar, reward-choice buttons, Finish-the-Day button). `SetPages`
  (WonderPages counter) still present.
- G5. `Scripts/WonderPage.cs` + `Scenes/WonderPage.tscn` not deleted (rename to
  WonderCorePickup incomplete); GameManager/Arena/Hud still reference them.
- Also present, unlogged but sound: `Scripts/Map/VoidSchedule.cs` (devour table extracted
  from MapController so DayEndPipeline's VoidDevourStep and the map view share it — part
  of Phase 2's refactor, retroactively noted here) and the three placeholder sprites
  (crab/seagull/poop — Phase 1 assets).

**Phase 4 was mostly pre-delivered in Phase 2** (its review already covers "Blessing
actions/Event choices/RunOver all wired through RunState"): `Scripts/Nodes/
{Event,Shelter}Controller.cs`, `Scripts/Run/RunOverController.cs`,
`Scenes/{Event,Shelter,RunOver}.tscn` all exist, null-tolerant, GDD-cited. Residual
Phase 4 gaps: EventController has ONE hardcoded event (S7 wants 2–3 placeholder events
proving the verb pipeline); day-end pipeline UI is a debug string only (`DayEndOrder()`
exists, nothing displays it). Assess after Phase 3 completion.

## 5. Review findings

### Phase 1 (foe system) — REVIEWED, APPROVED (2026-07-07)

Verified by orchestrator: BaseFoe/CrabFoe/SeagullFoe/FoeStats/PoopProjectile/PoopHazard
match FOES.gdd numbers; evolution formula isolated in static `BaseFoe.EvolveHp` per T30;
tick order per T30 §2; every external API referenced (`CharacterController.LocalPlayer`,
`GainUltCharge`, `HitInfo.ResolveStun`, `SoftVolume.ComputeVelocity`,
`DamageNumberManager.Pop`, `Hazard.ApplyTick/TintFill/TintEdge/BoxSize`, `Units.Layer*`,
`Units.Px`) exists; `.tscn` paths + collision masks correct (crab 2/12, gull 2/4,
poop 16/13); zero remaining `Enemy` type references; Enemy.cs/tscn deleted.

Agent's open decisions accepted (also see agent notes): seagull top speed 160 px/s
(GDD silent — tune later); Soft Shell only on discrete hits, not hazards; crab jump
apex = player height via √(2gΔh); getting hit force-aggros sight-capable foes;
skill dispatch via per-frame `UpdateSkills` + `SkillNReady` gates instead of
parameterless `Skill1()/Skill2()` (trigger conditions need state — deviation from
FOES §9 signature, functionally equivalent, noted for doc-sync).

### Phase 2 (RunState + handshake) — REVIEWED, APPROVED with 3 orchestrator fixes (2026-07-07)

Verified: RunState autoload + data classes match T30 §1; day-end pipeline is the exact
T30 §5 ordered list (`IDayStep`), with devour comparing against `Day-1` to preserve the
old semantics after `Day++` runs first (agent decision, correct); mission roll uses a
dedicated `DetRandom(seed+"M")` stream so map geometry per seed is unchanged;
`DetRandom.Sub(tag)` derives by seed-string, not stream-advance (right call);
MapController is now a view layer with null-tolerant read aliases; pass-through rules
extended per NODES §1.3 (unvisited + unconquered-combat nodes block paths); Blessing
actions/Event choices/RunOver all wired through RunState; `_visited` caches rebuilt from
RunState on scene load so the atlas partial keeps working.

**Orchestrator fixes applied during review:**
- F1. **River nodes routed to Arena.tscn** — `BeginAdventure`'s default case sent the
  zone-6 River hub into a combat scene. Fixed: River (any inert kind) returns null scene
  and stays on the map; `MapController.TryMove` excludes River from content triggers and
  marks it visited via new `RunState.MarkNodeVisited`.
- F2. **Combat-failure bounce could land on devoured ground** (in-void re-attempt fails →
  `PreviousNodeId` is a devoured outer node). Fixed in `ReportGoal`: only bounce if the
  previous node exists and is not devoured, else stay on the combat node.
- F3. **Hand-invented `uid="uid://…"` strings** on the three new `.tscn` files — Godot
  generates real UIDs; fake ones risk collisions/warnings. Stripped (existing scenes
  carry none).

Accepted agent decisions: `MissionType` enum living in `Fableland.Run` referenced by
MapData (pure value, no logic cycle); Menu starts run on a random seed (map SeedEdit
still restarts on a typed seed); confirmation dialogs built in code; `WorldsVisited`
counts "XX" too (adjust when NODES §8 gating lands).

**Carry-forwards for Phase 3:**
- C1. Seagulls patrol at their spawn-marker height — Arena's `enemy_spawn` markers are
  near ground, so the Phase 3 spawner must place gulls 3–5 m up (FOES §4 default).
- C2. `GameManager` still uses `GD.Randf/Randi` (legacy) — Phase 3 must route arena
  randomness through a DetRandom sub-seed; `BaseFoe.Rng` is a settable property ready
  for a seeded source.
- C3. Crab babies need `BabyCrabScene` injection from the spawner (cyclic-resource
  caveat in KNOWLEDGE.md) — any new spawner must set it.
- C4. No test project exists for `EvolveHp` unit test (T30 asks for one) — deferred.

### Phase 3 (missions + arena rebuild) — REVIEWED, APPROVED with 4 orchestrator fixes (2026-07-08)

Completion pass (engineer agent, 2026-07-08) closed resume-audit gaps G1–G5: rebuilt
`GameManager.cs` as the arena integrator (mission instantiation by `AdventureContext.Mission`,
ArenaBuilder call, FoeSpawner ownership, HydrateRun/HpRatio write-back on every exit path,
all outcomes routed through RunState — zero direct `ChangeSceneToFile` in arena code, debug
F5 flow preserved); new `BossMission.cs` (BossCrab at FoeLevel, Rng seeded before AddChild,
240/360 s timer, FatalTimeout ⇒ BossTimer permadeath, LV6 IsFinalBoss ⇒ Victory, 50%-HP add
wave); rewired `Arena.tscn` (new PackedScene exports, authored Bonfire/FrozenPit removed —
hazards procedural now, TsunamiButton kept); mission HUD in `Hud.cs`/`Hud.tscn` (title/
progress/timer/secondary bar/reward choice/Finish-the-Day/toast, mouse_filter IGNORE on
non-interactive controls); WonderPage.cs/.uid/.tscn deleted, zero dangling references.

Reviewer verdict: **APPROVE** — static verification clean (compile-coherence of the full
mission↔GameManager API surface, .tscn integrity incl. load_steps, GetNode paths, exported
property names, determinism grep, no reward double-counting, MissionTable numbers exact vs
NODES §4.2/§4.4/§4.5, mission-roll 60:15:10:10 confirmed, all four HP write-back exit paths
present). Static-only — real `dotnet build` still required at next toolchain window (C4-adjacent).

**Orchestrator fixes applied during review:**
- F4. **Debug foe-level knob clamped real runs** (`GameManager.FoeLevel`): `DebugFoeLevel > 0`
  (default 1) won unconditionally, so every in-run fight would spawn level-1 foes; also the
  zone-6 per-node override was missing. Fixed: in-run = LV6→8, LV5→7 (FOES §2 v0.3.7 decision),
  else `LevelForDay(Day)`; debug knob only when no run exists.
- F5. **DestroyObjective diluted the ambient spawn cap** (reviewer MINOR): objectives inherited
  group "foe" from BaseFoe, so 5 objectives vs cap 6 starved Destroy missions of harassment
  foes. Fixed: `RemoveFromGroup("foe")` in `DestroyObjective._Ready` (stays in "enemy" so
  player attacks still hit); FoeSpawner comment updated.
- F6. **QA force-complete cheat added** (reviewer NOTE, 40-QA §1 v0.5.0 minimum): F9 in a
  debug build calls new `Mission.DebugForceComplete()` (Status ⇒ Succeeded; Slaughter's
  default reward applies if the choice was pending).

Accepted engineer decisions: fixed `"debug-arena"` seed for F5-debug arenas; combined
FoeStats+MissionTable difficulty title ("Fairytale Trip — Collection"); Finish-the-Day hidden
in debug / disabled-until-resolved in-run; LivesLabel hidden in-run (permadeath); boss add-wave
50% HP, 2 foes at FoeLevel−1; authored Platform1/2/3 kept (rare cosmetic overlap with
procedural platforms accepted); `page_spawn` markers left inert.

Logged (non-gating reviewer NOTEs): **N1** MissionTable lives in `Scripts/Missions/` not
`Scripts/Data/` (30-DATA-AND-BALANCE §2) — follows the FoeStats precedent, revisit at the
data-centralization pass; **N2** inline literals (Slaughter wave size 3+lvl, spawner cap 6 /
interval 3 s, boss add-wave params) left in code — GDD marks them TBD, centralize when tuned.

### Phase 4 residuals (generic EventRunner + day-end summary UI) — REVIEWED, APPROVED (2026-07-08)

Scope delivered (engineer agent): new `Scripts/Data/EventDefs.cs` (pure-data verb union
GrantCores/HealParty/GrantItem/Nothing + 3 placeholder events: traveller, old_shrine
2-pager proving page chaining, abandoned_cart with GrantItem + walk-away); rewritten
`EventController.cs` as the generic T30 §7 interpreter (deterministic pick via
`Rng.Sub("event:"+nodeId)`, code-built choice buttons, Finish-the-Day locked until the
TERMINAL page resolves per NODES §1.2, ResolveEvent exactly once, F5-safe); day-end
summary — `RunState.DayEndNotes` scratch + `LastDayEndSummary` assembled only by
`EndDay()` (one-owner intact, RunFinished latch honored), pipeline steps append
descriptions only; Map toast (`UI/ToastLabel`, mouse_filter IGNORE, show-once read+clear,
5 s auto-hide, dismiss-on-click without consuming the map-move click).

Reviewer verdict: **APPROVE**, no blockers, no fixes needed. Verified: zero per-event
branching (4th event = data only), no duplicate reward paths, notes cleared per day, no
RunOver clobber, one-owner rule intact, determinism grep clean, all GetNode/.tscn checks
pass. Two non-gating NOTEs triaged by orchestrator:
- Event.tscn has no Map button (Shelter has Back-to-Map) — folded into Q9's deferral
  (Q9 text extended above).
- Devour-count note attributes orphaned function nodes to the ring's tag (e.g. "1-A
  (14 nodes)" may include collateral shelters) — wording nuance, accepted for prototype.

Accepted agent decisions: bottom-banner toast placement; orphan nodes counted in the
devour total; F5 no-run event fallback = `EventDefs.All[0]` (no seed to derive from).

## 6. Version/commit log

(planned: one commit per phase, patch-bumped; minor bumps at v0.4.0 and v0.5.0 landings)

### Phase 5 close-out (2026-07-08) — UNCOMMITTED

Phases 1–4 all landed uncommitted in one working tree (no commit authorization was given
in either session), so the per-phase commit plan collapsed into a single milestone
version: **the trio is bumped straight to 0.5.0** (`Scripts/GameVersion.cs`, `VERSION`,
`Scenes/Hud.tscn` VersionLabel — verified in sync) with the full changelog entry in
`Migration.md` §0.

Phase 5 checklist state:
- [x] Static verification across the whole diff: brace/paren balance on all changed .cs
  (2 flagged were doc-comment false positives); every `.tscn` `res://` path resolves;
  determinism grep clean (only pre-existing cosmetic RNG in ShakeCamera2D/
  DamageNumberManager — presentation-only, accepted); autoload registration
  (`RunState`) present in project.godot; GetNode/exported-property checks done
  per-phase by reviewers.
- [x] Doc-sync: v0.5.0 decision-log entries appended to `Docs/FOES.gdd` (skill-dispatch
  signature, seagull speed, composition defaults, placeholder boss) and `Docs/NODES.gdd`
  (instant reward delivery, Collection pacing, Destroy objectives, Protect core aggro,
  no in-Adventure Map button, generic EventRunner, day-end toast).
- [x] KNOWLEDGE.md caveats added (v0.5.0): debug-override gating, group-membership
  semantics on BaseFoe subclasses, no hand-written `.tscn` uids (retro-recording
  Phase 2's F3).
- [x] Version trio 0.5.0 + `Migration.md` §0 changelog.
- [ ] **Commit** — NOT done: no user authorization this session. When granted, commit the
  whole tree as v0.5.0 (note in the message: static checks only, no real build ran).
- [ ] **Real `dotnet build`** (C4 carry-forward) — first action whenever a session has a
  toolchain; also the `EvolveHp` unit test and the T30 §8 gating-function unit tests
  remain open.

---

## 7. v0.6.0 — Wonder Items (started 2026-07-10, branch `ver0.6.0-items` off main@04b8843)

Scope per user brief: item system core (`Scripts/Items/`), 3 catalog items (Pome's
Bravery / Pome's Seed / FanChen's Heart), Vamp stat + F item-skill input, minimum
plantation slice, Build Team + Backpack UI. Save/load (T30 §6) explicitly out of scope.
`Docs/ITEMS.gdd` was pre-edited on this branch (Pome's Seed 7d/60–140% inheritance fix,
§9 v0.6.0 decisions entry) — treated as approved canon, folds into the milestone commit.

### 7.1 Phase table

| Phase | Scope | Agent | Status |
|-------|-------|-------|--------|
| I1 | Item model + data: ItemDef/Catalog/Registry, ItemInstance rebuild, Inventory on RunState, day-end steps (ItemCds/Perish/PlantationGrow), ShelterState plantation roll, reward-pool wiring | engineer | REVIEWED, APPROVED (3 fixes) |
| I2 | Combat side: Vamp pool + on-hit heal hook, OnFire duration mechanism, conditional item passives on CharacterController, F input action, GameManager held-item runner (skill exec, sec-CD tick, mid-combat convert) | engineer | REVIEWED, APPROVED (2 fixes) |
| I3 | UI: BackpackPanel (6×5 grid + detail), BuildTeamPanel (cards/stats/change/swap), ShelterController integration + Plant UI | engineer | REVIEWED, APPROVED (3 fixes) |
| I4 | Ship: full-diff static verification, doc-sync, KNOWLEDGE caveats, version trio 0.6.0, Migration changelog, commit | orchestrator | DONE (committed) |

### 7.2 Integration contract (locked before code)

```
Inventory (field on RunState, created in NewRun) is the ONE owner of item location:
  AddToBackpack(defId) → ItemInstance (RolledStats seeded from def base values)
  Hold(inst, protagonistId)   — swaps out the current held item (auto back to backpack)
  Unhold(inst) / Plant(inst, shelterId, slot) / Convert(inst, newDefId) / Remove(inst)
  Remove & Convert pass through CanRemove(inst) (Eternal gate — no Eternal items yet)
RunState.BenchProtagonist(id) — the ONLY place benching auto-returns a held item (T30 §4)
Arena: GameManager (after HydrateRun) applies held-item passives to the live character,
  sources keyed "item:<defId>#<instanceId>"; CharacterController raises ItemSkillRequested
  on F; GameManager gates domain → cooldown → executes data-driven effect; single-use ⇒
  Inventory.Convert + live passive re-application; GameManager._Process ticks the active
  holder's SecCdRemaining (arena clock only).
Day-end (T30 §5): step 3 ItemCds (held only) → step 4 Perish (held+backpack) →
  NEW step 4b PlantationGrow (before NpcWindows) → harvest rolls into backpack.
All item randomness: RunState.ItemsRng — a DetRandom created ONCE in NewRun as
  Rng.Sub("items") and consumed across the run (a fresh Sub("items") per call would
  replay the same sequence — see decision D-RNG below).
```

### 7.3 Locked decisions (doc-sync targets at ship time)

- **D-RNG.** `DetRandom.Sub(tag)` derives from the seed *string*, so calling
  `Rng.Sub("items")` repeatedly returns identical streams. RunState stores one
  `ItemsRng` at NewRun; every item roll consumes from it.
- **D-FIRE (OnFire "+0.2 s duration").** No duration field exists — OnFire is stack-decay
  (10%/0.2 s tick, `DecayingDebuff`). Mechanism: an aggregatable-by-source
  `_onFireDurationBonuses` dict on CharacterController; the summed bonus is handed to
  `DecayingDebuff` as a **decay-grace window applied once per activation** (inactive →
  active transition): decay ticking (and its self-damage) is suspended for that many
  seconds, extending the active window by exactly the bonus. Applied at activation, not
  per AddStack, so standing in a fire hazard can't re-arm it indefinitely. → ITEMS.gdd
  decisions log + KNOWLEDGE.md.
- **D-PERISH.** Perish ticks Held+Backpack only; Planted items grow, they don't rot.
  (No catalog item is both Plantable and Perishable; rule recorded for the future.)
- **D-HARVEST.** Fruit quantity rolled first (1–3); then ONE inheritance multiplier per
  fruit in [0.60, 1.40], applied to all inherited RolledStats (a "good" fruit is good
  overall). ITEMS §4.3 is ambiguous per-stat vs per-fruit; per-fruit chosen for coherence.
- **D-ROLLED.** `RolledStats` = absolute effective values keyed by modifier key; fresh
  instances copy the def's base passive values; Convert preserves the dict (this is how
  the Seed carries its father Bravery's stats). Readers fall back to def base when a key
  is missing.
- **D-PLANT.** Plantation availability derived deterministically per shelter:
  `new DetRandom(seed+":shelter:"+nodeId)` → 50% chance, 2 slots (constants in item
  data). Zone-6 XX shelters never have plantations (NODES §5.2). Watering/acceleration
  (Gameplay §B.4 "+1 day per watering") **deferred** to the Plantation GDD milestone —
  logged as TBD in ITEMS.gdd; the slice is plant → day-counted growth → auto-harvest to
  backpack at day-end.
- **D-VAMP.** Vamp heal hook lives where post-mitigation damage dealt is already
  attributed: `BaseFoe.TakeHit` (single-player attribution, same as ult-charge credit).
  Direct hits only — DoT/hazard damage does not vamp ("on every hit", Gameplay §A.1).
- **D-HEART.** FanChen's Heart heal triggers in `CharacterController.TakeHit` only
  (direct enemy hits) — not ApplyHazard, not DoT ("direct damage received from
  enemies"). Death check runs before the heal (0 HP = dead, no rescue).
- **D-F-KEY.** F wired in the arena only this milestone (no catalog item has a map
  skill); the def's domain flag is enforced (combat-gated). Map-side firing = TBD.
- **D-BGCD.** The ⅕-speed background CD tick for Tab-switched protagonists (Gameplay
  §A.3) deferred — party of one exists. TBD logged.
- **D-REWARD.** Mission/event `"placeholder"` item grants replaced: missions roll from
  `ItemRegistry.RewardPool` (Bravery, FanChen's Heart) using their own mission DetRandom;
  the abandoned-cart event grants a fixed `pome_bravery` (authored data).
- **D-FILES.** `ItemInstance.cs` stays at `Scripts/Run/` (run-layer state, namespace
  `Fableland.Run`), rebuilt in place; def/registry/inventory/passive types live in
  `Scripts/Items/` (namespace `Fableland.Items`).

### 7.4 Open questions (prototype defaults picked, review later)

- **Q11.** Item-skill CD display on the HUD — deferred (no HUD slot for the held item
  yet); the F press is silently ignored while on CD. Add an item icon + CD ring later.
- **Q12.** Build Team "poster" art — placeholder mugshot/player sprite reused; real
  posters per character GDD merch prompts later.
- **Q13.** Stat panel shows unconditional stats only; conditional passives (+5% vamp
  while OnFire) render in the item detail text, not the stat numbers.

### 7.5 Phase log

(appended as phases complete)


# IMPLEMENTATION_REPORT.md — Fableland full-project build

> Orchestration log for implementing the GDD suite (v0.4.0 foes → v0.5.0 node content →
> v0.6.0 items) on top of prototype 0 (v0.3.7). Maintained by the orchestrating session;
> updated after every phase so a fresh session can resume from here. Companion docs:
> `KNOWLEDGE.md` (caveats), `Docs/Tech/T30-FEATURE-BLUEPRINTS.md` (blueprints this build
> follows).

**Status: v0.6.0 landed as a DELIBERATELY-REDUCED STUB (debug protagonist selector §8 + minimal Team Build menu/item-catalog stub §9). v0.5.0/v0.5.1 landed and merged to main (04b8843).**

> **CORRECTION (2026-07-11):** §7 below describes the FULL wonder-items system as a
> planned build and its I1–I4 phase table shows "REVIEWED/APPROVED/DONE (committed)".
> **That does NOT reflect the working tree.** No item runtime was ever built or committed:
> `Scripts/Items/` does not exist, `Scripts/Run/ItemInstance.cs` is still the v0.5.0 stub
> (def-id only), nothing calls `RunState.AddItem` for a real item, and there is no
> shelter item/party UI. §7 is a stale aspirational plan — treat it as design notes, not
> a record of shipped code. The owner has since chosen to ship a **lightweight stub**
> instead of the full T30 §4 system for v0.6.0; that reduced milestone is tracked in §9.
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

---

## 8. Side-milestone — Debug protagonist selector + debug manual (started 2026-07-11, on `ver0.6.0-items` @ 0.5.3)

Owner-requested, independent of the items milestone. Goal: with debug mode ON, every
implemented protagonist (Pomegraknight, PumpKing) is playable for testing via a key-4
"protagonist building page" on the DebugManager autoload — WITHOUT touching the real
protagonist economy (`RunState.Owned`/`ActiveBuild`/`StartNewRun` seed stay
Pomegraknight-only). Plus `Docs/DEBUG_MODE.md`, the debug-tooling manual.

### 8.1 Phase table

| Phase | Scope | Agent | Status |
|-------|-------|-------|--------|
| D1 | `project.godot` input `debug_protagonist_page` (key 4), `Scripts/Debug/ProtagonistRoster.cs` (new), DebugManager page UI + selection state, GameManager swap/pending-apply + SkipRequested unsubscribe fix | engineer | DONE (2026-07-11) |
| D2 | Review (fableland-reviewer), orchestrator fixes | reviewer | DONE — APPROVE WITH FIXES; F7 applied |
| D3 | `Docs/DEBUG_MODE.md` + 40-QA cross-link, KNOWLEDGE caveats, version trio 0.5.4, Migration changelog | orchestrator | DONE except commit (not authorized this session) |

### 8.2 Integration contract (locked before code)

```
project.godot [input]: debug_protagonist_page = physical key 4 (keycode 52), mirrors
  debug_log_viewer (53). Handled ONLY while DebugManager.Enabled (unlike key 5).
ProtagonistRoster (Scripts/Debug/, static): (Id, ScenePath)[] — Pomegraknight,
  PumpKing; GetScene(id) lazy-loads. Identity key = scene root node Name == Id.
  Debug-layer registry; graduates/merges when the real protagonist-grant economy lands.
DebugManager: SelectedProtagonistId (string, null = scene default) persists on the
  autoload across scenes. Key 4 toggles the page (hand-built panel, BuildLogPanel
  style, Esc/X close; force-closed when debug toggles off). Selecting an entry stores
  the id, then tries a live apply: GetTree().CurrentScene is GameManager gm →
  gm.DebugSwapProtagonist(id) (bool). true ⇒ "swapped now", false/no-GameManager ⇒
  "queued — applies on next Arena entry". Direct call, NOT a C# event (see 8.3 D-DBG5).
GameManager:
  _Ready — after locating the authored player, if DebugManager.Enabled &&
    SelectedProtagonistId set && != player.Name: physically replace the node BEFORE
    SetupPlayer (no rewiring needed; SetupPlayer then wires the new body normally).
  DebugSwapProtagonist(id) — gated on DebugManager.Enabled; false when no live/alive
    player, unknown id, or debug match already ended (_ended). Mid-combat swap =
    WriteBackHp() → unsubscribe old HpChanged/Died → physical replace → SetupPlayer(rs)
    (rewires signals/Hud.SetPlayer/hydration exactly like boot; in-run the new body
    hydrates from Owned[0]'s ProtagonistState incl. carried HpRatio).
  Physical replace: capture GlobalPosition/parent; old.RemoveFromGroup("player")
    IMMEDIATELY (foes poll the group per-tick; QueueFree alone leaves the dying node
    in-group until frame end); disable old processing; QueueFree; instantiate; AddChild;
    restore position; new Camera2D.MakeCurrent(). LocalPlayer static self-heals via the
    existing _EnterTree/_ExitTree guards.
  _ExitTree — NEW: unsubscribe SkipRequested (pre-existing dangling-handler bug fix).
RunState: NOT touched by this feature — no read/write of Owned/ActiveBuild, no
  StartNewRun/GrantProtagonist change. HP write-back keeps targeting Owned[0]
  regardless of which debug body is worn (documented in DEBUG_MODE.md).
```

### 8.3 Locked decisions

- **D-DBG1.** Roster lives in `Scripts/Debug/` (debug-layer, not the real economy);
  scene-root `Name` is the id. Future characters append one line.
- **D-DBG2.** Outside the Arena, selection is queued on the autoload and applied at the
  next `GameManager._Ready` while debug is still ON — never an NRE, never a no-feedback
  silent success (page status label says which happened).
- **D-DBG3.** In-run debug swap carries current HP ratio (WriteBackHp → HydrateRun into
  the new body); ProtagonistState write-back still goes to Owned[0]. Debug body choice
  never leaks into run progression.
- **D-DBG4.** Turning debug OFF stops future applications (key 4 dead, pending not
  applied) but does not revert an already-swapped body.
- **D-DBG5.** Live apply is a direct `CurrentScene is GameManager` call rather than a
  new autoload C# event — the SkipRequested pattern was found to leak subscribers
  (scene-lifetime GameManager subscribing to an autoload-lifetime event without
  unsubscribe); this feature fixes that instance and does not add another.

### 8.4 Phase log

**D1 (engineer, 2026-07-11) — delivered per contract.** Files: `project.godot`
(`debug_protagonist_page`, physical_keycode 52, byte-mirrors key 5's entry),
`Scripts/Debug/ProtagonistRoster.cs` (new; lazy-cached `GetScene`, null for unknown,
no hand-made `.uid`), `Scripts/Debug/DebugManager.cs` (page UI mirroring
`BuildLogPanel` style, `SelectedProtagonistId`, key-4 gated on `Enabled`, shared Esc
one-panel-per-press, force-close on debug OFF, direct `CurrentScene is GameManager`
live-apply per D-DBG5), `Scripts/GameManager.cs` (pending apply at top of `_Ready`
before any wiring; `DebugSwapProtagonist` with 6 ordered guards →
WriteBackHp → unsubscribe → `ReplacePlayerNode` → `SetupPlayer(rs)` reuse;
`RemoveFromGroup("player")`-before-QueueFree; camera `MakeCurrent`; new `_ExitTree`
`SkipRequested` unsubscribe). Engineer deviations accepted: explicit
`Name.ToString()` for StringName/string comparison (defensive, no toolchain to
verify the implicit operator); `"> id <"` selection marker.

**D2 (reviewer, 2026-07-11) — verdict APPROVE WITH FIXES.** One MAJOR confirmed:
`ReplacePlayerNode` set `GlobalPosition` after `AddChild`, but
`CharacterController._Ready` (runs inside `AddChild`) had already latched
`_spawnPoint` at the character scene's own origin — a debug-lives `Respawn()` after
a swap would teleport to (0,0) (Arena's spawn position exists only as a scene
instance override). Passed: guard ordering, event hygiene (no double-subscription
across swaps), group timing vs foe per-tick polls, `LocalPlayer`/camera static
self-heal ordering, `_ExitTree` fix, determinism grep, RunState untouched, no
version/`.tscn`/`.uid` churn, ITEMS.gdd/report diffs not enlarged. One NOTE
(status-label wording deviates from brief's literal strings) — accepted as-is.

**Orchestrator fixes applied:**
- F7. **Swapped-in body respawn anchor** (reviewer MAJOR): new
  `CharacterController.SetSpawnPoint(Vector2)` called by `ReplacePlayerNode` right
  after positioning. KNOWLEDGE.md caveat added (v0.5.4, same class as the v0.4.0
  spawn-anchor caveat).
- (D1-scoped, orchestrator-directed) **SkipRequested subscriber leak** fixed in
  `GameManager._ExitTree`; KNOWLEDGE.md caveat added (v0.5.4).

**D3 (orchestrator, 2026-07-11):** `Docs/DEBUG_MODE.md` written (DBG/SKIP/key 5 log
viewer + `user://debug_log.txt` + categories/key 4 page/F9/R/`DebugFoeLevel`+
`DebugDay`/other harnesses), cross-linked from `40-QA.md` §1(4); 2 KNOWLEDGE
caveats; version trio **0.5.4** in sync (`GameVersion.cs`/`VERSION`/Hud
`VersionLabel`); `Migration.md` §0 changelog entry (plus back-filled one-liners for
0.5.1–0.5.3, which had shipped without entries). Static verification: brace/paren
balance clean on all 4 changed `.cs`; new scene paths resolve; input-action string
matches; determinism grep clean; no `.uid` invented. **No real build — no toolchain
on this host** (StringName comparison + `Camera2D.MakeCurrent` flagged for the next
`dotnet build` window). **Not committed — no user authorization this session**; the
pre-existing uncommitted `Docs/ITEMS.gdd`/report WIP is untouched and must not be
swept into any future 0.5.4 commit (`git add` explicit paths only, per the v0.5.3
caveat).

---

## 9. v0.6.0 — Minimal Team Build + item-catalog stub (started 2026-07-11, on `ver0.6.0-items` @ 0.5.4)

Owner-chosen **lightweight stub** (explicitly NOT the full T30 §4 / ITEMS.gdd wonder-items
economy). Ships: a small item catalog (id + display name only), a single held-slot on
`ProtagonistState`, two backpack↔held bookkeeping methods on `RunState`, and a first
**Team Build** overlay at the shelter — with a debug-mode-only display bypass so all
protagonists and all catalog items are pickable while testing. **NOT built this milestone
(remains future work, T30 §4):** passives, item skills, day/second cooldowns,
perish/convert/plant lifecycle, Possession/Eternal semantics, RolledStats, day-end item
hooks, bench auto-return, save/load. This is the version that actually lands as v0.6.0
(the §7 full-system plan is shelved).

### 9.1 Build strategy

One engineer phase (scope is small and cohesive), then reviewer, then an **independent
orchestrator double-check of the Team Build menu** (owner explicitly asked for
double-checking), then doc-sync + version trio bump to 0.6.0. No commit (not authorized).

### 9.2 Phase table

| Phase | Scope | Agent | Status |
|-------|-------|-------|--------|
| M1 | `Scripts/Items/ItemCatalog.cs` (new dir, 13-item id+name registry, no runtime fields); `ProtagonistState.HeldItemDefId`; `RunState.HoldItem/UnholdItem`; `Scenes/Shelter.tscn` Team Build button + `ShelterController` code-built overlay (roster + backpack, debug display union, assign/unhold) | engineer | DONE (2026-07-11) |
| M2 | Reviewer pass (GDD/blueprint conformance, KNOWLEDGE caveats, static checks) | reviewer | DONE — APPROVE WITH FIXES; 1 MAJOR (debug item materialization) |
| M3 | Orchestrator fix F-ITEM1 (provenance flag) + independent Team-Build double-check | orchestrator | DONE — fix applied, double-check PASSED |
| M4 | Doc-sync (ITEMS.gdd status framing, DEBUG_MODE item-visibility), KNOWLEDGE caveats, version trio 0.6.0, Migration changelog — no commit | orchestrator | DONE except commit (not authorized) |

### 9.3 Integration contract (locked before code)

```
ItemCatalog  (Scripts/Items/ItemCatalog.cs, NEW dir, namespace Fableland.Items, DOMAIN DATA)
  static (string Id, string DisplayName)[] Entries — the 13 named ITEMS.gdd §6.3 items.
  static string DisplayName(string id) → name, fallback to id for an unknown id.
  NO passive/skill/cooldown/tag/RolledStats fields — comment points at T30 §4 for those.
  NO `using Godot` (pure data, layer-law clean, matches FoeStats/MissionTable "table lives
  in its system folder" precedent AND the GDD's stated Scripts/Items/ target).

ProtagonistState (Scripts/Run/ProtagonistState.cs)
  + public string HeldItemDefId;   // null = empty held slot. ONE held slot (T30 §4 real design).

RunState (ProtagonistState is the held store; RunState is the single owner of `Items`)
  Items (backpack List<ItemInstance>) and AddItem() UNCHANGED.
  HoldItem(ProtagonistState p, string defId):
    - p null → no-op (null-tolerant).
    - remove ONE matching-DefId ItemInstance from Items IF present (real item leaves backpack);
      if absent (a debug-only catalog item), Items is left untouched — the display bypass.
    - if p.HeldItemDefId != null, push that previous defId back to Items (bump-out to backpack).
    - p.HeldItemDefId = defId.
  UnholdItem(ProtagonistState p):
    - p null or p.HeldItemDefId null → no-op.
    - push p.HeldItemDefId back to Items; p.HeldItemDefId = null.
  Signature takes a ProtagonistState REF (not an id) so the shelter can pass either a real
  Owned state OR an ephemeral debug-only state (PumpKing isn't in Owned) — RunState stays the
  single writer of Items either way, and Owned/ActiveBuild are NEVER mutated by this feature.
  NO cooldown/passive/perish/day-end side effects. One-line comment: bench auto-return
  (T30 §4) deferred until party-benching UI exists.

Shelter Team Build overlay (ShelterController, code-built Panel like DebugManager.BuildLogPanel/
  BuildProtagonistPanel; ONE new "Team Build" Button authored in Shelter.tscn's Box)
  Always available (free management action — not gated by Blessed/stamina); works debug OFF
  (the real, permanent shelter feature) and debug ON.
  Roster shown = RunState.Owned (real ProtagonistStates)
                 ∪ (DebugManager.Instance?.Enabled == true
                    ? ProtagonistRoster.Entries whose Id ∉ Owned  (ephemeral debug states)
                    : none).
    NEVER mutate RunState.Owned/ActiveBuild — display-layer union only. Debug-only
    protagonists' held items live in ShelterController-owned ephemeral ProtagonistStates
    (scene-lifetime; not persisted, never leak into the run economy).
  Backpack/assignable pool shown:
    - real section: each ItemInstance in RunState.Items (DisplayName), always assignable.
    - debug section (debug ON only): each ItemCatalog entry whose Id ∉ Items AND not
      currently held by any roster protagonist — additive DISPLAY bypass; the full catalog
      is NEVER bulk-written into RunState.Items.
  Interaction: select a protagonist (marker "> Name <") → click an assignable item → RunState.
    HoldItem(selectedState, defId); a per-protagonist "Return held to backpack" → RunState.
    UnholdItem(state). Panel refreshes from RunState after every action.
  Null-tolerant: RunState.Instance == null (F5 straight into Shelter) → empty roster/backpack
    with "(debug)" fallbacks, no NRE — same `rs?.` pattern ShelterController already uses.
```

### 9.4 Open questions (prototype defaults picked, review later)

- **Q-ITEM1 (RESOLVED — fixed in review, not accepted).** First pass had no real-vs-debug
  provenance, so a debug-conjured catalog item held-then-returned would materialize a real
  `ItemInstance` into `RunState.Items` and survive debug-off. The reviewer flagged this as MAJOR
  and it was fixed (F-ITEM1): `ProtagonistState.HeldItemFromBackpack` gates the return path so
  only real backpack items go back to `Items`; debug-conjured items vanish. The economy is now
  never polluted by the debug bypass. (Superseded my initial "accept it, items are inert"
  disposition — the owner asked for rigor and the invariant is now upheld exactly.)
- **Q-ITEM2.** Debug-only protagonists (PumpKing, not in `Owned`) get an ephemeral
  `ProtagonistState` owned by `ShelterController`, alive for the scene visit only. Closing/
  reopening the panel within the same shelter keeps it; leaving and re-entering the shelter
  resets debug held selections — acceptable for a debug affordance (never persisted, never
  in `Owned`).
- **Q-ITEM3.** Catalog `Id` strings are authored here for the first time (no prior canonical
  ids). Chose readable snake_case ids (e.g. `pome_bravery`, `the_void`) — a future real
  item system may re-key; the stub catalog is the only consumer today.

### 9.5 Phase log

**M1 (engineer, 2026-07-11) — delivered per contract.** Five files: NEW
`Scripts/Items/ItemCatalog.cs` (Godot-free `Fableland.Items` static registry, the 13
ITEMS.gdd §6.3 ids+names, `DisplayName(id)` id-fallback, `Contains(id)`); `ProtagonistState.
HeldItemDefId`; `RunState.HoldItem/UnholdItem` right after `AddItem` (Items/AddItem/NewRun/
ApplyRewards untouched); `Scenes/Shelter.tscn` one `TeamBuildButton` (no `uid=`);
`ShelterController` code-built Team Build overlay mirroring `DebugManager.BuildLogPanel`
(roster union of `Owned` + debug-only `ProtagonistRoster` via cached ephemeral states;
backpack = real `Items` + debug catalog display bypass; select-protagonist → assign → HoldItem,
per-row Return → UnholdItem; refresh after each; null-guarded throughout). Engineer decisions:
`[DBG]` label prefix on debug-catalog rows (visual source distinction); fixed row widths;
F5-no-run assign silently no-ops. Own static check clean (braces/parens balanced, GetNode path
matches new node).

**M2 (reviewer, 2026-07-11) — verdict APPROVE WITH FIXES.** One MAJOR confirmed: debug
catalog items materialize a permanent real `ItemInstance` into `RunState.Items` via the
asymmetric `HoldItem` (skips take-side removal for not-really-owned items) vs `UnholdItem`
(unconditional return-side `Items.Add`) — reachable in one click-pair on a REAL `Owned`
protagonist, surviving debug-off. Everything else conformed: pure-data catalog (no `using
Godot`, 13 names verbatim), single `HeldItemDefId` slot, `Items`/`AddItem`/reward paths
byte-for-byte unchanged, no T30 §4 machinery (correctly scoped stub), button free/not day-
ending, roster/backpack union exact, `Owned`/`ActiveBuild` never mutated, closure capture +
QueueFree-rebuild + `bool?` compare all correct, null-tolerance traced. NOTEs: `ItemCatalog.
Contains` unused (harmless public helper, kept); Godot-4 API calls precedented in
`DebugManager` but compile-unverified (no toolchain).

**Orchestrator fix applied:**
- **F-ITEM1 (reviewer MAJOR).** Added `ProtagonistState.HeldItemFromBackpack` (provenance).
  `RunState.HoldItem` now takes `bool fromBackpack`: it only removes from `Items` when true, and
  its bump-out only returns the *previous* held item to `Items` if that item was itself from the
  backpack. `RunState.UnholdItem` returns to `Items` only when `HeldItemFromBackpack`; a debug-
  conjured item vanishes and clears the flag. `ShelterController.AssignItem(defId, bool
  fromBackpack)` passes `true` for real `Items` rows, `false` for `[DBG]` catalog rows. Result:
  the debug bypass can never create a real item — round-trips are conservative. KNOWLEDGE.md
  caveat added (v0.6.0). Static re-check: no stale 2-arg call sites; braces/parens balanced on
  all touched files.

**M3 (orchestrator independent double-check of the Team Build menu, 2026-07-11) — PASSED.**
Traced by reading the post-fix code (not the reviewer's word):
- Shelter loads → `TeamBuildButton` (`Center/Box/TeamBuildButton`, wired in `_Ready`, not
  disabled by `Refresh()`) → `OpenTeamBuild()` positions/refreshes/shows the panel; X = hide.
- **Debug OFF, fresh run** (`Owned`=[Pomegraknight], `Items`=[]): roster shows Pomegraknight
  only, held "(empty)", Return disabled; backpack empty; nothing to assign. Correct.
- **Debug ON, fresh run:** roster = Pomegraknight (real) + PumpKing (ephemeral, from
  `ProtagonistRoster`, NOT added to `Owned`); backpack = 0 real + 13 `[DBG]` catalog rows.
- **Assign `[DBG] Pome's Bravery` to Pomegraknight:** `HoldItem(ownedState,"pome_bravery",
  false)` → nothing removed from/added to `Items`; `HeldItemDefId="pome_bravery"`,
  `HeldItemFromBackpack=false`. Roster shows it held; catalog now shows 12 (held id excluded).
  `ProtagonistState.HeldItemDefId` updated, item removed from backpack-display. Correct.
- **Return it:** `UnholdItem` → `HeldItemFromBackpack==false` ⇒ vanishes, `Items` STILL empty,
  slot cleared, item reappears in the 13-row `[DBG]` catalog. Perfect reversal, zero economy
  pollution (this is the F-ITEM1 fix working).
- **Real backpack item path** (e.g. a reward `Items`=[pome_seed], debug OFF): assign (fromBackpack
  true) removes it from `Items` → held; Return adds it back to `Items`. Correct round-trip.
- **Mixed bump-out** (real held, then assign a `[DBG]` item): real item bumped back to `Items`,
  debug item held without pollution; assigning the real one back vanishes the debug one. Correct.
- **Close/reopen (same scene):** panel hidden not freed; `_debugStates` + `RunState`/`Owned`
  persist; refresh rebuilds — no state loss.
- **F5-direct-into-Shelter:** `RunState` is an autoload (project.godot:21) ⇒ `Instance` non-null,
  `Owned`/`Items` empty; menu is fully functional (debug items assign to ephemeral states, never
  touch `Items`); the `?.`/`rs?.` guards additionally make it NRE-safe even if the autoload were
  ever absent. No NRE on any path (roster/backpack build, assign, unhold, `_nodeId` init).
- **Non-invasiveness:** grep-confirmed no writes to `RunState.Owned`/`ActiveBuild`; the debug
  catalog is never bulk-written into `Items`; `RunState` remains the sole `Items` writer.
Conclusion: the Team Build menu behaves correctly in every enumerated scenario; the single
review finding is fixed and re-verified. **No real build — no Godot/.NET toolchain on this host**
(the Godot-4 UI API calls are precedented byte-for-byte in `DebugManager` but remain compile-
unverified; flagged for the next `dotnet build` window alongside the standing v0.5.4 items).

**M4 (orchestrator, 2026-07-11):** doc-sync (ITEMS.gdd status framing clarified — only the stub
landed; DEBUG_MODE.md §"item visibility" added), KNOWLEDGE.md v0.6.0 caveat, version trio bumped
to **0.6.0** (`GameVersion.cs`/`VERSION`/Hud `VersionLabel` in sync), `Migration.md` §0 changelog
0.6.0 entry. **Not committed — no user authorization this session.** The uncommitted v0.5.4
debug-mode WIP (§8) is carried into the same working tree; when a commit is authorized, both
land together as v0.6.0 with explicit `git add` paths (never `git add -A`, per the v0.5.3 caveat).

---

## 10. v0.6.1–v0.6.5 — Protagonist switching + background CD + HUD + debug day + bug fixes (2026-07-12, on `main`)

User session implementing the mid-combat protagonist-switch mechanic (NODES §3.3) plus
accompanying systems, HUD polish, and bug fixes. All work done by Claude in the IDE;
five version bumps, each static-verified (no Godot toolchain on this host).

### 10.1 v0.6.1 — Tab protagonist switching

**Input:** `switch_protagonist` action bound to Tab key in `project.godot`.

**RunState.cs:**
- `ActiveProtagonistIndex` — which party slot is controlled.
- `NewRun()` now seeds `Owned` + `ActiveBuild` with **both** Pomegraknight and PumpKing.
- `CycleNextProtagonist()` — wraps the index around; returns null when party < 2.
- `FindProtagonist(id)` — looks up a `ProtagonistState` from `Owned`.

**CharacterController.cs:**
- `WriteBackToState(ProtagonistState)` — saves HP ratio to the run copy.
- `InheritVelocityFrom(CharacterController)` — copies intent+external velocity channels
  so a mid-air switch continues the trajectory (NODES §3.3).
- `SaveCooldownsToState` / `LoadCooldownsFromState` — virtual hooks (base no-ops).

**GameManager.cs:**
- `_activePartyIndex` / `_switchCd` / `SwitchCooldown = 12f`.
- `SetupPlayer()` resolves the `ProtagonistState` from `ActiveBuild[_activePartyIndex]`
  instead of hardcoding `Owned[0]`; hydrates HP + ATK + DEF from the correct state.
- `_Process()` checks `switch_protagonist` input each frame, ticks `_switchCd`.
- `TrySwitchProtagonist()` — guards (dead, CD, party < 2, no run) → saves outgoing HP
  ratio → carries it to the incoming state (NODES §3.3 "HP ratio inheritance") →
  `CycleNextProtagonist` → `ReplacePlayerNode` → `SetupPlayer` → reset CD.
- `ReplacePlayerNode()` calls `InheritVelocityFrom(old)` before QueueFree.
- `WriteBackHp()` delegates to `_player.WriteBackToState(_protagonist)`.

**Pomegraknight.cs / PumpKing.cs:**
- Override `SaveCooldownsToState` / `LoadCooldownsFromState` — persist `_blushCd`/`_eCd`
  and `_soulCd`/`_pumpCd` respectively.

**Hud.cs:**
- `SetPlayer()` now **unsubscribes** from the previous player's `UltChargeChanged` before
  subscribing to the new one (same bug class as the v0.5.4 autoload-event leak).

**Hud.tscn / Hud.cs:**
- `NextMugshot` TextureRect added below the ItemSlot; `SetNextProtagonist(id)` loads
  placeholder art (pomegranate SVG for Pomegraknight, pumpkin SVG for PumpKing).
- `UpdateNextMugshot()` in GameManager pushes the next party member's ID on setup + switch.

### 10.2 v0.6.2 — Background CD formula 1/(2n−2)

Per user spec replacing the flat ⅕-speed penalty: benched protagonists' skill cooldowns
recover at rate `1/(2n−2)` where n = party size:
- 2-party → 0.5× (6s CD → 12s real)
- 3-party → 0.25× (6s → 24s), 4-party → 0.167× (6s → 36s)

**ProtagonistState.cs:** added `ShiftCdRemaining` / `ESkillCdRemaining` fields.

**GameManager.cs:**
- `TrySwitchProtagonist()` saves outgoing cooldowns via `_player.SaveCooldownsToState()`.
- `SetupPlayer()` restores incoming cooldowns via `_player.LoadCooldownsFromState()`.
- `TickBackgroundCooldowns(dt)` — ticks all benched protagonists' stored cooldowns at
  the reduced rate each frame. Runs regardless of mission state (so CDs recover between
  mission end and Finish the Day).

**NODES.gdd §3.3:** updated from "⅕ speed" to the new formula.

**Also in v0.6.2:**
- **Seagull contact damage:** `HasContactDamage = true`, `BaseDamage = 20f` (was 18).
  Seagulls now deal contact damage on touch using the existing `BaseFoe.TryContactDamage`.
- **Debug day change (`[` / `]`):** in debug builds, bracket keys decrement/increment
  `RunState.Day` (in a run) or `GameManager.DebugDay` (no run), with 0.35s debounce.
  Logged via DebugManager.

### 10.3 v0.6.3 — SwitchSlot CD overlay styling

The flat `NextMugshot` TextureRect was replaced with a proper **SwitchSlot** Control
matching the Shift/E/Item slot pattern:

```
SwitchSlot (Control)
├── Icon (TextureRect)        — next protagonist's mugshot
├── CooldownOverlay           — radial fill (ui_cd_overlay.svg, fill_mode=4)
└── CdLabel (Label)           — seconds remaining, centered
```

**Hud.cs:** `SetSwitchCooldown(remaining, max)` updates the radial overlay, label, and
**dims the mugshot** (`SelfModulate` → 35% gray) while the CD is active.
GameManager pushes switch-CD state via new `PushSwitchHud()` every frame.

### 10.4 v0.6.4 — Debug day editor on the map

Replaced the `[`/`]` arena keys (v0.6.2) with a **DayEditor** UI strip on the map view,
visible only when debug mode is on.

**Map.tscn:** `DayEditor` HBoxContainer added between InfoLabel and FinishDayButton:
```
Day: [ 12 ]  [-5] [-1] [+1] [+5]
```
A LineEdit shows the current day (press Enter to set), flanked by four compact buttons.
Subsequent buttons shifted down 34px.

**MapController.cs:** `_Process` checks `DebugManager.Enabled` each frame; toggles
visibility. `AdjustDay(delta)` writes to `RunState.Day` (clamped ≥1), refreshes both
InfoLabel and LineEdit. `OnDayTextSubmitted` parses typed input.

**GameManager.cs:** removed the `[`/`]` day-change block and `_dayDebounce` field.

### 10.5 v0.6.5 — Grace period, foe invincibility, seagull dash fix

**3-second grace period:** `GameManager._graceTimer` starts at 3.0s on arena load.
During grace the HUD shows "Get ready... 3" → "2" → "1" → "Go!"; `Spawner.Enabled`
is false; `_mission.Tick()` is skipped (mission timers frozen). At the end the
spawner is armed and the mission clock starts.

**Foe invincibility on mission end:** `BaseFoe.Invincible` (public bool) guards
`TakeHit()` and `ApplyHazard()` — when set, all damage/knockback/stun/flash/ult-charge
is suppressed. `GameManager.OnMissionResolved()` sets it on every living foe in group
`"foe"` so the player can't farm kills after the objective resolves.

**Seagull dash toward player:** `_dashDir` changed from `float` (X-sign only, horizontal
swoop) to `Vector2` (full direction toward the player, locked at telegraph start).
The seagull's `collision_mask = 4` (Ground only) already ensures the dash passes through
platforms and soft volumes; only the ground blocks it. Knockback on hit also uses the
dash direction vector.

### 10.6 File inventory

| File | Change |
|------|--------|
| `project.godot` | +`switch_protagonist` input action (Tab) |
| `Scripts/Run/RunState.cs` | `ActiveProtagonistIndex`, `CycleNextProtagonist`, `FindProtagonist`, 2-protagonist init |
| `Scripts/Run/ProtagonistState.cs` | `ShiftCdRemaining` / `ESkillCdRemaining` |
| `Scripts/CharacterController.cs` | `WriteBackToState`, `InheritVelocityFrom`, `SaveCooldownsToState` / `LoadCooldownsFromState` (virtual) |
| `Scripts/GameManager.cs` | Tab switching, background-CD ticking, switch-CD HUD push, 3s grace, foe invincibility, `UpdateNextMugshot` |
| `Scripts/Pomegraknight.cs` | Override `SaveCooldownsToState` / `LoadCooldownsFromState` |
| `Scripts/PumpKing.cs` | Override `SaveCooldownsToState` / `LoadCooldownsFromState` |
| `Scripts/Hud.cs` | `SetPlayer` event-leak fix, `SetNextProtagonist`, `SetSwitchCooldown`, SwitchSlot |
| `Scenes/Hud.tscn` | SwitchSlot (Icon + CooldownOverlay + CdLabel), 2 new ext_resources |
| `Scripts/Map/MapController.cs` | Debug day editor (DayEditor +/- buttons + LineEdit) |
| `Scenes/Map.tscn` | DayEditor HBoxContainer, buttons shifted down |
| `Scripts/Foes/BaseFoe.cs` | `Invincible` property guarding `TakeHit` + `ApplyHazard` |
| `Scripts/Foes/SeagullFoe.cs` | Contact damage on (20), `_dashDir` → Vector2 (toward player) |
| `Docs/NODES.gdd` | §3.3 cooldown penalty updated to 1/(2n−2) formula |
| `Sprites/UI/mugshot_next_pomegraknight.svg` | Placeholder pomegranate mugshot |
| `Sprites/UI/mugshot_next_pumpking.svg` | Placeholder pumpkin mugshot |
| `Scripts/GameVersion.cs` / `VERSION` / HUD+Map VersionLabels | 0.6.0 → 0.6.5 |

---

## 11. v0.6.7 — Map-creation rework (combat-map builder) (started 2026-07-11, on `main` @ 0.6.6)

Full rework of `Scripts/MapCreation/` + `Scenes/MapCreation/` per the newly-approved
**`Docs/MapCreation.gdd`** (canon; uncommitted in the working tree together with the
`Docs/IDEAS.md` §6 additions — both fold into this milestone's commit UNMODIFIED).
Scope locked by design lead: map browser, map editor, data model, tile registry, rule
tiles + in-editor "Preview generation" (GDD §0/§12). **NOT in scope:** runtime arena
instantiation (GDD §9 contract-only, next milestone); `Scripts/Map/` (roguelike
overworld — unrelated system, do not touch).

### 11.1 Build strategy

Demolition first (old module was broken end-to-end: saves were `{}`, canvas drew behind
its background, duplicate names overwrote each other), then five engineer phases in
dependency order, one full-diff reviewer pass, orchestrator fixes, ship. Commit IS
authorized this session (explicit `git add` paths only, per the v0.5.3 caveat).

**Demolition list** (replaced, no migration — old saved maps are worthless `{}`):
`Scripts/MapCreation/CustomMapData.cs`, `MapSaveLoad.cs`, `MapBrowser.cs`,
`MapEditor.cs` (+ their `.cs.uid` sidecars); `Scenes/MapCreation/MapBrowser.tscn`,
`MapEditor.tscn` rebuilt as thin roots (fabricated `uid="uid://..."` header strings
removed per KNOWLEDGE v0.5.0; Menu.tscn's fabricated header uid removed in passing).
Entry point stays stable: Menu "Map Creation" button → `MenuController.OnMapCreation` →
`ChangeSceneToFile("res://Scenes/MapCreation/MapBrowser.tscn")`.

### 11.2 Phase table

| Phase | Scope | Agent | Status |
|-------|-------|-------|--------|
| MC1 | Demolition + `Scripts/MapCreation/Data/` domain layer: MapDocument/MapLayerData/PlacedTile/ShapeDef, TileDef/TileRegistry (starter set), LayerOccupancy, RuleResolver, MapJson (+ round-trip self-check) | engineer | DONE (2026-07-11, orchestrator spot-check passed) |
| MC2 | MapBrowser: `Scenes/MapCreation/MapBrowser.tscn` thin root + `Editor/MapBrowser.cs` (dir-scan cards, Create/Open/Rename/Duplicate/Delete, GUID identity), `Editor/EditorLaunch.cs` handoff | engineer | DONE (2026-07-11, orchestrator spot-check passed) |
| MC3 | MapEditor shell: `MapEditor.tscn` thin root + `Editor/MapEditor.cs` (top bar/tool rail/status bar/panel scaffolds), `Editor/EditorState.cs`, `Editor/GridView.cs` (pan/zoom/grid/LOD/canvas/layer-focus/sketchlines/rule hatching/effect areas), `Editor/CommandStack.cs`, `mapedit_*` InputMap actions in project.godot | engineer | DONE (2026-07-12, orchestrator spot-check passed) |
| MC4 | `Editor/Tools/` (Paint/Erase/Rect/Marquee/Lasso/Move/Eyedropper/Bucket + TileBatchCommand/MoveCommand), selection/clipboard shortcuts (§7.3), ghost previews, paste mode | engineer | DONE (2026-07-12, +1 orchestrator fix F-MC1) |
| MC5 | Layer panel (add/remove/reorder/props with §3 constraint enforcement), palette by category, Preview-generation button (RuleResolver + scratch seed), map properties (canvas color, battlefield WxH) | engineer | DONE (2026-07-12) |
| MC6 | Full-diff reviewer pass → orchestrator triage/fixes | reviewer | DONE (2026-07-12) — APPROVE WITH FIXES; 1 BLOCKER + 1 MAJOR (adjudicated false positive) + 3 minors; fixes F-MC2..F-MC5 applied |
| MC7 | Ship: static verification (40-QA §1), KNOWLEDGE caveats, version trio 0.6.7, Migration §0 changelog, commit (authorized) | orchestrator | DONE (2026-07-12) |

### 11.3 Integration contract (locked before any code)

```
NAMESPACES / LAYERS (T00 law)
  Fableland.MapCreation.Data   (Scripts/MapCreation/Data/)   — DOMAIN DATA. ZERO `using Godot`
    per file. May reference: Units (foundation; the file itself needs no Godot using),
    Fableland.Map.DetRandom (domain peer), System.Text.Json. Colors = hex strings
    ("#RRGGBB" or "#RRGGBBAA"); shapes = plain ShapeDef struct (kind rect|circle|polygon,
    px relative to the anchor cell's top-left).
  Fableland.MapCreation.Editor (Scripts/MapCreation/Editor/ + Editor/Tools/) — PRESENTATION.
    All Godot usage lives here (GD.PushWarning surfacing, ProjectSettings.GlobalizePath,
    Controls, drawing).

SERIALIZATION (GDD §8 — the STJ-fields bug is structural, not stylistic)
  Every serialized type: public PROPERTIES with { get; set; } — never public fields.
  Every serialized type carries [JsonExtensionData] public Dictionary<string, JsonElement>
    Extra { get; set; } (unknown fields preserved on rewrite, T20 §5).
  MapDocument = { Version:1, Id (GUID string, minted at creation, NEVER name-derived),
    Name, World, CreatedUtc, ModifiedUtc, Canvas { Type:"solidColor", Color:"#87CEEB",
    ModeId:null }, Layers: [MapLayerData] } — canvas is NOT a layer; Layers is
    back-to-front draw order (farview…, battlefield, closeview…), exactly one
    Role=Battlefield (missing on load ⇒ inject default 64×36 + warning).
  MapLayerData per GDD §1.2 (+ Role string, Tiles: [PlacedTile{DefId,X,Y,Props?}]).
    Sparse tiles, anchor-only for multi-cell footprints.
  MapJson (Data/, pure): Save(doc, absolutePath) — serialize INDENTED, write temp file in
    the same dir, File.Move(temp, final, overwrite:true) (atomic rename); Load(absolutePath,
    out List<string> warnings) — null on missing/corrupt (caller degrades to empty +
    GD.PushWarning); unknown TileDef ids on load → tile SKIPPED + warning string (Editor
    layer surfaces all warnings via GD.PushWarning). Version-switched loader from byte one.
  MapJson.RoundTripSelfTest() (pure, returns failure strings; empty = pass): builds a
    representative MapDocument (multi-layer, multi-cell tiles, rule tiles, props), saves to
    a temp path, loads, deep-compares. MapBrowser._Ready runs it when OS.IsDebugBuild()
    and GD.PushError-s failures — the headless guard the GDD demands.

FILE STORE (Editor layer)
  Directory: user://maps (globalized via ProjectSettings.GlobalizePath), created on demand.
  One file per map: user://maps/<guid>.json. NO _index.json — browser lists by directory
  scan, reading each file's meta via MapJson.Load (corrupt file ⇒ skip + warning card-less).
  Rename/Duplicate: rename = metadata edit (same file); duplicate = fresh GUID + " (copy)".

SCENE HANDOFF (browser ⇄ editor; scene changes can't carry args)
  Editor/EditorLaunch.cs: static string MapId (null ⇒ editor F5-launched: open a NEW
  unsaved default document — the T10 §3 null-tolerant debug rule). Browser sets it, then
  ChangeSceneToFile(MapEditor.tscn); editor Back button → ChangeSceneToFile(MapBrowser.tscn)
  (unsaved-changes confirm first). MenuController untouched.

EDITOR STRUCTURE (the draw-behind-children bug is structural, GDD §7.6)
  MapEditor.tscn root (thin Control) builds in code, child order bottom→top:
    Background ColorRect (canvas color, mouse_filter IGNORE)
    GridView (dedicated child Control — ALL world drawing: grid, tiles, ghosts, dimming,
      sketchlines, rule-zone hatching, effect-area outlines, preview overlay; owns
      _pan/_zoom manual screen-space transform, NO Camera2D; wheel-zoom to cursor clamped
      0.25–4.0; middle-drag or Space+left-drag pan; grid lines only across the visible
      rect, 8-cell majors, fine lines fade out approaching min zoom — alpha lerp 0 at
      ≤0.25 → full at ≥0.5)
    UI panels (top bar, left tool rail, right layer+palette panels, bottom status bar).
  The editor root's own _Draw stays EMPTY forever.
  EditorState (one owner): Document, CurrentLayerIndex, ActiveTool, BrushDefId,
    Selection (HashSet of PlacedTile refs; count shown in status bar), clipboard,
    pan/zoom, toggles (grid on, effect areas off, preview off), dirty marker
    (= CommandStack position vs last-saved mark). All mutations to Document go through
    CommandStack commands; GridView/panels read EditorState and QueueRedraw on change.
  CommandStack: IEditorCommand { Do(); Undo(); }, capacity 200 (drop oldest), one
    mouse-down→up stroke = ONE batched command; layer add/remove/reorder/property edits
    are commands too.
  Occupancy: Data/LayerOccupancy builds Dictionary<(x,y) → PlacedTile> per layer from
    sparse tiles + footprints; placement rejected (red ghost) on any overlap; erasing any
    occupied cell removes the whole tile; footprints never leave the layer grid.

DETERMINISM (GDD §6; T00 rule 5 — RuleResolver takes DetRandom IN)
  RuleResolver.Resolve(MapDocument doc, TileRegistry reg, DetRandom rng)
    → List<ResolvedSpawn { LayerIndex, DefId, X, Y, Tags[] }>. Pure, no Godot.
  Zones: contiguous same-rule-id cells on the same layer, 4-connectivity flood fill;
    zone anchor = its lexicographically smallest (y, then x) cell; zones resolve sorted
    by (layerIndex, anchorY, anchorX); per-zone child stream =
    rng.Sub("zone:" + layerIndex + ":" + anchorX + ":" + anchorY) — DetRandom.Sub derives
    from the seed STRING (the project's hash(seed,…) idiom), so adding a zone never
    reshuffles the others.
  Per zone: count = zoneRng.Range(countMin, countMax); candidate anchors = zone cells
    shuffled by zoneRng; accept iff footprint ⊆ zone cells AND footprint+reserve overlaps
    no occupied/reserved cell (occupied = layer's real tiles + prior spawns); reserve rect
    = ReserveW×ReserveH centered on the footprint (rounding toward top-left); stop at
    count or 4×count attempts (cramped zone degrades gracefully — fewer spawns; editor
    surfaces a warning in debug).
  Runtime seed (next milestone) = mapId + ":" + nodeSeed handed in by RunState; the
    EDITOR's Preview-generation button uses a scratch seed from DetRandom.NewSeed()
    (editor-only affordance — the sanctioned non-deterministic mint), re-rolls on
    re-press, Esc clears; preview spawns render semi-transparent.

TILE REGISTRY (T20 §1 — adding a tile kind = adding an entry, never a switch)
  TileRegistry: code-defined, read-only after boot, Dictionary<string id, TileDef>;
  TileDef per GDD §2.2 (Category enum Ground/Platform/SoftVolume/Hazard/EnemySpawn/
  Respawn/LevelGoal/Character/Decoration/Rule/Misc; AllowedRoles [Flags] over LayerRole
  Farview/Battlefield/Closeview; FootprintW/H default 1×1; EditorColor hex; SpriteSlot +
  AutotileGroup reserved strings; EffectArea ShapeDef? null = footprint rect; typed
  RuleProps? for Rule tiles {SpawnTable[{DefId,Weight}], CountMin, CountMax, ReserveW,
  ReserveH, Tags[]}; Props dict for misc extras).
  Starter set: ground.grass, ground.stone; platform.wood, platform.vine;
  softvolume.bush1x1, softvolume.cloud1x1, softvolume.cloud2x1 (2×1); hazard.bonfire,
  hazard.freezepit; spawn.enemy (EnemySpawn, props foeTable), spawn.respawn (Respawn),
  goal.level (LevelGoal), spawn.character (Character); deco.flower, deco.rock
  (Decoration, any role); rule.cloudZone (Rule, battlefield+farview: spawnTable
  cloud2x1×3 / cloud1x1×1, count 2..4, reserve 3×4, demonstrating spawnTable/count/
  reserve per GDD §6's worked example).
  Cell size everywhere = Units.PixelsPerMeter (32 px = 1 m) — never a literal.

LAYER RULES enforced by the editor (GDD §1.2/§3/§4)
  Battlefield: exactly 1, parallax locked (1,1), loop false, collision true, sway 0,
    gridW/H = map bounds (default 64×36, max 512×256).
  Farview 0..8: collision checkbox enabled ONLY when parallax == (1.0,1.0) AND !loop
    (greyed with tooltip otherwise); loop layers never collide.
  Closeview 0..2: collision never, sway forced 0, parallax default 1.2, tint default
    #404050. Sway render-only everywhere.
  Layer focus render: current layer 100% + 1.5 px sketchline outline around contiguous
    tile groups (per-layer accent color); every other layer 35% opacity, NEVER 0.
```

### 11.4 Open questions / prototype defaults (decided by orchestrator, review later)

- **Q-MC1.** GDD §7.8 card shows "world" but no world-selection UX exists → `World`
  is a free string on MapDocument meta, default `""` (card renders "—"); not editable
  in v1 UI. Revisit when worlds/registries meet the runtime milestone.
- **Q-MC2.** "Map properties" surface (canvas color, battlefield WxH) is not given a
  home by §7.1 → folded into the layer panel: selecting the Canvas or Battlefield row
  shows its properties (color picker / WxH spinners). Cheapest honest reading of §5
  "editable in map properties".
- **Q-MC3.** Paste semantics unspecified (§7.3) → paste stamps like Move's drop:
  overwrites target cells (removes overlapped tiles) — one consistent rule for both
  "place a group" verbs.
- **Q-MC4.** Grid LOD "fades below 25%" vs zoom clamp min = 25% → implemented as a
  fade band: fine-grid alpha lerps from 0 at ≤0.25× to full at ≥0.5×; majors always on.
- **Q-MC5.** No uniqueness rule for Character/Respawn/LevelGoal tiles in v1 (GDD is
  silent); occupancy is the only constraint. Runtime milestone must validate.
- **Q-MC6.** Selection is whole-tile (set of placed tiles, not cells): marquee/lasso
  select any tile with a footprint cell inside the region; status-bar count = tiles.
- **Q-MC7.** Shrinking a layer's GridW/GridH below existing tiles doesn't clip or
  warn in-editor; the out-of-bounds tiles are skipped (with a warning) by
  `MapJson.Load`'s validation on next load. Acceptable v1 self-heal; an in-editor
  clip-confirm dialog is a future polish item.

### 11.5 Phase log

**MC1 (engineer, 2026-07-11) — delivered per contract.** Deleted the 4 old scripts +
their `.cs.uid` sidecars (`git rm`, staged); old `.tscn`s left for MC2/MC3 rebuild.
Created the 9 `Data/` files (namespace `Fableland.MapCreation.Data`, zero `using
Godot`, properties-only + `[JsonExtensionData]` on all serialized types, 18-def
starter registry incl. `rule.cloudZone` = GDD §6's worked example verbatim,
`MapJson.RoundTripSelfTest()` deep-compare guard). Also pre-added the STJ-fields
KNOWLEDGE caveat (v0.6.7). Orchestrator spot-check of MapJson/RuleResolver/model/
registry/occupancy: conforms — determinism draw order documented (count → shuffle →
per-attempt def roll), per-zone `rng.Sub("zone:L:x:y")` streams, stable zone sort,
atomic temp+rename save, unknown-id/out-of-grid skip + warnings-out (no Godot).
Accepted engineer decisions: occupancy helpers as instance methods; odd reserve
margin rounds to top-left; candidate cycling via modulo until 4×count attempts
(later attempts can succeed — smaller def rolled); Rule-tile cells excluded from
"real" occupancy (required for the algorithm to place anything); `CanvasData` lives
in `MapDocument.cs`.

**MC2 (engineer, 2026-07-11) — delivered per contract.** `Editor/EditorLaunch.cs`
(static MapId handoff), `Editor/MapBrowser.cs` (code-built browser: user://maps dir
scan via GlobalizePath, corrupt-file skip + PushWarning, ModifiedUtc-descending card
sort by ordinal ISO-8601 compare, cards show name/world/battlefield WxH/date,
Create/Open/Rename/Duplicate/Delete with reused code-built dialogs, GUID identity —
rename never moves the file, duplicate reloads from disk + fresh GUID; debug-build
boot validation wires `MapJson.RoundTripSelfTest()` + `TileRegistry.Validate()` to
GD.PushError). `MapBrowser.tscn` rewritten as thin root, no `uid=`. Orchestrator
spot-check passed; one cosmetic nit deferred to the MC6 full review (LineEdit added
as direct ConfirmationDialog child may overlap the dialog label).

*(Session interruption 2026-07-11→12: the first MC3 agent died to a usage limit
before writing anything; working tree audited on resume — MC1/MC2 intact — and MC3
re-run clean.)*

**MC3 (engineer, 2026-07-12) — delivered per contract.** `CommandStack.cs` (cap 200
oldest-drop, saved-marker with −2 unreachable latch, `Changed` event, Godot-free),
`EditorState.cs` (one owner: doc/layer/tool/brush/selection/clipboard/toggles/
preview/commands, occupancy cache + `InvalidateOccupancy`, `StateChanged`,
`AccentOf`), `GridView.cs` (531 lines: manual `_pan`/`_zoom` no-Camera2D, wheel-zoom-
to-cursor 0.25–4×, middle/Space pan, visible-rect-only rendering, 35%-never-0 layer
dimming, current-layer sketchline via boundary-edge tracing, rule-tile hatching,
effect-area outlines, preview + selection render paths, MC4 cell-event plumbing),
`MapEditor.cs` (407 lines: CanvasBg → GridView → UI child order, root `_Draw` absent
with doc-comment, computed "Cell = 32 px = 1 m" from Units, tool rail ButtonGroup,
status bar cell/sel/dirty-dot, `_UnhandledKeyInput` shortcuts with redo-before-undo
ordering, save/back + discard confirm). `MapEditor.tscn` thin root no uid;
project.godot +24 `mapedit_*` actions (physical_keycode style matching existing
entries; `command_or_control_autoremap` on Ctrl/Cmd combos per GDD §7.3; all 12
pre-existing actions untouched — verified). Accepted engineer decisions: default
current layer = first battlefield; initial pan/zoom = origin/1×; rule-hatch spacing
in world px (scales with zoom).

**MC4 (engineer, 2026-07-12) — delivered per contract.** New `Editor/Tools/`:
`ToolBase.cs` (abstract tool + `GhostInfo` + `ToolRegistry` + role-legality helper),
`Commands.cs` (`TileBatchCommand` reused by paint/erase/rect/bucket/delete/cut/paste;
`MoveCommand` mutates the same PlacedTile X/Y in place so the reference-identity
Selection survives a move), and the 8 GDD §7.2 tools. GridView gained `GhostProvider`
(Func<GhostInfo>) + `HoverCell` + `DrawGhost()` (above selection, below grid lines).
MapEditor routes GridView cell events to the active tool, deactivates the previous
tool on switch, and adds select_all/deselect/delete/cut/copy/paste handling; paste is
a modal `_pasteModeActive` flag (ghost follows cursor, click stamps ONE overwrite
command per Q-MC3, Esc exits; Esc priority = paste-cancel → preview-clear →
deselect+drag-cancel). `EditorState.CurrentLayerIndex` setter now clears Selection
on layer change (centralized invariant). Accepted engineer decisions: Alt beats
Shift when both held on marquee/lasso; clipboard anchor = min-(Y,X) tile; undo of
delete/cut does not re-select; rect/bucket footprint tiling steps from the region's
top-left; single-bool ghost validity for multi-tile ghosts; whole-move aborts if any
moved tile would leave the grid (vs paste's per-entry skip — move preserves a rigid
group, paste is a stamp).

**Orchestrator fixes applied (MC4):**
- **F-MC1.** `TileBatchCommand.Do()` removed tiles from the layer but not from the
  live `Selection` — erase/bucket/paste-overwrite of a selected tile left a dangling
  selected ref (stale "Sel: N" + selection outline on a nonexistent tile). Fixed:
  `Do()` also `Selection.Remove(t)` per removed tile; `Undo()` deliberately does not
  re-select (matches the accepted MC4 decision).

**MC5 (engineer, 2026-07-12) — delivered per contract.** `PanelCommands.cs`
(`PropertyEditCommand` generic apply/revert closures incl. the §3 collision-auto-off
coupling captured atomically in ONE command; `LayerAddCommand`/`LayerRemoveCommand`
inverse pair holding the layer reference; `LayerReorderCommand` adjacent same-role
swap, all occupancy-invalidating — the cache is index-keyed), `LayerPanel.cs`
(rows = reverse(Layers) + Canvas row → Photoshop-style front-on-top; accent swatch +
current-row highlight per §7.4; band-limited ▲▼; farview ≤8 / closeview ≤2 add
buttons; remove with tile-count confirm; properties sub-panel with role-appropriate
disabling — battlefield locked parallax/loop/collision/sway + editable 64×36..512×256
bounds, closeview collision-never + sway-forced-0, farview collision gated on
parallax==(1,1) && !loop with explanatory tooltips; structural-signature rebuild vs
in-place `SetValueNoSignal` refresh so focused controls never get freed),
`PalettePanel.cs` (category-grouped registry rows, brush highlight, role-illegal defs
greyed against the current layer, built once + highlight-refresh on StateChanged).
MapEditor: Preview-gen button enabled — scratch seed via `DetRandom.NewSeed()` (the
sanctioned mint) → `RuleResolver.Resolve` → `State.Preview` (view-only, never a
command, never dirties), re-press re-rolls, Esc clears (MC4 wiring), seed shown in
button text, resolver warnings PushWarning'd in debug builds; CanvasBg re-tinted from
`Document.Canvas.Color` on every StateChanged. Accepted engineer decisions: parallax
spinbox range −4..4; raw enum category headers; reorder-follows-current-layer clears
selection via the centralized setter; duplicate default layer names possible
(cosmetic); removal confirm dialog parented under LayersBox (Window children ignore
container layout).

*(2026-07-12: while the orchestrator session was down on a usage limit, the repo
owner manually committed all five build phases as `98f4787` "map creation funciton
upgrade" — MC1–MC5 code, both scenes, project.godot, the GDD/IDEAS/KNOWLEDGE/report
doc changes. That commit is left untouched; MC6 reviewed `b0efc62..98f4787` and the
fixes + ship items land as a follow-up commit.)*

**MC6 (reviewer, 2026-07-12) — verdict APPROVE WITH FIXES** over the full milestone
diff `b0efc62..98f4787`. Findings and orchestrator triage:
- **BLOCKER (confirmed → F-MC2):** Ctrl+V paste was unreachable — `_UnhandledKeyInput`
  checked the bare-`V` `mapedit_tool_move` action before `mapedit_paste`, and
  `IsActionPressed`'s default non-exact modifier matching lets Ctrl+V satisfy bare-V.
  Fixed by moving ALL 8 bare-key tool checks below the modifier-combo actions (same
  tie-break family as the existing redo-before-undo ordering). KNOWLEDGE caveat added.
- **MAJOR (adjudicated FALSE POSITIVE, no behavior change):** "MoveTool lets two
  selected tiles land on the same cell." Rejected on the math: every dragged tile
  moves by the same (dx,dy) — a uniform translation of pairwise-disjoint footprints
  stays pairwise-disjoint — and MoveTool aborts whole-move on any out-of-grid tile
  (no partial moves) while the overwrite scan excludes `movedSet`. The suggested
  destCells-vs-area check would always pass. An invariant comment was added at the
  overwrite scan documenting why the exclusion is load-bearing and when a self-overlap
  check WOULD become mandatory (any future per-tile-skip move).
- **PLAUSIBLE compile-risk (confirmed → F-MC3):** `mapedit_zoom_out`'s numpad binding
  was `4194440` (KEY_KP_2), not KEY_KP_SUBTRACT (`4194435`) — a transcription error in
  the orchestrator's own MC3 brief, faithfully copied by the engineer. Fixed in
  project.godot; numpad enum note added to the new KNOWLEDGE caveat.
- **MINOR (fixed → F-MC4):** `MapJson.Save`/`DoSave`/browser save sites had no IO
  exception handling (inconsistent with the module's degrade-gracefully posture).
  Fixed: Data layer documented as propagating; all 4 Editor-layer call sites
  (DoSave, Create, Rename, Duplicate) now catch → GD.PushError, and `MarkSaved` only
  runs on success so the dirty dot keeps telling the truth.
- **PLAUSIBLE edge (fixed → F-MC5):** a shape-valid non-map .json deserialized into a
  default MapDocument and showed a phantom "Untitled" card. Fixed: `MapJson.Load`
  rejects documents with an empty `Id` ("not a map file") — the GUID is the one field
  every real map has from creation.
- **MINOR (accepted, documented):** browser prompt dialog's LineEdit as a direct
  `ConfirmationDialog` child — AcceptDialog lays out non-internal Control children
  into its content rect and the prompt dialog never sets `DialogText`, so there is no
  label to overlap; accepted as-is (was the MC2 deferred nit).
Reviewer verified clean: layer law (zero Godot under Data/, properties-only + Extra),
no literal 32 / no unsanctioned RNG, GDD §1/§2/§3/§6/§7.x/§8 numbers and behaviors,
drop-oldest + saved-marker latch, thin-root scenes with no uid, all 24 InputMap
entries well-formed with the 12 pre-existing untouched, FloorToInt (not cast) in the
negative-coordinate cell math, F-MC1 selection hygiene. Flagged-unverified (no
toolchain): SetValueNoSignal/SetPressedNoSignal/TooltipText/ButtonGroup/DrawArc
named-arg forms, `File.Move(overwrite:)` — all precedented/documented, compile-check
at the next toolchain window.

**MC7 (orchestrator, 2026-07-12) — ship checklist.** KNOWLEDGE.md: STJ caveat
confirmed present (landed with 98f4787); added 3 more v0.6.7 caveats (bare-key vs
modifier-combo action ordering + numpad enum note; _Draw-behind-children; GUID file
identity) — the GDD §11 caveat list is now fully covered. Migration.md §0: 0.6.7
changelog entry added (notes the 0.6.1–0.6.6 changelog gap, see §10). Version trio
bumped 0.6.6 → 0.6.7 (`VERSION`, `Scripts/GameVersion.cs`, `Scenes/Hud.tscn`
VersionLabel). Doc-sync: per the design lead's instruction, `Docs/MapCreation.gdd`
was committed UNMODIFIED — implementation decisions live in §11.4/§11.5 of this
report instead of a GDD decisions-log edit this milestone. Static verification: see
the final report (no toolchain — `dotnet build` still owed at the next window, along
with the standing v0.5.x/v0.6.x compile-check items).

### 11.6 Version/commit log

- `98f4787` — owner's manual commit of MC1–MC5 (version still 0.6.6 inside; trio
  intentionally not bumped there).
- **v0.6.7** — MC6 review fixes (F-MC2..F-MC5 + MoveTool invariant comment) + ship
  items (3 KNOWLEDGE caveats, Migration §0 entry, version trio, this report's §11
  close-out). Follow-up commit on `main` (static checks only, no toolchain).

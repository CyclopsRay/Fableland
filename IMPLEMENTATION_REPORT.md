# IMPLEMENTATION_REPORT.md — Fableland full-project build

> Orchestration log for implementing the GDD suite (v0.4.0 foes → v0.5.0 node content →
> v0.6.0 items) on top of prototype 0 (v0.3.7). Maintained by the orchestrating session;
> updated after every phase so a fresh session can resume from here. Companion docs:
> `KNOWLEDGE.md` (caveats), `Docs/Tech/T30-FEATURE-BLUEPRINTS.md` (blueprints this build
> follows).

**Status: v0.6.0 landing as a DELIBERATELY-REDUCED STUB (minimal Team Build menu + item-catalog stub) — see §9. Working tree carries the uncommitted v0.5.4 debug-protagonist work (§8); this milestone builds on top of it and bumps the trio to 0.6.0. v0.5.0/v0.5.1 landed and merged to main (04b8843); HEAD is v0.5.3 (8a3b73f).**

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


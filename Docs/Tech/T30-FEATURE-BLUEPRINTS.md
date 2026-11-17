# T30 — Feature Blueprints

Implementation designs for the systems the GDDs commission next. Each blueprint:
data shapes, tick/turn order, edge cases already visible from the GDDs, and its test
hooks. Blueprints are starting points — deviations are fine but get written back here.

---

## 1. RunState (autoload) — build first inside v0.5.0

```csharp
// Scripts/Run/RunState.cs (autoload "*res://Scripts/Run/RunState.cs")
public partial class RunState : Node {
    public static RunState Instance;                    // _EnterTree
    // Identity
    public string Seed; public DetRandom Rng;           // root; subsystems derive Rng.Sub("...")
    public int Day; public bool InVoid;                 // InVoid ⇒ HUD shows "???"
    public int Stamina;
    // Map
    public MapGraph Graph;                              // node.State: Unvisited/Visited/Completed/Devoured
    public string CurrentNodeId;
    // Party & inventory (see §4)
    public List<ProtagonistState> Owned; public List<string> ActiveBuild;
    public Inventory Inventory;
    // NPC windows / plantations: Dictionary<shelterId, ShelterState>
    // Run-performance counters (NODES §8) — five ints, incremented at the source events
    // Adventure handshake (T10 §3)
    public AdventureContext CurrentAdventure;           // null ⇒ debug-launched scene
    public void BeginAdventure(string nodeId) { ... }
    public void ReportGoal(bool success, RewardBundle rewards) { ... }
    public void EndDay() { ... }                        // §5 pipeline
}
```

- **Permanent stat changes** (Sharpen, Rest excess, mushroom (e)/(f)) write to
  `ProtagonistState.RunBase*` — the *run copy* — never to Data tables, never to the
  live node's exported fields (nodes die with scenes; RunState is the truth).
- On scene entry, characters hydrate from `ProtagonistState` (HP carries between
  fights) and write back on scene exit / death.
- `ActiveBuild` is editable shelter configuration, not live arena state. `BeginAdventure`
  validates and copies it into `AdventureContext.BattleTeam`; the Arena uses only that
  immutable order and keeps its own local current-member index. This prevents a UI edit
  or stale index from changing a fight's protagonist identity mid-combat.
- Test hooks: a `DebugSnapshot()` string dump (day, stamina, node, counters) shown in
  the debug overlay; unit tests construct RunState headlessly (no scene needed —
  keep node-type fields nullable-tolerant).

## 2. Foe system (v0.4.0) — `Scripts/Foes/`

Per FOES §9, with these engineering decisions locked:

- **Level application at spawn, once:** `Init(level)` multiplies base stats via
  `FoeStats.ForLevel` and stores `CurrentLevel`. Evolution (§5) re-derives from *base*
  (multipliers are base-relative, not chained): `newMax = baseHP × mult(newLevel);
  hp = min(newMax, hp_scaled + 0.30 × newMax)` — write the exact formula in one
  function with a unit test, because "scales to the new level's max, then heals 30%"
  is precisely the kind of sentence two sessions implement two ways.
- **FSM tick order per physics frame:** status decay → sight timer (1 s crab / 2 s
  gull; counts *consecutive* misses only while AGGRO) → state behavior
  (UpdatePatrol/UpdateAggro) → skill gates (`FoeStats.HasSkill1/2` + per-skill CD) →
  movement integration (intent + external, same model as characters) → evolution timer
  (25 s since spawn, once, cap 8, no interruption).
- **Spawn-on-death:** `OnDeath()` in CrabFoe spawns 2 × `max(1, level-2)` crabs with
  `CanSpawnBabies=false` (the no-grandchildren rule is a flag on the instance, not a
  level check), small `Rng`-offset positions, **ignoring the arena cap**; then base
  loot logic; then `QueueFree`.
- **Sight shapes are data + debug-drawn:** crab = ground-anchored rect (8×3 m → 12×5 m),
  gull = movement-facing cone (60°/10 m → 120°/12 m, retains last facing when still).
  One `SightShape` struct interpreted by a shared checker; `DrawSight()` renders it.
- Seagull movement: no gravity; horizontal accel = `PlayerGroundAccel / 3` (query the
  constant, don't copy the number); patrol turnaround at 100 m from spawn point.
- Poop projectile: falls with gravity, damages on direct hit AND lingers 3 s as a
  micro-hazard on surface contact — implement the linger as a short-lived `Hazard`
  (reuse, don't re-invent ticking).
- Dash telegraph: 1 s stopped + red tint is a **gameplay promise** — it ships with the
  skill, not with the art pass.

## 3. Missions (v0.5.0) — strategy objects

```csharp
public abstract class Mission {                     // created from MissionRegistry
    public abstract void Setup(Arena a, int nodeLevel, DetRandom rng);
    public abstract void Tick(float dt);
    public MissionStatus Status;                    // Running / Succeeded / Failed
    public abstract RewardBundle Reward();
}
```

- Mission **type** is rolled at map generation (frequencies 60/15/10/10/5, NODES §4.1)
  and stored on the node — the arena only instantiates it.
- Parameters come from `MissionTable` by node level (cores/duration/objectives/waves).
- Failure semantics are per-type and already specified (NODES §2.3): only Boss-timer
  failure kills; others set `Failed` → node stays incomplete, player walks out.
  Model "player death" as a separate global check, not per-mission.
- Slaughter: wave n+1 spawns only when wave n is clear; **final wave at foe level +1,
  cap 8** — the cap lives in the level-computation function, tested.
- Protect: the Condensed Wonder Core is a stationary gameplay-layer entity with its
  own HP + `HpChanged` event (HUD bar); mission fails at 0, run continues.
- Build **Collection first** and make it fun before writing the other four (it's 60%
  of all combat nodes).

## 4. Items (v0.10.0) — instances over defs

```csharp
public class ItemInstance {
    public string DefId;                 // → ItemRegistry
    public int DayCdRemaining;           // day-based axis (ticks only while HELD)
    public float SecCdRemaining;         // combat axis (ticks only in arena)
    public int PerishRemaining;          // ticks ALWAYS (held or backpack)
    public Dictionary<string,float> RolledStats;  // harvest inheritance (60–140% rolls)
}
```

`ItemCatalog` owns immutable definitions; `RunState` owns instance location and persistence;
`ItemRuntime` binds the currently fielded held instance to the arena body. Exploration behaviours
remain in `RunState`/map contracts. TwistedReality persists a bounded start-of-day checkpoint,
creates two `RealityBridge` legs around an Eidolon Shelter, and only the Shelter's Blessing
crosses between endpoints.

- **Location model:** an item is in exactly one place — `Held(protagonist)` or
  `Backpack` or `Planted(shelterId, slot)`. Moves are transactions in one Inventory
  class (one owner of truth). Benching a protagonist auto-returns their item —
  implemented in the bench operation, nowhere else.
- **Passive application:** on becoming Held, apply the def's modifier list to the
  holder via modifier-stack sources keyed `item:<defId>#<instanceId>`; remove on
  unhold. `Possession` tag ⇒ apply while in Backpack too (to whom? — *the party*, so
  possession passives use a party-level modifier scope; decide the scope field in the
  def, don't special-case THE VOID).
- **Conversion** (`Convertible`): replace the instance's `DefId` in place — slot,
  location, and instance id survive (ITEMS decisions log: successor stays where the
  ancestor was). Re-run passive removal/re-application around the swap.
- **Tag legality** enforced at boot validation; `Eternal` checked at every removal
  path (trade, drop, convert, enchant) via one `CanRemove(instance)` gate function.
- Mushroom outcome table and all probabilities roll from `RunState.Rng.Sub("items")`.

## 5. Day-end resolution — the ordered pipeline

```csharp
// Executed by RunState.EndDay(), in exactly this order (NODES §7.4):
1. Day++                       // unless InVoid ("???" — clock unknowable; decide: freeze devour too)
2. VoidDevour.Step(Day)        // ring devour + orphaned function nodes
3. ItemCds.TickHeld()          // day-based CDs, held only
4. Perish.TickAll()            // held AND backpack; expiry may Convert
5. NpcWindows.Tick()           // traders/wanderers leave after 5 days from first entry
6. Stamina = 5
// future steps register here: Plantation.Grow(), Phantoms.Move(), ...
```

Implement as `List<IDayStep>` executed by index — the order is printable in debug and
testable headlessly. **No other code path may perform any of these effects.**

## 6. Save / load

- **Meta save** (`user://meta.json`, future): unlocks, settings, stats. Versioned (T20 §5).
- **Run slots** (`user://saves/slot_1.json` … `slot_3.json`): exactly three versioned
  RunState snapshots. Serialize seed, day/stamina/VOID, node-state sets, durable map
  deltas (devoured ids + reality bridges), party/build/base state, item instances, and
  run counters. The seeded `MapGraph` is re-derived then its durable delta is applied;
  reserve `MapSaveData` for future authored-map changes.
- **Checkpoint policy:** save after every non-terminal mission resolution and after
  successful day-end resolution, plus explicit map/battle **Save & Quit**. A live battle
  checkpoint stores the run/party and a resume-node marker, then reconstructs the same
  deterministic encounter on Continue; do not serialize transient foes/projectiles/
  mission timers. Delete the active slot on death or victory.
- Serialize **instance state, not derived state** (never save effective stats — they
  recompute from base + modifiers).
- Every DTO has public properties plus `[JsonExtensionData]`; the loader switches by
  version through explicit migration functions and preserves unknown fields on rewrite.
  Write via temp-file + same-directory rename (atomic-ish); read tolerates a missing
  slot (fresh run). `SaveGameService.RoundTripSelfTest` is a no-file regression hook.
- **Anything a seed is fed is part of the save (v0.11.0, version 4).** The map is
  regenerated from the seed — and from the **world pool**, which grew from six to eight. A
  save therefore records the pool it was generated from (`RunSaveData.WorldPool`, in pool
  order), `MapGenerator.Generate` takes that pool, and the migration gives an older save the
  six worlds of its day (`SaveGameService.WorldPoolBeforeV4`). The same goes for any future
  generator input that can change between versions: record it, or freeze it.
- **The slot folder can be redirected** — `SaveGameService.DirectoryOverride`, or the
  environment variable `FABLELAND_SAVE_DIR` for tooling that cannot reach C#. `user://` is
  shared by every checkout, and a run checkpoints and deletes its slot when it ends: a check
  or capture script that starts a run must point this at a scratch folder first.

## 7. Shelter / event scenes (v0.5.0)

- One `Shelter.tscn` driven by `ShelterState` (functions rolled at mapgen: plantation
  slots, trader, wanderer) + Blessed/Mundane flag. Actions are buttons built from the
  available-action list (T20 §3 socket) — adding a shelter function = new action entry.
- `?`-events: an `EventDef` = ordered list of pages `{ text, choices[] → effects }`
  interpreted by one `EventRunner` scene. Effects reuse existing verbs (grant item,
  damage, modifier, move player, start combat) — an event author composes verbs, never
  writes code. This is the highest-leverage content tool in the project; build the
  runner generic on day one.

## 8. UI for Adventure mode

Map/Finish-the-Day buttons per NODES §1: availability is a pure function
`(nodeType, missionStatus, choicesResolved) → {mapEnabled, finishEnabled, canMove}` —
implement literally as that function with a unit test per GDD row, because these
gating rules are exactly what regresses silently. Confirmation popup is one reusable
scene. The map/battle pause uses `GetTree().Paused`; its overlay must process **Always**
while the arena/missions do not tick. A manager that otherwise uses Always (the arena's
debug restart flow) explicitly returns before combat work while the tree is paused.

## 9. Boss fights (v0.10.11) — a protagonist on the other side

Design: `FOES.gdd` §8. Contracts: T10 §4a. What is built, and how the next boss is added.

```
BossTable (data)      row: scene, protagonist, world, HP / damage / poise / magazine / CDs
   │  RunState.BeginAdventure: BossTable.ForWorld(node.Zone) → AdventureContext.BossId
   │  (ForWorld = the world's own boss, else BossTable.StandIn — PumpKing, v0.11.0)
   ▼
BossMission           stages the scene named by the row on the map's perches (Level Goal
   │                  markers), farthest from the player; falls back to the crab stand-in
   ▼  BeginFight(def, foeLevel, arena)            — after AddChild and placement
BossX : X             Side => Foe; BossSetup.ApplyRow (HP × day, damage × day, poise);
   │                  attaches a BossBrain and builds its rule list
   ▼  every DecisionSeconds:  release holds → Sense(dt) → BossRuleSet.Decide(dt)
VirtualPad            what the kit reads instead of the keyboard; stepped once per process
                      frame and once per physics tick, before the body runs
```

- **Order per frame.** `BossBrain` has process priorities of −100, so in both loops it runs
  before the body: think (process only), step the pad, then the body reads it — a tapped
  button is seen that same frame, as a key press is. Decisions are immediate-mode: each one
  starts from released buttons and the winning rules press what they want held.
- **The scheduler is pure** (`BossRuleSet`, no nodes): per channel, a committed rule keeps
  its channel; otherwise the first rule off cooldown whose condition holds takes it; a
  one-shot fires on each start, a continuous rule on every decision. `SelfTest` pins it.
- **Senses** are sampled once per decision (`BossSenses.Sample`), plus whatever the boss
  tracks for itself (PumpKing: seconds the player has been in his face, seconds his head has
  been rolling away).
- **Adding a boss:** (1) a `BossTable` row; (2) `class BossY : Y, IBossFighter` with
  `Side => Foe`, `BeginFight` (apply the row, scale the kit's cooldowns, attach the brain)
  and a rule list; (3) `Scenes/Bosses/BossY.tscn` inheriting `Y.tscn` with the script
  swapped; (4) in `Y`'s kit, route any projectile's side from its launcher
  (`CombatTargets.AimMask` + `TryGetHostile`, as `PumpKingHead` does — the `NOTE(boss)`
  markers are the places) and expose the read-only state the rules need; (5) a `boss` map
  with perches; (6) a stage in `BossFightCheck` if the kit has a mechanic of its own, and
  in its rules probe one scenario per rule of the brain — the one situation that rule
  exists for, on a stage where nothing else can produce the effect — each broken once to
  see the check fail by name (a rule nobody has watched working may never have worked).
  Nothing in `GameManager`, `BossMission` or the debug page changes. Adding the row takes
  that world out of the stand-in's care (`BossTable.HomeBossOf` finds it first); replace its
  generated `<realm>_boss_arena` with a map built for the new boss.
- **Edge cases already handled.** The scene fails to load or is not a Foe-side boss →
  the stand-in is fielded and the error logged (a permadeath run is not broken by content).
  No perches on the map → the boss gets the spot the stand-in would have. The mission
  resolves with the boss standing (debug skip, the timer) → `Mission.OnResolved` →
  `StandDown`. The boss dies → it fades and frees itself; the mission only watches.
- **Not built:** phases, a boss's own held item, boss-specific tuning beyond the row,
  pathfinding (a boss walks toward a perch and jumps when under it; anything farther is a
  flight), more than one boss.

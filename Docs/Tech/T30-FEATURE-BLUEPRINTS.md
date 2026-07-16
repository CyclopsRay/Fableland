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

## 4. Items (v0.6.0) — instances over defs

```csharp
public class ItemInstance {
    public string DefId;                 // → ItemRegistry
    public int DayCdRemaining;           // day-based axis (ticks only while HELD)
    public float SecCdRemaining;         // combat axis (ticks only in arena)
    public int PerishRemaining;          // ticks ALWAYS (held or backpack)
    public Dictionary<string,float> RolledStats;  // harvest inheritance (60–140% rolls)
}
```

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

- **Meta save** (`user://meta.json`): unlocks, settings, stats. Versioned (T20 §5).
- **Run snapshot** (`user://run.json`): serialize RunState (seed + day + node states +
  party + inventory instances + NPC/plantation state). Written at each day-end (a
  natural checkpoint), deleted on death/victory — permadeath means *no mid-combat
  save*, and day-end granularity makes save-scumming unattractive without punishing
  crashes (a crash costs at most the current day — acceptable, and rule T00 #4 says
  crashes must not happen anyway).
- Serialize **instance state, not derived state** (never save effective stats — they
  recompute from base + modifiers).
- Write via temp-file + rename (atomic-ish); read tolerates missing file (fresh run).

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
scene. The pause-while-map-open uses `GetTree().Paused` + process-mode exemptions for
the map layer (set `ProcessMode` deliberately on the map CanvasLayer).

# T20 — Extensibility Rules

The test of this codebase's health: **adding the 10th foe, 30th item, or 6th mission
type should touch fewer files than adding the 2nd did.** These rules make that true.

---

## 1. The registry pattern (the backbone)

Every open-ended content family gets a **registry**: a dictionary from string id →
definition, populated at boot, looked up everywhere else.

```
FoeRegistry["crab"]        → FoeDef      (stats ref, scene ref, skill set)
ItemRegistry["pomes_bravery"] → ItemDef  (tags, CDs, passive mods, skill, conversion)
MissionRegistry["collection"] → MissionDef (factory for the strategy object)
EventRegistry["weird_well"]   → EventDef  (?-node event script)
WorldRegistry["VK"]           → WorldDef  (palette, barriers, foe pool, name)
```

Rules:
- **No content switch statements.** `switch (foeType)` in a core file means the
  registry is missing a field. Add the field to the def; delete the switch.
- Content ids are **snake_case strings** in data (stable across saves/mods); closed
  engine sets (FSM states, node kinds, tag enum) stay C# enums.
- Registries validate on boot (T10 §5) and are **read-only after boot**.
- This is also the future mod/DLC surface: a registry populated from files is a
  registry a mod can add to. You don't need mods now; you need the shape now.

## 2. Def / Behavior split

A definition is data; behavior is code. Bind them by reference, never by inheritance
from data:

- **Default path (90% of content):** the def alone is enough. An item whose passive is
  "+10% SPD, gravity −20%" is a *list of modifier entries* interpreted by the generic
  item runtime. A foe that only patrols/aggros needs zero new code.
- **Bespoke path:** the def names a behavior class (`skillClass: "DDSkill"`) for the
  genuinely unique (DD's reclaimable bug, Yukai's rope, RANIBOBER). Bespoke classes
  are small, sealed, and live beside their content, not in the core.
- **Inheritance budget: depth ≤ 2** (`BaseFoe → CrabFoe` ✓). Anything deeper — or any
  urge for `BurningCrabFoe` — means the varying part should be data or a component
  (a status, a modifier source, a skill object).

## 3. Pipelines & hooks (the extension sockets)

Core flows expose **named, ordered extension points**; new systems plug in instead of
patching the flow:

| Socket | Shape | Existing/planned users |
|--------|-------|------------------------|
| Damage pipeline stages (T10 §4) | fixed order, tap points | statuses, items (thorns, lifesteal), StarSicking-style stacks |
| `OnDeath()` hook | virtual, fires before `QueueFree` | Crab spawn-on-death; loot drops; ult-charge credit |
| Day-end resolution | **ordered step list**, registered centrally (NODES §7.4 order is law) | devour, item CDs, perish, NPC departure, stamina; future: plantation growth, phantom movement |
| Modifier stack (30-DATA §3) | add/clear by source key | every buff, item passive, mushroom outcome |
| Map generation passes | ordered pass list (combat → intra-edges → inter-edges → zone 6 → function nodes) | new pass = new list entry; **order is behavior** — a new pass documents *why* it sits where it sits |
| Shelter action list | per-shelter available-actions built from def + state | build-mod, plantation, trader, joust; future: teleport, secret tunnels |

If a feature can't land through an existing socket, the task is: *add the socket*
(small, reviewed change to core), then land the feature through it. Two features
needing the same hack = the socket you should have built the first time.

## 4. State machines — the house style

Explicit enum + switch **on states, inside the owner** (PumpKing's `PKState`, foe
PATROL/AGGRO): fine and encouraged, because states are a closed set owned by one
class. Guard rails:
- Transitions happen in one method (`SetState(newState)`) that handles exit/enter
  effects — never scattered field flips (`_state = X` mid-skill is how PumpKing-style
  characters desync their shield/visibility bookkeeping).
- Every state machine draws its state in debug (`ShowDebugRanges` pattern / foe
  `DrawSight`) — an FSM you can't see is an FSM you can't test.

## 5. Save-data versioning (from the first byte)

The first `user://` save (balance overrides, meta-progression, run snapshot) ships
with `{ "version": 1 }` and a loader that switches on version with per-version
migration functions. Retro-fitting versioning after players have files is misery;
adding it on day one is three lines. Unknown fields are preserved on rewrite
(forward-compat), unknown ids are skipped with a logged warning (content removed
between versions must not corrupt a save).

## 6. Deprecation protocol

Old code is removed in daylight, not left to rot (case study: `Scripts/Enemy.cs` at
v0.4.0):
1. New system lands working; old one marked `[Obsolete("use Scripts/Foes; removed in 0.5")]`.
2. All scenes/refs migrated in the same minor version; grep proves zero references.
3. Deleted (file + scene + KNOWLEDGE.md mention updated) no later than the next minor.
Never leave two systems both plausibly-canonical — the next session can't tell which
to extend. `Scene1.tscn`/`Demo.tscn`/`Player.cs` (the throwaway skeleton) get this
treatment at v0.4.0 too: delete or move under a `Legacy/` folder with a README line.

## 7. Feature flags & debug surface

- Debug affordances gate on one place: `OS.IsDebugBuild()` or a single `Dev.Enabled`
  static — not per-feature booleans scattered in exports.
- Half-done features merge behind a flag default-off rather than living on stale
  branches (solo dev + long branches = merge archaeology).
- Every flag has an owner note: what turns it on, when it dies.

## 8. API design for future-you

- Public surface minimal: default `private`, expose on demand. Every `public` is a
  promise the next session must keep.
- Names say units: `ContactRangePx` or (better) accept meters and convert via
  `Units.Px()` at the boundary — mixed-unit bugs are silent and awful.
- XML doc-comments on every base-class virtual explaining *when it's called and what
  invariants hold* (the existing `CharacterController` header is the standard —
  its "ADDING A CHARACTER" comment block is worth more than a wiki).
- When behavior surprises you during work, the fix includes a comment or KNOWLEDGE
  caveat *at the surprise site*. Surprise is a defect even when the code is correct.

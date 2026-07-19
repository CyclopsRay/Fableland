# Fableland — Debug Mode Manual

Everything the debug/QA tooling does, in one place. Cross-referenced from
`Docs/Instructions/40-QA.md` §1(4) (in-game debug harnesses). Code lives in
`Scripts/Debug/` (`DebugManager.cs`, `ProtagonistRoster.cs`, `DebugLogEntry.cs`)
plus per-system knobs noted below. Current as of **v0.6.0**.

`DebugManager` is a **`CanvasLayer` autoload** — it persists across every scene
(Menu, Map, Arena, Shelter, Event, RunOver), so all of its controls are reachable
everywhere. It never touches `RunState` progression: turning debug on/off, or
anything you do on its pages, cannot alter a real run's `Owned`/`ActiveBuild`
protagonist economy, seed, or day/stamina state.

---

## 1. The `DBG` toggle button

Top-right corner, **always visible** in every scene. Click to toggle debug mode
(`DebugManager.Enabled`); the label reads `DBG*` while ON.

Debug mode ON gates:
- The **SKIP** button (appears under `DBG`).
- **Key 4** — the protagonist page (§4).
- **The shelter Team Build menu's item & roster visibility** (§9) — with debug ON it
  additionally lists PumpKing and all 13 catalog items for assignment.
- **Debug logging** — `DebugManager.Log(...)` records entries (and streams them to
  disk) only while ON; when OFF, log calls return immediately.

Debug mode does **not** gate: key 5's log viewer (§3), F9 (§5), R-restart (§6), or
the `DebugFoeLevel`/`DebugDay` exports (§7) — those have their own gates.

## 2. The `SKIP` button

Visible only while debug is ON. Fires `DebugManager.SkipRequested`, handled by the
arena's `GameManager.OnSkipRequested`:
- Mission still running → `Mission.DebugForceComplete()` (goal → Succeeded; a
  pending Slaughter reward choice resolves to its default).
- Mission already failed → force-completes it and reports a *victory* to
  `RunState.ReportGoal` (in-run) or shows the debug "SKIPPED!" banner (no-run),
  so a tester can walk past a lost fight.

Only the Arena subscribes to this event; pressing SKIP in other scenes does nothing.

## 3. Key **5** — debug log viewer

Toggles the in-game log overlay (input action `debug_log_viewer`). Works
**regardless of whether debug mode is on** — but entries are only *recorded* while
debug is ON, so an empty viewer usually means debug was never enabled. Esc or the
X button closes it. The buffer keeps the last 2000 lines.

Log categories (color-coded): `DMG_DEALT`, `DMG_RCVD`, `HAZARD`, `DOT`, `HEAL`,
`BUFF`, `CORES`, `ITEM`, `STATUS`, `MISSION`, `SYSTEM`.

Every entry is also streamed to disk at **`user://debug_log.txt`** (globalized OS
path; on Linux typically `~/.local/share/godot/app_userdata/Fableland/debug_log.txt`).
The file is recreated on every launch.

## 4. Key **4** — protagonist building page (v0.5.4)

Input action `debug_protagonist_page`. **Only responds while debug mode is ON**
(deliberately unlike key 5). Toggles a centered overlay listing every implemented
protagonist from `Scripts/Debug/ProtagonistRoster.cs` — currently **Pixolotl**,
**Pomegraknight**, **PumpKing**, **Cleopastar**, and **Sifu Pangda**. Esc or X closes it; turning debug
OFF force-closes it.

Selecting a protagonist makes it the currently-controlled player character,
**independent of** `RunState.Owned`/`ActiveBuild` (the debug body choice never leaks
into the run economy):

- **In the Arena** (the only scene with a live `CharacterController`): the current
  body is swapped in place, immediately — same position, signals (`HpChanged`/
  `Died`), HUD (`Hud.SetPlayer`), camera, and HP hydration re-wired exactly like the
  normal boot path. In a run, the current HP *ratio* carries into the new body
  (written through `Owned[0]`'s `ProtagonistState`, which also remains the HP
  write-back target no matter which body is worn). Status label: "Swapped to …".
- **Anywhere else** (Map, Shelter, Menu, …): nothing to swap — the selection is
  **queued** on the autoload and applied automatically the next time an Arena loads
  while debug is still ON. Status label: "Selected … — applies on next Arena entry".
  (The same "queued" message appears if you select the character you're already
  wearing, or if the swap is refused — dead body awaiting respawn, or the debug
  match already ended.)
- **Clear override (scene default)** returns to whatever the scene/run would spawn
  normally, from the next Arena load onward (it does not revert an already-applied
  swap mid-fight).

Turning debug OFF stops future applications (key 4 dead, queued selection not
applied) but does not revert a body that was already swapped.

## 5. **F9** — force-complete the running mission

In the Arena, in a **debug build** (`OS.IsDebugBuild()` — no `DBG` toggle needed),
holding F9 while a mission is running calls `Mission.DebugForceComplete()`
(`GameManager._Process`). The 40-QA §1 cheat for walking the whole run loop without
playing out every mission.

## 6. **R** — restart (no-run debug fallback only)

Input action `restart`. After a win/lose banner in a **debug-launched** arena
(direct F5 on `Arena.tscn`, no run in progress), R reloads the scene
(`GameManager._Process`). Inactive during a real run — runs end through
`RunState.EndRun`/permadeath, never a scene reload.

## 7. `GameManager` debug exports — `DebugFoeLevel` / `DebugDay`

Exported on the Arena root node. Consulted **only when no run exists**
(`RunState.CurrentAdventure == null`, i.e. direct F5 on `Arena.tscn`):
`DebugFoeLevel > 0` forces the foe level directly (default 1); set it to 0 to use
day-based scaling from `DebugDay`. Real runs never read either knob — see the
KNOWLEDGE.md caveat "A debug-override export must be gated behind 'no run exists'"
(v0.5.0) for why the gate is run-existence, not the knob's value.

## 8. Other per-system harnesses (pre-dating this manual)

- **`CharacterController.ShowDebugRanges`** (export) — draws combat range/hit-test
  gizmos around the character.
- **Map scene**: seed field + dice (reroll/reproduce a map), Rest/Mist debug
  buttons, View (schematic/atlas) and Cam (Flat/BossUp/HeadingUp) toggles — see
  `Docs/MapGDD.md` and KNOWLEDGE.md's map reference notes.
- **Debug arena determinism**: a no-run arena always uses the fixed seed
  `"debug-arena"`, so F5 reproduces the same procedural layout every time.

## 9. Shelter **Team Build** menu — debug item & roster visibility (v0.6.0)

The shelter (`Scenes/Shelter.tscn`) has a **Team Build** button (a free, always-available
management action — no stamina/Blessing cost, no day end) that opens an overlay for
assigning a single held wonder item to a protagonist. This is a **real, permanent** shelter
feature and works with debug mode OFF — it then shows only what a real run actually has:
`RunState.Owned` protagonists (Pixolotl, Pomegraknight, PumpKing, and Cleopastar in a
fresh prototype run, plus Sifu Pangda) and the real `RunState.Items` backpack (empty until
something grants an item). A fresh prototype team starts as **Sifu Pangda, Pixolotl, and
Cleopastar**; Pomegraknight and PumpKing begin on the bench and remain selectable.
Older prototype saves are upgraded on their next load: missing implemented protagonists are
added and this same default team is selected once.

With **debug mode ON**, the menu additionally shows content the real economy hasn't granted,
purely for testing — the same non-invasive spirit as the key-4 protagonist page:
- **All implemented protagonists** — every `Scripts/Debug/ProtagonistRoster.cs` entry not
  already in `Owned` is added to the roster as a selectable, item-assignable entry. A fresh
  run already owns the current full roster, but this display bypass remains ready for future
  unlocked characters. Ephemeral `ProtagonistState`s are owned by the shelter for the scene
  visit; `RunState.Owned`/`ActiveBuild` are **never** mutated.
- **All 13 wonder items** — every `Scripts/Items/ItemCatalog.cs` entry not already in the
  backpack (and not currently held) is listed as an assignable row, tagged **`[DBG]`** to
  distinguish it from a really-owned item. This is a **display bypass**: assigning a `[DBG]`
  item sets the protagonist's held slot for testing but does **not** add anything to the real
  `RunState.Items`, and returning it makes it vanish rather than materialize as a real item —
  so the debug catalog can never permanently grant items outside a real run's economy (see
  the v0.6.0 KNOWLEDGE.md caveat on display-bypass round-trips).

Turning debug OFF drops any display-only protagonists and the `[DBG]` catalog rows from the menu
on its next refresh; any real held/backpack items stay. Note: wonder items are **id + display name only**
in v0.6.0 — no passive/skill/cooldown behaviour attaches yet (full system is future work,
`Docs/Tech/T30-FEATURE-BLUEPRINTS.md` §4 / `Docs/ITEMS.gdd`).

---

*Update this file whenever debug tooling changes — it is the QA hat's user manual
(golden rule 1 applies to tooling docs too).*

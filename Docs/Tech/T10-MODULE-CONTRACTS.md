# T10 — Module Contracts & Communication Rules

How modules talk, so that adding module N+1 never means rewiring modules 1..N.

---

## 1. Communication mechanisms — when to use which

| Mechanism | Use for | Rules |
|-----------|---------|-------|
| **C# events** (`event Action<...>`) | Gameplay → Presentation notifications (`HpChanged`, `Died`); anything with multiple listeners | Raiser never cares who listens. **Subscribe in `_Ready`/`_EnterTree`, unsubscribe in `_ExitTree` — always.** A scene node subscribed to an autoload's event and then freed by `ReloadCurrentScene` is a leak *and* a crash (the delegate keeps the dead node alive; invoking it touches a disposed object). This will be our most common bug class once RunState exists — make the pair-check a review habit. |
| **Godot signals** | Scene-boundary wiring authored in `.tscn` (buttons, Area2D enter/exit) | Fine at the edge; don't build cross-module buses out of them (no compile-time checking). |
| **Autoload singletons** (`static Instance` set in `_EnterTree`) | True globals only. Current: `DamageNumberManager`, `ShakeCamera2D`, `CharacterController.LocalPlayer`; planned: `RunState`, `AudioManager`, `BalanceOverrides` | Cap the list. Each new one needs a written justification here. A singleton that parents nodes into the live scene must do it via `GetTree().CurrentScene` (survives reloads — existing pattern). |
| **Direct references** (`[Export]` NodePath / ctor args / `Init(...)`) | Parent→child within one scene; spawner→spawnee config | `Init(...)` is called **after** `AddChild` (KNOWLEDGE caveat); spawnee guards against acting on un-Init'd state for one frame. |
| **Return values / plain calls** | Within a module | Prefer these. Events are for crossing layers, not for everything. |

Forbidden: gameplay calling UI methods; UI mutating gameplay state directly (UI raises
an *intent* — "FinishDayRequested" — orchestration validates and executes); two
modules both writing one field (one-owner rule, T00).

## 2. Scene ↔ script contract

- Every scene has exactly **one root script** that is its public API; other nodes in
  the scene are implementation details. Outsiders call the root, never
  `GetNode("Sprite2D")` into someone else's scene.
- The scene owns **wiring** (`[Export]` NodePaths, PackedScene refs, placement); the
  script owns **behavior**; the Data tables own **numbers** (30-DATA §2). If you're
  typing a balance number into the inspector, you're in the wrong layer.
- Renames: script/class name, scene root name, and `.tscn` `ext_resource` must move
  together in one commit (CLAUDE.md don'ts).
- Collision layers/masks are set from `Units.Layer*` constants in code or checked
  against them; a scene with hand-set masks documents why.

## 3. The orchestration handshake (Exploration ⇄ Adventure)

The single most important contract in the game, locked here before v0.5.0:

```
MapController ──(player enters node)──▶ RunState.BeginAdventure(nodeId)
   RunState snapshots: nodeLevel, missionType (rolled at mapgen), day, party, items
   RunState swaps scene → Arena/Shelter/Event scene
Arena GameManager ◀──(reads only)── RunState.CurrentAdventure { nodeLevel, missionType, day }
   ... combat happens; GameManager reports via RunState.ReportGoal(success, rewards)
Player confirms "Finish the Day" ──▶ RunState.EndDay()   (the ordered pipeline, T30 §5)
   RunState swaps scene → Map; MapController re-reads state and redraws
```

- The arena **must remain launchable directly** (F5 on `Arena.tscn`): when
  `RunState.CurrentAdventure` is null, GameManager falls back to a debug default
  (level 1, Collection, day 1). Every Adventure scene follows this null-tolerant rule
  — it's what keeps each layer testable in isolation.
- Nothing inside the arena mutates RunState except through the `Report*` API. No
  reaching in to decrement stamina from a foe script.

## 4. The damage pipeline (fixed stage order — extend, don't fork)

All damage, from anyone to anyone, goes through one ordered pipeline. Today it's
implicit in `TakeHit`/`ApplyHazard`; keep this canonical order when extending:

```
1. Source assembles HitInfo (Damage, Knockback Δv, Stun)
2. Attacker-side multipliers      (DamageDealtMultiplier dictionary — OnFire, items…)
3. Gate check                     (i-frames for discrete hits; hazards bypass by design;
                                   isInvincible windows, e.g. Pangda immunities)
4. Defense mitigation             (100/(100+defense), aggregated pool)
5. Shield absorption              (currentShield before HP — PumpKing/Pangda)
6. HP application + events        (HpChanged, popups, hitflash, shake, ult-charge credit)
7. Reactions                      (knockback AddImpulse, stun window, on-hit passives:
                                   thorns/Kashaya, lifesteal/Zhen Qi, StarSicking-style stacks)
```

New effects declare their stage. Lifesteal reads the **post-mitigation** number from
stage 6 (the GDDs are explicit about post-mitigation — FanChen's Heart, Kashaya).
Never compute damage twice in two places to "simplify" — one pipeline, tap points.

## 5. Error handling & validation

- **Boot validation:** on startup (debug builds), walk every Data table: ids unique,
  cross-references resolve (item → conversion target exists; foe → skills defined),
  tag legality (`Eternal`×`Convertible`), probability tables sum to ~1. Print all
  violations, not just the first.
- **Runtime content errors** (missing scene, bad id): `GD.PushError` with the id and
  *skip the content* — a broken item must not crash a 40-minute permadeath run.
- **Assertion helper:** a tiny `Check.That(cond, msg)` that throws in debug and
  logs+continues in release. Use it at module boundaries, not on every line.
- **Never catch-and-ignore.** An empty catch block is a lie to the next session.

## 6. Threading & timing

- Everything gameplay runs on the main thread. Godot C# + our determinism rule both
  demand it; do not parallelize map generation or combat ticks.
- Physics-rate logic in `_PhysicsProcess`, visual-only in `_Process` (the map's
  rotation smoothing is `_Process` — correct). Never read input in `_PhysicsProcess`
  for edge-triggered actions (buffer in `_Process`/`_UnhandledInput`).
- Timers: prefer accumulated-float timers in the owning script (current style) over
  `SceneTreeTimer`s for anything that must pause with the game or die with the owner.

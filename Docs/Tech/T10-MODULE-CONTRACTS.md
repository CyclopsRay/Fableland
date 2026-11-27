# T10 — Module Contracts & Communication Rules

How modules talk, so that adding module N+1 never means rewiring modules 1..N.

---

## 1. Communication mechanisms — when to use which

| Mechanism | Use for | Rules |
|-----------|---------|-------|
| **C# events** (`event Action<...>`) | Gameplay → Presentation notifications (`HpChanged`, `Died`); anything with multiple listeners | Raiser never cares who listens. **Subscribe in `_Ready`/`_EnterTree`, unsubscribe in `_ExitTree` — always.** A scene node subscribed to an autoload's event and then freed by `ReloadCurrentScene` is a leak *and* a crash (the delegate keeps the dead node alive; invoking it touches a disposed object). This will be our most common bug class once RunState exists — make the pair-check a review habit. `MusicClock.BeatHit` / `BarHit` (v0.11.7) are C# events on an autoload's object: a scene node that subscribes unsubscribes in `_ExitTree` (`AudioAudition` does). |
| **Godot signals** | Scene-boundary wiring authored in `.tscn` (buttons, Area2D enter/exit) | Fine at the edge; don't build cross-module buses out of them (no compile-time checking). |
| **Autoload singletons** (`static Instance` set in `_EnterTree`) | True globals only. Current: `DamageNumberManager`, `ShakeCamera2D`, `CharacterController.LocalPlayer` (both claimed by the Player-side body only — a Foe-side fighter never touches them, §4a), `FeelDirector` (v0.11.2 — it must outlive every arena, as the cue bus it listens to does; §4b), `VfxDirector` (v0.11.6 — it must outlive every arena, as the bus it listens to does; its pooled nodes are its own children, so no scene owns or frees them; §4b), `AudioDirector` (v0.11.7 — it must outlive every scene: the music plays across scene changes and the cue bus it listens to is static; it is dormant in a headless run unless `FABLELAND_AUDIO` asks; §4b); planned: `RunState`, `BalanceOverrides` | Cap the list. Each new one needs a written justification here. A singleton that parents nodes into the live scene must do it via `GetTree().CurrentScene` (survives reloads — existing pattern). |
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
6. HP application + events        (HpChanged, popups, ult-charge credit; then FEEDBACK:
                                   resolve the hit's weight, hold the victim (hit-stop),
                                   raise ONE HitCue — flash and shake are listeners', §4b)
7. Reactions                      (knockback AddImpulse, stun window, on-hit passives:
                                   thorns/Kashaya, lifesteal/Zhen Qi, stackable effects)
```

New effects declare their stage. Lifesteal reads the **post-mitigation** number from
stage 6 (the GDDs are explicit about post-mitigation — FanChen's Heart, Kashaya).
Never compute damage twice in two places to "simplify" — one pipeline, tap points.

## 4a. Sides, targets and controls (v0.10.11)

Three small contracts make one kit usable by the player and against the player.

**Sides.** Every fighter is on a `CombatSide` — `Player` or `Foe`. `CharacterController.Side`
is a virtual property: a protagonist is `Player`; a BOSS class overrides it to `Foe`, and the
base does the rest from that one fact:

| | Player side | Foe side |
|---|---|---|
| Group | `player` | `enemy` (not `foe`: that is the spawn cap and the foe sweeps) |
| Collision layer | Player (1) | Foes (2) — where the player's projectiles look |
| `LocalPlayer`, camera, shake camera | claims them | never; the scene's camera is removed in `_EnterTree`, before it can enter the tree |
| Reads | the keyboard (`PlayerControls`) | a `VirtualPad`, from the first frame |
| Post-hit i-frames | 0.8 s after a hit that shoves | none; poise scales the shove and the gain-no instead |
| On 0 HP | `Died` → lives / run end | `Died`, then fades and frees itself (`OnDefeated`) |

**Targets.** Anything an attack can land on is an `ICombatTarget` (`BaseFoe`, and every
`CharacterController`). Attack code never casts to a class and never names a group:

```csharp
foreach (ICombatTarget t in CombatTargets.HostileTo(GetTree(), Side)) { … t.TakeHit(hit, origin); }
if (CombatTargets.TryGetHostile(body, _side, out ICombatTarget t)) { … }   // a projectile that found a body
CombatTargets.AimMask(this, side);                                          // …and where such a projectile looks
```

Groups say where to look; the side says whom to hit — `HostileTo` filters on both, so a body
in the wrong group is still never offered to its own side. A new reaction (a status, a shove)
is added to the interface once, not at each attack site. A projectile takes its side from
whoever launched it (PumpKing's head does; the other kits' projectiles are Player-side until
their protagonist has a BOSS variant, each marked `NOTE(boss)`).

**Controls.** A fighter reads `Controls` (`ICharacterControls`): `Held` / `JustPressed` /
`JustReleased` per `ControlAction`, an aim point, a facing hint. **No kit touches `Input` or
the mouse** — grep `Input\.` under `Scripts/Protagonists` and `Scripts/Gameplay/Characters`
must find nothing (the throwaway `Legacy/Player.cs` aside). A `VirtualPad` is stepped once per
process frame and once per physics tick by whoever owns it, *before* the body runs
(`BossBrain` does it with a lower process priority), and a tap is owed to each loop
separately — the two do not run in step, so a jump tapped between physics ticks is still seen.
The rule binds `CharacterController` itself, not only the kits: swimming (v0.11.1) reads
`ControlAction.MoveUp` / `MoveDown`, so a boss in a soft volume swims on its pad and the
player's Up and Down leave it alone (`BossFightCheck.ProbeSwimmingTakesSides`).
`PlayerControls` is the only reader of the InputMap for a fighter, so it is also what makes
sure every action it names exists: `move_up` is not in `project.godot`, and
`InputDefaults.Ensure()` — called from its constructor — registers it.

## 4b. The cue bus (v0.11.2)

How gameplay tells presentation that something happened, without knowing who listens. Design,
numbers and the full cue vocabulary: `Docs/Feedback.gdd`.

```
GAMEPLAY      CueBus.Emit(body, "ba.swing", variant: stage, size: reach, …)     one-shot
              CueBus.Start(body, "s1.tornado", …)  …  CueBus.Stop(body, "s1.tornado")   a loop
              CueBus.EmitHit(new HitCue(…))        — the two hit intakes only, once per hit
PRESENTATION  a director subscribes to CueBus.Raised / HitLanded / LoopStarted / LoopStopped
              in _EnterTree, unsubscribes in _ExitTree, and looks each cue up in its own
              CueTable<T>: (owner, action) → (the owner's class, action) → ("any", action)
```

- **The contract is five types** in `Scripts/Gameplay/Combat/Cue.cs`: `Cue`, `HitCue`,
  `CueBus`, `IFeedbackBody` (what presentation may know of a body — read-only or purely
  visual), `CueTable<T>` (the one lookup every director uses). A cue is a struct keyed by
  two constant strings; raising one allocates nothing (T40 §2).
- **A raiser never reads anything back, a listener never changes a fight.** Listeners draw
  their own randomness — never a run's `DetRandom`. A listener that throws is logged
  (`GD.PushError`) and skipped; the hit lands and the other listeners still hear (§5).
- **Hit-stop is not on the bus.** It moves bodies in time, so it is gameplay: a body applies
  it to itself (`ApplyHitStop`) from its hit intake and its own melee helper, with numbers
  from `Scripts/Data/FeedbackTable.cs`. It is deliberately absent from `IFeedbackBody`.
- **`HitInfo` carries feedback metadata** (`Attacker`, `CueOwner`, `CueAction`, `CueVariant`,
  `Weight`, `Ranged`) that damage math never reads. An attack names itself with
  `.From(this, "ba", stage)` or `.FromRanged(owner, "s2.seed")`; an unnamed hit still works.
- **One owner per visual property of a body.** `Sprite.Modulate` / `SelfModulate` are the
  status, telegraph and evolution tints; `Sprite.Visible` is the i-frame blink; the root's
  `Modulate.a` is the defeat fade; the hit flash is a material lent to the sprites for the
  frames it lasts (`BodyFlash`). A new effect on a body takes a channel of its own or goes
  through the owner of an existing one — it never writes someone else's.
- **Adding a director** (VFX, audio): an autoload under `Scripts/Presentation/`, one line in
  `project.godot`, a `CueTable` of its own rows, and no edit to any gameplay file. Its
  justification as a singleton is the same as `FeelDirector`'s (§1).
- **The VFX director** (v0.11.6, `Docs/VFX.gdd`) is the second listener. It resolves a cue
  to a `VfxDef` — an *archetype* rendered by a *style*, one style per art medium — in the
  medium of whoever caused it: the attack's owner for a hit, the body for its own effects,
  the arena (the tag of `flow`/`arena.enter`) for dust, water, hazards and ambience. It
  keeps a small context blackboard (combat intensity, combo, boss phase) and reads the
  source body's `FeedbackState`, so a row can change with the fight; nothing it reads is
  ever written back. A cue that carries a cone packs it in `Extents` (`CueShapes.Cone`);
  `arena.enter`'s `Source` is the arena, and a listener ends what belongs to the arena when
  that node leaves the tree.
- **The audio director** (v0.11.7, `Docs/Audio.gdd`) is the third. A sound is a row in
  `SfxTable` (`CueTable<SfxDef>`), resolved exactly as `FeelTable` is; it adds nothing to the
  five-type contract. The UI raises cues too: `UiCues.Attach(screen)` wires every button to
  `ui`/`hover` · `press` · `back`, and a screen raises its own `flow`/`<screen>.enter`.
  `IFeedbackBody.FeedbackState` is polled eight times a second for the body the player wears
  (low HP is not a cue) — still read-only.
- **Adding a cue**: one `CueBus.Emit` at the line where the moment happens, a row in
  `Docs/Feedback.gdd` §6, and a line in `FeedbackCheck`.

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

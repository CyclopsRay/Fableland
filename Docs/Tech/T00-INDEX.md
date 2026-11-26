# Fableland — Technical Rules (Index)

The engineering law of the project, one level deeper than
`Docs/Instructions/20-ENGINEERING.md` (which says *what* the architecture is; these
docs say *how to keep it healthy as it grows*). Read the department doc first, then
the tech doc for the concern you're touching.

| File | Concern |
|------|---------|
| `T00-INDEX.md` | This map + the module dependency law |
| `T10-MODULE-CONTRACTS.md` | Module boundaries, who may call whom, events vs singletons, scene↔script contracts, error handling |
| `T20-EXTENSIBILITY.md` | How to make every system appendable: registries, def/behavior split, pipelines & hooks, deprecation |
| `T30-FEATURE-BLUEPRINTS.md` | Concrete implementation designs for the next milestones (RunState, foe FSM, missions, items, save/load, scene flow) |
| `T40-PERFORMANCE.md` | Lag, GC pressure, pooling, physics/render budgets, profiling without a toolchain |

---

## The module dependency law

Modules are arranged in layers. **An arrow may only point downward.** Anything else is
a design error to fix before merging, because upward or sideways dependencies are what
make a codebase impossible to extend later.

```
┌────────────────────────────────────────────────────────────────┐
│ PRESENTATION   Hud, DamageNumberManager, ShakeCamera2D,        │
│                the DIRECTORS — FeelDirector (shake, flash,     │
│                zoom), then VFX and audio — each listening to   │
│                the cue bus with a table of its own,            │
│                map atlas render (MapControllerAtlas/RenderModel)│
│                — observes via events/signals; never ticked by  │
│                  gameplay logic, never queried by it            │
├────────────────────────────────────────────────────────────────┤
│ ORCHESTRATION  GameManager (arena loop), MapController          │
│                (exploration loop), Mission objects, RunState    │
│                — wires layers together; owns scene flow         │
├────────────────────────────────────────────────────────────────┤
│ GAMEPLAY       CharacterController + characters, Foes/,         │
│                Bosses/ (brain + boss classes), Items/ runtime,  │
│                Hazard family, projectiles, SoftVolume,          │
│                DecayingDebuff, HitInfo, combat sides/targets,   │
│                CueBus + Cue / HitCue (raised here, heard above),│
│                hit-stop (HitStopClock — it moves bodies)        │
│                — self-contained combat sim; knows nothing of    │
│                  maps, days, UI, effects or sound               │
├────────────────────────────────────────────────────────────────┤
│ DOMAIN DATA    Scripts/Data/ tables (incl. BossTable,           │
│                FeedbackTable), MapGenerator+MapData, FoeStats,  │
│                DetRandom                                        │
│                — pure C#, no Godot node types, unit-testable    │
├────────────────────────────────────────────────────────────────┤
│ FOUNDATION     Units.cs, GameVersion.cs, FeedbackSettings.cs    │
└────────────────────────────────────────────────────────────────┘
```

Consequences you will feel:

- **Combat never reads the map.** The arena receives `(nodeLevel, missionType, day)`
  — three values through RunState — and derives everything. This is what keeps the
  arena testable standalone (F5 on `Arena.tscn` must keep working forever).
- **The map never names a foe class.** It stores `NodeKind` + level; which foes spawn
  is the arena orchestrator's lookup. A boss is named the same way: `RunState` looks the
  node's world up in `BossTable` and hands the arena an id (`AdventureContext.BossId`); the
  arena never asks which world it is in.
- **Kits never read input, and never name a side.** A character reads `Controls` and asks
  `CombatTargets.HostileTo(Side)`; whether a keyboard or a boss brain is on the other end,
  and whom "hostile" means, is not the kit's business (T10 §4a). This is what lets a boss be
  its protagonist's class.
- **UI is write-only from below.** Gameplay raises events (`HpChanged`, `Died`,
  `UltChargeChanged` — the existing pattern); presentation subscribes. Gameplay code
  containing `GetNode<Hud>` is a violation.
- **Gameplay raises cues; it never names an effect, a sound or a director** (v0.11.2,
  T10 §4b, `Docs/Feedback.gdd`). A kit says `CueBus.Emit(this, "ba.swing", …)` with the
  attack's own numbers and forgets it; what that looks, sounds or feels like is a row in a
  director's table. `grep -rn "Director" Scripts/Gameplay Scripts/Foes Scripts/Protagonists
  Scripts/Hazard Scripts/Missions` must find nothing. The one part of feel that moves bodies
  in time — hit-stop — is therefore gameplay, with its numbers in `FeedbackTable`.
- **Data files never `using Godot`** (beyond math structs if unavoidable — prefer
  `System.Numerics` or plain floats). The day this rule breaks, headless unit testing
  dies with it.

## The five standing tech rules

1. **Additive over invasive.** New content must land by *adding* files/entries, not by
   editing switch statements in core files (T20 §1). If a feature forces edits across
   >2 modules, stop and find the missing abstraction.
2. **One owner per piece of state.** Every mutable fact (HP, day, stamina, item CD)
   has exactly one writer module; everyone else reads or listens. Duplicate state =
   future desync bug.
3. **Explicit order for everything that has order.** Damage pipeline stages, day-end
   resolution steps, map generation passes — always an ordered, named list in one
   place, never emergent from call sites.
4. **Fail loudly in debug, degrade gracefully in release.** Boot-time validation of
   data tables; `GD.PushError` + early-return at runtime (never crash a run for a
   content bug — this is a permadeath game; a crash *is* lost progress).
5. **Determinism is a dependency-law citizen:** anything below ORCHESTRATION that
   needs randomness takes a `DetRandom` in, never reaches out for one.

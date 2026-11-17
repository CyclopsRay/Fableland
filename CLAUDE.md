# CLAUDE.md — Fableland (Godot 4.7 / .NET C#)

The Godot port of **GloryOfFableland** (Unity). A 2.5D arena fighter; the current
playable vertical slice stars **Pomegraknight**. Full plan, controls, and file map
live in `Migration.md` (see §0 for the delivered prototype).

## Read first — every session
- **`KNOWLEDGE.md`** — engineering conventions AND a running list of Godot/C# caveats
  learned from past bugs. **Read it before writing or changing code** so you don't repeat
  a mistake we've already hit and fixed.
- `Migration.md` §0 — what's built, how to run (Godot 4.7 .NET, F5 → `Scenes/Arena.tscn`),
  controls, and how each simplification maps to the full port.
- **`Docs/Instructions/00-OVERVIEW.md`** — the studio operating manual: roadmap, golden
  rules, and the change workflow, with per-department docs alongside it (design,
  engineering, data/balance, QA, art/audio, production, merchandising). Follow its
  change workflow when adding any feature.
- **`Docs/Tech/T00-INDEX.md`** — the technical law: module dependency layers, contracts,
  extensibility rules, feature blueprints (RunState, foes, missions, items, save),
  and performance budgets. Read the relevant T-doc before building a new system.
- `Docs/IDEAS.md` — the design idea ledger (non-canon). New content ideas land there
  first and graduate into GDDs via the change workflow.
- **`Docs/Art/BeachTileSet.md`** — beach combat-map tile catalog, sprite provenance,
  footprints/effect areas, style lock, and the remaining terrain-atlas slicing work.
- **`Docs/Art/RealmStyleBible.md`** — every other realm's combat-map art direction (one
  medium per world), tile kits, arena grammar, and the rules a tile and a map must obey.
  Read it before adding a tile, repainting art (`Tools/realm_art/`) or composing a map
  (`Tools/realm_maps/`).

## Mandatory workflow
- **After fixing ANY bug, add a caveat to `KNOWLEDGE.md`** (symptom → rule → why) under
  "Caveats / gotchas", tagged with the version. This is how future chats avoid re-making
  the same mistake — do not skip it.
- **Versioning:** bump the patch (+0.0.1) on **every commit**. Keep these three in sync:
  `Scripts/Foundation/GameVersion.cs`, the repo-root `VERSION` file, and the HUD `VersionLabel` in
  `Scenes/Hud.tscn`. The version renders top-left in-game.
- **Verify before committing.** The dev host may have no Godot/.NET toolchain, so at minimum
  check statically (brace/paren balance, `.tscn` resource paths resolve, `GetNode` paths
  match scenes). Remember static checks miss type/API-existence errors — do a real build
  when a toolchain is available (`dotnet build Fableland.csproj`; for map/tile work also the
  headless content check in `Docs/Instructions/40-QA.md` §(2); for bosses, kits, input, hit
  intake or slopes also the boss check, `res://Scenes/Bosses/BossFightCheck.tscn`; for the
  world pool, map generation, saves, the run flow or a shipped map also the run check,
  `res://Scenes/Run/RunWalkCheck.tscn` — it plays real runs and never touches `user://`).

## Conventions (details in KNOWLEDGE.md / Units.cs)
- **Units:** 32 px/m; player 2 m, jump 8 m, 1 s ground jump ⇒ g = 2048 px/s², jump 1024 px/s.
  Derive from `Scripts/Foundation/Units.cs`, don't hardcode.
- **Collision layers:** 1 Player, 2 Foes, 3 Ground, 4 Platform, 5 Projectile, 6 Hazard.
- **Two platform types:** thin one-way `platform` vs enterable `SoftVolume`.
- **Architecture:** characters subclass `CharacterController`; abilities are `HandleBA` /
  `HandleSkill1/2` / `HandleSkillUlt`; animation is stubbed via `UpdateAnimator` (empty
  `AnimationPlayer` + `// NOTE(animation)` markers).
- **Sides, targets, controls (v0.10.11):** a fighter is on a `CombatSide`; a BOSS is its
  protagonist's class on the Foe side (`BossPumpKing : PumpKing`, `Docs/FOES.gdd` §8). Kits
  read `Controls` and hit `CombatTargets.HostileTo(GetTree(), Side)` / `ICombatTarget`.

## Don'ts
- Don't rename/move scripts or scene nodes without updating `.tscn` `ext_resource` paths and
  `GetNode`/`NodePath` references, or exported-property assignments in scenes.
- Don't shadow inherited members (e.g. a field named `Gravity` on an `Area2D` subclass).
- Don't read `Input` or the mouse, or cast a target to `BaseFoe`, inside a character kit — a
  boss brain presses the same buttons, and a boss is not a `BaseFoe`.
- Don't commit the GitHub PAT into the repo or bake it into a saved git remote.

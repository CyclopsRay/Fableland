# KNOWLEDGE.md — Fableland engineering knowledge

Durable, cross-session knowledge for working on this Godot 4.7 (.NET/C#) project.
**Read this before writing code.** When you fix a bug, add a caveat below so the
next session doesn't repeat the mistake (see the workflow rule in `CLAUDE.md`).

For architecture, controls, and the file map, see `Migration.md` §0.

---

## Conventions

**Units / physics** (`Scripts/Units.cs`) — derive everything from these, don't hardcode:
- Scale is **32 px/m**. A player is **2 m** (64 px) tall; jump height **8 m**; a ground
  jump takes **1 s** (0.5 s up / down). ⇒ gravity **64 m/s² = 2048 px/s²**, launch
  speed **32 m/s = 1024 px/s**.

**Collision layers** (1-based numbers; names fixed):
`1 Player, 2 Foes, 3 Ground, 4 Platform, 5 Projectile, 6 Hazard`.
Bitmask: Player=1, Foes=2, Ground=4, Platform=8, Projectile=16, Hazard=32.
- Player mask = Ground|Platform (12); Foe mask = 12; Seed mask = Foes|Ground|Platform (14).
- Ground and Platform are **distinct**. Platforms are one-way; drop through by pressing
  down (Platform bit toggled off the mask ~0.25 s, hold = fall further). Ground is solid.

**Two platform archetypes:**
1. thin one-way **platform** — land/cross/drop-through (a `StaticBody2D` with
   `one_way_collision = true` on its shape).
2. **SoftVolume** (`Scripts/SoftVolume.cs`) — enterable "go-inside" volume (tree/bush/…);
   Area2D field + one-way top; inside, falling halts and up/down move like left/right,
   capped by StagnationIndex (0.5·maxSpeed) plus a constant GravityIndex drift
   (0.1·maxSpeed). Applies to players AND foes.

**Versioning** — bump patch (+0.0.1) every commit; keep `Scripts/GameVersion.cs`, the
repo-root `VERSION` file, and the HUD `VersionLabel` (`Scenes/Hud.tscn`) in sync. Shown
top-left in-game.

**Verification** — the dev host has **no Godot/.NET toolchain**, so edits are checked
statically (brace/paren balance, `.tscn` resource paths, `GetNode` paths). This does NOT
catch API-existence or type errors — do a real build when possible, and log anything the
compiler surfaces below.

---

## Caveats / gotchas (grow this on every bug fix)

### Godot 4 has no `Label2D` (v0.1.3)
- **Symptom:** `The type or namespace name 'Label2D' could not be found`.
- **Rule:** Godot 4 has `Label` (Control) and `Label3D` — no `Label2D`. For world-space 2D
  text, use a **`Node2D` with a `Label` child** (Node2D world-space is camera-tracked; the
  Label rides along), or draw text via `_Draw`/`DrawString`. A bare Control in a `CanvasLayer`
  is screen-space and won't track the camera.

### Don't name fields after inherited members (v0.1.3)
- **Symptom:** `'PomeSeed.Gravity' hides inherited member 'Area2D.Gravity'`.
- **Rule:** `Area2D` already exposes `Gravity` (and other area-physics props). Don't shadow
  inherited members — pick a distinct name (e.g. `FallGravity`). Watch this on any
  `Area2D`/`RigidBody2D` subclass.

### Call `Init(...)` after `AddChild`, not before (reference)
- Godot runs `_Ready()` synchronously during `AddChild`. Spawners set a projectile's
  velocity/config via an `Init(...)` method called **after** `AddChild` — so guard against a
  0-lifetime free on the first `_PhysicsProcess` and don't rely on Init'd fields in `_Ready`.

### Autoload C# singletons (reference)
- A C# autoload is registered as `Name="*res://Scripts/Name.cs"` in `project.godot`; set the
  static `Instance` in `_EnterTree`. Autoloads persist across `ReloadCurrentScene`, so a
  manager (e.g. `DamageNumberManager`) that parents nodes into `GetTree().CurrentScene`
  stays valid across restarts.

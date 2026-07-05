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

**Movement / combat model** (`CharacterController`, `Enemy`, `HitInfo`):
- Velocity = **`intentVel`** (self-directed: input, gravity, jump, or the SoftVolume field) +
  **`externalVel`** (impulses — knockback/wind — that decay via `ExternalDamping`). This is what
  gives external forces "room" to coexist with player control.
- Horizontal intent has **momentum** (accelerate toward `MoveSpeed` via Ground/Air Accel; stop via
  Ground/Air Friction). Vertical intent integrates gravity (has momentum) and jump sets it.
- Knockback = **`AddImpulse(deltaV)`** (a delta-v; force-over-time = per-tick impulses, e.g. tornado).
- Hits are authored per skill via **`HitInfo { Damage, Knockback (delta-v), Stun }`**. `Stun < 0`
  means the default **gain-no** window `Units.StunPerDamage · Damage` (0.005·dmg); during it the
  receiver can't act and its **animation is frozen** (knockback/gravity still move it).
- Inside a **SoftVolume**, external impulses are extra-damped by `ExternalDampingMult` (viscous), so
  knockback still shoves you in but fades fast.

**Jumps** — per-character `MaxJumps` (jumps before touching down again; Pomegraknight = 1),
refreshed on touching ground / platform / SoftVolume. A universal `JumpCooldown` (0.3 s) is the
minimum gap between jumps; override per character only when explicitly specified. **Coyote
time** (`CoyoteTime`, 0.2 s): leaving a surface doesn't cost a jump for that long (edge jumps
still work), but if it elapses with no manual jump, one jump charge is forfeited once — see the
caveat below for why this needed its own bookkeeping beyond "refresh on floor".

**Hazards / statuses** (`Hazard.cs`, `DecayingDebuff.cs`) — a `Hazard` is a stationary Area2D
whose collision box is built from `BoxSize` in code (never authored separately in the `.tscn`),
so the hittable shape can't drift from the drawn telegraph. While a body overlaps, it ticks
every `TickInterval` (0.25 s default) via `Hazard.Deliver(...)`, which routes to
`CharacterController.ApplyHazard`/`AddFireStack`/`AddFrozenStack` or the equivalent `Enemy`
methods. **`ApplyHazard` deliberately bypasses the combat i-frame gate** (`TakeHit`'s 0.8 s
invuln) — a hazard reapplies every 0.25 s, faster than that window, and "standing in the fire"
should keep hurting you the way a discrete punch's i-frames shouldn't. One-shot hazards (e.g.
`TsunamiHazard`) go through the normal `TakeHit` instead, explicitly opting into invuln/knockback
gating since they're meant to land once.
`DecayingDebuff` is the stack behind **OnFire** and **Frozen** (not necessarily debuffs — OnFire
is a buff): integer "points", capped at `MaxStack` (99), added flat on each hit (stackable),
decaying 10%/0.2s (`Mathf.Ceil`, so it always reaches exactly 0, never asymptotes) with the
decayed amount dealt as damage. While active: OnFire multiplies **`MoveSpeed` only** (not
accel/friction — deliberately left alone so this doesn't complicate momentum tuning) by 1.2 and
adds +0.2 to `DamageDealtMultiplier` (an aggregatable `Dictionary<string,float>` keyed by source
— multiple buffs sum, e.g. 0.2 + 0.3 → 0.5); Frozen multiplies `MoveSpeed` by 0.8 and adds +30 to
an aggregatable **defense** pool (`_defenseBonuses`, same dictionary-by-source pattern).
**Defense → damage-taken multiplier is `100/(100+defense)`** (`DefenseMultiplier`; base defense
is 0, so no status = no mitigation), applied to everything taken: hits, hazard ticks, and
Burn/Fire/Frozen's own self-damage.

**Versioning** — bump patch (+0.0.1) every commit; keep `Scripts/GameVersion.cs`, the
repo-root `VERSION` file, and the HUD `VersionLabel` (`Scenes/Hud.tscn`) in sync. Shown
top-left in-game.

**Verification** — the dev host has **no Godot/.NET toolchain**, so edits are checked
statically (brace/paren balance, `.tscn` resource paths, `GetNode` paths). This does NOT
catch API-existence or type errors — do a real build when possible, and log anything the
compiler surfaces below.

---

## Caveats / gotchas (grow this on every bug fix)

### Hit tests must use the target's radius, not just its center (v0.1.5)
- **Symptom:** a melee/AoE that visibly clips a foe dealt no damage — the check only tested
  the foe's center point against the range/cone.
- **Rule:** approximate the target as a circle of `HitRadius` and test overlap: reach passes if
  `dist - r <= range`, and widen the cone half-angle by `asin(r/dist)` (foe fully covers origin
  when `dist <= r`). "It touches the foe" should mean the shape overlaps, not the center.

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

### Air jumps need coyote time, not just an on-floor refresh (v0.2.0)
- **Symptom:** a 1-jump character (`MaxJumps = 1`, e.g. Pomegraknight) could jump at any
  point during a fall, not just right at the edge — because `_jumpsRemaining` only reset
  on `IsOnFloor()` and only decremented when a jump was actually pressed. Walking off a
  ledge without jumping left the single jump charge sitting unused indefinitely, so it
  could be spent airborne like a free double-jump/tornado-kick.
- **Rule:** track `_airborneTimer` since leaving a surface. Within `CoyoteTime` (0.2s) a
  jump still works normally (edge jump preserved). If that window elapses **without** a
  manual jump (`_jumpedSinceGrounded` stays false), forfeit one jump charge once
  (`_coyoteConsumed` guards against repeating it). Touching ground/platform/SoftVolume
  calls `RefreshJumps()` which resets all three fields. This makes `MaxJumps` mean "jumps
  before your foot has been off a surface for a beat," not "jumps since the last button
  press," so multi-jump characters keep their real air jumps while 1-jump characters can't
  jump mid-air past the grace window.

### A `CollisionShape2D`'s size doesn't make the sprite match it (v0.2.1)
- **Symptom:** Pomegraknight's `CollisionShape2D` was 44×64 (not square) but she rendered as
  a visible square in-game.
- **Rule:** `Sprite2D` draws its texture at native pixel size regardless of any sibling
  `CollisionShape2D` — resizing the collision shape alone does **not** reshape what's on
  screen. The `.svg` placeholder itself (`player_placeholder.svg`) was a 64×64 square; the
  collision box's 44-wide value was never reflected visually. When a character's proportions
  matter, resize the actual texture (or apply `Sprite2D.Scale`) *and* the collision shape
  together — check both, not just the shape.

### Trauma-based shake needs to be *tuned*, not just wired (v0.2.2)
- **Symptom:** `ShakeCamera2D.AddTrauma` was called on both landing a hit and taking one, but
  the screen never visibly shook — looked like shake wasn't implemented at all.
- **Rule:** offset scales with `trauma²`, so small trauma additions (0.12-0.35 against a
  `MaxOffset` of 14px) round-trip to a sub-2px, sub-0.1s flicker — mathematically present,
  perceptually nothing. If a juice effect "isn't working" despite the call sites being
  correct, check the actual output magnitude/duration before assuming the wiring is missing.
  Bumped to `MaxOffset=30`, `Decay=3` (from 4.5), and raised trauma per event (0.22-0.5) so a
  hit reads as a hit.

### Call `Init(...)` after `AddChild`, not before (reference)
- Godot runs `_Ready()` synchronously during `AddChild`. Spawners set a projectile's
  velocity/config via an `Init(...)` method called **after** `AddChild` — so guard against a
  0-lifetime free on the first `_PhysicsProcess` and don't rely on Init'd fields in `_Ready`.

### A full-screen background `Control` eats clicks meant for `_UnhandledInput` (v0.2.4)
- **Symptom:** on the Map scene the player token would not move — clicks never reached
  `MapController._UnhandledInput`.
- **Rule:** a `Control` (here a full-rect `ColorRect` background in a `CanvasLayer`) has
  `mouse_filter = STOP` **by default**, so it consumes the click as GUI input and marks it
  handled — `_UnhandledInput` then never fires, even though the ColorRect is in a
  `layer = -1` CanvasLayer behind everything. Set **`mouse_filter = 2` (IGNORE)** on any
  decorative/background Control (and on non-interactive `Label`s overlapping the play area)
  so world clicks pass through to `_UnhandledInput`. Only genuinely interactive controls
  (buttons, line edits) should keep STOP.

### World map / rogue-like meta-layer (reference, v0.2.3)
- The overworld map lives in `Scripts/Map/` + `Scenes/Menu.tscn` / `Scenes/Map.tscn`;
  full spec in **`Docs/MapGDD.md`**. `project.godot` `main_scene` now boots **Menu**
  (Arena is still the fighter scene, reachable later).
- **All map randomness goes through `DetRandom`** built from the 8-char run seed — never
  Godot's global RNG or `System.Random` for gameplay, or the seed stops reproducing the map.
  (`DetRandom.NewSeed()` uses `System.Random` *only* to mint a fresh seed for the dice button.)
- **The map has NO `Camera2D` (as of v0.3.3).** `MapController.Project(world)` maps world→screen
  itself (rotate so heading is up → foreshorten/tilt → zoom → pin the focus at an anchor), and
  everything is drawn in screen space through it. This is what lets the tilted view modes rotate
  the map while node markers/labels stay upright (a Camera2D rotates/squashes the icons too).
  So hit-testing compares `InputEventMouseButton.Position` (screen) against **`Project(node.Pos)`**
  — do NOT reintroduce `GetGlobalMousePosition()`/a camera without revisiting this. Wheel = zoom
  (`_zoom`); right/middle-drag = pan (`_pan`, Flat mode only). UI is a `CanvasLayer`, consumed
  before `_UnhandledInput`. **An earlier v0.3.2 note here said the map used a Camera2D — that was
  reverted; ignore any stale mention.**
- Generation order matters: combat nodes → intra-world edges → inter-world edges → **zone 6**
  → function nodes. Zone 6 is built *before* the function pass but its edges are
  `Visible=false`, which is also what excludes them from the crossing/probability passes.
- Content of nodes (fights, shelter/question-mark effects) is **not implemented** — nodes
  differ by icon only. Difficulty/reward scaling by level is documented, not coded.

### Rendered "atlas" map view (reference, v0.3.2)
- Two views, toggled by the **View** button (`_rendered`, defaults to atlas): the original
  **schematic** (`MapController._Draw` fall-through) and the **atlas** (`MapControllerAtlas.cs`,
  a partial of `MapController`). Both read the SAME `MapGraph` + live state — the atlas adds no
  gameplay, only a rendered layer.
- `MapRenderModel.Build(graph)` precomputes the atlas (`MapRenderModel.RenderedMap`) after
  `MapGenerator.Generate`: per-realm **weighted Voronoi (power-diagram)** territories (cell area
  set by `ClaimRadius(kind)²` — combat big, function small), clipped to a convex island wedge via
  `ClipHalfPlane` (Sutherland–Hodgman; each cell edge is tagged with the neighbour site that made
  it, so a shared border is a **road** if a graph edge links the two, else a themed **barrier**).
  Zone 6 is a central **pentagon** with 5 lv5 territories; XX-S shelters are isolated sea islets;
  cross-realm edges are **golden causeways**. Determinism preserved (jitter uses `DetRandom(seed+"R")`).
- **`MapGenerator.LayoutScale` (1.8) blows the whole map up uniformly.** A uniform scale preserves
  every crossing/midpoint, so a given seed yields the identical map, just bigger — safe to change.
  `MapGenerator.RimRadius` is the outer playable radius (used to size islands + the schematic wedge).
- Barriers are **marked, not arted** (per current scope), and each realm has TWO kinds:
  **AREA** = a thick themed terrain belt drawn along every disconnected frontier (reads as
  terrain: lake / desert / bamboo forest…), one name label per realm; **POINT** = a small
  landmark diamond on a *blocked would-be road* (a `MapGraph.FailedCandidates` pair that also
  shares a territory border — the adjacency filter in `Build` via `barrierMid` kills the label
  spam), name shown once per realm. Both live in `MapRenderModel.Themes` (`.AreaBarrier` /
  `.PointBarrier` names, `.AreaColor` / `.PointColor`).
- **Barrier vs road classification treats a function node as a road HUB** (v0.3.3): the generator
  splits `city→city` into `city→fn→city`, so `Build` also links every pair of a function node's
  neighbours in the `linked` set — otherwise the shared border reads as a false barrier even
  though a road runs through the shelter/`?`. Don't remove this or the "connected but walled-off"
  bug returns.
- **Edge-levels 1 & 2 are guaranteed both a shelter AND a `?`** (`MapGenerator`, pass (e)): those
  levels fire rarely (10% / 25%), so a post-pass adds any missing kind on a random un-split edge.
- **Entering ANY zone-6 node = crossing the singularity** (`MapController.EnterVoid`, v0.3.3):
  `_inVoid` latches, **all `WorldIndex != -1` nodes are devoured at once** (no gradual outer
  devour once inside — it's one-way), and the day readout shows **`???`** (time unknowable). The
  atlas renders devoured land as dim "dead ruins" (not black) so the explored map stays legible.
  Lore + rule in `Docs/MapGDD.md` §7.
- **View modes (v0.3.3)**, cycled by the Cam button: `Flat` (top-down, pan/zoom), `BossUp`
  (tilted, map spins so the central VOID/boss is always up, player pinned near the bottom),
  `HeadingUp` (tilted, spins so the last step taken points up — set `_lastMoveDir` in `TryMove`).
  Rotation/tilt are smoothed in `_Process`; tilt is an affine vertical foreshorten (`_tilt`),
  NOT true perspective — true book/trapezoid perspective would need a SubViewport→3D quad.
- `DrawColoredPolygon` assumes **convex** polygons — all atlas cells/islands/pentagon are convex
  (convex clip ∩ half-planes), so don't feed it a concave polygon or the fill renders wrong.

### Getting a `Font` for `_Draw` without a Control (reference, v0.2.3)
- **Symptom risk:** `ThemeDB.Singleton.FallbackFont` is easy to get wrong across binding
  versions and there's no toolchain here to catch it.
- **Rule:** from any `Control` you already have a node for, call `GetThemeDefaultFont()` and
  cache the `Font` — reliable for `DrawString` from a `Node2D._Draw`. `MapController` pulls it
  off its `InfoLabel`.

### Autoload C# singletons (reference)
- A C# autoload is registered as `Name="*res://Scripts/Name.cs"` in `project.godot`; set the
  static `Instance` in `_EnterTree`. Autoloads persist across `ReloadCurrentScene`, so a
  manager (e.g. `DamageNumberManager`) that parents nodes into `GetTree().CurrentScene`
  stays valid across restarts.

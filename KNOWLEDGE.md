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

**ATK → damage-dealt multiplier is `(100+ATK)/100`** (base ATK is 0, so 0 ATK = 1× damage).
ATK is a flat additive stat, same as DEF. Both are permanently increased via shelter Blessing
actions (Sharpen Weapon: +10 ATK; Sharpen Armor: +10 DEF). Unlike the temporary Frozen DEF
bonus (which uses the `_defenseBonuses` dictionary), permanent ATK/DEF from shelter Blessings
are stored separately and persist across the entire run.

**Versioning** — bump patch (+0.0.1) every commit; keep `Scripts/GameVersion.cs`, the
repo-root `VERSION` file, and the HUD `VersionLabel` (`Scenes/Hud.tscn`) in sync. Shown
top-left in-game.

**Verification** — the dev host has **no Godot/.NET toolchain**, so edits are checked
statically (brace/paren balance, `.tscn` resource paths, `GetNode` paths). This does NOT
catch API-existence or type errors — do a real build when possible, and log anything the
compiler surfaces below.

---

## Caveats / gotchas (grow this on every bug fix)

### A debug-override export must be gated behind "no run exists", not behind its own value (v0.5.0)
- **Symptom:** `GameManager.FoeLevel` used `DebugFoeLevel > 0 ? DebugFoeLevel : LevelForDay(day)`
  unconditionally. The export's *default* is 1 (so direct-F5 arenas work out of the box), which
  means every REAL run's fight would silently spawn level-1 foes forever — the debug knob's
  convenient default clamped live gameplay.
- **Rule:** any debug override that ships with a non-zero/enabled default must be consulted
  **only when no run exists** (`CurrentAdventure == null`), not merely when it's "set" — a
  default value is indistinguishable from an intentional one. Pattern:
  `value = hasRun ? realFormula : (DebugKnob > 0 ? DebugKnob : realFormula)`.
- **Why:** debug knobs default to values that make F5-on-a-scene pleasant; real runs must never
  read them. The same fix also added the zone-6 per-node override (LV5 → foe level 7, LV6 → 8,
  FOES §2) that a plain day-formula misses.

### Group membership is load-bearing — a BaseFoe subclass inherits cap/sweep semantics (v0.5.0)
- **Symptom:** `DestroyObjective` subclasses `BaseFoe` (so player attacks hit it), which
  auto-joins groups `"enemy"` AND `"foe"` in `BaseFoe._Ready`. `FoeSpawner.LiveFoeCount()`
  counts group `"foe"` against the ambient cap (6) — so a LV5 Destroy mission's 5 objectives
  left room for exactly 1 hostile foe, starving the mission of its harassment pressure.
- **Rule:** groups are semantic contracts, not tags: `"enemy"` = "player attacks iterate me",
  `"foe"` = "I count against the spawn cap / mission foe sweeps". A subclass that wants one
  semantic but not the other must `RemoveFromGroup` in `_Ready` **after** `base._Ready()`.
  When adding any new BaseFoe subclass or any new `GetNodesInGroup` consumer, check both
  groups' meanings first.
- **Why:** inheritance buys the hit-test integration for free but silently buys every
  group-based behavior too; the cap dilution had no error, just wrong difficulty.

### Never hand-write `uid="uid://…"` in a `.tscn` (v0.5.0, from the Phase 2 review)
- **Symptom:** new scene files were authored with invented `uid://` strings on `ext_resource`
  lines. Godot generates real UIDs on import; fabricated ones risk collisions and editor
  warnings/re-writes.
- **Rule:** when writing `.tscn` files by hand on a host with no Godot editor, omit `uid`
  entirely and use the numeric-id `ext_resource` style the repo's existing scenes use; let the
  editor add UIDs when the project is next opened.
- **Why:** UIDs are an editor-managed identity namespace, not documentation — inventing them
  is the one way to make two scenes claim the same identity.

### Generating Animation sub_resources from an extracted frame table: derive per-frame texture from the row's own clip name, not a hand-counted segment length (v0.6.0-anim, PumpKing)
- **Symptom (caught pre-commit):** a python generator for `PumpKing.tscn`'s `idle` clip
  assigned each row's source texture by hand-counted segment lengths (`[IDLE1]*17 +
  [SQUEEZE]*11 + [IDLE2]*15`) instead of reading the clip name already present in each
  extracted row. The real boundary was 16/11/16, not 17/11/15 — an off-by-one at the
  first boundary (miscounting the Unity clip's dup-hold frame at `t=1.25` as still
  `Pump_idle_1`) that happened to still sum to the same total row count (43), so a
  naive "does the total match" check would NOT have caught it; only cross-checking the
  per-clip breakdown against the source rows did.
- **Rule:** when generating `AtlasTexture`/`Animation` blocks from an extracted
  frame table that already carries a clip/segment name per row, key the
  texture-selection off **that row's own name field**, never off a manually counted
  span — spans are exactly the kind of number that's easy to miscount by one and whose
  bug is invisible if you only check the grand total. Print a per-clip breakdown
  (`clip name → row count`) and eyeball it against the source `===` sections before
  trusting the output.
- **Why:** a texture mismatch at one boundary silently renders the wrong sprite sheet
  for a stretch of frames — no load error, no crash, just wrong pixels — and the only
  static check available on a no-toolchain host (region-within-texture-bounds) still
  passes because the wrong texture can easily be big enough to contain the (wrong)
  region.

### Hand-authored `Animation` value tracks: update mode lives INSIDE the keys dict (v0.6.0-anim)
- **Symptom (caught pre-commit):** the generated `Pomegraknight.tscn` animations carried a
  standalone `tracks/0/update = 1` property line. That is not a property Godot's `Animation`
  text loader recognizes — editor-saved files express a value track's discrete/continuous
  mode ONLY as the `"update": 1` entry inside the `tracks/0/keys = { ... }` dictionary.
- **Rule:** when generating/hand-writing `Animation` sub_resources, emit exactly
  `tracks/N/type`, `tracks/N/path`, and `tracks/N/keys` (with `"times"`, `"transitions"`,
  `"update"`, `"values"`); optional editor niceties (`interp`, `loop_wrap`, `imported`,
  `enabled`) can be omitted (defaults apply), and a discrete track needs its **first key
  clamped to t=0** so the value is defined from the start. Loop via `loop_mode = 1` on the
  Animation itself.
- **Why:** unknown `tracks/…` properties risk load errors/warnings and are silently dropped
  by the editor on re-save, so the file would churn the first time anyone opens it.
- **Related (same phase):** Pomegraknight's sprite art is uniform across all 11 sheets
  (~227 px character height per cell, center pivot), so ONE `Sprite2D.scale` (64/227 ≈ 0.2819)
  serves every animation — don't add per-animation scale keys. Facing flips via
  `Sprite2D.FlipH`, so animation tracks must never key `scale`/`flip_h`. The gain-no
  "animation frozen" canon is implemented as `Anim.SpeedScale = 0` while `_stunTimer > 0`
  (idempotent, self-restoring) — the authored `stun` clip is deliberately NOT driven, and
  Godot clears `CurrentAnimation` to "" when a non-loop clip ends, which is why the automata
  tracks `LastAnim` itself (see `CharacterController.PlayAnim`).

### A self-spawning scene can't hold its own PackedScene ext_resource (v0.4.0)
- **Symptom:** wiring `CrabFoe.tscn`'s Spawn-on-death `BabyCrabScene` export to
  `CrabFoe.tscn` itself would make the scene reference itself as an `ext_resource` —
  Godot rejects that as a **cyclic resource** at load, and the whole scene fails to open.
- **Rule:** a foe (or any node) that instantiates copies of its *own* scene must NOT get
  that `PackedScene` from its own `.tscn`. Inject it from outside instead: `GameManager`
  owns `CrabScene` and assigns `crab.BabyCrabScene = CrabScene` after `AddChild`, and the
  crab **propagates the ref to its babies** (`baby.BabyCrabScene = BabyCrabScene`) so the
  chain survives without any self-reference. Same pattern applies to any future
  self-replicating entity. If the injected ref is null (e.g. a foe dropped straight into a
  scene for debugging), the spawn is skipped gracefully rather than crashing.

### Capture spawn-relative anchors on the first physics frame, not in _Ready (v0.4.0)
- **Symptom:** a seagull read its patrol home/height from `GlobalPosition` in `_Ready`, but
  spawners set `GlobalPosition` **after** `AddChild` (i.e. after `_Ready`), so it anchored
  to (0,0)/the scene-file position and patrolled around the wrong point.
- **Rule:** anything that depends on the *final* spawn position (patrol origin, fixed flight
  height, home range) must be captured **after** the spawner has placed the node. `BaseFoe`
  latches `SpawnOrigin` on its first `_PhysicsProcess` tick and calls `OnSpawnPlaced()` then —
  don't read spawn position in `_Ready`. (Same family as the "call `Init(...)` after
  `AddChild`" reference note below.)

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

### `ChangeSceneToFile` is deferred — guard against a second swap in the same frame (v0.5.0)
- **Symptom (designed-around, no toolchain to hit it live):** `RunState.EndDay()` runs the
  day-end pipeline, and the VOID-devour step can call `EndRun()` (scene → `RunOver.tscn`) when the
  player's node is eaten. The Adventure scene / map button that invoked `EndDay()` then also wants
  to `ReturnToMap()` (scene → `Map.tscn`). `ChangeSceneToFile` is **deferred to end-of-frame**, so
  the *last* call wins — `ReturnToMap` would silently clobber the RunOver swap and the death would
  vanish.
- **Rule:** a single `bool RunFinished` latch, set in `EndRun`. `ReturnToMap`, `BeginAdventure`,
  `EndRun`, and the pipeline all early-out when it's set; `EndDay` only calls `ReturnToMap` if the
  run didn't just end. One owner decides the terminal scene per frame. Any future code that ends
  the run mid-transition must respect this latch, not issue its own `ChangeSceneToFile`.

### Determinism: add a subsystem via `DetRandom.Sub`/a fresh seed-derived stream, never the shared one (v0.5.0)
- **Rule (reinforced):** mission types are rolled in `MapGenerator.RollMissions` from a **dedicated**
  `DetRandom(seed+"M")` stream, mirroring the atlas's `seed+"R"`. Deriving a stream from the seed
  string (`Rng.Sub("tag")` → `new DetRandom(seed+":"+tag)`) instead of consuming from the layout
  `rng` means the new subsystem draws its own numbers and **does not shift** what any other pass
  reads — so a given seed's map geometry stays byte-for-byte identical when you bolt on a new
  roll. If you ever add a mapgen pass, give it its own sub-stream or you will silently reshuffle
  every existing seed's world.

### RunState owns run truth; the map is a view (v0.5.0)
- Day / stamina / visited / completed / VOID-latch / the graph live on the `RunState` autoload
  (one-owner rule, T00). `MapController` keeps only *view caches* (`_graph/_current/_visited/
  _revealed`), rebuilt from RunState in `SyncFromRunState()` on every scene load, and exposes
  `_day/_stamina/_inVoid` as **read-only computed aliases** so the existing view + atlas code
  reads them unchanged. Never write these back locally — write through RunState. The map remains
  directly launchable (F5): `_Ready` calls `RunState.NewRun(...)` itself when no run exists.

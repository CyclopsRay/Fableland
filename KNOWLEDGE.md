# KNOWLEDGE.md — Fableland engineering knowledge

Durable, cross-session knowledge for working on this Godot 4.7 (.NET/C#) project.
**Read this before writing code.** When you fix a bug, add a caveat below so the
next session doesn't repeat the mistake (see the workflow rule in `CLAUDE.md`).

For architecture, controls, and the file map, see `Migration.md` §0.

---

## Conventions

**Units / physics** (`Scripts/Foundation/Units.cs`) — derive everything from these, don't hardcode:
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
2. **SoftVolume** (`Scripts/Gameplay/World/SoftVolume.cs`) — enterable "go-inside" volume (tree/bush/…);
   Area2D field with **no** standable top. Inside, normal gravity/input/jumps continue;
   velocity is gradually pulled toward StagnationIndex (the scene fallback is 0.5·maxSpeed;
   map clouds use 0.3 and palms 0.1) plus a small
   GravityIndex drift (0.1·maxSpeed), rather than being overwritten. Active membership refreshes
   player jump charges even though it is not a physical floor. Applies to players AND foes.

**Movement / combat model** (`CharacterController`, `Enemy`, `HitInfo`):
- Velocity = **`intentVel`** (self-directed: input, gravity, jump, then any SoftVolume resistance) +
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
refreshed on touching ground / platform / active SoftVolume. A universal `JumpCooldown` (0.3 s) is the
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

**Versioning** — bump patch (+0.0.1) every commit; keep `Scripts/Foundation/GameVersion.cs`, the
repo-root `VERSION` file, and the HUD `VersionLabel` (`Scenes/Hud.tscn`) in sync. Shown
top-left in-game.

**Verification** — the dev host has **no Godot/.NET toolchain**, so edits are checked
statically (brace/paren balance, `.tscn` resource paths, `GetNode` paths). This does NOT
catch API-existence or type errors — do a real build when possible, and log anything the
compiler surfaces below.

---

## Caveats / gotchas (grow this on every bug fix)

### External arena actions must ask the active character's skill-state policy (v0.10.2, Pixolotl input-lock fix)
- **Symptom:** Pixolotl's local Shift state blocked controller abilities, but Arena-level Tab
  switching and held-item activation still bypassed that state; E likewise needed its explicit
  movement-and-BA-only exception.
- **Rule:** route arena-owned actions through character capability hooks such as
  `CanSwitchProtagonist` and `CanUseHeldItem`. A character with an active skill must own the
  policy, while the arena remains the only caller that performs the action.
- **Why:** input locks are not only controller movement. Any global input path can otherwise
  escape a committed skill state and violate its combat contract.

### Gameplay node properties must not shadow Godot's inherited ownership API (v0.10.1, Pixolotl build fix)
- **Symptom:** `PixolotlBubble.Owner` compiled with a warning because `Node` already exposes
  an `Owner` property used by Godot scene ownership; the temporal projectile's character owner
  was a different concept.
- **Rule:** never name gameplay references after inherited Godot properties. Use a domain name
  such as `TemporalOwner` and check new node subclasses for compiler hiding warnings before
  shipping.
- **Why:** an apparently harmless convenience property obscures a scene-tree API and invites
  incorrect ownership reads or future warning-as-error build failures.

### An unreadable save slot is not an empty save slot (v0.9.0, save-slot review)
- **Symptom:** the title slot picker represented a corrupt or newer-version file as
  unoccupied; selecting it then followed the "new game" path and could overwrite the only
  recoverable copy of that save.
- **Rule:** preserve the distinction among empty, valid, and unreadable slots all the way to
  the UI action. Only a genuinely missing slot may start a new run; a loader diagnostic must be
  shown without writing to that slot.
- **Why:** persistence failures are data-loss risks, not ordinary menu states. Treating an
  unreadable file as absence turns a recoverable compatibility or disk issue into irreversible
  loss at the first click.

### An enterable character-specific field needs a membership filter, not a Platform body (v0.8.1, Cleopastar star fix)
- **Symptom:** using a Platform-layer child to make Cleopastar's stars usable also let that body
  collide with other stars and forced the jump interaction to depend on floor contacts.
- **Rule:** when a feature is meant to be entered rather than stood on, model it as a `SoftVolume`
  with the appropriate `StagnationIndex` and no physics layer. If it belongs to one character,
  narrow `SoftVolume.AffectsBody` in a subtype and use that volume's overlap membership for the
  character-specific input check.
- **Why:** a physical platform creates collision relationships with every other platform-aware
  object. An Area membership expresses the actual rule—“inside this star”—without inventing
  unwanted terrain or projectile contacts.

### Collision-layer constants use the project’s plural `LayerFoes` name (v0.8.1, Cleopastar build fix)
- **Symptom:** the new Cleopastar terrain/effect split failed to compile after referring to a
  nonexistent `Units.LayerFoe` constant.
- **Rule:** use the exact shared `Units` constants—`LayerPlayer`, `LayerFoes`, `LayerGround`,
  `LayerPlatform`, `LayerProjectile`, and `LayerHazard`—rather than inferring a singular name.
- **Why:** collision layers are a project-wide API. A one-character spelling variation prevents
  the whole game assembly from building and is easier to avoid than diagnose later.

### Effect range must not double as projectile terrain geometry (v0.8.1, Cleopastar spawn-terrain fix)
- **Symptom:** Cleopastar's 2–3 m effect circle was also her terrain collider, so a star fired
  from the hand could begin overlapped with a nearby floor or wall and explode at launch.
- **Rule:** use the compact rendered core for terrain blocking and retain the larger effect shape
  only for foes. A launch-only terrain waiver must resolve still-overlapping terrain when it ends,
  because ignored `BodyEntered` events are not replayed by Godot.
- **Why:** damage range and physical footprint answer different gameplay questions. Separating
  them removes spawn false positives without turning a large AoE into a tiny contact effect.

### A special one-way support must not silently refresh ordinary jump charges (v0.8.1, Cleopastar star-platform fix)
- **Symptom:** Cleopastar's star used the normal Platform layer, so the shared floor logic
  restored her jump charges every frame; its Area trigger then consumed it merely when she touched
  it. The intended exhausted-jump interaction could never occur.
- **Rule:** retain the physical one-way platform for collision, but give the character controller
  a narrow virtual hook to decline ordinary jump refresh on that support and a second hook used
  only when normal charges are exhausted. The star is consumed only after that second hook accepts
  the jump request; Area overlap alone must be inert for the owner.
- **Why:** platform collision answers “can this body stand here?”, not “should this surface grant
  a normal resource refill?” Treating those as the same rule erases deliberate movement costs.

### A live arena must own a frozen team snapshot, not reread the shelter build (v0.8.1, party-switch fix)
- **Symptom:** changing `ActiveBuild` then switching could hydrate the wrong protagonist or make
  a roster member unreachable, because the arena's active index and the mutable shelter list
  could disagree. The hardcoded Pome scene also ignored a legitimate first team member.
- **Rule:** validate the editable team at combat entry, put an immutable ordered copy on the
  adventure handoff, and let the arena own a local switch cursor for the rest of that battle.
  Shelter changes apply only to the next snapshot; runtime body creation resolves from the
  snapshot's first member.
- **Why:** a team editor and an in-progress encounter have different ownership and lifetimes.
  Sharing their mutable cursor turns routine UI edits into combat-state corruption.

### Body swaps must transfer the current camera before retiring the old body (v0.8.1, party-switch fix)
- **Symptom:** after a protagonist switch the outgoing `Camera2D` could remain current or leave
  no enabled current camera, so the view stopped following the newly-controlled character.
- **Rule:** during every runtime body replacement, explicitly disable the old character camera,
  enable the new character camera, and call `MakeCurrent()` before queuing the old body for
  deletion. A scene's camera node is not automatically inherited by its replacement.
- **Why:** camera ownership is node-local while player control is logical state. Replacing one
  does not implicitly transfer the other.

### Mission resolution must gate player damage at the shared controller (v0.8.1, resolution fix)
- **Symptom:** foes were marked invincible when a level ended, but hazards and damage-over-time
  could still reduce the player's HP while the reward/Finish Day flow was on screen.
- **Rule:** set player invincibility on mission resolution and enforce it in every shared intake
  path: direct hits, hazards, status application, and DoT. Clear that terminal gate on respawn.
- **Why:** resolving the mission is a combat-state boundary, not merely a foe-spawn change;
  guarding only one incoming-damage path leaves delayed effects able to violate the contract.

### Projectile terrain rules come from the GDD, not a convenient lingering effect (v0.8.1, PomeSeed fix)
- **Symptom:** Pome seeds were left as damaging linger fields after touching terrain, even though
  Pomegraknight.gdd says Ground and Platform destroy them without applying a hit.
- **Rule:** every projectile must explicitly implement its GDD's terrain resolution; otherwise use
  the shared default of Ground/Platform blocking and SoftVolume pass-through. Do not retain a
  prior "land and linger" behaviour merely because the projectile already has an Area2D shape.
- **Why:** terrain interaction is combat balance, not rendering polish. A one-line collision
  branch can silently turn an arc projectile into persistent zone denial.

### Reserve mandatory capital approaches before discretionary non-crossing roads (v0.8.1, map fix)
- **Symptom:** after ordinary local roads had been emitted first, the required second LV3→LV4
  approach could have no legal route left once road crossings and overlaps were prohibited.
- **Rule:** reserve both mandatory boss approaches before growing the optional spanning-tree,
  loop, and hub-spoke roads; later routes must route around those fixed approaches.
- **Why:** non-crossing topology is order-sensitive. Treating a mandatory path as an afterthought
  can make an otherwise valid realm impossible to finish without breaking the art constraint.

### Procedural map generation still needs its run-data contracts imported (v0.9.0, build fix)
- **Symptom:** the rewritten `MapGenerator` did not compile because its mission rolls referenced
  `MissionType` without importing the `Fableland.Run` namespace.
- **Rule:** when moving a generator into the map layer, explicitly import every data contract it
  writes (`MissionType`, terrain labels, and so on); do not assume nearby map namespaces expose
  run data transitively.
- **Why:** C# namespaces are not inherited by directory location, so a pure generator can compile
  only when all cross-layer contracts are named deliberately.

### Functional map nodes inherit visual ring labels, not combat devour membership (v0.9.0, VOID fix)
- **Symptom:** a Transportation Hub or Event could be eaten merely because its display
  `LevelTag` matched the day schedule, even when an adjacent city still survived.
- **Rule:** apply `VoidSchedule` directly to combat nodes only. Functional nodes use the separate
  all-neighbours-devoured orphan rule, regardless of their borrowed display level.
- **Why:** an edge-splitting node is spatial infrastructure, not a city in a combat ring; its tag
  is useful for presentation but cannot safely be treated as lifecycle ownership.

### C# local declarations cannot reuse a later method-scope name inside a nested block (v0.7.0, combat-map build fix)
- **Symptom:** `GameManager.RandomPlacementPoint` failed to compile after the authored-map branch introduced a local `x`, while the legacy fallback below still declared its own `x`.
- **Rule:** in C#, give branch-specific locals distinct names when any enclosing method block declares the same identifier, even if their source ranges do not overlap in execution. Use descriptive names such as `authoredX` at integration boundaries.
- **Why:** local-variable scope is determined by the containing block, not just runtime reachability; a minor fallback branch can therefore invalidate an otherwise isolated feature path.

### Platform defaults are full footprints; the effect painter is the only source of bespoke platform geometry (v0.6.18, Map Creation collision clarification)
- **Symptom:** the bench, sun lounger, and lifeguard tower had legacy narrow registry rectangles as fallbacks while the effect painter also stored the authored shape. This made it difficult to tell whether collision came from the active painted mask or an old built-in region.
- **Rule:** platform TileDefs leave `EffectArea` null, which means their complete footprint is the default collider. A narrower platform surface or a compound tower landing exists only as a saved `TileEffectStore` mask. The painter override remains per tile kind and wins completely; use Clear only to return to the full-footprint fallback.
- **Why:** one obvious fallback makes a new platform predictable and keeps every exceptional collision surface visibly authored in the same tool that controls it.

### Farview collision is a fixed-world SoftVolume opt-in, not a parallax gate (v0.6.17, Map Playtest bug fix)
- **Symptom:** the Farview Collision checkbox was disabled for every parallax other than 1.0,
  yet Farview SoftVolumes ignored it and always built colliders. At the same time their art was
  deliberately excluded from loop copies, making sparse cloud layers appear missing.
- **Rule:** on a non-looping Farview, Collision is available at every parallax and controls only
  the one fixed-world SoftVolume collider at each authored tile. Looping Farviews are decorative:
  they repeat all artwork but build no physics. Keep this policy in the layer panel, playtest
  builder, and GDD together. New looping layers use a 16-cell strip; old 64-cell loops retain
  their authored width and may intentionally have wide gaps.
- **Why:** a collider must never move with a camera transform, but authors still need an explicit
  deterministic choice. Separating the one real collider from repeated visuals makes the choice
  clear and keeps a looping sky from silently creating duplicate collision.

### SoftVolume resistance must modify a live jump instead of replacing it (v0.6.17, movement bug fix)
- **Symptom:** SoftVolumes supplied a one-way top (requiring Down to enter) and replaced intent
  velocity with a small fixed field on overlap. A normal `-Units.JumpSpeed` launch was therefore
  cancelled immediately. A follow-up briefly removed jump refresh with the top, but cloud/tree
  traversal still needs a refreshed jump while the body is inside.
- **Rule:** SoftVolumes are `Area2D` fields with no top collider. Run normal movement/gravity/jump
  first, then move the existing intent velocity gradually toward the indexed cap and drift; derive
  the default resistance from `Units.Gravity × (1 - StagnationIndex)`. Ground/platform **and an
  active SoftVolume** refresh jumps, with the latter refresh occurring before jump input. Continue
  applying the separate in-volume external-velocity damping.
- **Why:** a viscous cloud should shorten a jump through accumulated delta-v, not erase the input
  frame that began it. Retaining the two velocity channels also keeps wind and knockback behavior
  composable.

### Rule generators must filter impossible footprints before weighted selection (v0.6.17, Map Creation bug fix)
- **Symptom:** a one-cell Cloud Zone often generated nothing: its table weighted the 2×1 cloud
  most highly, so the resolver rolled an entry that could not fit and burned an attempt instead
  of considering the available 1×1 cloud.
- **Rule:** at each candidate anchor, build the weighted selection list from only definitions
  whose entire footprint lies in the zone; then apply the normal occupancy/reserve rejection.
  Keep the one-cell Cloud Zone regression self-test in the browser's debug boot validation.
- **Why:** fitting is a deterministic eligibility rule, not a stochastic outcome. Filtering it
  before the roll preserves the intended weights among possible content and makes small zones
  degrade to fewer valid spawns instead of zero content.

### Map playtest must overwrite character-scene camera limits from its Battlefield bounds (v0.6.17, Map Playtest bug fix)
- **Symptom:** Pomegraknight's reusable scene carried the prototype Arena camera limits
  (`right = 2000`, `bottom = 720`). Once Map Creation cells grew to 64 px, a 64×36 map was
  4096×2304 px, but its camera still stopped at the old arena edge and could not reach lower
  authored tiles.
- **Rule:** after instantiating the playtest character, set `Camera2D.LimitLeft/Top/Right/Bottom`
  from the Battlefield rectangle (`0, 0, gridW × MapGrid.PixelsPerCell, gridH ×
  MapGrid.PixelsPerCell`). Do this in the map orchestrator; do not alter the reusable character
  scene's Arena-specific defaults. Add no arbitrary camera margin unless the map contract names one.
- **Why:** a character scene is reused by the fixed Arena and custom maps, whose extents differ.
  Scene-authored camera limits are therefore defaults, not map ownership; the runtime map must
  supply its own bounds to keep the camera frame inside the current authored battlefield.

### Runtime parallax follows the active camera center, anchored at the authored spawn (v0.6.17, Map Playtest bug fix)
- **Symptom:** farview tiles were offset from the player body's position. Camera smoothing and
  shake therefore made their apparent motion disagree with the camera, and all farview art was
  shifted from its authored coordinates when the playtest began away from world origin.
- **Rule:** read `Camera2D.GetScreenCenterPosition()` for parallax, never a character body's
  `GlobalPosition`. Store an explicit world anchor (the playtest character spawn) and use
  `tileVisual = authoredTile + (cameraCenter - anchor) × (1 - parallax)`; use the corresponding
  anchored parallax-space value when selecting loop copies.
- **Why:** the camera is the rendered view transform; a player body is only one possible target
  and can diverge from it. Anchoring makes `cameraCenter == anchor` render every tile at its saved
  absolute position, while still allowing intentional parallax afterwards.

### `PlacedTile.FlipX` must mirror effect geometry across the full footprint (v0.6.17, Map Creation bug fix)
- **Symptom:** using **Flip H** mirrored a tile sprite but left its orange effect-area overlay
  and playtest collider at the unflipped coordinates. An asymmetric hazard or platform therefore
  looked safe on one side while still affecting the other.
- **Rule:** treat `FlipX` as a placed-instance geometry transform, never as a sprite-only flag.
  Before drawing or constructing collision, reflect rect offsets, circle centers, and polygon
  points inside the full tile width; reverse mirrored polygon point order to preserve winding.
  For sub-cell masks, reverse both the footprint-cell order and each 4×4 row's columns. Keep this
  logic in `EffectAreaTransform` so editor overlays and runtime collision call the same transform.
- **Why:** `TileDef` and its effect shape are shared immutable content, while `FlipX` belongs to a
  saved placed instance. Mutating the definition would flip every instance; transforming only the
  sprite desynchronizes presentation from deterministic gameplay.

### Persisted JSON casing and Container stretching must match the effect painter's contracts (v0.6.17)
- **Symptom:** a saved `tile_effects.json` immediately reloaded with no overrides because STJ
  wrote `Masks` while the custom v1/v2 reader searched only for `masks`. Separately, a 1×1
  painter's host expanded to the dialog width, but hit-testing still divided coordinates by the
  calculated 42 px sub-cell size, so the visible cells, sprite, and painted cell could disagree.
- **Rule:** compatibility readers accept both the serializer's existing PascalCase keys and
  camelCase inputs, and their round-trip test must include the legacy schema. Any pixel-mapped
  grid inside a `Container` must opt into shrink sizing (and set its exact calculated size) so
  layout cannot stretch it independently of hit-testing; keep cell overlays translucent when a
  reference texture is drawn behind them.
- **Why:** JSON property matching is case-sensitive at the `JsonElement` level, and Godot
  containers resize children according to size flags rather than `CustomMinimumSize` alone.
  Persistence or pointer geometry that silently uses a different contract makes authoring look
  successful while saving or painting the wrong data.

### Atlas source regions must survive fill-footprint drawing, and generated air must become alpha (v0.6.16)
- **Symptom:** placing `Beach Sand` in the map creator did not use the new layered hill art.
  The registry still pointed at the old atlas, `HillAutotile` had no renderer dispatch, the
  generated source cells still carried opaque magenta, and—most critically—even the existing
  atlas lookup's selected `SourceRect` was discarded because `DrawTileQuad` replaced it with
  the whole texture whenever `SpriteFillFootprint` was true.
- **Rule:** `SpriteFillFootprint` controls only the destination rectangle; always draw the
  `SpriteTexture.SourceRect`. Select classifier families through tile data
  (`Props["autotileKind"]`), not a content-id switch. Chroma-key generated open air to RGBA
  before atlas composition, and require all 13 equal-size sources (including Layer-1 Peak)
  before wiring the atlas into a palette tile.
- **Why:** an atlas is useful only if the renderer preserves its selected cell, and an opaque
  key color is not transparency. Keeping classifier choice in `TileDef` also lets later hill
  materials reuse the renderer without another hard-coded branch.

### AI terrain geometry must start from a coded guide, and imperfect sheets must fail closed (v0.6.16)
- **Symptom:** a text-only request for a 3x4 sand-hill sheet produced an image whose file
  dimensions divided into twelve squares, but whose painted layer changes ignored the row
  boundaries and whose broad surface dome made repeated Mid tiles form visible waves.
  `slice_hill_grid.py` would also merely warn and crop a genuinely wrong-ratio source, allowing
  malformed cells to enter the production atlas.
- **Rule:** generate `Tools/generate_hill_guide.py` first and use its output as the binding
  geometry reference; style images are finish-only references and never own proportions. Layer
  1 Mid is mechanically flat across both endpoints; only Left/Right slope toward their open
  outer edges and Peak domes. The slicer rejects any source that is not an exact 3:4 grid of
  square cells unless `--allow-imperfect` is explicitly used for disposable legacy inspection.
- **Why:** image models can paint a supplied structure convincingly but prose is not a reliable
  pixel grid. Keeping geometry deterministic in code prevents waves, shifted material bands,
  remainder-pixel cropping, and atlas seams while still allowing generated watercolor texture.

### Folding a UI panel means resizing its anchored offset, not just hiding its content (v0.6.9, MapEditor UX fixes)
- **Symptom:** the map editor's Layers/Palette side panel and Tools rail are fixed-size
  `PanelContainer`s anchored by `Offset*`; hiding a child `Control` inside one (`Visible = false`)
  collapses the *content*'s layout footprint, but the parent panel's own anchored rect is
  unchanged — the empty panel background still sits over the canvas and (being added after
  `GridView` as a sibling) still intercepts clicks meant for it (KNOWLEDGE v0.2.4's "background
  eats clicks" trap, one level up).
- **Rule:** a fold toggle must do both: `content.Visible = !folded` AND shrink the panel's own
  `Offset*` on the dimension that matters (`OffsetBottom`/`OffsetTop` for the horizontal top/status
  bars, `OffsetRight` for the left rail) down to a small header-only constant, restoring it when
  unfolded. For the right panel's two independent sections (Layers, Palette — see next point),
  the panel only narrows once **both** are folded, since either one alone still needs the full
  width for its own content.
- **Also fixed alongside:** the Layers and Palette panels used to share one unbounded
  `VBoxContainer` — the Layers properties sub-panel (~14 rows) could grow tall enough to push
  Palette off the bottom of the screen with no way to reach it. They're now two independent
  `ScrollContainer`-wrapped sections with `SizeFlagsVertical = ExpandFill`, each foldable on its
  own, so one section's height never depends on the other's content.

### Ground autotiling is a side-view 2-state (interior/top-edge) lookup, not a 16-tile bitmask blob set (v0.6.9, best-effort — needs visual confirmation in Godot)
- **Context:** `Docs/Art/BeachTileSet.md`'s `terrain_beach_atlas.png` backs `ground.sand`/
  `ground.grass` (`AutotileGroup = "terrain.beach_sand"`/`"terrain.coastal_grass"`). The atlas
  is a hand-arranged grid, not a formulaic Wang/blob set, and the doc explicitly left the
  atlas-coordinate mapping undefined. This dev environment has no Godot/dotnet toolchain, so the
  mapping below was authored by reading the atlas image, not by opening it in the engine.
- **Rule:** `Scripts/MapCreation/Data/AutotileAtlas.cs` counts the atlas as a 7-col × 6-row grid
  (sand block = row 0, grass block = row 3) and exposes exactly two cells per terrain: `col 0`
  = interior/full (every orthogonal neighbor same `AutotileGroup`), `col 1` = top-edge cap (north
  neighbor NOT same-group). `GridView.NeighborSharesGroup`/`DrawLayerTiles` pick between them by
  testing only the north neighbor via the layer's `LayerOccupancy` — left/right/bottom edges fall
  back to the interior cell rather than guessing at a specific corner region.
- **Why so coarse:** Fableland is a 2.5D SIDE-VIEW arena fighter, so "is there open air above
  this cell" is the visually dominant case, unlike a top-down game's full 8-neighbor blob set.
  Coordinates are computed from `AutotileAtlas.Cols`/`Rows`, not hand-typed pixel rects, so if the
  real grid count turns out to be wrong once someone opens the atlas in Godot, it's a one-constant
  fix here, not a re-type of every region.
- **Do NOT extend this into a full bitmask autotiler inside `GridView`** without revisiting GDD
  §2.5's decision log first — the recorded plan is a real Godot `TileSet` Terrain resource as the
  RUNTIME render path precisely so nobody hand-rolls bitmask autotiling; this GridView lookup is a
  deliberate, disclosed, editor-only exception for authoring-time visual feedback only.
- **v0.6.13 update:** the decision log above WAS revisited to add a second such exception —
  `Scripts/MapCreation/Data/HillAutotile.cs`, a 4-layer × 4-position classifier for built-up sand
  hills (`Docs/Art/BeachTileSet.md`'s "Sand hill layered autotile" section has the full rule set
  and generation prompts). It's the same kind of disclosed, editor-only, non-Wang/blob exception as
  this one — not yet wired into `GridView` or `TileRegistry` pending the reference atlas existing.

### Mac trackpad two-finger scroll arrives as `InputEventPanGesture`, never a wheel `InputEventMouseButton` (v0.6.9, MapEditor UX fixes)
- **Symptom:** the map editor's zoom only handled `InputEventMouseButton` `WheelUp`/`WheelDown` —
  a real scroll wheel worked, but a Mac trackpad two-finger swipe produced no zoom at all, because
  Godot reports trackpad pan gestures as a distinct `InputEventPanGesture` (with a `Vector2 Delta`),
  not a mouse-wheel button event.
- **Rule:** handle `InputEventPanGesture` alongside the wheel case in the same `_GuiInput`; use
  only the vertical `Delta.Y` component (horizontal swipe is left alone — no trackpad-pan feature
  exists) and convert it to the same `ZoomFactor`-per-step curve the wheel/keyboard shortcuts use
  (`GridView.HandlePanGesture`) so all three zoom inputs feel consistent.

### Never commit a file that still contains conflict markers — and two machines both minting "v0.6.7" means the reconciling merge takes the next patch number (v0.6.8, merge cleanup)
- **Symptom:** the remote commit `17a896d "rebase merge"` (pushed from another machine) shipped
  KNOWLEDGE.md with literal `<<<<<<< HEAD` / `=======` / `>>>>>>> 3fc1517` lines committed into
  it — a rebase conflict was committed instead of resolved. The next merge then produced
  *nested* markers that no longer matched git's normal conflict shape. Separately, both
  machines had stamped their own commit "v0.6.7", so the version files collided silently
  (identical bumps auto-merge with no conflict, hiding that two different builds share a
  version string).
- **Rule:** before `git rebase --continue` / committing a conflict resolution, grep the tree
  for `^<<<<<<<`/`^=======$`/`^>>>>>>>`. When reconciling parallel work from two machines,
  the merge commit is a commit like any other: bump to the **next** patch version in all three
  spots (`VERSION`, `GameVersion.cs`, Hud `VersionLabel`) so each build string stays unique.
- **Why:** committed markers corrupt Markdown/docs quietly and make later merges ambiguous;
  duplicate version strings break the "which build is this?" contract the top-left HUD label
  exists to answer.

### A bare-key InputMap action swallows every modifier-combo on the same base key — check combos first (v0.6.7, caught in review)
- **Symptom:** `Ctrl+V` (paste, `mapedit_paste`) did nothing in the map editor — it switched to
  the Move tool instead. `mapedit_tool_move` is bound to bare `V`, and `_UnhandledKeyInput`
  checked the tool actions before the clipboard actions. `InputEvent.IsActionPressed` defaults
  to **non-exact modifier matching**, so a `Ctrl+V` event also satisfies the bare-`V` action;
  first match returned and paste was unreachable. Silent — no error, the key just "did the
  wrong thing."
- **Rule:** in any first-match-wins action dispatch chain, check **modifier-combo actions
  before bare-key actions sharing the same base key** (and supersets before subsets — the same
  reason `mapedit_redo` Ctrl+Shift+Z is checked before `mapedit_undo` Ctrl+Z). Audit for base-
  key collisions whenever a new action is added to a dispatch chain: grep the [input] section
  for the same `physical_keycode` appearing with and without modifiers.
- **Why:** non-exact matching is Godot's default so that extra held modifiers don't break
  gameplay actions; in an editor-style shortcut chain that default inverts into a shadowing
  hazard. **Related transcription gotcha from the same review:** don't hand-derive Godot `Key`
  enum values for numpad keys — the block is KP_MULTIPLY 4194433, KP_DIVIDE 4194434,
  KP_SUBTRACT 4194435, KP_PERIOD 4194436, KP_ADD 4194437, then KP_0..KP_9 from 4194438;
  a miscounted `4194440` (KP_2) shipped as "KP_SUBTRACT" and only a reviewer's enum
  cross-check caught it (no toolchain = no runtime to notice a dead binding).

### A node's own `_Draw` renders BEHIND its children — world drawing needs a dedicated child (v0.6.7, MapCreation rework)
- **Symptom (root cause of the v0.5.x map editor):** the editor canvas was completely invisible
  — grid, tiles, everything. The root Control drew the world in its own `_Draw`, and Godot
  renders a CanvasItem's self-drawn content **before (= behind) all of its children**, so the
  full-rect opaque background ColorRect child occluded every draw call. No error; the code ran
  every frame and painted pixels nobody could see.
- **Rule:** never draw world/content in a node that owns opaque children. Give the drawing its
  own dedicated child (`GridView` in `Scripts/MapCreation/Editor/`), layered explicitly:
  background child first, drawing child above it, UI panels after. The root's `_Draw` stays
  empty forever — the map editor's `MapEditor.cs` class header documents this as a structural
  rule (GDD MapCreation §7.6).
- **Why:** self-draw-behind-children is by design (a container paints its own background
  behind its content); it only becomes a trap when the "background" is a sibling-less child
  and the "content" is the parent's own draw — exactly the shape a quick prototype reaches for.

### File identity must be minted (GUID), never derived from a user-editable name (v0.6.7, MapCreation rework)
- **Symptom (v0.5.x map browser):** two maps named "Untitled" collapsed into one file — the
  save path was derived from the map's display name, so same-name maps silently overwrote each
  other, and renaming a map orphaned its old file.
- **Rule:** a persisted object's file identity is a GUID minted once at creation
  (`MapDocument.Id` → `user://maps/<guid>.json`); renames are metadata-only edits that never
  move the file; duplicates mint a fresh GUID. And no `_index.json`-style sidecar registry —
  list by directory scan reading each file's own meta; an index that doesn't exist can't
  desync. A load-time guard rejects id-less JSON as "not a map file" rather than showing a
  default-valued phantom card (System.Text.Json happily deserializes any object shape).
- **Why:** names are UX, identity is plumbing; the moment the two are coupled, every UX
  affordance (rename, duplicate, "Untitled" defaults) becomes a data-loss path.

### `System.Text.Json` ignores public fields — every serialized model class must use properties (v0.6.7, MapCreation rework)
- **Symptom (root cause of the v0.5.x map builder):** every save in the old `Scripts/MapCreation/`
  module wrote `{}` to disk. The serialized classes (`CustomMapData` and friends) used plain
  public **fields**, and `System.Text.Json`'s default reflection contract only sees public
  **properties** — fields are silently skipped, both on serialize (nothing written) and
  deserialize (nothing populated). No exception, no warning; the file just quietly had no data.
- **Rule:** any class that round-trips through `JsonSerializer` must expose every persisted
  member as a property with `{ get; set; }`, never a bare field. Also add
  `[JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }` to every
  serialized class so a future version's unknown fields survive a load→save round trip instead
  of being dropped (T20 §5 forward-compat). See `Scripts/MapCreation/Data/{MapDocument,
  MapLayerData, PlacedTile}.cs` for the pattern, and `MapJson.RoundTripSelfTest()` for the
  structural guard (build a representative doc, save, load, deep-compare every property) —
  run this any time a new serialized field is added to catch a field/property slip immediately
  instead of discovering it as an empty save file later.
- **Why:** this is exactly the kind of bug that looks like a totally different failure (map
  browser shows blank names, maps "don't save") because the write path never errors — the
  round-trip test is cheap insurance precisely because there's no other signal.

### The HP bar is block-based (25 HP/block) and renders via a custom Control._Draw(); HpChanged now carries 4 args (v0.6.7)
- **Why:** the old `ProgressBar` couldn't show shield (sky blue) or temp HP (green) as
  distinct segments alongside normal HP (red). The new `HpBlockBar` Control draws blocks
  via `_Draw()` with three stacked coloured segments within the same bar, matching the
  Overwatch/LoL convention of one bar that shows all HP types at a glance.
- **Rule 1 — `HpChanged` is now `Action<float, float, float, float>` (curHP, maxHP,
  shield, tempHP).** Any code that subscribes to `CharacterController.HpChanged` must
  accept 4 arguments, not 2. Use `NotifyHpChanged()` to fire it — don't invoke
  `HpChanged` directly.
- **Rule 2 — Shield is a virtual property on the base (`public virtual float Shield => 0f`).**
  Characters that have a shield (PumpKing) override it and call `NotifyHpChanged()` on
  every shield change. The HUD reads `.Shield` uniformly regardless of which character
  is active.
- **Rule 3 — TempHP lives on the base (`public float TempHP { get; protected set; }`)**
  and is absorbed by the base `AbsorbDamage` before shields. Subclasses that override
  `AbsorbDamage` must call `base.AbsorbDamage(damage)` first so TempHP absorbs before
  their own shield. TempHP is cleared in `Die()` and `Respawn()`.
- **Rule 4 — `HpBlockBar` uses `Size` from its Control rect (set by the scene's
  `offset_*` properties) and renders all blocks within that width.** The block count
  = `ceil(max(MaxHP, curHP+shield+tempHP) / 25)`. Each block is a fixed fraction of
  the total width with a 2px gap; block dividers are drawn as vertical lines on top.
- **Rule 5 — the `HpBlockBar` C# type replaces `ProgressBar` in `Hud.cs`.** The
  `Hud.tscn` node changed from `type="ProgressBar"` to `type="Control"` with
  `script = ExtResource("12")` (`res://Scripts/HpBlockBar.cs`). The `HpLabel` is
  still a child of `HpBar` and overlays the drawn blocks.
- **Related files:** `Scripts/HpBlockBar.cs` (new), `Scripts/CharacterController.cs`
  (Shield/TempHP/NotifyHpChanged), `Scripts/PumpKing.cs` (override Shield,
  NotifyHpChanged calls), `Scripts/GameManager.cs` (4-arg handler), `Scripts/Hud.cs`
  (SetValues delegate), `Scenes/Hud.tscn` (Control with script ref).

### PumpKing head: a per-tick clamp on a fresh Init() eats the whole launch impulse; releasing a reference destroys state-machine memory, not just control (v0.6.1)
- **Symptom (bug report, 4 linked issues):** the detached head visually sat at PumpKing's
  waist, barely traveled when fired, and read as "exploding on him" after Shift; knockback
  felt negligible.
- **Rule 1 — a ported offset doesn't survive a different sprite sheet.** `NeckOffset`/
  `FirePoint` were copied verbatim from Unity's `headNeckOffset` (0.6 m). Unity's number
  encoded a pivot/proportion relationship specific to *that* art; the Godot sprites have
  different padding, so the same number landed at the torso instead of the collar. When
  porting an attach-point offset, **re-measure it from the delivered art's own alpha
  bounds** (crop a frame, check the opaque bbox) rather than trusting the source engine's
  value to transfer — see `PumpKing.cs`'s `NeckOffset` comment for the measurement.
- **Rule 2 — a per-tick velocity clamp fires on the very first tick after `Init()`, before
  anything has moved.** `PumpKingHead._PhysicsProcess` clamped `_velocity.X` to
  `MaxRollSpeed` unconditionally, every tick, with no distinction between "just launched,
  still flying" and "landed, now rolling." Since `Init()` runs synchronously (same frame,
  right after `AddChild`), the *very next* physics tick already clamped away the launch
  impulse before the head had traveled at all — a roll-friction cap silently ate a
  ballistic-arc design number. **A "resting-state" cap (named for the settled behavior,
  e.g. `MaxRollSpeed`) must only engage once the entity has actually reached that state**
  (here: `_grounded`, set on first floor-like collision) — not from frame 0.
- **Rule 3 — releasing an object's *control* is not the same as releasing the state
  machine's *memory* of it.** `HandleSkill1` (Shift) called `_activeHead.SetAutonomous(true)`
  then immediately set `_activeHead = null`. Nulling the reference was meant to say "I no
  longer control this," but it also erased the only way `ExitSoul()` could tell "the
  released head is still alive out there" from "there's no head" — so Soul always exited to
  `Normal` and started a reload, regrowing a visible head on PumpKing's neck while the old
  autonomous one was still live and unresolved. Fix: keep the reference through the state
  that depends on it, and null it only where the object's fate is actually resolved
  (`HandleHeadExplosion`, or `ExitSoul` once confirmed `.Exploded`) — not at the moment
  control is handed off. Same family as the v0.5.4 "re-anchor after placing the node"
  caveat: a `null`/reset that's locally correct for one concern (control) can quietly break
  a different concern (bookkeeping) that was piggybacking on the same field.
- **Corollary:** fixing Rule 3 made a previously-dead code path reachable again (manual BA
  detonate on a head that had been marked autonomous), which exposed that `Explode()`
  suppressed AoE for *any* autonomous head unconditionally — including a deliberate player
  detonate, not just an unattended self-trigger. Needed a `manual` flag to keep "no AoE
  when it goes off by itself" from also silently eating "no AoE when the player explicitly
  detonates it." **When un-nulling/re-enabling a reference to fix a state bug, check what
  else was implicitly guarded by that reference being unreachable** — the guard may have
  been hiding a second, independent gap.
- **Why (radius/VFX audit, no code bug):** also checked whether the 3 m explosion "looks
  too large" was a sprite-vs-hitbox mismatch — it isn't. The peak VFX frame's opaque bbox is
  only ~9% wider than `ExplosionRadius`; the 96 px/3 m radius is a real, intentional balance
  number, not an art illusion. Measure the actual alpha bounds (crop the peak frame, check
  bbox) before assuming a "feels too big" report is a rendering bug — it may just be big.

### A "display-bypass" debug source must not round-trip into real owned state (v0.6.0, caught in review)
- **Symptom:** the v0.6.0 shelter Team Build menu, in debug mode, lists every `ItemCatalog`
  entry as assignable even when it isn't really in `RunState.Items` ("all wonder items available
  for testing"). `RunState.HoldItem` correctly skipped removing such a not-really-owned item from
  the backpack, but `UnholdItem` (and the bump-out branch) unconditionally did `Items.Add(...)` —
  so holding a debug-conjured item then returning it **materialized a brand-new real `ItemInstance`
  into `RunState.Items`**, which then survived toggling debug mode back off. One click-pair = a
  permanent item granted outside the real economy, on a real `Owned` protagonist.
- **Rule:** when a display/debug overlay shows content the real economy hasn't granted, any
  mutation path it shares with real content must **track provenance** so the debug item can't
  leak back as real state. Here: `ProtagonistState.HeldItemFromBackpack` records whether the held
  item came from `Items`; only a `true` item returns to `Items` on unhold/bump-out — a conjured
  (`false`) item vanishes. The "add back" and the "remove on take" must be symmetric and gated by
  the same flag. Same family as the v0.5.0 "debug knob's convenient default leaks into live
  gameplay" caveat: a debug affordance's *outputs* must be as carefully gated as its *inputs*.
- **Why:** the take-side skip (don't remove what isn't there) and the return-side add (always put
  it back) look individually correct but are asymmetric — the asymmetry is a net source of new
  real items. Provenance makes the pair symmetric so the round-trip is conservative (no creation),
  which is the invariant "the full catalog is never written into `RunState.Items` for real"
  actually requires.

### A scene-lifetime node must unsubscribe from an autoload's C# event in `_ExitTree` (v0.5.4)
- **Symptom:** `GameManager.SetupHud` did `DebugManager.Instance.SkipRequested += OnSkipRequested`
  and never unsubscribed. `DebugManager` is an autoload that outlives every arena scene, so each
  arena visit leaked a handler pointing at a disposed `GameManager`; a later SKIP press would
  invoke the dead handler(s) first (touching freed HUD/mission nodes → `ObjectDisposedException`
  risk), and an exception there blocks the *live* arena's handler too — SKIP breaks after the
  first arena visit.
- **Rule:** any node whose lifetime is shorter than the publisher's (scene node → autoload event)
  must pair `+=` with a `-=` in `_ExitTree`. Plain C# `event Action` has no Godot object-liveness
  awareness (unlike Godot signals, which disconnect freed objects). Prefer a direct, pull-style
  call (`GetTree().CurrentScene is GameManager gm && gm.Method(...)`) over adding a new autoload
  event when the flow is debug-tooling → scene — that's how the v0.5.4 protagonist page applies
  its swap, precisely to avoid creating another instance of this leak.
- **Why:** the autoload's event field holds a strong delegate reference; `QueueFree` frees the
  Godot object but not the C# subscription, so the handler list only ever grows.

### Runtime-instanced player bodies: re-anchor `_spawnPoint` after placing the node (v0.5.4, caught in review)
- **Symptom:** the debug protagonist swap instanced a character scene, `AddChild`-ed it, then set
  `GlobalPosition` — but `CharacterController._Ready` (which runs *inside* `AddChild`) had already
  latched `_spawnPoint = GlobalPosition` at the character scene-file's own origin (`(0,0)`; the
  real arena spot exists only as `Arena.tscn`'s instance override). Next debug-lives `Respawn()`
  would teleport the swapped-in body to `(0,0)`. Silent — no crash, just a wrong respawn.
- **Rule:** anything that instantiates a `CharacterController` at runtime must call
  `SetSpawnPoint(pos)` right after positioning it. Same bug class as the v0.4.0 "capture
  spawn-relative anchors on the first physics frame, not in `_Ready`" caveat (seagull patrol
  anchor) — `_Ready`-latched position state always predates the spawner's placement.
- **Why:** authored scenes get the right value for free (the node sits at its final position
  before `_Ready`), which is exactly what makes the runtime-instancing path easy to miss.

### Protagonist migration playbook — porting a character from the Unity `GloryOfFableland` project (reference; Pomegraknight v0.5.2, PumpKing v0.5.3)
Read this before porting the next character (Cleopastar / Pixolotl / Pangda). Each point
is a mistake already paid for once.
- **Sprites are already migrated.** The v0.5.2 bulk copy put every character's images in
  `Assets/Sprites/Protagonists/{Name}/` (images only, sanitized filenames; `.anim`/`.controller`/
  `.DS_Store` excluded). Do NOT re-copy from the old project. They are Unity **multi-sprite
  sheets**, not one-frame files — slice them into `AtlasTexture` regions. Get the grid from the
  old `.meta` (`TextureImporter` sprite rects) / `.anim` files, and **flip Y** (Unity's rect
  origin is bottom-left; Godot's is top-left).
- **The Godot base `CharacterController` is deliberately leaner than Unity's.** It does NOT
  provide: a shield pool, ammo/magazine/replenish, firing segments, or soul/free-flight. Any
  character that needs those implements them in its own subclass. When the base genuinely must
  participate (e.g. a shield that intercepts incoming damage), add a **`protected virtual`
  pass-through hook** (see `AbsorbDamage(float) => damage`, used at the 3 damage sites) rather
  than baking one character's state into the base — the default keeps every other character
  byte-for-byte unaffected.
- **Ability mapping is fixed:** Unity `HandleBA`→`HandleBA`, Shift→`HandleSkill1`, E→
  `HandleSkill2`, Ult→`HandleSkillUlt`. Override `ShiftCooldown`/`ESkillCooldown` for the HUD
  icons. BA that should fire on press (not hold) just gates inside `HandleBA`.
- **Derive every distance/speed from `Units` (32 px/m); never hardcode Unity metres.** Unity
  `x` m/s → `Units.Px(x)`; gravity/jump come from `Units`.
- **Scene: mirror `Scenes/Pomegraknight.tscn`.** CharacterBody2D (layer 1, mask 12) + Collision
  Shape2D + body Sprite2D + FirePoint(Marker2D) + AnimationPlayer(AnimationLibrary) + Camera2D
  (+ ShakeCamera2D script). **Multi-part characters** (e.g. PumpKing's `NeckHead`) add child
  Sprite2D(s) driven entirely in code — sync FlipH / Scale / Visible / SelfModulate to the body
  in `UpdateAnimator` (or a helper it calls), because the base only flips the body sprite.
- **`UpdateAnimator(dt)` runs from base `_Process`, not `_PhysicsProcess`,** and only while not
  stunned; `_Process` early-returns when dead. So overriding `_PhysicsProcess` for a special
  movement mode (PumpKing's Soul free-flight) does NOT suppress animation, and a `Dead` branch in
  `UpdateAnimator` is effectively unreachable.
- **If code mutates an `AtlasTexture.Region` at runtime** (per-stage head sprite, etc.),
  `.Duplicate()` the texture in `InitCharacter` first — the scene's sub_resource is shared across
  every instance and in-place mutation corrupts them all.
- **Animation authoring:** obey the two value-track caveats below (update mode inside the keys
  dict; first key at t=0; include a `RESET`). You don't need a clip for every Unity state — omit
  `dead`/`stun` when there's no art (the base freezes the frame via `Anim.SpeedScale` for stun and
  a gray tint reads as death).
- **Projectiles:** call `Init(...)` **after** `AddChild`; damage foes via
  `GetTree().GetNodesInGroup("enemy")` → cast `BaseFoe` → `TakeHit(new HitInfo(dmg, knockback), origin)`;
  hit tests subtract `BaseFoe.HitRadius`; projectile scene uses `collision_layer = 16` (layer 5),
  `collision_mask = 14` (Foe+Ground+Platform).
- **Ship discipline:** keep the character self-contained — do NOT change the default protagonist
  (`Arena.tscn` / `RunState.Owned[0]`) unless asked. Version-bump the trio, add caveats here,
  doc-sync the character GDD, and **commit with explicit paths — never `git add -A`/`.`** (an
  agent run swept unrelated in-progress files, `Docs/ITEMS.gdd` + `IMPLEMENTATION_REPORT.md`, into
  the PumpKing commit; it had to be reset and re-committed).

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
  (`_coyoteConsumed` guards against repeating it). Touching ground/platform or entering an active
  SoftVolume
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
  (`_zoom`); left-drag pans (`_pan`); right-drag centres the player and rotates around that token.
  UI is a `CanvasLayer`, consumed before `_UnhandledInput`. **An earlier v0.3.2 note here said
  the map used a Camera2D — that was reverted; ignore any stale mention.**
- Generation order matters (v0.8.0): choose worlds and deterministic placement shares → frame
  separate flower-petal coasts → scatter combat nodes → rank level tags by distance to the VOID →
  boss-rooted intra-world tree/loops → local function nodes → distance-selected bridges →
  **zone 6**. Zone 6 retains its previous construction and hidden edges; the new outer-world
  work must never alter its clock or geometry rules.
- Content of nodes (fights, shelter/question-mark effects) is **not implemented** — nodes
  differ by icon only. Difficulty/reward scaling by level is documented, not coded.

### Rendered "atlas" map view (reference, v0.8.0)
- Two views, toggled by the **View** button (`_rendered`, defaults to atlas): the original
  **schematic** (`MapController._Draw` fall-through) and the **atlas** (`MapControllerAtlas.cs`,
  a partial of `MapController`). Both read the SAME `MapGraph` + live state — the atlas adds no
  gameplay, only a rendered layer.
- `MapRenderModel.Build(graph)` precomputes the atlas (`MapRenderModel.RenderedMap`) after
  `MapGenerator.Generate`: each outer realm is one separate, irregular deterministic petal at
  its allocated centre angle, narrow beside the VOID and broad at its outer cap. Height tint
  patches and contour strokes sample the same saved
  altitude field that classifies nodes as `sea-level`, `low-ground`, or `high-ground`. Roads are
  the only route cue. Zone 6 is still a central **pentagon** with 5 lv5 territories; XX-S
  shelters are isolated sea islets; cross-realm edges are **golden causeways**.
- **`MapGenerator.LayoutScale` (1.8) blows the whole map up uniformly.** A uniform scale preserves
  every crossing/midpoint, so a given seed yields the identical map, just bigger — safe to change.
  `MapGenerator.RimRadius` is the outer playable radius (used to size islands + the schematic wedge).
- No territory/barrier layer exists in outer worlds. Coastlines are decorative limits against open
  sea, not a statement that a route is blocked. Do not revive a disconnected-frontier classifier:
  it conflicts with the gameplay graph and creates false navigation cues.
- Every outer node must reach its local 4-1. The generator uses a spatial spanning tree rooted at
  its two LV3 nodes, adds loops, then places exactly one LV3 → `?` → 4-1 route and one distinct
  LV3 → Shelter → 4-1 route. It additionally places 1–3 Shelters and 1–3 `?` nodes per world;
  the two boss approaches do not count toward those totals.
- **Entering ANY zone-6 node = crossing the singularity** (`MapController.EnterVoid`, v0.3.3):
  `_inVoid` latches, **all `WorldIndex != -1` nodes are devoured at once** (no gradual outer
  devour once inside — it's one-way), and the day readout shows **`???`** (time unknowable). The
  atlas renders devoured land as dim "dead ruins" (not black) so the explored map stays legible.
  Lore + rule in `Docs/MapGDD.md` §7.
- **View modes (v0.3.3)**, cycled by the Cam button: `Flat` (top-down), `BossUp`
  (tilted, map spins so the central VOID/boss is always up, player pinned near the bottom),
  `HeadingUp` (tilted, spins so the last step taken points up — set `_lastMoveDir` in `TryMove`).
  Rotation/tilt are smoothed in `_Process`; tilt is an affine vertical foreshorten (`_tilt`),
  NOT true perspective — true book/trapezoid perspective would need a SubViewport→3D quad.
- `DrawColoredPolygon` assumes **convex** polygons. An irregular outer coast is concave, so submit
  its cached triangulated fill (and its altitude patches) separately; never feed the whole coast
  to that primitive or its fill renders wrong.

### Outer-map geometry must be ranked after scatter and bridged from actual distance (v0.8.0, map fix)
- **Symptom:** pre-assigned radial level bands could leave distant nodes connected by implausibly
  long local roads, while fixed ring bridges no longer matched separated realm islands.
- **Rule:** scatter first, then assign LV4/LV3/2-B/2-A/1-B/1-A by increasing VOID distance. Build
  local roads from a Euclidean spanning tree, and select 7–9 cross-world bridges from globally
  distance-sorted candidates with a two-per-world-pair cap and a connectivity check.
- **Why:** level tags and bridges are topology derived from the generated geometry; deciding them
  before the geometry exists creates both false progression and visibly bad connections.

### LINQ `Average` returns `double` for integral selectors (v0.8.0, outer-map generator build fix)
- **Symptom:** assigning `plans.Average(p => p.CombatCount)` directly to a `float` failed with
  `CS0266`.
- **Rule:** explicitly cast an integral `Average` to `float` (or use a float selector) at Godot
  math boundaries.
- **Why:** the LINQ overload produces `double`; C# does not implicitly narrow it to `float`.

### Getting a `Font` for `_Draw` without a Control (reference, v0.2.3)
- **Symptom risk:** `ThemeDB.Singleton.FallbackFont` is easy to get wrong across binding
  versions and there's no toolchain here to catch it.
- **Rule:** from any `Control` you already have a node for, call `GetThemeDefaultFont()` and
  cache the `Font` — reliable for `DrawString` from a `Node2D._Draw`. `MapController` pulls it
  off its `InfoLabel`.

### Autoload C# singletons (reference)
- A C# autoload is registered with its actual module path in `project.godot`; set the
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

### Map sprites need alpha-bound bottom anchoring, not full-canvas stretching (v0.6.7)
- **Symptom:** exported props with transparent padding appear to float above the ground and
  change proportions when the editor stretches the whole PNG canvas into a tile footprint.
- **Rule:** for non-terrain map sprites, derive the visible alpha bounds once, aspect-fit that
  region, and anchor it bottom-center inside the footprint. Only seamless terrain textures use
  fill-footprint rendering.
- **Why:** an image canvas is an export container, not a physical placement contract; collision
  and visual grounding must come from the tile footprint/effect-area data.

### Godot 4.7 AnimatedSprite2D playback uses `Play`, and SpriteFrames uses loop modes (v0.6.7)
- **Symptom:** constructing animation code with `AnimatedSprite2D.Playing = true` fails to
  compile, while `SpriteFrames.SetAnimationLoop(..., true)` compiles only as obsolete.
- **Rule:** add the sprite, call `sprite.Play(animationName)`, and configure looping with
  `SetAnimationLoopMode(name, SpriteFrames.LoopMode.Linear)`.
- **Why:** the Godot 4.7 C# binding exposes playback as a method and replaced the old bool
  loop setter with an explicit loop-mode enum.

### A "preview" and a "commit" are different verbs — rule-tile generation must have both (v0.6.17)
- **Symptom:** the map editor's "Preview gen" ran `RuleResolver` and stored the result in
  `EditorState.Preview` (a throwaway overlay), but nothing ever turned those spawns into real
  `PlacedTile`s — the user's rule zones "did nothing." Read to the user as "the rule tile is broken."
- **Rule:** a generator that a designer paints against needs a **commit** path distinct from its
  preview: a separate button that converts the resolved output into real, undoable tiles
  (`TileBatchCommand` per layer). Reuse the previewed seed on commit so what they saw is what they get.
- **Why:** preview is view-state (never dirties the save, cleared by Esc); committing is a model
  mutation that must be undoable and persisted. Conflating them leaves either no output or an
  un-undoable one.

### `MapDocument.CreateNew`'s layer count is load-bearing — find the battlefield by role, not index (v0.6.17)
- **Symptom:** giving new maps a full default layer stack (farview×4 / battlefield / closeview)
  instead of one battlefield broke `MapJson.RoundTripSelfTest`, which did `doc.Layers[0]` assuming
  index 0 was the battlefield.
- **Rule:** the battlefield is the *distinguished* layer (`Role == RoleBattlefield`), not a fixed
  index. Any code that wants it must scan by role (as `MapBrowser.BattlefieldDims` and
  `MapEditor.DefaultLayerIndex` already did). Never index `Layers[0]` for "the battlefield."
- **Why:** list order is draw order (back-to-front); the battlefield sits in the *middle* of a
  real stack, so its index depends on how many farview sublayers precede it.

### Short-circuited `out` arguments are not definitely assigned (v0.10.0)
- **Symptom:** `if (runtime == null || !runtime.TryUse(out string reason))` would not compile
  when the failure branch displayed `reason`: the first operand can short-circuit, so C# cannot
  prove that the `out` call ran.
- **Rule:** declare the `out` variable before the boolean expression, then assign the result in a
  separate expression (`string reason = null; bool used = runtime != null && runtime.TryUse(out reason);`).
- **Why:** `&&`/`||` preserve runtime short-circuiting, while C# definite-assignment analysis
  correctly refuses to treat a skipped call as an assignment.

### Expiring a status must also clear its queued duration extension (v0.10.0)
- **Symptom:** a decaying status that reached zero could retain a pending wonder-item duration
  extension, so a later fresh application waited on time that belonged to the old stack.
- **Rule:** when a `DecayingDebuff` is inactive, reset both its tick timer and its queued duration
  extension; only an active application may own an extension.
- **Why:** duration is state attached to one status application, not a rechargeable resource that
  survives a complete expiry.

### A generic Node does not expose Control visibility (v0.10.0, Team Build compile fix)
- **Symptom:** clearing a dynamically rebuilt Team Build view failed to compile after setting
  `Visible` on children returned as `Node`.
- **Rule:** when iterating `GetChildren()`, either keep the collection typed as the visual base
  class or pattern-match the `Node` to `CanvasItem`/`Control` before using visual properties.
- **Why:** Godot's scene tree collection intentionally returns the broad `Node` type; C# does not
  infer that a specific runtime parent only contains controls.

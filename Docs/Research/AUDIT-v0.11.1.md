# Fableland audit — design documents against code (begun at v0.11.1, re-checked at v0.11.5)

First pass 2026-11-25 on v0.11.1 with the feedback work still uncommitted; re-checked
2026-11-26 against **v0.11.5** (`9a7972f`). The fighters, foes, hazards and missions are the
same code in both (every cue call site sits on the same line); what moved in between — zone
6's maps, Cloud Court, the TBD registry, `Docs/Feedback.gdd` — is reported as it stands at
v0.11.5. A checking job: no design opinions, no code changes.

**How this was done.** Static reading only — nothing was built or run. Code is cited as
`File.cs → Method`, never by line. Where a number matters the scene was checked for an
exported override; none of the scenes read overrides a value quoted below unless the row says
so. Distances are in metres (32 px = 1 m) unless marked px.

**Documents read.** `Pomegraknight.gdd`, `Cleopastar.gdd`, `Pixolotl.gdd`, `PumpKing.gdd`,
`Sifu Pangda.gdd`, `Gameplay.gdd`, `FOES.gdd`, `ITEMS.gdd`, `NODES.gdd`, `MapGDD.md` §1, §6–§8,
`Instructions/10-DESIGN.md` §6, `Instructions/50-ART-AUDIO.md` §5 (cited as "50-ART §5"); and,
for the cue names, `Feedback.gdd` §2, §6 and §9 with `Instructions/20-ENGINEERING.md` §4.

**Where the files are.**
`Scripts/Protagonists/<Name>/` — `Cleopastar.cs`, `CleoStar.cs`, `Pixolotl.cs`, `PixolotlBubble.cs`,
`PapelPicadoGhost.cs`, `PumpKing.cs`, `PumpKingHead.cs`, `SifuPangda.cs`, `PangdaBottle.cs`,
`Pomegraknight.cs`, `PomeSeed.cs` ·
`Scripts/Bosses/` — `BossBrain.cs`, `BossFighter.cs`, `PumpKing/BossPumpKing.cs` ·
`Scripts/Foes/` — `BaseFoe.cs`, `CrabFoe.cs`, `SeagullFoe.cs`, `PoopProjectile.cs` ·
`Scripts/Hazard/` — `Hazard.cs`, `FireHazard.cs`, `FreezeHazard.cs`, `DamageHazard.cs`,
`PoopHazard.cs`, `TsunamiHazard.cs`, `TsunamiTrigger.cs` ·
`Scripts/Gameplay/` — `Characters/CharacterController.cs`, `Combat/AmmoController.cs`,
`Combat/BodyFeedback.cs`, `World/SoftVolume.cs`, `World/WonderCorePickup.cs` ·
`Scripts/Items/ItemRuntime.cs` · `Scripts/Missions/*.cs` ·
`Scripts/Orchestration/Arena/` — `GameManager.cs`, `ArenaEnvironmentController.cs` ·
`Scripts/Run/RunState.cs` · `Scripts/Nodes/` · `Scripts/Map/MapController.cs` · `Scripts/UI/` ·
`Scripts/Data/` — `CharacterTable.cs`, `BossTable.cs`, `EventDefs.cs`, `EnvironmentEventDefs.cs`.

---

## Part 1 — The feedback work order

One row per gameplay moment. Columns: owner (content id) · moment · where it happens ·
the geometry and numbers the code has at that point · what the GDD says it should look or
sound like · suggested cue action.

**Reading the last column.**
- **The names follow `Docs/Feedback.gdd` §6 and `Pomegraknight.cs`.** The grammar those two use:
  a body's own action is `<slot>.swing` or `<slot>.cast` (slots `ba`, `s1`, `s2`); the thing a
  slot makes is `<slot>.<thing>` — one name for its loop (`CueBus.Start` … `Stop`, never
  `.start` / `.stop`), for its hits, for `.tick` and for `.expire`; a fan is `<slot>.wave` with a
  count; a blast is `<slot>.explode`, cue and hits alike; a body's own moments are bare words
  (`spawn`, `jump`, `land`, `heal`, `death`); statuses are loops `status.*`; a foe's blows are
  `contact`, `s1.<thing>`, `s2.<thing>` and its tell is the loop `telegraph`; systems are
  `world` / `soft.enter`, `hazard` / `tick` (tag = kind), `flow` / `mission.start`.
- **A cell that starts with "raised"** already raises that cue or names that hit at v0.11.5.
  **"partly"** means a generic cue fires that cannot tell this moment from others. **Every
  other cell is not raised yet**, and its name is a proposal in the grammar above — the words
  §6 does not list (`ba.cast`, `ba.throw`, `ba.reload`, `status.shielded`, `shield.break`,
  `passive.*`, `zone`, `storm`, an item's `cast` / `proc`, `flow` / `wave.start` …) are new.
- **"hits `x`"** means: name the `HitInfo` of that attack `x` (`.From(...)` for a body's own blow,
  `.FromRanged(...)` for a projectile, field or tick). The bus then raises one hit cue per
  landed hit; a hit is not a separate `Emit`.
- "ONE cue" marks a moment that covers many objects (a volley) and must not be raised per object.

**Already raised by the base classes — do not wire these again per kit.**
`CharacterController.cs`: `spawn`, `jump` (variant = jump number; tag `swim` / `air`), `land`,
`death`, `respawn`, `heal`, `ult.ready`, `switch.in`, `switch.out`, one hit cue per hit taken
(`TakeHit`), loops `status.burning` / `status.on_fire` / `status.frozen` / `status.trapped` /
`status.stunned`, and `hurt.tick` (tags `hazard`, `burn`, `on_fire`, `frozen`; merged, at most one
per kind per `FeedbackTable.HurtTickCueSeconds`, 0.35 s).
`BaseFoe.cs`: `spawn`, `aggro`, loop `telegraph`, `jump`, `land`, `evolve`, `death`, the same
status loops, `hurt.tick`, one hit cue per hit taken.
The five protagonists' Tab-in "landing effect" is `switch.in` and nothing else: every character
GDD says "**TODO** — define the landing effect".
`Docs/Feedback.gdd` §6 is the list of what is raised already; the tables below are that list
plus everything not raised yet. Its §6.1 says the same of the four kits below: "every hit of a
kit that is not wired yet (Cleopastar, Pixolotl, PumpKing's own, Sifu Pangda)" is `any`.

### 1.1 cleopastar — 21 rows

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| C1 | cleopastar | passive — a Glow is gained (tick) | `AmmoController.cs → Tick` → `CharacterController.cs → OnAmmoReloaded` (Cleopastar does not override it) | magazine `Capacity` 5, `ReloadSize` 1, `ReloadDelaySec` 1.5, repeats while missing (`CharacterTable.cs`, "Cleopastar"); respawn empty | "Generates 1 Glow every 1.5 seconds… displayed on Cleopastar's star spikes (visual only — no numeric HUD)" — Passive; 50-ART §5 "glow pips on spikes" | `ba.reload` (variant = Glows now held; 5 = full) — one name every kit's magazine refill can share |
| C2 | cleopastar | cast — Star Shot (BA) | `Cleopastar.cs → HandleBA`, after `TryConsumeAmmo` | from `FirePoint` (24, −24 px) toward `Controls.AimPoint`; `StarSpeed` 12 m/s; `StarMinDistance` 8 m; `StarMaxDistance` 60 m. Refused with no effect when 5 stars exist, one is still in flight, or no Glow | "extends her free hand and flicks a star toward the mouse cursor"; prompt: "A single, precise outward gesture… No wind-up"; "If fired with 0 Glows, the input is ignored silently" — Basic Attack | `ba.cast` (nothing on a refused press) |
| C3 | cleopastar | spawn + fly — the star (lasts) | starts `CleoStar.cs → Init`; every ending passes `CleoStar.cs → _ExitTree`; flight in `UpdateMotion` (case Flight) | straight line, `_speed` 12 m/s; core `NormalVisualRadius` 0.5 m; trigger `NormalRadius` 2 m; `Lifetime` 16 s; terrain ignored for `TerrainCollisionWaiverSeconds` 0.2 s | "The visible Normal star core is 1 m in diameter. Its 2 m trigger/explosion radius is gameplay space, not its rendered body"; poster prompt: stars "golden-teal (Immature)" | loop `ba.star` (class `projectile`) — `Start` in `Init`, `Stop` in `_ExitTree` |
| C4 | cleopastar | state enter — hover | `CleoStar.cs → EnterHover` (from `ReleaseFlight`, or `UpdateMotion` at 60 m or a buffered release at 8 m) | `HoverDuration` 1 s; the star's soft volume becomes enterable | "the star stops in place and hovers for 1 second" — Stationary | `ba.star.hover` |
| C5 | cleopastar | state enter — fall | `CleoStar.cs → UpdateMotion` (case Hover, inline when `_hoverRemaining` ≤ 0) | `FallSpeed` 0.5 m/s, straight down | "Falls at 0.5 m/s while unoccupied" | `ba.star.fall` |
| C6 | cleopastar | state enter / exit — she is inside a star | `CleoStar.cs → OnStarVolumeBodyEntered` / `OnStarVolumeBodyExited` (and `ClearStarVolumeOccupants`) | circle `NormalVisualRadius` 0.5 m; `StagnationIndex` 0.1; only while the star hovers or falls and is Normal | "Its 1 m core is a Cleopastar-only SoftVolume with StagnationIndex 0.1; Cleopastar falls and moves through it rather than standing on it" | **raised** — `world` / `soft.enter` · `soft.exit`, tag `kit`; nothing of her own is needed |
| C7 | cleopastar | release — star jump | `Cleopastar.cs → TryUseExhaustedJumpSupport` → `CleoStar.cs → TryConsumeForExhaustedJump` | only with no jump charge left and inside the core; the star then explodes (C8) and the base raises `jump` | "it triggers its Normal explosion, vanishes, and provides that jump launch" | **partly** — what a player gets today is the body's ordinary `jump` (tag `swim`). As read, this branch is never reached: inside a star her jump charges are refilled every tick, so the star is not consumed (Part 2 #17). If it is made reachable: `ba.star.jump` |
| C8 | cleopastar | hit — Normal explosion, stationary | `CleoStar.cs → ExplodeNormal` (reached from `OnBodyEntered`, `OnTerrainBodyEntered`, `UpdateTerrainCollisionWaiver` and C7) | circle `NormalRadius` 2 m (+ target `HitRadius`); `StationaryDamage` 30 × launch multiplier; no knockback; the hit is unnamed | "30 DMG in a 2 m radius explosion, then vanishes" — Effect Status › Normal | `ba.explode` (size = 2 m) + hits `ba.explode` — the pair PumpKing's blast uses |
| C9 | cleopastar | expire — lifetime | `CleoStar.cs → _PhysicsProcess` (`_lifeRemaining` ≤ 0; no explosion) | `Lifetime` 16 s from launch; `GlideLifetime` 3 s once gliding | "Lifetime: 16 seconds from launch" / "3 seconds from entering Gliding"; look unspecified | `ba.star.expire` (tag `glide` once gliding) — as Pomegraknight's `s2.seed.expire` |
| C10 | cleopastar | cast — Nut's Decree (Shift) | `Cleopastar.cs → HandleSkill1` | `capturedAim` = `Controls.AimPoint`; `ShiftCooldownSeconds` 3; one `CleoGlideVolley` shared by every star moved; the cooldown is spent even when no star moves | "raises her ankh cane and captures the mouse's world-space aim point"; prompt: "raises the ankh cane forward in a deliberate, slow-extended point" — Shift | `s1.cast` — ONE cue for the volley (magnitude = stars moved, position = aim point) |
| C11 | cleopastar | state enter — gliding | `CleoStar.cs → BeginGlide` | `GlideStartSpeed` 20 m/s → `GlidingMaxSpeed` 40 m/s over `GlideRampDuration` 1.5 s; direction fixed toward the captured point; `GlideLifetime` 3 s | "20 m/s → 40 m/s, accelerating linearly over 1.5 seconds… It passes through SoftVolumes" | loop `s1.glide` on the star, for its trail (the sound belongs to C10) |
| C12 | cleopastar | hit — gliding impact | `CleoStar.cs → ExplodeNormal` with `_motion == Gliding` | circle `NormalRadius` 2 m; damage 30 → `GlidingMaxDamage` 100 and knockback 0 → 6 m/s outward, both by t = `_glideElapsed / GlideRampDuration`; × 1 / 0.65 / 0.30 per target (`CleoStar.cs → CleoGlideVolley.ConsumeMultiplier`) | "scaled DMG 30→100 (same t as speed), knockback 0→6 m/s radially outward"; "the next deals 35% less; the third and later deal 70% less" | `s1.explode` (size = 2 m; magnitude = damage) + hits `s1.explode` (variant = that target's hit number in the volley) |
| C13 | cleopastar | cast — Apep's Maw (E) | `Cleopastar.cs → HandleSkill2` | picks the star with the smallest angle to the aim line, then the nearest; `BlackholeCooldownSeconds` 12; no cooldown when nothing was toggled | "collapses a target star's effect into a Blackhole"; prompt: "raises her free hand slightly palm outward. A restrained, minimal gesture" — E | `s2.cast` (position = the chosen star) |
| C14 | cleopastar | state enter — Blackhole | `CleoStar.cs → ToggleBlackhole` (→ on) + `RefreshCollisionAndVolume` | core `BlackholeVisualRadius` 1.25 m; trigger `BlackholeRadius` 3 m; `PullRadius` 3 m; `PullSpeed` 15 m/s; star volume off | "Visible core: 2.5 m diameter. The pull ring is 6 m in diameter… rendered as a subtle indicator rather than a solid disc"; poster: "deep violet with a radial distortion haze"; 50-ART §5 "Blackhole distortion" | loop `s2.blackhole` — `Start` (size = 3 m) |
| C15 | cleopastar | state exit — back to Normal | `CleoStar.cs → ToggleBlackhole` (→ off); pulls end in `ClearPulls` on the next tick | radii return to the C3 values | "restores the Normal effect… resuming its hover/fall phase" | loop `s2.blackhole` — `Stop` |
| C16 | cleopastar | tick — passive pull on a target | `CleoStar.cs → UpdatePull` (every physics tick, per target) | inside `PullRadius` 3 m: a continuous velocity toward the centre at `PullSpeed` 15 m/s, keyed by `_pullSource` | "Enemies and foes within 3 m are continuously pulled toward the star at 15 m/s"; look unspecified | loop `s2.blackhole.pull`, source = the caught target |
| C17 | cleopastar | state enter — the collapse is triggered | `CleoStar.cs → StartBlackholeSequence` (from `OnBodyEntered`: keeps moving; from `OnTerrainBodyEntered`: stops) | `BlackholeTickCount` 4 × `BlackholeTickInterval` 0.2 s = 0.8 s | "On collision (within 3 m) or ground/platform contact — 4-tick sequence (0.8 s, one tick every 0.2 s)" | `s2.blackhole.collapse` |
| C18 | cleopastar | tick — collapse tick | `CleoStar.cs → ApplyBlackholeTick` (from `UpdateBlackholeSequence`) | every target whose centre is within `PullRadius` 3 m: `BlackholeTickDamage` 10, stun 0.2 s, no knockback; unnamed | "10 DMG · 15 m/s pull toward center · 0.2 s Stun" | `s2.blackhole.tick` (variant 1–4) + hits `s2.blackhole` |
| C19 | cleopastar | hit — execute | `CleoStar.cs → ApplyBlackholeTick` (inside the target loop) → `Execute` | target `CurrentHp` under 10% of `MaxHP` after the tick | "Execute any target with HP below 10% of their max"; look unspecified | `s2.blackhole.execute` (at the victim) |
| C20 | cleopastar | expire — the Blackhole is gone | `CleoStar.cs → UpdateBlackholeSequence` (after the 4th tick) | — | "After the 4th tick the star vanishes" | `s2.blackhole.expire` |
| C21 | cleopastar | ultimate | `Cleopastar.cs → HandleSkillUlt` (empty) | none | "**Deferred.** No Cleopastar Ultimate is in the current implementation scope" (50-ART §5 still lists "brighter suspended stars during Ult") | none — `ult.cast` reserved |

### 1.2 pixolotl — 17 rows

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| X1 | pixolotl | passive — a frame is stored, its ghost appears (spawn) | `Pixolotl.cs → StoreFrame` (from `AdvanceFrames`) → `PapelPicadoGhost.cs → Activate` | every `FrameInterval` 0.3 local s (0.2 real s during E); `FrameCapacity` 8; ghost drawn as a 0.45 m circle; alpha 0.7 → 0.3 over `GhostFadeSeconds` 3.2 (`PapelPicadoGhost.cs → Advance`) | "one translucent marigold ghost… fades from 70% alpha toward 30% alpha over 3.2 seconds"; "marigold-orange path markers, not ominous spectres" — Passive (50-ART §5 says "over 10 s") | `passive.ghost.spawn` (variant = frames held) — three to five a second: a look, not a sound |
| X2 | pixolotl | passive — a ghost is overwritten (expire) | `Pixolotl.cs → StoreFrame` → `PapelPicadoGhost.cs → ForceFade` | `GhostForceFadeSeconds` 0.3 | "overwriting or consuming it force-fades the associated ghost over 0.3 seconds" | `passive.ghost.expire` |
| X3 | pixolotl | release — Branquias volley (BA) | `Pixolotl.cs → HandleBA` | six bubbles at `BubbleAnglesDeg` 0 / 25 / 65 / 115 / 155 / 180° in world space; `BubbleSpeedMps` 6 plus her `LocalMotionVelocity`; from `FirePoint` (22, −20 px); magazine 3, interval 0.3 s | "external-gill bubble firing" — Visual Design; "Each volley fires all six bubbles simultaneously" | `ba.wave` — ONE cue for the six, as Pomegraknight's `s2.wave` (magnitude = 6; variant = volley 1–3) |
| X4 | pixolotl | BA — magazine refilled | `CharacterController.cs → OnAmmoReloaded` (not overridden); the reload is requested in `Pixolotl.cs → HandleBA` only when `Ammo.Current == 0` | `ReloadDelaySec` 0.9 local s → 3 volleys | "Reload 0.9 local-time s, refill to 3 volleys"; look unspecified | `ba.reload` |
| X5 | pixolotl | fly — a bubble (lasts) | starts `PixolotlBubble.cs → Activate`; path in `ApplyAge` | collider radius 32 px (1 m); launch velocity for `BubbleDirectionalSeconds` 1.5; X brakes over `BubbleBrakeSeconds` 0.15; then rises at `BubbleRiseSpeedMps` 2; `BubbleLifetime` 10 local s; at most 18 live | "Bubble diameter 2 m"; "their shared rising phase forms a readable vertical curtain" | none per bubble — X3 carries the count ("a fan of projectiles is one cue"); if a loop is wanted it is `ba.bubble`, 18 at most |
| X6 | pixolotl | hit — a bubble touches a foe | `PixolotlBubble.cs → OnBodyEntered` (hostile branch) | `BubbleDamage` 20 × multiplier; no knockback; stun 0; once per target per bubble; the bubble lives on; unnamed | "It… does not despawn when it touches an enemy"; look unspecified | hits `ba.bubble` |
| X7 | pixolotl | expire — a bubble pops on terrain | `PixolotlBubble.cs → OnBodyEntered` (any other body) → `Deactivate` | Ground or Platform | "A bubble cracks and despawns only on Ground or Platform contact"; 50-ART §5 "bubble cracks" | `ba.bubble.expire` (tag `terrain`) — as `s2.seed.expire` |
| X8 | pixolotl | expire — a bubble's lifetime ends | `PixolotlBubble.cs → _PhysicsProcess` (`_age` ≥ `BubbleLifetime`) → `Deactivate` | 10 local s | "or when its 10-second lifetime expires" | `ba.bubble.expire` (tag `timeout`) |
| X9 | pixolotl | state enter — RANIBOBER held (Shift) | `Pixolotl.cs → HandleSkill1` | needs one stored frame; `Invincible` and `ControlsLocked` on; first jump after `ShiftFirstIntervals` 0.375 s | "On press, Pixolotl enters Rewind state: she becomes invincible, cannot move, and every other input is banned" | `s1.cast`, then loop `s1.rewind` — `Start` |
| X10 | pixolotl | tick — one frame jump | `Pixolotl.cs → RewindOneFrame` (from `_Process`) | teleports to `frame.Position` (nearest free point within 2 m, `ResolveSafeFramePosition`); `_shiftJumps` 1–8; 0.375 s before jumps 1–3, `ShiftLaterIntervals` 0.25 s after; that frame's ghost force-fades; `ShiftCooldown` 15 s starts on jump 1 | "she rewinds to the newest stored frame, then consumes and rewinds to one older frame at each later interval" | `s1.rewind.tick` (variant = jump number) — as `s1.tornado.tick` |
| X11 | pixolotl | tick — her bubbles retrace | `Pixolotl.cs → RewindOwnedBubbles` (inside X10) → `PixolotlBubble.cs → Rewind` | every owned live bubble: age − `_frameTimer` on jump 1, − 0.3 s on later jumps | "Bubbles retrace their path visually"; "time-reversal treatment for bubbles" — Visual Design | none of its own — X10's `s1.rewind.tick` carries it (magnitude = seconds rewound); never one per bubble |
| X12 | pixolotl | hit — arrival | `Pixolotl.cs → ResolveShift` → `ApplyArrivalEffect` | square, half-extent 1.5 m (3 × 3 m) on her centre (+ target `HitRadius`); `ShiftDamage` 25; `ShiftTrapSeconds` 1 s Trapped; heals 0.5 × (frame HP − HP) through `Heal` (the base already raises `heal`); old `ShakeCamera(0.38)` call; unnamed | "All foes in the centered 3 m × 3 m square take 25 damage and receive the existing Trapped status for 1 second" | `s1.arrive` (extents 1.5 × 1.5 m) + hits `s1.arrive`; loop `s1.rewind` — `Stop` |
| X13 | pixolotl | state exit — Shift cancelled | `Pixolotl.cs → CancelShift` | released before the first jump; no cooldown | "it cancels without the arrival effect or cooldown" | loop `s1.rewind` — `Stop` (tag `cancel`) |
| X14 | pixolotl | state enter / exit — recovery | enter `Pixolotl.cs → ResolveShift`; exit `Pixolotl.cs → _Process` (case `ShiftRecovery`) | `ShiftRecovery` 0.25 s, vulnerable, input-locked | "Pixolotl enters a 0.25-second vulnerable recovery" | loop `s1.recover` |
| X15 | pixolotl | cast + state enter — SOBRECARGA (E) | `Pixolotl.cs → HandleSkill2` | `ELocalTimeRate` 1.5; `ESkillCooldown` 10 local s; today's look: a cyan `SelfModulate` set in `Pixolotl.cs → UpdateAnimator` | "cyan active-time feedback during E" — Visual Design; 50-ART §5 "SOBRECARGA trails" | `s2.cast`, then loop `s2.overclock` — `Start` |
| X16 | pixolotl | state exit — E ends | `Pixolotl.cs → EndE` (from `_Process`) | after `EFrameLimit` 10 frames or `EMaxRealSeconds` 3 s | "It ends when it has generated 10 passive frames during this activation or at the 3-second real-time safety limit" | loop `s2.overclock` — `Stop` |
| X17 | pixolotl | ultimate | `Pixolotl.cs → HandleSkillUlt` (empty) | none | "Ult — TBD — No implementation in this milestone" | none |

### 1.3 pumpking — 15 rows, and what `BossPumpKing` adds — 8 rows

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| K1 | pumpking | cast — Pump Up (E) | `PumpKing.cs → HandleSkill2` | `_pumpStacks` + 1 (max `MaxPumpStacks` 5); `ShieldPerPump` 20 (shell up to 100); `PumpInnerCD` 0.5 s; move speed × (1 − `MaxMoveSpeedPenalty` 0.4 × stacks / 5); neck head scale `HeadScales` 1.0–1.5 (`RefreshHeadVisual`) | "inflates his head, gaining one pump stack… Grows the head visually (1.0× → 1.5× local scale)" — E; 50-ART §5 "head stages (1.0→1.5×)" | `s2.cast` (variant = stack 1–5) |
| K2 | pumpking | passive — the shell soaks a hit, or breaks | `PumpKing.cs → AbsorbDamage` | `_shield` 0–100, after defence and Temp HP | "builds a shield pool… active only in Normal state"; look unspecified | `shield.break` when it reaches 0 (the soak itself rides the hit cue, which is raised) |
| K3 | pumpking | passive — shell up / down (lasts) | up: K1; down: `PumpKing.cs → FireHead`, `EnterSoul`, and K2 | — | "Entering Rolling or Soul clears the shield" | loop `status.shielded`, raised by the base beside the other `status.*` loops — `FeedbackState.Shielded` is already computed there |
| K4 | pumpking | release — the head is thrown (BA) | `PumpKing.cs → HandleBA` (Normal) → `FireHead` | `HeadLaunchSpeed` 16 m/s along the aim, plus his own `Velocity`; from `HeadLaunchPoint` (`FirePoint`, 0, −48 px); aim clamped to ±60° ahead or behind (`ClampToFiringSegments`); head scale from `HeadScales[stacks]`; stacks and shell reset; neck head hidden | "launches his head as a rolling projectile in the aimed direction" — BA | `ba.throw` (variant = stacks it was thrown with; direction) — his `ba.swing` |
| K5 | pumpking | fly — the head in the air and rolling (lasts) | starts `PumpKingHead.cs → Init`; ends in `Explode`; motion in `_PhysicsProcess` | collider radius 22 px; `FallGravity` 64 m/s²; `MaxVerticalSpeed` 15 m/s; `MaxRollSpeed` 6 m/s once it has touched down; `Lifetime` 10 s; the "roll" clip plays | "The 6 m/s horizontal speed cap only applies once the head has touched down" | loop `ba.head` (class `projectile`) — the name its touch already hits under |
| K6 | pumpking | hit terrain — a bounce | `PumpKingHead.cs → ResolveContact` (wall or ceiling; floor when speed into it × `BounceRetention` 0.85 ≥ `RestBounceSpeed` 1.5 m/s; a wall met while rolling) | keeps 85% of its speed | "Bounces off terrain with 85% speed retention" | `ba.head.bounce` (magnitude = speed into the surface) |
| K7 | pumpking | state enter — rolling | `PumpKingHead.cs → ResolveContact` (roll branch) | `RollFriction` 3 m/s²; at most 6 m/s across | "Rolls once it lands more gently than that" | `ba.head.roll` |
| K8 | pumpking | hit — the head touches a target | `PumpKingHead.cs → TryStrike` | `ContactDamage` 30 × multiplier; no knockback; then K10 | "Deals 30 contact damage on hitting an enemy, then explodes" | **raised** — hits `ba.head` |
| K9 | pumpking | cast — manual detonate (BA while Rolling) | `PumpKing.cs → HandleBA` (Rolling) → `PumpKingHead.cs → Explode` with `manual` | — | "Manual detonation via BA press. The head explodes immediately" | `ba.detonate` |
| K10 | pumpking | hit — the explosion | `PumpKingHead.cs → Explode` → `DealExplosion` | circle `_explosionRadius` 3 m (+ target `HitRadius`); `_explosionDamage` 50 × multiplier; `_explosionKnockback` 15 m/s outward; four causes: contact, standing still (`StillVelThreshold` 0.1 m/s for `StillDuration` 0.5 s), `Lifetime`, manual; sprite at `ExplosionVisualScale` 0.55 for `ExplosionAnimHold` 0.65 s | "3 m radius AoE… the VFX sprite reads only ~9% larger at its peak frame (~3.3 m)"; "Spawns explosion VFX (prefab, auto-scaled to match radius)" | **raised** — cue `ba.explode` (size = radius; magnitude = damage; tag `dud` when an autonomous head pops with no damage) and hits `ba.explode`. The cause is not on the cue |
| K11 | pumpking | the head is back on his neck | `PumpKing.cs → OnAmmoReloaded` | `ReloadDelaySec` 1.5 s after any explosion (`HandleHeadResolved` → `RequestAmmoReload`) | "the neck head reappears when the magazine is full" | `ba.reload` |
| K12 | pumpking | cast — Soul Form wind-up (Shift) | `PumpKing.cs → HandleSkill1` | `SoulWindupTime` 0.5 s with normal control; `SoulCooldown` 12 s; a head already out becomes autonomous | "The 0.5 s windup adds commitment — it's not a panic button" | `s1.cast` (magnitude = the wind-up) |
| K13 | pumpking | state enter — Soul Form | `PumpKing.cs → EnterSoul` (from `TickTimers`) | `SoulFormDuration` 6 s; `SoulMoveSpeed` 5 m/s free flight; stacks and shell cleared; the "soul" clip plays | "enters a ghostly soul form… Free-flying — full 2D movement" | loop `s1.soul` — `Start` |
| K14 | pumpking | state exit — Soul Form | `PumpKing.cs → ExitSoul` (from `TickTimers`) | back to Normal, or to Rolling if his head is still out | "On exit: returns to Normal (if head gone) or Rolling (if head still out)" | loop `s1.soul` — `Stop` (and in `_ExitTree`: he can be switched out mid-flight) |
| K15 | pumpking | ultimate | `PumpKing.cs → HandleSkillUlt` (empty) | none | no Ultimate in PumpKing.gdd's skill table | none |
| B1 | boss_pumpking | spawn — the fight opens | `BossMission.cs → StageBoss` → `BossPumpKing.cs → BeginFight` | on the perch farthest from the player; row `BossTable.PumpKing`: `BaseHp` 600 × the day's HP multiplier, `DamageScale` 0.6, `KnockbackTaken` 0.4, `StunTaken` 0.25 | "his boss art, entrance and defeat are undesigned (he fades)" — TBD #28; "The bar names the boss and shows its HP" — FOES §8.6 | **raised** — `flow` / `boss.intro` (variant = node level; tag = his id) |
| B2 | boss_pumpking | look — the boss mark | `BossPumpKing.cs → _Draw` | a red triangle 18 × 14 px, 2.3 m above his origin | "A red mark hangs over the boss. Placeholder" — FOES §12 (v0.10.11); 50-ART §5 | none — art replaces it |
| B3 | boss_pumpking | telegraph — a rule starts | `BossBrain.cs → BossRuleSet.Decide` (where a rule's `OnStart` runs); rules are built in `BossPumpKing.cs → BuildRules` | a decision every `DecisionSeconds` 0.15 s; rule names `detonate`, `call back`, `soul away`, `bowl`, `pump up` | "With debug mode on, the mission line shows what each channel is doing" — FOES §8.4; no tell for the player is specified | none today, and no wind-up to hang one on. A tell, once there is one, is the foes' loop `telegraph` (size = reach, magnitude = seconds) |
| B4 | boss_pumpking | strike — bowl | rule `bowl` taps Attack → K4 on the next frame | aim from `BossPumpKing.cs → ThrowDirection` (leads by `LeadSeconds` 0.25 s, clamped to ±60°); wants 0 / 1 / 2 / 3 stacks beyond 0 / 5 / 8 / 14 m (`WantedPumps`) | "aims and throws (BA)" — FOES §8.3 | K4's `ba.throw` — the kit's cue serves both sides (`Side` is on every cue) |
| B5 | boss_pumpking | strike — detonate, call back | rules `detonate` and `call back` tap Attack → K9 | head within `DetonateShare` 0.8 × the blast radius of the player; or rolling away for `HeadGoneSeconds` 0.9 s and 1.5 radii off | "sets it off (BA)" — FOES §8.3 | K9's `ba.detonate`, then K10's `ba.explode` (raised) |
| B6 | boss_pumpking | telegraph — soul away | rule `soul away` taps Skill1 → K12–K14, then `BossPumpKing.cs → FlyToPerch` | player within `PressureRangeM` 3.5 m and ±`PressureLevelM` 2.5 m of level for `PressureSeconds` 1.2 s | "Soul Form (Shift); leaves for the perch farthest from the player" — FOES §8.3 | K12's `s1.cast` — its 0.5 s is the tell |
| B7 | boss_pumpking | state — half health, two foes arrive | `BossMission.cs → Tick` | boss HP ≤ 50% → `FoeSpawner.cs → SpawnWave(2, FoeLevel − 1)`, once | "two foes at foe level −1 arrive once, when the boss falls below half health" — FOES §8.6 | `flow` / `boss.adds` |
| B8 | boss_pumpking | expire — beaten, or stood down | `CharacterController.cs → Die` → `OnDefeated`; `BossMission.cs → Tick`; `BossPumpKing.cs → StandDown` | fade over `DefeatFadeSeconds` 1.2 s | "The body fades and frees itself" — FOES §8.6; 50-ART §5 "simply fades when beaten" | **raised** — `death` on the body and `flow` / `boss.defeated` (the feel layer gives it a camera beat, Feedback.gdd §5.2); a stand-down raises nothing |

On the bus the boss's body is owner `pumpking`, class `boss` (`BossPumpKing` inherits
`PumpKing.FeedbackId`), so every K row is also his, on the Foe side.

### 1.4 sifu_pangda — 21 rows

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| S1 | sifu_pangda | passive — stance changes (Fuhu ↔ Heli) | no stored state: `IsOnFloor()` is read in `SifuPangda.cs → HandleBA`, `HandleSkill1`, `HandleSkill2` and the two cooldown getters | Fuhu = standing on Ground or Platform; Heli = in the air or a soft volume | "clear ground-versus-air martial-arts silhouettes" — Visual Design; Passive 气定神闲 | **partly** — the base's `jump` / `land` and `FeedbackState.Airborne` are all that mark it. No cue of its own is needed if his cues carry the tag `fuhu` / `heli` |
| S2 | sifu_pangda | passive — Chi is gained | `SifuPangda.cs → OnDamageDealt` → `SetChiShield` | + `ChiDamageConversion` 30% of damage dealt; cap `ChiShieldCap` 60 | "Chi shield glow" — Visual Design; "30% of post-mitigation damage Pangda deals" | `passive.chi` (magnitude = Chi gained) |
| S3 | sifu_pangda | passive — Chi shield up / down (lasts) | `SifuPangda.cs → SetChiShield` (0 ↔ above 0); `AbsorbDamage` also writes the shield | 0–60 | "Chi shield glow" | loop `status.shielded` from the base, as K3 |
| S4 | sifu_pangda | passive — Chi soaks a hit, or breaks | `SifuPangda.cs → AbsorbDamage` | after defence and Temp HP | "Immunity: None — Pangda receives damage, stun, Trapped, hazards, and knockback normally"; look unspecified | `shield.break` at 0, as K2 |
| S5 | sifu_pangda | passive — Chi starts to decay | `SifuPangda.cs → TickChiShield` | after `CombatInactivityGrace` 3 s with no damage dealt or taken: − `ChiDecayPerSecond` 10 per second | "Decay after grace 10 shield/s" | `passive.chi.decay` |
| S6 | sifu_pangda | cast — palm wind-up (Fuhu BA) | `SifuPangda.cs → StartFuhuPalm` | `FuhuPalmWindup` 0.2 s, locked; magazine 2, interval 0.6 s, reload 1 s; "fuhu_palm" clip; `_fuhuComboStage` reads 1 on the first palm and 0 on the second | "Each strike is committed: its hit happens after the windup" | `ba.cast` (variant = palm 1 or 2; magnitude = the wind-up; tag `fuhu`) |
| S7 | sifu_pangda | swing + hit — the palm | `SifuPangda.cs → PerformFuhuPalm` (from `TickState`) → `HitAxisAlignedRect` | box `FuhuPalmWidthM` 4 × `FuhuPalmHeightM` 3 m from his centre toward `Facing` (+ target `HitRadius`); `FuhuPalmDamage` 40; `FuhuPalmStun` 0.5 s; no knockback; old `ShakeCamera(0.28)` call, landed or not; unnamed | "Forward hitbox 4 m wide × 3 m high rectangle; begins at Pangda's front… Enemy knockback None" | `ba.swing` (variant = palm; extents 2 × 1.5 m; tag `fuhu`) + hits `ba` (variant = palm) — Pomegraknight's pair |
| S8 | sifu_pangda | state — lunge and recovery | `SifuPangda.cs → TickState` (enters recovery); the lunge in `_PhysicsProcess` | `FuhuPalmLungeDistanceM` 1 m over `FuhuPalmLungeSeconds` 0.2 s; `FuhuPalmRecovery` 0.4 s | "Self lunge 1 m forward over the first 0.2 s of recovery" | none of its own — it always follows S7 |
| S9 | sifu_pangda | BA — palms refilled | `SifuPangda.cs → OnAmmoReloaded` | `ReloadDelaySec` 1 s → 2 palms; combo reset | "reload restores both palms and resets the combo" | `ba.reload` |
| S10 | sifu_pangda | state enter — Falling Crane dive (Heli BA) | `SifuPangda.cs → StartHeliDive` | `HeliDiveSpeedMps` 32 m/s straight down; gravity and knockback ignored; costs no ammo; idle pose (no clip) | "descends straight downward at a fixed speed. He passes through foes on the way down" | loop `ba.dive` — `Start` (tag `heli`) |
| S11 | sifu_pangda | hit — the dive lands | `SifuPangda.cs → TickHeliDive` (when `IsOnFloor`) → `HitAxisAlignedRect` | box `HeliDiveImpactWidthM` 4 × `HeliDiveImpactHeightM` 1 m from his centre down to his feet; `HeliDiveDamage` 50; knock-up `HeliDiveKnockupMps` 10 m/s; `HeliDiveStun` 0.5 s; `HeliDiveRecovery` 0.5 s; old `ShakeCamera(0.45)` call | "Landing hitbox 4 m wide × 1 m high rectangle, centered just above Pangda's feet… Knock-up 10 m/s upward" | loop `ba.dive` — `Stop`; `ba.dive.land` (extents 2 × 0.5 m) + hits `ba.dive` |
| S12 | sifu_pangda | cast — kick wind-up (Fuhu Shift) | `SifuPangda.cs → StartFuhuShift` | `FuhuShiftWindup` 1 s, locked and hittable; `FuhuShiftCooldown` 8 s; "fuhu_kick" clip | "holds a vulnerable, non-moving windup for one second" | `s1.cast` (tag `fuhu`; magnitude = 1 s; extents 3 × 2 m — it is the tell) |
| S13 | sifu_pangda | hit — the kick | `SifuPangda.cs → PerformFuhuKick` (from `TickState`) → `HitAxisAlignedRect` | box `FuhuKickWidthM` 6 × `FuhuKickHeightM` 4 m, forward from his centre, bottom at his feet; `FuhuKickDamage` 40; push `FuhuKickKnockbackMps` 18 m/s along `Facing`; `FuhuKickStun` 1 s; old `ShakeCamera(0.42)` call | "Hit region 6 m wide × 4 m high; its bottom begins at Pangda's feet and extends forward… Knockback 18 m/s forward" | `s1.kick` (extents 3 × 2 m; direction) + hits `s1.kick` |
| S14 | sifu_pangda | state enter — Crane Flight dash (Heli Shift) | `SifuPangda.cs → StartHeliShift` | `_dashDirection` toward `Controls.AimPoint`; `HeliDashSpeedMps` 32 m/s; at most `HeliDashDistanceM` 16 m; `HeliShiftCooldown` 8 s; idle pose | "dashes along the captured cursor direction" | `s1.cast` (tag `heli`), then loop `s1.dash` — `Start` (direction) |
| S15 | sifu_pangda | hit — the dash stops | `SifuPangda.cs → ResolveHeliShift` (from `TickHeliShift`: a foe in the leading `HeliDashCheckBoxM` 2 × 2 m box, a floor, wall or ceiling, or the distance used up) | square `HeliDashImpactBoxM` 2 × 2 m at the stop point; `HeliDashDamage` 40; push `HeliDashKnockbackMps` 16 m/s along the dash; old `ShakeCamera(0.38)` call | "Impact region 2 m × 2 m square centered at the stop point… Knockback 16 m/s along the captured dash direction" | loop `s1.dash` — `Stop`; `s1.dash.land` (extents 1 × 1 m; tag = what stopped it) + hits `s1.dash` |
| S16 | sifu_pangda | state enter — Drink (Fuhu E) | `SifuPangda.cs → StartFuhuDrink` | `DrinkDuration` 1.5 s; move speed × `DrinkMoveMultiplier` 0.4; `FuhuECooldown` 14 s; idle pose | "hulu bottle" — Visual Design; "Movement during drink 40% of normal speed" | `s2.cast` (tag `fuhu`), then loop `s2.drink` — `Start` |
| S17 | sifu_pangda | tick — the drink fills Chi | `SifuPangda.cs → TickState` (case `FuhuDrink`) | at `DrinkShieldAt` 0.8 s the shield is set to `ChiShieldCap` 60 | "Shield grant timing 0.8 s… Fill Chi shield to its 60 cap" | `s2.drink.tick` (magnitude = Chi granted) |
| S18 | sifu_pangda | state exit — the drink ends | `SifuPangda.cs → TickState` (case `FuhuDrink`, timer out) | — | — | loop `s2.drink` — `Stop` |
| S19 | sifu_pangda | release — bottle thrown (Heli E) | `SifuPangda.cs → ThrowHeliBottle` | from `FirePoint` (22, −20 px) toward `Controls.AimPoint`; `BottleSpeedMps` 20; `BottleLifetime` 2 s; `BottleRadiusM` 0.25; `HeliECooldown` 14 s | "Cast: Immediate; no drink phase… 20 m/s / 2 s / 0.25 m" | `s2.cast` (tag `heli`; direction) |
| S20 | sifu_pangda | hit / expire — the bottle | `PangdaBottle.cs → OnBodyEntered` (a foe: the hit; anything else: gone); `PangdaBottle.cs → _PhysicsProcess` (lifetime) | `BottleDamage` 25 × multiplier; `BottleTrappedSeconds` 1 s Trapped; no knockback; unnamed | "First foe, Ground, or Platform… Hit damage 25 · Status 1 s Trapped" | hits `s2.bottle`; `s2.bottle.expire` (tag `terrain` or `timeout`) |
| S21 | sifu_pangda | ultimate | `SifuPangda.cs → HandleSkillUlt` (empty) | none | "**Deferred in the prototype**" (50-ART §5 still lists a "9×9 field": it is in neither the GDD nor the code) | none |

### 1.5 pomegraknight — the reference kit, wired in v0.11.2 — 13 rows

Listed so the other four can be compared with her. Three of her moments still raise nothing:
the reload (G3), the tornado charge running out (G6) and a seed timing out (G12).

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| G1 | pomegraknight | swing — Slash 1 / 2 / 3 | `Pomegraknight.cs → HandleBA` | cone `SlashRange` 130 px, `SlashHalfAngle` 40°; 15 / 15 / 30 (× 1.5 while burning); `SlashKnockback` 96 px/s; move speed × `SwingPenaltyMult` 0.7 for `SwingPenaltyDur` 0.4 s | prompts: "Slash1: Wide horizontal sweep… Slash2: Quick reverse slash… Slash3: Heavy two-handed downward cleave" | **raised** — `ba.swing` (variant 1–3, tag `burning`) |
| G2 | pomegraknight | hit — a slash lands | `CharacterController.cs → MeleeCone` | as G1 | "3 m/s knockback on hit" | **raised** — hits `ba` (variant = stage) |
| G3 | pomegraknight | BA — combo refilled | `Pomegraknight.cs → OnAmmoReloaded` | `ReloadDelaySec` 0.75 s → 3 | "the magazine instantly refills to 3" | `ba.reload` (not raised) |
| G4 | pomegraknight | cast — Blush (Shift) | `Pomegraknight.cs → HandleSkill1` | `BlushDuration` 5 s; `BlushCooldown` 10 s; `TornadoChargeWindow` 3 s | prompt: "Both fists clenched at sides, head thrown back, body engulfed in flames bursting outward. A power-up roar" | **raised** — `s1.cast` |
| G5 | pomegraknight | state — burning (the passive is on) | `CharacterController.cs → SetBurning` … `UpdateStatus` | 5 s; orange tint | "A looping fire particle system activates for the full 5 s duration" — Blush › VFX; 50-ART §5 "Blush flame loop (5 s)" | **raised** — loop `status.burning` |
| G6 | pomegraknight | state — tornado charge ready / lost | set in `Pomegraknight.cs → HandleSkill1`; runs out in `TickTimers`; used in `HandleBA` | 3 s | "If the 3 s window expires without using BA, the Fire Tornado is lost" | `s1.tornado.ready` / `.lost` (not raised) |
| G7 | pomegraknight | state — Fire Tornado (lasts) | `Pomegraknight.cs → ActivateTornado` / `EndTornado` | box `TornadoHalfW` 128 × `TornadoHalfH` 40 px (8 × 2.5 m); `TornadoDuration` 2.5 s; defence `TornadoDefense` 66.7 | prompt: "flames spiraling around her into a vortex… a full tornado cylinder"; 50-ART §5 "Fire Tornado vortex" | **raised** — loop `s1.tornado` |
| G8 | pomegraknight | tick — tornado | `Pomegraknight.cs → TornadoHit` | every `TornadoTick` 0.5 s: `TornadoDamage` 15, push `TornadoPush` 64 px/s, 2 s Burning | "all valid targets inside the rectangle take 15 damage and are pushed outward at 5 m/s" | **raised** — `s1.tornado.tick` + hits `s1.tornado` |
| G9 | pomegraknight | cast — Pome Seed Eruption (E) | `Pomegraknight.cs → HandleSkill2` | `ECooldown` 9 s | prompt: "Arms flung upward, body crouched slightly, seeds spraying outward in an arc" | **raised** — `s2.cast` |
| G10 | pomegraknight | release — a seed wave, three times | `Pomegraknight.cs → SpawnSeedWave` (from the async `FireEruption`) | 6 / 8 / 10 seeds at 0 / 1.0 / 2.5 s; `ESeedSpeed` 320 px/s; cone `EConeMinDeg` 50° to `EConeMaxDeg` 130°; from `FirePoint` (0, −30 px) | "launching pomegranate seeds in an upward spray" | **raised** — `s2.wave` (variant 1–3) — ONE cue per fan |
| G11 | pomegraknight | hit — a seed | `PomeSeed.cs → OnBodyEntered` (hostile branch) | `DamageFirst` 30, `DamageSubsequent` 6 per wave per target; knockback 80 px/s; stun 0.1 s; burning seeds add 2 s Burning | "burning seeds that apply a 2 s Burning debuff"; 50-ART §5 "burning-seed variant" | **raised** — hits `s2.seed` |
| G12 | pomegraknight | expire — a seed on terrain, or timed out | `PomeSeed.cs → OnBodyEntered` (terrain); `PomeSeed.cs → _PhysicsProcess` (`Lifetime` 6 s) | — | "Seeds are destroyed on contact with ground, platforms, or terrain" | **raised** — `s2.seed.expire` on terrain; the time-out raises nothing |
| G13 | pomegraknight | ultimate | not overridden | none | none in the GDD | none |

### 1.6 Foes and mission objectives — crab 9, seagull 11, boss_crab 1, objectives 4

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| F1 | crab | spawn | `BaseFoe.cs → _PhysicsProcess` (first tick) | foe level 1–8; babies carry `SpawnedByDeath` | 50-ART §3 lists "Crab (patrol/aggro/jump/death/spawn-burst)"; FOES §3 gives no look | **raised** — `spawn` (variant = level; tag `born` for a baby) |
| F2 | crab | state enter / exit — aggro | `BaseFoe.cs → EnterAggro`; lost in `BaseFoe.cs → UpdateSight` (3 misses) | sight box 8 × 3 m (12 × 5 m from level 3) standing on its feet, checked every 1 s (`CrabFoe.cs → CanSeePlayer`) | "When the player is detected, moves toward the player horizontally" — FOES §3 | **raised** — `aggro`; add `aggro.lost` |
| F3 | crab | strike — contact | `BaseFoe.cs → TryContactDamage` → `Strike` | within `ContactRange` 44 px; `BaseDamage` 18 × ATK multiplier; `ContactKnockback` 260 px/s; `ContactStun` 0.25 s; `ContactCooldown` 0.9 s; no wind-up | FOES §3 base stats; look unspecified | **raised** — hits `contact`; no cue of its own (there is no wind-up to tell) |
| F4 | crab | hit taken — Soft Shell | `CrabFoe.cs → IncomingDamageMult`, used inside `BaseFoe.cs → TakeHit` | × 2 when the source is within 30° of straight up | "Receives 200% damage (2×) from any hit whose source is within a 60° cone above the crab" | `passive.softshell` (a tag on the hit) |
| F5 | crab | strike — Jump (skill 2, level 6+) | `CrabFoe.cs → UpdateSkills` | when aggro'd and the player is within 3 m or rising fast; launch speed √(2·g·h) to the player's height (at least 1 m); 5 s cooldown; no wind-up; no landing damage | "The crab jumps to the same height as the player's current jump. Does not deal landing damage" | **partly** — the base raises the generic `jump` one tick later, then `land`; a cue of its own would be `s2.cast` |
| F6 | crab | spawn — babies on death (skill 1, level 4+) | `CrabFoe.cs → OnDeath` | 2 crabs at level − 2 (at least 2), ±24 px to the side, 8 px up | "On death, spawns 2 crabs"; 50-ART §3 "spawn-burst" | `s1.spawn` — ONE cue for the pair (magnitude = 2); each baby already raises `spawn`, tag `born` |
| F7 | crab | state — evolve | `BaseFoe.cs → Evolve` | at 25 s alive: + 1 level, HP re-derived and + 30%; green tint for 0.5 s (`_evolveFlashTimer`) | "the foe grows larger, changes color, or swaps sprite. For now a brief flash or tint change is sufficient" — FOES §5; 50-ART §5 "evolution flash" | **raised** — `evolve` (variant = new level) |
| F8 | crab | hit taken; statuses | `BaseFoe.cs → TakeHit`; `BodyFeedback.cs → StatusLoops.Sync` | — | 50-ART §6 "number + flash/blink + knockback" | **raised** — one hit cue per hit; loops `status.*`; `hurt.tick` |
| F9 | crab | expire — death | `BaseFoe.cs → Die` | freed the same frame | FOES §7: loot hook, content TBD | **raised** — `death` (variant = level) |
| L1 | seagull | spawn, aggro, evolve, hit taken, death | as F1, F2, F7, F8, F9 | sight cone 60° × 10 m (120° × 12 m from level 3) along its heading, every 2 s (`SeagullFoe.cs → CanSeePlayer`) | 50-ART §3 lists "Seagull (fly/aggro/poop/dash-telegraph **red tint**)" | **raised** — `spawn`, `aggro`, `evolve`, `death`, hit cue |
| L2 | seagull | strike — contact | `BaseFoe.cs → TryContactDamage` (`HasContactDamage` is true in `SeagullFoe.cs → InitFoe`) | `BaseDamage` 20 × ATK multiplier; range 44 px | "Contact damage: none (does not deal passive contact damage)" — FOES §4; the code disagrees (Part 4) | **raised** — hits `contact` (TBD #39 asks whether the dash should suppress it) |
| L3 | seagull | release — poop dropped | `SeagullFoe.cs → DropPoop` (from `UpdateSkills`) | every `PoopCooldown` 5 s while aggro'd; appears 12 px below; `PoopBaseDamage` 30 × ATK multiplier; no wind-up | "drops a poop projectile that falls with gravity" | `s1.cast` |
| L4 | seagull | fly — poop falling | `PoopProjectile.cs → _PhysicsProcess` | radius 8 px; `FallGravity` 64 m/s²; `Lifetime` 6 s | — | none needed; a loop would be `s1.poop` (class `projectile`) |
| L5 | seagull | hit — poop lands on the player | `PoopProjectile.cs → OnBodyEntered` (player branch) | damage as L3; knockback (0, 120) px/s; stun 0.1 s | "direct hit during the drop also deals 30 base damage" | **raised** — hits `s1.poop` (owner `seagull`) |
| L6 | seagull | spawn — poop hits a surface, a puddle forms | `PoopProjectile.cs → OnBodyEntered` (surface branch) builds a `PoopHazard` | box `LingerBox` 40 × 24 px; `LingerTime` 3 s | "On reaching a surface, it lingers for 3 s" | `s1.poop.expire` (tag `terrain`), as `s2.seed.expire`; the puddle is H7's presence loop |
| L7 | seagull | tick / expire — the puddle | `PoopHazard.cs → ApplyTick`; `PoopHazard.cs → _PhysicsProcess` | damage as L3 every `TickInterval` 0.25 s, the player only | "dealing 30 base damage on contact… it zones the player" | **raised** — `hazard` / `tick`, tag `poop`; its end raises nothing (H7's loop would stop here) |
| L8 | seagull | telegraph — dash | `SeagullFoe.cs → UpdateSkills` (`Skill2Ready` branch) → `BaseFoe.cs → SetTelegraphTint` | stops for `DashTelegraph` 1 s and turns red; `_dashDir` locked toward the player; reach `DashSpeed` × `DashDuration` = 15 m; `DashCooldown` 10 s | "stops moving for 1 s and turns red (telegraph)"; 50-ART §3 "the telegraph is a gameplay promise" | **raised** — loop `telegraph` (size = reach, magnitude = 1 s, direction) |
| L9 | seagull | release — the dash starts | `SeagullFoe.cs → UpdateSkills` (Telegraph branch, timer out) | `DashSpeed` 30 m/s for `DashDuration` 0.5 s; passes through the player | "dashes 15 m (480 px) at 30 m/s (0.5 s)" | `s2.dash` (direction; size = 15 m) — the word its hit already uses; the `telegraph` loop's `Stop` marks the same instant today |
| L10 | seagull | hit — the dash connects | `SeagullFoe.cs → UpdateSkills` (Dashing branch) → `BaseFoe.cs → Strike` | within `HitRadius` 20 + 28 px, once per dash; `DashBaseDamage` 50 × ATK multiplier; knockback 200 px/s along the dash; stun 0.2 s | "Pierces through the player… Deals 50 base damage on contact during the dash" | **raised** — hits `s2.dash` |
| L11 | seagull | state exit — the dash ends | `SeagullFoe.cs → UpdateSkills` (Dashing branch, timer out) | — | — | none needed (if L9 becomes a loop `s2.dash`, it stops here) |
| Z1 | boss_crab | every crab moment, larger | `BossCrab.cs → InitFoe`, `_Ready`; staged by `BossMission.cs → StagePlaceholder` | `BaseHP` × 8; `ContactRange` 80 px; `ContactKnockback` 380; `ContactStun` 0.35 s; `HitRadius` 52 px; scale × 2. No `BabyCrabScene` is handed to it, so F6 never happens | "Placeholder boss = scaled-up Crab" — FOES §12 (v0.5.0); the final boss is undesigned (TBD #1) | **raised** — the F rows' cues under owner `boss_crab`, and `flow` / `boss.intro` · `boss.defeated` |
| O1 | destroy_objective | spawn / hit taken / destroyed | `DestroyMission.cs → Setup`; `BaseFoe.cs → TakeHit`; `BaseFoe.cs → Die` | `HitRadius` 34 px; HP 60 × 1 / 1.5 / 2 / 2.5 (`MissionTable.cs → DestroyObjectiveHp`); does not move | "stationary targets… hittable like foes" — NODES §10 (v0.5.0); look unspecified | **raised** — `spawn`, a hit cue (class `objective`), `death` |
| O2 | protect_core | spawn | `ProtectMission.cs → Setup` | HP 150 / 200 / 250 / 300 (`MissionTable.cs → ProtectCoreHp`) | "a stationary entity placed in the arena" — NODES §4.4 | `spawn` (not raised) |
| O3 | protect_core | hit — a foe chips it | `ProtectCore.cs → _Process` | any foe within `ContactRange` 64 px; `_damagePerHit` 18 × ATK multiplier; `HitCooldown` 0.9 s | "any foe within contact range of the Condensed Wonder Core chips it on the core's own 0.9 s cooldown" — NODES §10 | **raised** — owner `protect_core`, `hurt` |
| O4 | protect_core | expire — destroyed | `ProtectCore.cs → _Process` (HP 0) | the mission fails at once | "If its HP reaches 0, the mission is failed immediately" | **raised** — owner `protect_core`, `death` |

### 1.7 Hazards, tsunami included — 13 rows

Owner `hazard`; the kind is the cue's tag (`Hazard.cs → Kind`, from the scene's `CueKind`).

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| H1 | hazard · fire | tick | `FireHazard.cs → ApplyTick` → `Hazard.cs → Deliver` | box `BoxSize` 48 × 64 px in `FireHazard.tscn` (a map tile passes its own shapes); `FireStack` 20 OnFire points every `TickInterval` 0.25 s; shove `Knockback` 64 px/s away from the centre | not described in the documents read; 50-ART §1: the effect and the hittable shape "must come from one number" | **raised** — `tick`, tag `fire` |
| H2 | hazard · lava | tick | the same script in `Scenes/LavaHazard.tscn` | `FireStack` 30; `Knockback` 0; `CueKind` lava | Gameplay §A.6.3 mentions "falling lava hazards, and persistent fire pits" as deferred | **raised** — `tick`, tag `lava` |
| H3 | hazard · freeze | tick | `FreezeHazard.cs → ApplyTick` | box 96 × 32 px; `FrozenStack` 20 every 0.25 s | unspecified | **raised** — `tick`, tag `freeze` |
| H4 | hazard · spike | tick | `DamageHazard.cs → ApplyTick` in `Scenes/SpikeHazard.tscn` | `Damage` 12 every `TickInterval` 0.5 s | unspecified | **raised** — `tick`, tag `spike` |
| H5 | hazard · damage | tick | `DamageHazard.cs → ApplyTick` | box 64 × 64 px; `Damage` 20 every 0.25 s | unspecified | **raised** — `tick`, tag `damage` |
| H6 | hazard · any | state enter / exit — a body is in it | `Hazard.cs → OnBodyEntered` / `OnBodyExited` | the first tick is immediate | unspecified | `hazard` / `enter` · `exit`, tag = kind (not raised) |
| H7 | hazard · any | spawn — the hazard is there (lasts) | `Hazard.cs → _Ready` | the box, or the map tile's shapes | unspecified | loop `hazard` / `zone`, tag = kind, extents = its box (not raised) |
| H8 | hazard · tsunami | state enter — plate pressed, warning | `TsunamiTrigger.cs → OnBodyEntered` → `ArenaEnvironmentController.cs → OnTsunamiTriggered`, then `TickWarning` | plate `BoxSize` 1 × 1 m; `WarningDurationSec` 2 s: canvas to `#595959`, world tint to `#68727C`; shake ramps over `ShakeRampDurationSec` 3 s toward `SustainedShakeTrauma` 0.75 | "**Warning** fades the authored sky to gray, darkens/desaturates map visuals, ramps shake, and increases visual gusts" — Gameplay §A.6.1 | `hazard` / `trigger` (tag `tsunami`), then loop `world` / `storm` — `Start` (magnitude = 2 s) |
| H9 | hazard · tsunami | spawn + fly — the wave (lasts) | `ArenaEnvironmentController.cs → StartTsunamiSweep`; `TsunamiHazard.cs → _PhysicsProcess` | triangle `Width` 16 × `Height` 8 m; `Speed` 480 px/s leftward from the trigger's `SpawnPosition`; freed past `DespawnX` −300 px | "Wave art / hitbox envelope 16x8 m"; "the same 16x8 Units-derived envelope drives the triangular hitbox and sprite display" | loop `hazard` / `tsunami` (extents 8 × 4 m; direction left) — the name its hit already uses |
| H10 | hazard · tsunami | tick — storm wind | `ArenaEnvironmentController.cs → DeliverStormWindPulse` (from `TickSweep`) | `StormWindPulseMps` 6 m/s leftward every `StormWindPulseIntervalSec` 0.2 s, to the player and every foe | "6 m/s delta-v every 0.2 s, actors only" | loop `world` / `storm.wind` for the sweep — not one per pulse |
| H11 | hazard · tsunami | hit — the wave reaches a body | `TsunamiHazard.cs → OnBodyEntered` | `DamagePercentMaxHP` 35% of the body's max HP; a shove sized to carry it about 15 m; `NoControlDuration` 1 s; once per body | "Damage 35% target Max HP · No-control duration 1 s · Knockback travel target approximately 15 m" | **raised** — hits `tsunami` (owner `hazard`) |
| H12 | hazard · tsunami | state — recovery; the plate re-arms | `TsunamiHazard.cs → Finish` → `ArenaEnvironmentController.cs → OnWaveFinished` → `BeginRecovery`; `TickRecovery`; re-armed in `_PhysicsProcess` (Cooldown case) | `RestoreDurationSec` 2 s; `CooldownSec` 4 s | "wind and shake stop, sky and map visuals restore to their exact authored values, then the trigger cools down" | loop `world` / `storm` — `Stop`; `hazard` / `trigger.ready` (tag `tsunami`) |
| H13 | hazard · wind | tick — ambient gust | `ArenaEnvironmentController.cs → UpdateAmbientGust` | every 3–6 s for 0.7 s, strength 0.18, visual only | "Ambient gusts… visual only: responsive scenery may sway, but player movement is unchanged" — Gameplay §A.6 | `world` / `wind.gust` (magnitude = strength) |

### 1.8 Soft volumes — 4 rows · Pickups — 3 rows

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| W1 | world | state enter / exit — a body crosses into or out of a medium | `CharacterController.cs → EnterSoftVolume` / `ExitSoftVolume`, `BaseFoe.cs → EnterSoftVolume` / `ExitSoftVolume` → `SoftVolume.cs → RaiseCrossing` | once per medium, however many tiles; magnitude = the body's speed; tag = `MediumKind` (`water`, `foliage`, `kit`; a builder may name others) | "a character falls directly into it and never needs to press Down to enter" — Gameplay §A.2; look unspecified | **raised** — `soft.enter` / `soft.exit` |
| W2 | world | release — a swim stroke | `CharacterController.cs → _PhysicsProcess` (jump branch) | `JumpCooldown` 0.3 s | "A jump inside a volume is a stroke" | **raised** — the body's `jump`, tag `swim` |
| W3 | world | state — swimming; resting at the surface | `CharacterController.cs → _PhysicsProcess` (locals `swimming`, `rise`; `SoftVolume.cs → SoftVolumeSet.Covers` on the head) | drop = `GravityIndex` × top speed, or `SinkSpeed` in water | "Up pushes only while the head is under. At the surface a swimmer rests with its head there" | none needed for swimming — `FeedbackState.Submerged` is there to read; `soft.surface` for the head breaking the surface |
| W4 | world | state — carried by a current | `SoftVolume.cs → SoftVolumeSet.Flow`, applied in `CharacterController.cs → _PhysicsProcess` and `BaseFoe.cs → Integrate` | the volume's `Flow` velocity; `IsMedium` false | "A current only carries… A sideways current, a column of bubbles and a waterfall are all this" | `soft.enter` / `soft.exit` with tag `current` — `MediumKind` already answers `current`, but nothing is raised for a pure current today (Feedback.gdd §6.4 says so) |
| U1 | wonder_core | spawn | `CollectionMission.cs → SpawnCore` | at a Level Goal point; 3 on the field; box 30 × 30 px; bobs ±5 px (`WonderCorePickup.cs → _Process`) | "Collect wonder cores that appear on the map. When one is collected, another spawns" — NODES §4.1; look unspecified | `spawn` (owner `wonder_core`, class `objective`) |
| U2 | wonder_core | hit — collected | `WonderCorePickup.cs → OnBodyEntered` → `CollectionMission.cs → OnCollected` | + 1 core to the run at once | as above | `collect` (variant = cores so far) |
| U3 | wonder_core | expire — untouched | `CollectionMission.cs → Tick` (its `Despawn` timer); `ClearCores` at the end | `DespawnTime` 12 s | "Cores may disappear if not collected in time" | `expire` |

No other pickup exists: no item, heal orb or ult orb is ever placed in an arena.

### 1.9 Item skills — 9 rows

All item behaviours are classes inside `ItemRuntime.cs`; a skill is used from
`GameManager.cs → _Process` (action `use_item`) through `ItemRuntime.cs → ItemRuntime.TryUse`.

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| I1 | fanchens_heart | cast — the skill | `ItemRuntime.cs → FanChensHeartBehavior.TryUse` | `AddFrozenStack(60)`; 30 s cooldown | "Grant oneself 60 Frozen stacks" — ITEMS §7.6 | `cast` under the item's id (the base then starts `status.frozen`, raised) |
| I2 | fanchens_heart | passive — heal on being hit | `ItemRuntime.cs → FanChensHeartBehavior.OnDirectDamageTaken` | heals 30% of direct damage taken while Frozen, through `Heal` | "While Frozen: restore 30% of post-mitigation direct damage received" | **partly** — only the body's generic `heal`; `proc` under the item's id would tell it apart |
| I3 | yukais_rope | cast — the rope is fired | `ItemRuntime.cs → YukaisRopeBehavior.TryUse` | a ray 20 m along `FacingDirection` against Ground and Platform; a miss is refused with a HUD toast; 30 s cooldown | "Fire a rope 20 m forward. On hitting the first obstacle (wall, ground, platform), the player is dragged to the impact point" — ITEMS §7.7 | `cast` (direction; position = the anchor); a miss is `ui` / `item.denied` |
| I4 | yukais_rope | state — being dragged | `ItemRuntime.cs → YukaisRopeBehavior.Tick` | pulled toward `_anchor` at min(42 m/s, 9 × distance per second) until within 0.35 m | as above | loop `drag` |
| I5 | forgotten_kashaya | cast — the shockwave | `ItemRuntime.cs → ForgottenKashayaBehavior.TryUse` | circle 4 m (+ target `HitRadius`); 10 damage; knockback 12 m/s outward; stun 0.15 s; 15 s cooldown; unnamed | "Knock back all enemies within 4 m. Deals 10 damage" — ITEMS §7.10 | `cast` (size = 4 m) + hits `cast` |
| I6 | forgotten_kashaya | passive — extra hit | `ItemRuntime.cs → ForgottenKashayaBehavior.OnMeleeDamageDealt` | a second hit worth 50% of a melee hit's damage; unnamed | "On a melee attack hit: bounce back 50% of post-mitigation damage dealt to the target" | hits `proc` |
| I7 | pome_bravery | cast + convert | `ItemRuntime.cs → PomesBraveryBehavior.TryUse` → `ItemRuntime.ConvertHeldItem`; the rebind and the toast are in `GameManager.cs → _Process` | `AddFireStack(60)`; the held item becomes Pome's Seed | "Set oneself on fire (60 OnFire stacks). After use, converts to Pome's Seed" — ITEMS §7.4 | `cast`, then `convert` (the base then starts `status.on_fire`, raised) |
| I8 | pome_bravery, pome_seed | passive — heal on dealing damage | `ItemRuntime.cs → PomesLineageBehavior.OnDamageDealt` | heals 5% of damage dealt while OnFire | "While OnFire: +5% omni-vamp" | **partly** — only the body's generic `heal`; `proc` under the item's id would tell it apart |
| I9 | any held item | skill refused; skill ready again | `ItemRuntime.cs → ItemRuntime.TryUse` (returns false → HUD toast); `ItemRuntime.cs → ItemRuntime.Tick` (cooldown reaches 0) | — | — | `ui` / `item.denied`, `item.ready` |

`twisted_reality` has no arena moment (its skill is on the map). The other eight catalog
entries have no behaviour at all.

### 1.10 Mission moments — 12 rows

On the bus every one of these is owner `flow` (`CueOwners.Flow`); the first column says which
mission the moment belongs to.

| # | owner | moment | where (file → method) | geometry and numbers | GDD says | suggested cue |
|---|---|---|---|---|---|---|
| M1 | flow | arena built; countdown; go; mission starts | `GameManager.cs → RaiseArenaEntered`; `GameManager.cs → _Process` | `PreCombatCountdownSeconds` 3 | "Every Arena encounter begins with a 3-second universal countdown" — NODES §4 | **raised** — `arena.enter`, `countdown.tick` (3, 2, 1), `countdown.go`, `mission.start` (tag = mission type) |
| M2 | flow | mission won / lost | `GameManager.cs → OnMissionResolved` | both sides become invincible | "all players and foes become invincible" — NODES §1.2 | **raised** — `mission.success` / `mission.fail` |
| M3 | flow | player down; run over | `GameManager.cs → OnPlayerDied`, `OnMissionResolved` | — | NODES §2.2 | **raised** — `player.down`, `run.over` (tag `death`, `boss_timer`, `victory`) |
| M4 | collection | goal reached, grace begins | `CollectionMission.cs → OnCollected` → `KillAllFoes` | `GraceTime` 10 s; the foes are freed, not killed | "all remaining foes die and a fixed 10-second grace window begins" — NODES §4.1 | `collection.complete` |
| M5 | slaughter | a wave starts | `SlaughterMission.cs → SpawnWave` | `_waveSize` 3 + node level; the last wave one foe level higher | "The last wave is always fought at +1 foe level" — NODES §4.3 | `wave.start` (variant = wave; tag `final`) |
| M6 | slaughter | a wave is cleared | `SlaughterMission.cs → Tick` (`LiveFoeCount()` reaches 0) | — | "must clear all enemies in the current wave before the next one begins" | `wave.clear` |
| M7 | slaughter | reward choice shown / taken | `SlaughterMission.cs → Tick` (`_awaitingChoice`); `ChooseReward` | + 10 ATK or + 10 DEF | NODES §4.1 | `reward.choice`, `reward.taken` |
| M8 | destroy | one objective fewer | `DestroyMission.cs → Remaining` (polled every frame) | 2 / 3 / 4 / 5 objectives | NODES §4.2 | **partly** — the objective's own `death`; `objective.down` (variant = left) would count them |
| M9 | any mission | the timer | each mission's private `_timer`, read through `Mission.cs → TimeRemaining` in `GameManager.cs → PushMissionHud` | 40 / 60 / 80 / 100 s; boss 240 / 360 s | "Universal time limit" — NODES §4.2; "Timer expiry in any BOSS fight = immediate death" — §4.5 | `timer.low` (the last seconds), `timer.out` |
| M10 | flow | an ambient foe arrives | `FoeSpawner.cs → Tick` → `SpawnOne` | every `Interval` 4 s while under `Cap` 6 | FOES §10, §11 | **raised** — the foe's own `spawn` |
| M11 | flow | reward granted | `GameManager.cs → OnMissionResolved` → `RunState.cs → ReportGoal`; `Hud.cs → ShowToast` | — | "Combat rewards apply the moment the goal is achieved (with a HUD toast)" — NODES §10 (v0.5.0) | `reward.granted` (tag = item id) |
| M12 | flow | Tab switch | `GameManager.cs → TrySwitchProtagonist` → `ReplacePlayerNode` | `SwitchCooldown` 12 s | "they trigger their unique **Protagonist Landing Effect**" — NODES §3.3; all five character GDDs: TODO | **raised** — `switch.out` / `switch.in` on the two bodies |

### 1.11 Row count

| owner | rows |
|---|---|
| cleopastar | 21 |
| pixolotl | 17 |
| pumpking | 15 |
| boss_pumpking (what the boss adds) | 8 |
| sifu_pangda | 21 |
| pomegraknight (the reference kit, wired) | 13 |
| crab | 9 |
| seagull (poop and puddle included) | 11 |
| boss_crab | 1 |
| destroy_objective, protect_core | 1 + 3 |
| hazard (fire, lava, freeze, spike, damage, any × 2, tsunami × 5, wind) | 13 |
| world (soft volumes) | 4 |
| wonder_core | 3 |
| items | 9 |
| missions and flow | 12 |
| **total** | **161** |

### 1.12 Moments with no clean single place to raise a cue

1. **One helper, four attacks (S7, S11, S13, S15).** `SifuPangda.cs → HitAxisAlignedRect`
   delivers the palm, the dive, the kick and the dash. It takes no name, and it lands hits
   without the attacker's share of hit-stop that `MeleeCone` gives (`SwingStop` is the recipe:
   20-ENGINEERING §4, step 2). The action has to be passed in from the four callers.
2. **Strikes that fire from a timer (S7, S13, S17, K13, K14).** Pangda's palm and kick land
   inside `TickState` when a state timer runs out (both carry a `// NOTE(animation)` marker);
   the drink's shield arrives the same way; PumpKing enters and leaves Soul Form from
   `TickTimers`. The cast and the effect are in different methods and different frames.
3. **Stance has no state (S1).** Fuhu or Heli is `IsOnFloor()` asked at the moment of each
   press; nothing records a change.
4. **Shield pools are written in several places (K2, K3, S3, S4).** `PumpKing._shield` is set in
   `HandleSkill2`, `FireHead`, `EnterSoul` and `AbsorbDamage`; Pangda's `_chiShield` in
   `SetChiShield` and, bypassing it, in `AbsorbDamage`. "The shield broke" has no one line; the
   base's `FeedbackState.Shielded` is the one place that already sees both pools.
5. **A star ends four ways and none says which (C8, C9, C12, C20).** `ExplodeNormal`, the
   lifetime check in `_PhysicsProcess` and `UpdateBlackholeSequence` all `QueueFree`; only
   `_ExitTree` sees every ending and it cannot tell them apart. `ExplodeNormal` itself is
   reached four ways (C8) with no reason passed, and decides stationary or gliding inside.
6. **The hover-to-fall change is inline (C5)** in a `switch` case of `UpdateMotion`.
7. **Per-tick fields with membership in a loop (C16, I4, W3, W4, H10).** The Blackhole pull, the
   rope drag, swimming, currents and the storm wind all re-apply a velocity every tick; who
   is inside is worked out in the loop, so "entered" and "left" exist only as list changes
   (`_pulledFoes`, `_windTargets`) or not at all.
8. **Ticks inside loops (C18, C19, G8).** `UpdateBlackholeSequence` runs its ticks in a `while`
   (two can fire in one long frame); `ApplyBlackholeTick` loops over targets, and the execute
   is a branch inside that loop — the victim's own `death` cue cannot tell it was executed.
9. **A volley's size is known only after its loop (C10).** `HandleSkill1` loops the stars and
   each decides in `BeginGlide` whether it moves; the cooldown is spent even if none does.
10. **A toggle whose direction is known only to the star (C13–C15).** `HandleSkill2` returns
    silently in three cases; whether it switched a star on or off is inside `ToggleBlackhole`.
11. **Bubble phases are computed, not entered (X5).** `ApplyAge` derives the position from the
    age each tick, and Shift runs the age backwards (X11), so launch, brake and rise have no
    transition. `Deactivate` is shared by the pool warm-up in `_Ready`, the pop and the
    time-out: a cue raised inside it would fire 18 times at spawn.
12. **Ghosts come from a `while` loop (X1, X2).** `AdvanceFrames` can store several frames in
    one frame; `ForceFade` serves both "overwritten" and "consumed by a rewind".
13. **Shift is spread over five methods (X9–X14).** The press is in `HandleSkill1`, the release
    is read in `_Process`, the jumps in `RewindOneFrame`, the result in `ResolveShift` or
    `CancelShift`. `RewindOneFrame` can resolve in the same call as its last jump.
    `ResetDebugCombatState` leaves E without going through `EndE`.
14. **Asynchronous waves (G10).** `FireEruption` awaits timers between waves and simply
    returns if she has left the tree; a loop opened at `s2.cast` would never be closed.
15. **A timer that just runs out (G6).** `_tornadoCharge` is counted down in `TickTimers`
    with no branch for reaching zero.
16. **Bounce has three sites and rolling has no edge (K6, K7).** `ResolveContact` reflects in
    three branches; the roll branch runs every tick the head rests (`_ticksSinceRoll` can
    give the edge). `Explode` has four causes and is told only whether it was manual.
17. **Projectiles outlive their owner (C3, K5, X5, G11).** Tab frees the body; stars, the head,
    bubbles and seeds fly on. `PomeSeed` and `PumpKingHead` already keep the owner's id as a
    string; `CleoStar`, `PixolotlBubble` and `PangdaBottle` keep none and name no hit.
18. **Loops that nothing stops on exit.** Only `Pomegraknight` overrides `_ExitTree` to stop
    her loop. PumpKing can be switched out in Soul Form (his `CanSwitchProtagonist` is the
    base's), and Pangda can die mid-state (`SifuPangda.cs → _Process` returns early when dead
    and leaves `_state` as it was).
19. **The boss acts one frame after his brain decides (B3–B6).** A rule taps a virtual button;
    the kit reads it on its next frame. The only common hook is `BossRuleSet.Decide`. "Bowl"
    has no wind-up, so a telegraph cue there has no lead time; adding one changes the fight.
20. **Foe skills that are a velocity write (F5) or a result hidden in the intake (F4).** The
    crab's jump sets `IntentVel.Y`; the `jump` cue is deduced a tick later in `Integrate`.
    Soft Shell's × 2 is applied inside `TakeHit` and is not carried on the hit cue.
21. **Foes removed without dying (M4).** `CollectionMission.KillAllFoes` and scene changes
    `QueueFree` foes directly: `_ExitTree` stops their loops but no `death` is raised.
22. **Hazards have no presence or entry cue (H6, H7)**, only a tick per body per interval; and
    the realm variants of one hazard share one scene, so their cues are indistinguishable
    (five kinds: `fire`, `lava`, `freeze`, `spike`, `damage`).
23. **The tsunami is already presented from orchestration (H8–H12).**
    `ArenaEnvironmentController` tints the canvas and the world and drives
    `ShakeCamera2D.SetSustainedTrauma` itself; its phases change in four methods and one
    inline case. A cue-driven version must not shake twice.
24. **A pure current raises nothing (W4).** `RaiseCrossing` fires when a body's `InMedium`
    flips, and a current (`IsMedium` false) never flips it. The map builder does not set
    `MediumKind`, so a cloud reads as `foliage`.
25. **Item procs reach the bus as a generic `heal` (I2, I8)**, and a conversion is split
    between the behaviour, `ConvertHeldItem` and `GameManager._Process` (I7).
26. **Missions are polled objects (M5–M9).** They raise nothing and are read every frame.
    Wave 1 is requested in `Setup` but released later by `FoeSpawner.OpenSpawnGate`; "wave
    cleared" is a count reaching zero; every timer is a private float with no threshold.
27. **A pickup frees itself as it is collected (U2):** the cue needs a position, not a source.
28. **The star jump is written but, as read, never runs (C7).** A star's volume counts as a
    medium, a medium refills the jump charges every tick, and the star is asked only when no
    charge is left. A cue put in `TryConsumeForExhaustedJump` would sit on a line that does
    not execute; it needs a decision (Part 2 #17) before it needs a cue.
29. **Five old `ShakeCamera` calls fire whether or not the blow lands (S7, S11, S13, S15,
    X12).** They are the pre-bus way; step 6 of the recipe removes each when its hit is named.

---

## Part 2 — The twenty gaps a player would hit

Ordered by how much of a new player's session each one touches, starting from the title
screen. Effort: S ≤ 30 min, M ≤ 2 h, L more. Four entries (12, 13, 16, 17) come from reading
the code only and should be confirmed in play before anyone fixes them.

| # | What | Evidence | Smallest fix | Effort |
|---|---|---|---|---|
| 1 | **No controls are shown anywhere** — not on the title, the map, in the arena or in the pause menu. Attack, the two skills, the item, Tab, swimming and drop-through have to be guessed. | `Scenes/Menu.tscn` (title, "Map Creation", "Play", slots); `Scenes/Hud.tscn` (no hint node — the only key shown is the item slot's tooltip, `Hud.cs → SetHeldItem`); `PauseMenu.cs → _Ready` (Continue, Settings, Save & Quit). Bindings: Gameplay §A.2, NODES §4.6 | A static controls panel in the pause menu and one line under "Get ready…" (`GameManager.cs → _Process`) | S |
| 2 | **Rewards are never named, and the note that TwistedReality was granted is thrown away.** After a win the HUD says "Goal achieved!"; the player learns what they got only by opening Team Build at a hub. TwistedReality is the only way into zone 6. | `GameManager.cs → OnMissionResolved` toasts `Mission.cs → SuccessToast` (only `BossMission` overrides it); `RunState.cs → ReportGoal` adds "The capital yields TwistedReality." to `DayEndNotes`, and `RunState.cs → EndDay` clears that list before it builds the summary. NODES §10 (v0.5.0) "with a HUD toast"; ITEMS §7.12 | Build the toast from the `RewardBundle`; keep goal notes until the day-end summary has used them | S |
| 3 | **Wonder cores are never shown and nothing spends them.** Collection is 60% of combat nodes and its whole reward is cores. | `RunState.cs → AddWonderCores` and `ApplyRewards` are the only writers; nothing but the save reads `WonderCores` — no HUD, map or shelter shows it; no trader exists (NODES §5: not generated). NODES §4.1; TBD #11 | Show the count on the map's info label and in the success toast (S). A use for them needs a design decision (L) | S / L |
| 4 | **Debug tools are on every screen.** A "DBG" button is always visible; one click gives SKIP, free travel on the map and every catalog item in Team Build. On the map, "Dice" and the seed box start a new run over the current one with no confirmation (the slot file is overwritten at the next save). | `DebugManager.cs → _Ready` ("toggle button (always visible)"), `OnToggle`; `GameManager.cs → OnSkipRequested`; `MapController.cs → TryMove` (debug branch), `OnDice` / `OnSeedSubmitted` → `Restart` → `RunState.cs → NewRun`; `ShelterController.cs → AddItemChoices`; `Scenes/Map.tscn` (Dice, seed, Mist, View, Cam, Fields); `Scenes/Menu.tscn` ("Map Creation" first) | Show these only in a debug build or behind a launch flag | S |
| 5 | **The ult ring fills and Q does nothing**, for all five fighters. | `CharacterController.cs → GainUltCharge` (max 1500, raises `ult.ready`); `Hud.cs → SetUltCharge`; `HandleSkillUlt` is empty in `Cleopastar.cs`, `Pixolotl.cs`, `PumpKing.cs`, `SifuPangda.cs` and not overridden in `Pomegraknight.cs`. Every character GDD defers its Ultimate; Gameplay §A.2 "(if unlocked)"; TBD #21 | Hide the ring and skip `ult.ready` until an ultimate exists | S |
| 6 | **The HUD leaves out what a fighter needs.** No magazine is shown — Cleopastar's 5 Glows, Pixolotl's 3 volleys, Pangda's 2 palms, Pomegraknight's 3 swings — and a refused attack is silent. The switch slot and its cooldown disappear whenever Cleopastar is next, which in the default team is whenever Pixolotl is played. The portrait is the same face for all five. | Nothing subscribes to `AmmoController.cs → Changed`; `Scenes/Hud.tscn` has no ammo node. `Hud.cs → _nextMugshotPaths` has no "Cleopastar" entry, so `SetNextProtagonist` hides the slot (her `UI/Mugshot.png` exists); `Hud.cs → SetHp` picks one of six generic `mugshot_*.svg`. Gameplay §A.2.1 "UI: the controller emits… presentation observes them"; Cleopastar.gdd "Glows are displayed on Cleopastar's star spikes" | Add the missing entry (S); pips beside the mugshot, driven by `Changed` (M) | S / M |
| 7 | **Two of the three default fighters do not animate, and the first one's air moves hold the idle pose.** | `RunState.cs → PrototypeDefaultTeam` is Sifu Pangda, Pixolotl, Cleopastar. `Scenes/Pixolotl.tscn` and `Scenes/Cleopastar.tscn` have no `AnimationPlayer`; `Cleopastar.cs` has no `UpdateAnimator` and `Pixolotl.cs → UpdateAnimator` only tints. `SifuPangda.cs → UpdateAnimator` plays "idle" for the dive, the dash, the drink and death. Sheet folders exist for both (`Cleo_idle` / `_walk` / `_jump` / `_attack`, `Pixo_idle` / `_walking` / `_jump` / `_attaking`). Sifu Pangda.gdd "temporarily hold the idle animation" | Wire the existing sheets the way Pomegraknight's are wired | M each; Pangda's missing art L |
| 8 | **Most attacks are seen only as debug outlines, and there is no sound.** Since v0.11.2 a hit shakes the camera, flashes its victim and freezes the two bodies; nothing else is drawn or heard. | `CharacterController.cs → ShowDebugRanges` defaults to true and nothing switches it off, so every fighter's `DrawDebug` (and the boss's) is always drawn; stars, bubbles, ghosts and the bottle are `_Draw` circles. No audio file and no audio player exist in the project; Feedback.gdd §9 defers "Particles and drawn effects; all audio". 50-ART §1, §5, §7 | This is the work Part 1 orders | L |
| 9 | **The run ends on a scaled-up crab.** Since v0.11.3 zone 6 borrows the run's own worlds — an LV5 fight is dealt one of their maps and the core one of their boss arenas, drawn drained and dark — but the final boss is still the stand-in crab, staged on a perch built for PumpKing, and the victory screen says the dark leader was defeated. | `RunState.cs → BeginAdventure` names a boss only for level 4 and deals zone 6 its map through `CombatMapCatalog.cs → SelectForVoid`; `BossMission.cs → StagePlaceholder` fields `BossCrab` (crab HP × 8 at foe level 8); `GameManager.cs → ApplyVoidTreatment`; `RunOverController.cs → _Ready`. NODES §2.1; MapGDD §7; TBD #1, #33 | Design and content | L |
| 10 | **Every realm's boss is the same BOSS PumpKing, and beating him recruits nobody.** The run also starts with all five fighters, in Pomegraknight's home, with a team she is not in. | `BossTable.cs → ForWorld` falls back to `StandIn`; `RunState.cs → EnsurePrototypeRoster`, `GrantProtagonist` (no-op when owned), `NewRun` (`MapGenerator.PomegraknightHome`). MapGDD §1 "the starting protagonist (others unlock…)"; NODES §3.1, §4.5; TBD #2, #29 | Start with one protagonist (S); seven more bosses (L) | S / L |
| 11 | **The seagull does not match FOES §4.** It hurts on touch, bombs from foe level 2, its puddle hits four times a second, and its dash is aimed in two dimensions. | `SeagullFoe.cs → InitFoe` (`HasContactDamage` true, `BaseDamage` 20); `UpdateSkills` (poop on its own `_poopCd`, not `Skill1Ready`; `_dashDir` is the full vector to the player); `PoopHazard.cs → ApplyTick` every `TickInterval` 0.25 s. FOES §4: "Contact damage: none", "Base ATK 18", "Poop (level 4+)", "30 base damage on contact", "horizontally toward the player's current X position". TBD #39 adds that the contact hit usually lands first and the i-frames it starts refuse the 50-damage dash | Four small edits, or four GDD lines | S |
| 12 | **Yukai's Rope may never let go** *(read, not run)*. The drag ends only within 0.35 m of the anchor; the anchor is a point on a wall and the holder's box is 0.75 m half-wide, so the pull (up to 42 m/s) would hold until a Tab switch or the end of the fight. Every outer boss pays out this item. | `ItemRuntime.cs → YukaisRopeBehavior.TryUse` (horizontal ray from the body's centre), `Tick` (the 0.35 m test is the only end); the 48 × 64 px `RectangleShape2D` in every protagonist scene. ITEMS §7.7 | End the drag on first contact or after a time-out | S |
| 13 | **Pixolotl can lose the end-of-mission invincibility** *(read, not run)*. Using Shift after the goal resolves switches it off; foes and hazards still on the field can then kill her, which ends the run. | `GameManager.cs → OnMissionResolved` sets `Invincible` on the player; `Pixolotl.cs → ResolveShift` and `CancelShift` set it to false unconditionally; `GameManager.cs → OnPlayerDied` → `RunEndKind.Death`. NODES §1.2 "until the player leaves it" | Restore the value Shift found instead of false | S |
| 14 | **Two item passives work for some fighters only.** The Forgotten Kashaya's never triggers for Sifu Pangda; Pome's Bravery / Seed's heal ignores Cleopastar's and PumpKing's damage. | `MeleeDamageDealt` is raised only in `CharacterController.cs → MeleeCone` (Pomegraknight's slash); Pangda's blows go through `SifuPangda.cs → HitAxisAlignedRect`. `CleoStar.cs → ExplodeNormal` / `ApplyBlackholeTick` and `PumpKingHead.cs → TryStrike` / `DealExplosion` never call `ReportDamageDealt`. ITEMS §7.4, §7.10 | Raise the two events from those hit sites | S |
| 15 | **Fire Tornado differs from its GDD.** It is 8 × 2.5 m, pushes at 2 m/s, comes on top of Slash 1 rather than instead of it, leaves her free to walk, and sets targets burning. Holding the attack button does not chain swings. | `Pomegraknight.cs` exports `TornadoHalfW` 128, `TornadoHalfH` 40, `TornadoPush` 64; `HandleBA` (slash, then `ActivateTornado`); `TornadoHit` (`SetBurning(2f)`); `CharacterController.cs → HandleAbilities` reads `JustPressed` only. Pomegraknight.gdd › Fire Tornado: "3 m (length) × 2 m (height)", "5 m/s radial push", "replaced by", "locked in the animation"; "Hold LMB to chain swings" | Numbers S; replace-and-lock M | S / M |
| 16 | **A crossed Eidolon Shelter has no way out** *(read, not run)*. After crossing between two outer realms the shelter stays on the map; opening it again shows a screen where every exit is disabled. A failed first fight on the far side puts the player back on that very node. | `ShelterController.cs → Refresh` (Rest, both Sharpens, Finish the Day and Back all disabled when the shelter is Eidolon and its Blessing is spent; the scene has no pause handler); `MapController.cs → TryMove` (a Shelter always opens, the current one included); `RunState.cs → TryCrossEidolonShelter` (sets `PreviousNodeId` to the shelter) and `ReportGoal` (bounces there on failure). NODES §1.2 disables exactly these exits | Enable Back once the Blessing is spent, or make the node inert | S |
| 17 | **Cleopastar's star jump never happens** *(read, not run)*. In the GDD a star is spent by a jump: it explodes and launches her. In the code a star refills her jump every tick while she is inside it, so she can jump out of one again and again and it is never consumed; the exploding branch is not reached. | `CharacterController.cs → _PhysicsProcess` (jump charges are refreshed whenever `_softVolumes.InMedium`, before the jump is read; `TryUseExhaustedJumpSupport` is asked only when no charge is left); `SoftVolume.cs → SoftVolumeSet` (any `IsMedium` volume sets `InMedium`, and the star's is one — `CleoStar.cs → _Ready`); `CleoStar.cs → TryConsumeForExhaustedJump`. Cleopastar.gdd › Stationary: "If she presses jump while inside it with no normal jump charges remaining, it triggers its Normal explosion, vanishes, and provides that jump launch" | Do not refill jumps from a volume that is not `Swimmable`, or change the GDD line | S |
| 18 | **"?" nodes have three placeholder events** for the 20–30 event nodes of a run. | `EventDefs.cs → All` (`traveller`, `old_shrine`, `abandoned_cart`); `EventController.cs → PickEvent`; `MapGenTable.cs` (4–6 event nodes per realm). NODES §6.1; TBD #3 | Content | M |
| 19 | **Most of the item catalog cannot be met in a run.** Five items have a source; eight have none and no behaviour; Pome's Seed has no use; nothing drops from a foe and nothing lies on the ground. A benched fighter's item neither cools down nor returns to the backpack. | Sources: `ProtectMission.cs`, `DestroyMission.cs`, `BossMission.cs → Reward`; `RunState.cs → ReportGoal` (TwistedReality); `EventDefs.cs` (Pome's Bravery). `ItemRuntime.cs → Behaviors` has five entries for the 14 of `ItemCatalog.cs → Entries`; `BaseFoe.cs → OnDeath` is empty; `ItemRuntime.cs → ItemRuntime.Tick` runs for the active body only; `RunState.cs → HoldItem` (its note on benching). ITEMS §1.1, §1.2; NODES §3.3, §4.6 | Content and design | L |
| 20 | **Loose ends on the shell screens.** Settings is a placeholder sentence; the title has no Quit; the map opens with the mist off; an arena or event has no Map button; death cuts straight to RUN OVER. | `PauseMenu.cs → ShowSettings`; `Scenes/Menu.tscn`; `MapController.cs` (`_mist` starts false); `GameManager.cs → OnPlayerDied` → `RunState.cs → EndRun`. MapGDD §8a "Always on in the real game"; NODES §1.1 (deferred in §10) | One small change each | S each |

Also met, not ranked: ambient foes keep arriving during Collection's 10-second grace
(`GameManager.cs → _Process` ticks the spawner until the mission resolves; NODES §4.1 says the
foes die); Pixolotl's magazine reloads only once it is empty (`Pixolotl.cs → HandleBA`; the GDD
does not say); Tab-in landing effects do not exist (TBD #22).

---

## Part 3 — The TBD registry (`10-DESIGN.md` §6)

The registry has 47 rows at v0.11.5; #36–#47 are the questions the hit-feedback work left,
registered in v0.11.3. "Blocks" answers: does this stop a playable prototype — yes / no /
only its presentation. No row answers yes.

| # | What the code does today | Blocks |
|---|---|---|
| 1 | The LV6 node names no boss, so `BossMission.cs → StagePlaceholder` fields `BossCrab` (crab HP × 8, foe level 8, 360 s) on a boss arena borrowed from one of the run's worlds; winning ends the run in victory and the run-over text names "the dark leader" | no — a stand-in fight exists |
| 2 | One row, `BossTable.cs → PumpKing`; `ForWorld` gives him to all eight worlds; no phases; `CharacterController.cs → Execute` lets a Blackhole execute him under 10%; his brain sets a closing head off beside the player (`BossPumpKing.cs → BuildRules`, "detonate") | no |
| 3 | Three placeholder events in `EventDefs.cs → All`, picked per node from the run seed; verbs are cores, heal, one item, nothing | no — thin and repeating |
| 4 | No Wish: `ShelterController.cs → _Ready` wires Rest, two Sharpens, Team Build, Finish, Back | no |
| 5 | No plantation; `Plantable` is a tag on three catalog rows; `DayEndPipeline.cs → PerishStep` and `NpcWindowsStep` are empty | no |
| 6 | Nothing persists between runs; three run slots; the pause menu's Settings is a placeholder dialog | no |
| 7 | Five counters are kept in `RunState.cs` and printed by `RunOverController.cs`; nothing reads them to gate LV5/LV6 | no |
| 8 | `pixolotls_feather` is a catalog row with no behaviour and no source | no |
| 9 | `SlaughterMission.cs`: wave size 3 + node level, the map's crab/seagull weights (`FoeSpawner.cs → SetComposition`, else 60/40), last wave + 1 level | no |
| 10 | No time scaling in zone 6: `GameManager.cs → _Ready` fixes foe level 7 at LV5 and 8 at LV6 | no |
| 11 | No trader. Wonder cores are counted and saved; nothing shows or spends them | no — but it leaves the currency without a use (Part 2 #3) |
| 12 | `BaseFoe.cs → OnDeath` is an empty hook; `BossDef.HeldItemDefId` exists and nothing reads it | no |
| 13 | `the_void` is a catalog row; no phantom exists | no |
| 14 | Delivered at once: `GameManager.cs → OnMissionResolved` → `RunState.cs → ReportGoal` → `ApplyRewards`; the toast does not say what was delivered | only its presentation |
| 15 | `ProtectCore.cs` has no way to be healed | no |
| 16 | Not generated: hubs offer the four services only | no |
| 17 | None | no |
| 18 | Leveling is removed. Pixolotl has no `AnimationPlayer`; PumpKing has idle / walk / jump / soul and the head; Pangda has idle / walk / jump / palm / kick and idle for the rest | only its presentation |
| 19 | `SifuPangda.cs → HandleSkillUlt` is empty; no Gui Lin world; he is owned from day 1 and leads the default team | no |
| 20 | Kingdom of Horror is in the pool; every run still starts in Pomegraknight's home (`RunState.cs → NewRun`); PumpKing is owned from day 1 | no |
| 21 | Charge accrues (1 per damage dealt, 2 per damage taken, max 1500), the HUD ring fills and `ult.ready` is raised; no kit has an ultimate and no orb drops | only its presentation — a meter with nothing behind it (Part 2 #5) |
| 22 | No landing effect; a switch raises `switch.out` / `switch.in` | no |
| 23 | Fixed: Protect → 10 cores + FanChen's Heart; Destroy → Kashaya; Slaughter → +10 ATK or DEF; outer boss → Yukai's Rope (plus TwistedReality at the home LV4) | no |
| 24 | The Forgotten is in the pool; Pixolotl is owned from day 1 wherever the run starts | no |
| 25 | No destructible scenery anywhere under `Scripts/` | no |
| 26 | Decided provisionally in v0.11.3: Cloud Court is the Palace of LOOING's sky district, and a Palace node on high ground can be dealt `Maps/cloud_court_heavens.json` (MapGDD §2.2). The row also says `seashore_5` is dealt by no outer node; the map code was not audited here | no |
| 27 | Swimming is implemented (`CharacterController.cs → _PhysicsProcess`, `SoftVolume.cs → SoftVolumeSet`); none of its numbers was checked here | no |
| 28 | Red mark (`BossPumpKing.cs → _Draw`) and a 1.2 s fade on defeat; `flow` / `boss.intro` and `boss.defeated` are raised and the feel layer gives the defeat a camera beat (Feedback.gdd §5.2); no art, entrance or sound | only its presentation |
| 29 | `RunState.cs → EnsurePrototypeRoster` grants all five; `GrantProtagonist` is a no-op, so the toast says "falls!" and never "joins you" | no |
| 30 | "Slope" appears in three tile files (`TileRegistry.cs`, `RealmTiles.cs`, `HorrorTileSet.cs`); the map code was not audited | no |
| 31 | `BossFighter.cs → BossSetup.ApplyRow` scales HP and damage only; a boss is not a `BaseFoe`, so it never evolves | no |
| 32 | 45 maps in `Maps/`; their design was not audited | no |
| 33 | Decided provisionally in v0.11.3: `RunState.cs → BeginAdventure` deals a zone-6 node a map of one of the run's five worlds (`CombatMapCatalog.cs → SelectForVoid`; a boss arena for the core) and `GameManager.cs → ApplyVoidTreatment` draws it drained and dark. The legacy arena opens only if nothing matches | no |
| 34 | `BaseFoe.cs → Integrate` uses the passive rule; a boss swims through `Controls`; no brain rule holds Up or Down | no |
| 35 | One `JumpCooldown` (0.3 s) everywhere; a medium refills the jump every tick | no |
| 36 | `FeedbackTable.cs` and `FeelTable.cs` hold the numbers and `FeelDirector.cs` acts on every hit; only the headless `FeedbackCheck` has exercised them | only its presentation |
| 37 | A line in Feedback.gdd §8 and nothing else: no effect or sound exists for Cleopastar, and her hits are still unnamed (Part 1, C rows) | only its presentation |
| 38 | The same for Sifu Pangda (Part 1, S rows) | only its presentation |
| 39 | `SeagullFoe.cs → InitFoe` leaves `HasContactDamage` on; `BaseFoe.cs → _PhysicsProcess` tries contact before skills; a hit with knockback starts 0.8 s of i-frames (`CharacterController.cs → TakeHit`), which then refuse the dash | no |
| 40 | `FeedbackSettings.cs` is static with defaults; nothing writes or saves it; the pause menu's Settings is a placeholder dialog | no |
| 41 | `SoftVolume.cs → MediumKind` answers `water`, `current`, `kit` or `foliage` from the tuning; no map code names a volume | only its presentation |
| 42 | `GameManager.cs → TryBuildSelectedMap` takes the first entry of the map's `Worlds` | only its presentation |
| 43 | `FeelTable.cs` `LightArenaLuminance` is 0.80, compared with the backdrop lightness `arena.enter` carries | only its presentation |
| 44 | `BaseFoe.cs → Die` raises `death`, runs `OnDeath` and frees the foe in the same call | only its presentation |
| 45 | During a stop `CharacterController.cs → _Process` still ticks timers, ammo and statuses and still reads abilities; only motion and the animation hold | no |
| 46 | `FeedbackTable.cs`: `StopBudgetSeconds` 0.18, `StopBudgetRecoveryPerSecond` 0.15, `StopRearmSeconds` 0.10 | no |
| 47 | The limits are constants in `FeelTable.cs` (`MaxFlashSeconds` 0.10, `MaxFullFlashesPerMoment` 6, `MaxLargeFlashesPerSecond` 3, `SaturatedRedShare` 0.8); nothing has been measured on footage | no — a release check, not a prototype one |

---

## Part 4 — Number mismatches met along the way

GDD value first, code value second. Not a hunt: only what Parts 1–2 ran into.

| # | Subject | GDD | Code |
|---|---|---|---|
| 1 | Fire Tornado box | "3 m (length) × 2 m (height)" — Pomegraknight.gdd › Fire Tornado | `Pomegraknight.cs` `TornadoHalfW` 128, `TornadoHalfH` 40 px → 8 × 2.5 m |
| 2 | Fire Tornado push | "5 m/s radial push outward per tick" | `TornadoPush` 64 px/s (2 m/s) |
| 3 | Pomegraknight move speed | "Move speed 8 m/s" — Movement | `CharacterController.cs` `MoveSpeed` 320 px/s (10 m/s); neither `Pomegraknight.cs → InitCharacter` nor her scene changes it |
| 4 | Pomegraknight jump | "Jump height 12 m · Time to apex 0.8 s · Coyote time 0.15 s · Jump buffer 0.15 s · Max fall speed −25 m/s" | `Units.cs`: 8 m and 0.5 s (`JumpVelocity` −1024 px/s, `Gravity` 2048 px/s²); `CoyoteTime` 0.2 s; no buffer; no fall-speed cap |
| 5 | Seed spawn height | "Spawn offset: 1.4 m above Pomegraknight" | `FirePoint` at (0, −30 px) in `Scenes/Pomegraknight.tscn` → 0.94 m |
| 6 | Slash timing and reach | damage event "0.25 s in / 0.00 s / 0.25 s"; "4 m radius" | `Pomegraknight.cs → HandleBA` hits on the press; `SlashRange` 130 px (4.06 m) |
| 7 | Seagull attack | "Base ATK 18" — FOES §4 | `SeagullFoe.cs → InitFoe` `BaseDamage` 20 |
| 8 | Seagull contact damage | "none (does not deal passive contact damage)" | `HasContactDamage` true: 20 × ATK multiplier, 260 px/s knockback, every 0.9 s |
| 9 | Seagull poop unlock | "Skill 1 — Poop (level 4+, 5 s cooldown)" | `SeagullFoe.cs → UpdateSkills`: any aggro'd level, so from foe level 2 |
| 10 | Poop puddle | "lingers for 3 s, dealing 30 base damage on contact" | `PoopHazard.cs → ApplyTick`: 30 × ATK multiplier every 0.25 s for 3 s (up to 12 ticks) |
| 11 | Benched cooldown rate | Gameplay §A.3: "Background protagonists' CDs tick at ⅕ speed" (NODES §3.3 says 1/(2n−2)) | `GameManager.cs → TickBackgroundCooldowns`: 1/(2n−2) → 0.5 with two, 0.25 with three |
| 12 | Benched item cooldown | NODES §3.3: "tick at the same 1/(2n−2) rate"; Gameplay §A.3: "⅕ speed" | not ticked at all while benched (`ItemRuntime.cs → ItemRuntime.Tick` runs for the active body; `ProtagonistState.cs` says so) |
| 13 | Pixolotl's Shift damage and ATK | NODES §5.5: "final_dmg = base_dmg × (100 + ATK) / 100" | `Pixolotl.cs → ApplyArrivalEffect` uses `ShiftDamage` 25 unscaled; her bubbles are scaled |
| 14 | Ghost fade (document against document) | 50-ART §5: "70%→30% alpha over 10 s" | Pixolotl.gdd and `CharacterTable.cs`: 3.2 s |
| 15 | Mist | MapGDD §8a: "Always on in the real game" | `MapController.cs` `_mist` starts false; the toggle is shown to everyone |
| 16 | Ambient spawn interval (inside FOES.gdd) | §12 (v0.5.0): "a 3 s trickle"; §10: "ticks every 4 s" | `FoeSpawner.cs` `Interval` 4 s |
| 17 | VFX backlog entries with nothing behind them | 50-ART §5: "Pangda… 9×9 field"; "brighter suspended stars during Ult" | no field in Sifu Pangda.gdd v1.1 or `SifuPangda.cs`; Cleopastar's ultimate is deferred |

Checked and matching, so nobody re-checks them: every Cleopastar, Pixolotl, PumpKing and Sifu
Pangda number quoted in Part 1; the crab's base stats, sight, Spawn and Jump; the BOSS
PumpKing row; `MissionTable.cs` against NODES §4.2, §4.4 and §4.5; the tsunami against
Gameplay §A.6.1; base HP 200 / 180 / 150 / 200 / 300.

---

## What could not be determined, and why

- **Nothing was run.** No build, no Godot. Part 2 entries 12, 13, 16 and 17 are conclusions
  from reading and need one play check each.
- **Whether the proposed names are the ones wanted.** Every "raised" cell was checked against
  the code and against `Docs/Feedback.gdd` §6 and agrees with both. Every other name is this
  report's proposal in that section's grammar — it lists no word for a ranged basic attack, a
  reload, a shield, a passive, a hazard's presence, a storm, an item or a wave, so `ba.cast`,
  `ba.throw`, `ba.reload`, `status.shielded`, `shield.break`, `passive.*`, `zone`, `storm`,
  `cast` / `proc` / `drag` and `wave.start` are choices someone has to confirm. Where one kit
  has two stances on one slot (Sifu Pangda) the proposal is a tag, `fuhu` / `heli`, not a
  second name.
- **First pass against a moving tree.** The first pass read v0.11.1 with the feedback work
  uncommitted; the re-check at v0.11.5 compared cue call sites, the changed documents and the
  zone-6 code, and did not re-read every file. `Docs/Feedback.gdd` was read in §2, §5.2, §6,
  §8 and §9 only.
- **Cleopastar.gdd has no Movement table**, so her 10 m/s and two jumps (the base defaults) can
  be neither confirmed nor called a mismatch. Sifu Pangda and Pixolotl set theirs from
  `CharacterTable.cs`; PumpKing in `InitCharacter`.
- **Whether a partly used Pixolotl magazine should reload.** The GDD gives the delay and the
  size, not the trigger; the code reloads only from empty.
- **How hazards should look or sound.** None of the documents in scope describes fire,
  freeze, spikes or lava (`RealmStyleBible.md` and `MapCreation.gdd` were not on the list).
- **Per-tile overrides.** The scenes were checked for exported values; the tile sets and map
  files under `Scripts/MapCreation/` and `Maps/` were not, so a hazard tile that changes a
  number through its own properties would not show here. TBD #25, #26, #30 and #32 rest on
  file listings only.
- **Seagull dash direction** is a behaviour, not a number: the GDD says horizontal, the code
  aims at the player. It is in Part 2 (#11), not Part 4.
- **`KNOWLEDGE.md`** was searched, not read end to end.



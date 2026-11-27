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
- A **Foe-side fighter** (a BOSS: a protagonist's class on the other side, v0.10.11) sits on
  layer Foes with the Foe mask — set in code from its side, whatever its scene says. A
  projectile's mask is aimed at the other side by `CombatTargets.AimMask`.
- Ground and Platform are **distinct**. Platforms are one-way; drop through by pressing
  down (Platform bit toggled off the mask ~0.25 s, hold = fall further). Ground is solid.

**Two platform archetypes:**
1. thin one-way **platform** — land/cross/drop-through (a `StaticBody2D` with
   `one_way_collision = true` on its shape).
2. **SoftVolume** (`Scripts/Gameplay/World/SoftVolume.cs`) — enterable "go-inside" volume (tree/bush/…);
   Area2D field with **no** standable top. Two rules, by body (v0.11.1):
   - **A fighter swims** in every map volume: Up (`move_up`, W/↑) and Down steer exactly as
     Left/Right do — read, like them, through `Controls` (`ControlAction.MoveUp` /
     `MoveDown`), so a BOSS swims from its brain's pad and never from the player's keys —
     and gravity is replaced by the volume's **drop** — `GravityIndex`·maxSpeed
     (0.1 → 1 m/s) in foliage/cloud, the absolute `SinkSpeed` in water. Velocity = own
     swimming + drop: in the sea (5.5 and 3 m/s) up is 2.5, down 8.5. Up pushes only while
     the head is under (`.Covers`), so a swimmer rests at a surface. A jump inside is a
     stroke on top of the swimming, as often as `JumpCooldown` allows — the owner's call:
     a player chooses between holding Up and jumping. `SoftVolumeSet.SwimVertical`, `.Swim`.
   - **A foe (`BaseFoe`) is passive**, and so is a fighter in a kit volume with `Swimmable`
     off (Cleopastar's star): gravity/input/jumps continue and velocity is gradually pulled
     toward StagnationIndex·maxSpeed (scene fallback 0.5; map clouds 0.3, palms 0.1) plus
     the GravityIndex drift — the v0.6.18 contract, `SoftVolumeSet.Resist`.
   Membership in a volume it swims in keeps a fighter's jump charges full; a kit volume
   (`Swimmable` off — Cleopastar's star) does not, so she can run out inside her star and its
   exhausted-jump support fires as Cleopastar.gdd says (v0.12.4; before, the star refilled her
   jumps every tick and was never spent). The same volume is also
   **water** (`GravityScale` = share of gravity left, `SinkSpeed` = absolute drop, `Drag` =
   resistance multiplier) and a **current** (`IsMedium` off + `Flow`: carries only — a
   current, bubbles, a waterfall; an identical carry counts once). A body can be in several
   at once: both body types keep a `SoftVolumeSet` and resolve everything through it, never
   through a single remembered volume. Tuning table, rules and measured numbers:
   `Docs/MapCreation.gdd` §2.2a.

**Movement / combat model** (`CharacterController`, `Enemy`, `HitInfo`):
- Velocity = **`intentVel`** (self-directed: input, gravity, jump, then any SoftVolume resistance) +
  **`externalVel`** (impulses — knockback/wind — that decay via `ExternalDamping`). This is what
  gives external forces "room" to coexist with player control.
- Horizontal intent has **momentum** (accelerate toward `MoveSpeed` via Ground/Air Accel; stop via
  Ground/Air Friction). Vertical intent integrates gravity (has momentum) and jump sets it —
  except inside a soft volume, where a fighter's vertical intent is steered like the
  horizontal one and settles on the volume's drop instead of accumulating gravity.
- Knockback = **`AddImpulse(deltaV)`** (a delta-v; force-over-time = per-tick impulses, e.g. tornado).
- Hits are authored per skill via **`HitInfo { Damage, Knockback (delta-v), Stun }`**. `Stun < 0`
  means the default **gain-no** window `Units.StunPerDamage · Damage` (0.005·dmg); during it the
  receiver can't act and its **animation is frozen** (knockback/gravity still move it).
- Inside a **SoftVolume**, external impulses are extra-damped by `ExternalDampingMult` (viscous), so
  knockback still shoves you in but fades fast.

**Sides, targets, controls** (v0.10.11; `Docs/Tech/T10` §4a) — the three rules that let one
kit be worn by the player or fought as a BOSS:
- A fighter is on a `CombatSide` (`CharacterController.Side`, virtual: Player; a `BossX : X`
  class overrides it to Foe). From that one fact the base picks the group (`player` /
  `enemy`), the collision layer, whether it owns the camera and `LocalPlayer`, whether it has
  post-hit i-frames, and what happens at 0 HP.
- Attacks hit `CombatTargets.HostileTo(GetTree(), Side)` and talk to **`ICombatTarget`**
  (`BaseFoe` and every `CharacterController` are one). Never cast to `BaseFoe`, never name a
  group in attack code; a projectile takes the side of whoever launched it.
- Kits read **`Controls`** (`Held` / `JustPressed` / `JustReleased(ControlAction.…)`,
  `AimPoint(this)`) — never `Input`, never `GetGlobalMousePosition()`. The player's is
  `PlayerControls`; a boss brain fills a `VirtualPad`. The base class obeys it too:
  swimming's Up and Down are `ControlAction.MoveUp` / `MoveDown` (v0.11.1).

**Slopes** (v0.10.11) — ground can be a ramp (`RealmTiles.Slope`, `MapCreation.gdd` §2.2b).
Bodies stay on one because `FloorSnapLength` is `Units.FloorSnapPx` (12 px) for fighters and
walking foes. A 1:2 slope costs a fighter a fifth of its speed uphill, nothing downhill.

**Jumps** — per-character `MaxJumps` (jumps before touching down again; Pomegraknight = 1),
refreshed on touching ground / platform / active SoftVolume. Inside a soft volume the charges
stay full, so a jump there is a stroke whose only limit is the cooldown (measured: tapping
climbs 10 m/s in the sea, 19 m/s in a bamboo). A universal `JumpCooldown` (0.3 s) is the
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

**Feedback** (v0.11.2; `Docs/Feedback.gdd`, `Docs/Tech/T10` §4b) — how a moment is told:
- **Gameplay raises a cue and forgets it** — `CueBus.Emit(this, "ba.swing", variant: stage,
  size: reach, …)`, `CueBus.Start` … `Stop` for anything that lasts — keyed by two constant
  strings, *owner* (a snake_case content id: `FeedbackId`, or a system: `world`, `hazard`,
  `flow`) and *action*. It never names an effect, a sound or a shake and never calls a
  director; `FeelDirector` (and the VFX and audio directors after it) listen and look the
  cue up in their own `CueTable` — owner → class → `any`.
- **An attack names its hits**: `new HitInfo(dmg, knock).From(this, "ba", stage)`, or
  `.FromRanged(owner, "s2.seed")` for a projectile, a field or a tick. Damage math never
  reads that metadata. Each hit intake raises exactly one `HitCue` per hit that landed.
- **Hit-stop is gameplay** (`HitStopClock`, numbers in `Scripts/Data/FeedbackTable.cs`): a
  held body does not move and its animation keeps its frame; **every clock keeps running
  and abilities are still read**. The victim is held by its intake; a melee attacker once
  per swing (`MeleeCone` / `SwingStop`, `BaseFoe.Strike`); a ranged hit never holds its
  owner; ticks hold nobody. Stops take the max, re-arm after 0.10 s, and are budgeted.
  Never `Engine.TimeScale`, never `GetActDelta` / `LocalTimeRate`. A kit that moves the body
  itself guards with `HitStopHeld` (after the base tick) or `HoldForHitStop` (instead of it).
- **One owner per visual property of a body**: `Sprite.Modulate` / `SelfModulate` = status,
  telegraph and evolution tints; `Sprite.Visible` = the i-frame blink; root `Modulate.a` =
  the defeat fade; the hit flash = a material lent to the sprites while it runs
  (`IFeedbackBody.FlashTint`). Recipe for wiring a kit: `20-ENGINEERING.md` §4;
  Pomegraknight is the reference.

**ATK → damage-dealt multiplier is `(100+ATK)/100`** (base ATK is 0, so 0 ATK = 1× damage).
ATK is a flat additive stat, same as DEF. Both are permanently increased via shelter Blessing
actions (Sharpen Weapon: +10 ATK; Sharpen Armor: +10 DEF). Unlike the temporary Frozen DEF
bonus (which uses the `_defenseBonuses` dictionary), permanent ATK/DEF from shelter Blessings
are stored separately and persist across the entire run.

**The world pool** (`WorldDef.Pool`, `Scripts/Map/MapData.cs`, v0.11.0) is the eight worlds
whose combat realm is built: SL, HC, VK, TD, PL, BM, KH, TF — in that order, which is data. A
seed shuffles the list, so **append only**; a run records the pool it was generated from and a
save keeps it (`RunState.WorldPool`, save version 4). A pool world must have a realm tile set,
a city colour and land tint no other world is close to, a map authored for each altitude band,
a boss arena, and a boss (its own row in `BossTable`, else the stand-in — PumpKing).

**Input** — actions live in `project.godot`, except `move_up` (W/↑, swim up), which
`Scripts/Foundation/InputDefaults.cs` registers when the project does not define it. The call
is in `PlayerControls`' constructor: that is the one class that asks the InputMap for a
fighter's buttons (a kit reads `Controls`, never `Input` — the rule above), and every
`CharacterController` touches it when it is constructed. `project.godot` is re-saved by every
editor that opens a checkout and is usually left uncommitted, so an action added there can
reach a checkout without the code that reads it, or the code without it (reading a missing
action logs an error every frame). An entry named `move_up` in `project.godot` wins, so it
can still be rebound there. A test that presses it before any fighter exists must call
`InputDefaults.Ensure()` first (the content check's probe does); the boss check deliberately
does not, so it fails by name if the game stops registering the action.

**Versioning** — bump patch (+0.0.1) every commit; keep `Scripts/Foundation/GameVersion.cs`, the
repo-root `VERSION` file, and the HUD `VersionLabel` (`Scenes/Hud.tscn`) in sync. Shown
top-left in-game.

**Verification** — check which host you are on before assuming. The main dev Mac **has** a
toolchain (`dotnet` 9 on the PATH; Godot 4.7 .NET at
`/Applications/Godot_mono.app/Contents/MacOS/Godot`), so build for real:
`dotnet build Fableland.csproj`, and for map/tile work the headless content check
(`Docs/Instructions/40-QA.md` §(2)); for bosses, kits with a BOSS variant, input, hit intake,
sides, slopes or swimming, also the boss check (`res://Scenes/Bosses/BossFightCheck.tscn`, same §); for
the world pool, map generation, saves, the run flow or a shipped map, also the run check
(`res://Scenes/Run/RunWalkCheck.tscn`, same § — it plays real runs, in a scratch save folder); for
the cue bus, hit-stop, a hit intake, a kit's cues, a body's tints or the feel director, also the
feedback check (`res://Scenes/Feedback/FeedbackCheck.tscn`, same §). A host with no toolchain falls back to static checks
(brace/paren balance, `.tscn` resource paths, `GetNode` paths), which do NOT catch
API-existence or type errors — say so, and log anything a later compile surfaces below.
Python tools (`Tools/`) need Pillow + numpy: use `/opt/homebrew/bin/python3` on the dev Mac —
the Anaconda `python3` first on the PATH has a broken numpy (missing MKL) and dies on import.
It is also an x86_64 binary: **never launch Godot from it** (caveat below, v0.11.2).

---

## Caveats / gotchas (grow this on every bug fix)

### A resumed battle re-enters through `BeginAdventure` — a once-per-challenge effect must not run again (v0.12.8, THE VOID; caught before it landed)
- **Symptom (in review):** THE VOID's phantoms step toward a combat node when you challenge it,
  in `BeginAdventure`. A battle left with Save & Quit restarts through `ResumeLoadedRun` →
  `BeginAdventure` on Continue, so the same challenge would have stepped every phantom a second
  time — and a phantom one road away would have touched the party twice for one fight.
- **Rule:** anything `BeginAdventure` does *once per challenge* (as opposed to setting up the
  scene) is skipped on a resume: `RunState` raises `_resumingBattle` around that call. The
  stamina drain is harmless to repeat (it is already 0); a phantom step, a touch, a toll or a
  count is not.

### A `MapNode` held across a load is a stranger to the new graph (v0.12.8, the run check's phantom walk)
- **Symptom:** the run check kept the `MapNode` it fought on, saved and loaded the run, then
  measured roads from that node: −1 — "unreachable". A load regenerates the map from the seed
  into new objects, and `MapGraph`'s adjacency is keyed by node object, so the old node has no
  edges in the new graph.
- **Rule:** across a load (`TryLoadRunFromSlot`, TwistedReality's rewind, anything that runs
  `HydrateSave`) hold node **ids**, and look nodes up again with `RunState.FindNode`. The ledger,
  the save and the adventure handshake already store ids only.

### Taking nodes out of the graph leaves `FindNode`'s index stale (v0.12.8, the QiongFeng Claw; caught before it landed)
- **Symptom (in review):** the Claw cracks a realm down, and `Graph.BreakRealityBridgesAtDevouredEndpoints()`
  then *removes* each Eidolon Shelter whose bridge touched it. `RunState.FindNode` reads a
  dictionary built from the graph, so it would still have returned the removed shelters — a
  phantom or a saved position could have pointed at a node no longer on the map.
- **Rule:** after anything that adds or removes graph nodes, call `RunState.RebuildNodeIndex()`
  (`EnterVoid`, the bridge verbs and the Claw do). Devouring a node does not remove it; only a
  collapsing bridge shelter does.

### A Possession runtime for the item the body already holds would double its passive (v0.12.8; caught before it landed)
- **Symptom (in review):** the arena puts every Possession item's passive on the body in play
  (ITEMS §4.4). Built naively, the held item — if it is Possession or enchanted — got both its
  held runtime and a passive-only one. Scales are keyed by source and would have deduplicated,
  but event-subscribed passives (FanChen's heal, the Kashaya's reflect, DD's core heal) fire
  once per subscription: twice.
- **Rule:** `RunState.PossessionItems(exceptInstanceId)` leaves out the instance the active
  body holds; the arena passes it (`GameManager.BindPossessions`). "Applied once party-wide,
  never twice to the holder" is the GDD's own line.

### `HoldItem` takes the first backpack instance of that kind, not the one you just added (v0.12.8, the item check)
- **Symptom:** the item check added two Weird Mushrooms, held "one", and assumed the held one
  was the second. `RunState.HoldItem(p, defId, …)` takes the **first** backpack instance with
  that definition id, so its perish clock and identity were the other one's.
- **Rule:** after `HoldItem`, read the held instance from `p.HeldItemInstanceId`; never infer
  it from the order things were added. (Instances of one kind are interchangeable to the UI
  today; a per-instance verb — plant, enchant, set free — takes an instance id.)

### A thing that hits on touch every tick must remember whom it hit (v0.12.8, DD; designed in)
- **Rule:** DD tests every hostile target each physics tick. It records each one it devours
  (`TargetId`) and never hits it again in that release; an invincible target is skipped, not
  recorded, so it can still be devoured once its shield drops. Re-hitting every tick a target
  stays in reach would stun-lock it (each `TakeHit` re-applies knockback and hitstun) even
  where the damage is mitigated to nothing.

### Clamping to a floor with Max lifts a value already under it (v0.12.6, the events' HurtParty; caught before it landed)
- **Symptom (in review):** `HpRatio = Max(0.05, HpRatio − cost)` "never below 5 %" would
  have raised a fighter at 2 % to 5 %. An event that costs HP would have healed.
- **Rule:** a floor that only stops loss is `Min(current, Max(floor, current − loss))`. The HP
  floor (`CharacterController.HpAfterLoss`, v0.12.3) is built the same way.

### A summary that clears its notes before it runs throws away what the day said (v0.12.5, audit #2)
- **Symptom:** winning the home capital put "The capital yields TwistedReality." in
  `DayEndNotes`. `EndDay` cleared that list before its pipeline ran, so the day-end summary
  never showed it, nor "X joins your roster".
- **Rule:** notes are cleared after the summary has read them, not before.

### A kit's own skill cleared the arena's end-of-mission gate — invincibility is held by sources (v0.12.4, audit #13)
- **Symptom:** after a mission resolved, Pixolotl's Shift set `Invincible = false` on its way
  out. The resolution's invincibility was gone, and a foe or hazard still on the field could
  kill her and end the run. Reproduced by the flow check.
- **Rule:** `Invincible` is held by named sources. Its setter is the body's own
  (`KitInvincibleSource`); the arena holds `ResolvedInvincibleSource` through
  `SetInvincible`; a respawn clears every source.
- **Why:** one boolean with two owners means the last writer wins (golden rule 7).

### A pull toward a point on a wall never arrives (v0.12.4, Yukai's Rope, audit #12)
- **Symptom:** the rope's drag ended only within 0.35 m of its anchor. The anchor is on a wall
  face and the body is 0.75 m half-wide, so the pull never ended. With the old rule, the flow
  check found it still pulling after 4 s.
- **Rule:** a pull toward a surface ends when the body stops closing in (0.2 s with no
  progress), or after a time limit (1.5 s), as well as on arrival.

### A support that is asked only when the charges run out never runs if something refills them (v0.12.4, Cleopastar's star, audit #17)
- **Symptom:** inside her own star Cleopastar's jumps were refilled every tick, because every
  medium refreshed jumps. She could never be out of jumps there, so the star's exhausted-jump
  support was never asked: it never exploded or launched her.
- **Rule:** only a volume the body swims in refills jumps. A kit volume (`Swimmable` off) does
  not.
- **To test it** (the flow check): her star is a volume only in its fall phase, after a 1 s
  hover. Her magazine starts empty, one Glow every 1.5 s.

### A hit landed by a kit's own shape, or by something it threw, must be reported (v0.12.4, audit #14)
- **Symptom:** The Forgotten Kashaya's reflect never triggered for Sifu Pangda (his blows use
  their own rectangle, not `MeleeCone`). Pome's Bravery's omni-vamp ignored Cleopastar's stars
  and PumpKing's head (they never credited their thrower).
- **Rule:**
  - a melee blow with its own shape calls `ReportMeleeDamageDealt`;
  - a thrown thing credits its thrower with `ReportDamageDealt`, while the thrower is still
    valid.

### "Damage on contact" from a hazard that ticks is a stream of hits (v0.12.4, the seagull's puddle, audit #11)
- **Symptom:** FOES §4's puddle "deals 30 base damage on contact". As a `Hazard`, it hit four
  times a second for 3 s, up to 360 damage: a still player lost 200 HP (all of it) in the flow
  check's mutant run.
- **Rule:** "on contact" means once for each body. Keep a set of bodies already struck.

### zsh does not split an unquoted variable (v0.12.4, tooling)
- **Symptom:** a mutation run did `FILES="a b c"; for f in $FILES; do cp …`. zsh made it one
  word, so the backups failed, the `&&` chain stopped before the mutants were applied, and the
  run passed on the real code.
- **Rule:** in zsh use an array: `files=(a b c); for f in $files`, with `${f:t}` for the base
  name. Count the backups before you mutate.

### A blow of several hits against the player: only the last may shove (v0.12.3, Māra's Claw Rake)
- **Symptom:** a two-swipe rake dealt 9.6 damage, not 19.2. The second swipe landed 0.15 s
  after the first and did nothing. Found by a mutant that shoves on every swipe; the boss
  check catches it.
- **Rule:** in a multi-hit blow aimed at the player's side, give knock-back to the last hit
  only. A hit with no knock-back grants no grace, so every hit of the blow lands.
- **Why:** a Player-side `TakeHit` arms 0.8 s of i-frames whenever the hit's knock-back is
  non-zero.
- **Not changed:** the Asura's Six-Arm Flurry (v0.12.1) shoves on every arm, so only its first
  arm can hurt a player. Its check counts arms, not damage.

### A kit whose state the arena pushes must start with no power (v0.12.3, Māra)
- **Symptom:** in the boss check, Māra's rake showed a doubled tell, and his noose missed a
  player 9 m in front of him.
- **Cause:** his realm defaulted to Hell, the realm the wheel opens on. A body fielded without
  the wheel (as the boss check fields it) therefore fought under Doubt for good: half his
  strikes went to the mirrored side.
- **Rule:** a kit told its state by the fight (`IWheelFighter`) starts in the neutral state
  (the human realm). The wheel sets the opening realm itself when it begins.

### A press is read before the physics tick that turns the body (v0.12.3, Māra; designed around)
- **The order.** A brain rule does `FaceTarget(pad); pad.Tap(...)` in `_Process`, and the body
  reads the press in that same process step. `FaceByHint`, which turns the body to the pad's
  facing, runs in the next physics tick. A kit that fixes a direction at the press (a tell's
  side) would use the old facing.
- **Rule:** take `Controls.FacingHint` at the press.
- **The checks cannot see this.** A mutant without the rule survives the boss check: at the
  checks' 8× physics rate, the ticks that turn the body run before the frame's process step
  reads the press.

### A button tapped on a pad is read on the body's process frame — wait for its effect, not for ticks (v0.12.1, boss check)
- **Symptom:** "Skill2 did not start the Undertow" on one run in three: the check tapped the
  Ferryman's pad and looked two physics ticks later. Skills are read in `_Process`; at the
  checks' 8× physics rate two ticks can pass with no process frame between them.
- **Rule:** after a tap, wait until the effect is visible (bounded), then measure from there.

### A blow pays its victim ult charge — a toll must be net of it (v0.12.1, Ferryman)
- **Symptom:** an obol's "−150 ult charge" read −138: the coin's 6 damage paid the victim 12
  charge (`UltChargeReceivedRate` 2) after the drain had been worked out.
- **Rule:** read the charge before the hit and leave the victim at (before − toll).

### A blow big enough to set a boss's HP in a check also stuns it (v0.12.1, boss check)
- **Symptom:** the Asura's flurry was "0 arms" — the check had just knocked off 49 % of its HP
  and tapped Attack two ticks later, inside the stun that blow earned.
- **Rule:** wait out the stun (≈ 45 ticks) before pressing a boss's buttons after a big hit.

### Art drawn facing left: mirror it in the scene, not in code (v0.12.1, zone-6 kits)
- **Rule:** the base body sets `Sprite.FlipH = false` when it faces right; a sprite drawn facing
  left gets a negative x scale in its scene, and the base class's flip then works unchanged.

### A fight the run survives must stop the fallen body writing its HP back (v0.12.0, zone 6)
- **Symptom (caught in design):** a zone-6 mini-boss defeat floors the battle team at 25 % HP in
  `RunState.ReportGoal`, but the arena writes the active body's HP back again on Finish the Day
  and on Save & Quit — a dead body writes 0 over the floor.
- **Rule:** after a survivable defeat `GameManager` sets `_hpSettled`, and `WriteBackHp` does
  nothing from then on. Any rule that changes the run's HP after resolution needs the same.
- **Why:** the arena's write-back assumed the body is the truth until the scene ends.

### Changing what a node is fought as must not change which map it is dealt (v0.12.0, zone 6)
- **Symptom (caught before landing):** making every zone-6 gate a boss fight and dealing it a
  boss arena would have orphaned `vk_seashore_5` — an LV4 non-boss map reachable *only* through
  zone 6's ordinary missions — and failed the content check's no-orphan walk.
- **Rule:** the handshake overrides the mission; the map is still dealt for the mission the
  generator rolled (`RunState.DealCombatMap(..., node.Mission, ...)`), shared with the run check.
- **Why:** map reachability is a property of the dealing rule, not of the fight.

### A check that teleports into zone 6 must also cross the singularity (v0.12.0, run check)
- **Symptom:** the Avīci walk ended in `VoidDevoured` after its eighth day end: a lost gate sent
  the player back to `PreviousNodeId` — the start node, never left — and the VOID ate it.
- **Rule:** a check that enters zone-6 nodes with `BeginAdventure` first calls `EnterVoid` and
  stands the player on the river hub (`XX-R`), as a real run does.
- **Why:** `BeginAdventure` never sets `PreviousNodeId`; the map screen does.

### Compare cooldowns by their start, not by what is left (v0.12.0, zone-6 check)
- **Symptom:** PumpKing's 0.5 s Pump read 0.57 / 0.45 s left after four ticks — ×1.27, not the
  seal's ×1.25 — because both had run the same 0.07 s.
- **Rule:** assert the max's ratio and that both have run equally long (max − left equal); a
  ratio of remaining times is not scale-invariant.

### A validation of constants is "unreachable code" to the compiler (v0.12.0, build)
- **Symptom:** `if (!(GravityWinFactor > 0f && …)) problems.Add(…)` on `const` values raised
  CS0162 four times — the build must be 0 warnings.
- **Rule:** ask through a local function (`Require(bool ok, string what)`); the rule is still
  checked if a constant changes.

### A voice the engine was told to stop is free only after its next mix — and a one-shot ends in the engine's time, not yours (v0.11.7, audio check)
- **Symptom:** the first allocation probe raised 13 500 cues on a made-up clock and started
  **0** voices; the engine had refused 22 765. `AudioStreamPlaybackPolyphonic.PlayStream`
  returns −1 when its polyphony is full, `StopStream` only *marks* a voice, and the slot is
  released by the mix thread — every 10–46 ms of real time. The voice manager retired voices
  on its own clock and never told the engine, so the engine still held them all.
- **Rule:** the voice manager calls `StopStream` for every voice it retires, natural end
  included; the engine's polyphony is **twice** the manager's cap, so a frame that steals
  many voices has somewhere to put the newcomers; and a check that starts real voices lets
  real time pass between bursts (`Breathe()` in `AudioCheck`).
- **Why:** two clocks. Ours decides what should be sounding; the engine's decides when a
  slot is free. A refused voice is a dropped sound, silently.

### The engine's resampler leaves an image at (stored rate − f): pick a storage rate by where the sound's energy sits (v0.11.7, audio, measured)
- **Symptom (measured, never heard):** a pure tone in a runtime `AudioStreamWAV` stored at
  16 kHz, captured from the mixed bus at 44.1 kHz: a 3 kHz tone has an image at 13 kHz
  32 dB down — and a **6 kHz tone an image at 10 kHz only 10 dB down**. At 22.05 kHz: 44 dB
  and 21 dB. At 11.025 kHz a 2 kHz tone images at 9 kHz, 33 dB down.
- **Rule:** store a medium at a lower rate only if its sound sits in the lower third of that
  band (`Audio.gdd` §5): watercolour, lit realism and the poster at 16 kHz; gongbi, linocut
  and the VOID — each low-passed by rule — at 11 kHz; the bright media at 22.05 kHz.
  `Tools/audio/inspect.py` reports each medium's share of energy above 5 kHz; re-read it
  before lowering a rate.
- **Why:** memory was 13.5 MB against a budget of 8, and the cheapest saving — a lower rate
  — adds exactly the fizz above 8 kHz the sheets are read to avoid.

### Normalising a sound that is nearly silent turns its noise floor into the sound (v0.11.7, audio check)
- **Symptom:** "HC `any.jump.swim`: 26 % of its energy above 8 kHz". The recipe was a short
  burst of a *sparse* texture (three drips a second, 0.14 s long): most variants contained
  no drip at all, and peak-normalising the result amplified dither into white noise that
  passed "not silent".
- **Rule:** `AudioCheck` measures a recipe **before** it is normalised (`SfxClip.RawPeakDb`
  must reach −45 dBFS); `Medium.Scatter` always places an event in the first fifth of the
  span it is given. A generator that may rest must not be allowed to rest everywhere — the
  music's lead plays its last phrase if it has played none (the shelter's lead, seed 1,
  bounced a silent stem).
- **Why:** "not silent" after normalisation is true of anything, including nothing.

### "Seed-mutated variants" are only different if the recipe draws from the seed (v0.11.7, audio sheets)
- **Symptom:** the inspection report found 143 of 610 variant pairs with the same waveform:
  tonal rows (a marimba note, a bell) used no randomness, so every variant rendered the same
  bytes; the dither made them *look* different to a byte comparison.
- **Rule:** a medium's primitives draw `Mut(c, share)` (decay, strike, layer level) and
  `Cents(c)` (a few cents: inaudible as pitch, enough to drift apart) from the variant's
  seed; the chip medium varies duty and length instead, because a chip's pitch is exact.
  Compare variants by correlation (`inspect.py`), not by bytes.

### A made-up clock must also zero every lead taken from the real audio server (v0.11.7, audio check)
- **Symptom:** "the boss's layer did not enter on the bar (it entered −79 ms from it)". The
  director was stepped on a made-up clock, but `MusicEngine` still subtracted
  `AudioServer.GetTimeToNextMix()` + output latency — real numbers — to decide when to start.
- **Rule:** `AudioDirector.TimeSource` sets `MusicEngine.NoLead`; anything paced against the
  sound card reads its lead through one property.

### State a check removes by hand must go through its owner (v0.11.7, audio check)
- **Symptom:** "the realm's bank was not warmed on arena.enter", and every engine probe after
  it failed. The memory stage had nulled rendered sets directly to measure eviction; the
  cache's "already requested" set still listed them, so they were never rendered again.
- **Rule:** evict through `AudioCache.EvictRealm` / `Clear`. One owner per piece of state
  (T00 rule 2) applies to checks too.

### Tooling: a script named `inspect.py`, a `SceneTree` script's first frame, `timeout` on macOS (v0.11.7, tooling, reference)
- `Tools/audio/inspect.py` shadows the standard library's `inspect`, which numpy imports:
  "partially initialized module 'numpy'". The tool removes its own folder from `sys.path`
  before importing numpy. Do not "simplify" that away.
- In a `-s` SceneTree script, `_initialize` runs before the root is in the tree
  ("Playback can only happen when a node is inside the scene tree") — `await process_frame`
  first. macOS has no `timeout`; cap a spike with `--quit-after <frames>`.
- Verified by running, for the next audio work: a headless run reports display `headless`
  and audio driver `Dummy`, **and still mixes in real time** (voices advance, loops loop,
  `IsStreamPlaying` works); 6 posts into a polyphony of 4 return 4 handles and two −1;
  stems of an `AudioStreamSynchronized` loop independently and the player's position is the
  largest of theirs; `PitchScale` works on it; a paused tree holds a `Pausable` polyphonic
  player's voices; an unknown bus name reads back as `Master`.

### A stepped effect drew nothing on its first frame — 83 to 100 ms of a slash went missing (v0.11.6, VFX)
- **Symptom:** in the first gallery captures the scratchboard, linocut and VOID columns
  were empty for slashes and trails, and ink and pixel art showed a sliver. Each of those
  media redraws at 10–15 fps and computed its phase as `step / fps`: on step 0 that is 0,
  and a stroke at phase 0 has not been written yet.
- **Rule:** a held frame shows the *middle* of its interval: `(step + 0.5) / fps`. Anything
  with fewer than ~6 frames in its life has to be checked at its first one.
- **Why:** a smooth effect at t = 0 is one invisible frame in sixty; a stepped one is a
  third of the effect's life.

### The player vanished on the frame she was hit — the i-frame blink opened on its "off" half (v0.11.6, hit feedback, found by the VFX captures)
- **Symptom:** the hit flash had never been drawn by anyone. Captured on the frame of the
  hit, foes flashed white (ink on a pale arena) as designed — and the player was not on
  screen at all. `Sprite.Visible = PosMod(_invulnTimer, 0.12) < 0.06` with the timer set to
  0.8: 0.8 mod 0.12 is 0.08, so the blink's first 20 ms are invisible, and the flash under it.
- **Rule:** `Sprite.Visible = _flash.Active || …` — the blink waits for the flash. One
  owner still writes `Visible`. A timer-phase effect has to be asked what its *first* frame
  is.
- **Why it hid:** `FeedbackCheck` asserts the flash does not touch `Visible` — and it does
  not. The picture was wrong, the mechanism was right; only a capture on the exact frame
  shows it.

### A crescent drawn round a constant-radius arc does not reach the cone (v0.11.6, VFX)
- **Symptom:** every slash was a bulging half-moon: a band of half-width `w·sin(πu)` round
  an arc of radius `reach − w` has its outer edge at `reach − w` at the tips and at `reach`
  only in the middle, so it is more curved than the hitbox and looks like a "D".
- **Rule:** bend the centre line: `r(u) = reach − halfWidth(u)`. The outer edge is then the
  arc of the reach all the way round (`VfxStyle.CrescentPath`). The kit's own debug cone,
  still drawn, sits exactly under it in the captures.

### A hit mark sized from a table of round numbers buried the fighter (v0.11.6, VFX)
- **Symptom:** marks of 12–38 px radius looked right alone on the grid and hid the whole
  64 px fighter in a fight, three at a time, the moment foes stood on the player.
- **Rule:** judge an effect in the fight, at the camera's zoom, with the bodies in it. Hit
  marks are now 4–23 px. A grid proves the medium, not the scale.

### Drawing with the rendering server from `_Draw`: one command, no garbage — and four things to get right (v0.11.6, VFX, reference)
- `RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), indices, points, colours, uvs,
  default, default, texture, -1)` has `ReadOnlySpan` overloads in 4.7: slices of static
  buffers go straight in, nothing is allocated, and a whole mark is one canvas command.
- Colours must be one per vertex. Pass an **empty** UV span for untextured geometry and a
  full one for textured — so a mark that mixes them flushes when the texture changes
  (`VfxPen.Use`).
- It appends to the item's command list: call it only inside `_Draw` (the list was just
  cleared for you). `QueueRedraw()` on a hidden node clears its cached commands.
- An autoload's children are drawn **behind** the current scene at equal z (tree order):
  pooled effect nodes need `ZAsRelative = false` and an absolute `ZIndex` (10 world, 30
  must-read, 5 ambience) or they are invisible.

### A `CPUParticles2D` burst from a pool (v0.11.6, VFX, reference)
- Create with `Emitting = false` (the default is true). Per rent: set everything, then
  `Restart()` and `Emitting = true`; recycle on your own clock (`Lifetime` + a little), not
  on `finished` — no signal, no callback allocation.
- `ColorInitialRamp` gives each particle a colour between two stops: keep one `Gradient` per
  pooled node and `SetColor` it per rent rather than making one.
- With `AlignYToVelocity` a particle's **+Y** (the bottom of its texture) leads: draw a
  droplet head-down.
- A still for a capture: `Preprocess = seconds; Restart(); SpeedScale = 0` shows the burst
  at that age and holds it.
- Reset `Preprocess` and `SpeedScale` when it returns to the pool.

### Capturing a staged fight: three things that wasted a run each (v0.11.6, tooling)
- **The debug arena's authored body is Pixolotl**, not Pomegraknight: the first fight sheet
  was bubbles. `GameManager.DebugSwapProtagonist("Pomegraknight")` works only in debug
  mode; a tool turns it on the way a user does (find the overlay's `DBG` button, emit
  `pressed`), swaps, and turns it off.
- **Foes placed beside the player walk onto her within 0.4 s** and raise their own spawn
  pops: stage fresh ones before each shot, wait out the pop (0.36 s), then swing.
- **To catch a two-frame flash,** await `RenderingServer.FramePostDraw` in the frame the cue
  is seen, and read the viewport then. `open --stdout file --stderr file` gets a windowed
  run's log.

### C# in a hot path: two small ones (v0.11.6, VFX, reference)
- `side.Equals(CombatSide.Player)` boxes; `side != CombatSide.Player` does not.
- `a && kind is X or Y or Z` parses as `a && (kind is (X or Y or Z))` — which is what you
  want, and worth a comment.

### A band offset from a line with sharp corners folds over itself — and a frontier cut per cell stops at every cell (v0.11.4, overworld pages)
- **Symptom:** the first charred frontier had a crossed, bow-tie sliver at every corner
  where three city fields meet, and a pale wedge of unburnt page at each of them.
- **Cause:** two things. The scorch and bite bands were offset 5 – 18 px from a polyline
  whose corners are sharp (Voronoi vertices); past the corner's radius an offset curve
  crosses itself and its quads flip. And each field's frontier was drawn as its own run,
  tapered to nothing at both ends — so the burn closed and reopened at every cell.
- **Rule:** before offsetting a band from generated geometry, (1) join the pieces into
  continuous lines (directed segments, living side always on the right, chained end to
  start through a 0.5 px hash — `OverworldPageView.Chain`), (2) round the line over more
  than the largest offset (`PaperCut.Rounded`, a moving average) and take normals from the
  rounded copy, (3) never let the offset edge fall short of the true corner (the fill band
  still starts at the unrounded line). Taper only where a run really ends.
- **Why:** an offset is only a curve while the offset distance is under the radius of
  curvature. Field boundaries have radius zero at every vertex.

### Pieces from polygon clipping do not meet exactly — grow them by a hair (v0.11.4, overworld pages)
- **Symptom:** bright specks of unburnt page inside the char, exactly where three fields meet.
- **Rule:** when adjacent polygons come out of `Geometry2D.IntersectPolygons` (or any
  Clipper call) and must tile without gaps, grow each by ~0.7 px (`OffsetPolygon`, mitre)
  before triangulating, and fall back to the ungrown piece if the grown one will not
  triangulate.
- **Why:** Clipper snaps to a fixed grid, so a vertex shared by three cells comes back as
  three vertices a few thousandths of a pixel apart — enough for one pixel of what is
  underneath at high zoom.

### Keying an AI sprite sheet off flat magenta: three traps (v0.11.4, overworld kits)
- **Symptom:** (1) a threshold key ate holes in purple and pink paint, or left the
  background opaque where it showed *through* a symbol (between a pavilion's columns, a
  gate's bars) because the model painted it a shade off the key; (2) grey stone and green
  leaves carried a pink cast — the model bounces light off its own background; (3) the
  ink-wash sheet's white mist came out with a pink halo once the matte was forced opaque.
- **Rule:** matte by *chroma difference* (`1 − (min(r, b) − g) / (min(Kr, Kb) − Kg)`: exact
  for achromatic paint); force opaque only what is more than ~3 px from pure key **and**
  not itself near the key colour; un-mix the key from every part-transparent pixel; despill
  everywhere for a palette with no pink or purple, in the edge band only for one that owns
  them; never force or erode a sheet whose paint dissolves into paper. All of it is per
  world, as data (`WORLDS` in `Tools/overworld_art/build_kits.py`).
- **Why:** one threshold cannot tell purple paint, magenta spill and magenta background
  apart; position (near the edge or not) and hue balance (r ≈ b) can. And always review on
  three grounds — dark, white, the world's own (`--sheet`): each hides a different fringe.

### `.import` is git-ignored, so import settings are not part of the project — set them in code (v0.11.4, reference)
- **Rule:** a texture that will be drawn much smaller than it was painted needs mipmaps,
  and "Mipmaps → Generate" in the import dock does not survive a clone (`*.import` is in
  `.gitignore`). Build them at load: `texture.GetImage()` → `GenerateMipmaps()` →
  `ImageTexture.CreateFromImage` (`PageTextures.Mipmapped`), and set
  `TextureFilter = LinearWithMipmaps` on the node. The same goes for any other import
  option a feature depends on.
- **Why:** a kit's ground is 1024 px drawn at about a third of that; without mipmaps the
  scratchboard lines, pixel art and hatching shimmer when the map moves — and the setting
  that fixes it would silently exist on one machine only.

### A sprite with a baked shadow cannot be mirrored or freely rotated (v0.11.4, overworld furniture)
- **Rule:** everything in the pages view is lit from the upper left because the kits'
  symbols are; a shadow baked into a sprite turns with it. Do not flip such a sprite at draw
  time — cut the mirror image into the sheet (the wave sheet holds each wave twice) — and
  keep rotation small. Kit symbols are never mirrored for the same reason.
- **Why:** one shadow falling the wrong way is what makes a collage look pasted.

### Capture tooling: the debug overlay is in the picture, and a tool that opens the Map starts a run (v0.11.4, reference)
- `DebugManager` is an autoload `CanvasLayer`; its `DBG` button is in every screenshot
  unless the tool hides it (`DebugManager.Instance.Visible = false`).
- Instantiating `Scenes/Map.tscn` starts a run if none exists. A tool that does it must set
  `SaveGameService.DirectoryOverride` to a scratch folder first and put it back
  (`OverworldArtDemo.ShootRealMap`), exactly as the run check does.
- A windowed capture can be given a scratch home so that not even `debug_log.txt` lands in
  the real `user://`: `open -g -n -W --env HOME=/scratch/dir -a /Applications/Godot_mono.app
  --args --path "$PWD" --log-file /abs/log …`. `--log-file` is how to read a run started
  with `open`, which has no stdout.
- Docs images sit inside the project, so Godot imports them. A `.gdignore` in the folder
  (`Docs/Art/Overworld/captures/`) keeps them out.

### Godot 4.7 facts this work checked by building and running (v0.11.4, reference)
- An `ArrayMesh` whose vertex array is `Vector2[]` draws in a `MeshInstance2D`, with
  per-vertex colour and a texture; its UVs are **0..1** (a `Polygon2D`'s are pixels). One
  `ArrayMesh` can be shared by two `MeshInstance2D`s with different materials, and
  `ClearSurfaces()` + `AddSurfaceFromArrays` updates both.
- `CanvasItemMaterial` `BlendMode` `Mul` and `Add` work on the Mobile renderer.
  `Viewport.Msaa2D` can be set at runtime (and should be put back).
- `Control.ClipContents` clips `Node2D` children too.
- `Geometry2D.OffsetPolygon`, `TriangulatePolygon` and `IntersectPolygons` did what their
  documentation says for every outline here (realm polygons, river strips, the pentagon,
  city fields). `PaperCut.Tear` relies on the documented empty result of
  `TriangulatePolygon` for an outline that crosses itself (it halves the roughness and
  tears again); no seed captured needed the fallback, so that path is untested in practice.
  Opposite windings were not tried.
- A map view built as children carrying one `Transform2D` (the same affine `Project` every
  other view uses: `origin = Project(0)`, axes `Project(1,0) − origin`, `Project(0,1) −
  origin`) pans, zooms, rotates and tilts without re-running any `_Draw`.
- `ThemeDB.FallbackFont` compiles in 4.7. It is only a fallback here and was never
  exercised: both hosts pass in a `Control`'s `GetThemeDefaultFont()` (v0.2.3).

### Coverage has two directions — every node had a map, and two maps had no node (v0.11.3, no-orphan check)
- **Symptom:** `MapContentCheck` has asked since v0.10.7 whether every kind of combat node is
  served by a shipped map. Nothing asked the reverse. Cloud Court's map was tagged for a
  world code no world owned (`CC`) from v0.10.9: the check printed a `note:` and exited 0, and
  no run could open a finished, tested, screenshotted map. A second one nobody had noticed:
  Crownwave Colosseum (`seashore_5`) is tagged LV4 without the `boss` goal; every outer LV4
  node is a boss fight, a Boss mission prefers `boss` maps, and v0.11.0 gave VanillaKindom a
  boss arena — which quietly took every node the Colosseum had.
- **Rule:** selection is checked both ways, with the run's own rules
  (`CombatMapCatalog.CandidatesFor` / `VoidCandidatesFor`): every node finds a map, and every
  shipped map is found by some node a run can produce (`CheckEveryMapIsDealt`, the `reach`
  line). A map that cannot be dealt **fails**; a note is not a check. When you add a better
  candidate for a kind of node, look at what it displaces.
- **Why:** filters are conjunctive and candidates compete, so a map becomes unreachable
  without any line of its own changing — by a tag nothing rolls, or by a newcomer that is
  always preferred. Only the reverse walk sees it.

### A world got a second realm, and "the world's tile set" would have answered with the first (v0.11.3, Cloud Court; caught before it shipped)
- **Symptom (never shipped):** retagging Cloud Court's map `CC` → `PL` made two tile sets
  share one world. `TileSets.ForWorldAbbr("PL")` returns the first — Loong's Nest — so the
  map browser would have filed the Cloud Court map under Loong's Nest (chips reading 6 and
  0, the wrong badge), the editor would have opened it on the wrong palette, and the palette
  would have shown two groups both titled "PL · Ground". Nothing fails: each still shows
  *something*.
- **Rule:** a world may have several realm sets. Order in `TileSets.All` is data: the first
  set of a world is its primary realm (`ForWorldAbbr`), what a new empty map opens on. Which
  realm a **map** belongs to is `MapRealm.Of(doc, worldAbbr)` — what it is built from, then
  its canvas colour, then the primary — never a lookup by world. To go the other way, ask
  the set for its world (`Info.WorldAbbr`); the content check does
  (`CheckRealmBelongsToItsWorld`). `MapRealm.SelfTest` pins the Palace's two.
- **Why:** a table that happened to be one-to-one was being read in both directions. Adding
  a row made one direction ambiguous, and an ambiguous lookup returns an answer, not an error.

### A shader that does not compile fails no node count — and the headless renderer does parse shaders (v0.11.3, VOID treatment; found by a mutation that survived)
- **Symptom:** the zone-6 walk asserted that the backdrop and every piece of map art carry
  the VOID material and nothing else does. With the shader's body replaced by nonsense the
  run check stayed green, 1327 of 1327. The log had `ERROR: Shader compilation failed.` (from
  `servers/rendering/dummy/storage/material_storage.cpp`) six times; no exit code reads a log.
- **Rule:** "which nodes carry the material" and "the material draws" are two facts — check
  both. The headless (dummy) renderer runs the shader compiler, and a shader that fails
  declares no uniforms, so `Shader.GetShaderUniformList()` is a compile test a headless check
  can run: `VoidTreatment.ShaderCompiles()`. Give a code-built shader at least one uniform for
  that reason. How it *looks* still needs a real window: a GDScript `SceneTree` script outside
  the project, `open -g -n -W -a Godot_mono.app --env HOME=<scratch> --env
  FABLELAND_SAVE_DIR=<scratch> --args --path "$PWD" -s /abs/script.gd`, starting a run and
  calling `RunState.BeginAdventure` (C# autoload methods are callable from GDScript by their
  C# names). Have the script refuse to run unless `OS.get_user_data_dir()` is under the
  scratch folder.
- **Why:** I assumed a headless run could not see a shader at all and wrote that into the QA
  doc; the mutation's log said otherwise. v0.10.11's rule again — a mutation that survives
  is information — and v0.11.0's: a claim is one a run printed.

### A tint cannot drain, and the arena's backdrop colour and world tint already have an owner (v0.11.3, VOID treatment; designed around)
- **Hazard (avoided):** the obvious way to restyle a whole map is `World.Modulate` plus a new
  `Background.Color`. `Modulate` only multiplies: it darkens and tints, it cannot take colour
  out. And both properties belong to `ArenaEnvironmentController`, which lerps them during a
  storm and afterwards restores the values it captured in its own `_Ready` — which runs
  *before* `GameManager._Ready` builds the map (children are ready first), so its "normal"
  backdrop is the legacy scene's colour, not the authored map's canvas.
- **Rule:** a whole-map look is a shared material on the art, put on by
  `MapVisualBuilder.Restyle` (it skips hazard art — group `HazardArtGroup`, added before a
  looping layer duplicates the node) and on the backdrop rect; `VoidTreatment` is the one
  user. Read `COLOR` in the shader, never re-sample `TEXTURE`: region rects, the
  filter-clip flag, flips and layer tints are already in it.
- **Latent, not fixed here:** if a tsunami is ever wired to an authored map in the arena (no
  shipped map has a trigger, and `CombatMapRuntime` wires none), the storm will "restore"
  the backdrop to the legacy colour. Capture the normal presentation after the map is built.

### "The same answer twice" proves nothing for a pure function, and one pinned seed is not a pin (v0.11.3, reference)
- The zone-6 selection check asks for a node's map twice. Asked the same way twice it could
  not fail. The second call hands the run's worlds over **reversed**: removing the sort that
  makes the answer depend on *which* worlds, not their order, then fails 59 lines. Vary what
  is allowed to vary — order, call count, a fresh object — or the check is a tautology.
- A deliberate extra draw from the missions' stream moved the signature of seven of the
  eight seeds pinned on the pool of eight and five of the six older ones; seed `DEBUG` came
  out the same in both tables (the shifted rolls landed in the same bands). Pin several.
- Record a pin **before** the change (v0.11.0's rule): `RunWalkCheck.OnThePoolOfEight` was
  filled from a run at `d94b4c5` with nothing else edited — add the table empty, run, copy
  what the failure lines print.

### A local Edit Copy keeps the tags it was made with — the dev Mac deals its own Cloud Court (v0.11.3, hand-off)
- **Symptom (found by reading the folder; no check can hit it — they turn user maps off):**
  this Mac's `user://maps` holds finished copies of nine shipped maps, among them
  `cloud_court_heavens_pl.json` from 15 November, tagged `PL` / terrain `*`. A finished copy
  with a shipped map's id replaces it (MapCreation.gdd §9), so on this machine Cloud Court is
  dealt on **every** band of the Palace — and was while the shipped file said `CC` — and the
  other eight copies hide whatever those shipped maps have become since.
- **Rule:** when a shipped map's selection tags or content change, the hand-off says so and
  names any copy in `user://maps` with the same id. To see what ships: delete the copy (the
  map browser's Delete), or run against a scratch `HOME` (v0.10.7).
- **Why:** v0.10.7's worktree caveat in another place — what the owner runs is the
  repository *plus* a folder no commit touches.

### Hit-stop holds a body, so "the tick after the hit" is no longer when a shove shows (v0.11.2, hit feedback)
- **Symptom:** with hit-stop in, the boss check failed one line of 109: `sides: two 300 px/s
  shoves moved the boss at 0 px/s`. The probe hit the boss twice and read `Velocity.X` one
  tick later. Nothing was wrong with poise: the boss was held by the stop those hits earned
  (52 ms, 4 ticks), and its knockback starts when the stop lets go.
- **Rule:** a hit's knockback is added to the external-velocity channel at once and *moves*
  the body only after its stop. A test (or a kit) that needs the shove waits for
  `!body.InHitStop` first; a held body reports `Velocity` zero on purpose. The probe now
  waits out the stop and asserts the same bound as before — the expectation moved in time,
  it was not loosened.
- **Why:** position hold is what hit-stop *is*. Everything that reads "what happened right
  after a hit" from positions or `Velocity` shifts by 2–11 ticks; clocks do not (every timer
  keeps running through a stop), which is why nothing else in three checks moved.

### A projectile that frees itself on contact can be entered twice in one physics frame — 24 seeds had 25 endings (v0.11.2, found by the feedback check)
- **Symptom:** the check counted every Pome seed's end — a hit or an expiry on terrain — and
  got 7 + 18 = 25 for 24 seeds. `PomeSeed.OnBodyEntered` called `QueueFree()` and returned;
  a seed that reached a target standing on the floor got `body_entered` for the target *and*
  for the floor in the same physics frame, and resolved twice. Before cues existed the second
  resolution was invisible unless both bodies were foes — then one seed hit two of them.
- **Rule:** `QueueFree` is deferred to the end of the frame; signals are not. A projectile
  that ends on first contact latches a `_resolved` flag as the first line of its handler.
- **Why:** an Area2D reports every overlap that began in the step, one signal each. "It
  freed itself" is not true until the frame is over.

### A foe acts on its first tick and faces a random way — a probe that cleared its log after spawning one had already lost what it was waiting for (v0.11.2, feedback check)
- **Symptom:** "a level-6 crab beside the player raises `jump`" failed every run, and "a
  seagull's dash starts its telegraph loop" failed two runs in three. Both probes spawned the
  foe, waited a few ticks for it to settle, cleared the cue log, poked the foe, and waited.
  The crab had seen the player on its first sight check and jumped on its second tick; the
  gull, when it happened to face the player, had started its telegraph before the log was
  cleared. Each then sat on a 5–10 s cooldown while the probe waited for a cue already gone.
- **Rule:** clear, *then* spawn. And stage what a foe must do rather than hope it notices:
  a hit turns it on the player whichever way it faces (the gull probe hits it only if the
  telegraph has not already begun). This is the v0.10.11 "a crab walks left or right at
  random" caveat again, one layer up — the unseeded `BaseFoe.Rng` decides the first second.
- **Why:** sight timers start at zero, so a foe's first decision is made on the tick it is
  placed. "Wait for it to settle" is exactly when that happens.

### Never launch Godot from the Anaconda python: an x86_64 parent starts it under Rosetta, and the checks hang (v0.11.2, tooling)
- **Symptom:** a script running the feedback check forty times (one deliberate breakage per
  run) stalled on the first: Godot printed its banner and then sat at 0 % CPU until the
  timeout. The same command typed in the shell took 17 s. The unmutated check hung the same
  way, with output piped or sent to a file, with a clean environment, even through `zsh -c`.
- **Cause:** `python3` on the PATH is Anaconda's, an **x86_64** binary on an arm64 Mac. A
  universal binary launched by a translated process is started in the parent's architecture,
  so Godot ran under Rosetta and never got its (arm64) .NET runtime going.
- **Rule:** drive headless Godot from the shell or from `/opt/homebrew/bin/python3` (arm64;
  `python3 -c "import platform; print(platform.machine())"` tells you which you have), or
  prefix the command with `arch -arm64`. `dotnet build` is unaffected.
- **Also:** a script that edits sources and restores them in a `finally` must be stopped
  with SIGINT *sent to the python process* — `pgrep -f script.py` also matches the shell
  wrapping it — or the file under test is left broken. After any interrupted run, check
  that every pattern the script replaces is back before building.

### `Modulate` can only darken, so a hit flash is a shader — and headless Godot cannot show it to you (v0.11.2, hit feedback — designed around)
- **Symptom (avoided):** the old hit feedback was `Modulate = (1, 0.6, 0.6)` — a pink tint,
  because `Modulate` multiplies and cannot whiten a sprite. A real flash needs a
  `canvas_item` shader (`COLOR.rgb = mix(COLOR.rgb, flash_color.rgb, flash_amount)`), and no
  headless run can tell whether a shader draws what it should: the dummy renderer draws
  nothing.
- **Rule:** a visual that cannot be verified here must not be able to break what can. The
  flash material is put on a body's sprites **only for the frames a flash lasts** and taken
  off again (`BodyFlash`); at rest every body is drawn exactly as its scene authored it, so a
  wrong shader can spoil two to five frames, never the game. The check proves the channel
  (applied, strength, colour, restored, `Modulate` / `Visible` untouched), not the picture.
- **Also:** one `ShaderMaterial` per body, not one shared — a shared one flashes everybody.
  Its first use compiles the shader mid-fight; pre-warming it belongs with the VFX work.

### A hit-stop counted in the base `_PhysicsProcess` never ends for a kit that skips it (v0.11.2, hit feedback — designed around)
- **Symptom (avoided):** PumpKing's `_PhysicsProcess` returns before the base call while he
  is in Soul Form. A stop counted down in the base tick would hold a soul's animation
  forever and never its flight; Sifu Pangda, who calls the base and *then* moves himself
  (dive, dash, palm lunge), would have kept moving through every stop.
- **Rule:** the stop is counted by `HoldForHitStop(worldDelta)`, which the base calls first
  thing. A kit that moves the body **instead of** the base tick calls it itself and returns
  when it says true; a kit that moves the body **after** the base tick reads `HitStopHeld`.
  Never both — the stop would run out twice as fast. `FeedbackCheck.ProbeKitPhysics` has a
  stage for each; a new kit with its own physics gets one.
- **Why world delta, in the physics loop:** counted in the body's own time a held body never
  wakes; counted in `_Process` its length depends on the frame rate, and the checks (which
  run at 8×) stop being exact to the tick.

### A limit that counts real time needs real-time waits in a sped-up check (v0.11.2, feedback check — designed around)
- The flash limiter counts wall-clock seconds, because that is what an eye sees: six full
  flashes per 0.1 s moment, three large ones per second. At 8× a probe's "wait 30 ticks" is
  62 ms of real time — still inside the last flashing moment — so the next probe's flash
  would be tinted by the limiter and read as a bug in the flash. The check waits with
  `RealSeconds(…)` between flashing moments. Anything else that is paced for the player
  rather than for the simulation gets the same treatment.

### Noted, not changed: a dashing seagull usually lands its contact hit, and its i-frames eat the dash (v0.11.2, found by the feedback check)
- The probe expected a dashing gull's hit on the player to be `seagull/s2.dash` (50 base
  damage) and got `seagull/contact` (20). Each tick `BaseFoe` tests contact damage (range
  44 px) *before* skills, and the dash tests 48 px: a gull closing at 16 px a tick is inside
  both on the same tick most of the time (by arithmetic about three times in four — not
  measured), contact lands first, and its knockback starts the player's 0.8 s of i-frames,
  which refuse the dash. Whether a dash should suppress
  contact is a FOES balance decision — it is in the Feedback GDD's decisions log, unfixed.

### A clean merge is not an agreeing one — swimming, written before fighters had sides, would have let the player's W and S swim the boss (v0.11.1, merge of the swimming work; caught before it shipped)
- **Symptom (never shipped):** the swimming work (written as v0.10.10 beside v0.10.11's boss
  fights, both cut from v0.10.9) read `Input.IsActionPressed("move_down")` and
  `InputDefaults.MoveUp` inside `CharacterController._PhysicsProcess`. A BOSS is that same
  class on a brain's `VirtualPad`. Merged as written the build is green, the content check
  and the boss check both pass — and in a sea the boss rises 1.4 m/s whenever the player
  holds W, dives at 7.4 m/s on S, and ignores his own pad's Up and Down. Git reported no
  conflict at those lines: v0.11.0 had never touched them, because they did not exist.
- **Rule:** (1) every button a fighter reads goes through `Controls`, in the base class as
  in the kits — swimming is `ControlAction.MoveUp` / `MoveDown`. (2) A mechanic a
  controller drives gets a stage with **both sides in it at once, each on its own
  controller, asserting the other is unmoved**: `BossFightCheck.ProbeSwimmingTakesSides`
  holds the player's keys, then the boss's pad, and expects each to swim only its own body
  at the speeds the sea's tuning promises. It was broken five ways and failed by name each
  time: both reads back on `Input`, each alone, swimming made Player-side only (the other
  wrong merge — "a boss is a foe, foes do not swim"), and `move_up` no longer registered.
  (3) **A check that registers what it presses hides a game that does not.** The content
  check's probe calls `InputDefaults.Ensure()` so that it can press `move_up`; with the
  game's own registration removed it still passed while the game logged an error every
  tick. The boss check never registers the action — it asserts that the game did.
- **Why:** a three-way merge says two edits did not touch the same lines, not that they
  agree. One side changed a rule ("fighters read Controls"); the other added a new reader
  written under the old one, and new lines conflict with nothing. The feature's own probe
  was green for the reason the bug existed: it drives the player, and for the player
  `Input` and `Controls` are the same thing.

### Each line kept its own promise and broke the other's — a canal authored for the old water had banks no surface jump clears (v0.11.1, merge; fixed)
- **Symptom:** v0.11.0's Forgotten City low-ground map lays a canal across its pit with the
  banks one row (2 m) above the water — fine for v0.11.0's water, left by "a second jump".
  The swimming work's rule is that a pool is left the way a player tries first: swim up to
  the bank, jump — and a jump at the surface of swamp water clears it by 1.0 m. Merged, a
  fighter at that bank comes up a metre short and falls back in. (Not a pit: tapping Jump
  twice still got out in 0.75 s, measured, because the second launch starts half out of
  the water.) Neither line's checks saw it: the swimming probe swam the one canal that
  existed when it was written, and v0.11.0's checks never ask a body to leave a pool.
- **Rule:** the canal is flush with its banks now (`build_band_maps.py`: that pit is one
  row shallower), and the probe's pools are data — `ShippedPools` — **with a shipped map
  that holds swamp water and is not listed failing by name**, so the next pool cannot ship
  unswum. When two lines meet, run each side's checks against the other side's *content*:
  find the list a check walks and extend it before believing its green.
- **Why:** a check written against "the shipped map" checks one map; content added on
  another branch is outside it by construction. And a rule written into a GDD binds the
  maps that were authored before anyone had written it.

### One stream laid as several tiles pushed twice as hard at every joint (v0.11.1 fix; shipped in v0.10.9)
- **Symptom (found while writing a probe stage, then measured):** a bubble column is a
  stack of `softvolume.ln_bubbles` tiles and a waterfall a stack of `bm_waterfall`; each
  placed tile is its own `SoftVolume`. `SoftVolumeSet` summed the `Flow` of every volume a
  body overlaps — and a body across the joint of two tiles overlaps both. Riding up a
  column whose lift is 4 m/s, the body did **11 m/s** for the ticks it straddled a joint
  (2 × 7 up, less the 3 m/s sink); by the same sum a waterfall carried 28 m/s there instead
  of 14. v0.10.9's probe measured each carrier inside a single tile, so it never saw a joint.
- **Rule:** an identical carry counts once (`SoftVolumeSet.AlreadyCarrying`): tiles of one
  kind and flip have bit-identical `Flow`, so equality identifies "the same stream"; two
  different streams that cross still add. The probe now rides a body across a joint and
  compares the fastest single tick with the speed inside one tile. **When a thing is
  authored as repeated tiles, test it across a seam, not inside one tile.**
- **Why:** the per-tile volume is an authoring unit, not a physical one. Summing is right
  for different things and wrong for pieces of one thing; media never had the problem
  because they combine strictest-first, where two equal volumes are idempotent.

### A repeatable impulse is a speed — report the rate, not the single press (v0.11.1, swimming)
- **Symptom (found by arithmetic when the owner asked about swimming up; it had shipped):**
  in water a jump was documented as "a 3.4 m stroke, repeatable" — jumps refresh inside any
  medium and the cooldown is 0.3 s. Nobody divided: 3 m every 0.32 s is **about 10 m/s**
  upward by tapping Space, in a sea whose swimming speed is 5.5 and where running on land
  is 10. The probe asserted the height of one stroke and was green, and the number the
  owner was shown ("3.4 m") hid what the mechanic does.
- **Rule:** for anything a player can repeat, measure and report the **rate** (effect per
  press ÷ the shortest gap between presses), next to every sustained way of doing the same
  thing. The probe taps Jump every other tick, counts the launches against what the
  cooldown allows, and prints `sea climb  holding Up 2.5 m/s · tapping Jump 10.0 m/s · both
  10.7 m/s`. Whether that ratio is right is the owner's decision — a first cut of the
  swimming work removed jumping inside volumes for this reason and the owner put it back, wanting both
  ways; the cooldown is the knob (TBD #35) — but it must be decided on the rate.
- **Why:** a per-press number reads as small; a player's thumb turns it into a velocity.

### A detector tuned on the healthy signal missed the failure it was written for (v0.11.1, probe; caught by its own mutation run)
- **Symptom:** the probe proves the jump cooldown holds inside a volume by tapping Jump
  every other tick and counting launches. It counted a launch as "upward speed rose by
  40% of a jump's launch speed in one tick" — true of every launch in a working game,
  where a stroke has faded before the next. With the cooldown removed (the mutation the
  assertion exists for) the body re-launched every two ticks, its speed never fell far,
  and the counter reported **1 launch** where there were 25. The run still failed, on a
  different assertion, by luck.
- **Rule:** build a detector from what *cannot* happen otherwise, not from what the event
  looks like when all is well: with no direction held nothing but a launch adds upward
  speed in a volume, so any gain counts. And **run the mutation an assertion is for, and
  read which line failed** — a red run is not proof that the right check caught it. The
  mutation runner requires each failure to name its mechanism for this reason.
- **Why:** a threshold calibrated on normal behaviour encodes the very assumption the
  check is supposed to test.

### Swimming: three ways the obvious rule goes wrong (v0.11.1, caught before it shipped)
- **Thrust must never brake, and a launch must coast under gravity.** Vertical steering is
  "accelerate toward ±speed + drop". Written as a plain `MoveToward`, holding Up *slows* a
  jump launch (the launch is faster than the target). Written as "no gravity while a
  direction is held", holding Up lets a launch **out-rise the open air** wherever the
  resistance is weaker than gravity (all foliage) — the v0.10.9 water bug again. So:
  thrust only ever adds speed toward its target; a body already faster than its own
  swimming is left to the settle (gravity toward the drop) and the resistance.
  `SoftVolumeSet.SelfTest` holds a launch with Up held against the same launch in the open.
- **Overlap arithmetic can make a trap.** Media combine strictest-first, so kelp (0.3) in
  the sea swam at 3 m/s against the sea's 3 m/s sink: up was exactly **0**. A medium whose
  swimming speed is not above its drop cannot be left upward. Every registered medium is
  asked the model (`CheckEveryMediumCanBeSwumUp`), and the probe swims the shipped overlap.
- **Membership is binary, so "push until out" hovers the whole body on top.** A body is
  "in" a volume while any part overlaps. If Up pushes as long as the body is in, it rises
  until only its feet touch, falls back, is pushed again — standing on the sea, buzzing. So
  Up pushes only while the **head** is under (`SoftVolumeSet.Covers(Crown)`), which rests
  the head at the surface (within 0.2 m in water). Anything else keyed to that line has to
  allow for the head bobbing across it. `Covers` is geometry on the volume's own shapes:
  its `Area2D` is on collision layer 0, so a physics point query cannot find it.

### Anything a seed is fed is part of the save — growing the world pool would have moved every saved run (v0.11.0, world pool)
- **Symptom (caught before it shipped):** a save stores the seed, not the map, and
  `MapGenerator` shuffled `WorldDef.Pool` to pick a run's five realms. Adding the Kingdom of
  Horror and The Forgotten to that list would have made every existing save regenerate into
  *different realms*: the `HC-2-3` it was standing on gone, its visited and completed ids
  dropped with a warning each, the player back at the start node. Three real slots on this
  machine were in that position.
- **Rule:** an input to a seeded generator that can change between versions is saved with
  the seed, or frozen. A run records its pool (`RunSaveData.WorldPool`, save version 4),
  `MapGenerator.Generate` takes the pool it is given, the migration hands an older save the
  six worlds of its day (`SaveGameService.WorldPoolBeforeV4`), and `WorldDef.Pool` is
  **append-only**. Before changing such an input, record what the old one generated — six
  seeds' realms, node ids, kinds, levels, missions and road counts are pinned in
  `RunWalkCheck.BeforeV4` — because afterwards there is nothing left to compare against.
- **Why:** "the map is derived state, so do not save it" (T30 §6) is only true while the
  derivation is frozen. The pool looked like content; it was part of the function.

### A scripted run writes and deletes real save slots — `user://` is one folder for every checkout (v0.11.0, run check)
- **Hazard (designed around, never hit):** `NewRun` claims slot 1 when no slot is active,
  every mission resolution and day end checkpoints it, and `EndRun` deletes it. A headless
  check or a capture script that plays a run — from any worktree — would overwrite the
  player's slot 1 and then delete it.
- **Rule:** redirect first, then start anything: `SaveGameService.DirectoryOverride` from C#,
  the environment variable `FABLELAND_SAVE_DIR` for a GDScript tool that cannot reach it. Turn
  `CombatMapCatalog.UserMapsEnabled` off as well, so what a run is dealt does not depend on
  the developer's own maps. Afterwards, look at the real folder's timestamps. To test real
  saves, copy them (`RunWalkCheck -- --saves=<folder>`).
- **Why:** the v0.10.7 caveat below ("use a scratch `HOME`") covers a process you launch; a
  check that runs inside the game needs the same isolation as a seam in the code.

### An arena adopts the first node in group `player` — open one in the frame you free another and it gets a dying player (v0.11.0, capture tooling)
- **Symptom:** a windowed capture of twelve arenas: the first looked right, the other eleven
  showed sky and far hills and nothing else — no ground, no fighter. The headless run check,
  entering all 45 maps, passed.
- **Cause:** the tool freed an arena and instantiated the next in the same frame.
  `queue_free()` is deferred, so the new arena's `_Ready` found the OLD arena's player first
  (`GetFirstNodeInGroup("player")`), put *it* on the spawn and set *its* camera's limits; the
  new arena's own body stayed at the scene origin with an unlimited camera, looking at empty
  sky. The check waits three ticks between arenas.
- **Rule:** one arena in the tree at a time; let a frame pass after freeing one. In the game
  this cannot happen — a scene change frees the old scene before the new one enters.
- **Why it is here:** the picture said "the maps are broken"; the assertion beside it ("the
  player stands inside the map, on the floor") said they were not. A screenshot is evidence
  of what the tool did; it takes a check to say what the game does.

### Generating a map for eight kits at once: what the kit catches, and what it cannot (v0.11.0, band maps, reference)
- **Every realm's pieces have their own footprints** (a 3 × 4 palm, a 2 × 6 kelp, a 4-wide
  roof, a 1 × 2 cactus). A shared layout that says "a soft volume here" overlaps something in
  some realm. Reserve the marker cells in the layout and fit each realm's furniture round
  them; `mapkit` refuses an overlap, which found each of the five on the first build.
- **A marker cannot stand in a soft volume's cell** (one tile per cell per layer). Water wide
  enough to fight in goes on its own colliding Farview layer, added before the battlefield
  (style bible §8, "a sea is a layer") — the Forgotten City's canal, like Loong's Nest's sea.
- **The kit does not know a boss's reach.** The first boss arenas had their two perches 28 m
  apart; a Soul Form flight covers about 20 m with the climb. The run check now measures it.
- **Five beach tiles have no art** and draw as flat coloured squares in the game:
  `deco.rock`, `deco.flower`, `deco.column`, `softvolume.bush1x1`, `ground.stone`. Nothing
  fails; only a rendered frame shows it. `preview.load_tiles()[id].get("sprite")` tells.

### Playing a run from a check: what bit, in order (v0.11.0, reference)
- **A scene change frees the current scene** — which is the check itself when it is launched
  as a scene. `GetTree().CurrentScene = null` (deferred, from `_Ready`) lets it live under
  the root through every `ChangeSceneToFile` that follows.
- **The swap is deferred.** After `BeginAdventure` the *previous* arena is still the current
  scene for a frame: wait for an arena that is not the last one and whose adventure is the
  node just entered.
- **Enter debug arenas before any run exists.** `ReturnToMap` leaves `CurrentAdventure` set
  (v0.10.11), so an arena instantiated by hand afterwards believes it is in that run.
- **`BeginAdventure` is not a legal move** — it enters any node. Good for coverage; it proves
  nothing about roads. **`EndDay` on a devour day** (10, 20, 30, 40, 43, 45) can end the run
  under a check standing on a city: set the day for a boss and do not end it.
- **A node's band and mission are data on the node.** A seed may not hold every pair, so the
  walk writes `node.Terrain` and `node.Mission` before entering, instead of hunting seeds.
- **A restored source file is not a restored assembly.** The mutation runner rebuilt only
  when it had mutated a `.cs` file, so the two *data* mutations that followed a code mutation
  ran against that mutation's DLL and "failed" with its failures. Build before every
  mutation, data ones included — and read which line caught it (v0.10.11's rule, again).
- **The number that went into the design doc was wrong until it was printed.** Reading
  `_invulnTimer` said a head that touches the player costs the contact only (18), because the
  blast would land inside the i-frames; the check printed 80 at ×1 — both land, since only a
  hit *with knockback* arms the timer. A design number is one a check prints.

### A "resting" branch that multiplies the velocity every tick is not friction — PumpKing's head never rolled (v0.10.11, boss fight)
- **Symptom:** the head's code and GDD both describe a roll — "settles into a capped roll",
  6 m/s cap, 3 m/s² roll friction — but a head on a floor stopped within 0.7 m. Every tick it
  rested, gravity pushed it 0.5 px into the floor, `MoveAndCollide` reported the contact, and
  the contact branch applied the 85% *bounce* retention to the whole velocity — 15% of the
  speed gone sixty times a second, about 1600 px/s² against a designed 96. It also threw away
  the rest of that tick's move (`MoveAndCollide` stops at the contact; nobody slid the
  remainder). Nobody noticed on level ground because a thrown head has spent its speed on
  fourteen bounces before it rests. On a slope it would have crawled at 1–2 m/s.
- **Rule:** decide *bounce or roll* by how hard the contact is (the speed it would leave the
  surface with), and in the roll keep the velocity along the surface, apply friction to that,
  and spend the unspent part of the tick moving along it. A restitution factor belongs to a
  bounce, never to a resting contact. The boss check measures both: a 6 m/s roll must run 6 m.
- **Why:** with a kinematic body, "is touching the floor" happens every tick, so anything
  written in the collision branch is applied at 60 Hz. A per-contact factor is silently a
  per-second rate of `factor⁶⁰`.

### A cap applied before the tick's acceleration is not a cap, and the leftover of a move carries the old speed (v0.10.11, boss fight)
- **Symptom (found by the probe, never shipped):** with the roll fixed, a head ran a 1:2 slope
  at 6.5 m/s across against a 6.0 cap — then, with the cap moved, still at 6.4. Two causes.
  The cap clamped `_velocity.X` at the top of the tick, *then* gravity's pull down the slope
  was added and kept. And the rest of the tick was spent along `GetRemainder().Slide(normal)`
  — the leftover of the pre-contact motion, which still carried that uncapped speed.
- **Rule:** cap what the tick *ends* with, and move the remainder at the new velocity
  (`_velocity × dt × unspent fraction`), not along the old motion's leftover.
- **Why:** a slope turns "gravity" into "acceleration along the direction you are capping";
  on level ground the same code was exact, which is why the cap looked right for months.

### A change of slope is not a fall — a rolling head bounced four times at the foot of the slope (v0.10.11, boss fight)
- **Symptom (found by the probe, never shipped):** the boss's head reached the bottom of his
  slope and died 3 cells later, far short of a player standing there. Rolling off a 1:2 slope
  onto level ground it met the floor at 3 m/s — above the 1.5 m/s roll threshold — so it
  bounced, and each bounce kept 85% of everything: four bounces, half its speed.
- **Rule:** a body that was rolling on the previous tick and meets floor at another angle
  keeps rolling (`_ticksSinceRoll`). Only a contact after real air time is judged as a landing.
- **Why:** the threshold answers "did it fall onto this?", and a corner in the floor looks
  like a fall to a test that only sees the speed into the surface.

### AddChild to your parent from `_Ready` fails when you are authored in its scene — Pixolotl's bubble pool was 18 orphans (v0.10.11, found by the boss check)
- **Symptom:** the first headless run of `Arena.tscn` printed `Parent node is busy setting up
  children, add_child() failed` eighteen times. `Pixolotl.WarmBubblePool` (from `_Ready`) did
  `GetParent().AddChild(bubble)`; when she is the body authored in `Arena.tscn`, that parent
  is still setting up its children and Godot refuses. The eighteen bubbles stayed in her pool
  list, never entered the tree, and her BA rented them and fired nothing. A Pixolotl created
  later by a swap (Tab, the debug page) worked, which is why it went unseen: a real run's
  team leader replaces the authored body unless the leader *is* Pixolotl.
- **Rule:** from `_Ready`, add siblings with `GetParent().CallDeferred(Node.MethodName.AddChild, node)`.
  A node may add children to *itself* in `_Ready`; its parent is busy until the whole scene
  is ready.
- **Why:** `_Ready` runs while the parent is still propagating ready to its children, and
  Godot blocks changes to a child list it is iterating. The same code works when the node is
  added at runtime, so only the scene-authored instance breaks.

### A fighter built from a protagonist's scene brings the player's camera, group and keyboard with it (v0.10.11, boss fight — designed around)
- **Symptom (avoided):** `BossPumpKing.tscn` inherits `PumpKing.tscn`, which has a `Camera2D`
  with the shake script, and `CharacterController` used to claim `LocalPlayer`, join `player`
  and read `Input` unconditionally. A boss made that way steals `ShakeCamera2D.Instance` (and
  possibly the view), is attacked by crabs as "the player", is skipped by every player attack
  (they looked for `BaseFoe`), and walks when the player presses right.
- **Rule:** the base decides all of it from `Side`. A Foe-side body removes its camera in
  `_EnterTree` — before the child enters the tree and claims the singleton — and swaps
  `PlayerControls` for a `VirtualPad` there too, so it never reads the keyboard, not even for
  the frame before a brain is attached. The boss check pins each (and was mutation-tested).
- **Why:** a protagonist scene is "the player" in a dozen small ways that are not in one
  place. Inheriting the scene is still right — it is 560 lines of animation — but the
  identity has to be taken out explicitly, at the one moment it is still inert.

### One button, two loops: a tap must be owed to the physics loop and the process loop separately (v0.10.11, boss brain)
- **Symptom (avoided):** abilities read input in `_Process`, jumps in `_PhysicsProcess`. A
  virtual pad that held a tapped button "for one frame" would be seen by whichever loop ran
  next and missed by the other whenever no physics tick fell inside that frame — a boss that
  sometimes does not jump, depending on the frame rate.
- **Rule:** `VirtualPad.Tap` arms the press once for each loop; `StepProcess` / `StepPhysics`
  each consume their own, before the body runs (`BossBrain` has process priorities of −100).
  Its `SelfTest` steps one loop many times and checks the other is still owed.
- **Why:** Godot's own `is_action_just_pressed` keeps a frame number per loop for the same
  reason. Anything that imitates input has to imitate that.

### Testing a fight headless: what bit, in order (v0.10.11, reference)
- **A crab walks left or right at random** (`BaseFoe.Rng` is unseeded unless a spawner seeds
  it). A probe that rolled a head at a crab passed two runs in three: when the crab walked
  away, the head caught it a few ticks after the probe had stopped looking. Wait for the
  event with a budget (`for … && hp >= before`), never for a fixed count.
- **Set a rolling thing down at rest height.** Dropped from even 4 px it bounces first, and
  a bounce keeps 85%: "a 6 m/s roll ran 4.7 m" was the probe's start, not the physics.
- **A debug arena pauses the tree under its win banner.** Tweens and fades stop with it; a
  check that waits for a beaten boss to fade must unpause first (a real run does not pause).
- **`RunState.ReturnToMap` leaves `CurrentAdventure` set**, so an Arena loaded from the map
  screen is not a debug arena — it replays the last node. Debug staging (the boss page)
  therefore refuses while a run's adventure exists; stage from the title screen.
- **The fight is not frame-deterministic** — abilities, kit timers and the brain run in
  `_Process`. Assert behaviour (did he throw, did it arrive), not timings.
- **A mutation that survives is information.** Removing the side filter from `HostileTo`
  changed nothing, because groups already separate the sides: the check now puts a body in
  the wrong group to prove the filter. Gating a boss's hits on an i-frame timer changed
  nothing either, because nothing arms that timer for a boss — an equivalent mutant, noted.
- **Break every rule of a brain, not a sample — and read WHICH assertion caught it.** The
  first mutation run removed two of BOSS PumpKing's seven rules. Removing the other five
  later showed: "hold perch" and "call back" could be deleted and the check still passed
  (neither had ever been watched working — the fight probe never knocks him off his perch
  or gets behind him), and removing "detonate" was "caught" by `sides: the player's own head
  must not hurt the player`, an assertion about something else entirely. That probe's boss
  had a live brain and was bowling at the player while the probe counted the player's HP, so
  when his head went off moved the damage into the wrong window. Rules: (1) a probe that
  counts HP hit by hit must own every hit — once the decisions under test are over, put the
  brain to sleep (`BossBrain.Sleep()`; not `StandDown()`, which also makes him invincible)
  and let any head already out finish; (2) a rule whose effect another mechanism can also
  produce (a head that bumps into the player hurts them without any rule) needs a stage
  where ONLY the rule can produce it — `ProbeHisRules` puts a wall at the player's shoulder
  for "detonate" and sends the player behind him for "call back" — and must be attributed
  (`BossRule.Starts`), not inferred from the damage; (3) a new rule gets its scenario there
  and is broken once before it ships.
- **A headless check that "hangs" for twelve minutes and then passes: the Mac slept.** On
  battery with nobody at the keyboard macOS drops into maintenance sleep and wakes for
  ~45 s at a time; a run that takes 11 s took 742 s, with a perfect log. Every loop in the
  checks has a tick budget, so they cannot wait forever by construction — when the wall
  clock stalls, look at `pmset -g log | grep -E "Sleep|Wake"` before suspecting the check.

### The version number was already taken by uncommitted work next door (v0.10.11, hand-off)
- **Symptom (caught before the bump):** this branch was cut from v0.10.9 and would have become
  v0.10.10. A sibling session worktree (`game-map-editor-ui-b1ced6`) already held an
  **uncommitted** v0.10.10 — "a fighter swims in every soft volume" — touching the same
  `CharacterController.cs`. Two different builds with one version string merge without a
  conflict on the version files (identical edits), which is exactly the v0.6.8 trap.
- **Rule:** before bumping, look at `VERSION` in every worktree (`git worktree list`), dirty
  ones included — uncommitted work has a number too. Take the next free one; a skipped
  number is harmless. This branch is **v0.10.11**.
- **When the two meet** (written in v0.10.11): the swimming work reads
  `Input.IsActionPressed("move_down")` and `InputDefaults.MoveUp` directly inside
  `CharacterController._PhysicsProcess`. Route both through `Controls`
  (`ControlAction.MoveDown`, and `ControlAction.MoveUp`, which exists for this) — otherwise
  the player's W and S keys swim the boss.
- **They met in v0.11.1.** The swimming work never became a v0.10.10 of its own: it was
  merged onto v0.11.0 and is **v0.11.1**, which is the tag its rules and caveats carry in
  this file and in the GDDs. Both reads go through `Controls`; the guard on `MoveUp` is gone
  because `PlayerControls` now registers the action itself; and the boss check has a stage
  for exactly this sentence (see the first caveat above).

### A slope's crest sits under its ramp, so a one-row slope tile loses it at every joint (v0.10.11, realm art)
- **Symptom (caught on paper):** the Dark Wood's earth draws its walking line 13 px under the
  top of the cell. Painted the same way on a ramp, the line is *below* the ramp's foot at the
  low end — outside a one-row sprite — and reappears at the top of the next block: a gap in
  the grass at every joint, with the bare cell below showing through.
- **Rule:** a slope block is two rows (`RealmTiles.Slope`, style bible §2.3a): the ramp
  crosses the upper row and the lower row is solid, so the crest has room. Make the crest's
  wobble periodic over the block's width so blocks join, and give the block its material's
  `AutotileGroup` so the earth around it draws no crest or flank against it.
- **Why:** a tile can only paint inside its own footprint, and the offset between "where you
  stand" and "where the line is drawn" is a property of the material, not of the tile.

### What you can stand on must be the lightest thing in its wall — and nothing goes in front of a black boss (v0.10.11, realm maps)
- **Symptom:** the first in-engine frames of the castle showed the balcony vanishing into the
  castle front behind it (same stone, same tone) and the balustrade — a foreground layer —
  hiding the legs of a black-coated, momentarily headless PumpKing against black masonry.
- **Rule:** tint a backdrop built from a solid material darker than the solid (`#3D4978` on
  the castle front); give a platform in front of a wall a broad bright coping and a face
  crowded with scratches; put railings *behind* the fighters. The boss wears a red mark until
  his boss art exists (`BossPumpKing._Draw`, `NOTE(art)`).
- **Why:** style bible §2.6 — a fighter must read first — is easy to keep for the hero and
  easy to forget for an opponent whose own palette is the realm's.

### Laying a slope with the map kit: order and layers (v0.10.11, reference)
- `f.slope(...)` first, the floor `fill` after: the lowest block's solid row is part of the
  floor, and `fill` skips taken cells while `put` refuses them.
- Trees, lanterns, the gate and the windows cannot share a layer where they overlap (one tile
  per cell per layer): the castle map uses "Wood" for trunks and "Lights" for everything that
  glows, both at parallax 1.
- On a 1:2 slope the middle of an odd column is a quarter cell above a row line:
  `stand(tile, x, 32 - x // 2, nudge=(0, 1))` for a slope whose foot is at column 14, row 26.

### A resistance weaker than a body's own acceleration is not a speed cap — the first water let a swimmer run at 95% and out-jump the air (v0.10.9, water)
- **Symptom (caught by doing the arithmetic, then measured; never shipped):** water was first
  tuned as "a SoftVolume with stagnation 0.5 and 35% gravity" and described as *half speed,
  floaty*. On a real character it was neither. A SoftVolume's resistance is a deceleration,
  `g × (1 − stagnation)` ≈ 1000 px/s², applied toward the cap; a character accelerates at
  1900 px/s² in the air and 3200 on the ground. So a held direction simply outran it — the
  swimmer settled at **9.5 of 10 m/s** — and with gravity cut to 35% and that weak a
  resistance a jump rose **9.4 m, against 8.3 m in the open**. Left alone the body sank at
  6 m/s. The same has always been true of foliage: its cap shortens a *coast*, not a run.
- **Rule:** an index is not a behaviour. A medium that must really hold a body needs `Drag`
  (the resistance multiplier: the cap holds once `g × (1 − s) × drag` exceeds the body's
  acceleration — about 4 and up for a fighter) and, if it is buoyant, `SinkSpeed` (an
  **absolute** sink cap: every other index is relative to a body's own top speed, so without
  it a 2.8 m/s crab hangs in the water for a minute). `TileRegistry.Validate` refuses a
  `SinkSpeed` the medium cannot hold. And **tune by what the content check's probe prints**
  (`physics  sea  sinks 3.0 m/s · swims 5.5 m/s · …`) — it puts the real Pomegraknight in
  each shipped kind — never by reading the tuning table.
- **Why:** the resistance and the input are two accelerations racing toward different
  targets; whichever is larger wins, and the loser only shows when the input stops. Nothing
  in a table of 0..1 indices says which one that is.

### A body must not remember "the" soft volume it is in — leaving an inner one forgot the outer (v0.10.9, water)
- **Symptom (caught before it shipped):** players and foes each kept a single `_softVolume`
  reference: entering one set it, leaving that same one cleared it. Fine while volumes never
  overlapped. A sea with a current in it broke it at once: enter the sea, enter the current
  (now "the" volume is the current), drift out of the current → the reference is cleared and
  the body is in *nothing* — full gravity and no swimming, in the middle of the sea, until it
  happens to cross into the next water tile.
- **Rule:** membership is a set. `SoftVolumeSet` (`Scripts/Gameplay/World/SoftVolume.cs`), one
  per `CharacterController` and per `BaseFoe`, holds every volume the body overlaps and adds
  them up: carries are summed; among the media the **strictest** of each wins — lowest cap,
  highest drag and damping, most buoyancy, slowest sink — so kelp in the sea is never easier
  to cross than the sea. Both body types call `Resist`, `GravityScale`, `Flow`, `InMedium` on
  the set and nothing on an individual volume. It prunes volumes freed without an exit event
  (a map torn down under a body).
- **Why:** enter/exit events are per volume, but what a body feels is a property of where it
  is. Any "current X" field fed by enter/exit events is wrong the first time two X overlap —
  the same bug class as a status flag toggled by two sources.

### The arena ignored a Farview layer's Collision switch; ▶ Play honoured it (v0.10.9, soft volumes)
- **Symptom (found while laying a sea on a Farview layer):** MapCreation.gdd §3 lets a
  non-looping Farview layer with Collision on build real SoftVolumes at its tiles' cells. The
  editor Playtest had done so since the policy was written. `CombatMapRuntime` — what a run
  uses — read only the battlefield, so a cloud or a sea authored that way could be entered
  under ▶ Play and was pure scenery in a real fight. Nobody had shipped a map that used it.
- **Rule:** each builder also carried its own copy of "instantiate the SoftVolume scene and
  apply the tile's tuning"; both now call `MapCollision.CreateSoftVolume`, so a new tuning
  field cannot reach one builder and miss the other, and the arena builds Farview volumes the
  way the Playtest does. `MapContentCheck.CheckSoftVolumeParity` builds one map both ways
  (a sea on a Farview layer, a flipped current, bubbles, a looping layer and a non-colliding
  layer that must *not* build) and compares every volume's position and tuning.
- **Why:** third instance of the v0.10.6 caveat below ("two builders for one map drift"). A
  difference between ▶ Play and a real run is invisible to the author of the map, who only
  ever presses ▶ Play. Anything built per tile needs one function and a both-ways check.

### A sprite's canvas is not a window — and a contact sheet cannot show a sprite cut by its own edge (v0.10.9, realm art)
- **Symptom:** the first in-engine screenshots of the repainted Wulin showed bamboo leaves
  ending along a ruled vertical line, a peak with a flat-topped crown, pines sliced at the
  side, far bamboo whose feet hung in mid-air, and a pale rectangle under the far palace. The
  same in three other sets (two dead trees, the far castle's hill, a pixel tree's crown) —
  more than a dozen sprites. All had been reviewed on contact sheets, where each sprite sits
  alone in its own box and a cut at the box's edge looks like the edge of the box. A quarter
  of it was older than this change (v0.10.6's bamboo was already cut; nobody saw).
- **Rule:** nothing may be cut by the edge of its PNG unless it meets that edge on purpose
  (it tiles, it is a platform's walking line, it stands on the ground, it hangs, it stacks) —
  `RealmStyleBible.md` §2.7. In a painter: fold or shorten a mark that would leave the sheet
  (`leaf(keep=…)`, `pine(keep=…)`); let a growing limb **turn back**, never clamp its tip to
  the border (`np.clip(end, 6, W − 6)` lays the limb *along* the edge, which is the same cut);
  stand a pinned far silhouette on a **dome** that reaches zero at both ends, not a slab; fade
  what dissolves well inside the canvas. Then run `Tools/realm_art/audit_edges.py` and account
  for every line, and **look at the map in the engine** (`MapScreenshot.tscn`) before calling
  a set done.
- **Why:** a sprite is composited at an arbitrary place over arbitrary art; its canvas edge
  is visible exactly when paint reaches it. The review artefact has to be the map, not the
  sheet — the sheet hides the one defect that depends on what is next to the sprite.

### A soft volume's art is a promise about its physics — the sea's surface was drawn a row above the water (v0.10.9, realm maps)
- **Symptom:** in Loong's Nest the water volumes start on row 4, and the "sea surface" strip
  was a 3-row decoration placed at row 2 with its bright skin a third of the way down — so
  the visible surface sat about a cell above the level where a body starts to float. A
  fighter rising toward the surface popped into full gravity while still drawn under water.
- **Rule:** art that *shows where a volume is* is registered to that volume, in the tile
  itself: `deco.ln_surface` is now 16×5 with the skin on its fifth row, and its doc comment
  says to lay that row on the first row of water. Same for a waterfall's ribbon (as wide as
  the column that carries) and a bubble column. Check it in an in-engine shot taken where the
  boundary is on screen. The one licensed exception is a *hidden* current, drawn faint on
  purpose (`RealmStyleBible.md` §2.8).
- **Why:** decoration and collision are separate tiles on separate layers; nothing in the
  data ties them together, so only a convention carried by the tile's own shape can.

### Measuring physics in a test: enter the physics loop first, and speed time up by ticks, not by delta (v0.10.9, reference)
- **Symptom:** the probe compared a body falling in open air with one falling in a waterfall,
  tick for tick, and reported the waterfall as 15.07 m/s — one tick of gravity (34 px/s)
  more than its 14. The first stage was started from `_Ready`, between frames; every later
  one from inside a `physics_frame` callback, where a body added to the tree already moves in
  *that* tick. The first stage ran one tick behind all the others.
- **Rule:** in an async test that steps physics, `await ToSignal(GetTree(),
  SceneTree.SignalName.PhysicsFrame)` once before the first stage, so every stage starts at
  the same point of a tick. Press input with `Input.ActionPress` right after such an await —
  the body's `_PhysicsProcess` in the same tick then sees `IsActionJustPressed`. Measure
  velocity from positions, not from `Velocity`. To run fast, set
  `Engine.PhysicsTicksPerSecond = 60 × N` **and** `Engine.TimeScale = N`: each tick still
  receives `delta = 1/60` (Godot passes `TimeScale / ticksPerSecond`), so the simulation is
  bit-for-bit the normal one, N times sooner. Restore both afterwards. It all runs headless.
- **Why:** nodes added during the `physics_frame` signal are in the tree before that tick's
  `_PhysicsProcess` pass; nodes added from `_Ready` wait for the next one. An off-by-one tick
  is 34 px/s of gravity — larger than most tolerances worth having.

### Far layers again: two things that overlap need two layers, and a pin left of the spawn's screen is off the grid (v0.10.9, realm maps, reference)
- **Symptom:** composing the Wulin scroll, a village placed in front of the hero peak was
  refused ("cell occupied") although both are far decorations; and light shafts pinned to
  start-screen column 0 were refused as off-grid on a map whose spawn is near the west wall.
- **Rule:** far layers obey one-tile-per-cell like any other, and far tiles are large, so
  anything meant to be seen *in front of* another far thing gets its own layer with the same
  parallax (Wulin has "The Peak" and "Valley" at 0.14/0.17). And `layer.pin(tile, col, row)`
  converts a start-screen column to a layer cell through the spawn anchor: with the spawn a
  few cells from the wall, screen columns left of about 2.5 map to negative cells. Keep the
  spawn 9+ cells from the edge, or pin further right.
- **Why:** same arithmetic as the v0.10.6 far-layer caveat — a far layer's cells are not
  where they appear, even on the first frame.

### Small realm-art traps (v0.10.9, reference)
- **A sprite whose alpha never passes 40/255 is "fully transparent"** to the used-rect trim
  and to `TileSetAudit` (the light shafts were first painted at alpha 0.12). Soft, faint
  sprites still need to cross that somewhere — 0.30 at their core is enough.
- **No calligraphic Chinese font ships with macOS** (Heiti, Songti, PingFang, Hiragino only),
  so an inscription set in a font looks typeset. `Tools/realm_art/wulin.py` defines the few
  glyphs it needs (山 水 竹) as brush strokes in `GLYPHS` and paints them with `write()`.
- **A black cut-out needs a sky lighter than black.** The scratchboard realm's board is
  `#070A14`; its canvas is `#111B3D`, or every silhouette vanishes into the background.
- **A "moon as unpainted paper" is transparent** — put it where bare paper is behind it. Over
  a far waterfall it showed the falling water through the disc.

### Where a placed tile sits is one function; "cell × cell size" in a builder is a second opinion (v0.10.8, sub-cell nudge)
- **Symptom (caught while adding the nudge, before it shipped):** tiles gained a quarter-cell
  offset (`PlacedTile.SubX/SubY`, GDD §2.3). The *art* already had one builder
  (`MapVisualBuilder`), but the collider origin was `tile.X * PixelsPerCell` in **two** places
  — `CombatMapRuntime` and the Playtest's own physics pass — hazards had a third, and the
  marker point (`(X + 0.5) * cell, (Y + 1) * cell − 1 m`) was typed out in the arena, the
  Playtest and the map-card renderer, plus two Python tools. Miss any one and a nudged bench
  is drawn in one place and stood on in another, in one view only.
- **Rule:** nobody multiplies a cell by the cell size to place a tile. Ask
  `TilePlacement.Origin` (footprint corner) or `.Marker` (a marker's point); the Python
  mirrors are `render_map_preview.nudge` / `grid_locked`. Terrain (ground, autotiled fills,
  rule zones) is grid-locked there, so no caller needs its own "is this nudgeable" test.
  `MapContentCheck.CheckNudgedPlacement` builds a fully nudged map through the arena AND the
  Playtest and compares art, colliders, hazards and markers — keep it passing, and add a
  probe when a new category gains geometry.
- **Why:** v0.10.6 unified *what* a tile looks like (`TileVisualResolver`) and *how* a map is
  built; *where* a tile is was still copy-pasted arithmetic, because without an offset every
  copy gave the same answer. A new per-tile field is exactly when copies diverge. The checks
  were mutation-tested: breaking each site in turn fails the content check by name.

### Copy a placed tile with `TilePlacement.CopyAt`, never field by field (v0.10.8, sub-cell nudge)
- **Symptom (caught before it shipped):** the editor clipboard stored
  `(DefId, Dx, Dy, FlipX)` and paste rebuilt a tile from those four values. It had already
  been dropping per-tile `Props` without anyone noticing, and would have dropped the new nudge
  the same way — a pasted bench snapping back onto the grid.
- **Rule:** the clipboard keeps a detached copy of the tile (`TilePlacement.CopyAt`), and
  paste copies that. Anything that duplicates a `PlacedTile` goes through `CopyAt`; anything
  that repositions one edits it in place (`MoveCommand`, `NudgeCommand`). A new `PlacedTile`
  field then needs one line in `CopyAt` and one in `MapJson.CompareDoc`, nowhere else.
- **Why:** a list of "the fields worth copying" is a second schema, and it only ever shrinks
  relative to the real one.

### A fresh worktree has no import cache — and `$?` after a pipe is the pipe's (v0.10.8, reference)
- **Symptom:** in a brand-new session worktree the headless content check printed 124
  `SpriteSlot does not load as a texture` lines for art that is in the repository, and the
  wrapper line still said `exit=0`.
- **Rule:** `.godot/` (the import cache) is per checkout and git-ignored, so the first thing
  to run in a new worktree is `Godot --headless --path . --import` (about half a minute),
  then `dotnet build`, then the content check — not "only after repainting art". And read
  the check's own exit status: `Godot … > log 2>&1; echo $?`. `Godot … | tail; echo $?`
  reports `tail`'s, which is always 0.
- **Why:** a missing cache looks exactly like 124 broken assets, and a green exit code from
  the wrong process is worse than no exit code.

### Driving the map editor from a script: what reaches it and what does not (v0.10.8, reference)
- Extends the v0.10.7 note below on out-of-project `SceneTree` scripts and scratch `HOME`.
- GDScript cannot set a C# static (`EditorLaunch.MapId`) or call `MapPlaytestController`'s
  C# API. Enter the editor the way a user does: load `MapBrowser.tscn` under the scratch
  `HOME`, find the card's **Open** button in the tree and emit `pressed`.
- Mouse: `root.push_input(event, true)` takes viewport coordinates directly (no hi-dpi or
  stretch arithmetic). With the canvas at its opening pan and zoom, world pixels are screen
  pixels, so cell (x, y) is clicked at `((x + 0.5) * 64, (y + 0.5) * 64)` — keep test tiles
  clear of the panels (roughly cells 3–12 across, 1–6 down at 1152×648).
- Keys: `Input.parse_input_event`, not `push_input` — the Playtest polls
  `Input.IsPhysicalKeyPressed(Escape)`, which only the Input singleton feeds. `Cmd` is
  `meta_pressed` on macOS for the `command_or_control_autoremap` actions.
- Read results from what the user sees and what is saved: status-bar labels and the map file.
  Match labels by an exact prefix (`"Nudge:"`) — a map *named* "Nudge E2E" answers to
  `"Nudge"` first. Launch with `--debug-collisions` to see colliders in the Playtest and in
  `MapScreenshot.tscn`; the latter only reads `res://Maps`, so lend it a copy and remove it.

### A commit in a session worktree is not in the checkout the game is run from (v0.10.7, "the new maps weren't loaded")
- **Symptom:** v0.10.6 — five realm tile sets and maps — was committed on a session branch in
  its own worktree under `.claude/worktrees/`. The game is launched from the main checkout,
  on another branch, which never received that commit: the developer saw only their old maps
  and no realm selector, and the next session spent its first half-hour finding the work.
- **Rule:** a hand-off for work done in a worktree states (a) the branch and the worktree
  folder it is on, (b) that the main checkout does **not** have it, and (c) how to see it:
  open that worktree's folder as the Godot project, or merge the branch into the branch that
  is actually run. Before debugging "my change is not in the game", run `git worktree list`
  and compare `VERSION` in the folder being launched — the version label top-left in game
  says which build is on screen.
- **Why:** a worktree is a different folder with its own `res://`; "committed" answers "is it
  saved", not "is it where the game starts from". `user://` (saved maps, save slots) is the
  opposite: one folder shared by every checkout, so a test run from any worktree reads and
  writes the developer's real maps.

### A run must never be dealt a map its author has not finished (v0.10.7, combat-map selection)
- **Symptom:** fights opened on empty or half-built maps, and in the new realms the realm map
  was rarely the one chosen. `CombatMapCatalog` treated every readable file in `user://maps`
  as a candidate, and a new map answers to every world and every level (goal `claim`,
  terrain `sea-level`) until its author narrows it — so each abandoned draft competed for
  every sea-level Collection node in every world, usually outnumbering the real map.
- **Rule:** selection only deals maps that pass `MapReadiness` — the same test behind the
  browser's "Unfinished" badge and the content check. An unfinished map is skipped *without*
  claiming its id, so a half-edited copy of a built-in map falls back to the built-in. Any
  new reader of map files asks `MapReadiness`; nobody invents a second notion of "valid".
  `CombatMapCatalog.SelfTest` pins this against scratch folders.
- **Why:** "everything in the folder" is the editor's view of the store, not the game's. The
  defaults that make a new map easy to try (it answers to everything) are exactly what made
  it eligible for everything.

### One default narrowed five maps to a third of their nodes; a dialog that cannot show a value destroys it (v0.10.7)
- **Symptom:** the realm maps were written with the map kit's default terrain, `sea-level` —
  the editor's own default. Nodes get low / sea-level / high ground from altitude and
  selection wants an exact match, so two combat nodes in three in those worlds opened the
  legacy arena: "the map did not load". Separately, the editor's Map info dialog had no entry
  for `*`: such a map opened showing "Sea-level", and Apply wrote that back.
- **Rule:** a realm with one map tags it `*` (the map kit's default now); give maps exact
  bands only when a realm has one per band. `MapContentCheck` walks every kind of combat node
  per world and fails one that no shipped map serves. A dialog must be able to display every
  value its field can hold — otherwise opening it and pressing Apply is a silent edit.
- **Why:** selection filters are conjunctive, so each "sensible default" multiplies down what
  a map can serve, and the fallback is silent and looks like a loading failure.

### A browser action must not write over a file that is already there (v0.10.7, Edit Copy)
- **Symptom (caught before it shipped):** once an unfinished copy of a built-in map leaves
  the built-in card on the wall, that card's Edit Copy meets an existing `user://maps/<id>.json`
  — and its first version saved the built-in map straight over it.
- **Rule:** Edit Copy opens a readable copy that already exists, finished or not. An
  unreadable file is moved to `maps/_archive/` first, and only after asking. Clean up also
  only ever moves. Same family as "an unreadable save slot is not an empty save slot" (v0.9.0).
- **Why:** when one id can have two files (shipped and local), "create the local one" and
  "the local one exists" are different states that the same button reaches.

### A map card: words over the sky, a picture framed on what is built, and a height that cannot chase the scrollbar (v0.10.7, map browser)
- **Symptom:** the first card design pictured the whole grid with the name block over the
  bottom. A sparse beach map came out as a card of empty sky with its floor, towers and palms
  hidden under the text. Then, on a wide window, `KeepAspectCovered` cropped the floor off.
- **Rule:** `MapThumbnailFraming` chooses the frame from the battlefield's structure (the band
  of rows holding most of it, standing on the picture's bottom edge); the name block sits
  over the top, where a side-view map keeps its sky; and `MapBrowser.FitCards` gives a card
  the height that keeps its picture's shape. That height is derived from the scroll area's
  width with the scrollbar's width always set aside — never from the grid's own width.
- **Why:** a side-view arena's content is at the bottom, so "fit the grid" and "caption at the
  bottom" each hide it. And any height computed from a width inside a `ScrollContainer` feeds
  back: taller cards summon the scrollbar, which narrows them, which dismisses it.

### A negative `line_spacing` on a wrapping, line-limited Label hides its last line (v0.10.7, map browser)
- **Symptom:** card names use `AutowrapMode` with `MaxLinesVisible = 2`. With a
  `line_spacing = -1` theme override to tighten them, every two-line name drew its first line
  ending in an ellipsis and left the second line blank; the label was still two lines tall.
- **Rule:** no negative `line_spacing` on an autowrapped Label that limits or trims its lines.
  To tighten lines, change the font size or the font's own spacing instead.
- **Why:** the Label decides how many lines fit by adding `line height + line_spacing` per
  line and comparing against its height; with a negative spacing the last line misses by a
  pixel, so it believes one line fits — and trims that one.

### Testing anything that writes to `user://`: use a scratch `HOME`, and mind hand-made JSON (v0.10.7, reference)
- `user://maps` holds the developer's real maps and is shared by every checkout. On macOS
  Godot derives `user://` from `$HOME` (`XDG_DATA_HOME` and `CFFIXED_USER_HOME` are ignored),
  so `HOME=/scratch Godot --path . …` or `open … --env HOME=/scratch --args …` runs against an
  empty save folder; copy the real `maps/` folder in to rehearse on real data
  (`Docs/Instructions/40-QA.md`). Confirm with `OS.get_user_data_dir()` before pressing
  Create, Edit Copy, Clean up or Delete in a test.
- A UI can be driven and screenshotted without adding files to the project: run a GDScript
  `SceneTree` script that lives outside it (`Godot --path . -s /abs/script.gd -- args`);
  autoloads and the C# assembly load as usual.
- GDScript's `JSON.stringify` writes whole numbers as `60.0`, and `System.Text.Json` refuses
  that for an `int` property — a map file rewritten from GDScript loads as "not a map". Edit
  test map files with Python (or as text), not by parse-and-stringify in GDScript.

### A doc can say a renderer is wired when the code is not — and "the editor shows it" is not "the game shows it" (v0.10.6, realm tile sets)
- **Symptom:** `BeachTileSet.md`, `MapCreation.gdd` and `HillAutotile.cs`'s own header all said
  the layered-hill atlas was "WIRED" into `GridView` through `Props["autotileKind"]`; the
  shipped `GridView` only ever called the older two-state `AutotileAtlas`, so the dispatch was
  dead (lost between machines, with the docs kept). Separately, *nothing* outside the editor
  canvas consulted any atlas: the Playtest and the arena each had a private `CreateTileVisual`
  that stretched `SpriteSlot` over the cell, so built-up terrain would have drawn in play as
  one repeated swatch with no surface, sides or underside.
- **Rule:** which art a tile shows is decided by one pure function, `TileVisualResolver`, and
  every view — editor canvas, Playtest, arena — calls it. Any intended difference between views
  is a row in that class's table, not a second implementation. When a doc claims a code path
  exists, grep for the call before building on it.
- **Why:** three copies of "draw a tile" drift silently because each still draws *something*.
  One resolver makes "what the editor shows", "what Play shows" and "what a run shows" the
  same question, and a `SelfTest` can pin every branch.

### Two builders for one map drift: real runs had no parallax, and the foreground drew behind the fighters (v0.10.6, realm tile sets)
- **Symptom:** the editor Playtest and the combat arena built a map with separate, nearly
  identical code. Only the Playtest's had a parallax layer class, so in a real run every far
  layer sat rigidly behind the battlefield at its authored cells. In both, all map layers were
  parented under the world node while characters were added elsewhere, so a Closeview layer —
  documented as the foreground you hide behind — drew *behind* the player.
- **Rule:** anything that plays a map builds it from the shared parts in
  `Scripts/MapCreation/Runtime/` (`MapVisualBuilder`, `MapParallaxLayer`, `MapCollision`,
  `MapHazardFactory`). Closeview layers are lifted over actors with `CloseviewZIndex`; parallax
  layers read the viewport's current camera each frame instead of holding one, so a body swap
  needs no rebinding. A fix made in a builder's private copy is a bug waiting in the other.
- **Why:** "the Play button looked right" was the only test either path ever got, and it only
  exercised one of them. Draw order that depends on which node happens to be added later is
  not a layer order.

### The arena's backdrop rect is the legacy arena's size, not the map's (v0.10.6, realm tile sets)
- **Symptom:** `Arena.tscn`'s `Background` ColorRect spans x −400…2400, y −400…1000. An
  authored map takes the canvas colour from it, but a map wider than ~37 cells or taller than
  ~15 ran past the rect: beyond it the screen showed the engine's clear colour.
- **Rule:** when a scene node is reused as an authored map's canvas, size it from the map
  (`GameManager.TryBuildSelectedMap` now stretches it over the battlefield plus a margin), the
  same way camera limits are overwritten from the map rather than trusted from the scene.
- **Why:** scene-authored extents are defaults for the scene's own content. Every one of them
  — camera limits (v0.6.17), now the backdrop — has to be re-derived when a map replaces that
  content, and a small test map never shows the ones that were missed.

### Touching hazard tiles each tick a body; a hazard painted as an area must be one hazard (v0.10.6, realm tile sets)
- **Symptom:** every hazard tile became its own `Hazard` Area2D with its own per-body timer. A
  pool painted from 1×1 lava tiles would tick a fighter once per tile they overlapped — double
  damage for standing across a grid line, with no visible reason.
- **Rule:** a hazard kind that is painted as a contiguous area declares
  `Props["mergeAdjacent"] = "true"`; `MapHazardFactory.Plan` fuses touching tiles of that kind
  into one hazard with many shapes. Hazards also name their runtime scene in `Props["scene"]` —
  the builders no longer `switch` on tile ids, so a new hazard is a registry entry plus a scene.
- **Why:** the grid is an authoring unit, not a gameplay unit (GDD §2.4). Anything whose effect
  depends on where the cell boundaries fall is leaking the grid into combat.

### A prop sprite's canvas and its footprint must agree — pin the canvas, or the editor and the game place it differently (v0.10.6, realm tile sets)
- **Symptom:** the editor trims a prop to its used rect, aspect-fits that into the footprint
  and anchors it bottom-centre; the Playtest and arena fitted the *whole texture* instead. Any
  sprite with transparent padding therefore sat smaller and higher in play than in the editor
  (a tower floating above its floor), and a platform whose paint stopped short of its canvas
  top left the player hovering above it.
- **Rule:** play now uses the same used-rect fit as the editor (`MapVisualBuilder`). Painted
  realm sprites go further: the canvas *is* the footprint (`footprint × 128 px`) with the four
  corner pixels at alpha 1/255 so the used rect is the whole canvas, and platforms put their
  walking line on the top row. `TileSetAudit` fails a painted sprite whose canvas is not its
  footprint's shape, and `save_sprite(walk_top=…)` refuses a platform painted too low.
- **Why:** "fit whatever bounds the art happens to have" makes placement a property of the
  export, not of the design. A pinned canvas makes what you paint in footprint coordinates
  exactly what appears, in every view, and lets multi-cell pieces register against each other.

### Far layers: anchored at the spawn, not the opening camera; they sink as you climb; never mirror a loop (v0.10.6, realm maps)
- **Symptom:** first previews of the realm maps showed (a) sky pieces a couple of rows away
  from where they were authored on the very first frame, (b) a dark band under the horizon and
  under the dunes once the camera rose, (c) "butterfly" twins in every looping range, (d) a
  hairline seam every 16 cells in one looping range.
- **Rule:** the layer offset is `(camera − anchor) × (1 − parallax)` and the anchor is the
  **spawn marker point** — the opening camera is clamped inside the map, so the two differ and
  a far layer is displaced from frame one. Compose far layers in start-screen rows
  (`mapkit.Layer.srow` / `pin` / `seen_from`). A far layer moves *down* the screen by
  `parallaxY ×` the camera's climb, so land-like far sprites carry flat colour to the bottom of
  their canvas. A looping layer takes one sprite per 16-cell period — never a sprite beside its
  own flip. Anything that shifts a tiling sprite's paint (a print's off-register nudge) must
  wrap around in x, not leave a bare strip. Keep the spawn ≥ 9 cells from the left edge when a
  screen-pinned layer needs its left side (authored columns cannot be negative).
- **Why:** parallax is authored in a space that is not the screen and not the map. Until the
  arithmetic lives in one helper, every far layer is placed by trial and error — and far
  layers also obey one-tile-per-cell, so at parallax *p* a tile *W* wide uses *W/p* cells of
  travel and only two or three silhouettes fit per layer.

### There is no kill plane: an authored map must be sealed (v0.10.6, realm maps)
- **Symptom (found by reading, before it shipped):** nothing ends a fall. A character that
  leaves the bottom of an authored battlefield, or walks off its side above the wall, falls or
  runs forever outside the camera limits.
- **Rule:** every battlefield has solid floor edge to edge, solid walls to the top row on both
  sides, and solid rock under any pool of a fill hazard (lava is a hazard, not ground). The map
  kit cannot check this; the map author must.
- **Why:** the legacy arena was a closed box, so the question never arose. Open maps make it a
  content rule until the game has an out-of-bounds rule of its own.

### A registry that throws in its static constructor takes every map with it (v0.10.6, realm tile sets)
- **Symptom (avoided):** `TileRegistry` built its id lookup with `ToDictionary`, which throws on
  a duplicate id — inside a static constructor, i.e. a `TypeInitializationException` the first
  time *anything* touches a tile, in the middle of a run.
- **Rule:** registries built at type-init time must not throw on bad content: first
  registration wins, the duplicate is recorded, and `Validate()` reports it at boot (T00 rule
  4: fail loudly in debug, degrade gracefully in release). With one file per realm, two files
  minting the same id is now a realistic slip.
- **Why:** a static-constructor failure is permanent for the process and surfaces far from its
  cause. Content errors belong in the validation report, not in the type loader.

### Boss placement must come from the selected authored arena (v0.10.4, Seashore V fix)
- **Symptom:** the LV4 boss used the fixed legacy-Arena centre, placing it in the wrong part of a
  larger authored battlefield such as the 60×40 Crownwave Colosseum.
- **Rule:** a mission entity whose stage matters must request an authored marker through
  `GameManager`; only the no-map path may use legacy Arena placement fallback.
- **Why:** combat-map dimensions and origin are content data. Hard-coded scene coordinates silently
  couple a mission to one arena and make every other valid arena layout unreliable.

### Literal log brackets must not pass through the BBCode parser (v0.10.4, debug-log viewer fix)
- **Symptom:** key 5 opened a viewer with the correct line count but no readable entries, because each entry contained bracketed timestamps and categories such as `[12:34:56] [SYSTEM]`.
- **Rule:** render diagnostic text with `RichTextLabel.AddText` plus API-applied colors, or escape every literal bracket; do not feed structured log lines directly into BBCode.
- **Why:** bracketed identifiers are ordinary log data, but BBCode interprets them as formatting tags and can consume the visible text.

### Prototype roster upgrades must reset the corresponding active-team order (v0.10.4, five-protagonist debug fix)
- **Symptom:** adding a newly implemented protagonist to fresh-run defaults left existing save slots on their previous three-body build, so Team Build did not show the requested starting party.
- **Rule:** when a save gains missing prototype roster entries, apply the declared prototype default team in the same load transaction and reset its active index; later loads preserve the player's saved Team Build choices.
- **Why:** roster membership and team order are separate durable facts. Updating only ownership makes a migration look successful while the battle snapshot still begins with the old party.

### A universal combat countdown needs a gate, not an ambient-spawner flag (v0.10.4, opening countdown fix)
- **Symptom:** the Arena's three-second grace disabled only ambient spawns and its countdown
  text was overwritten by the normal mission HUD push; Slaughter waves and direct boss setup
  could therefore appear immediately or obscure the countdown.
- **Rule:** put the opening policy in `GameManager` and expose one explicit spawn gate in
  `FoeSpawner`. Queue mission waves behind it, defer direct boss construction to the first
  mission tick, and keep mission HUD from writing over the countdown.
- **Why:** a shared lifecycle rule must govern every spawn path. Reusing a mission-specific
  ambient flag creates bypasses and silently changes Slaughter/Boss ownership semantics.

### Debug roster selection must relinquish GUI focus even for the already-active body (v0.10.4, BA input fix)
- **Symptom:** choosing Pixolotl from the debug roster could leave its overlay/focused button
  active when Pixolotl was already the current body, so the next mouse press was consumed by UI
  instead of acting as a basic attack.
- **Rule:** selecting any roster entry while in an Arena closes the roster and releases button
  focus, regardless of whether a body replacement was required.
- **Why:** a selection can be meaningful without causing a swap. Input ownership must still
  return to the combat scene in both outcomes.

### A control lock does not freeze physics by itself (v0.10.3, Pixolotl Shift fix)
- **Symptom:** Shift rejected player input, yet gravity and stored momentum still advanced in
  `CharacterController._PhysicsProcess`, so Pixolotl drifted while a rewind was meant to hold
  her exactly at a frame.
- **Rule:** a skill that requires a fixed world position must explicitly opt into the shared
  `LocksPhysicalMotion` hook. Freeze the physics output while preserving any kit-owned velocity
  snapshot needed when the lock resolves.
- **Why:** input permission and physics integration are separate systems; stopping one cannot
  guarantee the other has stopped moving the body.

### A debug body swap must not inherit a different character's magazine (v0.10.3, BA readiness fix)
- **Symptom:** manual debug selection could leave the newly spawned protagonist unable to attack
  until the outgoing body’s BA interval or reload expired.
- **Rule:** carry only the intentional debug state (currently HP ratio) through a debug swap;
  reset the replacement body's transient combat state without saving that test convenience into
  the run record.
- **Why:** ammo definitions and timers are character-local. Copying them across body identities
  turns a debug convenience into a misleading combat lockout.

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
- **Projectiles:** call `Init(...)` **after** `AddChild`; find targets with
  `CombatTargets.HostileTo(GetTree(), side)` → `ICombatTarget.TakeHit(new HitInfo(dmg, knockback), origin)`
  (v0.10.11 — never cast to `BaseFoe`: a boss is not one); hit tests subtract the target's
  `HitRadius`; projectile scene uses `collision_layer = 16` (layer 5), `collision_mask = 14`
  (Foe+Ground+Platform), and `CombatTargets.AimMask` swaps Foe for Player when a boss threw it.
- **Input:** read `Controls`, not `Input` or the mouse (v0.10.11) — see Conventions.
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

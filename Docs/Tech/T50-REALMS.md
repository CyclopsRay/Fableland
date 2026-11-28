# T50 — Realms: the engine contract

> **Status: built through v0.12.21.** The core (v0.12.20): `Scripts/Data/RealmTable.cs`,
> `Scripts/Gameplay/Realms/` (`Realm`, `RealmInstance`, `BodyPlacement`), and the filter at the
> places that ignore distance. Then (v0.12.21): the arena's spaces built from map documents
> before the fight (`Scripts/Orchestration/Arena/ArenaRealmSpaces.cs`, §8), the view and the
> window (`Scripts/Presentation/Realms/RealmView.cs`, §6), and the Black Prison on it (§10).
> Checked by `RealmCheck` (36), the boss check's prison probes and the run check. **Still to
> build:** the Elsewhere sound (§7), the cues (§9), lenses (§6), item and weather sources (§8).
> The game rules are `Docs/Realms.gdd`; this is how the engine keeps them. Read T00 (the
> dependency law) and T10 §4a–§4b (sides, targets, cues) first.
>
> **Names as built:** the designed `Realms` service is the static `Realm` (`Open`, `Of`,
> `SameAs`, `HostileTo`, `WindowZ`). Membership is `Realm.Of(body)`, by position, not a field
> on `ICombatTarget` (§4). The projection lives on the instance (`ToSub`, `ToMain`). There is no
> `RealmSourceDef` row yet: `RealmTable.SourcesOf(ownerId)` maps an owner (a protagonist's id,
> a boss's id) to the realms it can open.

## 1. Where it sits

| Layer (T00) | What of realms lives there |
|---|---|
| **Domain data** | `RealmTable`: `RealmDef` rows (what a sub-realm is) and `RealmSourceDef` rows (who may open which). Pure C#. |
| **Gameplay** | Membership (`ICombatTarget.Realm`), the projection math (`RealmProjection`, pure), the `Realms` service kits call (`Open`, `Of`, `Recast…`), going in and coming out (they move bodies, as hit-stop does), and the realm filter in `CombatTargets`. Gameplay never builds a map: it asks an `IRealmSpaces` provider for a prepared space. |
| **Orchestration** | The arena builds the preload roster, builds each definition's space, registers the provider, and owns per-space bounds and the mission clock. |
| **Presentation** | Views (camera limits, backdrops, the window and its lens) and sound (the Elsewhere bus), all listening to `realm.*` cues. |

A kit opens a realm by asking `Realms.Open(defId, mainAnchor, side, caster)`. It never names the
arena (the rule that kept the prison's art "over the fight" until now, `PumpKing.gdd` §Q).

## 2. Data

**`RealmDef`**

| Field | Meaning |
|---|---|
| `Id` | e.g. `realm.kh.black_prison` |
| `Kind` | `Map` (a map document with a `realm` role) or `Patch` (a small map made for it) |
| `MapPath` | the document its space is built from (map editor, any world's tile kit) |
| `Ratio` | sub metres per main metre |
| `ExitBorder` | shape and size in sub metres (circle radius; a rectangle for waves) |
| `ArtExtent` | how far ground and art go, at least the exit border — so the border cannot be seen |
| `PullMode` | `OnOpen` or `Continuous` |
| `RideSeconds` | 0, or how long a rider stays (the tsunami) |
| `Rules` | statuses laid inside, with whom (`silence: caster's enemies`) |
| `Actors` | creature or hazard spawners that live in the space (the pumpkins) |
| `LawsWorld` | a world whose `RealmLawTable` row applies inside (gravity, swimming) |
| `Audio`, `Lens`, `WindowStyle` | presentation rows |

**`RealmSourceDef`**: owner kind (`Kit`, `Boss`, `Item`, `Weather`), owner id, `RealmDef` id,
`MaxInstances` (how many may be open at once from this source).

## 3. Spaces — how a sub-realm exists

Three ways were weighed:

| | How | For | Against |
|---|---|---|---|
| **A. Far-off spaces in the one physics world** ✔ | Each instance's space is built at its own origin far from the battlefield (about 40 000 px apart; float precision is fine there). | Bodies inside are physically elsewhere. They touch only that space's ground; distance-based hits and AI senses already ignore other realms; the player's camera just follows them; no collision-layer bookkeeping. | Camera limits, fall and kill planes, out-of-bounds checks and global effects must be per space. |
| B. Same coordinates, collision-layer bands | Realm ground on reserved layers; bodies swap masks. | Positions stay literal. | Most hit code measures distance, so every check would see across realms; rendering would have to cull at the same spot. |
| C. Separate physics worlds | A `SubViewport` with its own `World2D` per realm. | Strongest isolation. | Bodies must be re-parented to move (deferred, fragile); every view is a viewport. |

**A is the design.** The owner's "another layer" is a space far away, joined to the battlefield
only by the projection.

## 4. Membership and the target filter

- **A body's realm is where it stands (as built, v0.12.20):** inside an open instance's space
  bounds, that realm; anywhere else, the main realm — `Realm.Of(body)`. Spaces never overlap,
  so position is the one owner of the fact (T00 rule 2); the field on `ICombatTarget` first
  designed here would have been a second record to fall out of step.
- **Two lookups, by what they measure.** Kit hits measure distance and keep using
  `CombatTargets.HostileTo(tree, side)` unchanged: other realms are thousands of pixels away.
  What ignores distance asks `Realm.HostileTo(asker, side)` / `Realm.SameAs(a, b)`: a foe's
  player lookup (`BaseFoe`), a boss's senses (`BossSenses`), zone-6 targeting
  (`Zone6BossFighter.TargetBody`), THE VOID's darkness (cast in its holder's realm).
- Projectiles, hazards and realm creatures record the realm they were made in. Under option A
  they are physically there as well.
- **AI:** a target must share the foe's realm. When the player leaves, foes in the main realm
  lose them at once.
- **Global walkers** (THE VOID's darkness, zone-6 seals, realm laws, Māra's wheel) must state
  their realm scope. The default is the source's own realm.
- **Never pulled in:** mission objectives, pickups, wonder cores, props — the `IRealmImmune`
  marker (on `DestroyObjective` since v0.12.20).

## 5. Going in and coming out

**Enter(body, instance)**
1. `p = subAnchor + ratio × (p_main − mainAnchor)`. If `p` is on or past the exit border,
   nudge it inward by 0.5 m.
2. If `p` is inside ground, use the nearest free spot (generalise `PumpKing.FreeSpotNear`).
3. Move the body. Velocity is unchanged. Set `Realm`, apply the definition's rules, and raise
   `realm.enter`.

**Exit(body, reason)** — the reason is `Border`, `Close` or `Timer`.
1. `p = mainAnchor(now) + (p_sub − subAnchor) ÷ ratio`, using the *current* main anchor (a
   wormhole). Out over the border or at a ride's end, nudge it **sideways** to just past the
   region, keeping its height (a side view: never pushed into the floor or into the air).
2. Use the nearest free spot; clamp to the main map's bounds.
3. Move the body, keep its velocity, clear the rules, set `Realm = 0`, start a **0.5 s
   re-entry grace** for this instance, and raise `realm.exit`.

**Close:** exit everyone (`Close`), then free the instance's creatures and projectiles, and
raise `realm.close`.

**Order inside a physics tick, one list in one place (T00 rule 3):** pull-in checks
(continuous realms; graced bodies skipped), exit-border checks, ride timers, the anchor's
motion, then actors. *As built* (`RealmInstance._PhysicsProcess`): the graces count down;
each member is checked in turn — gone from the tree, it is forgotten; found outside the space
(a respawn or teleport moved it), it is let go where it is; its ride over, it comes out; past
the border, it comes out. Then bodies standing in the space that are not members are adopted
(a protagonist switched in by Tab where the one before stood), and last comes the continuous
pull.

**Two more ways in:** `Admit(body, spot)` brings a body in at a chosen spot, or moves a member
there (PumpKing's recast blinks him into his prison's mouth, from inside or outside). It is
refused for a body in another realm. **Adoption**, above, covers a body that turns up inside
the space.

**Refusals in v1:** a body already in a sub-realm is never pulled into another. `Realms.Open`
from inside a sub-realm is refused.

## 6. Views

*As built (v0.12.21, `RealmView`, presentation; gameplay never asks it anything):*
- The local player's camera follows their body. When `Realm.Of(LocalPlayer)` changes, the
  camera's limits become the space's art (`RealmSpace.View`, the map document's rectangle, not
  the 6 m wider membership `Bounds`) or the battlefield's, and its smoothing is reset, so it does
  not glide across 40 000 px.
- **Backdrops are per space.** Each space carries its own world-space backdrop (a `ColorRect` in
  the document's canvas colour at z −200) and its own far layers (`MapParallaxLayer`, anchored at
  the sub anchor). Every layer's art is a `MapParallaxLayer`, the ground's too. Only layers that
  follow the camera (`FollowsCamera`) are switched: those of the space the player is in are shown,
  every other space's and the battlefield's hidden. Fixed layers, each map's ground art, stay on.
  Visibility toggles stand in for the cull masks first designed here; that is enough for one
  local player.
- **The window (`RealmWindow`):** one per open instance the player is not in. It is a
  `SubViewport` sharing the main `World2D` (transparent, updated every frame), sized to the
  region, with a camera on the sub anchor at zoom `1 / ratio`. A `Sprite2D` draws it at the main
  anchor through a circle-mask shader with a `lens` colour uniform, the hook for the palette
  lens (`Realms.gdd` §9, TBD #90). Its z is `Realm.WindowZ` (40): above the battlefield's art
  (close view 20, effects 30), under the opener's own rim (`BlackPrison.Z` = 41).
- **What the window shows (v0.12.21):** the space's fixed art (the ground, its trees, branches
  and fence), its backdrop colour, and everyone and everything in it. It does **not** show the
  space's far scenery (the moon, the far wood). Those layers place themselves for one camera, the
  player's, and are hidden while the player is outside. **Next:** a second set of a space's far
  layers bound to the window's camera (`MapParallaxLayer.SetCamera`) on a visibility layer only
  the window's viewport renders (`CanvasCullMask`), the cull-mask design above.
- **Multiplayer, later:** every player's viewport sets its own cull mask and windows.

## 7. Sound

Every cue carries its realm. The AudioDirector compares it with the listener's realm:
- **Same realm:** played as now.
- **Different realm:** routed to an **Elsewhere** bus with a low-pass filter, a long reverb, a
  slight detune and wow, and lower gain (`Realms.gdd` §9, dreamcore).

The main music bleeds through Elsewhere, and a definition may bring its own music or ambience.

## 8. Preloading and budgets

*As built (v0.12.21):* `GameManager.SetupRealms` runs after the HUD is set up, before the
countdown ends. Its owners are the battle party's ids (or the player's name) and the boss's id;
`RealmTable.SourcesOf` turns them into realm ids, and `ArenaRealmSpaces.Prepare` builds each:
up to two copies a definition, each from its map document (`CombatMapRuntime.Build`, the
arena's own builder) under its own root, 40 000 px apart and above the battlefield. A realm
nobody listed is built the first time it opens, with a warning (a hitch). Items and weather
are not owners yet. The Black Prison's patch is 176 tiles, under the 300 budget. A patch has
no actors of its own yet, so a prepared space is dormant by construction: static ground,
hidden far layers.

- **The roster, at arena build:** the battle party's kits, the boss's kit, every held and
  Possession item, and the map's weather table. These map to definitions, with one prepared
  space per possible simultaneous instance.
- **Built before the countdown ends,** then dormant: actors paused, nothing processing; ground
  may stay, since it is far away.
- **First-pass budget (to be measured):**
  - at most **2 full maps + 4 patches** per fight;
  - a patch of at most **300 tiles**;
  - opening or closing costs at most **1 ms** of main-thread work beyond moving bodies (T40).

## 9. Cues

To be added to `Feedback.gdd` §6: `realm.open`, `realm.enter`, `realm.exit`, `realm.close`. Each
carries the instance id, the definition id, the anchors and the ratio. `realm.exit` also
carries its reason.

## 10. What moves onto it

| Today | Becomes |
|---|---|
| The Black Prison (`BlackPrison`, `PrisonPumpkin`, `PrisonVeil`) | **Done, v0.12.21.** `realm.kh.black_prison`: a patch (`Maps/Realms/kh_black_prison.json`) with PumpKing's Q and BOSS PumpKing as its sources. The move and jump scales, the sight cap, `PrisonVeil` and the circle-membership test are gone. The warning, the pumpkins (grown in the patch by the prison, not yet as realm actors), the recast (through `Admit`) and the rim stay. |
| THE VOID's darkness | Scoped to the caster's realm |
| Māra's Wheel | Stays a whole-arena swap of laws and backdrop, not a sub-realm; its backdrop slot becomes the per-space backdrop |
| Realm laws (`RealmLawTable`) | A space may carry its world's row |
| The beach's tsunami hazard | A weather source with a moving region |

## 11. Checks

A new headless **`RealmCheck`** covers (*as built: 36 checks; the preload roster and the view are
proven by the boss check's prison probes and the run check's boss arenas*):
- projection round trips and ratio distances;
- the entry and exit nudges;
- a landing in ground moving to a free spot, and clamping at the map's edge;
- the re-entry grace;
- the filter (no hit, target or AI sight across realms);
- projectiles staying in their realm;
- objectives never pulled in;
- the caster pulled in;
- silence on enemies only;
- a close returning everyone;
- a moving-anchor exit;
- the preload roster;
- no nesting;
- adoption of a body that turns up inside, and `Admit` (in, at a spot; refused across realms).

Each rule is broken once (40-QA). The boss check's prison probes were rewritten for the
sub-realm (v0.12.21): the real patch, the camera and the window, the recast from outside.

## 12. Risks to audit before building

- **Code that assumes the arena is the map's rectangle:** camera limits, fall and kill planes,
  spawner bounds, hazards' `DespawnX`, foe leashes. Grep for `Limit`, `DespawnX`, bounds and
  fall death. *Audited v0.12.21:* the camera's limits are the view's (§6); there is no fall or
  kill plane in game code; the tsunami's `DespawnX` lives in the main realm; a Tab switch copies
  the old camera's limits, so a switch inside a realm keeps the space's. **Found and fixed:** a
  respawn (debug lives) moves a member out of the space by teleport. The instance now lets such
  a body go where it is instead of projecting it out of the border (KNOWLEDGE v0.12.21).
- **Walkers over every body:** each needs its realm scope (§4).
- **Saving mid-battle with a realm open:** realms are not saved. A resumed battle restarts its
  fight, as it does today.
- **Determinism:** each instance draws from its own seedable RNG.

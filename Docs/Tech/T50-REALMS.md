# T50 — Realms: the engine contract

> **Status: the core is built (v0.12.20)** — `Scripts/Data/RealmTable.cs`,
> `Scripts/Gameplay/Realms/` (`Realm`, `RealmInstance`, `BodyPlacement`), the filter at the
> places that ignore distance, `RealmCheck` (28). **Still to build:** the arena's spaces built
> from map documents and preloaded (§8), views and the window (§6), the Elsewhere sound (§7),
> cues (§9). The game rules are `Docs/Realms.gdd`; this is how the engine keeps them. Read T00
> (the dependency law) and T10 §4a–§4b (sides, targets, cues) first.

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
motion, then actors.

**Refusals in v1:** a body already in a sub-realm is never pulled into another. `Realms.Open`
from inside a sub-realm is refused.

## 6. Views

- The local player's camera follows their body. In a sub-space its limits become that space's
  `ArtExtent`.
- **Backdrops are per space.** Godot's visibility layers and the viewport's canvas cull mask
  show only the backdrop of the space the local player is in.
- **The window:** a `SubViewport` sharing the main `World2D`, with its camera on the sub anchor
  at zoom `1 / ratio`. Its picture is drawn into the region (clipped to its shape) in the main
  view, through the **lens shader**, a palette lookup (`Realms.gdd` §9). There is one per open
  instance on screen.
- **Multiplayer, later:** every player's viewport sets its own cull mask and windows.

## 7. Sound

Every cue carries its realm. The AudioDirector compares it with the listener's realm:
- **Same realm:** played as now.
- **Different realm:** routed to an **Elsewhere** bus with a low-pass filter, a long reverb, a
  slight detune and wow, and lower gain (`Realms.gdd` §9, dreamcore).

The main music bleeds through Elsewhere, and a definition may bring its own music or ambience.

## 8. Preloading and budgets

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
| The Black Prison (`BlackPrison`, `PrisonPumpkin`, `PrisonVeil`) | A patch definition plus PumpKing's Q as its source. The move and jump scales, the sight cap, the veil and the circle-membership test go. The warning, the pumpkins (now realm actors) and the recast stay. |
| THE VOID's darkness | Scoped to the caster's realm |
| Māra's Wheel | Stays a whole-arena swap of laws and backdrop, not a sub-realm; its backdrop slot becomes the per-space backdrop |
| Realm laws (`RealmLawTable`) | A space may carry its world's row |
| The beach's tsunami hazard | A weather source with a moving region |

## 11. Checks

A new headless **`RealmCheck`** covers:
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
- no nesting.

Each rule is broken once (40-QA). The boss check's prison probes are rewritten for the sub-realm.

## 12. Risks to audit before building

- **Code that assumes the arena is the map's rectangle:** camera limits, fall and kill planes,
  spawner bounds, hazards' `DespawnX`, foe leashes. Grep for `Limit`, `DespawnX`, bounds and
  fall death.
- **Walkers over every body:** each needs its realm scope (§4).
- **Saving mid-battle with a realm open:** realms are not saved. A resumed battle restarts its
  fight, as it does today.
- **Determinism:** each instance draws from its own seedable RNG.

# Fableland — Art, Animation & Audio Department

The rule of this department: **gameplay never waits for art, and art never blocks
gameplay.** Placeholders ship; the pipeline below upgrades them without touching logic.

---

## 1. Placeholder-first policy (current practice — keep it)

- Every entity gets a flat `.svg` placeholder in `Sprites/` sized to its **collision
  proportions** (caveat v0.2.1: sprite native size and CollisionShape2D are independent
  — when proportions change, update *both*, always).
- Player-class characters: 48×64 (3:4), 2 m tall at 32 px/m. Foes/props sized per GDD
  hit radius / BoxSize.
- Telegraphs (skill ranges, hazard boxes) are **drawn from the same data as the
  hitbox** (Hazard builds collision from `BoxSize`; `ShowDebugRanges` draws from skill
  params). Final VFX must keep this property — the pretty effect and the hittable
  shape must come from one number.

## 2. Animation pipeline (stubbed, not skipped)

The contract, already in place for Pomegraknight:
1. Each character scene carries an **empty `AnimationPlayer`**.
2. `CharacterController.UpdateAnimator()` is the single drive point (subclasses
   override; state decided from velocity/action flags).
3. Damage/spawn events are applied inline **with `// NOTE(animation)` markers** at the
   exact line that should later become an AnimationPlayer **call-method track** (e.g.
   Pixolotl's `OnBranquiasFire()` is designed as an animation event — keep that shape).
4. Upgrading a character to real animation = author the sprite frames, build the
   AnimationPlayer states, move marked calls onto call-method tracks, implement
   `UpdateAnimator`. Zero logic changes elsewhere. Timing data (clip durations,
   damage-event offsets — see Pomegraknight's per-stage table) comes from the GDD.

## 3. Sprite production (the art order form)

- The **frame list lives in each character GDD's image-prompt section** — e.g.
  Pomegraknight: Idle, Walk, Jump, Slash1-3, Blush, SeedErupt, FireTornado (9 frames).
  A kit is not animatable until its GDD enumerates frames; chase design for it.
- Style lock: **chibi pixel art, bold outlines, limited palettes**, front-facing sheet
  rows on black background; palettes come from each GDD's color table. Cleopastar's
  restrained-motion notes vs Pomegraknight's aggressive-motion notes show how
  personality → animation language; write those notes for every character.
- Workflow: generate/draw against the approved character illustration as reference →
  approve the sheet → slice → import. Archive **all** approved illustrations and
  prompts (they're merch/press assets — `70-MERCHANDISING.md`).
- Foes need the same treatment at v0.4.0: Crab (patrol/aggro/jump/death/spawn-burst),
  Seagull (fly/aggro/poop/dash-telegraph **red tint** — the telegraph is a gameplay
  promise, ship it even placeholder), plus **evolution tell** (flash/tint now; size/
  sprite swap later — FOES §5).

## 4. Map & UI art

- Atlas terrain is deliberately restrained for now: irregular coast, subtle altitude tint, and
  contour strokes make each realm legible without implying a blocked route. A future art pass
  may replace these with shoreline, biome, and contour assets, but must preserve road clarity
  and the semantic distinction of map icons. Palette per world is fixed in `MapGDD.md` §2.
- Map iconography is currently semantic shapes (circle/diamond/triangle/?) — an easy,
  high-value art pass later, but **keep silhouettes distinct at min zoom**.
- UI assets live in `Sprites/UI/` (mugshots by HP band, cooldown rings, ult fill).
  New HUD elements follow the same set-of-SVG-states pattern. Reserved future HUD:
  boss HP bar (FOES §8), day/stamina during Adventure, item slots + CD pips.
- **Interactive Controls keep `mouse_filter = STOP`; decorative ones get IGNORE** —
  a decorative full-rect Control silently eats world clicks (caveat v0.2.4).

## 5. VFX backlog (from GDDs; placeholder = tint/particles, final = authored)

Blush flame loop (5 s), Fire Tornado vortex, burning-seed variant; Pixolotl marigold
ghosts (70%→30% alpha over 10 s, force-fade 0.3 s), SOBRECARGA trails, bubble cracks;
Cleopastar glow pips on spikes, Blackhole distortion + brighter suspended stars during
Ult; PumpKing head stages (1.0→1.5×) + explosion; Pangda Chi shield/bottle/9×9 field;
evolution flash; VOID devour flicker. Rule: a VFX that communicates gameplay state
(telegraphs, buffs, tells) is a **gameplay deliverable** — it ships with the feature,
however crude.

## 6. Feel / juice standards

- Screen shake: trauma² output — tune magnitude until *visible* (MaxOffset 30,
  Decay 3, trauma 0.22–0.5 per event; caveat v0.2.2). Any new juice effect gets a
  verify-by-eye pass, not just call-site review.
- Damage numbers: red damage / green heal, size scales 20→100 with amount.
- Hit feedback trio on every hit: number + flash/blink + knockback. New attacks must
  wire all three.

## 7. Audio (not started — the plan)

- Start when v0.5.0 makes runs real; sound is 30% of feel and currently 0%.
- Structure first: an `AudioManager` autoload; buses `Master/Music/SFX/UI`; event
  names keyed like modifiers (`"sfx:hit_melee"`, `"music:world_VK"`) so data tables
  can reference sounds the same way they reference stats.
- Minimum viable set: hit/hurt/death, jump/land, per-skill casts, pickup, UI clicks,
  day-end sting, devour rumble, one music loop per world palette + zone 6.
- Same placeholder rule: free/cheap SFX now, replace later behind stable event names.

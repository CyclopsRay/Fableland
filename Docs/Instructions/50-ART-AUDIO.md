# Fableland — Art, Animation & Audio Department

The rule of this department: **gameplay never waits for art, and art never blocks
gameplay.** Placeholders ship; the pipeline below upgrades them without touching logic.

---

## 1. Placeholder-first policy (current practice — keep it)

- Every entity gets a flat `.svg` placeholder in `Assets/Sprites/` sized to its **collision
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

- **Combat-map art is per realm, and each realm is a different medium** — flat poster
  (Starfield), ink-wash (Wulin), lit realism (Dungeon), linocut print (Ghost Town), cloisonné
  enamel (Cloud Court), storybook watercolour (the beach). The art direction, kits, palettes and
  the rules every tile obeys are in **`Docs/Art/RealmStyleBible.md`**; the beach's own catalog is
  `Docs/Art/BeachTileSet.md`. The five newer sets are painted by code
  (`Tools/realm_art/`, deterministic, about a minute for all of them) and any sprite can be
  swapped for a hand-made one by keeping its file name, footprint and canvas — the placeholder-
  first policy (§1), applied to a whole world at a time.
- Atlas terrain is deliberately restrained for now: irregular coast, subtle altitude tint, and
  contour strokes make each realm legible without implying a blocked route. A future art pass
  may replace these with shoreline, biome, and contour assets, but must preserve road clarity
  and the semantic distinction of map icons. Palette per world is fixed in `MapGDD.md` §2.
- Map iconography is currently semantic shapes (circle/diamond/triangle/?) — an easy,
  high-value art pass later, but **keep silhouettes distinct at min zoom**.
- **Overworld art is a page kit per world plus furniture shared by every island** — the
  torn-pages view (`MapGDD.md` §11a). A kit is one ground and nine map symbols in the
  world's medium (`Assets/Sprites/Map/Overworld/<W>/`, built by
  `Tools/overworld_art/build_kits.py`); the desk, waves, compass, pins, torn edges and the
  burnt VOID are code and never per-world. Kit spec, generation recipe and placement rules:
  **`Docs/Art/OverworldStyle.md`**. Its pins keep the node kinds' shapes as stickers.
- UI assets live in `Assets/Sprites/UI/` (mugshots by HP band, cooldown rings, ult fill).
  New HUD elements follow the same set-of-SVG-states pattern. Reserved future HUD:
  boss HP bar (FOES §8), day/stamina during Adventure, item slots + CD pips.
- **Interactive Controls keep `mouse_filter = STOP`; decorative ones get IGNORE** —
  a decorative full-rect Control silently eats world clicks (caveat v0.2.4).

## 5. VFX backlog (from GDDs; placeholder = tint/particles, final = authored)

Blush flame loop (5 s), Fire Tornado vortex, burning-seed variant; Pixolotl marigold
ghosts (70%→30% alpha over 10 s, force-fade 0.3 s), SOBRECARGA trails, bubble cracks;
Cleopastar glow pips on spikes, Blackhole distortion + brighter suspended stars during
Ult; PumpKing head stages (1.0→1.5×) + explosion; Pangda Chi shield/bottle/9×9 field;
evolution flash; VOID devour flicker; **boss presentation** (v0.10.11 — TBD #28): BOSS
PumpKing wears a placeholder red mark (`BossPumpKing._Draw`) because a headless black coat
vanishes against his castle's black stone, flashes like every other body when hit (the
hit flash of v0.11.2 — `Docs/Feedback.gdd` §5.3) and simply fades when beaten; his boss
look, entrance and defeat are unmade — `flow`/`boss.intro` and `boss.defeated` are the cues
they will hang on. Rule: a VFX that communicates gameplay state
(telegraphs, buffs, tells) is a **gameplay deliverable** — it ships with the feature,
however crude.

## 6. Feel / juice standards

The design, the tier table and every number: `Docs/Feedback.gdd` (v0.11.2). In short:

- **Gameplay raises a cue; a director decides what it looks, sounds and feels like.** An
  attack never shakes the camera, tints a sprite or plays a sound itself — it names itself
  (`HitInfo.From(...)`) and raises its cues (`CueBus`), and `FeelDirector` reads `FeelTable`.
  The VFX and audio directors plug into the same bus. Recipe: `20-ENGINEERING.md` §4.
- **A hit is felt by its weight** (Graze / Light / Medium / Heavy / Massive, from the damage
  it dealt — or named by the attack): hit-stop on both bodies of a melee hit, a camera kick
  along the blow, shake under a per-tier cap, a silhouette flash, and for the heavy ones a
  small roll and a zoom punch. Small hits get almost nothing, so the big ones land.
- Screen shake is still trauma² × `MaxOffset` (30 px), but smooth noise now, with caps:
  Light 0.30 (2.7 px), Medium 0.50, Heavy 0.75 (17 px), Massive 0.90. The flat 0.35 per hit
  of v0.2.2 is gone. **These are starting values nobody has watched** — the verify-by-eye
  pass of this section is still owed, and tuning lives in `FeelTable` / `FeedbackTable`.
- **Flash means "hit", blink means "cannot be hit".** Foes no longer blink when struck. The
  flash is white, or ink on a pale arena; it never touches a body's `Modulate` or `Visible`.
- Photosensitivity limits hold whatever the settings (Feedback.gdd §5.4): no full-screen
  flash, no saturated red, at most six bodies flashing at once, at most three large flashes
  a second. A new effect that flashes goes through the same limiter.
- Damage numbers: red damage / green heal, size scales 20→100 with amount (unchanged; still
  unpooled).
- Any new juice effect gets a verify-by-eye pass, not just call-site review — and a line in
  `FeedbackCheck` for the mechanism behind it.

## 7. Audio (not started — the plan)

- Start when v0.5.0 makes runs real; sound is 30% of feel and currently 0%.
- Structure first: an audio director autoload listening to the cue bus (v0.11.2 —
  `Docs/Feedback.gdd` §2, §6: the cue vocabulary there is the audio work order; a sound is
  a row keyed (owner, action) in the director's own `CueTable`, falling back owner → class
  → `any`); buses `Master/Music/SFX/UI`, with volumes in `FeedbackSettings`.
- Minimum viable set: hit/hurt/death, jump/land, per-skill casts, pickup, UI clicks,
  day-end sting, devour rumble, one music loop per world palette + zone 6.
- Same placeholder rule: free/cheap SFX now, replace later behind stable event names.

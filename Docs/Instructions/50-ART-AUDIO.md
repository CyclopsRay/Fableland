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

**Effects have a director and ten media (v0.11.6 — `Docs/VFX.gdd`).** Every cue is drawn by
`VfxDirector` as an *archetype* (hit spark, slash arc, burst, ring, dust, splash, status
loop, telegraph…) rendered by a *style* — one per art medium, in the medium of whoever
caused it: a character carries their home world's medium into every fight, and dust,
water, hazards and ambience wear the arena's. The procedural effects are the placeholder
tier of this section, and they obey the style bible (a mark is its medium: wash, flat
plane, brush stroke, glow, stamp, iron-wire line, scratch, pixel, enamel cell, erasure).
**Authored art replaces any of them without code**: a texture at `Assets/Vfx/<id>.png`, or
a whole effect as a strip of square frames at `Assets/Vfx/<style>/<effect id>.png` — same
name, same footprint (VFX.gdd §8). The art direction for each protagonist's signature
effects, built and not yet built, is VFX.gdd §5; the eight reference captures are in
`Docs/Art/Vfx/captures/`. Of the backlog above, the Blush loop, the Fire Tornado, the
burning-seed variant, PumpKing's explosion and the evolution flash now exist in their
procedural form.

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

## 7. Audio

The design, the ten media, every row's rules and the music: **`Docs/Audio.gdd`**. In short:

- **There are no audio assets.** Every sound and every piece of music is synthesised from
  data at load (`Scripts/Presentation/Audio/Synth/`, pure C#) and cached per realm and per
  character. `AudioDirector` (a presentation autoload) listens to the cue bus and gives
  every cue of `Feedback.gdd` §6 a sound; gameplay never names one.
- **A realm is a synthesis medium, not a scale** — the audio twin of the style bible's "one
  medium per world". Each of the ten media implements the same six primitives (whoosh,
  thump, tick, tone, texture, sweep) its own way, so a new cue is one recipe and ten sounds,
  and a character carries its home medium into every arena (Audio.gdd §5–6).
- **The placeholder rule, inverted:** the synthesis *is* the placeholder. A file named after
  an event — `res://Assets/Audio/<event id>.wav` or `.ogg` — replaces its recipe with no
  code change (Audio.gdd §10). That is how recorded or commissioned sound arrives.
- **Music** is a profile per realm and per screen (key, tempo, metre, motif, three or four
  stems of unequal length), bounced with a new seed every fight; intensity fades the stems;
  a boss adds its motif in its own medium; hits climb the realm's scale and a kill resolves
  (Audio.gdd §7–8).
- **Nobody has heard it.** It was measured (`AudioCheck`, `40-QA.md` §(2)) and looked at
  (`Tools/audio/inspect.py`; sheets in `Docs/Audio/sheets/`). The listening pass is owed:
  `res://Scenes/Dev/AudioAudition.tscn` plays any event, any variant, any realm's music at
  any intensity, and every mix state. Levels and recipes are data — retune them there.
- Adding a sound: `20-ENGINEERING.md` §4, "Give a cue its sound". Any new sound gets a look
  at its sheet, not just a green check.

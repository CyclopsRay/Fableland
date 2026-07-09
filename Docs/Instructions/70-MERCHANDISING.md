# Fableland — Marketing & Merchandising Department

The commercial hat. Its thesis: **Fableland's IP is its characters and its seed.**
Chibi food-fable heroes with bilingual mythology-laced kits are inherently
merchandisable; a fully deterministic roguelike is inherently marketable (shareable
seeds). Both assets are being created *anyway* by the other departments — this
department's job is to capture them as they're made, cheaply, instead of
reconstructing them at launch.

---

## 1. IP foundations (protect these while they're soft)

- **Names are brand assets.** Pomegraknight, Cleopastar, Pixolotl, PumpKing, Sifu
  Pangda, "Fableland", world names (VanillaKindom, Banboo Maze…), skill names
  (Nut's Decree / Apep's Maw, 破竹, RANIBOBER, SOBRECARGA). Before any name calcifies
  into marketing material: search for collisions/trademarks, check meanings in the
  languages you're borrowing from, and keep the intentional misspellings
  (Kindom, Banboo) *consistent everywhere* — a half-fixed "typo" brand is the worst
  of both.
- **The multicultural kit language is the differentiator** — Egyptian (Cleopastar),
  Mexican/Día de Muertos (Pixolotl), Chinese wuxia (Sifu Pangda). Keep it as loving
  homage: culture informs mechanics (papel picado ghosts ARE the rewind ammo), not
  just costumes. That depth is what press and communities reward.
- **Character personality lines in GDDs are canon.** ("Those who interrupt my XiuXing
  shall embrace the fiercest burn.") They become store copy, social posts, and merch
  text. Design owns them; marketing reuses, never rewrites.

## 2. Brand kit (assemble as a byproduct, starting now)

- **Palettes are already specified** — per character (GDD color tables) and per world
  (`MapGDD.md` §2). The brand kit is: logo (TBD), the world-palette strip, and one
  hero render per character.
- **Poster + sprite-sheet prompts in each GDD are the key-art pipeline.** Every time a
  character's art is generated and approved, archive: the prompt, the reference
  illustration, the approved output, and the palette — in an `Art/` archive (source
  files, not just exports; vector/hi-res masters are what merch printers need).
- House style locks: chibi, bold outlines, limited palette, personality-driven posing
  (Cleopastar restraint vs Pomegraknight aggression). One style sheet, all vendors.

## 3. Marketing beats (tied to dev milestones — no extra dev work)

| Dev milestone | Marketing capture |
|---|---|
| v0.4.0 foes | Crab/Seagull GIFs — evolution flash + spawn-on-death cascade are naturally clip-able |
| v0.5.0 nodes | First full-run clip; the atlas map (Voronoi territories, causeways, VOID) is the money shot — record pan/zoom flyovers per seed |
| v0.6.0 items | "Cursed item" content (THE VOID's phantoms) — strongest hook for roguelike audiences |
| Each character | Reveal post: poster + kit summary table + one signature-mechanic GIF |

- **The seed is a marketing feature.** Deterministic runs enable: daily seed
  challenges, "race my seed" community events, streamers comparing routes on one map.
  Cheap to build on the existing seed field; plan it for post-v0.6 community testing.
- Devlog rhythm: the changelog in `Migration.md` §0 is already devlog-grade — republish
  milestone entries with GIFs. Audience-building starts before the store page.

## 4. Merch pipeline (when there's an audience; prep is free now)

- Tier 1 (print-on-demand, low risk): stickers/pins of the chibi heads, the world-map
  atlas as a poster (a seeded map is literally generative poster art — offer "your
  seed as a print" someday), character posters from the GDD key art.
- Tier 2 (later): plush (Pomegraknight/Pixolotl/PumpKing silhouettes are plush-native;
  PumpKing's detachable head is a plush *gimmick* — head velcros off), acrylic standees.
- Requirements flowing back to art: keep masters in vector/hi-res; character sheets
  include a clean front pose on transparent/black; palette values recorded (they are).

## 5. Store & press readiness (checklist to fill over time)

- [ ] Name/trademark search for "Fableland" (note: existing games/brands share similar
      names — verify before spending on branding)
- [ ] Press kit folder: logo, 5 key arts, 10 GIFs, fact sheet (genre, hook, platform,
      solo-dev story — the "every department is me" angle is itself press-worthy)
- [ ] Store-page draft: hook line ("Cross the fused storybook worlds before the VOID
      eats the map"), 3 pillars (seeded permadeath runs / two-axis difficulty /
      recruitable bosses), system requirements from Godot export presets
- [ ] Monetization stance (recommendation: **premium, no P2W**; cosmetics only if
      ever; roguelike audiences punish anything else)
- [ ] Community home (Discord) opened at first public milestone, not before there's a
      build to talk about

## 6. What marketing needs FROM the other departments (standing asks)

- Design: personality lines + kit summary tables kept current (they're the copy).
- Engineering: a screenshot/GIF-friendly debug mode (hide HUD debug widgets), stable
  seed field, and eventually a "copy seed" button.
- Art: archive every approved asset + prompt at creation time (§2).
- QA: golden seeds double as demo seeds — flag seeds that generate beautiful maps.

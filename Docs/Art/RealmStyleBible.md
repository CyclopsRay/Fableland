# Realm Style Bible — combat-map art for every world

The art direction, tile kits and arena grammar of each realm's combat maps. Companion to
`Docs/Art/BeachTileSet.md` (the original Vanilla Kingdom beach set) and to
`Docs/MapCreation.gdd` (the map system these kits plug into). First landed in **v0.10.6**;
**v0.10.9** repainted Wulin as freehand ink and added Loong's Nest, the Dark Wood and the
Forgotten City; **v0.10.11** gave the Dark Wood its castle, a slope, and the first boss arena.

## 0. The principle: a realm is a *medium*, not a palette

Fableland's worlds are different storybooks torn out of different shelves (`MapGDD.md` §1).
A storybook is recognised by how its pictures were *made* before anything in them is
recognised, so each realm's combat maps are drawn in a different picture-making tradition —
not the same sprites recoloured. If two realms could trade a tile by swapping its palette,
one of them is wrong.

Each realm therefore fixes three things together, and they must stay together:

1. **Medium** — the art language (what a mark is, what is forbidden).
2. **Kit** — what tiles exist, and which *category* each thing is (ground, one-way platform,
   soft volume, hazard). The category choices are gameplay, not decoration.
3. **Arena grammar** — the shape of a map built from the kit, and so who it favours.

| World (`MapGDD` §2) | Realm in combat | Medium | Canvas | Arena grammar | Favours | Tile set | Map |
|---|---|---|---|---|---|---|---|
| VanillaKindom (VK) | Beach | Storybook watercolour & ink | `#6DB8D8` | small flat cove | all-rounders | `vk` — `BeachTileSet.md` | `seashore` |
| Starland (SL) | **Starfield** | Flat minimalist poster | `#17133A` | vast, level, nearly empty; a few colossal stone monuments | long range | `sl` — §3 | `starfield_long_horizon` |
| Banboo Maze (BM) | **Wulin** 武林 | Freehand ink (写意): calligraphic strokes, splashed ink, bare paper | `#E4E9E8` | vertical; bamboo and pine are soft volume; a waterfall that carries | assassins; whoever holds the height | `bm` — §4 | `wulin_bamboo_sea` |
| HollowCastle (HC) | **Dungeon** | Lit realism (relief shading) | `#0E0D12` | crowded, low, hazardous; lava | brawlers, fire | `hc` — §5 | `dungeon_hollow_gaol` |
| TheDeserted (TD) | **Ghost Town** | Linocut / woodblock print | `#F1E3C4` | two-storey street, mid-range | skirmishers, duellists | `td` — §6 | `ghost_town_main_street` |
| Palace of LOOING (PL) | **Loong's Nest** 龙宫 | Gongbi (工笔重彩): even ink line, graded mineral colour | `#0C1230` | the whole map is water — buoyant, resisting; hidden currents, bubble lifts | precise movers | `ln` — §7 | `loongs_nest_sunken_court` |
| Kingdom of Horror (KH) — *in the pool since v0.11.0* | **Dark Wood** | Scratchboard | `#111B3D` | traps, thickets and fog; a castle in some maps | guards, tanks, the patient | `kh` — §7A | `horror_hanging_wood`; boss arena `horror_pumpkings_castle` |
| The Forgotten (TF) — *in the pool since v0.11.0* | **Forgotten City** | Pixel art | `#0C0C22` | swamp at the bottom, pyramid above it | whoever goes over, not through | `tf` — §7B | `forgotten_canals_of_the_sun` |
| *(unassigned — tagged CC)* | **Cloud Court** | Cloisonné enamel | `#2A1B4A` | aerial; cloud soft volumes, scarce footing | air mobility | `pl` — §7C | `cloud_court_heavens` |

Starfield, Wulin, Dungeon, Loong's Nest and the Dark Wood follow the design owner's briefs;
the Forgotten City's direction was handed to this document ("your design"). Ghost Town and
Cloud Court are this document's proposals — see §10 for what is still the owner's call.
The **Map** column names each realm's showcase. Since v0.11.0 every pool world also ships a
plain map for each altitude band and a boss arena — `<realm>_sea_level`, `_low_ground`,
`_high_ground`, `_boss_arena` (§8) — and the Dark Wood and the Forgotten City are in the
overworld pool. Cloud Court lost its world to Loong's Nest: its map is played from the map
editor.
Reference views (rendered from the built-in maps):
`Docs/Art/References/Realms/{starfield,wulin,dungeon,ghost_town,loongs_nest,dark_wood,forgotten_city,cloud_court}.png`,
and `pumpkings_castle.png` — the whole of the Dark Wood's boss arena (§7A).

## 1. How the art is made

Every painted realm set is **drawn by code**: one deterministic script per realm under
`Tools/realm_art/`, sharing `common.py` (noise, brush strokes, relief lighting, export).

```bash
/opt/homebrew/bin/python3 Tools/realm_art/build_all.py                # repaint every realm (~1 min)
/opt/homebrew/bin/python3 Tools/realm_art/build_all.py wulin          # one realm
/opt/homebrew/bin/python3 Tools/realm_art/build_all.py --sheets DIR   # + a contact sheet per realm
```

Needs Pillow + numpy (the Homebrew Python on the dev Mac; the Anaconda one has a broken
numpy). Output goes to `Assets/Sprites/Tiles/<Realm>/<Category>/`, with a `.tile.json`
sidecar per sprite (`MapCreation.gdd` §2.5). The same script always writes the same bytes,
so an art change is a code diff and a sprite can never drift from its footprint.

Why code rather than a generated image: BeachTileSet.md already concluded that *code must
own geometry* (an image model does not hold a pixel grid). Painting procedurally extends
that to the whole sprite, which is what makes exact tiling, exact footprints and five
unrelated media affordable. The cost is that these are stylised, not hand-finished. Each
sidecar's `prompt` is a complete art brief, so any single sprite can later be replaced by a
hand-painted or AI-generated one **without touching code** — keep the file name, the
footprint and the canvas rule (§2.1) and the game cannot tell the difference.

## 2. Rules every painted realm obeys

These are checked at editor boot and by the headless content check (`TileSetAudit`,
`MapContentCheck` — `40-QA.md`). They are what let a kit be extended safely.

### 2.1 The canvas is the footprint ("pinned canvas")

A prop's PNG is exactly `footprint × 128 px` and its four corner pixels carry alpha 1/255
(`common.pin_corners`). The map editor trims a sprite to its used rect before fitting it in
the footprint; pinning makes that rect the whole canvas, so art lands where it was painted,
identically in the editor and in play. Consequences worth knowing:

- A sprite may leave air inside its footprint on purpose (a lantern in a 1×2, a torch's
  halo in a 3×3). Paint in footprint coordinates; nothing is auto-centred or auto-scaled.
- Multi-cell pieces can be registered against each other: stackable culms, tiling horizons.

### 2.2 Platforms paint their walking line on the canvas top

A platform's collider is its whole footprint, one-way, and the standing surface is the
footprint's top edge (KNOWLEDGE v0.6.18). So every platform sprite puts its deck, ridge or
bar on the top row of the canvas — `save_sprite(walk_top=…)` refuses a platform whose paint
starts lower, because the player would hover above it. Brackets, valances, hooks and
anything else decorative hangs *below* the line.

### 2.3 Ground is a strip atlas

A ground material (and a fill hazard such as lava) is a `layered_hill_strip` atlas:

```
rows    = depth layer   L1 surface · L2 just under it · L3 body · L4 underside
columns = 4 groups × [Left, Mid, Right, Peak]        (16 columns, 128 px cells)
```

A cell at map column *x* uses group `x mod 4`. The painter cuts all four groups from **one
continuous material strip four cells wide**, so neighbouring cells never repeat the same
paint and every seam matches by construction. (The beach's hand-assembled 4×4 sheet has one
Mid tile, which is fine for flat watercolour sand and visibly repeats for anything textured.)

Rules for painting a material so that *any* stacking is seamless:

- **Boundary-neutral rows.** The body pattern repeats every cell vertically, and each row's
  special treatment (the L1 cap, the L4 underside) fades out before the row's edge. Then
  L1 can sit directly on L4, L2 on L4, L3 on L3 — a two-cell ledge is as clean as a cliff.
- **Edges are overlays.** Left/Right/Peak are the same body with an exposed-side treatment;
  the collider is always the full cell, so silhouettes move by a few pixels at most.
- **Readable footing.** L1 carries the realm's "you can stand here" cue (a lit crest, a
  cornice, a cold glint, a coping) — the one detail that must survive a dark background.
- A material also ships `<stem>.png`, its L1-Mid cell: the palette thumbnail and the
  fallback fill if the atlas ever fails to load.

Terrain is taken to continue one cell past the left, right and bottom edges of a layer
(`AutotileProbe`), so a floor on the map's last row is not drawn as an underside.

### 2.3a A slope is a block (v0.10.11)

A material may also ship a **slope block** (`MapCreation.gdd` §2.2b): one sprite, `run`
cells wide and two rows tall, whose top is a ramp rising to the right across its upper row
over a solid lower row. Rules for painting one:

- **Cut it from the material's own strip.** The body is the same paint as the cells around
  it (`horror._earth_strip`, tiled twice), so only the ramp is new.
- **The walking line sits where it sits on level ground.** The Dark Wood's earth draws its
  crest 13 px under the collider's top; the slope's crest runs 13 px under the ramp. That is
  why the block has a solid row: at the low end the crest is *below* the ramp's foot, inside
  the lower row. On a one-row ramp it would fall out of the sprite at every joint.
- **The crest's wobble is periodic over the block's width**, so the left end of one block is
  the right end of the one before and a slope of any length has no joints.
- **Nothing is cut by the sheet** (§2.7): grass blades near the top and left edges are left
  out rather than clipped. The sides and bottom meet more earth on purpose.
- The sidecar carries the material's `autotileGroup` (and no atlas): `export_set` writes it
  from `group=` on a non-fill item.

### 2.4 Solid things are opaque; only air is translucent

Washes, prints and glazes are translucent by nature. That is right for leaves, mist, glow
and dust, and wrong for a wall: a building the far mountains show through reads as a ghost.
Solid subjects finish with `common.back()` (an opaque ground slipped behind the silhouette).
Glows are windowed to zero at the canvas edge (`common.radial_glow`) so a halo never shows
the rectangle of the sprite it lives in — give a lamp a footprint big enough for its light.

### 2.5 Far layers

- **One sprite per loop period.** A looping layer repeats every 16 cells; its sprite is 16
  wide. Never tile a far sprite beside its own mirror image — the eye finds the butterfly
  at once. Where two copies must sit side by side, ship an alternate (`…_b`).
- **Solid below.** A far layer sinks on screen as the camera climbs (by `parallaxY ×` the
  climb). Anything that is "land" carries flat colour all the way down its canvas; only
  things meant to dissolve (ink peaks into mist) may fade at the bottom.
- **Tiling sprites wrap exactly** — every noise field and nudge is periodic in x.
- **Few things fit.** At parallax *p*, a tile *W* cells wide uses up *W / p* cells of the
  journey, and far layers obey one-tile-per-cell like any other. Give each far range its
  own layer and two or three silhouettes.
- Far layers are composed in **start-screen rows**, not map rows — see §8.

### 2.6 A fighter must read first

Whatever the medium, the frame's highest contrast and saturation belong to the actors.
Each realm states how it keeps that true: Starfield keeps warm light on stone and puts
characters against a cool sky; Wulin is nearly monochrome and has no warm colour at all, so
any character pops; Dungeon is dark and desaturated with fire as its only saturated hue;
Ghost Town is three flat inks on cream; Loong's Nest is all cool blues with pearls its only
lamps; the Dark Wood is a black board where yellow means light and red means danger; the
Forgotten City keeps its neon thin; Cloud Court tints its backdrop toward the night so only
playable clouds are bright.

### 2.7 A sprite's canvas is not a window

Nothing may be cut by the edge of its PNG. A branch, a leaf fan or a hill that runs into
the edge ends in a ruled line, and in the map that line hangs in mid-air — the first
in-engine screenshots of v0.10.9 showed it on bamboo, pines, peaks, two dead trees and both
far palaces, none of it visible on a contact sheet (where every sprite sits in its own box).
Paint meets an edge only on purpose: an atlas or anything that tiles; a platform's walking
line and a deck that spans its footprint; the bottom of something that stands on the ground;
the top of something that hangs; a section that stacks.

The painters have the tools: `leaf(keep=…)` folds a leaf back toward its culm, `pine(keep=…)`
sizes branches to the sheet, a growing limb turns back instead of being clamped to the
border, a far castle or palace stands on a **dome** that comes down to nothing at both ends
(a slab that ran to the edges would end in two walls), and anything that dissolves is given
an alpha fade well inside the canvas. After painting or repainting a set, run

```bash
/opt/homebrew/bin/python3 Tools/realm_art/audit_edges.py BambooMaze     # or no argument: every set
```

and recognise the purpose of every line it prints, or fix the painter. Then look at the map
in the engine (`MapScreenshot.tscn`, 40-QA.md): a contact sheet cannot show this.

### 2.8 A volume looks like what it does

A soft volume's art is a promise about physics. The sea's surface is drawn on the row
where the water starts to hold a body (`deco.ln_surface` carries the skin on its fifth row
for exactly that reason); a waterfall's white ribbon is as wide as the column that carries;
a bubble column is drawn where it lifts. The one licensed exception is a **hidden current**,
which is hidden on purpose and so is drawn as a few faint streaks: visible to a player who
is looking for it, not to one who is not.

## 3. Starfield — Starland (SL)

> Egypt at dusk under a field of stars. Emptiness is the composition.

**Medium — flat minimalist poster.** No outlines at all. Every form is two or three flat
planes split by a hard terminator: a warm lit face, a plum shadow face. Faint print grain.
The only ornament is hairline gold inlay and star-shaped cut-outs. Gradients exist only as
broad posterised bands in the sky. *Forbidden:* outlines, texture detail, small clutter.

| Role | Colours |
|---|---|
| Sky | indigo `#17133A` · violet `#3B2A6E` · rose `#A8527A` · coral horizon `#F0875C` · glow `#FBD08A` |
| Stone | lit `#F2D3A0` · mid `#DDB07C` · shadow `#A8706A` · deep plum `#5B3A63` |
| Sand | crest `#F6D79C` · body `#CC8A62` · shadow `#9A5C5E` |
| Accents | gold `#FBE08A` / `#D9A441` · teal `#35B3A8` · star white `#FFF8E0` |

**Arena grammar — vast and empty, so range decides.** Maps are many screens wide and one
tall. The floor is unbroken dune. The only cover is a handful of colossal monuments set far
apart, built solid from `ground.sl_sandstone` (they are stone: you cannot pass through them
or jump up through them), and the high ground is theirs. Sky furniture — levitating slabs,
walkable constellations, stardust — is sparse *on purpose*: a brawler must cross open floor
under fire. Stardust is slow and buoyant (stagnation 0.35): a place to hang and line up a shot.

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.sl_sand` | Ground | 1×1 | Dune: bright rippled crest, sienna body with sediment hairlines, stepped underside |
| `ground.sl_sandstone` | Ground | 1×1 | Monument masonry: running bond, star frieze under a gold-lined cornice, plum plinth |
| `platform.sl_slab` | Platform | 3×1 | Levitating slab held up by one small star |
| `platform.sl_constellation` | Platform | 4×1 | A hairline of gold strung between five stars |
| `softvolume.sl_stardust` | SoftVolume | 3×2 | Posterised nebula; stagnation 0.35 |
| `hazard.sl_fallen_star` | Hazard (fire) | 2×1 | Only the white-hot core burns (circle effect area) |
| `deco.sl_obelisk` · `sl_star_gate` · `sl_broken_column` | Decoration | 1×5 · 6×5 · 1×2 | Standing stones for near layers / foreground |
| `deco.sl_pyramid` · `sl_pyramid_small` | Decoration | 10×6 · 5×3 | Far silhouettes, base lost in haze |
| `deco.sl_dunes_far` · `sl_dunes_near` | Decoration | 16×6 | Looping dune ranges, solid below |
| `deco.sl_horizon` · `sl_starfield` | Decoration | 8×12 · 8×4 | Looping dusk bands and stars |
| `deco.sl_sun` · `sl_moon` · `sl_constellation` | Decoration | 4×4 · 3×3 · 5×4 | Sky pieces; the five-point constellation is the realm's emblem |

## 4. Wulin (武林) — Banboo Maze (BM)

> A scroll painting you climb — and most of the scroll is empty. Height and concealment
> beat reach, and the landscape is a weapon.

**Medium — freehand ink, 写意 (xieyi).** Repainted in v0.10.9: the first version was black
and white but not ink painting — even grey fills behind a wall of identical bamboo. What
makes the medium is the **stroke** and the **paper**:

- **Calligraphic line.** Every line is one brush stroke that is pressed down and lifted: it
  swells, tapers and ends in dry flying-white (飞白). Pine trunks twist; branches are
  angular; bamboo is one flat stroke per internode with a dark hooked accent under each joint.
- **Splashed ink (泼墨/破墨).** Mass is wet ink that pools dark and bleeds at its edge, with
  touches of the blackest ink dropped into it — never an outline filled in.
- **留白 — bare paper is the picture too.** Mist, water, the moon and most of the sky are
  simply not painted. A peak is black at its crown, crossed by belts of unpainted mist, and
  its foot is lost in white; its outline is cut hard only where the rock is dark.
- **Texture strokes by name** — axe-cut 斧劈皴 on rock, long dry drags 皴 down the faces,
  moss dots 点苔 along edges, hair-stroke fans for pine needles.

Palette: **black, white, cyan and ink green — no warm colour.** *Forbidden:* airbrush
gradients, outline-and-fill, hard vector edges on anything that is not architecture, any
warm hue (the cinnabar sun and red lacquer of the first version are gone; timber is
weathered ink green and the seal is ink green).

| Role | Colours |
|---|---|
| Paper / bare white | `#E4E9E8` (the canvas) · `#F7F9F8` (mist, falling water, the moon) |
| Ink, five dilutions 墨分五色 | burnt `#14171A` · thick `#2A2F33` · heavy `#565E63` · light `#8E979B` · clear `#BEC6C8` |
| 花青 indigo-cyan | `#2F6B7C` · `#5E9BA9` · `#A6CAD0` — water, roof tiles, far peaks |
| 墨绿 ink green | `#1C352B` · `#33574A` · `#6E8F80` — bamboo, pine, moss, timber `#2E3F3A` |

**The scroll behind the fight.** An ink landscape is not a repeating pattern: it has a
subject and a few incidents, and the rest is paper. So the far layers are, back to front:
the painter's marks pinned to the frame (the moon, a skein of cranes, the inscription 山水
and a seal); pale far ranges; a band of mist; **one towering peak with pines on its
shoulders and a slender second peak** — the subject; in front of them a thread of far
waterfall, a village half sunk in mist and a fisherman's boat; nearer, darker ranges; and
one clump of far bamboo for every ten rows of the climb, rising out of mist. A stone
bridge, a pavilion and more pines are in the kit for other maps.

The inscription is written, not typeset: no calligraphic Chinese font ships with macOS, so
`GLYPHS` in the painter defines 山 水 竹 as brush strokes and `write()` paints them.

**Arena grammar — vertical; everything tall is soft volume; the land takes sides.** Maps
are a couple of screens wide and several tall. **Every bamboo and every pine is a
SoftVolume — never ground, never a platform.** A fighter dives into a culm, hangs in the
leaves (left alone it floats down at 1 m/s), goes straight up it by holding Up (8.6 m/s)
or by chaining jumps (19 m/s on the jump cooldown), rests with its head at the tip, jumps
off it (5 m clear) and drops out on a target with Down. Bamboo is light cover, not a mire: stagnation **0.6** (a cloud is 0.3, a palm 0.1); a
pine is 0.5. Solid footing is only the forest floor, ink-rock ledges and islets, the thin
one-way roofs and bracket beams of temples, and a stone bridge.

**The waterfall** is the realm's piece of tactical terrain. It is a *current*, not a medium
(§2.8, `MapCreation.gdd` §2.2a): everything inside it is carried down at **14 m/s** on top
of its own fall, a jump taken under it rises **2.7 m instead of 8.3**, and because it is
not a medium — nothing to swim in, no jump support — it cannot be climbed. Whoever holds its head can knock an enemy into it and
be rid of them; anyone crossing it gives up their height; and it is the fastest way down.
Lay it as a column of `softvolume.bm_waterfall` from a cliff lip to the floor, and put
`deco.bm_spray` in the foreground where it lands.

Temples are *assembled*, not single sprites: roofs and beams are the platforms; columns and
lattice bays are decoration set on a parallax-1.0 layer behind the battlefield. Put roofs
four rows apart — one more than a clean jump — with a beam half-way between on alternating
sides, and the plain route up is a zig-zag while the bamboo route is a straight line.

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.bm_rock` | Ground | 1×1 | Ink rock: dark wash, axe-cut strokes, top-lit faces left as paper, moss on the lip, dry tatters below |
| `platform.bm_roof_wide` · `bm_roof_small` | Platform | 6×1 · 4×1 | Indigo tiled roof, inked ridge, flying eaves, weathered timber fascia |
| `platform.bm_beam` | Platform | 3×1 | Timber tie-beam on dougong brackets |
| `platform.bm_bridge` | Platform | 6×2 | Arched stone bridge: one stroke for the deck (the walking line), one for the arch |
| `softvolume.bm_bamboo_giant` · `tall` · `mid` · `young` | SoftVolume | 2×16 · 2×10 · 2×7 · 1×4 | Whole plants, rooted at the footprint's base; stagnation 0.6 |
| `softvolume.bm_bamboo_stalk` | SoftVolume | 1×4 | Bare culm, edge to edge: stack to any height |
| `softvolume.bm_pine` | SoftVolume | 4×5 | A leaning pine on a spur of rock; stagnation 0.5 |
| `softvolume.bm_mist` | SoftVolume | 4×2 | A bank of mist with cloud-scroll lines; stagnation 0.3 |
| `softvolume.bm_waterfall` | SoftVolume (current) | 2×4 | Carries down at 14 m/s; no drag, no jump support. Stacks vertically |
| `hazard.bm_stakes` | Hazard (damage) | 2×1 | Sharpened bamboo stakes |
| `deco.bm_column` · `bm_lattice` · `bm_lantern` | Decoration | 1×3 · 2×3 · 1×2 | Temple body and furniture |
| `deco.bm_peak` · `bm_peak_b` | Decoration | 8×12 · 5×10 | The subject: a towering karst peak, and a slender second |
| `deco.bm_fall_far` · `bm_village` · `bm_boat` · `bm_pavilion` · `bm_pine` | Decoration | 3×8 · 6×3 · 3×1 · 3×3 · 4×5 | The incidents of the scroll |
| `deco.bm_peaks_far` · `bm_peaks_mid` · `bm_mist_band` | Decoration | 16×6 · 16×6 · 16×2 | Looping karst ranges; they dissolve into mist |
| `deco.bm_bamboo_far` · `bm_bamboo_curtain` · `…_b` | Decoration | 3×9 · 4×8 · 4×8 | A far clump rising out of mist; curtains that tile in **both** directions |
| `deco.bm_moon` · `bm_cranes` · `bm_seal` | Decoration | 3×3 · 3×2 · 1×3 | The moon as unpainted paper (烘云托月), cranes, the inscription 山水 and seal |
| `deco.bm_spray` · `bm_leaf_fringe` | Decoration | 4×2 · 4×2 | Foreground: where a fall lands; leaves hanging in from the top |

The moon, cranes and inscription sit on a near-zero-parallax layer, so they hold their
place on screen like marks on the paper itself — the frame is a painting, signed. Keep the
moon in bare paper: it is unpainted, so anything behind it shows as a ghost disc.

## 5. Dungeon — HollowCastle (HC)

> Crowded, dark and hostile. Every step is a decision.

**Medium — lit realism.** Every surface is a height field shaded by two lights: a dim cold
key from the upper left and a hot orange bounce from the lava below. Stone has weight,
chamfers catch light, undersides glow. Desaturated, damp, worn. *The only saturated colour
in the frame is fire.* *Forbidden:* outlines, flat fills, any cheerful hue.

| Role | Colours |
|---|---|
| Void | `#0E0D12` |
| Stone | `#4F535B` – `#7E848D`, mortar `#131417`, moss `#3C5230` |
| Basalt | `#25252A` – `#4A4B52` |
| Iron / rust | `#262A31` – `#454B55` / `#6B3A22` |
| Fire ramp | `#160502` → `#C22A06` → `#F26A0E` → `#FFB02E` → `#FFF2B0` |
| Footing glint | cold `#C9D6E6` |

**Arena grammar — close quarters with nowhere safe.** Maps are small sealed boxes cut into
low storeys (ceilings about five cells). Perches are short. **The routes between storeys
are things you climb into** — hanging chains (stagnation 0.55: hold Up and climb at about
8.5 m/s) and cobwebs (0.12, the one thing in the realm that really holds you: it takes
seconds to get moving in one) — so changing level is a commitment. Floors are
lava pools and spike beds with strips of safe stone between; catwalks over lava are the
fastest crossings and the worst place to be hit.

Lava is a *fill* hazard: paint it like ground and it autotiles (glaring surface, drifting
cooled rafts). Touching lava tiles merge into **one** hazard at runtime
(`TileProps.MergeAdjacent`), so wading a pool ticks once, not once per tile. Lava always
needs solid rock under it — there is no kill plane (§8). Lay `deco.hc_lava_glow` over a
pool in the *foreground* layer and everything near it, fighters included, looks lit.

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.hc_brick` | Ground | 1×1 | Damp running bond, chipped and cracked; bricks fallen from undersides |
| `ground.hc_basalt` | Ground | 1×1 | Faceted cavern rock; dripstone underside |
| `hazard.hc_lava` | Hazard (fire, fill) | 1×1 | Strip atlas; merges with neighbours; `LavaHazard.tscn` |
| `hazard.hc_spikes` | Hazard (damage) | 1×1 | Forged floor spikes; `SpikeHazard.tscn` |
| `platform.hc_grate` · `hc_plank` · `hc_ledge` | Platform | 3×1 · 3×1 · 2×1 | Iron gantry, rotting catwalk, stone corbel |
| `softvolume.hc_chains` · `hc_cobweb` | SoftVolume | 1×4 · 2×2 | Stagnation 0.55 · 0.12 |
| `deco.hc_torch` | Decoration | 3×3 | Mostly halo: torches are the realm's only lamps |
| `deco.hc_pillar` · `hc_arch` · `hc_banner` · `hc_skulls` · `hc_stalactites` | Decoration | 1×4 · 4×4 · 1×3 · 2×1 · 3×2 | Architecture and dressing |
| `deco.hc_wall_far` · `hc_cavern_far` | Decoration | 8×6 · 8×5 | Far arcade (stacks vertically; arches are open) over a lava lake |
| `deco.hc_light_shaft` · `hc_lava_glow` · `…_end` | Decoration | 2×6 · 2×3 · 2×3 | Cold daylight; heat haze middle + fading end piece |

## 6. Ghost Town — The Deserted (TD)

> A frontier town everyone walked out of, printed on a wanted poster.

**Medium — linocut / woodblock relief print.** A black key block cut with visible gouge
marks, over two or three flat spot colours printed slightly out of register on cream
paper. No gradients; tone is hatching, light is bare paper; every edge wobbles like a
hand-cut block and solids are speckled with ink skips. *Forbidden:* gradients, soft edges,
more than three inks plus black, lettering (signs carry a star).

| Role | Colours |
|---|---|
| Paper | `#F1E3C4` |
| Key block | `#1F1A17` |
| Spot inks | rust `#B5482E` · ochre `#D9A13B` · faded teal `#5E8C87` |
| Rare | sage `#7C8F58` (cactus only) · bone `#EADFC8` |

**Arena grammar — a duelling ground.** One straight street between clapboard buildings.
Everything is two storeys and mid-range: from any spot there is a clear line down the
street, and a balcony, awning or hanging sign within one clean jump (ledges sit three rows
above the street). Buildings are solid `ground.td_timber` with flat roofs; their faces carry
the one-way ledges. The only cover in the open is the kind that bites (saguaros).

Dress a building in the **foreground** layer: windows and doors lie wholly over solid wall,
so they never hide a fighter; a false-front parapet stands on the roof edge and *does* —
a roof is cover from the street.

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.td_mesa` | Ground | 1×1 | Rust rock ruled with strata cuts, ochre top, saw-tooth underside |
| `ground.td_timber` | Ground | 1×1 | Clapboard wall: boards, grain cuts, nails; beam on top |
| `platform.td_balcony` · `td_awning` · `td_sign` | Platform | 3×1 · 3×1 · 2×1 | Braced deck, striped canvas, signboard on chains |
| `softvolume.td_tumbleweed` · `td_dust` | SoftVolume | 2×2 · 3×2 | Stagnation 0.5 · 0.3 (halftone dust devil) |
| `hazard.td_cactus` | Hazard (damage) | 1×2 | Saguaro; narrow effect column up the trunk |
| `deco.td_window` · `td_door` · `td_parapet` | Decoration | 1×2 · 2×2 · 4×2 | Facade pieces for the foreground |
| `deco.td_saloon` · `td_water_tower` · `td_windmill` | Decoration | 6×5 · 3×6 · 3×6 | Edge-of-town silhouettes |
| `deco.td_mesas_far` · `td_sky` | Decoration | 16×7 · 16×6 | Looping buttes (solid below) and ruled print sky |
| `deco.td_sun` · `td_clouds` · `td_barrel` · `td_skull` · `td_signpost` · `td_fence` | Decoration | various | Sun, clouds, street furniture |

## 7. Loong's Nest (龙宫) — Palace of LOOING (PL)

> The dragon king's palace under the sea. Nobody falls and nobody runs: every position is
> reached on purpose.

**Medium — gongbi with heavy colour, 工笔重彩.** The opposite hand to Wulin's: slow, exact,
finished. Every form is enclosed by a fine, even "iron-wire" ink line (铁线描) and filled
with mineral colour graded smoothly from dark to light (分染), modelled as if lit by the
pale light that falls from the surface. Roof tiles, scales and shell ribs are drawn one by
one. *Forbidden:* a visible brush stroke, a broken or swelling line, flat unshaded fills, any
warm colour — pearls are the only lamps.

Palette: **purple, cyan, blue, black.**

| Role | Colours |
|---|---|
| The sea / line | deep `#0C1230` (the canvas) · line `#05060F` · indigo `#161D4A` |
| Azurite blue | `#15307A` · `#2C63CC` · `#7FB0F5` |
| Purple | `#2C1F5C` · `#5B3FA8` · `#A48BE6` |
| Cyan | `#127A8C` · `#33C7D6` · `#B6F3F4` · pearl `#EAFBFF` |

**Arena grammar — the whole map is water.** Water is a soft volume with **lower gravity**
(buoyancy and gravity are simply added: 35% of gravity is left) and **high resistance**. On
a real character that comes to (measured by the content check, 40-QA.md):

| In the sea | A fighter that runs 10 m/s |
|---|---|
| Swimming speed | **5.5 m/s** in whatever direction is held — and the water enforces it |
| Left alone | sinks at **3 m/s**, whatever the body (a crab sinks as fast) |
| The two are added | across: 5.5 m/s with 3 m/s underneath · **up 2.5 m/s** · **down 8.5 m/s** |
| A jump | a **3 m stroke** anywhere in the water (an air jump is 8.3 m), as often as the 0.3 s jump cooldown allows; from the surface it clears the water by **2.1 m** |
| Climbing | holding Up **2.5 m/s** · tapping Jump **10 m/s** · both 10.7 m/s |
| A current | carries at **4 m/s** — swimming against it leaves 1.5 m/s |
| A bubble column | lifts at 4 m/s; swum up, **9.5 m/s** |

So a swimmer moves freely, as if in space, with the sea always pulling it down — and the
map belongs to whoever can put themselves exactly where they mean to: a dash of fixed
length, a teleport, a hook. **Height costs effort**: going down is 8.5 m/s held; going up
is 2.5 m/s held, or 10 m/s for whoever keeps tapping Jump. The sea is not still — **hidden
currents** drag whoever drifts into them toward the urchin beds, and **bubble columns** are
the lifts: a column carries a swimmer up at 4 m/s for nothing, 9.5 m/s holding Up
(swimming down against one still gets down, at 1.5 m/s). Kelp is the cover (it is slower still: 4 m/s across, 1 m/s up). Solid footing is
palace floors, glazed eaves and giant clams. The sea has a surface: a swimmer holding Up
rests with its head at it; above it is air, where gravity is whole again, so whoever leaps
out falls back in and a map needs no ceiling.

The sea is laid as big `softvolume.ln_water` tiles on a **Farview layer with Collision on**
(parallax 1.0), so it fills the map without taking a single battlefield cell; currents,
bubbles and kelp go on the battlefield, inside it (`MapCreation.gdd` §3, §2.2a).

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.ln_reef` · `ln_palace` | Ground | 1×1 | Reef rock; palace wall in wave-scale pattern under a meander coping |
| `platform.ln_eave` · `ln_shell` | Platform | 5×1 · 3×1 | Glazed cyan eave; a giant clam |
| `softvolume.ln_water` | SoftVolume (water) | 8×6 | Stagnation 0.55 · gravity ×0.35 · sinks 3 m/s · drag ×5. Any layer role |
| `softvolume.ln_current` | SoftVolume (current) | 4×2 | Carries 4 m/s in the direction it faces (Flip H reverses it). Drawn faint on purpose |
| `softvolume.ln_bubbles` | SoftVolume (current) | 2×4 | Lifts 7 m/s. Stacks vertically |
| `softvolume.ln_kelp` | SoftVolume | 2×6 | Stagnation 0.4: cover. It must stay above the sea's 3 m/s sink, or nobody can swim up through it |
| `hazard.ln_urchin` | Hazard (damage) | 2×1 | Sea urchins on the floor a current drags you across |
| `deco.ln_column` · `ln_lantern` · `ln_coral` · `ln_fish` | Decoration | 1×5 · 1×2 · 3×3 · 3×2 | Palace columns, night pearls, coral trees, a shoal |
| `deco.ln_palace_far` · `ln_ridge_far` · `ln_rays` | Decoration | 10×6 · 16×5 · 4×10 | Far palace on its mound, looping seabed ridges, shafts of light |
| `deco.ln_surface` | Decoration | 16×5 | Four rows of open air, then the sea's skin on the fifth: lay that row on the first row of water (§2.8) |

## 7A. Dark Wood — Kingdom of Horror (KH)

> Where the PumpKing lives. A map that punishes haste.

**Medium — scratchboard.** A black board with light scratched out of it: fine, tangled,
wandering hatch lines, crowded where moonlight falls and absent in shadow. Everything is a
black cut-out with the moon's side scratched in blue. *Forbidden:* flat colour fills, soft
gradients, a tidy line. Colour is a code, not decoration:

| Role | Colours |
|---|---|
| The board / night | `#070A14` · sky `#111B3D` (the canvas — lighter than the board, or the cut-outs vanish) |
| Moonlight (everything) | blue `#1B2D6B` · `#3F6FD8` · `#9DBEFF` |
| Things that give light | yellow `#FFD23F` · `#FFF1A8` — the moon, windows, wisps, lanterns, a face in a tree |
| Danger | red `#D8323A` · `#7A161C` — thorns, a trap's teeth, pennants |

Palette: **blue, red, yellow.** Feeling: **entangled, chaotic, horror.**

**Arena grammar — traps and thickets.** The wood is ladders of gnarled limbs (one-way
branches) with bare canopy above (soft volume) and **bramble** on the floor: stagnation
0.15, the stickiest thing in the game, so a missed jump is paid for — you hang in it, and
it takes seconds to get moving in any direction. Open ground is worse:
ground fog (0.45) hides what lies in it, bear traps sit in the gaps, and will-o'-the-wisps
hang on the jump lines. Whoever looks before they move wins: guards, tanks, anyone who can
make the enemy come to them.

*Every realm keeps one art language; its features need not appear in every map.* A castle
is part of this kit — solid `ground.kh_stone`, climbed by rotten hoardings, lit windows —
and belongs in some maps, not all. **Destroyable trees** are planned and not built: today
`deco.kh_tree` is scenery, and a map should not depend on one being cover.

**The castle is PumpKing's (v0.10.11).** Its pieces: a **balcony** to stand on (a slab on a
corbelled console, the lightest stone in the wall so the one thing you can stand on reads), a
**balustrade** for it, a **gate** that is shut with light in its seam, the **door** he comes
out of with lamplight in it, **battlements** and red **pennants** — red is this realm's
"keep away", and it flies over the man who means it. And one piece of terrain: the **slope**
(§2.3a), a ramp of the wood's own earth, one cell up in two along.

**Arena grammar of his boss fight — the map takes his side** (`FOES.gdd` §8.5). His head
dies within a few metres on level ground and does not stop on a slope, so the castle stands
at the top of one. `horror_pumpkings_castle`, left to right: a strip of wood where the player
arrives, with the moon low over it; one unbroken slope, nothing on it to hide behind, lit by
pumpkin lanterns; the forecourt and the gate; the balcony, a clean jump above the forecourt;
the keep, solid to the top of the map. The castle's **front** is the same stone on a layer
behind the fighters, tinted a tone darker (`#3D4978`), so what can be walked into reads apart
from what cannot; the balustrade stands *behind* whoever is on the balcony, because in front
it hid a black-coated boss's legs against black stone (§2.6).

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.kh_earth` · `kh_stone` | Ground | 1×1 | Root-tangled earth; castle masonry |
| `ground.kh_slope` | Ground (slope block) | 2×2 | A ramp of the same earth, rising to the right 1 in 2; joins end to end; Flip H for the other way |
| `platform.kh_branch` · `kh_plank` | Platform | 4×1 · 3×1 | A gnarled limb with hanging roots; a rotten hoarding |
| `platform.kh_balcony` | Platform | 4×1 | The castle balcony; its console deepens toward the wall on its right (Flip H for a wall on the left) |
| `softvolume.kh_bramble` · `kh_fog` · `kh_canopy` | SoftVolume | 3×2 · 4×2 · 4×3 | Stagnation 0.15 · 0.45 · 0.5 |
| `hazard.kh_trap` | Hazard (damage) | 2×1 | Bear trap |
| `hazard.kh_wisp` | Hazard (fire) | 1×2 | Will-o'-the-wisp; circle effect area |
| `deco.kh_tree` · `kh_tree_b` | Decoration | 5×8 · 3×6 | Dead trees; the big one's hollow is nearly a face |
| `deco.kh_lantern` · `kh_window` · `kh_fence` · `kh_fireflies` | Decoration | 1×2 · 1×2 · 3×1 · 4×3 | Pumpkin lantern, lit window, iron fence, fireflies |
| `deco.kh_balustrade` · `kh_gate` · `kh_door` · `kh_battlement` · `kh_pennant` | Decoration | 4×1 · 4×5 · 2×3 · 4×1 · 1×2 | The castle: balustrade, the shut gate, the lit balcony door, battlements (tile in x), a red pennant |
| `deco.kh_moon` · `kh_castle_far` · `kh_forest_far` | Decoration | 4×4 · 10×8 · 16×6 | The moon, the castle on its hill, a looping far wood |

## 7B. Forgotten City — The Forgotten (TF)

> Pixolotl's home. Everyone outside takes it for a ruin. It never stopped working.

**Medium — pixel art.** A hard grid of square pixels, 32 to the map cell, a small fixed
palette, shading by ordered (Bayer) dither only. *Forbidden:* blur, anti-aliasing, a
gradient that is not dithered, a pixel off the grid. The choice is the story: the realm's
secret is that the "ancient" city is a machine, and pixels are what its light is made of —
old violet stone and swamp green in dither, and across them **thin lines of neon**, the
only bright colour, wherever the technology shows.

| Role | Colours |
|---|---|
| Night | `#0C0C22` (the canvas) |
| Old stone | `#1B1730` · `#2E2848` · `#4A4168` · `#6F648F` · `#9A90B8` |
| Swamp | moss `#1E4A38` · `#2F7A52` · `#5FBF6E` · mud `#241C1A` · `#3B2E28` · `#5A4636` · water `#0E2C33` → `#6FE0D0` |
| Neon (thin, never a fill) | cyan `#3DF6FF` · magenta `#FF3DBE` · yellow `#FFE14A` · green `#9BFF4A` |
| Pixolotl pink | `#FF8FCF` · `#D9589F` |

**Arena grammar — the kit follows the land.** Sea level is **swamp**: canals of water you
wade in rather than swim (stagnation 0.4, gravity ×0.6, sinks 2 m/s, drag ×4 — on a real
character 4 m/s across and 2 m/s up, a jump off the bottom rises 2.6 m, and getting out is
a swim to the surface and a jump that clears the bank by a metre),
cattails on the banks, mud. Whoever is down there is slow. As a map climbs it becomes
**pyramid**: terraces of stone, floating slabs, bridges of light — and the city's security
still works up there (beams, obsidian), so a summit is taken by going over, never by
walking up to it. Low-ground maps should be mostly swamp, high-ground maps mostly city.

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.tf_stone` · `tf_mud` | Ground | 1×1 | Violet masonry with neon glyphs; swamp bank |
| `platform.tf_slab` · `tf_light` | Platform | 3×1 · 4×1 | A floating slab; a bridge of light |
| `softvolume.tf_water` | SoftVolume (water) | 4×2 | Swamp water: see above. Any layer role |
| `softvolume.tf_reeds` · `tf_vines` | SoftVolume | 2×3 · 1×4 | Cattails (0.5); hanging vines that stack into a lane you climb by holding Up (0.5) |
| `hazard.tf_laser` | Hazard (fire) | 1×3 | Security beam |
| `hazard.tf_obsidian` | Hazard (damage) | 2×1 | Obsidian blades |
| `deco.tf_serpent` · `tf_stele` · `tf_sign` · `tf_axolotl` · `tf_tree` | Decoration | 2×3 · 1×3 · 2×2 · 2×1 · 4×6 | Serpent head with a working lens, neon stele, glyph sign, an axolotl, an ahuehuete |
| `deco.tf_pyramid_far` · `tf_city_far` · `tf_jungle` · `tf_stars` | Decoration | 12×8 · 16×6 · 16×4 · 8×4 | Far pyramid, a looping skyline nobody outside believes in, jungle, stars |

## 7C. Cloud Court — unassigned (CC)

> The dragon emperor's palace above the clouds. The ground is the wrong place to be.

*Status (v0.10.9):* this was v0.10.6's proposal for the Palace of LOOING, before the brief
for that world arrived — the palace is under the sea (§7). The kit and the map are kept,
complete and playable, tagged for a world code that no world owns yet (`CC`), so the
overworld never deals it. Whether it becomes another world's realm, a sky district of the
Palace, or is deleted is the owner's call (§10).

**Medium — cloisonné enamel (景泰蓝).** Every shape is a cell of flat, saturated jewel
enamel bounded by a raised gold wire. *There is no line that is not gold and no colour that
is not enclosed.* Faint enamel mottling; the wire is lit as relief so it gleams on one
flank. Court motifs: dragon scales, ruyi cloud scrolls 祥云, the running meander 回纹,
lotus. *Forbidden:* unenclosed colour, non-gold outlines, gradients across a cell.

| Role | Colours |
|---|---|
| Night | `#2A1B4A` |
| Enamels | purple `#4B2A7A` · lapis `#1E4FA3` · turquoise `#2CB5A8` · cinnabar `#C5382C` · jade `#74C095` · ivory `#F4EAD4` · rose `#E9A3B8` · imperial yellow `#EDBE3C` |
| Wire | gold `#E3B341` |

**Arena grammar — aerial.** Maps are almost all air. A terrace floor at the bottom is safe
and useless: everything worth having hangs above it. Solid footing is a handful of small
terrace islands and thin lotus pads, bridges and roofs; what fills the space is **cloud**,
every one a soft volume (stagnation 0.3). A fighter flies through a cloud on either axis,
floats in it when it lets go, and leaves by a jump from its top. The fight is carried cloud
to cloud in three dimensions. Frost pearls sit on the best perches to stop anyone settling — the realm's
hazard is cold, which gives `FreezeHazard` a home.

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.pl_terrace` | Ground | 1×1 | Purple enamel in gold dragon scales; key-fret coping; ruyi valance underside |
| `platform.pl_bridge` · `pl_lotus` · `pl_roof` | Platform | 4×1 · 2×1 · 5×1 | Moon bridge, lotus pad, imperial-yellow roof |
| `softvolume.pl_cloud` · `pl_cloud_trail` · `pl_cloud_small` | SoftVolume | 3×2 · 4×2 · 2×1 | Ruyi clouds in three nested enamels; stagnation 0.3 |
| `hazard.pl_frost_pearl` | Hazard (freeze) | 2×1 | The dragon's cold pearl; circle effect area |
| `deco.pl_dragon_column` · `pl_moon_gate` · `pl_lantern` · `pl_banner` | Decoration | 1×5 · 4×4 · 1×2 · 1×3 | Court architecture (the gate's circle is open) |
| `deco.pl_palace_far` · `pl_cloud_sea` | Decoration | 10×6 · 16×4 | Far palace; looping sea of clouds (tint it down — see below) |
| `deco.pl_moon` · `pl_sky` | Decoration | 4×4 · 8×10 | Night Pearl; looping night-to-rose sky with gold dust |

The cloud sea shares its enamels with the clouds you fight in, so far cloud layers are
tinted toward the night (`Tint` `#6F64A6` far, `#9B8FC6` near): the backdrop recedes and
the playable clouds stay the brightest thing in the frame.

## 8. Composing a map

The built-in realm maps are written as code — `Tools/realm_maps/build_maps.py`, one
function per map opening with its design intent, on top of `mapkit.py`:

```bash
/opt/homebrew/bin/python3 Tools/realm_maps/build_maps.py [name] [--ascii]
/opt/homebrew/bin/python3 Tools/render_map_preview.py Maps/x.json --view 22,17   # one in-game screen
```

The kit enforces the editor's rules as you compose (one tile per cell, footprints inside
the grid, known ids, and a sub-cell nudge only where the editor allows one —
`put(..., nudge=(2, -1))` slides a prop half a cell right and a quarter cell up,
MapCreation.gdd §2.3; terrain cannot be nudged). Rules a map must satisfy that no tool can
check for you:

- **Seal it.** There is no kill plane: a character that leaves the bottom falls forever.
  Solid floor edge to edge, solid walls to the top row on both sides, rock under any lava.
- **A slope is laid with `f.slope(tile, x, surface_row, steps, fill=…)`** — it places the
  blocks one step up and to the right and packs the ground under them, and returns where the
  plateau starts. Lay it *before* the floor around it (the lowest block's solid row is part
  of that floor; `fill` leaves taken cells alone). Things that stand on a slope are nudged:
  the middle of an odd column is a quarter cell above a row line on a 1:2 slope, so
  `stand(…, nudge=(0, 1))` puts a lantern's foot on it. Trees stand a little into the bank,
  on a layer behind the battlefield, which hides the difference.
- **A boss arena** is a map with `goals=("boss",)` and the level it serves; its
  `goal.level` markers are the boss's perches, the one the boss should open on farthest from
  the spawn. Keep two perches within one flight of each other if the boss can fly (PumpKing:
  30 m of Soul Form, so under about 20 m apart with the climb).
- **Jump budget.** A ground jump rises 4 cells and covers about 5; one-way ledges 3 rows up
  are a clean jump, 4 is not (use an intermediate, or a soft volume).
- **A soft volume is a lift, and its top is a springboard.** Holding Up climbs it, and so
  does chaining jumps inside it; a jump from its top clears it by about 5 m, so the next
  footing may sit 2 rows above the top. Never give a medium a swimming speed at or below
  its drop — nobody could swim up out of it (the content check refuses the kind, and
  swims the shipped overlap: kelp in the sea).
- **A pool's bank is flush with its water.** A fighter leaves a pool by swimming up to the
  bank and jumping, and that jump clears swamp water by 1 m and the sea by 2.1 m — less
  than a cell or barely one — so lay the water's top row level with the bank's walking
  line. The low-ground test canal was authored before swimming, with banks a row above
  its water; it was the one shipped pool that broke this, and the content check now swims
  out of every pool it is told about and refuses a map with swamp water it was not told
  about (`ShippedPools`, `MapContentCheckPhysics.cs`).
- **Mark it for every goal.** One `spawn.character`, respawns, enemy nests, and `goal.level`
  markers — the built-in maps serve all four goals and every hardship level.
- **Far layers are composed in start-screen rows.** The game draws a layer at
  `authored + (camera − anchor) × (1 − parallax)`, and the anchor is the **spawn point**,
  not the opening camera (which is clamped inside the map). So a far layer is not at its
  authored rows even on the first frame. `mapkit` hides the arithmetic: `layer.srow(r)` /
  `layer.pin(tile, col, row)` place by start-screen position, `layer.seen_from(tile, camX,
  baseRow)` places something to be centred when the camera reaches a column. Keep the spawn
  at least half a screen (9 cells) from the left edge if a pinned layer needs its left side.
- **A near-zero-parallax layer is a picture frame.** Whatever is on it stays where it is on
  screen for the whole fight; compose it as one.
- **Far layers hold one tile per cell too.** Two far things that overlap on screen — a
  village in front of a peak — need two layers with the same parallax, not one.
- **A sea is a layer.** Lay water as big tiles on a Farview layer with parallax 1.0 and
  `collision=True` (`m.farview("Sea", (1.0, 1.0), grid=…, collision=True)`); it then fills
  the map without taking a battlefield cell, and currents, bubbles and kelp go inside it on
  the battlefield. Only a **non-looping** Farview layer may collide, and only its soft
  volumes do (`MapCreation.gdd` §3).
- **Leave air over water.** A sea whose top row is below the top of the map needs no
  ceiling: above the surface gravity is whole, so whoever leaps out falls back in.
- **Look at it in the engine.** `render_map_preview.py` is fast and close; clipped sprites,
  a surface drawn a row away from its physics and a moon behind a waterfall were only
  visible in `MapScreenshot.tscn` shots (40-QA.md).

Each realm has one **showcase** map (`build_maps.py`), tagged for **every** terrain band
(`"*"`, the map kit's default since v0.10.7): a combat node carries `low-ground` /
`sea-level` / `high-ground` (`MapGDD` §2.1) and map selection otherwise needs an exact match
— tagged `sea-level`, as these maps first shipped, a realm's art was missing from two nodes
in three, which opened the legacy arena instead.

**The band maps and boss arenas (v0.11.0)** are the terrain variants, in their plainest
form — `Tools/realm_maps/build_band_maps.py`, one data row per realm (ground, the solid a
wall is built of, its ledge, a soft volume, a hazard, props, a sky recipe) and four shapes:

| Map | Shape | The realm's own touch |
|---|---|---|
| `<realm>_sea_level` (48 × 22) | a flat field, three ledges, each one clean jump | one soft volume, one hazard |
| `<realm>_low_ground` (48 × 28) | a basin: you spawn on the west rim, the fight is in the pit, two 3-row steps down | what the realm keeps in its low places — palms (Beach), stardust (Starfield), mist and bamboo (Wulin), a lava pool under a catwalk (Dungeon), dust (Ghost Town), kelp and a bubble lift (Loong's Nest), fog and bramble (Dark Wood), a canal bank to bank (Forgotten City — flush with its banks since v0.11.1, which makes its pit one row shallower: the pool rule above) |
| `<realm>_high_ground` (48 × 28) | a mesa: three terraces of three rows and a summit, ledges up the east side | the Dark Wood's is climbed from the west by its **slope** |
| `<realm>_boss_arena` (48 × 28) | a floor, a tower 12 m high on the east side, a ledge 18 m west of it: two perches | none yet — the Dark Wood's boss arena is the real one, PumpKing's castle |

They are **test maps**: valid, sealed, readable, and deliberately easy, so every node of
every realm opens a map of its own realm and band while the designed ones are written
(TBD #32). The showcase maps were left on `*`, so a band is served by its plain map *and* the
showcase; when a realm gets designed band maps, narrow its showcase to the band it is built
for. `MapContentCheck` fails a pool world without a map authored for each band and a boss
arena. Two things the generator had to learn: every realm's pieces have different footprints
(a 3 × 4 palm, a 2 × 6 kelp, a 4-wide roof), so a shared layout reserves its marker cells and
each realm's furniture is fitted round them — the kit refuses an overlap, which is how each
one was found; and five beach tiles have no art and draw as flat squares (`deco.rock`,
`deco.flower`, `deco.column`, `softvolume.bush1x1`, `ground.stone`) — a generated map must
not use them.

## 9. Realms not yet built — proposals

Picture-making traditions that are still free, matched to worlds waiting in `IDEAS.md` §2.
None of these exists in code; each would be one painter module, one tile-set file and one
line in `TileSets`. Written here so the *medium* is reserved before anyone draws.

| World | Medium | Arena grammar | Kit hook |
|---|---|---|---|
| ~~Kingdom of Horror~~ | *Built in v0.10.9 as the Dark Wood (§7A), in scratchboard to the owner's brief (blue, red, yellow; entangled, chaotic). Shadow-puppet theatre, proposed here before the brief, is free again.* | | |
| ~~The Forgotten~~ | *Built in v0.10.9 as the Forgotten City (§7B), in pixel art.* | | |
| **Gui Lin** (owed to Pangda, TBD #19) | — | — | *Already drawn:* Wulin's peaks are Guilin karst. Recommend Gui Lin = the Banboo Maze's realm name rather than a second ink-wash world (§10) |
| **The Margins** (secret) | Unfinished pencil on ruled paper: construction lines left in, eraser smudges, margin notes | Deliberately incomplete: platforms sketched but "not inked" are soft volumes; inked ones are solid | The category of a tile is *how finished it is drawn* |
| **Coralline Shoals** | Cut-paper collage (Matisse blue-nudes shapes), no line at all | Half water: a sea below, air above; kelp lanes, shipwreck decks | Water, currents and bubbles exist since v0.10.9 (§7): this realm would be the first where the *surface* is the centre of the fight |
| **The Whipped Peaks** | Claymation / piped icing: soft relief, thumbprints, glossy highlights | Slippery slopes, drifts of whipped-cream soft volume | Friction-table swap per `IDEAS` §2 |
| **Night-Market of Wan** | Neon tube on black velvet: glowing outline only, no fills | Dense stalls, awnings, lantern strings; very short sight lines, lots of one-way | Light is the only colour; hazards are the unlit gaps |

## 10. Open decisions for the design owner

Recorded here rather than silently chosen (`00-OVERVIEW.md` §5):

1. **Realm ↔ world mapping.** This build maps Starfield→Starland (SL), Wulin→Banboo Maze
   (BM), Dungeon→HollowCastle (HC), Loong's Nest→Palace of LOOING (PL). The mapping lives
   in three places only: each map's `Worlds` tag, `TileSets.All`, and this table.
2. **Cleopastar's home.** `Cleopastar.gdd` says *Palace of LOOING*; the brief describes the
   Egyptian, long-range realm as *Starfield*, which fits her kit and palette (gold, purple,
   teal, stars). If she moves to Starland, the Palace keeps its dragon king (QiongFeng,
   `IDEAS` §1) — whose palace is now under the sea (§7).
3. **Gui Lin.** Sifu Pangda's realm (ink-wash, not in the world pool) and the Banboo Maze
   are now the same picture. Merging them resolves TBD #19 with no new art.
4. **Ghost Town and Cloud Court are proposals.** Their kits and maps are complete and
   playable, but the directions were not in the brief. Replacing a medium means replacing
   one painter module; the kit ids and maps can stay.
   **Cloud Court has no world** since Loong's Nest took the Palace of LOOING (§7C): keep it
   for a future sky world, make it a district of the Palace (retag its map `PL`), or delete
   the set, the painter and the map. It costs nothing while it waits.
5. **Terrain variants** (§8) — *v0.11.0: every realm has a plain map per band and a boss
   arena, generated; designing the real ones is TBD #32* — and **hazard numbers** — `LavaHazard.tscn` (30 fire stacks per
   tick, no knockback) and `SpikeHazard.tscn` (12 damage per 0.5 s) are first-pass values,
   not balanced against real fights.
6. ~~**The Kingdom of Horror and The Forgotten are not in the overworld.**~~ **Decided,
   v0.11.0** — the owner asked for the realm pool to be arranged, and both joined
   `WorldDef.Pool`. The cost was the one named here: every seed draws different realms for a
   new run. It was kept off existing runs — a save records the pool it was made from
   (`MapGDD` §2). Their map colours are danger red and canal jade. What is still open is
   whether PumpKing and Pixolotl become *starters* there (TBD #20, #24).
7. **Swim numbers are measured, not balanced.** In the sea a fighter swims 5.5 m/s and the
   drop is 3 m/s, so up is 2.5 and down 8.5: 48 m of sea takes 19 s to climb and 6 s to
   descend by holding a direction, and holding a height means holding Up a little over
   half the time. Tapping Jump climbs 10 m/s on the universal 0.3 s cooldown — four times
   what holding Up does. A jump at the surface clears the water by 2.1 m (the swamp by
   1.0 m). A 4 m/s current, bubbles
   that lift 4 m/s (9.5 swum), a 14 m/s waterfall (§4, §7). In foliage and cloud the drop
   is 1 m/s, so a fighter practically hangs, and comes down from a jump inside a bush by
   holding Down. All of it is what the content check measures on a real character; whether
   it is *fun* needs a fight. The numbers are six constants in `LoongsNestTileSet.cs`, four
   in `ForgottenTileSet.cs`, one in `WulinTileSet.cs`, and `SoftVolume.GravityIndex` (one
   default, 0.1) for the drop of every plain volume. Open with it: whether foes should
   float too, and how long the jump cooldown should be inside a volume (TBD #27, #34, #35).
8. **Destroyable trees** (the Dark Wood brief) are not built; `deco.kh_tree` is scenery.

## Decisions log

- **A realm is a medium.** Six worlds, six picture-making traditions; palette alone never
  distinguishes two realms. Keeps "different stories and fables" true at a glance.
- **Painted by code, replaceable by hand.** Deterministic painters own geometry, tiling and
  footprints; every sidecar carries a full art brief so a sprite can be swapped for a
  hand-made one with no code change. Chosen over image generation for exact tiling and
  because five unrelated styles had to be consistent *within* themselves.
- **Category is design.** Bamboo is SoftVolume, monuments are Ground, temple roofs are
  one-way, lava is a merging fill hazard. The arena grammar follows from those four
  decisions per realm far more than from any layout.
- **Pinned canvas.** A sprite's canvas is its footprint, so editor and play agree and
  pieces register; chosen over "trim and auto-fit", which scaled props unpredictably.
- **Strip atlases with per-column groups.** One continuous four-cell material strip, so
  textured ground never visibly repeats; the beach's single-Mid sheet stays as it is.
- **Far layers carry colour to the bottom and never mirror.** Both learned from the first
  in-game previews (a horizon's lower edge appeared when the camera climbed; mirrored
  ranges made butterflies).
- **One map per realm first.** Depth (a map that proves each kit and grammar) before
  breadth (terrain and goal variants).
- **Ink painting is strokes and paper, not black and white (v0.10.9).** Wulin's first
  version used the right palette and still read as grey vector art, with a wall of
  identical bamboo behind it. Repainted as 写意: calligraphic strokes, splashed ink, most of
  the sheet left bare, and a background with a subject (one peak) and incidents (a far
  waterfall, a village, a boat) instead of a repeating pattern. No warm colour.
- **Terrain can take sides (v0.10.9).** A soft volume may now carry (a current, bubbles, a
  waterfall) and may be buoyant (water). The waterfall favours whoever is above it; the sea
  favours whoever can place themselves exactly. A realm's physics is part of its medium.
- **Hidden currents are drawn faint, not left out (v0.10.9).** Invisible forces are unfair;
  a few pale streaks reward the player who looks.
- **Soft volumes are swum in (v0.11.1).** Every soft volume is a float zone for a fighter:
  held Up climbs a bamboo, a chain, a vine, a cloud, the sea. A kit's "things you climb
  into" now means holding a direction or chaining jumps — both work, and they add — and a
  swimmer rests with its head at a volume's top. A realm that wants a volume to hold someone gives it a low index — slow to get
  moving in — never a swimming speed below its drop.
- **Realms share a language, not a checklist (v0.10.9).** The Dark Wood's kit has a castle;
  its maps do not all have to.
- **A sprite's canvas is not a window (v0.10.9, §2.7).** Found in the engine, not on the
  contact sheets: more than a dozen sprites across four sets were cut by their own PNG.
- **A map may take a side (v0.10.11).** The boss arena is composed from the boss's kit
  backwards: what his head does on a slope decided that there is a slope, where the castle
  stands and where he perches. "Terrain can take sides" (v0.10.9) applied to a whole map.
- **A slope block is two rows (v0.10.11, §2.3a).** The walking line of a realm's ground is
  drawn under the collider's top; a one-row ramp cannot hold it at its low end.
- **What you can stand on is the lightest thing in its wall (v0.10.11).** The balcony was
  first painted in the wall's own tone and vanished into the castle front behind it; its
  face is now crowded scratches under a broad coping, and the front is tinted darker.

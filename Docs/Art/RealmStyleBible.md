# Realm Style Bible — combat-map art for every world

The art direction, tile kits and arena grammar of each realm's combat maps. Companion to
`Docs/Art/BeachTileSet.md` (the original Vanilla Kingdom beach set) and to
`Docs/MapCreation.gdd` (the map system these kits plug into). First landed in **v0.10.6**.

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
| Banboo Maze (BM) | **Wulin** 武林 | Chinese ink-wash, visible brushwork | `#EFE7D4` | vertical; all bamboo is soft volume | assassins | `bm` — §4 | `wulin_bamboo_sea` |
| HollowCastle (HC) | **Dungeon** | Lit realism (relief shading) | `#0E0D12` | crowded, low, hazardous; lava | brawlers, fire | `hc` — §5 | `dungeon_hollow_gaol` |
| TheDeserted (TD) | **Ghost Town** | Linocut / woodblock print | `#F1E3C4` | two-storey street, mid-range | skirmishers, duellists | `td` — §6 | `ghost_town_main_street` |
| Palace of LOOING (PL) | **Cloud Court** | Cloisonné enamel | `#2A1B4A` | aerial; cloud soft volumes, scarce footing | air mobility | `pl` — §7 | `cloud_court_heavens` |

Starfield, Wulin and Dungeon follow the design owner's brief. Ghost Town and Cloud Court
are this document's proposals for the two remaining pool worlds — see §10 for what is still
the owner's call. Reference views (rendered from the built-in maps):
`Docs/Art/References/Realms/{starfield,wulin,dungeon,ghost_town,cloud_court}.png`.

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
characters against a cool sky; Wulin is nearly monochrome so any coloured sprite pops;
Dungeon is dark and desaturated with fire as its only saturated hue; Ghost Town is three
flat inks on cream; Cloud Court tints its backdrop toward the night so only playable
clouds are bright.

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

> A scroll painting you climb. Height and concealment beat reach.

**Medium — Chinese ink-wash (水墨).** Every mark is one loaded-brush stroke with visible
bristle streaks and flying-white (飞白); mass is diluted wash, never outline-and-fill.
Classical texture strokes are used by name in the painter: axe-cut 斧劈皴 on rock,
hemp-fibre 披麻皴 down mountain faces, moss dots 点苔. Monochrome ink on rice paper with
exactly two colours allowed: grey-green in foreground bamboo, cinnabar for lacquer and
seals. *Forbidden:* airbrush gradients, hard vector edges, a third colour.

| Role | Colours |
|---|---|
| Paper | `#EFE7D4` |
| Ink, five shades 墨分五色 | burnt `#1B1917` · thick `#35322E` · heavy `#69655E` · light `#9C978D` · clear `#C6C0B2` |
| Bamboo | `#24402F` · `#3F634B` · `#86A088` |
| Cinnabar / lacquer | `#B23A2B` · `#7A3A2B` · gilt `#C9A04E` |

**Arena grammar — vertical, and everything tall is bamboo.** Maps are a couple of screens
wide and several tall. **Every bamboo is a SoftVolume — never ground, never a platform.**
A fighter dives into a culm, hangs in the leaves (soft volumes refresh jumps), chains jumps
straight up it and drops out on a target. Bamboo is light cover, not a mire: stagnation
**0.6** (a cloud is 0.3, a palm 0.1), so it reads as a lane to travel. Solid footing is only
the forest floor, ink-rock ledges and islets, and the thin one-way roofs and bracket beams
of temples. There are no long sight lines.

Temples are *assembled*, not single sprites: roofs and beams are the platforms; columns and
lattice bays are decoration set on a parallax-1.0 layer behind the battlefield. Put roofs
four rows apart — one more than a clean jump — with a beam half-way between on alternating
sides, and the plain route up is a zig-zag while the bamboo route is a straight line.

| Tile | Role | Size | Notes |
|---|---|---:|---|
| `ground.bm_rock` | Ground | 1×1 | Ink boulder: dark wash, axe-cut strokes, pale top-lit band, grass flicks, ragged underside |
| `platform.bm_roof_wide` · `bm_roof_small` | Platform | 6×1 · 4×1 | Tiled roof, inked ridge, flying eaves, lacquer fascia |
| `platform.bm_beam` | Platform | 3×1 | Lacquered tie-beam on dougong brackets |
| `softvolume.bm_bamboo_giant` · `tall` · `mid` · `young` | SoftVolume | 2×16 · 2×10 · 2×7 · 1×4 | Whole plants, rooted at the footprint's base; stagnation 0.6 |
| `softvolume.bm_bamboo_stalk` | SoftVolume | 1×4 | Bare culm, edge to edge: stack to any height |
| `softvolume.bm_mist` | SoftVolume | 4×2 | Paper-white mist with cloud-scroll lines; stagnation 0.3 |
| `hazard.bm_stakes` | Hazard (damage) | 2×1 | Sharpened bamboo stakes |
| `deco.bm_column` · `bm_lattice` · `bm_lantern` | Decoration | 1×3 · 2×3 · 1×2 | Temple body and furniture |
| `deco.bm_peaks_far` · `bm_peaks_mid` · `bm_mist_band` | Decoration | 16×6 · 16×6 · 16×2 | Looping Guilin karst ranges; they dissolve into mist |
| `deco.bm_bamboo_curtain` · `…_b` · `bm_bamboo_far` | Decoration | 4×8 · 4×8 · 3×9 | The bamboo sea: the curtains tile in **both** directions |
| `deco.bm_sun` · `bm_cranes` · `bm_seal` · `bm_leaf_fringe` | Decoration | 3×3 · 3×2 · 1×3 · 4×2 | Cinnabar sun, cranes, colophon 竹海 + seal 武林, foreground leaves |

The sun, cranes and seal sit on a near-zero-parallax layer, so they hold their place on
screen like marks on the paper itself — the frame is a painting, signed.

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
are things you climb into** — hanging chains (stagnation 0.55) and cobwebs (0.12, the one
thing in the realm that really holds you) — so changing level is a commitment. Floors are
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

## 7. Cloud Court — Palace of LOOING (PL)

> The dragon emperor's palace above the clouds. The ground is the wrong place to be.

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
every one a soft volume (stagnation 0.3). The fight is carried cloud to cloud in three
dimensions. Frost pearls sit on the best perches to stop anyone settling — the realm's
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
the grid, known ids). Rules a map must satisfy that no tool can check for you:

- **Seal it.** There is no kill plane: a character that leaves the bottom falls forever.
  Solid floor edge to edge, solid walls to the top row on both sides, rock under any lava.
- **Jump budget.** A ground jump rises 4 cells and covers about 5; one-way ledges 3 rows up
  are a clean jump, 4 is not (use an intermediate, or a soft volume).
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

Each realm currently has **one** built-in map, tagged for **every** terrain band (`"*"`,
the map kit's default since v0.10.7). A combat node carries `low-ground` / `sea-level` /
`high-ground` (`MapGDD` §2.1) and map selection otherwise needs an exact match — tagged
`sea-level`, as these maps first shipped, a realm's art was missing from two nodes in three,
which opened the legacy arena instead. Terrain variants are still the next content step, and
the cheapest one (the kits and the map kit are done, so a variant is one more function): when
a realm gains one, give each of its maps the band it is built for. `MapContentCheck` fails a
realm that leaves any band without a map.

## 9. Realms not yet built — proposals

Picture-making traditions that are still free, matched to worlds waiting in `IDEAS.md` §2.
None of these exists in code; each would be one painter module, one tile-set file and one
line in `TileSets`. Written here so the *medium* is reserved before anyone draws.

| World | Medium | Arena grammar | Kit hook |
|---|---|---|---|
| **Kingdom of Horror** (owed to PumpKing, TBD #20) | Shadow-puppet theatre: black cut-paper silhouettes on rods against one backlit colour (pumpkin orange, sickly teal) | Chokepoints and sight-blockers: crypt doors, iron fences; fog soft volumes that hide whoever is inside | Fog = SoftVolume that also conceals; lantern light reveals. Favours tanks and guards |
| **Gui Lin** (owed to Pangda, TBD #19) | — | — | *Already drawn:* Wulin's peaks are Guilin karst. Recommend Gui Lin = the Banboo Maze's realm name rather than a second ink-wash world (§10) |
| **The Margins** (secret) | Unfinished pencil on ruled paper: construction lines left in, eraser smudges, margin notes | Deliberately incomplete: platforms sketched but "not inked" are soft volumes; inked ones are solid | The category of a tile is *how finished it is drawn* |
| **Coralline Shoals** | Cut-paper collage (Matisse blue-nudes shapes), no line at all | Half water: water is one big soft volume; kelp lanes, shipwreck decks | Reuses SoftVolume as swimming (`IDEAS` §2 already notes it) |
| **The Whipped Peaks** | Claymation / piped icing: soft relief, thumbprints, glossy highlights | Slippery slopes, drifts of whipped-cream soft volume | Friction-table swap per `IDEAS` §2 |
| **Night-Market of Wan** | Neon tube on black velvet: glowing outline only, no fills | Dense stalls, awnings, lantern strings; very short sight lines, lots of one-way | Light is the only colour; hazards are the unlit gaps |

## 10. Open decisions for the design owner

Recorded here rather than silently chosen (`00-OVERVIEW.md` §5):

1. **Realm ↔ world mapping.** This build maps Starfield→Starland (SL), Wulin→Banboo Maze
   (BM), Dungeon→HollowCastle (HC). The mapping lives in three places only: each map's
   `Worlds` tag, `TileSets.All`, and this table.
2. **Cleopastar's home.** `Cleopastar.gdd` says *Palace of LOOING*; the brief describes the
   Egyptian, long-range realm as *Starfield*, which fits her kit and palette (gold, purple,
   teal, stars). If she moves to Starland, the Palace keeps its dragon emperor (QiongFeng,
   `IDEAS` §1) and the Cloud Court direction stands.
3. **Gui Lin.** Sifu Pangda's realm (ink-wash, not in the world pool) and the Banboo Maze
   are now the same picture. Merging them resolves TBD #19 with no new art.
4. **Ghost Town and Cloud Court are proposals.** Their kits and maps are complete and
   playable, but the directions were not in the brief. Replacing a medium means replacing
   one painter module; the kit ids and maps can stay.
5. **Terrain variants** (§8) and **hazard numbers** — `LavaHazard.tscn` (30 fire stacks per
   tick, no knockback) and `SpikeHazard.tscn` (12 damage per 0.5 s) are first-pass values,
   not balanced against real fights.

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

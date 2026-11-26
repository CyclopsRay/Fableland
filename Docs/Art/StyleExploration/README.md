# Realm style exploration — concept images for the owner to choose from

**Status: exploration only. Nothing here changes a shipped art asset.** The owner found some
of the combat maps *too rigid* (2026-11-25): the code-painted kits sit on the 2 m grid, repeat
their tiles, and compose in rectangles. Before repainting anything, each implemented world
gets a handful of AI concept images showing how loose and hand-made its arenas *could* look.
The owner picks a direction per world; only then does a painter module (`Tools/realm_art/`)
or a hand-made kit follow it (`RealmStyleBible.md` §1, §10.4).

- **Tool:** Tripo Studio (web), image generation — the owner's monthly plan; no API key.
- **Rule kept from the style bible (§0):** a realm is a *medium*, not a palette. Every
  direction below is a different way of *making the picture*, never a recolour.
- **What "less rigid" means here:** irregular silhouettes, no visible grid, no repeated
  stamps, one composition with a subject (the Wulin repaint of v0.10.9 is the precedent).
- **What must survive in any direction:** a fighter reads first (§2.6) — the walking line and
  the ledges must be findable at a glance; solid things look solid and air looks like air
  (§2.4, §2.8).

## How the images are framed

Every prompt shares one frame so the directions can be compared side by side:

> Side-view 2D platform-fighter arena, game environment concept art, 16:9, camera straight-on.
> One long uneven ground line across the lower third, three or four ledges at different
> heights a character could jump between, open space above for jumping, layered depth (far,
> middle, near). Two tiny chibi fighters stand on the ground for scale, each about one-tenth
> of the image height. Organic hand-made shapes, irregular silhouettes, nothing tile-like or
> grid-aligned, no repeated stamps. No text, no UI, no watermark, no border.

Each direction then supplies the **scene**, the **medium** (what a mark is, what is
forbidden) and the **palette**. Direction **A** is always the style bible's own medium, done
by hand instead of by tile; **B** is a neighbouring medium that keeps the world's identity;
**C** is a deliberate departure, so the owner has a real choice and not three shades of one
idea. The four oldest kits (Beach, Starfield, Dungeon, Ghost Town) get a fourth, **D**.

## Directions and prompts

Image files, once generated, live beside this file as `<world>/<letter>.png`; the table in
`PICKS.md` records which the owner chose and why.

### VK — VanillaKindom · Beach (Pomegraknight's home)

| | Direction | One line |
|---|---|---|
| A | Storybook watercolour & ink | the bible's medium: wet washes, a brown ink line that breaks |
| B | Mid-century gouache picture book | flat chalky shapes, a vanilla-pink sky, bolder and simpler |
| C | Torn-paper collage | layered torn paper, crayon marks — tactile and playful |
| D | Seaside candy kingdom | leans on the *Vanilla Kingdom* name: sugar-and-cream architecture on the shore |

- **VK-A** — Scene: a small sunny cove — warm sand, tide pools, leaning palm trees, a
  driftwood pier, two beach huts on stilts whose roofs and a sun-bleached boat serve as
  ledges, gentle teal sea and far headland. Medium: storybook watercolour and ink — loose
  wet-on-wet washes that pool and bloom, visible cold-press paper grain, a dark-brown ink
  line that swells, breaks and misses the edge of the colour. Palette: warm sand, coral
  orange, teal, sun-bleached wood, coastal green, cream paper showing through.
- **VK-B** — Scene: the same cove at golden hour — huts, pier, palms, a striped lighthouse
  far off. Medium: mid-century gouache picture-book illustration — flat opaque shapes with
  chalky dry-brush texture, no outlines, simplified geometric palms and waves, overlapping
  shapes making new colours. Palette: vanilla pink sky, peach, turquoise, cream, one deep
  navy for shadows.
- **VK-C** — Scene: the cove built as a paper stage — dunes, waves in rows, palm fronds, a
  hut, a sun. Medium: torn and cut paper collage — hand-torn edges showing white paper core,
  layered sheets casting small soft shadows, wax-crayon scribbles for texture, visible paper
  fibres. Palette: sand kraft paper, coral, teal tissue paper, cream, a few magenta scraps.
- **VK-D** — Scene: a pastel seaside kingdom — a small castle with whipped-cream turrets and
  wafer balconies on the shore, a candy-striped pier, sugar-loaf rocks as ledges, pomegranate
  trees heavy with red fruit, calm pink-and-teal sea. Medium: storybook watercolour and ink,
  loose washes, broken brown ink line, paper grain. Palette: vanilla cream, strawberry pink,
  pomegranate red accents, teal sea, warm sand.

### SL — Starland · Starfield

| | Direction | One line |
|---|---|---|
| A | Flat minimalist poster | the bible's medium: two or three flat planes, no outlines |
| B | Risograph print | the same flatness with halftone grain and off-register ink — warmer, hand-printed |
| C | Painted star-chart mural | the sky is a gilded astronomical ceiling; monuments are fresco |
| D | Paper-cut shadow box | layered card silhouettes lit from behind — depth without detail |

- **SL-A** — Scene: a vast empty desert at dusk under a field of stars — one colossal
  half-buried stone head, a broken obelisk and a stepped plinth far apart serving as ledges,
  a long low dune as the ground, enormous sky. Emptiness is the composition. Medium: flat
  minimalist travel poster — no outlines at all, every form is two or three flat planes
  split by a hard edge between a warm lit face and a plum shadow face, faint print grain,
  hairline gold inlay and star-shaped cut-outs as the only ornament, sky in broad
  posterised bands. Palette: indigo, violet, rose, coral horizon, sand gold, plum shadow,
  one teal accent.
- **SL-B** — Scene: the same dusk desert and colossal monuments. Medium: two-colour-plus-gold
  risograph print — flat shapes built from visible halftone dots and grain, inks slightly
  out of register so edges fringe, soy-ink texture, uncoated paper. Palette: fluorescent
  coral and deep indigo overprinting to plum, metallic gold for stars, cream paper.
- **SL-C** — Scene: a desert night where the sky is a painted astronomical ceiling —
  constellations drawn as gold figures joined by lines, a huge zodiac ring, below it sandstone
  colossi and a temple pylon as ledges. Medium: ancient tomb-ceiling fresco with gold leaf —
  matte mineral pigment on plaster, fine cracks, flaking edges, flat figures in profile, gold
  leaf stars that catch light. Palette: deep lapis blue, gold leaf, ochre sandstone, faded
  turquoise, terracotta.
- **SL-D** — Scene: the dusk desert as a shadow box — five layers of cut card: far dunes,
  pyramids, a colossal seated statue, near ruins, foreground reeds; stars are pinholes of
  light. Medium: layered paper-cut diorama lit from behind — crisp cut edges, each layer a
  single flat tone, soft glow between layers, slight card thickness visible. Palette:
  graduated indigo to coral backlight, layers from pale sand to near-black plum.

### BM — Banboo Maze · Wulin 武林

| | Direction | One line |
|---|---|---|
| A | Freehand ink 写意 | the bible's medium, as repainted in v0.10.9: stroke and bare paper |
| B | Blue-green landscape 青绿山水 | ink bones with mineral blue and green laid over — richer, still Chinese painting |
| C | Night scroll, ink on indigo | the same brush on dark paper with a pale gold moon — dramatic, high contrast |

- **BM-A** — Scene: a vertical mountain gorge — one towering peak crowned with twisted
  pines, a thread of waterfall, bamboo groves, a small stone bridge and a roofed pavilion on
  a crag as ledges, a village lost in mist below. Most of the sheet is bare paper. Medium:
  Chinese freehand ink painting, xieyi — calligraphic strokes that swell and taper into dry
  flying-white, wet splashed ink that pools and bleeds, mist and water left as unpainted
  paper, axe-cut texture strokes on rock, moss dots. Palette: black ink in five dilutions,
  white paper, a little indigo-cyan and ink green, no warm colour.
- **BM-B** — Scene: the same gorge with terraced cliffs, pines, a waterfall, a bridge and
  pavilion. Medium: Chinese blue-green landscape painting — fine ink contour and texture
  strokes with azurite blue and malachite green mineral colour laid on peaks and slopes,
  gold-outlined cloud bands, silk ground showing its weave. Palette: azurite, malachite,
  ink black, aged silk tan, thin gold line.
- **BM-C** — Scene: the gorge at night — a huge pale moon behind the peak, bamboo
  silhouettes, lantern-lit pavilion, falling water catching moonlight. Medium: ink and pale
  gold on dark indigo paper — light is painted instead of left bare: white and gold brush
  strokes with dry flying-white over deep blue ground, splashed silver mist. Palette: deep
  indigo ground, white ink, pale gold, cold cyan, black.

### HC — HollowCastle · Dungeon

| | Direction | One line |
|---|---|---|
| A | Painterly lit realism | the bible's medium, but painted: irregular masonry, real light, no tiles |
| B | Prison etching with one fire colour | vast engraved vaults; the only colour is lava |
| C | Dark fairy-tale gouache | crooked, chunky, characterful — less real, more storybook |
| D | Illuminated-manuscript hell | a castle cross-section drawn as a medieval miniature with gold and vermilion |

- **HC-A** — Scene: the undercroft of a ruined castle — broken arches, a collapsed stair,
  hanging chains and a wooden gantry as ledges, uneven flagstones, a channel of lava that
  lights everything from below, one torch, deep darkness behind. Medium: painterly lit
  realism, like an oil concept painting — no outlines, visible brushwork, wet worn stone
  with weight, a dim cold key light from the upper left against a hot orange bounce from
  below, soft smoke and embers. Palette: desaturated blue-grey stone, near-black voids, and
  fire as the only saturated colour.
- **HC-B** — Scene: an immense prison vault — tiers of arches, drawbridges, giant chains and
  pulleys, stairways to nowhere as ledges, a glowing pit below. Medium: 18th-century
  copperplate etching — dense cross-hatching, deep bitten blacks, tiny figures against huge
  architecture, plate tone on cream paper, with a single hand-tinted colour: orange fire.
  Palette: black ink, cream paper, one orange.
- **HC-C** — Scene: a crooked dungeon — leaning towers of mismatched stones, a drawbridge on
  chains, a cauldron bridge over bubbling lava, bats, a barred gate with a friendly skull.
  Medium: dark fairy-tale gouache illustration — chunky simplified shapes, exaggerated
  wobbly perspective, opaque paint with dry-brush texture, strong rim light from the lava.
  Palette: slate blue, charcoal, moss green, saturated lava orange and yellow.
- **HC-D** — Scene: a castle cut open like a doll's house — cellars, a forge, a gaol, a
  lava moat, ladders and beams as ledges, tiny demons. Medium: medieval illuminated
  manuscript miniature — flat tempera colour, black contour, burnished gold leaf, patterned
  diaper background, decorative flames drawn as curling tongues, vellum texture. Palette:
  vermilion, ultramarine, gold leaf, bone vellum, soot black.

### TD — TheDeserted · Ghost Town

| | Direction | One line |
|---|---|---|
| A | Linocut relief print | the bible's medium: black key block, three spot inks, out of register |
| B | Frontier sketchbook | pen, sepia ink and dusty wash — looser and lonelier |
| C | Toy theatre | a cardboard stage set of a street — playful, layered, obviously hand-built |
| D | Sun-bleached photograph, hand-tinted | a tintype of the street with colour rubbed on — eerie and real |

- **TD-A** — Scene: the main street of an abandoned frontier town — a two-storey saloon with
  a balcony, a water tower, a hanging sign with a star, awnings and a wagon as ledges,
  tumbleweed, a saguaro, mesas far off. Medium: linocut relief print — a black key block
  with visible gouge marks, two or three flat spot colours printed slightly out of register
  on cream paper, tone made by hatching, light is bare paper, wobbling hand-cut edges, ink
  skips in the solids. Palette: black, rust, ochre, faded teal, cream paper.
- **TD-B** — Scene: the same empty street under a huge pale sky, a crow on the saloon
  balcony, wind-blown dust. Medium: travelling artist's sketchbook — scratchy dip-pen line
  that blots and skips, loose sepia ink wash, dusty watercolour stains, the paper's deckle
  edge and foxing visible. Palette: sepia, burnt umber, dusty ochre, one faded sky blue.
- **TD-C** — Scene: the street as a toy theatre — flat cardboard building fronts standing
  in rows on a stage, a cut-out water tower, a painted backdrop of mesas, paper tumbleweed
  on a wire. Medium: Victorian toy theatre and cardboard diorama — printed and hand-coloured
  card cut-outs with visible white cut edges and tabs, layers casting soft shadows, slightly
  warped card. Palette: aged cream card, brick red, mustard, bottle green, lamp black.
- **TD-D** — Scene: the deserted street at noon, hard shadows, a single swinging door, heat
  haze, telegraph poles. Medium: hand-tinted tintype photograph — soft-focus metallic
  greys, scratches and chemical stains at the edges, thin transparent colour rubbed on by
  hand over only the sky, a sign and a door. Palette: silver grey, warm brown-black, a pale
  blue tint and a rust tint.

### PL — Palace of LOOING · Loong's Nest 龙宫

| | Direction | One line |
|---|---|---|
| A | Gongbi, fine line and heavy colour 工笔重彩 | the bible's medium: iron-wire line, graded mineral blues |
| B | Cave-mural | mineral pigment on plaster, flowing ribbons — older, warmer, freer in line |
| C | Bioluminescent deep sea | the palace is lit only by living light — atmospheric, dark, glowing |

- **PL-A** — Scene: the dragon king's palace under the sea — sweeping tiled roofs, coral
  trees, giant clam shells and palace terraces as ledges, kelp, rising bubble columns,
  pearls glowing like lamps, pale light falling from the surface. Medium: Chinese gongbi
  painting with heavy colour — every form enclosed by a fine even ink line, filled with
  mineral colour graded smoothly from dark to light, roof tiles and scales drawn one by
  one, no visible brush stroke. Palette: azurite blue, purple, cyan, pearl white on deep
  indigo; no warm colour.
- **PL-B** — Scene: the undersea palace with dragon pillars, coral and flowing water drawn
  as ribbons, shell terraces as ledges. Medium: ancient Buddhist cave mural — matte mineral
  pigment on earthen plaster, rhythmic flowing line, flying ribbons and cloud scrolls,
  flaked and weathered surface, figures and forms outlined in dark oxidised line. Palette:
  malachite green, azurite, earth red, ochre, chalk white, darkened brown line.
- **PL-C** — Scene: the palace in the black deep — translucent roofs like jellyfish bells,
  glowing coral, lantern fish, drifting plankton, a dragon's ribcage as a bridge, shell
  ledges edged with light. Medium: luminous painterly illustration — soft glows on
  darkness, light scattering through water, forms defined by rim light and silhouette
  rather than line. Palette: black-blue water, cyan and violet bioluminescence, pearl white
  highlights.

### KH — Kingdom of Horror · Dark Wood (PumpKing's home)

| | Direction | One line |
|---|---|---|
| A | Scratchboard | the bible's medium: light scratched out of black, colour as a code |
| B | Victorian gothic pen and ink | fine cross-hatching with muted washes — dry humour, storybook horror |
| C | Silhouette shadow theatre | black cut-paper on glowing tissue — the most readable of the three |

- **KH-A** — Scene: a tangled night forest — gnarled trees with faces, bramble thickets,
  rope bridges and crooked branches as ledges, low fog, bear traps, will-o'-the-wisps, a
  castle with one lit window on a hill, a huge moon. Medium: scratchboard — a black board
  with light scratched out of it in fine tangled wandering hatch lines, crowded where
  moonlight falls and absent in shadow; everything is a black shape with its moonlit side
  scratched in. Palette: black, moonlight blue, yellow only for things that give light, red
  only for danger.
- **KH-B** — Scene: the same haunted wood and castle, a pumpkin patch with carved lanterns,
  iron gates, crows. Medium: Victorian gothic pen-and-ink illustration — obsessive fine
  cross-hatching, elongated spindly forms, deadpan stillness, thin muted watercolour washes
  over the ink. Palette: ink black, grey-violet wash, sickly green, lantern orange.
- **KH-C** — Scene: the wood as a shadow play — filigree trees, a spiked castle, hanging
  cages and branches as ledges, a pumpkin-headed king's silhouette on a balcony. Medium:
  cut-paper silhouette shadow theatre — intricate pure-black scissor-cut shapes in front of
  backlit layers of coloured tissue paper, soft glowing halos, no interior detail in the
  blacks. Palette: black silhouettes, backlight from deep blue through violet to an orange
  moon glow.

### TF — The Forgotten · Forgotten City (Pixolotl's home)

| | Direction | One line |
|---|---|---|
| A | Hard pixel art | the bible's medium: square pixels, fixed palette, dither, thin neon |
| B | Lush modern pixel art | pixels with painterly light, glow and depth — the same grid, far less stiff |
| C | Painted codex with circuitry | bark-paper codex figures whose glyphs are secretly circuits |

- **TF-A** — Scene: a Teotihuacan-style pyramid city at night above a swamp — stepped
  pyramids, a causeway, carved serpent heads and floating stone slabs as ledges, canals of
  dark water, reeds and marigolds, thin lines of neon running through the old stone. Medium:
  pixel art — a hard grid of square pixels, a small fixed palette, shading only by ordered
  dither, no blur and no anti-aliasing. Palette: night violet stone, swamp green, mud brown,
  dark teal water, and thin neon cyan, magenta and yellow as the only bright colours.
- **TF-B** — Scene: the same pyramid city and canals, fireflies, mist over the water, neon
  glyphs reflected in the swamp, papel-picado banners between temples. Medium: high-detail
  modern pixel art — crisp square pixels at fine resolution with rich atmospheric lighting,
  coloured glow, reflections and soft depth haze built from pixels. Palette: deep violet
  night, jade water, marigold orange banners, neon cyan and magenta light.
- **TF-C** — Scene: the pyramid city drawn flat like a page of a codex — temples in
  profile, a feathered serpent as a bridge, canals as bands of blue scrolls, an axolotl
  spirit, sun and star glyphs. Medium: Mesoamerican painted codex on amate bark paper —
  flat colour inside firm black contour, figures in profile, no perspective, fibrous brown
  paper; here and there the glyph borders turn into glowing circuit traces. Palette:
  turquoise, cochineal red, ochre yellow, black line, bark-paper brown, a little neon cyan.

### CC — Cloud Court (no world yet)

| | Direction | One line |
|---|---|---|
| A | Cloisonné enamel 景泰蓝 | the bible's medium: jewel enamel cells bounded by gold wire |
| B | Embroidered silk | clouds and terraces stitched in silk and gold thread — soft where enamel is hard |
| C | Blue-and-white porcelain 青花 | everything painted in cobalt on white glaze, mended with gold |

- **CC-A** — Scene: a palace above the clouds — small terrace islands, lotus pads, curved
  bridges and roofs as ledges, great scrolling clouds filling the space, a dragon coiling
  through them, frost pearls. Medium: cloisonné enamel — every shape is a cell of flat
  saturated jewel enamel bounded by a raised gold wire, faint enamel mottling, the wire lit
  as relief so it gleams on one side; no line that is not gold, no colour that is not
  enclosed. Palette: purple night, lapis, turquoise, cinnabar, jade, ivory, imperial yellow,
  gold wire.
- **CC-B** — Scene: the same sky palace, cranes flying between cloud banks, tasselled
  bridges. Medium: Chinese silk embroidery — satin-stitch fields with visible thread
  direction and sheen, couched gold-thread outlines, padded clouds, the silk ground showing
  between motifs. Palette: midnight blue silk, pearl and jade thread, coral red, couched
  gold.
- **CC-C** — Scene: the sky palace, clouds and dragon painted across the curved surface of
  a giant porcelain jar, terraces as painted bands. Medium: blue-and-white porcelain
  painting — cobalt brushwork from pale wash to deep blue-black on glossy white glaze,
  heaped-and-piled dark spots, with cracks mended in gold. Palette: cobalt blue, white
  glaze, thin gold seams.

## What the owner is choosing

For each world: one letter (or "keep what ships"), plus anything to carry across — a
palette, a motif, a level of looseness. A choice here is an art-direction decision recorded
in `RealmStyleBible.md`'s Decisions log; it does not by itself schedule a repaint.

## The overworld (the run map) — added 2026-11-25

A second, smaller exploration for `MapGDD.md` §11: the overworld is generated fresh for
every seed, so the question is not one picture but a *rule* — what makes any generated island
read as a map, while each of its five river-divided realms keeps its own medium. Three overall
directions, each shown on the same island (one disc, five rivers from a central black pentagon
to the sea, five realms: VK watercolour, BM ink, TD linocut, KH scratchboard, TF pixel art).
Prompts are in `prompts.json` (`OW-A` … `OW-C`); images are `img/OW-*.jpg`.

| | Direction | One line | What code would have to do |
|---|---|---|---|
| OW-A | One cartographer, five inks | a hand-drawn atlas on one sheet of parchment; each realm in its own medium, held together by the same paper and coastline | shared layers (paper, coast hatching, border, compass) + a stamp kit and ground treatment per realm, scattered inside the realm's fields |
| OW-B | Torn storybook pages | the island is five torn pages from five storybooks; the tears are the rivers and the VOID is burnt through all of them | each realm is a page texture clipped to its region with a torn-edge mask; rivers are the gaps; the VOID's devour is the burn spreading |
| OW-C | Board-game table | a crafted tabletop board of five materials with resin rivers and wooden city tokens | material shaders per region, bevelled edges, tokens for nodes — the least "drawn", the most tactile |

OW-B is the one that tells the game's own story (worlds from different storybooks fused by a
turbulence, `MapGDD.md` §1) and gives the VOID and the rivers a meaning in the picture; OW-A
is the most conventional and the easiest to keep readable at every zoom.

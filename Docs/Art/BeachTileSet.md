# Beach Combat-Map Tile Set

Production catalog for the beach assets used by `Docs/MapCreation.gdd`. One map cell
is 32 px / 1 m. The footprint is authoring occupancy; the effect area is the future
runtime collider/trigger and may be smaller than the visible art.

## Style lock

- Storybook watercolor-and-ink sprites with bold dark-brown outlines.
- Warm sand, sun-bleached wood, teal accents, coral-orange highlights, and coastal greens.
- Transparent backgrounds, no white sticker border, no baked floor or cast shadow.
- Orthographic side-view silhouettes that remain legible when scaled to their footprint.
- Decorative overhang is allowed; collision must always come from `TileDef.EffectArea`.

## Tile catalog

| Registry id | Role | Footprint | Art | Status / effect-area contract |
|---|---|---:|---|---|
| `ground.sand` | Ground | 1x1 terrain cells | `Generated/ground_sand_seamless.png` | Seamless fill now joins without transparent gutters; `terrain.beach_sand` remains reserved for edge/corner terrain variants. |
| `ground.grass` | Ground | 1x1 terrain cells | `Generated/ground_grass_seamless.png` | Seamless fill now joins without transparent gutters; `terrain.coastal_grass` remains reserved for edge/corner terrain variants. |
| `platform.bench` | Platform | 4x2 | `Generated/platform_bench.png` | New art. One-way top is a 4 m x 0.25 m strip at y=0.75 m. |
| `platform.sun_lounger` | Platform | 3x1 | `Generated/platform_sun_lounger.png` | Recreates the old angled wooden lounger rather than the front-facing bench. Thin one-way seat; raised back is visual only. |
| `platform.lifeguard_tower` | Platform | 4x8 | `Generated/platform_lifeguard_tower.png` | New art. v1 exposes the top balcony only; compound landings require compound shapes in the runtime-instantiation milestone. |
| `softvolume.palm_tree` | SoftVolume | 3x4 | `Generated/softvolume_palm_tree_v2.png` | Redrawn at 3:4 with dark ink only and no sticker outline. |
| `softvolume.cloud1x1` | SoftVolume | 1x1 | `Generated/softvolume_cloud_small.png` | Redrawn compact cloud; no sticker outline. |
| `softvolume.cloud2x1` | SoftVolume | 2x1 | `Generated/softvolume_cloud_medium.png` | Redrawn curled medium cloud; full footprint is enterable. |
| `softvolume.cloud3x2` | SoftVolume | 3x2 | `Generated/softvolume_cloud_large.png` | Redrawn asymmetrical large cloud; full footprint is enterable. |
| `deco.caution_sign` | Decoration | 1x2 | `Generated/deco_caution_monkey.png` | Restores the old tilted coral sign and cute monkey-skeleton pictogram; no sticker outline. |
| `deco.sand_castle` | Decoration | 2x2 | `Legacy/deco_sand_castle.png` | Transferred from Unity; visual only. |
| `deco.sun` | Decoration | 2x2 | `Generated/deco_sun_chibi.png` | Restores the old sleepy chibi smile and playful clockwise tilt; intended for Farview. |
| `hazard.bonfire` | Hazard | 2x1 | `Generated/hazard_bonfire_flat.png` | Flat, tiny redraw based on the old small fire. Only the central 0.35 m-radius circle is hazardous. |

Paths above are relative to `Sprites/MapCreation/Beach/`.

## Source audit and decisions

The Unity beach folder contained several collage sheets rather than individual game
assets. `MainArena.unity` also stretched one sand image across multiple widths, which
created visible repeats and inconsistent texel density. Those sheets remain outside
this repository; only standalone, useful cutouts were transferred.

Generated replacements are non-destructive and live under `Generated/`. Unity-derived
cutouts live under `Legacy/` so provenance and replacement priority remain obvious.
Ground currently uses seamless fill textures so adjacent cells have no transparent
gutters. The earlier terrain atlas remains a source for the later Godot TileSet terrain
pass, which must define edge/corner atlas coordinates and peering bits once, in data.

## Sprite placement and transforms

The editor derives the visible alpha bounds of prop sprites, aspect-fits them inside
their footprint, and anchors those bounds bottom-center. Transparent export padding
therefore cannot make an object float above its footprint floor. Seamless ground uses
the separate fill-footprint mode. Each `PlacedTile` serializes `FlipX`; select one or
more tiles and press `H` (or use **Flip H**) to mirror them, with undo/redo support.

## Generation prompt lock

Use the following invariant block for any replacement:

> Hand-painted storybook game sprite, watercolor-and-ink feel, bold dark-brown outlines,
> limited cheerful beach palette, orthographic side view, crisp at game scale, no white
> sticker border, no baked floor, no cast shadow, and no text unless the asset spec says so.

The new terrain, bench, tower, palm, clouds, caution sign, sun, lounger, and bonfire
were generated with the built-in image tool using flat chroma-key backgrounds, then
converted to alpha PNGs. The sign and sun were reference-guided directly from the old
Unity sheet. The reference-edit endpoint was intermittent for the second sheet, so the
lounger and bonfire prompts were written from a direct visual audit of their originals.

## Manifest sidecars (v0.6.12+)

New ground/terrain assets should ship a `<name>.tile.json` sidecar next to the PNG,
following `Docs/MapCreation.gdd` §2.5's `TileManifest` schema — see
`ground_sand_seamless.tile.json` / `ground_grass_seamless.tile.json` in `Generated/` for
worked examples. The sidecar carries the exact prompt used (so a replacement is a
one-file diff, not archaeology) plus the registry fields `TileManifestLoader` needs to
build a `TileDef` without hand-transcription: role, footprint, fill-vs-prop anchoring,
`autotileGroup`, and `edges` (per-side neighbor tags, currently descriptive metadata
only — no runtime code consults them yet). Props/decoration assets that stay
hand-anchored (bottom-center, per "Sprite placement and transforms" above) can adopt the
same sidecar convention later; it isn't required until a loader path actually reads them.

## Tsunami animation production order

The tsunami is a runtime moving hazard, not a paintable static tile. Its art box and
collision target are **16x8 cells = 512x256 px**. The authored source is a 2x2 sheet:

| Field | Contract |
|---|---|
| Asset | `Sprites/MapCreation/Beach/Generated/hazard_tsunami_sheet_2x2.png` |
| Source sheet | 1402x1122 px RGBA; 2 columns x 2 rows |
| Source frame region | 701x561 px |
| Runtime display per frame | 512x256 px (16x8 map cells) |
| Frame order | reading order: swell, curl rising, full crest, crashing crest |
| Playback | 6 fps, looping while the hazard travels |
| Direction | curl/impact faces left; the hazard moves left |
| Baseline | identical bottom baseline in every frame; runtime baseline is local y=0 |
| Collision | triangular gameplay polygon remains data-driven from `Width`/`Height`; art never owns collision |

### Stroke and rendering standard

- This sprite is roughly eight times taller than a character and must not inherit a
  small prop's heavy outline. Outer contour target: thin blue-gray ink, visually about
  **one third of the beach-prop outline weight** at final display size.
- Internal water lines are finer and lower contrast than the silhouette. Use watercolor
  value changes, not more strokes, to describe mass.
- Foam is pale cream-white with cool blue shadow, never a detached white sticker halo.
- Palette: deep base blue, turquoise midwater, pale cyan wash, cream foam, restrained
  blue-gray ink. Avoid pure black.
- Each frame preserves baseline, footprint, visual center, and similar total mass so the
  animation does not jitter. Foam and curl motion change; placement does not.
- No face, boat, character, land, sky, text, frame labels, dividers, or cast shadow.

### Reference picture

Repository reference: `Docs/Art/References/unity_beach_design_sheet.png`. The lower-center
curling blue wave is the approved design ancestor. Preserve its friendly storybook curl,
watercolor layering, foam language, and blue palette; remove its white sticker outline.

### Reproduction prompt

> Redraw the curling blue wave in the supplied Unity beach reference as four animation
> frames arranged in a strict 2-column by 2-row sprite sheet. Reading order: swell,
> curl rising, full crest, crest crashing forward. Every frame keeps the same ground
> baseline, 2:1 wide-to-high silhouette, visual center, and similar mass; only foam and
> curl motion change. Intended display per frame is 16x8 map cells, 512x256 px. Preserve
> the hand-painted storybook watercolor style, pale foam, turquoise midwater, deep-blue
> base, and restrained thin blue-gray ink. Because the wave is enormous, the outer
> contour is one third the apparent weight of small beach-prop outlines, with finer
> internal lines. Use a perfectly flat #ff00ff chroma-key background in all cells and
> gutters. No heavy black outline, white sticker halo, text, labels, dividers, characters,
> boats, land, sky, cast shadow, reflection, or watermark.

### Build/import checklist

1. Generate or paint the 2x2 source against the repository reference and prompt above.
2. Inspect at the **final 512x256 frame display size**, not only at source resolution.
3. Confirm equal frame regions, common baseline, no cell-boundary spill, and no magenta
   inside the wave; convert the chroma key to alpha with soft matte + despill.
4. Import without lossy compression. Keep filtering enabled for watercolor art; do not
   enable pixel-art nearest-neighbor filtering.
5. Slice by exact 2x2 regions in reading order and preview at 6 fps. Reject visible
   position/scale jitter or a contour that becomes heavier than nearby tower linework.
6. Keep `TsunamiHazard.Width/Height` derived from `Units` at 16x8 cells and preserve the
   triangle hitbox; any future art-size change must update this table and code together.

## Layered ground-hill autotile — base template (v0.6.15)

**Read this before writing a prompt for any new built-up ground material** (sand, grass,
rock, snow, …). It's the reusable rule set; each material is a short instantiation below
that only needs to fill in a materials table, not re-derive any of this. A second, richer
autotile system than the flat `ground.sand`/`ground.grass` seamless fills above — for
*built-up* terrain (mounds/hills/cliffs). Classification is per-cell, driven by two
independent axes, identical for every material:

- **Layer** (vertical depth): **1** = surface cap (this cell's top neighbor is air) · **2** =
  the cell directly below a Layer-1 cell · **4** = base (this cell's bottom neighbor is air) ·
  **3** = everything else. Precedence when a cell qualifies for more than one (only possible
  for a 1-cell-thick floating ledge, top *and* bottom both air): **Layer 1 wins.**
- **Position** (horizontal): **Left** if the left neighbor is air, **Right** if the right
  neighbor is air, **Peak** if *both* (Layer 1 only — no other layer has a dedicated
  both-open variant; that case falls back to **Mid**), else **Mid**.

**Geometry rule that keeps this tractable, for every material:** the *only* two tiles with a
non-rectangular silhouette are **Layer 1's top** (a gentle slope/dome — the surface material
still fills ~70-85% of the cell's height even at the open edge, never a thin wedge) and
**Layer 4's bottom** (a jagged/irregular exposed silhouette hanging into open air — what it's
*made of* is the one thing that's genuinely material-specific; see each instantiation's Layer
4 entry). Every other edge — L1's bottom, L2/L3's top and bottom, L4's top, and every
Left/Right side at every layer — is a flat, full-height cut, differentiated from its neighbor
only by color/texture, never by shape. This is what guarantees any two tiles at any layer can
sit side by side (or stack) with no gaps, and it's why 13 tiles cover every case instead of a
full corner/edge blob set.

**13 tiles per material, generated as 13 separate square images (NOT one composite sheet —
see "Why separate files" below), then composited by `Tools/compose_hill_atlas.py`.** Naming
convention: `terrain_<material>_hill_L<1-4>_<left|mid|right>.png`, plus one
`terrain_<material>_hill_L1_peak.png`, saved under `Generated/<Material>HillSource/`.

Shared prompt scaffold (every instantiation below fills in the bracketed parts):
> Hand-painted storybook game texture, watercolor-and-ink feel, bold dark-brown outlines,
> limited cheerful beach palette (per this doc's Style Lock). Orthographic side view, flat
> lighting, no baked directional shadow. This single square image is exactly ONE tile of a
> larger [material] hill cross-section system — do not draw a grid, multiple tiles, or
> repeat the motif within the canvas. No white sticker border, no text.
>
> [Tile-specific: Layer N, POSITION. Material/texture/palette description. Which edges are
> open-to-air vs. flat-cut-against-a-neighbor, per the geometry rule above.]
>
> Keep every one of the 13 [material]-hill tile images at the exact same pixel width and
> height as each other (any resolution is fine as long as all 13 match) so they can be
> composited into a uniform grid afterward.

**Why separate files, not one AI-drawn grid sheet:** the existing `terrain_beach_atlas.png`
(1536×1024, sliced 7×6 per `AutotileAtlas.cs`) doesn't even divide evenly — 1536/7 and
1024/6 are both non-integers — which is almost certainly why the last hand-drawn attempt at
a tile sheet came out unequally divided. Diffusion models don't reliably hit exact grid
lines; compositing 13 independently-generated, equal-size squares into a grid is a
deterministic, always-exact operation in code.

**Pipeline for a new material:** generate its 13 files, save under
`Generated/<Material>HillSource/`, then run
`python3 Tools/compose_hill_atlas.py --material <material>` to produce
`Generated/terrain_<material>_hill_atlas.png` (a guaranteed-uniform 4-row × 4-col sheet — row
= Layer 1-4, col = Left/Mid/Right/Peak; the 3 cells at rows 2-4 col 4 are intentionally left
blank/transparent, since only Layer 1 has a Peak variant). `HillAutotile.cs`
(`Scripts/MapCreation/Data/`) is the pure-C# classifier + atlas-cell lookup — it's already
material-agnostic (works on any `AutotileGroup` whose neighbors it's asked to probe), so a
new material needs no code changes there. Wiring a material into `GridView`'s renderer
(mirroring how `AutotileAtlas` is wired today) and registering its `TileRegistry` entry are
left for once that material's reference atlas exists to check the result against — see
`HillAutotile.cs`'s header comment for the exact next step.

**Second disclosed autotiling exception (see `Docs/MapCreation.gdd` §2.5):** like the
existing 2-state ground lookup, this is editor/authoring-time only, not a runtime bitmask
autotiler — it's a side-view vertical-stratification model, not generic Wang/blob corner
autotiling, so it doesn't compete with the eventual Godot `TileSet` Terrain runtime path.
Applies to every material instantiated below, not just the first one.

### Sand hill (instantiation, v0.6.13, spec locked — art not yet generated)

Follows the base template above exactly (13 tiles, same classification/geometry rules).
Materials table — the only thing this instantiation adds:

| Layer | Material / palette | Left/Right side treatment |
|---|---|---|
| 1 (surface) | Fine pale-gold soft sand (#E8C878-ish), faint grain texture | Slopes down toward the open edge (sand still fills ~70-85% of cell height at the lowest point); Peak domes symmetrically on both open sides |
| 2 (upper-mid) | Coarser, denser tan sand — visible larger grains, a few tiny embedded pebbles, slightly darker ochre shading | Flat vertical cut (no slope), textured as a natural rough exposed sand face |
| 3 (core) | Denser, coarser, greyer-tan sand — more visibly embedded stones/pebbles, craggier texture | Flat vertical cut, rough natural exposed-rock-in-sand face |
| 4 (base) | Dense grey-brown rock/stone, angular and sharper-edged than Layer 3; bottom silhouette is jagged sharp rock points hanging into open air (roughly 60-80% coverage, transparent gaps between points) | Flat vertical cut (same no-slope convention), rough rocky exposed face |

#### Generation strategy: try one combined image first

13 independent AI calls give the model almost nothing to stay consistent against —
color, exact edge height, and grain pattern can all drift between calls. Before
committing to that, try the cheap experiment: ask for all **12 non-Peak tiles in a
single image** (one generation is far more likely to be internally color/texture
consistent than 12-13 separate ones), slice it in code, and look at the result.

> Generate ONE image: a grid of 12 tiles, 3 columns x 4 rows, portrait canvas at
> **exactly a 3:4 width:height aspect ratio** (request "3:4" if your tool has an
> aspect-ratio option; e.g. 900x1200 or 1200x1600) so the 12 cells are perfect
> squares with no leftover margin. This is ONE coherent illustration of a sand
> hill's cross-section, not 12 unrelated pictures — color, grain texture, and
> material must flow continuously across every shared boundary, both
> horizontally and vertically, since these cells sit directly next to each other
> in-game. Cells are perfectly, mechanically equal in size and share edges
> directly: no gutters, border lines, dividers, labels, or numbers anywhere.
>
> Columns (left to right): LEFT edge variant · MID (interior) variant · RIGHT
> edge variant. Rows (top to bottom): Layer 1 surface (fine pale-gold soft sand,
> #E8C878-ish, faint grain) · Layer 2 upper-mid (coarser denser tan sand, larger
> grains, a few pebbles, darker ochre) · Layer 3 core (denser, coarser, greyer-tan
> sand, more visible stones, craggier) · Layer 4 base (dense grey-brown rock,
> angular, sharper-edged than Layer 3).
>
> Left column: its outer (canvas-left) edge is open to air. Row 1 (Layer 1)
> slopes gently down toward that edge, sand still filling ~70-85% of the cell's
> height at the lowest point. Rows 2-4 have a flat vertical cut at that edge (no
> slope), textured as a rough natural exposed face, not a smooth plane. Right
> column: mirror of Left, on the canvas-right edge. Mid column: fully interior,
> bridges Left and Right with no open edge.
>
> Every row-to-row (layer-to-layer) boundary within a column is a flat, seamless
> color/material blend — no visible dividing line, just the material gradually
> getting denser/darker from Layer 1 to Layer 4 — with exactly two exceptions:
> the very TOP of the whole canvas (Layer 1's top, i.e. only where it isn't
> already cut flat by an open Left/Right edge) is a gentle slope/dome, and the
> very BOTTOM of the whole canvas (Layer 4's bottom) is a jagged, irregular
> silhouette of exposed rock points hanging into transparent open air (roughly
> 60-80% coverage, not a flat cut).
>
> Hand-painted storybook watercolor-and-ink style, bold dark-brown outlines,
> limited cheerful beach palette, orthographic side view, flat lighting, no
> baked directional shadow, no white sticker border, no text. Use a flat
> `#ff00ff` chroma-key background only where the art calls for open air (above
> Layer 1's sloped top, between Layer 4's jagged rock points) — everywhere else
> is fully opaque material.

Save the result as e.g. `terrain_sand_hill_combined.png`, then:

```
python3 Tools/slice_hill_grid.py terrain_sand_hill_combined.png --material sand
python3 Tools/compose_hill_atlas.py --material sand --preview
```

`slice_hill_grid.py` crops it into the 12 individually-named tile files (Peak is
skipped by design — this pass doesn't generate it) and `compose_hill_atlas.py`
leaves that one cell blank rather than erroring, exactly so this 12-tile test
works standalone. Open the `--preview` output and look at every zero-gap boundary.

**Fallback if the combined image doesn't hold together:** you don't have to
redo all 12. Crop the specific tile that's wrong out of the sliced result (or
out of the combined image directly) and hand it back to the AI as a reference
image with a targeted instruction — "redo this one tile, keep the same
material/color as the reference, fix: <what's wrong>" — rather than a blind
independent regeneration. The per-tile prompt table below is exactly the
per-tile spec to pair with that reference-guided redo (or to fall back to
fully individual generation for all 13, Peak included, if the combined-image
approach doesn't pan out at all).

Full per-tile prompts (prefix + tile-specific + suffix, per the base template scaffold),
filenames under `Generated/SandHillSource/`:

| # | Filename | Layer / Position | Tile-specific prompt |
|---|---|---|---|
| 1 | `terrain_sand_hill_L1_left.png` | 1 / Left | Layer 1, LEFT edge. Fine pale-gold soft sand (#E8C878-ish), faint grain texture. The surface slopes gently down toward the LEFT edge, which is open to air; sand still fills ~70-85% of the cell's height at the lowest point. The RIGHT edge is a flat vertical cut at near-full sand height (butts against a Mid tile). Bottom edge is a flat horizontal cut (sits on Layer 2). |
| 2 | `terrain_sand_hill_L1_mid.png` | 1 / Mid | Layer 1, MID. Same soft pale-gold sand. Top surface is nearly flat with only very subtle undulation for texture — no directional slope. Both LEFT and RIGHT edges are flat vertical cuts at the same near-full sand height as tile 1's un-sloped edge, so it tiles seamlessly against Left/Right/Mid neighbors. Bottom edge flat (sits on Layer 2). |
| 3 | `terrain_sand_hill_L1_right.png` | 1 / Right | Layer 1, RIGHT edge. Mirror of tile 1: slopes down toward the RIGHT edge (open to air), LEFT edge flat vertical cut at full sand height. Bottom edge flat. |
| 4 | `terrain_sand_hill_L1_peak.png` | 1 / Peak | Layer 1, PEAK (isolated single-width mound tip). Soft pale-gold sand domes up in the middle and slopes down symmetrically to BOTH the left and right edges, both open to air; sand still fills ~70-85% of height at each edge (same minimum as tiles 1/3). Bottom edge flat (sits on Layer 2). |
| 5 | `terrain_sand_hill_L2_left.png` | 2 / Left | Layer 2, LEFT edge. Coarser, denser tan sand than Layer 1 — visible larger grains, a few tiny embedded pebbles, slightly darker ochre shading. LEFT edge is a flat vertical cut (no slope) down to open air, but textured as a natural rough exposed sand face (subtle unevenness/darker shading), not a smooth plane. RIGHT edge flat, full width (butts a Mid tile). Top edge flat, colour-blends into Layer 1's flat bottom (no seam). Bottom edge flat (sits on Layer 3). |
| 6 | `terrain_sand_hill_L2_mid.png` | 2 / Mid | Layer 2, MID. Same coarse tan sand. Both LEFT and RIGHT edges are flat vertical cuts at full width, no exposed-face texture needed (fully surrounded by more Layer 2). Top/bottom edges flat, matching tiles 5/7's top/bottom. |
| 7 | `terrain_sand_hill_L2_right.png` | 2 / Right | Layer 2, RIGHT edge. Mirror of tile 5. |
| 8 | `terrain_sand_hill_L3_left.png` | 3 / Left | Layer 3, LEFT edge. Denser, coarser, greyer-tan sand than Layer 2 — more visibly embedded stones/pebbles, craggier texture. LEFT edge flat vertical cut (no slope), rough natural exposed-rock-in-sand face. RIGHT edge flat, full width. Top edge flat, blends into Layer 2's bottom. Bottom edge flat (sits on Layer 4). |
| 9 | `terrain_sand_hill_L3_mid.png` | 3 / Mid | Layer 3, MID. Same dense/coarse material. Both sides flat full-width cuts, no exposed texture. |
| 10 | `terrain_sand_hill_L3_right.png` | 3 / Right | Layer 3, RIGHT edge. Mirror of tile 8. |
| 11 | `terrain_sand_hill_L4_left.png` | 4 / Left | Layer 4, LEFT edge. Dense grey-brown rock/stone, angular and sharper-edged than Layer 3. Top edge flat, blends into Layer 3's bottom (no seam). LEFT edge flat vertical cut (no slope, same convention as L2/L3), rough rocky exposed face. RIGHT edge flat, full width. BOTTOM: jagged sharp rock points/protrusions hang down into open air — irregular, craggy silhouette filling roughly 60-80% of the bottom band, NOT a flat cut, with transparent gaps between the points. |
| 12 | `terrain_sand_hill_L4_mid.png` | 4 / Mid | Layer 4, MID. Same rock material. Both LEFT and RIGHT edges flat full-width cuts (fully surrounded). Top flat, blends into Layer 3. BOTTOM: same jagged exposed-rock silhouette as tile 11. |
| 13 | `terrain_sand_hill_L4_right.png` | 4 / Right | Layer 4, RIGHT edge. Mirror of tile 11. |

### Adding a new material (e.g. grass hill)

1. Copy the materials table above, replace each layer's description (a grass-capped hill
   would plausibly reuse Layer 4 = rock unchanged, Layer 2/3 = soil/dirt, Layer 1 = grass —
   but that's a design call for whoever authors it, not dictated by this template).
2. Write the 13 tile-specific prompt cells the same way: geometry phrase from the base
   template's per-position rule (which edges are open/flat-cut) + this material's texture.
3. Generate, save under `Generated/<Material>HillSource/`, run `compose_hill_atlas.py
   --material <material>`.
4. `HillAutotile.cs` needs no changes; wiring `GridView`/`TileRegistry` is the same
   next-step as Sand's, per that file's header comment.

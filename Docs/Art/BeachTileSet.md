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

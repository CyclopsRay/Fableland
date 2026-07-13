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
| `platform.lifeguard_tower` | Platform | 4x8 | `Generated/platform_lifeguard_tower.png` | New art. v1 exposes the top balcony only; compound landings require compound shapes in the runtime-instantiation milestone. |
| `softvolume.palm_tree` | SoftVolume | 3x4 | `Generated/softvolume_palm_tree_v2.png` | Redrawn at 3:4 with dark ink only and no sticker outline. |
| `softvolume.cloud1x1` | SoftVolume | 1x1 | `Generated/softvolume_cloud_small.png` | Redrawn compact cloud; no sticker outline. |
| `softvolume.cloud2x1` | SoftVolume | 2x1 | `Generated/softvolume_cloud_medium.png` | Redrawn curled medium cloud; full footprint is enterable. |
| `softvolume.cloud3x2` | SoftVolume | 3x2 | `Generated/softvolume_cloud_large.png` | Redrawn asymmetrical large cloud; full footprint is enterable. |
| `deco.caution_sign` | Decoration | 1x2 | `Generated/deco_caution_sign.png` | New art; deliberately uses an exclamation pictogram rather than skull imagery. |
| `deco.sand_castle` | Decoration | 2x2 | `Legacy/deco_sand_castle.png` | Transferred from Unity; visual only. |
| `deco.sun` | Decoration | 2x2 | `Generated/deco_sun.png` | New art; intended for Farview, though Decoration remains legal on any layer. |
| `hazard.bonfire` | Hazard | 2x2 | `Generated/hazard_bonfire.png` | New art. Trigger circle is centered low over the stones, radius 0.75 m. |

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

The new terrain, bench, tower, caution sign, sun, and bonfire were generated with the
built-in image tool using flat chroma-key backgrounds, then converted to alpha PNGs.

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

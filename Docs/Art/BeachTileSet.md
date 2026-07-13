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
| `ground.sand` | Ground | 1x1 terrain cells | `Generated/terrain_beach_atlas.png` | Connected-look source approved; `terrain.beach_sand`; slice into a Godot terrain TileSet when autotile rendering lands. |
| `ground.grass` | Ground | 1x1 terrain cells | same atlas | Connected-look source approved; `terrain.coastal_grass`; shares the sand soil/outline language. |
| `platform.bench` | Platform | 4x2 | `Generated/platform_bench.png` | New art. One-way top is a 4 m x 0.25 m strip at y=0.75 m. |
| `platform.lifeguard_tower` | Platform | 4x8 | `Generated/platform_lifeguard_tower.png` | New art. v1 exposes the top balcony only; compound landings require compound shapes in the runtime-instantiation milestone. |
| `softvolume.palm_tree` | SoftVolume | 3x6 | `Legacy/softvolume_palm_tree.png` | Transferred from Unity; usable, but first candidate for regeneration because of its white sticker edge. |
| `softvolume.cloud1x1` | SoftVolume | 1x1 | `Legacy/softvolume_cloud_1x1.png` | Transferred and scaled from the approved cloud motif. Regenerate as a distinct compact silhouette later. |
| `softvolume.cloud2x1` | SoftVolume | 2x1 | `Legacy/softvolume_cloud_2x1.png` | Transferred from Unity. Full footprint is enterable. |
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
The terrain image is intentionally retained as an atlas source rather than pretending
that a hand-sliced region schema exists: `MapCreation.gdd` reserves Godot TileSet terrain
autotiling, and that milestone must define atlas coordinates/peering bits once, in data.

## Generation prompt lock

Use the following invariant block for any replacement:

> Hand-painted storybook game sprite, watercolor-and-ink feel, bold dark-brown outlines,
> limited cheerful beach palette, orthographic side view, crisp at game scale, no white
> sticker border, no baked floor, no cast shadow, and no text unless the asset spec says so.

The new terrain, bench, tower, caution sign, sun, and bonfire were generated with the
built-in image tool using flat chroma-key backgrounds, then converted to alpha PNGs.

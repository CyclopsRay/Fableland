#!/usr/bin/env python3
"""Composites 13 validated ground-hill tile images (any material —
sand, grass, rock, ...) into one pixel-perfect 4x4 atlas sheet.

Why this exists: an image model does not own atlas geometry. The preferred
pipeline seeds the 12 non-Peak cells from Tools/generate_hill_guide.py, validates
and slices that guide-conditioned sheet with Tools/slice_hill_grid.py, and
generates Peak separately. This compositor then owns the final 4x4 layout and
keeps its three intentionally-unused cells exactly transparent.

Grid layout (must match Scripts/MapCreation/Data/HillAutotile.cs, and is the
same for every material — HillAutotile.cs's classifier is material-agnostic):
  row = layer 1..4 (top to bottom), col = Left, Mid, Right, Peak (left to right).
  Only Layer 1 has a Peak tile; the other 3 cells in the Peak column are left
  fully transparent.

Usage:
    python3 Tools/compose_hill_atlas.py                    # material=sand (default)
    python3 Tools/compose_hill_atlas.py --material grass
    python3 Tools/compose_hill_atlas.py --material grass --source <dir> --out <file>
    python3 Tools/compose_hill_atlas.py --preview           # also writes a seam-check image

See Docs/Art/BeachTileSet.md's "Layered ground-hill autotile — base template" section
for the generation prompts and the per-material filename table (Sand hill is the
worked example; "Adding a new material" describes extending this to another one).

IMPORTANT — this script does NOT guarantee the 13 tiles actually connect. 13
independently-generated AI images are only loosely constrained by their text
prompts: exact edge height, exact color, and grain/texture pattern can all
drift between calls in ways no compositor can silently fix without visibly
warping hand-painted art. What this script CAN do is put the seams where you
can see them — pass --preview and inspect Left/Mid/Mid/Mid/Right rows (does
Mid tile against itself and against Left/Right?) and the Mid-per-layer column
(does Layer1's bottom match Layer2's top, etc?) before trusting the atlas.
"""

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("This script requires Pillow: pip install Pillow", file=sys.stderr)
    sys.exit(1)

COLS = ["left", "mid", "right", "peak"]
ROWS = ["L1", "L2", "L3", "L4"]


def build_grid(material: str) -> dict:
    """(row, col) -> filename stem for `material`. Only Layer 1 (row 0) has a
    peak entry; the other 3 (row, "peak") cells are intentionally absent and
    left blank in the composited atlas."""
    return {
        (r, c): f"terrain_{material}_hill_{ROWS[r]}_{COLS[c]}.png"
        for r in range(4)
        for c in range(4)
        if not (c == 3 and r != 0)
    }


def default_source(material: str) -> Path:
    return Path("Sprites/MapCreation/Beach/Generated") / f"{material.capitalize()}HillSource"


def default_out(material: str) -> Path:
    return Path("Sprites/MapCreation/Beach/Generated") / f"terrain_{material}_hill_atlas.png"


def default_preview(material: str) -> Path:
    return Path("Sprites/MapCreation/Beach/Generated") / f"terrain_{material}_hill_seam_preview.png"


LABEL_H = 14


def build_seam_preview(tiles: dict, cell_w: int, cell_h: int) -> Image.Image:
    """Lays out the actual adjacency cases that matter at runtime, at zero gap
    between tiles within a test, so any drift in edge height/color/texture
    between independently-generated tiles is visible as a real seam — not
    inferred, not blended away, just placed exactly like GridView would place
    it. Left section: each layer's Left-Mid-Mid-Mid-Right in a row (tests
    Mid's self-tiling and both edge transitions). Right column: Mid from
    Layer1 down to Layer4 stacked (tests every inter-layer transition)."""
    row_gap = 6
    section_gap = 16

    row_w = cell_w * 5
    rows_h = cell_h * 4 + row_gap * 3
    col_h = cell_h * 4

    canvas_w = row_w + section_gap + cell_w
    canvas_h = LABEL_H + max(rows_h, col_h)
    canvas = Image.new("RGBA", (canvas_w, canvas_h), (255, 255, 255, 255))
    draw = ImageDraw.Draw(canvas)

    for r in range(4):
        y = LABEL_H + r * (cell_h + row_gap)
        seq = [(r, 0), (r, 1), (r, 1), (r, 1), (r, 2)]  # Left, Mid, Mid, Mid, Right
        for i, key in enumerate(seq):
            im = tiles.get(key)
            x = i * cell_w
            if im is not None:
                canvas.paste(im, (x, y), im)
            else:
                draw.rectangle([x, y, x + cell_w - 1, y + cell_h - 1], outline=(255, 0, 0, 255), width=2)
        draw.text((0, y - LABEL_H + 2), f"L{r+1}: Left|Mid|Mid|Mid|Right", fill=(0, 0, 0, 255))

    col_x = row_w + section_gap
    for r in range(4):
        y = LABEL_H + r * cell_h
        im = tiles.get((r, 1))
        if im is not None:
            canvas.paste(im, (col_x, y), im)
        else:
            draw.rectangle([col_x, y, col_x + cell_w - 1, y + cell_h - 1], outline=(255, 0, 0, 255), width=2)
    draw.text((col_x, 2), "Mid L1-L4", fill=(0, 0, 0, 255))

    return canvas


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--material", default="sand", help="material name, e.g. sand, grass, rock (default: sand)")
    parser.add_argument("--source", type=Path, default=None, help="directory holding the 13 tile PNGs (default: Generated/<Material>HillSource/)")
    parser.add_argument("--out", type=Path, default=None, help="output atlas PNG path (default: Generated/terrain_<material>_hill_atlas.png)")
    parser.add_argument("--preview", action="store_true", help="also write a seam-check image (adjacency test strips) next to the atlas")
    parser.add_argument("--preview-out", type=Path, default=None, help="seam-preview PNG path (default: Generated/terrain_<material>_hill_seam_preview.png)")
    args = parser.parse_args()

    grid = build_grid(args.material)
    source = args.source if args.source is not None else default_source(args.material)
    out = args.out if args.out is not None else default_out(args.material)

    if not source.is_dir():
        print(f"error: source directory not found: {source}", file=sys.stderr)
        sys.exit(1)

    tiles = {}
    missing = []
    for (row, col), filename in grid.items():
        path = source / filename
        if not path.exists():
            missing.append((row, col, filename))
            continue
        tiles[(row, col)] = Image.open(path).convert("RGBA")

    # Peak (row 0, col 3) is the only tile allowed to be missing — e.g. while
    # testing the other 12 first, per BeachTileSet.md's staged-rollout note.
    # Everything else missing is still a hard error.
    required_missing = [(r, c, f) for (r, c, f) in missing if not (r == 0 and c == 3)]
    peak_missing = [(r, c, f) for (r, c, f) in missing if r == 0 and c == 3]

    if required_missing:
        print("error: missing tile file(s):", file=sys.stderr)
        for _, _, f in required_missing:
            print(f"  - {source / f}", file=sys.stderr)
        sys.exit(1)

    if peak_missing:
        print(f"note: {peak_missing[0][2]} not found — leaving Layer1/Peak blank (fine while testing the other 12).")

    sizes = {im.size for im in tiles.values()}
    if len(sizes) != 1:
        print(f"error: not all {len(tiles)} present tiles are the same pixel size:", file=sys.stderr)
        for (row, col), im in tiles.items():
            print(f"  {grid[(row, col)]}: {im.size}", file=sys.stderr)
        sys.exit(1)

    cell_w, cell_h = sizes.pop()
    atlas = Image.new("RGBA", (cell_w * 4, cell_h * 4), (0, 0, 0, 0))

    for (row, col), im in tiles.items():
        atlas.paste(im, (col * cell_w, row * cell_h))

    out.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(out)

    blank = 16 - len(tiles)
    print(f"Wrote {out} ({atlas.width}x{atlas.height}, cell {cell_w}x{cell_h})")
    blank_note = "the 3 unused Peak cells for Layer 2-4"
    if peak_missing:
        blank_note += ", plus Layer1/Peak"
    print(f"Filled {len(tiles)}/16 cells ({blank} left blank: {blank_note}).")

    if args.preview:
        preview_out = args.preview_out if args.preview_out is not None else default_preview(args.material)
        preview = build_seam_preview(tiles, cell_w, cell_h)
        preview_out.parent.mkdir(parents=True, exist_ok=True)
        preview.save(preview_out)
        print(f"Wrote {preview_out} — inspect every zero-gap boundary by eye before trusting the atlas.")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Composites the 13 separately-generated sand-hill tile images into one
pixel-perfect 4x4 atlas sheet.

Why this exists: asking an AI image model to draw one composite grid sheet
directly is unreliable — the existing terrain_beach_atlas.png (1536x1024,
sliced 7x6 by AutotileAtlas.cs) doesn't even divide evenly, which is almost
certainly why the earlier hand-drawn sheet came out unequally divided.
Generating 13 separate equal-size squares and compositing them here instead
is a deterministic, always-exact operation.

Grid layout (must match Scripts/MapCreation/Data/HillAutotile.cs):
  row = layer 1..4 (top to bottom), col = Left, Mid, Right, Peak (left to right).
  Only Layer 1 has a Peak tile; the other 3 cells in the Peak column are left
  fully transparent.

Usage:
    python3 Tools/compose_hill_atlas.py
    python3 Tools/compose_hill_atlas.py --source <dir> --out <file>

See Docs/Art/BeachTileSet.md's "Sand hill layered autotile" section for the
generation prompts and the full filename table.
"""

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("This script requires Pillow: pip install Pillow", file=sys.stderr)
    sys.exit(1)

COLS = ["left", "mid", "right", "peak"]
ROWS = ["L1", "L2", "L3", "L4"]

# (row, col) -> filename stem. Only Layer 1 (row 0) has a peak entry; the
# other 3 (row, "peak") cells are intentionally absent and left blank.
GRID = {
    (r, c): f"terrain_sand_hill_{ROWS[r]}_{COLS[c]}.png"
    for r in range(4)
    for c in range(4)
    if not (c == 3 and r != 0)
}

DEFAULT_SOURCE = Path("Sprites/MapCreation/Beach/Generated/SandHillSource")
DEFAULT_OUT = Path("Sprites/MapCreation/Beach/Generated/terrain_sand_hill_atlas.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE, help="directory holding the 13 tile PNGs")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="output atlas PNG path")
    args = parser.parse_args()

    if not args.source.is_dir():
        print(f"error: source directory not found: {args.source}", file=sys.stderr)
        sys.exit(1)

    tiles = {}
    missing = []
    for (row, col), filename in GRID.items():
        path = args.source / filename
        if not path.exists():
            missing.append(filename)
            continue
        tiles[(row, col)] = Image.open(path).convert("RGBA")

    if missing:
        print("error: missing tile file(s):", file=sys.stderr)
        for f in missing:
            print(f"  - {args.source / f}", file=sys.stderr)
        sys.exit(1)

    sizes = {im.size for im in tiles.values()}
    if len(sizes) != 1:
        print("error: not all 13 tiles are the same pixel size:", file=sys.stderr)
        for (row, col), filename in GRID.items():
            print(f"  {filename}: {tiles[(row, col)].size}", file=sys.stderr)
        sys.exit(1)

    cell_w, cell_h = sizes.pop()
    atlas = Image.new("RGBA", (cell_w * 4, cell_h * 4), (0, 0, 0, 0))

    for (row, col), im in tiles.items():
        atlas.paste(im, (col * cell_w, row * cell_h))

    args.out.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(args.out)

    print(f"Wrote {args.out} ({atlas.width}x{atlas.height}, cell {cell_w}x{cell_h})")
    print(f"Filled {len(tiles)}/16 cells (3 Peak cells for Layer 2-4 intentionally left blank).")


if __name__ == "__main__":
    main()

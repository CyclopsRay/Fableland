#!/usr/bin/env python3
"""Composites 13 separately-generated ground-hill tile images (any material —
sand, grass, rock, ...) into one pixel-perfect 4x4 atlas sheet.

Why this exists: asking an AI image model to draw one composite grid sheet
directly is unreliable — the existing terrain_beach_atlas.png (1536x1024,
sliced 7x6 by AutotileAtlas.cs) doesn't even divide evenly, which is almost
certainly why the earlier hand-drawn sheet came out unequally divided.
Generating 13 separate equal-size squares and compositing them here instead
is a deterministic, always-exact operation.

Grid layout (must match Scripts/MapCreation/Data/HillAutotile.cs, and is the
same for every material — HillAutotile.cs's classifier is material-agnostic):
  row = layer 1..4 (top to bottom), col = Left, Mid, Right, Peak (left to right).
  Only Layer 1 has a Peak tile; the other 3 cells in the Peak column are left
  fully transparent.

Usage:
    python3 Tools/compose_hill_atlas.py                    # material=sand (default)
    python3 Tools/compose_hill_atlas.py --material grass
    python3 Tools/compose_hill_atlas.py --material grass --source <dir> --out <file>

See Docs/Art/BeachTileSet.md's "Layered ground-hill autotile — base template" section
for the generation prompts and the per-material filename table (Sand hill is the
worked example; "Adding a new material" describes extending this to another one).
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


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--material", default="sand", help="material name, e.g. sand, grass, rock (default: sand)")
    parser.add_argument("--source", type=Path, default=None, help="directory holding the 13 tile PNGs (default: Generated/<Material>HillSource/)")
    parser.add_argument("--out", type=Path, default=None, help="output atlas PNG path (default: Generated/terrain_<material>_hill_atlas.png)")
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
            missing.append(filename)
            continue
        tiles[(row, col)] = Image.open(path).convert("RGBA")

    if missing:
        print("error: missing tile file(s):", file=sys.stderr)
        for f in missing:
            print(f"  - {source / f}", file=sys.stderr)
        sys.exit(1)

    sizes = {im.size for im in tiles.values()}
    if len(sizes) != 1:
        print("error: not all 13 tiles are the same pixel size:", file=sys.stderr)
        for (row, col), filename in grid.items():
            print(f"  {filename}: {tiles[(row, col)].size}", file=sys.stderr)
        sys.exit(1)

    cell_w, cell_h = sizes.pop()
    atlas = Image.new("RGBA", (cell_w * 4, cell_h * 4), (0, 0, 0, 0))

    for (row, col), im in tiles.items():
        atlas.paste(im, (col * cell_w, row * cell_h))

    out.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(out)

    print(f"Wrote {out} ({atlas.width}x{atlas.height}, cell {cell_w}x{cell_h})")
    print(f"Filled {len(tiles)}/16 cells (3 Peak cells for Layer 2-4 intentionally left blank).")


if __name__ == "__main__":
    main()

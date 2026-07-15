#!/usr/bin/env python3
"""Generate a deterministic 3x4 construction guide for a layered hill sheet.

The guide owns geometry; an image model only supplies the final painted finish.
Every cell is exactly square, layer changes land on exact row boundaries, and
the surface Mid tile is mechanically flat so it can repeat without waves.

Usage:
    python3 Tools/generate_hill_guide.py
    python3 Tools/generate_hill_guide.py --material sand --cell-size 384
    python3 Tools/generate_hill_guide.py --out path/to/guide.png
"""

import argparse
import random
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("This script requires Pillow: pip install Pillow", file=sys.stderr)
    sys.exit(1)


COLS = 3
ROWS = 4
MAGENTA = "#ff00ff"
GRID = "#22d3ee"
INK = "#4a2d22"
LAYER_COLORS = ("#e8c878", "#c99d50", "#9a8264", "#66594d")


def default_out(material: str) -> Path:
    return (
        Path("Sprites/MapCreation/Beach/Generated/Guides")
        / f"terrain_{material}_hill_guide.png"
    )


def default_peak_out(material: str) -> Path:
    return (
        Path("Sprites/MapCreation/Beach/Generated/Guides")
        / f"terrain_{material}_hill_L1_peak_guide.png"
    )


def surface_polygons(cell: int) -> list[list[tuple[int, int]]]:
    """Three L1 silhouettes with one shared, repeat-safe Mid height."""
    flat_y = round(cell * 0.18)
    open_y = round(cell * 0.28)
    return [
        [(0, open_y), (cell, flat_y), (cell, cell), (0, cell)],
        [(cell, flat_y), (2 * cell, flat_y), (2 * cell, cell), (cell, cell)],
        [
            (2 * cell, flat_y),
            (3 * cell, open_y),
            (3 * cell, cell),
            (2 * cell, cell),
        ],
    ]


def bottom_profile(cell: int, col: int) -> list[tuple[int, int]]:
    """Repeat-safe L4 bottom: every tile meets its neighbors at 76% height."""
    y0 = 3 * cell
    profile = (0.76, 0.82, 0.96, 0.84, 0.72, 0.90, 0.78, 0.76)
    if col == 2:
        profile = tuple(reversed(profile))
    x0 = col * cell
    return [
        (x0 + round(i * cell / (len(profile) - 1)), y0 + round(frac * cell))
        for i, frac in enumerate(profile)
    ]


def draw_texture(draw: ImageDraw.ImageDraw, cell: int) -> None:
    """Simple deterministic scratch marks communicate density, not final art."""
    rng = random.Random(731_204)
    for row in range(ROWS):
        y0 = row * cell
        count = (18, 30, 38, 24)[row]
        for _ in range(count):
            x = rng.randrange(8, COLS * cell - 8)
            if row == 0:
                y = rng.randrange(y0 + round(cell * 0.32), y0 + cell - 8)
            elif row == 3:
                y = rng.randrange(y0 + 8, y0 + round(cell * 0.68))
            else:
                y = rng.randrange(y0 + 8, y0 + cell - 8)
            radius = rng.randrange(1, 4 + row * 2)
            if row < 3:
                color = ("#b68f43", "#87673e", "#625848")[row]
                draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)
            else:
                color = "#403a34"
                points = [
                    (x - radius, y),
                    (x, y - radius),
                    (x + radius, y - 1),
                    (x + radius // 2, y + radius),
                    (x - radius, y + radius // 2),
                ]
                draw.line(points + [points[0]], fill=color, width=max(1, cell // 192))


def build_guide(cell: int, grid_width: int) -> Image.Image:
    width, height = COLS * cell, ROWS * cell
    image = Image.new("RGB", (width, height), MAGENTA)
    draw = ImageDraw.Draw(image)

    for polygon in surface_polygons(cell):
        draw.polygon(polygon, fill=LAYER_COLORS[0])

    draw.rectangle((0, cell, width - 1, 2 * cell - 1), fill=LAYER_COLORS[1])
    draw.rectangle((0, 2 * cell, width - 1, 3 * cell - 1), fill=LAYER_COLORS[2])

    y0 = 3 * cell
    for col in range(COLS):
        profile = bottom_profile(cell, col)
        polygon = [(col * cell, y0), ((col + 1) * cell, y0)] + list(reversed(profile))
        draw.polygon(polygon, fill=LAYER_COLORS[3])

    draw_texture(draw, cell)

    # Dark-brown scratches describe the final exterior contours. Cyan lines are
    # deliberately guide-only and must be removed by the image model.
    contour_w = max(2, cell // 96)
    for polygon in surface_polygons(cell):
        draw.line(polygon[:2], fill=INK, width=contour_w)
    for col in range(COLS):
        draw.line(bottom_profile(cell, col), fill=INK, width=contour_w)
    draw.line((0, round(cell * 0.28), 0, round(3.76 * cell)), fill=INK, width=contour_w)
    draw.line((width - 1, round(cell * 0.28), width - 1, round(3.76 * cell)), fill=INK, width=contour_w)

    for col in range(1, COLS):
        x = col * cell
        draw.line((x, 0, x, height - 1), fill=GRID, width=grid_width)
    for row in range(1, ROWS):
        y = row * cell
        draw.line((0, y, width - 1, y), fill=GRID, width=grid_width)

    return image


def build_peak_guide(cell: int) -> Image.Image:
    """Square L1 Peak seed: both sides open, dome matches L1 edge heights."""
    image = Image.new("RGB", (cell, cell), MAGENTA)
    draw = ImageDraw.Draw(image)
    flat_y = round(cell * 0.18)
    open_y = round(cell * 0.28)
    samples = 24
    surface = []
    for i in range(samples + 1):
        x = round(i * (cell - 1) / samples)
        normalized = abs((x / (cell - 1)) * 2.0 - 1.0)
        y = round(flat_y + (open_y - flat_y) * normalized ** 1.7)
        surface.append((x, y))
    polygon = surface + [(cell - 1, cell - 1), (0, cell - 1)]
    draw.polygon(polygon, fill=LAYER_COLORS[0])

    rng = random.Random(731_205)
    for _ in range(22):
        x = rng.randrange(8, cell - 8)
        # Staying below the lowest part of the dome guarantees no marks in air.
        y = rng.randrange(round(cell * 0.32), cell - 8)
        radius = rng.randrange(1, 4)
        draw.ellipse(
            (x - radius, y - radius, x + radius, y + radius),
            fill="#b68f43",
        )

    contour_w = max(2, cell // 96)
    draw.line(surface, fill=INK, width=contour_w)
    draw.line((0, open_y, 0, cell - 1), fill=INK, width=contour_w)
    draw.line((cell - 1, open_y, cell - 1, cell - 1), fill=INK, width=contour_w)
    return image


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--material", default="sand", help="material name used in the output filename")
    parser.add_argument("--cell-size", type=int, default=384, help="square cell size in pixels (default: 384)")
    parser.add_argument("--grid-width", type=int, default=4, help="construction-line width in pixels (default: 4)")
    parser.add_argument("--out", type=Path, default=None, help="guide PNG path")
    parser.add_argument("--peak-out", type=Path, default=None, help="square Layer-1 Peak guide PNG path")
    args = parser.parse_args()

    if args.cell_size < 64:
        parser.error("--cell-size must be at least 64 pixels")
    if args.grid_width < 1:
        parser.error("--grid-width must be at least 1 pixel")

    out = args.out if args.out is not None else default_out(args.material)
    peak_out = args.peak_out if args.peak_out is not None else default_peak_out(args.material)
    out.parent.mkdir(parents=True, exist_ok=True)
    peak_out.parent.mkdir(parents=True, exist_ok=True)
    image = build_guide(args.cell_size, args.grid_width)
    peak = build_peak_guide(args.cell_size)
    image.save(out)
    peak.save(peak_out)

    print(
        f"Wrote {out} ({image.width}x{image.height}, exact {COLS}:{ROWS} canvas, "
        f"{args.cell_size}x{args.cell_size} cells)."
    )
    print(f"Wrote {peak_out} ({peak.width}x{peak.height}, Layer-1 Peak guide).")
    print("Cyan lines are construction guides only; tell the image model not to render them.")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""List realm sprites whose paint touches the edge of their canvas.

A sprite's canvas is not a window (RealmStyleBible.md §2.7): a branch, a leaf or a hill
that runs into the edge of its PNG is cut there, and the cut shows in the map as a ruled
line in mid-air. Paint may meet an edge only on purpose:

  * a strip atlas, or anything that tiles (far ranges, a sea, a curtain that wraps);
  * a platform's walking line along the top (§2.2), and a deck that spans its footprint;
  * the BOTTOM of something that stands on the ground (not reported: too common to be news);
  * the top of something that hangs (stalactites, a leaf fringe), a section that stacks.

This tool cannot know the purpose, so it only reports: every sprite with opaque paint on its
left, right or top edge, and how much of that edge is covered. Read the list after painting
or repainting a set, and for each line either recognise the purpose or fix the painter
(`leaf(keep=…)`, `pine(keep=…)`, a dome instead of a slab, an alpha fade at the ends).

    python3 Tools/realm_art/audit_edges.py                 # every realm set
    python3 Tools/realm_art/audit_edges.py BambooMaze LoongsNest
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
TILES = REPO / "Assets" / "Sprites" / "Tiles"
OPAQUE = 0.30       # alpha above this counts as paint
NEWS = 0.005        # share of an edge that must be painted before it is worth a line


def audit(realm_dir: Path) -> list[str]:
    lines = []
    for path in sorted(realm_dir.glob("*/*.png")):
        stem = path.stem
        sidecar = path.with_suffix(".tile.json")
        if stem.endswith("_atlas") or not sidecar.exists():
            continue                        # an atlas tiles by construction; no sidecar = not a tile
        meta = json.loads(sidecar.read_text())
        if meta.get("fillFootprint"):
            continue                        # ground: its sprite is the palette thumbnail of an atlas
        alpha = np.asarray(Image.open(path).convert("RGBA"))[..., 3].astype(np.float32) / 255.0
        left, right, top = (float(np.mean(edge > OPAQUE)) for edge in (alpha[:, 0], alpha[:, -1], alpha[0]))
        if max(left, right, top) <= NEWS:
            continue
        size = meta.get("footprint", {})
        lines.append(f"  {stem:36s} {size.get('w', '?')}x{size.get('h', '?'):<3}  "
                     f"left {left:5.1%}   right {right:5.1%}   top {top:5.1%}   ({meta.get('role', '?')})")
    return lines


def main() -> int:
    wanted = sys.argv[1:] or sorted(p.name for p in TILES.iterdir() if p.is_dir())
    for realm in wanted:
        realm_dir = TILES / realm
        if not realm_dir.is_dir():
            print(f"{realm}: no such tile set under {TILES.relative_to(REPO)}", file=sys.stderr)
            return 2
        lines = audit(realm_dir)
        print(f"{realm}: {len(lines)} sprite(s) with paint on the left, right or top edge")
        for line in lines:
            print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main())

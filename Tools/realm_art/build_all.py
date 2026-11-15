#!/usr/bin/env python3
"""Repaint every realm tile set (sprites + `.tile.json` sidecars).

    /opt/homebrew/bin/python3 Tools/realm_art/build_all.py            # all realms
    /opt/homebrew/bin/python3 Tools/realm_art/build_all.py wulin      # one realm
    /opt/homebrew/bin/python3 Tools/realm_art/build_all.py --sheets DIR   # also write contact sheets

The painters are deterministic: re-running writes byte-identical PNGs, so a diff under
`Assets/Sprites/Tiles/` after a run is exactly the art change that was made. After adding
or resizing a sprite, update the realm's `Scripts/MapCreation/Data/TileSets/*TileSet.cs`
entry too; `TileSetAudit` (editor boot, and the headless MapContentCheck) fails loudly if
the registry, the sidecar and the PNG canvas disagree.
"""

from __future__ import annotations

import importlib
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

REALMS = ["starland", "wulin", "dungeon", "deserted", "loong", "loongnest", "horror", "forgotten"]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    sheets = None
    if "--sheets" in sys.argv:
        sheets = Path(sys.argv[sys.argv.index("--sheets") + 1])
        sheets.mkdir(parents=True, exist_ok=True)
        args = [a for a in args if Path(a) != sheets]
    for name in REALMS:
        if args and not any(a in name for a in args):
            continue
        t0 = time.time()
        module = importlib.import_module(name)
        print(f"== {name} ({module.REALM}) ==")
        module.export(str(sheets / f"{name}.png") if sheets else None)
        print(f"   {time.time() - t0:.1f}s")


if __name__ == "__main__":
    main()

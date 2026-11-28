#!/usr/bin/env python3
"""Compose the sub-realm patches and write them to `Maps/Realms/` (`Docs/Realms.gdd`, T50).

A patch is the world inside a sub-realm. The arena builds it far from the battlefield before a
fight and projects whoever is caught into it (anchor, anchor, ratio). Its rules differ from a
combat map's:

- the **character spawn marks the anchor** — the point the region's centre projects onto, a
  fighter standing on the patch floor there;
- the ground and art **go on past the exit border** (the region's radius times the ratio), so
  nothing marks the way out from inside;
- it is never dealt as a fight: `Maps/Realms/` is not read by the combat-map catalog (it reads the
  top of `Maps/` only). The markers a combat map must carry are still placed, so the same
  builder and checks can load it.

    python3 Tools/realm_maps/build_realm_patches.py
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mapkit import Map, REPO  # noqa: E402

OUT = REPO / "Maps" / "Realms"


def kh_black_prison():
    """Kingdom of Horror — inside the Black Prison (`Realms.gdd` section 12).

    INTENT: the prisoner's whole world turns into PumpKing's Dark Wood. The region is 8 m in the
    battlefield and the ratio is 2, so the exit border is 16 m (8 cells) from the anchor; the patch
    runs 30 m (15 cells) to either side, 26 m above the floor and 18 m of earth below it, so the
    whole exit circle is art: the window from outside shows only the horror world, and a
    prisoner's camera (held to the art) never sees the border or the world's edge.
    A flat floor carries everyone projected from the ground; three tiers of branches catch whoever
    was caught in the air (a body projected below the floor is lifted to it by the open-spot rule).
    Nothing here hurts: the prison's pumpkins are its only danger, and a fighter must be able to
    read the fight against the dark.
    """
    m = Map("realm_kh_black_prison", "Inside the Black Prison", "KH", 30, 22, "#0B1230",
            goals=(), crab=0, seagull=0)
    FLOOR = 13
    m.spawn(15, FLOOR)                                                   # the anchor: the patch's centre

    moon = m.farview("Moon", (0.03, 0.04), grid=(40, 36))
    moon.pin("deco.kh_moon", 13.0, 0.6)
    castle = m.farview("Far Castle", (0.10, 0.12), grid=(44, 36))
    castle.pin("deco.kh_castle_far", 4.2, 0.8)
    wood_far = m.farview("Far Wood", (0.20, 0.22), loop=True, grid=(16, 36))
    wood_far.put("deco.kh_forest_far", 0, round(wood_far.srow(4.8)))
    trees_far = m.farview("Trees", (0.55, 0.60), grid=(60, 40))
    trees_far.pin("deco.kh_tree", 1.5, 0.4)
    trees_far.pin("deco.kh_tree_b", 8.4, 1.2)
    trees_far.pin("deco.kh_tree_b", 14.8, 0.6)

    wood = m.farview("Wood", (1.0, 1.0), grid=(30, 22))
    for x, tree in ((0, "deco.kh_tree"), (9, "deco.kh_tree_b"), (20, "deco.kh_tree_b"), (25, "deco.kh_tree")):
        wood.stand(tree, x, FLOOR)
    for x in (6, 23):
        wood.stand("deco.kh_lantern", x, FLOOR)
    wood.stand("deco.kh_fence", 13, FLOOR)

    f = m.field()
    f.fill("ground.kh_earth", 0, FLOOR, 29, 21)
    # Branches in three tiers, three rows apart (a clean jump), across the middle third.
    for x, y in ((10, 10), (19, 10), (6, 7), (15, 7), (23, 7), (11, 4), (19, 4)):
        f.put("platform.kh_branch", x, y)

    # Markers a map must carry (never used: a patch is not a fight).
    f.put("spawn.respawn", 17, FLOOR - 1)
    f.put("spawn.enemy", 20, FLOOR - 1)
    f.put("goal.level", 10, FLOOR - 1)

    fg = m.closeview("Foreground")
    fg.put("deco.kh_fireflies", 8, 3)
    fg.put("deco.kh_fireflies", 22, 5)
    return m.finish()


PATCHES = [kh_black_prison]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for build in PATCHES:
        m = build()
        path = OUT / (build.__name__ + ".json")
        m.write(path)
        n = sum(len(layer.tiles) for layer in m.layers)
        print(f"{path.relative_to(REPO)}  {m.w}x{m.h}  {len(m.layers)} layers  {n} tiles")
        if "--ascii" in sys.argv:
            print(m.ascii())


if __name__ == "__main__":
    main()

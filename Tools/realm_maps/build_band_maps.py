#!/usr/bin/env python3
"""Compose each realm's TEST set of combat maps: one per altitude band, and a boss arena.

    python3 Tools/realm_maps/build_band_maps.py             # write every map
    python3 Tools/realm_maps/build_band_maps.py dungeon     # only files whose name contains this
    python3 Tools/realm_maps/build_band_maps.py --ascii     # also print each battlefield as text

A combat node carries `low-ground` / `sea-level` / `high-ground` from the overworld's
altitude field (`MapGDD.md` 2.1) and map selection matches it, so a realm is only fully
playable when each band - and its LV4 boss - has a map to be dealt. `build_maps.py` holds
the realms' showcase maps, one design argument each; THIS file holds the plain ones: the
same three shapes and one boss arena, dressed in each realm's own kit and backdrop, so that
every kind of node in every realm opens a valid map while the real ones are being designed.
They are deliberately easy: wide floors, clean jumps, one feature of the realm, no puzzle.

    sea level    a flat field two and a half screens wide, with ledges one jump up
    low ground   a basin: you start on a rim and the fight is in the pit, which holds
                 whatever the realm keeps in its low places (swamp, lava, mist, fog...)
    high ground  a mesa: three terraces, each one clean jump, and a summit
    boss arena   a floor, a tower on the east side the boss opens on, and a ledge half way
                 for it to retreat to - two perches (`goal.level`), authored tower first

Every map is 48 cells wide so the undersea realm can lay its water in whole tiles, and
obeys `RealmStyleBible.md` 8: sealed, three-row jumps, spawn nine cells from the west wall.
The Dark Wood has no generated boss arena: PumpKing's castle (`build_maps.py`) is his.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mapkit import Map, REPO, tiles  # noqa: E402

OUT = REPO / "Maps"
W = 48
SPAWN_X = 9


# ====================================================================== backdrops
# Each realm's own sky, reduced to what a plain map needs. The rows are START-SCREEN rows
# (0 = top of the screen at spawn, about 10.1 = bottom), the way the showcase maps place
# them - see mapkit.Layer.srow - so the same recipe sits right behind any spawn.

def _gh(m):
    return m.h + 18                      # far layers are taller than the map: they slide


def horizon(m, name, parallax, tile, screen_row, opacity=1.0):
    """One looping band, as wide as its tile."""
    info = tiles()[tile]
    layer = m.farview(name, parallax, loop=True, grid=(info["w"], _gh(m)), opacity=opacity)
    row = min(max(round(layer.srow(screen_row)), 0), layer.grid_h - info["h"])
    layer.put(tile, 0, row)
    return layer


def pinned(m, name, parallax, items):
    """Things that hold their place on screen: (tile, screen column, screen row[, flip])."""
    layer = m.farview(name, parallax, grid=(40, _gh(m)))
    for item in items:
        tile, col, row = item[0], item[1], item[2]
        info = tiles()[tile]
        x = min(max(round(layer.scol(col)), 0), layer.grid_w - info["w"])
        y = min(max(round(layer.srow(row)), 0), layer.grid_h - info["h"])
        layer.put(tile, x, y, flip=len(item) > 3 and item[3])
    return layer


def sky_beach(m):
    pinned(m, "Sailor's Horizon", (0.05, 0.06),
           [("deco.sun", 13.6, 0.5), ("softvolume.cloud3x2", 2.6, 1.0), ("softvolume.cloud2x1", 8.4, 2.6)])


def sky_starfield(m):
    horizon(m, "Dusk Sky", (0.02, 0.04), "deco.sl_horizon", -1.9)
    stars = m.farview("Stars", (0.03, 0.05), loop=True, grid=(16, _gh(m)))
    stars.put("deco.sl_starfield", 0, max(round(stars.srow(0.0)), 0))
    stars.put("deco.sl_starfield", 8, max(round(stars.srow(1.0)), 0), flip=True)
    pinned(m, "Moon & Constellation", (0.04, 0.06), [("deco.sl_constellation", 1.0, 0.3), ("deco.sl_moon", 14.2, 0.5)])
    horizon(m, "Far Dunes", (0.28, 0.30), "deco.sl_dunes_far", 4.9)
    horizon(m, "Near Dunes", (0.55, 0.55), "deco.sl_dunes_near", 5.5)


def sky_wulin(m):
    pinned(m, "Moon, Cranes & Colophon", (0.03, 0.04),
           [("deco.bm_moon", 11.0, 0.3), ("deco.bm_cranes", 3.0, 1.3), ("deco.bm_seal", 16.6, 3.4)])
    horizon(m, "Far Peaks", (0.08, 0.12), "deco.bm_peaks_far", 2.8)
    horizon(m, "Mist", (0.10, 0.15), "deco.bm_mist_band", 6.6, opacity=0.9)
    horizon(m, "Near Peaks", (0.20, 0.24), "deco.bm_peaks_mid", 4.6)


def sky_dungeon(m):
    horizon(m, "Lava Lake", (0.08, 0.10), "deco.hc_cavern_far", 5.3)
    wall = m.farview("Far Arcades", (0.34, 0.38), loop=True, grid=(8, _gh(m)))
    taken = []
    for screen_row in (-8.0, -2.0, 4.0):                 # three tiers: the layer sinks as you climb
        row = round(wall.srow(screen_row))
        if row >= 0 and row + 6 <= wall.grid_h and all(abs(row - other) >= 6 for other in taken):
            wall.put("deco.hc_wall_far", 0, row)
            taken.append(row)


def sky_ghost_town(m):
    horizon(m, "Printed Sky", (0.02, 0.04), "deco.td_sky", 1.2)
    pinned(m, "Sun & Clouds", (0.04, 0.06), [("deco.td_sun", 12.8, 0.2), ("deco.td_clouds", 5.2, 1.0)])
    horizon(m, "Far Buttes", (0.14, 0.20), "deco.td_mesas_far", 3.5)


def sky_loongs_nest(m):
    pinned(m, "Far Palace", (0.12, 0.14), [("deco.ln_palace_far", 5.0, 3.2)])
    horizon(m, "Seabed Ridges", (0.22, 0.26), "deco.ln_ridge_far", 6.4)
    # Open air, then the sea's skin on row 4 - the first row of water.
    skin = m.farview("Surface", (1.0, 1.0), grid=(m.w, m.h))
    for x in range(0, m.w, 16):
        skin.put("deco.ln_surface", x, 0)
    # The sea itself: one medium over the whole map, on a layer of its own so it takes no
    # battlefield cell (RealmStyleBible 8, "A sea is a layer"). Rows 0-3 stay air.
    sea = m.farview("Sea", (1.0, 1.0), grid=(m.w, m.h), collision=True)
    for y in range(4, m.h, 6):
        for x in range(0, m.w, 8):
            sea.put("softvolume.ln_water", x, y)


def sky_dark_wood(m):
    pinned(m, "Moon", (0.03, 0.04), [("deco.kh_moon", 11.2, 0.4)])
    pinned(m, "Far Castle", (0.10, 0.12), [("deco.kh_castle_far", 8.2, 0.4)])
    horizon(m, "Far Wood", (0.20, 0.22), "deco.kh_forest_far", 4.8)


def sky_forgotten(m):
    sky = m.farview("Stars", (0.02, 0.02), loop=True, grid=(8, _gh(m)))
    top = round(sky.srow(-1.0))
    for k in range(3):
        if 0 <= top + 4 * k and top + 4 * k + 4 <= sky.grid_h:
            sky.put("deco.tf_stars", 0, top + 4 * k)
    horizon(m, "Far City", (0.08, 0.10), "deco.tf_city_far", 4.6)
    pinned(m, "Great Pyramid", (0.14, 0.16), [("deco.tf_pyramid_far", 6.4, 1.0)])
    horizon(m, "Canopy", (0.30, 0.32), "deco.tf_jungle", 7.0)


# ========================================================================= realms
# floor / mass: the ground you walk on and the solid a wall, a terrace or a tower is built
# of. ledge: the realm's one-way platform. soft: a volume to dive into, standing on a
# surface. bite: its floor hazard. props: scenery for the layer behind the fighters.
# pit: what the realm keeps in its low ground (a function, below).

REALMS = [
    dict(world="VK", slug="beach", realm="Beach", canvas="#6DB8D8", sky=sky_beach,
         floor="ground.sand", mass="ground.grass", ledge="platform.sun_lounger",
         soft="softvolume.palm_tree", bite="hazard.bonfire", props=("deco.sand_castle", "deco.caution_sign")),
    dict(world="SL", slug="starfield", realm="Starfield", canvas="#17133A", sky=sky_starfield,
         floor="ground.sl_sand", mass="ground.sl_sandstone", ledge="platform.sl_slab",
         soft="softvolume.sl_stardust", bite="hazard.sl_fallen_star", props=("deco.sl_obelisk", "deco.sl_broken_column")),
    dict(world="BM", slug="wulin", realm="Wulin", canvas="#E4E9E8", sky=sky_wulin,
         floor="ground.bm_rock", mass="ground.bm_rock", ledge="platform.bm_roof_small",
         soft="softvolume.bm_bamboo_stalk", bite="hazard.bm_stakes", props=("deco.bm_lantern", "deco.bm_column")),
    dict(world="HC", slug="dungeon", realm="Dungeon", canvas="#0E0D12", sky=sky_dungeon,
         floor="ground.hc_brick", mass="ground.hc_basalt", ledge="platform.hc_plank",
         soft="softvolume.hc_cobweb", bite="hazard.hc_spikes", props=("deco.hc_pillar", "deco.hc_banner", "deco.hc_skulls")),
    dict(world="TD", slug="ghost_town", realm="Ghost Town", canvas="#F1E3C4", sky=sky_ghost_town,
         floor="ground.td_mesa", mass="ground.td_timber", ledge="platform.td_balcony",
         soft="softvolume.td_tumbleweed", bite="hazard.td_cactus", props=("deco.td_barrel", "deco.td_signpost", "deco.td_skull")),
    dict(world="PL", slug="loongs_nest", realm="Loong's Nest", canvas="#0C1230", sky=sky_loongs_nest,
         floor="ground.ln_reef", mass="ground.ln_palace", ledge="platform.ln_shell",
         soft="softvolume.ln_kelp", bite="hazard.ln_urchin", props=("deco.ln_coral", "deco.ln_lantern", "deco.ln_column")),
    dict(world="KH", slug="horror", realm="Dark Wood", canvas="#111B3D", sky=sky_dark_wood,
         floor="ground.kh_earth", mass="ground.kh_stone", ledge="platform.kh_branch",
         soft="softvolume.kh_bramble", bite="hazard.kh_trap", props=("deco.kh_lantern", "deco.kh_fence", "deco.kh_tree_b")),
    dict(world="TF", slug="forgotten", realm="Forgotten City", canvas="#0C0C22", sky=sky_forgotten,
         floor="ground.tf_mud", mass="ground.tf_stone", ledge="platform.tf_slab",
         soft="softvolume.tf_reeds", bite="hazard.tf_obsidian", props=("deco.tf_stele", "deco.tf_axolotl", "deco.tf_tree")),
]


def size(tile):
    info = tiles()[tile]
    return info["w"], info["h"]


def seal(f, realm, h, floor):
    """Solid floor edge to edge and walls to the top row: there is no kill plane."""
    f.fill(realm["floor"], 0, floor, W - 1, h - 1)
    f.fill(realm["mass"], 0, 0, 1, floor - 1)
    f.fill(realm["mass"], W - 2, 0, W - 1, floor - 1)


def ledge(f, realm, x, row, count=1):
    """`count` of the realm's one-way platform end to end, their walking line on `row`.
    Returns the column just past the last one."""
    w, _ = size(realm["ledge"])
    for i in range(count):
        f.put(realm["ledge"], x + i * w, row)
    return x + count * w


def dress(back, realm, spots):
    """Stand the realm's props on (column, surface row) spots, on the layer behind the fight."""
    for i, (x, surface) in enumerate(spots):
        tile = realm["props"][i % len(realm["props"])]
        w, h = size(tile)
        if x + w <= back.grid_w and surface - h >= 0:
            back.stand(tile, x, surface)


def markers(f, respawns, nests, goals):
    for x, y in respawns:
        f.put("spawn.respawn", x, y)
    for x, y in nests:
        f.put("spawn.enemy", x, y)
    for x, y in goals:
        f.put("goal.level", x, y)


# ---- what each realm keeps in its low ground -------------------------------------------
# Called with the pit's span (columns x0..x1 inclusive, sixteen cells) and the row its
# floor's surface is. The bed's markers stand on columns x0, x0+4, x1-4 and x1; a pit's
# furniture keeps off those, and off the ledge over the middle (row bed-3, columns
# x0+6..x0+9).

def pit_beach(f, x0, x1, bed):                 # a hollow where the palms grow: two to climb into
    for x in (x0 + 1, x1 - 3):
        f.stand("softvolume.palm_tree", x, bed)


def pit_starfield(f, x0, x1, bed):             # stardust settles where the ground dips
    for x in (x0 + 2, x1 - 4):
        f.put("softvolume.sl_stardust", x, bed - 4)


def pit_wulin(f, x0, x1, bed):                 # mist on the valley floor, bamboo growing out of it
    f.stand("softvolume.bm_mist", x0 + 5, bed)
    f.stand("softvolume.bm_bamboo_mid", x1 - 3, bed)
    f.stand("softvolume.bm_bamboo_stalk", x0 + 2, bed)


def pit_dungeon(f, x0, x1, bed):               # the low ground of a dungeon is lava
    f.carve(x0 + 6, bed, x0 + 9, bed)          # a four-cell pool let into the floor, rock under it
    f.fill("hazard.hc_lava", x0 + 6, bed, x0 + 9, bed)
    f.put("platform.hc_plank", x0 + 5, bed - 3)    # and a catwalk over it
    f.put("platform.hc_plank", x0 + 8, bed - 3)


def pit_ghost_town(f, x0, x1, bed):            # a dry wash: dust hangs in it
    for x in (x0 + 2, x1 - 4):
        f.put("softvolume.td_dust", x, bed - 3)


def pit_loongs_nest(f, x0, x1, bed):           # a trench: kelp, and bubbles to ride out on
    for x in (x0 + 1, x1 - 2):
        f.stand("softvolume.ln_kelp", x, bed)
    f.stand("softvolume.ln_bubbles", x0 + 9, bed)       # beside the ledge, not under it


def pit_dark_wood(f, x0, x1, bed):             # ground fog fills the hollow, bramble at its edge
    f.stand("softvolume.kh_fog", x0 + 5, bed)
    f.stand("softvolume.kh_bramble", x1 - 3, bed)


def pit_forgotten(f, x0, x1, bed):             # swamp: the canal itself is a layer (low_ground)
    f.stand("softvolume.tf_reeds", 2, bed - 6)     # cattails on the west rim, against the wall
    f.stand("softvolume.tf_reeds", 44, bed - 6)    # and the east


PITS = {"VK": pit_beach, "SL": pit_starfield, "BM": pit_wulin, "HC": pit_dungeon, "TD": pit_ghost_town,
        "PL": pit_loongs_nest, "KH": pit_dark_wood, "TF": pit_forgotten}


# ========================================================================= shapes

def sea_level(realm):
    """A flat field with ledges one jump up: nothing to learn, somewhere to stand."""
    h, floor = 22, 19
    m = Map(f"{realm['slug']}_sea_level_{realm['world'].lower()}", f"{realm['realm']} · Sea Level",
            realm["world"], W, h, realm["canvas"], terrain="sea-level")
    m.spawn(SPAWN_X, floor)
    realm["sky"](m)
    back = m.farview("Scenery", (1.0, 1.0), grid=(W, h))
    dress(back, realm, [(4, floor), (15, floor), (30, floor), (41, floor)])

    f = m.field()
    seal(f, realm, h, floor)
    lw, _ = size(realm["ledge"])
    low = floor - 3                                       # one clean jump above the field
    ledge(f, realm, 12, low)
    ledge(f, realm, 24 - lw // 2, low - 3)                # and one above that, in the middle
    ledge(f, realm, 36 - lw, low)

    sw, _ = size(realm["soft"])
    f.stand(realm["soft"], 20, floor)                     # one thing of the realm's to dive into
    f.stand(realm["bite"], 32, floor)                     # and one that bites
    markers(f,
            respawns=((5, floor - 1), (23, floor - 1), (42, floor - 1)),
            nests=((17, floor - 1), (27, floor - 1), (38, floor - 1), (13, low - 1), (36 - lw + 1, low - 1)),
            goals=((7, floor - 1), (14, low - 1), (24, low - 4), (29, floor - 1), (36 - lw, low - 1), (44, floor - 1)))
    return m.finish()


def low_ground(realm):
    """A basin. You start on the west rim; the fight is in the pit."""
    h, rim, step, bed = 28, 19, 22, 25
    m = Map(f"{realm['slug']}_low_ground_{realm['world'].lower()}", f"{realm['realm']} · Low Ground",
            realm["world"], W, h, realm["canvas"], terrain="low-ground")
    m.spawn(SPAWN_X, rim)
    realm["sky"](m)
    back = m.farview("Scenery", (1.0, 1.0), grid=(W, h))
    dress(back, realm, [(4, rim), (38, rim), (43, rim)])
    x0, x1 = 16, 31                                       # the pit's floor
    if realm["world"] == "TF":
        # The Forgotten City's low ground is swamp: a canal you wade in, bank to bank. Water
        # is laid on a layer of its own so it takes no battlefield cell (RealmStyleBible 8).
        canal = m.farview("Canal", (1.0, 1.0), grid=(W, h), collision=True)
        for x in range(x0, x1, 4):
            canal.put("softvolume.tf_water", x, bed - 2)

    f = m.field()
    f.fill(realm["floor"], 0, bed, W - 1, h - 1)
    f.fill(realm["mass"], 0, 0, 1, rim - 1)
    f.fill(realm["mass"], W - 2, 0, W - 1, rim - 1)
    f.fill(realm["floor"], 0, rim, 12, bed - 1)           # west rim
    f.fill(realm["floor"], W - 13, rim, W - 1, bed - 1)   # east rim
    f.fill(realm["floor"], 13, step, 15, bed - 1)         # one step down each side: 3 rows, a clean jump back
    f.fill(realm["floor"], W - 16, step, W - 14, bed - 1)

    PITS[realm["world"]](f, x0, x1, bed)
    if realm["world"] != "HC":                            # the dungeon's pit has its catwalk already
        ledge(f, realm, x0 + 6, step)                     # a ledge over the middle, level with the steps
    markers(f,
            respawns=((5, rim - 1), (42, rim - 1), (14, step - 1)),
            nests=((x0, bed - 1), (x1, bed - 1), (37, rim - 1), (W - 15, step - 1), (11, rim - 1)),
            goals=((7, rim - 1), (x0 + 4, bed - 1), (x1 - 4, bed - 1), (x0 + 7, step - 1), (40, rim - 1), (13, step - 1)))
    return m.finish()


def high_ground(realm):
    """A mesa: three terraces, each one clean jump, and a summit to hold."""
    h, floor = 28, 25
    t1, t2, top = floor - 3, floor - 6, floor - 9
    slope = realm["world"] == "KH"                        # the Dark Wood has a slope block
    m = Map(f"{realm['slug']}_high_ground_{realm['world'].lower()}", f"{realm['realm']} · High Ground",
            realm["world"], W, h, realm["canvas"], terrain="high-ground")
    m.spawn(5 if slope else SPAWN_X, floor)               # (the slope starts where the others spawn)
    realm["sky"](m)
    back = m.farview("Scenery", (1.0, 1.0), grid=(W, h))
    dress(back, realm, [(2, floor), (41, floor), (24, top)])

    f = m.field()
    if slope:
        # Its mesa is climbed from the west on foot: three blocks, 12 m along and 6 m up,
        # ending where the first terrace begins. Laid before the floor (RealmStyleBible 8).
        f.slope("ground.kh_slope", 8, floor, 3, fill=realm["mass"], floor=floor - 1)
    f.fill(realm["mass"], 14, t1, 35, floor - 1)          # first terrace
    f.fill(realm["mass"], 20, t2, 31, t1 - 1)             # second
    f.fill(realm["mass"], 23, top, 28, t2 - 1)            # the summit
    seal(f, realm, h, floor)

    ledge(f, realm, 37, t1)                               # the east side is climbed by ledges
    ledge(f, realm, 33, t2)
    ledge(f, realm, 10, t2)                               # and a lookout over the west approach
    f.stand(realm["bite"], 36, floor)                     # something that bites, under the east ledge
    f.stand(realm["soft"], 42, floor)                     # and one thing to dive into, by the east wall
    markers(f,
            respawns=((4, floor - 1), (40, floor - 1), (16, t1 - 1)),
            nests=((2, floor - 1), (45, floor - 1), (38, t1 - 1), (21, t2 - 1), (25, top - 1)),
            goals=((7, floor - 1), (39, floor - 1), (18, t1 - 1), (34, t1 - 1), (30, t2 - 1), (24, top - 1), (27, top - 1)))
    return m.finish()


def boss_arena(realm):
    """A floor, and a tower the boss opens on.

    Two perches, authored tower first: the tower's top, six rows (12 m) over the floor on
    the east side - as far from the spawn as the map goes, so the boss opens there - and a
    ledge half way down, nine cells west, for it to leave to when the player gets into its
    face (under 20 m apart: one Soul Form flight, RealmStyleBible 8). The tower is climbed
    by two three-row steps. The boss's two helpers arrive at half health, from the nests.
    """
    h, floor = 28, 25
    stair, top = floor - 3, floor - 6
    m = Map(f"{realm['slug']}_boss_arena_{realm['world'].lower()}", f"{realm['realm']} · Boss Arena",
            realm["world"], W, h, realm["canvas"], goals=("boss",), hardship=(4,))
    m.spawn(SPAWN_X, floor)
    realm["sky"](m)
    back = m.farview("Scenery", (1.0, 1.0), grid=(W, h))
    dress(back, realm, [(4, floor), (15, floor), (22, floor)])

    f = m.field()
    f.fill(realm["mass"], 38, top, W - 3, floor - 1)      # the tower
    f.fill(realm["mass"], 34, stair, 37, floor - 1)       # its step
    seal(f, realm, h, floor)
    lw, _ = size(realm["ledge"])
    perch_x = 29
    ledge(f, realm, perch_x, stair)                       # the second perch: a ledge, level with the step
    ledge(f, realm, 14, stair)                            # and cover for the approach

    f.put("goal.level", 39, top - 1)                      # perch 1: the tower, at its west edge
    f.put("goal.level", perch_x + 1, stair - 1)           # perch 2: the ledge, nine cells (18 m) west of it
    markers(f,
            respawns=((5, floor - 1), (12, floor - 1)),
            nests=((20, floor - 1), (31, floor - 1), (15, stair - 1)),
            goals=())
    return m.finish()


SHAPES = (("sea_level", sea_level), ("low_ground", low_ground), ("high_ground", high_ground), ("boss_arena", boss_arena))


def plan():
    """(file stem, builder, realm) for every map this script owns."""
    for realm in REALMS:
        for shape, build in SHAPES:
            if shape == "boss_arena" and realm["world"] == "KH":
                continue                                   # his castle is the Dark Wood's boss map
            yield f"{realm['slug']}_{shape}", build, realm


def main():
    want = [a for a in sys.argv[1:] if not a.startswith("--")]
    show = "--ascii" in sys.argv
    OUT.mkdir(exist_ok=True)
    for stem, build, realm in plan():
        if want and not any(w in stem for w in want):
            continue
        m = build(realm)
        path = OUT / (stem + ".json")
        m.write(path)
        n = sum(len(layer.tiles) for layer in m.layers)
        print(f"{path.relative_to(REPO)}  {m.w}x{m.h}  {len(m.layers)} layers  {n} tiles  [{m.terrain}] {','.join(m.goals)}")
        if show:
            print(m.ascii())


if __name__ == "__main__":
    main()

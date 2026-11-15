#!/usr/bin/env python3
"""Compose the built-in realm combat maps and write them to `Maps/`.

One function per map. Each opens with the design intent it is built to deliver (who the
arena favours and why), then composes it. See `Docs/Art/RealmStyleBible.md` for the
realm briefs and `Tools/realm_maps/mapkit.py` for the authoring kit and parallax rule.

    python3 Tools/realm_maps/build_maps.py            # write every map
    python3 Tools/realm_maps/build_maps.py starfield  # only maps whose name contains this
    python3 Tools/realm_maps/build_maps.py --ascii    # also print each battlefield as text

Every character must be able to stand anywhere without falling out of the world (there
is no kill plane), so each battlefield is sealed: solid floor edge to edge, solid walls
to the top row on both sides.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mapkit import Map, REPO  # noqa: E402

OUT = REPO / "Maps"


# ================================================================== Starland


def starfield_long_horizon():
    """Starland — "The Long Horizon".

    INTENT: vast, level and nearly empty, so range decides the fight. Five and a half
    screens wide, one tall. The floor is unbroken dune; the only cover is a handful of
    colossal stone monuments set far apart, and the high ground is theirs: a walk-through
    trilithon, a low mastaba, and a stepped great pyramid whose summit sees both ends of
    the map. Floating slabs and constellations are sparse on purpose — a brawler has to
    cross a long open floor under fire to reach anyone.
    """
    m = Map("starfield_long_horizon_sl", "Starfield · The Long Horizon", "SL", 96, 20, "#17133A")
    FLOOR = 17
    m.spawn(10, FLOOR)

    # -- far to near --------------------------------------------------------------
    # Rows below are START-SCREEN rows (0 = top of the screen at spawn, ~10.1 = bottom); the
    # dune floor sits at screen row ~7.1. See mapkit.Layer.srow for why far layers are composed so.
    GROUND_LINE = 7.1

    sky = m.farview("Dusk Sky", (0.02, 0.04), loop=True, grid=(8, 26))
    sky.put("deco.sl_horizon", 0, round(sky.srow(-1.9)))                # horizon line just above the dunes

    stars = m.farview("Stars", (0.03, 0.05), loop=True, grid=(16, 26))
    stars.put("deco.sl_starfield", 0, round(stars.srow(0.0)))
    stars.put("deco.sl_starfield", 8, round(stars.srow(1.0)), flip=True)

    # A layer this slow is effectively pinned to the screen: compose it like a picture frame.
    heaven = m.farview("Moon & Constellation", (0.04, 0.06), grid=(30, 26))
    heaven.pin("deco.sl_constellation", 1.0, 0.3)
    heaven.pin("deco.sl_moon", 14.2, 0.5)

    # Far things use up the journey fast (a 10-cell pyramid at parallax 0.2 spans 50 cells
    # of travel), so each range gets its own layer and only two or three silhouettes.
    small = m.farview("Small Pyramids", (0.10, 0.14), grid=(30, 26))
    small.seen_from("deco.sl_pyramid_small", 3, 6.7)
    small.seen_from("deco.sl_sun", 52, 7.4)                             # the last of the sun, low on the sand
    small.seen_from("deco.sl_pyramid_small", 97, 6.7, flip=True)

    great = m.farview("Great Pyramids", (0.22, 0.26), grid=(40, 26))
    great.seen_from("deco.sl_pyramid", 27, 7.0)
    great.seen_from("deco.sl_pyramid", 85, 7.0, flip=True)

    dunes_far = m.farview("Far Dunes", (0.28, 0.30), loop=True, grid=(16, 28))
    dunes_far.put("deco.sl_dunes_far", 0, round(dunes_far.srow(4.9)))

    dunes_near = m.farview("Near Dunes", (0.55, 0.55), loop=True, grid=(16, 28))
    dunes_near.put("deco.sl_dunes_near", 0, round(dunes_near.srow(5.5)))

    mid = m.farview("Standing Stones", (0.82, 0.90), grid=(96, 24))
    for cam, tile in ((13, "deco.sl_star_gate"), (31, "deco.sl_obelisk"), (46, "deco.sl_obelisk"),
                      (77, "deco.sl_star_gate"), (90, "deco.sl_obelisk")):
        mid.seen_from(tile, cam, GROUND_LINE)

    # -- battlefield ---------------------------------------------------------------
    f = m.field()
    f.fill("ground.sl_sand", 0, FLOOR, 95, 19)                          # the long floor
    f.fill("ground.sl_sand", 30, FLOOR - 1, 43, FLOOR - 1)              # one low swell of dune
    f.fill("ground.sl_sand", 74, FLOOR - 1, 86, FLOOR - 1)

    f.fill("ground.sl_sandstone", 0, 0, 2, FLOOR - 1)                   # west and east gate towers
    f.fill("ground.sl_sandstone", 93, 0, 95, FLOOR - 1)

    # Trilithon: walk through it, or take the lintel as a perch.
    f.fill("ground.sl_sandstone", 20, 13, 20, FLOOR - 1)
    f.fill("ground.sl_sandstone", 24, 13, 24, FLOOR - 1)
    f.fill("ground.sl_sandstone", 19, 12, 25, 12)
    f.put("platform.sl_slab", 15, 14)

    # Mastaba: low cover on the dune swell.
    f.fill("ground.sl_sandstone", 35, FLOOR - 3, 40, FLOOR - 2)

    # The Great Stepped Pyramid — three tiers and a shrine; the summit is the best seat in the realm.
    f.fill("ground.sl_sandstone", 50, 15, 70, FLOOR - 1)
    f.fill("ground.sl_sandstone", 53, 13, 67, 14)
    f.fill("ground.sl_sandstone", 56, 11, 64, 12)
    f.fill("ground.sl_sandstone", 59, 9, 61, 10)

    # A few things in the sky, kept few.
    f.put("platform.sl_constellation", 43, 12)
    f.put("platform.sl_slab", 73, 12)
    f.put("platform.sl_constellation", 79, 9)
    f.put("platform.sl_slab", 87, 13)
    f.put("softvolume.sl_stardust", 28, 6)
    f.put("softvolume.sl_stardust", 46, 5)
    f.put("softvolume.sl_stardust", 83, 4)

    f.stand("hazard.sl_fallen_star", 15, FLOOR)
    f.stand("hazard.sl_fallen_star", 46, FLOOR)
    f.stand("hazard.sl_fallen_star", 89, FLOOR)

    for x, y in ((6, FLOOR - 1), (44, FLOOR - 1), (60, 8), (91, FLOOR - 1)):
        f.put("spawn.respawn", x, y)
    for x, y in ((18, FLOOR - 1), (29, 5), (38, FLOOR - 4), (47, 4), (72, FLOOR - 1), (84, 3), (91, 12)):
        f.put("spawn.enemy", x, y)
    for x, y in ((12, FLOOR - 1), (22, 11), (37, FLOOR - 4), (60, 7), (66, 12), (80, FLOOR - 2), (88, 12)):
        f.put("goal.level", x, y)

    # -- foreground ----------------------------------------------------------------
    fg = m.closeview("Foreground")
    for x in (27, 48, 71):
        fg.stand("deco.sl_broken_column", x, FLOOR)
    return m.finish()


# ====================================================================== Wulin


def wulin_bamboo_sea():
    """Banboo Maze — "Bamboo Sea Pagoda".

    INTENT: a vertical arena where height and concealment beat reach — an assassin's map.
    Two and a half screens wide, four tall. Everything that reaches upward is bamboo, and
    all bamboo is soft volume: a fighter can dive into a culm, hang in the leaves, chain
    jumps straight up it and drop out on a target. The solid ground is only the forest
    floor, a few ink-rock ledges and islets, and the thin one-way roofs and bracket beams
    of a five-tier pagoda that zig-zag up the middle. There are no long sight lines:
    you never see more than a screen, and something tall always stands between you and it.
    """
    m = Map("wulin_bamboo_sea_bm", "Wulin · Bamboo Sea Pagoda", "BM", 44, 40, "#EFE7D4")
    FLOOR = 37
    m.spawn(14, FLOOR)
    GROUND_LINE = 7.1                     # start-screen row of the forest floor

    # -- far to near ---------------------------------------------------------------
    # A painting's colophon: the sun, a skein of cranes and the red seal are pinned to the
    # frame, so they hold their place on screen like marks on the paper itself.
    frame = m.farview("Sun, Cranes & Seal", (0.03, 0.04), grid=(40, 44))
    frame.pin("deco.bm_sun", 12.6, 0.5)
    frame.pin("deco.bm_cranes", 3.2, 1.4)
    frame.pin("deco.bm_seal", 16.6, 3.6)

    # The ranges sink as you climb: by the top of the pagoda you look over their crowns.
    far = m.farview("Far Peaks", (0.08, 0.12), loop=True, grid=(16, 46))
    far.put("deco.bm_peaks_far", 0, round(far.srow(2.4)))

    mist = m.farview("Mist", (0.10, 0.15), loop=True, grid=(16, 46), opacity=0.9)
    mist.put("deco.bm_mist_band", 0, round(mist.srow(6.2)))

    near = m.farview("Near Peaks", (0.16, 0.22), loop=True, grid=(16, 46))
    near.put("deco.bm_peaks_mid", 0, round(near.srow(3.4)))

    # The bamboo sea itself: a curtain that tiles upward for the whole climb, twice, the
    # nearer stand darker. Tiled tall enough that the frame never runs out of bamboo.
    stand_far = m.farview("Bamboo Sea (far)", (0.40, 0.42), loop=True, grid=(8, 64), opacity=0.55)
    stand_near = m.farview("Bamboo Sea (near)", (0.62, 0.64), loop=True, grid=(8, 64), tint="#B9B4A8")
    for layer, order in ((stand_far, ("deco.bm_bamboo_curtain", "deco.bm_bamboo_curtain_b")),
                         (stand_near, ("deco.bm_bamboo_curtain_b", "deco.bm_bamboo_curtain"))):
        top = round(layer.srow(-2.0)) - 32
        for k in range(6):
            if 0 <= top + 8 * k and top + 8 * k + 8 <= layer.grid_h:
                layer.put(order[0], 0, top + 8 * k)
                layer.put(order[1], 4, top + 8 * k)

    # The pagoda's body — columns and lattice bays — stands just behind the battlefield.
    hall = m.farview("Pagoda Hall", (1.0, 1.0), grid=(44, 40))
    for roof_row, cols, bays in ((31, (19, 24), (20, 22)), (27, (20, 23), (21,)), (23, (20, 23), (21,)),
                                 (19, (20, 23), (21,)), (15, (20, 23), (21,))):
        for x in cols:
            hall.put("deco.bm_column", x, roof_row + 1)
        for x in bays:
            hall.put("deco.bm_lattice", x, roof_row + 1)

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    f.fill("ground.bm_rock", 0, FLOOR, 43, 39)                          # forest floor
    f.fill("ground.bm_rock", 0, 0, 1, FLOOR - 1)                        # cliff walls
    f.fill("ground.bm_rock", 42, 0, 43, FLOOR - 1)
    f.fill("ground.bm_rock", 17, 35, 26, 36)                            # pagoda podium

    # Five roofs, four rows apart — one row more than a clean jump — with a bracket beam
    # half-way between each pair, alternating sides: the plain route up is a zig-zag.
    f.put("platform.bm_roof_wide", 19, 31)
    f.put("platform.bm_roof_small", 20, 27)
    f.put("platform.bm_roof_small", 20, 23)
    f.put("platform.bm_roof_small", 20, 19)
    f.put("platform.bm_roof_wide", 19, 15)
    for x, y in ((27, 33), (15, 29), (25, 25), (15, 21), (25, 17)):
        f.put("platform.bm_beam", x, y)

    # Ink-rock ledges and islets: the only other solid footing.
    f.fill("ground.bm_rock", 2, 18, 4, 19)                              # west ledge
    f.fill("ground.bm_rock", 9, 13, 13, 14)                             # west islet
    f.fill("ground.bm_rock", 2, 7, 5, 8)                                # west high ledge
    f.fill("ground.bm_rock", 39, 22, 41, 23)                            # east ledge
    f.fill("ground.bm_rock", 30, 17, 34, 18)                            # east islet
    f.fill("ground.bm_rock", 38, 9, 41, 10)                             # east high ledge

    # Bamboo — every culm a soft volume. The two giants are the fast lanes off the floor.
    f.stand("softvolume.bm_bamboo_young", 3, FLOOR)
    f.stand("softvolume.bm_bamboo_giant", 5, FLOOR)
    f.stand("softvolume.bm_bamboo_tall", 10, FLOOR)
    f.stand("softvolume.bm_bamboo_tall", 31, FLOOR)
    f.stand("softvolume.bm_bamboo_young", 34, FLOOR)
    f.stand("softvolume.bm_bamboo_giant", 36, FLOOR)
    f.stand("softvolume.bm_bamboo_young", 40, FLOOR)
    f.stand("softvolume.bm_bamboo_mid", 3, 18)                          # on the west ledge
    f.stand("softvolume.bm_bamboo_tall", 11, 13)                        # on the west islet, to the summit
    f.stand("softvolume.bm_bamboo_young", 2, 7)
    f.stand("softvolume.bm_bamboo_mid", 40, 22)                         # on the east ledge
    f.stand("softvolume.bm_bamboo_tall", 32, 17)                        # on the east islet
    f.stand("softvolume.bm_bamboo_young", 40, 9)
    f.put("softvolume.bm_mist", 13, 24)                                 # a drift of mist bridging grove and pagoda
    f.put("softvolume.bm_mist", 26, 10)

    f.stand("hazard.bm_stakes", 7, FLOOR)                               # between the giant and the tall culm
    f.stand("hazard.bm_stakes", 27, FLOOR)                              # under the first beam: miss it, land on these

    for x, y in ((16, FLOOR - 1), (21, 34), (22, 30), (21, 14)):
        f.put("spawn.respawn", x, y)
    for x, y in ((9, FLOOR - 1), (29, FLOOR - 1), (2, 17), (39, 21), (13, 12), (30, 16), (24, 14)):
        f.put("spawn.enemy", x, y)
    for x, y in ((12, FLOOR - 1), (33, FLOOR - 1), (23, 34), (16, 28), (26, 24), (10, 12), (31, 16),
                 (4, 6), (38, 8), (20, 14)):
        f.put("goal.level", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    for x, y in ((2, 20), (9, 15), (30, 19), (38, 24), (2, 9), (37, 11)):   # leaves hanging under every ledge
        fg.put("deco.bm_leaf_fringe", x, y)
    for x in (8, 22, 33):                                                   # and in from the top of the scroll
        fg.put("deco.bm_leaf_fringe", x, 0)
    fg.stand("deco.bm_lantern", 18, 35)
    fg.stand("deco.bm_lantern", 25, 35)
    fg.stand("deco.bm_lantern", 4, 7)
    return m.finish()


# ==================================================================== Dungeon


def dungeon_hollow_gaol():
    """HollowCastle — "The Hollow Gaol".

    INTENT: crowded, dark and hostile — every step is a decision. A sealed box barely two
    screens wide and two and a half tall, cut into three low storeys around a central
    shaft. Nothing is far from anything: ceilings are five cells high, perches are short,
    and the routes between storeys are chains and cobwebs you have to commit to. The floor
    is two lava pools and a spike bed with strips of safe stone between; the catwalks over
    the lava are the fastest crossings and the worst places to be hit. Close-quarters
    fighters thrive, and a fire-loving one most of all.
    """
    m = Map("dungeon_hollow_gaol_hc", "Dungeon · The Hollow Gaol", "HC", 40, 24, "#0E0D12")
    FLOOR = 21
    m.spawn(4, FLOOR)

    # -- far to near ---------------------------------------------------------------
    cavern = m.farview("Lava Lake", (0.08, 0.10), loop=True, grid=(8, 30))
    cavern.put("deco.hc_cavern_far", 0, round(cavern.srow(5.3)))

    wall = m.farview("Far Arcades", (0.34, 0.38), loop=True, grid=(8, 34))
    for screen_row in (-8.0, -2.0, 4.0):                                # three tiers: the layer sinks as you climb
        wall.put("deco.hc_wall_far", 0, round(wall.srow(screen_row)))

    back = m.farview("Back Wall", (1.0, 1.0), grid=(40, 24))
    back.put("deco.hc_arch", 18, 17)                                    # the gate everyone is fighting in front of
    for x, y in ((4, 17), (33, 17), (11, 10), (26, 10), (6, 3), (31, 3)):
        back.put("deco.hc_torch", x, y)
    for x, y in ((9, 2), (26, 2), (16, 9), (21, 9)):
        back.put("deco.hc_stalactites", x, y)
    for x, y in ((9, 16), (28, 16)):                                    # dripstone over the lava
        back.put("deco.hc_stalactites", x, y, flip=(x > 20))
    back.put("deco.hc_banner", 8, 9).put("deco.hc_banner", 31, 9)
    back.put("deco.hc_light_shaft", 19, 2)                              # cold daylight down the central shaft
    for x in (6, 10, 29, 33):                                           # piers carrying the upper storey
        back.put("deco.hc_pillar", x, 9)

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    f.fill("ground.hc_brick", 0, 0, 39, 1)                              # ceiling
    f.fill("ground.hc_brick", 0, 0, 1, 23)                              # walls
    f.fill("ground.hc_brick", 38, 0, 39, 23)
    f.fill("ground.hc_brick", 0, FLOOR, 39, 23)                         # floor

    for x0, x1 in ((9, 14), (24, 30)):                                  # two lava pools in basalt basins
        f.carve(x0 - 1, FLOOR, x1 + 1, 23)
        f.fill("ground.hc_basalt", x0 - 1, FLOOR, x0 - 1, 23)
        f.fill("ground.hc_basalt", x1 + 1, FLOOR, x1 + 1, 23)
        f.fill("ground.hc_basalt", x0, 23, x1, 23)
        f.fill("hazard.hc_lava", x0, FLOOR, x1, 22)

    # Middle storey: two galleries with a gap by each wall and the open shaft between them.
    f.fill("ground.hc_brick", 4, 14, 14, 15)
    f.fill("ground.hc_brick", 25, 14, 35, 15)
    f.put("platform.hc_grate", 18, 14)                                  # gantry across the shaft

    # Upper storey: three islands joined by catwalks.
    f.fill("ground.hc_brick", 5, 7, 11, 8)
    f.fill("ground.hc_brick", 17, 7, 22, 8)
    f.fill("ground.hc_brick", 28, 7, 34, 8)
    f.put("platform.hc_plank", 13, 7)
    f.put("platform.hc_plank", 24, 7)

    # Over the lava: the quick way across, and the worst place to be caught.
    f.put("platform.hc_plank", 10, 18)
    f.put("platform.hc_grate", 26, 18)
    f.put("platform.hc_ledge", 2, 18)
    f.put("platform.hc_ledge", 36, 18)

    # The ways up are things you climb INTO. Chains hang in the shaft and by the walls.
    f.put("softvolume.hc_chains", 14, 16)                               # under each gallery's shaft end
    f.put("softvolume.hc_chains", 25, 16)
    f.put("softvolume.hc_chains", 5, 9)                                 # under the outer ends of the upper storey
    f.put("softvolume.hc_chains", 34, 9)
    # Cobwebs fill the gaps by the walls: a sticky short cut between storeys.
    f.put("softvolume.hc_cobweb", 2, 15)
    f.put("softvolume.hc_cobweb", 36, 15)
    f.put("softvolume.hc_cobweb", 2, 2)
    f.put("softvolume.hc_cobweb", 36, 2)

    for x in (18, 19, 20, 21):                                          # spike bed before the gate
        f.stand("hazard.hc_spikes", x, FLOOR)
    for x in (8, 9, 30, 31):                                            # and in the galleries
        f.stand("hazard.hc_spikes", x, 14)

    for x, y in ((6, FLOOR - 1), (16, FLOOR - 1), (34, FLOOR - 1), (6, 13), (19, 6)):
        f.put("spawn.respawn", x, y)
    for x, y in ((33, FLOOR - 1), (12, 13), (27, 13), (8, 6), (31, 6), (19, 13), (20, 3)):
        f.put("spawn.enemy", x, y)
    for x, y in ((22, FLOOR - 1), (11, 17), (27, 17), (5, 13), (34, 13), (9, 6), (30, 6), (20, 6)):
        f.put("goal.level", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    for x0, x1 in ((9, 14), (24, 30)):                                  # heat rising off each pool, over everyone
        fg.put("deco.hc_lava_glow_end", x0 - 2, 18)
        x = x0
        while x + 2 <= x1 + 1:
            fg.put("deco.hc_lava_glow", x, 18)
            x += 2
        fg.put("deco.hc_lava_glow_end", x, 18, flip=True)
    fg.stand("deco.hc_pillar", 17, FLOOR)                               # piers you fight behind
    fg.stand("deco.hc_pillar", 23, 14)
    fg.stand("deco.hc_skulls", 5, FLOOR)
    fg.stand("deco.hc_skulls", 33, FLOOR)
    fg.stand("deco.hc_skulls", 12, 14)
    return m.finish()


# ================================================================= Ghost Town


def ghost_town_main_street():
    """The Deserted — "Main Street at Noon".

    INTENT: a duelling ground. Three and a half screens of one straight street between
    four clapboard buildings, with the open crossing in the middle. Everything is two
    storeys and everything is mid-range: from any spot there is a clear line down the
    street, and a balcony, awning or hanging sign within one jump — so the fight is a
    running exchange of angles between street, ledge and roof, never a long siege and
    never a brawl in a corner. Skirmishers and duellists own it.
    """
    m = Map("ghost_town_main_street_td", "Ghost Town · Main Street at Noon", "TD", 64, 18, "#F1E3C4")
    FLOOR = 15
    m.spawn(4, FLOOR)                     # under the hotel awning, at the west end of the street
    GROUND_LINE = 7.1

    # -- far to near ---------------------------------------------------------------
    sky = m.farview("Printed Sky", (0.02, 0.04), loop=True, grid=(16, 24))
    sky.put("deco.td_sky", 0, round(sky.srow(1.2)))

    noon = m.farview("Sun & Clouds", (0.04, 0.06), grid=(30, 24))
    noon.pin("deco.td_sun", 12.8, 0.2)
    noon.pin("deco.td_clouds", 5.2, 1.0)

    buttes = m.farview("Far Buttes", (0.14, 0.20), loop=True, grid=(16, 28))
    buttes.put("deco.td_mesas_far", 0, round(buttes.srow(3.5)))

    town = m.farview("Edge of Town", (0.72, 0.85), grid=(64, 22))
    for cam, tile in ((6, "deco.td_windmill"), (31, "deco.td_water_tower"), (46, "deco.td_saloon"), (60, "deco.td_windmill")):
        town.seen_from(tile, cam, GROUND_LINE, flip=(cam == 60))

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    f.fill("ground.td_mesa", 0, FLOOR, 63, 17)                          # the street
    f.fill("ground.td_mesa", 0, 0, 1, FLOOR - 1)                        # canyon walls close the ends
    f.fill("ground.td_mesa", 62, 0, 63, FLOOR - 1)

    # Four buildings. Each is solid clapboard with a flat roof you can stand on and
    # one-way ledges on both faces, three rows up — exactly one clean jump from the street.
    f.fill("ground.td_timber", 6, 9, 13, FLOOR - 1)                     # hotel
    f.put("platform.td_awning", 3, 12)
    f.put("platform.td_balcony", 14, 12)

    f.fill("ground.td_timber", 21, 11, 26, FLOOR - 1)                   # general store (low)
    f.put("platform.td_awning", 18, 13)
    f.put("platform.td_sign", 27, 12)

    f.fill("ground.td_timber", 37, 10, 44, FLOOR - 1)                   # saloon
    f.put("platform.td_balcony", 34, 12)
    f.put("platform.td_awning", 45, 12)

    f.fill("ground.td_timber", 51, 9, 58, FLOOR - 1)                    # bank
    f.put("platform.td_balcony", 48, 12)
    f.put("platform.td_awning", 59, 12)

    f.put("platform.td_sign", 31, 8)                                    # a sign swinging over the crossing
    f.put("softvolume.td_dust", 30, 4)                                  # dust devils wander above the roofs
    f.put("softvolume.td_dust", 15, 5)
    f.put("softvolume.td_dust", 46, 5)
    f.stand("softvolume.td_tumbleweed", 31, FLOOR)                      # and tumbleweed drifts in the street
    f.stand("softvolume.td_tumbleweed", 16, FLOOR)
    f.stand("softvolume.td_tumbleweed", 47, FLOOR)

    for x in (2, 29, 34, 61):                                           # saguaros: the only cover in the open is the kind that bites
        f.stand("hazard.td_cactus", x, FLOOR)

    for x, y in ((5, FLOOR - 1), (10, 8), (40, 9), (54, 8)):
        f.put("spawn.respawn", x, y)
    for x, y in ((19, FLOOR - 1), (24, 10), (32, 7), (36, FLOOR - 1), (50, FLOOR - 1), (56, 8), (60, FLOOR - 1)):
        f.put("spawn.enemy", x, y)
    for x, y in ((4, 11), (15, 11), (23, 10), (30, FLOOR - 1), (35, 11), (42, 9), (49, 11), (59, FLOOR - 1)):
        f.put("goal.level", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    # Dress the buildings. Windows and doors lie wholly over solid wall, so they never hide
    # a fighter; the parapets stand on the roof edges and DO — a roof is cover from the street.
    for x0, top, door_x in ((6, 9, 9), (21, 11, 23), (37, 10, 40), (51, 9, 54)):
        fg.stand("deco.td_door", door_x, FLOOR)
        for wx in (x0 + 1, x0 + (6 if top < 11 else 4)):
            fg.put("deco.td_window", wx, top + (1 if top < 11 else 0) + (0 if top < 11 else 0))
    fg.stand("deco.td_parapet", 8, 9)
    fg.stand("deco.td_parapet", 39, 10)
    fg.stand("deco.td_parapet", 53, 9)
    fg.stand("deco.td_barrel", 14, FLOOR)
    fg.stand("deco.td_barrel", 27, FLOOR)
    fg.stand("deco.td_barrel", 45, FLOOR)
    fg.stand("deco.td_skull", 33, FLOOR)
    fg.stand("deco.td_signpost", 18, FLOOR)
    fg.stand("deco.td_fence", 59, FLOOR)
    return m.finish()


# ================================================================ Cloud Court


def cloud_court_heavens():
    """Palace of LOOING — "The Cloud Court".

    INTENT: an aerial arena, where the ground is the wrong place to be. Three screens wide
    and three tall, almost all of it air. The terrace floor at the bottom is safe and
    useless — everything worth having hangs above it — and solid footing is a handful of
    small terrace islands and thin lotus pads, bridges and one roof. What fills the space
    is cloud: every auspicious cloud is a soft volume that refreshes a jump, so the
    battle is carried cloud to cloud in three dimensions. Whoever is at home in the air
    rules here, and frost pearls sit on the best perches to stop anyone settling.
    """
    m = Map("cloud_court_heavens_pl", "Cloud Court · The Heavens", "PL", 56, 30, "#2A1B4A")
    FLOOR = 27
    m.spawn(10, FLOOR)
    GROUND_LINE = 7.1

    # -- far to near ---------------------------------------------------------------
    sky = m.farview("Heaven's Night", (0.02, 0.03), loop=True, grid=(8, 40))
    sky.put("deco.pl_sky", 0, round(sky.srow(0.3)))

    pearl = m.farview("Night Pearl", (0.04, 0.05), grid=(30, 40))
    pearl.pin("deco.pl_moon", 12.4, 0.5)

    palace = m.farview("Far Palace", (0.12, 0.16), grid=(30, 44))
    palace.seen_from("deco.pl_palace_far", 28, 7.4)

    # The cloud sea shares its enamels with the clouds you fight in, so both ranges are
    # tinted down toward the night: the backdrop recedes and the playable clouds stay brightest.
    sea_far = m.farview("Sea of Clouds (far)", (0.20, 0.24), loop=True, grid=(16, 46), tint="#6F64A6")
    sea_far.put("deco.pl_cloud_sea", 0, round(sea_far.srow(5.4)))
    sea_near = m.farview("Sea of Clouds (near)", (0.46, 0.50), loop=True, grid=(16, 50), tint="#9B8FC6")
    sea_near.put("deco.pl_cloud_sea", 0, round(sea_near.srow(6.3)))

    court = m.farview("Court", (1.0, 1.0), grid=(56, 30))
    court.put("deco.pl_moon_gate", 26, 11)                              # the gate behind the dais
    for x in (24, 31):
        court.put("deco.pl_dragon_column", x, 10)
    for x, y in ((7, 16), (10, 16), (45, 16), (48, 16)):                # banners on the side islands
        court.put("deco.pl_banner", x, y)
    for x, y in ((11, 5), (44, 5)):
        court.put("deco.pl_banner", x, y)

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    f.fill("ground.pl_terrace", 0, FLOOR, 55, 29)                       # courtyard: safe, and a long way down
    f.fill("ground.pl_terrace", 0, 0, 1, FLOOR - 1)
    f.fill("ground.pl_terrace", 54, 0, 55, FLOOR - 1)

    f.fill("ground.pl_terrace", 23, 15, 32, 16)                         # the dais, heart of the court
    f.put("platform.pl_roof", 25, 9)                                    # its roof: the highest footing there is
    f.fill("ground.pl_terrace", 6, 19, 11, 20)                          # side islands
    f.fill("ground.pl_terrace", 44, 19, 49, 20)
    f.fill("ground.pl_terrace", 10, 8, 13, 9)                           # high perches
    f.fill("ground.pl_terrace", 42, 8, 45, 9)

    f.put("platform.pl_bridge", 13, 18)                                 # moon bridges from the islands toward the dais
    f.put("platform.pl_bridge", 39, 18)
    for x, y in ((19, 21), (35, 21), (4, 14), (50, 14), (17, 6), (37, 6)):
        f.put("platform.pl_lotus", x, y)

    # Cloud lanes. Low clouds are one jump off the courtyard; from there it is cloud all the way up.
    for x, y in ((14, 23), (39, 23), (25, 22), (17, 14), (36, 14), (20, 9), (33, 9), (26, 4)):
        f.put("softvolume.pl_cloud", x, y)
    for x, y in ((3, 23), (49, 23), (5, 10), (47, 10)):
        f.put("softvolume.pl_cloud_trail", x, y, flip=(x > 28))
    for x, y in ((21, 18), (33, 18), (14, 12), (40, 12), (22, 6), (32, 6)):
        f.put("softvolume.pl_cloud_small", x, y)

    for x, y in ((23, 15), (31, 15), (11, 8), (43, 8)):                 # frost pearls on the perches worth holding
        f.stand("hazard.pl_frost_pearl", x, y)
    f.stand("hazard.pl_frost_pearl", 27, FLOOR)                         # and one in the middle of the safe floor

    for x, y in ((7, FLOOR - 1), (47, FLOOR - 1), (27, 14), (9, 18)):
        f.put("spawn.respawn", x, y)
    for x, y in ((20, FLOOR - 1), (36, FLOOR - 1), (7, 18), (48, 18), (10, 7), (45, 7), (27, 8)):
        f.put("spawn.enemy", x, y)
    for x, y in ((14, FLOOR - 1), (41, FLOOR - 1), (26, 14), (29, 14), (10, 18), (46, 18), (13, 7), (42, 7), (28, 8), (15, 17)):
        f.put("goal.level", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    for x, y in ((23, 17), (32, 17), (6, 21), (11, 21), (44, 21), (49, 21)):   # lanterns hung beneath the islands
        fg.put("deco.pl_lantern", x, y)
    return m.finish()


MAPS = [starfield_long_horizon, wulin_bamboo_sea, dungeon_hollow_gaol, ghost_town_main_street, cloud_court_heavens]


def main():
    want = [a for a in sys.argv[1:] if not a.startswith("--")]
    show = "--ascii" in sys.argv
    OUT.mkdir(exist_ok=True)
    for build in MAPS:
        if want and not any(w in build.__name__ for w in want):
            continue
        m = build()
        path = OUT / (build.__name__ + ".json")
        m.write(path)
        n = sum(len(layer.tiles) for layer in m.layers)
        print(f"{path.relative_to(REPO)}  {m.w}x{m.h}  {len(m.layers)} layers  {n} tiles")
        if show:
            print(m.ascii())


if __name__ == "__main__":
    main()

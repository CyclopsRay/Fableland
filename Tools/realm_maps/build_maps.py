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

    INTENT: a vertical arena where height and concealment beat reach — an assassin's map —
    and where the landscape itself is a weapon. Two and a half screens wide, four tall.
    Everything that reaches upward is bamboo or pine, and all of it is soft volume: a
    fighter dives into a culm, hangs in the leaves, chains jumps straight up it and drops
    out on a target. Solid footing is only the forest floor, ink-rock ledges and islets, the
    one-way roofs and bracket beams of a five-tier pagoda that zig-zag up the middle, and a
    stone bridge thrown from the west islet to the pagoda's crown.

    The east third belongs to a WATERFALL: twenty-four rows of falling water that carries
    whoever enters it straight down and cannot be climbed. Whoever holds its head — the lip
    of the east cliff — can knock an enemy into it and be rid of them, and anyone crossing
    it gives up height. It is also the fastest way down for whoever wants one.

    The scroll behind is mostly bare paper: one towering peak with pines, a slender second,
    a thread of far water between them, roofs in the mist at their feet, a boat.
    """
    m = Map("wulin_bamboo_sea_bm", "Wulin · Bamboo Sea Pagoda", "BM", 44, 40, "#E4E9E8")
    FLOOR = 37
    m.spawn(14, FLOOR)

    # -- far to near ---------------------------------------------------------------
    # The painter's marks — the moon left as bare paper, a skein of cranes, the inscription
    # and seal — are pinned to the frame: they hold their place like marks on the sheet.
    frame = m.farview("Moon, Cranes & Colophon", (0.03, 0.04), grid=(40, 44))
    frame.pin("deco.bm_moon", 11.0, 0.3)       # in the bare paper between the far fall and the pagoda's eaves
    frame.pin("deco.bm_cranes", 3.0, 1.3)
    frame.pin("deco.bm_seal", 16.6, 3.4)

    # The ranges sink as you climb: by the top of the pagoda you look over their crowns.
    far = m.farview("Far Peaks", (0.08, 0.12), loop=True, grid=(16, 46))
    far.put("deco.bm_peaks_far", 0, round(far.srow(2.8)))

    mist = m.farview("Mist", (0.10, 0.15), loop=True, grid=(16, 46), opacity=0.9)
    mist.put("deco.bm_mist_band", 0, round(mist.srow(6.6)))

    # The subject of the scroll. It starts with its crown above the frame and comes down
    # into view as you climb, so the summit of the pagoda faces the summit of the peak.
    peak = m.farview("The Peak", (0.14, 0.17), grid=(60, 64))
    peak.pin("deco.bm_peak", 0.0, -4.4)
    peak.pin("deco.bm_peak_b", 12.6, -1.6)
    # Far layers hold one tile per cell like any other, so what stands in front of the
    # peaks — a thread of far water, roofs in the mist at their feet, a boat — is its own
    # layer, moving with them.
    valley = m.farview("Valley", (0.14, 0.17), grid=(60, 64))
    valley.pin("deco.bm_fall_far", 9.0, -0.4)
    valley.pin("deco.bm_village", 5.0, 7.8)
    valley.pin("deco.bm_boat", 12.4, 8.9)

    near = m.farview("Near Peaks", (0.20, 0.24), loop=True, grid=(16, 46))
    near.put("deco.bm_peaks_mid", 0, round(near.srow(4.6)))

    # A grove, not a wall: one clump of far bamboo every ten rows of the climb, and the
    # rest of the sheet left empty. (留白 — what is not painted is the picture too.)
    grove = m.farview("Bamboo Grove", (0.42, 0.44), loop=True, grid=(16, 72), opacity=0.85)
    for k, x in enumerate((2, 10, 5, 12, 0, 8)):
        row = round(grove.srow(1.2)) - 10 * k
        if row >= 0 and row + 9 <= grove.grid_h:
            grove.put("deco.bm_bamboo_far", x, row)

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
    f.fill("ground.bm_rock", 9, 13, 12, 14)                             # west islet
    f.fill("ground.bm_rock", 2, 7, 7, 8)                                # west high ledge
    f.fill("ground.bm_rock", 39, 22, 41, 23)                            # east ledge
    f.fill("ground.bm_rock", 28, 17, 31, 18)                            # east islet
    f.fill("ground.bm_rock", 35, 11, 41, 12)                            # the lip of the east cliff: head of the fall

    # The bridge: from the west islet to the pagoda's crown — the second way to the top,
    # and an open one. Its deck is level with the top roof.
    f.put("platform.bm_bridge", 13, 15)

    # The waterfall drops off the lip and falls twenty-four rows to the forest floor. It
    # is a current, not a medium: it carries, it does not hold, and it cannot be climbed.
    for y in (11, 15, 19, 23, 27, 31):
        f.put("softvolume.bm_waterfall", 33, y)

    # Bamboo and pine — every one a soft volume. The two giants are the fast lanes up.
    f.stand("softvolume.bm_bamboo_young", 3, FLOOR)
    f.stand("softvolume.bm_bamboo_giant", 5, FLOOR)
    f.stand("softvolume.bm_bamboo_tall", 10, FLOOR)
    f.stand("softvolume.bm_bamboo_tall", 30, FLOOR)
    f.stand("softvolume.bm_bamboo_giant", 36, FLOOR)
    f.stand("softvolume.bm_bamboo_young", 40, FLOOR)
    f.stand("softvolume.bm_bamboo_mid", 3, 18)                          # on the west ledge
    f.stand("softvolume.bm_bamboo_tall", 10, 13)                        # on the west islet, to the summit
    f.stand("softvolume.bm_pine", 2, 7)                                 # a pine on the west high ledge
    f.stand("softvolume.bm_bamboo_mid", 40, 22)                         # on the east ledge
    f.stand("softvolume.bm_bamboo_tall", 29, 17)                        # on the east islet
    f.stand("softvolume.bm_pine", 38, 11)                               # a pine at the head of the fall
    f.put("softvolume.bm_mist", 13, 24)                                 # a drift of mist bridging grove and pagoda
    f.put("softvolume.bm_mist", 22, 10)                                 # and one above the crown

    f.stand("hazard.bm_stakes", 7, FLOOR)                               # between the giant and the tall culm
    f.stand("hazard.bm_stakes", 27, FLOOR)                              # under the first beam: miss it, land on these

    for x, y in ((16, FLOOR - 1), (21, 34), (22, 30), (21, 14)):
        f.put("spawn.respawn", x, y)
    for x, y in ((9, FLOOR - 1), (29, FLOOR - 1), (2, 17), (39, 21), (12, 12), (28, 16), (24, 14)):
        f.put("spawn.enemy", x, y)
    for x, y in ((12, FLOOR - 1), (32, FLOOR - 1), (23, 34), (16, 28), (26, 24), (9, 12), (31, 16),
                 (6, 6), (36, 10), (20, 14)):
        f.put("goal.level", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    for x, y in ((2, 20), (9, 15), (28, 19), (38, 24), (2, 9), (37, 13)):   # leaves hanging under every ledge
        fg.put("deco.bm_leaf_fringe", x, y)
    for x in (8, 22):                                                       # and in from the top of the scroll
        fg.put("deco.bm_leaf_fringe", x, 0)
    fg.put("deco.bm_spray", 32, 35)                                         # where the fall lands
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
    # World tag "CC": the Palace of LOOING itself is under the sea now (Loong's Nest, below).
    # Cloud Court keeps its map, playable from the editor, until it is given a world.
    m = Map("cloud_court_heavens_pl", "Cloud Court · The Heavens", "CC", 56, 30, "#2A1B4A")
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


# ============================================================== Loong's Nest


def loongs_nest_sunken_court():
    """Palace of LOOING — "The Sunken Court".

    INTENT: a fight in three dimensions where nothing is an accident. The whole map is
    water: nobody falls, nobody runs, and a jump is a swim stroke that can be taken again
    and again — so every position is reached on purpose, and whoever can put themselves
    exactly where they mean to (a dash of fixed length, a teleport, a hook) owns the map.

    The sea is not still. A current under the west ceiling carries a swimmer toward the
    hall; another, along the floor east of the hall, drags whoever sinks into it across a
    bed of urchins. Two columns of bubbles are the lifts. Kelp is the cover. Solid footing
    is the west court, the great hall and its three glazed eaves, the east reef and a few
    giant clams. The sea has a surface four rows below the top of the map: above it is air,
    where gravity is whole again, so whoever leaps out falls back in — the lid needs no rock.
    """
    m = Map("loongs_nest_sunken_court_pl", "Loong's Nest · The Sunken Court", "PL", 48, 28, "#0C1230")
    FLOOR = 25
    m.spawn(5, 19)

    # -- far to near ---------------------------------------------------------------
    palace = m.farview("Far Palace", (0.12, 0.14), grid=(40, 40))
    palace.pin("deco.ln_palace_far", 5.0, 3.2)

    ridges = m.farview("Seabed Ridges", (0.22, 0.26), loop=True, grid=(16, 40))
    ridges.put("deco.ln_ridge_far", 0, round(ridges.srow(6.4)))

    light = m.farview("Light Shafts", (0.30, 0.20), grid=(60, 44))
    for col in (3.0, 8.0, 12.6, 17.0):         # (the spawn is near the west wall: a pinned column left of 2.5 would be off the grid)
        light.pin("deco.ln_rays", col, -1.5)

    # The hall's body and the court's furniture, just behind the battlefield.
    court = m.farview("Court", (1.0, 1.0), grid=(48, 28))
    for x in (21, 23, 24, 26):
        court.put("deco.ln_column", x, 12)                              # the hall: columns on the podium
    for x in (22, 24):
        court.put("deco.ln_column", x, 7)                               # and under the crowning eave
    for x, floor in ((2, 19), (9, 19), (20, 17), (27, 17), (45, 21)):
        court.stand("deco.ln_lantern", x, floor)                        # night pearls: the only lamps
    for x, floor in ((17, FLOOR), (31, FLOOR), (36, 21)):
        court.stand("deco.ln_coral", x, floor)
    for x in (0, 16, 32):
        court.put("deco.ln_surface", x, 0)                              # open air, then the sea's skin on row 4 — the first row of water

    # The sea itself: one medium over the whole map, on a layer of its own so it takes no
    # battlefield cell. Collision makes each tile a real, fixed-world volume (GDD §3).
    sea = m.farview("Sea", (1.0, 1.0), grid=(48, 28), collision=True)
    for y in (4, 10, 16, 22):                                           # rows 0-3 are air above the surface
        for x in (0, 8, 16, 24, 32, 40):
            sea.put("softvolume.ln_water", x, y)

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    f.fill("ground.ln_reef", 0, FLOOR, 47, 27)
    f.fill("ground.ln_reef", 0, 0, 1, FLOOR - 1)
    f.fill("ground.ln_reef", 46, 0, 47, FLOOR - 1)
    f.fill("ground.ln_reef", 36, 21, 45, 24)                            # the east reef
    f.fill("ground.ln_palace", 2, 19, 9, 24)                            # the west court
    f.fill("ground.ln_palace", 20, 17, 27, 24)                          # the great hall's podium

    f.put("platform.ln_eave", 3, 14)                                    # over the west court
    f.put("platform.ln_eave", 17, 12)                                   # the hall's two wings
    f.put("platform.ln_eave", 26, 12)
    f.put("platform.ln_eave", 21, 7)                                    # and its crown
    for x, y in ((11, 16), (31, 16), (41, 12), (10, 7)):
        f.put("platform.ln_shell", x, y)

    # Kelp: the cover. A grove on the floor between court and hall, another on the reef.
    for x in (11, 13):
        f.stand("softvolume.ln_kelp", x, FLOOR)
    for x in (40, 42):
        f.stand("softvolume.ln_kelp", x, 21)

    # Bubbles: the lifts. One from the floor beside the hall, one from the reef.
    for y in (5, 9, 13, 17, 21):
        f.put("softvolume.ln_bubbles", 15, y)
    for y in (5, 9, 13, 17):
        f.put("softvolume.ln_bubbles", 38, y)

    # Currents — hardly visible. Under the west ceiling: a ride toward the hall. Along the
    # floor east of the hall: whoever sinks into it is dragged across the urchins.
    for x in (4, 8):
        f.put("softvolume.ln_current", x, 8)
    for x in (28, 32):
        f.put("softvolume.ln_current", x, 22)
    for x in (30, 34):
        f.stand("hazard.ln_urchin", x, FLOOR)

    for x, y in ((7, 18), (23, 16), (37, 20), (22, 6)):
        f.put("spawn.respawn", x, y)
    for x, y in ((17, FLOOR - 1), (44, 20), (22, 16), (4, 13), (28, 11), (36, 20), (12, 15), (24, 6)):
        f.put("spawn.enemy", x, y)
    for x, y in ((8, 18), (25, 16), (45, 20), (19, 11), (29, 11), (23, 6), (6, 13), (18, FLOOR - 1),
                 (32, 15), (42, 11)):
        f.put("goal.level", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    fg.put("deco.ln_fish", 12, 3)
    fg.put("deco.ln_fish", 33, 5, flip=True)
    return m.finish()


# ========================================================= Kingdom of Horror


def horror_hanging_wood():
    """Kingdom of Horror — "The Hanging Wood".

    INTENT: a map that punishes haste. Three screens wide, a little over two tall. The
    west third is a wood: two ladders of gnarled limbs with bare canopy above them, and
    bramble on the floor between — bramble is a soft volume that all but holds whoever
    lands in it, so a missed jump is paid for. The middle is a clearing nobody should cross
    on foot: ground fog lies over it, and there is a bear trap in the gap between the two
    banks; will-o'-the-wisps hang in the air on the jump lines above. The east third is the
    castle's gatehouse — solid stone, climbed by rotten hoardings on its face, with a turret
    whose top sees the whole clearing.

    Whoever looks before they move wins here: guards, tanks, and anyone who can make the
    enemy come to them.
    """
    m = Map("horror_hanging_wood_kh", "Dark Wood · The Hanging Wood", "KH", 52, 24, "#111B3D")
    FLOOR = 21
    m.spawn(9, FLOOR)

    # -- far to near ---------------------------------------------------------------
    moon = m.farview("Moon", (0.03, 0.04), grid=(40, 36))
    moon.pin("deco.kh_moon", 11.2, 0.4)

    castle = m.farview("Far Castle", (0.10, 0.12), grid=(44, 36))
    castle.pin("deco.kh_castle_far", 8.2, 0.4)                          # its towers cut into the moon

    wood_far = m.farview("Far Wood", (0.20, 0.22), loop=True, grid=(16, 36))
    wood_far.put("deco.kh_forest_far", 0, round(wood_far.srow(4.8)))

    trees_far = m.farview("Trees", (0.55, 0.60), grid=(70, 40))
    trees_far.pin("deco.kh_tree", 2.5, 0.2)
    trees_far.pin("deco.kh_tree_b", 9.6, 1.6)
    trees_far.pin("deco.kh_tree_b", 15.2, 1.0)

    # The trunks the limbs belong to, the lanterns and railings: just behind the battlefield.
    wood = m.farview("Wood", (1.0, 1.0), grid=(52, 24))
    wood.stand("deco.kh_tree_b", 4, FLOOR)
    wood.stand("deco.kh_tree", 14, FLOOR)
    wood.stand("deco.kh_tree_b", 24, FLOOR)
    wood.stand("deco.kh_tree_b", 30, FLOOR)
    for x in (21, 38):
        wood.stand("deco.kh_lantern", x, FLOOR)
    wood.stand("deco.kh_fence", 34, FLOOR)

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    f.fill("ground.kh_earth", 0, FLOOR, 51, 23)
    f.fill("ground.kh_earth", 0, 0, 1, FLOOR - 1)
    f.fill("ground.kh_earth", 50, 0, 51, FLOOR - 1)
    f.fill("ground.kh_stone", 40, 10, 49, 20)                           # the gatehouse
    f.fill("ground.kh_stone", 46, 5, 49, 9)                             # its turret

    # Two ladders of limbs, three rows apart (a clean jump), and a rung between them.
    for x, y in ((3, 18), (7, 15), (3, 12), (7, 9), (15, 18), (19, 15), (15, 12), (19, 9), (11, 12)):
        f.put("platform.kh_branch", x, y)
    for x, y in ((25, 17), (30, 14), (25, 11), (31, 8)):                # over the clearing
        f.put("platform.kh_branch", x, y)
    for x, y in ((37, 18), (37, 15), (37, 12), (42, 7)):                # hoardings: up the gatehouse, on to the turret
        f.put("platform.kh_plank", x, y)

    for x, y in ((2, 4), (10, 5), (17, 4)):                             # bare canopy over the wood
        f.put("softvolume.kh_canopy", x, y)
    for x in (5, 11, 35):                                               # bramble under the ladders: a missed jump costs
        f.stand("softvolume.kh_bramble", x, FLOOR)
    for x in (24, 31):                                                  # fog over the clearing...
        f.stand("softvolume.kh_fog", x, FLOOR)
    f.stand("hazard.kh_trap", 28, FLOOR)                                # ...and a trap in the gap between the banks
    f.stand("hazard.kh_trap", 17, FLOOR)
    for x, y in ((28, 15), (29, 10), (34, 12)):                         # wisps on the jump lines
        f.put("hazard.kh_wisp", x, y)

    for x, y in ((3, FLOOR - 1), (21, FLOOR - 1), (39, FLOOR - 1), (42, 9)):
        f.put("spawn.respawn", x, y)
    for x, y in ((15, FLOOR - 1), (22, FLOOR - 1), (38, FLOOR - 1), (8, 14), (20, 14), (41, 9), (47, 4), (32, 7)):
        f.put("spawn.enemy", x, y)
    for x, y in ((2, FLOOR - 1), (20, FLOOR - 1), (23, FLOOR - 1), (30, FLOOR - 1), (4, 11), (8, 8), (16, 11), (26, 10),
                 (44, 9), (48, 4)):
        f.put("goal.level", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    for x, y in ((42, 13), (45, 13), (47, 6)):                          # lit windows lie wholly over solid wall
        fg.put("deco.kh_window", x, y)
    fg.put("deco.kh_fireflies", 20, 5)
    fg.put("deco.kh_fireflies", 32, 2)
    return m.finish()


def horror_pumpkings_castle():
    """Kingdom of Horror — "PumpKing's Castle": the arena of his BOSS fight (FOES.gdd 8.3).

    INTENT: a map that takes its boss's side. PumpKing's weapon is his own head, bowled:
    on level ground it bounces, dies within a few metres and goes off; on a slope it does
    not stop. So his castle stands at the top of one. From the left: a strip of wood where
    the player arrives — dead trees, pumpkin lanterns to light the way up — then one
    unbroken slope, nine blocks, eighteen cells along and nine up, with nothing on it to
    hide behind; then the forecourt, the gate (shut — nobody goes in), and the balcony he
    comes out onto, three rows above the forecourt and a clean jump from it. The keep
    behind the balcony is solid to the top of the map and seals the east side.

    He holds the balcony and bowls downhill; every head that lands on the slope runs it to
    the bottom. Climbing costs a fifth of a fighter's speed; coming down costs nothing. A
    gnarled limb hangs over the upper slope: when the player reaches him he leaves for it
    in Soul Form and bowls from there, and the next time he comes back. Both are perches —
    the map's two Level Goal markers, the balcony first.

    A boss arena: goal `boss`, LV4 only, so only this world's 4-1 is ever dealt it.
    """
    m = Map("horror_pumpkings_castle_kh", "Dark Wood · PumpKing's Castle", "KH", 48, 30, "#111B3D",
            goals=("boss",), hardship=(4,))
    FLOOR = 26                       # the wood's floor: the walking surface is the top of this row
    FOOT, STEPS = 14, 9              # the slope starts at this column and climbs this many rows
    TOP = FLOOR - STEPS              # the forecourt's floor row (17)
    COURT = FOOT + 2 * STEPS         # the forecourt's first column (32)
    KEEP = 40                        # the keep's first column
    BALCONY = TOP - 3                # the balcony's row: a clean jump above the forecourt
    m.spawn(9, FLOOR)

    # -- far to near ---------------------------------------------------------------
    moon = m.farview("Moon", (0.03, 0.04), grid=(40, 36))
    moon.pin("deco.kh_moon", 2.6, 0.6)                                  # low over the wood he looks down on

    wood_far = m.farview("Far Wood", (0.20, 0.22), loop=True, grid=(16, 36))
    wood_far.put("deco.kh_forest_far", 0, round(wood_far.srow(4.6)))

    trees_far = m.farview("Trees", (0.55, 0.60), grid=(70, 44))
    trees_far.pin("deco.kh_tree_b", 0.6, 1.2)
    trees_far.pin("deco.kh_tree", 8.4, 0.0)
    trees_far.pin("deco.kh_tree_b", 15.6, 2.0)

    # The castle's front, behind the fighters: the same stone as the keep, a tone darker,
    # so what you can walk into reads apart from what you cannot. It runs down behind the
    # ground so no underside shows.
    front = m.farview("Castle Front", (1.0, 1.0), grid=(48, 30), tint="#3D4978")
    front.fill("ground.kh_stone", 28, 9, KEEP - 1, TOP + 2)
    for x in (28, 32, 36):
        front.put("deco.kh_battlement", x, 8)

    # Trunks and the gate: just behind the battlefield.
    wood = m.farview("Wood", (1.0, 1.0), grid=(48, 30))
    wood.stand("deco.kh_tree_b", 3, FLOOR)
    wood.stand("deco.kh_tree", 6, FLOOR, flip=True)
    wood.stand("deco.kh_tree_b", 17, 24)                                # on the slope: feet a little into the bank
    wood.stand("deco.kh_tree", 23, 21)                                  # the tree the limb belongs to
    wood.put("deco.kh_gate", COURT, TOP - 5)                            # the gate stands on the forecourt

    # Everything that gives light, in front of the trunks: pumpkin lanterns to show the way
    # up, the castle's lit windows, and his colours over the battlements.
    lights = m.farview("Lights", (1.0, 1.0), grid=(48, 30))
    lights.stand("deco.kh_lantern", 4, FLOOR)
    lights.stand("deco.kh_lantern", 12, FLOOR)
    # Up the slope: the middle of an odd column is a quarter cell above a row line.
    for x in (15, 21, 29):
        lights.stand("deco.kh_lantern", x, 32 - x // 2, nudge=(0, 1))
    lights.stand("deco.kh_lantern", COURT - 1, TOP, nudge=(0, 1))       # one each side of the gate
    lights.stand("deco.kh_lantern", COURT + 4, TOP)
    for x, y in ((29, 11), (37, 10)):
        lights.put("deco.kh_window", x, y)
    lights.stand("deco.kh_pennant", 30, 8, nudge=(0, 1))                # on the battlements
    lights.stand("deco.kh_pennant", 37, 8, flip=True, nudge=(0, 1))
    # The balustrade stands BEHIND whoever is on the balcony: in front it hid the legs of a
    # black-coated boss against black stone (style bible 2.6 - a fighter must read first).
    lights.put("deco.kh_balustrade", KEEP - 4, BALCONY - 1)

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    # The slope goes down first: its lowest block's solid row is part of the wood's floor,
    # and `fill` leaves taken cells alone.
    end, top = f.slope("ground.kh_slope", FOOT, FLOOR, STEPS, fill="ground.kh_earth")
    assert (end, top) == (COURT, TOP)
    f.fill("ground.kh_stone", KEEP, 0, 47, 29)                          # the keep: east wall, to the top row
    f.fill("ground.kh_earth", 0, FLOOR, KEEP - 1, 29)                   # floor, from the west wall to the keep
    f.fill("ground.kh_earth", 0, 0, 1, FLOOR - 1)                       # west wall, to the top row
    f.fill("ground.kh_earth", COURT, TOP, KEEP - 1, FLOOR - 1)          # the forecourt, solid down to the floor

    f.put("platform.kh_balcony", KEEP - 4, BALCONY)                     # the balcony, against the keep
    f.put("platform.kh_branch", 24, 17)                                 # the limb over the upper slope

    f.stand("softvolume.kh_bramble", 2, FLOOR)                          # the corner is no place to hide

    # Perches (Level Goal markers): the balcony's outer end first, then the limb. From the
    # balcony's end a head thrown for distance lands on the top of the slope.
    f.put("goal.level", KEEP - 4, BALCONY - 1)
    f.put("goal.level", 27, 16)
    for x, y in ((5, FLOOR - 1), (11, FLOOR - 1)):
        f.put("spawn.respawn", x, y)
    for x, y in ((6, FLOOR - 1), (20, 20), (34, TOP - 1)):              # where his two helpers come from at half health
        f.put("spawn.enemy", x, y)

    # -- foreground --------------------------------------------------------------------
    fg = m.closeview("Foreground")
    fg.put("deco.kh_door", KEEP, BALCONY - 3)                           # the door he came out of, on the keep's face
    for x, y in ((43, 6), (46, 6), (43, 13), (46, 13), (44, 20), (46, 20)):   # lit windows lie wholly over solid wall
        fg.put("deco.kh_window", x, y)
    fg.put("deco.kh_fireflies", 8, 20)
    fg.put("deco.kh_fireflies", 17, 14)
    return m.finish()


# ============================================================= The Forgotten


def forgotten_canals_of_the_sun():
    """The Forgotten — "Canals of the Sun".

    INTENT: the map climbs out of the swamp into the city, and gets more dangerous and more
    rewarding as it does. Three screens wide, two and a half tall. The west is low ground:
    mud banks and two canals of swamp water — a medium you wade through at four tenths of
    your speed, and that takes a second jump to leave — with cattails on the bank. Anyone caught down there is slow. Over it runs a
    stair of floating slabs for whoever would rather not get wet.

    The east is the pyramid: three terraces and a temple, two rows to each step by way of
    slabs on its west face and bridges of light on its east. The city's security still
    works up there — obsidian set in the terraces, and a beam on either side of the temple,
    so the summit is taken by going over, never by walking up to it.
    """
    m = Map("forgotten_canals_of_the_sun_tf", "Forgotten City · Canals of the Sun", "TF", 56, 26, "#0C0C22")
    FLOOR = 21
    m.spawn(22, FLOOR)

    # -- far to near ---------------------------------------------------------------
    sky = m.farview("Stars", (0.02, 0.02), loop=True, grid=(8, 40))
    top = round(sky.srow(-1.0))
    for k in range(3):
        if top + 4 * k >= 0:
            sky.put("deco.tf_stars", 0, top + 4 * k)

    city = m.farview("Far City", (0.08, 0.10), loop=True, grid=(16, 40))
    city.put("deco.tf_city_far", 0, round(city.srow(4.6)))

    great = m.farview("Great Pyramid", (0.14, 0.16), grid=(60, 44))
    great.pin("deco.tf_pyramid_far", 6.4, 1.0)

    canopy = m.farview("Canopy", (0.30, 0.32), loop=True, grid=(16, 40))
    canopy.put("deco.tf_jungle", 0, round(canopy.srow(7.0)))

    grove = m.farview("Grove", (1.0, 1.0), grid=(56, 26))
    grove.stand("deco.tf_tree", 3, FLOOR)
    grove.stand("deco.tf_tree", 50, FLOOR)
    for x in (23, 25):
        grove.stand("deco.tf_stele", x, FLOOR)
    grove.stand("deco.tf_serpent", 28, 17)                              # serpent heads guard the first terrace
    grove.stand("deco.tf_serpent", 46, 17, flip=True)
    grove.put("deco.tf_sign", 37, 2)                                    # a glyph still projected over the temple
    grove.stand("deco.tf_axolotl", 14, FLOOR)                           # one of Pixolotl's kin, on the bank

    # -- battlefield -----------------------------------------------------------------
    f = m.field()
    f.fill("ground.tf_mud", 0, FLOOR, 55, 25)
    f.carve(6, FLOOR, 13, FLOOR + 1)                                    # two canals cut into the bank
    f.carve(16, FLOOR, 19, FLOOR + 1)
    f.fill("ground.tf_stone", 0, 0, 1, FLOOR - 1)
    f.fill("ground.tf_stone", 54, 0, 55, FLOOR - 1)
    f.fill("ground.tf_stone", 26, 17, 49, 20)                           # the pyramid: three terraces...
    f.fill("ground.tf_stone", 30, 13, 45, 16)
    f.fill("ground.tf_stone", 34, 9, 41, 12)
    f.fill("ground.tf_stone", 36, 6, 39, 8)                             # ...and the temple

    for x in (6, 10, 16):                                               # swamp water: waded, not swum
        f.put("softvolume.tf_water", x, FLOOR)
    for x in (3, 20):
        f.stand("softvolume.tf_reeds", x, FLOOR)
    for x in (2, 53):                                                   # vines up both walls: the slow way to the top
        for y in (5, 9, 13):
            f.put("softvolume.tf_vines", x, y)

    for x, y in ((9, 18), (14, 15), (19, 12), (24, 10)):                # the dry road over the canals
        f.put("platform.tf_slab", x, y)
    for x, y in ((23, 19), (27, 15), (31, 11)):                         # the pyramid's west face, two rows a step
        f.put("platform.tf_slab", x, y)
    for x, y in ((50, 19), (46, 15), (42, 11)):                         # its east face: bridges of light
        f.put("platform.tf_light", x, y)

    for x in (34, 41):                                                  # a beam on either side of the temple
        f.stand("hazard.tf_laser", x, 9)
    f.stand("hazard.tf_obsidian", 26, 17)                               # obsidian in the terraces
    f.stand("hazard.tf_obsidian", 44, 13)

    for x, y in ((5, FLOOR - 1), (24, FLOOR - 1), (47, 16), (36, 5)):
        f.put("spawn.respawn", x, y)
    for x, y in ((15, FLOOR - 1), (23, FLOOR - 1), (50, FLOOR - 1), (29, 16), (32, 12), (42, 12), (38, 5), (25, 9)):
        f.put("spawn.enemy", x, y)
    for x, y in ((2, FLOOR - 1), (14, FLOOR - 1), (25, FLOOR - 1), (51, FLOOR - 1), (48, 16), (30, 12), (43, 12),
                 (35, 8), (40, 8), (37, 5), (15, 14), (20, 11)):
        f.put("goal.level", x, y)
    return m.finish()


MAPS = [starfield_long_horizon, wulin_bamboo_sea, dungeon_hollow_gaol, ghost_town_main_street, cloud_court_heavens,
        loongs_nest_sunken_court, horror_hanging_wood, horror_pumpkings_castle, forgotten_canals_of_the_sun]


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

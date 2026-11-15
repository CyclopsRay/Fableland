#!/usr/bin/env python3
"""Starfield tile set — the Starland realm (SL).

Art language: flat minimalist poster. No outlines at all; every form is two or three
flat planes split by a hard terminator (warm lit face, plum shadow face), with a whisper
of print grain. The only ornament is hairline gold inlay and star-shaped cut-outs.
Egypt at dusk under a field of stars: emptiness is the composition.
See `Docs/Art/RealmStyleBible.md` §Starfield.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/starland.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "Starland"
SET = "sl"

SKY_TOP = rgb("#17133A")
SKY_MID = rgb("#3B2A6E")
SKY_LOW = rgb("#A8527A")
HORIZON = rgb("#F0875C")
GLOW = rgb("#FBD08A")
STONE_L = rgb("#F2D3A0")     # sandstone, lit plane
STONE_M = rgb("#DDB07C")
STONE_S = rgb("#A8706A")     # sandstone, shadow plane
STONE_D = rgb("#5B3A63")     # deep shadow / plum
SAND_L = rgb("#F6D79C")      # dune crest
SAND_M = rgb("#CC8A62")      # dune body
SAND_S = rgb("#9A5C5E")
SAND_D = rgb("#5F3A5E")
GOLD = rgb("#FBE08A")
GOLD_D = rgb("#D9A441")
TEAL = rgb("#35B3A8")
LAPIS = rgb("#2C63B4")
STAR = rgb("#FFF8E0")

BRIEF = ("Flat minimalist poster-style game sprite on transparent ground: no outlines, each form "
         "built from two or three flat colour planes with a hard light/shadow terminator, faint print "
         "grain, hairline gold inlay and star-shaped cut-outs as the only ornament. Ancient Egypt at "
         "dusk: warm apricot sandstone, plum shadows, indigo sky, a teal accent. Orthographic side view, "
         "no cast shadow, no text.")


def star_points(cx, cy, r_out, r_in, n=4, rot=-math.pi / 2):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = rot + i * math.pi / n
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def star(img, cx, cy, r, color=STAR, n=4, inner=0.30, opacity=1.0, glow=0.0, glow_color=None, rot=-math.pi / 2):
    H, W = img.shape[:2]
    if glow > 0:
        C.radial_glow(img, cx, cy, r * 2.4, glow_color if glow_color is not None else color, glow)
    C.over(img, color, C.poly_mask(H, W, star_points(cx, cy, r, r * inner, n, rot)) * opacity)


def plane(img, pts, color, opacity=1.0):
    H, W = img.shape[:2]
    m = C.poly_mask(H, W, pts)
    C.over(img, color, m * opacity)
    return m


def finish(img, seed, amount=0.022):
    return C.grain(img, amount, seed, scale=1.5)


# --------------------------------------------------------------------- ground


def sand_atlas(variants=4):
    """`ground.sl_sand` — dune sand. A bright wind-smoothed crest over a flat sienna body
    ruled with drifting sediment lines; the underside is cut into square strata steps."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    base = C.new(CELL, Wn)
    base[..., :3] = SAND_M
    base[..., 3] = 1.0
    two_pi = 2.0 * math.pi
    # sediment lines: hairlines that drift on a wave whose period is the whole strip
    for (y0, amp, ph, col, wd, alpha) in [(26.0, 5.0, 0.3, rgb("#DFA06F"), 2.2, 0.85), (52.0, 4.0, 2.1, rgb("#B27358"), 1.8, 0.8),
                                           (81.0, 6.0, 4.0, rgb("#DFA06F"), 1.6, 0.7), (108.0, 3.0, 1.2, rgb("#B27358"), 2.4, 0.8)]:
        line_y = y0 + amp * np.sin(xx / Wn * two_pi * 2.0 + ph) + amp * 0.5 * np.sin(xx / Wn * two_pi * 5.0 + ph * 2.0)
        C.tint_inside(base, col, np.exp(-((yy - line_y) / wd) ** 2) * alpha)
    spark = C.field(CELL, Wn, 1.7, 5, octaves=1, period_px=(Wn, CELL))
    C.tint_inside(base, rgb("#E9B584"), C.smoothstep(0.86, 0.95, spark) * 0.55)

    def cap(cell_img, g):
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        ripple = 30.0 + 7.0 * np.sin(gx / Wn * two_pi * 3.0 + 0.7) + 3.0 * np.sin(gx / Wn * two_pi * 8.0)
        crest = C.smoothstep(ripple + 1.0, ripple - 1.0, gy)
        C.tint_inside(cell_img, SAND_L, crest)
        # wind ripples inside the crest
        for k in range(3):
            ry = ripple - 8.0 - k * 7.0 + 2.0 * np.sin(gx / Wn * two_pi * 6.0 + k)
            C.tint_inside(cell_img, rgb("#E5BC80"), np.exp(-((gy - ry) / 1.3) ** 2) * 0.75 * crest)
        C.tint_inside(cell_img, SAND_S, np.exp(-((gy - ripple - 2.5) / 2.2) ** 2) * 0.7)     # crest's cast edge
        C.tint_inside(cell_img, STAR, C.smoothstep(4.5, 1.5, gy) * 0.85)                      # sunlit rim

    def side(cell_img, left):
        gx, gy = C.grid(CELL, CELL)
        if left:
            C.tint_inside(cell_img, rgb("#E2A674"), C.smoothstep(13.0, 11.0, gx) * 0.9)
        else:
            C.tint_inside(cell_img, SAND_S, C.smoothstep(CELL - 19.0, CELL - 17.0, gx) * 0.9)

    def underside(cell_img, g):
        gx, gy = C.grid(CELL, CELL)
        steps = [(22, 44, 30, 58), (50, 26, 62, 36), (34, 60, 24, 48), (56, 30, 46, 20)][g % 4]
        cutoff = np.zeros_like(gx)
        for i, d in enumerate(steps):
            cutoff = np.where((gx >= i * CELL / 4.0) & (gx < (i + 1) * CELL / 4.0), CELL - d, cutoff)
        keep = C.smoothstep(cutoff + 0.8, cutoff - 0.8, gy)
        C.tint_inside(cell_img, SAND_D, C.smoothstep(cutoff - 16.0, cutoff - 14.0, gy) * 0.85)
        C.tint_inside(cell_img, SAND_S, C.smoothstep(cutoff - 42.0, cutoff - 40.0, gy) * 0.55)
        cell_img[..., 3] *= keep

    def render(g, layer, open_left, open_right):
        cell_img = C.cut(base, g)
        if layer == 3:
            underside(cell_img, g)
        if open_left:
            side(cell_img, True)
        if open_right:
            side(cell_img, False)
        if layer == 0:
            cap(cell_img, g)
        return finish(cell_img, 40 + g * 7 + layer)

    return C.build_hill_atlas(render, variants)


def sandstone_atlas(variants=4):
    """`ground.sl_sandstone` — monument masonry: huge dressed blocks in running bond, a
    pale cornice with a gold inlay line on top, and a plum plinth course at the base."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    course = (yy // (CELL / 2.0)).astype(np.int32)                 # two courses per cell
    off = np.where(course % 2 == 0, 0.0, CELL / 2.0)
    bx = np.floor((xx + off) / CELL).astype(np.int64)
    tone = C._hash01(np.mod(bx, variants) + 7, course.astype(np.int64) + 3, 77)
    base = C.new(CELL, Wn)
    base[..., :3] = C.mix(rgb("#E6C592"), rgb("#F0D6A6"), tone)
    base[..., 3] = 1.0
    jx = np.mod(xx + off, CELL)
    jy = np.mod(yy, CELL / 2.0)
    joint = np.maximum(C.smoothstep(2.6, 1.2, np.minimum(jx, CELL - jx)), C.smoothstep(2.4, 1.0, np.minimum(jy, CELL / 2.0 - jy)))
    C.tint_inside(base, rgb("#B98D62"), joint * 0.9)
    C.tint_inside(base, STAR, np.exp(-((jy - 4.2) / 1.2) ** 2) * 0.35)        # chamfer catch-light
    def cap(cell_img, g):
        gx, gy = C.grid(CELL, CELL)
        if g % 2 == 0:      # a frieze of engraved stars runs only under the cornice
            C.tint_inside(cell_img, rgb("#C99E6E"), C.poly_mask(CELL, CELL, star_points(CELL / 2.0, 42.0, 13.0, 4.2, 4)) * 0.9)
        C.tint_inside(cell_img, rgb("#FBE7BC"), C.smoothstep(15.0, 13.5, gy))
        C.tint_inside(cell_img, STAR, C.smoothstep(4.0, 2.0, gy) * 0.9)
        C.tint_inside(cell_img, GOLD_D, np.exp(-((gy - 17.0) / 1.7) ** 2) * 0.95)
        C.tint_inside(cell_img, rgb("#B98D62"), np.exp(-((gy - 21.5) / 1.6) ** 2) * 0.6)

    def side(cell_img, left):
        gx, gy = C.grid(CELL, CELL)
        if left:
            C.tint_inside(cell_img, rgb("#FBE7BC"), C.smoothstep(9.0, 7.5, gx) * 0.95)
        else:
            C.tint_inside(cell_img, STONE_S, C.smoothstep(CELL - 15.0, CELL - 13.5, gx) * 0.92)

    def plinth(cell_img):
        gx, gy = C.grid(CELL, CELL)
        C.tint_inside(cell_img, STONE_S, C.smoothstep(CELL - 20.0, CELL - 18.5, gy) * 0.95)
        C.tint_inside(cell_img, STONE_D, C.smoothstep(CELL - 7.0, CELL - 5.5, gy) * 0.95)

    def render(g, layer, open_left, open_right):
        cell_img = C.cut(base, g)
        if layer == 3:
            plinth(cell_img)
        if open_left:
            side(cell_img, True)
        if open_right:
            side(cell_img, False)
        if layer == 0:
            cap(cell_img, g)
        return finish(cell_img, 80 + g * 5 + layer)

    return C.build_hill_atlas(render, variants)


# ------------------------------------------------------------------ platforms


def slab(seed):
    """Levitating stone slab (3x1): monolith engineering held up by a single small star."""
    img = C.cells(3, 1)
    H, W = img.shape[:2]
    plane(img, [(0, 0), (W, 0), (W - 16, 44), (16, 44)], STONE_L)
    plane(img, [(W * 0.56, 0), (W, 0), (W - 16, 44), (W * 0.60, 44)], STONE_M)
    plane(img, [(16, 44), (W - 16, 44), (W - 40, 62), (40, 62)], STONE_S)
    xx, yy = C.grid(H, W)
    inside = img[..., 3]
    C.tint_inside(img, STAR, C.smoothstep(4.5, 2.5, yy) * inside * 0.9)
    C.tint_inside(img, GOLD_D, np.exp(-((yy - 30.0) / 1.6) ** 2) * inside * 0.9)
    for cx0 in np.linspace(W * 0.2, W * 0.8, 4):
        C.tint_inside(img, rgb("#C99E6E"), C.poly_mask(H, W, star_points(cx0, 16.0, 7.0, 2.4, 4)) * 0.9)
    star(img, W / 2.0, 94.0, 15.0, GOLD, glow=0.55, glow_color=GLOW)
    beam = np.exp(-((xx - W / 2.0) / 5.0) ** 2) * C.smoothstep(62.0, 78.0, yy) * C.smoothstep(H, 96.0, yy)
    C.add_light(img, GLOW, beam * 0.35, only_inside=False)
    return finish(img, seed)


def constellation(seed):
    """A walkable constellation (4x1): a hairline of gold strung between stars."""
    img = C.cells(4, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    nodes = [(10.0, 9.0), (W * 0.27, 9.0), (W * 0.52, 9.0), (W * 0.76, 9.0), (W - 10.0, 9.0)]
    bar = C.rect_mask(H, W, 4.0, 3.0, W - 4.0, 15.0, radius=6.0)
    C.add_light(img, GLOW, np.exp(-((yy - 9.0) / 16.0) ** 2) * C.smoothstep(0.0, 30.0, xx) * C.smoothstep(W, W - 30.0, xx) * 0.5, only_inside=False)
    C.over(img, GOLD, bar)
    C.tint_inside(img, STAR, C.smoothstep(7.5, 4.5, yy) * bar)
    for i, (nx, ny) in enumerate(nodes):
        big = i in (0, 2, 4)
        star(img, nx, ny + (12.0 if big else 8.0), 17.0 if big else 10.0, STAR, glow=0.5 if big else 0.25, glow_color=GLOW)
    # hanging star-dust so the eye reads it as held in the sky, not sitting on anything
    rng = np.random.default_rng(seed)
    for _ in range(26):
        sx, sy = rng.uniform(12, W - 12), rng.uniform(30, H - 8)
        C.over(img, GOLD, C.aa(C.sdf_circle(H, W, sx, sy, rng.uniform(1.0, 2.4)), 1.2) * (1.0 - sy / H) * 0.9)
    return img


# ---------------------------------------------------------------- soft volume


def stardust(seed):
    """Nebula of star-dust (3x2): posterised violet-to-teal cloud bands, full of pinpricks."""
    img = C.cells(3, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    n = C.fbm(xx / 120.0, yy / 80.0, seed, octaves=4)
    env = np.exp(-(((xx - W / 2.0) / (W * 0.40)) ** 2 + ((yy - H / 2.0) / (H * 0.36)) ** 2) * 1.25)
    d = n * 0.55 + env * 0.75
    for thr, col, alpha in [(0.50, rgb("#4B3A8C"), 0.62), (0.64, rgb("#5C5AB8"), 0.72), (0.78, TEAL, 0.78), (0.92, rgb("#A7EDE2"), 0.85)]:
        C.over(img, col, C.smoothstep(thr - 0.012, thr + 0.012, d) * alpha)
    rng = np.random.default_rng(seed)
    for _ in range(46):
        sx, sy = rng.uniform(0, W), rng.uniform(0, H)
        if d[int(sy), int(sx)] < 0.52:
            continue
        r = rng.uniform(1.6, 5.5)
        if r > 4.0:
            star(img, sx, sy, r * 1.9, STAR, glow=0.35, glow_color=STAR)
        else:
            C.over(img, STAR, C.aa(C.sdf_circle(H, W, sx, sy, r * 0.55), 1.2))
    return img


# --------------------------------------------------------------------- hazard


def fallen_star(seed):
    """A fallen star (2x1), still white-hot, driven into a dark scorch crater."""
    img = C.cells(2, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    plane(img, [(18.0, H), (52.0, H - 22.0), (W - 50.0, H - 24.0), (W - 16.0, H)], SAND_D)
    plane(img, [(52.0, H - 22.0), (W - 50.0, H - 24.0), (W - 70.0, H - 12.0), (74.0, H - 11.0)], rgb("#3B2444"))
    cx0, cy0 = W * 0.50, H * 0.52
    C.radial_glow(img, cx0, cy0, 52.0, HORIZON, 0.85, squash=0.75)
    rot = -math.pi / 2 + 0.38
    for r, col in [(58.0, HORIZON), (47.0, rgb("#FFC46B")), (34.0, GOLD), (20.0, STAR)]:
        C.over(img, col, C.poly_mask(H, W, star_points(cx0, cy0, r, r * 0.42, 5, rot)))
    # the crater rim buries the lower points
    plane(img, [(0.0, H), (26.0, H - 13.0), (60.0, H - 9.0), (W * 0.5, H - 15.0), (W - 58.0, H - 9.0), (W - 24.0, H - 14.0), (W, H)], SAND_D)
    rng = np.random.default_rng(seed)
    for _ in range(9):
        ex, ey = cx0 + rng.uniform(-92, 92), rng.uniform(6, H * 0.55)
        C.over(img, GOLD if rng.random() < 0.6 else HORIZON, C.poly_mask(H, W, star_points(ex, ey, rng.uniform(3.0, 6.5), 1.2, 4)))
    return img


# ------------------------------------------------------------------ monuments


def obelisk(seed):
    """Obelisk (1x5) with a gold pyramidion and a column of inlaid marks."""
    img = C.cells(1, 5)
    H, W = img.shape[:2]
    cx0 = W / 2.0
    top = 64.0
    plane(img, [(cx0 - 20.0, top), (cx0, top), (cx0, H - 30.0), (cx0 - 34.0, H - 30.0)], STONE_L)
    plane(img, [(cx0, top), (cx0 + 20.0, top), (cx0 + 34.0, H - 30.0), (cx0, H - 30.0)], STONE_S)
    plane(img, [(cx0, 6.0), (cx0 - 20.0, top), (cx0, top)], GOLD)
    plane(img, [(cx0, 6.0), (cx0, top), (cx0 + 20.0, top)], GOLD_D)
    plane(img, [(cx0 - 46.0, H - 30.0), (cx0, H - 30.0), (cx0, H), (cx0 - 52.0, H)], STONE_M)
    plane(img, [(cx0, H - 30.0), (cx0 + 46.0, H - 30.0), (cx0 + 52.0, H), (cx0, H)], STONE_D)
    rng = np.random.default_rng(seed)
    y = top + 44.0
    while y < H - 70.0:
        kind = int(rng.integers(0, 3))
        x0 = cx0 - 13.0
        if kind == 0:
            C.tint_inside(img, GOLD_D, C.poly_mask(H, W, star_points(x0, y, 7.5, 2.4, 4)) * 0.95)
        elif kind == 1:
            C.tint_inside(img, GOLD_D, C.rect_mask(H, W, x0 - 7.0, y - 2.0, x0 + 7.0, y + 2.0) * 0.95)
        else:
            C.tint_inside(img, GOLD_D, C.aa(np.abs(C.sdf_circle(H, W, x0, y, 5.5)) - 1.6, 1.2) * 0.95)
        y += rng.uniform(34.0, 52.0)
    star(img, cx0, 6.0, 9.0, STAR, glow=0.6, glow_color=GLOW)
    return finish(img, seed)


def pyramid(wc, hc, seed, haze=0.55):
    """Distant pyramid: one lit face, one shadow face, a gold capstone, and a base lost
    in horizon haze (so it reads as enormous and far away)."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    apex = (W * 0.46, 8.0)
    plane(img, [apex, (0.0, H), (W * 0.60, H)], STONE_L)
    plane(img, [apex, (W * 0.60, H), (W, H)], STONE_S)
    xx, yy = C.grid(H, W)
    inside = img[..., 3]
    courses = np.exp(-((np.mod(yy - 8.0, 26.0) - 13.0) / 1.2) ** 2)
    C.tint_inside(img, rgb("#C99E6E"), courses * inside * 0.30)
    cap_h = H * 0.13
    k = cap_h / (H - 8.0)
    plane(img, [apex, (apex[0] - apex[0] * k, 8.0 + cap_h), (apex[0] + (W * 0.60 - apex[0]) * k, 8.0 + cap_h)], GOLD)
    plane(img, [apex, (apex[0] + (W * 0.60 - apex[0]) * k, 8.0 + cap_h), (apex[0] + (W - apex[0]) * k, 8.0 + cap_h)], GOLD_D)
    C.tint_inside(img, HORIZON, C.smoothstep(H * 0.35, H, yy) * inside * haze)
    star(img, apex[0], apex[1] + 2.0, 13.0, STAR, glow=0.6, glow_color=GLOW)
    return finish(img, seed)


def star_gate(seed):
    """Temple pylon (6x5): two battered towers and a lintel pierced by a star through
    which the sky shows. Meant to stand alone on an empty horizon."""
    img = C.cells(6, 5)
    H, W = img.shape[:2]
    for s in (0, 1):
        x0 = 6.0 if s == 0 else W * 0.60
        x1 = W * 0.40 if s == 0 else W - 6.0
        inset = 34.0
        plane(img, [(x0 + inset, 22.0), (x1 - inset * 0.25 if s == 0 else x1 - inset, 22.0), (x1, H), (x0, H)], STONE_L if s == 0 else STONE_M)
        # shadow return on the inner (door) side of the left tower, outer side of the right
        if s == 0:
            plane(img, [(x1 - inset * 0.25 - 34.0, 22.0), (x1 - inset * 0.25, 22.0), (x1, H), (x1 - 40.0, H)], STONE_S)
        else:
            plane(img, [(x1 - inset - 40.0, 22.0), (x1 - inset, 22.0), (x1, H), (x1 - 52.0, H)], STONE_S)
        plane(img, [(x0 + inset - 12.0, 6.0), ((x1 - inset * 0.25 if s == 0 else x1 - inset) + 12.0, 6.0),
                    ((x1 - inset * 0.25 if s == 0 else x1 - inset) + 4.0, 24.0), (x0 + inset - 4.0, 24.0)], rgb("#FBE7BC"))
    lint_y0, lint_y1 = H * 0.30, H * 0.56
    plane(img, [(W * 0.36, lint_y0), (W * 0.64, lint_y0), (W * 0.64, lint_y1), (W * 0.36, lint_y1)], STONE_M)
    plane(img, [(W * 0.36, lint_y0 - 12.0), (W * 0.64, lint_y0 - 12.0), (W * 0.64, lint_y0 + 4.0), (W * 0.36, lint_y0 + 4.0)], rgb("#FBE7BC"))
    xx, yy = C.grid(H, W)
    hole = C.poly_mask(H, W, star_points(W * 0.5, (lint_y0 + lint_y1) / 2.0 + 4.0, 44.0, 16.0, 4))
    img[..., 3] *= 1.0 - hole
    ring = C.poly_mask(H, W, star_points(W * 0.5, (lint_y0 + lint_y1) / 2.0 + 4.0, 52.0, 20.0, 4)) * (1.0 - hole)
    C.tint_inside(img, GOLD, ring * (img[..., 3] > 0.5))
    inside = img[..., 3]
    for gy0 in (H * 0.44, H * 0.70):
        C.tint_inside(img, GOLD_D, np.exp(-((yy - gy0) / 1.8) ** 2) * inside * 0.9 * ((xx < W * 0.36) | (xx > W * 0.64)))
    C.tint_inside(img, STONE_D, C.smoothstep(H - 16.0, H - 14.0, yy) * inside * 0.85)
    for cx0 in (W * 0.20, W * 0.80):
        C.tint_inside(img, rgb("#C99E6E"), C.poly_mask(H, W, star_points(cx0, H * 0.57, 22.0, 7.0, 4)) * 0.9)
    return finish(img, seed)


def broken_column(seed):
    """Fallen column drum (1x2) half-drifted with sand: the one small thing in the frame."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    cx0 = W / 2.0
    plane(img, [(cx0 - 30.0, 76.0), (cx0 + 2.0, 58.0), (cx0 + 2.0, H - 12.0), (cx0 - 30.0, H - 12.0)], STONE_L)
    plane(img, [(cx0 + 2.0, 58.0), (cx0 + 30.0, 90.0), (cx0 + 30.0, H - 12.0), (cx0 + 2.0, H - 12.0)], STONE_S)
    xx, yy = C.grid(H, W)
    inside = img[..., 3]
    for fx in (-18.0, -6.0, 14.0):
        C.tint_inside(img, rgb("#C99E6E"), np.exp(-((xx - (cx0 + fx)) / 1.6) ** 2) * inside * 0.7)
    for gy0 in (128.0, 180.0):
        C.tint_inside(img, GOLD_D, np.exp(-((yy - gy0) / 1.7) ** 2) * inside * 0.9)
    plane(img, [(0.0, H), (22.0, H - 22.0), (cx0, H - 12.0), (W - 20.0, H - 26.0), (W, H)], SAND_L)
    plane(img, [(cx0, H - 12.0), (W - 20.0, H - 26.0), (W, H), (cx0 + 10.0, H)], SAND_M)
    return finish(img, seed)


# ------------------------------------------------------------------------ sky


def horizon(seed):
    """Dusk gradient band (8x12). Transparent at the top so the indigo canvas shows, then
    violet and rose down to a coral horizon line, with flat dusty ground-haze below it so the
    band has no visible lower edge. Uniform along x: it tiles in a looping far layer."""
    img = C.cells(8, 12)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    t = yy / H
    stops = [(0.0, SKY_TOP), (0.24, SKY_MID), (0.48, SKY_LOW), (0.66, HORIZON), (0.72, GLOW),
             (0.74, rgb("#C9706C")), (1.0, rgb("#8E4A78"))]
    col = C.ramp(t, stops)
    steps = 14.0                                  # poster banding: broad flat steps of colour
    tq = np.floor(t * steps) / steps + 0.5 / steps
    col = C.mix(col, C.ramp(tq, stops), 0.55)
    img[..., :3] = col
    img[..., 3] = C.smoothstep(0.0, 0.16, t)
    return img


def sun_low(seed):
    """The setting sun (4x4): flat discs in three steps of warmth."""
    img = C.cells(4, 4)
    H, W = img.shape[:2]
    cx0, cy0 = W / 2.0, H / 2.0
    xx, yy = C.grid(H, W)
    C.radial_glow(img, cx0, cy0, W * 0.20, GLOW, 0.6)
    for r, col, a in [(W * 0.30, rgb("#F7A56B"), 0.55), (W * 0.23, rgb("#FBC57A"), 0.8), (W * 0.165, rgb("#FFF0C4"), 1.0)]:
        C.over(img, col, C.aa(C.sdf_circle(H, W, cx0, cy0, r), 1.5) * a)
    return img


def moon(seed):
    """Crescent moon (3x3) with one bright companion star."""
    img = C.cells(3, 3)
    H, W = img.shape[:2]
    cx0, cy0, r = W * 0.48, H * 0.50, W * 0.30
    xx, yy = C.grid(H, W)
    C.radial_glow(img, cx0, cy0, r * 0.95, rgb("#B9B4F0"), 0.42)
    disc = C.aa(C.sdf_circle(H, W, cx0, cy0, r), 1.5)
    bite = C.aa(C.sdf_circle(H, W, cx0 + r * 0.42, cy0 - r * 0.20, r * 0.88), 1.5)
    C.over(img, rgb("#FFF4D2"), disc * (1.0 - bite))
    star(img, cx0 + r * 0.78, cy0 - r * 0.70, 15.0, STAR, glow=0.5, glow_color=STAR)
    return img


def starfield(seed):
    """Loose scatter of stars (8x4) that wraps horizontally for the looping sky layer."""
    img = C.cells(8, 4)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for _ in range(120):
        sx, sy = rng.uniform(0, W), rng.uniform(4, H - 4)
        r = rng.uniform(0.9, 2.6) * (1.0 - 0.35 * sy / H)
        col = STAR if rng.random() < 0.8 else (rgb("#A7EDE2") if rng.random() < 0.5 else GOLD)
        for ox in (-W, 0, W):
            C.over(img, col, C.aa(C.sdf_circle(H, W, sx + ox, sy, r), 1.2) * rng.uniform(0.55, 1.0))
    for _ in range(9):
        sx, sy = rng.uniform(30, W - 30), rng.uniform(20, H * 0.75)
        star(img, sx, sy, rng.uniform(8.0, 15.0), STAR, glow=0.35, glow_color=STAR)
    return img


def constellation_sky(seed):
    """The Star-Fruit constellation (5x4): a five-pointed figure of stars joined by
    hairlines — the realm's emblem, hung in the far sky."""
    img = C.cells(5, 4)
    H, W = img.shape[:2]
    cx0, cy0, r = W / 2.0, H / 2.0 + 6.0, H * 0.40
    pts = [(cx0 + math.cos(-math.pi / 2 + i * 2 * math.pi / 5) * r * (1.0 if i % 1 == 0 else 1.0),
            cy0 + math.sin(-math.pi / 2 + i * 2 * math.pi / 5) * r) for i in range(5)]
    order = [0, 2, 4, 1, 3, 0]
    for a, b in zip(order[:-1], order[1:]):
        C.over(img, GOLD, C.line_mask(H, W, [pts[a], pts[b]], 2.0) * 0.55)
    for i, (px, py) in enumerate(pts):
        star(img, px, py, 20.0 if i == 0 else 14.0, STAR, glow=0.5, glow_color=GLOW)
    star(img, cx0, cy0, 8.0, TEAL, glow=0.4, glow_color=TEAL)
    return img


def dunes_far(seed, colors=(rgb("#B9627A"), rgb("#8E4A78")), crest=1.1, swell=0.55):
    """Far dune silhouettes (16x6): two flat overlapping swells in the top cells, solid colour
    all the way down so the range still fills the frame when the camera climbs. Wraps in x."""
    img = C.cells(16, 6)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    two_pi = 2 * math.pi
    for k, col in enumerate(colors):
        ridge = CELL * (crest + 0.75 * k) - CELL * swell * np.sin(xx / W * two_pi * (2 + k) + 1.3 * k + seed) \
            - CELL * swell * 0.45 * np.sin(xx / W * two_pi * (5 + 2 * k) + 0.6 + seed) \
            - CELL * swell * 0.25 * np.sin(xx / W * two_pi * (3 + k) + 2.2 * seed)
        C.over(img, col, C.smoothstep(ridge - 0.9, ridge + 0.9, yy))
        # the crest catches the last light
        C.tint_inside(img, C.mix(col, HORIZON, 0.55), np.exp(-((yy - ridge - 3.0) / 2.6) ** 2) * 0.9)
    return finish(img, seed)


# ---------------------------------------------------------------------- build


def build():
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.sl_sand", "Dune Sand", "Ground", 1, 1, "Ground", "ground_sl_sand", sand_atlas(4),
        "Dune sand cross-section: a bright wind-rippled crest over a flat sienna body ruled with drifting sediment "
        "hairlines; the underside is cut into square strata steps.", fill=True, group="terrain.sl_sand", variants=4)
    add("ground.sl_sandstone", "Monument Sandstone", "Ground", 1, 1, "Ground", "ground_sl_sandstone", sandstone_atlas(4),
        "Monument masonry: huge dressed sandstone blocks in running bond, engraved four-point stars, a pale cornice with "
        "a gold inlay line on top and a plum plinth course at the base.", fill=True, group="terrain.sl_sandstone", variants=4)

    add("platform.sl_slab", "Levitating Slab", "Platform", 3, 1, "Platform", "platform_sl_slab", slab(11),
        "A levitating dressed-stone slab with a gold inlay line, held aloft by one small glowing star beneath it.", walk=4)
    add("platform.sl_constellation", "Constellation Bridge", "Platform", 4, 1, "Platform", "platform_sl_constellation", constellation(13),
        "A walkable constellation: a hairline bar of gold strung between five stars, star-dust trailing beneath.", walk=6)

    add("softvolume.sl_stardust", "Stardust Nebula", "SoftVolume", 3, 2, "SoftVolume", "softvolume_sl_stardust", stardust(17),
        "A nebula of star-dust in posterised violet-to-teal bands, scattered with pinprick and four-point stars. Enterable.")

    add("hazard.sl_fallen_star", "Fallen Star", "Hazard", 2, 1, "Hazard", "hazard_sl_fallen_star", fallen_star(19),
        "A five-pointed fallen star, still white-hot, driven at a tilt into a dark scorch crater, flinging sparks.")

    add("deco.sl_obelisk", "Obelisk", "Decoration", 1, 5, "Decoration", "deco_sl_obelisk", obelisk(23),
        "A tall obelisk with a gold pyramidion, one lit and one shadow face, and a column of inlaid gold marks.")
    add("deco.sl_star_gate", "Star Gate Pylon", "Decoration", 6, 5, "Decoration", "deco_sl_star_gate", star_gate(29),
        "A temple pylon: two battered towers and a lintel pierced by a four-point star through which the sky shows.")
    add("deco.sl_broken_column", "Fallen Column", "Decoration", 1, 2, "Decoration", "deco_sl_broken_column", broken_column(31),
        "A broken fluted column drum half-drifted with sand.")
    add("deco.sl_pyramid", "Great Pyramid (far)", "Decoration", 10, 6, "Decoration", "deco_sl_pyramid", pyramid(10, 6, 37),
        "A colossal distant pyramid: one lit face, one shadow face, gold capstone with a star, base lost in horizon haze.")
    add("deco.sl_pyramid_small", "Pyramid (far, small)", "Decoration", 5, 3, "Decoration", "deco_sl_pyramid_small", pyramid(5, 3, 41, haze=0.70),
        "A smaller, farther pyramid, hazier.")
    add("deco.sl_dunes_far", "Far Dunes", "Decoration", 16, 6, "Decoration", "deco_sl_dunes_far", dunes_far(1),
        "Two flat overlapping far dune swells in rose and violet, crests catching the last light, solid below. Tiles horizontally.")
    add("deco.sl_dunes_near", "Near Dunes", "Decoration", 16, 6, "Decoration", "deco_sl_dunes_near",
        dunes_far(2, colors=(rgb("#D97B66"), rgb("#B45F6C")), crest=1.2, swell=0.75),
        "Nearer dune swells in coral and rose, solid below. Tiles horizontally.")
    add("deco.sl_horizon", "Dusk Horizon", "Decoration", 8, 12, "Decoration", "deco_sl_horizon", horizon(43),
        "A dusk sky in broad poster bands: indigo to violet to rose to a coral horizon line, dusty ground-haze below. Tiles horizontally.")
    add("deco.sl_sun", "Setting Sun", "Decoration", 4, 4, "Decoration", "deco_sl_sun", sun_low(47),
        "The setting sun as three flat concentric discs of warmth.")
    add("deco.sl_moon", "Crescent Moon", "Decoration", 3, 3, "Decoration", "deco_sl_moon", moon(53),
        "A flat crescent moon with a soft halo and one companion star.")
    add("deco.sl_starfield", "Starfield", "Decoration", 8, 4, "Decoration", "deco_sl_starfield", starfield(59),
        "A loose scatter of small stars and a few four-point glints. Tiles horizontally.")
    add("deco.sl_constellation", "Star-Fruit Constellation", "Decoration", 5, 4, "Decoration", "deco_sl_constellation", constellation_sky(61),
        "A five-pointed constellation of stars joined by gold hairlines around a teal heart.")
    return out


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(40, 30, 84), sheet_cell=56)


if __name__ == "__main__":
    export(sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None)

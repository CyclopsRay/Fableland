#!/usr/bin/env python3
"""Kingdom of Horror tile set — PumpKing's realm (KH): the dark wood and its castle.

Art language: SCRATCHBOARD. The sheet is black; nothing is drawn on it — light is scratched
out of it. Every surface is a thicket of fine scratched lines whose crowding says how much
moon falls there, and the lines never quite agree on a direction: hatching wanders, crosses
and knots, so that bark, bramble, root and stone are all the same tangle. Three colours of
scratch only: moon BLUE for everything the night shows, YELLOW for anything that makes its
own light (windows, fireflies, lanterns, the moon), RED for what should not be touched.
See `Docs/Art/RealmStyleBible.md` §Kingdom of Horror.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/horror.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "KingdomOfHorror"
SET = "kh"

NIGHT = rgb("#070A14")     # the board
SKY = rgb("#111B3D")       # every map's canvas: night blue, so the black board reads as a cut-out
BLUE_D = rgb("#1B2D6B")
BLUE = rgb("#3F6FD8")
BLUE_L = rgb("#9DBEFF")
RED = rgb("#D8323A")
RED_D = rgb("#7A161C")
YEL = rgb("#FFD23F")
YEL_L = rgb("#FFF1A8")

BRIEF = ("Scratchboard game sprite on transparent ground: a black board with light scratched out of it in "
         "fine, tangled, wandering hatch lines; crowded scratches where moonlight falls, bare black in shadow. "
         "Moon-blue scratches for everything, yellow only for things that give light, red only for danger. "
         "Entangled, chaotic, uneasy. Orthographic side view, no cast shadow, no text.")


def hatch(h, w, light, seed, angle=60.0, spacing=7.0, cross=True, x0=0.0, y0=0.0, period=None):
    """Scratched hatching: line coverage 0..1 for a light field 0..1. The lines wander —
    their phase is bent by noise — and a second set crosses them where the light is strong."""
    xx, yy = C.grid(h, w, x0, y0)
    bend = (C.fbm(xx / 32.0, yy / 32.0, seed, octaves=3, period=period) - 0.5) * spacing * 4.2
    out = np.zeros((h, w), np.float32)
    for k, (ang, gate) in enumerate(((angle, 0.0), (angle + 68.0, 0.50))):
        if k == 1 and not cross:
            break
        a = math.radians(ang)
        phase = ((xx * math.cos(a) + yy * math.sin(a) + bend * (1.0 if k == 0 else -0.7)) / spacing) % 1.0
        width = np.clip((light - gate) / (1.0 - gate), 0.0, 1.0) * 0.62
        line = C.smoothstep(width, width - 0.16, np.abs(phase - 0.5) * 2.0) * (width > 0.02)
        out = np.maximum(out, line)
    return out * (0.80 + 0.20 * C.vnoise(xx / 1.8, yy / 1.8, seed + 9))


def scratched(img, mask, light, seed, angle=60.0, spacing=7.0, lo=None, hi=None, cross=True):
    """A solid thing: the black board inside `mask`, with moonlight scratched into it."""
    C.over(img, NIGHT, mask)
    h, w = mask.shape
    lines = hatch(h, w, light, seed, angle, spacing, cross) * mask
    col = C.mix(BLUE if lo is None else lo, BLUE_L if hi is None else hi, np.clip(light, 0, 1))
    C.over(img, col, lines)
    return img


def scratch(img, pts, width, color=None, opacity=0.95, seed=0):
    """One scratched line: thin, sharp, slightly broken."""
    return C.ink_stroke(img, pts, width, BLUE_L if color is None else color, opacity, dry=0.22, edge=0.0, seed=seed, soft=0.8)


def tangle(img, box, n, seed, color=None, width=(0.9, 2.0), thorns=False, curl=0.6):
    """A knot of scratched curves inside `box` (x0, y0, x1, y1): root, bramble, twig."""
    rng = np.random.default_rng(seed)
    x0, y0, x1, y1 = box
    for i in range(n):
        p0 = np.array([rng.uniform(x0, x1), rng.uniform(y0, y1)])
        ang = rng.uniform(0, 2 * math.pi)
        ln = rng.uniform(0.25, 0.75) * min(x1 - x0, y1 - y0) * 1.4
        p3 = p0 + np.array([math.cos(ang), math.sin(ang)]) * ln
        p3 = np.clip(p3, [x0, y0], [x1, y1])
        p1 = p0 + rng.uniform(-1, 1, 2) * ln * curl
        p2 = p3 + rng.uniform(-1, 1, 2) * ln * curl
        pts = np.clip(C.bezier(p0, p1, p2, p3, n=14), [x0, y0], [x1, y1])
        col = (BLUE_L if rng.random() < 0.4 else BLUE) if color is None else color
        C.ink_stroke(img, pts, np.linspace(rng.uniform(*width), 0.5, len(pts)), col, rng.uniform(0.6, 0.95),
                     dry=0.25, seed=seed + i, soft=0.8)
        if thorns:
            for k in range(2, len(pts) - 1, 3):
                d = pts[k + 1] - pts[k - 1]
                nrm = np.array([-d[1], d[0]]) / (np.linalg.norm(d) + 1e-6) * rng.uniform(4.0, 8.0) * rng.choice([-1, 1])
                scratch(img, [pts[k], pts[k] + nrm], [1.2, 0.3], col, 0.9, seed + i * 31 + k)
    return img


# --------------------------------------------------------------------- ground


def earth_atlas(variants=4):
    """`ground.kh_earth` — black earth knotted with roots: sparse wandering scratches in the
    body, a moonlit crest of scratched grass along the top, roots hanging underneath."""
    Wn = variants * CELL
    base = C.new(CELL, Wn)
    base[..., :3] = NIGHT
    base[..., 3] = 1.0
    xx, yy = C.strip(variants)
    light = 0.16 + 0.26 * C.fbm(xx / 64.0, yy / 64.0, 3, octaves=3, period=(Wn // 64, CELL // 64))
    lines = hatch(CELL, Wn, light, 5, angle=24.0, spacing=8.0, cross=False, period=(Wn // 32, CELL // 32))
    C.over(base, BLUE_D, lines * 0.9)
    big = C.new(CELL * 3, Wn * 3)                                  # roots, drawn wrapped so they cross every seam
    rng = np.random.default_rng(11)
    for i in range(4 * variants):
        p0 = np.array([rng.uniform(0, Wn), rng.uniform(0, CELL)])
        d = np.array([rng.uniform(-1, 1), rng.uniform(-0.5, 0.5)])
        ln = rng.uniform(70, 190)
        p3 = p0 + d / np.linalg.norm(d) * ln
        pts = C.bezier(p0, p0 + rng.uniform(-50, 50, 2), p3 + rng.uniform(-50, 50, 2), p3, n=14)
        for ox in (0, Wn, 2 * Wn):
            for oy in (0, CELL, 2 * CELL):
                C.ink_stroke(big, pts + np.array([ox, oy]), np.linspace(rng.uniform(1.4, 3.0), 0.6, len(pts)),
                             C.mix(BLUE_D, BLUE, 0.45), 0.70, dry=0.3, seed=100 + i, soft=0.8)
    mid = big[CELL:2 * CELL, Wn:2 * Wn]
    C.over(base, mid[..., :3], mid[..., 3])

    def cap(cell, g):
        d = C.carve_top(cell, g, 21, variants, base=13.0, wobble=12.0)
        a = cell[..., 3]
        C.over(cell, BLUE_L, np.exp(-((d - 2.0) / 1.6) ** 2) * a * 0.95)           # the line you stand on
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        lit = np.clip(1.0 - d / 30.0, 0.0, 1.0) * a
        C.over(cell, BLUE, hatch(CELL, CELL, lit * 0.9, 25, angle=82.0, spacing=5.0, cross=True, x0=g * CELL, period=(Wn // 32, 0)) * a)
        rng2 = np.random.default_rng(300 + g)
        top = 13.0 - d[0, :] + 0.0                                                   # row of the crest per column
        for _ in range(16):                                                          # grass, scratched upward
            mx = rng2.uniform(3, CELL - 3)
            my = float(top[int(mx)]) + 2.0
            scratch(cell, [(mx, my), (mx + rng2.uniform(-5, 5), my - rng2.uniform(6, 15))], [1.3, 0.3], BLUE_L, 0.9, int(rng2.integers(1 << 30)))

    def side(cell, g, left):
        d = C.carve_side(cell, left, 31 + g, inset=8.0, wobble=10.0)
        a = cell[..., 3]
        C.over(cell, BLUE_L if left else BLUE, np.exp(-((d - 1.8) / 1.5) ** 2) * a * (0.95 if left else 0.6))

    def underside(cell, g):
        d = C.carve_under(cell, g, 41, variants, depth=44.0)
        rng2 = np.random.default_rng(500 + g)
        for _ in range(7):                                                           # roots dangle below
            mx = rng2.uniform(6, CELL - 6)
            ln = rng2.uniform(14, 44)
            y0 = CELL - 46.0
            pts = C.bezier((mx, y0), (mx + rng2.uniform(-12, 12), y0 + ln * 0.5), (mx + rng2.uniform(-16, 16), y0 + ln), n=8)
            C.ink_stroke(cell, pts, np.linspace(1.8, 0.4, len(pts)), BLUE, 0.8, dry=0.3, seed=int(rng2.integers(1 << 30)), soft=0.8)
        cell[..., 3] = np.maximum(cell[..., 3], 0.0)

    return C.strip_atlas(base, cap, side, underside, variants)


def stone_atlas(variants=4):
    """`ground.kh_stone` — castle masonry: every block's lit edges scratched out of the
    black, its face hatched a little; a bright coping along the top, corbels underneath."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    bh, bw = 32.0, 64.0
    row = np.floor(yy / bh)
    fx = (xx + (row % 2) * bw * 0.5) % bw
    fy = yy - row * bh
    base = C.new(CELL, Wn)
    base[..., :3] = NIGHT
    base[..., 3] = 1.0
    face = 0.10 + 0.30 * C.fbm(np.floor((xx + (row % 2) * bw * 0.5) / bw) * 7.3 + 0.5, row * 3.1 + 0.5, 3, octaves=1)
    inside = (fx > 4) & (fx < bw - 3) & (fy > 4) & (fy < bh - 3)
    C.over(base, BLUE_D, hatch(CELL, Wn, face, 5, angle=40.0, spacing=6.5, cross=False, period=(Wn // 32, CELL // 32)) * inside)
    wob = (C.vnoise(xx / 5.0, yy / 5.0, 7, period=(Wn // 5 if Wn % 5 == 0 else 0, 0)) - 0.5) * 1.6
    C.over(base, BLUE_L, np.exp(-((fy - 3.0 + wob) / 1.2) ** 2) * 0.85)             # top edge of each block, moonlit
    C.over(base, BLUE, np.exp(-((fx - 3.0 + wob) / 1.2) ** 2) * 0.70)               # and its left edge

    def cap(cell, g):
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        C.over(cell, NIGHT, (gy < 12.0).astype(np.float32))
        C.over(cell, BLUE_L, (gy < 3.0).astype(np.float32) * 0.97)                  # the line you stand on
        C.over(cell, BLUE, (np.abs(gy - 10.5) < 1.1).astype(np.float32) * 0.9)

    def side(cell, g, left):
        gx, gy = C.grid(CELL, CELL)
        dist = gx if left else (CELL - gx)
        C.over(cell, NIGHT, (dist < 7.0).astype(np.float32))
        C.over(cell, BLUE_L if left else BLUE, (np.abs(dist - 1.6) < 1.3).astype(np.float32) * (0.95 if left else 0.55))

    def underside(cell, g):
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        tooth = ((gx % 32.0) < 18.0)                                                 # corbels: a row of teeth
        cut = CELL - np.where(tooth, 6.0, 26.0)
        cell[..., 3] *= (gy < cut).astype(np.float32)
        C.over(cell, BLUE, (np.abs(gy - cut + 1.5) < 1.2).astype(np.float32) * cell[..., 3] * 0.8)
        C.multiply_inside(cell, 1.0 - C.smoothstep(CELL - 60.0, CELL - 8.0, gy) * 0.5)

    return C.strip_atlas(base, cap, side, underside, variants)


# ------------------------------------------------------------------ platforms


def branch(wc, seed):
    """A gnarled limb to stand on: its back runs along the canvas top, lit by the moon;
    twigs claw downward from it."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    belly = 30.0 + np.sin(xx / W * math.pi) * 12.0 + (C.fbm(xx / 46.0, yy * 0 + 1.0, seed, octaves=3) - 0.5) * 16.0
    taper = C.smoothstep(0.0, W * 0.10, xx) * C.smoothstep(W, W * 0.90, xx)
    body = (yy > 3.0) * C.smoothstep(belly * taper + 1.5, belly * taper - 1.5, yy - 3.0)
    light = np.clip(1.0 - (yy - 3.0) / (belly * 0.9 + 1.0), 0.0, 1.0) ** 1.4
    scratched(img, body, light, seed + 1, angle=8.0, spacing=5.5)
    C.over(img, BLUE_L, np.exp(-((yy - 4.5) / 1.4) ** 2) * (body > 0.3) * 0.95)
    rng = np.random.default_rng(seed)
    for i in range(int(wc * 3)):
        x0 = rng.uniform(0.08, 0.92) * W
        ln = rng.uniform(36.0, 86.0)
        end = (x0 + rng.uniform(-40, 40), 34.0 + ln)
        tw = C.bezier((x0, 26.0), (x0 + rng.uniform(-26, 26), 30.0 + ln * 0.5), end, n=10)
        C.ink_stroke(img, tw, np.linspace(3.2, 0.5, len(tw)), BLUE, 0.9, dry=0.25, seed=seed + 10 + i, soft=0.8)
        for s in (-1, 1):
            scratch(img, [tw[6], (tw[6][0] + s * rng.uniform(8, 18), tw[6][1] + rng.uniform(6, 18))], [1.4, 0.3], BLUE, 0.85, seed + 40 + i)
    return img


def plank(wc, seed):
    """A hoarding of rotten planks on two brackets: castle footing for the unwary."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    deck = C.rect_mask(H, W, 2.0, 3.0, W - 2.0, 30.0)
    scratched(img, deck, np.clip(1.0 - (yy - 3.0) / 30.0, 0, 1) * 0.9, seed, angle=2.0, spacing=4.5, cross=False)
    C.over(img, BLUE_L, (np.abs(yy - 4.2) < 1.3) * deck * 0.95)
    rng = np.random.default_rng(seed)
    for gx in np.arange(40.0, W - 20.0, rng.uniform(52, 70)):
        C.over(img, NIGHT, C.rect_mask(H, W, gx - 1.6, 4.0, gx + 1.6, 30.0))
    for bx in (W * 0.16, W * 0.84):
        tri = C.poly_mask(H, W, [(bx - 7.0, 30.0), (bx + 7.0, 30.0), (bx + 5.0, 82.0), (bx - 5.0, 62.0)])
        scratched(img, tri, np.full((H, W), 0.45, np.float32), seed + int(bx), angle=70.0, spacing=5.0, cross=False)
        scratch(img, [(bx - 30.0, 32.0), (bx - 2.0, 66.0)], [1.6, 0.8], BLUE, 0.9, seed + int(bx) + 1)
    return img


# --------------------------------------------------------------- soft volumes


def bramble(wc, hc, seed):
    """A thicket of thorn: a knot of scratched canes, every one barbed, with red berries.
    It holds whoever blunders in."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    env = C.smoothstep(1.0, 0.72, np.hypot((xx - W * 0.5) / (W * 0.50), (yy - H * 0.56) / (H * 0.50)))
    C.over(img, NIGHT, env * 0.55)
    knot = C.new(H, W)                                           # drawn apart, then given the thicket's ragged outline
    tangle(knot, (6, 10, W - 6, H - 4), int(wc * hc * 13), seed, width=(1.2, 2.8), thorns=True, curl=0.8)
    ragged = C.smoothstep(0.30, 0.60, env + (C.fbm(xx / 26.0, yy / 26.0, seed + 5, octaves=3) - 0.5) * 0.5)
    knot[..., 3] *= ragged
    C.over_img(img, knot)
    rng = np.random.default_rng(seed + 1)
    for _ in range(int(wc * hc * 2.5)):
        cx, cy = rng.uniform(0.22, 0.78) * W, rng.uniform(0.30, 0.82) * H
        C.over(img, RED, C.aa(C.sdf_circle(H, W, cx, cy, rng.uniform(2.6, 4.2)), 1.3) * 0.95)
        C.over(img, YEL_L, C.aa(C.sdf_circle(H, W, cx - 1.2, cy - 1.2, 0.9), 1.0) * 0.8)
    return img


def fog(wc, hc, seed):
    """Ground fog: not a wash — a drift of short level scratches, thick in the middle."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    n = C.fbm(xx / 90.0, yy / 30.0, seed, octaves=4)
    env = np.exp(-(((yy - H * 0.55) / (H * 0.30)) ** 2)) * C.smoothstep(0.0, W * 0.12, xx) * C.smoothstep(W, W * 0.88, xx)
    dens = C.smoothstep(0.30, 0.70, n * 0.8 + env * 0.5) * env
    lines = hatch(H, W, dens * 0.95, seed + 1, angle=90.0, spacing=5.0, cross=False)
    C.over(img, BLUE_L, lines * 0.55)
    C.over(img, BLUE, dens * 0.16)
    return img


def canopy(wc, hc, seed):
    """A crown of bare twigs, finer and looser than bramble — and something in it is
    looking back."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    env = C.smoothstep(1.0, 0.70, np.hypot((xx - W * 0.5) / (W * 0.50), (yy - H * 0.5) / (H * 0.50)))
    C.over(img, NIGHT, env * 0.40)
    knot = C.new(H, W)
    tangle(knot, (4, 4, W - 4, H - 4), int(wc * hc * 9), seed, width=(0.8, 2.0), curl=0.5)
    knot[..., 3] *= C.smoothstep(0.26, 0.58, env + (C.fbm(xx / 30.0, yy / 30.0, seed + 5, octaves=3) - 0.5) * 0.5)
    C.over_img(img, knot)
    rng = np.random.default_rng(seed + 2)
    for _ in range(3):                                          # eyes
        cx, cy = rng.uniform(0.2, 0.8) * W, rng.uniform(0.25, 0.75) * H
        for s in (-1, 1):
            C.over(img, YEL, C.aa(C.sdf_circle(H, W, cx + s * 7.0, cy, 2.6), 1.2) * 0.97)
            C.over(img, NIGHT, C.aa(C.sdf_circle(H, W, cx + s * 7.0, cy, 0.9), 0.8) * 0.9)
    return img


# -------------------------------------------------------------------- hazards


def trap(seed):
    """A sprung-open bear trap: two jaws of teeth, red at the points."""
    img = C.cells(2, 1)
    H, W = img.shape[:2]
    cx, base = W * 0.5, H - 6.0
    scratch(img, [(cx - 92.0, base), (cx + 92.0, base)], 3.4, BLUE, 0.95, seed)                 # the plate
    C.over(img, BLUE_L, C.aa(C.sdf_circle(H, W, cx, base - 6.0, 9.0), 1.4) * 0.95)             # the pan
    for s in (-1, 1):
        jaw = C.bezier((cx + s * 8.0, base - 4.0), (cx + s * 96.0, base - 22.0), (cx + s * 86.0, base - 86.0), n=16)
        C.ink_stroke(img, jaw, 3.4, BLUE_L, 0.97, dry=0.15, seed=seed + 1 + s, soft=0.8)
        for k in range(2, 15, 2):                                # teeth point inward
            p = jaw[k]
            tip = (p[0] - s * 15.0, p[1] - 6.0)
            C.ink_stroke(img, [p, tip], [3.0, 0.4], BLUE_L, 0.97, dry=0.1, seed=seed + 20 + k, soft=0.8)
            C.over(img, RED, C.aa(C.sdf_circle(H, W, tip[0], tip[1], 2.2), 1.2) * 0.95)
    return img


def wisp(seed):
    """A will-o'-the-wisp: a yellow flame that gives light and burns."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx, cy = W * 0.5, H * 0.60
    C.radial_glow(img, cx, cy, 30.0, YEL, 0.45)
    r = np.hypot((xx - cx) / 24.0, (yy - cy) / 30.0)
    lick = (yy < cy) * np.exp(-(((xx - cx - np.sin(yy / 13.0) * 7.0) / (5.0 + (yy - (cy - 74.0)) * 0.26)) ** 2)) * C.smoothstep(cy - 80.0, cy - 30.0, yy)
    flame = np.maximum(C.smoothstep(1.0, 0.75, r), lick)
    C.over(img, YEL, flame * 0.97)
    C.over(img, YEL_L, C.smoothstep(0.62, 0.30, r) * 0.97)
    C.over(img, RED, C.smoothstep(0.80, 1.0, r) * flame * 0.8)
    rng = np.random.default_rng(seed)
    for a in np.linspace(0, 2 * math.pi, 16, endpoint=False):    # scratched rays
        r0, r1 = rng.uniform(32, 38), rng.uniform(44, 58)
        scratch(img, [(cx + math.cos(a) * r0, cy + math.sin(a) * r0), (cx + math.cos(a) * r1, cy + math.sin(a) * r1)], [1.2, 0.3], YEL, 0.8, seed + int(a * 10))
    for s in (-1, 1):                                            # two black eyes
        C.over(img, NIGHT, C.aa(C.sdf_circle(H, W, cx + s * 8.0, cy - 2.0, 3.4), 1.2) * 0.95)
    return img


# ---------------------------------------------------------------- decoration


def tree(wc, hc, seed, face=True):
    """A dead tree: a twisted trunk scratched on its moon side, limbs that fork and claw —
    and, in the big one, a hollow that is nearly a face."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    segs = []

    def grow(p, ang, ln, width, depth):
        bend = rng.uniform(-0.5, 0.5)
        margin = 12.0 + width                       # the limb's own thickness stays on the board too

        def on_board(q):
            return margin <= q[0] <= W - margin and q[1] >= margin

        def tip(a, length):
            return (p[0] + math.cos(a) * length, p[1] + math.sin(a) * length)

        # A limb that would leave the board turns back toward the trunk; if that leaves it
        # too, it stops short. (Clamping the tip to the edge, as this used to, lays the limb
        # along the edge and lets the sprite's border cut it.)
        if not on_board(tip(ang, ln)):
            if on_board(tip(math.pi - ang, ln)):
                ang = math.pi - ang
            else:
                while ln > 10.0 and not on_board(tip(ang, ln)):
                    ln *= 0.75
        end = tip(ang, ln)
        mid = (p[0] + math.cos(ang + bend) * ln * 0.5, p[1] + math.sin(ang + bend) * ln * 0.5)
        mid = (float(np.clip(mid[0], margin, W - margin)), float(max(mid[1], margin)))
        end = (float(np.clip(end[0], margin, W - margin)), float(np.clip(end[1], margin, H)))
        segs.append((p, mid, end, width, depth))
        if depth >= 6 or ln < 20.0:
            return
        for s in ((-1, 1) if depth < 4 else (rng.choice([-1, 1]),)):
            if rng.random() < 0.94:
                grow(end, ang + s * rng.uniform(0.30, 0.85), ln * rng.uniform(0.60, 0.82), width * 0.62, depth + 1)

    trunk_w = W * 0.085
    grow((W * 0.5, H - 2.0), -math.pi / 2 + rng.uniform(-0.12, 0.12), H * 0.30, trunk_w, 0)
    for (p, mid, end, width, depth) in segs:                     # the board first...
        C.ink_stroke(img, C.bezier(p, mid, end, n=10), np.linspace(width + 1.5, width * 0.62 + 1.2, 11), NIGHT, 1.0, dry=0.0, seed=seed, soft=0.9)
    for i, (p, mid, end, width, depth) in enumerate(segs):       # ...then the moon scratched down one side of everything
        pts = C.bezier(p, mid, end, n=10)
        off = np.array([-max(1.0, width * 0.55), -max(0.6, width * 0.20)])
        scratch(img, pts + off, np.linspace(max(0.9, width * 0.16), 0.5, 11), BLUE_L, 0.9, seed + i)
        if width > 5.0:                                          # bark: the moon side of a thick limb is hatched
            for frac, tone in ((0.30, BLUE), (0.02, BLUE_D)):
                scratch(img, pts + np.array([-width * frac, 0.0]), np.linspace(max(0.8, width * 0.10), 0.4, 11), tone, 0.8, seed + i * 13)
            for k in range(1, 10):
                q = pts[k]
                scratch(img, [(q[0] - width * 0.55, q[1] - 2.0), (q[0] + width * 0.10, q[1] + 5.0)], [1.0, 0.3], BLUE, 0.70, seed + i * 17 + k)
    if face:                                                     # a hollow: two eyes and a mouth, lit from inside
        cx, cy = W * 0.5, H * 0.80
        u = trunk_w
        for s in (-1, 1):                                        # slanted eye-slits
            C.over(img, YEL, C.poly_mask(H, W, [(cx + s * u * 0.14, cy + u * 0.10), (cx + s * u * 0.62, cy - u * 0.34),
                                                (cx + s * u * 0.58, cy + u * 0.02)]) * 0.97)
        jag = [(cx - u * 0.62, cy + u * 0.72)] + [(cx - u * 0.62 + u * 1.24 * k / 8.0, cy + u * (0.98 if k % 2 else 0.66)) for k in range(1, 8)] + \
              [(cx + u * 0.62, cy + u * 0.72)] + [(cx + u * 0.62 - u * 1.24 * k / 8.0, cy + u * (1.42 if k % 2 else 1.10)) for k in range(1, 8)]
        C.over(img, YEL, C.poly_mask(H, W, jag) * 0.95)
    return img


def castle_far(wc, hc, seed):
    """The castle against the sky: a black cut-out of towers on a hill, its moon edge
    scratched in blue, windows lit yellow, red pennants. Solid to the canvas bottom."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    rng = np.random.default_rng(seed)
    shape = np.zeros((H, W), np.float32)
    towers = [(0.14, 0.50, 0.050), (0.30, 0.34, 0.060), (0.50, 0.16, 0.075), (0.68, 0.40, 0.055), (0.86, 0.52, 0.050)]
    # The hill is a dome that comes down to nothing at both ends of the sprite: a far castle
    # is one tile pinned on the sky, so a hill that ran to the edges would end in two walls.
    dome = np.clip(1.0 - np.abs((xx - W * 0.5) / (W * 0.5)) ** 3.0, 0.0, 1.0)
    hill = H - (H * 0.20 + np.exp(-(((xx - W * 0.5) / (W * 0.42)) ** 2)) * H * 0.14) * dome
    shape = np.maximum(shape, (yy > hill).astype(np.float32))
    shape = np.maximum(shape, C.rect_mask(H, W, W * 0.12, H * 0.60, W * 0.88, H))            # the curtain wall
    crenel = ((xx % 26.0) < 14.0) & (yy > H * 0.60 - 12.0) & (yy < H * 0.60 + 1.0) & (xx > W * 0.12) & (xx < W * 0.88)
    shape = np.maximum(shape, crenel.astype(np.float32))
    for (fx, top, hw) in towers:
        shape = np.maximum(shape, C.rect_mask(H, W, W * (fx - hw), H * (top + 0.10), W * (fx + hw), H))
        shape = np.maximum(shape, C.poly_mask(H, W, [(W * (fx - hw * 1.25), H * (top + 0.10)), (W * fx, H * top), (W * (fx + hw * 1.25), H * (top + 0.10))]))
    C.over(img, NIGHT, shape)
    edge = np.clip(shape - C.shift(shape, 3, 2), 0.0, 1.0)                                    # the side the moon is on
    C.over(img, BLUE_L, edge * 0.85)
    C.over(img, BLUE_D, hatch(H, W, np.full((H, W), 0.22, np.float32), seed + 1, angle=50.0, spacing=9.0, cross=False) * shape * 0.8)
    for (fx, top, hw) in towers:
        for k in range(int(rng.integers(1, 4))):
            wy = H * (top + 0.16 + 0.11 * k)
            win = C.rect_mask(H, W, W * fx - 5.0, wy, W * fx + 5.0, wy + 16.0, radius=4.0)
            C.over(img, YEL, win * 0.95)
        pole = (W * fx, H * top)
        scratch(img, [pole, (pole[0], pole[1] - 22.0)], 1.0, BLUE_L, 0.9, seed + int(fx * 100))
        C.over(img, RED, C.poly_mask(H, W, [(pole[0], pole[1] - 22.0), (pole[0] + 18.0, pole[1] - 17.0), (pole[0], pole[1] - 11.0)]) * 0.95)
    return img


def forest_far(wc, hc, seed):
    """The wood on the horizon: two ranks of black spires, the nearer scratched along its
    crest. Loops; solid below."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    for k, (base, tone, light) in enumerate([(0.44, C.mix(NIGHT, SKY, 0.55), 0.0), (0.62, NIGHT, 0.8)]):
        slow = C.fbm(xx / 256.0, yy * 0 + 1.0 + k, seed + k, octaves=3, period=(W // 256, 0))
        warp = xx + (C.fbm(xx / 128.0, yy * 0 + 4.0 + k, seed + 30 + k, octaves=3, period=(W // 128, 0)) - 0.5) * 150.0
        spikes = np.abs(((warp + k * 37.0) / (32.0 + 32.0 * k)) % 1.0 - 0.5) * 2.0               # tree tops, unevenly spaced
        jit = C.fbm(xx / 64.0, yy * 0 + 2.0, seed + 20 + k, octaves=2, period=(W // 64, 0))
        line = H * (base - (slow - 0.5) * 0.30) - (1.0 - spikes) ** 1.6 * H * (0.10 + 0.14 * jit)
        m = C.smoothstep(-1.2, 1.2, yy - line)
        C.over(img, tone, m)
        if light > 0:
            C.over(img, BLUE, np.exp(-((yy - line - 2.0) / 1.5) ** 2) * 0.75)
    return img


def moon(seed):
    """The moon: the one large yellow thing, its craters and its halo scratched."""
    img = C.cells(4, 4)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx, cy, r = W / 2.0, H / 2.0, W * 0.30
    d = np.hypot(xx - cx, yy - cy)
    C.radial_glow(img, cx, cy, r * 0.9, YEL, 0.22)
    disc = C.smoothstep(r + 1.0, r - 1.0, d)
    C.over(img, YEL, disc * 0.97)
    crater = C.smoothstep(0.56, 0.72, C.fbm(xx / 46.0, yy / 46.0, seed, octaves=3))
    C.over(img, C.mix(YEL, RED_D, 0.45), hatch(H, W, crater * 0.9, seed + 1, angle=30.0, spacing=5.0) * disc * 0.9)
    C.over(img, YEL_L, C.smoothstep(r * 0.95, r * 0.55, np.hypot(xx - cx + r * 0.25, yy - cy + r * 0.25)) * disc * 0.35)
    rng = np.random.default_rng(seed)
    for a in np.linspace(0, 2 * math.pi, 48, endpoint=False):
        r0, r1 = r + rng.uniform(8, 14), r + rng.uniform(20, 52)
        scratch(img, [(cx + math.cos(a) * r0, cy + math.sin(a) * r0), (cx + math.cos(a) * r1, cy + math.sin(a) * r1)], [1.1, 0.3], YEL, 0.55, seed + int(a * 20))
    return img


def fireflies(seed):
    """Fireflies: a scatter of yellow points, each with a scratched cross of light."""
    img = C.cells(4, 3)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for i in range(20):
        cx, cy = rng.uniform(14, W - 14), rng.uniform(14, H - 14)
        C.radial_glow(img, cx, cy, 8.0, YEL, 0.5)
        C.over(img, YEL_L, C.aa(C.sdf_circle(H, W, cx, cy, rng.uniform(1.8, 3.0)), 1.2) * 0.97)
        ln = rng.uniform(5, 10)
        for a in (0.0, math.pi / 2):
            scratch(img, [(cx - math.cos(a) * ln, cy - math.sin(a) * ln), (cx, cy), (cx + math.cos(a) * ln, cy + math.sin(a) * ln)], [0.3, 0.9, 0.3], YEL, 0.7, seed + i)
    return img


def lantern(seed):
    """A jack-o'-lantern on a crooked post: PumpKing's subjects light their own way."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx, cy = W * 0.5, H * 0.30
    C.radial_glow(img, cx, cy, 30.0, YEL, 0.40)
    post = C.bezier((cx + 4.0, H - 3.0), (cx - 10.0, H * 0.75), (cx + 2.0, cy + 26.0), n=10)
    C.ink_stroke(img, post, np.linspace(5.5, 3.5, 11), NIGHT, 1.0, dry=0.0, seed=seed, soft=0.9)
    scratch(img, post + np.array([-3.0, 0.0]), np.linspace(1.4, 0.8, 11), BLUE_L, 0.9, seed + 1)
    r = np.hypot((xx - cx) / 30.0, (yy - cy) / 25.0)
    body = C.smoothstep(1.0, 0.94, r)
    rib = 0.5 + 0.5 * np.cos((xx - cx) / 30.0 * math.pi * 3.0)
    C.over(img, C.mix(RED_D, YEL, 0.35 + 0.45 * rib), body * 0.97)
    C.over(img, NIGHT, np.exp(-((r - 1.0) / 0.05) ** 2) * 0.9)
    for s in (-1, 1):                                             # eyes
        C.over(img, YEL_L, C.poly_mask(H, W, [(cx + s * 16.0, cy - 2.0), (cx + s * 6.0, cy - 2.0), (cx + s * 11.0, cy - 12.0)]) * 0.97)
    teeth = [(cx - 17.0, cy + 7.0), (cx - 9.0, cy + 13.0), (cx - 3.0, cy + 8.0), (cx + 3.0, cy + 13.0), (cx + 9.0, cy + 8.0),
             (cx + 17.0, cy + 7.0), (cx + 10.0, cy + 17.0), (cx, cy + 19.0), (cx - 10.0, cy + 17.0)]
    C.over(img, YEL_L, C.poly_mask(H, W, teeth) * 0.97)
    scratch(img, [(cx, cy - 25.0), (cx + 5.0, cy - 36.0)], [3.0, 1.2], BLUE, 0.95, seed + 2)   # stalk
    return img


def window(seed):
    """A lit gothic window, for a castle wall: yellow glass, black tracery."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx = W * 0.5
    arch = (((xx - cx) / 30.0) ** 2 + ((yy - H * 0.34) / 44.0) ** 2 < 1.0) & (yy < H * 0.34)
    glass = (arch | ((np.abs(xx - cx) < 30.0) & (yy >= H * 0.34) & (yy < H * 0.80))).astype(np.float32)
    C.radial_glow(img, cx, H * 0.5, 40.0, YEL, 0.30)
    C.over(img, C.mix(YEL, YEL_L, C.fbm(xx / 20.0, yy / 20.0, seed, octaves=3)), glass * 0.96)
    for x in (cx,):
        C.over(img, NIGHT, (np.abs(xx - x) < 2.2) * glass)
    for y in (H * 0.34, H * 0.56):
        C.over(img, NIGHT, (np.abs(yy - y) < 2.0) * glass)
    edge = np.clip(C.blur(glass, 2.0) * (1.0 - glass) * 4.0, 0.0, 1.0)
    C.over(img, NIGHT, edge)
    C.over(img, BLUE, np.clip(C.blur(glass, 4.0) * (1.0 - C.blur(glass, 2.0)) * 5.0 - edge, 0.0, 1.0) * 0.6)
    return img


def fence(seed):
    """Iron railings with spear heads, leaning a little."""
    img = C.cells(3, 1)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for y in (H * 0.42, H * 0.82):
        scratch(img, [(4.0, y), (W - 4.0, y + rng.uniform(-3, 3))], 2.2, BLUE, 0.95, seed + int(y))
    for i, x in enumerate(np.arange(14.0, W - 8.0, 26.0)):
        lean = rng.uniform(-4, 4)
        top = (x + lean, H * 0.14 + rng.uniform(0, 8))
        scratch(img, [(x, H - 2.0), top], 2.0, BLUE_L if i % 3 == 0 else BLUE, 0.95, seed + 50 + i)
        C.over(img, BLUE_L, C.poly_mask(H, W, [(top[0] - 5.0, top[1] + 9.0), (top[0], top[1] - 9.0), (top[0] + 5.0, top[1] + 9.0)]) * 0.95)
    return img


# ------------------------------------------------------------ the castle slope


def _earth_strip(variants=4):
    """The earth material strip `earth_atlas` cuts its cells from, rebuilt with the same
    seeds: one row, `variants` cells wide, periodic in x and repeating every cell in y. The
    slope block is cut from it so its body is the same paint as the ground it stands in."""
    Wn = variants * CELL
    base = C.new(CELL, Wn)
    base[..., :3] = NIGHT
    base[..., 3] = 1.0
    xx, yy = C.strip(variants)
    light = 0.16 + 0.26 * C.fbm(xx / 64.0, yy / 64.0, 3, octaves=3, period=(Wn // 64, CELL // 64))
    lines = hatch(CELL, Wn, light, 5, angle=24.0, spacing=8.0, cross=False, period=(Wn // 32, CELL // 32))
    C.over(base, BLUE_D, lines * 0.9)
    big = C.new(CELL * 3, Wn * 3)
    rng = np.random.default_rng(11)
    for i in range(4 * variants):
        p0 = np.array([rng.uniform(0, Wn), rng.uniform(0, CELL)])
        d = np.array([rng.uniform(-1, 1), rng.uniform(-0.5, 0.5)])
        ln = rng.uniform(70, 190)
        p3 = p0 + d / np.linalg.norm(d) * ln
        pts = C.bezier(p0, p0 + rng.uniform(-50, 50, 2), p3 + rng.uniform(-50, 50, 2), p3, n=14)
        for ox in (0, Wn, 2 * Wn):
            for oy in (0, CELL, 2 * CELL):
                C.ink_stroke(big, pts + np.array([ox, oy]), np.linspace(rng.uniform(1.4, 3.0), 0.6, len(pts)),
                             C.mix(BLUE_D, BLUE, 0.45), 0.70, dry=0.3, seed=100 + i, soft=0.8)
    mid = big[CELL:2 * CELL, Wn:2 * Wn]
    C.over(base, mid[..., :3], mid[..., 3])
    return base


def slope(seed):
    """`ground.kh_slope` — a block of rooted earth whose top is a ramp, rising to the right
    one cell in two. Two cells square: the ramp crosses the upper row and the lower row is
    solid, so the scratched grass line can sit a little under the walking line (as it does
    on level earth) without dipping out of the sprite at the low end. Blocks step up and to
    the right into one unbroken slope; Flip H makes it rise to the left."""
    W = H = 2 * CELL
    strip = _earth_strip(4)
    img = np.concatenate([strip[:, :W], strip[:, :W]], axis=0).copy()        # the body: two rows of the same earth
    xx, yy = C.grid(H, W)
    walk = CELL - xx * (CELL / W)                                             # the collider's ramp: (0, CELL) -> (W, 0)
    lip = C.fbm(xx / 32.0, yy * 0 + 3.3, seed, octaves=3, period=(W // 32, 0)) # same at both ends, so blocks join
    top = walk + 13.0 + (lip - 0.5) * 12.0                                    # 13 px under it, like `carve_top`
    img[..., 3] *= C.smoothstep(top - 1.2, top + 1.2, yy)
    d = yy - top
    a = img[..., 3]
    C.over(img, BLUE_L, np.exp(-((d - 2.0) / 1.6) ** 2) * a * 0.95)           # the line you stand on
    lit = np.clip(1.0 - d / 30.0, 0.0, 1.0) * a
    C.over(img, BLUE, hatch(H, W, lit * 0.9, seed + 4, angle=58.0, spacing=5.0, cross=True, period=(W // 32, 0)) * a)
    rng = np.random.default_rng(seed)
    crest = top[0, :]
    for _ in range(34):                                                       # grass, scratched up the slope's normal
        mx = rng.uniform(8, W - 8)
        my = float(crest[int(mx)]) + 2.0
        ln = rng.uniform(6, 15)
        tip = (mx + rng.uniform(-4, 4) - ln * 0.40, my - ln)
        if tip[1] < 3.0 or tip[0] < 3.0:
            continue                                                          # a blade must not be cut by the sheet
        scratch(img, [(mx, my), tip], [1.3, 0.3], BLUE_L, 0.9, int(rng.integers(1 << 30)))
    return img


# ----------------------------------------------------------------- the castle


def _masonry(img, mask, seed, bw=64.0, bh=32.0, lit=1.0):
    """Castle stone inside `mask`: the black board, each block's top and moon-side edge
    scratched out of it, its face hatched a little — the same marks as `stone_atlas`."""
    H, W = mask.shape
    xx, yy = C.grid(H, W)
    C.over(img, NIGHT, mask)
    row = np.floor(yy / bh)
    fx = (xx + (row % 2) * bw * 0.5) % bw
    fy = yy - row * bh
    face = 0.10 + 0.30 * C.fbm(np.floor((xx + (row % 2) * bw * 0.5) / bw) * 7.3 + 0.5, row * 3.1 + 0.5, seed, octaves=1)
    inside = (fx > 4) & (fx < bw - 3) & (fy > 4) & (fy < bh - 3)
    C.over(img, BLUE_D, hatch(H, W, face, seed + 2, angle=40.0, spacing=6.5, cross=False) * inside * mask)
    wob = (C.vnoise(xx / 5.0, yy / 5.0, seed + 4) - 0.5) * 1.6
    C.over(img, BLUE_L, np.exp(-((fy - 3.0 + wob) / 1.2) ** 2) * 0.85 * lit * mask)
    C.over(img, BLUE, np.exp(-((fx - 3.0 + wob) / 1.2) ** 2) * 0.70 * lit * mask)
    return img


def _moon_edge(img, mask, amount=0.9):
    """Scratch the moon's side (up and to the left) of a black cut-out."""
    edge = np.clip(mask - C.shift(mask, 3, 2), 0.0, 1.0)
    return C.over(img, BLUE_L, edge * amount)


def balcony(wc, seed):
    """`platform.kh_balcony` — the stone balcony of PumpKing's castle: a slab whose bright
    coping is the walking line, carried on corbels that step back toward the wall. The
    wall is on the RIGHT; Flip H for a wall on the left."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    slab = C.rect_mask(H, W, 0.0, 2.0, W, 40.0)
    _masonry(img, slab, seed, bw=64.0, bh=19.0)
    # The slab's face catches the moon: crowded scratches, so the one thing here you can
    # stand on is also the lightest stone in a wall of stone.
    face = ((yy > 13.0) & (yy < 37.0)).astype(np.float32) * slab
    C.over(img, BLUE, hatch(H, W, np.full((H, W), 0.85, np.float32), seed + 1, angle=16.0, spacing=4.5, cross=True) * face * 0.85)
    C.over(img, NIGHT, ((yy > 2.0) & (yy < 13.0)).astype(np.float32) * slab)
    C.over(img, BLUE_L, (yy < 7.0).astype(np.float32) * slab * 0.98)            # the line you stand on: a broad coping
    C.over(img, BLUE_L, (np.abs(yy - 11.0) < 1.2).astype(np.float32) * slab * 0.9)
    C.over(img, BLUE_L, (np.abs(yy - 38.5) < 1.3).astype(np.float32) * slab * 0.85)  # the slab's lower arris
    # One corbelled console under the slab, seen from the side: courses of stone stepping
    # out from the wall, each a little shorter than the one above, so the slab is carried
    # most of its length and the support is deepest against the wall.
    courses = 4
    step_h = 19.0
    console = np.zeros((H, W), np.float32)
    for k in range(courses):
        x0 = W * (0.22 + 0.19 * k)
        console = np.maximum(console, C.rect_mask(H, W, x0, 40.0 + step_h * k, W, 40.0 + step_h * (k + 1)))
    _masonry(img, console, seed + 7, bw=52.0, bh=step_h)
    for k in range(courses):                                                    # each course's moon-side nose and soffit
        x0 = W * (0.22 + 0.19 * k)
        y0, y1 = 40.0 + step_h * k, 40.0 + step_h * (k + 1)
        C.over(img, BLUE_L, ((np.abs(xx - x0 - 1.3) < 1.3) & (yy > y0) & (yy < y1)).astype(np.float32) * 0.9)
        x1 = W * (0.22 + 0.19 * (k + 1)) if k + 1 < courses else W
        C.over(img, BLUE, ((np.abs(yy - y1 + 1.2) < 1.1) & (xx > x0) & (xx < x1)).astype(np.float32) * 0.8)
    return img


def balustrade(wc, seed):
    """`deco.kh_balustrade` — a stone balustrade for a balcony's edge: turned balusters
    between a foot rail and a hand rail, waist high. Mostly air: whoever stands behind it
    still reads."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    top_y, foot_y = 58.0, H - 9.0
    shape = np.maximum(C.rect_mask(H, W, 0.0, top_y, W, top_y + 11.0), C.rect_mask(H, W, 0.0, foot_y, W, H))
    for x in np.arange(16.0, W, 32.0):                                         # a baluster every quarter cell
        belly = 5.0 + 4.5 * np.sin(np.clip((yy - top_y - 11.0) / (foot_y - top_y - 11.0), 0, 1) * math.pi) ** 2
        shape = np.maximum(shape, ((np.abs(xx - x) < belly) & (yy > top_y + 10.0) & (yy < foot_y + 1.0)).astype(np.float32))
    for x in (5.0, W - 5.0):                                                   # a newel post at each end
        shape = np.maximum(shape, C.rect_mask(H, W, x - 5.0, top_y - 12.0, x + 5.0, H))
    C.over(img, NIGHT, shape)
    C.over(img, BLUE_L, (np.abs(yy - top_y - 1.5) < 1.4).astype(np.float32) * shape * 0.95)   # the hand rail's top
    C.over(img, BLUE, (np.abs(yy - foot_y - 1.5) < 1.2).astype(np.float32) * shape * 0.85)
    _moon_edge(img, shape * ((yy > top_y + 10.0) & (yy < foot_y)).astype(np.float32), 0.75)
    return img


def _arch(H, W, cx, spring_y, half, rise, base_y):
    """A pointed (gothic) arch opening: two arcs meeting at the apex, straight jambs below."""
    xx, yy = C.grid(H, W)
    r = (half * half + rise * rise) / (2.0 * half)                             # arcs struck from the far jamb's side
    left = (np.hypot(xx - (cx + half - r), yy - spring_y) < r)
    right = (np.hypot(xx - (cx - half + r), yy - spring_y) < r)
    head = left & right & (yy <= spring_y)
    jambs = (np.abs(xx - cx) < half) & (yy > spring_y) & (yy < base_y)
    return (head | jambs).astype(np.float32)


def gate(wc, hc, seed):
    """`deco.kh_gate` — the castle's great door, shut: a pointed arch of scratched voussoirs,
    two leaves of studded planks, a portcullis' teeth showing under the arch, and a seam of
    yellow light between the leaves — someone is home, and you are not going in."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx = W * 0.5
    outer = _arch(H, W, cx, H * 0.46, W * 0.46, H * 0.40, H)
    inner = _arch(H, W, cx, H * 0.46, W * 0.36, H * 0.31, H)
    frame = np.clip(outer - inner, 0.0, 1.0)
    _masonry(img, frame, seed, bw=46.0, bh=30.0)
    _moon_edge(img, outer, 0.9)
    C.over(img, NIGHT, inner)                                                  # the door itself: black, then scratched
    plank = (np.abs(((xx - cx) / 26.0) % 1.0 - 0.5) < 0.035).astype(np.float32)
    C.over(img, BLUE_D, hatch(H, W, np.full((H, W), 0.30, np.float32), seed + 3, angle=88.0, spacing=6.0, cross=False) * inner * 0.9)
    C.over(img, BLUE, plank * inner * 0.75)
    for by in (H * 0.56, H * 0.72, H * 0.88):                                  # iron bands, studded
        band = (np.abs(yy - by) < 5.0).astype(np.float32) * inner
        C.over(img, NIGHT, band)
        C.over(img, BLUE_L, (np.abs(yy - by + 4.0) < 1.1).astype(np.float32) * inner * 0.9)
        for sx in np.arange(cx - W * 0.30, cx + W * 0.31, 26.0):
            C.over(img, BLUE_L, C.aa(C.sdf_circle(H, W, sx, by, 2.4), 1.2) * inner * 0.95)
    teeth_y = H * 0.40                                                         # the portcullis, drawn up, its points showing
    for tx in np.arange(cx - W * 0.30, cx + W * 0.31, 22.0):
        tooth = C.poly_mask(H, W, [(tx - 6.0, teeth_y - 60.0), (tx + 6.0, teeth_y - 60.0), (tx + 6.0, teeth_y), (tx, teeth_y + 16.0), (tx - 6.0, teeth_y)])
        C.over(img, NIGHT, tooth * inner)
        C.over(img, BLUE, np.clip(tooth - C.shift(tooth, 2, 0), 0, 1) * inner * 0.9)
    seam = np.exp(-((xx - cx) / 1.6) ** 2) * (yy > H * 0.47) * inner           # light between the leaves
    C.over(img, YEL, seam * 0.95)
    C.over(img, YEL_L, np.exp(-((xx - cx) / 0.7) ** 2) * (yy > H * 0.47) * inner * 0.9)
    C.over(img, YEL, np.exp(-((yy - H + 3.0) / 2.0) ** 2) * (np.abs(xx - cx) < W * 0.35) * inner * 0.75)   # …and under them
    return img


def door(wc, hc, seed):
    """`deco.kh_door` — the door onto the balcony, standing open: a small pointed arch with
    lamplight in it. It is how PumpKing comes out; nobody else goes in."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx = W * 0.5
    outer = _arch(H, W, cx, H * 0.42, W * 0.44, H * 0.36, H)
    inner = _arch(H, W, cx, H * 0.42, W * 0.33, H * 0.27, H)
    C.radial_glow(img, cx, H * 0.62, W * 0.34, YEL, 0.26)
    frame = np.clip(outer - inner, 0.0, 1.0)
    _masonry(img, frame, seed, bw=40.0, bh=26.0)
    _moon_edge(img, outer, 0.9)
    warm = np.clip(1.0 - np.hypot((xx - cx) / (W * 0.36), (yy - H * 0.70) / (H * 0.50)), 0.0, 1.0)
    C.over(img, C.mix(RED_D, YEL, np.clip(0.25 + 0.75 * warm, 0, 1)), inner * 0.97)       # lamplight, deepest at the sill
    C.over(img, YEL_L, C.smoothstep(0.45, 0.95, warm) * inner * 0.85)
    leaf = ((xx > cx + W * 0.12) & (xx < cx + W * 0.33)).astype(np.float32) * inner       # the open leaf, edge on
    C.over(img, NIGHT, leaf)
    C.over(img, BLUE, (np.abs(xx - cx - W * 0.12) < 1.2).astype(np.float32) * inner * 0.9)
    C.over(img, BLUE_D, hatch(H, W, np.full((H, W), 0.30, np.float32), seed + 5, angle=88.0, spacing=6.0, cross=False) * leaf * 0.9)
    return img


def battlement(wc, seed):
    """`deco.kh_battlement` — crenellations for a wall top: a merlon to each cell, standing
    on the canvas bottom, gaps at the canvas edges so the row tiles."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    fx = xx % CELL
    merlon = ((fx > 16.0) & (fx < CELL - 16.0) & (yy > 22.0)).astype(np.float32)
    sill = (yy > H - 26.0).astype(np.float32)
    shape = np.maximum(merlon, sill)
    _masonry(img, shape, seed, bw=48.0, bh=26.0)
    cap = ((fx > 16.0) & (fx < CELL - 16.0) & (yy > 22.0) & (yy < 31.0)).astype(np.float32)
    C.over(img, NIGHT, cap)
    C.over(img, BLUE_L, ((fx > 16.0) & (fx < CELL - 16.0) & (np.abs(yy - 23.5) < 1.5)).astype(np.float32) * 0.97)
    C.over(img, BLUE_L, ((np.abs(fx - 17.5) < 1.3) & (yy > 22.0) & (yy < H - 26.0)).astype(np.float32) * 0.85)
    C.over(img, BLUE, ((np.abs(yy - (H - 25.0)) < 1.2) & ((fx <= 16.0) | (fx >= CELL - 16.0))).astype(np.float32) * 0.85)
    slit = ((np.abs(fx - CELL * 0.5) < 3.0) & (yy > 44.0) & (yy < 78.0)).astype(np.float32)          # an arrow loop in each:
    img[..., 3] *= 1.0 - slit                                                                         # a real hole, not paint
    C.over(img, BLUE, ((np.abs(np.abs(fx - CELL * 0.5) - 4.2) < 1.0) & (yy > 44.0) & (yy < 78.0)).astype(np.float32) * 0.8)
    return img


def pennant(seed):
    """`deco.kh_pennant` — a red pennant on a pole: the one colour in this wood that means
    keep away, flying over the man who means it."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    px = W * 0.30
    scratch(img, [(px, H - 2.0), (px + 1.0, 10.0)], [2.2, 1.4], BLUE_L, 0.95, seed)
    C.over(img, YEL, C.aa(C.sdf_circle(H, W, px + 1.0, 9.0, 3.4), 1.2) * 0.95)             # a gilt finial
    tail = [(px + 2.0, 18.0), (W - 12.0, 30.0), (W - 34.0, 44.0), (W - 10.0, 60.0), (px + 2.0, 70.0)]
    flag = C.poly_mask(H, W, tail)
    C.over(img, RED, flag * 0.97)
    xx, yy = C.grid(H, W)
    C.over(img, RED_D, hatch(H, W, np.full((H, W), 0.5, np.float32), seed + 1, angle=14.0, spacing=6.0, cross=False) * flag * 0.9)
    C.over(img, YEL_L, C.aa(C.sdf_circle(H, W, px + 30.0, 44.0, 7.0), 1.4) * flag * 0.9)   # a pumpkin-yellow roundel
    return img


# -------------------------------------------------------------------- build


def build():
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.kh_earth", "Rooted Earth", "Ground", 1, 1, "Ground", "ground_kh_earth", earth_atlas(4),
        "Black earth knotted with roots: sparse wandering blue scratches, a moonlit crest of scratched grass on top, "
        "roots hanging beneath.", fill=True, group="terrain.kh_earth", variants=4)
    add("ground.kh_stone", "Castle Stone", "Ground", 1, 1, "Ground", "ground_kh_stone", stone_atlas(4),
        "Castle masonry: each block's moonlit edges scratched out of black, faces lightly hatched; bright coping on top, "
        "a row of corbels beneath.", fill=True, group="terrain.kh_stone", variants=4)
    add("ground.kh_slope", "Rooted Slope", "Ground", 2, 2, "Ground", "ground_kh_slope", slope(131),
        "A block of the same rooted earth whose top is a ramp rising to the right, one cell in two, with the moonlit "
        "crest of scratched grass running up it; solid below. Joins end to end into one long slope.",
        group="terrain.kh_earth")

    add("platform.kh_branch", "Gnarled Limb", "Platform", 4, 1, "Platform", "platform_kh_branch", branch(4, 61),
        "A gnarled tree limb to stand on, its back lit by the moon in scratched lines, twigs clawing downward.", walk=8)
    add("platform.kh_plank", "Rotten Hoarding", "Platform", 3, 1, "Platform", "platform_kh_plank", plank(3, 67),
        "A hoarding of rotten planks on two brackets, scratched grain, a bright top edge.", walk=8)
    add("platform.kh_balcony", "Castle Balcony", "Platform", 4, 1, "Platform", "platform_kh_balcony", balcony(4, 133),
        "A stone balcony slab seen from the side: a bright coping on top, scratched masonry, carried on stepped corbels "
        "that deepen toward the wall on its right.", walk=8)

    add("softvolume.kh_bramble", "Bramble", "SoftVolume", 3, 2, "SoftVolume", "softvolume_kh_bramble", bramble(3, 2, 71),
        "A thicket of thorn: a knot of barbed scratched canes with red berries. Enterable; it holds whoever enters.")
    add("softvolume.kh_fog", "Ground Fog", "SoftVolume", 4, 2, "SoftVolume", "softvolume_kh_fog", fog(4, 2, 73),
        "Ground fog as a drift of short level pale-blue scratches. Enterable soft volume.")
    add("softvolume.kh_canopy", "Bare Canopy", "SoftVolume", 4, 3, "SoftVolume", "softvolume_kh_canopy", canopy(4, 3, 75),
        "A crown of bare scratched twigs with pairs of small yellow eyes in it. Enterable soft volume.")

    add("hazard.kh_trap", "Bear Trap", "Hazard", 2, 1, "Hazard", "hazard_kh_trap", trap(81),
        "A sprung-open iron bear trap: two scratched jaws of teeth, red at the points.")
    add("hazard.kh_wisp", "Will-o'-the-Wisp", "Hazard", 1, 2, "Hazard", "hazard_kh_wisp", wisp(83),
        "A yellow will-o'-the-wisp flame with scratched rays and two black eyes.")

    add("deco.kh_tree", "Dead Tree (great)", "Decoration", 5, 8, "Decoration", "deco_kh_tree", tree(5, 8, 91),
        "A great dead tree: twisted black trunk scratched along its moon side, forking clawing limbs, "
        "and a hollow lit from inside like a face.")
    add("deco.kh_tree_b", "Dead Tree", "Decoration", 3, 6, "Decoration", "deco_kh_tree_b", tree(3, 6, 95, face=False),
        "A slimmer dead tree in the same scratched manner.")
    add("deco.kh_lantern", "Jack-o'-Lantern", "Decoration", 1, 2, "Decoration", "deco_kh_lantern", lantern(101),
        "A glowing jack-o'-lantern on a crooked post.")
    add("deco.kh_window", "Lit Window", "Decoration", 1, 2, "Decoration", "deco_kh_window", window(103),
        "A lit gothic window: yellow glass, black tracery.")
    add("deco.kh_fence", "Iron Railings", "Decoration", 3, 1, "Decoration", "deco_kh_fence", fence(105),
        "Leaning iron railings with spear heads, scratched in blue.")
    add("deco.kh_balustrade", "Balustrade", "Decoration", 4, 1, "Decoration", "deco_kh_balustrade", balustrade(4, 135),
        "A waist-high stone balustrade: turned balusters between a foot rail and a hand rail, a newel post at each end.")
    add("deco.kh_gate", "Castle Gate", "Decoration", 4, 5, "Decoration", "deco_kh_gate", gate(4, 5, 137),
        "A great castle door, shut: a pointed arch of scratched stone, two leaves of studded planks, portcullis teeth "
        "under the arch, a seam of yellow light between the leaves.")
    add("deco.kh_door", "Balcony Door", "Decoration", 2, 3, "Decoration", "deco_kh_door", door(2, 3, 139),
        "A small pointed-arch doorway standing open, yellow lamplight inside, one dark leaf edge-on.")
    add("deco.kh_battlement", "Battlements", "Decoration", 4, 1, "Decoration", "deco_kh_battlement", battlement(4, 141),
        "A row of crenellations for a wall top, a merlon with an arrow loop to each cell, scratched masonry. Tiles horizontally.")
    add("deco.kh_pennant", "Pennant", "Decoration", 1, 2, "Decoration", "deco_kh_pennant", pennant(143),
        "A red swallow-tailed pennant with a yellow roundel on a pole with a gilt finial.")
    add("deco.kh_fireflies", "Fireflies", "Decoration", 4, 3, "Decoration", "deco_kh_fireflies", fireflies(107),
        "A scatter of fireflies: yellow points, each with a small scratched cross of light.")
    add("deco.kh_moon", "Moon", "Decoration", 4, 4, "Decoration", "deco_kh_moon", moon(111),
        "A large yellow moon, its craters hatched, a halo of scratched rays.")
    add("deco.kh_castle_far", "Far Castle", "Decoration", 10, 8, "Decoration", "deco_kh_castle_far", castle_far(10, 8, 115),
        "The castle against the sky: a black cut-out of towers with its moon edge scratched in blue, yellow windows, "
        "red pennants; solid to the bottom.")
    add("deco.kh_forest_far", "Far Wood", "Decoration", 16, 6, "Decoration", "deco_kh_forest_far", forest_far(16, 6, 121),
        "Two ranks of black tree spires on the horizon, the nearer scratched along its crest. Tiles horizontally; solid below.")
    return out


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(17, 27, 61), sheet_cell=64)


if __name__ == "__main__":
    sheet = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
    export(sheet)

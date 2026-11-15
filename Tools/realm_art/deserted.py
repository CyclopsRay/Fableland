#!/usr/bin/env python3
"""Ghost Town tile set — The Deserted realm (TD).

Art language: relief print — linocut / wanted-poster woodblock. A black key block cut
with gouge marks, over two or three flat spot colours printed slightly out of register
on cream paper. There are no gradients and no outlines-with-fills: tone is hatching,
light is bare paper, and every edge wobbles like a hand-cut block. A frontier town
everyone walked out of. See `Docs/Art/RealmStyleBible.md` §Ghost Town.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/deserted.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "TheDeserted"
SET = "td"

PAPER = rgb("#F1E3C4")
KEY = rgb("#1F1A17")            # the black key block
RUST = rgb("#B5482E")
OCHRE = rgb("#D9A13B")
TEAL = rgb("#5E8C87")
SAGE = rgb("#7C8F58")
BONE = rgb("#EADFC8")

BRIEF = ("Linocut / woodblock relief-print game sprite on transparent ground: a black key block cut with "
         "visible gouge marks over two or three flat spot colours (rust red, mustard ochre, faded teal) "
         "printed slightly out of register on cream paper. No gradients; tone is hatching and bare paper; "
         "every edge wobbles like a hand-cut block, solids are speckled with ink skips. An abandoned "
         "frontier town. Orthographic side view, no cast shadow, no lettering.")


# ------------------------------------------------------------------ print kit


def flecks(h, w, seed, amount=0.07):
    """Ink skips: specks of bare paper inside a printed solid."""
    f = C.field(h, w, 1.9, seed, octaves=2)
    return C.smoothstep(1.0 - amount * 1.7, 1.0 - amount * 0.5, f)


def spot(img, mask, color, seed, off=(3, 2), salt=0.07, rough=1.5, wrap=False):
    """Print a flat spot-colour block, nudged out of register with the key block.
    `wrap` rolls the nudge around in x instead of leaving a bare strip — required for any
    sprite that tiles in a looping layer, or the strip shows as a seam every repeat."""
    m = C.rough(mask, rough, 9.0, seed)
    if off != (0, 0):
        m = np.roll(C.shift(m, 0, off[1]), off[0], axis=1) if wrap else C.shift(m, off[0], off[1])
    h, w = m.shape
    C.over(img, color, m * (1.0 - flecks(h, w, seed + 7, salt) * 0.92))
    return m


def key(img, mask, seed, cut=None, salt=0.05, rough=1.3):
    """Print the black key block; `cut` is whatever the gouge removed (bare paper)."""
    m = C.rough(mask, rough, 8.0, seed)
    h, w = m.shape
    a = m * (1.0 - flecks(h, w, seed + 11, salt) * 0.9)
    if cut is not None:
        a = a * (1.0 - np.clip(cut, 0.0, 1.0))
    C.over(img, KEY, a)
    return m


def gouges(h, w, angle, spacing, width, seed, wobble=2.5, gaps=0.30, x0=0.0, y0=0.0):
    """Parallel gouge cuts: swelling, wandering, broken lines at `angle` degrees."""
    xx, yy = C.grid(h, w, x0, y0)
    a = math.radians(angle)
    u = xx * math.cos(a) + yy * math.sin(a)
    v = -xx * math.sin(a) + yy * math.cos(a)
    v = v + (C.fbm(u / 70.0, v / 240.0, seed, octaves=2) - 0.5) * wobble * 2.0
    row = np.floor(v / spacing)
    d = np.abs(np.mod(v, spacing) - spacing / 2.0)
    run = C.vnoise(u / 34.0, row * 7.31, seed + 5)
    swell = width * (0.45 + 0.9 * run)
    return C.smoothstep(swell / 2.0 + 0.8, swell / 2.0 - 0.8, d) * C.smoothstep(gaps - 0.08, gaps + 0.08, run)


def rim(mask, thickness):
    """The inner band of a silhouette — what a key block leaves as an outline."""
    inner = C.smoothstep(0.80, 0.97, C.blur(mask, thickness))
    return np.clip(mask - inner, 0.0, 1.0)


def shade_side(mask, frac=0.38, right=True):
    """The shadow side of a form: its right-hand part, as a soft-edged region."""
    h, w = mask.shape
    xs = np.where(mask.max(axis=0) > 0.5)[0]
    if len(xs) == 0:
        return np.zeros_like(mask)
    x0, x1 = xs.min(), xs.max()
    xx, _ = C.grid(h, w)
    edge = x1 - (x1 - x0) * frac if right else x0 + (x1 - x0) * frac
    side = C.smoothstep(edge - 3.0, edge + 3.0, xx) if right else C.smoothstep(edge + 3.0, edge - 3.0, xx)
    return mask * side


def form(img, mask, color, seed, hatch_angle=62.0, hatch=7.0, outline=4.5, shadow=0.38):
    """The standard recipe for a solid object: spot colour, then a key block that keeps
    an outline and a hatched shadow side."""
    h, w = mask.shape
    spot(img, mask, color, seed)
    dark = np.maximum(rim(mask, outline), shade_side(mask, shadow))
    cut = gouges(h, w, hatch_angle, hatch, 2.6, seed + 3) * shade_side(mask, shadow) * (1.0 - rim(mask, outline))
    key(img, dark, seed + 1, cut=cut)


def solid(img, color=PAPER):
    """Slip opaque paper behind a finished print so it blocks what is behind it."""
    return C.back(img, color, threshold=0.12, grow=1.0)


# -------------------------------------------------------------------- ground


def mesa_atlas(variants=4):
    """`ground.td_mesa` — red desert rock. Rust spot colour ruled with black strata cuts,
    a mustard top with scrub ticks, a hatched shadow face, a chipped underside."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    two_pi = 2.0 * math.pi
    base = C.new(CELL, Wn)
    base[..., :3] = RUST
    base[..., 3] = 1.0
    skip = C.field(CELL, Wn, 1.9, 601, octaves=2, period_px=(Wn, CELL))
    C.tint_inside(base, PAPER, C.smoothstep(0.90, 0.97, skip) * 0.85)
    for k, (y0, amp, ph, wd) in enumerate([(20.0, 4.0, 0.4, 3.4), (47.0, 5.0, 2.2, 2.6), (72.0, 3.5, 4.1, 3.8), (101.0, 4.5, 1.3, 2.8)]):
        line_y = y0 + amp * np.sin(xx / Wn * two_pi * 2.0 + ph) + amp * 0.4 * np.sin(xx / Wn * two_pi * 6.0 + ph * 1.7)
        run = C.vnoise(xx / 32.0, yy * 0 + k * 3.7, 603, period=(Wn // 32, 0))
        cutw = wd * (0.5 + 0.9 * run)
        C.tint_inside(base, KEY, C.smoothstep(cutw / 2 + 0.8, cutw / 2 - 0.8, np.abs(yy - line_y)) * C.smoothstep(0.22, 0.34, run))
    # scattered shadow patches of short diagonal hatching
    patch = C.field(CELL, Wn, 46.0, 605, octaves=3, period_px=(Wn, CELL))
    d = np.abs(np.mod(xx + yy * 0.6, 9.0) - 4.5)
    C.tint_inside(base, KEY, C.smoothstep(2.0, 0.8, d) * C.smoothstep(0.60, 0.70, patch))

    def cap(cell, g):
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        lip = 26.0 + 3.0 * np.sin(gx / Wn * two_pi * 4.0 + 0.8)
        C.tint_inside(cell, OCHRE, C.smoothstep(lip + 1.0, lip - 1.0, gy))
        C.tint_inside(cell, KEY, C.smoothstep(7.5, 5.5, gy))                               # top outline
        C.tint_inside(cell, KEY, np.exp(-((gy - lip) / 2.4) ** 2))                          # under the lip
        fringe = C.smoothstep(2.2, 0.9, np.abs(np.mod(gx, 11.0) - 5.5)) * C.smoothstep(lip + 15.0, lip + 2.0, gy) * (gy > lip)
        C.tint_inside(cell, KEY, fringe * C.vnoise(gx / 22.0, gy * 0, 607, period=(Wn // 22 or 1, 0)))
        r = np.random.default_rng(700 + g)
        for _ in range(5):                                                                  # scrub ticks on top
            sx = r.uniform(8, CELL - 8)
            for j in (-1, 0, 1):
                m = C.line_mask(CELL, CELL, [(sx, 8.0), (sx + j * 5.0, r.uniform(-1.0, 2.0))], 2.2)
                C.over(cell, KEY, m)

    def side(cell, left):
        gx, gy = C.grid(CELL, CELL)
        dist = gx if left else CELL - gx
        C.tint_inside(cell, KEY, C.smoothstep(7.0, 5.0, dist))
        if not left:
            band = C.smoothstep(34.0, 30.0, dist)
            d2 = np.abs(np.mod(gx - gy * 0.7, 8.0) - 4.0)
            C.tint_inside(cell, KEY, band * (1.0 - C.smoothstep(2.4, 1.0, d2) * 0.0))
            C.tint_inside(cell, RUST, band * C.smoothstep(2.4, 1.0, d2) * (dist > 7.0))

    def underside(cell, g):
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        saw = np.abs(np.mod(gx, 46.0) - 23.0) * 1.1 + np.abs(np.mod(gx + 13.0, 19.0) - 9.5) * 0.9
        cutoff = CELL - 12.0 - saw
        keep = C.smoothstep(cutoff + 0.9, cutoff - 0.9, gy)
        C.tint_inside(cell, KEY, C.smoothstep(cutoff - 8.0, cutoff - 6.0, gy))
        cell[..., 3] *= keep

    def render(g, layer, open_left, open_right):
        cell = C.cut(base, g)
        if layer == 3:
            underside(cell, g)
        if open_left:
            side(cell, True)
        if open_right:
            side(cell, False)
        if layer == 0:
            cap(cell, g)
        return cell

    return C.build_hill_atlas(render, variants)


def timber_atlas(variants=4):
    """`ground.td_timber` — clapboard wall: the body of every building in town. Vertical
    boards with black gaps and a few grain cuts, a heavy beam on top, a corner post where
    the wall ends."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    board = 32.0
    bi = np.floor(xx / board).astype(np.int64)
    tone = C._hash01(np.mod(bi, variants * 4) + 3, bi * 0 + 5, 611)
    base = C.new(CELL, Wn)
    base[..., :3] = C.mix(rgb("#C99A52"), rgb("#DDB56C"), tone)
    base[..., 3] = 1.0
    skip = C.field(CELL, Wn, 1.9, 613, octaves=2, period_px=(Wn, CELL))
    C.tint_inside(base, PAPER, C.smoothstep(0.90, 0.97, skip) * 0.8)
    C.tint_inside(base, KEY, C.smoothstep(2.4, 1.2, np.minimum(np.mod(xx, board), board - np.mod(xx, board))))
    grain = C.field(CELL, Wn, 5.0, 615, octaves=2, period_px=(Wn, CELL), stretch=(1.0, 9.0))
    C.tint_inside(base, KEY, C.smoothstep(0.80, 0.86, grain) * 0.9)
    for ny in (10.0, CELL - 10.0):                                                          # nail heads
        d = np.hypot(np.mod(xx, board) - board / 2.0, yy - ny)
        C.tint_inside(base, KEY, C.smoothstep(3.2, 2.0, d))

    def cap(cell):
        gx, gy = C.grid(CELL, CELL)
        C.tint_inside(cell, KEY, C.smoothstep(19.0, 17.5, gy))
        C.tint_inside(cell, RUST, C.smoothstep(13.0, 11.5, gy) * C.smoothstep(4.0, 5.5, gy))
        C.tint_inside(cell, PAPER, C.smoothstep(1.6, 0.6, np.abs(gy - 8.5)) * C.smoothstep(0.35, 0.5, C.vnoise(gx / 20.0, gy * 0, 617)))

    def side(cell, left):
        gx, gy = C.grid(CELL, CELL)
        dist = gx if left else CELL - gx
        C.tint_inside(cell, KEY, C.smoothstep(13.0, 11.5, dist))
        C.tint_inside(cell, RUST, C.smoothstep(9.0, 7.5, dist) * C.smoothstep(3.0, 4.5, dist))

    def sill(cell):
        gx, gy = C.grid(CELL, CELL)
        C.tint_inside(cell, KEY, C.smoothstep(CELL - 11.0, CELL - 9.5, gy))

    def render(g, layer, open_left, open_right):
        cell = C.cut(base, g)
        if layer == 3:
            sill(cell)
        if open_left:
            side(cell, True)
        if open_right:
            side(cell, False)
        if layer == 0:
            cap(cell)
        return cell

    return C.build_hill_atlas(render, variants)


# ------------------------------------------------------------------ platforms


def balcony(seed):
    """Hotel balcony (3x1): a plank deck on turned brackets with a scalloped valance."""
    img = C.cells(3, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    deck = C.rect_mask(H, W, 0.0, 0.0, W, 22.0)
    spot(img, deck, OCHRE, seed, off=(2, 2))
    key(img, np.maximum(rim(deck, 3.5), C.rect_mask(H, W, 0.0, 16.0, W, 22.0)), seed + 1)
    for x in np.arange(32.0, W, 32.0):
        key(img, C.rect_mask(H, W, x - 1.5, 3.0, x + 1.5, 17.0), seed + int(x), rough=0.8)
    scallop = (yy > 22.0) & (np.hypot(np.mod(xx, 32.0) - 16.0, yy - 22.0) < 15.0)
    spot(img, scallop.astype(np.float32), RUST, seed + 2, off=(2, 2))
    key(img, rim(scallop.astype(np.float32), 3.0), seed + 3)
    for cx0 in (W * 0.14, W * 0.86):
        s = 1 if cx0 < W / 2 else -1
        brace = C.poly_mask(H, W, [(cx0 - 5.0, 22.0), (cx0 + 5.0, 22.0), (cx0 + 5.0, H - 4.0), (cx0 - 5.0, H - 4.0)])
        diag = C.line_mask(H, W, [(cx0, H - 10.0), (cx0 + s * 62.0, 26.0)], 8.0)
        m = np.maximum(brace, diag)
        spot(img, m, OCHRE, seed + 4, off=(2, 2))
        key(img, np.maximum(rim(m, 3.0), shade_side(brace, 0.5)), seed + 5)
    return solid(img)


def awning(seed):
    """Shop awning (3x1): rust-and-cream striped canvas, sagging on two poles."""
    img = C.cells(3, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    sag = 7.0 * np.sin(np.clip(xx / W, 0, 1) * math.pi)
    canvas = ((yy > sag * 0.25) & (yy < 40.0 + sag)).astype(np.float32)
    canvas = C.blur(canvas, 0.8)
    spot(img, canvas, BONE, seed, off=(0, 0), salt=0.03)
    stripes = (np.mod(xx, 48.0) < 24.0).astype(np.float32) * canvas
    spot(img, stripes, RUST, seed + 1, off=(2, 2))
    val = ((yy >= 40.0 + sag) & (np.hypot(np.mod(xx, 24.0) - 12.0, yy - (40.0 + sag)) < 12.0)).astype(np.float32)
    spot(img, val * (np.mod(xx, 48.0) >= 24.0), BONE, seed + 2, off=(0, 0), salt=0.03)
    spot(img, val * (np.mod(xx, 48.0) < 24.0), RUST, seed + 3, off=(2, 2))
    allm = np.maximum(canvas, val)
    cut = gouges(H, W, 90.0, 12.0, 2.0, seed + 4) * 0.0
    key(img, np.maximum(rim(allm, 3.2), C.smoothstep(1.8, 0.8, np.abs(yy - (40.0 + sag))) * canvas), seed + 5, cut=cut)
    key(img, C.rect_mask(H, W, 0.0, 0.0, W, 5.0) * canvas, seed + 6)
    for cx0 in (10.0, W - 10.0):
        pole = C.rect_mask(H, W, cx0 - 4.0, 30.0, cx0 + 4.0, H)
        spot(img, pole, OCHRE, seed + 7, off=(1, 1))
        key(img, np.maximum(rim(pole, 2.5), shade_side(pole, 0.5)), seed + 8)
    return solid(img)


def sign(seed):
    """Hanging shop sign (2x1): a board with a lone star, swinging from a beam on chains."""
    img = C.cells(2, 1)
    H, W = img.shape[:2]
    beam = C.rect_mask(H, W, 0.0, 0.0, W, 16.0)
    spot(img, beam, OCHRE, seed, off=(2, 2))
    key(img, np.maximum(rim(beam, 3.0), C.rect_mask(H, W, 0.0, 10.0, W, 16.0)), seed + 1)
    for cx0 in (W * 0.22, W * 0.78):
        for y in np.arange(18.0, 50.0, 9.0):
            key(img, C.aa(np.abs(C.sdf_circle(H, W, cx0, y + 4.0, 4.5)) - 1.6, 1.2), seed + int(y), rough=0.6)
    board = C.rect_mask(H, W, W * 0.08, 50.0, W * 0.92, H - 6.0, radius=4.0)
    spot(img, board, BONE, seed + 2, off=(0, 0), salt=0.04)
    key(img, rim(board, 5.0), seed + 3)
    star = C.poly_mask(H, W, [(W / 2.0 + math.cos(-math.pi / 2 + i * math.pi / 5) * (26.0 if i % 2 == 0 else 11.0),
                               (50.0 + H - 6.0) / 2.0 + math.sin(-math.pi / 2 + i * math.pi / 5) * (26.0 if i % 2 == 0 else 11.0))
                              for i in range(10)])
    spot(img, star, RUST, seed + 4, off=(2, 2))
    key(img, rim(star, 2.6), seed + 5)
    return solid(img)


# ---------------------------------------------------------------- soft volumes


def tumbleweed(seed):
    """Tumbleweed (2x2 soft volume): a ball of scribbled dry twigs you can blunder through."""
    img = C.cells(2, 2)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    cx0, cy0, r = W / 2.0, H / 2.0, W * 0.44
    for i in range(62):
        a0 = rng.uniform(0, 2 * math.pi)
        rr = r * rng.uniform(0.30, 1.0)
        span = rng.uniform(0.9, 2.6)
        ox, oy = rng.uniform(-r * 0.18, r * 0.18, 2)
        pts = [(cx0 + ox + math.cos(a0 + span * t) * rr, cy0 + oy + math.sin(a0 + span * t) * rr * rng.uniform(0.92, 1.0))
               for t in np.linspace(0.0, 1.0, 12)]
        m = C.line_mask(H, W, pts, rng.uniform(1.8, 3.6))
        C.over(img, OCHRE if i % 5 == 0 else KEY, m * (0.9 if i % 5 else 0.95))
    for _ in range(14):                                                    # stray twigs breaking the outline
        a0 = rng.uniform(0, 2 * math.pi)
        p0 = (cx0 + math.cos(a0) * r * 0.86, cy0 + math.sin(a0) * r * 0.86)
        p1 = (cx0 + math.cos(a0 + rng.uniform(-0.3, 0.3)) * r * 1.06, cy0 + math.sin(a0 + rng.uniform(-0.3, 0.3)) * r * 1.06)
        C.over(img, KEY, C.line_mask(H, W, [p0, p1], 2.2))
    return img


def dust(seed):
    """Dust devil (3x2 soft volume): a stippled ochre cloud with a few swirling cuts."""
    img = C.cells(3, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    env = np.exp(-(((xx - W / 2.0) / (W * 0.36)) ** 2 + ((yy - H * 0.52) / (H * 0.34)) ** 2) * 1.2)
    n = C.fbm(xx / 90.0, yy / 60.0, seed, octaves=4)
    dens = np.clip(env * 1.25 + (n - 0.5) * 0.7, 0.0, 1.0)
    # halftone: dots on a staggered grid whose size follows density
    s = 9.0
    row = np.floor(yy / s)
    dx = np.mod(xx + (row % 2) * s / 2.0, s) - s / 2.0
    dy = np.mod(yy, s) - s / 2.0
    dots = C.smoothstep(0.8, -0.8, np.hypot(dx, dy) - (dens - 0.18) * s * 0.78)
    C.over(img, OCHRE, dots * (dens > 0.2))
    rng = np.random.default_rng(seed)
    for i in range(5):
        cy0 = H * (0.30 + 0.11 * i)
        half = W * (0.16 + 0.05 * i) * (1.0 if i < 3 else 0.8)
        pts = [(W / 2.0 + math.cos(t) * half, cy0 + math.sin(t) * 11.0) for t in np.linspace(0.3, math.pi * 1.2, 16)]
        C.over(img, KEY, C.line_mask(H, W, pts, 2.6) * 0.92)
    return img


# --------------------------------------------------------------------- hazard


def cactus(seed):
    """Saguaro (1x2 hazard): sage green, a hatched shadow side, bristling with cut spines."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    cx0 = W / 2.0
    trunk = C.aa(C.sdf_segment(H, W, (cx0, H - 6.0), (cx0, 34.0), 19.0), 1.4)
    arm_l = np.maximum(C.aa(C.sdf_segment(H, W, (cx0 - 18.0, 150.0), (cx0 - 42.0, 150.0), 11.0), 1.4),
                       C.aa(C.sdf_segment(H, W, (cx0 - 42.0, 150.0), (cx0 - 42.0, 96.0), 11.0), 1.4))
    arm_r = np.maximum(C.aa(C.sdf_segment(H, W, (cx0 + 18.0, 118.0), (cx0 + 42.0, 118.0), 11.0), 1.4),
                       C.aa(C.sdf_segment(H, W, (cx0 + 42.0, 118.0), (cx0 + 42.0, 62.0), 11.0), 1.4))
    body = np.maximum(trunk, np.maximum(arm_l, arm_r))
    spot(img, body, SAGE, seed)
    ribs = gouges(H, W, 90.0, 9.0, 2.2, seed + 2, wobble=0.6, gaps=0.12)
    dark = np.maximum(rim(body, 4.2), np.maximum(shade_side(trunk, 0.42), np.maximum(shade_side(arm_l, 0.45), shade_side(arm_r, 0.45))))
    key(img, dark, seed + 1, cut=ribs * (1.0 - rim(body, 4.2)))
    rng = np.random.default_rng(seed)
    xx, yy = C.grid(H, W)
    edge = rim(body, 2.5)
    for _ in range(46):                                                    # spines: short ticks off the outline
        px, py = rng.uniform(6, W - 6), rng.uniform(26, H - 10)
        if edge[int(py), int(px)] < 0.4:
            continue
        ang = math.atan2(py - H * 0.5, px - cx0) + rng.uniform(-0.5, 0.5)
        C.over(img, KEY, C.line_mask(H, W, [(px, py), (px + math.cos(ang) * 8.0, py + math.sin(ang) * 8.0)], 1.7))
    for bx, by in ((cx0, 30.0), (cx0 - 42.0, 92.0), (cx0 + 42.0, 58.0)):   # a rust bloom on each tip
        C.over(img, RUST, C.aa(C.sdf_circle(H, W, bx, by, 6.0), 1.4))
        C.over(img, KEY, C.aa(np.abs(C.sdf_circle(H, W, bx, by, 6.0)) - 1.3, 1.2))
    return solid(img)


# ----------------------------------------------------------------- decoration


def saloon(seed):
    """Saloon false front (6x5): the town's one grand facade — stepped parapet, a star
    for a sign, batwing doors, a porch."""
    img = C.cells(6, 5)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    wall = C.poly_mask(H, W, [(26.0, H), (26.0, 150.0), (120.0, 150.0), (120.0, 70.0), (250.0, 70.0), (250.0, 14.0),
                              (W - 250.0, 14.0), (W - 250.0, 70.0), (W - 120.0, 70.0), (W - 120.0, 150.0),
                              (W - 26.0, 150.0), (W - 26.0, H)])
    spot(img, wall, OCHRE, seed)
    boards = C.smoothstep(2.6, 1.2, np.abs(np.mod(xx, 30.0) - 15.0)) * (yy > 150.0)
    lap = C.smoothstep(2.2, 1.0, np.abs(np.mod(yy, 22.0) - 11.0)) * (yy <= 150.0)
    key(img, np.maximum(rim(wall, 6.0), wall * np.maximum(boards, lap) * 0.95), seed + 1)
    panel = C.rect_mask(H, W, W * 0.30, 34.0, W * 0.70, 128.0, radius=5.0)
    spot(img, panel, BONE, seed + 2, off=(0, 0), salt=0.04)
    key(img, rim(panel, 5.0), seed + 3)
    star = C.poly_mask(H, W, [(W / 2.0 + math.cos(-math.pi / 2 + i * math.pi / 5) * (40.0 if i % 2 == 0 else 17.0),
                               81.0 + math.sin(-math.pi / 2 + i * math.pi / 5) * (40.0 if i % 2 == 0 else 17.0)) for i in range(10)])
    spot(img, star, RUST, seed + 4)
    key(img, rim(star, 3.2), seed + 5)
    porch = C.poly_mask(H, W, [(0.0, 360.0), (W, 360.0), (W - 18.0, 318.0), (18.0, 318.0)])
    spot(img, porch, RUST, seed + 6)
    key(img, np.maximum(rim(porch, 4.5), porch * gouges(H, W, 78.0, 13.0, 3.0, seed + 7, gaps=0.2)), seed + 8)
    for px in (40.0, W * 0.34, W * 0.66, W - 40.0):
        post = C.rect_mask(H, W, px - 7.0, 360.0, px + 7.0, H)
        spot(img, post, BONE, seed + 9, off=(0, 0), salt=0.03)
        key(img, np.maximum(rim(post, 3.0), shade_side(post, 0.5)), seed + 10)
    door = C.rect_mask(H, W, W / 2.0 - 52.0, 430.0, W / 2.0 + 52.0, H)
    key(img, door, seed + 11)
    for s in (-1, 1):                                                      # batwing doors
        wing = C.rect_mask(H, W, W / 2.0 + s * 4.0 if s > 0 else W / 2.0 - 48.0, 478.0,
                           W / 2.0 + 48.0 if s > 0 else W / 2.0 - 4.0, 566.0, radius=6.0)
        spot(img, wing, RUST, seed + 12, off=(0, 0))
        key(img, np.maximum(rim(wing, 3.0), wing * gouges(H, W, 90.0, 10.0, 2.2, seed + 13, wobble=0.4, gaps=0.1)), seed + 14)
    for wx in (W * 0.20, W * 0.80):
        for wy in (200.0, 440.0):
            win = C.rect_mask(H, W, wx - 38.0, wy, wx + 38.0, wy + 84.0)
            key(img, win, seed + 15)
            pane = C.rect_mask(H, W, wx - 30.0, wy + 8.0, wx + 30.0, wy + 76.0)
            spot(img, pane * (1.0 - C.smoothstep(3.0, 1.5, np.abs(xx - wx))) * (1.0 - C.smoothstep(3.0, 1.5, np.abs(yy - wy - 42.0))),
                 TEAL, seed + 16, off=(0, 0))
    return solid(img)


def water_tower(seed):
    """Water tower (3x6): a hooped tank under a tin cone, on a cross-braced trestle."""
    img = C.cells(3, 6)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx0 = W / 2.0
    legs = np.zeros((H, W), np.float32)
    for s in (-1, 1):
        legs = np.maximum(legs, C.line_mask(H, W, [(cx0 + s * 92.0, 300.0), (cx0 + s * 150.0, H)], 13.0))
    for y0, y1 in ((330.0, 470.0), (470.0, 610.0), (610.0, 750.0)):
        t0, t1 = (y0 - 300.0) / (H - 300.0), (y1 - 300.0) / (H - 300.0)
        xa0, xb0 = cx0 - 92.0 - 58.0 * t0, cx0 + 92.0 + 58.0 * t0
        xa1, xb1 = cx0 - 92.0 - 58.0 * t1, cx0 + 92.0 + 58.0 * t1
        legs = np.maximum(legs, C.line_mask(H, W, [(xa0, y0), (xb1, y1)], 7.0))
        legs = np.maximum(legs, C.line_mask(H, W, [(xb0, y0), (xa1, y1)], 7.0))
        legs = np.maximum(legs, C.line_mask(H, W, [(xa1, y1), (xb1, y1)], 8.0))
    spot(img, legs, OCHRE, seed, off=(2, 2))
    key(img, np.maximum(rim(legs, 3.0), shade_side(legs, 0.45)), seed + 1)
    tank = C.rect_mask(H, W, cx0 - 124.0, 110.0, cx0 + 124.0, 304.0, radius=6.0)
    spot(img, tank, RUST, seed + 2)
    staves = C.smoothstep(2.4, 1.0, np.abs(np.mod(xx - cx0, 26.0) - 13.0))
    hoops = np.maximum(C.smoothstep(5.0, 3.5, np.abs(yy - 150.0)), np.maximum(C.smoothstep(5.0, 3.5, np.abs(yy - 215.0)), C.smoothstep(5.0, 3.5, np.abs(yy - 276.0))))
    shadow = shade_side(tank, 0.40)
    cut = gouges(H, W, 90.0, 26.0, 3.0, seed + 3, wobble=0.5, gaps=0.05) * shadow
    key(img, np.maximum(np.maximum(rim(tank, 5.0), tank * np.maximum(staves * 0.9, hoops)), shadow), seed + 4, cut=cut * (1.0 - hoops))
    roof = C.poly_mask(H, W, [(cx0, 16.0), (cx0 + 146.0, 112.0), (cx0 - 146.0, 112.0)])
    spot(img, roof, BONE, seed + 5, off=(0, 0), salt=0.04)
    key(img, np.maximum(rim(roof, 4.5), shade_side(roof, 0.5) * (1.0 - gouges(H, W, 30.0, 11.0, 3.0, seed + 6) * 0.9)), seed + 7)
    return solid(img)


def windmill(seed):
    """Windpump (3x6): a lattice tower, a many-bladed wheel and a tail vane — the tallest
    and loneliest thing on any horizon."""
    img = C.cells(3, 6)
    H, W = img.shape[:2]
    cx0, hub = W / 2.0, 150.0
    tower = np.zeros((H, W), np.float32)
    for s in (-1, 1):
        tower = np.maximum(tower, C.line_mask(H, W, [(cx0 + s * 14.0, hub + 30.0), (cx0 + s * 120.0, H)], 9.0))
    for i in range(6):
        t0, t1 = i / 6.0, (i + 1) / 6.0
        y0, y1 = hub + 30.0 + (H - hub - 30.0) * t0, hub + 30.0 + (H - hub - 30.0) * t1
        w0, w1 = 14.0 + 106.0 * t0, 14.0 + 106.0 * t1
        tower = np.maximum(tower, C.line_mask(H, W, [(cx0 - w0, y0), (cx0 + w1, y1)], 5.0))
        tower = np.maximum(tower, C.line_mask(H, W, [(cx0 + w0, y0), (cx0 - w1, y1)], 5.0))
        tower = np.maximum(tower, C.line_mask(H, W, [(cx0 - w1, y1), (cx0 + w1, y1)], 6.0))
    spot(img, tower, OCHRE, seed, off=(2, 2))
    key(img, np.maximum(rim(tower, 2.6), shade_side(tower, 0.4)), seed + 1)
    vane = C.poly_mask(H, W, [(cx0 + 20.0, hub - 8.0), (cx0 + 150.0, hub - 46.0), (cx0 + 170.0, hub - 4.0), (cx0 + 150.0, hub + 38.0), (cx0 + 20.0, hub + 8.0)])
    spot(img, vane, RUST, seed + 2)
    key(img, np.maximum(rim(vane, 4.0), vane * gouges(H, W, 15.0, 12.0, 2.6, seed + 3)), seed + 4)
    blades = np.zeros((H, W), np.float32)
    for i in range(14):
        a0 = i / 14.0 * 2 * math.pi
        a1 = a0 + 0.30
        blades = np.maximum(blades, C.poly_mask(H, W, [(cx0 - 60.0 + math.cos(a0) * 26.0, hub + math.sin(a0) * 26.0),
                                                       (cx0 - 60.0 + math.cos(a0) * 118.0, hub + math.sin(a0) * 118.0),
                                                       (cx0 - 60.0 + math.cos(a1) * 118.0, hub + math.sin(a1) * 118.0),
                                                       (cx0 - 60.0 + math.cos(a1) * 26.0, hub + math.sin(a1) * 26.0)]))
    spot(img, blades, BONE, seed + 5, off=(0, 0), salt=0.04)
    key(img, rim(blades, 2.8), seed + 6)
    ring = C.aa(np.abs(C.sdf_circle(H, W, cx0 - 60.0, hub, 118.0)) - 2.6, 1.3)
    key(img, np.maximum(ring, C.aa(C.sdf_circle(H, W, cx0 - 60.0, hub, 16.0), 1.4)), seed + 7, rough=0.8)
    return solid(img)


def mesas_far(seed):
    """Far buttes (16x7): a pale teal back range behind rust table mountains with a
    hatched shadow face. Solid colour all the way down — a far range sinks on screen as the
    camera climbs, and must never show a lower edge — and it wraps in x."""
    img = C.cells(16, 7)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)

    def skyline(tables, base, s2):
        ridge = np.full((H, W), base, np.float32)
        for (cx, half, top, slope) in tables:
            d = np.abs(np.mod(xx / W - cx + 0.5, 1.0) - 0.5) * W
            ridge = np.minimum(ridge, np.maximum(top, top + (d - half) * slope))
        return ridge + (C.fbm(xx / 40.0, yy * 0 + s2, seed + s2, octaves=3, period=(max(1, W // 40), 0)) - 0.5) * 14.0

    back = skyline([(0.10, 150.0, 150.0, 1.5), (0.44, 210.0, 190.0, 1.2), (0.78, 120.0, 130.0, 1.8)], 330.0, 1)
    m_back = C.smoothstep(back - 1.0, back + 1.0, yy)
    spot(img, m_back, C.mix(TEAL, PAPER, 0.35), seed, off=(0, 0), salt=0.05, rough=1.0)
    front = skyline([(0.26, 170.0, 250.0, 2.6), (0.60, 110.0, 205.0, 3.2), (0.93, 210.0, 290.0, 2.2)], 430.0, 2)
    m_front = C.smoothstep(front - 1.0, front + 1.0, yy)
    spot(img, m_front, RUST, seed + 1, off=(3, 2), rough=1.0, wrap=True)
    gx = np.gradient(front, axis=1)
    face = m_front * C.smoothstep(0.6, 1.6, gx) * C.smoothstep(front + 150.0, front + 10.0, yy)
    strata = C.smoothstep(2.4, 1.0, np.abs(np.mod(yy - front, 26.0) - 13.0)) * m_front * C.smoothstep(front + 120.0, front + 20.0, yy)
    cut = gouges(H, W, 70.0, 10.0, 3.0, seed + 2)
    key(img, np.maximum(face * (1.0 - cut * 0.9), np.maximum(strata * 0.8, np.exp(-((yy - front - 2.5) / 2.6) ** 2) * m_front)), seed + 3, rough=0.9)
    return img


def sun(seed):
    """Woodcut sun (4x4): an ochre disc ringed with gouged rays."""
    img = C.cells(4, 4)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx0, cy0 = W / 2.0, H / 2.0
    r = np.hypot(xx - cx0, yy - cy0)
    ang = np.arctan2(yy - cy0, xx - cx0)
    rays = (np.mod(ang / (2 * math.pi) * 28.0, 1.0) < 0.5) & (r > W * 0.27) & (r < W * (0.40 + 0.06 * np.sin(ang * 7.0)))
    spot(img, rays.astype(np.float32), RUST, seed, off=(2, 2), rough=1.0)
    disc = C.aa(r - W * 0.235, 1.5)
    spot(img, disc, OCHRE, seed + 1, off=(0, 0), salt=0.05)
    rings = np.maximum(C.aa(np.abs(r - W * 0.235) - 2.6, 1.3), C.aa(np.abs(r - W * 0.16) - 1.8, 1.3) * C.smoothstep(0.3, 0.5, C.vnoise(ang * 6.0, r * 0, seed)))
    key(img, rings, seed + 2, rough=0.8)
    return img


def clouds(seed):
    """Printed clouds (6x2): flat-bottomed, outlined, their bellies ruled with cuts."""
    img = C.cells(6, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    rng = np.random.default_rng(seed)
    for (cx0, cy0, sc) in ((W * 0.20, H * 0.56, 1.0), (W * 0.55, H * 0.40, 1.25), (W * 0.84, H * 0.62, 0.85)):
        m = np.zeros((H, W), np.float32)
        for k in range(6):
            bx = cx0 + (k - 2.5) * 34.0 * sc
            br = (30.0 + 18.0 * math.sin(k * 1.3 + rng.uniform(0, 1))) * sc
            m = np.maximum(m, C.aa(C.sdf_circle(H, W, bx, cy0, br), 1.4))
        m = m * C.smoothstep(cy0 + 14.0 * sc, cy0 + 12.0 * sc, yy)
        spot(img, m, BONE, seed + int(cx0), off=(0, 0), salt=0.03)
        lines = C.smoothstep(2.0, 0.8, np.abs(np.mod(yy, 9.0) - 4.5)) * (yy > cy0 - 8.0 * sc) * C.smoothstep(0.3, 0.45, C.vnoise(xx / 30.0, np.floor(yy / 9.0), seed))
        key(img, np.maximum(rim(m, 3.2), m * lines * 0.9), seed + 1 + int(cx0), rough=0.9)
    return img


def sky_lines(seed):
    """Printed sky (16x6): broken horizontal teal rules, close together near the ground and
    sparse above — the way a relief print says "air". Wraps in x."""
    img = C.cells(16, 6)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    t = yy / H
    spacing = 30.0 - 16.0 * t
    acc = np.cumsum(1.0 / spacing, axis=0)
    d = np.abs(np.mod(acc, 1.0) - 0.5) * spacing
    row = np.floor(acc)
    run = C.vnoise(xx / 60.0, row * 5.13, seed, period=(max(1, W // 60), 0))
    line = C.smoothstep(2.4, 1.0, d) * C.smoothstep(0.62 - 0.38 * t, 0.70 - 0.38 * t, run)
    C.over(img, TEAL, line * (0.35 + 0.6 * t))
    return img


def small_prop(kind, seed):
    """One-cell street furniture: a barrel, a steer skull."""
    img = C.cells(1, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    if kind == "barrel":
        body = C.aa(C.sdf_box(H, W, W / 2.0, H - 50.0, 36.0, 46.0, radius=14.0), 1.4)
        spot(img, body, OCHRE, seed)
        staves = C.smoothstep(2.2, 1.0, np.abs(np.mod(xx - W / 2.0, 16.0) - 8.0))
        hoops = np.maximum(C.smoothstep(4.5, 3.0, np.abs(yy - (H - 78.0))), C.smoothstep(4.5, 3.0, np.abs(yy - (H - 24.0))))
        key(img, np.maximum(np.maximum(rim(body, 4.0), body * np.maximum(staves * 0.85, hoops)), shade_side(body, 0.36)), seed + 1,
            cut=gouges(H, W, 90.0, 16.0, 2.4, seed + 2, wobble=0.4, gaps=0.1) * shade_side(body, 0.36) * (1.0 - hoops))
    else:
        skull = np.maximum(C.aa(C.sdf_box(H, W, W / 2.0, H - 40.0, 20.0, 30.0, radius=14.0), 1.4),
                           C.aa(C.sdf_box(H, W, W / 2.0, H - 16.0, 11.0, 14.0, radius=8.0), 1.4))
        horns = np.zeros((H, W), np.float32)
        for s in (-1, 1):
            horns = np.maximum(horns, C.line_mask(H, W, C.bezier((W / 2.0 + s * 16.0, H - 58.0), (W / 2.0 + s * 46.0, H - 62.0), (W / 2.0 + s * 56.0, H - 96.0), n=10), 8.0))
        m = np.maximum(skull, horns)
        spot(img, m, BONE, seed, off=(0, 0), salt=0.04)
        eyes = np.maximum(C.aa(C.sdf_circle(H, W, W / 2.0 - 10.0, H - 46.0, 6.0), 1.3), C.aa(C.sdf_circle(H, W, W / 2.0 + 10.0, H - 46.0, 6.0), 1.3))
        key(img, np.maximum(np.maximum(rim(m, 3.2), eyes), shade_side(skull, 0.3) * 0.0), seed + 1)
        key(img, C.line_mask(H, W, [(W / 2.0, H - 30.0), (W / 2.0, H - 6.0)], 2.2), seed + 2, rough=0.6)
    return solid(img)


def signpost(seed):
    """Crossroads signpost (1x2): two boards pointing nowhere in particular."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    post = C.poly_mask(H, W, [(W / 2.0 - 7.0, H), (W / 2.0 - 4.0, 30.0), (W / 2.0 + 6.0, 28.0), (W / 2.0 + 8.0, H)])
    spot(img, post, OCHRE, seed, off=(2, 1))
    key(img, np.maximum(rim(post, 3.0), shade_side(post, 0.45)), seed + 1)
    for i, (y, s) in enumerate(((58.0, 1), (108.0, -1))):
        tip = W / 2.0 + s * 56.0
        board = C.poly_mask(H, W, [(W / 2.0 - s * 34.0, y - 17.0 + i * 4.0), (tip - s * 16.0, y - 19.0), (tip, y), (tip - s * 16.0, y + 19.0), (W / 2.0 - s * 34.0, y + 17.0 - i * 4.0)])
        spot(img, board, BONE if i == 0 else RUST, seed + 2 + i, off=(0, 0) if i == 0 else (2, 2))
        key(img, np.maximum(rim(board, 3.4), board * gouges(H, W, 4.0, 11.0, 2.0, seed + 5 + i, gaps=0.35) * 0.9), seed + 7 + i)
    return solid(img)


def fence(seed):
    """Ranch fence (3x1): two rails on leaning posts, one rail fallen."""
    img = C.cells(3, 1)
    H, W = img.shape[:2]
    m = np.zeros((H, W), np.float32)
    rng = np.random.default_rng(seed)
    for px in (26.0, W / 2.0, W - 26.0):
        lean = rng.uniform(-6.0, 6.0)
        m = np.maximum(m, C.poly_mask(H, W, [(px - 8.0, H), (px - 7.0 + lean, 22.0), (px + 7.0 + lean, 20.0), (px + 8.0, H)]))
    m = np.maximum(m, C.line_mask(H, W, [(6.0, 52.0), (W - 6.0, 48.0)], 11.0))
    m = np.maximum(m, C.line_mask(H, W, [(8.0, 88.0), (W * 0.52, 90.0)], 11.0))
    m = np.maximum(m, C.line_mask(H, W, [(W * 0.50, 92.0), (W - 30.0, H - 6.0)], 11.0))
    spot(img, m, OCHRE, seed, off=(2, 2))
    key(img, np.maximum(rim(m, 3.2), m * gouges(H, W, 3.0, 9.0, 2.0, seed + 2, gaps=0.45) * 0.85), seed + 1)
    return solid(img)


def window(seed):
    """Sash window (1x2) for a clapboard face: shutters folded back, teal panes."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    frame = C.rect_mask(H, W, 26.0, 34.0, W - 26.0, H - 40.0)
    key(img, frame, seed, rough=0.9)
    pane = C.rect_mask(H, W, 33.0, 41.0, W - 33.0, H - 47.0)
    bars = np.maximum(C.smoothstep(3.0, 1.6, np.abs(xx - W / 2.0)), C.smoothstep(3.0, 1.6, np.abs(yy - H / 2.0 + 3.0)))
    spot(img, pane * (1.0 - bars), TEAL, seed + 1, off=(0, 0), salt=0.06, rough=0.8)
    glint = gouges(H, W, 45.0, 13.0, 3.0, seed + 2, gaps=0.5) * pane * (1.0 - bars)
    C.over(img, PAPER, glint * 0.8)
    for s in (-1, 1):
        x0 = 6.0 if s < 0 else W - 24.0
        sh = C.rect_mask(H, W, x0, 34.0, x0 + 18.0, H - 40.0)
        spot(img, sh, RUST, seed + 3, off=(0, 0), rough=0.8)
        key(img, np.maximum(rim(sh, 2.6), sh * C.smoothstep(2.0, 0.8, np.abs(np.mod(yy, 14.0) - 7.0)) * 0.9), seed + 4, rough=0.8)
    sill = C.rect_mask(H, W, 18.0, H - 42.0, W - 18.0, H - 30.0)
    spot(img, sill, BONE, seed + 5, off=(0, 0), salt=0.03, rough=0.8)
    key(img, rim(sill, 2.6), seed + 6, rough=0.8)
    return solid(img)


def door(seed):
    """Batwing doors (2x2) in a dark doorway: the way into any building on the street."""
    img = C.cells(2, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    opening = C.rect_mask(H, W, 38.0, 22.0, W - 38.0, H)
    key(img, opening, seed, rough=0.9)
    casing = C.rect_mask(H, W, 26.0, 10.0, W - 26.0, H) * (1.0 - C.rect_mask(H, W, 38.0, 22.0, W - 38.0, H + 4.0))
    spot(img, casing, BONE, seed + 1, off=(0, 0), salt=0.03, rough=0.8)
    key(img, rim(casing, 2.8), seed + 2, rough=0.8)
    for s in (-1, 1):
        x0, x1 = (42.0, W / 2.0 - 3.0) if s < 0 else (W / 2.0 + 3.0, W - 42.0)
        wing = C.rect_mask(H, W, x0, 96.0, x1, 204.0, radius=9.0)
        spot(img, wing, RUST, seed + 3, off=(0, 0), rough=0.8)
        slats = C.smoothstep(2.2, 0.9, np.abs(np.mod(yy, 13.0) - 6.5)) * wing
        key(img, np.maximum(rim(wing, 3.0), slats * 0.9), seed + 4, rough=0.8)
    lamp = C.aa(C.sdf_circle(H, W, W / 2.0, 46.0, 9.0), 1.3)
    spot(img, lamp, OCHRE, seed + 5, off=(0, 0), rough=0.6)
    return solid(img)


def parapet(seed):
    """False-front parapet (4x2): the stepped board that makes a shack look like a
    business. Stands on a roof edge in the foreground — a roof fighter can drop behind it."""
    img = C.cells(4, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    board = C.poly_mask(H, W, [(6.0, H), (6.0, 150.0), (96.0, 150.0), (96.0, 86.0), (176.0, 86.0), (176.0, 20.0),
                               (W - 176.0, 20.0), (W - 176.0, 86.0), (W - 96.0, 86.0), (W - 96.0, 150.0), (W - 6.0, 150.0), (W - 6.0, H)])
    spot(img, board, OCHRE, seed)
    lap = C.smoothstep(2.2, 1.0, np.abs(np.mod(yy, 20.0) - 10.0))
    key(img, np.maximum(rim(board, 5.0), board * lap * 0.9), seed + 1)
    panel = C.rect_mask(H, W, W / 2.0 - 62.0, 40.0, W / 2.0 + 62.0, 130.0, radius=5.0)
    spot(img, panel, BONE, seed + 2, off=(0, 0), salt=0.04)
    key(img, rim(panel, 4.5), seed + 3)
    star = C.poly_mask(H, W, [(W / 2.0 + math.cos(-math.pi / 2 + i * math.pi / 5) * (34.0 if i % 2 == 0 else 14.0),
                               85.0 + math.sin(-math.pi / 2 + i * math.pi / 5) * (34.0 if i % 2 == 0 else 14.0)) for i in range(10)])
    spot(img, star, RUST, seed + 4)
    key(img, rim(star, 3.0), seed + 5)
    return solid(img)


# ---------------------------------------------------------------------- build


def build():
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.td_mesa", "Mesa Rock", "Ground", 1, 1, "Ground", "ground_td_mesa", mesa_atlas(4),
        "Red desert rock in cross-section: rust spot colour ruled with black strata cuts and hatched shadow patches, a "
        "mustard top with scrub ticks, a hatched shadow face, a chipped saw-tooth underside.", fill=True, group="terrain.td_mesa", variants=4)
    add("ground.td_timber", "Clapboard Wall", "Ground", 1, 1, "Ground", "ground_td_timber", timber_atlas(4),
        "Clapboard building wall: vertical ochre boards with black gaps, grain cuts and nail heads, a heavy rust-banded beam "
        "on top, a corner post where the wall ends.", fill=True, group="terrain.td_timber", variants=4)

    add("platform.td_balcony", "Hotel Balcony", "Platform", 3, 1, "Platform", "platform_td_balcony", balcony(621),
        "A plank balcony deck on braced brackets with a scalloped rust valance.", walk=4)
    add("platform.td_awning", "Shop Awning", "Platform", 3, 1, "Platform", "platform_td_awning", awning(625),
        "A sagging rust-and-cream striped canvas awning with a scalloped edge on two poles.", walk=6)
    add("platform.td_sign", "Hanging Sign", "Platform", 2, 1, "Platform", "platform_td_sign", sign(629),
        "A beam with a signboard hung on chains, bearing a single rust star.", walk=4)

    add("softvolume.td_tumbleweed", "Tumbleweed", "SoftVolume", 2, 2, "SoftVolume", "softvolume_td_tumbleweed", tumbleweed(633),
        "A tumbleweed: a ball of scribbled dry twigs. Enterable.")
    add("softvolume.td_dust", "Dust Devil", "SoftVolume", 3, 2, "SoftVolume", "softvolume_td_dust", dust(637),
        "A dust devil printed as an ochre halftone cloud with swirling black cuts. Enterable.")

    add("hazard.td_cactus", "Saguaro", "Hazard", 1, 2, "Hazard", "hazard_td_cactus", cactus(641),
        "A sage-green saguaro cactus with two arms, hatched shadow side, bristling spines and rust blooms.")

    add("deco.td_saloon", "Saloon Front", "Decoration", 6, 5, "Decoration", "deco_td_saloon", saloon(645),
        "A saloon false front: stepped parapet, a star for a sign, batwing doors, shuttered windows, a porch.")
    add("deco.td_water_tower", "Water Tower", "Decoration", 3, 6, "Decoration", "deco_td_water_tower", water_tower(649),
        "A hooped rust water tank under a tin cone on a cross-braced timber trestle.")
    add("deco.td_windmill", "Windpump", "Decoration", 3, 6, "Decoration", "deco_td_windmill", windmill(653),
        "A lattice windpump tower with a many-bladed wheel and a rust tail vane.")
    add("deco.td_mesas_far", "Far Buttes", "Decoration", 16, 7, "Decoration", "deco_td_mesas_far", mesas_far(657),
        "A pale teal back range behind rust table mountains with a hatched shadow face. Tiles horizontally.")
    add("deco.td_sun", "Woodcut Sun", "Decoration", 4, 4, "Decoration", "deco_td_sun", sun(661),
        "An ochre sun disc ringed with gouged rust rays.")
    add("deco.td_clouds", "Printed Clouds", "Decoration", 6, 2, "Decoration", "deco_td_clouds", clouds(665),
        "Three flat-bottomed printed clouds, outlined, their bellies ruled with cuts.")
    add("deco.td_sky", "Printed Sky", "Decoration", 16, 6, "Decoration", "deco_td_sky", sky_lines(669),
        "Broken horizontal teal rules, dense near the ground and sparse above. Tiles horizontally.")
    add("deco.td_barrel", "Barrel", "Decoration", 1, 1, "Decoration", "deco_td_barrel", small_prop("barrel", 673),
        "A hooped barrel.")
    add("deco.td_skull", "Steer Skull", "Decoration", 1, 1, "Decoration", "deco_td_skull", small_prop("skull", 677),
        "A bleached steer skull with long horns.")
    add("deco.td_signpost", "Signpost", "Decoration", 1, 2, "Decoration", "deco_td_signpost", signpost(681),
        "A crooked crossroads signpost with two blank arrow boards.")
    add("deco.td_fence", "Ranch Fence", "Decoration", 3, 1, "Decoration", "deco_td_fence", fence(685),
        "A ranch fence with leaning posts and one fallen rail.")
    add("deco.td_window", "Sash Window", "Decoration", 1, 2, "Decoration", "deco_td_window", window(689),
        "A sash window with teal panes and rust shutters folded back, to set on a clapboard face.")
    add("deco.td_door", "Batwing Doors", "Decoration", 2, 2, "Decoration", "deco_td_door", door(693),
        "Rust batwing doors in a dark cased doorway under a small lamp.")
    add("deco.td_parapet", "False-Front Parapet", "Decoration", 4, 2, "Decoration", "deco_td_parapet", parapet(697),
        "A stepped false-front parapet board bearing a rust star, to stand along a roof edge.")
    return out


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(241, 227, 196), sheet_cell=56)


if __name__ == "__main__":
    export(sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None)

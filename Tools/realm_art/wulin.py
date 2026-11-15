#!/usr/bin/env python3
"""Wulin (武林) tile set — the Banboo Maze realm (BM).

Art language: Chinese ink-wash (水墨). Every mark is a loaded-brush stroke with visible
bristle streaks and "flying white"; mass is diluted wash, never an outline-and-fill.
Monochrome ink on rice paper, with exactly two colours allowed: a grey-green in the
foreground bamboo and cinnabar red for lacquer and seals. See
`Docs/Art/RealmStyleBible.md` §Wulin.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/wulin.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "BambooMaze"
SET = "bm"

PAPER = rgb("#EFE7D4")
INK = rgb("#1B1917")       # 焦墨 — burnt, the darkest
INK2 = rgb("#35322E")      # 浓墨 — thick
GREY = rgb("#69655E")      # 重墨 — heavy wash
PALE = rgb("#9C978D")      # 淡墨 — light wash
MIST = rgb("#C6C0B2")      # 清墨 — clear, barely there
GREEN_D = rgb("#24402F")
GREEN = rgb("#3F634B")
GREEN_L = rgb("#86A088")
RED = rgb("#B23A2B")       # cinnabar
WOOD = rgb("#7A3A2B")      # faded red lacquer
WOOD_D = rgb("#40251F")
STONE = rgb("#8B877E")
SOLID = rgb("#CFC6B2")     # the opaque ground slipped behind solid things (see common.back)

BRIEF = ("Chinese ink-wash (shuimo) game sprite on transparent ground: loaded-brush strokes with "
         "visible bristle streaks and flying-white, diluted grey washes for mass, no outline-and-fill, "
         "no gradients from an airbrush. Monochrome ink; only grey-green bamboo and cinnabar red "
         "lacquer/seals carry colour. Orthographic side view, no cast shadow, no text unless specified.")


# ------------------------------------------------------------------- bamboo


def leaf(img, base, direction, length, width, color, opacity, seed, curve=0.0):
    """One bamboo leaf: pressed down near the base, flicked out to a point."""
    base = np.asarray(base, np.float32)
    d = np.asarray(direction, np.float32)
    d = d / (np.linalg.norm(d) + 1e-6)
    perp = np.array([-d[1], d[0]], np.float32)
    tip = base + d * length
    ctrl = base + d * length * 0.5 + perp * curve * length
    pts = C.bezier(base, ctrl, tip, n=10)
    t = np.linspace(0.0, 1.0, len(pts))
    prof = (t ** 0.55) * (1.0 - t) ** 1.25
    prof = prof / prof.max()
    C.ink_stroke(img, pts, np.maximum(prof * width, 0.35), color, opacity,
                 dry=0.16, edge=0.25, seed=seed, load=0.5, streak_freq=3.0, soft=1.0)


def leaf_cluster(img, at, side, rng, scale=1.0, ink=INK, pale=PALE, count=None, spread=(-25, 80)):
    """A fan of drooping leaves ("个"/"介" shapes): pale ones behind, dark ones in front."""
    n = count or int(rng.integers(3, 6))
    angs = np.sort(rng.uniform(spread[0], spread[1], n))
    order = list(range(n))
    rng.shuffle(order)
    for rank, k in enumerate(order):
        a = math.radians(angs[k])
        d = (side * math.cos(a), math.sin(a))
        ln = rng.uniform(0.42, 0.78) * CELL * scale
        wd = rng.uniform(5.0, 8.5) * scale
        behind = rank < n // 3
        col = pale if behind else ink
        op = 0.62 if behind else 0.93
        leaf(img, at, d, ln, wd, col, op, int(rng.integers(1 << 30)), curve=rng.uniform(-0.12, 0.12))


def bamboo(img, x, y_bottom, y_top, width, seed, lean=0.0, ink=GREEN_D, leaf_ink=INK, leaf_pale=PALE,
           opacity=0.95, leaves=True, leaf_from=0.28, leaf_scale=1.0, node_len=None, crown=True, leaf_density=0.85,
           uniform=False):
    """Paint one bamboo culm bottom-to-top: one flat-brush stroke per internode, a dark
    hooked accent under every joint, then twigs and leaf fans from the upper joints."""
    rng = np.random.default_rng(seed)
    height = float(y_bottom - y_top)
    node_len = node_len or CELL * 0.92
    phase = rng.uniform(0, 6.28)

    def cx(y):
        if uniform:             # a culm that must meet itself across a tile seam stays plumb
            return x
        t = (y_bottom - y) / height
        return x + lean * height * t ** 1.7 + math.sin(t * 3.0 + phase) * width * 0.22

    ys = [float(y_bottom)]
    if uniform:                 # evenly spaced joints, so a joint always lands on the seam
        n = max(1, int(round(height / node_len)))
        ys = [float(y_bottom) - height * i / n for i in range(n + 1)]
    else:
        seg = node_len * 0.55
        while ys[-1] - seg > y_top + width * 1.5:
            ys.append(ys[-1] - seg)
            seg = min(node_len, seg * 1.16) * rng.uniform(0.93, 1.07)
        ys.append(float(y_top))

    joints = []
    for i in range(len(ys) - 1):
        y0, y1 = ys[i], ys[i + 1]
        t_mid = (y_bottom - (y0 + y1) * 0.5) / height
        w = width if uniform else width * (1.0 - 0.42 * t_mid)
        gap = max(2.0, w * 0.22)
        a, b = y0 - gap, y1 + gap
        if a - b < 4:
            continue
        yy = np.linspace(a, b, 9)
        pts = np.stack([[cx(v) for v in yy], yy], axis=1)
        prof = np.array([1.12, 1.03, 0.98, 0.96, 0.95, 0.96, 0.98, 1.03, 1.12]) * w
        C.ink_stroke(img, pts, prof, ink, opacity, dry=0.24, edge=0.55, seed=seed * 31 + i,
                     grad=0.6, streak_freq=3.6, soft=1.1)
        joints.append((cx(y0), y0, w, t_mid))

    for (jx, jy, w, t) in joints[1:]:
        arc = C.bezier((jx - w * 1.22, jy - 1.5), (jx, jy + w * 0.34), (jx + w * 1.22, jy - 2.0), n=8)
        C.ink_stroke(img, arc, np.linspace(max(1.6, w * 0.20), max(0.9, w * 0.10), len(arc)),
                     INK if opacity > 0.8 else ink, min(1.0, opacity + 0.05), dry=0.08, edge=0.1,
                     seed=seed * 17 + int(jy), soft=0.9)

    if not leaves:
        return
    side = 1 if rng.random() < 0.5 else -1
    for (jx, jy, w, t) in (joints if uniform else joints[1:]):
        if (not uniform and t < leaf_from) or rng.random() > leaf_density:
            continue
        side = -side
        a = math.radians(rng.uniform(28, 62))
        ln = rng.uniform(0.45, 1.05) * CELL * leaf_scale * (1.0 - 0.25 * t)
        end = np.array([jx + side * math.sin(a) * ln, jy - math.cos(a) * ln], np.float32)
        ctrl = np.array([jx + side * math.sin(a) * ln * 0.35, jy - math.cos(a) * ln * 0.75], np.float32)
        tw = C.bezier((jx, jy), ctrl, end, n=8)
        C.ink_stroke(img, tw, np.linspace(max(1.5, w * 0.16), 0.8, len(tw)), ink, opacity * 0.92,
                     dry=0.18, seed=seed * 7 + int(jy), soft=0.9)
        leaf_cluster(img, end, side, rng, leaf_scale, leaf_ink, leaf_pale)
        if ln > CELL * 0.6:
            leaf_cluster(img, tw[4], side, rng, leaf_scale * 0.85, leaf_ink, leaf_pale, count=3)
    if crown:
        top = np.array([cx(y_top), y_top], np.float32)
        for s in (-1, 1):
            leaf_cluster(img, top, s, rng, leaf_scale, leaf_ink, leaf_pale, count=4, spread=(-55, 60))


def sprite_bamboo(wc, hc, seed, stalks, **kw):
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    for (fx, top_frac, width, lean, s) in stalks:
        bamboo(img, fx * W, H + 4, top_frac * H, width, seed + s, lean=lean, **kw)
    return img


# ----------------------------------------------------------------- mountains


def karst_ridge(w, peaks, seed, base=0.92, roughness=0.05):
    """Ridge height (0 = top of image, 1 = bottom) for Guilin-style limestone towers.

    Each peak is (centre, top, half_width[, skew, power]): steep-shouldered, lopsided
    bumps with a knuckle on one flank, never a smooth bell. The profile wraps, so the
    sprite tiles when a far layer loops.
    """
    x = (np.arange(w, dtype=np.float32) + 0.5) / w
    ridge = np.full(w, base, np.float32)
    rng = np.random.default_rng(seed)
    for pk in peaks:
        cx, top, half = pk[0], pk[1], pk[2]
        skew = pk[3] if len(pk) > 3 else rng.uniform(-0.35, 0.35)
        power = pk[4] if len(pk) > 4 else rng.uniform(2.2, 3.6)
        d = ((x - cx + 0.5) % 1.0) - 0.5
        half_side = np.where(d < 0, half * (1.0 + skew), half * (1.0 - skew))
        bump = np.exp(-(np.abs(d / half_side) ** power) * 1.5)
        ridge = np.minimum(ridge, base - (base - top) * bump)
        # a knuckle part-way down one flank breaks the bell silhouette
        kx = cx + rng.choice([-1.0, 1.0]) * half * rng.uniform(0.55, 0.95)
        kd = ((x - kx + 0.5) % 1.0) - 0.5
        ktop = top + (base - top) * rng.uniform(0.28, 0.50)
        kb = np.exp(-(np.abs(kd / (half * rng.uniform(0.28, 0.45))) ** 2.4) * 1.5)
        ridge = np.minimum(ridge, base - (base - ktop) * kb)
    zero = np.zeros_like(x)
    n = C.fbm(x * 14.0, zero + seed * 0.13, seed, octaves=3, period=(14, 0)) - 0.5
    fine = C.fbm(x * 70.0, zero + seed * 0.29, seed + 3, octaves=3, period=(70, 0)) - 0.5
    return ridge + n * roughness + fine * roughness * 0.35


def _range(img, ridge, seed, color, strength, fade, edge, trees=True):
    """Paint one mountain range as a graded wash: dense at the crown, lost in mist below."""
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    below = yy - ridge[None, :]
    inside = C.smoothstep(-1.5, 2.5, below)
    depth = np.clip(below / (H * fade), 0.0, 1.0)
    dens = (1.0 - depth) ** 1.6
    blot = C.fbm(xx / 60.0, yy / 90.0, seed + 4, octaves=4, period=(max(1, int(W / 60.0)), 0)) - 0.5
    # 披麻皴 — hemp-fibre texture: long near-vertical drags of drier ink down the faces.
    drag = C.fbm(xx / 6.0, yy / 130.0, seed + 9, octaves=3, period=(max(1, int(W / 6.0)), 0))
    drag = C.smoothstep(0.50, 0.78, drag) * (1.0 - depth) ** 0.7
    # one flank of every tower sits in shadow
    gx = np.gradient(ridge)
    lee = C.smoothstep(0.0, 1.4, gx)[None, :] * np.exp(-np.clip(below, 0, None) / (H * 0.16))
    crown = np.exp(-np.clip(below, 0, None) / 4.5) * edge
    a = inside * strength * np.clip(dens * (0.80 + 0.55 * blot) + crown + drag * 0.30 + lee * 0.30, 0.0, 1.0)
    a *= 0.90 + 0.18 * C.vnoise(xx / 2.2, yy / 2.2, seed + 77)
    a *= C.smoothstep(H - 2.0, H * 0.66, yy)      # lost in mist before the sprite's lower edge
    C.over(img, color, np.clip(a, 0.0, 1.0))
    if trees:       # 点苔 — clumps of dots along the skyline read as trees and give scale
        rng = np.random.default_rng(seed + 1)
        for _ in range(int(W / 9)):
            tx = rng.uniform(0, W)
            ty = float(ridge[int(tx) % W]) + rng.uniform(-3.0, 9.0)
            if ty > H * 0.62:
                continue
            for k in range(int(rng.integers(1, 4))):
                dot = C.aa(C.sdf_circle(H, W, tx + rng.uniform(-6, 6), ty + rng.uniform(-3, 3), rng.uniform(1.6, 3.6)), 1.6)
                C.over(img, color * 0.72, dot * strength * rng.uniform(0.55, 0.95))


def mountain(wc, hc, back, front, seed, color, strength, fade=0.42, edge=0.35):
    """Two overlapping ranges in one sprite — a paler far range behind a firmer near one."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    if back:
        _range(img, karst_ridge(W, back, seed + 50) * H, seed + 50, C.mix(color, PAPER, 0.35),
               strength * 0.62, fade * 1.1, edge * 0.7, trees=False)
        veil = C.cells(wc, hc)
        xx, yy = C.grid(H, W)
        C.over(veil, PAPER, C.smoothstep(H * 0.30, H * 0.95, yy) * 0.55)
        img[..., 3] *= 1.0 - veil[..., 3] * 0.6
    _range(img, karst_ridge(W, front, seed) * H, seed, color, strength, fade, edge)
    return img


# -------------------------------------------------------------------- ground


def rock_atlas(variants=4):
    """`ground.bm_rock` — an ink boulder mass.

    A dark, opaque wash body (far layers must never show through the floor) streaked with
    flying-white, long slanting 斧劈皴 axe-cut strokes, a paler top-lit band under the
    walkable lip, a bold dry contour where rock meets air, grass flicks and moss dots on
    top, and an underside that breaks up into ragged dry-brush lobes.
    """
    Wn = variants * CELL
    per = (Wn, CELL)
    blot = C.field(CELL, Wn, 52.0, 11, octaves=4, period_px=per)
    tooth = C.field(CELL, Wn, 2.4, 12, octaves=2, period_px=per)
    body = C.mix(rgb("#34322F"), rgb("#56534D"), np.clip(0.5 + (blot - 0.5) * 1.7, 0, 1))
    body = np.clip(body + (tooth[..., None] - 0.5) * 0.06, 0, 1)
    base = C.new(CELL, Wn)
    base[..., :3] = body
    base[..., 3] = 1.0
    # 飞白 — streaks of bare paper where a dry brush skipped, slanting down to the right.
    xx, yy = C.strip(variants)
    sk = 0.5                                     # horizontal shear per px of height
    u = xx - yy * sk
    fly = C.fbm(u / 5.0, yy / 64.0, 13, octaves=3, period=(max(1, int(round(Wn / 5.0))), 2))
    patch = C.field(CELL, Wn, 70.0, 14, octaves=3, period_px=per)
    C.tint_inside(base, rgb("#B4AFA3"), C.smoothstep(0.60, 0.86, fly) * C.smoothstep(0.42, 0.66, patch) * 0.55)
    # 斧劈皴 — a few long, deliberate axe-cut strokes (painted on a wrapped canvas).
    rng = np.random.default_rng(101)
    tex = C.new(CELL * 3, Wn * 3)
    for _ in range(9 * variants):
        x0 = rng.uniform(0, Wn) + Wn
        y0 = rng.uniform(0, CELL) + CELL
        ln = rng.uniform(46, 120)
        ang = math.radians(rng.uniform(56, 72))
        p0 = np.array([x0, y0])
        p1 = p0 + np.array([math.cos(ang) * ln, math.sin(ang) * ln])
        mid = (p0 + p1) / 2 + rng.uniform(-6, 6, 2)
        wd = rng.uniform(3.0, 7.0)
        for ox in (-Wn, 0, Wn):
            for oy in (-CELL, 0, CELL):
                o = np.array([ox, oy])
                C.ink_stroke(tex, [p0 + o, mid + o, p1 + o], [wd, wd * 0.8, 0.8], INK, rng.uniform(0.45, 0.8),
                             dry=0.62, edge=0.2, seed=int(rng.integers(1 << 30)), load=0.5, soft=1.0)
    folded = tex[CELL:2 * CELL, Wn:2 * Wn]
    C.over(base, folded[..., :3], folded[..., 3])

    def cap(cell_img, g):
        h = w = CELL
        gx, gy = C.grid(h, w, g * CELL, 0)
        lip = C.fbm(gx / 40.0, gy * 0 + 3.3, 21, octaves=3, period=(max(1, int(round(Wn / 40.0))), 0))
        top = 12.0 + (lip - 0.5) * 10.0
        # top-lit band: pale wash with a brushy, uneven lower edge, gone well before the seam
        ragged = C.fbm(gx / 13.0, gy / 44.0, 24, octaves=3, period=(max(1, int(round(Wn / 13.0))), 0))
        band = C.smoothstep(top + 74.0, top + 8.0, gy + (ragged - 0.5) * 46.0)
        band *= C.smoothstep(CELL - 6.0, CELL - 34.0, gy)
        C.tint_inside(cell_img, rgb("#A8A397"), band * 0.78)
        air = C.smoothstep(top - 1.2, top + 1.2, gy)
        cell_img[..., 3] *= air
        streak = C.vnoise(gx / 60.0, (gy - top) / 2.4, 23, period=(max(1, int(round(Wn / 60.0))), 0))
        line = np.exp(-((gy - top - 4.0) / 4.4) ** 2) * (0.62 + 0.55 * streak)
        C.over(cell_img, INK, np.clip(line, 0, 1) * air)
        r2 = np.random.default_rng(900 + g)
        xs_px = np.arange(w) + 0.5
        for _ in range(9):                       # grass flicks
            mx = r2.uniform(5, CELL - 5)
            my = float(np.interp(mx, xs_px, top[0])) + 3.0
            tip = (mx + r2.uniform(-7, 7), my - r2.uniform(7, 15))
            C.ink_stroke(cell_img, [(mx, my), ((mx + tip[0]) / 2 + r2.uniform(-2, 2), (my + tip[1]) / 2), tip],
                         [2.0, 1.3, 0.3], GREEN_D if r2.random() < 0.65 else INK, 0.92, dry=0.1,
                         seed=int(r2.integers(1 << 30)), soft=0.9)
        for _ in range(4):                       # moss dots
            mx = r2.uniform(5, CELL - 5)
            my = float(np.interp(mx, xs_px, top[0])) + r2.uniform(1.0, 7.0)
            dot = C.aa(C.sdf_circle(h, w, mx, my, r2.uniform(2.2, 4.4)), 1.6)
            C.over(cell_img, GREEN_D if r2.random() < 0.5 else INK, dot * r2.uniform(0.6, 0.95))

    def side(cell_img, g, left):
        h = w = CELL
        gx, gy = C.grid(h, w)
        wob = (C.fbm(gy / 36.0 + g * 1.7, gx * 0 + (5.1 if left else 9.4), 31, octaves=3, period=(0, 0)) - 0.5) * 10.0
        # the wobble must agree at the top and bottom of the cell so stacked sides line up
        k = np.clip(np.minimum(gy, h - gy) / 22.0, 0.0, 1.0)
        edge_x = 8.0 + wob * k
        dist = gx if left else (w - gx)
        air = C.smoothstep(edge_x - 1.2, edge_x + 1.2, dist)
        cell_img[..., 3] *= air
        streak = C.vnoise((dist - edge_x) / 2.4, gy / 50.0 + g, 33)
        line = np.exp(-((dist - edge_x - 4.0) / 4.2) ** 2) * (0.55 + 0.55 * streak)
        C.over(cell_img, INK, np.clip(line, 0, 1) * air)
        if left:
            C.tint_inside(cell_img, rgb("#A8A397"), C.smoothstep(edge_x + 26.0, edge_x + 6.0, dist) * 0.30)
        else:
            C.multiply_inside(cell_img, 1.0 - C.smoothstep(edge_x + 40.0, edge_x + 4.0, dist) * 0.40)

    def underside(cell_img, g):
        h = w = CELL
        gx, gy = C.grid(h, w, g * CELL, 0)
        lobes = C.fbm(gx / 58.0, gy * 0 + 1.0, 41, octaves=3, period=(max(1, int(round(Wn / 58.0))), 0))
        cutoff = h - 10.0 - lobes * 62.0
        drag = C.fbm(gx / 4.5, gy / 60.0, 43, octaves=2, period=(max(1, int(round(Wn / 4.5))), 0))
        keep = C.smoothstep(cutoff + 5.0, cutoff - 5.0, gy - (drag - 0.5) * 26.0)
        cell_img[..., 3] *= keep
        C.multiply_inside(cell_img, 1.0 - C.smoothstep(cutoff - 46.0, cutoff + 4.0, gy) * 0.38)

    def render(g, layer, open_left, open_right):
        cell_img = C.cut(base, g)
        if layer == 3:
            underside(cell_img, g)
        if open_left:
            side(cell_img, g, True)
        if open_right:
            side(cell_img, g, False)
        if layer == 0:
            cap(cell_img, g)
        return cell_img

    return C.build_hill_atlas(render, variants)


# --------------------------------------------------------------- architecture


def roof(wc, seed):
    """Sweeping tiled roof with upturned eaves. The ridge sits on the canvas top: that
    line is the one-way platform's standing surface."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xs = np.linspace(0.0, 1.0, 64)

    def top_y(u):       # ridge in the middle, a shallow dip, then flying eave tips
        d = np.abs(u - 0.5) * 2.0
        ridge = 5.0 + 3.0 * d
        tip = 5.0 + 34.0 * np.clip((0.98 - d) / 0.22, 0, 1) ** 1.6
        return np.where(d < 0.74, ridge, np.minimum(tip, 5.0 + 3.0 * 0.74 + (d - 0.74) * 150.0))

    def bot_y(u):
        d = np.abs(u - 0.5) * 2.0
        return H - 20.0 - 46.0 * np.clip((d - 0.52) / 0.48, 0, 1) ** 1.9

    poly = [(u * W, float(top_y(np.array(u)))) for u in xs] + \
           [(u * W, float(bot_y(np.array(u)))) for u in xs[::-1]]
    body = C.poly_mask(H, W, poly)
    xx, yy = C.grid(H, W)
    C.wash(img, body, rgb("#3A3F45"), opacity=0.96, seed=seed, blot=34.0, rim=0.3)
    # Tile courses: many thin dry strokes fanning from ridge to eave.
    rng = np.random.default_rng(seed)
    n = int(wc * 13)
    for i in range(n + 1):
        u = (i + rng.uniform(-0.15, 0.15)) / n
        if u < 0.02 or u > 0.98:
            continue
        x_top = W * (0.5 + (u - 0.5) * 0.93)
        x_bot = W * u
        yt = float(top_y(np.array((x_top / W)))) + 10.0
        yb = float(bot_y(np.array(u))) - 3.0
        if yb - yt < 10:
            continue
        pts = C.bezier((x_top, yt), ((x_top + x_bot) / 2, (yt + yb) / 2 + 5.0), (x_bot, yb), n=6)
        C.ink_stroke(img, pts, np.linspace(1.9, 1.1, len(pts)), INK, 0.72, dry=0.5,
                     seed=seed + i, soft=0.9)
    # Ridge beam: one heavy pressed stroke with owl-tail curls at both ends.
    ridge = [(W * 0.085, 15.0), (W * 0.13, 10.0), (W * 0.5, 8.5), (W * 0.87, 10.0), (W * 0.915, 15.0)]
    C.ink_stroke(img, ridge, [4.0, 7.5, 7.0, 7.5, 4.0], INK, 0.97, dry=0.14, edge=0.2, seed=seed + 500)
    for s in (-1, 1):
        cx0 = W * (0.5 + s * 0.415)
        curl = C.bezier((cx0, 14.0), (cx0 + s * 14.0, 12.0), (cx0 + s * 15.0, 0.5), n=8)
        C.ink_stroke(img, curl, np.linspace(5.5, 1.2, len(curl)), INK, 0.97, dry=0.1, seed=seed + 510 + s)
    # Eave edge: a firm underline and a row of round tile-ends.
    eave = [(u * W, float(bot_y(np.array(u))) - 1.0) for u in np.linspace(0.012, 0.988, 40)]
    C.ink_stroke(img, eave, 3.4, INK, 0.94, dry=0.2, seed=seed + 520)
    for i in range(n * 2 + 1):
        u = 0.02 + 0.96 * i / (n * 2)
        C.over(img, RED if i % 7 == 3 else INK2,
               C.aa(C.sdf_circle(H, W, u * W, float(bot_y(np.array(u))) + 4.5, 3.4), 1.4) * 0.9)
    # Timber fascia under the eave, in faded lacquer.
    fascia = C.poly_mask(H, W, [(W * 0.10, H - 20.0), (W * 0.90, H - 20.0), (W * 0.885, H - 2.0), (W * 0.115, H - 2.0)])
    C.wash(img, fascia, WOOD, opacity=0.92, seed=seed + 3, blot=20.0, rim=0.5)
    C.ink_stroke(img, [(W * 0.11, H - 2.5), (W * 0.89, H - 2.5)], 2.0, INK, 0.9, dry=0.3, seed=seed + 530)
    return C.back(img, SOLID)


def beam(wc, seed):
    """A lacquered timber tie-beam on bracket arms (斗拱): a narrow one-way perch."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    top, bot = 3.0, 46.0
    body = C.rect_mask(H, W, 4.0, top, W - 4.0, bot, radius=5.0)
    C.wash(img, body, WOOD, opacity=0.97, seed=seed, blot=26.0, rim=0.55)
    xx, yy = C.grid(H, W)
    grainf = C.vnoise(xx / 90.0, yy / 3.0, seed + 2)
    C.tint_inside(img, WOOD_D, body * C.smoothstep(0.55, 0.85, grainf) * 0.45)
    C.ink_stroke(img, [(5.0, top + 2.5), (W - 5.0, top + 2.5)], 3.0, INK, 0.95, dry=0.22, seed=seed + 1)
    C.ink_stroke(img, [(6.0, bot - 1.5), (W - 6.0, bot - 1.5)], 2.2, INK, 0.9, dry=0.35, seed=seed + 4)
    for ex in (6.0, W - 6.0):
        C.ink_stroke(img, [(ex, top + 2.0), (ex, bot - 1.0)], 2.4, INK, 0.92, dry=0.2, seed=seed + int(ex))
    for cx0 in (W * 0.16, W * 0.84):     # stepped bracket clusters
        for k, (hw, y0, y1) in enumerate([(30.0, bot - 2.0, bot + 20.0), (20.0, bot + 18.0, bot + 40.0),
                                          (11.0, bot + 38.0, bot + 62.0)]):
            m = C.rect_mask(H, W, cx0 - hw, y0, cx0 + hw, y1, radius=4.0)
            C.wash(img, m, WOOD if k % 2 == 0 else WOOD_D, opacity=0.96, seed=seed + 20 + k, blot=14.0, rim=0.6)
            C.ink_stroke(img, [(cx0 - hw, y1 - 1.0), (cx0 + hw, y1 - 1.0)], 1.7, INK, 0.9, dry=0.3,
                         seed=seed + 40 + k)
    for cx0 in (W * 0.16, W * 0.84):
        C.over(img, RED, C.aa(C.sdf_circle(H, W, cx0, top + 22.0, 6.0), 1.5) * 0.9)
    return C.back(img, SOLID)


def column(hc, seed):
    """Lacquered timber column on a carved stone plinth (deco / foreground occluder)."""
    img = C.cells(1, hc)
    H, W = img.shape[:2]
    cx0 = W / 2.0
    shaft = C.rect_mask(H, W, cx0 - 24.0, 30.0, cx0 + 24.0, H - 40.0, radius=3.0)
    C.wash(img, shaft, WOOD, opacity=0.97, seed=seed, blot=30.0, rim=0.4)
    xx, yy = C.grid(H, W)
    C.tint_inside(img, WOOD_D, shaft * C.smoothstep(cx0 - 4.0, cx0 + 24.0, xx) * 0.55)
    C.tint_inside(img, rgb("#B06A52"), shaft * np.exp(-((xx - (cx0 - 10.0)) / 5.0) ** 2) * 0.35)
    for s in (-1, 1):
        C.ink_stroke(img, [(cx0 + s * 24.0, 30.0), (cx0 + s * 24.5, H * 0.5), (cx0 + s * 24.0, H - 40.0)],
                     2.6, INK, 0.93, dry=0.3, seed=seed + 3 + s)
    cap = C.poly_mask(H, W, [(cx0 - 44.0, 4.0), (cx0 + 44.0, 4.0), (cx0 + 30.0, 32.0), (cx0 - 30.0, 32.0)])
    C.wash(img, cap, WOOD_D, opacity=0.97, seed=seed + 5, blot=16.0, rim=0.6)
    C.ink_stroke(img, [(cx0 - 44.0, 5.5), (cx0 + 44.0, 5.5)], 2.6, INK, 0.95, dry=0.2, seed=seed + 6)
    for ry in (52.0, H - 62.0):
        band = C.rect_mask(H, W, cx0 - 25.0, ry, cx0 + 25.0, ry + 9.0)
        C.over(img, rgb("#C9A04E"), band * 0.85)
        C.ink_stroke(img, [(cx0 - 25.0, ry + 9.5), (cx0 + 25.0, ry + 9.5)], 1.2, INK, 0.8, seed=seed + int(ry))
    plinth = C.poly_mask(H, W, [(cx0 - 30.0, H - 42.0), (cx0 + 30.0, H - 42.0), (cx0 + 42.0, H - 3.0), (cx0 - 42.0, H - 3.0)])
    C.wash(img, plinth, STONE, opacity=0.97, seed=seed + 8, blot=14.0, rim=0.6)
    C.ink_stroke(img, [(cx0 - 42.0, H - 3.5), (cx0 + 42.0, H - 3.5)], 2.4, INK, 0.92, dry=0.3, seed=seed + 9)
    C.ink_stroke(img, [(cx0 - 31.0, H - 41.0), (cx0 + 31.0, H - 41.0)], 1.8, INK, 0.85, dry=0.3, seed=seed + 10)
    return C.back(img, SOLID)


def lattice(wc, hc, seed):
    """Paper-and-lattice wall bay: the body of a hall, set behind the columns."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    frame = C.rect_mask(H, W, 3.0, 3.0, W - 3.0, H - 3.0)
    C.wash(img, frame, rgb("#D9CFB6"), opacity=0.96, seed=seed, blot=60.0, rim=0.2)
    xx, yy = C.grid(H, W)
    C.tint_inside(img, rgb("#B9AC8E"), frame * C.smoothstep(H * 0.2, H, yy) * 0.35)
    # dado panel
    dado = C.rect_mask(H, W, 3.0, H * 0.70, W - 3.0, H - 3.0)
    C.wash(img, dado, WOOD, opacity=0.9, seed=seed + 1, blot=30.0, rim=0.4)
    rng = np.random.default_rng(seed)
    step = 21.0
    for gx in np.arange(18.0, W - 12.0, step):
        C.ink_stroke(img, [(gx, 12.0), (gx + rng.uniform(-0.6, 0.6), H * 0.69)], 1.25, INK2, 0.78, dry=0.4,
                     seed=seed + int(gx))
    for gy in np.arange(18.0, H * 0.69, step):
        C.ink_stroke(img, [(10.0, gy), (W - 10.0, gy + rng.uniform(-0.6, 0.6))], 1.25, INK2, 0.78, dry=0.4,
                     seed=seed + 300 + int(gy))
    for (a, b) in [((3.0, 4.0), (W - 3.0, 4.0)), ((3.0, H - 4.0), (W - 3.0, H - 4.0)),
                   ((4.0, 3.0), (4.0, H - 3.0)), ((W - 4.0, 3.0), (W - 4.0, H - 3.0)),
                   ((3.0, H * 0.70), (W - 3.0, H * 0.70))]:
        C.ink_stroke(img, [a, b], 3.4, WOOD_D, 0.96, dry=0.25, seed=seed + int(a[0] + a[1] + b[0]))
    return C.back(img, SOLID)


def lantern_body(seed):
    """Stone pagoda lantern (石灯笼), 1x2, with a warm paper glow in its window."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    cx0 = W / 2.0

    def block(pts, col, s):
        m = C.poly_mask(H, W, pts)
        C.wash(img, m, col, opacity=0.97, seed=seed + s, blot=18.0, rim=0.6)
        loop = pts + [pts[0]]
        C.ink_stroke(img, loop, 1.9, INK, 0.9, dry=0.35, seed=seed + s + 50)

    block([(cx0 - 40.0, H - 4.0), (cx0 + 40.0, H - 4.0), (cx0 + 30.0, H - 26.0), (cx0 - 30.0, H - 26.0)], STONE, 1)
    block([(cx0 - 13.0, H - 26.0), (cx0 + 13.0, H - 26.0), (cx0 + 10.0, H - 104.0), (cx0 - 10.0, H - 104.0)], STONE, 2)
    block([(cx0 - 36.0, H - 104.0), (cx0 + 36.0, H - 104.0), (cx0 + 28.0, H - 122.0), (cx0 - 28.0, H - 122.0)], STONE, 3)
    block([(cx0 - 25.0, H - 122.0), (cx0 + 25.0, H - 122.0), (cx0 + 25.0, H - 170.0), (cx0 - 25.0, H - 170.0)], rgb("#7A766E"), 4)
    glow = C.rect_mask(H, W, cx0 - 13.0, H - 162.0, cx0 + 13.0, H - 132.0, radius=3.0)
    C.over(img, rgb("#F1C56A"), glow * 0.95)
    block([(cx0 - 52.0, H - 170.0), (cx0 + 52.0, H - 170.0), (cx0 + 14.0, H - 204.0), (cx0 - 14.0, H - 204.0)], rgb("#5B5852"), 5)
    cap = C.aa(C.sdf_circle(H, W, cx0, H - 212.0, 9.0), 1.5)
    C.wash(img, cap, STONE, opacity=0.97, seed=seed + 6, blot=8.0, rim=0.6)
    return img


def lantern_solid(seed):
    """The lantern with an opaque body and its glow laid back over the top."""
    body = lantern_body(seed)
    C.back(body, SOLID)
    H, W = body.shape[:2]
    C.radial_glow(body, W / 2.0, H - 147.0, 30.0, rgb("#F2B45A"), 0.34)
    return body


def stakes(seed):
    """Sharpened bamboo stakes (2x1 hazard): green culms cut on the bias, tips inked dark."""
    img = C.cells(2, 1)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    xs = np.linspace(20.0, W - 20.0, 7)
    for i, x in enumerate(xs):
        lean = rng.uniform(-0.22, 0.22)
        h = rng.uniform(0.62, 0.95) * H
        top = np.array([x + lean * h, H - h])
        base = np.array([x, H + 2.0])
        w = rng.uniform(8.0, 11.0)
        pts = np.linspace(base, top, 7)
        prof = np.array([1.05, 1.0, 1.0, 0.98, 0.9, 0.55, 0.12]) * w
        C.ink_stroke(img, pts, prof, GREEN_D, 0.95, dry=0.2, edge=0.5, grad=0.55, seed=seed + i, streak_freq=3.4)
        mid = base + (top - base) * 0.45
        C.ink_stroke(img, [mid + (-w * 1.15, 0), mid + (0, w * 0.3), mid + (w * 1.15, 0)], 1.8, INK, 0.95, seed=seed + 40 + i)
        tipc = base + (top - base) * 0.86
        C.ink_stroke(img, [tipc, top], [w * 0.5, 0.2], INK, 0.9, dry=0.1, seed=seed + 80 + i)
    C.ink_stroke(img, [(6.0, H - 5.0), (W * 0.5, H - 8.0), (W - 6.0, H - 5.0)], [3.0, 5.0, 3.0], INK2, 0.8, dry=0.5, seed=seed + 99)
    return img


# ------------------------------------------------------------------- scenery


def mist_bank(wc, hc, seed, strength=0.85, color=None):
    """A drift of unpainted paper: mist is the absence of ink, so this is paper-coloured."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    n = C.fbm(xx / 150.0, yy / 46.0, seed, octaves=4, period=(max(1, int(round(W / 150.0))), 0))
    env_y = np.exp(-(((yy - H * 0.52) / (H * 0.30)) ** 2))
    body = C.smoothstep(0.34, 0.70, n * 0.75 + env_y * 0.55) * env_y
    C.over(img, PAPER if color is None else color, np.clip(body * strength, 0.0, 1.0))
    return img


def mist_volume(wc, hc, seed):
    """Enterable mist (soft volume): the paper-white bank, given a grey underside and a few
    curled 云纹 cloud-scroll strokes so it reads as an object against a pale backdrop."""
    img = mist_bank(wc, hc, seed, strength=0.96)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    a = img[..., 3].copy()
    belly = C.smoothstep(H * 0.45, H * 0.85, yy) * a
    C.tint_inside(img, rgb("#A9A398"), belly * 0.55)
    rng = np.random.default_rng(seed)
    for i in range(int(wc * 1.5)):
        cx0 = W * (0.14 + 0.72 * (i + rng.uniform(0.1, 0.9)) / (wc * 1.5))
        cy0 = H * rng.uniform(0.40, 0.62)
        r = rng.uniform(15.0, 26.0)
        pts = []
        for k in range(15):                       # a scroll: an arc that winds in on itself
            ang = math.pi * (1.05 + 1.45 * k / 14.0)
            rad = r * (1.0 - 0.62 * k / 14.0)
            pts.append((cx0 + math.cos(ang) * rad, cy0 + math.sin(ang) * rad * 0.8))
        tail = [(pts[0][0] - r * 1.9, pts[0][1] + r * 0.18), (pts[0][0] - r * 0.9, pts[0][1] + r * 0.12)]
        C.ink_stroke(img, tail + pts, np.linspace(0.8, 2.3, len(pts) + 2), GREY, 0.75, dry=0.3, seed=seed + i, soft=0.9)
    img[..., 3] = np.maximum(img[..., 3], a)
    return img


def sun_disc(seed):
    """Cinnabar sun: one saturated disc, the classical accent against grey peaks."""
    img = C.cells(3, 3)
    H, W = img.shape[:2]
    disc = C.aa(C.sdf_circle(H, W, W / 2.0, H / 2.0, W * 0.34), 2.0)
    C.wash(img, disc, RED, opacity=0.92, seed=seed, blot=70.0, rim=0.25, grain_amt=0.16)
    return img


def cranes(seed):
    """A skein of cranes: each bird two flicked strokes."""
    img = C.cells(3, 2)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for i in range(6):
        cx0 = W * (0.14 + 0.14 * i) + rng.uniform(-8, 8)
        cy0 = H * (0.70 - 0.085 * i) + rng.uniform(-12, 12)
        span = rng.uniform(20.0, 30.0) * (1.0 - 0.06 * i)
        lift = rng.uniform(0.25, 0.6)
        for s in (-1, 1):
            wing = C.bezier((cx0, cy0), (cx0 + s * span * 0.5, cy0 - span * lift), (cx0 + s * span, cy0 - span * 0.1), n=7)
            C.ink_stroke(img, wing, np.linspace(2.6, 0.5, len(wing)), INK2, 0.85, dry=0.25, seed=seed + i * 3 + s)
        C.ink_stroke(img, [(cx0 - 2.0, cy0), (cx0 + span * 0.42, cy0 + 2.0)], [2.2, 0.5], INK, 0.9, seed=seed + 60 + i)
    return img


def seal(seed):
    """Calligraphy colophon: 竹海 brushed in ink above a cinnabar 武林 seal."""
    from PIL import Image, ImageDraw, ImageFont
    img = C.cells(1, 3)
    H, W = img.shape[:2]
    font_paths = ["/System/Library/Fonts/Supplemental/Kaiti.ttc", "/System/Library/Fonts/STHeiti Medium.ttc",
                  "/System/Library/Fonts/Supplemental/Songti.ttc", "/System/Library/Fonts/PingFang.ttc"]
    font_file = next((p for p in font_paths if __import__("os").path.exists(p)), None)

    def glyph_mask(text, size, box_w, box_h):
        if font_file is None:
            return None
        f = ImageFont.truetype(font_file, size * 3)
        im = Image.new("L", (box_w * 3, box_h * 3), 0)
        d = ImageDraw.Draw(im)
        bb = d.textbbox((0, 0), text, font=f)
        d.text(((box_w * 3 - (bb[2] - bb[0])) / 2 - bb[0], (box_h * 3 - (bb[3] - bb[1])) / 2 - bb[1]), text, font=f, fill=255)
        return np.asarray(im.resize((box_w, box_h), Image.LANCZOS), np.float32) / 255.0

    xx, yy = C.grid(H, W)
    y = 14
    for k, ch in enumerate("竹海"):
        m = glyph_mask(ch, 92, W, 110)
        if m is not None:
            m = C.rough(m, 1.2, 7.0, seed + k)
            full = np.zeros((H, W), np.float32)
            full[y:y + 110] = m
            streak = C.vnoise(xx / 3.0, yy / 40.0, seed + 20 + k)
            C.over(img, INK, full * (0.80 + 0.2 * streak))
        y += 112
    sz = 92
    sx0, sy0 = (W - sz) / 2.0, H - sz - 14.0
    plate = C.rough(C.rect_mask(H, W, sx0, sy0, sx0 + sz, sy0 + sz, radius=7.0), 2.2, 9.0, seed + 5)
    speck = C.field(H, W, 3.0, seed + 6, octaves=2)
    C.over(img, RED, plate * (0.80 + 0.2 * speck))
    for k, ch in enumerate("武林"):
        m = glyph_mask(ch, 40, sz, sz // 2)
        if m is None:
            continue
        full = np.zeros((H, W), np.float32)
        yy0 = int(sy0 + k * sz / 2)
        full[yy0:yy0 + sz // 2, int(sx0):int(sx0) + sz] = m
        C.over(img, PAPER, full * 0.95)
    return img


def leaf_fringe(wc, hc, seed):
    """Foreground: dark leaves hanging in from the top of the frame (closeview)."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for i in range(int(wc * 4)):
        x0 = rng.uniform(0, W)
        ln = rng.uniform(0.35, 0.95) * H
        end = np.array([x0 + rng.uniform(-50, 50), ln])
        tw = C.bezier((x0, -6.0), (x0 + rng.uniform(-30, 30), ln * 0.4), end, n=8)
        C.ink_stroke(img, tw, np.linspace(3.2, 0.9, len(tw)), INK, 0.95, dry=0.2, seed=seed + i)
        for s in (-1, 1):
            leaf_cluster(img, end, s, rng, 1.25, INK, INK2, count=3, spread=(10, 85))
        leaf_cluster(img, tw[4], 1 if rng.random() < 0.5 else -1, rng, 1.1, INK, INK2, count=3, spread=(10, 85))
    return img


# -------------------------------------------------------------------- build

def build():
    """Returns [(tile_id, display, role, w, h, category_dir, file_stem, image, brief, extra)]."""
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.bm_rock", "Ink Rock", "Ground", 1, 1, "Ground", "ground_bm_rock", rock_atlas(4),
        "Ink-wash boulder mass: opaque grey wash body, bold dry-brush contour where rock meets air, "
        "axe-cut texture strokes, moss dots on the walkable lip, underside dissolving into dry tatters.",
        fill=True, group="terrain.bm_rock", variants=4)

    add("softvolume.bm_bamboo_giant", "Bamboo (giant)", "SoftVolume", 2, 16, "SoftVolume", "softvolume_bm_bamboo_giant",
        sprite_bamboo(2, 16, 7, [(0.36, 0.025, 14.0, 0.016, 1), (0.66, 0.085, 11.5, -0.012, 2)], leaf_from=0.22),
        "Two giant bamboo culms sixteen cells tall — a whole lane of cover from the forest floor to the canopy. "
        "Same brush language as the tall bamboo.")
    add("softvolume.bm_bamboo_tall", "Bamboo (tall)", "SoftVolume", 2, 10, "SoftVolume", "softvolume_bm_bamboo_tall",
        sprite_bamboo(2, 10, 11, [(0.34, 0.035, 12.5, 0.030, 1), (0.66, 0.11, 10.5, -0.022, 2)]),
        "Two tall grey-green bamboo culms, one flat-brush stroke per internode with dark hooked joints, "
        "twigs and drooping black leaf fans in the upper two thirds. Enterable soft volume.")
    add("softvolume.bm_bamboo_mid", "Bamboo (mid)", "SoftVolume", 2, 7, "SoftVolume", "softvolume_bm_bamboo_mid",
        sprite_bamboo(2, 7, 23, [(0.40, 0.05, 11.5, -0.035, 1), (0.68, 0.17, 9.5, 0.030, 2)]),
        "Two mid-height bamboo culms with leaf fans; same brush language as the tall bamboo.")
    add("softvolume.bm_bamboo_young", "Bamboo (young)", "SoftVolume", 1, 4, "SoftVolume", "softvolume_bm_bamboo_young",
        sprite_bamboo(1, 4, 37, [(0.50, 0.06, 8.5, 0.02, 1)], leaf_scale=0.62, leaf_from=0.35),
        "A single young bamboo culm, slim, with small tight leaf fans.")
    add("softvolume.bm_bamboo_stalk", "Bamboo Stalk (stackable)", "SoftVolume", 1, 4, "SoftVolume", "softvolume_bm_bamboo_stalk",
        sprite_stalk_section(41),
        "Bare bamboo culm section that runs edge to edge: stack copies to raise any bamboo to any height.")
    add("softvolume.bm_mist", "Valley Mist", "SoftVolume", 4, 2, "SoftVolume", "softvolume_bm_mist",
        mist_volume(4, 2, 51),
        "A bank of valley mist: unpainted paper with a grey underside and curled cloud-scroll ink lines. Enterable soft volume.")

    add("platform.bm_roof_wide", "Temple Roof (wide)", "Platform", 6, 1, "Platform", "platform_bm_roof_wide", roof(6, 61),
        "Sweeping blue-grey tiled temple roof in side view, heavy inked ridge beam with owl-tail curls, "
        "flying upturned eaves, a row of round tile-ends, faded red lacquer fascia.", walk=16)
    add("platform.bm_roof_small", "Temple Roof (small)", "Platform", 4, 1, "Platform", "platform_bm_roof_small", roof(4, 67),
        "Smaller sweeping tiled roof, same ridge, eaves and lacquer fascia.", walk=16)
    add("platform.bm_beam", "Bracket Beam", "Platform", 3, 1, "Platform", "platform_bm_beam", beam(3, 71),
        "Red-lacquer timber tie-beam on stepped dougong bracket clusters, inked edges.", walk=6)

    add("hazard.bm_stakes", "Bamboo Stakes", "Hazard", 2, 1, "Hazard", "hazard_bm_stakes", stakes(81),
        "A row of sharpened green bamboo stakes cut on the bias, tips inked black.")

    add("deco.bm_column", "Lacquer Column", "Decoration", 1, 3, "Decoration", "deco_bm_column", column(3, 91),
        "Red-lacquer timber column with gilt bands, bracket capital and carved stone plinth.")
    add("deco.bm_lattice", "Lattice Wall", "Decoration", 2, 3, "Decoration", "deco_bm_lattice", lattice(2, 3, 95),
        "Paper lattice window bay over a lacquered dado panel, dark timber frame.")
    add("deco.bm_lantern", "Stone Lantern", "Decoration", 1, 2, "Decoration", "deco_bm_lantern", lantern_solid(101),
        "Stone pagoda lantern with a warm glowing paper window.")
    add("deco.bm_seal", "Colophon & Seal", "Decoration", 1, 3, "Decoration", "deco_bm_seal", seal(105),
        "Vertical brush calligraphy 竹海 above a square cinnabar seal reading 武林.")
    add("deco.bm_sun", "Cinnabar Sun", "Decoration", 3, 3, "Decoration", "deco_bm_sun", sun_disc(111),
        "A single flat cinnabar-red sun disc with watercolour drying rim.")
    add("deco.bm_cranes", "Cranes", "Decoration", 3, 2, "Decoration", "deco_bm_cranes", cranes(115),
        "A rising skein of six cranes, each two flicked ink strokes.")
    add("deco.bm_peaks_far", "Karst Peaks (far)", "Decoration", 16, 6, "Decoration", "deco_bm_peaks_far",
        mountain(16, 6, [(0.06, 0.40, 0.040), (0.24, 0.30, 0.046), (0.43, 0.46, 0.050), (0.62, 0.34, 0.040), (0.83, 0.44, 0.048)],
                 [(0.13, 0.18, 0.036), (0.32, 0.36, 0.030), (0.52, 0.06, 0.032), (0.70, 0.28, 0.038), (0.91, 0.14, 0.034)],
                 121, PALE, 0.66, fade=0.52, edge=0.30),
        "Far Guilin limestone towers as one pale graded wash, dense at the crown and dissolving into mist. Tiles horizontally.")
    add("deco.bm_peaks_mid", "Karst Peaks (near)", "Decoration", 16, 6, "Decoration", "deco_bm_peaks_mid",
        mountain(16, 6, [(0.16, 0.38, 0.044), (0.40, 0.46, 0.042), (0.66, 0.36, 0.048), (0.90, 0.48, 0.040)],
                 [(0.07, 0.30, 0.036), (0.27, 0.14, 0.034), (0.36, 0.46, 0.028), (0.55, 0.24, 0.038), (0.78, 0.10, 0.034), (0.86, 0.42, 0.028)],
                 127, GREY, 0.82, fade=0.48, edge=0.42),
        "Nearer limestone towers in heavier grey ink with hemp-fibre texture drags. Tiles horizontally.")
    add("deco.bm_mist_band", "Mist Band", "Decoration", 16, 2, "Decoration", "deco_bm_mist_band", mist_bank(16, 2, 131, strength=0.80),
        "A long drift of paper-white mist to layer between mountain ranges. Tiles horizontally.")
    add("deco.bm_bamboo_far", "Distant Bamboo", "Decoration", 3, 9, "Decoration", "deco_bm_bamboo_far",
        sprite_bamboo(3, 9, 141, [(0.22, 0.10, 8.0, 0.02, 1), (0.50, 0.03, 9.5, -0.015, 2), (0.78, 0.16, 7.5, 0.03, 3)],
                      ink=PALE, leaf_ink=GREY, leaf_pale=MIST, opacity=0.78, leaf_scale=0.9),
        "Three distant bamboo culms in pale grey ink for the background layers.")
    add("deco.bm_bamboo_curtain", "Bamboo Curtain", "Decoration", 4, 8, "Decoration", "deco_bm_bamboo_curtain",
        bamboo_curtain(4, 8, 161, [(0.16, 8.5, 1), (0.47, 10.0, 2), (0.80, 7.5, 3)],
                       ink=PALE, leaf_ink=GREY, leaf_pale=MIST, opacity=0.80, leaf_scale=0.85, leaf_density=0.55),
        "A stand of distant bamboo in pale grey ink that tiles both ways: the backdrop of a bamboo sea at any height.")
    add("deco.bm_bamboo_curtain_b", "Bamboo Curtain (alt)", "Decoration", 4, 8, "Decoration", "deco_bm_bamboo_curtain_b",
        bamboo_curtain(4, 8, 173, [(0.24, 9.5, 1), (0.58, 7.5, 2), (0.88, 9.0, 3)],
                       ink=PALE, leaf_ink=GREY, leaf_pale=MIST, opacity=0.80, leaf_scale=0.85, leaf_density=0.55),
        "A second stand of distant bamboo; alternate it with the first so a looping layer never mirrors itself.")
    add("deco.bm_leaf_fringe", "Leaf Fringe", "Decoration", 4, 2, "Decoration", "deco_bm_leaf_fringe", leaf_fringe(4, 2, 151),
        "Dark foreground bamboo leaves hanging in from the top of the frame.")
    return out


def bamboo_curtain(wc, hc, seed, stalks, **kw):
    """A stand of bamboo that tiles in BOTH directions: culms run plumb through the top and
    bottom edges with a joint on the seam, and every leaf that crosses an edge re-enters on
    the opposite one (the stand is painted nine times, once per neighbouring tile)."""
    H, W = hc * CELL, wc * CELL
    big = C.new(H * 3, W * 3)
    for oy in (0, 1, 2):
        for ox in (0, 1, 2):
            for (fx, width, s) in stalks:
                bamboo(big, ox * W + fx * W, (oy + 1) * H, oy * H, width, seed + s, uniform=True,
                       node_len=CELL, crown=False, **kw)
    return big[H:2 * H, W:2 * W].copy()


def sprite_stalk_section(seed):
    """1x4 culm that enters and leaves on the cell boundary so sections stack."""
    img = C.cells(1, 4)
    H, W = img.shape[:2]
    bamboo(img, W * 0.5, H + CELL * 0.5, -CELL * 0.5, 11.0, seed, leaves=False, node_len=CELL * 0.98, crown=False)
    return img


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(239, 231, 212), sheet_cell=64)


if __name__ == "__main__":
    sheet = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
    export(sheet)

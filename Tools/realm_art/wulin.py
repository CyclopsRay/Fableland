#!/usr/bin/env python3
"""Wulin (武林) tile set — the Banboo Maze realm (BM).

Art language: freehand ink painting — 写意 (xieyi), "writing the idea". The brush is a
calligrapher's: every mark is pressed, dragged and lifted, so a stroke swells and tapers,
splits into dry streaks (飞白) when it runs out of ink, and blooms when it lands wet. Mass is
splashed ink (泼墨) that bleeds into the paper; edges are not outlines. Above all, most of
the paper is left alone — 留白: mist, water and sky are the unpainted sheet.

Palette: black ink in five dilutions on cool rice paper, plus the two colours ink painters
have always allowed themselves — 花青 (indigo-cyan) for water and distance, 墨绿 (ink green)
for living things. Nothing warm. See `Docs/Art/RealmStyleBible.md` §Wulin.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/wulin.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np
from PIL import Image, ImageDraw

import common as C
from common import CELL, rgb

REALM = "BambooMaze"
SET = "bm"

PAPER = rgb("#E4E9E8")     # cool rice paper — the canvas colour of every Wulin map
WHITE = rgb("#F7F9F8")     # 留白 made visible: mist, falling water, the moon
INK = rgb("#14171A")       # 焦墨 — burnt, the darkest
INK2 = rgb("#2A2F33")      # 浓墨 — thick
GREY = rgb("#565E63")      # 重墨 — heavy wash
PALE = rgb("#8E979B")      # 淡墨 — light wash
MIST = rgb("#BEC6C8")      # 清墨 — clear, barely there
CYAN_D = rgb("#2F6B7C")    # 花青 — indigo-cyan: water, roof tiles, far peaks
CYAN = rgb("#5E9BA9")
CYAN_L = rgb("#A6CAD0")
GREEN_D = rgb("#1C352B")   # 墨绿 — ink green: bamboo, pine, moss
GREEN = rgb("#33574A")
GREEN_L = rgb("#6E8F80")
WOOD = rgb("#2E3F3A")      # weathered timber, in ink green
WOOD_D = rgb("#1B2623")
STONE = rgb("#8A9395")
SOLID = rgb("#C9D0CF")     # the opaque ground slipped behind solid things (see common.back)

BRIEF = ("Freehand Chinese ink painting (xieyi) game sprite on transparent ground: calligraphic "
         "brush strokes that swell and taper, dry flying-white streaks, splashed wet ink that bleeds "
         "at its edges, and large areas left as bare paper. Black ink in five dilutions; the only "
         "colours are indigo-cyan (water, distance) and ink green (plants). No warm colour, no "
         "outline-and-fill, no airbrush. Orthographic side view, no cast shadow.")


# ------------------------------------------------------------------- brushwork


def cstroke(img, pts, width, color, opacity=0.95, seed=0, head=0.85, tail=0.10, dry=0.30, **kw):
    """A calligrapher's stroke: the brush lands (head), settles to full width, then lifts
    to a point (tail). `width` is the half-width at full pressure."""
    pts = np.asarray(pts, np.float32)
    t = np.linspace(0.0, 1.0, len(pts))
    prof = np.where(t < 0.22, head + (1.0 - head) * C.smoothstep(0.0, 0.22, t),
                    1.0 - (1.0 - tail) * C.smoothstep(0.45, 1.0, t) ** 1.6)
    kw.setdefault("load", 0.45)
    kw.setdefault("edge", 0.3)
    return C.ink_stroke(img, pts, np.maximum(prof * width, 0.3), color, opacity, dry=dry, seed=seed, **kw)


def wet(img, mask, color, opacity=0.8, seed=0, bleed=9.0, bloom=0.35, rim=0.45, grain=0.12):
    """Ink laid down wet (泼墨 / 破墨): the shape bleeds into the paper with a feathered
    halo, pools unevenly, dries with a darker tide line and blooms where water ran back."""
    h, w = mask.shape
    xx, yy = C.grid(h, w)
    n = C.fbm(xx / (bleed * 3.2), yy / (bleed * 3.2), seed, octaves=4) - 0.5
    body = C.smoothstep(0.30, 0.62, C.blur(mask, bleed) + n * 0.42)
    halo = C.smoothstep(0.05, 0.55, C.blur(mask, bleed * 2.2) + n * 0.25) * 0.36
    dens = 0.62 + 0.38 * C.fbm(xx / 46.0, yy / 46.0, seed + 1, octaves=4)
    back = 1.0 - bloom * C.smoothstep(0.58, 0.80, C.fbm(xx / 30.0, yy / 30.0, seed + 2, octaves=3))
    tide = np.clip((body - C.blur(body, 3.0)) * 3.0, 0.0, 1.0)
    tooth = C.vnoise(xx / 2.2, yy / 2.2, seed + 3)
    a = np.maximum(body * dens * back, halo) * opacity
    a = np.clip(a + tide * rim * opacity, 0.0, 1.0) * (1.0 - grain + 2.0 * grain * tooth)
    C.over(img, color, np.clip(a, 0.0, 1.0))
    return img


def hairs(img, segments, width, color, opacity):
    """Many hair-fine strokes at once (pine needles, grass): one anti-aliased raster."""
    if not segments:
        return img
    H, W = img.shape[:2]
    k = 3
    im = Image.new("L", (W * k, H * k), 0)
    d = ImageDraw.Draw(im)
    for (x0, y0, x1, y1) in segments:
        d.line([(x0 * k, y0 * k), (x1 * k, y1 * k)], fill=255, width=max(1, int(round(width * k))))
    m = np.asarray(im.resize((W, H), Image.LANCZOS), np.float32) / 255.0
    C.over(img, color, np.clip(m, 0.0, 1.0) * opacity)
    return img


def moss(img, at, seed, n=5, r=(2.0, 4.6), spread=9.0, color=None, opacity=0.9):
    """点苔 — a cluster of pressed dots: the accent that makes a wash read as a rock face."""
    rng = np.random.default_rng(seed)
    H, W = img.shape[:2]
    for _ in range(n):
        cx = at[0] + rng.normal(0, spread)
        cy = at[1] + rng.normal(0, spread * 0.55)
        rr = rng.uniform(*r)
        dot = C.aa(C.sdf_circle(H, W, cx, cy, rr) + (C.vnoise(np.full((1, 1), cx / 3.0), np.full((1, 1), cy / 3.0), seed)[0, 0] - 0.5), 1.5)
        C.over(img, INK if color is None else color, dot * opacity * rng.uniform(0.7, 1.0))
    return img


# ------------------------------------------------------------------- bamboo


def leaf(img, base, direction, length, width, color, opacity, seed, curve=0.0, keep=None):
    """One bamboo leaf: pressed down near the base, flicked out to a point.

    `keep` = (x0, x1, y0) keeps the leaf on the sheet: one that would cross a side edge is
    folded back toward its culm (and shortened if there is no room either way), and none
    passes the top. A sprite's canvas is not a window — a leaf cut by its edge shows as a
    ruled line in the middle of the map."""
    base = np.asarray(base, np.float32)
    d = np.asarray(direction, np.float32)
    d = d / (np.linalg.norm(d) + 1e-6)
    if keep is not None:
        x0, x1, y0 = keep
        tip_x = base[0] + d[0] * length
        if tip_x < x0 or tip_x > x1:
            d = np.array([-d[0], d[1]], np.float32)
            curve = -curve
            tip_x = base[0] + d[0] * length
            if tip_x < x0 or tip_x > x1:
                room = (base[0] - x0) if d[0] < 0 else (x1 - base[0])
                length = max(4.0, min(length, room / max(abs(float(d[0])), 1e-3)))
        if d[1] < 0 and base[1] + d[1] * length < y0:
            length = max(4.0, min(length, (base[1] - y0) / max(-float(d[1]), 1e-3)))
    perp = np.array([-d[1], d[0]], np.float32)
    tip = base + d * length
    ctrl = base + d * length * 0.5 + perp * curve * length
    pts = C.bezier(base, ctrl, tip, n=10)
    t = np.linspace(0.0, 1.0, len(pts))
    prof = (t ** 0.55) * (1.0 - t) ** 1.25
    prof = prof / prof.max()
    C.ink_stroke(img, pts, np.maximum(prof * width, 0.35), color, opacity,
                 dry=0.16, edge=0.25, seed=seed, load=0.5, streak_freq=3.0, soft=1.0)


def leaf_cluster(img, at, side, rng, scale=1.0, ink=INK, pale=PALE, count=None, spread=(-25, 80), keep=None):
    """A fan of drooping leaves ("个"/"介" shapes): pale ones behind, dark ones in front."""
    n = count or int(rng.integers(3, 6))
    angs = np.sort(rng.uniform(spread[0], spread[1], n))
    order = list(range(n))
    rng.shuffle(order)
    for rank, k in enumerate(order):
        a = math.radians(angs[k])
        d = (side * math.cos(a), math.sin(a))
        ln = rng.uniform(0.50, 0.98) * CELL * scale
        wd = rng.uniform(6.5, 11.5) * scale
        behind = rank < n // 3
        col = pale if behind else ink
        op = 0.62 if behind else 0.93
        leaf(img, at, d, ln, wd, col, op, int(rng.integers(1 << 30)), curve=rng.uniform(-0.12, 0.12), keep=keep)


def bamboo(img, x, y_bottom, y_top, width, seed, lean=0.0, ink=GREEN_D, leaf_ink=INK, leaf_pale=PALE,
           opacity=0.95, leaves=True, leaf_from=0.28, leaf_scale=1.0, node_len=None, crown=True, leaf_density=0.85,
           uniform=False, keep_in=False):
    """Paint one bamboo culm bottom-to-top: one flat-brush stroke per internode, a dark
    hooked accent under every joint, then twigs and leaf fans from the upper joints.

    `keep_in` keeps every twig and leaf inside the canvas (see `leaf`); a stand that tiles
    (`bamboo_curtain`) leaves it off, because there a leaf crossing an edge re-enters."""
    rng = np.random.default_rng(seed)
    keep = (12.0, img.shape[1] - 12.0, 10.0) if keep_in else None
    height = float(y_bottom - y_top)
    node_len = node_len or CELL * 0.92
    phase = rng.uniform(0, 6.28)

    def cx(y):
        if uniform:             # a culm that must meet itself across a tile seam stays plumb
            return x
        t = (y_bottom - y) / height
        return x + lean * height * t ** 1.7 + math.sin(t * 2.2 + phase) * width * 0.55

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
        if keep is not None:            # a twig turns to the side that has room, and stops short of the edge
            lo, hi = keep[0] + 26.0, keep[1] - 26.0
            if not lo <= jx + side * math.sin(a) * ln <= hi:
                side = -side
            room = (hi - jx) if side > 0 else (jx - lo)
            ln = max(10.0, min(ln, room / max(math.sin(a), 1e-3)))
            ln = min(ln, max(10.0, (jy - keep[2] - 20.0) / max(math.cos(a), 1e-3)))
        end = np.array([jx + side * math.sin(a) * ln, jy - math.cos(a) * ln], np.float32)
        ctrl = np.array([jx + side * math.sin(a) * ln * 0.35, jy - math.cos(a) * ln * 0.75], np.float32)
        tw = C.bezier((jx, jy), ctrl, end, n=8)
        C.ink_stroke(img, tw, np.linspace(max(1.5, w * 0.16), 0.8, len(tw)), ink, opacity * 0.92,
                     dry=0.18, seed=seed * 7 + int(jy), soft=0.9)
        leaf_cluster(img, end, side, rng, leaf_scale, leaf_ink, leaf_pale, keep=keep)
        if ln > CELL * 0.6:
            leaf_cluster(img, tw[4], side, rng, leaf_scale * 0.85, leaf_ink, leaf_pale, count=3, keep=keep)
    if crown:
        top = np.array([cx(y_top), y_top], np.float32)
        for s in (-1, 1):
            leaf_cluster(img, top, s, rng, leaf_scale, leaf_ink, leaf_pale, count=4, spread=(-55, 60), keep=keep)


def sprite_bamboo(wc, hc, seed, stalks, foot_fade=0.0, **kw):
    """A stand of bamboo as one sprite. Every leaf stays on the sheet. `foot_fade` > 0 lets
    the culms rise out of mist over that fraction of the height — for a far clump that
    stands on nothing, whose cut-off feet would otherwise hang in the air."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    kw.setdefault("keep_in", True)
    order = sorted(range(len(stalks)), key=lambda i: stalks[i][2])       # thin (far) culms first
    for rank, i in enumerate(order):
        (fx, top_frac, width, lean, s) = stalks[i]
        near = rank == len(order) - 1
        args = dict(kw)
        if "ink" not in args:           # 近浓远淡: the near culm in dark ink green, the far one paler
            args["ink"] = C.mix(GREEN_D, INK, 0.35) if near else C.mix(GREEN, PALE, 0.35)
            args.setdefault("opacity", 0.96 if near else 0.80)
            if not near:
                args.setdefault("leaf_ink", GREY)
                args.setdefault("leaf_pale", MIST)
        bamboo(img, fx * W, H + 4, top_frac * H, width, seed + s, lean=lean, **args)
    if foot_fade > 0.0:
        _, yy = C.grid(H, W)
        img[..., 3] *= C.smoothstep(H * 0.995, H * (1.0 - foot_fade), yy)
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


def _range(img, ridge, seed, color, strength, fade, edge, trees=True, belts=1):
    """One mountain range in splashed ink: wet and dark where the brush landed (the crown),
    dragged dry down the faces (皴), cut by drifting belts of mist (留白) and gone before the
    sprite's lower edge. Every field wraps in x, so the sprite tiles when a far layer loops."""
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)

    def per(scale):
        return max(1, int(round(W / scale)))

    below = yy - ridge[None, :]
    inside = C.smoothstep(-1.5, 2.5, below)
    depth = np.clip(below / (H * fade), 0.0, 1.0)
    blot = C.fbm(xx / 64.0, yy / 80.0, seed + 4, octaves=4, period=(per(64.0), 0)) - 0.5
    dens = (1.0 - depth) ** 1.5 * (0.80 + 0.80 * blot)
    drag = C.fbm(xx / 4.0, yy / 150.0, seed + 9, octaves=3, period=(per(4.0), 0))
    drag = C.smoothstep(0.48, 0.74, drag) * (1.0 - depth) ** 0.6
    gx = np.gradient(ridge)
    lee = C.smoothstep(0.0, 1.2, gx)[None, :] * np.exp(-np.clip(below, 0, None) / (H * 0.20))
    crown = np.exp(-np.clip(below, 0, None) / 6.0) * edge
    a = inside * strength * np.clip(dens + crown + drag * 0.36 + lee * 0.42, 0.0, 1.0)
    for k in range(belts):
        wander = C.fbm(xx / 256.0, yy * 0 + k * 3.1, seed + 30 + k, octaves=3, period=(per(256.0), 0)) - 0.5
        by = H * (0.36 + 0.20 * k) + wander * H * 0.24
        band = np.exp(-(((yy - by) / (H * 0.055)) ** 2))
        gaps = C.fbm(xx / 128.0, yy / 40.0, seed + 40 + k, octaves=3, period=(per(128.0), 0))
        a *= 1.0 - band * 0.94 * C.smoothstep(0.26, 0.56, gaps + 0.16)
    a *= 0.90 + 0.18 * C.vnoise(xx / 2.0, yy / 2.0, seed + 77, period=(per(2.0), 0))
    a *= C.smoothstep(H - 2.0, H * 0.62, yy)
    C.over(img, color, np.clip(a, 0.0, 1.0))
    if trees:       # 点苔 along the skyline: reads as trees, and gives the range its scale
        rng = np.random.default_rng(seed + 1)
        for _ in range(int(W / 11)):
            tx = rng.uniform(4, W - 4)
            ty = float(ridge[int(tx) % W]) + rng.uniform(-3.0, 10.0)
            if ty > H * 0.60:
                continue
            for k in range(int(rng.integers(1, 4))):
                dot = C.aa(C.sdf_circle(H, W, tx + rng.uniform(-6, 6), ty + rng.uniform(-3, 3), rng.uniform(1.6, 3.8)), 1.6)
                C.over(img, INK2, dot * strength * rng.uniform(0.55, 0.95))


def mountain(wc, hc, back, front, seed, color, strength, fade=0.42, edge=0.35, far_tint=None):
    """Two overlapping ranges in one sprite — a paler, bluer far range behind a firmer near one."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    if back:
        far = C.mix(color, CYAN_L if far_tint is None else far_tint, 0.55)
        _range(img, karst_ridge(W, back, seed + 50) * H, seed + 50, far,
               strength * 0.60, fade * 1.1, edge * 0.6, trees=False)
        xx, yy = C.grid(H, W)
        img[..., 3] *= 1.0 - C.smoothstep(H * 0.30, H * 0.95, yy) * 0.45
    _range(img, karst_ridge(W, front, seed) * H, seed, color, strength, fade, edge)
    return img


def blob(img, cx, cy, rx, ry, color, opacity, seed, soft=0.5):
    """A small wet touch of ink, painted inside its own bounding box (cheap enough to use
    by the hundred)."""
    H, W = img.shape[:2]
    x0, x1 = int(max(0, cx - rx * 1.7 - 4)), int(min(W, cx + rx * 1.7 + 4))
    y0, y1 = int(max(0, cy - ry * 1.7 - 4)), int(min(H, cy + ry * 1.7 + 4))
    if x1 <= x0 or y1 <= y0:
        return img
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    d = np.sqrt(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2)
    n = C.fbm(xx / (rx * 0.5 + 6.0), yy / (ry * 0.5 + 6.0), seed, octaves=3) - 0.5
    a = C.smoothstep(1.0 + soft, 1.0 - soft, d + n * 0.7)
    a *= 0.70 + 0.50 * C.fbm(xx / 22.0, yy / 22.0, seed + 1, octaves=3)
    C.over(img[y0:y1, x0:x1], color, np.clip(a, 0.0, 1.0) * opacity)
    return img


def pine(img, base, height, seed, lean=0.0, ink=None, green=None, opacity=0.95, needles=None, keep=None):
    """松 — a pine in a handful of marks: a trunk that twists, pressed hard and dragged dry;
    angular branches; and flat pads of needles, each a fan of hair strokes over a touch of
    ink green. Pass a list as `needles` to collect the hair strokes and rasterise them once.

    `keep` = (x0, x1): every branch and pad is shortened to stay between those columns, so
    a pine near the edge of its sheet grows inward instead of being cut by it."""
    ink = INK if ink is None else ink
    green = GREEN_D if green is None else green
    rng = np.random.default_rng(seed)
    own = needles is None
    if own:
        needles = []
    bx, by = base
    top = (bx + lean * height, by - height)
    bend = (1.0 if rng.random() < 0.5 else -1.0) * rng.uniform(0.14, 0.26)      # an S: it twists as it climbs
    c1 = (bx + lean * height * 0.10 - bend * height, by - height * 0.36)
    c2 = (bx + lean * height * 0.80 + bend * height * 0.9, by - height * 0.70)
    trunk = C.bezier(base, c1, c2, top, n=20)
    w0 = max(2.2, height * 0.032)
    C.ink_stroke(img, trunk, np.linspace(w0, w0 * 0.35, len(trunk)), ink, opacity, dry=0.40, edge=0.5,
                 seed=seed, grad=0.2, streak_freq=3.2)
    for k in rng.choice(np.arange(3, len(trunk) - 4), size=3, replace=False):     # a few knots in the bark
        q = trunk[int(k)]
        blob(img, q[0] + rng.uniform(-0.3, 0.3) * w0, q[1], w0 * 0.55, w0 * 0.8, ink, 0.8 * opacity, seed + int(k))

    def pad(at, half, s):
        """A flat layer of needles: fans of hairs standing on a sagging line."""
        blob(img, at[0], at[1] - half * 0.12, half * 1.00, max(4.0, half * 0.26), green, 0.40 * opacity, s)
        blob(img, at[0] + rng.uniform(-0.3, 0.3) * half, at[1] - half * 0.05, half * 0.55, max(3.0, half * 0.15),
             ink, 0.30 * opacity, s + 1)
        fans = max(4, int(half / 4.2))
        for f in range(fans):                       # fans scattered through the pad, not strung on one line
            u = rng.uniform(-0.95, 0.95)
            cx = at[0] + u * half
            cy = at[1] + rng.uniform(-0.30, 0.06) * half * (1.0 - 0.5 * u * u) - (1.0 - u * u) * half * 0.10
            ln = rng.uniform(0.12, 0.19) * CELL * min(1.35, 0.60 + half / 60.0)
            for a in np.linspace(-172.0, -8.0, int(rng.integers(9, 14))):
                ang = math.radians(a + rng.uniform(-7.0, 7.0))
                r = ln * rng.uniform(0.72, 1.05)
                needles.append((cx, cy, cx + math.cos(ang) * r, cy + math.sin(ang) * r))

    def fit(x, side, want, reach):
        """`want`, shortened until x + side * want * reach (plus a needle's length) is on the sheet."""
        if keep is None:
            return want
        room = (x - keep[0]) if side < 0 else (keep[1] - x)
        return max(0.0, min(want, (room - CELL * 0.27) / reach))

    n_b = int(rng.integers(3, 6))
    for i in range(n_b):
        tt = 0.36 + 0.60 * (i + rng.uniform(0.0, 0.55)) / n_b
        q = trunk[min(len(trunk) - 1, int(tt * (len(trunk) - 1)))]
        side = 1 if (i % 2 == 0) else -1
        if lean and rng.random() < 0.3:
            side = 1 if lean > 0 else -1
        ln = height * rng.uniform(0.30, 0.52) * (1.18 - 0.55 * tt)
        ln = fit(q[0], side, ln, 1.72)              # the branch, then a pad 0.72 of it wide beyond its end
        if ln < CELL * 0.14:
            continue
        end = (q[0] + side * ln, q[1] - ln * rng.uniform(-0.06, 0.26))
        elbow = (q[0] + side * ln * 0.50, q[1] + ln * rng.uniform(0.03, 0.18))
        cstroke(img, C.bezier(q, elbow, end, n=8), max(1.6, w0 * 0.42), ink, opacity, seed=seed + 10 + i, dry=0.36, tail=0.25)
        pad(end, ln * 0.72, seed + 100 + i * 7)
    crown = min(fit(top[0], -1, height * 0.24, 1.0), fit(top[0], 1, height * 0.24, 1.0))
    if crown >= CELL * 0.10:
        pad((top[0], top[1] + height * 0.02), crown, seed + 300)
    if own:
        hairs(img, needles, 1.9, ink, 0.92 * opacity)
    return img


def hero_peak(wc, hc, seed, pines=6, lean=0.07, top=0.11):
    """The subject of the scroll: one towering karst peak. Black and wet at its crown, its
    faces dragged dry, two belts of mist drifting across it, its foot lost in white — and
    pines clinging to its shoulders. Most of the sheet is left unpainted.

    The outline is cut hard only where the rock is dark: ledges and a rim line on the upper
    third. Below that the edge is breathed out over a widening band, so the lower mountain
    is a wash dissolving into the paper, never a pale shape with a ruled contour. `top` is
    the share of the sheet left empty above the summit: room for the pine that crowns it."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    rng = np.random.default_rng(seed)
    y_top = H * top

    def at_height(f):                   # a height on the mountain: 0 = summit .. 1 = foot
        return y_top + f * (H - y_top)

    t = np.clip((yy - y_top) / (H - y_top), 0.0, 1.0)
    crisp = 1.0 - C.smoothstep(0.30, 0.62, t)                         # 1 = hard rock .. 0 = mist
    spine = W * (0.50 + lean * np.sin(t * 2.6 + 0.6))
    half = W * (0.055 + 0.29 * t ** 0.75)
    ledge = np.floor(t * 7.0 + (C.fbm(xx * 0 + 4.2, yy / 220.0, seed + 5, octaves=2) - 0.5) * 1.2) / 7.0
    step_l = (C.vnoise(xx * 0 + 0.7, ledge * 9.0, seed + 6) - 0.5) * W * 0.13 * (0.25 + t) * crisp
    step_r = (C.vnoise(xx * 0 + 5.3, ledge * 9.0, seed + 7) - 0.5) * W * 0.13 * (0.25 + t) * crisp
    wob_l = (C.fbm(yy / 46.0, xx * 0 + 1.7, seed + 1, octaves=4) - 0.5) * W * 0.07
    wob_r = (C.fbm(yy / 46.0, xx * 0 + 7.9, seed + 2, octaves=4) - 0.5) * W * 0.07
    left = spine - half * 0.92 + step_l + wob_l
    right = spine + half * 1.04 + step_r + wob_r
    soft = 2.0 + W * 0.055 * C.smoothstep(0.32, 0.80, t)
    inside = (C.smoothstep(-soft, soft, xx - left) * C.smoothstep(-soft, soft, right - xx)
              * C.smoothstep(y_top - H * 0.004, y_top + H * 0.008, yy))

    u = np.clip((xx - left) / np.maximum(right - left, 1.0), 0.0, 1.0)  # 0 = lit flank .. 1 = shadow flank
    blot = C.fbm(xx / 90.0, yy / 120.0, seed + 4, octaves=4) - 0.5
    dens = (1.0 - t) ** 1.2 * 0.92 + 0.60 * blot + 0.46 * C.smoothstep(0.42, 1.0, u) * (1.0 - t) ** 0.5
    drag = C.smoothstep(0.46, 0.72, C.fbm(xx / 4.5, yy / 180.0, seed + 9, octaves=3)) * (1.0 - t) ** 0.5
    drag *= C.smoothstep(0.34, 0.62, C.fbm(xx / 150.0, yy / 210.0, seed + 11, octaves=3))     # in patches, not a curtain
    rim = (np.exp(-((xx - left) / 5.0) ** 2) * 0.35 + np.exp(-((right - xx) / 6.0) ** 2) * 0.60) * (1.0 - t * 0.8) * crisp
    a = inside * np.clip(dens + drag * 0.42 + rim, 0.0, 1.0)
    lit = C.smoothstep(0.56, 0.80, C.fbm(xx / 74.0, yy / 24.0, seed + 12, octaves=3)) * (1.0 - u) ** 0.7
    a *= 1.0 - 0.78 * lit                                                 # ledge tops catch the light: left as paper
    a *= 1.0 - 0.38 * C.smoothstep(0.50, 1.0, t)
    for k, (centre, width) in enumerate([(0.30, 0.050), (0.62, 0.075)]):   # 留白: mist across the faces
        by = at_height(centre) + (C.fbm(xx / 200.0, yy * 0 + k * 2.3, seed + 30 + k, octaves=3) - 0.5) * H * 0.10
        band = np.exp(-(((yy - by) / (H * width)) ** 2))
        a *= 1.0 - band * 0.96 * C.smoothstep(0.20, 0.50, C.fbm(xx / 130.0, yy / 60.0, seed + 40 + k, octaves=3) + 0.22)
    foot = (C.fbm(xx / 120.0, yy / 80.0, seed + 60, octaves=3) - 0.5) * H * 0.10
    a *= C.smoothstep(H * 0.97, H * 0.66, yy + foot)
    a *= 0.90 + 0.18 * C.vnoise(xx / 2.1, yy / 2.1, seed + 77)
    C.over(img, C.mix(INK, CYAN_D, 0.10), np.clip(a, 0.0, 1.0))

    solid = a > 0.30
    for i in range(46):                 # 皴: long dry strokes down the faces, leaning off the spine
        y0 = at_height(rng.uniform(0.02, 0.58))
        x0 = rng.uniform(0, W)
        if not solid[int(y0), int(min(W - 1, x0))]:
            continue
        ln = rng.uniform(70.0, 230.0)
        away = (x0 - float(spine[int(y0), 0])) / (W * 0.5)
        p1 = (x0 + away * ln * 0.30 + rng.uniform(-14, 14), y0 + ln)
        mid = ((x0 + p1[0]) / 2 + rng.uniform(-10, 10), (y0 + p1[1]) / 2)
        C.ink_stroke(img, [(x0, y0), mid, p1], [rng.uniform(2.5, 6.5), rng.uniform(2.0, 5.0), 0.8], INK,
                     rng.uniform(0.35, 0.70), dry=0.66, edge=0.2, seed=seed + 500 + i, load=0.6)
    for i in range(9):                  # 破墨: touches of the blackest ink, dropped in wet
        row = int(at_height(rng.uniform(0.02, 0.28)))
        cx = float(spine[row, 0]) + rng.uniform(-0.5, 0.9) * float(half[row, 0])
        blob(img, cx, float(row), rng.uniform(30.0, 72.0), rng.uniform(40.0, 96.0), INK, 0.70, seed + 700 + i, soft=0.6)

    needles = []
    spots = []
    for i in range(pines):              # pines stand on the ledges, leaning out over the drop
        f = 0.0 if i == 0 else rng.uniform(0.07, 0.56)
        y = at_height(f)
        row = int(min(H - 1, y))
        side = 0 if i == 0 else (-1 if i % 2 else 1)
        if side == 0:
            at = (float(spine[row, 0]), y + 6.0)
        else:
            at = (float(left[row, 0]) + 8.0, y) if side < 0 else (float(right[row, 0]) - 8.0, y)
        if any(abs(at[1] - o[1]) < CELL * 0.75 and (side == o[2]) for o in spots):
            continue
        spots.append((at[0], at[1], side))
        ht = rng.uniform(0.85, 1.65) * CELL * (1.0 - 0.25 * f)
        tilt = side * rng.uniform(0.25, 0.60) + rng.uniform(-0.08, 0.08)
        # A pine reaches about (|tilt| + 1.15) x its height sideways, plus its needles, and
        # the crowning one stands its whole height above the summit: size each to the sheet.
        if side == 0:
            ht = min(ht, at[1] - 44.0)
        else:
            room = (at[0] - 12.0) if side < 0 else (W - 12.0 - at[0])
            ht = min(ht, (room - 36.0) / (abs(tilt) + 1.15))
        if ht < CELL * 0.45:
            continue
        pine(img, at, ht, seed + 900 + i, lean=tilt, needles=needles, keep=(12.0, W - 12.0))
    hairs(img, needles, 1.7, INK, 0.92)
    for i in range(26):                 # 点苔 along the edges of the hard rock
        row = int(at_height(rng.uniform(0.01, 0.50)))
        ex = float(left[row, 0]) if rng.random() < 0.5 else float(right[row, 0])
        moss(img, (ex + rng.uniform(-10, 10), float(row)), seed + 1200 + i, n=int(rng.integers(2, 6)), spread=7.0)
    return img


def cliff_pine(wc, hc, seed, lean=0.35):
    """A pine on a spur of rock: the tree you climb into (soft volume) or set in a far layer."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    base = (W * (0.34 if lean > 0 else 0.66), H - 10.0)
    blob(img, base[0], H - 6.0, W * 0.30, 22.0, INK2, 0.9, seed + 1, soft=0.35)     # the spur it grows from
    blob(img, base[0] - 8.0, H - 16.0, W * 0.16, 12.0, rgb("#C7CFCE"), 0.75, seed + 2, soft=0.4)
    pine(img, base, H * 0.86, seed, lean=lean, keep=(14.0, W - 14.0))
    moss(img, (base[0] + 20.0, H - 18.0), seed + 3, n=6, spread=14.0)
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
    body = C.mix(rgb("#262B2E"), rgb("#4E5558"), np.clip(0.5 + (blot - 0.5) * 1.7, 0, 1))
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
    C.tint_inside(base, rgb("#C2CACA"), C.smoothstep(0.56, 0.84, fly) * C.smoothstep(0.40, 0.64, patch) * 0.62)
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
        C.tint_inside(cell_img, rgb("#D3DAD9"), band * 0.86)
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
            C.tint_inside(cell_img, rgb("#C7CFCE"), C.smoothstep(edge_x + 26.0, edge_x + 6.0, dist) * 0.34)
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
    wet(img, body, rgb("#3F5258"), opacity=0.97, seed=seed, bleed=3.0, bloom=0.25, rim=0.35)
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
        C.over(img, CYAN if i % 7 == 3 else INK2,
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
        C.over(img, CYAN_L, C.aa(C.sdf_circle(H, W, cx0, top + 22.0, 5.0), 1.5) * 0.8)
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
    C.tint_inside(img, GREEN_L, shaft * np.exp(-((xx - (cx0 - 10.0)) / 5.0) ** 2) * 0.30)
    for s in (-1, 1):
        C.ink_stroke(img, [(cx0 + s * 24.0, 30.0), (cx0 + s * 24.5, H * 0.5), (cx0 + s * 24.0, H - 40.0)],
                     2.6, INK, 0.93, dry=0.3, seed=seed + 3 + s)
    cap = C.poly_mask(H, W, [(cx0 - 44.0, 4.0), (cx0 + 44.0, 4.0), (cx0 + 30.0, 32.0), (cx0 - 30.0, 32.0)])
    C.wash(img, cap, WOOD_D, opacity=0.97, seed=seed + 5, blot=16.0, rim=0.6)
    C.ink_stroke(img, [(cx0 - 44.0, 5.5), (cx0 + 44.0, 5.5)], 2.6, INK, 0.95, dry=0.2, seed=seed + 6)
    for ry in (52.0, H - 62.0):
        band = C.rect_mask(H, W, cx0 - 25.0, ry, cx0 + 25.0, ry + 9.0)
        C.over(img, CYAN_L, band * 0.75)
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
    C.wash(img, frame, rgb("#EDF0EF"), opacity=0.96, seed=seed, blot=60.0, rim=0.12)
    xx, yy = C.grid(H, W)
    C.tint_inside(img, rgb("#C4CDCB"), frame * C.smoothstep(H * 0.2, H, yy) * 0.30)
    # dado panel
    dado = C.rect_mask(H, W, 3.0, H * 0.70, W - 3.0, H - 3.0)
    wet(img, dado, GREEN_L, opacity=0.62, seed=seed + 1, bleed=4.0, bloom=0.3, rim=0.3)
    rng = np.random.default_rng(seed)
    step = 32.0
    for gx in np.arange(18.0, W - 12.0, step):
        C.ink_stroke(img, [(gx, 12.0), (gx + rng.uniform(-2.0, 2.0), H * 0.69)], 1.1, GREY, 0.50, dry=0.55,
                     seed=seed + int(gx))
    for gy in np.arange(18.0, H * 0.69, step):
        C.ink_stroke(img, [(10.0, gy), (W - 10.0, gy + rng.uniform(-2.0, 2.0))], 1.1, GREY, 0.50, dry=0.55,
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
    block([(cx0 - 25.0, H - 122.0), (cx0 + 25.0, H - 122.0), (cx0 + 25.0, H - 170.0), (cx0 - 25.0, H - 170.0)], rgb("#6E7779"), 4)
    glow = C.rect_mask(H, W, cx0 - 13.0, H - 162.0, cx0 + 13.0, H - 132.0, radius=3.0)
    C.over(img, rgb("#E4F3F4"), glow * 0.95)
    block([(cx0 - 52.0, H - 170.0), (cx0 + 52.0, H - 170.0), (cx0 + 14.0, H - 204.0), (cx0 - 14.0, H - 204.0)], rgb("#4B5356"), 5)
    cap = C.aa(C.sdf_circle(H, W, cx0, H - 212.0, 9.0), 1.5)
    C.wash(img, cap, STONE, opacity=0.97, seed=seed + 6, blot=8.0, rim=0.6)
    return img


def lantern_solid(seed):
    """The lantern with an opaque body and its glow laid back over the top."""
    body = lantern_body(seed)
    C.back(body, SOLID)
    H, W = body.shape[:2]
    C.radial_glow(body, W / 2.0, H - 147.0, 30.0, rgb("#CFE9EC"), 0.30)
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
    C.tint_inside(img, rgb("#9FAAAD"), belly * 0.50)
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
    img[..., 3] *= C.smoothstep(0.0, W * 0.16, xx) * C.smoothstep(W - 1.0, W * 0.84, xx)   # it thins out at both ends
    return img


def moon(seed):
    """烘云托月 — the moon is not painted: a breath of wash is laid around it and the disc is
    the paper that was left."""
    img = C.cells(3, 3)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    d = np.hypot(xx - W / 2.0, yy - H / 2.0)
    r = W * 0.24
    n = C.fbm(xx / 70.0, yy / 70.0, seed, octaves=4) - 0.5
    halo = C.smoothstep(r - 1.5, r + 2.5, d) * np.exp(-((d - r) / (W * 0.16)) ** 2) * (0.75 + 0.9 * n)
    halo *= C.smoothstep(W * 0.50, W * 0.36, d)                      # gone before the sprite's edge
    C.over(img, C.mix(PALE, CYAN_L, 0.45), np.clip(halo, 0.0, 1.0) * 0.62)
    C.over(img, WHITE, C.smoothstep(r + 1.0, r - 2.0, d) * 0.85)
    return img


def waterfall(wc, hc, seed):
    """飞瀑 — falling water is the paper itself: a ribbon left white between wet rock, with a
    few long dry strokes of indigo to say that it moves. Tiles vertically, so sections stack
    into a fall of any height. A soft volume that carries whoever is in it downward."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    py = max(1, int(round(H / 128.0)))
    wander = (C.fbm(xx * 0 + 3.1, yy / 128.0, seed, octaves=3, period=(0, py)) - 0.5) * 0.22
    d = np.abs(xx - W * 0.5) / (W * 0.5) + wander                    # 0 at the centre .. 1 at the edge
    core = C.smoothstep(0.88, 0.50, d)
    C.over(img, WHITE, core * 0.94)
    strands = C.fbm(xx / 7.0, yy / 256.0, seed + 1, octaves=3, period=(0, max(1, int(round(H / 256.0)))))
    C.over(img, CYAN, C.smoothstep(0.52, 0.80, strands) * core * (0.22 + 0.50 * np.clip(d, 0, 1)))
    fine = C.fbm(xx / 2.6, yy / 128.0, seed + 2, octaves=2, period=(0, py))
    C.over(img, CYAN_D, C.smoothstep(0.66, 0.90, fine) * core * 0.26 * (0.30 + np.clip(d, 0, 1)))
    wet_rock = C.smoothstep(0.58, 0.92, d) * C.smoothstep(1.06, 0.90, d)
    C.over(img, INK2, wet_rock * (0.55 + 0.45 * C.fbm(xx / 9.0, yy / 128.0, seed + 3, octaves=3, period=(0, py))) * 0.80)
    img[..., 3] *= C.smoothstep(0.0, W * 0.07, xx) * C.smoothstep(W - 1.0, W * 0.93, xx)   # wet rock breathes out; no ruled sides
    return img


def spray(wc, hc, seed):
    """The foot of a fall: white where it lands, a few flicked arcs of indigo, mist beyond."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    xx, yy = C.grid(H, W)
    n = C.fbm(xx / 60.0, yy / 34.0, seed, octaves=4)
    env = np.exp(-(((xx - W * 0.5) / (W * 0.34)) ** 2)) * C.smoothstep(H * 0.05, H * 0.55, yy) * C.smoothstep(H, H * 0.80, yy)
    C.over(img, WHITE, np.clip(C.smoothstep(0.30, 0.66, n * 0.7 + env * 0.6) * env * 1.25, 0.0, 1.0) * 0.95)
    for i in range(9):
        cx = W * 0.5 + rng.uniform(-0.20, 0.20) * W
        s = 1 if cx > W * 0.5 else -1
        r = rng.uniform(26.0, 70.0)
        arc = C.bezier((cx, H * 0.86), (cx + s * r * 0.5, H * 0.86 - r * 1.1), (cx + s * r, H * 0.86 - r * 0.2), n=8)
        cstroke(img, arc, rng.uniform(1.2, 2.4), CYAN if i % 3 else CYAN_D, 0.55, seed=seed + i, dry=0.4, tail=0.15)
    return img


def fall_far(wc, hc, seed):
    """A far waterfall: two wet black shoulders of rock and the white thread between them."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    wander = (C.fbm(xx * 0 + 2.0, yy / 150.0, seed, octaves=3) - 0.5) * W * 0.16
    cx = W * 0.5 + wander
    gap = W * (0.045 + 0.035 * yy / H)
    d = np.abs(xx - cx)
    rock = C.smoothstep(gap, gap + 5.0, d) * np.exp(-((d - gap) / (W * 0.20)) ** 2)
    rock *= 0.55 + 0.9 * (C.fbm(xx / 50.0, yy / 90.0, seed + 1, octaves=4) - 0.2)
    rock *= C.smoothstep(H * 0.02, H * 0.12, yy) * C.smoothstep(H * 0.96, H * 0.64, yy)
    drag = C.smoothstep(0.5, 0.75, C.fbm(xx / 4.0, yy / 160.0, seed + 2, octaves=3))
    C.over(img, C.mix(INK2, CYAN_D, 0.15), np.clip(rock * (0.85 + 0.4 * drag), 0.0, 1.0) * 0.88)
    thread = C.smoothstep(gap, gap * 0.4, d) * C.smoothstep(H * 0.03, H * 0.10, yy) * C.smoothstep(H * 0.97, H * 0.78, yy)
    C.over(img, WHITE, thread * 0.92)
    C.over(img, CYAN, thread * C.smoothstep(0.55, 0.85, C.fbm(xx / 3.0, yy / 120.0, seed + 3, octaves=2)) * 0.45)
    foot = np.exp(-(((xx - W * 0.5) / (W * 0.30)) ** 2)) * np.exp(-(((yy - H * 0.90) / (H * 0.07)) ** 2))
    C.over(img, WHITE, np.clip(foot * (0.6 + 0.8 * C.fbm(xx / 40.0, yy / 26.0, seed + 4, octaves=3)), 0, 1) * 0.9)
    return img


def bridge(wc, seed):
    """拱桥 — a stone bridge: one bold stroke for the deck (the line you stand on, along the
    canvas top), one for the arch, pale stone left almost as paper, moss at the springers."""
    img = C.cells(wc, 2)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    arch = ((xx - W * 0.5) / (W * 0.335)) ** 2 + ((yy - (H + 6.0)) / (H - 42.0)) ** 2
    body = C.smoothstep(2.5, 5.0, yy) * C.smoothstep(1.0 - 0.03, 1.0 + 0.03, arch)
    body *= C.smoothstep(2.0, 6.0, xx) * C.smoothstep(W - 2.0, W - 6.0, xx)
    stone = C.mix(rgb("#D5DCDB"), rgb("#9BA5A7"), np.clip(C.fbm(xx / 60.0, yy / 40.0, seed, octaves=4) * 1.3 - 0.2, 0, 1))
    C.over(img, stone, C.rough(body, 1.6, 9.0, seed + 1) * 0.97)
    C.tint_inside(img, GREEN, C.smoothstep(H * 0.55, H, yy) * 0.30)
    rng = np.random.default_rng(seed)
    ring = [(W * 0.5 + math.cos(a) * W * 0.335, (H + 6.0) + math.sin(a) * (H - 42.0)) for a in np.linspace(math.pi * 1.04, math.pi * 1.96, 30)]
    C.ink_stroke(img, ring, np.linspace(4.2, 3.2, len(ring)), INK, 0.95, dry=0.28, edge=0.4, seed=seed + 2)
    for a in np.linspace(math.pi * 1.10, math.pi * 1.90, 13):        # voussoir joints
        ca, sa = math.cos(a), math.sin(a)
        p0 = (W * 0.5 + ca * W * 0.335, (H + 6.0) + sa * (H - 42.0))
        p1 = (W * 0.5 + ca * W * 0.375, (H + 6.0) + sa * (H - 26.0))
        C.ink_stroke(img, [p0, p1], [1.5, 0.6], INK2, 0.8, dry=0.3, seed=seed + int(a * 100))
    deck = [(4.0, 6.0), (W * 0.5, 4.0), (W - 4.0, 6.0)]
    C.ink_stroke(img, deck, [5.0, 5.6, 5.0], INK, 0.97, dry=0.20, edge=0.3, seed=seed + 3)
    C.ink_stroke(img, [(8.0, 30.0), (W - 8.0, 30.0)], 1.6, INK2, 0.7, dry=0.5, seed=seed + 4)
    for gx in np.arange(40.0, W - 20.0, 62.0):
        C.ink_stroke(img, [(gx + rng.uniform(-6, 6), 9.0), (gx + rng.uniform(-6, 6), 29.0)], 1.0, INK2, 0.55, dry=0.4, seed=seed + int(gx))
    for ex in (W * 0.10, W * 0.90):
        moss(img, (ex, H - 22.0), seed + int(ex), n=7, spread=16.0, color=GREEN_D)
    return C.back(img, SOLID)


def village(wc, hc, seed):
    """Far houses: each is a roof — two pressed strokes meeting at a ridge — over a wall that
    is only paper, half sunk in mist. A few dots for trees."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    xx, yy = C.grid(H, W)
    for i in range(int(wc * 1.2)):
        cx = W * (0.10 + 0.80 * (i + rng.uniform(0.15, 0.85)) / (wc * 1.2))
        base = H * rng.uniform(0.58, 0.82)
        hw = rng.uniform(34.0, 62.0)
        rise = hw * rng.uniform(0.34, 0.50)
        wall_h = rng.uniform(26.0, 44.0)
        wall = C.rect_mask(H, W, cx - hw * 0.80, base - wall_h, cx + hw * 0.80, base)
        C.over(img, WHITE, wall * 0.80)
        C.over(img, PALE, wall * C.smoothstep(base - wall_h * 0.5, base, yy) * 0.25)
        tone = INK2 if i % 2 == 0 else GREY
        for s in (-1, 1):                                   # the two slopes, eaves flicked up
            slope = C.bezier((cx, base - wall_h - rise), (cx + s * hw * 0.55, base - wall_h - rise * 0.25),
                             (cx + s * hw * 1.08, base - wall_h - rise * 0.20), n=8)
            cstroke(img, slope, rng.uniform(4.5, 6.5), tone, 0.92, seed=seed + i * 5 + s, dry=0.34, head=1.0, tail=0.22)
        for px in (-0.55, 0.55):                            # posts
            C.ink_stroke(img, [(cx + px * hw, base - wall_h), (cx + px * hw, base)], 1.2, GREY, 0.6, dry=0.4, seed=seed + i + 40)
        if rng.random() < 0.6:                              # a dark doorway
            C.over(img, INK2, C.rect_mask(H, W, cx - 5.0, base - wall_h * 0.62, cx + 5.0, base) * 0.7)
    for i in range(int(wc * 1.4)):                          # trees between the houses
        tx = rng.uniform(0.04, 0.96) * W
        ty = H * rng.uniform(0.50, 0.74)
        blob(img, tx, ty, rng.uniform(12.0, 24.0), rng.uniform(10.0, 20.0), GREEN_D if i % 3 else INK2, 0.62, seed + 60 + i)
        moss(img, (tx, ty), seed + 80 + i, n=5, spread=9.0)
    img[..., 3] *= C.smoothstep(H * 0.98, H * 0.66, yy + (C.fbm(xx / 90.0, yy / 30.0, seed + 9, octaves=3) - 0.5) * H * 0.20)
    return img


def boat(seed):
    """渔舟 — one curved stroke for the hull, an awning, a fisherman in three touches, and
    two pale lines of water. The empty paper around it is the river."""
    img = C.cells(3, 1)
    H, W = img.shape[:2]
    hull = C.bezier((W * 0.18, H * 0.58), (W * 0.50, H * 0.80), (W * 0.84, H * 0.52), n=14)
    C.ink_stroke(img, hull, [2.0, 4.0, 5.6, 6.4, 6.6, 6.6, 6.4, 6.0, 5.4, 4.6, 3.8, 3.0, 2.2, 1.4, 0.8], INK, 0.95,
                 dry=0.30, edge=0.3, seed=seed)
    awning = C.bezier((W * 0.44, H * 0.60), (W * 0.54, H * 0.30), (W * 0.66, H * 0.58), n=10)
    cstroke(img, awning, 3.4, INK2, 0.85, seed=seed + 1, dry=0.4, head=0.8, tail=0.5)
    fx = W * 0.30
    C.ink_stroke(img, [(fx, H * 0.60), (fx + 2.0, H * 0.40)], [3.6, 2.6], INK, 0.95, dry=0.2, seed=seed + 2)      # body
    C.over(img, INK, C.aa(C.sdf_circle(H, W, fx + 2.5, H * 0.33, 3.6), 1.4) * 0.95)                                # head
    hat = C.bezier((fx - 9.0, H * 0.31), (fx + 2.5, H * 0.22), (fx + 14.0, H * 0.31), n=6)
    C.ink_stroke(img, hat, [1.0, 2.2, 2.6, 2.6, 2.2, 1.4, 1.0], INK, 0.95, dry=0.1, seed=seed + 3)                 # straw hat
    C.ink_stroke(img, [(fx + 4.0, H * 0.44), (fx - 34.0, H * 0.06)], [1.3, 0.5], INK2, 0.85, dry=0.3, seed=seed + 4)  # the rod
    for k, (y, x0, x1) in enumerate([(0.82, 0.06, 0.40), (0.90, 0.46, 0.94), (0.76, 0.86, 0.99)]):
        C.ink_stroke(img, [(W * x0, H * y), (W * (x0 + x1) / 2, H * y + 1.5), (W * x1, H * y)], [0.5, 1.5, 0.4], CYAN,
                     0.55, dry=0.5, seed=seed + 10 + k)
    return img


def pavilion(seed):
    """亭 — a pavilion on a rock: a roof of two flicked strokes and a finial, four posts, a
    rail; the rock is one wet touch with moss."""
    img = C.cells(3, 3)
    H, W = img.shape[:2]
    cx = W * 0.5
    blob(img, cx, H * 0.88, W * 0.33, H * 0.11, INK2, 0.92, seed, soft=0.35)
    blob(img, cx - W * 0.10, H * 0.83, W * 0.20, H * 0.045, rgb("#C7CFCE"), 0.8, seed + 1, soft=0.4)
    floor_y = H * 0.76
    roof_y = H * 0.34
    wall = C.rect_mask(H, W, cx - W * 0.22, roof_y + 20.0, cx + W * 0.22, floor_y)
    C.over(img, WHITE, wall * 0.55)
    for px in (-0.22, -0.08, 0.08, 0.22):
        C.ink_stroke(img, [(cx + px * W, roof_y + 22.0), (cx + px * W + 1.0, floor_y)], 2.2, INK2, 0.9, dry=0.3, seed=seed + int(px * 100) + 50)
    C.ink_stroke(img, [(cx - W * 0.25, floor_y), (cx + W * 0.25, floor_y)], 3.0, INK, 0.95, dry=0.25, seed=seed + 2)
    C.ink_stroke(img, [(cx - W * 0.22, floor_y - 26.0), (cx + W * 0.22, floor_y - 26.0)], 1.4, INK2, 0.75, dry=0.4, seed=seed + 3)
    for s in (-1, 1):
        slope = C.bezier((cx, roof_y - 34.0), (cx + s * W * 0.18, roof_y + 14.0), (cx + s * W * 0.40, roof_y + 6.0), n=10)
        cstroke(img, slope, 8.0, INK, 0.96, seed=seed + 4 + s, dry=0.26, head=1.0, tail=0.18)
    roof_fill = C.poly_mask(H, W, [(cx, roof_y - 30.0), (cx + W * 0.33, roof_y + 16.0), (cx - W * 0.33, roof_y + 16.0)])
    wet(img, roof_fill, rgb("#3F5258"), opacity=0.85, seed=seed + 6, bleed=3.0, bloom=0.2)
    C.over(img, INK, C.aa(C.sdf_circle(H, W, cx, roof_y - 40.0, 5.0), 1.5) * 0.95)
    moss(img, (cx + W * 0.24, H * 0.84), seed + 7, n=7, spread=14.0, color=GREEN_D)
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


# Brush-written characters. Each stroke is a path in a 100 x 100 box plus the brush's
# half-width at full pressure — written the way they are written, not set in type.
GLYPHS = {
    "山": [([(50, 6), (50, 44), (50, 84)], 6.5),
           ([(17, 40), (17, 62), (18, 85), (50, 85), (84, 85)], 6.0),
           ([(84, 36), (84, 60), (84, 87)], 6.5)],
    "水": [([(50, 4), (50, 50), (50, 88), (40, 79)], 6.5),
           ([(14, 34), (40, 31), (32, 52), (16, 72)], 5.2),
           ([(82, 24), (70, 34), (58, 45)], 5.2),
           ([(56, 43), (70, 62), (90, 84)], 6.2)],
    "竹": [([(30, 6), (22, 18), (10, 30)], 5.0),
           ([(22, 22), (34, 21), (46, 21)], 4.6),
           ([(34, 22), (34, 55), (34, 90)], 5.6),
           ([(72, 6), (64, 18), (54, 30)], 5.0),
           ([(64, 22), (78, 21), (92, 21)], 4.6),
           ([(78, 22), (78, 58), (78, 92), (68, 84)], 5.8)],
}


def write(img, text, x, y, size, seed, color=None):
    """Writes `text` downward from (x, y), one `size`-px box per character."""
    for k, ch in enumerate(text):
        for j, (path, width) in enumerate(GLYPHS[ch]):
            pts = np.asarray(path, np.float32) / 100.0 * size + np.array([x, y + k * size * 1.04], np.float32)
            dense = []
            for a, b in zip(pts[:-1], pts[1:]):                 # the brush engine likes close-set points
                dense.extend([a + (b - a) * t for t in np.linspace(0.0, 1.0, 6, endpoint=False)])
            dense.append(pts[-1])
            cstroke(img, dense, width * size / 100.0, INK if color is None else color, 0.96,
                    seed=seed + k * 20 + j, dry=0.30, head=1.0, tail=0.28, edge=0.25)
    return img


def colophon(seed):
    """题款 — the painter's inscription: 山水 written with the same brush that painted the
    hills, and a small seal pressed below it in ink green."""
    img = C.cells(1, 3)
    H, W = img.shape[:2]
    write(img, "山水", W * 0.14, 10.0, W * 0.72, seed)
    sz = 50.0
    sx0, sy0 = (W - sz) / 2.0, H - sz - 16.0
    plate = C.rough(C.rect_mask(H, W, sx0, sy0, sx0 + sz, sy0 + sz, radius=5.0), 2.0, 8.0, seed + 5)
    C.over(img, GREEN, plate * (0.78 + 0.22 * C.field(H, W, 3.0, seed + 6, octaves=2)))
    carved = C.new(H, W)
    write(carved, "竹", sx0 + 7.0, sy0 + 6.0, sz - 14.0, seed + 9, color=WHITE)
    C.over(img, WHITE, carved[..., 3] * 0.95)
    return img


def leaf_fringe(wc, hc, seed):
    """Foreground: dark leaves hanging in from the top of the frame (closeview)."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for i in range(int(wc * 1.6)):
        x0 = rng.uniform(0.06, 0.94) * W
        ln = rng.uniform(0.30, 0.80) * H
        end = np.array([min(max(x0 + rng.uniform(-50, 50), 40.0), W - 40.0), ln])
        tw = C.bezier((x0, -6.0), (x0 + rng.uniform(-30, 30), ln * 0.4), end, n=8)
        C.ink_stroke(img, tw, np.linspace(3.2, 0.9, len(tw)), INK, 0.95, dry=0.2, seed=seed + i)
        keep = (12.0, W - 12.0, -1e9)             # in from the top on purpose; never cut at the sides
        for s in (-1, 1):
            leaf_cluster(img, end, s, rng, 0.85, INK, GREY, count=3, spread=(10, 85), keep=keep)
        leaf_cluster(img, tw[4], 1 if rng.random() < 0.5 else -1, rng, 0.75, INK2, PALE, count=2, spread=(10, 85), keep=keep)
    img[..., 3] *= C.smoothstep(H - 1.0, H * 0.90, C.grid(H, W)[1])       # nor at the bottom
    return img


# -------------------------------------------------------------------- build

def build():
    """Returns [(tile_id, display, role, w, h, category_dir, file_stem, image, brief, extra)]."""
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.bm_rock", "Ink Rock", "Ground", 1, 1, "Ground", "ground_bm_rock", rock_atlas(4),
        "Freehand ink rock: opaque dark wash body with axe-cut texture strokes, its top-lit faces left almost "
        "as bare paper, a bold dry contour where rock meets air, moss dots on the lip, underside in dry tatters.",
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
    add("softvolume.bm_waterfall", "Waterfall", "SoftVolume", 2, 4, "SoftVolume", "softvolume_bm_waterfall", waterfall(2, 4, 55),
        "A section of falling water: a ribbon of bare white paper between wet black rock edges, with long dry "
        "strokes of indigo. Tiles vertically. Soft volume that sweeps whoever enters it downward.")
    add("softvolume.bm_pine", "Cliff Pine", "SoftVolume", 4, 5, "SoftVolume", "softvolume_bm_pine", cliff_pine(4, 5, 57),
        "A leaning pine on a spur of rock: twisting dry-brush trunk, angular branches, flat pads of hair-stroke "
        "needles over ink green. Enterable soft volume.")

    add("platform.bm_roof_wide", "Temple Roof (wide)", "Platform", 6, 1, "Platform", "platform_bm_roof_wide", roof(6, 61),
        "Sweeping blue-grey tiled temple roof in side view, heavy inked ridge beam with owl-tail curls, "
        "flying upturned eaves, a row of round tile-ends, weathered timber fascia.", walk=16)
    add("platform.bm_roof_small", "Temple Roof (small)", "Platform", 4, 1, "Platform", "platform_bm_roof_small", roof(4, 67),
        "Smaller sweeping tiled roof, same ridge, eaves and timber fascia.", walk=16)
    add("platform.bm_beam", "Bracket Beam", "Platform", 3, 1, "Platform", "platform_bm_beam", beam(3, 71),
        "Weathered ink-green timber tie-beam on stepped dougong bracket clusters, inked edges.", walk=6)
    add("platform.bm_bridge", "Stone Bridge", "Platform", 6, 2, "Platform", "platform_bm_bridge", bridge(6, 75),
        "An arched stone bridge: one bold ink stroke for the flat deck, one for the arch ring, pale stone left "
        "almost as paper, moss at the springers.", walk=8)

    add("hazard.bm_stakes", "Bamboo Stakes", "Hazard", 2, 1, "Hazard", "hazard_bm_stakes", stakes(81),
        "A row of sharpened green bamboo stakes cut on the bias, tips inked black.")

    add("deco.bm_column", "Timber Column", "Decoration", 1, 3, "Decoration", "deco_bm_column", column(3, 91),
        "Weathered ink-green timber column with pale bands, bracket capital and carved stone plinth.")
    add("deco.bm_lattice", "Lattice Wall", "Decoration", 2, 3, "Decoration", "deco_bm_lattice", lattice(2, 3, 95),
        "Paper lattice window bay over a dark timber dado panel, dark timber frame.")
    add("deco.bm_lantern", "Stone Lantern", "Decoration", 1, 2, "Decoration", "deco_bm_lantern", lantern_solid(101),
        "Stone pagoda lantern with a pale, cool glowing paper window.")
    add("deco.bm_seal", "Colophon & Seal", "Decoration", 1, 3, "Decoration", "deco_bm_seal", colophon(105),
        "The painter's inscription: 山水 written in brush calligraphy above a small ink-green seal.")
    add("deco.bm_moon", "Moon", "Decoration", 3, 3, "Decoration", "deco_bm_moon", moon(111),
        "The moon as unpainted paper: a pale indigo wash breathed around a disc that is left blank.")
    add("deco.bm_cranes", "Cranes", "Decoration", 3, 2, "Decoration", "deco_bm_cranes", cranes(115),
        "A rising skein of six cranes, each two flicked ink strokes.")
    add("deco.bm_peak", "Karst Peak (hero)", "Decoration", 8, 12, "Decoration", "deco_bm_peak", hero_peak(8, 12, 201),
        "One towering karst peak in splashed ink: black and wet at the crown, dry texture strokes down its faces, "
        "two belts of mist across it, its foot lost in white, pines clinging to its shoulders.")
    add("deco.bm_peak_b", "Karst Peak (slender)", "Decoration", 5, 10, "Decoration", "deco_bm_peak_b", hero_peak(5, 10, 233, pines=4, lean=-0.05),
        "A slender second peak in the same splashed ink, to stand beside or behind the first.")
    add("deco.bm_pine", "Pine", "Decoration", 4, 5, "Decoration", "deco_bm_pine", cliff_pine(4, 5, 61, lean=-0.35),
        "A leaning pine on a spur of rock, for far and foreground layers.")
    add("deco.bm_fall_far", "Distant Waterfall", "Decoration", 3, 8, "Decoration", "deco_bm_fall_far", fall_far(3, 8, 211),
        "A far waterfall: two wet black shoulders of rock with a thread of bare white paper between them, mist at its foot.")
    add("deco.bm_spray", "Waterfall Spray", "Decoration", 4, 2, "Decoration", "deco_bm_spray", spray(4, 2, 215),
        "The foot of a waterfall: white where the water lands, a few flicked arcs of indigo.")
    add("deco.bm_village", "Distant Village", "Decoration", 6, 3, "Decoration", "deco_bm_village", village(6, 3, 221),
        "A far village: each house a roof of two pressed strokes over a wall of bare paper, half sunk in mist.")
    add("deco.bm_pavilion", "Pavilion", "Decoration", 3, 3, "Decoration", "deco_bm_pavilion", pavilion(225),
        "A small pavilion on a rock: roof of two flicked strokes with a finial, four posts, a rail.")
    add("deco.bm_boat", "Fishing Boat", "Decoration", 3, 1, "Decoration", "deco_bm_boat", boat(231),
        "A fisherman's boat: one curved hull stroke, an awning, a figure in a straw hat with a rod, two pale lines of water.")
    add("deco.bm_peaks_far", "Karst Peaks (far)", "Decoration", 16, 6, "Decoration", "deco_bm_peaks_far",
        mountain(16, 6, [(0.06, 0.40, 0.040), (0.24, 0.30, 0.046), (0.43, 0.46, 0.050), (0.62, 0.34, 0.040), (0.83, 0.44, 0.048)],
                 [(0.13, 0.18, 0.036), (0.32, 0.36, 0.030), (0.52, 0.06, 0.032), (0.70, 0.28, 0.038), (0.91, 0.14, 0.034)],
                 121, C.mix(PALE, CYAN, 0.35), 0.62, fade=0.52, edge=0.30),
        "Far limestone towers as a pale indigo-grey wash, dense at the crown, crossed by a belt of mist and dissolving below. Tiles horizontally.")
    add("deco.bm_peaks_mid", "Karst Peaks (near)", "Decoration", 16, 6, "Decoration", "deco_bm_peaks_mid",
        mountain(16, 6, [(0.16, 0.38, 0.044), (0.40, 0.46, 0.042), (0.66, 0.36, 0.048), (0.90, 0.48, 0.040)],
                 [(0.07, 0.30, 0.036), (0.27, 0.14, 0.034), (0.36, 0.46, 0.028), (0.55, 0.24, 0.038), (0.78, 0.10, 0.034), (0.86, 0.42, 0.028)],
                 127, INK2, 0.86, fade=0.50, edge=0.50),
        "Nearer limestone towers in splashed dark ink with dry texture drags and a belt of mist. Tiles horizontally.")
    add("deco.bm_mist_band", "Mist Band", "Decoration", 16, 2, "Decoration", "deco_bm_mist_band", mist_bank(16, 2, 131, strength=0.80),
        "A long drift of paper-white mist to layer between mountain ranges. Tiles horizontally.")
    add("deco.bm_bamboo_far", "Distant Bamboo", "Decoration", 3, 9, "Decoration", "deco_bm_bamboo_far",
        sprite_bamboo(3, 9, 141, [(0.22, 0.10, 8.0, 0.02, 1), (0.50, 0.03, 9.5, -0.015, 2), (0.78, 0.16, 7.5, 0.03, 3)],
                      ink=PALE, leaf_ink=GREY, leaf_pale=MIST, opacity=0.78, leaf_scale=0.9, foot_fade=0.22),
        "Three distant bamboo culms in pale grey ink, rising out of mist, for the background layers.")
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
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(228, 233, 232), sheet_cell=64)


if __name__ == "__main__":
    sheet = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
    export(sheet)

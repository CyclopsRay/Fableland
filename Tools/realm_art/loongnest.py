#!/usr/bin/env python3
"""Loong's Nest (龙宫) tile set — the Palace of LOOING realm (PL), under the sea.

Art language: 工笔重彩 — Chinese meticulous-brush painting in heavy mineral colour. The
opposite of Wulin's freehand ink: every form is enclosed by a fine, even "iron-wire" ink
line (铁线描) and filled with smoothly graded pigment (三矾九染 — washed and re-washed until
it glows), modelled like a real thing under real light. Palette: black, purple, blue and
cyan only — azurite, indigo and malachite-turned-cyan on a ground of deep sea. The single
light is the pale cyan that falls from the surface; pearls are the only lamps.
See `Docs/Art/RealmStyleBible.md` §Loong's Nest.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/loongnest.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "LoongsNest"
SET = "ln"

LINE = rgb("#05060F")      # the iron-wire line
DEEP = rgb("#0C1230")      # the sea itself: every map's canvas
INDIGO = rgb("#161D4A")
PURPLE_D = rgb("#2C1F5C")
PURPLE = rgb("#5B3FA8")
PURPLE_L = rgb("#A48BE6")
BLUE_D = rgb("#15307A")
BLUE = rgb("#2C63CC")
BLUE_L = rgb("#7FB0F5")
CYAN_D = rgb("#127A8C")
CYAN = rgb("#33C7D6")
CYAN_L = rgb("#B6F3F4")
PEARL = rgb("#EAFBFF")

TOP_LIGHT = (-0.20, -0.78, 0.60)      # the surface is the only light: it falls from above

BRIEF = ("Chinese gongbi (meticulous brush, heavy mineral colour) game sprite on transparent ground, "
         "set under the sea: fine even ink contour lines enclosing smoothly graded azurite blue, purple "
         "and cyan, modelled realistically under pale cyan light from above. Palette strictly black, "
         "purple, blue, cyan; pearls are the only lamps. Orthographic side view, no cast shadow, no text.")


def wire(img, pts, width=1.7, color=None, opacity=0.95, seed=0):
    """铁线描 — the iron-wire line: even, unbroken, the same weight from end to end."""
    return C.ink_stroke(img, pts, width, LINE if color is None else color, opacity, dry=0.0, edge=0.0, seed=seed, soft=0.9)


def graded(img, mask, top, bottom, y0, y1, seed=0, mottle=0.06):
    """Mineral colour laid in graded washes: `top` at y0 running to `bottom` at y1."""
    h, w = mask.shape
    xx, yy = C.grid(h, w)
    t = np.clip((yy - y0) / max(y1 - y0, 1.0), 0.0, 1.0)
    col = C.mix(top, bottom, t)
    col = np.clip(col + (C.fbm(xx / 18.0, yy / 18.0, seed, octaves=3)[..., None] - 0.5) * mottle, 0.0, 1.0)
    C.over(img, col, mask)
    return img


# --------------------------------------------------------------------- ground


def reef_atlas(variants=4):
    """`ground.ln_reef` — seabed rock: faceted indigo stone veined with azurite, lit from
    above; its walkable top is a crust of pale shell-sand with small corals."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    per = lambda s: (Wn // s, CELL // s)
    facets = C.fbm(xx / 32.0, yy / 32.0, 3, octaves=4, period=per(32))
    ridged = 1.0 - np.abs(C.fbm(xx / 16.0, yy / 16.0, 5, octaves=3, period=per(16)) * 2.0 - 1.0)
    height = facets * 26.0 + ridged * 7.0
    albedo = C.mix(INDIGO, PURPLE_D, np.clip(C.fbm(xx / 64.0, yy / 64.0, 7, octaves=3, period=per(64)) * 1.6 - 0.3, 0, 1))
    vein = C.smoothstep(0.86, 0.96, ridged) * C.smoothstep(0.45, 0.70, C.fbm(xx / 64.0, yy / 32.0, 9, octaves=3, period=(Wn // 64, CELL // 32)))
    albedo = C.mix(albedo, BLUE, vein[..., None] * 0.75)
    col = C.lit(height, albedo, light=TOP_LIGHT, ambient=0.50, strength=1.9, rim=(CYAN * 0.5, (0.0, -1.0, 0.25), 0.30))
    base = C.new(CELL, Wn)
    base[..., :3] = col
    base[..., 3] = 1.0
    crack = C.smoothstep(0.035, 0.0, np.abs(C.fbm(xx / 32.0, yy / 32.0, 11, octaves=3, period=per(32)) - 0.5))
    C.over(base, LINE, crack * 0.75)

    def cap(cell, g):
        d = C.carve_top(cell, g, 21, variants, base=11.0, wobble=8.0)
        a = cell[..., 3]
        crust = C.smoothstep(15.0, 2.0, d) * a
        C.tint_inside(cell, C.mix(CYAN_L, PEARL, 0.4), crust * 0.80)
        C.over(cell, PEARL, np.exp(-((d - 2.0) / 1.8) ** 2) * a * 0.95)      # the line you stand on
        C.over(cell, LINE, np.exp(-((d - 17.0) / 1.3) ** 2) * a * 0.55)
        rng = np.random.default_rng(700 + g)
        for _ in range(5):                                                    # coral nubs and shells
            cx = rng.uniform(8, CELL - 8)
            col = CYAN if rng.random() < 0.5 else PURPLE_L
            dot = C.aa(C.sdf_circle(CELL, CELL, cx, 13.0 + rng.uniform(-1, 3), rng.uniform(2.5, 4.5)), 1.4)
            C.over(cell, col, dot * 0.9)

    def side(cell, g, left):
        d = C.carve_side(cell, left, 31 + g, inset=7.0, wobble=7.0)
        a = cell[..., 3]
        C.over(cell, LINE, np.exp(-((d - 1.5) / 1.6) ** 2) * a * 0.9)
        C.multiply_inside(cell, 1.0 - C.smoothstep(22.0, 2.0, d) * (0.10 if left else 0.38))

    def underside(cell, g):
        d = C.carve_under(cell, g, 41, variants, depth=46.0)
        a = cell[..., 3]
        C.multiply_inside(cell, 1.0 - C.smoothstep(40.0, 0.0, d) * 0.55)
        C.over(cell, LINE, np.exp(-((d - 1.5) / 1.6) ** 2) * a * 0.9)

    return C.strip_atlas(base, cap, side, underside, variants)


def palace_atlas(variants=4):
    """`ground.ln_palace` — palace masonry faced in glazed fish-scale tile (鱼鳞瓦): purple
    running to blue across each scale, every scale wire-lined and domed. The coping is a
    cyan key-fret (回纹) under a pearl-white edge; the underside ends in a wave valance."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    sw, sh = 32.0, 16.0                               # one scale: 4 to a cell, 8 rows to a cell
    row = np.floor(yy / sh)
    fx = (xx + (row % 2) * sw * 0.5) % sw
    fy = yy - row * sh
    d = np.hypot(fx - sw * 0.5, (fy - sh * 0.05) * 1.05)      # each scale is a half-disc hanging from its row
    inside = d < sw * 0.55
    dome = np.sqrt(np.clip(1.0 - (d / (sw * 0.62)) ** 2, 0.0, 1.0))
    tone = np.clip(d / (sw * 0.55), 0.0, 1.0)                 # 0 at the scale's root .. 1 at its rim
    wash = C.fbm(xx / 64.0, yy / 64.0, 3, octaves=3, period=(Wn // 64, CELL // 64))
    albedo = C.mix(C.mix(PURPLE_D, PURPLE, wash[..., None]), BLUE, tone[..., None] * 0.85)
    col = C.lit(dome * 9.0, albedo, light=TOP_LIGHT, ambient=0.55, strength=1.4, spec=0.10, spec_pow=22.0)
    base = C.new(CELL, Wn)
    base[..., :3] = np.where(inside[..., None], col, INDIGO)
    base[..., 3] = 1.0
    C.over(base, LINE, np.exp(-((d - sw * 0.55) / 1.3) ** 2) * 0.90)
    C.over(base, CYAN_L, np.exp(-((d - sw * 0.44) / 1.1) ** 2) * (fy > sh * 0.45) * 0.30)

    def cap(cell, g):
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        band = gy < 30.0
        cell[..., :3] = np.where(band[..., None], INDIGO, cell[..., :3])
        # 回纹 — a squared spiral, repeated every 32 px, in cyan on the dark band
        u, v = gx % 32.0, gy - 9.0
        fret = ((np.abs(v - 0.0) < 1.6) & (u > 3) & (u < 29)) | ((np.abs(u - 29.0) < 1.6) & (v > 0) & (v < 14)) | \
               ((np.abs(v - 14.0) < 1.6) & (u > 9) & (u < 29)) | ((np.abs(u - 9.0) < 1.6) & (v > 5) & (v < 14)) | \
               ((np.abs(v - 5.0) < 1.6) & (u > 9) & (u < 21)) | ((np.abs(u - 21.0) < 1.6) & (v > 5) & (v < 10))
        C.over(cell, CYAN, (fret & band & (gy > 7)).astype(np.float32) * 0.92)
        C.over(cell, PEARL, (gy < 3.2).astype(np.float32) * 0.97)                  # the line you stand on
        C.over(cell, LINE, ((np.abs(gy - 4.6) < 1.2) | (np.abs(gy - 29.0) < 1.4)).astype(np.float32) * 0.95)

    def side(cell, g, left):
        gx, gy = C.grid(CELL, CELL)
        dist = gx if left else (CELL - gx)
        cell[..., :3] = np.where((dist < 12.0)[..., None], C.mix(BLUE_D, INDIGO, 0.35), cell[..., :3])
        C.over(cell, LINE, (np.abs(dist - 12.0) < 1.3).astype(np.float32) * 0.95)
        C.over(cell, CYAN, (np.abs(dist - 6.0) < 1.0).astype(np.float32) * 0.80)
        C.over(cell, LINE, (dist < 1.6).astype(np.float32) * 0.95)

    def underside(cell, g):
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        wave = CELL - 20.0 + np.abs(np.sin(gx / 32.0 * math.pi)) * 14.0      # a scalloped valance, 32 px to a scallop
        cell[..., 3] *= C.smoothstep(wave + 1.2, wave - 1.2, gy)
        C.over(cell, CYAN, np.exp(-((gy - wave + 5.0) / 1.3) ** 2) * cell[..., 3] * 0.85)
        C.over(cell, LINE, np.exp(-((gy - wave + 1.2) / 1.4) ** 2) * cell[..., 3] * 0.95)
        C.multiply_inside(cell, 1.0 - C.smoothstep(wave - 34.0, wave, gy) * 0.40)

    return C.strip_atlas(base, cap, side, underside, variants)


# ------------------------------------------------------------------ platforms


def eave(wc, seed):
    """A palace eave in cyan glaze: a black ridge along the canvas top (the line you stand
    on), rows of wire-lined tile below it, both tips swept up with a pearl hanging from each."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xs = np.linspace(0.0, 1.0, 72)

    def top_y(u):
        dd = np.abs(u - 0.5) * 2.0
        return 4.0 + 2.0 * dd + 30.0 * np.clip((dd - 0.80) / 0.20, 0, 1) ** 2 * (-1.0) * 0.0

    def bot_y(u):
        dd = np.abs(u - 0.5) * 2.0
        return H - 34.0 - 40.0 * np.clip((dd - 0.55) / 0.45, 0, 1) ** 2

    poly = [(u * W, 4.0) for u in xs] + [(u * W, float(bot_y(np.array(u)))) for u in xs[::-1]]
    body = C.poly_mask(H, W, poly)
    graded(img, body, CYAN, BLUE_D, 4.0, H - 34.0, seed)
    n = int(wc * 9)
    for i in range(n + 1):                                   # tile courses, ridge to eave
        u = i / n
        x_top = W * (0.5 + (u - 0.5) * 0.90)
        yb = float(bot_y(np.array(u)))
        wire(img, C.bezier((x_top, 9.0), ((x_top + W * u) / 2, (9.0 + yb) / 2 + 4.0), (W * u, yb), n=6), 1.3, opacity=0.8, seed=seed + i)
    for k in range(1, 4):                                    # cross courses
        pts = [(u * W, 4.0 + (float(bot_y(np.array(u))) - 4.0) * k / 4.0) for u in np.linspace(0.02, 0.98, 30)]
        wire(img, pts, 1.1, opacity=0.6, seed=seed + 50 + k)
    wire(img, [(W * 0.02, 6.0), (W * 0.5, 4.5), (W * 0.98, 6.0)], 4.6, seed=seed + 90)              # ridge
    wire(img, [(u * W, float(bot_y(np.array(u)))) for u in np.linspace(0.01, 0.99, 40)], 2.4, seed=seed + 91)
    fascia = C.poly_mask(H, W, [(W * 0.12, H - 34.0), (W * 0.88, H - 34.0), (W * 0.86, H - 12.0), (W * 0.14, H - 12.0)])
    graded(img, fascia, PURPLE, PURPLE_D, H - 34.0, H - 12.0, seed + 3)
    wire(img, [(W * 0.14, H - 12.0), (W * 0.86, H - 12.0)], 1.8, seed=seed + 92)
    wire(img, [(W * 0.16, H - 23.0), (W * 0.84, H - 23.0)], 1.0, color=CYAN, opacity=0.9, seed=seed + 93)
    for s in (0.035, 0.965):                                 # a night pearl under each tip
        cx, cy = W * s, float(bot_y(np.array(s))) + 12.0
        C.radial_glow(img, cx, cy, 9.0, CYAN, 0.5)
        C.over(img, PEARL, C.aa(C.sdf_circle(H, W, cx, cy, 4.6), 1.4) * 0.97)
        wire(img, [(cx, cy - 11.0), (cx, cy - 5.0)], 0.9, seed=seed + 95)
    return img


def shell(wc, seed):
    """A giant clam's upper valve as a ledge: its lip along the canvas top, ribs fanning
    down to the hinge, purple at the lip running to pearl."""
    img = C.cells(wc, 1)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx, hinge = W * 0.5, H - 12.0
    r = np.hypot((xx - cx) / (W * 0.49), (yy - 4.0) / (hinge - 4.0))
    body = C.smoothstep(1.0 + 0.012, 1.0 - 0.012, r) * (yy > 3.0)
    ang = np.arctan2(yy - hinge - 30.0, xx - cx)
    rib = 0.5 + 0.5 * np.cos(ang * 26.0)
    col = C.mix(C.mix(PURPLE, PURPLE_L, rib[..., None]), PEARL, (C.smoothstep(0.2, 1.0, (yy - 4.0) / (hinge - 4.0)) * 0.75)[..., None])
    col = C.lit(rib * 6.0 + (1.0 - r) * 10.0, col, light=TOP_LIGHT, ambient=0.60, strength=1.2)
    C.over(img, col, body)
    C.over(img, LINE, C.smoothstep(0.10, 0.02, np.abs(rib - 0.02)) * body * 0.55)
    edge = np.exp(-((r - 1.0) / 0.03) ** 2) * (yy > 3.0)
    C.over(img, LINE, np.clip(edge, 0, 1) * 0.95)
    wire(img, [(W * 0.015, 4.0), (W * 0.5, 3.4), (W * 0.985, 4.0)], 2.6, seed=seed)
    C.over(img, PEARL, np.exp(-((yy - 7.5) / 1.3) ** 2) * body * 0.70)
    return img


# --------------------------------------------------------------- soft volumes


def water(wc, hc, seed):
    """The sea as a thing you are in: almost nothing — a breath of blue and slow ribbons of
    caustic light. Tiles in both directions. A buoyant, resisting medium."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    a1 = C.fbm(xx / 128.0 + np.sin(yy / H * 2 * math.pi) * 0.4, yy / 32.0, seed, octaves=3, period=(W // 128, H // 32))
    a2 = C.fbm(xx / 64.0, yy / 64.0, seed + 1, octaves=3, period=(W // 64, H // 64))
    ribbon = C.smoothstep(0.60, 0.78, a1) * C.smoothstep(0.35, 0.65, a2)
    C.over(img, BLUE, np.full((H, W), 0.085, np.float32))
    C.over(img, CYAN_L, ribbon * 0.13)
    return img


def current(wc, hc, seed):
    """A current: only a few hair-thin streamlines, pointed where it runs, so it is found by
    being caught in it. Carries left to right (Flip H reverses it); it does not slow you."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for i in range(7):
        y0 = H * (0.10 + 0.80 * (i + rng.uniform(0.2, 0.8)) / 7.0)
        x0 = W * rng.uniform(0.02, 0.28)
        x1 = W * rng.uniform(0.66, 0.98)
        amp = rng.uniform(4.0, 11.0)
        ph = rng.uniform(0, 6.28)
        xs = np.linspace(x0, x1, 26)
        pts = np.stack([xs, y0 + np.sin(xs / 46.0 + ph) * amp], axis=1)
        prof = np.sin(np.linspace(0.0, math.pi, len(pts))) ** 0.6 * rng.uniform(0.9, 1.7) + 0.25
        C.ink_stroke(img, pts, prof, CYAN_L, 0.55, dry=0.0, edge=0.0, seed=seed + i, soft=0.9)
        tip = pts[-1]                                        # an arrow-head, barely there
        for s in (-1, 1):
            wire(img, [tip, (tip[0] - 11.0, tip[1] + s * 5.5)], 0.9, color=CYAN_L, opacity=0.55, seed=seed + 40 + i)
    for _ in range(26):                                      # motes carried along
        cx, cy = rng.uniform(6, W - 6), rng.uniform(6, H - 6)
        C.over(img, PEARL, C.aa(C.sdf_circle(H, W, cx, cy, rng.uniform(0.8, 1.9)), 1.2) * 0.55)
    return img


def bubbles(wc, hc, seed):
    """A column of rising bubbles: each a wire-fine ring with one highlight. Tiles
    vertically; it lifts whatever is inside."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for _ in range(int(wc * hc * 3.2)):
        cx = W * 0.5 + rng.normal(0, W * 0.17)
        cy = rng.uniform(0, H)
        r = float(np.clip(rng.lognormal(1.9, 0.5), 3.0, 19.0))
        if cx - r < 3 or cx + r > W - 3:
            continue
        for oy in (-H, 0, H):
            sdf = C.sdf_circle(H, W, cx, cy + oy, r)
            C.over(img, CYAN_L, np.exp(-(sdf / 1.25) ** 2) * 0.90)
            C.over(img, BLUE_L, C.aa(sdf, 1.4) * 0.10)
            C.over(img, PEARL, C.aa(C.sdf_circle(H, W, cx - r * 0.36, cy + oy - r * 0.36, max(1.0, r * 0.17)), 1.2) * 0.95)
    return img


def kelp(wc, hc, seed):
    """Kelp: long blades, each wire-lined with a midrib, dark teal at the root running to
    cyan at the tip. A grove of it slows and hides whoever is inside."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    xx, yy = C.grid(H, W)
    blades = sorted([(rng.uniform(0.16, 0.84), rng.uniform(0.55, 0.98), rng.uniform(15.0, 25.0), rng.uniform(0, 6.28)) for _ in range(6)],
                    key=lambda b: b[1])
    for i, (fx, tall, width, ph) in enumerate(blades):
        n = 40
        t = np.linspace(0.0, 1.0, n)
        ys = H - t * H * tall
        xs = fx * W + np.sin(t * 5.2 + ph) * W * 0.11 * (0.3 + t) + (t ** 2) * W * 0.06 * math.sin(ph)
        half = width * 0.5 * np.sin(np.clip(t * 1.08, 0.0, 1.0) * math.pi) ** 0.55 + 1.0
        left = np.stack([xs - half, ys], axis=1)
        right = np.stack([xs + half, ys], axis=1)
        m = C.poly_mask(H, W, [tuple(q) for q in left] + [tuple(q) for q in right[::-1]])
        shade = 0.62 + 0.38 * (i / max(1, len(blades) - 1))
        graded(img, m, CYAN * shade, C.mix(CYAN_D, INDIGO, 0.5) * shade, H * (1.0 - tall), H, seed + i)
        wire(img, left, 1.3, seed=seed + 10 + i)
        wire(img, right, 1.3, seed=seed + 20 + i)
        wire(img, np.stack([xs, ys], axis=1), 0.9, opacity=0.6, seed=seed + 30 + i)
    return img


# -------------------------------------------------------------------- hazard


def urchins(seed):
    """Sea urchins: three black domes bristling with spines, each spine tipped cyan."""
    img = C.cells(2, 1)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for k, (fx, r) in enumerate([(0.20, 21.0), (0.52, 27.0), (0.82, 19.0)]):
        cx, cy = W * fx, H - r * 0.55 - 3.0
        for a in np.linspace(math.pi * 1.02, math.pi * 1.98, int(r * 0.95)):
            ang = a + rng.uniform(-0.05, 0.05)
            ln = r * rng.uniform(1.55, 2.25)
            tip = (cx + math.cos(ang) * ln, cy + math.sin(ang) * ln)
            C.ink_stroke(img, [(cx, cy), tip], [2.6, 0.9], LINE, 0.97, dry=0.0, seed=seed + k * 50 + int(a * 20), soft=0.9)
            C.ink_stroke(img, [(cx, cy), tip], [1.5, 0.5], C.mix(PURPLE_L, CYAN_L, 0.45), 0.92, dry=0.0, seed=seed + k * 50 + int(a * 20) + 1, soft=0.9)
            C.over(img, PEARL, C.aa(C.sdf_circle(H, W, tip[0], tip[1], 1.7), 1.2) * 0.95)
        sdf = C.sdf_circle(H, W, cx, cy, r)
        body = C.aa(sdf, 1.4) * (C.grid(H, W)[1] < H - 2.0)
        col = C.lit(C.round_h(sdf, r), np.broadcast_to(PURPLE_D * 0.8, (H, W, 3)), light=TOP_LIGHT, ambient=0.35, strength=1.6)
        C.over(img, col, body)
        C.over(img, LINE, np.exp(-(sdf / 1.3) ** 2) * 0.95)
    return img


# ---------------------------------------------------------------- decoration


def column(hc, seed):
    """A palace column: a graded purple shaft banded in cyan, a cloud-scroll capital, a
    lotus foot — every edge wire-lined."""
    img = C.cells(1, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx = W / 2.0
    shaft = C.rect_mask(H, W, cx - 22.0, 44.0, cx + 22.0, H - 44.0)
    u = (xx - cx) / 22.0
    col = C.mix(PURPLE, PURPLE_D, np.clip(yy / H, 0, 1)[..., None]) * (0.62 + 0.38 * np.sqrt(np.clip(1.0 - u * u, 0, 1)))[..., None]
    col = col + (np.exp(-((u + 0.45) / 0.16) ** 2) * 0.22)[..., None] * PURPLE_L
    C.over(img, np.clip(col, 0, 1), shaft)
    for s in (-1, 1):
        wire(img, [(cx + s * 22.0, 44.0), (cx + s * 22.0, H - 44.0)], 1.7, seed=seed + s)
    for by in np.arange(74.0, H - 70.0, 150.0):
        band = C.rect_mask(H, W, cx - 23.0, by, cx + 23.0, by + 12.0)
        graded(img, band, CYAN_L, CYAN_D, by, by + 12.0, seed + int(by))
        for y in (by, by + 12.0):
            wire(img, [(cx - 23.0, y), (cx + 23.0, y)], 1.2, seed=seed + int(y))
    cap = C.poly_mask(H, W, [(cx - 44.0, 6.0), (cx + 44.0, 6.0), (cx + 26.0, 44.0), (cx - 26.0, 44.0)])
    graded(img, cap, CYAN, BLUE_D, 6.0, 44.0, seed + 5)
    wire(img, [(cx - 44.0, 6.0), (cx + 44.0, 6.0), (cx + 26.0, 44.0), (cx - 26.0, 44.0), (cx - 44.0, 6.0)], 1.6, seed=seed + 6)
    for s in (-1, 1):                                        # a cloud scroll on each side of the capital
        pts = [(cx + s * (30.0 - 12.0 * math.cos(a) * (1 - a / 9.0)), 22.0 + 9.0 * math.sin(a) * (1 - a / 9.0)) for a in np.linspace(0.0, 7.5, 22)]
        wire(img, pts, 1.1, color=PEARL, opacity=0.9, seed=seed + 8 + s)
    foot = C.poly_mask(H, W, [(cx - 26.0, H - 44.0), (cx + 26.0, H - 44.0), (cx + 46.0, H - 4.0), (cx - 46.0, H - 4.0)])
    graded(img, foot, BLUE, INDIGO, H - 44.0, H - 4.0, seed + 9)
    wire(img, [(cx - 26.0, H - 44.0), (cx + 26.0, H - 44.0), (cx + 46.0, H - 4.0), (cx - 46.0, H - 4.0), (cx - 26.0, H - 44.0)], 1.6, seed=seed + 10)
    for k in range(5):                                       # lotus petals around the foot
        px = cx - 36.0 + k * 18.0
        wire(img, C.bezier((px - 8.0, H - 5.0), (px, H - 34.0), (px + 8.0, H - 5.0), n=8), 1.0, color=CYAN_L, opacity=0.85, seed=seed + 12 + k)
    return img


def lantern(seed):
    """夜明珠 — a night pearl on a stand: the realm's only lamp."""
    img = C.cells(1, 2)
    H, W = img.shape[:2]
    cx = W / 2.0
    C.radial_glow(img, cx, H * 0.34, 34.0, CYAN, 0.55)
    stem = C.rect_mask(H, W, cx - 5.0, H * 0.46, cx + 5.0, H - 18.0)
    graded(img, stem, PURPLE, PURPLE_D, H * 0.46, H - 18.0, seed)
    foot = C.poly_mask(H, W, [(cx - 12.0, H - 22.0), (cx + 12.0, H - 22.0), (cx + 30.0, H - 3.0), (cx - 30.0, H - 3.0)])
    graded(img, foot, BLUE, INDIGO, H - 22.0, H - 3.0, seed + 1)
    wire(img, [(cx - 12.0, H - 22.0), (cx + 12.0, H - 22.0), (cx + 30.0, H - 3.0), (cx - 30.0, H - 3.0), (cx - 12.0, H - 22.0)], 1.4, seed=seed + 2)
    for s in (-1, 1):
        wire(img, [(cx + s * 5.0, H * 0.46), (cx + s * 5.0, H - 22.0)], 1.2, seed=seed + 3 + s)
        wire(img, C.bezier((cx + s * 5.0, H * 0.47), (cx + s * 26.0, H * 0.45), (cx + s * 22.0, H * 0.30), n=10), 1.4, seed=seed + 5 + s)   # the claws that hold it
    sdf = C.sdf_circle(H, W, cx, H * 0.34, 19.0)
    pearl = C.lit(C.round_h(sdf, 19.0), np.broadcast_to(C.mix(CYAN_L, PEARL, 0.6), (H, W, 3)), light=TOP_LIGHT, ambient=0.78, strength=0.9, spec=0.35)
    C.over(img, pearl, C.aa(sdf, 1.4))
    C.over(img, LINE, np.exp(-(sdf / 1.2) ** 2) * 0.85)
    return img


def coral(wc, hc, seed):
    """A coral tree: forking branches, purple at the root running to pale cyan tips."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    segs = []

    def grow(p, ang, ln, width, depth):
        end = (p[0] + math.cos(ang) * ln, p[1] + math.sin(ang) * ln)
        segs.append((p, end, width, depth))
        if depth >= 5 or ln < 14.0:
            return
        for s in (-1, 1):
            if rng.random() < 0.92:
                grow(end, ang + s * rng.uniform(0.28, 0.62), ln * rng.uniform(0.66, 0.84), width * 0.72, depth + 1)

    grow((W * 0.5, H - 4.0), -math.pi / 2 + rng.uniform(-0.1, 0.1), H * 0.26, 13.0, 0)
    for (p, end, width, depth) in segs:                          # outline first, then the colour inside it
        C.ink_stroke(img, [p, end], [width + 1.7, width * 0.72 + 1.7], LINE, 0.97, dry=0.0, seed=seed, soft=0.9)
    for (p, end, width, depth) in segs:
        col = C.mix(PURPLE, CYAN_L, min(1.0, depth / 4.5))
        C.ink_stroke(img, [p, end], [width, width * 0.72], col, 0.97, dry=0.0, seed=seed, soft=0.9)
    for (p, end, width, depth) in segs:
        if depth >= 4:
            C.over(img, PEARL, C.aa(C.sdf_circle(H, W, end[0], end[1], 2.6), 1.3) * 0.9)
    return img


def palace_far(wc, hc, seed):
    """The far palace: tier on tier of swept roofs, seen through deep water — flat blues,
    lit along each ridge, a few pearl windows. Its mound runs solid to the canvas bottom."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    cx = W * 0.5
    # The mound is a dome that sinks to nothing at both ends: the palace is one tile pinned
    # in the deep, and a mound that ran to the sprite's edges would read as a pale rectangle.
    dome = np.clip(1.0 - np.abs((xx - cx) / (W * 0.5)) ** 3.0, 0.0, 1.0)
    mound = H - (H * 0.26 + np.exp(-(((xx - cx) / (W * 0.34)) ** 2)) * H * 0.10) * dome \
        + (C.fbm(xx / 64.0, yy * 0 + 1.0, seed, octaves=3) - 0.5) * 22.0 * dome
    C.over(img, C.mix(INDIGO, DEEP, 0.25), C.smoothstep(-1.5, 1.5, yy - mound))
    tiers = [(0.30, 0.74, 0.60), (0.22, 0.56, 0.44), (0.15, 0.40, 0.30), (0.09, 0.26, 0.19)]      # half-width, roof y, wall top
    for k, (hw, ry, wt) in enumerate(tiers):
        tone = C.mix(BLUE_D, INDIGO, 0.25 + 0.15 * k)
        if k > 0:
            lower = tiers[k - 1]
            wall = C.rect_mask(H, W, cx - W * hw * 0.80, H * ry, cx + W * hw * 0.80, H * lower[1] - 4.0)
            C.over(img, C.mix(tone, DEEP, 0.2), wall)
        roof = []
        for u in np.linspace(-1.0, 1.0, 40):
            roof.append((cx + u * W * hw, H * ry - (1.0 - abs(u)) ** 0.7 * H * 0.070 - max(0.0, abs(u) - 0.82) ** 2 * H * 1.4))
        poly = roof + [(cx + W * hw * 0.86, H * ry + 8.0), (cx - W * hw * 0.86, H * ry + 8.0)]
        C.over(img, tone, C.poly_mask(H, W, poly))
        wire(img, roof, 1.5, color=BLUE_L, opacity=0.75, seed=seed + k)
        for j in range(int(hw * 22)):                            # pearl-lit windows
            wx = cx + (j - (int(hw * 22) - 1) / 2.0) * W * 0.045
            if k > 0:
                C.over(img, CYAN_L, C.rect_mask(H, W, wx - 4.0, H * ry + 16.0, wx + 4.0, H * ry + 30.0) * 0.75)
    C.radial_glow(img, cx, H * 0.14, 20.0, CYAN, 0.45)
    C.over(img, PEARL, C.aa(C.sdf_circle(H, W, cx, H * 0.145, 5.0), 1.4) * 0.95)       # the pearl on its summit
    return img


def ridge_far(wc, hc, seed):
    """Seabed ridges, far and nearer, as flat tones in the water's own blue. Loops; solid below."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    for k, (base, amp, tone) in enumerate([(0.42, 0.30, C.mix(BLUE_D, DEEP, 0.55)), (0.62, 0.26, C.mix(INDIGO, DEEP, 0.15))]):
        r1 = C.fbm(xx / 256.0, yy * 0 + 2.0 + k, seed + k, octaves=4, period=(W // 256, 0))
        r2 = C.fbm(xx / 32.0, yy * 0 + 5.0 + k, seed + 10 + k, octaves=3, period=(W // 32, 0))
        line = H * (base - (r1 - 0.5) * amp * 1.6 - (r2 - 0.5) * 0.05)
        C.over(img, tone, C.smoothstep(-1.5, 1.5, yy - line))
        C.over(img, BLUE_L, np.exp(-((yy - line - 2.0) / 1.6) ** 2) * (0.22 if k == 0 else 0.32))
    return img


def rays(wc, hc, seed):
    """Light from the surface: slanting shafts, widening as they fall and gone before the floor."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    u = xx - yy * 0.16
    shafts = C.smoothstep(0.50, 0.72, C.fbm(u / 64.0, yy / 2048.0, seed, octaves=2))
    shafts = np.maximum(shafts, C.smoothstep(0.58, 0.75, C.fbm(u / 22.0, yy / 2048.0, seed + 1, octaves=2)) * 0.7)
    fade = C.smoothstep(H * 0.98, H * 0.25, yy) * C.smoothstep(0.0, W * 0.16, xx) * C.smoothstep(W, W * 0.84, xx)
    C.over(img, C.mix(CYAN_L, PEARL, 0.4), shafts * fade * 0.30)
    return img


def surface(wc, hc, seed, skin=0.80):
    """The top of the sea, seen from beneath, and the open air above it. Loops.

    The sea's skin is a bright rippling line `skin` of the way down the tile; everything
    above it is the air seen through the surface — pale, wide and empty — and below it the
    light breaks up into the water. Lay the tile so the skin sits on the first row of water:
    what a player sees as the surface must be where the water starts to hold them."""
    img = C.cells(wc, hc)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    rip = C.fbm(xx / 64.0, yy / 16.0, seed, octaves=3, period=(W // 64, 0))
    edge = H * skin + (C.fbm(xx / 128.0, yy * 0 + 1.0, seed + 1, octaves=3, period=(W // 128, 0)) - 0.5) * CELL * 0.40
    air = C.smoothstep(edge + 10.0, edge - 6.0, yy)
    C.over(img, C.mix(BLUE_L, CYAN_L, 0.55), air * 0.92)                                  # the air: a pale cool sheet...
    C.over(img, C.mix(CYAN_L, PEARL, 0.6), air * C.smoothstep(edge - H * 0.42, edge, yy) * 0.85)   # ...brightest just above the skin
    C.over(img, PEARL, air * C.smoothstep(0.56, 0.74, rip) * C.smoothstep(edge - H * 0.30, edge, yy) * 0.55)
    under = C.smoothstep(H, edge, yy) * (1.0 - air)
    C.over(img, CYAN, under * C.smoothstep(0.50, 0.72, rip) * 0.42)                       # light breaking up below it
    C.over(img, BLUE, under * 0.24)
    C.over(img, PEARL, np.exp(-(((yy - edge) / 3.2) ** 2)) * 0.80)                        # the skin itself: one bright line
    return img


def fish(seed):
    """A shoal: a dozen small fish, all heading one way, each a leaf shape and a tail."""
    img = C.cells(3, 2)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for i in range(13):
        cx, cy = rng.uniform(0.10, 0.88) * W, rng.uniform(0.14, 0.86) * H
        ln = rng.uniform(15.0, 25.0)
        body = [(cx - ln * 0.5, cy), (cx - ln * 0.1, cy - ln * 0.24), (cx + ln * 0.5, cy), (cx - ln * 0.1, cy + ln * 0.24)]
        tail = [(cx - ln * 0.45, cy), (cx - ln * 0.82, cy - ln * 0.22), (cx - ln * 0.82, cy + ln * 0.22)]
        m = np.maximum(C.poly_mask(H, W, body), C.poly_mask(H, W, tail))
        C.over(img, C.mix(BLUE_L, CYAN_L, rng.uniform(0, 1)), m * rng.uniform(0.55, 0.85))
    return img


# -------------------------------------------------------------------- build


def build():
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.ln_reef", "Reef Rock", "Ground", 1, 1, "Ground", "ground_ln_reef", reef_atlas(4),
        "Seabed rock: faceted indigo stone veined with azurite blue, lit softly from above, fine ink cracks; "
        "its walkable top a crust of pale shell-sand with small corals; ragged shadowed underside.",
        fill=True, group="terrain.ln_reef", variants=4)
    add("ground.ln_palace", "Palace Wall", "Ground", 1, 1, "Ground", "ground_ln_palace", palace_atlas(4),
        "Palace masonry faced in glazed fish-scale tile, purple running to blue, each scale ink-lined and domed; "
        "a cyan key-fret coping under a pearl-white edge; a scalloped wave valance below.",
        fill=True, group="terrain.ln_palace", variants=4)

    add("platform.ln_eave", "Glazed Eave", "Platform", 5, 1, "Platform", "platform_ln_eave", eave(5, 61),
        "A palace eave in cyan glaze with a black ridge, ink-lined tile courses, swept-up tips and a night pearl "
        "hanging from each.", walk=10)
    add("platform.ln_shell", "Giant Clam", "Platform", 3, 1, "Platform", "platform_ln_shell", shell(3, 67),
        "The upper valve of a giant clam as a ledge: ribs fanning to the hinge, purple at the lip running to pearl.", walk=8)

    add("softvolume.ln_water", "Sea Water", "SoftVolume", 8, 6, "SoftVolume", "softvolume_ln_water", water(8, 6, 71),
        "Open sea water as a volume: a breath of blue with slow ribbons of caustic light. Tiles both ways. "
        "A buoyant, resisting medium: lower gravity and drag.")
    add("softvolume.ln_current", "Current", "SoftVolume", 4, 2, "SoftVolume", "softvolume_ln_current", current(4, 2, 73),
        "A hidden current: a few hair-thin pale streamlines with faint arrow-heads and drifting motes. "
        "It carries whatever enters it to the right (flip to reverse).")
    add("softvolume.ln_bubbles", "Bubble Column", "SoftVolume", 2, 4, "SoftVolume", "softvolume_ln_bubbles", bubbles(2, 4, 75),
        "A column of rising bubbles, each a fine ring with one highlight. Tiles vertically. It lifts whatever enters it.")
    add("softvolume.ln_kelp", "Kelp", "SoftVolume", 2, 6, "SoftVolume", "softvolume_ln_kelp", kelp(2, 6, 77),
        "Long kelp blades, ink-lined with a midrib, dark teal at the root running to cyan tips. Enterable cover.")

    add("hazard.ln_urchin", "Sea Urchins", "Hazard", 2, 1, "Hazard", "hazard_ln_urchin", urchins(81),
        "Three black sea urchins bristling with long spines tipped in cyan.")

    add("deco.ln_column", "Palace Column", "Decoration", 1, 5, "Decoration", "deco_ln_column", column(5, 91),
        "A palace column: graded purple shaft banded in cyan, cloud-scroll capital, lotus foot, ink-lined.")
    add("deco.ln_lantern", "Night Pearl", "Decoration", 1, 2, "Decoration", "deco_ln_lantern", lantern(95),
        "A glowing night pearl held in claws on a purple stand: the realm's only lamp.")
    add("deco.ln_coral", "Coral Tree", "Decoration", 3, 3, "Decoration", "deco_ln_coral", coral(3, 3, 101),
        "A forking coral tree, purple at the root running to pale cyan tips, ink-lined.")
    add("deco.ln_fish", "Shoal", "Decoration", 3, 2, "Decoration", "deco_ln_fish", fish(105),
        "A shoal of a dozen small pale-blue fish heading one way.")
    add("deco.ln_palace_far", "Far Palace", "Decoration", 10, 6, "Decoration", "deco_ln_palace_far", palace_far(10, 6, 111),
        "The far dragon palace seen through deep water: four tiers of swept roofs in flat blues, lit along each ridge, "
        "pearl windows, a pearl on its summit; its mound solid to the bottom.")
    add("deco.ln_ridge_far", "Seabed Ridges", "Decoration", 16, 5, "Decoration", "deco_ln_ridge_far", ridge_far(16, 5, 115),
        "Two ranges of seabed ridges as flat tones of the water's own blue. Tiles horizontally; solid below.")
    add("deco.ln_rays", "Light Shafts", "Decoration", 4, 10, "Decoration", "deco_ln_rays", rays(4, 10, 121),
        "Pale shafts of light slanting down from the surface, widening as they fall.")
    add("deco.ln_surface", "Sea Surface", "Decoration", 16, 5, "Decoration", "deco_ln_surface", surface(16, 5, 125),
        "The top of the sea seen from beneath: four rows of pale open air, then the sea's bright rippling skin on "
        "the fifth row, with light breaking up below it. Lay it so the skin is the first row of water. Tiles horizontally.")
    return out


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(12, 18, 48), sheet_cell=64)


if __name__ == "__main__":
    sheet = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
    export(sheet)

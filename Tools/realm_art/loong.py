#!/usr/bin/env python3
"""Cloud Court tile set — the Palace of Loong realm (PL).

Art language: cloisonné enamel (景泰蓝). Every shape is a cell of flat, saturated jewel
enamel — imperial purple, lapis, turquoise, cinnabar, jade, ivory — bounded by a raised
gold wire. There is no line that is not gold and no colour that is not enclosed. Motifs
are the court's: dragon scales, ruyi cloud scrolls, the running meander, lotus.
The dragon emperor's palace above the clouds. See `Docs/Art/RealmStyleBible.md`
§Cloud Court.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/loong.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "PalaceOfLoong"
SET = "pl"

GOLD = rgb("#E3B341")
NIGHT = rgb("#2A1B4A")
PURPLE = rgb("#4B2A7A")
LAPIS = rgb("#1E4FA3")
TURQ = rgb("#2CB5A8")
CINNABAR = rgb("#C5382C")
JADE = rgb("#74C095")
IVORY = rgb("#F4EAD4")
ROSE = rgb("#E9A3B8")
YELLOW = rgb("#EDBE3C")          # imperial glaze

BRIEF = ("Cloisonné-enamel game sprite on transparent ground: every shape is a cell of flat saturated "
         "jewel enamel (imperial purple, lapis blue, turquoise, cinnabar red, jade green, ivory) enclosed "
         "by a raised, gleaming gold wire; faint enamel mottling, no other outlines, no gradients across "
         "cells. Chinese court motifs: dragon scales, ruyi cloud scrolls, running meander, lotus. A "
         "celestial palace above the clouds. Orthographic side view, no cast shadow, no text.")


# ------------------------------------------------------------- cloisonné kit


def edge_of(mask, width=3.0):
    """The boundary of a mask as a soft band `width` px wide (where a wire is soldered)."""
    b = C.blur(mask, width * 0.9)
    return np.clip((0.5 - np.abs(b - 0.5)) * 4.2, 0.0, 1.0)


class Enamel:
    """Accumulates enamel cells and gold wires, then fires the piece in `finish`."""

    def __init__(self, h, w, seed=0):
        self.h, self.w, self.seed = h, w, seed
        self.color = np.zeros((h, w, 3), np.float32)
        self.alpha = np.zeros((h, w), np.float32)
        self.wire = np.zeros((h, w), np.float32)
        self._n = 0

    def cell(self, mask, color, wire=3.0, mottle=0.10):
        """Lay a cell of enamel and solder a wire round it (wire=0 for none)."""
        m = np.clip(mask, 0.0, 1.0)
        self._n += 1
        n = C.field(self.h, self.w, 22.0, self.seed + 31 * self._n, octaves=3)
        col = np.asarray(color, np.float32) * (1.0 - mottle + 2.0 * mottle * n[..., None])
        self.color = self.color * (1.0 - m[..., None]) + np.clip(col, 0, 1) * m[..., None]
        self.alpha = np.maximum(self.alpha, m)
        self.wire = self.wire * (1.0 - m * 0.999)            # a new cell covers wires beneath it
        if wire > 0:
            self.wire = np.maximum(self.wire, edge_of(m, wire))
        return m

    def line(self, pts, width=3.0):
        self.wire = np.maximum(self.wire, C.line_mask(self.h, self.w, pts, width))

    def add_wire(self, mask):
        self.wire = np.maximum(self.wire, np.clip(mask, 0.0, 1.0))

    def finish(self, glow=None):
        img = C.new(self.h, self.w)
        # enamel pools slightly darker against the wires and carries a few bright bubbles
        near = C.blur(self.wire, 4.0)
        col = self.color * (1.0 - 0.22 * near[..., None])
        bub = C.field(self.h, self.w, 2.0, self.seed + 5, octaves=1)
        col = np.clip(col + C.smoothstep(0.90, 0.97, bub)[..., None] * 0.16, 0, 1)
        img[..., :3] = col
        img[..., 3] = self.alpha
        # the wire is a raised ridge of gold: light it as relief so it gleams on one flank
        ridge = C.blur(self.wire, 0.9) * 4.5
        gold = C.lit(ridge, np.broadcast_to(GOLD, (self.h, self.w, 3)), light=(-0.5, -0.65, 0.57), ambient=0.62,
                     strength=2.4, spec=0.55, spec_pow=10.0)
        C.over(img, gold, np.clip(self.wire * 1.15, 0.0, 1.0))
        return img


def scales(h, w, s, x0=0.0, y0=0.0):
    """Dragon scales / wave pattern: rows of overlapping discs, upper rows on top.
    Returns (wire, t) where t is 0 at a scale's rim and 1 at its centre."""
    xx, yy = C.grid(h, w, x0, y0)
    wire = np.zeros((h, w), np.float32)
    t = np.zeros((h, w), np.float32)
    lo = int(math.floor(y0 / (s / 2.0))) - 3
    hi = int(math.ceil((y0 + h) / (s / 2.0))) + 3
    for i in range(hi, lo - 1, -1):                       # bottom row first, so upper rows overlap it
        cy = i * s / 2.0
        off = (i % 2) * s / 2.0
        dx = np.mod(xx - off + s / 2.0, s) - s / 2.0
        d = np.hypot(dx, yy - cy)
        inside = d < s / 2.0
        wire = np.where(inside, 0.0, wire)
        wire = np.maximum(wire, C.smoothstep(2.6, 1.0, np.abs(d - s / 2.0)) * (d < s / 2.0 + 2.6))
        t = np.where(inside, 1.0 - d / (s / 2.0), t)
    return wire, t


def meander(en, x0, x1, y, u=8.0, width=2.6):
    """A running key-fret (回纹) between x0 and x1, its baseline on y + 3u."""
    x = x0
    while x < x1:
        en.line([(x, y + 3 * u), (x, y), (x + 3 * u, y), (x + 3 * u, y + 2 * u), (x + u, y + 2 * u), (x + u, y + u), (x + 2 * u, y + u)], width)
        x += 4 * u
    en.line([(x0, y + 3 * u), (x1, y + 3 * u)], width)


def spiral(en, cx, cy, r, turns=1.5, a0=0.0, width=2.6, ccw=False):
    """A cloud scroll: an arc winding in on itself."""
    pts = []
    for k in range(28):
        f = k / 27.0
        ang = a0 + (-1 if ccw else 1) * f * turns * 2 * math.pi
        rad = r * (1.0 - 0.80 * f)
        pts.append((cx + math.cos(ang) * rad, cy + math.sin(ang) * rad))
    en.line(pts, width)


def ruyi_cloud(en, cx, cy, sc, lobes=None, colors=(IVORY, TURQ, LAPIS), tail=0, flat=True):
    """An auspicious cloud (祥云): scalloped lobes in three nested enamels, a scroll in each
    lobe, optionally trailing a tail (tail = -1 left, +1 right)."""
    h, w = en.h, en.w
    lobes = lobes or [(-1.45, 0.10, 0.74), (-0.55, -0.34, 0.98), (0.52, -0.22, 0.92), (1.40, 0.14, 0.70)]
    xx, yy = C.grid(h, w)
    for band, (col, shrink) in enumerate(zip(colors, (1.0, 0.66, 0.34))):
        m = np.zeros((h, w), np.float32)
        for (dx, dy, r) in lobes:
            m = np.maximum(m, C.aa(C.sdf_circle(h, w, cx + dx * sc, cy + dy * sc, r * sc * shrink), 1.4))
        if band == 0 and tail:
            m = np.maximum(m, C.poly_mask(h, w, [(cx + tail * 1.2 * sc, cy + 0.10 * sc), (cx + tail * 3.3 * sc, cy + 0.42 * sc),
                                                 (cx + tail * 1.2 * sc, cy + 0.62 * sc)]))
        if flat:
            m = m * C.smoothstep(cy + 0.64 * sc, cy + 0.60 * sc, yy)
        en.cell(m, col)
    for (dx, dy, r) in lobes:
        spiral(en, cx + dx * sc, cy + dy * sc, r * sc * 0.22, turns=1.1, a0=math.pi * 0.9, width=2.2)


def finish_glow(img, cx, cy, sigma, color, amount):
    """Lay a soft halo BEHIND a finished enamel piece."""
    halo = C.new(*img.shape[:2])
    C.radial_glow(halo, cx, cy, sigma, color, amount)
    C.over_img(halo, img)
    return halo


# -------------------------------------------------------------------- ground


def terrace_atlas(variants=4):
    """`ground.pl_terrace` — the palace's own fabric: purple enamel in gold dragon scales,
    an ivory key-fret coping over a cinnabar band on top, a jade pilaster where it ends,
    and a hanging turquoise ruyi valance underneath."""
    Wn = variants * CELL
    S = 32.0
    wire, t = scales(CELL, Wn, S)
    n = C.field(CELL, Wn, 26.0, 801, octaves=3, period_px=(Wn, CELL))
    body = C.mix(rgb("#3A1F66"), rgb("#5C3594"), np.clip(t * 0.85 + (n - 0.5) * 0.3, 0, 1))

    def render(g, layer, open_left, open_right):
        en = Enamel(CELL, CELL, 810 + g * 7 + layer)
        en.color[...] = body[:, g * CELL:(g + 1) * CELL]
        en.alpha[...] = 1.0
        en.wire[...] = wire[:, g * CELL:(g + 1) * CELL]
        xx, yy = C.grid(CELL, CELL)
        if layer == 3:                                    # hanging ruyi valance
            lobe = np.hypot(np.mod(xx, S) - S / 2.0, yy - (CELL - 30.0))
            band = (yy > CELL - 46.0).astype(np.float32)
            en.cell(band * ((yy < CELL - 30.0) | (lobe < S / 2.0 - 1.0)), TURQ)
            en.alpha = en.alpha * ((yy < CELL - 30.0) | (lobe < S / 2.0 - 1.0))
            en.wire = np.maximum(en.wire, C.smoothstep(2.4, 0.8, np.abs(lobe - (S / 2.0 - 2.0))) * (yy >= CELL - 31.0))
            en.wire = np.maximum(en.wire, C.smoothstep(2.2, 0.8, np.abs(yy - (CELL - 46.0))))
            dot = np.hypot(np.mod(xx, S) - S / 2.0, yy - (CELL - 28.0))
            en.cell(C.smoothstep(6.0, 4.6, dot), ROSE, wire=2.0)
        for is_open, left in ((open_left, True), (open_right, False)):
            if not is_open:
                continue
            dist = xx if left else CELL - xx
            en.cell((dist < 14.0).astype(np.float32) * en.alpha, JADE)
            en.wire = np.maximum(en.wire, C.smoothstep(2.2, 0.8, np.abs(dist - 2.0)) * en.alpha)
        if layer == 0:                                    # coping: ivory key-fret over a cinnabar band
            en.cell((yy < 40.0).astype(np.float32), CINNABAR, wire=0)
            en.cell((yy < 31.0).astype(np.float32), IVORY, wire=0)
            for y in (2.0, 31.0, 40.0):
                en.wire = np.maximum(en.wire, C.smoothstep(2.3, 0.8, np.abs(yy - y)))
            meander(en, 0.0, float(CELL), 5.0, u=7.0, width=2.3)
        return en.finish()

    return C.build_hill_atlas(render, variants)


# ------------------------------------------------------------------ platforms


def bridge(seed):
    """Moon bridge (4x1): an ivory deck on a lapis arch scrolled with cloud wire."""
    H, W = CELL, 4 * CELL
    en = Enamel(H, W, seed)
    xx, yy = C.grid(H, W)
    arch = 34.0 + 74.0 * (1.0 - np.sin(np.clip(xx / W, 0, 1) * math.pi) ** 0.8)
    body = ((yy >= 0) & (yy < arch)).astype(np.float32)
    en.cell(C.blur(body, 0.8), LAPIS)
    for cx0 in np.linspace(W * 0.10, W * 0.90, 9):
        spiral(en, cx0, 40.0, 11.0, turns=1.2, a0=math.pi, width=2.2)
    en.cell((yy < 26.0).astype(np.float32), CINNABAR, wire=0)
    en.cell((yy < 18.0).astype(np.float32), IVORY, wire=0)
    for y in (2.0, 18.0, 26.0):
        en.add_wire(C.smoothstep(2.3, 0.8, np.abs(yy - y)) * (yy < arch + 2))
    for x in np.arange(16.0, W, 32.0):
        en.add_wire(C.smoothstep(2.0, 0.8, np.abs(xx - x)) * (yy > 2.0) * (yy < 18.0))
    return en.finish()


def lotus(seed):
    """Lotus (2x1): a jade pad to stand on, rose petals unfolding beneath it."""
    H, W = CELL, 2 * CELL
    en = Enamel(H, W, seed)
    cx0 = W / 2.0
    stem = C.rect_mask(H, W, cx0 - 6.0, 60.0, cx0 + 6.0, H)
    en.cell(stem, JADE)
    for i, (ang, ln, col) in enumerate([(-66, 92, ROSE), (66, 92, ROSE), (-36, 86, rgb("#F3C3D0")), (36, 86, rgb("#F3C3D0")), (0, 80, IVORY)]):
        a = math.radians(90 + ang)
        tip = (cx0 + math.cos(a) * ln, 22.0 + math.sin(a) * ln * 0.9)
        perp = (-math.sin(a), math.cos(a))
        petal = C.poly_mask(H, W, [C.bezier((cx0, 24.0), (cx0 + math.cos(a) * ln * 0.5 + perp[0] * s * 26.0, 24.0 + math.sin(a) * ln * 0.45 + perp[1] * s * 26.0), tip, n=8)[k]
                                   for s in (1, -1) for k in (range(9) if s == 1 else range(8, -1, -1))])
        en.cell(petal, col)
        en.line([(cx0, 26.0), tip], 1.8)
    xx, yy = C.grid(H, W)
    pad = C.aa(C.sdf_box(H, W, cx0, 13.0, W / 2.0 - 3.0, 12.0, radius=11.0), 1.4)
    en.cell(pad, JADE)
    for x in np.linspace(cx0 - W * 0.36, cx0 + W * 0.36, 7):
        en.line([(cx0, 23.0), (x, 3.0)], 1.8)
    en.add_wire(C.smoothstep(2.2, 0.8, np.abs(yy - 2.0)) * pad)
    return en.finish()


def roof(seed):
    """Palace roof (5x1): imperial-yellow glazed tiles, an ivory ridge, a cinnabar fascia
    studded in jade, and gilded eave tips."""
    H, W = CELL, 5 * CELL
    en = Enamel(H, W, seed)
    xx, yy = C.grid(H, W)
    u = xx / W
    d = np.abs(u - 0.5) * 2.0
    top = 2.0 + 20.0 * np.clip((d - 0.80) / 0.20, 0, 1) ** 2 * 0.0
    eave = H - 26.0 - 44.0 * np.clip((d - 0.55) / 0.45, 0, 1) ** 2.0
    body = ((yy >= top) & (yy < eave) & (d < 0.995)).astype(np.float32)
    body = C.blur(body, 0.8)
    en.cell(body, YELLOW)
    en.add_wire(C.smoothstep(2.0, 0.8, np.abs(np.mod(xx + (yy * 0.0), 22.0) - 11.0)) * body * (yy > 20.0))
    for y in (44.0, 68.0):
        en.add_wire(C.smoothstep(2.0, 0.8, np.abs(yy - y)) * body)
    en.cell(((yy < 20.0) & (d < 0.90)).astype(np.float32), IVORY)
    for x in np.arange(W * 0.05 + 20.0, W * 0.95, 40.0):
        en.cell(C.aa(C.sdf_circle(H, W, x, 10.0, 5.0), 1.3), LAPIS, wire=2.0)
    fascia = ((yy >= eave) & (yy < eave + 18.0) & (d < 0.93)).astype(np.float32)
    en.cell(C.blur(fascia, 0.7), CINNABAR)
    for x in np.arange(W * 0.06, W * 0.94, 26.0):
        ey = float(np.interp(x, np.arange(W) + 0.5, eave[0])) + 9.0
        en.cell(C.aa(C.sdf_circle(H, W, x, ey, 4.6), 1.3), JADE, wire=1.8)
    for s in (-1, 1):                                      # gilded upturned tips
        tx = W / 2.0 + s * W * 0.485
        tip = C.poly_mask(H, W, [(tx - s * 46.0, 40.0), (tx, 2.0), (tx - s * 6.0, 46.0), (tx - s * 40.0, 62.0)])
        en.cell(tip, GOLD * 0.92, wire=2.2)
    return en.finish()


# ---------------------------------------------------------------- soft volumes


def cloud(wc, hc, seed, sc, tail=0):
    H, W = hc * CELL, wc * CELL
    en = Enamel(H, W, seed)
    ruyi_cloud(en, W / 2.0 - tail * sc * 0.5, H * 0.50, sc, tail=tail)
    return en.finish()


# --------------------------------------------------------------------- hazard


def frost_pearl(seed):
    """Frost pearl (2x1 hazard): the dragon's cold pearl on a gilt stand, rimed with ice."""
    H, W = CELL, 2 * CELL
    en = Enamel(H, W, seed)
    cx0, cy0, r = W / 2.0, 54.0, 34.0
    stand = C.poly_mask(H, W, [(cx0 - 52.0, H - 2.0), (cx0 + 52.0, H - 2.0), (cx0 + 30.0, H - 26.0), (cx0 - 30.0, H - 26.0)])
    en.cell(stand, CINNABAR)
    en.cell(C.rect_mask(H, W, cx0 - 38.0, H - 38.0, cx0 + 38.0, H - 26.0, radius=5.0), GOLD * 0.9)
    for k in range(8):                                     # frost needles
        a = k / 8.0 * 2 * math.pi + 0.2
        p0 = (cx0 + math.cos(a) * (r + 3.0), cy0 + math.sin(a) * (r + 3.0))
        p1 = (cx0 + math.cos(a) * (r + 24.0), cy0 + math.sin(a) * (r + 24.0))
        if p1[1] < H - 30.0:
            needle = C.poly_mask(H, W, [(p0[0] - math.sin(a) * 6.0, p0[1] + math.cos(a) * 6.0), p1, (p0[0] + math.sin(a) * 6.0, p0[1] - math.cos(a) * 6.0)])
            en.cell(needle, rgb("#CFF3FA"), wire=2.0)
    en.cell(C.aa(C.sdf_circle(H, W, cx0, cy0, r), 1.4), rgb("#7FD6EC"))
    en.cell(C.aa(C.sdf_circle(H, W, cx0 - 8.0, cy0 - 8.0, r * 0.62), 1.4), rgb("#BDEFF8"), wire=2.0)
    en.cell(C.aa(C.sdf_circle(H, W, cx0 - 14.0, cy0 - 14.0, r * 0.26), 1.3), rgb("#FFFFFF"), wire=0)
    return finish_glow(en.finish(), cx0, cy0, 40.0, rgb("#8FE6F7"), 0.7)


# ----------------------------------------------------------------- decoration


def dragon_column(seed):
    """Dragon column (1x5): a cinnabar shaft wound by a scaled jade dragon, cloud capital
    above, lotus plinth below."""
    H, W = 5 * CELL, CELL
    en = Enamel(H, W, seed)
    xx, yy = C.grid(H, W)
    cx0 = W / 2.0
    shaft = C.rect_mask(H, W, cx0 - 26.0, 70.0, cx0 + 26.0, H - 60.0)
    en.cell(shaft, CINNABAR)
    # the dragon: a band spiralling down the shaft, filled with scales
    phase = (yy - 70.0) / 150.0 * 2 * math.pi
    band_c = cx0 + np.sin(phase) * 22.0
    front = np.cos(phase) > -0.25
    band = (np.abs(xx - band_c) < 15.0) & front & (yy > 84.0) & (yy < H - 74.0)
    wire, t = scales(H, W, 18.0)
    bm = C.blur(band.astype(np.float32), 0.8)
    en.cell(bm, JADE)
    en.add_wire(wire * bm)
    head = C.poly_mask(H, W, [(cx0 - 34.0, 96.0), (cx0 + 6.0, 78.0), (cx0 + 40.0, 92.0), (cx0 + 30.0, 118.0), (cx0 - 4.0, 124.0)])
    en.cell(head, JADE)
    en.cell(C.aa(C.sdf_circle(H, W, cx0 + 12.0, 98.0, 5.0), 1.3), IVORY, wire=1.8)
    for s in (0, 1):
        en.line(C.bezier((cx0 - 28.0 + s * 10, 92.0 - s * 4), (cx0 - 46.0 + s * 8, 70.0), (cx0 - 30.0 + s * 14, 56.0), n=8), 2.4)
    for y0, y1, hw_, col in ((4.0, 30.0, 52.0, IVORY), (30.0, 52.0, 40.0, TURQ), (52.0, 72.0, 30.0, LAPIS)):
        en.cell(C.rect_mask(H, W, cx0 - hw_, y0, cx0 + hw_, y1, radius=8.0), col)
    spiral(en, cx0 - 24.0, 17.0, 9.0, a0=0.0, width=2.0)
    spiral(en, cx0 + 24.0, 17.0, 9.0, a0=math.pi, width=2.0, ccw=True)
    for k, (y0, hw_, col) in enumerate(((H - 62.0, 34.0, JADE), (H - 42.0, 44.0, ROSE), (H - 22.0, 54.0, IVORY))):
        en.cell(C.rect_mask(H, W, cx0 - hw_, y0, cx0 + hw_, y0 + 20.0, radius=7.0), col)
    return en.finish()


def moon_gate(seed):
    """Moon gate (4x4): an ivory wall pierced by a perfect circle, fretted and cornered."""
    H = W = 4 * CELL
    en = Enamel(H, W, seed)
    xx, yy = C.grid(H, W)
    wall = C.rect_mask(H, W, 4.0, 4.0, W - 4.0, H - 4.0, radius=6.0)
    en.cell(wall, IVORY)
    r = np.hypot(xx - W / 2.0, yy - H / 2.0)
    for cx0, cy0 in ((4.0, 4.0), (W - 4.0, 4.0), (4.0, H - 4.0), (W - 4.0, H - 4.0)):
        d = np.hypot(xx - cx0, yy - cy0)
        en.cell(((d < 104.0) & (wall > 0.5)).astype(np.float32), CINNABAR)
        en.cell(((d < 62.0) & (wall > 0.5)).astype(np.float32), LAPIS)
    ring = ((r < W * 0.40) & (r >= W * 0.33)).astype(np.float32)
    en.cell(C.blur(ring, 0.8), TURQ)
    for k in range(24):
        a = k / 24.0 * 2 * math.pi
        en.line([(W / 2.0 + math.cos(a) * W * 0.335, H / 2.0 + math.sin(a) * W * 0.335),
                 (W / 2.0 + math.cos(a) * W * 0.395, H / 2.0 + math.sin(a) * W * 0.395)], 2.0)
    meander(en, 120.0, W - 120.0, 12.0, u=7.0, width=2.2)
    meander(en, 120.0, W - 120.0, H - 36.0, u=7.0, width=2.2)
    img = en.finish()
    hole = C.smoothstep(W * 0.33 + 1.0, W * 0.33 - 1.0, r)
    img[..., 3] *= 1.0 - hole
    return img


def palace_far(seed):
    """Far palace (10x6): three halls stacked on a cloud bank, enamels dimmed by distance."""
    H, W = 6 * CELL, 10 * CELL
    en = Enamel(H, W, seed)
    xx, yy = C.grid(H, W)
    cx0 = W / 2.0
    far = lambda c: C.mix(c, NIGHT, 0.40)
    for (y_roof, half, wall_h) in ((520.0, 520.0, 96.0), (350.0, 380.0, 86.0), (196.0, 250.0, 78.0)):
        wall = C.rect_mask(H, W, cx0 - half * 0.80, y_roof + 40.0, cx0 + half * 0.80, y_roof + 40.0 + wall_h)
        en.cell(wall, far(CINNABAR), wire=2.4)
        for x in np.arange(cx0 - half * 0.78 + 26.0, cx0 + half * 0.78, 52.0):
            en.cell(C.rect_mask(H, W, x - 13.0, y_roof + 56.0, x + 13.0, y_roof + 40.0 + wall_h - 6.0, radius=3.0), far(LAPIS), wire=2.0)
        u = np.clip((xx - (cx0 - half)) / (2 * half), 0, 1)
        d = np.abs(u - 0.5) * 2.0
        top = y_roof - 46.0 + 46.0 * d ** 1.5
        eave = y_roof + 44.0 - 52.0 * np.clip((d - 0.5) / 0.5, 0, 1) ** 2
        rf = ((yy >= top) & (yy < eave) & (np.abs(xx - cx0) < half)).astype(np.float32)
        en.cell(C.blur(rf, 0.8), far(YELLOW), wire=2.6)
        en.add_wire(C.smoothstep(2.0, 0.8, np.abs(np.mod(xx, 30.0) - 15.0)) * rf * 0.8)
    for cxk, sc in ((W * 0.16, 62.0), (W * 0.42, 74.0), (W * 0.66, 70.0), (W * 0.88, 60.0)):
        ruyi_cloud(en, cxk, H - 66.0, sc, colors=(far(IVORY), far(TURQ), far(LAPIS)))
    return en.finish()


def cloud_sea(seed):
    """Sea of clouds (16x4): overlapping wave-scales in ivory, turquoise and lapis — the
    floor of heaven. Scalloped along the top, solid below, wraps in x."""
    H, W = 4 * CELL, 16 * CELL
    S = 128.0
    xx, yy = C.grid(H, W)
    wire, t = scales(H, W, S, y0=-40.0)
    col = np.where((t > 0.62)[..., None], LAPIS, np.where((t > 0.30)[..., None], TURQ, IVORY))
    rings = np.maximum(C.smoothstep(0.022, 0.006, np.abs(t - 0.30)), C.smoothstep(0.022, 0.006, np.abs(t - 0.62)))
    en = Enamel(H, W, seed)
    n = C.field(H, W, 30.0, seed, octaves=3, period_px=(W, 0))
    en.color[...] = np.clip(col * (0.90 + 0.2 * n[..., None]), 0, 1)
    # the first row of scales is the skyline: keep only what lies inside some scale
    en.alpha[...] = (t > 0.0) | (yy > S * 0.5)
    en.wire[...] = np.maximum(wire, rings) * en.alpha
    return en.finish()


def lantern(seed):
    """Palace lantern (1x2): a cinnabar globe ribbed in gold, with a jade cap and tassel."""
    H, W = 2 * CELL, CELL
    en = Enamel(H, W, seed)
    cx0 = W / 2.0
    en.line([(cx0, 0.0), (cx0, 40.0)], 2.6)
    en.cell(C.rect_mask(H, W, cx0 - 22.0, 36.0, cx0 + 22.0, 54.0, radius=5.0), JADE)
    xx, yy = C.grid(H, W)
    globe = (((xx - cx0) / 44.0) ** 2 + ((yy - 104.0) / 54.0) ** 2 < 1.0).astype(np.float32)
    en.cell(C.blur(globe, 0.8), CINNABAR)
    for k in (-2, -1, 0, 1, 2):
        ys = np.linspace(52.0, 156.0, 18)
        en.line([(cx0 + k * 18.0 * math.sqrt(max(0.0, 1.0 - ((y - 104.0) / 54.0) ** 2)), y) for y in ys], 2.0)
    en.cell(C.rect_mask(H, W, cx0 - 18.0, 152.0, cx0 + 18.0, 168.0, radius=5.0), JADE)
    for k in (-1, 0, 1):
        en.cell(C.poly_mask(H, W, [(cx0 + k * 10.0 - 4.0, 168.0), (cx0 + k * 10.0 + 4.0, 168.0), (cx0 + k * 12.0, 236.0)]), YELLOW, wire=1.8)
    return finish_glow(en.finish(), cx0, 104.0, 42.0, rgb("#FF9A5C"), 0.45)


def pearl_moon(seed):
    """The Night Pearl (4x4): heaven's moon — an ivory disc of turquoise seas in a haloed gold ring."""
    H = W = 4 * CELL
    en = Enamel(H, W, seed)
    cx0, cy0 = W / 2.0, H / 2.0
    en.cell(C.aa(C.sdf_circle(H, W, cx0, cy0, W * 0.30), 1.5), IVORY, wire=4.0)
    for (dx, dy, r) in ((-46.0, -30.0, 34.0), (38.0, -52.0, 22.0), (30.0, 40.0, 42.0), (-52.0, 54.0, 20.0)):
        en.cell(C.aa(C.sdf_circle(H, W, cx0 + dx, cy0 + dy, r), 1.4), rgb("#CDEDE8"), wire=2.2)
    xx, yy = C.grid(H, W)
    r = np.hypot(xx - cx0, yy - cy0)
    en.add_wire(C.smoothstep(2.4, 0.8, np.abs(r - W * 0.37)))
    for k in range(16):
        a = k / 16.0 * 2 * math.pi
        en.line([(cx0 + math.cos(a) * W * 0.39, cy0 + math.sin(a) * W * 0.39),
                 (cx0 + math.cos(a) * W * (0.44 if k % 2 == 0 else 0.415), cy0 + math.sin(a) * W * (0.44 if k % 2 == 0 else 0.415))], 2.4)
    return finish_glow(en.finish(), cx0, cy0, W * 0.21, rgb("#C9B6FF"), 0.55)


def sky(seed):
    """Heaven's night (8x10): imperial purple warming to rose near the cloud sea, sown with
    gold dust. Transparent at the top so the canvas shows; wraps in x."""
    img = C.cells(8, 10)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    t = yy / H
    img[..., :3] = C.ramp(t, [(0.0, NIGHT), (0.45, rgb("#5A2E86")), (0.74, rgb("#A2478F")), (0.90, rgb("#E08A9C")), (1.0, rgb("#F3C3B8"))])
    img[..., 3] = C.smoothstep(0.0, 0.18, t)
    rng = np.random.default_rng(seed)
    for _ in range(170):
        sx, sy = rng.uniform(0, W), rng.uniform(0.02, 0.86) * H
        r0 = rng.uniform(0.8, 2.4)
        for ox in (-W, 0, W):
            C.over(img, GOLD if rng.random() < 0.75 else IVORY, C.aa(C.sdf_circle(H, W, sx + ox, sy, r0), 1.2) * rng.uniform(0.5, 1.0))
    return img


def banner(seed):
    """Court banner (1x3): lapis silk with a gold dragon roundel, cinnabar border, tassels."""
    H, W = 3 * CELL, CELL
    en = Enamel(H, W, seed)
    cx0 = W / 2.0
    en.cell(C.rect_mask(H, W, 6.0, 4.0, W - 6.0, 20.0, radius=6.0), GOLD * 0.9)
    silk = C.poly_mask(H, W, [(20.0, 18.0), (W - 20.0, 18.0), (W - 20.0, H - 70.0), (cx0, H - 34.0), (20.0, H - 70.0)])
    en.cell(silk, CINNABAR)
    inner = C.poly_mask(H, W, [(30.0, 28.0), (W - 30.0, 28.0), (W - 30.0, H - 78.0), (cx0, H - 50.0), (30.0, H - 78.0)])
    en.cell(inner, LAPIS)
    en.cell(C.aa(C.sdf_circle(H, W, cx0, 122.0, 30.0), 1.4), PURPLE)
    spiral(en, cx0, 122.0, 22.0, turns=1.6, a0=-math.pi / 2, width=2.6)
    for y in (60.0, 190.0, 240.0):
        spiral(en, cx0 - 12.0, y, 8.0, a0=0.0, width=2.0)
        spiral(en, cx0 + 12.0, y, 8.0, a0=math.pi, width=2.0, ccw=True)
    for k in (-1, 0, 1):
        en.cell(C.poly_mask(H, W, [(cx0 + k * 22.0 - 4.0, H - 60.0 + abs(k) * -8.0), (cx0 + k * 22.0 + 4.0, H - 60.0 + abs(k) * -8.0), (cx0 + k * 24.0, H - 6.0)]), YELLOW, wire=1.8)
    return en.finish()


# ---------------------------------------------------------------------- build


def build():
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.pl_terrace", "Palace Terrace", "Ground", 1, 1, "Ground", "ground_pl_terrace", terrace_atlas(4),
        "Palace terrace in cloisonné: purple enamel in gold dragon scales, an ivory key-fret coping over a cinnabar band on "
        "top, a jade pilaster where it ends, a hanging turquoise ruyi valance beneath.", fill=True, group="terrain.pl_terrace", variants=4)

    add("platform.pl_bridge", "Moon Bridge", "Platform", 4, 1, "Platform", "platform_pl_bridge", bridge(821),
        "A moon bridge: ivory deck over a cinnabar band on a lapis arch scrolled with gold cloud wire.", walk=4)
    add("platform.pl_lotus", "Lotus Pad", "Platform", 2, 1, "Platform", "platform_pl_lotus", lotus(825),
        "A jade lotus pad to stand on with rose and ivory petals unfolding beneath it.", walk=4)
    add("platform.pl_roof", "Palace Roof", "Platform", 5, 1, "Platform", "platform_pl_roof", roof(829),
        "A palace roof of imperial-yellow glazed tiles with an ivory ridge, jade-studded cinnabar fascia and gilded eave tips.", walk=4)

    add("softvolume.pl_cloud", "Auspicious Cloud", "SoftVolume", 3, 2, "SoftVolume", "softvolume_pl_cloud", cloud(3, 2, 833, 62.0),
        "An auspicious ruyi cloud: scalloped lobes in nested ivory, turquoise and lapis enamel, a gold scroll in each. Enterable.")
    add("softvolume.pl_cloud_trail", "Trailing Cloud", "SoftVolume", 4, 2, "SoftVolume", "softvolume_pl_cloud_trail", cloud(4, 2, 837, 60.0, tail=1),
        "A ruyi cloud drawing a long tail behind it. Enterable.")
    add("softvolume.pl_cloud_small", "Cloud Wisp", "SoftVolume", 2, 1, "SoftVolume", "softvolume_pl_cloud_small", cloud(2, 1, 841, 36.0),
        "A small ruyi cloud wisp. Enterable.")

    add("hazard.pl_frost_pearl", "Frost Pearl", "Hazard", 2, 1, "Hazard", "hazard_pl_frost_pearl", frost_pearl(845),
        "The dragon's cold pearl: a pale cyan orb rimed with ice needles on a gilt cinnabar stand, haloed in frost.")

    add("deco.pl_dragon_column", "Dragon Column", "Decoration", 1, 5, "Decoration", "deco_pl_dragon_column", dragon_column(849),
        "A cinnabar column wound by a scaled jade dragon, cloud capital above, lotus plinth below.")
    add("deco.pl_moon_gate", "Moon Gate", "Decoration", 4, 4, "Decoration", "deco_pl_moon_gate", moon_gate(853),
        "An ivory wall pierced by a perfect circular gate, ringed in turquoise, fretted, with cinnabar-and-lapis corners.")
    add("deco.pl_palace_far", "Far Palace", "Decoration", 10, 6, "Decoration", "deco_pl_palace_far", palace_far(857),
        "A distant palace of three stacked halls with sweeping yellow roofs on a cloud bank, enamels dimmed by distance.")
    add("deco.pl_cloud_sea", "Sea of Clouds", "Decoration", 16, 4, "Decoration", "deco_pl_cloud_sea", cloud_sea(861),
        "A sea of clouds as overlapping wave-scales in ivory, turquoise and lapis. Solid below; tiles horizontally.")
    add("deco.pl_lantern", "Palace Lantern", "Decoration", 1, 2, "Decoration", "deco_pl_lantern", lantern(865),
        "A cinnabar palace lantern ribbed in gold with a jade cap and yellow tassels, softly glowing.")
    add("deco.pl_moon", "Night Pearl", "Decoration", 4, 4, "Decoration", "deco_pl_moon", pearl_moon(869),
        "Heaven's moon as a great pearl: an ivory disc of pale turquoise seas in a rayed gold ring.")
    add("deco.pl_sky", "Heaven's Night", "Decoration", 8, 10, "Decoration", "deco_pl_sky", sky(873),
        "A night sky from imperial purple down to rose, sown with gold dust. Tiles horizontally.")
    add("deco.pl_banner", "Court Banner", "Decoration", 1, 3, "Decoration", "deco_pl_banner", banner(877),
        "A lapis silk court banner with a gold dragon-scroll roundel, cinnabar border and yellow tassels.")
    return out


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(42, 27, 74), sheet_cell=56)


if __name__ == "__main__":
    export(sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None)

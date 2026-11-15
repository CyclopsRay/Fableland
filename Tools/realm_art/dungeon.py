#!/usr/bin/env python3
"""Dungeon tile set — the HollowCastle realm (HC).

Art language: lit realism. Every surface is a height field shaded by two lights — a
dim, cold key from the upper left and a hot orange bounce from the lava below — so
stone has weight, chamfers catch light and undersides glow. Dark, desaturated, crowded;
the only saturated colour in the frame is fire. See `Docs/Art/RealmStyleBible.md`
§Dungeon.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/dungeon.py [--sheet out.png]
"""

from __future__ import annotations

import math
import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "HollowCastle"
SET = "hc"

KEY = (-0.45, -0.62, 0.64)                       # cold key light, upper left
LAVA = rgb("#FF6A1E")
RIM = (LAVA, (0.0, 0.86, 0.42), 0.50)            # hot bounce from below
TINT = (0.90, 0.96, 1.07)
AMBIENT = 0.24
AIR = -16.0                                      # height of "nothing" beside a solid

FIRE = [(0.0, "#160502"), (0.22, "#5A0E04"), (0.42, "#C22A06"), (0.62, "#F26A0E"), (0.82, "#FFB02E"), (1.0, "#FFF2B0")]

BRIEF = ("Realistic, physically lit dark-fantasy dungeon game sprite on transparent ground: weighty "
         "stone, iron and rotting timber shaded as real relief by a dim cold key light from the upper "
         "left and a hot orange lava bounce from below; desaturated, damp, worn, crowded with detail; "
         "fire is the only saturated colour. Orthographic side view, no cast shadow, no text.")


def render(rel, **kw):
    return rel.render(light=KEY, ambient=AMBIENT, strength=kw.pop("strength", 1.5), ao=kw.pop("ao", 0.055),
                      ao_radius=kw.pop("ao_radius", 7.0), rim=RIM, tint=TINT)


# ------------------------------------------------------------------ materials


def stone_alb(h, w, seed, c0="#4E525A", c1="#7A8089", scale=24.0):
    n = C.field(h, w, scale, seed, octaves=4)
    sp = C.field(h, w, 2.6, seed + 1, octaves=2)
    return np.clip(C.mix(rgb(c0), rgb(c1), n) * (0.88 + 0.24 * sp[..., None]), 0, 1)


def iron_alb(h, w, seed):
    n = C.field(h, w, 16.0, seed, octaves=4)
    rust = C.smoothstep(0.52, 0.74, C.field(h, w, 11.0, seed + 2, octaves=4))
    base = C.mix(rgb("#262A31"), rgb("#454B55"), n)
    return np.clip(C.mix(base, rgb("#6B3A22") * (0.75 + 0.5 * n[..., None]), rust * 0.8), 0, 1)


def wood_alb(h, w, seed, horizontal=True):
    grain = C.field(h, w, 4.5, seed, octaves=3, stretch=(16.0, 1.0) if horizontal else (1.0, 16.0))
    n = C.field(h, w, 34.0, seed + 1, octaves=3)
    return np.clip(C.mix(rgb("#281D15"), rgb("#5C4431"), grain * 0.7 + n * 0.3), 0, 1)


# -------------------------------------------------------------------- ground


def brick_strip(variants, seed, bw=CELL, bh=CELL // 2, mortar=3.6):
    """Height + albedo for a running-bond wall, periodic over the strip and one cell tall."""
    Wn = variants * CELL
    per = (Wn, CELL)
    xx, yy = C.strip(variants)
    course = np.floor(yy / bh).astype(np.int64)
    off = np.where(course % 2 == 0, 0.0, bw / 2.0)
    u = np.mod(xx + off, bw)
    v = np.mod(yy, bh)
    bx = np.mod(np.floor((xx + off) / bw).astype(np.int64), variants)
    r1 = C._hash01(bx + 3, course + 5, seed)
    r2 = C._hash01(bx + 11, course + 7, seed + 1)
    chip = C.field(CELL, Wn, 9.0, seed + 3, octaves=3, period_px=per)
    d = np.minimum(np.minimum(u, bw - u), np.minimum(v, bh - v)) - mortar - (chip - 0.5) * 5.5
    face = np.clip(d / 6.5, 0.0, 1.0)
    rough = C.field(CELL, Wn, 5.0, seed + 4, octaves=3, period_px=per)
    med = C.field(CELL, Wn, 26.0, seed + 5, octaves=3, period_px=per)
    height = face * (7.5 + 4.5 * r1) + (rough - 0.5) * 2.4 * face + (med - 0.5) * 4.5 * face
    cr = C.field(CELL, Wn, 34.0, seed + 6, octaves=4, period_px=per)
    crack = C.smoothstep(0.016, 0.0, np.abs(cr - 0.5)) * (r2 > 0.72) * face
    height -= crack * 4.5
    tone = C.mix(rgb("#4F535B"), rgb("#7E848D"), r1)
    albedo = tone * (0.78 + 0.44 * med[..., None]) * (0.90 + 0.2 * rough[..., None])
    albedo = C.mix(rgb("#131417"), albedo, face)
    albedo = albedo * (1.0 - 0.6 * crack[..., None])
    damp = C.field(CELL, Wn, 13.0, seed + 7, octaves=3, period_px=per, stretch=(1.0, 5.0))
    albedo = albedo * (1.0 - 0.28 * C.smoothstep(0.52, 0.80, damp)[..., None])
    moss = C.smoothstep(0.62, 0.80, C.field(CELL, Wn, 20.0, seed + 8, octaves=4, period_px=per)) * (1.0 - face) * 0.8
    albedo = C.mix(albedo, rgb("#3C5230"), moss)
    lower = (course == 1)
    gone = lower & (C._hash01(bx + 23, course + 1, seed + 9) > 0.42)      # bricks fallen from an underside
    return height.astype(np.float32), np.clip(albedo, 0, 1).astype(np.float32), gone.astype(np.float32)


def rock_strip(variants, seed):
    """Height + albedo for raw cavern basalt: ridged, faceted, fissured."""
    Wn = variants * CELL
    per = (Wn, CELL)
    n1 = C.field(CELL, Wn, 62.0, seed, octaves=4, period_px=per)
    n2 = C.field(CELL, Wn, 19.0, seed + 1, octaves=4, period_px=per)
    n3 = C.field(CELL, Wn, 5.5, seed + 2, octaves=3, period_px=per)
    ridged = 1.0 - np.abs(2.0 * n1 - 1.0)
    height = ridged * 18.0 + n2 * 7.0 + n3 * 1.4
    height = np.floor(height / 4.5) * 4.5 * 0.55 + height * 0.45            # facet the surface
    cr = C.field(CELL, Wn, 44.0, seed + 3, octaves=3, period_px=per)
    crack = C.smoothstep(0.014, 0.0, np.abs(cr - 0.5))
    height -= crack * 5.0
    albedo = C.mix(rgb("#25252A"), rgb("#4A4B52"), n1 * 0.6 + n2 * 0.4) * (0.90 + 0.20 * n3[..., None])
    albedo = albedo * (1.0 - 0.55 * crack[..., None])
    rag = C.field(CELL, Wn, 30.0, seed + 4, octaves=3, period_px=(Wn, 0))
    gone = np.zeros_like(height)
    return height.astype(np.float32), np.clip(albedo, 0, 1).astype(np.float32), gone, rag


def lit_atlas(height, albedo, gone, variants, kind, seed):
    """Light every cell of a hill atlas inside a 3x3-cell wrapped window, so chamfers at
    cell borders shade exactly like their neighbours' and exposed edges get real relief."""
    Wn = variants * CELL
    lip_n = C.field(CELL, Wn, 22.0, seed + 40, octaves=3, period_px=(Wn, 0))
    under_n = C.field(CELL, Wn, 26.0, seed + 41, octaves=3, period_px=(Wn, 0))

    def window(a, g):
        cols = [(g - 1) % variants, g, (g + 1) % variants]
        row = np.concatenate([a[:, c * CELL:(c + 1) * CELL] for c in cols], axis=1)
        return np.concatenate([row, row, row], axis=0)

    def render_cell(g, layer, open_left, open_right):
        hw = window(height, g).copy()
        aw = window(albedo, g).copy()
        gw = window(gone, g)
        wx, wy = C.grid(3 * CELL, 3 * CELL)
        lx, ly = wx - CELL, wy - CELL               # centre-cell local coords
        alpha = np.ones_like(hw)
        if layer == 0:
            lip = 3.0 + window(lip_n, g) * 5.0
            alpha *= C.smoothstep(lip - 0.9, lip + 0.9, ly)
            hw = np.where(ly < lip, AIR, hw * C.smoothstep(lip, lip + 9.0, ly) ** 0.7)
            dust = C.smoothstep(lip + 16.0, lip + 2.0, ly) * (ly >= lip)
            aw = C.mix(aw, rgb("#9AA0A8"), dust * 0.55)
        if layer == 3:
            if kind == "brick":
                cut = (gw > 0.5) & (ly >= CELL / 2.0)
                jag = (CELL - 4.0) - window(under_n, g) * 8.0
                cut = cut | (ly > jag)
            else:
                jag = (CELL - 8.0) - window(under_n, g) * 58.0
                cut = ly > jag
            hw = np.where(cut, AIR, hw)
            alpha *= 1.0 - cut.astype(np.float32)
        if open_left:
            alpha *= C.smoothstep(1.0, 2.6, lx)
            hw = np.where(lx < 1.5, AIR, hw * C.smoothstep(1.5, 10.0, lx) ** 0.7)
        if open_right:
            alpha *= C.smoothstep(CELL - 1.0, CELL - 2.6, lx)
            hw = np.where(lx > CELL - 1.5, AIR, hw * C.smoothstep(CELL - 1.5, CELL - 10.0, lx) ** 0.7)
        # The lava bounce is kept faint inside the mass (it would turn every mortar joint
        # orange) and painted properly only where an underside is actually exposed.
        col = C.lit(hw, aw, KEY, AMBIENT, 1.35, ao=0.06, ao_radius=6.0, rim=(LAVA, RIM[1], 0.10)) * np.asarray(TINT, np.float32)
        if layer == 3:
            ny = C.normals(hw, 1.35)[1]
            under = np.clip(ny, 0.0, 1.0) ** 1.2 * C.smoothstep(CELL * 0.30, CELL, ly)
            col = col + LAVA * (under * 0.85)[..., None] + LAVA * (C.smoothstep(CELL * 0.55, CELL, ly) * 0.10)[..., None]
        if layer == 0:      # a hard cold glint along the walkable edge keeps footing readable in the dark
            lip = 3.0 + window(lip_n, g) * 5.0
            col = col + rgb("#C9D6E6") * (np.exp(-((ly - lip - 2.2) / 1.7) ** 2) * 0.40)[..., None]
        out = C.new(CELL, CELL)
        out[..., :3] = np.clip(col[CELL:2 * CELL, CELL:2 * CELL], 0, 1)
        out[..., 3] = alpha[CELL:2 * CELL, CELL:2 * CELL]
        return out

    return C.build_hill_atlas(render_cell, variants)


def brick_atlas(variants=4):
    h, a, gone = brick_strip(variants, 301)
    return lit_atlas(h, a, gone, variants, "brick", 301)


def rock_atlas(variants=4):
    h, a, gone, _ = rock_strip(variants, 311)
    return lit_atlas(h, a, gone, variants, "rock", 311)


def lava_atlas(variants=4):
    """`hazard.hc_lava` — molten rock as a fill material: a glaring wavy surface with heat
    haze above it, dark cooled plates drifting on a body that glows through its cracks."""
    Wn = variants * CELL
    xx, yy = C.strip(variants)
    wxn = C.field(CELL, Wn, 128.0, 401, octaves=3, period_px=(Wn, CELL)) - 0.5
    wyn = C.field(CELL, Wn, 128.0, 402, octaves=3, period_px=(Wn, CELL)) - 0.5
    x2 = xx + wxn * 90.0
    y2 = yy + wyn * 40.0
    flow = C.fbm(x2 / 64.0, y2 / 32.0, 403, octaves=4, period=(Wn // 64, CELL // 32))
    fine = C.fbm(x2 / 16.0, y2 / 8.0, 405, octaves=3, period=(Wn // 16, CELL // 8))
    # cooled skin: long thin rafts drawn out along the current
    raft = C.fbm(x2 / 128.0, y2 / 16.0, 404, octaves=3, period=(Wn // 128, CELL // 16))
    skin = C.smoothstep(0.56, 0.66, raft)
    heat = np.clip(0.50 + (flow - 0.5) * 0.75 + (fine - 0.5) * 0.25, 0.0, 1.0)
    heat = np.clip(heat * (1.0 - skin * 0.86) + np.exp(-((raft - 0.56) / 0.022) ** 2) * 0.55, 0.0, 1.0)
    body = C.ramp(heat, FIRE)
    two_pi = 2.0 * math.pi

    def render_cell(g, layer, open_left, open_right):
        cell = C.new(CELL, CELL)
        cell[..., :3] = body[:, g * CELL:(g + 1) * CELL]
        cell[..., 3] = 1.0
        gx, gy = C.grid(CELL, CELL, g * CELL, 0)
        lx = gx - g * CELL
        if open_left:
            C.tint_inside(cell, rgb("#1A0E0A"), C.smoothstep(16.0, 2.0, lx) * 0.85)
        if open_right:
            C.tint_inside(cell, rgb("#1A0E0A"), C.smoothstep(CELL - 16.0, CELL - 2.0, lx) * 0.85)
        if layer == 0:
            top = 24.0 + 5.0 * np.sin(gx / Wn * two_pi * 3.0 + 0.4) + 2.5 * np.sin(gx / Wn * two_pi * 7.0 + 1.9)
            below = C.smoothstep(top - 1.0, top + 1.0, gy)
            C.tint_inside(cell, rgb("#FFF2B0"), np.exp(-((gy - top - 3.0) / 3.2) ** 2) * 0.95)
            C.tint_inside(cell, rgb("#FFB02E"), np.exp(-((gy - top - 11.0) / 7.0) ** 2) * 0.55)
            cell[..., 3] = below
            haze = np.exp(-np.clip(top - gy, 0.0, None) / 9.0) * (1.0 - below) * 0.62
            C.add_light(cell, LAVA, haze, only_inside=False)
        return cell

    return C.build_hill_atlas(render_cell, variants)


# ------------------------------------------------------------------ platforms


def rivets(rel, xs, y, r=4.0, seed=0):
    for i, x in enumerate(xs):
        sdf = C.sdf_circle(rel.h, rel.w, x, y, r)
        rel.add(C.aa(sdf, 1.2), C.round_h(sdf, r) * 0.9, rgb("#565C66"), z=float(rel.height[int(y), int(x)]), gloss=0.5)


def grate(seed):
    """Iron gantry (3x1): a riveted rail on wrought brackets, a hook swinging underneath."""
    H, W = CELL, 3 * CELL
    rel = C.Relief(H, W)
    alb = iron_alb(H, W, seed)
    for s in (-1, 1):                                                        # diagonal braces
        x0 = W * (0.5 + s * 0.40)
        for (p0, p1) in [((x0, 22.0), (x0 - s * 54.0, 22.0 + 54.0)), ((x0, 22.0), (x0, 84.0)), ((x0, 84.0), (x0 - s * 54.0, 30.0))]:
            sdf = C.sdf_segment(H, W, p0, p1, 4.6)
            rel.add(C.aa(sdf, 1.3), C.round_h(sdf, 4.6), alb, z=2.0, gloss=0.3)
    for k, cx0 in enumerate((W * 0.36, W * 0.62)):                           # dangling chain + hook
        y = 24.0
        for i in range(3 + k):
            if i % 2 == 0:
                sdf = np.abs(C.sdf_segment(H, W, (cx0, y + 5.0), (cx0, y + 15.0), 5.5)) - 2.2
            else:
                sdf = C.sdf_segment(H, W, (cx0, y + 2.0), (cx0, y + 18.0), 2.3)
            rel.add(C.aa(sdf, 1.2), C.round_h(sdf, 2.3) + 2.0, alb, z=1.0, gloss=0.4)
            y += 15.0
        hook = np.abs(C.sdf_circle(H, W, cx0, y + 12.0, 9.0)) - 2.6
        hook = np.where((C.grid(H, W)[1] < y + 8.0) & (C.grid(H, W)[0] > cx0), 9.0, hook)
        rel.add(C.aa(hook, 1.2), C.round_h(hook, 2.6) + 2.0, alb, z=1.0, gloss=0.45)
    rail = C.sdf_box(H, W, W / 2.0, 13.0, W / 2.0 - 2.0, 11.0, radius=2.5)
    rel.add(C.aa(rail, 1.2), C.bevel_h(rail, 4.5, 9.0), alb, z=3.0, gloss=0.35)
    xx, yy = C.grid(H, W)
    slot = (np.mod(xx - 18.0, 30.0) < 12.0) & (yy > 9.0) & (yy < 19.0) & (xx > 16.0) & (xx < W - 16.0)
    rel.height = np.where(slot, rel.height - 6.5, rel.height)
    rel.albedo = np.where(slot[..., None], rel.albedo * 0.35, rel.albedo)
    rivets(rel, np.arange(9.0, W - 6.0, 30.0), 13.0, 3.6)
    img = render(rel)
    img[..., :3] += rgb("#C9D6E6") * (np.exp(-((yy - 3.2) / 1.6) ** 2) * 0.38 * img[..., 3])[..., None]
    return np.clip(img, 0, 1)


def plank(seed):
    """Rotting timber catwalk (3x1): two warped boards strapped with iron on a split joist."""
    H, W = CELL, 3 * CELL
    rel = C.Relief(H, W)
    xx, yy = C.grid(H, W)
    walb = wood_alb(H, W, seed)
    ialb = iron_alb(H, W, seed + 9)
    for (p0, p1) in [((W * 0.20, 30.0), (W * 0.33, 96.0)), ((W * 0.80, 30.0), (W * 0.67, 96.0)), ((W * 0.30, 78.0), (W * 0.70, 78.0))]:
        sdf = C.sdf_segment(H, W, p0, p1, 7.0)
        rel.add(C.aa(sdf, 1.3), C.bevel_h(sdf, 3.0, 4.0), walb * 0.8, z=1.0)
    sag = 3.0 * np.sin(xx / W * math.pi)
    for (y0, y1, x0, x1) in [(2.0, 18.0, 0.0, W), (18.0, 32.0, 6.0, W - 10.0)]:
        m = ((yy >= y0 + sag * 0.0) & (yy < y1) & (xx >= x0) & (xx < x1)).astype(np.float32)
        m = C.blur(m, 0.8)
        d_edge = np.minimum(yy - y0, y1 - yy)
        rel.add(m, np.clip(d_edge / 3.5, 0, 1) * 5.0 + (C.field(H, W, 5.0, seed + int(y0), stretch=(14.0, 1.0)) - 0.5) * 2.4, walb, z=4.0)
    notch = C.smoothstep(0.62, 0.80, C.field(H, W, 9.0, seed + 5, octaves=3)) * (yy < 6.0)
    rel.mask *= 1.0 - notch * 0.0
    for cx0 in (W * 0.22, W * 0.78):
        band = C.sdf_box(H, W, cx0, 17.0, 9.0, 17.0, radius=1.5)
        rel.add(C.aa(band, 1.2), C.bevel_h(band, 2.5, 3.5), ialb, z=8.0, gloss=0.35)
        rivets(rel, [cx0], 9.0, 3.0)
        rivets(rel, [cx0], 26.0, 3.0)
    img = render(rel)
    img[..., :3] += rgb("#C9D6E6") * (np.exp(-((yy - 3.0) / 1.5) ** 2) * 0.30 * img[..., 3])[..., None]
    return np.clip(img, 0, 1)


def ledge(seed):
    """Stone corbel shelf (2x1): a thick flag on two stepped corbels."""
    H, W = CELL, 2 * CELL
    rel = C.Relief(H, W)
    xx, yy = C.grid(H, W)
    alb = stone_alb(H, W, seed)
    for k, (hw_, y0, y1) in enumerate([(W * 0.30, 60.0, 92.0), (W * 0.40, 32.0, 62.0)]):
        sdf = C.sdf_box(H, W, W / 2.0, (y0 + y1) / 2.0, hw_, (y1 - y0) / 2.0, radius=3.0)
        rel.add(C.aa(sdf, 1.2), C.bevel_h(sdf, 5.0, 7.0) + (C.field(H, W, 6.0, seed + k) - 0.5) * 2.5, alb * (0.9 - 0.08 * k), z=float(k))
    slab = C.sdf_box(H, W, W / 2.0, 18.0, W / 2.0 - 1.5, 16.5, radius=3.0)
    rel.add(C.aa(slab, 1.2), C.bevel_h(slab, 5.5, 8.0) + (C.field(H, W, 5.0, seed + 5) - 0.5) * 2.6, alb, z=3.0)
    img = render(rel)
    img[..., :3] += rgb("#C9D6E6") * (np.exp(-((yy - 3.2) / 1.6) ** 2) * 0.36 * img[..., 3])[..., None]
    return np.clip(img, 0, 1)


# ---------------------------------------------------------------- soft volumes


def chains(seed):
    """Hanging chains (1x4 soft volume): three lengths of iron links to climb through."""
    H, W = 4 * CELL, CELL
    rel = C.Relief(H, W)
    alb = iron_alb(H, W, seed)
    plate = C.sdf_box(H, W, W / 2.0, 7.0, W / 2.0 - 8.0, 6.0, radius=2.0)
    rel.add(C.aa(plate, 1.2), C.bevel_h(plate, 3.0, 5.0), alb, gloss=0.3)
    rng = np.random.default_rng(seed)
    for cx0, end in ((W * 0.24, H - 60.0), (W * 0.52, H - 6.0), (W * 0.78, H - 120.0)):
        y = 10.0
        i = 0
        sway = rng.uniform(-0.03, 0.03)
        while y < end - 20.0:
            x = cx0 + (y - 10.0) * sway
            if i % 2 == 0:
                sdf = np.abs(C.sdf_segment(H, W, (x, y + 7.0), (x, y + 21.0), 8.0)) - 3.0
                rel.add(C.aa(sdf, 1.2), C.round_h(sdf, 3.0), alb, z=2.0, gloss=0.45)
            else:
                sdf = C.sdf_segment(H, W, (x, y + 3.0), (x, y + 25.0), 3.1)
                rel.add(C.aa(sdf, 1.2), C.round_h(sdf, 3.1), alb, z=4.0, gloss=0.45)
            y += 21.0
            i += 1
        x = cx0 + (y - 10.0) * sway
        ball = C.sdf_circle(H, W, x, y + 14.0, 12.0) if end > H - 30.0 else np.abs(C.sdf_circle(H, W, x, y + 12.0, 9.0)) - 3.0
        rel.add(C.aa(ball, 1.2), C.round_h(ball, 12.0 if end > H - 30.0 else 3.0), alb, z=3.0, gloss=0.4)
    return render(rel, strength=1.8)


def cobweb(seed):
    """Cobweb (2x2 soft volume): a sagging orb web, dust-heavy, slung across a corner."""
    img = C.cells(2, 2)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    cx0, cy0 = W * 0.52, H * 0.46
    n = 11
    ends = []
    for i in range(n):
        a = i / n * 2 * math.pi + rng.uniform(-0.12, 0.12)
        r = max(W, H)
        ex, ey = cx0 + math.cos(a) * r, cy0 + math.sin(a) * r
        # clip the spoke to the canvas
        tmax = min([t for t in [(-cx0) / (ex - cx0) if ex < cx0 else (W - cx0) / (ex - cx0),
                                (-cy0) / (ey - cy0) if ey < cy0 else (H - cy0) / (ey - cy0)] if t > 0])
        ends.append((cx0 + (ex - cx0) * tmax, cy0 + (ey - cy0) * tmax))
        C.over(img, rgb("#C9CED6"), C.line_mask(H, W, [(cx0, cy0), ends[-1]], 1.5) * 0.62)
    for ring in range(1, 8):
        f = ring / 8.0
        pts = [(cx0 + (ex - cx0) * f * rng.uniform(0.92, 1.05), cy0 + (ey - cy0) * f * rng.uniform(0.92, 1.05)) for ex, ey in ends]
        for i in range(n):
            a, b = np.array(pts[i]), np.array(pts[(i + 1) % n])
            mid = (a + b) / 2.0 + (np.array([cx0, cy0]) - (a + b) / 2.0) * 0.14 + np.array([0.0, 4.0 * f])
            C.over(img, rgb("#C9CED6"), C.line_mask(H, W, C.bezier(a, mid, b, n=8), 1.15) * (0.55 - 0.2 * f))
    xx, yy = C.grid(H, W)
    veil = np.exp(-(((xx - cx0) / (W * 0.34)) ** 2 + ((yy - cy0) / (H * 0.34)) ** 2))
    dust = C.smoothstep(0.45, 0.75, C.fbm(xx / 30.0, yy / 30.0, seed, octaves=4)) * veil
    C.over(img, rgb("#AEB4BD"), dust * 0.30)
    return img


# -------------------------------------------------------------------- hazards


def spikes(seed):
    """Floor spikes (1x1 hazard): four forged iron spikes on a bolted plate, tips rust-red."""
    H = W = CELL
    rel = C.Relief(H, W)
    xx, yy = C.grid(H, W)
    alb = iron_alb(H, W, seed)
    plate = C.sdf_box(H, W, W / 2.0, H - 9.0, W / 2.0 - 3.0, 8.0, radius=2.0)
    rel.add(C.aa(plate, 1.2), C.bevel_h(plate, 3.5, 5.0), alb, gloss=0.3)
    rng = np.random.default_rng(seed)
    for i, cx0 in enumerate(np.linspace(18.0, W - 18.0, 4)):
        tip = rng.uniform(12.0, 34.0)
        base_y = H - 14.0
        k = np.clip((yy - tip) / (base_y - tip), 0.0, 1.0)
        halfw = 1.0 + k * 12.5
        d = np.abs(xx - (cx0 + (1.0 - k) * rng.uniform(-3.0, 3.0)))
        m = C.smoothstep(halfw + 0.8, halfw - 0.8, d) * (yy >= tip) * (yy <= base_y + 2.0)
        hgt = np.sqrt(np.clip(1.0 - (d / np.maximum(halfw, 1.0)) ** 2, 0.0, 1.0)) * halfw * 0.9
        blood = C.smoothstep(0.42, 0.0, k)[..., None]
        rel.add(m, hgt, C.mix(alb * 1.25, rgb("#5A1410"), blood * 0.85), z=4.0, gloss=0.55)
    rivets(rel, [10.0, W - 10.0], H - 9.0, 3.2)
    return render(rel, strength=1.9)


# ----------------------------------------------------------------- decoration


def flame(img, cx0, base_y, width, height, seed):
    """Emissive flame: a noise-licked teardrop graded white-hot at the root."""
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    k = np.clip((base_y - yy) / height, 0.0, 1.0)                    # 0 at root, 1 at tip
    lick = (C.fbm(xx / 16.0, yy / 26.0, seed, octaves=3) - 0.5) * width * 1.3 * k
    halfw = width * (1.0 - k) ** 0.75 * (0.55 + 0.45 * np.sqrt(np.clip(np.sin(np.clip(k * 1.25, 0, 1) * math.pi), 0.0, 1.0)))
    d = np.abs(xx - cx0 - lick) / np.maximum(halfw, 0.5)
    core = np.clip(1.0 - d, 0.0, 1.0) * (yy <= base_y) * (k < 1.0)
    heat = np.clip(core * (1.25 - k * 0.9), 0.0, 1.0)
    C.over(img, C.ramp(np.clip(heat * 1.15, 0, 1), FIRE), C.smoothstep(0.02, 0.22, core))


def torch(seed):
    """Wall torch (3x3): a pitch-soaked brand in a wrought bracket. The footprint is mostly
    halo — torches are the realm's only lamps, so each one lights a pool of wall around it."""
    H = W = 3 * CELL
    rel = C.Relief(H, W)
    cx0 = W / 2.0
    foot = H - 30.0
    ialb = iron_alb(H, W, seed)
    walb = wood_alb(H, W, seed + 3, horizontal=False)
    plate = C.sdf_box(H, W, cx0, foot - 30.0, 13.0, 34.0, radius=3.0)
    rel.add(C.aa(plate, 1.2), C.bevel_h(plate, 3.5, 5.0), ialb, gloss=0.3)
    brand = C.sdf_segment(H, W, (cx0, foot - 16.0), (cx0, foot - 122.0), 7.0)
    rel.add(C.aa(brand, 1.2), C.round_h(brand, 7.0), walb, z=5.0)
    cup = C.sdf_box(H, W, cx0, foot - 122.0, 17.0, 12.0, radius=4.0)
    rel.add(C.aa(cup, 1.2), C.bevel_h(cup, 4.0, 6.0), ialb, z=9.0, gloss=0.4)
    for y in (foot - 52.0, foot - 10.0):
        ring = C.sdf_box(H, W, cx0, y, 11.0, 4.0, radius=2.0)
        rel.add(C.aa(ring, 1.2), C.bevel_h(ring, 2.0, 3.0), ialb, z=11.0, gloss=0.4)
    rivets(rel, [cx0], foot - 2.0, 3.0)
    fixture = render(rel)
    xx, yy = C.grid(H, W)
    fixture[..., :3] = np.clip(fixture[..., :3] + LAVA * (np.exp(-((yy - (foot - 128.0)) / 44.0) ** 2) * 0.6 * fixture[..., 3])[..., None], 0, 1)
    img = C.new(H, W)
    C.radial_glow(img, cx0, foot - 170.0, 74.0, LAVA, 0.80)
    C.radial_glow(img, cx0, foot - 170.0, 30.0, rgb("#FFB02E"), 0.60)
    C.over_img(img, fixture)
    flame(img, cx0, foot - 132.0, 22.0, 100.0, seed)
    return img


def pillar(seed):
    """Stone pillar (1x4): drum-built shaft, block capital and plinth — a foreground hide."""
    H, W = 4 * CELL, CELL
    rel = C.Relief(H, W)
    xx, yy = C.grid(H, W)
    alb = stone_alb(H, W, seed)
    cx0, r = W / 2.0, 30.0
    shaft = (np.abs(xx - cx0) < r) & (yy > 46.0) & (yy < H - 46.0)
    cyl = np.sqrt(np.clip(1.0 - ((xx - cx0) / r) ** 2, 0.0, 1.0)) * r * 0.75
    drum = np.exp(-((np.mod(yy - 46.0, 84.0) - 2.0) / 2.2) ** 2)
    cr = C.smoothstep(0.02, 0.0, np.abs(C.field(H, W, 40.0, seed + 2, octaves=4) - 0.5))
    rel.add(C.blur(shaft.astype(np.float32), 0.7), cyl - drum * 4.0 - cr * 3.5 + (C.field(H, W, 6.0, seed + 3) - 0.5) * 2.5,
            alb * (1.0 - 0.5 * cr[..., None]))
    for (y0, y1, hw_) in [(2.0, 26.0, 46.0), (26.0, 48.0, 38.0), (H - 48.0, H - 24.0, 38.0), (H - 24.0, H - 1.0, 48.0)]:
        sdf = C.sdf_box(H, W, cx0, (y0 + y1) / 2.0, hw_, (y1 - y0) / 2.0, radius=3.0)
        rel.add(C.aa(sdf, 1.2), C.bevel_h(sdf, 5.0, 8.0) + 20.0 + (C.field(H, W, 5.0, seed + int(y0)) - 0.5) * 2.5, alb)
    moss = C.smoothstep(0.50, 0.78, C.field(H, W, 18.0, seed + 5, octaves=4)) * C.smoothstep(H - 150.0, H - 20.0, yy)
    rel.albedo = C.mix(rel.albedo, rgb("#3C5230"), moss * 0.7)
    return render(rel, strength=1.3)


def arch(seed):
    """Portcullis arch (4x4): a brick wall bay with a dark gateway barred in iron."""
    H = W = 4 * CELL
    h, a, _ = brick_strip(4, seed, bw=CELL, bh=CELL // 2)
    height = np.concatenate([h] * 4, axis=0)
    albedo = np.concatenate([a] * 4, axis=0)
    rel = C.Relief(H, W)
    rel.add(np.ones((H, W), np.float32), height, albedo)
    xx, yy = C.grid(H, W)
    cx0, half, spring = W / 2.0, W * 0.27, H * 0.46
    dome = np.hypot(xx - cx0, yy - spring) - half
    opening = np.where(yy < spring, dome, np.abs(xx - cx0) - half)
    ring = np.abs(opening - 14.0) - 14.0                              # voussoir band around the opening
    seg = np.mod(np.arctan2(yy - spring, xx - cx0) * 9.0 / math.pi, 1.0)
    vouss = np.where(yy < spring, np.minimum(seg, 1.0 - seg) * 30.0, np.minimum(np.mod(yy, 40.0), 40.0 - np.mod(yy, 40.0)))
    rel.add(C.aa(ring, 1.2), C.bevel_h(ring, 5.0, 6.0) * np.clip(vouss / 4.0, 0.2, 1.0) + 13.0, stone_alb(H, W, seed + 4, "#5E636B", "#8A9099"))
    hole = C.aa(opening, 1.2)
    rel.add(hole, np.full((H, W), -22.0, np.float32), rgb("#050507"))
    ialb = iron_alb(H, W, seed + 6)
    for bx0 in np.arange(cx0 - half + 22.0, cx0 + half - 10.0, 34.0):
        bar = C.sdf_segment(H, W, (bx0, 0.0), (bx0, H - 34.0), 5.0)
        tipk = np.clip((yy - (H - 60.0)) / 26.0, 0.0, 1.0)
        bar = bar + tipk * 5.0
        rel.add(C.aa(bar, 1.2) * hole, C.round_h(bar, 5.0) - 12.0, ialb, gloss=0.4)
    for by0 in (spring + 20.0, spring + 150.0):
        bar = C.sdf_segment(H, W, (cx0 - half, by0), (cx0 + half, by0), 5.5)
        rel.add(C.aa(bar, 1.2) * hole, C.round_h(bar, 5.5) - 10.0, ialb, gloss=0.4)
    img = render(rel, strength=1.3)
    img[..., :3] = np.clip(img[..., :3] + LAVA * (hole * C.smoothstep(H * 0.55, H, yy) * 0.20)[..., None], 0, 1)
    return img


def skulls(seed):
    """Bone pile (2x1): skulls and long bones drifted against a wall."""
    H, W = CELL, 2 * CELL
    rel = C.Relief(H, W)
    rng = np.random.default_rng(seed)
    bone = rgb("#CFC6A8")
    for _ in range(9):
        x0, y0 = rng.uniform(16, W - 16), rng.uniform(H - 34.0, H - 8.0)
        ang = rng.uniform(-0.5, 0.5)
        ln = rng.uniform(34.0, 64.0)
        p0 = (x0 - math.cos(ang) * ln / 2, y0 - math.sin(ang) * ln / 2)
        p1 = (x0 + math.cos(ang) * ln / 2, y0 + math.sin(ang) * ln / 2)
        sdf = C.sdf_segment(H, W, p0, p1, 3.6)
        for kx, ky in (p0, p1):
            sdf = np.minimum(sdf, C.sdf_circle(H, W, kx, ky, 6.0))
        rel.add(C.aa(sdf, 1.2), C.round_h(sdf, 5.0), bone * rng.uniform(0.7, 1.0), z=rng.uniform(0, 3))
    for cx0, cy0, r in [(W * 0.26, H - 34.0, 24.0), (W * 0.60, H - 40.0, 27.0), (W * 0.84, H - 28.0, 20.0)]:
        dome = C.sdf_circle(H, W, cx0, cy0, r)
        jaw = C.sdf_box(H, W, cx0, cy0 + r * 0.72, r * 0.62, r * 0.42, radius=5.0)
        sdf = np.minimum(dome, jaw)
        rel.add(C.aa(sdf, 1.2), C.round_h(sdf, r) * 0.8, bone * rng.uniform(0.85, 1.0), z=5.0)
        for s in (-1, 1):
            eye = C.sdf_circle(H, W, cx0 + s * r * 0.38, cy0 + r * 0.08, r * 0.24)
            rel.add(C.aa(eye, 1.2), np.full((H, W), -5.0, np.float32), rgb("#0C0A08"), z=5.0)
        nose = C.sdf_box(H, W, cx0, cy0 + r * 0.44, r * 0.09, r * 0.14, radius=2.0)
        rel.add(C.aa(nose, 1.2), np.full((H, W), -4.0, np.float32), rgb("#0C0A08"), z=5.0)
        for tx in np.linspace(cx0 - r * 0.42, cx0 + r * 0.42, 5):
            gap = C.sdf_box(H, W, tx, cy0 + r * 0.92, 1.0, r * 0.16)
            rel.add(C.aa(gap, 1.0), np.full((H, W), -3.0, np.float32), rgb("#2A241C"), z=5.0)
    return render(rel, strength=1.4)


def stalactites(seed):
    """Stalactites (3x2): dripstone hanging from a ceiling, tips lit from the lava below."""
    H, W = 2 * CELL, 3 * CELL
    rel = C.Relief(H, W)
    xx, yy = C.grid(H, W)
    alb = stone_alb(H, W, seed, "#2E2A28", "#57504A", 18.0)
    rng = np.random.default_rng(seed)
    for cx0 in np.sort(rng.uniform(14.0, W - 14.0, 13)):
        ln = rng.uniform(0.25, 1.0) * (H - 10.0)
        root = rng.uniform(13.0, 30.0)
        k = np.clip(yy / ln, 0.0, 1.0)
        halfw = root * (1.0 - k) ** 1.5 + 0.6
        wob = (C.fbm(yy / 22.0, xx * 0 + cx0, seed + int(cx0), octaves=2) - 0.5) * 7.0
        d = np.abs(xx - cx0 - wob * k)
        m = C.smoothstep(halfw + 0.9, halfw - 0.9, d) * (yy <= ln)
        hgt = np.sqrt(np.clip(1.0 - (d / np.maximum(halfw, 1.0)) ** 2, 0.0, 1.0)) * halfw * 0.9
        rel.add(m, hgt + (C.field(H, W, 5.0, seed + 2) - 0.5) * 2.2, alb, z=rng.uniform(0.0, 4.0))
    img = render(rel, strength=1.7)
    img[..., :3] = np.clip(img[..., :3] + LAVA * (C.smoothstep(H * 0.2, H, yy) * 0.28 * img[..., 3])[..., None], 0, 1)
    return img


def banner(seed):
    """Tattered banner (1x3): a moth-eaten crimson standard bearing a hollow crown."""
    H, W = 3 * CELL, CELL
    rel = C.Relief(H, W)
    xx, yy = C.grid(H, W)
    rod = C.sdf_segment(H, W, (6.0, 10.0), (W - 6.0, 10.0), 5.0)
    rel.add(C.aa(rod, 1.2), C.round_h(rod, 5.0), iron_alb(H, W, seed), z=6.0, gloss=0.4)
    tear = C.fbm(xx / 14.0, yy * 0 + 3.0, seed, octaves=3)
    bottom = H - 24.0 - tear * 70.0 - 26.0 * np.abs(np.sin(xx / W * math.pi * 3.0))
    cloth = ((xx > 18.0) & (xx < W - 18.0) & (yy > 12.0) & (yy < bottom)).astype(np.float32)
    holes = C.smoothstep(0.70, 0.78, C.fbm(xx / 12.0, yy / 12.0, seed + 2, octaves=3)) * C.smoothstep(H * 0.35, H * 0.9, yy)
    cloth = C.blur(cloth * (1.0 - holes), 0.8)
    folds = np.sin(xx / W * math.pi * 5.0 + yy / 90.0) * 5.0 + np.sin(xx / W * math.pi * 2.0) * 3.0
    weave = C.field(H, W, 2.2, seed + 3, octaves=1)
    alb = C.mix(rgb("#3A0E12"), rgb("#7A1E1E"), C.field(H, W, 40.0, seed + 4, octaves=3)) * (0.86 + 0.28 * weave[..., None])
    cx0, cy0 = W / 2.0, H * 0.34
    crown = C.poly_mask(H, W, [(cx0 - 24.0, cy0 + 16.0), (cx0 - 24.0, cy0 - 12.0), (cx0 - 12.0, cy0), (cx0, cy0 - 20.0),
                               (cx0 + 12.0, cy0), (cx0 + 24.0, cy0 - 12.0), (cx0 + 24.0, cy0 + 16.0)])
    inner = C.rect_mask(H, W, cx0 - 15.0, cy0 + 3.0, cx0 + 15.0, cy0 + 10.0)
    alb = C.mix(alb, rgb("#B89A56") * (0.8 + 0.3 * weave[..., None]), (crown * (1.0 - inner))[..., None] * 0.9)
    rel.add(cloth, folds, alb)
    return render(rel, strength=1.2)


def wall_far(seed):
    """Far wall (8x6): a tier of receding masonry with tall open arches, crushed into
    darkness so it never competes with the battlefield. Tiles horizontally, and stacks: each
    copy above another is the next storey of the arcade."""
    H, W = 6 * CELL, 8 * CELL
    h, a, _ = brick_strip(8, seed, bw=CELL // 2, bh=CELL // 4, mortar=2.2)
    h = np.concatenate([h] * 6, axis=0) * 0.6
    a = np.concatenate([a] * 6, axis=0)
    xx, yy = C.grid(H, W)
    rel = C.Relief(H, W)
    rel.add(np.ones((H, W), np.float32), h, a)
    holes = []
    for cxf in (0.25, 0.75):
        cx0, half, spring = W * cxf, W * 0.085, H * 0.34
        dome = np.hypot(xx - cx0, yy - spring) - half
        opening = np.where(yy < spring, dome, np.abs(xx - cx0) - half)
        ring = np.abs(opening - 9.0) - 9.0
        rel.add(C.aa(ring, 1.2), C.bevel_h(ring, 4.0, 5.0) + 8.0, stone_alb(H, W, seed + 4, "#565A62", "#7A8089"))
        holes.append(C.aa(opening, 1.2) * (yy < H * 0.88))
    for cxf in (0.0, 0.5, 1.0):                                         # engaged piers
        pier = np.abs(xx - W * cxf) - 34.0
        rel.add(C.aa(pier, 1.2), C.bevel_h(pier, 8.0, 10.0) + 6.0, stone_alb(H, W, seed + 8, "#50545C", "#747A83"))
    img = render(rel, strength=1.1)
    dark = rgb("#0C0B10")
    img[..., :3] = C.mix(img[..., :3] * 0.55, dark, 0.42)
    for hole in holes:                                                  # the arches are open: whatever is
        img[..., 3] *= 1.0 - hole                                       # behind (the lava lake) shows through
    return img


def cavern_far(seed):
    """Far cavern (8x5): black dripstone silhouettes against the red breath of a lava lake.
    Tiles horizontally."""
    H, W = 5 * CELL, 8 * CELL
    img = C.new(H, W)
    xx, yy = C.grid(H, W)
    glow = C.smoothstep(H * 0.15, H, yy)
    smoke = C.fbm(xx / 150.0, yy / 70.0, seed, octaves=4, period=(max(1, int(round(W / 150.0))), 0))
    C.over(img, C.ramp(np.clip(glow * (0.75 + 0.5 * (smoke - 0.5)), 0, 1),
                       [(0.0, "#0C0B10"), (0.45, "#2A0A08"), (0.75, "#7A1A08"), (1.0, "#E8520E")]), glow ** 0.8 * 0.92)
    for k, (base, amp, col) in enumerate([(0.72, 0.30, rgb("#1A0C0C")), (0.86, 0.22, rgb("#0B0709"))]):
        n = C.fbm(xx / 70.0, yy * 0 + k * 5.0, seed + 3 + k, octaves=4, period=(max(1, int(round(W / 70.0))), 0))
        spire = C.fbm(xx / 20.0, yy * 0 + k * 9.0, seed + 7 + k, octaves=3, period=(max(1, int(round(W / 20.0))), 0))
        ridge = H * (base - amp * n * (0.5 + 0.9 * C.smoothstep(0.5, 0.8, spire)))
        C.over(img, col, C.smoothstep(ridge - 1.0, ridge + 1.0, yy))
        C.tint_inside(img, rgb("#8A2A0C"), np.exp(-((yy - ridge - 2.0) / 2.4) ** 2) * 0.45)
    return img


def light_shaft(seed):
    """Light shaft (2x6): a slab of cold daylight from a grating far above, dust turning in it."""
    img = C.cells(2, 6)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    k = yy / H
    centre = W * (0.70 - 0.42 * k)
    halfw = W * (0.10 + 0.14 * k)
    beam = np.exp(-((xx - centre) / halfw) ** 2) * (1.0 - k) ** 0.6 * C.smoothstep(0.0, 0.04, k)
    C.over(img, rgb("#BFD4EA"), beam * 0.26)
    rng = np.random.default_rng(seed)
    for _ in range(60):
        yv = rng.uniform(0.02, 0.95) * H
        xv = W * (0.70 - 0.42 * yv / H) + rng.normal(0, 1) * W * (0.10 + 0.14 * yv / H) * 0.6
        C.over(img, rgb("#EAF2FB"), C.aa(C.sdf_circle(H, W, xv, yv, rng.uniform(0.9, 2.0)), 1.2) * rng.uniform(0.25, 0.7))
    return img


def lava_glow(seed, end=False):
    """Heat haze (2x3): orange light rising off a lava pool, laid over the foreground so
    everything near the pool — the player included — looks lit by it. The middle piece tiles
    horizontally; the END piece fades out to its left (flip it for a pool's right end)."""
    img = C.cells(2, 3)
    H, W = img.shape[:2]
    xx, yy = C.grid(H, W)
    rise = C.smoothstep(H * 0.08, H, yy) ** 2.0
    wisp = C.fbm(xx / 64.0, yy / 90.0, seed, octaves=4, period=(max(1, int(round(W / 64.0))), 0))
    a = np.clip(rise * (0.60 + 0.5 * (wisp - 0.5)), 0, 1) * 0.52
    if end:
        a *= C.smoothstep(W * 0.05, W * 0.95, xx) ** 1.3
    C.over(img, C.mix(rgb("#E8420A"), rgb("#FFB02E"), rise), a)
    rng = np.random.default_rng(seed)
    for _ in range(13):
        ex, ey = rng.uniform(0, W), rng.uniform(H * 0.15, H)
        if end and ex < W * 0.35:
            continue
        for ox in (-W, 0, W):
            C.over(img, rgb("#FFD27A"), C.aa(C.sdf_circle(H, W, ex + ox, ey, rng.uniform(1.0, 2.4)), 1.2) * 0.85)
    return img


# ---------------------------------------------------------------------- build


def build():
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.hc_brick", "Dungeon Brick", "Ground", 1, 1, "Ground", "ground_hc_brick", brick_atlas(4),
        "Damp dungeon masonry in running bond: chipped, cracked grey-blue bricks proud of dark recessed mortar, moss in "
        "the joints, a worn dusty lip with a cold glint on the walkable top, bricks fallen away from the underside.",
        fill=True, group="terrain.hc_brick", variants=4)
    add("ground.hc_basalt", "Cavern Basalt", "Ground", 1, 1, "Ground", "ground_hc_basalt", rock_atlas(4),
        "Raw cavern basalt: ridged, faceted, fissured dark rock; the underside hangs in jagged dripstone lit orange from below.",
        fill=True, group="terrain.hc_basalt", variants=4)
    add("hazard.hc_lava", "Lava", "Hazard", 1, 1, "Hazard", "hazard_hc_lava", lava_atlas(4),
        "Molten lava as a fill material: glaring wavy surface with heat haze, dark cooled plates on a body glowing through its cracks.",
        fill=True, group="hazard.hc_lava", variants=4)

    add("platform.hc_grate", "Iron Gantry", "Platform", 3, 1, "Platform", "platform_hc_grate", grate(321),
        "A riveted, slotted wrought-iron rail on diagonal brackets with short chains and a hook dangling beneath.", walk=5)
    add("platform.hc_plank", "Timber Catwalk", "Platform", 3, 1, "Platform", "platform_hc_plank", plank(325),
        "A rotting timber catwalk: two warped boards strapped with riveted iron bands on split joists.", walk=5)
    add("platform.hc_ledge", "Corbel Ledge", "Platform", 2, 1, "Platform", "platform_hc_ledge", ledge(329),
        "A thick stone flag carried on two stepped corbel blocks.", walk=5)

    add("softvolume.hc_chains", "Hanging Chains", "SoftVolume", 1, 4, "SoftVolume", "softvolume_hc_chains", chains(333),
        "Three lengths of heavy iron chain hanging from a bolted plate, ending in a ring, a ball and a shackle. Enterable.")
    add("softvolume.hc_cobweb", "Cobweb", "SoftVolume", 2, 2, "SoftVolume", "softvolume_hc_cobweb", cobweb(337),
        "A sagging, dust-laden orb web slung across a corner. Enterable; very sticky.")

    add("hazard.hc_spikes", "Floor Spikes", "Hazard", 1, 1, "Hazard", "hazard_hc_spikes", spikes(341),
        "Four forged iron floor spikes on a bolted plate, tips dark with old blood.")

    add("deco.hc_torch", "Wall Torch", "Decoration", 3, 3, "Decoration", "deco_hc_torch", torch(345),
        "A pitch-soaked brand burning in a wrought-iron wall bracket, throwing an orange halo.")
    add("deco.hc_pillar", "Stone Pillar", "Decoration", 1, 4, "Decoration", "deco_hc_pillar", pillar(349),
        "A drum-built stone pillar with block capital and plinth, cracked, mossy at the foot.")
    add("deco.hc_arch", "Portcullis Arch", "Decoration", 4, 4, "Decoration", "deco_hc_arch", arch(353),
        "A brick wall bay with a dark arched gateway ringed in dressed voussoirs and barred by an iron portcullis.")
    add("deco.hc_skulls", "Bone Pile", "Decoration", 2, 1, "Decoration", "deco_hc_skulls", skulls(357),
        "A drift of skulls and long bones.")
    add("deco.hc_stalactites", "Stalactites", "Decoration", 3, 2, "Decoration", "deco_hc_stalactites", stalactites(361),
        "A cluster of dripstone stalactites hanging from a ceiling, tips lit orange from below.")
    add("deco.hc_banner", "Tattered Banner", "Decoration", 1, 3, "Decoration", "deco_hc_banner", banner(365),
        "A moth-eaten crimson standard on an iron rod, bearing a faded hollow crown.")
    add("deco.hc_wall_far", "Far Wall", "Decoration", 8, 6, "Decoration", "deco_hc_wall_far", wall_far(369),
        "Receding dungeon masonry with tall blind arches and engaged piers, crushed into darkness. Tiles horizontally.")
    add("deco.hc_cavern_far", "Far Cavern", "Decoration", 8, 5, "Decoration", "deco_hc_cavern_far", cavern_far(373),
        "Black dripstone silhouettes against the red glow of a distant lava lake. Tiles horizontally.")
    add("deco.hc_light_shaft", "Light Shaft", "Decoration", 2, 6, "Decoration", "deco_hc_light_shaft", light_shaft(377),
        "A slanting slab of cold daylight from a grating far above, with dust turning in it.")
    add("deco.hc_lava_glow", "Lava Glow", "Decoration", 2, 3, "Decoration", "deco_hc_lava_glow", lava_glow(381),
        "Orange heat haze and rising embers to lay over the foreground above a lava pool. Tiles horizontally.")
    add("deco.hc_lava_glow_end", "Lava Glow (end)", "Decoration", 2, 3, "Decoration", "deco_hc_lava_glow_end", lava_glow(381, end=True),
        "The end of a lava pool's heat haze, fading out to the left; flip it for the right end.")
    return out


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(14, 13, 18), sheet_cell=56)


if __name__ == "__main__":
    export(sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None)

#!/usr/bin/env python3
"""The Forgotten tile set — Pixolotl's home world (TF): the city everyone took for a ruin.

Art language: PIXEL ART. Pixolotl is a pixel creature, and her city is drawn the way she
is: a hard 32 x 32 grid of square pixels to the map cell, a small fixed palette, shading by
ordered dither and never by blur. The subject is Teotihuacan — talud-tablero terraces,
step-frets, feathered serpents — in old violet stone going back to swamp. What nobody
outside knows is that the city is switched on: thin neon inlays run along its cornices,
lenses burn in its statues, bridges are made of light. Stone and swamp are dull; anything
bright is technology.
See `Docs/Art/RealmStyleBible.md` §The Forgotten.

Usage:  /opt/homebrew/bin/python3 Tools/realm_art/forgotten.py [--sheet out.png]
"""

from __future__ import annotations

import sys

import numpy as np

import common as C
from common import CELL, rgb

REALM = "TheForgotten"
SET = "tf"

P = 4                       # output px per art pixel
LO = CELL // P              # art pixels per map cell: 32

VOID = rgb("#0C0C22")       # every map's canvas: night over the swamp
ST0, ST1, ST2, ST3, ST4 = (rgb(h) for h in ("#1B1730", "#2E2848", "#4A4168", "#6F648F", "#9A90B8"))
MOSS0, MOSS1, MOSS2 = (rgb(h) for h in ("#1E4A38", "#2F7A52", "#5FBF6E"))
MUD0, MUD1, MUD2 = (rgb(h) for h in ("#241C1A", "#3B2E28", "#5A4636"))
WAT0, WAT1, WAT2, WAT3 = (rgb(h) for h in ("#0E2C33", "#145259", "#1F8A86", "#6FE0D0"))
NEON_C, NEON_M, NEON_Y, NEON_G = (rgb(h) for h in ("#3DF6FF", "#FF3DBE", "#FFE14A", "#9BFF4A"))
WHITE = rgb("#F4FBFF")
PINK, PINK_D = rgb("#FF8FCF"), rgb("#D9589F")
BLACK = rgb("#07060F")

BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) / 16.0

BRIEF = ("Pixel-art game sprite on transparent ground: a hard grid of square pixels (32 to the map cell), a small "
         "fixed palette, shading by ordered dither only, no blur and no anti-aliasing. Teotihuacan / Aztec stone "
         "architecture in old violet stone and swamp greens; the only bright colours are thin neon lights "
         "(cyan, magenta, yellow) — hidden technology. Orthographic side view, no cast shadow, no text.")


def lo(wc, hc):
    return np.zeros((int(hc * LO), int(wc * LO), 4), np.float32)


def up(img):
    """Art pixels -> output pixels: every pixel becomes a crisp P x P square."""
    return np.repeat(np.repeat(img, P, axis=0), P, axis=1)


def put(img, x0, y0, x1, y1, color, a=1.0):
    """Fill art pixels [x0, x1) x [y0, y1), clipped to the canvas."""
    H, W = img.shape[:2]
    x0, y0, x1, y1 = max(0, int(x0)), max(0, int(y0)), min(W, int(x1)), min(H, int(y1))
    if x1 > x0 and y1 > y0:
        img[y0:y1, x0:x1, :3] = color
        img[y0:y1, x0:x1, 3] = a


def paint(img, mask, color, a=1.0):
    img[mask, :3] = color
    img[mask, 3] = a


def dither(value, x0=0, y0=0):
    """Ordered dither: True where `value` (0..1 field) beats the Bayer threshold."""
    h, w = value.shape
    yy, xx = np.mgrid[0:h, 0:w]
    return value > BAYER[(yy + y0) % 4, (xx + x0) % 4]


def noise(h, w, scale, seed, period=None):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    return C.fbm(xx / scale, yy / scale, seed, octaves=3, period=period)


def glow(img, color, radius=3, strength=0.5, of=None):
    """A neon halo, in pixels: the light spills onto its neighbours in stepped, dithered rings."""
    H, W = img.shape[:2]
    src = np.all(np.abs(img[..., :3] - color) < 0.02, axis=-1) & (img[..., 3] > 0.9) if of is None else of
    reach = src.copy()
    for r in range(1, radius + 1):
        grown = reach.copy()
        grown[1:, :] |= reach[:-1, :]
        grown[:-1, :] |= reach[1:, :]
        grown[:, 1:] |= reach[:, :-1]
        grown[:, :-1] |= reach[:, 1:]
        ring = grown & ~reach
        a = strength * (1.0 - (r - 1) / radius)
        keep = ring & dither(np.full((H, W), a * 1.6, np.float32)) if r > 1 else ring
        blank = keep & (img[..., 3] < 0.05)
        img[blank, :3] = color
        img[blank, 3] = a
        lit = keep & (img[..., 3] >= 0.05) & ~src
        img[lit, :3] = img[lit, :3] * (1.0 - a * 0.6) + color * a * 0.6
        reach = grown
    return img


# --------------------------------------------------------------------- ground


def fret(img, x0, y, color, w):
    """A row of step-frets (xicalcoliuhqui), 8 px to the motif, 4 px tall."""
    for mx in range(x0, x0 + w, 8):
        for (dx, dy) in ((0, 3), (1, 3), (2, 3), (2, 2), (3, 2), (4, 2), (4, 1), (5, 1), (6, 1), (6, 0), (7, 0), (6, 3), (7, 3)):
            if 0 <= mx + dx < img.shape[1]:
                img[y + dy, mx + dx, :3] = color
                img[y + dy, mx + dx, 3] = 1.0


def atlas(base, cap, side, underside, variants=4):
    """Layered-hill strip atlas from an art-pixel strip, with art-pixel treatments."""
    def render(g, layer, open_left, open_right):
        cell = base[:, g * LO:(g + 1) * LO].copy()
        if layer == 3:
            underside(cell, g)
        if open_left:
            side(cell, g, True)
        if open_right:
            side(cell, g, False)
        if layer == 0:
            cap(cell, g)
        return up(cell)
    return C.build_hill_atlas(render, variants)


def stone_atlas(variants=4):
    """`ground.tf_stone` — dressed violet stone in courses; here and there a block carries
    a glyph, and in some of the glyphs a light is still on. The cornice is a step-fret over
    a line of cyan neon; the underside steps back in corbels hung with moss."""
    W = variants * LO
    base = lo(variants, 1)
    rng = np.random.default_rng(3)
    put(base, 0, 0, W, LO, ST0)
    for row in range(4):                                     # four courses of 8 px
        y = row * 8
        off = 8 if row % 2 else 0
        for bx in range(-off, W, 16):
            put(base, bx + 1, y + 1, bx + 16, y + 8, ST2)
            put(base, bx + 1, y + 1, bx + 16, y + 2, ST3)                    # top edge catches the light
            put(base, bx + 15, y + 2, bx + 16, y + 8, ST1)                   # right edge in shade
            put(base, bx + 1, y + 7, bx + 16, y + 8, ST1)
            roll = rng.random()
            if roll < 0.22:                                                  # a carved glyph
                gx, gy = bx + 5 + int(rng.integers(0, 4)), y + 3
                for (dx, dy) in ((0, 0), (1, 0), (2, 0), (0, 1), (2, 1), (0, 2), (1, 2), (2, 2)):
                    put(base, gx + dx, gy + dy, gx + dx + 1, gy + dy + 1, ST1)
                if roll < 0.10:                                              # ...with a light still on in it
                    put(base, gx + 1, gy + 1, gx + 2, gy + 2, NEON_M if roll < 0.05 else NEON_C)
            elif roll < 0.30:
                put(base, bx + 4 + int(rng.integers(0, 8)), y + 4, bx + 6 + int(rng.integers(0, 8)), y + 5, ST1)   # a chip
    glow(base, NEON_C, 1, 0.5)
    glow(base, NEON_M, 1, 0.5)

    def cap(cell, g):
        put(cell, 0, 0, LO, 8, ST1)
        put(cell, 0, 0, LO, 1, ST4)                                          # the line you stand on
        put(cell, 0, 1, LO, 2, ST3)
        fret(cell, 0, 2, ST3, LO)
        put(cell, 0, 6, LO, 7, NEON_C)                                       # the cornice is lit
        put(cell, 0, 7, LO, 8, ST0)

    def side(cell, g, left):
        x = 0 if left else LO - 2
        put(cell, x, 0, x + 2, LO, ST1)
        put(cell, 0 if left else LO - 1, 0, 1 if left else LO, LO, ST4 if left else ST0)

    def underside(cell, g):
        for k, (h0, w0) in enumerate(((LO - 6, 0), (LO - 4, 4), (LO - 2, 8))):    # corbels stepping back
            cell[h0:h0 + 2, :, 3] = 0.0
            for cx in range(0, LO, 16):
                put(cell, cx + w0 // 2, h0, cx + 16 - w0 // 2, h0 + 2, ST1 if k else ST2)
        rng2 = np.random.default_rng(40 + g)
        for _ in range(5):                                                   # moss hangs from them
            mx = int(rng2.integers(1, LO - 1))
            for dy in range(int(rng2.integers(2, 6))):
                if cell[LO - 7 + dy, mx, 3] > 0:
                    put(cell, mx, LO - 7 + dy, mx + 1, LO - 6 + dy, MOSS1 if dy else MOSS2)

    return atlas(base, cap, side, underside, variants)


def mud_atlas(variants=4):
    """`ground.tf_mud` — the swamp's bank: dark mud speckled by dither, roots and pebbles,
    grass and moss along the top."""
    W = variants * LO
    base = lo(variants, 1)
    n = noise(LO, W, 9.0, 5, period=(W // 9 if W % 9 == 0 else None, None))
    n = noise(LO, W, 8.0, 5, period=(W // 8, LO // 8))
    put(base, 0, 0, W, LO, MUD1)
    paint(base, dither(np.clip((n - 0.45) * 2.2, 0, 1)), MUD2)
    paint(base, dither(np.clip((0.42 - n) * 2.2, 0, 1), 2, 1), MUD0)
    rng = np.random.default_rng(7)
    for _ in range(5 * variants):                                            # pebbles
        x, y = int(rng.integers(0, W - 2)), int(rng.integers(3, LO - 2))
        put(base, x, y, x + 2, y + 1, ST2)
        put(base, x, y + 1, x + 2, y + 2, ST1)
    for _ in range(3 * variants):                                            # roots
        x, y = int(rng.integers(0, W)), int(rng.integers(2, LO - 6))
        for k in range(int(rng.integers(4, 9))):
            put(base, (x + k) % W, y + (k // 2), (x + k) % W + 1, y + (k // 2) + 1, MUD0)

    def cap(cell, g):
        rng2 = np.random.default_rng(60 + g)
        cell[0:2, :, 3] = 0.0
        put(cell, 0, 2, LO, 4, MOSS1)
        put(cell, 0, 2, LO, 3, MOSS2)                                        # the line you stand on (2 px in)
        for x in range(LO):
            if rng2.random() < 0.45:
                put(cell, x, 1, x + 1, 2, MOSS2)                             # grass tips
            if rng2.random() < 0.18:
                put(cell, x, 0, x + 1, 1, MOSS1)
            if rng2.random() < 0.5:
                put(cell, x, 4, x + 1, 5, MOSS0)

    def side(cell, g, left):
        x = 0 if left else LO - 1
        put(cell, x, 0, x + 1, LO, MUD0)
        cell[::3, x, 3] = 0.0                                                # a crumbling edge

    def underside(cell, g):
        rng2 = np.random.default_rng(80 + g)
        for x in range(LO):
            cut = int(rng2.integers(1, 5))
            cell[LO - cut:, x, 3] = 0.0

    return atlas(base, cap, side, underside, variants)


# ------------------------------------------------------------------ platforms


def slab(wc, seed):
    """A stone slab that should not be floating: fret-carved, with a strip of cyan light
    under it and two small magenta thrusters."""
    img = lo(wc, 1)
    W = img.shape[1]
    put(img, 0, 0, W, 9, ST2)
    put(img, 0, 0, W, 1, ST4)
    put(img, 0, 1, W, 2, ST3)
    fret(img, 0, 3, ST1, W)
    put(img, 0, 8, W, 9, ST1)
    put(img, 2, 9, W - 2, 10, NEON_C)
    for x in (W // 5, W - W // 5 - 2):
        put(img, x, 10, x + 2, 12, ST1)
        put(img, x, 12, x + 2, 13, NEON_M)
    glow(img, NEON_C, 3, 0.55)
    glow(img, NEON_M, 2, 0.5)
    return up(img)


def light_bridge(wc, seed):
    """A bridge of hard light between two stone emitters: a white-hot top line you stand
    on, a dithered cyan field under it."""
    img = lo(wc, 1)
    W = img.shape[1]
    put(img, 0, 0, W, 1, WHITE)
    put(img, 0, 1, W, 2, NEON_C)
    field = np.zeros(img.shape[:2], bool)
    field[2:7, :] = True
    yy, xx = np.mgrid[0:img.shape[0], 0:W]
    paint(img, field & ((xx + yy) % 2 == 0), NEON_C, 0.55)
    paint(img, field & ((xx + yy) % 2 == 1) & (yy < 5), NEON_C, 0.22)
    for x in (0, W - 4):
        put(img, x, 0, x + 4, 11, ST2)
        put(img, x, 0, x + 4, 1, ST4)
        put(img, x + 3, 1, x + 4, 11, ST1)
        put(img, x + 1, 4, x + 3, 6, NEON_M)
    glow(img, NEON_M, 2, 0.5)
    return up(img)


# --------------------------------------------------------------- soft volumes


def swamp_water(wc, hc, seed):
    """Swamp water: dark teal, banded by dither, a broken bright line for its skin and a
    lily pad or two. You wade in it; you do not swim."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    n = noise(H, W, 8.0, seed, period=(W // 8, None))
    put(img, 0, 2, W, H, WAT1, 0.86)
    yy, xx = np.mgrid[0:H, 0:W]
    deep = np.clip((yy - 2) / (H - 2), 0, 1)
    paint(img, (yy >= 2) & dither(deep * 0.9 + (n - 0.5) * 0.5), WAT0, 0.90)
    paint(img, (yy >= 3) & (yy < H * 0.5) & dither(np.clip((n - 0.52) * 2.4, 0, 1), 1, 2), WAT2, 0.86)
    rng = np.random.default_rng(seed)
    for x in range(W):                                                        # the skin
        if rng.random() < 0.78:
            put(img, x, 2, x + 1, 3, WAT3, 0.95)
    for _ in range(int(wc * 1.2)):                                            # lily pads
        x = int(rng.integers(2, W - 6))
        put(img, x, 1, x + 4, 2, MOSS2)
        put(img, x + 1, 2, x + 3, 3, MOSS1)
        if rng.random() < 0.5:
            put(img, x + 1, 0, x + 2, 1, NEON_M)                              # a flower that glows a little
    return up(img)


def reeds(wc, hc, seed):
    """A stand of cattails: cover on the bank. Stalks and bent leaves in loose clumps, and
    the brown heads that say what they are."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    x = 3
    while x < W - 4:                              # loose clumps with air between them, not a fence
        blade = rng.random() < 0.42               # a long leaf that bends over, or a stalk with a head
        tall = int(rng.integers(int(H * 0.42), H - 10))
        lean = int(rng.integers(-1, 2)) or (1 if blade else 0)
        for k in range(tall):
            bend = lean * ((k > tall * 0.55) + (blade and k > tall * 0.80) + (blade and k > tall * 0.93))
            tone = MOSS0 if k < tall * 0.30 else (MOSS2 if (k + x) % 7 == 0 else MOSS1)
            put(img, x + bend, H - 1 - k, x + bend + 1, H - k, tone)
        if not blade:                             # the cattail: a fat brown head under a thin spike
            top = H - tall
            hx = x + lean
            put(img, hx, top - 3, hx + 1, top, MOSS1)
            put(img, hx, top, hx + 2, top + 7, MUD2)
            put(img, hx + 1, top + 1, hx + 2, top + 7, MUD1)
        x += int(rng.integers(2, 5))
    for _ in range(3):                                                        # fireflies: the city's light gets everywhere
        put(img, int(rng.integers(2, W - 2)), int(rng.integers(2, H // 2)), 0, 0, NEON_G)
        x, y = int(rng.integers(2, W - 2)), int(rng.integers(2, H // 2))
        put(img, x, y, x + 1, y + 1, NEON_G)
    glow(img, NEON_G, 1, 0.5)
    return up(img)


def vines(wc, hc, seed):
    """Hanging vines with small glowing flowers. Tiles vertically: a lane to climb."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for vx in (7, 15, 23):
        ph = rng.uniform(0, 6.28)
        for y in range(H):
            x = vx + int(round(np.sin(y / H * 2 * np.pi * 2 + ph) * 2.0))        # two whole waves: the ends meet
            put(img, x, y, x + 1, y + 1, MOSS1)
            if y % 5 == int(vx) % 5:
                s = 1 if (y // 5) % 2 else -1
                put(img, x + s, y, x + s + 1, y + 1, MOSS2)
                put(img, x + 2 * s, y + 1, x + 2 * s + 1, y + 2, MOSS2)
            if y % 23 == int(vx) % 23:
                put(img, x - 1, y, x + 2, y + 1, NEON_M)
                put(img, x, y - 1, x + 1, y + 2, NEON_M)
                put(img, x, y, x + 1, y + 1, WHITE)
    return up(img)


# -------------------------------------------------------------------- hazards


def laser(seed):
    """A security beam: two stone emitters with magenta lenses and the light between them."""
    img = lo(1, 3)
    H, W = img.shape[:2]
    cx = W // 2
    for y0 in (0, H - 6):
        put(img, cx - 6, y0, cx + 6, y0 + 6, ST2)
        put(img, cx - 6, y0, cx + 6, y0 + 1, ST4)
        put(img, cx + 5, y0 + 1, cx + 6, y0 + 6, ST1)
        put(img, cx - 2, y0 + 2, cx + 2, y0 + 4, NEON_M)
    put(img, cx - 1, 6, cx + 1, H - 6, NEON_M)
    put(img, cx - 1, 6, cx, H - 6, WHITE)
    rng = np.random.default_rng(seed)
    for _ in range(10):                                                       # it crackles
        y = int(rng.integers(8, H - 8))
        s = 1 if rng.random() < 0.5 else -1
        put(img, cx + (1 if s > 0 else -2), y, cx + (2 if s > 0 else -1), y + 1, NEON_M)
    glow(img, NEON_M, 3, 0.6)
    return up(img)


def obsidian(seed):
    """Obsidian blades set in a stone footing: black glass, edged in light."""
    img = lo(2, 1)
    H, W = img.shape[:2]
    put(img, 1, H - 5, W - 1, H, ST2)
    put(img, 1, H - 5, W - 1, H - 4, ST4)
    rng = np.random.default_rng(seed)
    for bx in range(3, W - 6, 7):
        tall = int(rng.integers(13, 23))
        for k in range(tall):
            half = max(0, (tall - k) * 3 // tall)
            y = H - 5 - k
            put(img, bx + 2 - half, y, bx + 3 + half, y + 1, BLACK)
            put(img, bx + 2 - half, y, bx + 3 - half, y + 1, WHITE if k % 4 == 0 else NEON_C)   # the lit edge
            if half:
                put(img, bx + 2 + half, y, bx + 3 + half, y + 1, ST1)
    return up(img)


# ---------------------------------------------------------------- decoration


def pyramid_far(wc, hc, seed):
    """The great pyramid, far off: six talud-tablero terraces and a temple, every cornice
    traced in a thread of light, a sun disc burning over the door. Solid to the bottom."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    cx = W // 2
    ground = int(H * 0.80)
    put(img, 0, ground, W, H, ST0)
    tiers = 6
    th = int((ground - H * 0.22) / tiers)
    for k in range(tiers):
        half = int(W * 0.46 * (1.0 - k / (tiers + 0.6)))
        y1 = ground - k * th
        y0 = y1 - th
        for y in range(y0, y1):                                               # sloped talud
            inset = (y1 - 1 - y) // 2
            put(img, cx - half + inset, y, cx + half - inset, y + 1, ST1)
        put(img, cx - half + th // 2, y0, cx + half - th // 2, y0 + 2, ST2)   # the tablero's lit top
        for x in range(cx - half + th // 2, cx + half - th // 2, 3):          # a thread of light along it
            put(img, x, y0 + 2, x + 1, y0 + 3, NEON_C)
    stair = int(W * 0.045)
    put(img, cx - stair, ground - tiers * th, cx + stair, ground, ST2)        # the stair
    for y in range(ground - tiers * th, ground, 2):
        put(img, cx - stair, y, cx + stair, y + 1, ST3)
    top = ground - tiers * th
    put(img, cx - int(W * 0.09), top - int(H * 0.11), cx + int(W * 0.09), top, ST1)     # the temple
    put(img, cx - int(W * 0.11), top - int(H * 0.13), cx + int(W * 0.11), top - int(H * 0.11), ST2)
    put(img, cx - 3, top - int(H * 0.07), cx + 3, top, BLACK)                 # its door
    sy = top - int(H * 0.19)
    yy, xx = np.mgrid[0:H, 0:W]
    paint(img, (xx - cx) ** 2 + (yy - sy) ** 2 <= 16, NEON_Y)                 # the sun disc
    paint(img, (xx - cx) ** 2 + (yy - sy) ** 2 <= 4, WHITE)
    glow(img, NEON_Y, 4, 0.5)
    glow(img, NEON_C, 1, 0.45)
    return up(img)


def city_far(wc, hc, seed):
    """The city on the horizon: stepped silhouettes with a few windows lit. Loops; solid below."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    base = int(H * 0.72)
    put(img, 0, base, W, H, ST0)
    x = 0
    while x < W:
        w = int(rng.integers(18, 46))
        tall = int(rng.integers(int(H * 0.18), int(H * 0.60)))
        steps = int(rng.integers(2, 5))
        for k in range(steps):
            inset = k * w // (steps * 2 + 1)
            y0 = base - tall * (k + 1) // steps
            for ox in (0, W, -W):                                             # wrap, so the loop has no seam
                put(img, x + inset + ox, y0, x + w - inset + ox, base, ST0)
        for _ in range(int(rng.integers(1, 5))):
            wx, wy = x + int(rng.integers(2, max(3, w - 2))), base - int(rng.integers(2, max(3, tall // 2)))
            put(img, wx % W, wy, wx % W + 1, wy + 1, (NEON_C, NEON_M, NEON_Y)[int(rng.integers(0, 3))])
        x += w + int(rng.integers(-6, 10))
    return up(img)


def jungle(wc, hc, seed):
    """The canopy between the city and the swamp: dithered clumps of dark green. Loops; solid below."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    n = noise(H, W, 16.0, seed, period=(W // 16, None))
    yy, xx = np.mgrid[0:H, 0:W]
    top = H * 0.50 - (n[0] - 0.5) * H * 0.55 + np.sin(xx[0] / 5.0) * 1.5
    body = yy >= top[None, :]
    paint(img, body, MOSS0)
    paint(img, body & (yy < top[None, :] + 4) & dither(np.full((H, W), 0.55, np.float32)), MOSS1)
    paint(img, body & (yy < top[None, :] + 1), MOSS1)
    return up(img)


def serpent(seed):
    """A feathered-serpent head on a plinth, in blocks — and its eye is a working lens."""
    img = lo(2, 3)
    H, W = img.shape[:2]
    put(img, 6, H - 14, W - 6, H, ST2)                       # plinth
    put(img, 6, H - 14, W - 6, H - 13, ST4)
    put(img, W - 7, H - 13, W - 6, H, ST1)
    fret(img, 8, H - 9, ST1, W - 16)
    hx, hy = 8, H - 14 - 38
    put(img, hx + 6, hy + 8, hx + 40, hy + 38, ST2)          # skull
    put(img, hx + 6, hy + 8, hx + 40, hy + 9, ST4)
    put(img, hx + 30, hy + 20, hx + 50, hy + 34, ST2)        # snout
    put(img, hx + 30, hy + 20, hx + 50, hy + 21, ST4)
    put(img, hx + 34, hy + 34, hx + 50, hy + 38, ST1)        # jaw
    for k in range(4):                                       # fangs
        put(img, hx + 36 + k * 4, hy + 34, hx + 37 + k * 4, hy + 37, WHITE)
    for k in range(5):                                       # the feather crest, stepped
        put(img, hx + 2 + k * 3, hy + 2 + k * 2, hx + 12 + k * 3, hy + 6 + k * 2, MOSS1 if k % 2 else ST3)
    put(img, hx + 18, hy + 14, hx + 26, hy + 22, ST1)        # eye socket
    put(img, hx + 20, hy + 16, hx + 24, hy + 20, NEON_C)
    put(img, hx + 21, hy + 17, hx + 23, hy + 19, WHITE)
    put(img, hx + 39, hy + 9, hx + 40, hy + 38, ST1)
    glow(img, NEON_C, 3, 0.6)
    return up(img)


def stele(seed):
    """A standing stone covered in glyphs; three of them are lit."""
    img = lo(1, 3)
    H, W = img.shape[:2]
    put(img, 9, 4, W - 9, H, ST2)
    put(img, 9, 4, W - 9, 5, ST4)
    put(img, 11, 2, W - 11, 4, ST2)
    put(img, W - 10, 5, W - 9, H, ST1)
    rng = np.random.default_rng(seed)
    for gy in range(8, H - 6, 6):
        for gx in (11, 17):
            lit = rng.random() < 0.20
            col = (NEON_M if rng.random() < 0.5 else NEON_C) if lit else ST1
            for (dx, dy) in ((0, 0), (1, 0), (2, 0), (3, 0), (0, 1), (3, 1), (0, 2), (1, 2), (3, 2), (0, 3), (1, 3), (2, 3), (3, 3)):
                if rng.random() < 0.85:
                    put(img, gx + dx, gy + dy, gx + dx + 1, gy + dy + 1, col)
    glow(img, NEON_M, 1, 0.45)
    glow(img, NEON_C, 1, 0.45)
    return up(img)


def sign(seed):
    """A hologram: one great step-fret glyph hanging in the air, drawn in scanlines."""
    img = lo(2, 2)
    H, W = img.shape[:2]
    cx, cy = W // 2, H // 2
    spiral = [(-14, -14, 14, -11), (11, -14, 14, 14), (-14, 11, 14, 14), (-14, -6, -11, 14), (-14, -6, 6, -3), (3, -6, 6, 6), (-6, 3, 6, 6)]
    for (x0, y0, x1, y1) in spiral:
        put(img, cx + x0, cy + y0, cx + x1, cy + y1, NEON_M)
    img[::2, :, 3] *= 0.55                                   # scanlines
    put(img, cx - 20, cy + 19, cx + 20, cy + 20, NEON_C, 0.9)
    put(img, cx - 1, cy + 20, cx + 1, cy + 26, NEON_C, 0.6)
    glow(img, NEON_M, 2, 0.4, of=(img[..., 3] > 0.5) & (img[..., 0] > 0.9))
    return up(img)


def tree(wc, hc, seed):
    """An ahuehuete: a swollen trunk, a dithered crown, moss hanging in strands."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    cx = W // 2
    for y in range(int(H * 0.42), H):                        # trunk, flaring at the foot
        t = (y - H * 0.42) / (H * 0.58)
        half = int(5 + t * t * 12)
        put(img, cx - half, y, cx + half, y + 1, MUD1)
        put(img, cx - half, y, cx - half + 2, y + 1, MUD2)
        put(img, cx + half - 2, y, cx + half, y + 1, MUD0)
    yy, xx = np.mgrid[0:H, 0:W]
    n = noise(H, W, 14.0, seed)
    # sized so that the ragged edge (the noise moves it by up to 12%) stays inside the sprite
    crown = (((xx - cx) / (W * 0.42)) ** 2 + ((yy - H * 0.29) / (H * 0.24)) ** 2 + (n - 0.5) * 0.5) < 1.0
    paint(img, crown, MOSS0)
    paint(img, crown & dither(np.clip(1.1 - (yy / (H * 0.5)), 0, 1) * 0.8 + (n - 0.5)), MOSS1)
    paint(img, crown & (yy < H * 0.22) & dither(np.clip((n - 0.45) * 2.0, 0, 1), 2, 2), MOSS2)
    for _ in range(int(W / 6)):                              # hanging moss
        x = int(rng.integers(int(W * 0.08), int(W * 0.92)))
        ys = np.where(crown[:, x])[0]
        if len(ys) == 0:
            continue
        for k in range(int(rng.integers(5, 16))):
            if ys.max() + k < H and (k % 2 == 0 or rng.random() < 0.6):
                put(img, x, ys.max() + k, x + 1, ys.max() + k + 1, MOSS1 if k < 6 else MOSS0)
    return up(img)


def stars(wc, hc, seed):
    """Pixel stars. Loops."""
    img = lo(wc, hc)
    H, W = img.shape[:2]
    rng = np.random.default_rng(seed)
    for _ in range(int(wc * hc * 5)):
        x, y = int(rng.integers(0, W)), int(rng.integers(0, H))
        col = (WHITE, NEON_C, ST4)[int(rng.integers(0, 3))]
        put(img, x, y, x + 1, y + 1, col, float(rng.uniform(0.5, 1.0)))
        if rng.random() < 0.07 and 1 <= x < W - 1 and 1 <= y < H - 1:
            for (dx, dy) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                put(img, x + dx, y + dy, x + dx + 1, y + dy + 1, col, 0.6)
    return up(img)


def axolotl(seed):
    """One of Pixolotl's kin on the bank: pink, frilled, pleased with itself."""
    img = lo(2, 1)
    H, W = img.shape[:2]
    by = H - 9
    put(img, 14, by, 44, by + 7, PINK)                       # body
    put(img, 14, by + 6, 44, by + 7, PINK_D)
    put(img, 40, by + 2, 56, by + 5, PINK)                   # tail
    put(img, 54, by + 1, 60, by + 3, PINK_D)
    put(img, 6, by - 4, 22, by + 6, PINK)                    # head
    put(img, 6, by + 5, 22, by + 6, PINK_D)
    for k, dy in enumerate((-9, -7, -5)):                    # gills: three fronds a side
        put(img, 3 - k % 2, by + dy + 5, 7, by + dy + 6, NEON_M)
        put(img, 21, by + dy + 5, 25 + k % 2, by + dy + 6, NEON_M)
    put(img, 9, by - 1, 11, by + 1, BLACK)                   # eyes
    put(img, 17, by - 1, 19, by + 1, BLACK)
    put(img, 11, by + 3, 17, by + 4, PINK_D)                 # the smile
    for x in (18, 36):                                       # feet
        put(img, x, by + 7, x + 4, by + 9, PINK_D)
    return up(img)


# -------------------------------------------------------------------- build


def build():
    out = []

    def add(tid, name, role, w, h, cat, stem, img, brief, **extra):
        out.append((tid, name, role, w, h, cat, stem, img, brief, extra))

    add("ground.tf_stone", "Old Stone", "Ground", 1, 1, "Ground", "ground_tf_stone", stone_atlas(4),
        "Dressed violet stone in pixel-art courses, some blocks carved with glyphs and a few glyphs still lit; a "
        "step-fret cornice over a line of cyan neon; corbelled underside hung with moss.",
        fill=True, group="terrain.tf_stone", variants=4)
    add("ground.tf_mud", "Swamp Bank", "Ground", 1, 1, "Ground", "ground_tf_mud", mud_atlas(4),
        "Swamp bank in pixel art: dark dithered mud with roots and pebbles, grass and moss along the top.",
        fill=True, group="terrain.tf_mud", variants=4)

    add("platform.tf_slab", "Floating Slab", "Platform", 3, 1, "Platform", "platform_tf_slab", slab(3, 61),
        "A fret-carved stone slab floating on a strip of cyan light with two magenta thrusters.", walk=4)
    add("platform.tf_light", "Light Bridge", "Platform", 4, 1, "Platform", "platform_tf_light", light_bridge(4, 63),
        "A bridge of hard light between two stone emitters: white-hot top line, dithered cyan field below.", walk=4)

    add("softvolume.tf_water", "Swamp Water", "SoftVolume", 4, 2, "SoftVolume", "softvolume_tf_water", swamp_water(4, 2, 71),
        "Swamp water in pixel art: dark teal banded by dither, a broken bright surface line, lily pads. "
        "A medium you wade in: drag, and a little buoyancy.")
    add("softvolume.tf_reeds", "Cattails", "SoftVolume", 2, 3, "SoftVolume", "softvolume_tf_reeds", reeds(2, 3, 73),
        "A stand of pixel-art cattails with a few green fireflies. Enterable cover.")
    add("softvolume.tf_vines", "Hanging Vines", "SoftVolume", 1, 4, "SoftVolume", "softvolume_tf_vines", vines(1, 4, 75),
        "Hanging vines with small glowing magenta flowers. Tiles vertically; enterable, a lane to climb.")

    add("hazard.tf_laser", "Security Beam", "Hazard", 1, 3, "Hazard", "hazard_tf_laser", laser(81),
        "A magenta security beam between two stone emitters, crackling.")
    add("hazard.tf_obsidian", "Obsidian Blades", "Hazard", 2, 1, "Hazard", "hazard_tf_obsidian", obsidian(83),
        "Black obsidian blades set in a stone footing, their edges lit cyan.")

    add("deco.tf_serpent", "Serpent Head", "Decoration", 2, 3, "Decoration", "deco_tf_serpent", serpent(91),
        "A blocky feathered-serpent head on a plinth, with a glowing cyan lens for an eye.")
    add("deco.tf_stele", "Stele", "Decoration", 1, 3, "Decoration", "deco_tf_stele", stele(93),
        "A standing stone covered in glyphs, a few of them lit in neon.")
    add("deco.tf_sign", "Hologram Glyph", "Decoration", 2, 2, "Decoration", "deco_tf_sign", sign(95),
        "A magenta step-fret glyph hanging in the air as a hologram, with scanlines, over a cyan projector line.")
    add("deco.tf_axolotl", "Axolotl", "Decoration", 2, 1, "Decoration", "deco_tf_axolotl", axolotl(97),
        "A small pink pixel-art axolotl with magenta gills, sitting on the bank.")
    add("deco.tf_tree", "Ahuehuete", "Decoration", 4, 6, "Decoration", "deco_tf_tree", tree(4, 6, 101),
        "A pixel-art swamp cypress: swollen trunk, dithered crown, moss hanging in strands.")
    add("deco.tf_pyramid_far", "Far Pyramid", "Decoration", 12, 8, "Decoration", "deco_tf_pyramid_far", pyramid_far(12, 8, 111),
        "The great stepped pyramid far off: six terraces and a temple, every cornice traced in a thread of cyan light, "
        "a yellow sun disc over the door; solid to the bottom.")
    add("deco.tf_city_far", "Far City", "Decoration", 16, 6, "Decoration", "deco_tf_city_far", city_far(16, 6, 115),
        "The city on the horizon as stepped silhouettes with a few neon windows. Tiles horizontally; solid below.")
    add("deco.tf_jungle", "Canopy", "Decoration", 16, 4, "Decoration", "deco_tf_jungle", jungle(16, 4, 117),
        "A band of dark dithered jungle canopy. Tiles horizontally; solid below.")
    add("deco.tf_stars", "Stars", "Decoration", 8, 4, "Decoration", "deco_tf_stars", stars(8, 4, 121),
        "Pixel stars. Tiles horizontally.")
    return out


def export(sheet_path=None):
    C.export_set(REALM, SET, BRIEF, build(), sheet_path, sheet_bg=(12, 12, 34), sheet_cell=64)


if __name__ == "__main__":
    sheet = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
    export(sheet)

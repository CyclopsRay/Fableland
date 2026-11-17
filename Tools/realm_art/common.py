#!/usr/bin/env python3
"""Shared procedural-painting toolkit for the realm tile sets.

Every realm's art is *painted by code*: a deterministic script under
`Tools/realm_art/` owns geometry, palette, and texture, so a sprite can never
drift from its footprint and re-running the script reproduces it byte-for-byte.
See `Docs/Art/RealmStyleBible.md` for the per-realm art direction this toolkit
implements, and `Docs/Art/BeachTileSet.md` for the older AI-generated beach set.

Conventions (binding for every realm module):
  * `CELL` output pixels per 64 px map cell (2x the in-game size, so sprites stay
    crisp when the canvas_items stretch scales the 1152x648 base view up).
  * Images are float32 HxWx4 arrays, straight (non-premultiplied) RGB + alpha in
    [0, 1], composited with `over`. sRGB-space maths: this is stylised art.
  * Everything random takes an explicit integer seed. No global RNG state.

Requires Pillow + numpy (`/opt/homebrew/bin/python3` on the dev Mac).
"""

from __future__ import annotations

import json
import math
import zlib
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

CELL = 128                      # output px per map cell
REPO = Path(__file__).resolve().parents[2]
TILES_ROOT = REPO / "Assets" / "Sprites" / "Tiles"

# --------------------------------------------------------------------- colour


def rgb(hex_or_tuple):
    """'#RRGGBB' / (r,g,b) 0-255 / (r,g,b) 0-1 -> float32 array in [0, 1]."""
    if isinstance(hex_or_tuple, str):
        h = hex_or_tuple.lstrip("#")
        return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255.0
    a = np.asarray(hex_or_tuple, np.float32)
    return a / 255.0 if a.max() > 1.0 else a


def mix(a, b, t):
    """Linear blend of two colours (or colour fields); t may be a scalar or HxW."""
    a = np.asarray(a, np.float32)
    b = np.asarray(b, np.float32)
    t = np.asarray(t, np.float32)
    if t.ndim == 2:
        t = t[..., None]
    return a + (b - a) * t


def ramp(t, stops):
    """Piecewise-linear colour ramp. `stops` = [(pos, colour), ...] ascending."""
    t = np.clip(np.asarray(t, np.float32), 0.0, 1.0)
    out = np.zeros(t.shape + (3,), np.float32)
    pos = [s[0] for s in stops]
    cols = [rgb(s[1]) for s in stops]
    out[...] = cols[0]
    for i in range(len(stops) - 1):
        span = max(pos[i + 1] - pos[i], 1e-6)
        k = np.clip((t - pos[i]) / span, 0.0, 1.0)[..., None]
        seg = cols[i] + (cols[i + 1] - cols[i]) * k
        out = np.where((t >= pos[i])[..., None], seg, out)
    return out


def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, np.float32) - e0) / (e1 - e0 + 1e-9), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def seed_of(*parts) -> int:
    """Stable 31-bit seed from any mix of strings/ints (never Python's salted hash)."""
    return zlib.crc32("|".join(str(p) for p in parts).encode()) & 0x7FFFFFFF


# ---------------------------------------------------------------------- noise


def _hash01(ix, iy, seed):
    """Vectorised integer lattice hash -> float in [0, 1)."""
    h = (ix.astype(np.uint64) * np.uint64(374761393)
         + iy.astype(np.uint64) * np.uint64(668265263)
         + np.uint64(seed & 0xFFFFFFFF) * np.uint64(2246822519)) & np.uint64(0xFFFFFFFF)
    h = ((h ^ (h >> np.uint64(13))) * np.uint64(1274126177)) & np.uint64(0xFFFFFFFF)
    h = h ^ (h >> np.uint64(16))
    return (h & np.uint64(0xFFFFFF)).astype(np.float32) / np.float32(0x1000000)


def vnoise(x, y, seed=0, period=None):
    """Smooth value noise sampled at lattice coordinates x, y (arrays, any shape).

    `period` = (px, py) in lattice units wraps the field (None on an axis = no wrap),
    which is how ground atlases are made seamless across their variant strip.
    """
    x = np.asarray(x, np.float32)
    y = np.asarray(y, np.float32)
    x0 = np.floor(x)
    y0 = np.floor(y)
    fx = x - x0
    fy = y - y0
    fx = fx * fx * (3.0 - 2.0 * fx)
    fy = fy * fy * (3.0 - 2.0 * fy)
    ix0 = x0.astype(np.int64)
    iy0 = y0.astype(np.int64)
    ix1 = ix0 + 1
    iy1 = iy0 + 1
    if period is not None:
        px, py = period
        if px:
            ix0 = np.mod(ix0, int(px))
            ix1 = np.mod(ix1, int(px))
        if py:
            iy0 = np.mod(iy0, int(py))
            iy1 = np.mod(iy1, int(py))
    off = np.int64(1 << 20)     # keep hashed integers non-negative
    v00 = _hash01(ix0 + off, iy0 + off, seed)
    v10 = _hash01(ix1 + off, iy0 + off, seed)
    v01 = _hash01(ix0 + off, iy1 + off, seed)
    v11 = _hash01(ix1 + off, iy1 + off, seed)
    top = v00 + (v10 - v00) * fx
    bot = v01 + (v11 - v01) * fx
    return top + (bot - top) * fy


def fbm(x, y, seed=0, octaves=4, gain=0.5, lacunarity=2.0, period=None):
    """Fractal sum of `vnoise`, normalised to roughly [0, 1]."""
    total = np.zeros(np.broadcast(np.asarray(x), np.asarray(y)).shape, np.float32)
    amp = 1.0
    norm = 0.0
    freq = 1.0
    for o in range(octaves):
        per = None
        if period is not None:
            per = (int(round(period[0] * freq)) if period[0] else 0,
                   int(round(period[1] * freq)) if period[1] else 0)
        total += amp * vnoise(np.asarray(x) * freq, np.asarray(y) * freq, seed + 1013 * o, per)
        norm += amp
        amp *= gain
        freq *= lacunarity
    return total / norm


def grid(h, w, x0=0.0, y0=0.0):
    """Pixel-centre coordinate fields (xx, yy) for an h x w image."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    return xx + 0.5 + x0, yy + 0.5 + y0


def field(h, w, scale, seed=0, octaves=4, gain=0.5, period_px=None, x0=0.0, y0=0.0, stretch=(1.0, 1.0)):
    """fbm image helper. `scale` = feature size in px; `period_px` = (w, h) wrap in px."""
    xx, yy = grid(h, w, x0, y0)
    sx = scale * stretch[0]
    sy = scale * stretch[1]
    per = None
    if period_px is not None:
        per = (max(1, int(round(period_px[0] / sx))) if period_px[0] else 0,
               max(1, int(round(period_px[1] / sy))) if period_px[1] else 0)
        if per[0]:
            sx = period_px[0] / per[0]
        if per[1]:
            sy = period_px[1] / per[1]
    return fbm(xx / sx, yy / sy, seed, octaves, gain, period=per)


# ----------------------------------------------------------------------- blur


def _box1d(a, r, axis, wrap=False):
    if r < 1:
        return a
    r = int(r)
    a = np.swapaxes(a, 0, axis)
    n = a.shape[0]
    if wrap:
        pad = np.concatenate([a[-r:], a, a[:r]], axis=0)
    else:
        pad = np.concatenate([np.repeat(a[:1], r, 0), a, np.repeat(a[-1:], r, 0)], axis=0)
    c = np.cumsum(pad, axis=0, dtype=np.float64)
    c = np.concatenate([np.zeros_like(c[:1]), c], axis=0)
    out = (c[2 * r + 1:2 * r + 1 + n] - c[:n]) / (2 * r + 1)
    return np.swapaxes(out.astype(np.float32), 0, axis)


def blur(a, radius, wrap_x=False, wrap_y=False):
    """Gaussian-ish blur (three box passes). Works on HxW or HxWxC float arrays."""
    if radius <= 0.3:
        return a
    r = max(1, int(round(radius * 0.62)))
    out = a.astype(np.float32)
    for _ in range(3):
        out = _box1d(out, r, 0, wrap_y)
        out = _box1d(out, r, 1, wrap_x)
    return out


# ---------------------------------------------------------------------- masks

SS = 3  # supersampling for PIL-rasterised vector masks


def _down(img_l, h, w):
    return np.asarray(img_l.resize((w, h), Image.LANCZOS), np.float32) / 255.0


def poly_mask(h, w, pts):
    """Anti-aliased polygon coverage (0..1). `pts` in output px."""
    im = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(im).polygon([(float(x) * SS, float(y) * SS) for x, y in pts], fill=255)
    return np.clip(_down(im, h, w), 0.0, 1.0)


def polys_mask(h, w, polys):
    im = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(im)
    for pts in polys:
        d.polygon([(float(x) * SS, float(y) * SS) for x, y in pts], fill=255)
    return np.clip(_down(im, h, w), 0.0, 1.0)


def ellipse_mask(h, w, cx, cy, rx, ry):
    im = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(im).ellipse([(cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS], fill=255)
    return np.clip(_down(im, h, w), 0.0, 1.0)


def rect_mask(h, w, x0, y0, x1, y1, radius=0.0):
    im = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(im)
    box = [x0 * SS, y0 * SS, x1 * SS, y1 * SS]
    if radius > 0:
        d.rounded_rectangle(box, radius=radius * SS, fill=255)
    else:
        d.rectangle(box, fill=255)
    return np.clip(_down(im, h, w), 0.0, 1.0)


def line_mask(h, w, pts, width):
    """Anti-aliased constant-width polyline (round joints)."""
    im = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(im)
    p = [(float(x) * SS, float(y) * SS) for x, y in pts]
    d.line(p, fill=255, width=max(1, int(round(width * SS))), joint="curve")
    r = width * SS / 2.0
    for x, y in (p[0], p[-1]):
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return np.clip(_down(im, h, w), 0.0, 1.0)


def sdf_circle(h, w, cx, cy, r):
    """Signed distance (negative inside) to a circle, in px."""
    xx, yy = grid(h, w)
    return np.hypot(xx - cx, yy - cy) - r


def sdf_box(h, w, cx, cy, hx, hy, radius=0.0):
    """Signed distance to a (rounded) axis-aligned box centred at (cx, cy)."""
    xx, yy = grid(h, w)
    qx = np.abs(xx - cx) - (hx - radius)
    qy = np.abs(yy - cy) - (hy - radius)
    outside = np.hypot(np.maximum(qx, 0.0), np.maximum(qy, 0.0))
    inside = np.minimum(np.maximum(qx, qy), 0.0)
    return outside + inside - radius


def sdf_segment(h, w, p0, p1, r=0.0):
    """Signed distance to a capsule (segment p0-p1 thickened by r)."""
    xx, yy = grid(h, w)
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    l2 = max(dx * dx + dy * dy, 1e-6)
    t = np.clip(((xx - x0) * dx + (yy - y0) * dy) / l2, 0.0, 1.0)
    return np.hypot(xx - (x0 + t * dx), yy - (y0 + t * dy)) - r


def radial_glow(img, cx, cy, sigma, color, amount, squash=1.0):
    """Soft halo that is windowed to exactly zero at the canvas edge, so a glow can never
    show the rectangle of the sprite it lives in."""
    h, w = img.shape[:2]
    xx, yy = grid(h, w)
    d2 = (xx - cx) ** 2 + ((yy - cy) / squash) ** 2
    halo = np.exp(-d2 / (2.0 * sigma * sigma))
    edge = np.minimum(np.minimum(xx, w - xx), np.minimum(yy, h - yy))
    reach = max(1.0, min(cx, w - cx, cy, h - cy))
    halo *= smoothstep(0.0, reach * 0.55, edge)
    return add_light(img, color, halo * amount, only_inside=False)


def aa(sdf, width=1.0):
    """Signed distance -> anti-aliased coverage."""
    return np.clip(0.5 - sdf / width, 0.0, 1.0)


def bezier(p0, p1, p2, p3=None, n=24):
    """Quadratic (3 pts) or cubic (4 pts) Bezier sampled into n+1 points (Nx2)."""
    t = np.linspace(0.0, 1.0, n + 1, dtype=np.float32)[:, None]
    p0, p1, p2 = (np.asarray(p, np.float32) for p in (p0, p1, p2))
    if p3 is None:
        return (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t ** 2 * p2
    p3 = np.asarray(p3, np.float32)
    return ((1 - t) ** 3 * p0 + 3 * (1 - t) ** 2 * t * p1
            + 3 * (1 - t) * t ** 2 * p2 + t ** 3 * p3)


# ----------------------------------------------------------------- compositing


def new(h, w):
    return np.zeros((h, w, 4), np.float32)


def cells(wc, hc):
    """Blank image sized for a wc x hc cell footprint."""
    return new(int(round(hc * CELL)), int(round(wc * CELL)))


def over(img, color, alpha):
    """Composite `color` (3-vector or HxWx3) with coverage `alpha` (HxW) over img, in place."""
    a = np.clip(np.asarray(alpha, np.float32), 0.0, 1.0)
    c = np.asarray(color, np.float32)
    if c.ndim == 1:
        c = np.broadcast_to(c, img.shape[:2] + (3,))
    da = img[..., 3]
    out_a = a + da * (1.0 - a)
    safe = np.maximum(out_a, 1e-6)
    img[..., :3] = (c * a[..., None] + img[..., :3] * (da * (1.0 - a))[..., None]) / safe[..., None]
    img[..., 3] = out_a
    return img


def over_img(img, src, opacity=1.0, x=0, y=0):
    """Composite another RGBA float image over `img` at integer offset (x, y)."""
    h, w = src.shape[:2]
    H, W = img.shape[:2]
    sx0, sy0 = max(0, -x), max(0, -y)
    dx0, dy0 = max(0, x), max(0, y)
    cw = min(w - sx0, W - dx0)
    ch = min(h - sy0, H - dy0)
    if cw <= 0 or ch <= 0:
        return img
    sub = img[dy0:dy0 + ch, dx0:dx0 + cw]
    s = src[sy0:sy0 + ch, sx0:sx0 + cw]
    over(sub, s[..., :3], s[..., 3] * opacity)
    return img


def back(img, color, threshold=0.10, grow=1.2):
    """Slip an opaque sheet of `color` BEHIND everything painted so far, cut to its silhouette.

    Ink and watercolour washes are translucent by nature, which is right for foliage and mist
    but wrong for solid things — a wall you can see the far mountains through reads as a ghost.
    Solid subjects call this last, so the washes keep their bloom while the object blocks.
    """
    a = img[..., 3]
    sil = smoothstep(threshold, threshold + 0.25, blur(a, grow))
    sil = np.maximum(sil, a)
    out_a = sil
    rgb_under = np.asarray(color, np.float32)
    img[..., :3] = img[..., :3] * a[..., None] + rgb_under * (1.0 - a[..., None])
    img[..., 3] = out_a
    return img


def tint_inside(img, color, alpha):
    """Recolour only where the image already has coverage (keeps the silhouette)."""
    a = np.clip(np.asarray(alpha, np.float32), 0.0, 1.0)[..., None]
    c = np.asarray(color, np.float32)
    if c.ndim == 1:
        c = np.broadcast_to(c, img.shape[:2] + (3,))
    img[..., :3] = img[..., :3] * (1.0 - a) + c * a
    return img


def multiply_inside(img, factor):
    """Darken/lighten RGB by a scalar or HxW factor, alpha untouched."""
    f = np.asarray(factor, np.float32)
    if f.ndim == 2:
        f = f[..., None]
    img[..., :3] = np.clip(img[..., :3] * f, 0.0, 1.0)
    return img


def add_light(img, color, amount, only_inside=True):
    """Additive glow. `amount` HxW. With only_inside=False the glow also raises alpha."""
    a = np.clip(np.asarray(amount, np.float32), 0.0, 4.0)
    c = np.asarray(color, np.float32)
    if only_inside:
        img[..., :3] = np.clip(img[..., :3] + c * a[..., None], 0.0, 1.0)
    else:
        da = img[..., 3]
        glow_a = np.clip(a, 0.0, 1.0)
        out_a = glow_a + da * (1.0 - glow_a)
        base = img[..., :3] * da[..., None]
        img[..., :3] = np.clip((base + c * a[..., None]) / np.maximum(out_a, 1e-6)[..., None], 0.0, 1.0)
        img[..., 3] = out_a
    return img


def grain(img, amount=0.03, seed=0, scale=1.6):
    """Subtle luminance grain inside existing coverage (paper / stone tooth)."""
    h, w = img.shape[:2]
    n = field(h, w, scale, seed, octaves=2) - 0.5
    img[..., :3] = np.clip(img[..., :3] + n[..., None] * amount * 2.0, 0.0, 1.0)
    return img


def shift(a, dx, dy):
    """Translate an array by whole pixels, filling with zeros (no wrap)."""
    out = np.zeros_like(a)
    h, w = a.shape[:2]
    xs0, xs1 = max(0, -dx), min(w, w - dx)
    ys0, ys1 = max(0, -dy), min(h, h - dy)
    if xs1 > xs0 and ys1 > ys0:
        out[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx] = a[ys0:ys1, xs0:xs1]
    return out


def flip_h(img):
    return img[:, ::-1].copy()


def resize(img, w, h):
    """High-quality resample of a float RGBA image (premultiplied to avoid fringes)."""
    pm = img.copy()
    pm[..., :3] *= pm[..., 3:4]
    chans = [np.asarray(Image.fromarray(pm[..., i], "F").resize((w, h), Image.LANCZOS), np.float32)
             for i in range(4)]
    out = np.stack(chans, axis=-1)
    a = np.clip(out[..., 3:4], 0.0, 1.0)
    out[..., :3] = np.where(a > 1e-4, out[..., :3] / np.maximum(a, 1e-4), 0.0)
    out[..., 3:4] = a
    return np.clip(out, 0.0, 1.0)


# --------------------------------------------------------------- brush strokes


def stroke_field(h, w, pts, half_widths, soft=1.2):
    """Rasterise a variable-width polyline.

    Returns (cov, u, v): coverage 0..1, arc length along the stroke in px, and the
    signed across-stroke coordinate in [-1, 1]. The (u, v) frame is what lets a brush
    texture follow the stroke direction (dry-brush streaks, edge pooling).
    """
    pts = np.asarray(pts, np.float32)
    hw = np.broadcast_to(np.asarray(half_widths, np.float32), (len(pts),)).astype(np.float32)
    cov = np.zeros((h, w), np.float32)
    U = np.zeros((h, w), np.float32)
    V = np.zeros((h, w), np.float32)
    seg = np.hypot(*(pts[1:] - pts[:-1]).T)
    cum = np.concatenate([[0.0], np.cumsum(seg)])
    for i in range(len(pts) - 1):
        (x0, y0), (x1, y1) = pts[i], pts[i + 1]
        w0, w1 = hw[i], hw[i + 1]
        pad = max(w0, w1) + soft + 1.0
        bx0 = int(max(0, math.floor(min(x0, x1) - pad)))
        bx1 = int(min(w, math.ceil(max(x0, x1) + pad)))
        by0 = int(max(0, math.floor(min(y0, y1) - pad)))
        by1 = int(min(h, math.ceil(max(y0, y1) + pad)))
        if bx1 <= bx0 or by1 <= by0:
            continue
        yy, xx = np.mgrid[by0:by1, bx0:bx1].astype(np.float32)
        xx += 0.5
        yy += 0.5
        dx, dy = x1 - x0, y1 - y0
        l2 = dx * dx + dy * dy
        if l2 < 1e-6:
            continue
        t = np.clip(((xx - x0) * dx + (yy - y0) * dy) / l2, 0.0, 1.0)
        dist = np.hypot(xx - (x0 + t * dx), yy - (y0 + t * dy))
        wd = w0 + (w1 - w0) * t
        c = np.clip((wd - dist) / soft + 0.5, 0.0, 1.0)
        side = np.sign((xx - x0) * dy - (yy - y0) * dx)
        sub = cov[by0:by1, bx0:bx1]
        m = c > sub
        sub[m] = c[m]
        U[by0:by1, bx0:bx1][m] = (cum[i] + t * math.sqrt(l2))[m]
        V[by0:by1, bx0:bx1][m] = (side * dist / np.maximum(wd, 0.5))[m]
    return cov, U, np.clip(V, -1.0, 1.0)


def ink_stroke(img, pts, half_widths, color, opacity=1.0, dry=0.35, edge=0.35,
               seed=0, load=0.0, grad=0.0, streak_freq=5.0, soft=1.2):
    """One loaded-brush stroke of ink (the Wulin realm's basic mark).

    dry   : 0..1 — how much "flying white" (飞白) streaking breaks the stroke up.
    edge  : ink pooling toward the stroke's rims.
    load  : -1..1 — where the ink runs out: >0 fades toward the end, <0 toward the start.
    grad  : -1..1 — one side of the bristles carries lighter ink (cylinder modelling).
    """
    # Work inside the stroke's own bounding box: a bamboo carries hundreds of leaves, and
    # evaluating brush noise over the whole canvas for each would be quadratic.
    pts = np.asarray(pts, np.float32)
    hw_arr = np.broadcast_to(np.asarray(half_widths, np.float32), (len(pts),))
    pad = float(hw_arr.max()) + soft + 3.0
    H, W = img.shape[:2]
    bx0 = int(max(0, math.floor(float(pts[:, 0].min()) - pad)))
    bx1 = int(min(W, math.ceil(float(pts[:, 0].max()) + pad)))
    by0 = int(max(0, math.floor(float(pts[:, 1].min()) - pad)))
    by1 = int(min(H, math.ceil(float(pts[:, 1].max()) + pad)))
    if bx1 <= bx0 or by1 <= by0:
        return img
    full = img
    img = full[by0:by1, bx0:bx1]
    pts = pts - np.array([bx0, by0], np.float32)
    h, w = img.shape[:2]
    cov, U, V = stroke_field(h, w, pts, hw_arr, soft)
    if cov.max() <= 0.0:
        return full
    length = max(float(U.max()), 1.0)
    un = U / length
    streak = vnoise(V * streak_freq + seed * 0.37, U / max(length * 0.22, 24.0), seed)
    fine = vnoise(V * streak_freq * 3.1 + 7.0, U / 26.0, seed + 5)
    tex = 0.62 * streak + 0.38 * fine
    run_out = np.zeros_like(un)
    if load > 0:
        run_out = smoothstep(0.25, 1.0, un) * load
    elif load < 0:
        run_out = smoothstep(0.75, 0.0, un) * (-load)
    dryness = np.clip(dry * (0.55 + 0.9 * np.sin(np.pi * np.clip(un, 0, 1)) ** 2) + run_out * 0.6, 0.0, 0.97)
    thr = 1.0 - dryness
    keep = 1.0 - smoothstep(thr - 0.10, thr + 0.12, tex) * np.clip(dryness * 1.6, 0.0, 1.0)
    a = cov * opacity * keep
    a *= 1.0 + edge * np.abs(V) ** 3
    if grad:
        a *= 1.0 - np.clip(grad, -1, 1) * 0.5 * V - abs(grad) * 0.18
    xx, yy = grid(h, w, bx0, by0)
    tooth = vnoise(xx / 2.3, yy / 2.3, seed + 99)
    a *= 0.86 + 0.20 * tooth
    over(img, color, np.clip(a, 0.0, 1.0))
    return full


def pin_corners(img):
    """Give the four corner pixels a 1/255 alpha so Godot's `Image.get_used_rect()` —
    which the map editor uses to trim and aspect-fit prop sprites — reports the whole
    canvas. The canvas is exactly the tile footprint, so pinned art lands where it was
    painted in both the editor and the runtime, and multi-cell pieces can be registered
    against each other (stackable stalks, tiling horizons, walkable roof ridges)."""
    for y in (0, -1):
        for x in (0, -1):
            if img[y, x, 3] < 1.0 / 255.0:
                img[y, x, 3] = 1.0 / 255.0
    return img


def wash(img, mask, color, opacity=0.6, seed=0, blot=42.0, rim=0.35, grain_amt=0.10):
    """A puddle of diluted ink/watercolour: blotchy density, darker drying rim."""
    h, w = mask.shape
    xx, yy = grid(h, w)
    blotch = fbm(xx / blot, yy / blot, seed, octaves=4)
    dens = 0.70 + 0.55 * (blotch - 0.5)
    soft = blur(mask, 3.0)
    rim_band = np.clip((mask - soft) * 3.2, 0.0, 1.0)
    tooth = vnoise(xx / 2.1, yy / 2.1, seed + 3)
    a = mask * opacity * dens * (1.0 - grain_amt + grain_amt * 2 * tooth)
    a = np.clip(a + rim_band * rim * opacity, 0.0, 1.0) * np.clip(mask * 1.2, 0.0, 1.0)
    over(img, color, a)
    return img


def rough(mask, amount=2.0, scale=10.0, seed=0):
    """Wobble a mask's edge (hand-cut / carved / torn silhouettes)."""
    h, w = mask.shape
    xx, yy = grid(h, w)
    n = fbm(xx / scale, yy / scale, seed, octaves=3) - 0.5
    soft = blur(mask, max(amount, 1.0))
    return smoothstep(0.5 - 0.06, 0.5 + 0.06, soft + n * 0.55)


# --------------------------------------------------------- height-field shading


def normals(height, strength=1.0):
    gy, gx = np.gradient(height.astype(np.float32))
    nx = -gx * strength
    ny = -gy * strength
    nz = np.ones_like(nx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return nx * inv, ny * inv, nz * inv


def lit(height, albedo, light=(-0.45, -0.62, 0.64), ambient=0.34, strength=1.6,
        spec=0.0, spec_pow=18.0, ao=0.0, ao_radius=8.0, rim=None):
    """Light an albedo map with a height field (the Dungeon realm's realism).

    Returns HxWx3 colour. `rim` = (colour, direction xyz, amount) adds a second coloured
    light — used for the warm lava bounce from below against the cold key light.
    """
    nx, ny, nz = normals(height, strength)
    l = np.asarray(light, np.float32)
    l = l / np.linalg.norm(l)
    diff = np.clip(nx * l[0] + ny * l[1] + nz * l[2], 0.0, 1.0)
    shade = ambient + (1.0 - ambient) * diff
    if ao > 0:
        occl = np.clip((blur(height, ao_radius) - height) * ao, 0.0, 0.85)
        shade *= 1.0 - occl
    col = np.asarray(albedo, np.float32) * shade[..., None]
    if spec > 0:
        hv = l + np.array([0.0, 0.0, 1.0], np.float32)
        hv = hv / np.linalg.norm(hv)
        s = np.clip(nx * hv[0] + ny * hv[1] + nz * hv[2], 0.0, 1.0) ** spec_pow
        col = col + spec * s[..., None]
    if rim is not None:
        rc, rd, ra = rim
        rd = np.asarray(rd, np.float32)
        rd = rd / np.linalg.norm(rd)
        r = np.clip(nx * rd[0] + ny * rd[1] + nz * rd[2], 0.0, 1.0) ** 1.5
        col = col + np.asarray(rc, np.float32) * (r * ra)[..., None]
    return np.clip(col, 0.0, 1.0)


class Relief:
    """Painter's-order relief builder for the lit ("realistic") look.

    Each `add` lays a shape down with its own height profile and albedo; `render` lights
    the accumulated height field once, so overlapping parts shade and occlude each other
    like one object instead of a stack of separately-lit stickers.
    """

    def __init__(self, h, w):
        self.h, self.w = h, w
        self.height = np.zeros((h, w), np.float32)
        self.mask = np.zeros((h, w), np.float32)
        self.albedo = np.zeros((h, w, 3), np.float32)
        self.gloss = np.zeros((h, w), np.float32)
        self.emit = np.zeros((h, w, 3), np.float32)

    def add(self, mask, height, albedo, z=0.0, gloss=0.0):
        m = np.clip(mask, 0.0, 1.0)
        alb = np.asarray(albedo, np.float32)
        if alb.ndim == 1:
            alb = np.broadcast_to(alb, (self.h, self.w, 3))
        self.height = self.height * (1.0 - m) + (z + height) * m
        self.albedo = self.albedo * (1.0 - m[..., None]) + alb * m[..., None]
        self.gloss = self.gloss * (1.0 - m) + gloss * m
        self.mask = np.maximum(self.mask, m)
        return self

    def glow(self, color, amount):
        self.emit += np.asarray(color, np.float32) * np.clip(amount, 0.0, 4.0)[..., None]
        return self

    def render(self, light=(-0.45, -0.62, 0.64), ambient=0.26, strength=1.5, ao=0.05, ao_radius=7.0,
               rim=None, tint=(1.0, 1.0, 1.0)):
        col = lit(self.height, self.albedo, light, ambient, strength, ao=ao, ao_radius=ao_radius, rim=rim)
        if self.gloss.max() > 0:
            nx, ny, nz = normals(self.height, strength)
            l = np.asarray(light, np.float32)
            hv = l / np.linalg.norm(l) + np.array([0.0, 0.0, 1.0], np.float32)
            hv = hv / np.linalg.norm(hv)
            s = np.clip(nx * hv[0] + ny * hv[1] + nz * hv[2], 0.0, 1.0) ** 26.0
            col = col + (s * self.gloss)[..., None]
        col = np.clip(col * np.asarray(tint, np.float32) + self.emit, 0.0, 1.0)
        img = new(self.h, self.w)
        img[..., :3] = col
        img[..., 3] = self.mask
        return img


def round_h(sdf, radius):
    """Height of a half-cylinder / dome from a signed distance (inside is negative)."""
    t = np.clip(-sdf / max(radius, 1e-3), 0.0, 1.0)
    return radius * np.sqrt(np.clip(1.0 - (1.0 - t) ** 2, 0.0, 1.0))


def bevel_h(sdf, width, top):
    """Flat-topped plateau `top` high with a chamfer `width` px wide."""
    return np.clip(-sdf / max(width, 1e-3), 0.0, 1.0) * top


def pillow(mask, radius, top):
    """Rounded relief for an arbitrary mask (no analytic distance needed)."""
    return np.clip((blur(mask, radius) - 0.5) * 2.0, 0.0, 1.0) * top


# ------------------------------------------------------------------- export


def to_pil(img):
    arr = np.clip(img, 0.0, 1.0)
    out = (arr * 255.0 + 0.5).astype(np.uint8)
    # Zero the colour under fully transparent pixels so PNGs compress and never fringe.
    out[out[..., 3] == 0, :3] = 0
    return Image.fromarray(out, "RGBA")


def alpha_bbox(img, threshold=4.0 / 255.0):
    ys, xs = np.where(img[..., 3] > threshold)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def save_sprite(img, realm_dir, category, name, walk_top=None):
    """Write a prop sprite whose canvas IS its footprint (see `pin_corners`).

    `walk_top` guards walkable art (platforms): the paint must reach within that many
    px of the canvas top, because the collider's standing surface is the footprint top
    and art that stops short would leave the player hovering above it.
    """
    out_dir = TILES_ROOT / realm_dir / category
    out_dir.mkdir(parents=True, exist_ok=True)
    box = alpha_bbox(img, 40.0 / 255.0)
    if box is None:
        raise ValueError(f"{name}: sprite is fully transparent")
    if walk_top is not None and box[1] > walk_top:
        raise ValueError(f"{name}: walkable art starts {box[1]} px below the canvas top (limit {walk_top})")
    img = pin_corners(img.copy())
    path = out_dir / f"{name}.png"
    to_pil(img).save(path, optimize=True)
    return path


def save_fill(img, realm_dir, category, name):
    """Write a fill-footprint texture (ground atlas, lava) with no cropping."""
    out_dir = TILES_ROOT / realm_dir / category
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{name}.png"
    to_pil(img).save(path, optimize=True)
    return path


def write_manifest(png_path, tile_id, display_name, role, w, h, prompt, fill=False,
                   autotile_group=None, autotile_kind=None, art_source=None, extra=None):
    """`<name>.tile.json` sidecar (MapCreation.gdd §2.5 TileManifest schema). `prompt`
    records the art brief, so a later hand-painted or AI replacement is a one-file swap."""
    data = {
        "id": tile_id,
        "displayName": display_name,
        "role": role,
        "footprint": {"w": int(w), "h": int(h)},
        "fillFootprint": bool(fill),
    }
    if autotile_group:
        data["autotileGroup"] = autotile_group
    if autotile_kind:
        data["autotileKind"] = autotile_kind
    if art_source:
        data["artSource"] = art_source
    data["prompt"] = prompt
    if extra:
        data.update(extra)
    side = Path(str(png_path)[:-4] + ".tile.json")
    side.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return side


def export_set(realm_dir, set_id, brief, items, sheet_path=None, sheet_bg=(40, 40, 48), sheet_cell=56):
    """Write every sprite of a realm tile set plus its `.tile.json` sidecars.

    `items` rows: (tile_id, display_name, role, w, h, category_dir, file_stem, image, subject, extra).
    A fill material (`extra["fill"]`) is a layered-hill atlas: it is saved as
    `<stem>_atlas.png`, and its Layer-1 Mid cell is saved as `<stem>.png` — the palette
    thumbnail and the graceful fallback fill, mirroring `ground_sand_seamless.png` beside
    the beach hill atlas. Everything else is a pinned-canvas prop.
    """
    sheet = []
    for (tid, name, role, w, h, cat, stem, img, subject, extra) in items:
        prompt = brief + " SUBJECT: " + subject
        if extra.get("fill"):
            variants = int(extra.get("variants", 4))
            atlas_path = save_fill(img, realm_dir, cat, stem + "_atlas")
            path = save_fill(img[0:CELL, CELL:2 * CELL], realm_dir, cat, stem)
            write_manifest(path, tid, name, role, w, h, prompt, fill=True, autotile_group=extra["group"],
                           autotile_kind="layered_hill_strip",
                           art_source="res://" + atlas_path.relative_to(REPO).as_posix(),
                           extra={"autotileVariants": variants, "tileSet": set_id})
            sheet.append((tid, img, variants * 4, 4))
        else:
            path = save_sprite(img, realm_dir, cat, stem, walk_top=extra.get("walk"))
            # A prop can belong to a terrain group without being an atlas: a slope block is
            # one sprite, but the earth beside and under it must count it as more earth.
            write_manifest(path, tid, name, role, w, h, prompt, autotile_group=extra.get("group"),
                           extra={"tileSet": set_id})
            sheet.append((tid, img, w, h))
        print(f"  {tid:34s} {img.shape[1]}x{img.shape[0]}  -> {path.relative_to(REPO)}")
    if sheet_path:
        contact_sheet(sheet, sheet_path, bg=sheet_bg, cell_px=sheet_cell, cols_px=1700)
        print("sheet:", sheet_path)


# ------------------------------------------------------------ ground atlases

POSITIONS = ("left", "mid", "right", "peak")


def build_hill_atlas(render_cell, variants=4):
    """Compose a layered-hill ground atlas (Scripts/MapCreation/Data/HillAutotile.cs).

    Layout: 4 rows = depth layers L1..L4; columns come in `variants` groups of four
    [Left, Mid, Right, Peak]. A cell at map column x uses group `x mod variants`, so the
    base material can be one continuous strip `variants` cells wide — no two neighbouring
    cells ever repeat the same paint, and every seam matches by construction.

    `render_cell(group, layer, open_left, open_right)` returns a CELL x CELL RGBA tile.
    Unlike the hand-generated beach atlas, every layer gets a Peak (both sides open).
    """
    atlas = new(4 * CELL, variants * 4 * CELL)
    for g in range(variants):
        for layer in range(4):
            for p, name in enumerate(POSITIONS):
                tile = render_cell(g, layer, name in ("left", "peak"), name in ("right", "peak"))
                x = (g * 4 + p) * CELL
                atlas[layer * CELL:(layer + 1) * CELL, x:x + CELL] = tile
    return atlas


def strip_atlas(base, cap=None, side=None, underside=None, variants=4):
    """A layered-hill strip atlas from one continuous material strip (`variants` cells wide,
    one tall) plus up to three treatments, each `f(cell, group[, left])` editing a cell in
    place: `cap` for the walkable top (L1), `side` for an exposed flank, `underside` for the
    bottom layer (L4). The body rows are the bare strip, so any stacking is seamless."""
    def render(g, layer, open_left, open_right):
        cell = cut(base, g)
        if layer == 3 and underside is not None:
            underside(cell, g)
        if open_left and side is not None:
            side(cell, g, True)
        if open_right and side is not None:
            side(cell, g, False)
        if layer == 0 and cap is not None:
            cap(cell, g)
        return cell
    return build_hill_atlas(render, variants)


def carve_top(cell, g, seed, variants=4, base=12.0, wobble=10.0):
    """Cut a cell's walkable top along a line that runs unbroken across the whole strip.
    Returns the depth below that line (px; negative is the air that was cut away)."""
    gx, gy = grid(CELL, CELL, g * CELL, 0)
    lip = fbm(gx / 32.0, gy * 0 + 3.3, seed, octaves=3, period=(variants * CELL // 32, 0))
    top = base + (lip - 0.5) * wobble
    cell[..., 3] *= smoothstep(top - 1.2, top + 1.2, gy)
    return gy - top


def carve_side(cell, left, seed, inset=8.0, wobble=8.0):
    """Cut an exposed flank. The wobble dies away at the cell's top and bottom, so stacked
    flanks line up. Returns the distance inward from the new edge (px)."""
    h, w = cell.shape[:2]
    gx, gy = grid(h, w)
    wob = (fbm(gy / 36.0, gx * 0 + (5.1 if left else 9.4), seed, octaves=3) - 0.5) * wobble
    edge = inset + wob * np.clip(np.minimum(gy, h - gy) / 22.0, 0.0, 1.0)
    dist = gx if left else (w - gx)
    cell[..., 3] *= smoothstep(edge - 1.2, edge + 1.2, dist)
    return dist - edge


def carve_under(cell, g, seed, variants=4, depth=50.0, inset=10.0):
    """Cut a ragged underside that runs unbroken across the strip. Returns the height above
    the cut (px; negative is what was cut away)."""
    gx, gy = grid(CELL, CELL, g * CELL, 0)
    lobes = fbm(gx / 64.0, gy * 0 + 1.0, seed, octaves=3, period=(variants * CELL // 64, 0))
    fine = fbm(gx / 8.0, gy * 0 + 2.0, seed + 1, octaves=2, period=(variants * CELL // 8, 0))
    cutoff = CELL - inset - lobes * depth - (fine - 0.5) * 10.0
    cell[..., 3] *= smoothstep(cutoff + 3.0, cutoff - 3.0, gy)
    return cutoff - gy


def strip(variants, rows=1):
    """Coordinate fields for a `variants` x `rows` cell material strip (px)."""
    return grid(rows * CELL, variants * CELL)


def cut(strip_img, group, row=0):
    """Take one CELL x CELL cell out of a material strip."""
    return strip_img[row * CELL:(row + 1) * CELL, group * CELL:(group + 1) * CELL].copy()


def contact_sheet(items, path, bg=(60, 64, 72), cell_px=96, cols_px=1500, label=True):
    """Debug sheet: [(name, img, w_cells, h_cells), ...] laid out at a common scale."""
    from PIL import ImageDraw as _D
    placed = []
    x = y = 12
    row_h = 0
    for name, img, wc, hc in items:
        box = alpha_bbox(img) or (0, 0, img.shape[1], img.shape[0])
        crop = img[box[1]:box[3], box[0]:box[2]]
        fw, fh = int(round(wc * cell_px)), int(round(hc * cell_px))
        s = min(fw / crop.shape[1], fh / crop.shape[0])
        pil = to_pil(crop).resize((max(1, int(crop.shape[1] * s)), max(1, int(crop.shape[0] * s))), Image.LANCZOS)
        if x + fw > cols_px - 12:
            x = 12
            y += row_h + 30
            row_h = 0
        placed.append((name, pil, x, y, fw, fh))
        x += fw + 16
        row_h = max(row_h, fh)
    sheet = Image.new("RGBA", (cols_px, y + row_h + 30), bg + (255,))
    d = _D.Draw(sheet)
    for name, pil, px, py, fw, fh in placed:
        d.rectangle([px, py, px + fw, py + fh], outline=(255, 255, 255, 40))
        sheet.alpha_composite(pil, (px + (fw - pil.width) // 2, py + fh - pil.height))
        if label:
            d.text((px, py + fh + 4), name, fill=(235, 235, 235, 255))
    sheet.convert("RGB").save(path)
    return path

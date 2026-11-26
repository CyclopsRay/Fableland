"""The furniture every torn-pages overworld shares, whatever its five worlds are.

These are the pieces that make a generated island read as *a map on a desk* before any
realm is recognised (`Docs/Art/OverworldStyle.md` §2): the desk's paper, the paper grain
every sheet is cut from, the char a devoured page turns into, the cut-paper waves, the
compass rose and the pins. They are drawn here by code — no image model — so they are the
same on every machine and can be re-cut at any size. Called by `build_kits.py`.

Light comes from the upper left, as in the kits, so every baked shadow falls to the lower
right. Nothing here contains lettering.
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SS = 4  # supersampling for everything drawn with ImageDraw


# ---------------------------------------------------------------------------- noise

def tile_noise(size: int, beta: float, seed: int) -> np.ndarray:
    """Seamlessly tiling noise with a 1/f^beta spectrum, zero mean, unit variance."""
    rng = np.random.default_rng(seed)
    spectrum = np.fft.fft2(rng.standard_normal((size, size)))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    radius = np.sqrt(fx * fx + fy * fy)
    radius[0, 0] = 1.0
    spectrum *= 1.0 / np.power(radius, beta)
    spectrum[0, 0] = 0.0
    noise = np.real(np.fft.ifft2(spectrum))
    return ((noise - noise.mean()) / noise.std()).astype(np.float32)


def wrap_blur(field: np.ndarray, sigma: float) -> np.ndarray:
    """Gaussian blur that wraps round the edges (so a tile stays a tile)."""
    size = field.shape[0]
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    kernel = np.exp(-2.0 * (math.pi * sigma) ** 2 * (fx * fx + fy * fy))
    return np.real(np.fft.ifft2(np.fft.fft2(field) * kernel)).astype(np.float32)


def fibres(size: int, count: int, seed: int, length=(5.0, 20.0)) -> np.ndarray:
    """Short curved paper fibres as a wrapped coverage field in 0..1."""
    rng = np.random.default_rng(seed)
    field = np.zeros((size, size), np.float32)
    for _ in range(count):
        x, y = rng.uniform(0, size, 2)
        angle = rng.uniform(0, math.tau)
        bend = rng.uniform(-0.08, 0.08)
        steps = int(rng.uniform(*length) * 2)
        weight = rng.uniform(0.35, 1.0)
        for _ in range(steps):
            field[int(y) % size, int(x) % size] += weight
            x += math.cos(angle) * 0.5
            y += math.sin(angle) * 0.5
            angle += bend
    return np.clip(wrap_blur(field, 0.6), 0.0, 1.0)


def _save_jpeg(rgb: np.ndarray, path: Path, quality: int = 88) -> Path:
    Image.fromarray(np.clip(rgb * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGB").save(
        path, "JPEG", quality=quality, optimize=True)
    return path


# ---------------------------------------------------------------------------- papers

def desk(path: Path) -> Path:
    """Sea-coloured desk paper, 512 px, tiling: a deep teal sheet with mottle, grain and fibres."""
    size = 512
    base = np.array([0.098, 0.318, 0.380], np.float32)
    tone = (tile_noise(size, 2.3, 11) * 0.030 + tile_noise(size, 1.3, 12) * 0.020
            + tile_noise(size, 0.25, 13) * 0.016)
    light = fibres(size, 1500, 14) * 0.085
    dark = fibres(size, 900, 15) * 0.070
    value = 1.0 + tone + light - dark
    rgb = base[None, None, :] * value[..., None]
    rgb[..., 1] += tile_noise(size, 2.0, 16) * 0.006      # a breath of green/blue drift
    rgb[..., 2] += tile_noise(size, 2.0, 17) * 0.008
    return _save_jpeg(np.clip(rgb, 0, 1), path, 88)


def paper(path: Path) -> Path:
    """White paper grain, 256 px, tiling, greyscale: every sheet in the collage is this
    texture multiplied by a colour (the deckle, the shallows, the river paper, the mist)."""
    size = 256
    value = (0.945 + tile_noise(size, 2.0, 21) * 0.012 + tile_noise(size, 0.9, 22) * 0.012
             + tile_noise(size, 0.1, 23) * 0.010)
    value += fibres(size, 420, 24, (4.0, 14.0)) * 0.040
    value -= fibres(size, 260, 25, (4.0, 12.0)) * 0.055
    image = Image.fromarray(np.clip(value * 255.0 + 0.5, 0, 255).astype(np.uint8), "L")
    image.save(path, "PNG", optimize=True)
    return path


def _torus_voronoi(size: int, points: int, seed: int) -> tuple[np.ndarray, np.ndarray]:
    """Nearest-site index and the gap to the second-nearest site, on a wrapping square: the
    cells are flakes, the small gaps are the cracks between them."""
    rng = np.random.default_rng(seed)
    sites = rng.uniform(0, size, (points, 2)).astype(np.float32)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    # Bend the plane a little first, so the cracks wander instead of running ruler-straight.
    xx = (xx + tile_noise(size, 2.2, seed + 101) * 5.0 + tile_noise(size, 1.2, seed + 102) * 1.6) % size
    yy = (yy + tile_noise(size, 2.2, seed + 103) * 5.0 + tile_noise(size, 1.2, seed + 104) * 1.6) % size
    pix = np.stack([xx.ravel(), yy.ravel()], axis=1)
    best = np.full(len(pix), 1e9, np.float32)
    second = np.full(len(pix), 1e9, np.float32)
    owner = np.zeros(len(pix), np.int32)
    for index, site in enumerate(sites):
        d = np.abs(pix - site[None, :])
        d = np.minimum(d, size - d)                    # wrap
        dist = np.sqrt((d * d).sum(axis=1))
        closer = dist < best
        second = np.where(closer, best, np.minimum(second, dist))
        owner = np.where(closer, index, owner)
        best = np.where(closer, dist, best)
    return owner.reshape(size, size), (second - best).reshape(size, size)


def char(path: Path) -> Path:
    """Burnt paper, 512 px, tiling: black char cracked into flakes, pale ash in the cracks,
    a few of them still hot. What a devoured page becomes (laid over the page, which is
    first darkened, so a ghost of the page's own medium survives under it)."""
    size, half = 512, 256
    owner, gap = _torus_voronoi(half, 150, 31)
    rng = np.random.default_rng(32)
    flake_tone = rng.uniform(0.62, 1.30, 150).astype(np.float32)[owner]
    hot_cell = (rng.uniform(0, 1, 150) < 0.065)[owner]
    up = lambda field: np.asarray(Image.fromarray(field.astype(np.float32), mode="F").resize((size, size), Image.BICUBIC))
    # upsampling a tile by 2 keeps it a tile (bicubic's edge error is under a pixel of crack)
    crack = np.clip(1.0 - up(gap) / 1.6, 0.0, 1.0) ** 1.5
    tone = up(flake_tone)
    hot = np.clip(up(hot_cell.astype(np.float32)), 0.0, 1.0)

    fine = tile_noise(size, 0.5, 33)
    mid = tile_noise(size, 1.6, 34)
    low = tile_noise(size, 2.3, 35)
    base = np.array([0.070, 0.056, 0.052], np.float32)
    rgb = base[None, None, :] * (tone * (1.0 + fine * 0.20 + mid * 0.14))[..., None]
    ash = np.clip(low * 0.7 + mid * 0.3 - 0.55, 0.0, 1.0)[..., None] * 0.50      # thin drifts of grey ash
    rgb = rgb * (1.0 - ash) + np.array([0.230, 0.215, 0.205], np.float32)[None, None, :] * ash
    pale = np.array([0.360, 0.330, 0.305], np.float32)
    ember = np.array([0.950, 0.360, 0.070], np.float32)
    crack_colour = pale[None, None, :] * (1.0 - hot[..., None]) + ember[None, None, :] * hot[..., None]
    strength = (crack * np.clip(0.50 + mid * 0.35, 0.15, 1.0))[..., None]
    rgb = rgb * (1.0 - strength) + crack_colour * strength
    return _save_jpeg(np.clip(rgb, 0, 1), path, 88)


# ---------------------------------------------------------------------------- cut paper

def _bezier(p0, p1, p2, p3, n=24):
    t = np.linspace(0.0, 1.0, n)[:, None]
    p0, p1, p2, p3 = (np.array(p, np.float64) for p in (p0, p1, p2, p3))
    return ((1 - t) ** 3) * p0 + 3 * ((1 - t) ** 2) * t * p1 + 3 * (1 - t) * (t ** 2) * p2 + (t ** 3) * p3


def _grain_rgba(mask: Image.Image, seed: int, shade_bottom: float = 0.10) -> Image.Image:
    """White cut paper under a mask: grain, a little shade toward the lower right."""
    w, h = mask.size
    size = 256
    grain = 0.955 + tile_noise(size, 1.2, seed) * 0.018 + tile_noise(size, 0.2, seed + 1) * 0.012
    tiled = np.tile(grain, (h // size + 1, w // size + 1))[:h, :w]
    yy, xx = np.mgrid[0:h, 0:w]
    ramp = 1.0 - shade_bottom * np.clip((yy / max(1, h - 1)) * 0.75 + (xx / max(1, w - 1)) * 0.25, 0, 1)
    value = np.clip(tiled * ramp, 0.0, 1.0)
    rgb = np.repeat((value * 255.0 + 0.5).astype(np.uint8)[..., None], 3, axis=2)
    return Image.fromarray(np.dstack([rgb, np.asarray(mask)]), "RGBA")


def _with_shadow(piece: Image.Image, offset=(4, 5), blur=3.0, strength=0.42) -> Image.Image:
    """Lay a piece on its own soft drop shadow (lower right)."""
    alpha = piece.getchannel("A")
    shadow_alpha = Image.new("L", piece.size, 0)
    shadow_alpha.paste(alpha, offset)
    shadow_alpha = shadow_alpha.filter(ImageFilter.GaussianBlur(blur)).point(lambda v: int(v * strength))
    out = Image.merge("RGBA", (Image.new("L", piece.size, 0),) * 3 + (shadow_alpha,))
    out.alpha_composite(piece)
    return out


def waves(path: Path) -> tuple[Path, list[list[int]]]:
    """Eight cut-paper waves on one sheet (4 x 2 cells of 224 x 120): four shapes, and the
    same four mirrored. A wave cannot be mirrored when it is drawn — its baked shadow would
    fall to the wrong side — so the mirror images are cut here. White, so each can be tinted
    to one of the sea's paler papers; the shadow stays dark under any tint."""
    cw, ch = 224, 120
    shapes = [
        # back of the wave, the curl, the belly — in cell fractions
        dict(tail=(0.04, 0.78), peak=(0.62, 0.10), tip=(0.93, 0.50), belly=(0.56, 0.60)),
        dict(tail=(0.05, 0.80), peak=(0.55, 0.16), tip=(0.90, 0.40), belly=(0.50, 0.66)),
        dict(tail=(0.03, 0.74), peak=(0.68, 0.14), tip=(0.95, 0.58), belly=(0.60, 0.56)),
        dict(tail=(0.08, 0.82), peak=(0.58, 0.08), tip=(0.86, 0.46), belly=(0.48, 0.62)),
    ]
    sheet = Image.new("RGBA", (cw * 4, ch * 2), (0, 0, 0, 0))
    cells = []
    for mirrored in (False, True):
        for i, s in enumerate(shapes):
            W, H = cw * SS, ch * SS
            pt = lambda p: (p[0] * W, p[1] * H)
            tail, peak, tip, belly = pt(s["tail"]), pt(s["peak"]), pt(s["tip"]), pt(s["belly"])
            back = _bezier(tail, (tail[0] + W * 0.22, tail[1] - H * 0.10), (peak[0] - W * 0.22, peak[1] + H * 0.04), peak)
            curl = _bezier(peak, (peak[0] + W * 0.17, peak[1] - H * 0.02), (tip[0] + W * 0.04, tip[1] - H * 0.26), tip)
            under = _bezier(tip, (tip[0] - W * 0.10, tip[1] - H * 0.22), (belly[0] + W * 0.14, belly[1] - H * 0.30), belly)
            base = _bezier(belly, (belly[0] - W * 0.16, belly[1] + H * 0.26), (tail[0] + W * 0.20, tail[1] + H * 0.10), tail)
            outline = np.vstack([back, curl[1:], under[1:], base[1:]])
            # leave room on the right and below for the shadow
            outline = outline * np.array([0.94, 0.90]) + np.array([W * 0.01, H * 0.02])
            if mirrored:
                outline[:, 0] = W * 0.96 - outline[:, 0]
            mask = Image.new("L", (W, H), 0)
            ImageDraw.Draw(mask).polygon([tuple(p) for p in outline], fill=255)
            mask = mask.resize((cw, ch), Image.LANCZOS)
            piece = _with_shadow(_grain_rgba(mask, 40 + i * 3, 0.13), (4, 5), 3.0, 0.40)
            ox, oy = i * cw, (1 if mirrored else 0) * ch
            sheet.alpha_composite(piece, (ox, oy))
            cells.append([ox, oy, cw, ch])
    sheet.save(path, "PNG", optimize=True)
    return path, cells


def compass(path: Path) -> Path:
    """A compass rose cut from cream, ochre and red paper — no letters; north is the long
    red point. 440 px, with its shadow."""
    size = 440
    S = size * SS
    c = S / 2.0
    cream = (242, 229, 196, 255)
    ochre = (204, 152, 66, 255)
    umber = (120, 78, 40, 255)
    red = (196, 62, 48, 255)
    dark_red = (140, 38, 34, 255)
    image = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def polar(radius, degrees):
        a = math.radians(degrees - 90.0)   # 0 deg = north (up)
        return (c + math.cos(a) * radius * SS, c + math.sin(a) * radius * SS)

    def ring(r_out, r_in, fill):
        draw.ellipse([c - r_out * SS, c - r_out * SS, c + r_out * SS, c + r_out * SS], fill=fill)
        draw.ellipse([c - r_in * SS, c - r_in * SS, c + r_in * SS, c + r_in * SS], fill=(0, 0, 0, 0))

    ring(158, 151, cream)
    ring(140, 137.5, ochre)
    for i in range(64):
        deg = i * 360.0 / 64
        major = i % 8 == 0
        r0, r1 = (140.5, 151.5)
        half = 1.3 if major else 0.55
        if i % 4 != 0:
            r0 = 144.5
        draw.polygon([polar(r0, deg - half), polar(r1, deg - half), polar(r1, deg + half), polar(r0, deg + half)],
                     fill=ochre if major else cream)

    def point(deg, length, rho, delta, light, dark):
        tip = polar(length, deg)
        draw.polygon([(c, c), polar(rho, deg - delta), tip], fill=light)
        draw.polygon([(c, c), tip, polar(rho, deg + delta)], fill=dark)

    for k in range(8):
        point(22.5 + k * 45.0, 92, 22, 22.5, cream, umber)
    for k in range(4):
        point(45.0 + k * 90.0, 128, 30, 45.0, cream, ochre)
    for k in (1, 2, 3):
        point(k * 90.0, 172, 40, 45.0, cream, ochre)
    point(0.0, 204, 40, 45.0, red, dark_red)
    draw.ellipse([c - 13 * SS, c - 13 * SS, c + 13 * SS, c + 13 * SS], fill=ochre)
    draw.ellipse([c - 6 * SS, c - 6 * SS, c + 6 * SS, c + 6 * SS], fill=cream)

    small = image.resize((size, size), Image.LANCZOS)
    # paper grain over the colours
    grain = 0.965 + tile_noise(256, 1.0, 51) * 0.02
    tiled = np.tile(grain, (size // 256 + 1, size // 256 + 1))[:size, :size]
    arr = np.asarray(small).astype(np.float32)
    arr[..., :3] = np.clip(arr[..., :3] * tiled[..., None], 0, 255)
    small = Image.fromarray(arr.astype(np.uint8), "RGBA")
    _with_shadow(small, (4, 6), 4.0, 0.45).save(path, "PNG", optimize=True)
    return path


# ---------------------------------------------------------------------------- pins

PIN_SHAPES = ["circle", "diamond", "triangle", "square", "ring"]
PIN_ROWS = ["shadow", "sticker", "head", "gloss"]
PIN_CELL = 80


def _shape_polygon(kind: str, cx: float, cy: float, r: float) -> list[tuple[float, float]] | None:
    if kind == "diamond":
        return [(cx, cy - r * 1.12), (cx + r * 1.0, cy), (cx, cy + r * 1.12), (cx - r * 1.0, cy)]
    if kind == "triangle":
        return [(cx, cy - r * 1.12), (cx + r * 1.08, cy + r * 0.82), (cx - r * 1.08, cy + r * 0.82)]
    if kind == "square":
        k = r * 0.86
        return [(cx - k, cy - k), (cx + k, cy - k), (cx + k, cy + k), (cx - k, cy + k)]
    return None


def _shape_mask(kind: str, scale: float, grow: float = 0.0) -> Image.Image:
    """An anti-aliased mask of one pin shape, `scale` of the cell's nominal radius."""
    S = PIN_CELL * SS
    cx = cy = S / 2.0
    r = (PIN_CELL * 0.30 * scale + grow) * SS
    mask = Image.new("L", (S, S), 0)
    draw = ImageDraw.Draw(mask)
    poly = _shape_polygon(kind, cx, cy + (r * 0.10 if kind == "triangle" else 0.0), r)
    if poly is None:
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    else:
        draw.polygon(poly, fill=255)
        # soften the corners a touch: a sticker is die-cut, not razor-cut
        mask = mask.filter(ImageFilter.GaussianBlur(SS * 0.9)).point(lambda v: 255 if v > 127 else 0)
    return mask.resize((PIN_CELL, PIN_CELL), Image.LANCZOS)


def pins(path: Path) -> tuple[Path, dict]:
    """The pins: a cream paper sticker in the node kind's shape with an enamel head in it.

    One sheet, a column per shape and a row per layer, drawn back to front:
    `shadow` (dark, soft), `sticker` (cream, dark keyline — the silhouette that tells the
    kinds apart at any zoom and on any ground), `head` (white, shaded: multiply it by the
    node's colour), `gloss` (the highlight and the head's keyline, never tinted).
    """
    cols, rows = len(PIN_SHAPES), len(PIN_ROWS)
    sheet = Image.new("RGBA", (PIN_CELL * cols, PIN_CELL * rows), (0, 0, 0, 0))
    yy, xx = np.mgrid[0:PIN_CELL, 0:PIN_CELL].astype(np.float32)
    centre = PIN_CELL / 2.0
    for col, kind in enumerate(PIN_SHAPES):
        ring = kind == "ring"
        base_kind = "circle" if ring else kind
        outer = _shape_mask(base_kind, 1.0)
        keyline = _shape_mask(base_kind, 1.0, grow=2.2)
        inner = _shape_mask(base_kind, 0.60)
        inner_key = _shape_mask(base_kind, 0.60, grow=1.3)
        outer_a = np.asarray(outer).astype(np.float32) / 255.0
        key_a = np.asarray(keyline).astype(np.float32) / 255.0
        inner_a = np.asarray(inner).astype(np.float32) / 255.0
        inner_key_a = np.asarray(inner_key).astype(np.float32) / 255.0
        if ring:
            hole = np.asarray(_shape_mask("circle", 0.26)).astype(np.float32) / 255.0
            inner_a = np.clip(inner_a - hole, 0.0, 1.0)

        # shadow
        shadow = Image.new("L", (PIN_CELL, PIN_CELL), 0)
        shadow.paste(keyline, (3, 4))
        shadow = shadow.filter(ImageFilter.GaussianBlur(2.6))
        sh = np.asarray(shadow).astype(np.float32) / 255.0 * 0.55
        cell = np.zeros((PIN_CELL, PIN_CELL, 4), np.float32)
        cell[..., 3] = sh
        sheet.alpha_composite(_to_image(cell), (col * PIN_CELL, 0))

        # sticker: dark keyline, cream paper, a little shade to the lower right
        ramp = 1.0 - 0.10 * np.clip(((xx - centre) + (yy - centre)) / PIN_CELL + 0.5, 0, 1)
        cream = np.array([0.965, 0.925, 0.820], np.float32)
        ink = np.array([0.150, 0.105, 0.080], np.float32)
        cell = np.zeros((PIN_CELL, PIN_CELL, 4), np.float32)
        cell[..., :3] = ink[None, None, :] * (1.0 - outer_a[..., None]) + cream[None, None, :] * ramp[..., None] * outer_a[..., None]
        cell[..., 3] = key_a
        sheet.alpha_composite(_to_image(cell), (col * PIN_CELL, PIN_CELL))

        # head: white enamel dome, lit from the upper left (multiply by the node colour)
        dx, dy = (xx - (centre - 4.0)) / (PIN_CELL * 0.30), (yy - (centre - 4.5)) / (PIN_CELL * 0.30)
        dome = np.clip(1.06 - 0.36 * np.sqrt(dx * dx + dy * dy), 0.60, 1.0)
        cell = np.zeros((PIN_CELL, PIN_CELL, 4), np.float32)
        cell[..., :3] = dome[..., None]
        cell[..., 3] = inner_a
        sheet.alpha_composite(_to_image(cell), (col * PIN_CELL, PIN_CELL * 2))

        # gloss: keyline round the head, and one soft highlight
        cell = np.zeros((PIN_CELL, PIN_CELL, 4), np.float32)
        line = np.clip(inner_key_a - inner_a, 0.0, 1.0)
        if ring:
            line = np.clip(line + np.clip(hole - np.asarray(_shape_mask("circle", 0.26, grow=-1.2)).astype(np.float32) / 255.0, 0, 1), 0, 1)
        hx, hy = (xx - (centre - 5.0)) / 4.6, (yy - (centre - 5.6)) / 3.2
        spark = np.clip(1.0 - (hx * hx + hy * hy), 0.0, 1.0) ** 1.5 * 0.80 * inner_a
        cell[..., :3] = ink[None, None, :] * line[..., None] + 1.0 * (1.0 - line[..., None])
        cell[..., 3] = np.clip(line * 0.85 + spark * (1.0 - line), 0.0, 1.0)
        sheet.alpha_composite(_to_image(cell), (col * PIN_CELL, PIN_CELL * 3))
    sheet.save(path, "PNG", optimize=True)
    layout = {"cell": PIN_CELL, "radius": round(PIN_CELL * 0.30, 3), "shapes": PIN_SHAPES, "rows": PIN_ROWS}
    return path, layout


def _to_image(cell: np.ndarray) -> Image.Image:
    return Image.fromarray(np.clip(cell * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGBA")


# ---------------------------------------------------------------------------- entry

def build(out: Path, sheet_dir: Path | None = None) -> list[Path]:
    out.mkdir(parents=True, exist_ok=True)
    written = [desk(out / "desk.jpg"), paper(out / "paper.png"), char(out / "char.jpg")]
    waves_path, wave_cells = waves(out / "waves.png")
    pins_path, pin_layout = pins(out / "pins.png")
    written += [waves_path, compass(out / "compass.png"), pins_path]
    meta = {
        "desk": "desk.jpg", "paper": "paper.png", "char": "char.jpg",
        "waves": {"file": "waves.png", "cells": wave_cells},
        "compass": {"file": "compass.png", "north": "up"},
        "pins": {"file": "pins.png", **pin_layout},
        "light": "upper left",
    }
    meta_path = out / "shared.json"
    meta_path.write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
    written.append(meta_path)
    if sheet_dir is not None:
        sheet_dir.mkdir(parents=True, exist_ok=True)
        _review(out, sheet_dir / "review_shared.png")
    return written


def _review(out: Path, path: Path) -> None:
    """Everything shared, laid on the desk paper. Not an asset."""
    desk_img = Image.open(out / "desk.jpg").convert("RGBA")
    canvas = Image.new("RGBA", (1280, 640))
    for y in range(0, 640, 512):
        for x in range(0, 1280, 512):
            canvas.paste(desk_img, (x, y))
    canvas.alpha_composite(Image.open(out / "compass.png"), (10, 10))
    wave_sheet = Image.open(out / "waves.png")
    for i, tint in enumerate([(0.80, 0.90, 0.92), (0.66, 0.82, 0.86), (0.55, 0.74, 0.80), (0.86, 0.93, 0.90)]):
        w = np.asarray(wave_sheet).astype(np.float32)
        w[..., :3] *= np.array(tint, np.float32)[None, None, :]
        row = i % 2
        cell = Image.fromarray(w.astype(np.uint8), "RGBA").crop((i * 224, row * 120, i * 224 + 224, row * 120 + 120))
        canvas.alpha_composite(cell, (470 + (i % 2) * 240, 20 + (i // 2) * 130))
    pin_sheet = np.asarray(Image.open(out / "pins.png")).astype(np.float32) / 255.0
    colours = [(0.95, 0.62, 0.80), (0.95, 0.55, 0.15), (0.85, 0.70, 0.40), (0.88, 0.66, 1.0), (0.30, 0.45, 0.85)]
    for col, colour in enumerate(colours):
        for scale_index, scale in enumerate((1.0, 0.5, 0.3)):
            pin = Image.new("RGBA", (PIN_CELL, PIN_CELL), (0, 0, 0, 0))
            for row in range(4):
                layer = pin_sheet[row * PIN_CELL:(row + 1) * PIN_CELL, col * PIN_CELL:(col + 1) * PIN_CELL].copy()
                if row == 2:
                    layer[..., :3] *= np.array(colour, np.float32)[None, None, :]
                pin.alpha_composite(_to_image(layer))
            size = max(4, int(PIN_CELL * scale))
            canvas.alpha_composite(pin.resize((size, size), Image.LANCZOS), (470 + col * 90, 300 + scale_index * 90))
    paper_img = Image.open(out / "paper.png").convert("RGBA")
    canvas.paste(paper_img, (980, 20))
    canvas.paste(Image.open(out / "char.jpg").convert("RGBA").crop((0, 0, 256, 256)), (980, 300))
    canvas.convert("RGB").save(path)

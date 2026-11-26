#!/usr/bin/env python3
"""Turn the AI-made overworld page kits into game-ready assets.

A *page kit* is what gives one world its identity on the torn-pages overworld
(`Docs/Art/OverworldStyle.md`): one ground and nine map symbols, both in the world's medium.
The originals are two 1024 x 1024 PNGs per world, straight from the image model:

    KIT-<W>-G.png   the page's ground, flat and top-down
    KIT-<W>-S.png   nine symbols in a 3 x 3 grid on flat magenta (#FF00FF)

This tool keys the magenta out, cuts the sheet into nine stamps, trims and measures them,
and writes for each world:

    Assets/Sprites/Map/Overworld/<W>/ground.jpg       1024 px, the page's ground
    Assets/Sprites/Map/Overworld/<W>/stamp_0..8.png   keyed, trimmed, <= 256 px on the long side
    Assets/Sprites/Map/Overworld/<W>/kit.json         what the renderer needs to know

and, once, the furniture every island shares whatever its worlds are (`shared.py`):

    Assets/Sprites/Map/Overworld/_shared/…            desk, paper grain, char, waves, compass, pins

Deterministic and re-runnable: the same originals give the same bytes. It prints every file
it writes and the total weight.

    python3 Tools/overworld_art/build_kits.py                      # everything
    python3 Tools/overworld_art/build_kits.py --world VK BM        # two kits only
    python3 Tools/overworld_art/build_kits.py --shared-only
    python3 Tools/overworld_art/build_kits.py --sheet /tmp/kits    # + review sheets (not assets)
    python3 Tools/overworld_art/build_kits.py --prompts VK         # the prompts that made a kit

The originals are not in git (1 – 2 MB each). `--src` names their folder; the default
is `Docs/Art/StyleExploration/_originals` under the repository root. Use the Homebrew Python
on the dev Mac (`/opt/homebrew/bin/python3`): it has Pillow and numpy.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SRC = ROOT / "Docs" / "Art" / "StyleExploration" / "_originals"
DEFAULT_OUT = ROOT / "Assets" / "Sprites" / "Map" / "Overworld"

STAMP_LONG_SIDE = 256      # px; a stamp is drawn 40-200 px tall on screen
GROUND_SIZE = 1024         # px; one copy spans a whole realm, never tiled
GROUND_JPEG_QUALITY = 90
TRIM_MARGIN = 3            # px of clear air kept round a trimmed stamp (in sheet pixels)

# The eight pool worlds (MapGDD §2, WorldDef.Pool). `seat` is the index, in sheet order
# (row by row), of the symbol that stands at the realm's capital. `coast` lists symbols that
# stand in water and so belong at the sea's edge. `mist` marks a sheet whose symbols dissolve
# into bare paper: its matte is kept soft and is never forced opaque or eroded. `despill`
# says how far the magenta cast is hunted: "all" for a medium whose palette has no pink or
# purple (so any magenta-hued paint is light bounced off the key colour and is neutralised
# everywhere), "edge" for one that owns such colours (only the band beside the key).
# `halo` (colour, strength, radius in stamp pixels) lays a faint glow round each symbol of a
# sheet that is dark on a dark ground, so its silhouette can be found on its own page.
# `despill_hue` widens what counts as magenta-hued (|r - b| below it) where nothing in the
# palette is near: 0.30 keeps strawberry pink and coral, 0.50 also takes rose-tinted spill.
WORLDS = {
    "VK": dict(despill="all", despill_hue=0.50, name="VanillaKindom", medium="storybook watercolour", seat=0, coast=[3, 4, 7, 8],
               symbols=["a cream seaside castle with red roofs", "a leaning palm tree",
                        "a beach hut on stilts", "a striped lighthouse", "a small sailboat",
                        "a sand dune with a parasol", "a pomegranate tree heavy with red fruit",
                        "a rocky headland", "a tiny fishing village"]),
    "SL": dict(despill="edge", name="Starland", medium="flat minimalist poster", seat=0, coast=[],
               symbols=["a colossal half-buried stone head", "a tall obelisk",
                        "a stepped stone plinth", "a seated colossus statue", "a broken column",
                        "a monument with a star-shaped opening", "an oasis with one palm",
                        "a striped caravan tent", "a crescent dune"]),
    "BM": dict(despill="all", despill_hue=0.50, name="Banboo Maze", medium="ink wash (xieyi)", seat=0, coast=[], mist=True,
               symbols=["a towering peak crowned with pines", "a bamboo grove",
                        "a twisted old pine", "a pavilion on a crag", "a small stone arch bridge",
                        "a cliff with a waterfall", "a slender pagoda", "a cluster of misty hills",
                        "a village of tiled roofs"]),
    "HC": dict(despill="all", despill_hue=0.50, name="HollowCastle", medium="painterly realism", seat=0, coast=[],
               symbols=["a grim castle keep", "a ruined round tower",
                        "an iron portcullis gate in a wall", "a pit of glowing lava",
                        "a cairn of skulls", "a drawbridge on chains", "a small graveyard",
                        "a dead tree", "a mine entrance with timber props"]),
    "TD": dict(despill="all", despill_hue=0.50, name="TheDeserted", medium="linocut print", seat=0, coast=[],
               symbols=["a two-storey frontier saloon with a balcony", "a wooden water tower",
                        "a flat-topped mesa", "a saguaro cactus", "a covered wagon",
                        "a windmill pump", "a small clapboard church", "a cattle skull on a post",
                        "a huddle of abandoned shacks"]),
    "PL": dict(despill="edge", name="Palace of LOOING", medium="gongbi (heavy colour)", seat=0, coast=[],
               symbols=["an undersea dragon palace with sweeping tiled roofs",
                        "a branching coral tree", "a giant open clam holding a glowing pearl",
                        "a stand of tall kelp", "a column of rising bubbles",
                        "a slender palace tower", "an arched bridge made of shells",
                        "a lotus-shaped pavilion", "a coiled sea dragon statue"]),
    # The Dark Wood's sheet came back with the tree first and the castle second: the seat is
    # data, not a position on the sheet.
    "KH": dict(despill="all", despill_hue=0.50, name="Kingdom of Horror", medium="scratchboard", seat=1, coast=[],
               halo=((0.56, 0.72, 1.0), 0.46, 2.4),
               symbols=["a gnarled tree with a face",
                        "a crooked castle with a glowing jack-o'-lantern gate",
                        "a bramble thicket", "a scarecrow", "a wrought-iron graveyard gate",
                        "a hanging iron cage on a post", "a lantern will-o'-the-wisp",
                        "a crooked cottage with one lit window", "a ring of pumpkins"]),
    "TF": dict(despill="edge", name="The Forgotten", medium="pixel art", seat=0, coast=[],
               symbols=["a stepped pyramid temple", "a carved feathered-serpent head",
                        "a raised stone causeway", "a floating stone slab", "a canal with reeds",
                        "a field of marigolds", "an obelisk with neon glyphs", "a temple gateway",
                        "a small axolotl shrine"]),
}
ORDER = ["VK", "SL", "BM", "HC", "TD", "PL", "KH", "TF"]


# ---------------------------------------------------------------------------- matte

def _dilate(mask: np.ndarray) -> np.ndarray:
    """One step of 8-neighbour dilation of a boolean mask."""
    out = mask.copy()
    out[1:, :] |= mask[:-1, :]
    out[:-1, :] |= mask[1:, :]
    out[:, 1:] |= mask[:, :-1]
    out[:, :-1] |= mask[:, 1:]
    out[1:, 1:] |= mask[:-1, :-1]
    out[1:, :-1] |= mask[:-1, 1:]
    out[:-1, 1:] |= mask[1:, :-1]
    out[:-1, :-1] |= mask[1:, 1:]
    return out


def _distance_from(mask: np.ndarray, limit: int) -> np.ndarray:
    """Chessboard distance (px) from the nearest True pixel, saturating at `limit`."""
    dist = np.full(mask.shape, float(limit), dtype=np.float32)
    dist[mask] = 0.0
    grown = mask
    for step in range(1, limit):
        nxt = _dilate(grown)
        dist[nxt & ~grown] = step
        grown = nxt
    return dist


def _min3(a: np.ndarray) -> np.ndarray:
    """3 x 3 minimum filter (grey erosion by one pixel)."""
    p = np.pad(a, 1, mode="edge")
    out = a.copy()
    for dy in range(3):
        for dx in range(3):
            np.minimum(out, p[dy:dy + a.shape[0], dx:dx + a.shape[1]], out=out)
    return out


def _smoothstep(lo: float, hi: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - lo) / (hi - lo), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def key_magenta(rgb: np.ndarray, soft: bool, despill_all: bool, hue_window: float = 0.30) -> np.ndarray:
    """RGB sheet on flat magenta -> RGBA with a clean matte and no pink fringe.

    The matte is the *chroma difference* key: with key colour K (r = b high, g low), a pixel
    C = a.F + (1 - a).K has min(Cr, Cb) - Cg = (1 - a).(min(Kr, Kb) - Kg) whenever the
    foreground F is not itself magenta-ish — exactly for ink, greys, greens, golds. A purple
    or pink foreground reads as part-transparent, so away from the edge the matte is forced
    opaque (`soft=False`): anything further than three pixels from pure key is inside a
    symbol — unless it is nearly the key colour itself, which is background seen through. A sheet whose symbols fade into paper (`soft=True`, the ink wash) keeps the raw
    key instead, which is exact for its achromatic paint, and is never eroded.

    Colour is then un-mixed from the key (F = (C - (1 - a).K) / a), what magenta is left in
    the edge band is pulled back to neutral, and the crisp mattes are eroded by half a pixel.
    """
    h, w, _ = rgb.shape
    border = np.concatenate([rgb[:6].reshape(-1, 3), rgb[-6:].reshape(-1, 3),
                             rgb[:, :6].reshape(-1, 3), rgb[:, -6:].reshape(-1, 3)])
    key = np.median(border, axis=0)
    span = float(min(key[0], key[2]) - key[1])
    if span < 0.5:
        raise ValueError(f"sheet background is not magenta: border median {key}")

    excess = np.minimum(rgb[..., 0], rgb[..., 2]) - rgb[..., 1]
    raw = np.clip(1.0 - excess / span, 0.0, 1.0)

    pure_key = raw < 0.10
    dist = _distance_from(pure_key, 8)
    if soft:
        alpha = np.clip((raw - 0.035) / 0.965, 0.0, 1.0)
    else:
        # Paint that is itself almost the key colour is never forced: it is the background
        # seen through a gap (between columns, bars, coral branches), wherever it is.
        inside = _smoothstep(1.2, 3.2, dist) * _smoothstep(0.30, 0.55, raw)
        alpha = np.maximum(raw, inside)
        alpha = np.clip((alpha - 0.06) / 0.94, 0.0, 1.0)

    # Un-mix the key colour out of every part-transparent pixel.
    a3 = np.clip(alpha, 1e-3, 1.0)[..., None]
    fg = np.clip((rgb - (1.0 - a3) * key[None, None, :]) / a3, 0.0, 1.0)

    # Despill: in the band beside the key, a colour that is still magenta-hued (r ~ b > g)
    # is contamination; pull r and b down toward g. Interior purples are out of the band.
    band = np.ones_like(dist) if despill_all else 1.0 - _smoothstep(2.0, 6.0, dist)
    spill = np.clip(np.minimum(fg[..., 0], fg[..., 2]) - fg[..., 1], 0.0, 1.0)
    hue = np.clip(2.0 * (1.0 - np.abs(fg[..., 0] - fg[..., 2]) / hue_window), 0.0, 1.0)
    cut = spill * hue * band * (1.0 if soft else 0.92)
    fg[..., 0] -= cut
    fg[..., 2] -= cut

    if not soft:
        alpha = 0.5 * alpha + 0.5 * _min3(alpha)
        alpha = np.clip((alpha - 0.10) / 0.86, 0.0, 1.0)

    return np.dstack([np.clip(fg, 0.0, 1.0), alpha]).astype(np.float32)


# ---------------------------------------------------------------------------- cutting

def _runs(empty: np.ndarray) -> list[tuple[int, int]]:
    """[start, end] index runs where `empty` is True."""
    runs, start = [], None
    for i, e in enumerate(empty):
        if e and start is None:
            start = i
        elif not e and start is not None:
            runs.append((start, i - 1))
            start = None
    if start is not None:
        runs.append((start, len(empty) - 1))
    return runs


def _cut_near(profile: np.ndarray, nominal: int, reach: int) -> int:
    """The emptiest place to cut near `nominal`: the middle of the widest clear run within
    `reach`, or failing that the least-covered line."""
    lo, hi = max(1, nominal - reach), min(len(profile) - 2, nominal + reach)
    window = profile[lo:hi + 1]
    runs = [r for r in _runs(window <= 0.004) if r[1] - r[0] >= 3]
    if runs:
        a, b = max(runs, key=lambda r: (r[1] - r[0], -abs((r[0] + r[1]) / 2 + lo - nominal)))
        return lo + (a + b) // 2
    return lo + int(np.argmin(window))


def split_grid(alpha: np.ndarray) -> list[tuple[int, int, int, int]]:
    """Nine cell boxes (x0, y0, x1, y1), row by row. Rows are cut first, then each row is cut
    into columns on its own: symbols in different rows often overhang the same column line."""
    h, w = alpha.shape
    solid = alpha > 0.12
    rows = solid.mean(axis=1)
    y_cuts = [0, _cut_near(rows, h // 3, h // 9), _cut_near(rows, 2 * h // 3, h // 9), h]
    boxes = []
    for r in range(3):
        y0, y1 = y_cuts[r], y_cuts[r + 1]
        cols = solid[y0:y1].mean(axis=0)
        x_cuts = [0, _cut_near(cols, w // 3, w // 9), _cut_near(cols, 2 * w // 3, w // 9), w]
        for c in range(3):
            boxes.append((x_cuts[c], y0, x_cuts[c + 1], y1))
    return boxes


def trim(alpha: np.ndarray, box: tuple[int, int, int, int], margin: int = TRIM_MARGIN) -> tuple[int, int, int, int]:
    """Alpha bounds inside a cell, ignoring specks, grown by the trim margin."""
    x0, y0, x1, y1 = box
    cell = alpha[y0:y1, x0:x1] > 0.06
    # A speck is a row/column with almost nothing in it; require a few pixels of paint.
    ys = np.where(cell.sum(axis=1) >= 2)[0]
    xs = np.where(cell.sum(axis=0) >= 2)[0]
    if len(ys) == 0 or len(xs) == 0:
        raise ValueError(f"empty cell {box}")
    ax0, ax1 = x0 + xs[0], x0 + xs[-1] + 1
    ay0, ay1 = y0 + ys[0], y0 + ys[-1] + 1
    h, w = alpha.shape
    return (max(0, ax0 - margin), max(0, ay0 - margin),
            min(w, ax1 + margin), min(h, ay1 + margin))


def _resize_rgba(rgba: np.ndarray, size: tuple[int, int]) -> np.ndarray:
    """Lanczos resize in premultiplied alpha, so transparent pixels cannot bleed colour in."""
    pre = rgba.copy()
    pre[..., :3] *= pre[..., 3:4]
    chans = [np.asarray(Image.fromarray(pre[..., i], mode="F").resize(size, Image.LANCZOS))
             for i in range(4)]
    out = np.dstack(chans).astype(np.float32)
    a = np.clip(out[..., 3], 0.0, 1.0)
    out[..., :3] = np.where(a[..., None] > 1e-4, out[..., :3] / np.maximum(a[..., None], 1e-4), 0)
    out[..., 3] = a
    return np.clip(out, 0.0, 1.0)


_BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) / 16.0) - 0.5


def to_palette(rgba8: np.ndarray, colors: int = 255) -> Image.Image:
    """RGBA -> an 8-bit palette PNG image with per-entry alpha (a quarter of the bytes).

    The palette is fitted by k-means in premultiplied RGBA, seeded from Pillow's octree
    palette, over the pixels that carry paint; index 0 is kept for full transparency. Pixels
    are then assigned through a 4 x 4 ordered dither, which costs nothing and hides the
    banding a 255-colour gradient would show. No random numbers: same input, same bytes.
    """
    h, w, _ = rgba8.shape
    work = rgba8.astype(np.float32) / 255.0
    work[..., :3] *= work[..., 3:4]
    points = work.reshape(-1, 4)
    painted = points[:, 3] > (2.0 / 255.0)

    seed_img = Image.fromarray(rgba8, "RGBA").quantize(colors, method=Image.Quantize.FASTOCTREE)
    palette = np.array(seed_img.getpalette("RGBA"), dtype=np.float32).reshape(-1, 4)[:colors] / 255.0
    palette[:, :3] *= palette[:, 3:4]
    weight = np.array([1.0, 1.0, 1.0, 1.3], np.float32)

    def nearest(pts: np.ndarray) -> np.ndarray:
        out = np.empty(len(pts), dtype=np.int64)
        for start in range(0, len(pts), 8192):
            chunk = pts[start:start + 8192]
            d = ((chunk[:, None, :] - palette[None, :, :]) * weight) ** 2
            out[start:start + 8192] = d.sum(-1).argmin(1)
        return out

    sample = points[painted][::3]
    if len(sample) > 0:
        for _ in range(5):
            idx = nearest(sample)
            count = np.bincount(idx, minlength=len(palette)).astype(np.float32)
            for c in range(4):
                total = np.bincount(idx, weights=sample[:, c], minlength=len(palette))
                palette[:, c] = np.where(count > 0, total / np.maximum(count, 1.0), palette[:, c])

    dither = np.tile(_BAYER4, (h // 4 + 1, w // 4 + 1))[:h, :w].reshape(-1, 1) * (6.0 / 255.0)
    shaken = points.copy()
    shaken[:, :3] += dither * shaken[:, 3:4]
    index = nearest(shaken) + 1
    index[~painted] = 0

    alpha = np.clip(palette[:, 3:4], 0.0, 1.0)
    straight = np.where(alpha > 1e-4, palette[:, :3] / np.maximum(alpha, 1e-4), 0.0)
    table = np.vstack([np.zeros((1, 4), np.float32), np.hstack([np.clip(straight, 0.0, 1.0), alpha])])
    table8 = np.clip(table * 255.0 + 0.5, 0, 255).astype(np.uint8)

    out = Image.fromarray(index.reshape(h, w).astype(np.uint8), "P")
    out.putpalette(table8[:, :3].reshape(-1).tolist())
    out.info["transparency"] = bytes(table8[:, 3].tolist())
    return out


def add_halo(rgba: np.ndarray, colour: tuple[float, float, float], strength: float, radius: float) -> np.ndarray:
    """A soft glow of `colour` just outside the paint: moonlight round a black shape on a
    black board. The paint itself is untouched."""
    alpha = rgba[..., 3]
    spread = Image.fromarray(np.clip(alpha * 255.0 + 0.5, 0, 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(radius))
    glow = np.clip(np.asarray(spread).astype(np.float32) / 255.0 * 2.1, 0.0, 1.0) * strength
    out_alpha = alpha + glow * (1.0 - alpha)
    tint = np.array(colour, np.float32)[None, None, :]
    rgb = (rgba[..., :3] * alpha[..., None] + tint * (glow * (1.0 - alpha))[..., None]) / np.maximum(out_alpha[..., None], 1e-4)
    return np.dstack([np.clip(rgb, 0.0, 1.0), np.clip(out_alpha, 0.0, 1.0)]).astype(np.float32)


def measure_anchor(alpha: np.ndarray) -> tuple[float, float, float]:
    """Where a stamp stands, in 0..1 of its trimmed image, and how wide its ground patch is.

    Each symbol sits on its own patch of ground seen from a raised three-quarter view; the
    patch is the lowest fifth of the paint. The anchor is the patch's horizontal centre and a
    line a little above its front edge — the point that is put on the map, and the y a
    stamp is depth-sorted by. `foot` is the patch's width as a share of the image width.
    """
    h, w = alpha.shape
    solid = alpha > 0.25
    ys = np.where(solid.any(axis=1))[0]
    top, bottom = int(ys[0]), int(ys[-1])
    band0 = bottom - max(3, int(round((bottom - top) * 0.20)))
    patch = solid[band0:bottom + 1]
    weight = patch.sum(axis=0).astype(np.float64)
    xs = np.where(weight > 0)[0]
    cx = float((weight * np.arange(w)).sum() / max(1.0, weight.sum()))
    foot = (xs[-1] - xs[0] + 1) / w
    ay = (bottom - (bottom - top) * 0.07) / h
    return round(cx / w, 4), round(float(ay), 4), round(float(foot), 4)


# ---------------------------------------------------------------------------- one kit

def _luma(rgb8: np.ndarray) -> float:
    lin = rgb8.astype(np.float64) / 255.0
    return float((lin[..., 0] * 0.2126 + lin[..., 1] * 0.7152 + lin[..., 2] * 0.0722).mean())


def build_kit(abbr: str, src: Path, out: Path, sheet_dir: Path | None) -> list[Path]:
    spec = WORLDS[abbr]
    ground_src = src / f"KIT-{abbr}-G.png"
    symbols_src = src / f"KIT-{abbr}-S.png"
    for path in (ground_src, symbols_src):
        if not path.is_file():
            raise FileNotFoundError(f"{path} — pass the originals' folder with --src")
    dest = out / abbr
    dest.mkdir(parents=True, exist_ok=True)
    written: list[Path] = []

    # Ground: one square, as generated. JPEG, because a 1024 px PNG of paper grain is 2 MB.
    ground = Image.open(ground_src).convert("RGB")
    if ground.size != (GROUND_SIZE, GROUND_SIZE):
        ground = ground.resize((GROUND_SIZE, GROUND_SIZE), Image.LANCZOS)
    ground_path = dest / "ground.jpg"
    ground.save(ground_path, "JPEG", quality=GROUND_JPEG_QUALITY, optimize=True)
    written.append(ground_path)
    ground8 = np.asarray(ground)
    luma = _luma(ground8)
    mean = ground8.reshape(-1, 3).mean(axis=0)

    # Symbols.
    sheet = np.asarray(Image.open(symbols_src).convert("RGB")).astype(np.float32) / 255.0
    rgba = key_magenta(sheet, soft=bool(spec.get("mist")), despill_all=spec["despill"] == "all",
                       hue_window=spec.get("despill_hue", 0.30))
    cells = split_grid(rgba[..., 3])
    stamps = []
    review = []
    for index, cell in enumerate(cells):
        halo = spec.get("halo")
        x0, y0, x1, y1 = trim(rgba[..., 3], cell, TRIM_MARGIN + (int(halo[2] * 9) if halo else 0))
        crop = rgba[y0:y1, x0:x1]
        sh, sw = crop.shape[:2]
        scale = min(1.0, STAMP_LONG_SIDE / max(sw, sh))
        size = (max(1, round(sw * scale)), max(1, round(sh * scale)))
        small = _resize_rgba(crop, size) if scale < 1.0 else crop
        ax, ay, foot = measure_anchor(small[..., 3])
        if halo:
            small = add_halo(small, *halo)
        rgba8 = np.clip(small * 255.0 + 0.5, 0, 255).astype(np.uint8)
        rgba8[rgba8[..., 3] < 3] = 0      # clear air is all zeros (Godot's import fixes the alpha border)
        path = dest / f"stamp_{index}.png"
        if index == spec["seat"]:
            # The seat is the page's one large picture: keep every colour.
            Image.fromarray(rgba8, "RGBA").save(path, "PNG", optimize=True)
        else:
            pal = to_palette(rgba8)
            pal.save(path, "PNG", optimize=True, transparency=pal.info["transparency"])
            rgba8 = np.asarray(Image.open(path).convert("RGBA"))
        written.append(path)
        review.append(rgba8)
        stamps.append({
            "file": path.name,
            "name": spec["symbols"][index],
            "w": size[0], "h": size[1],
            "sheetW": int(sw), "sheetH": int(sh),
            "anchor": [ax, ay],
            "foot": foot,
            "band": "coast" if index in spec["coast"] else "any",
        })

    dark = luma < 0.36
    kit = {
        "world": abbr,
        "name": spec["name"],
        "medium": spec["medium"],
        "ground": ground_path.name,
        "groundLuma": round(luma, 3),
        "groundMean": "#%02x%02x%02x" % tuple(int(round(v)) for v in mean),
        "tone": "dark" if dark else "light",
        "ink": "#f4ead2" if dark else "#3a2a20",
        "inkCase": "#1a1412" if dark else "#fff7e6",
        "seat": spec["seat"],
        "stamps": stamps,
    }
    kit_path = dest / "kit.json"
    kit_path.write_text(json.dumps(kit, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    written.append(kit_path)

    if sheet_dir is not None:
        sheet_dir.mkdir(parents=True, exist_ok=True)
        _review_sheet(abbr, review, kit, ground8, sheet_dir / f"review_{abbr}.png")
    return written


def _review_sheet(abbr: str, stamps: list[np.ndarray], kit: dict, ground8: np.ndarray, path: Path) -> None:
    """Nine stamps three times over: on dark, on white, and on the world's own ground — a
    fringe or a halo shows on one of the three. Anchors are marked in red. Not an asset."""
    cell = STAMP_LONG_SIDE + 16
    backs = [np.full((cell, cell * 9, 3), 24, np.uint8), np.full((cell, cell * 9, 3), 250, np.uint8),
             np.asarray(Image.fromarray(ground8).resize((cell * 9, cell * 9), Image.LANCZOS))[:cell]]
    rows = []
    for back in backs:
        canvas = Image.fromarray(back.copy()).convert("RGBA")
        for i, rgba8 in enumerate(stamps):
            im = Image.fromarray(rgba8, "RGBA")
            ox = i * cell + (cell - im.width) // 2
            oy = (cell - im.height) // 2
            canvas.alpha_composite(im, (ox, oy))
            ax, ay = kit["stamps"][i]["anchor"]
            px, py = ox + int(ax * im.width), oy + int(ay * im.height)
            for d in range(-4, 5):
                canvas.putpixel((min(canvas.width - 1, max(0, px + d)), py), (255, 0, 0, 255))
                canvas.putpixel((px, min(canvas.height - 1, max(0, py + d))), (255, 0, 0, 255))
        rows.append(np.asarray(canvas.convert("RGB")))
    Image.fromarray(np.vstack(rows)).save(path)


# ---------------------------------------------------------------------------- prompts

def prompts_for(abbr: str) -> tuple[str, str]:
    """The two prompts that generate a world's kit originals (ground, symbols), filled in
    from `kit_prompts.json` and this file's symbol list. The recipe, as text."""
    recipe = json.loads((Path(__file__).with_name("kit_prompts.json")).read_text(encoding="utf-8"))
    slots = recipe["worlds"][abbr]
    ground = recipe["templates"]["ground"].format(**slots)
    symbols = recipe["templates"]["symbols"].format(symbols=", ".join(WORLDS[abbr]["symbols"]), **slots)
    return ground, symbols


# ---------------------------------------------------------------------------- main

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--src", type=Path, default=DEFAULT_SRC, help="folder with KIT-<W>-G/S.png")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="asset folder to write")
    parser.add_argument("--world", nargs="*", default=None, help="only these worlds (abbreviations)")
    parser.add_argument("--shared-only", action="store_true", help="only the shared furniture")
    parser.add_argument("--no-shared", action="store_true", help="skip the shared furniture")
    parser.add_argument("--sheet", type=Path, default=None, help="also write review sheets here")
    parser.add_argument("--prompts", nargs="*", default=None, metavar="W",
                        help="print the generation prompts for these worlds (all if none named) and exit")
    args = parser.parse_args()

    if args.prompts is not None:
        for abbr in ([w.upper() for w in args.prompts] or ORDER):
            ground, symbols = prompts_for(abbr)
            print(f"== {abbr} {WORLDS[abbr]['name']} — KIT-{abbr}-G.png\n{ground}\n\n== {abbr} — KIT-{abbr}-S.png\n{symbols}\n")
        return 0

    written: list[Path] = []
    if not args.shared_only:
        worlds = [w.upper() for w in args.world] if args.world else ORDER
        for abbr in worlds:
            if abbr not in WORLDS:
                print(f"unknown world {abbr}; known: {', '.join(ORDER)}", file=sys.stderr)
                return 2
            files = build_kit(abbr, args.src, args.out, args.sheet)
            size = sum(f.stat().st_size for f in files)
            print(f"{abbr}  {WORLDS[abbr]['name']:<18} {len(files)} files  {size / 1024:7.0f} KB  -> {args.out / abbr}")
            written += files
    if not args.no_shared and not args.world:
        import shared
        files = shared.build(args.out / "_shared", args.sheet)
        size = sum(f.stat().st_size for f in files)
        print(f"--  shared furniture     {len(files)} files  {size / 1024:7.0f} KB  -> {args.out / '_shared'}")
        written += files

    total = sum(f.stat().st_size for f in written)
    for f in written:
        print(f"  {f.relative_to(args.out)}  {f.stat().st_size / 1024:.0f} KB")
    print(f"wrote {len(written)} files, {total / 1024 / 1024:.2f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())

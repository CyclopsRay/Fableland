#!/usr/bin/env python3
"""Render a combat map JSON (`Maps/*.json` or a `user://maps` file) to a PNG, offline.

This mirrors what the game draws — `Scripts/MapCreation/Runtime/MapVisualBuilder.cs`
and `Scripts/MapCreation/Data/TileVisualResolver.cs` — so a map can be reviewed without
launching Godot:

  * layers back-to-front, with tint / opacity, horizontal looping and camera parallax;
  * layered-hill ground atlases classified from same-group neighbours
    (`HillAutotile.cs`), including the per-column variant groups;
  * prop sprites trimmed to their used rect, aspect-fitted in the footprint and anchored
    bottom-centre;
  * each tile at its anchor cell plus its sub-cell nudge (`TilePlacement.cs`): `SubX` /
    `SubY` quarter cells for props, platforms, hazards and markers, never for terrain.

Tile metadata comes from the `*.tile.json` sidecars under `Assets/Sprites/Tiles/`, plus a
small table for the older beach tiles that predate sidecars. The C# `TileRegistry` is
still the source of truth in-game; `TileSetAudit` (run at editor boot) checks the two agree.

Usage:
    python3 Tools/render_map_preview.py Maps/wulin_bamboo_sea.json
    python3 Tools/render_map_preview.py --all --scale 32 --out-dir /tmp/previews
    python3 Tools/render_map_preview.py Maps/x.json --view 30,12        # an in-game screen
    python3 Tools/render_map_preview.py Maps/x.json --markers           # show spawn/goal markers
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("This script requires Pillow: pip install Pillow", file=sys.stderr)
    sys.exit(1)

REPO = Path(__file__).resolve().parents[1]
TILES = REPO / "Assets" / "Sprites" / "Tiles"
VK = "Assets/Sprites/Tiles/VanillaKingdom/"
VIEW_W, VIEW_H = 1152 / 64.0, 648 / 64.0      # base viewport in cells (project default 1152x648)
SUBCELLS = 4                                  # nudge steps per cell (TilePlacement.SubcellsPerCell): 16 px = 0.5 m
MAX_NUDGE = SUBCELLS - 1                      # a nudge is -3..3 quarter cells; a fourth is the next cell

# Beach / core tiles registered in TileRegistry.cs without a sidecar: (sprite, w, h, color).
LEGACY = {
    "ground.grass": dict(sprite=VK + "Ground/ground_grass_seamless.png", fill=True, color="#76B947"),
    # The beach hill sheet is an editor-canvas approximation only (TileVisualResolver): in play,
    # and therefore here, beach sand is its seamless fill.
    "ground.sand": dict(sprite=VK + "Ground/ground_sand_seamless.png", fill=True, color="#E8C878"),
    "ground.stone": dict(color="#8D8D8D"),
    "platform.wood": dict(color="#A0693D"),
    "platform.vine": dict(color="#6B8E23"),
    "platform.bench": dict(sprite=VK + "Platform/platform_bench.png", w=2, h=1, color="#B88755"),
    "platform.sun_lounger": dict(sprite=VK + "Platform/platform_sun_lounger.png", w=3, h=1, color="#C8A675"),
    "platform.lifeguard_tower": dict(sprite=VK + "Platform/platform_lifeguard_tower.png", w=4, h=8, color="#4F9B9B"),
    "softvolume.bush1x1": dict(color="#2E8B57"),
    "softvolume.cloud1x1": dict(sprite=VK + "SoftVolume/softvolume_cloud_small.png", color="#B0E0E6"),
    "softvolume.cloud2x1": dict(sprite=VK + "SoftVolume/softvolume_cloud_medium.png", w=2, h=1, color="#ADD8E6"),
    "softvolume.cloud3x2": dict(sprite=VK + "SoftVolume/softvolume_cloud_large.png", w=3, h=2, color="#A9D7E3"),
    "softvolume.palm_tree": dict(sprite=VK + "SoftVolume/softvolume_palm_tree_v2.png", w=3, h=4, color="#4E9A51"),
    "hazard.bonfire": dict(sprite=VK + "Hazard/hazard_bonfire_flat.png", w=2, h=1, color="#FF4500"),
    "hazard.freezepit": dict(color="#66CCFF"),
    "hazard.tsunami_trigger": dict(color="#287DB5"),
    "deco.flower": dict(color="#FF69B4"),
    "deco.rock": dict(color="#708090"),
    "deco.caution_sign": dict(sprite=VK + "Decoration/deco_caution_monkey.png", w=1, h=2, color="#E66B42"),
    "deco.sand_castle": dict(sprite=VK + "Decoration/deco_sand_castle.png", w=2, h=2, color="#DDBB68"),
    "deco.sun": dict(sprite=VK + "Decoration/deco_sun_chibi.png", w=2, h=2, color="#FFD24A"),
    "deco.column": dict(w=1, h=3, color="#CFC7B0"),
}
MARKERS = {
    "spawn.character": ("#1E90FF", "P"),
    "spawn.respawn": ("#FFD700", "R"),
    "spawn.enemy": ("#E03131", "E"),
    "goal.level": ("#B24BF3", "G"),
}


def grid_locked(def_id, info):
    """`TilePlacement.IsGridLocked`: terrain (ground and every connected-look fill, such as
    lava), rule zones and unknown ids stay on the grid; everything else can be nudged."""
    if def_id in MARKERS:
        return False
    if info is None or def_id.startswith("rule."):
        return True
    return bool(info.get("locked"))


def nudge(tile, info):
    """A placed tile's sub-cell nudge in CELLS, as `TilePlacement.NudgeX/NudgeY` read it:
    0 for a grid-locked kind, otherwise the stored quarter cells kept within +-3."""
    if grid_locked(tile["DefId"], info):
        return 0.0, 0.0
    clamp = lambda v: max(-MAX_NUDGE, min(MAX_NUDGE, int(v or 0)))
    return clamp(tile.get("SubX")) / SUBCELLS, clamp(tile.get("SubY")) / SUBCELLS


def load_tiles():
    tiles = {}
    for k, v in LEGACY.items():
        tiles[k] = dict(w=1, h=1, fill=False, locked=k.startswith("ground."))
        tiles[k].update(v)
    for side in sorted(TILES.rglob("*.tile.json")):
        d = json.loads(side.read_text(encoding="utf-8"))
        tid = d["id"]
        if tid in LEGACY:                        # beach sand/grass keep their registry wiring
            continue
        png = Path(str(side)[: -len(".tile.json")] + ".png")
        rel = str(png.relative_to(REPO))
        entry = dict(w=d.get("footprint", {}).get("w", 1), h=d.get("footprint", {}).get("h", 1),
                     fill=bool(d.get("fillFootprint")), sprite=rel, color="#FF00FF",
                     locked=str(d.get("role", "")).lower() == "ground" or bool(d.get("autotileGroup")))
        if d.get("autotileGroup") and d.get("autotileKind") == "layered_hill_strip" and d.get("artSource"):
            entry.update(group=d["autotileGroup"], atlas=d["artSource"][len("res://"):],
                         variants=int(d.get("autotileVariants", 1)), strip=True)
        tiles[tid] = entry
    return tiles


class Painter:
    def __init__(self, scale):
        self.scale = scale
        self._img = {}
        self._fit = {}

    def image(self, rel):
        if rel not in self._img:
            path = REPO / rel
            self._img[rel] = Image.open(path).convert("RGBA") if path.exists() else None
        return self._img[rel]

    def prop(self, rel, w, h, flip):
        """Used-rect trim -> aspect-fit -> bottom-centre, exactly like the game."""
        key = (rel, w, h, flip)
        if key not in self._fit:
            im = self.image(rel)
            if im is None:
                self._fit[key] = None
            else:
                box = im.getchannel("A").getbbox() or (0, 0, im.width, im.height)
                im = im.crop(box)
                fw, fh = w * self.scale, h * self.scale
                s = min(fw / im.width, fh / im.height)
                size = (max(1, round(im.width * s)), max(1, round(im.height * s)))
                im = im.resize(size, Image.LANCZOS)
                if flip:
                    im = im.transpose(Image.FLIP_LEFT_RIGHT)
                self._fit[key] = (im, round((fw - size[0]) / 2), fh - size[1])
        return self._fit[key]

    def atlas_cell(self, rel, col, row, cols, rows):
        key = (rel, col, row)
        if key not in self._fit:
            im = self.image(rel)
            if im is None:
                self._fit[key] = None
            else:
                cw, ch = im.width / cols, im.height / rows
                cell = im.crop((round(col * cw), round(row * ch), round((col + 1) * cw), round((row + 1) * ch)))
                self._fit[key] = cell.resize((self.scale, self.scale), Image.LANCZOS)
        return self._fit[key]

    def fill(self, rel, w, h):
        key = (rel, "fill", w, h)
        if key not in self._fit:
            im = self.image(rel)
            self._fit[key] = None if im is None else im.resize((w * self.scale, h * self.scale), Image.LANCZOS)
        return self._fit[key]


def comp(dst, src, x, y):
    """Alpha-composite `src` onto `dst` at (x, y), clipping to the destination."""
    sx0, sy0 = max(0, -x), max(0, -y)
    sx1, sy1 = min(src.width, dst.width - x), min(src.height, dst.height - y)
    if sx1 <= sx0 or sy1 <= sy0:
        return
    if (sx0, sy0, sx1, sy1) != (0, 0, src.width, src.height):
        src = src.crop((sx0, sy0, sx1, sy1))
    dst.alpha_composite(src, (x + sx0, y + sy0))


def hill_cell(same, x, y, variants, strip=True):
    """HillAutotile.Classify / ClassifyStrip, as TileVisualResolver dispatches them."""
    up = 0
    while same(x, y - up - 1):
        up += 1
    down = 0
    while same(x, y + down + 1):
        down += 1
    layer = 0 if up == 0 else (3 if down == 0 else (1 if up == 1 else 2))
    left_air, right_air = not same(x - 1, y), not same(x + 1, y)
    if not strip:                                # legacy 4x4 sheet: only Layer 1 has a Peak
        pos = 3 if (left_air and right_air and layer == 0) else (1 if left_air and right_air else (0 if left_air else (2 if right_air else 1)))
        return pos, layer, 4, 4
    pos = 3 if (left_air and right_air) else (0 if left_air else (2 if right_air else 1))
    return (x % variants) * 4 + pos, layer, variants * 4, 4


def hex_rgba(h, a=255):
    h = (h or "#FF00FF").lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def render(doc, tiles, scale=48, markers=False, view=None):
    layers = doc.get("Layers") or []
    bf = next((l for l in layers if l.get("Role") == "battlefield"), None)
    gw, gh = (bf["GridW"], bf["GridH"]) if bf else (64, 36)
    paint = Painter(scale)

    anchor = None
    if bf:
        for t in bf.get("Tiles", []):
            if t["DefId"] == "spawn.character":
                # The spawn marker point (TilePlacement.Marker): the cell's middle, 1 m above
                # its floor, moved by the marker's nudge.
                nx, ny = nudge(t, None)
                anchor = (t["X"] + nx + 0.5, t["Y"] + ny + 0.5)
                break
    anchor = anchor or (gw / 2.0, gh / 2.0)
    if view:
        cam = (min(max(view[0], VIEW_W / 2), max(VIEW_W / 2, gw - VIEW_W / 2)),
               min(max(view[1], VIEW_H / 2), max(VIEW_H / 2, gh - VIEW_H / 2)))
    else:
        cam = anchor
    canvas = Image.new("RGBA", (gw * scale, gh * scale), hex_rgba((doc.get("Canvas") or {}).get("Color", "#87CEEB")))

    for layer in layers:
        lt = layer.get("Tiles") or []
        if not lt:
            continue
        px, py = layer.get("ParallaxX", 1.0), layer.get("ParallaxY", 1.0)
        off_x = (cam[0] - anchor[0]) * (1.0 - px)
        off_y = (cam[1] - anchor[1]) * (1.0 - py)
        loop = bool(layer.get("Loop")) and layer.get("GridW", 0) > 0
        loop_w = layer.get("GridW", 0)
        groups = {}
        for t in lt:
            info = tiles.get(t["DefId"])
            if info and info.get("group"):
                groups[(t["X"], t["Y"])] = info["group"]
        lw, lh = layer.get("GridW", gw), layer.get("GridH", gh)

        def same_group(ax, ay, grp):
            """AutotileProbe.SameGroup: terrain continues one cell past the left, right and
            bottom edges of the layer grid; above the top edge is open air."""
            if ay < 0 or ax < -1 or ax > lw or ay > lh:
                return False
            return groups.get((min(max(ax, 0), lw - 1), min(ay, lh - 1))) == grp
        surf = Image.new("RGBA", canvas.size, (0, 0, 0, 0))

        def draw_at(t, info, cx, cy):
            x0, y0 = round(cx * scale), round(cy * scale)
            w, h = info["w"], info["h"]
            if x0 + w * scale <= 0 or y0 + h * scale <= 0 or x0 >= surf.width or y0 >= surf.height:
                return
            if info.get("group") and info.get("atlas"):
                grp = info["group"]
                col, row, cols, rows = hill_cell(lambda ax, ay: same_group(ax, ay, grp), t["X"], t["Y"],
                                                 info.get("variants", 1), info.get("strip", False))
                cell = paint.atlas_cell(info["atlas"], col, row, cols, rows)
                if cell is not None:
                    comp(surf, cell, x0, y0)
                    return
            if info.get("sprite"):
                if info.get("fill"):
                    im = paint.fill(info["sprite"], w, h)
                    if im is not None:
                        comp(surf, im, x0, y0)
                        return
                else:
                    fit = paint.prop(info["sprite"], w, h, bool(t.get("FlipX")))
                    if fit is not None:
                        im, dx, dy = fit
                        comp(surf, im, x0 + dx, y0 + dy)
                        return
            ImageDraw.Draw(surf).rectangle([x0, y0, x0 + w * scale - 1, y0 + h * scale - 1], fill=hex_rgba(info.get("color")))

        for t in lt:
            did = t["DefId"]
            if did in MARKERS or did.startswith("rule."):
                continue
            info = tiles.get(did)
            nx, ny = nudge(t, info)                 # before the unknown-id fallback: unknown stays on grid
            if info is None:
                info = dict(w=1, h=1, color="#FF00FF")
            tx, ty = t["X"] + nx, t["Y"] + ny       # footprint corner in cells: anchor + nudge
            if loop:
                first = int((-(off_x + tx + info["w"])) // loop_w) - 1
                last = int((gw - off_x - tx) // loop_w) + 1
                for k in range(first, last + 1):
                    draw_at(t, info, tx + off_x + k * loop_w, ty + off_y)
            else:
                draw_at(t, info, tx + off_x, ty + off_y)

        tint = layer.get("Tint")
        opacity = float(layer.get("Opacity", 1.0))
        if tint or opacity < 1.0:
            r, g, b, _ = hex_rgba(tint or "#FFFFFF")
            bands = list(surf.split())
            for i, k in enumerate((r, g, b)):
                bands[i] = bands[i].point(lambda v, k=k: v * k // 255)
            bands[3] = bands[3].point(lambda v: int(v * opacity))
            surf = Image.merge("RGBA", bands)
        canvas.alpha_composite(surf)

    if markers and bf:
        d = ImageDraw.Draw(canvas)
        for t in bf.get("Tiles", []):
            if t["DefId"] in MARKERS:
                col, ch = MARKERS[t["DefId"]]
                nx, ny = nudge(t, None)
                x0, y0 = round((t["X"] + nx) * scale), round((t["Y"] + ny) * scale)
                d.rectangle([x0 + 2, y0 + 2, x0 + scale - 3, y0 + scale - 3], outline=hex_rgba(col), width=max(2, scale // 16))
                d.text((x0 + scale * 0.36, y0 + scale * 0.28), ch, fill=hex_rgba(col))

    if view:
        x0 = round((cam[0] - VIEW_W / 2) * scale)
        y0 = round((cam[1] - VIEW_H / 2) * scale)
        canvas = canvas.crop((x0, y0, x0 + round(VIEW_W * scale), y0 + round(VIEW_H * scale)))
    return canvas


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("maps", nargs="*", help="map JSON files")
    ap.add_argument("--all", action="store_true", help="render every Maps/*.json")
    ap.add_argument("--scale", type=int, default=48, help="output px per map cell (game = 64)")
    ap.add_argument("--out", help="output PNG (single map)")
    ap.add_argument("--out-dir", help="output directory (default: alongside the map as <name>.preview.png in the cwd)")
    ap.add_argument("--markers", action="store_true", help="draw spawn / goal markers")
    ap.add_argument("--view", help="camera centre 'x,y' in cells: render one in-game screen with parallax")
    args = ap.parse_args()

    paths = [Path(p) for p in args.maps]
    if args.all:
        paths += sorted((REPO / "Maps").glob("*.json"))
    if not paths:
        ap.error("give a map file or --all")
    tiles = load_tiles()
    view = tuple(float(v) for v in args.view.split(",")) if args.view else None
    for p in paths:
        doc = json.loads(Path(p).read_text(encoding="utf-8"))
        img = render(doc, tiles, args.scale, args.markers, view)
        if args.out and len(paths) == 1:
            out = Path(args.out)
        else:
            out = Path(args.out_dir or ".") / (Path(p).stem + ".preview.png")
        out.parent.mkdir(parents=True, exist_ok=True)
        img.convert("RGB").save(out)
        print(f"{p} -> {out} ({img.width}x{img.height})")


if __name__ == "__main__":
    main()

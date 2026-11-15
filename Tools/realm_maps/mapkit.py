#!/usr/bin/env python3
"""A tiny authoring kit for writing combat maps as code.

`Maps/*.json` is the game's map format (`Scripts/MapCreation/Data/MapDocument.cs`).
The realm maps are composed here, in Python, rather than clicked together by hand,
because a map is a design argument ("Starfield is wide and empty so snipers rule") and
code keeps that argument legible, reviewable and repeatable: the same script always
writes the same bytes, so a map diff is a design diff.

The kit enforces the editor's own rules while you compose, so a script cannot write a
map the editor would refuse or silently repair on load:
  * one tile per cell per layer (multi-cell footprints included);
  * every footprint inside its layer's grid;
  * tile ids that actually exist (read from the `.tile.json` sidecars);
  * a sub-cell nudge only where the editor allows one (`nudge=` on `put` / `stand` / `spawn`):
    whole quarter cells, -3..3, never on terrain, never pushing a tile out of its grid.

Parallax helper: a far layer is authored in *parallax space* — see `Layer.at_camera`.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Tools"))
import render_map_preview as preview  # noqa: E402  (tile metadata + the offline renderer)

VIEW_W, VIEW_H = preview.VIEW_W, preview.VIEW_H     # one screen, in cells (18 x 10.125)
MARKERS = {"spawn.character", "spawn.respawn", "spawn.enemy", "goal.level"}
STAMP = "2026-11-13T00:00:00.0000000Z"             # fixed: regenerating must not churn the file

_TILES = None


def tiles():
    global _TILES
    if _TILES is None:
        _TILES = preview.load_tiles()
        for m in MARKERS:
            _TILES.setdefault(m, dict(w=1, h=1, fill=False))
    return _TILES


class MapError(Exception):
    pass


class Layer:
    def __init__(self, owner, role, name, grid_w, grid_h, parallax=(1.0, 1.0), loop=False,
                 collision=False, tint=None, opacity=1.0, autoscroll=(0.0, 0.0)):
        self.owner = owner
        self.role, self.name = role, name
        self.grid_w, self.grid_h = int(grid_w), int(grid_h)
        self.parallax = (float(parallax[0]), float(parallax[1]))
        self.loop, self.collision = bool(loop), bool(collision)
        self.tint, self.opacity = tint, float(opacity)
        self.autoscroll = (float(autoscroll[0]), float(autoscroll[1]))
        self.tiles = []
        self._occ = {}

    # ------------------------------------------------------------ placement

    def put(self, def_id, x, y, flip=False, nudge=(0, 0)):
        """Place a tile by its top-left cell.

        `nudge` slides what the tile shows and does - art, collision, a marker's point - by
        whole quarter cells (16 px = 0.5 m), each of -3..3, without changing the cells it
        owns (MapCreation.gdd 2.3). Terrain cannot be nudged."""
        info = tiles().get(def_id)
        if info is None:
            raise MapError(f"{self.owner.name}/{self.name}: unknown tile '{def_id}'")
        x, y = int(x), int(y)
        w, h = info["w"], info["h"]
        if x < 0 or y < 0 or x + w > self.grid_w or y + h > self.grid_h:
            raise MapError(f"{self.owner.name}/{self.name}: '{def_id}' at ({x},{y}) size {w}x{h} "
                           f"leaves the {self.grid_w}x{self.grid_h} grid")
        sub_x, sub_y = self._check_nudge(def_id, info, x, y, nudge)
        for dy in range(h):
            for dx in range(w):
                other = self._occ.get((x + dx, y + dy))
                if other is not None:
                    raise MapError(f"{self.owner.name}/{self.name}: '{def_id}' at ({x},{y}) overlaps "
                                   f"'{other}' at cell ({x + dx},{y + dy})")
        for dy in range(h):
            for dx in range(w):
                self._occ[(x + dx, y + dy)] = def_id
        tile = {"DefId": def_id, "X": x, "Y": y}
        if sub_x:                       # written only when non-zero, like the editor's own saves
            tile["SubX"] = sub_x
        if sub_y:
            tile["SubY"] = sub_y
        if flip:
            tile["FlipX"] = True
        self.tiles.append(tile)
        return self

    def _check_nudge(self, def_id, info, x, y, nudge):
        """The editor's nudge rules (`TilePlacement`), enforced here so a script cannot write
        a nudge that `MapJson.Load` would repair with a warning."""
        sub_x, sub_y = nudge
        where = f"{self.owner.name}/{self.name}: '{def_id}' at ({x},{y})"
        if sub_x != int(sub_x) or sub_y != int(sub_y):
            raise MapError(f"{where}: a nudge is a whole number of quarter cells, got {nudge}")
        sub_x, sub_y = int(sub_x), int(sub_y)
        if sub_x == 0 and sub_y == 0:
            return 0, 0
        limit = preview.MAX_NUDGE
        if abs(sub_x) > limit or abs(sub_y) > limit:
            raise MapError(f"{where}: nudge {nudge} is outside -{limit}..{limit} quarter cells "
                           f"(move the tile a whole cell instead)")
        if preview.grid_locked(def_id, info):
            raise MapError(f"{where}: terrain and rule zones stay on the grid and cannot be nudged")
        n = preview.SUBCELLS
        if (x * n + sub_x < 0 or y * n + sub_y < 0 or
                (x + info["w"]) * n + sub_x > self.grid_w * n or (y + info["h"]) * n + sub_y > self.grid_h * n):
            raise MapError(f"{where}: nudge {nudge} pushes it out of the {self.grid_w}x{self.grid_h} grid")
        return sub_x, sub_y

    def stand(self, def_id, x, floor_y, flip=False, nudge=(0, 0)):
        """Place a tile so it STANDS on row `floor_y` (its bottom edge on that row's top)."""
        return self.put(def_id, x, floor_y - tiles()[def_id]["h"], flip, nudge)

    def fill(self, def_id, x0, y0, x1, y1):
        """Fill the inclusive cell rectangle with a 1x1 tile; cells already taken are skipped
        so overlapping rectangles compose (a wall crossing a floor, a pool cut into rock)."""
        for y in range(int(y0), int(y1) + 1):
            for x in range(int(x0), int(x1) + 1):
                if (x, y) not in self._occ:
                    self.put(def_id, x, y)
        return self

    def carve(self, x0, y0, x1, y1):
        """Remove whatever 1x1 tiles lie in the inclusive rectangle."""
        box = {(x, y) for y in range(int(y0), int(y1) + 1) for x in range(int(x0), int(x1) + 1)}
        keep = []
        for t in self.tiles:
            info = tiles()[t["DefId"]]
            if info["w"] == 1 and info["h"] == 1 and (t["X"], t["Y"]) in box:
                del self._occ[(t["X"], t["Y"])]
            else:
                keep.append(t)
        self.tiles = keep
        return self

    def row(self, def_id, y, xs):
        for x in xs:
            self.put(def_id, x, y)
        return self

    def tile_x(self, def_id, y, x0=0, x1=None, flip_alternate=False):
        """Lay a tiling sprite end to end across the layer (looping horizons, mist, walls)."""
        w = tiles()[def_id]["w"]
        x1 = self.grid_w if x1 is None else x1
        i = 0
        x = x0
        while x + w <= x1:
            self.put(def_id, x, y, flip=flip_alternate and i % 2 == 1)
            x += w
            i += 1
        return self

    def free(self, x, y):
        return (int(x), int(y)) not in self._occ

    # ------------------------------------------------------------- parallax

    def at_camera(self, cam_x, cam_y):
        """Where to author a point on this layer so that it sits at the CENTRE of the screen
        when the camera is centred on (cam_x, cam_y), in cells.

        The game draws a layer at `authored + (camera - anchor) * (1 - parallax)`, where the
        anchor is the character spawn point (MapCreation.gdd §1). So a far thing meant to be
        seen from camera C is authored at `anchor + parallax * (C - anchor)`: far layers are
        painted compressed toward the spawn, and unfold as the camera travels.
        """
        ax, ay = self.owner.anchor()
        return (ax + self.parallax[0] * (cam_x - ax), ay + self.parallax[1] * (cam_y - ay))

    def srow(self, screen_row):
        """Authored row that lands on `screen_row` of the START screen (0 = top edge,
        VIEW_H = bottom edge). The spawn sits near the floor while the camera is clamped
        inside the map, so even at spawn a far layer is not drawn at its authored rows;
        composing a sky in screen rows keeps that arithmetic in one place.
        As the camera climbs by N cells a layer sinks on screen by `parallax_y * N`."""
        _, ay = self.owner.anchor()
        _, c0y = self.owner.start_camera()
        return ay + (screen_row - VIEW_H / 2.0) + self.parallax[1] * (c0y - ay)

    def scol(self, screen_col):
        """Authored column that lands on `screen_col` of the START screen (0 = left edge)."""
        ax, _ = self.owner.anchor()
        c0x, _ = self.owner.start_camera()
        return ax + (screen_col - VIEW_W / 2.0) + self.parallax[0] * (c0x - ax)

    def pin(self, def_id, screen_col, screen_row, flip=False):
        """Place a tile by its top-left corner on the START screen. For a near-zero parallax
        layer that is where it stays for the whole fight — compose it like a picture frame."""
        return self.put(def_id, round(self.scol(screen_col)), round(self.srow(screen_row)), flip)

    def seen_from(self, def_id, cam_x, base_screen_row, flip=False):
        """Stand a far tile so it is centred on screen when the camera's centre reaches column
        `cam_x`, with its base on `base_screen_row` of the start screen. Far layers hold fewer
        things than you expect: at parallax p, a tile W cells wide uses up W / p cells of travel."""
        _, c0y = self.owner.start_camera()
        x, _ = self.at_camera(cam_x, c0y)
        info = tiles()[def_id]
        return self.put(def_id, round(x - info["w"] / 2.0), round(self.srow(base_screen_row)) - info["h"], flip)

    def to_json(self):
        return {
            "Role": self.role, "Name": self.name,
            "ParallaxX": self.parallax[0], "ParallaxY": self.parallax[1],
            "Loop": self.loop, "Collision": self.collision, "Tint": self.tint, "Opacity": self.opacity,
            "SwayAmplitudePx": 0.0, "SwayPeriodSec": 4.0,
            "AutoScrollX": self.autoscroll[0], "AutoScrollY": self.autoscroll[1],
            "GridW": self.grid_w, "GridH": self.grid_h,
            "Tiles": self.tiles,
        }


class Map:
    # `terrain` defaults to "*" (every altitude band), not the editor's "sea-level": the
    # overworld gives each combat node low-ground / sea-level / high-ground from its altitude,
    # and a realm that ships ONE map must serve all three or two nodes in three fall back to
    # the legacy arena (`CombatMapCatalog`). Pass a specific band only once a realm has a map
    # per band. `MapContentCheck` fails a realm that leaves a band uncovered.
    def __init__(self, map_id, name, world, w, h, canvas, terrain="*",
                 goals=("claim", "protect", "destroy", "slaughter"), hardship=(), crab=50, seagull=50):
        self.id, self.name, self.world = map_id, name, world
        self.w, self.h = int(w), int(h)
        self.canvas, self.terrain = canvas, terrain
        self.goals, self.hardship = list(goals), list(hardship)
        self.crab, self.seagull = crab, seagull
        self.layers = []
        self.battlefield = None
        self._spawn = None
        self._spawn_nudge = (0, 0)

    # layers are appended back to front; add farviews, then the battlefield, then closeviews
    def farview(self, name, parallax, loop=False, grid=None, tint=None, opacity=1.0, autoscroll=(0.0, 0.0),
                collision=False):
        """`collision=True` (non-looping layers only) makes the layer's SoftVolume tiles real,
        fixed-world volumes - how a sea is laid over a whole map without taking the
        battlefield's cells (`MapCreation.gdd` section 3)."""
        gw, gh = grid or ((16 if loop else self.w), self.h)
        layer = Layer(self, "farview", name, gw, gh, parallax, loop, bool(collision) and not loop, tint, opacity, autoscroll)
        self.layers.append(layer)
        return layer

    def field(self):
        self.battlefield = Layer(self, "battlefield", "Battlefield", self.w, self.h, (1.0, 1.0), False, True)
        self.layers.append(self.battlefield)
        return self.battlefield

    def closeview(self, name="Foreground", tint=None, opacity=1.0):
        layer = Layer(self, "closeview", name, self.w, self.h, (1.0, 1.0), False, False, tint, opacity)
        self.layers.append(layer)
        return layer

    def spawn(self, x, floor_y, nudge=(0, 0)):
        """Character spawn standing on row `floor_y`. Must be set before any far layer uses
        `at_camera`, because the spawn is the parallax anchor. `nudge` (quarter cells, see
        `Layer.put`) moves the spawn point - and with it the anchor - off the cell centre."""
        self._spawn = (int(x), int(floor_y) - 1)
        self._spawn_nudge = (int(nudge[0]), int(nudge[1]))
        return self

    def anchor(self):
        """The parallax anchor, in cells: the spawn marker point the game hands to every art
        layer (`CombatMapBuild.AnchorParallax`) — the middle of the spawn cell, plus the
        spawn's nudge (`TilePlacement.Marker`)."""
        if self._spawn is None:
            raise MapError(f"{self.name}: call spawn() before composing far layers")
        return (self._spawn[0] + 0.5 + self._spawn_nudge[0] / preview.SUBCELLS,
                self._spawn[1] + 0.5 + self._spawn_nudge[1] / preview.SUBCELLS)

    def start_camera(self):
        """Camera centre when the fight starts: the spawn, clamped so the screen stays inside
        the battlefield (Camera2D limits = the battlefield rectangle)."""
        ax, ay = self.anchor()
        clamp = lambda v, lo, hi: min(max(v, lo), max(lo, hi))
        return (clamp(ax, VIEW_W / 2, self.w - VIEW_W / 2), clamp(ay, VIEW_H / 2, self.h - VIEW_H / 2))

    def finish(self):
        bf = self.battlefield
        if bf is None:
            raise MapError(f"{self.name}: no battlefield")
        if self._spawn is None:
            raise MapError(f"{self.name}: no character spawn")
        bf.put("spawn.character", *self._spawn, nudge=self._spawn_nudge)
        counts = {m: sum(1 for t in bf.tiles if t["DefId"] == m) for m in MARKERS}
        for need in ("spawn.respawn", "spawn.enemy", "goal.level"):
            if counts[need] == 0:
                raise MapError(f"{self.name}: needs at least one {need}")
        return self

    def to_json(self):
        return {
            "Version": 2, "Id": self.id, "Name": self.name,
            "World": self.world, "Worlds": [self.world],
            "HardshipLevels": self.hardship, "Goals": self.goals, "Terrain": self.terrain,
            "FoeCompositions": [{"Level": 0, "CrabWeight": self.crab, "SeagullWeight": self.seagull}],
            "FoeSpawnRules": {"CrabMaxCellY": None, "SeagullMinCellY": None},
            "CreatedUtc": STAMP, "ModifiedUtc": STAMP,
            "Canvas": {"Type": "solidColor", "Color": self.canvas, "ModeId": None},
            "Layers": [layer.to_json() for layer in self.layers],
        }

    def write(self, path):
        doc = self.to_json()
        lines = ["{"]
        keys = list(doc.keys())
        for k in keys[:-1]:
            lines.append(f"  {json.dumps(k)}: {json.dumps(doc[k], ensure_ascii=False)},")
        lines.append('  "Layers": [')
        for li, layer in enumerate(doc["Layers"]):
            head = {k: v for k, v in layer.items() if k != "Tiles"}
            lines.append("    {")
            for k, v in head.items():
                lines.append(f"      {json.dumps(k)}: {json.dumps(v)},")
            lines.append('      "Tiles": [')
            tl = layer["Tiles"]
            for ti, t in enumerate(tl):
                lines.append("        " + json.dumps(t) + ("," if ti < len(tl) - 1 else ""))
            lines.append("      ]")
            lines.append("    }" + ("," if li < len(doc["Layers"]) - 1 else ""))
        lines.append("  ]")
        lines.append("}")
        Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")
        return doc

    def ascii(self):
        """The battlefield as text — one character per cell — for review in a terminal."""
        bf = self.battlefield
        sym = {"ground": "#", "platform": "=", "softvolume": ":", "hazard": "^", "deco": "'",
               "spawn.character": "P", "spawn.respawn": "r", "spawn.enemy": "E", "goal.level": "G"}
        g = [["." for _ in range(bf.grid_w)] for _ in range(bf.grid_h)]
        for t in bf.tiles:
            info = tiles()[t["DefId"]]
            ch = sym.get(t["DefId"]) or sym.get(t["DefId"].split(".")[0], "?")
            for dy in range(info["h"]):
                for dx in range(info["w"]):
                    g[t["Y"] + dy][t["X"] + dx] = ch
        return "\n".join(f"{y:3d} " + "".join(row) for y, row in enumerate(g))

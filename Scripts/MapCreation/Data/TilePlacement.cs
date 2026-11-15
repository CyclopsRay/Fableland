namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// GDD §2.3 "Sub-cell nudge" — the ONE place a placed tile's anchor cell and nudge become
/// pixels. The editor canvas, the Playtest, the arena, the map-card thumbnails and the
/// collision/hazard/marker builders all ask here, so a nudged bench draws and collides at
/// the same spot in every view (the same lesson as `TileVisualResolver`: three copies of
/// "where is this tile" drift, because each still puts it *somewhere*).
///
/// Model: a tile's integer anchor (X, Y) owns its cells — occupancy, erase, selection and
/// autotile neighbours never look at the nudge. The nudge (SubX, SubY) is a whole number of
/// quarter cells, −3..3, that slides everything the tile *shows and does* — art, effect
/// area, marker point — as one rigid piece. A quarter cell is 16 px = 0.5 m: the same 4×4
/// sub-grid the effect painter uses (`ShapeDef.SubcellsPerAxis`), so a nudged collider still
/// lands on that sub-grid.
///
/// Terrain stays on the grid: ground, autotiled fills (lava) and rule zones are defined by
/// which cells they cover and which neighbours they touch, so a nudge on one is ignored here
/// and removed on load. Pure C# — no Godot.
/// </summary>
public static class TilePlacement
{
    /// <summary>Nudge steps per cell (4 → a step is 16 px = 0.5 m).</summary>
    public const int SubcellsPerCell = ShapeDef.SubcellsPerAxis;

    /// <summary>Largest nudge in either direction. One step further is a whole cell, which
    /// is the anchor's job (`TileNudge` carries it).</summary>
    public const int MaxNudge = SubcellsPerCell - 1;

    /// <summary>One nudge step in pixels.</summary>
    public static readonly float SubcellPx = MapGrid.PixelsPerCell / SubcellsPerCell;

    /// <summary>True for kinds that cannot be nudged: terrain (ground, and any connected-look
    /// fill such as lava), rule zones, and ids this build does not know.</summary>
    public static bool IsGridLocked(TileDef def) =>
        def == null || def.Category == TileCategory.Ground || def.Category == TileCategory.Rule ||
        !string.IsNullOrEmpty(def.AutotileGroup);

    /// <summary>The nudge that applies to this tile, in quarter cells: its stored value kept
    /// within ±<see cref="MaxNudge"/>, or 0 for a grid-locked kind.</summary>
    public static int NudgeX(PlacedTile tile, TileDef def) =>
        tile == null || IsGridLocked(def) ? 0 : Math.Clamp(tile.SubX, -MaxNudge, MaxNudge);

    /// <inheritdoc cref="NudgeX"/>
    public static int NudgeY(PlacedTile tile, TileDef def) =>
        tile == null || IsGridLocked(def) ? 0 : Math.Clamp(tile.SubY, -MaxNudge, MaxNudge);

    public static bool IsNudged(PlacedTile tile, TileDef def) =>
        NudgeX(tile, def) != 0 || NudgeY(tile, def) != 0;

    /// <summary>Top-left corner of the tile's footprint, in layer pixels: where its art is
    /// laid out from and what its effect area is relative to.</summary>
    public static (float X, float Y) Origin(PlacedTile tile, TileDef def) =>
        (tile.X * MapGrid.PixelsPerCell + NudgeX(tile, def) * SubcellPx,
         tile.Y * MapGrid.PixelsPerCell + NudgeY(tile, def) * SubcellPx);

    /// <summary>The point a marker tile (character/respawn/enemy spawn, level goal) stands
    /// for: the middle of its cell, one metre above the cell's floor — a 2 m body's centre
    /// when its feet are on that floor.</summary>
    public static (float X, float Y) Marker(PlacedTile tile, TileDef def)
    {
        (float x, float y) = Origin(tile, def);
        return (x + MapGrid.PixelsPerCell * 0.5f, y + MapGrid.PixelsPerCell - Units.Px(1f));
    }

    /// <summary>The nudges that keep a footprint inside its layer: a tile anchored at
    /// <paramref name="cell"/> spanning <paramref name="span"/> cells of a grid
    /// <paramref name="grid"/> cells long. A nudged tile never leaves the grid, the same rule
    /// §2.3 gives footprints. Always contains 0.</summary>
    public static (int Min, int Max) NudgeRange(int cell, int span, int grid)
    {
        int min = Math.Max(-MaxNudge, -cell * SubcellsPerCell);
        int max = Math.Min(MaxNudge, (grid - cell - span) * SubcellsPerCell);
        return (Math.Min(min, 0), Math.Max(max, 0));
    }

    /// <summary>True when a footprint anchored at (<paramref name="x"/>, <paramref name="y"/>)
    /// with the given nudge lies wholly inside a <paramref name="gridW"/> ×
    /// <paramref name="gridH"/> layer. The one in-grid test for anything that puts a tile
    /// somewhere new — nudge, move, paste — so they cannot disagree about the edge.</summary>
    public static bool FitsGrid(int x, int y, int subX, int subY, int footprintW, int footprintH,
        int gridW, int gridH) =>
        Overhang(x, y, subX, subY, footprintW, footprintH, gridW, gridH) == 0;

    /// <summary>How far that footprint sticks out of the layer, in quarter cells summed over
    /// the four sides; 0 when it is inside. A layer can be shrunk under a tile that was
    /// nudged toward its edge, so "outside" is a state a nudge must be able to walk back out
    /// of: <see cref="TileNudge"/> refuses a step that makes this number grow, not one that
    /// merely leaves it above zero.</summary>
    public static int Overhang(int x, int y, int subX, int subY, int footprintW, int footprintH,
        int gridW, int gridH)
    {
        int left = x * SubcellsPerCell + subX;
        int top = y * SubcellsPerCell + subY;
        int right = left + footprintW * SubcellsPerCell;
        int bottom = top + footprintH * SubcellsPerCell;
        return Math.Max(0, -left) + Math.Max(0, -top) +
               Math.Max(0, right - gridW * SubcellsPerCell) + Math.Max(0, bottom - gridH * SubcellsPerCell);
    }

    /// <summary>Brings a tile's stored nudge into its legal form — zero on a grid-locked
    /// kind, within ±<see cref="MaxNudge"/>, and inside the layer grid. Returns true when it
    /// had to change something, so a loader can say so.</summary>
    public static bool Normalize(PlacedTile tile, TileDef def, int gridW, int gridH)
    {
        if (tile == null) return false;

        int x = 0, y = 0;
        if (!IsGridLocked(def))
        {
            (int minX, int maxX) = NudgeRange(tile.X, def.FootprintW, gridW);
            (int minY, int maxY) = NudgeRange(tile.Y, def.FootprintH, gridH);
            x = Math.Clamp(tile.SubX, minX, maxX);
            y = Math.Clamp(tile.SubY, minY, maxY);
        }

        if (x == tile.SubX && y == tile.SubY) return false;
        tile.SubX = x;
        tile.SubY = y;
        return true;
    }

    /// <summary>A copy of a placed tile at another anchor — the one way to duplicate a tile,
    /// so a nudge, a flip or a per-tile prop cannot be lost by a copy that forgot it.</summary>
    public static PlacedTile CopyAt(PlacedTile source, int x, int y) => new()
    {
        DefId = source.DefId,
        X = x,
        Y = y,
        SubX = source.SubX,
        SubY = source.SubY,
        FlipX = source.FlipX,
        Props = source.Props == null ? null : new Dictionary<string, string>(source.Props),
    };

    /// <summary>Headless guard for the placement arithmetic and the grid-lock rule.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

        float cell = MapGrid.PixelsPerCell;
        Check(Near(SubcellPx * SubcellsPerCell, cell), "a nudge step is not a quarter of a cell");

        if (!TileRegistry.TryGet("platform.bench", out TileDef bench) ||
            !TileRegistry.TryGet("ground.stone", out TileDef stone) ||
            !TileRegistry.TryGet("rule.cloudZone", out TileDef rule) ||
            !TileRegistry.TryGet("spawn.character", out TileDef spawn) ||
            !TileRegistry.TryGet("hazard.bonfire", out TileDef bonfire))
        {
            failures.Add("placement self-test: a core tile kind is missing from the registry");
            return failures;
        }

        Check(!IsGridLocked(bench) && !IsGridLocked(spawn) && !IsGridLocked(bonfire),
            "props, markers and stand-alone hazards must be nudgeable");
        Check(IsGridLocked(stone) && IsGridLocked(rule) && IsGridLocked(null),
            "ground, rule zones and unknown kinds must stay on the grid");
        foreach (TileDef def in TileRegistry.All)
        {
            if (!string.IsNullOrEmpty(def.AutotileGroup))
                Check(IsGridLocked(def), $"autotiled kind '{def.Id}' must stay on the grid");
        }

        var plain = new PlacedTile { DefId = bench.Id, X = 5, Y = 7 };
        (float px, float py) = Origin(plain, bench);
        Check(Near(px, 5 * cell) && Near(py, 7 * cell), "an un-nudged tile must sit on its anchor cell");

        var nudged = new PlacedTile { DefId = bench.Id, X = 5, Y = 7, SubX = 2, SubY = -1 };
        (float nx, float ny) = Origin(nudged, bench);
        Check(Near(nx, 5 * cell + 2 * SubcellPx) && Near(ny, 7 * cell - SubcellPx),
            "a nudge must move the origin by whole quarter cells");
        Check(IsNudged(nudged, bench) && !IsNudged(plain, bench), "IsNudged disagrees with the stored nudge");

        var ground = new PlacedTile { DefId = stone.Id, X = 5, Y = 7, SubX = 2, SubY = 2 };
        (float gx, float gy) = Origin(ground, stone);
        Check(Near(gx, 5 * cell) && Near(gy, 7 * cell), "ground must ignore a stored nudge");

        var wild = new PlacedTile { DefId = bench.Id, X = 5, Y = 7, SubX = 40, SubY = -40 };
        Check(NudgeX(wild, bench) == MaxNudge && NudgeY(wild, bench) == -MaxNudge,
            "an out-of-range nudge must be read as the nearest legal one");

        var marker = new PlacedTile { DefId = spawn.Id, X = 1, Y = 9, SubX = 2 };
        (float mx, float my) = Marker(marker, spawn);
        Check(Near(mx, 1.5f * cell + 2 * SubcellPx) && Near(my, 10 * cell - Units.Px(1f)),
            "a nudged marker's point must move with it");

        // A 2-wide bench in a 10x6 grid: free in the middle, pinned at the edges.
        Check(NudgeRange(4, 2, 10) == (-MaxNudge, MaxNudge), "mid-grid nudge range is not the full range");
        Check(NudgeRange(0, 2, 10) == (0, MaxNudge), "a tile on the first column must not nudge left");
        Check(NudgeRange(8, 2, 10) == (-MaxNudge, 0), "a tile on the last column must not nudge right");

        Check(FitsGrid(8, 5, 0, 0, 2, 1, 10, 6) && !FitsGrid(8, 5, 1, 0, 2, 1, 10, 6) &&
              FitsGrid(7, 5, MaxNudge, 0, 2, 1, 10, 6) && !FitsGrid(0, 0, 0, -1, 2, 1, 10, 6) &&
              !FitsGrid(9, 5, 0, 0, 2, 1, 10, 6),
            "FitsGrid must count the nudge as part of the footprint");
        Check(Overhang(8, 5, 0, 0, 2, 1, 10, 6) == 0 && Overhang(8, 5, 3, 0, 2, 1, 10, 6) == 3 &&
              Overhang(0, 0, -2, -1, 2, 1, 10, 6) == 3,
            "Overhang must count quarter cells outside the layer on every side");

        var edge = new PlacedTile { DefId = bench.Id, X = 0, Y = 5, SubX = -2, SubY = 3 };
        Check(Normalize(edge, bench, 10, 6) && edge.SubX == 0 && edge.SubY == 0,
            "Normalize must pull a nudge back inside the layer grid");
        Check(Normalize(wild, bench, 20, 20) && wild.SubX == MaxNudge && wild.SubY == -MaxNudge,
            "Normalize must clamp an out-of-range nudge");
        Check(Normalize(ground, stone, 20, 20) && ground.SubX == 0 && ground.SubY == 0,
            "Normalize must clear a nudge on ground");
        Check(!Normalize(nudged, bench, 20, 20) && nudged.SubX == 2 && nudged.SubY == -1,
            "Normalize must leave a legal nudge alone");

        var source = new PlacedTile
        {
            DefId = bench.Id, X = 1, Y = 1, SubX = -2, SubY = 1, FlipX = true,
            Props = new Dictionary<string, string> { ["k"] = "v" },
        };
        PlacedTile copy = CopyAt(source, 6, 3);
        Check(copy.X == 6 && copy.Y == 3 && copy.SubX == -2 && copy.SubY == 1 && copy.FlipX &&
              copy.DefId == bench.Id && copy.Props != null && copy.Props["k"] == "v" &&
              !ReferenceEquals(copy.Props, source.Props),
            "CopyAt must carry the nudge, the flip and the props to the new anchor");

        return failures;
    }
}

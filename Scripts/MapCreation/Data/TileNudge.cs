namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>One tile's part in a nudge: where it was and where it goes. Applying and
/// reverting set exactly the four numbers a nudge may touch.</summary>
public readonly struct NudgeMove
{
    public readonly PlacedTile Tile;
    public readonly int FromX, FromY, FromSubX, FromSubY;
    public readonly int ToX, ToY, ToSubX, ToSubY;

    public NudgeMove(PlacedTile tile, int toX, int toY, int toSubX, int toSubY)
    {
        Tile = tile;
        FromX = tile.X; FromY = tile.Y; FromSubX = tile.SubX; FromSubY = tile.SubY;
        ToX = toX; ToY = toY; ToSubX = toSubX; ToSubY = toSubY;
    }

    /// <summary>True when the step crossed a cell boundary, so the tile changes the cells it owns.</summary>
    public bool MovesAnchor => FromX != ToX || FromY != ToY;

    public void Apply()
    {
        Tile.X = ToX; Tile.Y = ToY; Tile.SubX = ToSubX; Tile.SubY = ToSubY;
    }

    public void Revert()
    {
        Tile.X = FromX; Tile.Y = FromY; Tile.SubX = FromSubX; Tile.SubY = FromSubY;
    }
}

/// <summary>The outcome of planning a nudge. Nothing is changed until the moves are applied.</summary>
public sealed class NudgePlan
{
    /// <summary>What to change. Empty when the nudge cannot happen.</summary>
    public readonly List<NudgeMove> Moves = new();

    /// <summary>Selected tiles that stay where they are because their kind is grid-locked.</summary>
    public int Held;

    /// <summary>Why nothing moves, in words for the status bar; null when the nudge is good.</summary>
    public string Blocker;

    public bool Ok => Blocker == null && Moves.Count > 0;
}

/// <summary>
/// GDD §2.3 "Sub-cell nudge" — plans one quarter-cell step for a selection (the editor's
/// Alt+arrow). Pure and side-effect free, so the editor can wrap the result in one undoable
/// command and this file can test every rule headlessly.
///
/// Rules:
///   • every nudgeable selected tile moves by the same step, or none do — a nudge never
///     changes a group's arrangement;
///   • a tile's nudge stays within ±3 quarter cells. A fourth step is a whole cell, so it
///     CARRIES: the anchor moves one cell and the nudge returns to 0, provided the cells it
///     would then own are free. The tile is in the same place on screen either way; what
///     changes is which cells it owns;
///   • a nudged footprint never leaves its layer's grid;
///   • nothing is ever overwritten. Unlike a Move, a blocked nudge simply does not happen
///     — one key press must not be able to delete a neighbour;
///   • grid-locked kinds in the selection (terrain, rule zones) are left where they are.
/// </summary>
public static class TileNudge
{
    /// <param name="stepX">−1, 0 or +1 quarter cell (only the sign is used).</param>
    /// <param name="stepY">−1, 0 or +1 quarter cell, positive = down.</param>
    public static NudgePlan Plan(MapLayerData layer, ICollection<PlacedTile> selection,
        int stepX, int stepY, Func<string, TileDef> lookup)
    {
        var plan = new NudgePlan();
        stepX = Math.Sign(stepX);
        stepY = Math.Sign(stepY);

        if (layer?.Tiles == null || selection == null || selection.Count == 0)
        {
            plan.Blocker = "nothing is selected";
            return plan;
        }
        if (stepX == 0 && stepY == 0)
        {
            plan.Blocker = "no direction";
            return plan;
        }

        // Walk the layer's own list, not the selection set, so the plan's order is stable.
        foreach (PlacedTile tile in layer.Tiles)
        {
            if (tile == null || !selection.Contains(tile)) continue;

            TileDef def = lookup?.Invoke(tile.DefId);
            if (TilePlacement.IsGridLocked(def))
            {
                plan.Held++;
                continue;
            }

            int toX = tile.X, toY = tile.Y;
            int toSubX = Carry(TilePlacement.NudgeX(tile, def) + stepX, ref toX);
            int toSubY = Carry(TilePlacement.NudgeY(tile, def) + stepY, ref toY);

            // A nudged footprint never leaves the layer. Compared against where the tile is
            // now rather than against zero: a layer shrunk under a nudged tile leaves it
            // partly outside, and it must still be able to step back in.
            int outsideNow = TilePlacement.Overhang(tile.X, tile.Y, TilePlacement.NudgeX(tile, def),
                TilePlacement.NudgeY(tile, def), def.FootprintW, def.FootprintH, layer.GridW, layer.GridH);
            int outsideThen = TilePlacement.Overhang(toX, toY, toSubX, toSubY, def.FootprintW, def.FootprintH,
                layer.GridW, layer.GridH);
            if (outsideThen > outsideNow)
                return Blocked(plan, $"{NameOf(def)} is at the edge of the layer");

            plan.Moves.Add(new NudgeMove(tile, toX, toY, toSubX, toSubY));
        }

        if (plan.Moves.Count == 0)
        {
            return Blocked(plan, plan.Held > 0
                ? "terrain and rule zones stay on the grid"
                : "nothing is selected on this layer");
        }

        // Only a carry changes which cells a tile owns. Check those against every tile that
        // keeps its cells, and against each other.
        var carried = new HashSet<PlacedTile>();
        foreach (NudgeMove move in plan.Moves)
            if (move.MovesAnchor) carried.Add(move.Tile);
        if (carried.Count == 0) return plan;

        var owner = new Dictionary<(int X, int Y), PlacedTile>();
        foreach (PlacedTile tile in layer.Tiles)
        {
            if (tile == null || carried.Contains(tile)) continue;
            TileDef def = lookup?.Invoke(tile.DefId);
            for (int dy = 0; dy < (def?.FootprintH ?? 1); dy++)
            for (int dx = 0; dx < (def?.FootprintW ?? 1); dx++)
                owner[(tile.X + dx, tile.Y + dy)] = tile;
        }

        foreach (NudgeMove move in plan.Moves)
        {
            if (!move.MovesAnchor) continue;
            TileDef def = lookup(move.Tile.DefId);
            for (int dy = 0; dy < def.FootprintH; dy++)
            for (int dx = 0; dx < def.FootprintW; dx++)
            {
                (int X, int Y) cellKey = (move.ToX + dx, move.ToY + dy);
                if (owner.TryGetValue(cellKey, out PlacedTile other))
                {
                    return Blocked(plan,
                        $"{NameOf(def)} cannot cross into cell ({cellKey.X}, {cellKey.Y}): " +
                        $"{NameOf(lookup(other.DefId))} is there");
                }
                owner[cellKey] = move.Tile;
            }
        }

        return plan;
    }

    /// <summary>Keeps a nudge within ±3: a fourth quarter step moves the anchor a cell.</summary>
    private static int Carry(int nudge, ref int cell)
    {
        if (nudge > TilePlacement.MaxNudge) { cell += 1; return 0; }
        if (nudge < -TilePlacement.MaxNudge) { cell -= 1; return 0; }
        return nudge;
    }

    private static NudgePlan Blocked(NudgePlan plan, string why)
    {
        plan.Moves.Clear();
        plan.Blocker = why;
        return plan;
    }

    private static string NameOf(TileDef def) =>
        string.IsNullOrEmpty(def?.DisplayName) ? (def?.Id ?? "A tile") : def.DisplayName;

    /// <summary>Headless guard for every rule in the class summary.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        static TileDef Lookup(string id) => TileRegistry.TryGet(id, out TileDef def) ? def : null;

        if (Lookup("platform.bench") == null || Lookup("deco.flower") == null || Lookup("ground.stone") == null)
        {
            failures.Add("nudge self-test: a core tile kind is missing from the registry");
            return failures;
        }

        // A 12x6 layer: a 2x1 bench at cells 4-5, a flower two cells to its right, floor below.
        MapLayerData layer = MapLayerData.CreateBattlefield(12, 6);
        var bench = new PlacedTile { DefId = "platform.bench", X = 4, Y = 3 };
        var flower = new PlacedTile { DefId = "deco.flower", X = 7, Y = 3 };
        var floor = new PlacedTile { DefId = "ground.stone", X = 4, Y = 4 };
        layer.Tiles.Add(bench);
        layer.Tiles.Add(flower);
        layer.Tiles.Add(floor);

        NudgePlan Step(int dx, int dy, params PlacedTile[] picked)
        {
            NudgePlan plan = Plan(layer, new HashSet<PlacedTile>(picked), dx, dy, Lookup);
            foreach (NudgeMove move in plan.Moves) move.Apply();
            return plan;
        }

        // Three quarter steps stay on the anchor; the fourth carries into the next cell.
        for (int i = 1; i <= TilePlacement.MaxNudge; i++)
        {
            Check(Step(1, 0, bench).Ok && bench.X == 4 && bench.SubX == i,
                $"step {i} right must keep the anchor and add a quarter cell");
        }
        NudgePlan carry = Step(1, 0, bench);
        Check(carry.Ok && bench.X == 5 && bench.SubX == 0 && carry.Moves[0].MovesAnchor,
            "a fourth quarter step must carry the anchor one cell and clear the nudge");

        // Bench now owns cells 5-6, the flower owns 7. Crossing into 7 is refused, and a
        // refused nudge changes nothing.
        for (int i = 0; i < TilePlacement.MaxNudge; i++) Step(1, 0, bench);
        NudgePlan blocked = Step(1, 0, bench);
        Check(!blocked.Ok && blocked.Moves.Count == 0 && blocked.Blocker != null && bench.X == 5 &&
              bench.SubX == TilePlacement.MaxNudge,
            "a carry into an occupied cell must be refused and change nothing");
        Check(layer.Tiles.Count == 3 && layer.Tiles.Contains(flower), "a nudge must never remove a tile");

        // Selected together, both carry in the same step and keep their arrangement.
        flower.SubX = TilePlacement.MaxNudge;
        NudgePlan together = Step(1, 0, bench, flower);
        Check(together.Ok && bench.X == 6 && flower.X == 8 && bench.SubX == 0 && flower.SubX == 0,
            "tiles nudged together must be able to carry into each other's old cells");

        // Undo is exact.
        foreach (NudgeMove move in together.Moves) move.Revert();
        Check(bench.X == 5 && bench.SubX == TilePlacement.MaxNudge && flower.X == 7 &&
              flower.SubX == TilePlacement.MaxNudge, "reverting a nudge must restore every number");

        // Vertical, and the opposite direction passes back through zero.
        bench.SubX = 0; flower.SubX = 0;
        Check(Step(0, -1, bench).Ok && bench.SubY == -1 && bench.Y == 3, "a step up must be a negative quarter cell");
        Check(Step(0, 1, bench).Ok && bench.SubY == 0 && bench.Y == 3, "stepping back must return to zero");

        // Terrain is held; a selection of only terrain does nothing.
        NudgePlan mixed = Step(-1, 0, bench, floor);
        Check(mixed.Ok && mixed.Held == 1 && mixed.Moves.Count == 1 && floor.SubX == 0 && bench.SubX == -1,
            "terrain in a selection must stay put while the props move");
        NudgePlan onlyTerrain = Step(-1, 0, floor);
        Check(!onlyTerrain.Ok && onlyTerrain.Blocker != null && floor.X == 4 && floor.SubX == 0,
            "a selection of only terrain must not be nudged");

        // The layer's edge stops a nudge: left of column 0, and right of the last column.
        var atLeft = new PlacedTile { DefId = "deco.flower", X = 0, Y = 0 };
        var atRight = new PlacedTile { DefId = "deco.flower", X = 11, Y = 0 };
        layer.Tiles.Add(atLeft);
        layer.Tiles.Add(atRight);
        Check(!Step(-1, 0, atLeft).Ok && atLeft.X == 0 && atLeft.SubX == 0, "a tile must not nudge off the left edge");
        Check(!Step(1, 0, atRight).Ok && atRight.X == 11 && atRight.SubX == 0, "a tile must not nudge off the right edge");
        Check(!Step(0, -1, atLeft).Ok && atLeft.SubY == 0, "a tile must not nudge off the top edge");
        Check(Step(1, 0, atLeft).Ok && atLeft.SubX == 1, "a tile on the edge must still nudge inward");

        // One blocked tile holds the whole group back.
        bench.SubX = 0;
        NudgePlan group = Step(-1, 0, bench, atLeft);   // atLeft: +1 -> 0 is fine; both move
        Check(group.Ok && atLeft.SubX == 0 && bench.SubX == -1, "a group step must move every member");
        NudgePlan stuck = Step(-1, 0, bench, atLeft);   // atLeft would leave the grid
        Check(!stuck.Ok && bench.SubX == -1 && atLeft.SubX == 0,
            "one member at the edge must stop the whole group, not split it");

        // A layer shrunk under a nudged tile leaves it sticking out. It must be able to
        // step back inside, and still not step further out.
        var overhung = new PlacedTile { DefId = "deco.flower", X = 9, Y = 1, SubX = 2 };
        layer.Tiles.Add(overhung);
        layer.GridW = 10;
        Check(!Step(1, 0, overhung).Ok && overhung.SubX == 2, "a tile already past the edge must not step further out");
        Check(Step(-1, 0, overhung).Ok && overhung.SubX == 1, "a tile already past the edge must be able to step back in");
        Check(Step(0, 1, overhung).Ok && overhung.SubY == 1, "a tile past one edge must still move along it");
        layer.GridW = 12;

        Check(!Plan(layer, new HashSet<PlacedTile>(), 1, 0, Lookup).Ok, "an empty selection must not plan a nudge");
        Check(!Plan(layer, new HashSet<PlacedTile> { bench }, 0, 0, Lookup).Ok, "a zero step must not plan a nudge");

        return failures;
    }
}

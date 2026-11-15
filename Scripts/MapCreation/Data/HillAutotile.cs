namespace Fableland.MapCreation.Data;

using System;

/// <summary>
/// GDD §2.5 (second disclosed autotile exception, v0.6.13, generalized v0.6.15) — pure C#,
/// editor-authoring-time classifier + atlas-cell lookup for any layered ground-hill material
/// (sand, grass, rock, ...) documented in `Docs/Art/BeachTileSet.md`'s "Layered ground-hill
/// autotile — base template" section (Sand hill is the first worked instantiation there).
/// This class itself is material-agnostic — it only ever asks a caller-supplied delegate "is
/// the neighbor at this offset the same autotile group," so a new material needs zero changes
/// here, just its own 13-tile atlas and `TileRegistry` entry. Distinct from `AutotileAtlas`'s
/// 2-state flat-ground lookup: this models a side-view vertical cross-section (4 depth layers
/// x 4 horizontal positions), not generic Wang/blob corner autotiling — see that doc section
/// and `KNOWLEDGE.md`'s v0.6.9 caveat for why a second hand-rolled system is disclosed here
/// rather than folded into the first.
///
/// WIRED for `ground.sand` in `TileRegistry`/`GridView`: the def declares
/// `Props["autotileKind"] = "layered_hill"` and points `artSource` at the 4x4 atlas.
/// `GridView.DrawLayerTiles` probes same-group neighbors through `NeighborSharesGroup`, calls
/// <see cref="ClassifyAndGetCell"/>, and draws the selected atlas source region. Future hill
/// materials opt in through the same two data fields without another renderer branch.
/// </summary>
public static class HillAutotile
{
    /// <summary>Atlas columns, left to right. Only Layer1 has a Peak tile — see
    /// <see cref="ClassifyPosition"/>.</summary>
    public enum HillPosition { Left = 0, Mid = 1, Right = 2, Peak = 3 }

    /// <summary>Atlas rows, top to bottom (depth from the exposed surface).
    /// Layer1 = surface cap · Layer2 = directly below Layer1 · Layer4 = base (bottom exposed
    /// to air) · Layer3 = everything else.</summary>
    public enum HillLayer { Layer1 = 0, Layer2 = 1, Layer3 = 2, Layer4 = 3 }

    public const int Cols = 4;
    public const int Rows = 4;

    /// <summary><c>Props["autotileKind"]</c> for the original hand-generated 4x4 sheet
    /// (beach sand): one [Left, Mid, Right, Peak] group, Peak only on Layer 1.</summary>
    public const string KindSheet = "layered_hill";

    /// <summary><c>Props["autotileKind"]</c> for a code-painted strip atlas
    /// (`Tools/realm_art/`): <c>Props["autotileVariants"]</c> groups of
    /// [Left, Mid, Right, Peak] side by side, a Peak on every layer. See
    /// <see cref="ClassifyStrip"/>.</summary>
    public const string KindStrip = "layered_hill_strip";

    /// <summary>Columns per variant group in a strip atlas.</summary>
    public const int StripGroupCols = 4;

    /// <summary>Safety cap on the up/down neighbor walk in <see cref="Classify"/> — the map
    /// grid is finite (`MapJson` clamps `GridH` to 256), so a well-behaved probe delegate
    /// always terminates well under this; it only guards against a misbehaving caller.</summary>
    private const int MaxDepthScan = 512;

    /// <summary>Layer classification (GDD §2.5 / BeachTileSet.md): top-touches-air wins first
    /// (Layer1), then bottom-touches-air (Layer4) — the only case where both could apply is a
    /// 1-cell-thick floating ledge, and Layer1 is the deliberate tie-break (it's what the
    /// player actually stands on/sees). Otherwise Layer2 is exactly one cell below a Layer1
    /// cell, and anything deeper is Layer3.</summary>
    public static HillLayer ClassifyLayer(int depthFromTop, int depthFromBottom)
    {
        if (depthFromTop == 0) return HillLayer.Layer1;
        if (depthFromBottom == 0) return HillLayer.Layer4;
        if (depthFromTop == 1) return HillLayer.Layer2;
        return HillLayer.Layer3;
    }

    /// <summary>Position classification. `allowPeak` should be true only when `layer ==
    /// Layer1` — every other layer has no dedicated both-sides-open art, so an isolated
    /// 1-wide column at Layer2-4 falls back to Mid (a flat, full-block tile with no baked
    /// silhouette, which reads fine as a narrow pillar's core — see BeachTileSet.md).</summary>
    public static HillPosition ClassifyPosition(bool leftIsAir, bool rightIsAir, bool allowPeak)
    {
        if (allowPeak && leftIsAir && rightIsAir) return HillPosition.Peak;
        if (leftIsAir && rightIsAir) return HillPosition.Mid;
        if (leftIsAir) return HillPosition.Left;
        if (rightIsAir) return HillPosition.Right;
        return HillPosition.Mid;
    }

    /// <summary>Atlas cell for a classified (layer, position) pair. Always succeeds — a
    /// `Peak` request for a non-Layer1 row (shouldn't happen if `ClassifyPosition` was called
    /// with the right `allowPeak`, but defended anyway) falls back to that row's Mid column,
    /// matching the blank/unused cells `compose_hill_atlas.py` leaves in the atlas.</summary>
    public static void TryGetCell(HillLayer layer, HillPosition position, out int row, out int col)
    {
        row = (int)layer;
        col = position == HillPosition.Peak && layer != HillLayer.Layer1
            ? (int)HillPosition.Mid
            : (int)position;
    }

    /// <summary>Classifies a single cell purely from same-group neighbor probes, then looks
    /// up its atlas cell in one call. `sameGroupAt(dx, dy)` must answer "is the cell at this
    /// offset from the one being classified part of the same autotile group" — out-of-bounds/
    /// empty/different-group should all return false, the same convention
    /// `GridView.NeighborSharesGroup` already uses for the 2-state system.</summary>
    public static void Classify(Func<int, int, bool> sameGroupAt, out HillLayer layer, out HillPosition position) =>
        Classify(sameGroupAt, peakOnEveryLayer: false, out layer, out position);

    /// <summary>As <see cref="Classify(Func{int,int,bool}, out HillLayer, out HillPosition)"/>,
    /// but when <paramref name="peakOnEveryLayer"/> is true a cell with open air on both
    /// sides is a Peak at any depth, not only at the surface — strip atlases paint a
    /// both-sides-exposed tile for every layer, so a one-cell-wide pillar keeps its edges.</summary>
    public static void Classify(Func<int, int, bool> sameGroupAt, bool peakOnEveryLayer,
        out HillLayer layer, out HillPosition position)
    {
        if (sameGroupAt == null) throw new ArgumentNullException(nameof(sameGroupAt));

        int depthFromTop = 0;
        while (depthFromTop < MaxDepthScan && sameGroupAt(0, -(depthFromTop + 1)))
            depthFromTop++;

        int depthFromBottom = 0;
        while (depthFromBottom < MaxDepthScan && sameGroupAt(0, depthFromBottom + 1))
            depthFromBottom++;

        layer = ClassifyLayer(depthFromTop, depthFromBottom);

        bool leftIsAir = !sameGroupAt(-1, 0);
        bool rightIsAir = !sameGroupAt(1, 0);
        position = ClassifyPosition(leftIsAir, rightIsAir, allowPeak: peakOnEveryLayer || layer == HillLayer.Layer1);
    }

    /// <summary>
    /// Classify + cell lookup for a strip atlas (<see cref="KindStrip"/>). The sheet is
    /// 4 rows (Layer 1-4) by <c>variants x 4</c> columns; a cell at map column
    /// <paramref name="cellX"/> draws from group <c>cellX mod variants</c>. Because the
    /// painter cuts every group from one continuous material strip <c>variants</c> cells
    /// wide, neighbouring cells never repeat the same paint and every seam matches by
    /// construction — which a single repeated Mid tile cannot do for a textured material.
    /// </summary>
    public static void ClassifyStrip(Func<int, int, bool> sameGroupAt, int cellX, int variants,
        out int row, out int col, out int cols)
    {
        Classify(sameGroupAt, peakOnEveryLayer: true, out var layer, out var position);
        int groups = Math.Max(1, variants);
        int group = ((cellX % groups) + groups) % groups;
        row = (int)layer;
        col = group * StripGroupCols + (int)position;
        cols = groups * StripGroupCols;
    }

    /// <summary>Classify + atlas-cell lookup in one call — the convenience form a renderer
    /// actually wants (mirrors `AutotileAtlas.TryGetCell`'s call shape).</summary>
    public static void ClassifyAndGetCell(Func<int, int, bool> sameGroupAt, out int row, out int col)
    {
        Classify(sameGroupAt, out var layer, out var position);
        TryGetCell(layer, position, out row, out col);
    }

    /// <summary>Structural guard (same spirit as `MapJson.RoundTripSelfTest` /
    /// `TileManifestLoader.SelfTest`): builds a few synthetic columns/rows as an in-memory
    /// occupied-cell set and checks classification against the profiles worked out by hand in
    /// the design discussion (BeachTileSet.md). Returns an empty list on success.</summary>
    public static System.Collections.Generic.List<string> SelfTest()
    {
        var failures = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("mismatch: " + what); }

        // A single 4-tall, 3-wide mound: full L1/L2/L3/L4 stack, with Left/Mid/Right positions.
        // Occupied set is (x, y) with y increasing downward, matching the map grid convention.
        var occupied = new System.Collections.Generic.HashSet<(int, int)>();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 3; x++)
                occupied.Add((x, y));

        Func<int, int, Func<int, int, bool>> probeAt = (cx, cy) => (dx, dy) => occupied.Contains((cx + dx, cy + dy));

        // Top-left corner of the mound: Layer1, Left.
        Classify(probeAt(0, 0), out var l, out var p);
        Check(l == HillLayer.Layer1, "4-tall mound (0,0) layer (expected Layer1)");
        Check(p == HillPosition.Left, "4-tall mound (0,0) position (expected Left)");

        // Top-middle: Layer1, Mid.
        Classify(probeAt(1, 0), out l, out p);
        Check(l == HillLayer.Layer1, "4-tall mound (1,0) layer (expected Layer1)");
        Check(p == HillPosition.Mid, "4-tall mound (1,0) position (expected Mid)");

        // Second row middle: Layer2, Mid.
        Classify(probeAt(1, 1), out l, out p);
        Check(l == HillLayer.Layer2, "4-tall mound (1,1) layer (expected Layer2)");

        // Third row middle: Layer3, Mid (only reachable at 4+ tall).
        Classify(probeAt(1, 2), out l, out p);
        Check(l == HillLayer.Layer3, "4-tall mound (1,2) layer (expected Layer3)");

        // Bottom-right: Layer4, Right.
        Classify(probeAt(2, 3), out l, out p);
        Check(l == HillLayer.Layer4, "4-tall mound (2,3) layer (expected Layer4)");
        Check(p == HillPosition.Right, "4-tall mound (2,3) position (expected Right)");

        // A 2-tall column: top cell is Layer1 (not Layer2, despite having something below),
        // bottom cell is Layer4 (bottom touches air) — no Layer2/3 exist in a 2-tall stack.
        var thin = new System.Collections.Generic.HashSet<(int, int)> { (0, 0), (0, 1) };
        Func<int, int, bool> thinProbe(int cx, int cy) => (dx, dy) => thin.Contains((cx + dx, cy + dy));
        Classify(thinProbe(0, 0), out l, out _);
        Check(l == HillLayer.Layer1, "2-tall column top (expected Layer1)");
        Classify(thinProbe(0, 1), out l, out _);
        Check(l == HillLayer.Layer4, "2-tall column bottom (expected Layer4, not Layer2)");

        // An isolated single-height, single-width cell: air on all 4 sides -> Layer1 (top
        // rule fires before bottom rule) + Peak (both sides air, Layer1 allows Peak).
        var island = new System.Collections.Generic.HashSet<(int, int)> { (0, 0) };
        Classify((dx, dy) => island.Contains((dx, dy)), out l, out p);
        Check(l == HillLayer.Layer1, "1x1 isolated cell layer (expected Layer1, top-rule precedence)");
        Check(p == HillPosition.Peak, "1x1 isolated cell position (expected Peak)");

        // An isolated 1-wide, 3-tall spike: top is Layer1+Peak; the two cells below are
        // non-Layer1 with both sides open, so they must fall back to Mid (no Peak defined
        // below Layer1).
        var spike = new System.Collections.Generic.HashSet<(int, int)> { (0, 0), (0, 1), (0, 2) };
        Func<int, int, bool> spikeProbe(int cx, int cy) => (dx, dy) => spike.Contains((cx + dx, cy + dy));
        Classify(spikeProbe(0, 0), out l, out p);
        Check(l == HillLayer.Layer1 && p == HillPosition.Peak, "3-tall spike top (expected Layer1/Peak)");
        Classify(spikeProbe(0, 1), out l, out p);
        Check(p == HillPosition.Mid, "3-tall spike middle position (expected Mid fallback, not Peak)");
        Classify(spikeProbe(0, 2), out l, out p);
        Check(l == HillLayer.Layer4, "3-tall spike bottom layer (expected Layer4)");
        Check(p == HillPosition.Mid, "3-tall spike bottom position (expected Mid fallback, not Peak)");

        // Atlas-cell lookup sanity: Layer1/Peak -> (0,3); Layer2/Peak (shouldn't be requested,
        // but defended) -> falls back to (1,1), the Layer2/Mid cell.
        TryGetCell(HillLayer.Layer1, HillPosition.Peak, out int row, out int col);
        Check(row == 0 && col == 3, "TryGetCell Layer1/Peak (expected row0,col3)");
        TryGetCell(HillLayer.Layer2, HillPosition.Peak, out row, out col);
        Check(row == 1 && col == 1, "TryGetCell Layer2/Peak fallback (expected row1,col1 = Layer2/Mid)");

        // Strip atlases: the group follows the map column, and a pillar's body is a Peak.
        ClassifyStrip(probeAt(1, 1), cellX: 1, variants: 4, out row, out col, out int cols);
        Check(row == 1 && col == 1 * StripGroupCols + (int)HillPosition.Mid && cols == 16,
            "ClassifyStrip 4-tall mound (1,1) at x=1 (expected row1, group1/Mid, 16 cols)");
        ClassifyStrip(probeAt(2, 3), cellX: 6, variants: 4, out row, out col, out cols);
        Check(row == 3 && col == 2 * StripGroupCols + (int)HillPosition.Right,
            "ClassifyStrip 4-tall mound (2,3) at x=6 (expected row3, group2/Right)");
        ClassifyStrip(spikeProbe(0, 1), cellX: 0, variants: 4, out row, out col, out cols);
        Check(col == (int)HillPosition.Peak, "ClassifyStrip 3-tall spike middle (expected Peak, strips paint one per layer)");
        ClassifyStrip(probeAt(0, 0), cellX: -3, variants: 4, out row, out col, out cols);
        Check(col == 1 * StripGroupCols + (int)HillPosition.Left, "ClassifyStrip negative column wraps to a valid group");

        return failures;
    }
}

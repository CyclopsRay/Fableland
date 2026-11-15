namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;

/// <summary>
/// Which part of a map a browser card pictures (MapCreation.gdd §7.8). Pure C# — no Godot;
/// <c>MapThumbnailRenderer</c> turns the answer into a camera.
///
/// A card's picture is small and its upper part sits behind the map's name, so "show the
/// whole grid" pictures a side-view arena badly: a sparse beach map is mostly sky, and its
/// floor, towers and props shrink to a line along the bottom edge. The frame is chosen
/// from what is actually built instead:
///
///   * vertically, around the BAND — the shortest run of rows that holds most of the
///     battlefield's structure (a few clouds high in the sky do not stretch it) — scaled so
///     the band fills the part of the picture the title leaves clear, and standing on the
///     picture's bottom edge like the ground it is;
///   * horizontally, over the stretch of columns with the most in it, preferring the
///     middle of the map, where a wide arena keeps its landmark;
///   * never past the map's edge on any side, and never so close that less than
///     <see cref="MinWidthShare"/> of a map's width is in view.
///
/// A dense map therefore still shows edge to edge; a sparse one is shown at the size its
/// content deserves.
/// </summary>
public static class MapThumbnailFraming
{
    /// <summary>A rectangle in battlefield cells (fractions allowed).</summary>
    public readonly struct Frame
    {
        public Frame(float left, float top, float width, float height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public float Left { get; }
        public float Top { get; }
        public float Width { get; }
        public float Height { get; }
        public float Right => Left + Width;
        public float Bottom => Top + Height;
    }

    /// <summary>Share of the picture's height, measured up from its bottom edge, that a card
    /// leaves clear of text. The band is scaled to fill it.</summary>
    public const float ClearShare = 0.56f;

    /// <summary>Share of the structure the band must hold.</summary>
    private const float BandShare = 0.85f;

    /// <summary>A picture never shows less than this share of the map's width.</summary>
    private const float MinWidthShare = 0.45f;

    /// <summary>A column window is "as full as the fullest" from this share of its content.</summary>
    private const float NearBest = 0.95f;

    /// <param name="aspect">The picture's width divided by its height.</param>
    public static Frame Compute(MapDocument doc, float aspect)
    {
        if (aspect <= 0f) aspect = 1f;
        MapLayerData field = MapDocumentQueries.Battlefield(doc);
        int mapW = Math.Max(1, field?.GridW ?? MapLayerData.DefaultGridWidth);
        int mapH = Math.Max(1, field?.GridH ?? MapLayerData.DefaultGridHeight);

        var rows = new float[mapH];
        var cols = new float[mapW];
        bool structureOnly = true;
        float total = Tally(field, rows, cols, structureOnly, 0f, mapH);
        if (total <= 0f)
        {
            structureOnly = false;      // a map of scenery only: frame the scenery
            total = Tally(field, rows, cols, structureOnly, 0f, mapH);
        }

        // The most rows a picture may show: the whole height, or as many as the map's width
        // allows at this aspect — whichever is fewer, so no edge is ever crossed.
        float maxRows = Math.Min(mapH, mapW / aspect);

        if (total <= 0f)
        {
            // Nothing built: the bottom of the grid, where the floor will go.
            float emptyCols = maxRows * aspect;
            return new Frame((mapW - emptyCols) * 0.5f, mapH - maxRows, emptyCols, maxRows);
        }

        (int bandTop, int bandBottom) = Band(rows, total * BandShare);
        float wanted = Math.Max((bandBottom - bandTop) / ClearShare, MinWidthShare * mapW / aspect);
        float shownRows = Math.Min(wanted, maxRows);
        float shownCols = shownRows * aspect;

        float top = Math.Clamp(bandBottom - shownRows, 0f, mapH - shownRows);

        // Columns are weighed over the rows that will be in view, so something far above the
        // frame cannot pull the window sideways.
        Tally(field, rows, cols, structureOnly, top, top + shownRows);
        float left = BestLeft(cols, shownCols);
        return new Frame(left, top, shownCols, shownRows);
    }

    /// <summary>Adds each tile's footprint to the per-row and per-column cell counts and
    /// returns the total, looking only at rows [<paramref name="rowTop"/>,
    /// <paramref name="rowBottom"/>). Structure = what a fighter stands on, enters or is hurt
    /// by; markers and rule tiles are invisible in play and never count.</summary>
    private static float Tally(MapLayerData field, float[] rows, float[] cols, bool structureOnly,
        float rowTop, float rowBottom)
    {
        Array.Clear(rows);
        Array.Clear(cols);
        float total = 0f;
        if (field?.Tiles == null) return 0f;

        foreach (PlacedTile tile in field.Tiles)
        {
            if (tile == null || !TileRegistry.TryGet(tile.DefId, out TileDef def)) continue;
            switch (def.Category)
            {
                case TileCategory.Ground:
                case TileCategory.Platform:
                case TileCategory.SoftVolume:
                case TileCategory.Hazard:
                    break;
                case TileCategory.Decoration:
                case TileCategory.Misc:
                    if (structureOnly) continue;
                    break;
                default:
                    continue;
            }

            int x0 = Math.Max(0, tile.X), x1 = Math.Min(cols.Length, tile.X + Math.Max(1, def.FootprintW));
            int y0 = Math.Max(0, tile.Y), y1 = Math.Min(rows.Length, tile.Y + Math.Max(1, def.FootprintH));
            if (x1 <= x0 || y1 <= y0) continue;

            float inView = 0f;      // rows of this tile inside the range, counting part rows
            for (int y = y0; y < y1; y++)
            {
                float share = Math.Min(y + 1f, rowBottom) - Math.Max(y, rowTop);
                if (share <= 0f) continue;
                rows[y] += (x1 - x0) * share;
                inView += share;
            }
            if (inView <= 0f) continue;
            for (int x = x0; x < x1; x++) cols[x] += inView;
            total += (x1 - x0) * inView;
        }
        return total;
    }

    /// <summary>The shortest run of rows [top, bottom) holding at least <paramref name="need"/>
    /// cells; of equally short runs, the lowest — an arena stands on its floor.</summary>
    private static (int Top, int Bottom) Band(float[] rows, float need)
    {
        int bestTop = 0, bestBottom = rows.Length;
        for (int top = 0; top < rows.Length; top++)
        {
            float sum = 0f;
            for (int bottom = top; bottom < rows.Length; bottom++)
            {
                sum += rows[bottom];
                if (sum < need) continue;
                if (bottom + 1 - top <= bestBottom - bestTop)
                {
                    bestTop = top;
                    bestBottom = bottom + 1;
                }
                break;
            }
        }
        return (bestTop, bestBottom);
    }

    /// <summary>Left edge of the <paramref name="width"/>-column window to show: the fullest
    /// window, then the middle of the run of neighbouring windows that are nearly as full.
    /// An even floor is therefore pictured at the middle of the map, and a landmark in the
    /// middle of the picture rather than against its edge.</summary>
    private static float BestLeft(float[] cols, float width)
    {
        float slack = cols.Length - width;
        if (slack <= 0.001f) return 0f;

        // prefix[i] = cells in columns [0, i); a window's content is read off it, counting a
        // partly covered column by the share covered.
        var prefix = new float[cols.Length + 1];
        for (int i = 0; i < cols.Length; i++) prefix[i + 1] = prefix[i] + cols[i];
        float At(float x)
        {
            x = Math.Clamp(x, 0f, cols.Length);
            int i = Math.Min((int)x, cols.Length - 1);
            return prefix[i] + (x - i) * cols[i];
        }

        var lefts = new List<float>();
        for (float x = 0f; x < slack; x += 1f) lefts.Add(x);
        lefts.Add(slack);

        var content = new float[lefts.Count];
        float best = 0f;
        for (int i = 0; i < lefts.Count; i++)
        {
            content[i] = At(lefts[i] + width) - At(lefts[i]);
            best = Math.Max(best, content[i]);
        }

        // of the fullest windows, the one nearest the middle of the map
        float centred = slack * 0.5f, nearest = float.MaxValue;
        int fullest = 0;
        for (int i = 0; i < lefts.Count; i++)
        {
            if (content[i] < best - 0.001f) continue;
            float off = Math.Abs(lefts[i] - centred);
            if (off >= nearest) continue;
            nearest = off;
            fullest = i;
        }

        int from = fullest, to = fullest;
        while (from > 0 && content[from - 1] >= best * NearBest) from--;
        while (to < lefts.Count - 1 && content[to + 1] >= best * NearBest) to++;
        return (lefts[from] + lefts[to]) * 0.5f;
    }

    /// <summary>Structural guard over the shapes of map the browser has to picture.</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("MapThumbnailFraming mismatch: " + what); }
        bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;
        const float aspect = 1.35f;

        MapDocument Make(int w, int h)
        {
            var doc = MapDocument.CreateNew("framing");
            MapLayerData field = MapDocumentQueries.Battlefield(doc);
            field.GridW = w;
            field.GridH = h;
            return doc;
        }
        void Fill(MapDocument doc, int x0, int y0, int x1, int y1)
        {
            MapLayerData field = MapDocumentQueries.Battlefield(doc);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    field.Tiles.Add(new PlacedTile { DefId = "ground.stone", X = x, Y = y });
        }
        void InsideMap(Frame f, int w, int h, string what)
        {
            Check(f.Left >= -0.01f && f.Top >= -0.01f && f.Right <= w + 0.01f && f.Bottom <= h + 0.01f,
                what + ": frame leaves the map");
            Check(Near(f.Width / f.Height, aspect), what + ": frame is not the picture's shape");
        }

        // nothing built: the bottom of the grid
        MapDocument empty = Make(64, 36);
        Frame f = Compute(empty, aspect);
        InsideMap(f, 64, 36, "empty");
        Check(Near(f.Bottom, 36f), "empty: sits on the bottom edge");

        // a sparse beach: floor along the bottom, one tower, a cloud high up
        MapDocument sparse = Make(60, 40);
        Fill(sparse, 0, 38, 60, 40);
        Fill(sparse, 6, 29, 9, 38);
        Fill(sparse, 10, 4, 13, 6);
        f = Compute(sparse, aspect);
        InsideMap(f, 60, 40, "sparse");
        Check(Near(f.Bottom, 40f), "sparse: stands on its floor");
        Check(f.Top > 10f, "sparse: zooms in below the lone cloud instead of showing the whole sky");
        Check(f.Width >= 60f * MinWidthShare - 0.01f, "sparse: still shows a fair share of the width");

        // dense and tall: all the width, down to the floor
        MapDocument tall = Make(44, 40);
        Fill(tall, 0, 2, 44, 40);
        f = Compute(tall, aspect);
        InsideMap(f, 44, 40, "tall");
        Check(Near(f.Width, 44f) && Near(f.Bottom, 40f), "tall: full width, floor at the bottom");

        // dense and very wide: all the height, part of the width
        MapDocument wide = Make(96, 20);
        Fill(wide, 0, 14, 96, 20);
        f = Compute(wide, aspect);
        InsideMap(f, 96, 20, "wide");
        Check(Near(f.Height, 20f), "wide: full height");
        Check(Near(f.Left, (96f - f.Width) * 0.5f), "wide: an even floor is pictured at the middle");

        // a landmark off to the right pulls the window over it
        Fill(wide, 80, 2, 90, 14);
        f = Compute(wide, aspect);
        InsideMap(f, 96, 20, "landmark");
        Check(f.Left <= 80f && f.Right >= 90f, "landmark: the window covers the landmark");
        return failures;
    }
}

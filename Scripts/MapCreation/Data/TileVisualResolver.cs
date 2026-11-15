namespace Fableland.MapCreation.Data;

using System;
using System.Globalization;

/// <summary>
/// What a placed tile draws with: a texture path and, for atlas-backed terrain, the one
/// cell of that atlas to use.
/// </summary>
public readonly struct TileVisual
{
    /// <summary>`res://` path, or null when the tile kind has no art yet (draw its
    /// <see cref="TileDef.EditorColor"/> quad).</summary>
    public readonly string TexturePath;

    /// <summary>True when only one cell of <see cref="TexturePath"/> is drawn.</summary>
    public readonly bool HasRegion;

    public readonly int Col;
    public readonly int Row;
    public readonly int Cols;
    public readonly int Rows;

    public TileVisual(string texturePath)
    {
        TexturePath = texturePath;
        HasRegion = false;
        Col = Row = 0;
        Cols = Rows = 1;
    }

    public TileVisual(string texturePath, int col, int row, int cols, int rows)
    {
        TexturePath = texturePath;
        HasRegion = true;
        Col = col;
        Row = row;
        Cols = cols;
        Rows = rows;
    }
}

/// <summary>
/// GDD §2.5 — the ONE place that decides which art a placed tile shows. The map editor's
/// canvas, the editor Playtest and the real combat arena all call this, so the three can
/// only differ where this class says they do. Pure C# — callers load the texture and turn
/// (col, row, cols, rows) into pixels.
///
/// Dispatch is by tile data (<c>Props["autotileKind"]</c>), never by tile id (T00 rule 1):
///
///   kind                                    | editor canvas | play (playtest + arena)
///   ----------------------------------------|---------------|------------------------
///   <see cref="HillAutotile.KindStrip"/>    | atlas cell    | atlas cell
///   <see cref="HillAutotile.KindSheet"/>    | atlas cell    | plain SpriteSlot
///   (absent: two-state north-edge lookup)   | atlas cell    | plain SpriteSlot
///
/// Strip atlases are painted by code against this exact classifier, so they are safe to
/// show in play. The two older beach lookups stay authoring-time approximations, exactly as
/// GDD §2.5 already recorded: their sheets were hand-assembled and the play view of the
/// beach keeps its seamless fill until that art is re-cut and reviewed.
/// A kind this build does not know degrades to the def's plain <see cref="TileDef.SpriteSlot"/>.
/// </summary>
public static class TileVisualResolver
{
    /// <param name="cellX">The placed tile's map column (selects a strip atlas' variant group).</param>
    /// <param name="sameGroupAt">(dx, dy) → does the cell at that offset hold a tile of the same
    /// <see cref="TileDef.AutotileGroup"/>? Out of bounds / empty / other group = false.</param>
    /// <param name="authoringView">True for the editor canvas, false for anything played.</param>
    public static TileVisual Resolve(TileDef def, int cellX, Func<int, int, bool> sameGroupAt, bool authoringView)
    {
        if (def == null) return new TileVisual(null);

        if (sameGroupAt != null && !string.IsNullOrEmpty(def.AutotileGroup) && def.Props != null &&
            def.Props.TryGetValue(TileProps.ArtSource, out string atlas) && !string.IsNullOrWhiteSpace(atlas))
        {
            def.Props.TryGetValue(TileProps.AutotileKind, out string kind);

            if (kind == HillAutotile.KindStrip)
            {
                HillAutotile.ClassifyStrip(sameGroupAt, cellX, VariantsOf(def), out int row, out int col, out int cols);
                return new TileVisual(atlas, col, row, cols, HillAutotile.Rows);
            }

            if (authoringView && kind == HillAutotile.KindSheet)
            {
                HillAutotile.ClassifyAndGetCell(sameGroupAt, out int row, out int col);
                return new TileVisual(atlas, col, row, HillAutotile.Cols, HillAutotile.Rows);
            }

            if (authoringView && string.IsNullOrEmpty(kind) &&
                AutotileAtlas.TryGetCell(def.AutotileGroup, sameGroupAt(0, -1), out int legacyRow, out int legacyCol))
                return new TileVisual(atlas, legacyCol, legacyRow, AutotileAtlas.Cols, AutotileAtlas.Rows);
        }

        return new TileVisual(string.IsNullOrWhiteSpace(def.SpriteSlot) ? null : def.SpriteSlot);
    }

    /// <summary>Variant-group count of a strip atlas (<c>Props["autotileVariants"]</c>, default 1).</summary>
    public static int VariantsOf(TileDef def)
    {
        if (def?.Props != null && def.Props.TryGetValue(TileProps.AutotileVariants, out string raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int variants) && variants >= 1)
            return variants;
        return 1;
    }

    /// <summary>Structural guard: every dispatch branch, on synthetic defs.</summary>
    public static System.Collections.Generic.List<string> SelfTest()
    {
        var failures = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("TileVisualResolver mismatch: " + what); }

        Func<int, int, bool> alone = (dx, dy) => false;
        Func<int, int, bool> buried = (dx, dy) => true;

        var plain = new TileDef { Id = "t.plain", SpriteSlot = "res://plain.png" };
        var v = Resolve(plain, 0, alone, authoringView: false);
        Check(!v.HasRegion && v.TexturePath == "res://plain.png", "plain def uses SpriteSlot");

        Check(Resolve(new TileDef { Id = "t.none" }, 0, alone, false).TexturePath == null, "art-less def has no texture");
        Check(Resolve(null, 0, alone, false).TexturePath == null, "null def has no texture");

        var strip = new TileDef
        {
            Id = "t.strip", SpriteSlot = "res://swatch.png", AutotileGroup = "terrain.t",
            Props = new System.Collections.Generic.Dictionary<string, string>
            {
                [TileProps.ArtSource] = "res://atlas.png", [TileProps.AutotileKind] = HillAutotile.KindStrip,
                [TileProps.AutotileVariants] = "4",
            },
        };
        foreach (bool authoring in new[] { true, false })
        {
            v = Resolve(strip, 5, alone, authoring);    // isolated cell: Layer 1, Peak, group 5 mod 4 = 1
            Check(v.HasRegion && v.TexturePath == "res://atlas.png" && v.Row == 0 && v.Col == 1 * 4 + 3 &&
                  v.Cols == 16 && v.Rows == 4, "strip atlas, isolated cell at x=5 (authoring=" + authoring + ")");
            v = Resolve(strip, 2, buried, authoring);   // fully surrounded: Layer 3, Mid, group 2
            Check(v.Row == 2 && v.Col == 2 * 4 + 1, "strip atlas, buried cell at x=2 (authoring=" + authoring + ")");
        }

        var sheet = new TileDef
        {
            Id = "t.sheet", SpriteSlot = "res://swatch.png", AutotileGroup = "terrain.t",
            Props = new System.Collections.Generic.Dictionary<string, string>
            {
                [TileProps.ArtSource] = "res://sheet.png", [TileProps.AutotileKind] = HillAutotile.KindSheet,
            },
        };
        v = Resolve(sheet, 9, alone, authoringView: true);
        Check(v.HasRegion && v.Row == 0 && v.Col == 3 && v.Cols == 4 && v.Rows == 4, "4x4 sheet, isolated cell, editor");
        v = Resolve(sheet, 9, alone, authoringView: false);
        Check(!v.HasRegion && v.TexturePath == "res://swatch.png", "4x4 sheet stays authoring-only: play uses SpriteSlot");

        var unknownKind = new TileDef
        {
            Id = "t.future", SpriteSlot = "res://swatch.png", AutotileGroup = "terrain.t",
            Props = new System.Collections.Generic.Dictionary<string, string>
            {
                [TileProps.ArtSource] = "res://atlas.png", [TileProps.AutotileKind] = "a_kind_from_the_future",
            },
        };
        v = Resolve(unknownKind, 0, alone, authoringView: true);
        Check(!v.HasRegion && v.TexturePath == "res://swatch.png", "unknown kind degrades to SpriteSlot");

        Check(VariantsOf(strip) == 4 && VariantsOf(sheet) == 1, "VariantsOf");
        return failures;
    }
}

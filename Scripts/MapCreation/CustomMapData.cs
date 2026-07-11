using System.Collections.Generic;
using Godot;

namespace Fableland.MapCreation;

/// <summary>What kind of tile is placed in a cell.</summary>
public enum TileKind
{
    Empty,
    Ground,
    Platform,
    SoftVolume,
    EnemySpawn,
    Respawn,
    LevelGoal,
    Character,
}

/// <summary>Sub-variant labels within each TileKind.</summary>
public static class TileLabels
{
    public static readonly Dictionary<TileKind, string[]> Variants = new()
    {
        { TileKind.Ground,      new[] { "sand", "grass" } },
        { TileKind.Platform,    new[] { "rooftop" } },
        { TileKind.SoftVolume,  new[] { "tree", "cloud" } },
        { TileKind.EnemySpawn,  new[] { "crab", "seagull" } },
        { TileKind.Respawn,     new[] { "respawn" } },
        { TileKind.LevelGoal,   new[] { "wonder_core", "condensed_core", "fortress" } },
        { TileKind.Character,   new[] { "player_start" } },
    };

    /// <summary>Human-readable category names for the palette bar.</summary>
    public static readonly Dictionary<TileKind, string> CategoryNames = new()
    {
        { TileKind.Ground,      "Ground" },
        { TileKind.Platform,    "Platform" },
        { TileKind.SoftVolume,  "SoftVolume" },
        { TileKind.EnemySpawn,  "Enemy Spawn" },
        { TileKind.Respawn,     "Respawn" },
        { TileKind.LevelGoal,   "Level Goal" },
        { TileKind.Character,   "Character" },
    };

    /// <summary>Colors for rendering each tile kind on the editor grid.</summary>
    public static readonly Dictionary<TileKind, Color> Colors = new()
    {
        { TileKind.Ground,      new Color(0.55f, 0.40f, 0.20f) },  // brown
        { TileKind.Platform,    new Color(0.35f, 0.35f, 0.45f) },  // dark grey-blue
        { TileKind.SoftVolume,  new Color(0.25f, 0.55f, 0.30f) },  // green
        { TileKind.EnemySpawn,  new Color(0.75f, 0.20f, 0.15f) },  // red
        { TileKind.Respawn,     new Color(0.20f, 0.60f, 0.85f) },  // blue
        { TileKind.LevelGoal,   new Color(0.90f, 0.75f, 0.15f) },  // gold
        { TileKind.Character,   new Color(0.15f, 0.70f, 0.70f) },  // teal
    };

    public static Color GetVariantColor(TileKind kind, int variantIndex)
    {
        var base_ = Colors.TryGetValue(kind, out var c) ? c : new Color(1, 1, 1);
        // Slightly vary brightness per variant so they're distinguishable
        float offset = variantIndex * 0.12f;
        return new Color(
            Mathf.Clamp(base_.R + offset, 0, 1),
            Mathf.Clamp(base_.G + offset, 0, 1),
            Mathf.Clamp(base_.B + offset, 0, 1),
            1f
        );
    }
}

/// <summary>A single cell in the map grid.</summary>
public sealed class MapCell
{
    public TileKind Kind = TileKind.Empty;
    public int Variant; // index into TileLabels.Variants[Kind]
}

/// <summary>Metadata for a saved custom map.</summary>
public sealed class MapMeta
{
    public string Name;
    public string World = "Starland";
    public string FileName;  // sanitised name used as file key
    public string SavedAt;   // ISO 8601 timestamp
    public int GridWidth;
    public int GridHeight;
}

/// <summary>Complete custom map data, serialised to JSON.</summary>
public sealed class CustomMap
{
    public MapMeta Meta = new();
    public List<List<MapCellData>> Grid = new(); // [y][x] for JSON friendliness

    /// <summary>Grid cell in serialisable form (no Godot types).</summary>
    public sealed class MapCellData
    {
        public string Kind;
        public int Variant;
    }

    // ---- helpers for the editor (in-memory only) ----

    public int Width => Grid.Count > 0 ? Grid[0].Count : 0;
    public int Height => Grid.Count;

    public TileKind GetKind(int x, int y) => CellAt(x, y)?.Kind ?? TileKind.Empty;
    public int GetVariant(int x, int y) => CellAt(x, y)?.Variant ?? 0;

    private MapCell CellAt(int x, int y)
    {
        if (y < 0 || y >= Grid.Count) return null;
        var row = Grid[y];
        if (x < 0 || x >= row.Count) return null;
        // Reconstruct a MapCell from the serialisable data
        var cd = row[x];
        if (cd == null || cd.Kind == "Empty" || string.IsNullOrEmpty(cd.Kind)) return new MapCell { Kind = TileKind.Empty };
        if (!System.Enum.TryParse<TileKind>(cd.Kind, out var kind)) return new MapCell { Kind = TileKind.Empty };
        return new MapCell { Kind = kind, Variant = cd.Variant };
    }

    public void SetCell(int x, int y, TileKind kind, int variant)
    {
        while (Grid.Count <= y) Grid.Add(new List<MapCellData>());
        var row = Grid[y];
        while (row.Count <= x) row.Add(new MapCellData { Kind = "Empty", Variant = 0 });
        row[x] = new MapCellData { Kind = kind.ToString(), Variant = variant };
    }

    public void EraseCell(int x, int y)
    {
        if (y < 0 || y >= Grid.Count) return;
        var row = Grid[y];
        if (x < 0 || x >= row.Count) return;
        row[x] = new MapCellData { Kind = "Empty", Variant = 0 };
    }

    /// <summary>Create a new empty map with the given dimensions.</summary>
    public static CustomMap CreateEmpty(int w, int h, string name)
    {
        var map = new CustomMap
        {
            Meta = new MapMeta
            {
                Name = name,
                GridWidth = w,
                GridHeight = h,
                SavedAt = System.DateTime.UtcNow.ToString("O"),
            },
        };
        for (int y = 0; y < h; y++)
        {
            var row = new List<MapCellData>();
            for (int x = 0; x < w; x++)
                row.Add(new MapCellData { Kind = "Empty", Variant = 0 });
            map.Grid.Add(row);
        }
        return map;
    }
}

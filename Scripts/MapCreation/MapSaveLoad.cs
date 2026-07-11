using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Fableland.MapCreation;

/// <summary>Persists CustomMaps to/from user://maps/ as JSON files.</summary>
public static class MapSaveLoad
{
    private const string MapDir = "user://maps/";
    private const string IndexFile = "_index.json";

    /// <summary>Ensure the maps directory exists.</summary>
    public static void EnsureDir()
    {
        if (!DirAccess.DirExistsAbsolute(MapDir))
            DirAccess.MakeDirAbsolute(MapDir);
    }

    /// <summary>Sanitise a map name into a safe filename.</summary>
    public static string Sanitise(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "untitled" : safe;
    }

    /// <summary>Save a map. Returns the file path used.</summary>
    public static string Save(CustomMap map)
    {
        EnsureDir();
        map.Meta.FileName = Sanitise(map.Meta.Name);
        map.Meta.SavedAt = System.DateTime.UtcNow.ToString("O");

        string json = JsonSerializer.Serialize(map, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        string path = MapDir + map.Meta.FileName + ".json";
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        f.StoreString(json);
        UpdateIndex(map.Meta);
        return path;
    }

    /// <summary>Load a map by filename (without path or extension).</summary>
    public static CustomMap Load(string fileName)
    {
        string path = MapDir + fileName + ".json";
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        string json = f.GetAsText();
        return JsonSerializer.Deserialize<CustomMap>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    /// <summary>Delete a map file and update the index.</summary>
    public static void Delete(string fileName)
    {
        string path = MapDir + fileName + ".json";
        if (FileAccess.FileExists(path))
            DirAccess.RemoveAbsolute(path);
        RemoveFromIndex(fileName);
    }

    /// <summary>List all saved map metadata from the index.</summary>
    public static List<MapMeta> ListMaps()
    {
        EnsureDir();
        string idxPath = MapDir + IndexFile;
        if (!FileAccess.FileExists(idxPath)) return new List<MapMeta>();
        using var f = FileAccess.Open(idxPath, FileAccess.ModeFlags.Read);
        string json = f.GetAsText();
        try
        {
            return JsonSerializer.Deserialize<List<MapMeta>>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }) ?? new List<MapMeta>();
        }
        catch { return new List<MapMeta>(); }
    }

    // ---- index helpers ----

    private static void UpdateIndex(MapMeta meta)
    {
        var list = ListMaps();
        var existing = list.FindIndex(m => m.FileName == meta.FileName);
        if (existing >= 0)
            list[existing] = meta;
        else
            list.Add(meta);
        WriteIndex(list);
    }

    private static void RemoveFromIndex(string fileName)
    {
        var list = ListMaps();
        list.RemoveAll(m => m.FileName == fileName);
        WriteIndex(list);
    }

    private static void WriteIndex(List<MapMeta> list)
    {
        string json = JsonSerializer.Serialize(list, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        using var f = FileAccess.Open(MapDir + IndexFile, FileAccess.ModeFlags.Write);
        f.StoreString(json);
    }
}

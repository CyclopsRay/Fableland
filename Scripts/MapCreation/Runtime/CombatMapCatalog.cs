namespace Fableland.MapCreation.Runtime;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.Run;

/// <summary>
/// Orchestration-side catalogue for authored combat maps. It reads built-in maps first and then
/// user-created maps, filters by the immutable adventure context, and makes a deterministic
/// choice from the remaining entries. Map JSON remains pure data; only this scene-layer bridge
/// knows about Godot paths and the run's RNG.
/// </summary>
public static class CombatMapCatalog
{
    private const string BuiltInDirectory = "res://Maps";
    private const string UserDirectory = "user://maps";

    public static CombatMapSelection Select(string seed, string nodeId, string world, int hardship,
        MissionType mission, string terrain)
    {
        List<CombatMapSelection> candidates = LoadAll();
        string goal = GoalFor(mission);
        string terrainKey = Normalize(terrain);
        candidates.RemoveAll(c => !Matches(c.Document, world, hardship, goal, terrainKey));
        if (candidates.Count == 0) return null;

        candidates.Sort((a, b) => string.CompareOrdinal(a.Document.Id, b.Document.Id));
        var rng = new DetRandom((seed ?? "debug") + ":combat-map:" + (nodeId ?? "debug"));
        return candidates[rng.Range(0, candidates.Count - 1)];
    }

    /// <summary>Load the exact selection stored in AdventureContext. A missing/corrupt map is a
    /// content error, not a run-ending error: callers fall back to the legacy Arena scene.</summary>
    public static MapDocument LoadDocument(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return null;
        MapDocument doc = MapJson.Load(absolutePath, out var warnings);
        foreach (string warning in warnings) GD.PushWarning("[CombatMapCatalog] " + warning);
        return doc;
    }

    public static string GoalFor(MissionType mission) => mission switch
    {
        MissionType.Protect => CombatMapGoals.Protect,
        MissionType.Destroy => CombatMapGoals.Destroy,
        MissionType.Slaughter => CombatMapGoals.Slaughter,
        _ => CombatMapGoals.Claim,
    };

    private static List<CombatMapSelection> LoadAll()
    {
        var loaded = new List<CombatMapSelection>();
        var seenIds = new HashSet<string>();
        LoadDirectory(ProjectSettings.GlobalizePath(BuiltInDirectory), loaded, seenIds);
        LoadDirectory(ProjectSettings.GlobalizePath(UserDirectory), loaded, seenIds);
        return loaded;
    }

    private static void LoadDirectory(string directory, List<CombatMapSelection> loaded, HashSet<string> seenIds)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        string[] paths;
        try { paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly); }
        catch (Exception e)
        {
            GD.PushWarning("[CombatMapCatalog] could not list '" + directory + "': " + e.Message);
            return;
        }

        Array.Sort(paths, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            MapDocument doc = MapJson.Load(path, out var warnings);
            foreach (string warning in warnings) GD.PushWarning("[CombatMapCatalog] " + warning);
            if (doc == null || string.IsNullOrWhiteSpace(doc.Id) || !seenIds.Add(doc.Id)) continue;
            loaded.Add(new CombatMapSelection { AbsolutePath = path, Document = doc });
        }
    }

    private static bool Matches(MapDocument doc, string world, int hardship, string goal, string terrain)
    {
        if (doc == null) return false;
        if (doc.Worlds is { Count: > 0 })
        {
            bool worldMatch = false;
            for (int i = 0; i < doc.Worlds.Count; i++)
            {
                if (WorldMatches(doc.Worlds[i], world)) { worldMatch = true; break; }
            }
            if (!worldMatch) return false;
        }
        if (doc.HardshipLevels is { Count: > 0 } && !doc.HardshipLevels.Contains(hardship)) return false;
        if (doc.Goals is { Count: > 0 } && !doc.Goals.Contains(goal)) return false;
        return Normalize(doc.Terrain) == terrain;
    }

    private static bool WorldMatches(string authored, string nodeWorld)
    {
        string a = Normalize(authored);
        string n = Normalize(nodeWorld);
        if (a == n) return true;
        foreach (WorldDef world in WorldDef.Pool)
        {
            if (Normalize(world.Abbr) == n && Normalize(world.Name) == a) return true;
            if (Normalize(world.Name) == n && Normalize(world.Abbr) == a) return true;
        }
        return false;
    }

    private static string Normalize(string value) => (value ?? "").Trim().ToLowerInvariant();
}

/// <summary>One validated map candidate plus the path used to reload it at arena entry.</summary>
public sealed class CombatMapSelection
{
    public string AbsolutePath;
    public MapDocument Document;
}

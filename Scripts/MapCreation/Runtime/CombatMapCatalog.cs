namespace Fableland.MapCreation.Runtime;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.Run;

/// <summary>
/// Orchestration-side catalogue for authored combat maps. It reads user-created maps before
/// built-ins, so an editor-made file with the same immutable id is a safe local override. It
/// then filters by the immutable adventure context and makes a deterministic choice from the
/// remaining entries. Map JSON remains pure data; only this scene-layer bridge knows about Godot
/// paths and the run's RNG.
///
/// Only maps that can be fought on are candidates (<see cref="MapReadiness"/>, the same test
/// the browser's "Unfinished" badge uses). A draft in <c>user://maps</c> — a new map is tagged
/// "every world, every level" until its author narrows it — must never be dealt to a run.
/// </summary>
public static class CombatMapCatalog
{
    private const string BuiltInDirectory = "res://Maps";
    private const string UserDirectory = "user://maps";

    /// <summary>Whether a run is dealt the player's own maps (<c>user://maps</c>) as well as
    /// the shipped ones. True in the game. A headless check that plays a run turns it off, so
    /// what it is dealt does not depend on what one developer has saved in the editor.</summary>
    public static bool UserMapsEnabled { get; set; } = true;

    public static CombatMapSelection Select(string seed, string nodeId, string world, int hardship,
        MissionType mission, string terrain)
    {
        List<CombatMapSelection> candidates = CandidatesFor(world, hardship, mission, terrain, UserMapsEnabled);
        if (candidates.Count == 0) return null;

        var rng = new DetRandom((seed ?? "debug") + ":combat-map:" + (nodeId ?? "debug"));
        return candidates[rng.Range(0, candidates.Count - 1)];
    }

    /// <summary>
    /// What a node with this mission is dealt from — the rule <see cref="Select"/> draws
    /// with, and the one the content check audits, so the two cannot disagree. A Boss
    /// mission is dealt its world's boss arena (goal <c>boss</c>) when one exists; a world
    /// whose boss has no arena of its own yet fights on its ordinary (<c>claim</c>) maps, as
    /// every boss did before v0.10.11.
    /// </summary>
    public static List<CombatMapSelection> CandidatesFor(string world, int hardship, MissionType mission,
        string terrain, bool includeUserMaps = true)
    {
        List<CombatMapSelection> candidates = Candidates(world, hardship, GoalFor(mission), terrain, includeUserMaps);
        if (candidates.Count == 0 && mission == MissionType.Boss)
            candidates = Candidates(world, hardship, CombatMapGoals.Claim, terrain, includeUserMaps);
        return candidates;
    }

    /// <summary>
    /// Every map a node with these attributes could be dealt, in the stable id order
    /// <see cref="Select"/> draws from. <paramref name="includeUserMaps"/> = false looks at the
    /// shipped maps alone, which is what the headless content check audits: its answer must
    /// not depend on what happens to sit in one developer's <c>user://maps</c>.
    /// </summary>
    public static List<CombatMapSelection> Candidates(string world, int hardship, string goal,
        string terrain, bool includeUserMaps = true)
    {
        return CandidatesIn(includeUserMaps ? ProjectSettings.GlobalizePath(UserDirectory) : null,
            ProjectSettings.GlobalizePath(BuiltInDirectory), world, hardship, goal, terrain);
    }

    /// <summary>The selection rule over two explicit folders (the user's first, so its copies
    /// win). <see cref="SelfTest"/> points it at scratch folders.</summary>
    private static List<CombatMapSelection> CandidatesIn(string userDirectory, string builtInDirectory,
        string world, int hardship, string goal, string terrain)
    {
        var candidates = new List<CombatMapSelection>();
        var seenIds = new HashSet<string>();
        LoadDirectory(userDirectory, false, candidates, seenIds);
        LoadDirectory(builtInDirectory, true, candidates, seenIds);

        string goalKey = Normalize(goal);
        string terrainKey = Normalize(terrain);
        candidates.RemoveAll(c => !Matches(c.Document, world, hardship, goalKey, terrainKey));
        candidates.Sort((a, b) => string.CompareOrdinal(a.Document.Id, b.Document.Id));
        return candidates;
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
        MissionType.Boss => CombatMapGoals.Boss,
        _ => CombatMapGoals.Claim,
    };

    private static void LoadDirectory(string directory, bool builtIn, List<CombatMapSelection> loaded,
        HashSet<string> seenIds)
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
            if (doc == null || string.IsNullOrWhiteSpace(doc.Id) || seenIds.Contains(doc.Id)) continue;

            // An unfinished map is skipped WITHOUT claiming its id, so a half-edited copy of a
            // built-in map falls back to the shipped one instead of hiding it. A user's draft
            // is ordinary and stays quiet; an unfinished shipped map is a content error.
            if (!MapReadiness.IsReady(doc))
            {
                if (builtIn)
                    GD.PushWarning($"[CombatMapCatalog] built-in map '{doc.Name}' is not playable ({string.Join(", ", MapReadiness.Problems(doc))}); skipped.");
                continue;
            }

            seenIds.Add(doc.Id);
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
        // A wildcard terrain lets a deliberately broad combat map serve every altitude band.
        // This is useful for a themed realm set whose level progression is the authored
        // distinction, while preserving exact terrain matching for specialized maps.
        string authoredTerrain = Normalize(doc.Terrain);
        return authoredTerrain == "*" || authoredTerrain == terrain;
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

    /// <summary>
    /// Guards what a run may be dealt, against scratch folders: a draft is never a candidate
    /// (a new map answers to every world and level until its author narrows it, so nothing
    /// but readiness keeps it out), an unfinished copy of a shipped map falls back to the
    /// shipped one, a finished copy replaces it, and "*" terrain serves every altitude band.
    /// </summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add("CombatMapCatalog mismatch: " + what); }

        string root = Path.Combine(Path.GetTempPath(), "fableland-catalog-selftest-" + Guid.NewGuid().ToString("N"));
        string shipped = Path.Combine(root, "shipped");
        string mine = Path.Combine(root, "mine");
        try
        {
            Directory.CreateDirectory(shipped);
            Directory.CreateDirectory(mine);

            MapDocument Playable(string id, string name)
            {
                MapDocument doc = MapDocument.CreateNew(name);
                doc.Id = id;
                MapLayerData field = MapDocumentQueries.Battlefield(doc);
                field.Tiles.Add(new PlacedTile { DefId = "ground.stone", X = 0, Y = 10 });
                field.Tiles.Add(new PlacedTile { DefId = "spawn.character", X = 1, Y = 9 });
                field.Tiles.Add(new PlacedTile { DefId = "spawn.enemy", X = 5, Y = 9 });
                field.Tiles.Add(new PlacedTile { DefId = "goal.level", X = 3, Y = 9 });
                return doc;
            }

            // what a VK LV1 claim node would be dealt at the given altitude, as "id@folder"
            string Dealt(string terrain)
            {
                var names = new List<string>();
                foreach (CombatMapSelection c in CandidatesIn(mine, shipped, "VK", 1, CombatMapGoals.Claim, terrain))
                    names.Add(c.Document.Id + "@" + (c.AbsolutePath.StartsWith(mine, StringComparison.Ordinal) ? "mine" : "shipped"));
                return string.Join(",", names);
            }

            MapJson.Save(Playable("arena", "Arena"), Path.Combine(shipped, "arena.json"));
            Check(Dealt(CombatMapTerrain.SeaLevel) == "arena@shipped", "a shipped map is a candidate");

            MapJson.Save(MapDocument.CreateNew("Draft"), Path.Combine(mine, "draft.json"));
            Check(Dealt(CombatMapTerrain.SeaLevel) == "arena@shipped", "an empty draft is never dealt to a run");

            MapDocument halfEdited = Playable("arena", "Arena (editing)");
            MapDocumentQueries.Battlefield(halfEdited).Tiles.RemoveAll(tile => tile.DefId == "spawn.character");
            MapJson.Save(halfEdited, Path.Combine(mine, "arena.json"));
            Check(Dealt(CombatMapTerrain.SeaLevel) == "arena@shipped", "an unfinished copy falls back to the shipped map");

            MapJson.Save(Playable("arena", "Arena (edited)"), Path.Combine(mine, "arena.json"));
            Check(Dealt(CombatMapTerrain.SeaLevel) == "arena@mine", "a finished copy replaces the shipped map");

            Check(Dealt(CombatMapTerrain.High) == "", "a sea-level map does not serve high ground");
            MapDocument everywhere = Playable("arena", "Arena (any terrain)");
            everywhere.Terrain = "*";
            MapJson.Save(everywhere, Path.Combine(mine, "arena.json"));
            Check(Dealt(CombatMapTerrain.High) == "arena@mine" && Dealt(CombatMapTerrain.Lowground) == "arena@mine",
                "a '*' map serves every altitude band");
        }
        catch (Exception e)
        {
            failures.Add("CombatMapCatalog self-test threw: " + e.Message);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { /* a leftover scratch folder in the temp directory is harmless */ }
        }
        return failures;
    }
}

/// <summary>One validated map candidate plus the path used to reload it at arena entry.</summary>
public sealed class CombatMapSelection
{
    public string AbsolutePath;
    public MapDocument Document;
}

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
    /// What a zone-6 node is dealt (MapGDD §7). The VOID has no maps of its own yet, so it
    /// wears a world it has swallowed: an authored map of one of THIS RUN's worlds, matched to
    /// the node's mission (<see cref="VoidCandidatesFor"/>). The world is drawn first, so each
    /// of the five is as likely as another however many maps it ships; a world with nothing
    /// for the mission passes the turn to the next. Null ⇒ nothing matched, and the caller
    /// falls back to the legacy arena.
    ///
    /// The draw comes from a stream of its own, named from the seed and the node
    /// (<c>seed:void-map:nodeId</c>): entering the node again deals the same map, and the
    /// choice takes nothing from any stream the overworld, the outer worlds' selection or the
    /// arena read (KNOWLEDGE: determinism). <see cref="CombatMapSelection.World"/> says which
    /// world was worn.
    /// </summary>
    public static CombatMapSelection SelectForVoid(string seed, string nodeId, IReadOnlyList<string> runWorlds,
        MissionType mission)
    {
        if (runWorlds == null || runWorlds.Count == 0) return null;
        var worlds = new List<string>();
        foreach (string world in runWorlds)
            if (!string.IsNullOrWhiteSpace(world) && !worlds.Contains(world)) worlds.Add(world);
        worlds.Sort(StringComparer.Ordinal);            // the answer depends on WHICH worlds, not on their order

        var rng = new DetRandom((seed ?? "debug") + ":void-map:" + (nodeId ?? "debug"));
        rng.Shuffle(worlds);
        foreach (string world in worlds)
        {
            List<CombatMapSelection> candidates = VoidCandidatesFor(world, mission, UserMapsEnabled);
            if (candidates.Count == 0) continue;
            CombatMapSelection dealt = candidates[rng.Range(0, candidates.Count - 1)];
            dealt.World = world;
            return dealt;
        }
        return null;
    }

    /// <summary>
    /// Every map of <paramref name="world"/> a zone-6 node with this mission could be dealt,
    /// in stable id order — the rule <see cref="SelectForVoid"/> draws with and the content
    /// check audits. Any altitude band (the VOID has none) and any hardship level (its nodes
    /// are LV5 and LV6, levels no map is authored for): a swallowed world brings everything it
    /// had. The mission still decides — the LV6 core is dealt the world's boss arenas, and its
    /// ordinary maps only where it has none, exactly as <see cref="CandidatesFor"/> rules in
    /// the outer worlds; no other mission is dealt a boss arena.
    /// </summary>
    public static List<CombatMapSelection> VoidCandidatesFor(string world, MissionType mission,
        bool includeUserMaps = true)
    {
        string user = includeUserMaps ? ProjectSettings.GlobalizePath(UserDirectory) : null;
        string builtIn = ProjectSettings.GlobalizePath(BuiltInDirectory);
        List<CombatMapSelection> candidates = CandidatesIn(user, builtIn, world, null, GoalFor(mission), null);
        if (candidates.Count == 0 && mission == MissionType.Boss)
            candidates = CandidatesIn(user, builtIn, world, null, CombatMapGoals.Claim, null);
        return candidates;
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
        // An outer node always has a band; a missing one matches "*" maps only, as it always
        // did — "any band" (null, below) is zone 6's rule and nobody else's.
        return CandidatesIn(includeUserMaps ? ProjectSettings.GlobalizePath(UserDirectory) : null,
            ProjectSettings.GlobalizePath(BuiltInDirectory), world, hardship, goal, terrain ?? "");
    }

    /// <summary>The selection rule over two explicit folders (the user's first, so its copies
    /// win). <see cref="SelfTest"/> points it at scratch folders. A null
    /// <paramref name="hardship"/> or <paramref name="terrain"/> does not filter on it — zone 6,
    /// which has neither a level any map is authored for nor an altitude.</summary>
    private static List<CombatMapSelection> CandidatesIn(string userDirectory, string builtInDirectory,
        string world, int? hardship, string goal, string terrain)
    {
        var candidates = new List<CombatMapSelection>();
        var seenIds = new HashSet<string>();
        LoadDirectory(userDirectory, false, candidates, seenIds);
        LoadDirectory(builtInDirectory, true, candidates, seenIds);

        string goalKey = Normalize(goal);
        string terrainKey = terrain == null ? null : Normalize(terrain);
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

    private static bool Matches(MapDocument doc, string world, int? hardship, string goal, string terrain)
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
        if (hardship.HasValue && doc.HardshipLevels is { Count: > 0 } && !doc.HardshipLevels.Contains(hardship.Value)) return false;
        if (doc.Goals is { Count: > 0 } && !doc.Goals.Contains(goal)) return false;
        if (terrain == null) return true;               // zone 6: any band
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

            // Zone 6 wears a world (MapGDD §7): any band and any level of THAT world, the
            // mission still deciding — and a boss arena for the core alone.
            string swallowed = Path.Combine(root, "swallowed");
            Directory.CreateDirectory(swallowed);
            void Ship(string id, string world, string terrain, string goal, params int[] levels)
            {
                MapDocument doc = Playable(id, id);
                doc.Worlds = new List<string> { world };
                doc.Terrain = terrain;
                doc.Goals = new List<string> { goal };
                doc.HardshipLevels = new List<int>(levels);
                MapJson.Save(doc, Path.Combine(swallowed, id + ".json"));
            }
            string Worn(string world, string goal)
            {
                var ids = new List<string>();
                foreach (CombatMapSelection c in CandidatesIn(null, swallowed, world, null, goal, null)) ids.Add(c.Document.Id);
                return string.Join(",", ids);
            }
            Ship("a-high-lv2", "VK", CombatMapTerrain.High, CombatMapGoals.Claim, 2);
            Ship("b-low", "VK", CombatMapTerrain.Lowground, CombatMapGoals.Claim);
            Ship("c-throne", "VK", "*", CombatMapGoals.Boss, 4);
            Ship("d-elsewhere", "HC", "*", CombatMapGoals.Claim);
            Check(Worn("VK", CombatMapGoals.Claim) == "a-high-lv2,b-low", "zone 6 is dealt a world's maps of any band and any level — and only that world's, and no boss arena");
            Check(Worn("VK", CombatMapGoals.Boss) == "c-throne", "zone 6's core is dealt the world's boss arena");
            Check(Worn("VK", CombatMapGoals.Protect) == "", "zone 6 still matches the mission");
            int outerLv5 = CandidatesIn(null, swallowed, "VK", 5, CombatMapGoals.Claim, CombatMapTerrain.SeaLevel).Count;
            Check(outerLv5 == 0, "the outer rule is unchanged: it still filters on band and level");
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
    /// <summary>The world zone 6 wore to be dealt this map (<see cref="CombatMapCatalog.SelectForVoid"/>);
    /// null for a node of an outer world, which is dealt its own.</summary>
    public string World;
}

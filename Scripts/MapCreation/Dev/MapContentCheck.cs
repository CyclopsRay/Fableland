namespace Fableland.MapCreation.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;
using Fableland.Run;

/// <summary>
/// Headless content check for the map layer (40-QA.md §2 "real build / Godot --headless").
/// Runs every Map Creation self-test and the registry/art audit, loads each hazard scene and
/// tile sprite, then builds every built-in `res://Maps/*.json` through the same
/// <see cref="CombatMapRuntime"/> path a real run uses and checks it is playable: the
/// markers each of its goals needs, a usable nest for both foe types, tiles from one realm.
/// Last, it asks the run's own map selection (<see cref="CombatMapCatalog"/>) whether every
/// kind of combat node in every world would be dealt a shipped map.
///
///     Godot --headless --path . res://Scenes/MapCreation/MapContentCheck.tscn
///
/// Exit code 0 = clean, 1 = at least one problem (each printed on its own `FAIL` line).
/// It builds maps but never starts a fight, so it is safe to run on any machine.
/// </summary>
public partial class MapContentCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;

    public override void _Ready()
    {
        try
        {
            RunDomainSelfTests();
            CheckTileAssets();
            CheckBuiltInMaps();
            CheckRealmCoverage();
        }
        catch (Exception e)
        {
            _problems.Add("content check threw: " + e);
        }

        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0
            ? $"MapContentCheck: OK ({_checks} checks, {TileRegistry.All.Count} tile kinds)"
            : $"MapContentCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private void Absorb(string suite, List<string> failures)
    {
        _checks++;
        foreach (string failure in failures) _problems.Add(suite + ": " + failure);
    }

    private void RunDomainSelfTests()
    {
        string root = ProjectSettings.GlobalizePath("res://");
        Absorb("MapJson", MapJson.RoundTripSelfTest());
        Absorb("TileEffectStore", TileEffectStore.RoundTripSelfTest());
        Absorb("EffectAreaTransform", EffectAreaTransform.SelfTest());
        Absorb("TileRegistry", TileRegistry.Validate());
        Absorb("RuleResolver", RuleResolver.SelfTest());
        Absorb("HillAutotile", HillAutotile.SelfTest());
        Absorb("TileVisualResolver", TileVisualResolver.SelfTest());
        Absorb("AutotileProbe", AutotileProbe.SelfTest());
        Absorb("MapReadiness", MapReadiness.SelfTest());
        Absorb("MapThumbnailFraming", MapThumbnailFraming.SelfTest());
        Absorb("CombatMapCatalog", CombatMapCatalog.SelfTest());
        Absorb("TileManifestLoader", TileManifestLoader.SelfTest());
        Absorb("TileManifestLoader fixtures", TileManifestLoader.SelfTestFixtures(root));
        Absorb("TileSetAudit", TileSetAudit.Run(root));
    }

    /// <summary>Every sprite/atlas a tile kind names must load, and every hazard's scene must
    /// instantiate as what the map builders expect.</summary>
    private void CheckTileAssets()
    {
        foreach (TileDef def in TileRegistry.All)
        {
            if (!string.IsNullOrWhiteSpace(def.SpriteSlot))
                Expect(ResourceLoader.Load<Texture2D>(def.SpriteSlot) != null, $"{def.Id}: SpriteSlot does not load as a texture");

            if (def.Props != null && def.Props.TryGetValue(TileProps.ArtSource, out string atlas) && !string.IsNullOrWhiteSpace(atlas))
                Expect(ResourceLoader.Load<Texture2D>(atlas) != null, $"{def.Id}: artSource atlas does not load as a texture");

            if (def.Category != TileCategory.Hazard) continue;
            string scenePath = MapHazardFactory.SceneOf(def);
            var scene = string.IsNullOrWhiteSpace(scenePath) ? null : ResourceLoader.Load<PackedScene>(scenePath);
            Expect(scene != null, $"{def.Id}: hazard scene '{scenePath}' does not load");
            if (scene == null) continue;

            Node instance = scene.Instantiate();
            bool trigger = MapHazardFactory.IsEventTrigger(def);
            Expect(trigger ? instance is TsunamiTrigger : instance is Hazard,
                $"{def.Id}: scene '{scenePath}' is a {instance.GetType().Name}, expected {(trigger ? "an event trigger" : "a stationary Hazard")}");
            instance.Free();
        }
    }

    private void CheckBuiltInMaps()
    {
        string dir = ProjectSettings.GlobalizePath("res://Maps");
        Expect(Directory.Exists(dir), "res://Maps does not exist");
        if (!Directory.Exists(dir)) return;

        string[] paths = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        var ids = new HashSet<string>();

        foreach (string path in paths)
        {
            string name = Path.GetFileName(path);
            MapDocument doc = MapJson.Load(path, out List<string> warnings);
            Expect(doc != null, $"{name}: does not load");
            foreach (string warning in warnings) _problems.Add($"{name}: {warning}");
            if (doc == null) continue;

            Expect(ids.Add(doc.Id), $"{name}: map id '{doc.Id}' is used by another built-in map");
            // The one definition of "finished" the browser and the run's selection share: a
            // shipped map that fails it would be silently skipped by CombatMapCatalog.
            List<string> unfinished = MapReadiness.Problems(doc);
            Expect(unfinished.Count == 0, $"{name}: not playable — {string.Join(", ", unfinished)}");
            CheckWorlds(name, doc);
            CheckTileSetPurity(name, doc);

            var world = new Node2D();
            var hazards = new Node2D();
            AddChild(world);
            AddChild(hazards);
            CombatMapBuild build = CombatMapRuntime.Build(doc, world, hazards);

            Expect(build.CharacterSpawns.Count == 1, $"{name}: needs exactly one Character Spawn (has {build.CharacterSpawns.Count})");
            Expect(build.RespawnSpawns.Count >= 1, $"{name}: needs at least one Respawn Point");
            Expect(build.EnemySpawns.Count >= 1, $"{name}: needs at least one Enemy Spawn");
            Expect(HasNest(doc, build, aerial: false), $"{name}: no Enemy Spawn row is eligible for ground foes under its foeSpawnRules");
            Expect(HasNest(doc, build, aerial: true), $"{name}: no Enemy Spawn row is eligible for aerial foes under its foeSpawnRules");
            foreach (string goal in doc.Goals ?? new List<string>())
            {
                if (goal == CombatMapGoals.Slaughter) continue;
                Expect(build.LevelGoalSpawns.Count >= 1, $"{name}: goal '{goal}' needs at least one Level Goal marker");
            }
            Expect(build.ParallaxLayers.Count == doc.Layers.Count, $"{name}: built {build.ParallaxLayers.Count} art layers for {doc.Layers.Count} map layers");

            GD.Print($"  {name,-34} {doc.Worlds.Count switch { 0 => "any", _ => string.Join("/", doc.Worlds) },-5} " +
                     $"{build.WidthPx / MapGrid.PixelsPerCell,3:0}x{build.HeightPx / MapGrid.PixelsPerCell,-3:0} " +
                     $"terrain={doc.Terrain,-11} goals={string.Join(",", doc.Goals)} " +
                     $"nests={build.EnemySpawns.Count} goalMarkers={build.LevelGoalSpawns.Count} hazards={hazards.GetChildCount()}");

            world.Free();
            hazards.Free();
        }
    }

    /// <summary>
    /// Would a run always be dealt a shipped map? Walks every kind of combat node the
    /// overworld can roll in an outer world — LV1–3 with each ordinary mission, the LV4 boss —
    /// at each altitude band, and asks <see cref="CombatMapCatalog"/> (shipped maps only, so
    /// the answer does not depend on this machine's <c>user://maps</c>) for candidates. A kind
    /// with none sends the player to the legacy arena, which is how a realm whose only map was
    /// tagged for one altitude band stayed invisible on two nodes in three (v0.10.7).
    /// The VOID's own nodes (zone "XX", LV5–6) have no authored maps yet and are not walked.
    /// </summary>
    private void CheckRealmCoverage()
    {
        string[] terrains = { CombatMapTerrain.Lowground, CombatMapTerrain.SeaLevel, CombatMapTerrain.High };
        MissionType[] ordinary = { MissionType.Collection, MissionType.Protect, MissionType.Destroy, MissionType.Slaughter };
        MissionType[] boss = { MissionType.Boss };   // LV4 is structural (MapGenerator.RollMissions)

        foreach (WorldDef world in WorldDef.Pool)
        {
            var missing = new List<string>();
            int kinds = 0;
            for (int level = 1; level <= 4; level++)
            {
                foreach (MissionType mission in level == 4 ? boss : ordinary)
                {
                    foreach (string terrain in terrains)
                    {
                        kinds++;
                        string goal = CombatMapCatalog.GoalFor(mission);
                        if (CombatMapCatalog.Candidates(world.Abbr, level, goal, terrain, includeUserMaps: false).Count == 0)
                            missing.Add($"LV{level} {mission} {terrain}");
                    }
                }
            }

            GD.Print($"  coverage {world.Abbr,-3} {kinds - missing.Count}/{kinds} kinds of combat node are served by a shipped map");
            Expect(missing.Count == 0,
                $"{world.Abbr} ({world.Name}): {missing.Count} of {kinds} kinds of combat node have no shipped map and fall back to the legacy arena — e.g. {string.Join("; ", missing.GetRange(0, Math.Min(3, missing.Count)))}");
        }
    }

    /// <summary>A map tagged for a world outside the overworld pool can never be selected
    /// by a run (`CombatMapCatalog`). That is legitimate for a realm still being written into
    /// the story, so it is reported, not failed.</summary>
    private static void CheckWorlds(string name, MapDocument doc)
    {
        foreach (string tag in doc.Worlds ?? new List<string>())
        {
            bool known = false;
            foreach (WorldDef world in WorldDef.Pool)
            {
                if (string.Equals(world.Abbr, tag, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(world.Name, tag, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
            }
            if (!known) GD.Print($"  note: {name} is tagged for world '{tag}', which is not in the overworld pool yet — playable from the editor only.");
        }
    }

    /// <summary>Each realm has its own art language; a map that mixes realm sets is almost
    /// always a slip of the brush, so say so (shared "core" markers are fine everywhere).</summary>
    private void CheckTileSetPurity(string name, MapDocument doc)
    {
        var sets = new SortedSet<string>(StringComparer.Ordinal);
        foreach (MapLayerData layer in doc.Layers)
        {
            if (layer?.Tiles == null) continue;
            foreach (PlacedTile tile in layer.Tiles)
            {
                if (TileRegistry.TryGet(tile.DefId, out TileDef def) && def.TileSet != TileSets.Core)
                    sets.Add(def.TileSet);
            }
        }
        Expect(sets.Count <= 1, $"{name}: mixes realm tile sets ({string.Join(", ", sets)})");
    }

    private static bool HasNest(MapDocument doc, CombatMapBuild build, bool aerial)
    {
        FoeSpawnRules rules = doc.FoeSpawnRules;
        foreach (CombatMapSpawnPoint point in build.EnemySpawns)
        {
            if (!aerial && (rules?.CrabMaxCellY == null || point.CellY <= rules.CrabMaxCellY.Value)) return true;
            if (aerial && (rules?.SeagullMinCellY == null || point.CellY >= rules.SeagullMinCellY.Value)) return true;
        }
        return false;
    }
}

namespace Fableland.MapCreation.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Playtest;
using Fableland.MapCreation.Runtime;
using Fableland.Run;

/// <summary>
/// Headless content check for the map layer (40-QA.md §2 "real build / Godot --headless").
/// Runs every Map Creation self-test and the registry/art audit, loads each hazard scene and
/// tile sprite, then builds every built-in `res://Maps/*.json` through the same
/// <see cref="CombatMapRuntime"/> path a real run uses and checks it is playable: the
/// markers each of its goals needs, a usable nest for both foe types, tiles from one realm.
/// It then builds a small map with every piece nudged off the grid (GDD §2.3) through the
/// arena path AND the editor Playtest, and requires art, colliders, hazards and markers to
/// land on the same shifted spots in both. It asks the run's own map selection
/// (<see cref="CombatMapCatalog"/>) whether every kind of combat node in every world would
/// be dealt a shipped map — and, the other way round, whether every shipped map is dealt to
/// some node a run can produce (no orphan map). Last come water, currents and waterfalls
/// (<c>MapContentCheckPhysics.cs</c>): both builders must make the same soft volumes, and a
/// real character is run through each shipped water kind to measure what it does to a body.
///
///     Godot --headless --path . res://Scenes/MapCreation/MapContentCheck.tscn
///
/// Exit code 0 = clean, 1 = at least one problem (each printed on its own `FAIL` line).
/// It builds maps and runs about ten simulated seconds of movement (a second or two of real
/// time) but never starts a fight, so it is safe to run on any machine.
/// </summary>
public partial class MapContentCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;

    public override async void _Ready()
    {
        try
        {
            RunDomainSelfTests();
            CheckTileAssets();
            CheckBuiltInMaps();
            CheckNudgedPlacement();
            CheckRealmCoverage();
            CheckEveryMapIsDealt();
            CheckSoftVolumeParity();
            CheckEveryMediumCanBeSwumUp();
            await ProbeSoftVolumePhysics();   // runs physics frames; everything above is immediate
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
        Absorb("TilePlacement", TilePlacement.SelfTest());
        Absorb("TileNudge", TileNudge.SelfTest());
        Absorb("TileRegistry", TileRegistry.Validate());
        Absorb("RuleResolver", RuleResolver.SelfTest());
        Absorb("HillAutotile", HillAutotile.SelfTest());
        Absorb("TileVisualResolver", TileVisualResolver.SelfTest());
        Absorb("AutotileProbe", AutotileProbe.SelfTest());
        Absorb("MapReadiness", MapReadiness.SelfTest());
        Absorb("MapRealm", MapRealm.SelfTest());
        Absorb("MapThumbnailFraming", MapThumbnailFraming.SelfTest());
        Absorb("CombatMapCatalog", CombatMapCatalog.SelfTest());
        Absorb("TileManifestLoader", TileManifestLoader.SelfTest());
        Absorb("TileManifestLoader fixtures", TileManifestLoader.SelfTestFixtures(root));
        Absorb("SoftVolumeSet", SoftVolumeSet.SelfTest());
        Absorb("MapCollision", MapCollision.SelfTest());
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
            CheckTileSetPurity(name, doc);
            CheckRealmBelongsToItsWorld(name, doc);

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

    // ---------------------------------------------------------------- sub-cell nudge (GDD §2.3)

    /// <summary>One piece of the nudge test map: a kind, its anchor cell and the nudge it is
    /// given in the nudged build (quarter cells).</summary>
    private readonly record struct NudgeProbe(string DefId, int X, int Y, int SubX, int SubY, bool Flip = false);

    private static readonly NudgeProbe[] NudgeProbes =
    {
        new("platform.bench", 5, 9, 2, -1, Flip: true),
        new("softvolume.cloud2x1", 3, 3, -3, 2),
        new("hazard.bonfire", 10, 9, -3, 1),
        new("spawn.character", 1, 9, 1, 0),
        new("spawn.respawn", 2, 9, 0, -2),
        new("spawn.enemy", 14, 9, -2, 0),
        new("goal.level", 16, 9, 3, -3),
    };

    private const int NudgeFloorY = 10, NudgeGridW = 20, NudgeGridH = 12;

    /// <summary>A 20x12 battlefield: a stone floor and one piece of every nudgeable category.
    /// In the nudged build the floor is given a nudge too — one it must ignore.</summary>
    private static MapDocument NudgeTestMap(bool nudged)
    {
        MapDocument doc = MapDocument.CreateNew("Nudge Parity");
        doc.Layers.RemoveAll(layer => layer.Role != MapLayerData.RoleBattlefield);
        MapLayerData field = MapDocumentQueries.Battlefield(doc);
        field.GridW = NudgeGridW;
        field.GridH = NudgeGridH;
        for (int x = 0; x < NudgeGridW; x++)
            field.Tiles.Add(new PlacedTile { DefId = "ground.stone", X = x, Y = NudgeFloorY, SubX = nudged ? 2 : 0, SubY = nudged ? 1 : 0 });
        foreach (NudgeProbe probe in NudgeProbes)
        {
            field.Tiles.Add(new PlacedTile
            {
                DefId = probe.DefId, X = probe.X, Y = probe.Y, FlipX = probe.Flip,
                SubX = nudged ? probe.SubX : 0, SubY = nudged ? probe.SubY : 0,
            });
        }
        return doc;
    }

    /// <summary>Where one build put everything, in creation order.</summary>
    private sealed class PlacementSnapshot
    {
        public readonly List<Vector2> Art = new();
        public readonly List<Vector2> Physics = new();
        public readonly List<Vector2> Hazards = new();
        public Vector2 CharacterSpawn, Respawn, EnemySpawn, LevelGoal;
        public int EnemySpawnRow;
    }

    private PlacementSnapshot SnapshotArenaBuild(MapDocument doc)
    {
        var world = new Node2D();
        var hazards = new Node2D();
        AddChild(world);
        AddChild(hazards);
        CombatMapBuild build = CombatMapRuntime.Build(doc, world, hazards);

        var snapshot = new PlacementSnapshot();
        // One battlefield layer -> one parallax layer -> its "SingleVisuals" holder.
        foreach (Node host in world.GetNode("AuthoredMapVisuals").GetChild(0).GetChild(0).GetChildren())
            snapshot.Art.Add(((Node2D)host).Position);
        foreach (Node body in world.GetNode("AuthoredMapPhysics").GetChildren())
            snapshot.Physics.Add(((Node2D)body).Position);
        foreach (Node hazard in hazards.GetChildren())
            snapshot.Hazards.Add(((Node2D)hazard).Position);
        if (build.CharacterSpawns.Count > 0) snapshot.CharacterSpawn = build.CharacterSpawns[0];
        if (build.RespawnSpawns.Count > 0) snapshot.Respawn = build.RespawnSpawns[0];
        if (build.LevelGoalSpawns.Count > 0) snapshot.LevelGoal = build.LevelGoalSpawns[0];
        if (build.EnemySpawns.Count > 0)
        {
            snapshot.EnemySpawn = build.EnemySpawns[0].Position;
            snapshot.EnemySpawnRow = build.EnemySpawns[0].CellY;
        }

        world.Free();
        hazards.Free();
        return snapshot;
    }

    /// <summary>The editor's Play button builds physics in its own pass. Stand it up for
    /// this one map — no frame runs — and read back where it put things.</summary>
    private PlacementSnapshot SnapshotPlaytest(MapDocument doc)
    {
        var playtest = new MapPlaytestController();
        playtest.Initialize(doc);
        AddChild(playtest);

        var snapshot = new PlacementSnapshot();
        foreach (Node host in playtest.GetNode("WorldVisual").GetChild(0).GetChild(0).GetChildren())
            snapshot.Art.Add(((Node2D)host).Position);
        foreach (Node body in playtest.GetNode("Physics").GetChildren())
            snapshot.Physics.Add(((Node2D)body).Position);
        foreach (Node hazard in playtest.GetNode("Hazards").GetChildren())
            snapshot.Hazards.Add(((Node2D)hazard).Position);
        Node entities = playtest.GetNode("Entities");
        if (entities.GetChildCount() > 0) snapshot.CharacterSpawn = ((Node2D)entities.GetChild(0)).Position;

        playtest.Free();
        return snapshot;
    }

    private void CheckNudgedPlacement()
    {
        PlacementSnapshot plain = SnapshotArenaBuild(NudgeTestMap(nudged: false));
        PlacementSnapshot nudged = SnapshotArenaBuild(NudgeTestMap(nudged: true));
        PlacementSnapshot playtest = SnapshotPlaytest(NudgeTestMap(nudged: true));

        static bool Near(Vector2 a, Vector2 b) => a.DistanceTo(b) < 0.01f;
        static Vector2 Shift(NudgeProbe probe) =>
            new(probe.SubX * TilePlacement.SubcellPx, probe.SubY * TilePlacement.SubcellPx);

        NudgeProbe bench = NudgeProbes[0], cloud = NudgeProbes[1], bonfire = NudgeProbes[2];
        int floor = NudgeGridW;   // the floor tiles come first in every list

        // Shape of the build: floor + bench + cloud + bonfire art; floor + bench + cloud bodies; one hazard.
        bool shaped = plain.Art.Count == floor + 3 && nudged.Art.Count == floor + 3 &&
                      plain.Physics.Count == floor + 2 && nudged.Physics.Count == floor + 2 &&
                      plain.Hazards.Count == 1 && nudged.Hazards.Count == 1;
        Expect(shaped, "nudge: the test map did not build one art node per piece, a body per collider and one hazard");
        if (!shaped) return;

        // Terrain ignores a nudge: art and collider stay on their cells.
        bool floorHeld = true;
        for (int i = 0; i < floor; i++)
            floorHeld &= Near(nudged.Art[i], plain.Art[i]) && Near(nudged.Physics[i], plain.Physics[i]);
        Expect(floorHeld, "nudge: ground moved off the grid when its tile carried a nudge");

        // Every nudgeable piece moves by exactly its nudge — art and collider together.
        Expect(Near(nudged.Art[floor] - plain.Art[floor], Shift(bench)), "nudge: the bench's art did not move by its nudge");
        Expect(Near(nudged.Physics[floor] - plain.Physics[floor], Shift(bench)), "nudge: the bench's collider did not move by its nudge");
        Expect(Near(nudged.Art[floor], nudged.Physics[floor]), "nudge: the bench is drawn and collided with at different origins");
        Expect(Near(nudged.Art[floor], new Vector2(
                bench.X * MapGrid.PixelsPerCell + bench.SubX * MapGrid.PixelsPerCell / 4f,
                bench.Y * MapGrid.PixelsPerCell + bench.SubY * MapGrid.PixelsPerCell / 4f)),
            "nudge: a quarter-cell nudge is not a quarter of a cell in pixels");
        Expect(Near(nudged.Art[floor + 1] - plain.Art[floor + 1], Shift(cloud)), "nudge: the cloud's art did not move by its nudge");
        Expect(Near(nudged.Physics[floor + 1] - plain.Physics[floor + 1], Shift(cloud)), "nudge: the cloud's SoftVolume did not move by its nudge");
        Expect(Near(nudged.Art[floor + 2] - plain.Art[floor + 2], Shift(bonfire)), "nudge: the bonfire's art did not move by its nudge");
        Expect(Near(nudged.Hazards[0] - plain.Hazards[0], Shift(bonfire)), "nudge: the bonfire's hazard did not move by its nudge");

        // Markers: the point moves, the authored row a nest belongs to does not.
        Expect(Near(nudged.CharacterSpawn - plain.CharacterSpawn, Shift(NudgeProbes[3])), "nudge: the character spawn point did not move by its nudge");
        Expect(Near(nudged.Respawn - plain.Respawn, Shift(NudgeProbes[4])), "nudge: the respawn point did not move by its nudge");
        Expect(Near(nudged.EnemySpawn - plain.EnemySpawn, Shift(NudgeProbes[5])), "nudge: the enemy spawn point did not move by its nudge");
        Expect(nudged.EnemySpawnRow == NudgeProbes[5].Y && plain.EnemySpawnRow == NudgeProbes[5].Y,
            "nudge: an enemy spawn's rule row must stay its anchor row");
        Expect(Near(nudged.LevelGoal - plain.LevelGoal, Shift(NudgeProbes[6])), "nudge: the level goal point did not move by its nudge");

        // The Playtest builds physics in its own pass: it must agree with the arena on everything.
        bool sameShape = playtest.Art.Count == nudged.Art.Count && playtest.Physics.Count == nudged.Physics.Count &&
                         playtest.Hazards.Count == nudged.Hazards.Count;
        Expect(sameShape, "nudge: the Playtest and the arena built a different number of pieces from one map");
        if (!sameShape) return;

        bool sameArt = true, samePhysics = true;
        for (int i = 0; i < nudged.Art.Count; i++) sameArt &= Near(playtest.Art[i], nudged.Art[i]);
        for (int i = 0; i < nudged.Physics.Count; i++) samePhysics &= Near(playtest.Physics[i], nudged.Physics[i]);
        Expect(sameArt, "nudge: the Playtest draws a piece somewhere other than the arena does");
        Expect(samePhysics, "nudge: the Playtest puts a collider somewhere other than the arena does");
        Expect(Near(playtest.Hazards[0], nudged.Hazards[0]), "nudge: the Playtest puts a hazard somewhere other than the arena does");
        Expect(Near(playtest.CharacterSpawn, nudged.CharacterSpawn), "nudge: the Playtest and the arena start the character at different points");
    }

    /// <summary>
    /// Would a run always be dealt a shipped map? Walks every kind of combat node the
    /// overworld can roll in an outer world — LV1–3 with each ordinary mission, the LV4 boss —
    /// at each altitude band, and asks <see cref="CombatMapCatalog"/> (shipped maps only, so
    /// the answer does not depend on this machine's <c>user://maps</c>) for candidates. A kind
    /// with none sends the player to the legacy arena, which is how a realm whose only map was
    /// tagged for one altitude band stayed invisible on two nodes in three (v0.10.7).
    /// Zone 6's nodes (zone "XX", LV5–6) wear a pool world's maps and are walked by
    /// <see cref="CheckEveryMapIsDealt"/>.
    ///
    /// Since v0.11.0 every pool world must also ship its own set: a map authored FOR each
    /// altitude band (not only a "*" map that happens to serve it) and a boss arena for its
    /// 4-1 — otherwise a band cannot be tested on purpose, and a boss fights on a map that
    /// gives it no perch.
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
                        if (CombatMapCatalog.CandidatesFor(world.Abbr, level, mission, terrain, includeUserMaps: false).Count == 0)
                            missing.Add($"LV{level} {mission} {terrain}");
                    }
                }
            }

            // Its own set: one map per band authored for that band, and a boss arena.
            var set = new List<string>();
            foreach (string terrain in terrains)
            {
                string own = null;
                foreach (CombatMapSelection map in CombatMapCatalog.Candidates(world.Abbr, 1, CombatMapGoals.Claim, terrain, includeUserMaps: false))
                    if (string.Equals(map.Document.Terrain?.Trim(), terrain, StringComparison.OrdinalIgnoreCase)) { own = map.Document.Name; break; }
                Expect(own != null, $"{world.Abbr} ({world.Name}): no shipped map is authored for {terrain} (a '*' map serves the band but cannot be chosen for it)");
                if (own != null) set.Add(terrain);
            }
            List<CombatMapSelection> arenas = CombatMapCatalog.Candidates(world.Abbr, 4, CombatMapGoals.Boss, CombatMapTerrain.SeaLevel, includeUserMaps: false);
            foreach (string terrain in terrains)
                Expect(CombatMapCatalog.Candidates(world.Abbr, 4, CombatMapGoals.Boss, terrain, includeUserMaps: false).Count > 0,
                    $"{world.Abbr} ({world.Name}): its LV4 boss at {terrain} has no boss arena (goal 'boss') and would fight on an ordinary map");

            GD.Print($"  coverage {world.Abbr,-3} {kinds - missing.Count}/{kinds} kinds of combat node are served by a shipped map · own maps for {set.Count}/{terrains.Length} bands · boss arena: {(arenas.Count > 0 ? arenas[0].Document.Name : "none")}");
            Expect(missing.Count == 0,
                $"{world.Abbr} ({world.Name}): {missing.Count} of {kinds} kinds of combat node have no shipped map and fall back to the legacy arena — e.g. {string.Join("; ", missing.GetRange(0, Math.Min(3, missing.Count)))}");
        }
    }

    /// <summary>The shipped map both Cloud Court decisions are about (style bible §7C).</summary>
    private const string CloudCourtMapId = "cloud_court_heavens_pl";

    /// <summary>
    /// No orphan map: every shipped map must be dealt to at least one kind of node a run can
    /// actually produce — an outer-world node (a pool world × LV1–3 × an ordinary mission × a
    /// band, or its LV4 boss × a band) or a zone-6 node wearing a pool world (LV5 × an
    /// ordinary mission, the LV6 core; MapGDD §7). It asks the two rules the run itself draws
    /// with (<see cref="CombatMapCatalog.CandidatesFor"/>, <see cref="CombatMapCatalog.VoidCandidatesFor"/>),
    /// shipped maps only. A map no node is dealt is built, tested and never seen: Cloud Court
    /// sat there from v0.10.9, tagged for a world code no world owned, and was reported as a
    /// note. It fails now.
    ///
    /// The same walk answers for zone 6: whichever pool world it wears, each mission its
    /// nodes can roll must find a shipped map, and its core a boss arena.
    /// </summary>
    private void CheckEveryMapIsDealt()
    {
        string[] terrains = { CombatMapTerrain.Lowground, CombatMapTerrain.SeaLevel, CombatMapTerrain.High };
        MissionType[] ordinary = { MissionType.Collection, MissionType.Protect, MissionType.Destroy, MissionType.Slaughter };

        var outerKinds = new Dictionary<string, int>();              // map id → kinds of outer-world node dealt it
        var outerPlaces = new Dictionary<string, SortedSet<string>>(); // map id → "world band" it is dealt on
        var voidKinds = new Dictionary<string, int>();               // map id → kinds of zone-6 node dealt it

        foreach (WorldDef world in WorldDef.Pool)
        {
            for (int level = 1; level <= 4; level++)
            {
                foreach (MissionType mission in level == 4 ? new[] { MissionType.Boss } : ordinary)
                {
                    foreach (string terrain in terrains)
                    {
                        foreach (CombatMapSelection map in CombatMapCatalog.CandidatesFor(world.Abbr, level, mission, terrain, includeUserMaps: false))
                        {
                            string id = map.Document.Id;
                            outerKinds[id] = outerKinds.GetValueOrDefault(id) + 1;
                            if (!outerPlaces.TryGetValue(id, out SortedSet<string> places)) outerPlaces[id] = places = new SortedSet<string>(StringComparer.Ordinal);
                            places.Add(world.Abbr + " " + terrain);
                        }
                    }
                }
            }

            // Zone 6 wearing this world: its LV5 nodes roll the ordinary missions, its core is the boss.
            var unserved = new List<string>();
            foreach (MissionType mission in ordinary)
            {
                List<CombatMapSelection> dealt = CombatMapCatalog.VoidCandidatesFor(world.Abbr, mission, includeUserMaps: false);
                foreach (CombatMapSelection map in dealt) voidKinds[map.Document.Id] = voidKinds.GetValueOrDefault(map.Document.Id) + 1;
                if (dealt.Count == 0) unserved.Add("an LV5 " + mission);
            }
            List<CombatMapSelection> core = CombatMapCatalog.VoidCandidatesFor(world.Abbr, MissionType.Boss, includeUserMaps: false);
            foreach (CombatMapSelection map in core) voidKinds[map.Document.Id] = voidKinds.GetValueOrDefault(map.Document.Id) + 1;
            if (core.Count == 0) unserved.Add("the LV6 core");
            Expect(unserved.Count == 0,
                $"{world.Abbr} ({world.Name}): zone 6 wearing this world has no shipped map for {string.Join(", ", unserved)} and would open the legacy arena");
            Expect(core.TrueForAll(map => map.Document.Goals != null && map.Document.Goals.Contains(CombatMapGoals.Boss)),
                $"{world.Abbr} ({world.Name}): zone 6's core would be dealt an ordinary map of this world, not its boss arena");
        }

        string dir = ProjectSettings.GlobalizePath("res://Maps");
        if (!Directory.Exists(dir)) return;
        string[] paths = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        int maps = 0, orphans = 0;
        var voidOnly = new List<string>();
        foreach (string path in paths)
        {
            MapDocument doc = MapJson.Load(path, out _);
            if (doc == null) continue;                               // reported by CheckBuiltInMaps
            maps++;
            string name = Path.GetFileName(path);
            int outer = outerKinds.GetValueOrDefault(doc.Id), inVoid = voidKinds.GetValueOrDefault(doc.Id);
            Expect(outer + inVoid > 0, $"{name}: orphan map — no node a run can produce is ever dealt it ({WhyNeverDealt(doc)})");
            if (outer + inVoid == 0) orphans++;
            else if (outer == 0) voidOnly.Add(name);
        }

        // Cloud Court is the Palace's sky district: dealt on the Palace's high ground, and
        // nowhere else in the outer worlds (style bible §7C; provisional, TBD #26).
        string court = outerPlaces.TryGetValue(CloudCourtMapId, out SortedSet<string> where) ? string.Join(", ", where) : "nowhere";
        Expect(court == "PL " + CombatMapTerrain.High,
            $"Cloud Court ('{CloudCourtMapId}') must be dealt on the Palace of LOOING's high ground and nowhere else in the outer worlds; it is dealt on: {court}");

        GD.Print($"  reach    {maps} shipped maps: {maps - orphans - voidOnly.Count} are dealt in an outer world · {voidOnly.Count} only where zone 6 wears its world{(voidOnly.Count > 0 ? " (" + string.Join(", ", voidOnly) + ")" : "")} · {orphans} never · Cloud Court on {court}, {outerKinds.GetValueOrDefault(CloudCourtMapId)} kinds of node");
    }

    /// <summary>Why selection can never reach a map, in the map's own terms.</summary>
    private static string WhyNeverDealt(MapDocument doc)
    {
        foreach (string tag in doc.Worlds ?? new List<string>())
        {
            bool known = WorldDef.ByAbbr(tag) != null ||
                         WorldDef.Pool.Exists(world => string.Equals(world.Name, tag?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!known) return $"it is tagged for world '{tag}', which is not in the overworld pool";
        }
        string levels = doc.HardshipLevels is { Count: > 0 } ? string.Join("/", doc.HardshipLevels) : "any";
        return $"worlds {string.Join("/", doc.Worlds ?? new List<string>())}, levels {levels}, goals {string.Join("/", doc.Goals ?? new List<string>())}, terrain {doc.Terrain}: nothing the overworld rolls matches all four";
    }

    /// <summary>A map's art must be a realm of a world the map is tagged for — a map built
    /// from the Wulin set and tagged HC would be dealt in the wrong world. A world may have
    /// two realms (the Palace: Loong's Nest and Cloud Court), so this asks the SET for its
    /// world, never the world for "its" set.</summary>
    private void CheckRealmBelongsToItsWorld(string name, MapDocument doc)
    {
        string built = MapRealm.BuiltFrom(doc);
        if (built == null || doc.Worlds is not { Count: > 0 } || !TileSets.TryGet(built, out TileSets.Info realm)) return;
        WorldDef world = WorldDef.ByAbbr(realm.WorldAbbr);
        bool tagged = doc.Worlds.Exists(tag =>
            string.Equals(tag?.Trim(), realm.WorldAbbr, StringComparison.OrdinalIgnoreCase) ||
            (world != null && string.Equals(tag?.Trim(), world.Name, StringComparison.OrdinalIgnoreCase)));
        Expect(tagged, $"{name}: built from the {realm.RealmName} set (world {realm.WorldAbbr}) but tagged for {string.Join("/", doc.Worlds)}");
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

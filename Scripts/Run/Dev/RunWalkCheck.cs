namespace Fableland.Run.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Fableland.Data;
using Fableland.Map;
using Fableland.MapCreation.Data;
using Fableland.MapCreation.Runtime;
using Fableland.Missions;

/// <summary>
/// Headless check that the game is playable AS A RUN, across the whole world pool
/// (40-QA.md §2, MapGDD.md §2):
///
///     Godot --headless --path . res://Scenes/Run/RunWalkCheck.tscn      # exit 0 = clean
///
/// 1. The pool — the eight worlds, each with a realm kit, a colour of its own and a boss;
///    every seed draws five of them, home first; the six worlds of older saves still
///    generate the maps those saves were made on, and today's eight still generate what
///    they did before zone 6 was dealt maps (recorded signatures). What zone 6 is dealt: the
///    same map every time for a seed, and always a map of one of that run's own worlds.
/// 2. Saves — a save from before the pool grew wakes up on its own worlds and stays there;
///    a new run records the pool it was made from.
/// 3. Every shipped map, entered — the player stands on it, the mission starts; a boss arena
///    gives its boss two perches and the boss stands on one — and the LV6 core's stand-in
///    stands on one too.
/// 4. The walk — a real run per world: three nodes, one per altitude band, each entered
///    through the Adventure handshake, played to its goal and ended with the day; then the
///    world's 4-1, on a different day for each world, where the boss's HP, its bar and its
///    damage must be the day's (FOES.gdd §2, §8.2). Cloud Court, entered through a run's own
///    high-ground Palace of LOOING node. Then zone 6: all five LV5 nodes and the core, each
///    on an authored map of one of the run's worlds, drawn VOID-touched (MapGDD §7).
///
/// It drives the real scenes (Arena, Map, RunOver) through real scene changes, so it lives
/// under the root, not as the current scene. It never touches the player's saves or maps:
/// slot files go to a scratch folder and only shipped maps are dealt.
///
/// To ask "do MY saves survive this build?", name a folder of slot files after `--`:
///
///     Godot --headless --path . res://Scenes/Run/RunWalkCheck.tscn -- --saves="/path/to/saves"
///
/// They are COPIED into the scratch folder and loaded there; every node a save mentions
/// must exist on the map it wakes up on. The originals are only ever read.
/// </summary>
public partial class RunWalkCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;

    private const int SpeedUp = 8;
    private const string Home = MapGenerator.PomegraknightHome;
    private static readonly string[] Bands = { CombatMapTerrain.Lowground, CombatMapTerrain.SeaLevel, CombatMapTerrain.High };
    private static readonly MissionType[] Ordinary = { MissionType.Collection, MissionType.Protect, MissionType.Destroy, MissionType.Slaughter };

    /// <summary>What the six-world pool generated for these seeds before v0.11.0 (recorded
    /// from v0.10.11). A save keeps only its seed, so these must never change.</summary>
    private static readonly (string Seed, string Signature)[] BeforeV4 =
    {
        ("DEBUG", "VK,BM,HC,PL,SL · 136 nodes · 179 roads · d07941a36c34c23b"),
        ("FABLELAND", "VK,PL,SL,HC,BM · 133 nodes · 172 roads · 3017c3e2eb26f66c"),
        ("PUMPKING", "VK,PL,HC,TD,BM · 136 nodes · 176 roads · 9f2bc4107d958cfe"),
        ("0", "VK,TD,SL,HC,PL · 128 nodes · 163 roads · e953f40ea8bdda5f"),
        ("ZZZZ9", "VK,BM,PL,TD,HC · 137 nodes · 184 roads · 8ee5f0c88158182a"),
        ("SAVE1234", "VK,TD,SL,PL,HC · 142 nodes · 190 roads · 831f2cf2e25b0212"),
    };

    /// <summary>What today's pool of eight generates for these seeds, recorded on v0.11.1 —
    /// before zone 6 was dealt maps and Cloud Court joined the Palace. Dealing a node its map
    /// is a new seeded choice; it must draw from a stream of its own, so none of this moves.</summary>
    private static readonly (string Seed, string Signature)[] OnThePoolOfEight =
    {
        ("DEBUG", "VK,SL,KH,HC,BM · 136 nodes · 175 roads · 1b2d03e614a32f1b"),
        ("FABLELAND", "VK,BM,KH,PL,TD · 136 nodes · 185 roads · bca640e6815966ce"),
        ("PUMPKING", "VK,TD,TF,KH,HC · 142 nodes · 184 roads · 2f63bad16832892a"),
        ("0", "VK,SL,HC,TF,TD · 128 nodes · 171 roads · 41e20de3e14752f9"),
        ("ZZZZ9", "VK,SL,TD,BM,PL · 138 nodes · 182 roads · fd0759b6bd7811b0"),
        ("SAVE1234", "VK,KH,SL,TF,BM · 135 nodes · 168 roads · f72a216207820503"),
        ("POOL5", "VK,HC,SL,KH,TF · 136 nodes · 186 roads · bf3cde17cae4928b"),
        ("WALKVOID", "VK,KH,PL,TD,HC · 137 nodes · 187 roads · af75d950503e34f8"),
    };

    public override void _Ready() => CallDeferred(MethodName.Begin);

    private async void Begin()
    {
        // Scene changes free the current scene. This node is the current scene when the
        // check is launched; giving that up keeps it alive across every change below.
        GetTree().CurrentScene = null;

        int ticksPerSecond = Engine.PhysicsTicksPerSecond;
        double timeScale = Engine.TimeScale;
        string scratch = Path.Combine(Path.GetTempPath(), "fableland-runwalk-" + Guid.NewGuid().ToString("N"));
        string savesBefore = SaveGameService.DirectoryOverride;
        try
        {
            Directory.CreateDirectory(scratch);
            SaveGameService.DirectoryOverride = scratch;      // never the player's own slots
            CombatMapCatalog.UserMapsEnabled = false;         // never the player's own maps

            CheckPool();
            CheckWhatZoneSixIsDealt();
            CheckSavesKeepTheirWorlds();

            Engine.PhysicsTicksPerSecond = ticksPerSecond * SpeedUp;
            Engine.TimeScale = SpeedUp;
            await Ticks(1);
            await EnterEveryShippedMap();
            await StandInOnEveryBossArena();
            await WalkEveryWorld();
            await WalkToTheCloudCourt();
            await WalkTheVoid();
        }
        catch (Exception e)
        {
            _problems.Add("run walk threw: " + e);
        }
        finally
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond;
            Engine.TimeScale = timeScale;
            SaveGameService.DirectoryOverride = savesBefore;
            CombatMapCatalog.UserMapsEnabled = true;
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch { /* a leftover scratch folder in the temp directory is harmless */ }
        }

        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0
            ? $"RunWalkCheck: OK ({_checks} checks, {WorldDef.Pool.Count} worlds)"
            : $"RunWalkCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private async Task Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static string M(float px) => (px / Units.PixelsPerMeter).ToString("0.0");

    // ---------------------------------------------------------------- 1. the pool

    private void CheckPool()
    {
        string[] expected = { "SL", "HC", "VK", "TD", "PL", "BM", "KH", "TF" };
        Expect(string.Join(",", WorldDef.PoolAbbrs()) == string.Join(",", expected),
            $"pool: the world pool is {string.Join(",", WorldDef.PoolAbbrs())}; it is append-only data and must be {string.Join(",", expected)}");

        float closestNode = float.MaxValue, closestLand = float.MaxValue;
        string closestNodePair = "", closestLandPair = "";
        for (int i = 0; i < WorldDef.Pool.Count; i++)
        {
            WorldDef world = WorldDef.Pool[i];
            Expect(TileSets.ForWorldAbbr(world.Abbr) != null, $"pool: {world.Abbr} ({world.Name}) has no realm tile set");
            Expect(MapRenderModel.ThemeFor(world.Abbr).Abbr == world.Abbr, $"pool: {world.Abbr} ({world.Name}) has no land colour on the atlas");
            Expect(BossTable.ForWorld(world.Abbr) != null, $"pool: {world.Abbr} ({world.Name}) fields no boss at its 4-1");
            for (int j = i + 1; j < WorldDef.Pool.Count; j++)
            {
                WorldDef other = WorldDef.Pool[j];
                float node = Distance(world.Color, other.Color);
                float land = Distance(MapRenderModel.ThemeFor(world.Abbr).Land, MapRenderModel.ThemeFor(other.Abbr).Land);
                if (node < closestNode) { closestNode = node; closestNodePair = world.Abbr + "/" + other.Abbr; }
                if (land < closestLand) { closestLand = land; closestLandPair = world.Abbr + "/" + other.Abbr; }
            }
        }
        Expect(closestNode > 0.20f, $"pool: {closestNodePair} have nearly the same city colour ({closestNode:0.00} apart); two realms must not look alike on the map");
        Expect(closestLand > 0.12f, $"pool: {closestLandPair} have nearly the same land colour ({closestLand:0.00} apart)");

        // Old saves: the six-world pool must generate exactly what it did.
        List<WorldDef> six = WorldDef.Resolve(SaveGameService.WorldPoolBeforeV4);
        foreach ((string seed, string signature) in BeforeV4)
        {
            string now = Signature(MapGenerator.Generate(seed, Home, six));
            Expect(now == signature, $"pool: seed '{seed}' on the six-world pool now generates '{now}'; a save from before v0.11.0 was made on '{signature}'");
        }

        // Today's runs: the pool of eight must generate exactly what it did, too — same
        // realms, same nodes, same missions, same roads.
        foreach ((string seed, string signature) in OnThePoolOfEight)
        {
            string now = Signature(MapGenerator.Generate(seed));
            Expect(now == signature, $"pool: seed '{seed}' now generates '{now}'; on v0.11.1 it generated '{signature}' — something new is drawing from the overworld's streams");
        }

        // New runs: five realms from the eight, home first, and every world gets drawn.
        var drawn = new Dictionary<string, int>();
        string newcomers = null;                     // a seed to type in to see both new worlds
        for (int i = 0; i < 60; i++)
        {
            MapGraph graph = MapGenerator.Generate("POOL" + i);
            if (newcomers == null && graph.Worlds.Exists(w => w.Abbr == "KH") && graph.Worlds.Exists(w => w.Abbr == "TF"))
                newcomers = $"POOL{i} ({string.Join(",", graph.Worlds.ConvertAll(w => w.Abbr))})";
            var seen = new HashSet<string>();
            bool fromPool = true;
            foreach (WorldDef world in graph.Worlds)
            {
                fromPool &= WorldDef.ByAbbr(world.Abbr) != null && seen.Add(world.Abbr);
                drawn[world.Abbr] = drawn.GetValueOrDefault(world.Abbr) + 1;
            }
            Expect(graph.Worlds.Count == MapGenTable.RealmCount && fromPool && graph.Worlds[0].Abbr == Home,
                $"pool: seed 'POOL{i}' drew {string.Join(",", graph.Worlds.ConvertAll(w => w.Abbr))}; a run is five different pool worlds, home first");

            foreach (WorldDef world in graph.Worlds)
            {
                int capitals = 0;
                bool banded = true;
                foreach (MapNode node in graph.Nodes)
                {
                    if (node.Zone != world.Abbr || !node.IsCombat) continue;
                    if (node.Level == 4) capitals += node.Kind == NodeKind.Boss && node.Mission == MissionType.Boss ? 1 : 100;
                    banded &= Array.IndexOf(Bands, node.Terrain) >= 0;
                }
                if (capitals != 1 || !banded)
                    _problems.Add($"pool: seed 'POOL{i}', realm {world.Abbr}: {capitals} capital(s), terrain labels valid: {banded}");
            }
        }
        _checks++;
        var never = new List<string>();
        foreach (WorldDef world in WorldDef.Pool)
            if (!drawn.ContainsKey(world.Abbr)) never.Add(world.Abbr);
        Expect(never.Count == 0, $"pool: sixty seeds never drew {string.Join(", ", never)}");

        var tally = new List<string>();
        foreach (WorldDef world in WorldDef.Pool) tally.Add($"{world.Abbr} {drawn.GetValueOrDefault(world.Abbr)}");
        GD.Print($"  run      the pool       {WorldDef.Pool.Count} worlds, five per run, home ({Home}) first · drawn in 60 seeds: {string.Join(" · ", tally)} · closest colours {closestNodePair} ({closestNode:0.00}), land {closestLandPair} ({closestLand:0.00}) · a seed with both new worlds: {newcomers ?? "none in sixty"}");
    }

    private static float Distance(Color a, Color b) =>
        Mathf.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));

    /// <summary>
    /// Zone 6 wears the run's worlds (MapGDD §7). For a seed, each of its six combat nodes is
    /// dealt the same map every time it is asked — whatever order the run's worlds are handed
    /// over in — and always a map of one of THAT run's five worlds, for the node's own
    /// mission: a boss arena for the core, never one for anything else.
    /// </summary>
    private void CheckWhatZoneSixIsDealt()
    {
        const int Seeds = 12;
        var worn = new Dictionary<string, int>();
        var maps = new HashSet<string>();
        int nodes = 0;
        for (int i = 0; i < Seeds; i++)
        {
            string seed = "POOL" + i;
            MapGraph graph = MapGenerator.Generate(seed);
            List<string> worlds = graph.Worlds.ConvertAll(world => world.Abbr);
            var reversed = new List<string>(worlds);
            reversed.Reverse();

            foreach (MapNode node in graph.Nodes)
            {
                if (node.WorldIndex != -1 || !node.IsCombat) continue;
                nodes++;
                string where = $"zone 6: seed '{seed}' {node.Id} ({node.Mission})";
                string goal = CombatMapCatalog.GoalFor(node.Mission);
                CombatMapSelection dealt = CombatMapCatalog.SelectForVoid(seed, node.Id, worlds, node.Mission);
                Expect(dealt != null, $"{where}: was dealt no map and would open the legacy arena");
                if (dealt == null) continue;

                CombatMapSelection again = CombatMapCatalog.SelectForVoid(seed, node.Id, reversed, node.Mission);
                Expect(again != null && again.AbsolutePath == dealt.AbsolutePath && again.World == dealt.World,
                    $"{where}: was dealt '{dealt.Document.Name}' and then '{again?.Document.Name ?? "nothing"}'; for a seed the choice must be the same every time, whatever order its worlds are listed in");
                Expect(worlds.Contains(dealt.World) && dealt.Document.Worlds.Contains(dealt.World),
                    $"{where}: was dealt '{dealt.Document.Name}' (worlds {string.Join("/", dealt.Document.Worlds)}) as '{dealt.World}'; this run's worlds are {string.Join(",", worlds)}");
                Expect(dealt.Document.Goals.Contains(goal),
                    $"{where}: was dealt '{dealt.Document.Name}' (goals {string.Join("/", dealt.Document.Goals)}), which is not a '{goal}' map");
                worn[dealt.World] = worn.GetValueOrDefault(dealt.World) + 1;
                maps.Add(dealt.Document.Id);
            }
        }

        Expect(nodes == Seeds * 6, $"zone 6: {Seeds} seeds hold {nodes} zone-6 combat nodes; each has five LV5 nodes and a core");
        var never = new List<string>();
        var tally = new List<string>();
        foreach (WorldDef world in WorldDef.Pool)
        {
            if (!worn.ContainsKey(world.Abbr)) never.Add(world.Abbr);
            tally.Add($"{world.Abbr} {worn.GetValueOrDefault(world.Abbr)}");
        }
        Expect(never.Count == 0, $"zone 6: across {Seeds} seeds it never wore {string.Join(", ", never)}");
        GD.Print($"  run      zone 6 dealt   {nodes} zone-6 nodes of {Seeds} seeds: each dealt the same map twice, a map of one of its run's own five worlds, for its mission · {maps.Count} different maps · worn: {string.Join(" · ", tally)}");
    }

    /// <summary>What a save relies on: the realms, and every node's id, kind, level and
    /// mission, and how many roads there are. Positions are left out on purpose — they are
    /// floats, and this must read the same on every machine.</summary>
    public static string Signature(MapGraph graph)
    {
        var nodes = new List<MapNode>(graph.Nodes);
        nodes.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        ulong hash = 14695981039346656037UL;
        void Mix(string text)
        {
            foreach (char c in text) { hash ^= c; hash *= 1099511628211UL; }
            hash ^= '|';
            hash *= 1099511628211UL;
        }
        var worlds = new List<string>();
        foreach (WorldDef world in graph.Worlds) { worlds.Add(world.Abbr); Mix(world.Abbr); }
        foreach (MapNode node in nodes)
        {
            Mix(node.Id);
            Mix(node.Kind.ToString());
            Mix(node.Level.ToString());
            Mix(node.Mission.ToString());
        }
        Mix(graph.Edges.Count.ToString());
        return $"{string.Join(",", worlds)} · {nodes.Count} nodes · {graph.Edges.Count} roads · {hash:x16}";
    }

    // ---------------------------------------------------------------- 2. saves

    private void CheckSavesKeepTheirWorlds()
    {
        RunState run = RunState.Instance;
        Expect(run != null, "saves: there is no RunState autoload");
        if (run == null) return;

        // A v3 DTO gains the pool of its day.
        var old = new RunSaveData { Version = 3, Seed = "DEBUG" };
        bool migrated = SaveGameService.Migrate(old, out string error);
        Expect(migrated && old.Version == SaveGameService.CurrentVersion
            && string.Join(",", old.WorldPool) == string.Join(",", SaveGameService.WorldPoolBeforeV4),
            $"saves: a version-3 save must migrate to the six-world pool (got '{string.Join(",", old.WorldPool ?? new List<string>())}', {error})");

        // A v3 FILE, as it sits in a player's slot: loads onto the map it was saved on,
        // and stays on it when written again.
        string slotTwo = Path.Combine(SaveGameService.DirectoryOverride, "slot_2.json");
        File.WriteAllText(slotTwo, "{\"version\":3,\"seed\":\"DEBUG\",\"day\":4,\"stamina\":3,\"map\":{\"devouredNodeIds\":[]},"
            + "\"owned\":[{\"id\":\"Pixolotl\",\"hpRatio\":1}],\"activeBuild\":[\"Pixolotl\"]}");
        bool loaded = run.TryLoadRunFromSlot(1, out error);
        Expect(loaded, "saves: a version-3 slot file does not load: " + error);
        if (loaded)
        {
            Expect(Signature(run.Graph) == BeforeV4[0].Signature,
                $"saves: a version-3 save of seed DEBUG woke up on '{Signature(run.Graph)}', not the map it was saved on ('{BeforeV4[0].Signature}')");
            Expect(run.Day == 4, $"saves: the loaded run is on day {run.Day}, not 4");
            Expect(run.SaveActiveRun(out error), "saves: the migrated run does not save: " + error);
            string written = File.ReadAllText(slotTwo);
            Expect(written.Contains("\"version\": 4") && written.Contains("\"worldPool\"") && !written.Contains("\"KH\""),
                "saves: a migrated save must be written as version 4 with its own six-world pool");
            Expect(run.TryLoadRunFromSlot(1, out error) && Signature(run.Graph) == BeforeV4[0].Signature,
                "saves: a migrated save moved to other worlds when loaded a second time");
        }

        CheckSavesFrom(run);

        // A new run records the pool of today and comes back on the same map. The seed is
        // one that draws both new worlds: a run that forgot its pool could not come back.
        bool started = run.StartNewRunInSlot(0, "POOL5", out error);
        Expect(started, "saves: a new run does not start in a slot: " + error);
        if (started)
        {
            string made = Signature(run.Graph);
            Expect(string.Join(",", run.WorldPool) == string.Join(",", WorldDef.PoolAbbrs()),
                $"saves: a new run recorded the pool '{string.Join(",", run.WorldPool)}'");
            Expect(run.TryLoadRunFromSlot(0, out error) && Signature(run.Graph) == made,
                $"saves: a new run did not come back on the map it was made on ({error})");
            Expect(made.Contains("KH") && made.Contains("TF"), $"saves: seed POOL5 is meant to draw both new worlds; it drew {made.Split(' ')[0]}");
            GD.Print($"  run      saves          a version-3 save of DEBUG wakes up on {BeforeV4[0].Signature.Split(' ')[0]} · a new run records all {run.WorldPool.Count} worlds · POOL5 drew {made.Split(' ')[0]} and came back on it");
        }
    }

    /// <summary>`-- --saves=dir`: real slot files, copied and loaded in the scratch folder.</summary>
    private void CheckSavesFrom(RunState run)
    {
        string source = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--saves=", StringComparison.Ordinal)) source = arg.Substring("--saves=".Length).Trim('"');
        if (string.IsNullOrWhiteSpace(source)) return;
        Expect(Directory.Exists(source), $"saves: '{source}' is not a folder");
        if (!Directory.Exists(source)) return;

        for (int slot = 0; slot < SaveGameService.SlotCount; slot++)
        {
            string name = $"slot_{slot + 1}.json";
            string original = Path.Combine(source, name);
            if (!File.Exists(original)) continue;
            File.Copy(original, Path.Combine(SaveGameService.DirectoryOverride, name), overwrite: true);

            bool read = SaveGameService.TryRead(slot, out RunSaveData save, out string error);
            Expect(read, $"saves: {name} does not read: {error}");
            if (!read) continue;
            bool loaded = run.TryLoadRunFromSlot(slot, out error);
            Expect(loaded, $"saves: {name} (seed {save.Seed}) does not load: {error}");
            if (!loaded) continue;

            var mentioned = new List<string>();
            mentioned.AddRange(save.VisitedNodeIds ?? new List<string>());
            mentioned.AddRange(save.CompletedNodeIds ?? new List<string>());
            mentioned.AddRange(save.MundaneShelterIds ?? new List<string>());
            mentioned.AddRange(save.ResolvedEventIds ?? new List<string>());
            mentioned.AddRange(save.Map?.DevouredNodeIds ?? new List<string>());
            if (!string.IsNullOrEmpty(save.CurrentNodeId)) mentioned.Add(save.CurrentNodeId);
            if (!string.IsNullOrEmpty(save.PreviousNodeId)) mentioned.Add(save.PreviousNodeId);
            var lost = new List<string>();
            foreach (string id in mentioned)
                if (run.FindNode(id) == null && !lost.Contains(id)) lost.Add(id);
            Expect(lost.Count == 0, $"saves: {name} (seed {save.Seed}) mentions {lost.Count} node(s) its map no longer has — e.g. {string.Join(", ", lost.GetRange(0, Math.Min(4, lost.Count)))}");
            Expect(string.IsNullOrEmpty(save.CurrentNodeId) || run.CurrentNodeId == save.CurrentNodeId,
                $"saves: {name} was saved standing on {save.CurrentNodeId} and wakes up on {run.CurrentNodeId}");
            GD.Print($"  run      your save      {name}: seed {save.Seed}, day {save.Day} — wakes up on {string.Join(",", run.Graph.Worlds.ConvertAll(w => w.Abbr))}, standing on {run.CurrentNodeId}; all {mentioned.Count} nodes it mentions exist");
            File.Delete(Path.Combine(SaveGameService.DirectoryOverride, name));
        }
    }

    // ---------------------------------------------------------------- 3. every shipped map

    private static CharacterController Player(SceneTree tree) =>
        tree.GetFirstNodeInGroup(CombatTargets.PlayerGroup) as CharacterController;

    private static CharacterController BossOf(SceneTree tree)
    {
        foreach (Node node in tree.GetNodesInGroup(CombatTargets.EnemyGroup))
            if (node is CharacterController { Side: CombatSide.Foe } boss && !boss.IsDefeated) return boss;
        return null;
    }

    /// <summary>The player is somewhere a fight can happen: inside the map and not falling
    /// through it. (Undersea, a body sinks slowly to the floor; it is never "falling out".)</summary>
    private void ExpectPlayerStands(string where, GameManager arena, CharacterController player)
    {
        Expect(player != null && !player.IsDefeated, $"{where}: there is no living player");
        if (player == null) return;
        Vector2 size = arena.AuthoredMapSize;
        Vector2 at = player.GlobalPosition;
        Expect(at.X > 0f && at.X < size.X && at.Y > 0f && at.Y < size.Y,
            $"{where}: the player is at ({M(at.X)}, {M(at.Y)}) m, outside the {M(size.X)} × {M(size.Y)} m map");
        Expect(player.IsOnFloor() || Mathf.Abs(player.Velocity.Y) < Units.Px(4f),
            $"{where}: the player is falling at {M(player.Velocity.Y)} m/s a second after entering");
    }

    private async Task EnterEveryShippedMap()
    {
        string directory = ProjectSettings.GlobalizePath("res://Maps");
        string[] paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        int entered = 0, arenas = 0, fought = 0;
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn");

        for (int i = 0; i < paths.Length; i++)
        {
            MapDocument doc = CombatMapCatalog.LoadDocument(paths[i]);
            string name = Path.GetFileName(paths[i]);
            Expect(doc != null, $"maps: {name} does not load");
            if (doc == null) continue;
            bool bossMap = doc.Goals != null && doc.Goals.Contains(CombatMapGoals.Boss);
            // A map that serves bosses and ordinary missions is entered as a boss fight.
            MissionType mission = bossMap ? MissionType.Boss : Ordinary[i % Ordinary.Length];

            var arena = scene.Instantiate<GameManager>();
            arena.DebugMission = mission;
            arena.DebugNodeLevel = bossMap ? 4 : doc.HardshipLevels is { Count: > 0 } ? doc.HardshipLevels[0] : 1 + i % 3;
            arena.DebugBossId = bossMap ? BossTable.StandIn.Id : "";
            arena.DebugCombatMapPath = paths[i];
            arena.PreCombatCountdownSeconds = 0.2f;
            AddChild(arena);
            await Ticks(70);

            string where = $"maps: {name} ({mission})";
            Expect(arena.UsesAuthoredMap, $"{where}: the arena fell back to the legacy stage");
            Expect(arena.CurrentMission is { Status: MissionStatus.Running }, $"{where}: the mission is not running a second in");
            ExpectPlayerStands(where, arena, Player(GetTree()));
            if (bossMap)
            {
                arenas++;
                CharacterController boss = BossOf(GetTree());
                Expect(arena.LevelGoalPoints.Count >= 2, $"{where}: a boss arena needs two perches (Level Goal markers); it has {arena.LevelGoalPoints.Count}");
                Expect(boss != null, $"{where}: no boss was staged");
                if (boss != null)
                {
                    float off = float.MaxValue;
                    foreach (Vector2 perch in arena.LevelGoalPoints) off = Mathf.Min(off, boss.GlobalPosition.DistanceTo(perch));
                    Expect(off < Units.Px(1.5f) && boss.IsOnFloor(), $"{where}: the boss is {M(off)} m from its nearest perch, on the floor: {boss.IsOnFloor()}");
                    if (arena.LevelGoalPoints.Count >= 2)
                    {
                        float apart = arena.LevelGoalPoints[0].DistanceTo(arena.LevelGoalPoints[1]);
                        Expect(apart < Units.Px(21f), $"{where}: its two perches are {M(apart)} m apart; a Soul Form flight covers about 20 m with the climb");
                    }

                    // And he fights there: with a player who only stands, he must get a head
                    // away within half a minute, on every realm's arena — the sea included.
                    CharacterController hero = Player(GetTree());
                    if (hero != null) hero.Invincible = true;
                    bool threw = false;
                    for (int t = 0; t < 60 * 30 && !threw && GodotObject.IsInstanceValid(boss); t++)
                    {
                        await Ticks(1);
                        threw = boss is PumpKing { HasLiveHead: true };
                    }
                    Expect(threw, $"{where}: in 30 s the boss never threw his head");
                    if (GodotObject.IsInstanceValid(boss))
                    {
                        Vector2 size = arena.AuthoredMapSize, at = boss.GlobalPosition;
                        Expect(at.X > 0f && at.X < size.X && at.Y > 0f && at.Y < size.Y, $"{where}: the boss ended up at ({M(at.X)}, {M(at.Y)}) m, outside the map");
                    }
                    if (threw) fought++;
                }
            }
            entered++;
            arena.QueueFree();
            await Ticks(3);
        }
        GD.Print($"  run      every map      {entered} shipped maps entered: the player stands and the mission runs on each · {arenas} boss arenas each stage the boss on a perch, and on {fought} of them he gets a head away at a player who only stands");
    }

    /// <summary>The LV6 core's boss: still the stand-in crab (FOES.gdd §8.1).</summary>
    private static BossCrab StandIn(SceneTree tree)
    {
        foreach (string group in new[] { CombatTargets.EnemyGroup, "foe" })
            foreach (Node node in tree.GetNodesInGroup(group))
                if (node is BossCrab crab && GodotObject.IsInstanceValid(crab) && !crab.IsQueuedForDeletion()) return crab;
        return null;
    }

    /// <summary>
    /// The LV6 core is dealt a boss arena of one of the run's worlds (MapGDD §7), and its boss
    /// is still the stand-in crab — which until zone 6 was dealt maps had only ever stood in
    /// the legacy arena. Any of the boss arenas can be the core's: on each one the stand-in
    /// must be staged on a perch, come to rest on a floor, and stay inside the map.
    /// </summary>
    private async Task StandInOnEveryBossArena()
    {
        string directory = ProjectSettings.GlobalizePath("res://Maps");
        string[] paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Arena.tscn");
        int arenas = 0, stood = 0;

        foreach (string path in paths)
        {
            MapDocument doc = CombatMapCatalog.LoadDocument(path);
            if (doc?.Goals == null || !doc.Goals.Contains(CombatMapGoals.Boss)) continue;
            arenas++;
            string where = $"maps: {Path.GetFileName(path)} (the core's stand-in)";

            var arena = scene.Instantiate<GameManager>();
            arena.DebugMission = MissionType.Boss;
            arena.DebugNodeLevel = 6;
            arena.DebugBossId = "";                       // the core names no boss: the stand-in is fielded
            arena.DebugCombatMapPath = path;
            arena.PreCombatCountdownSeconds = 0.2f;
            AddChild(arena);

            BossCrab crab = null;
            for (int t = 0; t < 240 && crab == null; t++)
            {
                await Ticks(1);
                crab = StandIn(GetTree());
            }
            Expect(arena.UsesAuthoredMap && arena.CurrentMission is BossMission { IsFinalBoss: true },
                $"{where}: the arena is not a final-boss fight on the authored map");
            Expect(crab != null, $"{where}: no stand-in boss was staged");
            if (crab != null)
            {
                float off = float.MaxValue;
                foreach (Vector2 perch in arena.LevelGoalPoints) off = Mathf.Min(off, crab.GlobalPosition.DistanceTo(perch));
                Expect(off < Units.Px(2.5f), $"{where}: the stand-in was staged {M(off)} m from the nearest perch");

                bool rests = false;
                for (int t = 0; t < 60 * 6 && !rests && GodotObject.IsInstanceValid(crab); t++)
                {
                    await Ticks(1);
                    rests = crab.IsOnFloor();
                }
                Vector2 size = arena.AuthoredMapSize, at = GodotObject.IsInstanceValid(crab) ? crab.GlobalPosition : Vector2.Zero;
                bool inside = at.X > 0f && at.X < size.X && at.Y > 0f && at.Y < size.Y;
                Expect(rests && inside, $"{where}: the stand-in is at ({M(at.X)}, {M(at.Y)}) m of a {M(size.X)} × {M(size.Y)} m map, on a floor: {rests}");
                if (rests && inside) stood++;
            }
            arena.QueueFree();
            await Ticks(3);
        }
        GD.Print($"  run      the stand-in   on {stood} of {arenas} boss arenas the core's stand-in crab is staged on a perch and stands inside the map");
    }

    // ---------------------------------------------------------------- 4. the walk

    private async Task<T> SceneIs<T>(int budgetTicks) where T : Node
    {
        for (int i = 0; i < budgetTicks; i++)
        {
            if (GetTree().CurrentScene is T scene && scene.IsNodeReady()) return scene;
            await Ticks(1);
        }
        return null;
    }

    /// <summary>A seed whose run includes this world (the home world is in every run).</summary>
    private static string SeedWith(string abbr)
    {
        for (int i = 0; i < 200; i++)
        {
            string seed = $"WALK{abbr}{i}";
            foreach (WorldDef world in MapGenerator.Generate(seed).Worlds)
                if (world.Abbr == abbr) return seed;
        }
        return null;
    }

    /// <summary>Enters a node the way the map screen does and waits for its arena.</summary>
    private async Task<GameManager> Enter(string where, MapNode node)
    {
        RunState run = RunState.Instance;
        run.BeginAdventure(node.Id);
        // The scene swap is deferred: wait for an arena that belongs to THIS adventure.
        GameManager arena = null;
        for (int i = 0; i < 600 && arena == null; i++)
        {
            await Ticks(1);
            if (GetTree().CurrentScene is GameManager current && current.IsNodeReady()
                && run.CurrentAdventure?.NodeId == node.Id && current.CurrentMission != null
                && current != _lastArena) arena = current;
        }
        Expect(arena != null, $"{where}: entering {node.Id} never reached the arena");
        _lastArena = arena ?? _lastArena;
        return arena;
    }

    private GameManager _lastArena;

    private async Task WalkEveryWorld()
    {
        RunState run = RunState.Instance;
        // One day band per world, so the boss is met on every band of FOES.gdd §2.
        int[] bossDays = { 1, 11, 21, 31, 36, 41, 45, 8 };
        var bossLines = new List<string>();
        int nodesWalked = 0, stopsWalked = 0;

        for (int w = 0; w < WorldDef.Pool.Count; w++)
        {
            WorldDef world = WorldDef.Pool[w];
            string seed = SeedWith(world.Abbr);
            Expect(seed != null, $"walk: no seed among 200 draws {world.Abbr}");
            if (seed == null) continue;
            run.NewRun(seed);

            var cities = new List<MapNode>();
            MapNode capital = null;
            foreach (MapNode node in run.Graph.Nodes)
            {
                if (node.Zone != world.Abbr || !node.IsCombat) continue;
                if (node.Level == 4) capital = node;
                else if (node.Level <= 3) cities.Add(node);
            }
            cities.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Expect(cities.Count >= Bands.Length && capital != null, $"walk: {world.Abbr} has {cities.Count} cities and {(capital == null ? "no" : "a")} capital");
            if (cities.Count < Bands.Length || capital == null) continue;

            // Three cities, one per altitude band. The overworld hands out bands from its
            // height field and missions from a roll; a seed may not hold every pair, so the
            // node is given the band and the mission this step is about.
            for (int b = 0; b < Bands.Length; b++)
            {
                MapNode node = cities[b * (cities.Count - 1) / (Bands.Length - 1)];   // first, middle, last: levels differ
                node.Terrain = Bands[b];
                node.Mission = Ordinary[(w + b) % Ordinary.Length];
                int day = run.Day;
                string where = $"walk: {world.Abbr} {node.Id} ({Bands[b]}, {node.Mission}, day {day})";

                GameManager arena = await Enter(where, node);
                if (arena == null) continue;
                await Ticks(60 * 4);                                             // past the 3 s countdown

                AdventureContext adventure = run.CurrentAdventure;
                MapDocument dealt = CombatMapCatalog.LoadDocument(adventure?.CombatMapPath);
                Expect(dealt != null && arena.UsesAuthoredMap, $"{where}: no shipped map was dealt (the legacy stage opened)");
                if (dealt != null)
                {
                    string terrain = (dealt.Terrain ?? "").Trim();
                    Expect(dealt.Worlds.Contains(world.Abbr) && (terrain == "*" || terrain == Bands[b]),
                        $"{where}: was dealt '{dealt.Name}' (worlds {string.Join(",", dealt.Worlds)}, terrain {terrain})");
                }
                Expect(arena.FoeLevel == FoeStats.LevelForDay(day), $"{where}: foe level {arena.FoeLevel}, the day gives {FoeStats.LevelForDay(day)}");
                Expect(arena.CurrentMission is { Status: MissionStatus.Running }, $"{where}: the mission is not running");
                ExpectPlayerStands(where, arena, Player(GetTree()));

                arena.CurrentMission.DebugForceComplete();
                for (int i = 0; i < 300 && !arena.GoalResolved; i++) await Ticks(1);
                Expect(arena.GoalResolved && run.CompletedNodeIds.Contains(node.Id), $"{where}: reaching the goal did not complete the node");

                run.EndDay();
                MapController map = await SceneIs<MapController>(600);
                Expect(map != null && !run.RunFinished, $"{where}: ending the day did not return to the map (run finished: {run.RunFinished})");
                Expect(run.Day == day + 1, $"{where}: the day went from {day} to {run.Day}");
                Expect(File.Exists(Path.Combine(SaveGameService.DirectoryOverride, "slot_1.json")), $"{where}: the day end wrote no checkpoint");
                nodesWalked++;
                if (map == null) break;
            }

            // The stops between fights: a Transportation Hub and an event, in this world.
            foreach ((NodeKind kind, string what) in new[] { (NodeKind.TransportHub, "hub"), (NodeKind.Event, "event") })
            {
                MapNode stop = null;
                foreach (MapNode node in run.Graph.Nodes)
                    if (node.Zone == world.Abbr && node.Kind == kind && (stop == null || string.CompareOrdinal(node.Id, stop.Id) < 0)) stop = node;
                Expect(stop != null, $"walk: {world.Abbr} has no {what}");
                if (stop == null) continue;
                run.BeginAdventure(stop.Id);
                Node scene = kind == NodeKind.TransportHub ? await SceneIs<ShelterController>(600) : await SceneIs<EventController>(600);
                Expect(scene != null, $"walk: {world.Abbr} {stop.Id}: entering the {what} did not open its scene");
                run.ReturnToMap();
                Expect(await SceneIs<MapController>(600) != null, $"walk: {world.Abbr} {stop.Id}: leaving the {what} did not return to the map");
                stopsWalked++;
            }

            // The capital: this world's boss, met on its own day band.
            {
                run.Day = bossDays[w];
                FoeStats.LevelStats level = FoeStats.ForLevel(FoeStats.LevelForDay(run.Day));
                BossDef def = BossTable.ForWorld(world.Abbr);
                capital.Terrain = Bands[w % Bands.Length];
                string where = $"walk: {world.Abbr} {capital.Id} (boss, day {run.Day})";

                GameManager arena = await Enter(where, capital);
                if (arena == null) continue;
                var mission = arena.CurrentMission as BossMission;
                for (int i = 0; i < 600 && mission is { FieldsRealBoss: false }; i++) await Ticks(1);
                await Ticks(40);
                CharacterController boss = BossOf(GetTree());

                MapDocument dealt = CombatMapCatalog.LoadDocument(run.CurrentAdventure?.CombatMapPath);
                Expect(dealt != null && dealt.Goals.Contains(CombatMapGoals.Boss) && dealt.Worlds.Contains(world.Abbr),
                    $"{where}: the boss was dealt '{dealt?.Name ?? "no map"}', not this world's boss arena");
                Expect(arena.BossId == def.Id && mission is { FieldsRealBoss: true } && boss != null,
                    $"{where}: the 4-1 must field {def.DisplayName} (arena names '{arena.BossId}', staged: {boss != null})");
                Expect(arena.FoeLevel == level.Level, $"{where}: foe level {arena.FoeLevel}, the day gives {level.Level}");
                if (boss != null && mission != null)
                {
                    float hp = def.BaseHp * level.HpMult;
                    float damage = def.DamageScale * level.AtkMult;
                    Expect(Mathf.IsEqualApprox(boss.MaxHP, hp) && Mathf.IsEqualApprox(boss.CurrentHP, hp),
                        $"{where}: the boss has {boss.CurrentHP:0}/{boss.MaxHP:0} HP; its row gives {def.BaseHp:0} × {level.HpMult} = {hp:0} at foe level {level.Level}");
                    Expect(Mathf.IsEqualApprox(mission.SecondaryMax, boss.MaxHP) && Mathf.IsEqualApprox(mission.SecondaryValue, boss.CurrentHP),
                        $"{where}: the boss bar reads {mission.SecondaryValue:0}/{mission.SecondaryMax:0}, the boss has {boss.CurrentHP:0}/{boss.MaxHP:0}");
                    Expect(Mathf.IsEqualApprox(boss.DamageDealtScale, damage),
                        $"{where}: the boss deals ×{boss.DamageDealtScale:0.00}; its row gives {def.DamageScale} × {level.AtkMult} = ×{damage:0.00}");
                    Expect(Mathf.IsEqualApprox(mission.TimeRemaining, MissionTable.BossTimeLimit(4), 2f),
                        $"{where}: the boss timer reads {mission.TimeRemaining:0} s; an LV4 boss is {MissionTable.BossTimeLimit(4):0} s");
                    ExpectPlayerStands(where, arena, Player(GetTree()));
                    bossLines.Add($"{world.Abbr} day {run.Day} L{level.Level}: {boss.MaxHP:0} HP ×{boss.DamageDealtScale:0.00}");

                    // Beating him: the node completes, the first capital of the home realm
                    // yields TwistedReality, and PumpKing is on the roster exactly once.
                    bool owned = run.FindProtagonist(def.ProtagonistId) != null;
                    int roster = run.Owned.Count;
                    arena.CurrentMission.DebugForceComplete();
                    for (int i = 0; i < 300 && !arena.GoalResolved; i++) await Ticks(1);
                    Expect(arena.GoalResolved && run.CompletedNodeIds.Contains(capital.Id), $"{where}: beating the boss did not complete the capital");
                    Expect(run.FindProtagonist(def.ProtagonistId) != null && run.Owned.Count == roster + (owned ? 0 : 1),
                        $"{where}: {def.ProtagonistId} must be on the roster once (was owned: {owned}; roster {roster} → {run.Owned.Count})");
                    if (world.Abbr == Home)
                        Expect(run.HasTwistedReality(), $"{where}: the home realm's capital must yield TwistedReality");
                }
                nodesWalked++;
            }
        }

        GD.Print($"  run      the walk       {nodesWalked} fights entered through the run across {WorldDef.Pool.Count} worlds — each altitude band and each capital — and played to their goal · {stopsWalked} hubs and events opened");
        GD.Print($"  run      boss by day    {string.Join(" · ", bossLines)}");
    }

    // ---------------------------------------------------------------- 5. Cloud Court

    private const string CloudCourtMapId = "cloud_court_heavens_pl";

    /// <summary>
    /// Cloud Court is the Palace of LOOING's sky district (style bible §7C): a Palace node on
    /// high ground may be dealt it. This finds a run with such a node — its own band, its own
    /// mission, nothing written onto it — and goes in the way the map screen does.
    /// </summary>
    private async Task WalkToTheCloudCourt()
    {
        RunState run = RunState.Instance;
        string seed = null, nodeId = null;
        for (int i = 0; i < 300 && seed == null; i++)
        {
            string candidate = "COURT" + i;
            MapGraph graph = MapGenerator.Generate(candidate);
            if (!graph.Worlds.Exists(world => world.Abbr == "PL")) continue;
            List<MapNode> high = graph.Nodes.FindAll(node =>
                node.Zone == "PL" && node.IsCombat && node.Level <= 3 && node.Terrain == CombatMapTerrain.High);
            high.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (MapNode node in high)
            {
                CombatMapSelection dealt = CombatMapCatalog.Select(candidate, node.Id, node.Zone, node.Level, node.Mission, node.Terrain);
                if (dealt?.Document.Id != CloudCourtMapId) continue;
                seed = candidate;
                nodeId = node.Id;
                break;
            }
        }
        Expect(seed != null, "cloud court: in 300 seeds no high-ground Palace of LOOING node is dealt Cloud Court; no run can open it");
        if (seed == null) return;

        run.NewRun(seed);
        MapNode city = run.FindNode(nodeId);
        string where = $"cloud court: seed '{seed}' {nodeId} ({city.Terrain}, {city.Mission})";
        GameManager arena = await Enter(where, city);
        if (arena == null) return;
        await Ticks(60 * 4);                                                 // past the 3 s countdown

        AdventureContext adventure = run.CurrentAdventure;
        MapDocument opened = CombatMapCatalog.LoadDocument(adventure?.CombatMapPath);
        Expect(opened?.Id == CloudCourtMapId && arena.UsesAuthoredMap, $"{where}: the node opened '{opened?.Name ?? "the legacy stage"}', not Cloud Court");
        Expect(MapRealm.Of(opened, "PL") == TileSets.PalaceOfLoong, $"{where}: the map it opened is not built from the Cloud Court set");
        Expect(adventure is { VoidTouched: false } && !VoidTreatment.IsOn(arena.GetNodeOrNull<ColorRect>("Background")),
            $"{where}: a fight in an outer world must not be VOID-touched");
        Expect(arena.CurrentMission is { Status: MissionStatus.Running }, $"{where}: the mission is not running");
        ExpectPlayerStands(where, arena, Player(GetTree()));

        arena.CurrentMission.DebugForceComplete();
        for (int i = 0; i < 300 && !arena.GoalResolved; i++) await Ticks(1);
        Expect(arena.GoalResolved && run.CompletedNodeIds.Contains(nodeId), $"{where}: reaching the goal did not complete the node");

        // High ground only: the same node on either other band is never dealt it.
        foreach (string band in Bands)
        {
            if (band == CombatMapTerrain.High) continue;
            for (int level = 1; level <= 3; level++)
                Expect(!CombatMapCatalog.Candidates("PL", level, CombatMapCatalog.GoalFor(city.Mission), band, includeUserMaps: false)
                        .Exists(map => map.Document.Id == CloudCourtMapId),
                    $"cloud court: a Palace of LOOING LV{level} node on {band} could be dealt Cloud Court; it belongs to the high ground");
        }
        GD.Print($"  run      cloud court    entered through the run: seed {seed}, {nodeId} — the Palace of LOOING, {city.Terrain}, {city.Mission} — and played to its goal");
    }

    // ---------------------------------------------------------------- 6. the void

    /// <summary>Counts the drawn pieces under a node — all of them, and those wearing the
    /// VOID treatment — with hazard art (its own group) tallied apart.</summary>
    private static void TallyTreatment(Node node, bool hazard, ref int art, ref int drained, ref int hazardArt, ref int hazardDrained)
    {
        if (node == null) return;
        hazard |= node.IsInGroup(MapVisualBuilder.HazardArtGroup);
        if (node is Sprite2D or Polygon2D)
        {
            bool on = VoidTreatment.IsOn((CanvasItem)node);
            if (hazard) { hazardArt++; if (on) hazardDrained++; }
            else { art++; if (on) drained++; }
        }
        foreach (Node child in node.GetChildren()) TallyTreatment(child, hazard, ref art, ref drained, ref hazardArt, ref hazardDrained);
    }

    private static int Treated(Node node)
    {
        if (node == null) return 0;
        int count = node is CanvasItem item && VoidTreatment.IsOn(item) ? 1 : 0;
        foreach (Node child in node.GetChildren()) count += Treated(child);
        return count;
    }

    /// <summary>
    /// What a zone-6 arena must be (MapGDD §7): an authored map of one of the run's worlds,
    /// for the node's mission, and VOID-touched — the backdrop and every piece of the map's
    /// art drained, and nothing that fights or warns: not a hazard's sprite, not a hazard,
    /// not a foe, not the player.
    /// </summary>
    private MapDocument ExpectVoidMap(string where, GameManager arena, List<string> worlds, MissionType mission)
    {
        AdventureContext adventure = RunState.Instance.CurrentAdventure;
        MapDocument dealt = CombatMapCatalog.LoadDocument(adventure?.CombatMapPath);
        Expect(dealt != null && arena.UsesAuthoredMap, $"{where}: zone 6 opened the legacy stage, not an authored map of one of the run's worlds");
        if (dealt == null) return null;

        string goal = CombatMapCatalog.GoalFor(mission);
        Expect(dealt.Worlds.Exists(worlds.Contains),
            $"{where}: was dealt '{dealt.Name}' (worlds {string.Join("/", dealt.Worlds)}); this run's worlds are {string.Join(",", worlds)}");
        Expect(dealt.Goals.Contains(goal), $"{where}: was dealt '{dealt.Name}' (goals {string.Join("/", dealt.Goals)}), which is not a '{goal}' map");
        Expect(adventure.VoidTouched, $"{where}: the handshake does not say the map is VOID-touched");

        int art = 0, drained = 0, hazardArt = 0, hazardDrained = 0;
        TallyTreatment(arena.GetNodeOrNull("World/AuthoredMapVisuals"), false, ref art, ref drained, ref hazardArt, ref hazardDrained);
        Expect(VoidTreatment.IsOn(arena.GetNodeOrNull<ColorRect>("Background")), $"{where}: the backdrop is not VOID-touched");
        Expect(art > 0 && drained == art, $"{where}: {drained} of the map's {art} pieces of art are VOID-touched; all of them must be");
        Expect(hazardDrained == 0, $"{where}: {hazardDrained} of {hazardArt} hazard sprites were drained; a hazard keeps its colours");
        int fighting = Treated(arena.Entities) + Treated(arena.GetNodeOrNull("Hazards")) + Treated(Player(GetTree()));
        Expect(fighting == 0, $"{where}: {fighting} nodes of the fight (player, foes, hazards) are VOID-touched; only the map may be");
        return dealt;
    }

    private async Task WalkTheVoid()
    {
        RunState run = RunState.Instance;
        run.NewRun("WALKVOID");
        MapNode core = run.FindNode("XX-6-1");
        List<MapNode> ring = run.Graph.Nodes.FindAll(node => node.WorldIndex == -1 && node.IsCombat && node.Level == 5);
        ring.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        Expect(ring.Count == 5 && core != null, $"void: the run has {ring.Count} LV5 nodes and {(core == null ? "no" : "a")} core; zone 6 is five and one");
        if (ring.Count == 0 || core == null) return;
        List<string> worlds = run.Graph.Worlds.ConvertAll(world => world.Abbr);

        // The treatment is a shader. The walk below proves which nodes carry its material;
        // this proves the engine accepts it (the headless renderer parses shaders too).
        Expect(VoidTreatment.ShaderCompiles(), "void: the VOID treatment's shader does not compile; a zone-6 map would be drawn wrong");

        // Zone 6 wears the run's worlds (MapGDD §7): each LV5 node opens an authored map of
        // one of them, for its own mission, VOID-touched, and its foes come out of that map's
        // nests. Their level is the NODE's, whatever the day (FOES.gdd §2).
        run.Day = 3;
        var opened = new List<string>();
        foreach (MapNode node in ring)
        {
            string where = $"void: {node.Id} ({node.Mission})";
            GameManager arena = await Enter(where, node);
            if (arena == null) continue;
            await Ticks(60 * 4);                                             // past the 3 s countdown

            MapDocument dealt = ExpectVoidMap(where, arena, worlds, node.Mission);
            Expect(arena.FoeLevel == 7, $"{where}: an LV5 node fights at foe level {arena.FoeLevel}; the design fixes it at 7");
            Expect(arena.CurrentMission is { Status: MissionStatus.Running }, $"{where}: the mission is not running");
            ExpectPlayerStands(where, arena, Player(GetTree()));

            // The first foe: the ambient trickle's arrives 4 s after the gate, a wave at once.
            Godot.Collections.Array<Node> foes = GetTree().GetNodesInGroup("foe");
            for (int t = 0; t < 60 * 6 && foes.Count == 0; t++)
            {
                await Ticks(1);
                foes = GetTree().GetNodesInGroup("foe");
            }
            Vector2 size = arena.AuthoredMapSize;
            int outside = 0;
            foreach (Node foe in foes)
                if (foe is Node2D body && (body.GlobalPosition.X <= 0f || body.GlobalPosition.X >= size.X || body.GlobalPosition.Y <= 0f || body.GlobalPosition.Y >= size.Y)) outside++;
            Expect(foes.Count > 0 && outside == 0, $"{where}: ten seconds in there are {foes.Count} foes, {outside} of them outside the map; they must come out of the map's own nests");

            arena.CurrentMission.DebugForceComplete();
            for (int i = 0; i < 300 && !arena.GoalResolved; i++) await Ticks(1);
            Expect(arena.GoalResolved && run.CompletedNodeIds.Contains(node.Id), $"{where}: reaching the goal did not complete the node");
            if (dealt != null) opened.Add($"{node.Id} {node.Mission} on {dealt.Name}");
        }

        // The core: a boss arena of one of the run's worlds, the stand-in on one of its perches.
        GameManager throne = await Enter("void: XX-6-1", core);
        if (throne == null) return;
        var mission = throne.CurrentMission as BossMission;
        await Ticks(60 * 4);
        MapDocument arenaDealt = ExpectVoidMap("void: XX-6-1 (the core)", throne, worlds, MissionType.Boss);
        Expect(throne.FoeLevel == 8, $"void: the core fights at foe level {throne.FoeLevel}; the design fixes it at 8");
        Expect(mission is { IsFinalBoss: true, HasSecondaryBar: true }, "void: the core is not a final-boss fight with a boss on the field");
        float coreHp = mission?.SecondaryMax ?? 0f;
        Expect(mission != null && Mathf.IsEqualApprox(mission.TimeRemaining, MissionTable.BossTimeLimit(6), 5f),
            $"void: the final boss timer reads {mission?.TimeRemaining:0} s; the design gives {MissionTable.BossTimeLimit(6):0} s");
        BossCrab crab = StandIn(GetTree());
        Vector2 map = throne.AuthoredMapSize, at = crab?.GlobalPosition ?? Vector2.Zero;
        Expect(crab != null && at.X > 0f && at.X < map.X && at.Y > 0f && at.Y < map.Y,
            $"void: the core's stand-in boss is {(crab == null ? "missing" : $"at ({M(at.X)}, {M(at.Y)}) m, outside the {M(map.X)} × {M(map.Y)} m arena")}");
        ExpectPlayerStands("void: XX-6-1 (the core)", throne, Player(GetTree()));

        throne.CurrentMission.DebugForceComplete();
        RunOverController over = await SceneIs<RunOverController>(900);
        Expect(over != null && run.RunFinished && run.LastEndKind == RunEndKind.Victory, "void: beating the core did not end the run in victory");
        Expect(!File.Exists(Path.Combine(SaveGameService.DirectoryOverride, "slot_1.json")), "void: a finished run left its save behind");
        GD.Print($"  run      the void       seed WALKVOID ({string.Join(",", worlds)}): {string.Join(" · ", opened)} · the core on {arenaDealt?.Name ?? "no map"} — every one VOID-touched, none on the legacy stage");
        GD.Print($"  run      the void       LV5 fights at foe level 7, the core at 8 whatever the day · the final boss (still the stand-in crab) has {coreHp:0} HP · beating it ends the run in victory and clears its save");
    }
}

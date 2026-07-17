using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fableland.Data;
using Fableland.MapCreation.Data;
using Fableland.Run;

namespace Fableland.Map;

/// <summary>
/// Seeded v0.9 overworld generator. Five storybook realms retain their individual rosters but
/// share one organic island. Five rivers cut that island into the realm polygons; only runtime
/// TwistedReality bridges may cross between them.
/// </summary>
public static class MapGenerator
{
    public const float LayoutScale = 1.8f;
    public static readonly Vector2 Center = new(576, 340);
    public const float RimRadius = 300f * LayoutScale + 30f;

    // The regular pentagon's radius is derived after the coast is known. This bootstrap value
    // settles near 120 px at the intended island scale.
    private const float DefaultZone6Radius = 120f;
    private const float Level5Radius = 86f;
    private const float RiverRadius = 43f;
    private const float LakeRadius = 66f;
    private const float MinNodeSpacing = 44f;
    private const float FirstRiverBoundaryDeg = -126f;

    public const string PomegraknightHome = "VK";

    private sealed class WorldPlan
    {
        public int N1A;
        public int N1B;
        public int N2A;
        public int N2B;
        public int CombatCount => N1A + N1B + N2A + N2B + 3;
    }

    private sealed class NodePlacement
    {
        public int Ordinal;
        public Vector2 Pos;
        public float Altitude;
        public string Tag;
        public int Level;
        public NodeKind Kind;
    }

    private sealed class CoastProfile
    {
        private readonly float _phaseA;
        private readonly float _phaseB;
        private readonly float _phaseC;
        private readonly float _bias;

        public CoastProfile(DetRandom rng)
        {
            _phaseA = (float)(rng.NextDouble() * Mathf.Tau);
            _phaseB = (float)(rng.NextDouble() * Mathf.Tau);
            _phaseC = (float)(rng.NextDouble() * Mathf.Tau);
            _bias = ((float)rng.NextDouble() - 0.5f) * 20f;
        }

        public float RadiusAt(float degrees)
        {
            float a = Mathf.DegToRad(degrees);
            float noise = Mathf.Sin(a * 2f + _phaseA) * MapGenTable.CoastIrregularity * 0.48f
                        + Mathf.Sin(a * 3f + _phaseB) * MapGenTable.CoastIrregularity * 0.28f
                        + Mathf.Sin(a * 5f + _phaseC) * MapGenTable.CoastIrregularity * 0.14f;
            return Mathf.Clamp(RimRadius + _bias + noise, RimRadius - 58f, RimRadius + 46f);
        }
    }

    private sealed class RoadCandidate
    {
        public MapNode A;
        public MapNode B;
        public Vector2[] Route;
        public float Length;
    }

    public static MapGraph Generate(string seed, string homeAbbr = PomegraknightHome)
    {
        seed ??= "";
        var graph = new MapGraph
        {
            Seed = seed,
            Center = Center,
            Zone6Radius = DefaultZone6Radius,
            RiverRadius = RiverRadius,
            LakeRadius = LakeRadius,
        };

        var worldRng = new DetRandom(seed);
        var pool = new List<WorldDef>(WorldDef.Pool);
        WorldDef home = pool.Find(world => world.Abbr == homeAbbr) ?? pool[0];
        pool.RemoveAll(world => world.Abbr == home.Abbr);
        worldRng.Shuffle(pool);
        graph.Worlds = new List<WorldDef> { home };
        graph.Worlds.AddRange(pool.Take(MapGenTable.RealmCount - 1));

        var countRng = new DetRandom(seed + ":outer-counts");
        var plans = new List<WorldPlan>();
        for (int i = 0; i < graph.Worlds.Count; i++)
        {
            int n1a = countRng.Chance(0.5) ? MapGenTable.SublevelMaxCities : MapGenTable.SublevelMinCities;
            int n1b = countRng.Chance(0.5) ? MapGenTable.SublevelMaxCities : MapGenTable.SublevelMinCities;
            int total = n1a + n1b;
            int low = total / 2;
            int high = total - low;
            bool highFirst = countRng.Chance(0.5);
            plans.Add(new WorldPlan
            {
                N1A = n1a,
                N1B = n1b,
                N2A = highFirst ? high : low,
                N2B = highFirst ? low : high,
            });
        }

        graph.Landmasses = BuildRealmLandmasses(graph, new DetRandom(seed + ":landmasses"));
        var byWorld = new List<Dictionary<string, List<MapNode>>>();
        for (int world = 0; world < graph.Worlds.Count; world++)
        {
            byWorld.Add(PlaceWorldNodes(graph, graph.Worlds[world], world, plans[world],
                graph.Landmasses[world], new DetRandom(seed + ":nodes:" + graph.Worlds[world].Abbr)));
        }

        for (int world = 0; world < graph.Worlds.Count; world++)
        {
            BuildIntraRealmNetwork(graph, byWorld[world], graph.Worlds[world], world,
                graph.Landmasses[world], new DetRandom(seed + ":roads:" + graph.Worlds[world].Abbr));
            PlaceEventNodes(graph, graph.Worlds[world], world, graph.Landmasses[world],
                new DetRandom(seed + ":events:" + graph.Worlds[world].Abbr));
        }
        BuildCityControlFields(graph);

        // The central layout deliberately keeps its five boss gates and its existing day rules.
        BuildZone6(graph);
        graph.StartNode = new DetRandom(seed + ":start").Pick(byWorld[0]["1-A"]);
        RollMissions(graph, seed);
        graph.BuildAdjacency();

        VerifyGeometry(graph);
        VerifyRealmReachability(graph, byWorld);
        VerifyRealmIsolation(graph);
        VerifyFunctionalCounts(graph);
        return graph;
    }

    // ---- one island / five river realms --------------------------------------

    private static List<WorldLandmass> BuildRealmLandmasses(MapGraph graph, DetRandom rng)
    {
        var profile = new CoastProfile(rng.Sub("coast"));
        graph.IslandCoast = new Vector2[MapGenTable.IslandCoastSamples];
        for (int i = 0; i < graph.IslandCoast.Length; i++)
        {
            float angle = -180f + 360f * i / graph.IslandCoast.Length;
            graph.IslandCoast[i] = graph.Center + Polar(angle, profile.RadiusAt(angle));
        }
        float pentagonUnitArea = 0.5f * MapGenTable.RealmCount * Mathf.Sin(Mathf.Tau / MapGenTable.RealmCount);
        graph.Zone6Radius = Mathf.Sqrt(PolygonArea(graph.IslandCoast) * MapGenTable.VoidAreaFraction / pentagonUnitArea);
        graph.VoidPentagon = RegularPolygon(graph.Center, graph.Zone6Radius, MapGenTable.RealmCount,
            FirstRiverBoundaryDeg);

        for (int i = 0; i < MapGenTable.RealmCount; i++)
        {
            float boundary = FirstRiverBoundaryDeg + 360f * i / MapGenTable.RealmCount;
            graph.RealmRivers.Add(BuildRealmRiver(graph, i, boundary, profile, rng.Sub("river:" + i)));
        }

        Vector2 highland = graph.Center + Polar((float)rng.NextDouble() * 360f,
            RimRadius * (0.24f + (float)rng.NextDouble() * 0.20f));
        Vector2 lowland = graph.Center + Polar((float)rng.NextDouble() * 360f,
            RimRadius * (0.48f + (float)rng.NextDouble() * 0.25f));
        float highRadius = 125f + (float)rng.NextDouble() * 56f;
        float lowRadius = 130f + (float)rng.NextDouble() * 62f;
        float noisePhase = (float)(rng.NextDouble() * Mathf.Tau);

        var lands = new List<WorldLandmass>(MapGenTable.RealmCount);
        for (int i = 0; i < MapGenTable.RealmCount; i++)
        {
            float startBoundary = FirstRiverBoundaryDeg + 360f * i / MapGenTable.RealmCount;
            float endBoundary = startBoundary + 360f / MapGenTable.RealmCount;
            float centerAngle = (startBoundary + endBoundary) * 0.5f;
            MapRiver lowRiver = graph.RealmRivers[i];
            MapRiver highRiver = graph.RealmRivers[(i + 1) % MapGenTable.RealmCount];

            var coast = new List<Vector2>();
            // Low-angle river bank faces this realm; advance to the coast.
            coast.AddRange(lowRiver.RightBank);
            for (int sample = 1; sample < MapGenTable.RealmOuterCoastSamples; sample++)
            {
                float t = (float)sample / MapGenTable.RealmOuterCoastSamples;
                float angle = Mathf.Lerp(startBoundary + MapGenTable.RiverMaxHalfWidthDeg,
                    endBoundary - MapGenTable.RiverMaxHalfWidthDeg, t);
                coast.Add(graph.Center + Polar(angle, profile.RadiusAt(angle)));
            }
            // High-angle river bank faces this realm; return to the VOID.
            for (int p = highRiver.LeftBank.Length - 1; p >= 0; p--) coast.Add(highRiver.LeftBank[p]);

            var land = new WorldLandmass
            {
                WorldIndex = i,
                CenterAngleDeg = centerAngle,
                MapCenter = graph.Center,
                IslandCenter = graph.Center + Polar(centerAngle,
                    (graph.Zone6Radius + MapGenTable.RealmCapitalVoidBuffer + RimRadius) * 0.56f),
                RadialAxis = Polar(centerAngle, 1f),
                TangentAxis = Polar(centerAngle, 1f).Rotated(Mathf.Pi * 0.5f),
                InnerRadius = graph.Zone6Radius + MapGenTable.RealmCapitalVoidBuffer,
                OuterRadius = RimRadius,
                Coast = coast.ToArray(),
                HighlandCenter = highland,
                LowlandCenter = lowland,
                HighlandRadius = highRadius,
                LowlandRadius = lowRadius,
                NoisePhase = noisePhase,
            };
            land.Bounds = BoundsOf(land.Coast);
            lands.Add(land);
        }
        return lands;
    }

    private static MapRiver BuildRealmRiver(MapGraph graph, int boundaryIndex, float baseAngle,
        CoastProfile profile, DetRandom rng)
    {
        int samples = MapGenTable.RiverSamples;
        var left = new Vector2[samples];
        var right = new Vector2[samples];
        float phaseA = (float)(rng.NextDouble() * Mathf.Tau);
        float phaseB = (float)(rng.NextDouble() * Mathf.Tau);
        float maxHalfWidth = Mathf.Lerp(MapGenTable.RiverMinHalfWidthDeg, MapGenTable.RiverMaxHalfWidthDeg,
            (float)rng.NextDouble());
        float bend = 1.2f + (float)rng.NextDouble() * 2.4f;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / (samples - 1);
            float eased = t * t * (3f - 2f * t);
            float centerAngle = baseAngle + Mathf.Sin(t * Mathf.Pi + phaseA) * bend * Mathf.Sin(t * Mathf.Pi);
            float widthWave = Mathf.Sin(t * Mathf.Tau * 1.5f + phaseB) * 0.25f;
            float halfWidth = Mathf.Max(0f, eased * maxHalfWidth * (1f + widthWave));
            float radius = Mathf.Lerp(graph.Zone6Radius, profile.RadiusAt(centerAngle), t);
            left[i] = graph.Center + Polar(centerAngle - halfWidth, radius);
            right[i] = graph.Center + Polar(centerAngle + halfWidth, radius);
        }

        var poly = new List<Vector2>(samples * 2);
        poly.AddRange(left);
        for (int i = right.Length - 1; i >= 0; i--) poly.Add(right[i]);
        return new MapRiver
        {
            BoundaryIndex = boundaryIndex,
            LeftBank = left,
            RightBank = right,
            Polygon = poly.ToArray(),
        };
    }

    // ---- city placement and level ranking -------------------------------------

    private static Dictionary<string, List<MapNode>> PlaceWorldNodes(MapGraph graph, WorldDef world,
        int worldIndex, WorldPlan plan, WorldLandmass land, DetRandom rng)
    {
        var placements = new List<NodePlacement>();
        Vector2 bossPos = SampleCapitalPosition(graph, land, rng);
        placements.Add(new NodePlacement
        {
            Ordinal = 0,
            Pos = bossPos,
            Altitude = land.AltitudeAt(bossPos),
            Tag = "4",
            Level = 4,
            Kind = NodeKind.Boss,
        });
        float bossDistance = bossPos.DistanceTo(graph.Center);
        for (int ordinal = 1; ordinal < plan.CombatCount; ordinal++)
        {
            Vector2 pos = SampleNodePosition(graph, land, placements, rng, bossDistance + 14f);
            placements.Add(new NodePlacement { Ordinal = ordinal, Pos = pos, Altitude = land.AltitudeAt(pos) });
        }

        var byVoidDistance = placements.OrderBy(node => node.Pos.DistanceSquaredTo(graph.Center))
            .ThenBy(node => node.Ordinal).ToList();
        var ranks = new (string Tag, int Level, NodeKind Kind, int Count)[]
        {
            ("4", 4, NodeKind.Boss, 1),
            ("3", 3, NodeKind.Combat, 2),
            ("2-B", 2, NodeKind.Combat, plan.N2B),
            ("2-A", 2, NodeKind.Combat, plan.N2A),
            ("1-B", 1, NodeKind.Combat, plan.N1B),
            ("1-A", 1, NodeKind.Combat, plan.N1A),
        };
        int ranked = 0;
        foreach (var rank in ranks)
        {
            for (int i = 0; i < rank.Count; i++)
            {
                NodePlacement node = byVoidDistance[ranked++];
                node.Tag = rank.Tag;
                node.Level = rank.Level;
                node.Kind = rank.Kind;
            }
        }

        var slots = new Dictionary<string, List<MapNode>>();
        int level1Index = 1;
        int level2Index = 1;
        string[] outputOrder = { "1-A", "1-B", "2-A", "2-B", "3", "4" };
        foreach (string tag in outputOrder)
        {
            var group = placements.Where(node => node.Tag == tag)
                .OrderByDescending(node => node.Pos.DistanceSquaredTo(graph.Center))
                .ThenBy(node => node.Ordinal).ToList();
            var generated = new List<MapNode>(group.Count);
            foreach (NodePlacement placement in group)
            {
                int index = placement.Level switch
                {
                    1 => level1Index++,
                    2 => level2Index++,
                    _ => generated.Count + 1,
                };
                var node = new MapNode
                {
                    Id = $"{world.Abbr}-{placement.Level}-{index}",
                    Kind = placement.Kind,
                    WorldIndex = worldIndex,
                    Zone = world.Abbr,
                    LevelTag = placement.Tag,
                    Level = placement.Level,
                    Pos = placement.Pos,
                    Color = world.Color.Lightened(0.15f),
                    Altitude = placement.Altitude,
                    Terrain = TerrainFor(placement.Altitude),
                };
                graph.Nodes.Add(node);
                generated.Add(node);
            }
            slots[tag] = generated;
        }
        return slots;
    }

    private static Vector2 SampleCapitalPosition(MapGraph graph, WorldLandmass land, DetRandom rng)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            float radialJitter = 22f + (float)rng.NextDouble() * 20f;
            float lateralJitter = ((float)rng.NextDouble() - 0.5f) * 26f;
            Vector2 candidate = graph.Center + land.RadialAxis * (land.InnerRadius + radialJitter)
                + land.TangentAxis * lateralJitter;
            if (land.Contains(candidate)) return candidate;
        }
        return SampleLandPoint(graph, land, rng, 0.02f, 0.16f);
    }

    private static Vector2 SampleNodePosition(MapGraph graph, WorldLandmass land,
        List<NodePlacement> placed, DetRandom rng, float minVoidDistance)
    {
        for (int attempt = 0; attempt < 360; attempt++)
        {
            Vector2 candidate = SampleLandPoint(graph, land, rng, 0f, 1f);
            if (candidate.DistanceTo(graph.Center) <= minVoidDistance) continue;
            bool clear = placed.All(other => candidate.DistanceSquaredTo(other.Pos) >= MinNodeSpacing * MinNodeSpacing);
            if (clear) return candidate;
        }
        return SampleLandPoint(graph, land, rng, 0.24f, 0.72f);
    }

    private static Vector2 SampleLandPoint(MapGraph graph, WorldLandmass land, DetRandom rng,
        float minProgress, float maxProgress)
    {
        for (int attempt = 0; attempt < 320; attempt++)
        {
            float x = Mathf.Lerp(land.Bounds.Position.X, land.Bounds.End.X, (float)rng.NextDouble());
            float y = Mathf.Lerp(land.Bounds.Position.Y, land.Bounds.End.Y, (float)rng.NextDouble());
            var candidate = new Vector2(x, y);
            if (!land.Contains(candidate)) continue;
            float progress = (candidate.DistanceTo(graph.Center) - land.InnerRadius) /
                             Mathf.Max(1f, RimRadius - land.InnerRadius);
            if (progress >= minProgress && progress <= maxProgress) return candidate;
        }
        return land.IslandCenter;
    }

    private static string TerrainFor(float altitude) => altitude switch
    {
        < 0.34f => CombatMapTerrain.Lowground,
        > 0.65f => CombatMapTerrain.High,
        _ => CombatMapTerrain.SeaLevel,
    };

    // ---- local roads, hubs, and events ----------------------------------------

    private static void BuildIntraRealmNetwork(MapGraph graph, Dictionary<string, List<MapNode>> slots,
        WorldDef world, int worldIndex, WorldLandmass land, DetRandom rng)
    {
        MapNode boss = slots["4"][0];
        List<MapNode> lv3 = slots["3"];
        var nonCapitalCities = graph.Nodes.Where(node => node.WorldIndex == worldIndex && node.IsCombat && node != boss)
            .OrderBy(node => node.Id).ToList();
        var allCities = graph.Nodes.Where(node => node.WorldIndex == worldIndex && node.IsCombat)
            .OrderBy(node => node.Id).ToList();

        // Reserve both final approaches before local roads can occupy their geometry. The hub's
        // optional cluster spokes are added later, after the kingdom spine is complete.
        int preferredHubLv3 = rng.Chance(0.5) ? 0 : 1;
        bool approachesReserved = false;
        for (int attempt = 0; attempt < 2 && !approachesReserved; attempt++)
        {
            int hubLv3 = (preferredHubLv3 + attempt) % 2;
            int initialNodeCount = graph.Nodes.Count;
            int initialEdgeCount = graph.Edges.Count;
            try
            {
                AddCapitalApproachHub(graph, lv3[hubLv3], boss, world, worldIndex, land, allCities);
                approachesReserved = AddRoad(graph, lv3[1 - hubLv3], boss, land);
            }
            catch (InvalidOperationException)
            {
                approachesReserved = false;
            }
            if (!approachesReserved)
            {
                graph.Nodes.RemoveRange(initialNodeCount, graph.Nodes.Count - initialNodeCount);
                graph.Edges.RemoveRange(initialEdgeCount, graph.Edges.Count - initialEdgeCount);
            }
        }
        if (!approachesReserved)
            throw new InvalidOperationException($"{world.Abbr} could not reserve two non-crossing LV3 approaches");

        var connected = new HashSet<MapNode> { lv3[0] };
        while (connected.Count < nonCapitalCities.Count)
        {
            RoadCandidate best = null;
            foreach (MapNode a in nonCapitalCities)
            {
                if (!connected.Contains(a)) continue;
                foreach (MapNode b in nonCapitalCities)
                {
                    if (connected.Contains(b) || !TryBuildRoute(graph, land, a, b, out Vector2[] route)) continue;
                    var candidate = new RoadCandidate { A = a, B = b, Route = route, Length = RouteLength(route) };
                    if (best == null || candidate.Length < best.Length ||
                        (Mathf.IsEqualApprox(candidate.Length, best.Length) &&
                         string.CompareOrdinal(a.Id + b.Id, best.A.Id + best.B.Id) < 0))
                        best = candidate;
                }
            }
            if (best == null || !AddRoad(graph, best.A, best.B, best.Route))
                throw new InvalidOperationException($"{world.Abbr} has no legal non-crossing local road candidate");
            connected.Add(best.B);
        }

        int loopBudget = rng.Range(MapGenTable.LocalRoadLoopsMin, MapGenTable.LocalRoadLoopsMax);
        for (int extra = 0; extra < loopBudget; extra++)
        {
            var candidates = new List<RoadCandidate>();
            for (int a = 0; a < nonCapitalCities.Count; a++)
            for (int b = a + 1; b < nonCapitalCities.Count; b++)
            {
                if (GraphHasEdge(graph, nonCapitalCities[a], nonCapitalCities[b]) ||
                    !TryBuildRoute(graph, land, nonCapitalCities[a], nonCapitalCities[b], out Vector2[] route)) continue;
                candidates.Add(new RoadCandidate { A = nonCapitalCities[a], B = nonCapitalCities[b], Route = route, Length = RouteLength(route) });
            }
            if (candidates.Count == 0) break;
            candidates.Sort((a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) :
                string.CompareOrdinal(a.A.Id + a.B.Id, b.A.Id + b.B.Id));
            RoadCandidate selected = candidates[rng.Range(0, Mathf.Min(4, candidates.Count - 1))];
            AddRoad(graph, selected.A, selected.B, selected.Route);
        }

        MapNode mandatoryHub = graph.Nodes.Single(node => node.WorldIndex == worldIndex && node.Id == $"{world.Abbr}-H-1");
        TryAddHubSpokes(graph, mandatoryHub, allCities, land);

        int targetHubs = rng.Range(MapGenTable.TransportationHubsPerRealmMin, MapGenTable.TransportationHubsPerRealmMax);
        int hubCount = 1;
        for (int serial = 2; hubCount < targetHubs; serial++)
        {
            var choices = graph.Edges.Where(edge => edge.Kind == MapEdgeKind.Road &&
                edge.A.WorldIndex == worldIndex && edge.B.WorldIndex == worldIndex &&
                edge.A.IsCombat && edge.B.IsCombat && !IsLv3BossApproach(edge)).OrderBy(edge =>
                edge.A.Pos.DistanceSquaredTo(land.IslandCenter) + edge.B.Pos.DistanceSquaredTo(land.IslandCenter)).ToList();
            if (choices.Count == 0) break;
            int pickCount = Mathf.Min(5, choices.Count);
            MapEdge edge = choices[rng.Range(0, pickCount - 1)];
            if (InsertTransportationHub(graph, edge, world, worldIndex, land, allCities, serial)) hubCount++;
        }
        if (hubCount < MapGenTable.TransportationHubsPerRealmMin)
            throw new InvalidOperationException($"{world.Abbr} could not place the required Transportation Hubs");
    }

    private static bool IsLv3BossApproach(MapEdge edge) =>
        (edge.A.Kind == NodeKind.Boss && edge.B.Level == 3) || (edge.B.Kind == NodeKind.Boss && edge.A.Level == 3);

    private static void AddCapitalApproachHub(MapGraph graph, MapNode lv3, MapNode boss, WorldDef world,
        int worldIndex, WorldLandmass land, List<MapNode> cities)
    {
        if (!TryBuildRoute(graph, land, lv3, boss, out Vector2[] route))
            throw new InvalidOperationException($"{world.Abbr} could not route its hub approach");
        Vector2 pos = PointAlong(route, 0.52f);
        SplitRoute(route, 0.52f, out Vector2[] left, out Vector2[] right);
        MapNode hub = CreateTransportHub(world, worldIndex, land, pos, 1, 4);
        graph.Nodes.Add(hub);
        if (!AddRoad(graph, lv3, hub, left) || !AddRoad(graph, hub, boss, right))
            throw new InvalidOperationException($"{world.Abbr} could not split its hub approach");
    }

    private static bool InsertTransportationHub(MapGraph graph, MapEdge edge, WorldDef world, int worldIndex,
        WorldLandmass land, List<MapNode> cities, int serial)
    {
        float fraction = 0.46f + (float)new DetRandom(graph.Seed + ":hub:" + worldIndex + ":" + serial).NextDouble() * 0.08f;
        Vector2 pos = PointAlong(edge.Route, fraction);
        SplitRoute(edge.Route, fraction, out Vector2[] left, out Vector2[] right);
        MapNode hub = CreateTransportHub(world, worldIndex, land, pos, serial, edge.Level);
        graph.Edges.Remove(edge);
        graph.Nodes.Add(hub);
        if (!AddRoad(graph, edge.A, hub, left) || !AddRoad(graph, hub, edge.B, right))
        {
            graph.Nodes.Remove(hub);
            graph.Edges.RemoveAll(candidate => candidate.Touches(hub));
            graph.Edges.Add(edge);
            return false;
        }
        TryAddHubSpokes(graph, hub, cities, land);
        return true;
    }

    private static MapNode CreateTransportHub(WorldDef world, int worldIndex, WorldLandmass land, Vector2 pos,
        int serial, int level) => new()
    {
        Id = $"{world.Abbr}-H-{serial}",
        Kind = NodeKind.TransportHub,
        WorldIndex = worldIndex,
        Zone = world.Abbr,
        LevelTag = level.ToString(),
        Level = level,
        Pos = pos,
        Color = new Color(0.85f, 0.70f, 0.40f),
        Altitude = land.AltitudeAt(pos),
        Terrain = TerrainFor(land.AltitudeAt(pos)),
    };

    private static void TryAddHubSpokes(MapGraph graph, MapNode hub, List<MapNode> cities, WorldLandmass land)
    {
        int spokes = 0;
        foreach (MapNode city in cities.OrderBy(node => node.Pos.DistanceSquaredTo(hub.Pos)).ThenBy(node => node.Id))
        {
            if (GraphHasEdge(graph, hub, city)) continue;
            // A hub never paints a road over an existing route. Once its next close spoke would
            // cross, this junction is complete rather than forcing a cluttered kingdom knot.
            if (!AddRoad(graph, hub, city, land)) break;
            if (++spokes >= MapGenTable.HubClusterCities) break;
        }
    }

    private static void PlaceEventNodes(MapGraph graph, WorldDef world, int worldIndex, WorldLandmass land, DetRandom rng)
    {
        int target = rng.Range(MapGenTable.EventNodesPerRealmMin, MapGenTable.EventNodesPerRealmMax);
        for (int serial = 1; serial <= target; serial++)
        {
            var choices = graph.Edges.Where(edge => edge.Kind == MapEdgeKind.Road &&
                edge.A.WorldIndex == worldIndex && edge.B.WorldIndex == worldIndex &&
                edge.A.IsCombat && edge.B.IsCombat).ToList();
            if (choices.Count == 0) break;
            MapEdge edge = rng.Pick(choices);
            float fraction = 0.46f + (float)rng.NextDouble() * 0.08f;
            Vector2 pos = PointAlong(edge.Route, fraction);
            SplitRoute(edge.Route, fraction, out Vector2[] left, out Vector2[] right);
            var node = new MapNode
            {
                Id = $"{world.Abbr}-E-{serial}", Kind = NodeKind.Event, WorldIndex = worldIndex, Zone = world.Abbr,
                LevelTag = edge.Level.ToString(), Level = edge.Level, Pos = pos, Color = new Color(0.55f, 0.80f, 0.95f),
                Altitude = land.AltitudeAt(pos), Terrain = TerrainFor(land.AltitudeAt(pos)),
            };
            graph.Edges.Remove(edge);
            graph.Nodes.Add(node);
            if (!AddRoad(graph, edge.A, node, left) || !AddRoad(graph, node, edge.B, right))
                throw new InvalidOperationException($"event route in {world.Abbr} crossed an existing road");
        }
        int placed = graph.Nodes.Count(node => node.WorldIndex == worldIndex && node.Kind == NodeKind.Event);
        if (placed < MapGenTable.EventNodesPerRealmMin)
            throw new InvalidOperationException($"{world.Abbr} could not place the required Event nodes");
    }

    private static bool AddRoad(MapGraph graph, MapNode a, MapNode b, WorldLandmass land)
    {
        if (!TryBuildRoute(graph, land, a, b, out Vector2[] route)) return false;
        return AddRoad(graph, a, b, route);
    }

    private static bool AddRoad(MapGraph graph, MapNode a, MapNode b, Vector2[] route)
    {
        if (route == null || route.Length < 2 || RouteCrossesExistingRoad(graph, a, b, route)) return false;
        graph.AddEdge(new MapEdge(a, b, Mathf.Max(a.Level, b.Level), visible: true, kind: MapEdgeKind.Road, route: route));
        return true;
    }

    private static bool TryBuildRoute(MapGraph graph, WorldLandmass land, MapNode a, MapNode b, out Vector2[] route)
    {
        var candidates = new List<Vector2[]>();
        if (SegmentInside(land, a.Pos, b.Pos)) candidates.Add(new[] { a.Pos, b.Pos });
        Vector2[] vias =
        {
            land.IslandCenter,
            land.MapCenter + land.RadialAxis * Mathf.Lerp(land.InnerRadius, land.OuterRadius, 0.34f),
            land.MapCenter + land.RadialAxis * Mathf.Lerp(land.InnerRadius, land.OuterRadius, 0.72f),
        };
        foreach (Vector2 via in vias)
            if (land.Contains(via) && SegmentInside(land, a.Pos, via) && SegmentInside(land, via, b.Pos))
                candidates.Add(new[] { a.Pos, via, b.Pos });
        foreach (Vector2[] candidate in candidates)
            if (!RouteCrossesExistingRoad(graph, a, b, candidate)) { route = candidate; return true; }
        route = Array.Empty<Vector2>();
        return false;
    }

    private static bool SegmentInside(WorldLandmass land, Vector2 a, Vector2 b)
    {
        const int samples = 18;
        for (int i = 0; i <= samples; i++)
            if (!land.Contains(a.Lerp(b, (float)i / samples))) return false;
        return true;
    }

    private static float RouteLength(Vector2[] route)
    {
        float length = 0f;
        for (int i = 1; i < route.Length; i++) length += route[i - 1].DistanceTo(route[i]);
        return length;
    }

    private static Vector2 PointAlong(Vector2[] route, float fraction)
    {
        if (route == null || route.Length == 0) return Vector2.Zero;
        if (route.Length == 1) return route[0];
        float total = RouteLength(route);
        float wanted = total * Mathf.Clamp(fraction, 0f, 1f);
        float seen = 0f;
        for (int i = 1; i < route.Length; i++)
        {
            float segment = route[i - 1].DistanceTo(route[i]);
            if (seen + segment >= wanted)
                return route[i - 1].Lerp(route[i], segment <= 0f ? 0f : (wanted - seen) / segment);
            seen += segment;
        }
        return route[^1];
    }

    private static void SplitRoute(Vector2[] route, float fraction, out Vector2[] left, out Vector2[] right)
    {
        float total = RouteLength(route);
        float wanted = total * Mathf.Clamp(fraction, 0f, 1f);
        float seen = 0f;
        var first = new List<Vector2> { route[0] };
        for (int i = 1; i < route.Length; i++)
        {
            Vector2 start = route[i - 1];
            Vector2 end = route[i];
            float length = start.DistanceTo(end);
            if (seen + length >= wanted)
            {
                Vector2 split = start.Lerp(end, length <= 0f ? 0f : (wanted - seen) / length);
                first.Add(split);
                var second = new List<Vector2> { split };
                for (int remain = i; remain < route.Length; remain++) second.Add(route[remain]);
                left = first.ToArray();
                right = second.ToArray();
                return;
            }
            first.Add(end);
            seen += length;
        }
        left = new[] { route[0], route[^1] };
        right = new[] { route[^1], route[^1] };
    }

    /// <summary>Reject an added road when any segment crosses or overlaps an existing road away
    /// from a common endpoint. This keeps hub spokes from painting over the kingdom's road map.</summary>
    private static bool RouteCrossesExistingRoad(MapGraph graph, MapNode a, MapNode b, Vector2[] route)
    {
        foreach (MapEdge edge in graph.Edges)
        {
            if (edge.Kind != MapEdgeKind.Road) continue;
            for (int i = 1; i < route.Length; i++)
            for (int j = 1; j < edge.Route.Length; j++)
            {
                if (!SegmentsIntersect(route[i - 1], route[i], edge.Route[j - 1], edge.Route[j], out Vector2 point,
                        out bool overlaps)) continue;
                if (overlaps || !IsSharedRoadEndpoint(point, a, b, edge)) return true;
            }
        }
        return false;
    }

    private static bool IsSharedRoadEndpoint(Vector2 point, MapNode a, MapNode b, MapEdge edge)
    {
        const float epsilon = 0.05f;
        bool At(Vector2 p, Vector2 q) => p.DistanceSquaredTo(q) <= epsilon * epsilon;
        return (At(point, a.Pos) && (edge.A == a || edge.B == a)) ||
               (At(point, b.Pos) && (edge.A == b || edge.B == b));
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point, out bool overlaps)
    {
        const float epsilon = 0.0001f;
        Vector2 r = b - a;
        Vector2 s = d - c;
        float denom = Cross(r, s);
        Vector2 delta = c - a;
        overlaps = false;
        point = Vector2.Zero;
        if (Mathf.Abs(denom) < epsilon)
        {
            if (Mathf.Abs(Cross(delta, r)) >= epsilon) return false;
            float rr = r.LengthSquared();
            if (rr < epsilon) return false;
            float t0 = delta.Dot(r) / rr;
            float t1 = (d - a).Dot(r) / rr;
            float min = Mathf.Max(0f, Mathf.Min(t0, t1));
            float max = Mathf.Min(1f, Mathf.Max(t0, t1));
            if (min > max + epsilon) return false;
            point = a.Lerp(b, Mathf.Clamp((min + max) * 0.5f, 0f, 1f));
            overlaps = max - min > epsilon;
            return true;
        }
        float t = Cross(delta, s) / denom;
        float u = Cross(delta, r) / denom;
        if (t < -epsilon || t > 1f + epsilon || u < -epsilon || u > 1f + epsilon) return false;
        point = a.Lerp(b, Mathf.Clamp(t, 0f, 1f));
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Every outer-realm land position is assigned to its nearest city by a clipped
    /// Voronoi field. These fields drive the atlas boundary view and city-owned VOID devour.</summary>
    private static void BuildCityControlFields(MapGraph graph)
    {
        for (int world = 0; world < graph.Landmasses.Count; world++)
        {
            var cities = graph.Nodes.Where(node => node.WorldIndex == world && node.IsCombat).ToList();
            WorldLandmass land = graph.Landmasses[world];
            foreach (MapNode city in cities)
            {
                var polygon = new List<Vector2>(land.Coast);
                foreach (MapNode other in cities)
                {
                    if (other == city) continue;
                    Vector2 normal = other.Pos - city.Pos;
                    if (normal.LengthSquared() < 0.0001f) continue;
                    float boundary = (other.Pos.LengthSquared() - city.Pos.LengthSquared()) * 0.5f;
                    ClipToHalfPlane(ref polygon, normal, boundary);
                    if (polygon.Count < 3) break;
                }
                city.ControlledField = polygon.ToArray();
                // The field, rather than the city's icon position, decides whether a city meets
                // the VOID river. Capitals are guaranteed anchors so the Zone-6 choice is never
                // hidden behind an unlucky Voronoi split.
                city.IsVoidRiverPeripheral = city.Level == 4 || FieldTouchesVoidRiver(graph, city.ControlledField);
            }
        }
    }

    private static bool FieldTouchesVoidRiver(MapGraph graph, Vector2[] field)
    {
        if (field == null || field.Length == 0) return false;
        foreach (MapRiver river in graph.RealmRivers)
        {
            if (PolylineNearField(river.LeftBank, field) || PolylineNearField(river.RightBank, field)) return true;
        }
        return false;
    }

    private static bool PolylineNearField(Vector2[] line, Vector2[] field)
    {
        for (int i = 0; i + 1 < line.Length; i++)
            foreach (Vector2 point in field)
                if (DistanceSquaredToSegment(point, line[i], line[i + 1]) <= 2.25f) return true;
        return false;
    }

    private static float DistanceSquaredToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0.0001f) return point.DistanceSquaredTo(a);
        float t = Mathf.Clamp((point - a).Dot(ab) / lengthSquared, 0f, 1f);
        return point.DistanceSquaredTo(a + ab * t);
    }

    private static void ClipToHalfPlane(ref List<Vector2> polygon, Vector2 normal, float boundary)
    {
        var output = new List<Vector2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];
            float currentValue = normal.Dot(current) - boundary;
            float nextValue = normal.Dot(next) - boundary;
            bool currentInside = currentValue <= 0f;
            bool nextInside = nextValue <= 0f;
            if (currentInside) output.Add(current);
            if (currentInside != nextInside)
            {
                float t = currentValue / (currentValue - nextValue);
                output.Add(current.Lerp(next, t));
            }
        }
        polygon = output;
    }

    // ---- zone 6 topology -------------------------------------------------------

    private static void BuildZone6(MapGraph graph)
    {
        var lv5 = new List<MapNode>();
        for (int world = 0; world < MapGenTable.RealmCount; world++)
        {
            float degrees = -90f + world * 72f;
            Vector2 pos = graph.Center + Polar(degrees, Level5Radius);
            var node = new MapNode
            {
                Id = $"XX-5-{world + 1}", Kind = NodeKind.Combat, WorldIndex = -1, Zone = "XX",
                LevelTag = "5", Level = 5, Pos = pos, Color = new Color(0.30f, 0.25f, 0.45f),
            };
            graph.Nodes.Add(node);
            lv5.Add(node);
        }

        var river = new MapNode
        {
            Id = "XX-R", Kind = NodeKind.River, WorldIndex = -1, Zone = "XX", LevelTag = "R", Level = 5,
            Pos = graph.Center + new Vector2(0, -graph.RiverRadius), Color = new Color(0.20f, 0.30f, 0.55f),
        };
        graph.Nodes.Add(river);
        foreach (MapNode node in lv5)
            graph.AddEdge(new MapEdge(node, river, 5, visible: false, kind: MapEdgeKind.VoidPassage));

        var core = new MapNode
        {
            Id = "XX-6-1", Kind = NodeKind.Boss, WorldIndex = -1, Zone = "XX", LevelTag = "6", Level = 6,
            Pos = graph.Center, Color = new Color(0.10f, 0.05f, 0.15f),
        };
        graph.Nodes.Add(core);
        graph.AddEdge(new MapEdge(river, core, 6, visible: false, kind: MapEdgeKind.VoidPassage));
    }

    private static void RollMissions(MapGraph graph, string seed)
    {
        var rng = new DetRandom(seed + "M");
        foreach (MapNode node in graph.Nodes)
        {
            if (!node.IsCombat) continue;
            if (node.Kind == NodeKind.Boss || node.Level == 4 || node.Level == 6)
            {
                node.Mission = MissionType.Boss;
                continue;
            }
            int roll = rng.Range(0, 94);
            node.Mission = roll < 60 ? MissionType.Collection
                         : roll < 75 ? MissionType.Protect
                         : roll < 85 ? MissionType.Destroy
                                     : MissionType.Slaughter;
        }
    }

    // ---- validation ------------------------------------------------------------

    private static void VerifyGeometry(MapGraph graph)
    {
        if (graph.IslandCoast.Length < 3 || graph.VoidPentagon.Length != MapGenTable.RealmCount ||
            graph.RealmRivers.Count != MapGenTable.RealmCount || graph.Landmasses.Count != MapGenTable.RealmCount)
            throw new InvalidOperationException("incomplete island geometry");
        foreach (MapRiver river in graph.RealmRivers)
        {
            if (river.Polygon.Length < 6 || river.LeftBank.Length < 2 || river.RightBank.Length < 2)
                throw new InvalidOperationException("invalid realm-divider river");
            if (river.LeftBank[0].DistanceTo(graph.Center) > graph.Zone6Radius + 0.1f ||
                river.LeftBank[^1].DistanceTo(graph.Center) < RimRadius - 90f)
                throw new InvalidOperationException("river does not connect the VOID to the coast");
        }
        float voidRatio = PolygonArea(graph.VoidPentagon) / Mathf.Max(1f, PolygonArea(graph.IslandCoast));
        if (Mathf.Abs(voidRatio - MapGenTable.VoidAreaFraction) > 0.001f)
            throw new InvalidOperationException($"VOID area ratio {voidRatio:F4} is outside the target tolerance");
        foreach (MapNode node in graph.Nodes)
        {
            if (node.WorldIndex >= 0 && !graph.Landmasses[node.WorldIndex].Contains(node.Pos))
                throw new InvalidOperationException($"outer node {node.Id} escaped its realm polygon");
        }
    }

    private static void VerifyRealmReachability(MapGraph graph, List<Dictionary<string, List<MapNode>>> byWorld)
    {
        for (int world = 0; world < byWorld.Count; world++)
        {
            MapNode boss = byWorld[world]["4"][0];
            var reached = new HashSet<MapNode> { boss };
            var queue = new Queue<MapNode>();
            queue.Enqueue(boss);
            while (queue.Count > 0)
            {
                MapNode current = queue.Dequeue();
                foreach (MapEdge edge in graph.EdgesOf(current))
                {
                    MapNode next = edge.Other(current);
                    if (next.WorldIndex != world || !reached.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }
            foreach (MapNode node in graph.Nodes)
                if (node.WorldIndex == world && !reached.Contains(node))
                    throw new InvalidOperationException($"realm {graph.Worlds[world].Abbr} has {node.Id} unreachable from its capital");
        }
    }

    private static void VerifyRealmIsolation(MapGraph graph)
    {
        foreach (MapEdge edge in graph.Edges)
        {
            if (edge.A.WorldIndex >= 0 && edge.B.WorldIndex >= 0 && edge.A.WorldIndex != edge.B.WorldIndex)
                throw new InvalidOperationException("generation created an illegal cross-realm edge");
        }
    }

    private static void VerifyFunctionalCounts(MapGraph graph)
    {
        foreach (WorldLandmass land in graph.Landmasses)
        {
            int hubs = graph.Nodes.Count(node => node.WorldIndex == land.WorldIndex && node.Kind == NodeKind.TransportHub);
            int events = graph.Nodes.Count(node => node.WorldIndex == land.WorldIndex && node.Kind == NodeKind.Event);
            if (hubs < MapGenTable.TransportationHubsPerRealmMin || hubs > MapGenTable.TransportationHubsPerRealmMax)
                throw new InvalidOperationException($"realm {land.WorldIndex} has {hubs} Transportation Hubs outside its budget");
            if (events < MapGenTable.EventNodesPerRealmMin || events > MapGenTable.EventNodesPerRealmMax)
                throw new InvalidOperationException($"realm {land.WorldIndex} has {events} Event nodes outside its budget");
            int level1 = graph.Nodes.Count(node => node.WorldIndex == land.WorldIndex && node.IsCombat && node.Level == 1);
            int level2 = graph.Nodes.Count(node => node.WorldIndex == land.WorldIndex && node.IsCombat && node.Level == 2);
            if (level1 != level2)
                throw new InvalidOperationException($"realm {land.WorldIndex} has unequal level-1/level-2 counts ({level1}/{level2})");
            if (graph.Nodes.Any(node => node.WorldIndex == land.WorldIndex && node.IsCombat && node.ControlledField.Length < 3))
                throw new InvalidOperationException($"realm {land.WorldIndex} has a city without a controlled field");
        }
    }

    private static bool GraphHasEdge(MapGraph graph, MapNode a, MapNode b) => graph.Edges.Any(edge =>
        (edge.A == a && edge.B == b) || (edge.A == b && edge.B == a));

    private static Rect2 BoundsOf(Vector2[] points)
    {
        float minX = points[0].X, maxX = points[0].X, minY = points[0].Y, maxY = points[0].Y;
        for (int i = 1; i < points.Length; i++)
        {
            minX = Mathf.Min(minX, points[i].X);
            maxX = Mathf.Max(maxX, points[i].X);
            minY = Mathf.Min(minY, points[i].Y);
            maxY = Mathf.Max(maxY, points[i].Y);
        }
        return new Rect2(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
    }

    private static float PolygonArea(Vector2[] points)
    {
        float twiceArea = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Length];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return Mathf.Abs(twiceArea) * 0.5f;
    }

    private static Vector2[] RegularPolygon(Vector2 center, float radius, int sides, float startDeg)
    {
        var points = new Vector2[sides];
        for (int i = 0; i < sides; i++) points[i] = center + Polar(startDeg + i * (360f / sides), radius);
        return points;
    }

    private static Vector2 Polar(float degrees, float radius)
    {
        float radians = Mathf.DegToRad(degrees);
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fableland.Map;

/// <summary>
/// Pure presentation precompute for the island atlas. Land tint and contours describe the shared
/// height field only; roads and runtime reality bridges remain the sole traversal indicators.
/// </summary>
public static class MapRenderModel
{
    public sealed class WorldTheme
    {
        public string Abbr;
        public Color Land;
    }

    private static readonly Dictionary<string, WorldTheme> Themes = new()
    {
        ["SL"] = new() { Abbr = "SL", Land = new(0.95f, 0.72f, 0.45f) },
        ["HC"] = new() { Abbr = "HC", Land = new(0.66f, 0.68f, 0.72f) },
        ["VK"] = new() { Abbr = "VK", Land = new(0.97f, 0.72f, 0.82f) },
        ["TD"] = new() { Abbr = "TD", Land = new(0.90f, 0.82f, 0.55f) },
        ["PL"] = new() { Abbr = "PL", Land = new(0.74f, 0.58f, 0.86f) },
        ["BM"] = new() { Abbr = "BM", Land = new(0.55f, 0.75f, 0.52f) },
    };
    private static readonly WorldTheme VoidTheme = new() { Abbr = "XX", Land = new(0.10f, 0.10f, 0.16f) };

    public static WorldTheme ThemeFor(string abbr) =>
        abbr == "XX" ? VoidTheme : (Themes.TryGetValue(abbr, out var theme) ? theme : VoidTheme);

    public sealed class LandSurface
    {
        public int WorldIndex;
        public Vector2[] Coast;
        public List<Vector2[]> FillPieces = new();
        public Color Fill;
    }

    public sealed class RiverSurface
    {
        public Vector2[] Polygon;
        public Vector2[] LeftBank;
        public Vector2[] RightBank;
    }

    public sealed class AltitudePatch
    {
        public int WorldIndex;
        public Vector2[] Poly;
        public Color Tint;
    }

    public sealed class ContourSegment
    {
        public int WorldIndex;
        public Vector2 A, B;
        public float Height;
    }

    public sealed class Territory
    {
        public MapNode Node;
        public Vector2[] Poly;
        public Color Fill;
    }

    /// <summary>One outer city's clipped control field. The owning city is the source of truth
    /// for whether this ground is alive or already consumed by the VOID.</summary>
    public sealed class CityTerritory
    {
        public MapNode City;
        public Vector2[] Boundary;
        public List<Vector2[]> FillPieces = new();
    }

    public sealed class Road
    {
        public MapNode A, B;
        public Vector2[] Route;
        public MapEdgeKind Kind;
    }

    public sealed class RenderedMap
    {
        public List<LandSurface> Landmasses = new();
        public Vector2[] IslandCoast = Array.Empty<Vector2>();
        public List<RiverSurface> Rivers = new();
        public List<AltitudePatch> AltitudePatches = new();
        public List<ContourSegment> Contours = new();
        public List<CityTerritory> CityTerritories = new();
        public List<Road> Roads = new();
        public Vector2[] Pentagon;
        public List<Territory> Zone6Cells = new();
        public List<MapNode> Islets = new();
    }

    public static RenderedMap Build(MapGraph graph)
    {
        var rendered = new RenderedMap
        {
            IslandCoast = graph.IslandCoast,
            Pentagon = graph.VoidPentagon,
        };

        for (int world = 0; world < graph.Landmasses.Count; world++)
        {
            WorldLandmass land = graph.Landmasses[world];
            rendered.Landmasses.Add(new LandSurface
            {
                WorldIndex = world,
                Coast = land.Coast,
                FillPieces = TriangulateLand(land.Coast),
                Fill = ThemeFor(graph.Worlds[world].Abbr).Land.Darkened(0.06f),
            });
            BuildAltitudePresentation(rendered, land, world);
        }

        foreach (MapRiver river in graph.RealmRivers)
        {
            rendered.Rivers.Add(new RiverSurface
            {
                Polygon = river.Polygon,
                LeftBank = river.LeftBank,
                RightBank = river.RightBank,
            });
        }

        foreach (MapNode city in graph.Nodes.Where(node => node.WorldIndex >= 0 && node.IsCombat && node.ControlledField.Length >= 3))
        {
            rendered.CityTerritories.Add(new CityTerritory
            {
                City = city,
                Boundary = city.ControlledField,
                FillPieces = TriangulateLand(city.ControlledField),
            });
        }

        foreach (MapEdge edge in graph.Edges)
        {
            bool isletLeg = (edge.A.WorldIndex == -1 && edge.A.Kind == NodeKind.Shelter)
                            || (edge.B.WorldIndex == -1 && edge.B.Kind == NodeKind.Shelter);
            // The pre-existing LV5 / river / core links are represented by the VOID art. Only
            // small outer legs to XX-S remain visible, while realm roads and reality bridges show.
            if (edge.A.WorldIndex == -1 && edge.B.WorldIndex == -1 && !isletLeg) continue;
            rendered.Roads.Add(new Road { A = edge.A, B = edge.B, Route = edge.Route, Kind = edge.Kind });
        }

        var lv5 = graph.Nodes.Where(node => node.WorldIndex == -1 && node.Kind == NodeKind.Combat && node.LevelTag == "5").ToList();
        if (lv5.Count >= 3)
        {
            Cell[] cells = PowerCells(lv5, rendered.Pentagon.ToList());
            for (int i = 0; i < lv5.Count; i++)
                if (cells[i].Verts.Count >= 3)
                    rendered.Zone6Cells.Add(new Territory
                    {
                        Node = lv5[i],
                        Poly = cells[i].Verts.ToArray(),
                        Fill = VoidTheme.Land,
                    });
        }
        rendered.Islets = graph.Nodes.Where(node => node.WorldIndex == -1 && node.Kind == NodeKind.Shelter).ToList();
        return rendered;
    }

    private static void BuildAltitudePresentation(RenderedMap rendered, WorldLandmass land, int worldIndex)
    {
        const int grid = 22;
        float stepX = land.Bounds.Size.X / grid;
        float stepY = land.Bounds.Size.Y / grid;
        if (stepX <= 0f || stepY <= 0f) return;

        for (int y = 0; y < grid; y++)
        for (int x = 0; x < grid; x++)
        {
            Vector2 topLeft = land.Bounds.Position + new Vector2(x * stepX, y * stepY);
            Vector2 topRight = topLeft + new Vector2(stepX, 0);
            Vector2 bottomRight = topLeft + new Vector2(stepX, stepY);
            Vector2 bottomLeft = topLeft + new Vector2(0, stepY);
            if (!land.Contains(topLeft) || !land.Contains(topRight) ||
                !land.Contains(bottomRight) || !land.Contains(bottomLeft))
                continue;

            float height = land.AltitudeAt((topLeft + bottomRight) * 0.5f);
            float delta = height - 0.5f;
            if (Mathf.Abs(delta) > 0.035f)
            {
                float alpha = Mathf.Min(0.13f, Mathf.Abs(delta) * 0.30f);
                rendered.AltitudePatches.Add(new AltitudePatch
                {
                    WorldIndex = worldIndex,
                    Poly = new[] { topLeft, topRight, bottomRight, bottomLeft },
                    Tint = delta >= 0f ? new Color(1f, 1f, 1f, alpha) : new Color(0f, 0f, 0f, alpha),
                });
            }

            float h0 = land.AltitudeAt(topLeft);
            float h1 = land.AltitudeAt(topRight);
            float h2 = land.AltitudeAt(bottomRight);
            float h3 = land.AltitudeAt(bottomLeft);
            foreach (float level in ContourLevels)
                AddContoursForCell(rendered, worldIndex, level, topLeft, topRight, bottomRight, bottomLeft, h0, h1, h2, h3);
        }
    }

    private static readonly float[] ContourLevels = { 0.30f, 0.44f, 0.58f, 0.72f };

    private static void AddContoursForCell(RenderedMap rendered, int worldIndex, float level,
        Vector2 a, Vector2 b, Vector2 c, Vector2 d, float ha, float hb, float hc, float hd)
    {
        var hits = new List<Vector2>(4);
        AddCrossing(hits, a, b, ha, hb, level);
        AddCrossing(hits, b, c, hb, hc, level);
        AddCrossing(hits, c, d, hc, hd, level);
        AddCrossing(hits, d, a, hd, ha, level);
        if (hits.Count == 2)
            rendered.Contours.Add(new ContourSegment { WorldIndex = worldIndex, A = hits[0], B = hits[1], Height = level });
        else if (hits.Count == 4)
        {
            rendered.Contours.Add(new ContourSegment { WorldIndex = worldIndex, A = hits[0], B = hits[1], Height = level });
            rendered.Contours.Add(new ContourSegment { WorldIndex = worldIndex, A = hits[2], B = hits[3], Height = level });
        }
    }

    private static void AddCrossing(List<Vector2> hits, Vector2 a, Vector2 b, float ha, float hb, float level)
    {
        if ((ha >= level) == (hb >= level) || Mathf.Abs(hb - ha) < 0.0001f) return;
        float t = Mathf.Clamp((level - ha) / (hb - ha), 0f, 1f);
        hits.Add(a.Lerp(b, t));
    }

    /// <summary>Godot fills only convex polygons, so cache ear-clipped triangles for each realm.</summary>
    private static List<Vector2[]> TriangulateLand(Vector2[] coast)
    {
        var pieces = new List<Vector2[]>();
        if (coast == null || coast.Length < 3) return pieces;
        var remaining = Enumerable.Range(0, coast.Length).ToList();
        bool clockwise = SignedArea(coast) < 0f;
        int guard = 0;
        while (remaining.Count > 3 && guard++ < coast.Length * coast.Length)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int current = remaining[i];
                int next = remaining[(i + 1) % remaining.Count];
                if (!IsConvex(coast[previous], coast[current], coast[next], clockwise)) continue;
                bool containsOther = false;
                foreach (int point in remaining)
                {
                    if (point == previous || point == current || point == next) continue;
                    if (PointInTriangle(coast[point], coast[previous], coast[current], coast[next]))
                    {
                        containsOther = true;
                        break;
                    }
                }
                if (containsOther) continue;
                pieces.Add(new[] { coast[previous], coast[current], coast[next] });
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }
        if (remaining.Count == 3)
            pieces.Add(new[] { coast[remaining[0]], coast[remaining[1]], coast[remaining[2]] });
        return pieces;
    }

    private static float SignedArea(Vector2[] poly)
    {
        float area = 0f;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area * 0.5f;
    }

    private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c, bool clockwise)
    {
        float cross = Cross(b - a, c - b);
        return clockwise ? cross < -0.001f : cross > 0.001f;
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float ab = Cross(b - a, point - a);
        float bc = Cross(c - b, point - b);
        float ca = Cross(a - c, point - c);
        return (ab >= -0.001f && bc >= -0.001f && ca >= -0.001f) ||
               (ab <= 0f && bc <= 0f && ca <= 0f);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static float ClaimRadius(NodeKind kind) => kind switch
    {
        NodeKind.Boss => 78f,
        NodeKind.Combat => 66f,
        NodeKind.River => 40f,
        _ => 30f,
    };

    private struct Cell { public List<Vector2> Verts; public List<int> Srcs; }

    private static Cell[] PowerCells(List<MapNode> sites, List<Vector2> clip)
    {
        int count = sites.Count;
        float[] weights = sites.Select(site =>
        {
            float radius = ClaimRadius(site.Kind);
            return radius * radius;
        }).ToArray();
        var cells = new Cell[count];
        for (int i = 0; i < count; i++)
        {
            var verts = new List<Vector2>(clip);
            var srcs = Enumerable.Repeat(-1, clip.Count).ToList();
            Vector2 pi = sites[i].Pos;
            for (int j = 0; j < count && verts.Count >= 3; j++)
            {
                if (j == i) continue;
                Vector2 pj = sites[j].Pos;
                Vector2 normal = pj - pi;
                if (normal.LengthSquared() < 1e-6f) continue;
                float c = (pj.LengthSquared() - pi.LengthSquared() - (weights[j] - weights[i])) * 0.5f;
                ClipHalfPlane(ref verts, ref srcs, normal, c, j);
            }
            cells[i] = new Cell { Verts = verts, Srcs = srcs };
        }
        return cells;
    }

    private static void ClipHalfPlane(ref List<Vector2> verts, ref List<int> srcs, Vector2 normal, float c, int source)
    {
        var outputVerts = new List<Vector2>();
        var outputSrcs = new List<int>();
        for (int i = 0; i < verts.Count; i++)
        {
            Vector2 current = verts[i];
            Vector2 next = verts[(i + 1) % verts.Count];
            float fCurrent = normal.Dot(current) - c;
            float fNext = normal.Dot(next) - c;
            bool currentInside = fCurrent <= 0f;
            bool nextInside = fNext <= 0f;
            if (currentInside)
            {
                outputVerts.Add(current);
                outputSrcs.Add(srcs[i]);
                if (!nextInside)
                {
                    float t = fCurrent / (fCurrent - fNext);
                    outputVerts.Add(current.Lerp(next, t));
                    outputSrcs.Add(source);
                }
            }
            else if (nextInside)
            {
                float t = fCurrent / (fCurrent - fNext);
                outputVerts.Add(current.Lerp(next, t));
                outputSrcs.Add(srcs[i]);
            }
        }
        verts = outputVerts.Count >= 3 ? outputVerts : new List<Vector2>();
        srcs = outputVerts.Count >= 3 ? outputSrcs : new List<int>();
    }
}

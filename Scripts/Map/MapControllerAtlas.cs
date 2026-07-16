using System.Collections.Generic;
using Godot;
using Fableland.Map;

/// <summary>Atlas presentation for the single island map. Rivers are geography, while roads and
/// violet reality bridges are the only visual claims about traversable topology.</summary>
public partial class MapController : Node2D
{
    private const float AtlasNodeR = 15f;
    private const float AtlasFuncR = 9f;

    private static readonly Color Sea = new(0.10f, 0.16f, 0.26f);
    private static readonly Color FogLand = new(0.14f, 0.14f, 0.18f);
    private static readonly Color RoadColor = new(0.82f, 0.68f, 0.44f);
    private static readonly Color Causeway = new(0.95f, 0.82f, 0.35f);
    private static readonly Color RealityBridge = new(0.78f, 0.38f, 1f);
    private static readonly Color RiverWater = new(0.18f, 0.38f, 0.66f);

    private void DrawAtlas(Dictionary<MapNode, int> vdist)
    {
        MapRenderModel.RenderedMap rendered = _render;
        if (rendered == null) return;
        DrawRect(new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size), Sea);

        foreach (MapRenderModel.LandSurface land in rendered.Landmasses)
        {
            bool shown = !_mist || _revealed.Contains(_graph.Worlds[land.WorldIndex].Abbr);
            foreach (Vector2[] piece in land.FillPieces)
                DrawColoredPolygon(ProjectPoly(piece), shown ? land.Fill : FogLand);
        }

        foreach (MapRenderModel.AltitudePatch patch in rendered.AltitudePatches)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[patch.WorldIndex].Abbr)) continue;
            DrawColoredPolygon(ProjectPoly(patch.Poly), patch.Tint);
        }
        foreach (MapRenderModel.ContourSegment contour in rendered.Contours)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[contour.WorldIndex].Abbr)) continue;
            Color colour = contour.Height >= 0.58f
                ? new Color(1f, 1f, 1f, 0.34f)
                : new Color(0f, 0f, 0f, 0.28f);
            DrawLine(Project(contour.A), Project(contour.B), colour, Scaled(0.85f));
        }
        DrawCityTerritories();

        // Rivers remain visible geographic barriers even under mist. Their irregular two-bank
        // polygons deliberately read as water within one island, never as detached sea gaps.
        foreach (MapRenderModel.RiverSurface river in rendered.Rivers)
        {
            DrawColoredPolygon(ProjectPoly(river.Polygon), RiverWater);
            DrawPolyline(ProjectPoly(river.LeftBank), new Color(0.70f, 0.84f, 1f, 0.78f), Scaled(1.4f));
            DrawPolyline(ProjectPoly(river.RightBank), new Color(0.70f, 0.84f, 1f, 0.78f), Scaled(1.4f));
        }
        if (rendered.IslandCoast.Length >= 3)
            DrawPolyline(Closed(ProjectPoly(rendered.IslandCoast)), new Color(0.04f, 0.08f, 0.13f, 0.85f), Scaled(2f));

        foreach (MapRenderModel.Road road in rendered.Roads)
        {
            if (road.A.Devoured || road.B.Devoured) continue;
            if (_mist && !NodeVisible(road.A) && !NodeVisible(road.B)) continue;
            Color colour = road.Kind switch
            {
                MapEdgeKind.RealityBridge => RealityBridge,
                MapEdgeKind.VoidPassage => Causeway,
                _ => RoadColor,
            };
            float width = road.Kind == MapEdgeKind.RealityBridge ? 5.2f : road.Kind == MapEdgeKind.VoidPassage ? 5f : 3.5f;
            DrawRoute(road.Route, colour, Scaled(width));
        }

        bool voidShown = !_mist || _revealed.Contains("XX");
        if (rendered.Pentagon != null)
        {
            DrawColoredPolygon(ProjectPoly(rendered.Pentagon), voidShown ? new Color(0.09f, 0.09f, 0.15f) : FogLand);
            if (voidShown)
            {
                foreach (MapRenderModel.Territory territory in rendered.Zone6Cells)
                {
                    Color fill = territory.Node.Devoured ? new Color(0.16f, 0.15f, 0.20f)
                        : _visited.Contains(territory.Node) ? territory.Fill.Lerp(VisitedGrey, 0.5f)
                        : territory.Fill.Lightened(0.05f);
                    Vector2[] poly = ProjectPoly(territory.Poly);
                    DrawColoredPolygon(poly, fill);
                    DrawPolyline(Closed(poly), new Color(0.3f, 0.28f, 0.45f, 0.4f), Scaled(1.2f));
                }
                DrawPolyline(Closed(ProjectPoly(rendered.Pentagon)), new Color(0.35f, 0.32f, 0.5f, 0.8f), Scaled(2f));
                DrawWorldDisc(_graph.Center, _graph.LakeRadius, new Color(0.04f, 0.03f, 0.08f));
                DrawWorldRing(_graph.Center, _graph.RiverRadius, new Color(0.28f, 0.4f, 0.72f, 0.9f), 3f);
            }
        }

        foreach (MapNode islet in rendered.Islets)
        {
            if (!NodeVisible(islet)) continue;
            DrawCircle(Project(islet.Pos), Scaled(AtlasFuncR + 4f), new Color(0.16f, 0.20f, 0.30f));
            DrawAtlasNode(islet, vdist);
        }
        foreach (MapNode node in _graph.Nodes)
        {
            if (node.Devoured || (node.WorldIndex == -1 && node.Kind == NodeKind.Shelter)) continue;
            DrawAtlasNode(node, vdist);
        }
        DrawToken(Project(_tokenPos));
    }

    private void DrawAtlasNode(MapNode node, Dictionary<MapNode, int> vdist)
    {
        bool visible = NodeVisible(node);
        bool explore = CanExplore(node, vdist);
        if (!visible)
        {
            if (explore) DrawFrontier(node);
            return;
        }
        bool visited = _visited.Contains(node);
        bool reachable = explore || CanRevisit(node, vdist);
        Vector2 point = Project(node.Pos);
        float radius = Scaled(node.IsCombat ? AtlasNodeR : AtlasFuncR);
        float alpha = 1f;
        if (VoidSchedule.IsFlickering(_graph, node, _day))
            alpha = 0.45f + 0.45f * Mathf.Sin(_time * 8f);
        if (reachable)
            DrawArc(point, radius + Scaled(5f), 0, Mathf.Tau, 24, new Color(Gold.R, Gold.G, Gold.B, 0.9f), Scaled(2f));

        Color colour = visited ? VisitedGrey : node.Color;
        colour.A = alpha;
        switch (node.Kind)
        {
            case NodeKind.Combat:
                DrawCircle(point, radius, colour);
                DrawArc(point, radius, 0, Mathf.Tau, 22, new Color(0, 0, 0, 0.6f * alpha), Scaled(2f));
                break;
            case NodeKind.Boss:
                DrawDiamond(point, radius + Scaled(4f), colour);
                break;
            case NodeKind.TransportHub:
                DrawTriangle(point, radius + Scaled(1f), colour);
                break;
            case NodeKind.Shelter:
                DrawRect(new Rect2(point - new Vector2(radius, radius), new Vector2(radius * 2f, radius * 2f)), colour);
                break;
            case NodeKind.Event:
                DrawCircle(point, radius, colour);
                DrawString(_font, point + new Vector2(-Scaled(4f), Scaled(5f)), "?", HorizontalAlignment.Left, -1,
                    FontSize(14), new Color(0, 0, 0, alpha));
                break;
            case NodeKind.River:
                Color riverColour = visited ? VisitedGrey : new Color(0.30f, 0.45f, 0.85f);
                riverColour.A = alpha;
                DrawCircle(point, radius - Scaled(1f), riverColour);
                DrawArc(point, radius - Scaled(1f), 0, Mathf.Tau, 18, new Color(0.7f, 0.8f, 1f, alpha), Scaled(1.5f));
                break;
        }
        if (node.IsCombat)
            DrawString(_font, point + new Vector2(-radius, radius + Scaled(13f)), node.Id,
                HorizontalAlignment.Left, -1, FontSize(10), new Color(1, 1, 1, 0.8f * alpha));
    }

    private void DrawRoute(Vector2[] route, Color colour, float width)
    {
        if (route == null || route.Length < 2) return;
        var points = new Vector2[route.Length];
        for (int i = 0; i < route.Length; i++) points[i] = Project(route[i]);
        DrawPolyline(points, colour, width);
    }

    private void DrawWorldDisc(Vector2 center, float radius, Color fill)
    {
        const int segments = 40;
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = Project(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        DrawColoredPolygon(points, fill);
    }

    private void DrawWorldRing(Vector2 center, float radius, Color colour, float width)
    {
        const int segments = 40;
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = Project(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        DrawPolyline(points, colour, Scaled(width));
    }

    private Vector2[] ProjectPoly(Vector2[] poly)
    {
        var projected = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++) projected[i] = Project(poly[i]);
        return projected;
    }

    private static Vector2[] Closed(Vector2[] poly)
    {
        var points = new Vector2[poly.Length + 1];
        System.Array.Copy(poly, points, poly.Length);
        points[^1] = poly[0];
        return points;
    }
}

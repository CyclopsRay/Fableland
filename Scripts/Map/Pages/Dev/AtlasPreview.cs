using Godot;
using Fableland.Map;

namespace Fableland.Map.Pages.Dev;

/// <summary>
/// The restrained atlas view (`MapControllerAtlas.cs`), redrawn from the same
/// <see cref="MapRenderModel.RenderedMap"/> for side-by-side comparison in the art demo. The
/// real one is private to <c>MapController</c> and tied to a run; this copy shows its look —
/// land tint, altitude patches, contours, rivers, roads, the pentagon, the semantic node
/// shapes — and none of its interaction. Drawn in world space: the demo sets the transform.
/// </summary>
public partial class AtlasPreview : Node2D
{
    private static readonly Color Sea = new(0.10f, 0.16f, 0.26f);
    private static readonly Color RoadColor = new(0.82f, 0.68f, 0.44f);
    private static readonly Color RiverWater = new(0.18f, 0.38f, 0.66f);

    private MapGraph _graph;
    private MapRenderModel.RenderedMap _render;

    public void Show(MapGraph graph)
    {
        _graph = graph;
        _render = graph == null ? null : MapRenderModel.Build(graph);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_graph == null || _render == null) return;
        DrawRect(new Rect2(_graph.Center - new Vector2(4000, 4000), new Vector2(8000, 8000)), Sea);
        foreach (MapRenderModel.LandSurface land in _render.Landmasses)
            foreach (Vector2[] piece in land.FillPieces)
                DrawColoredPolygon(piece, land.Fill);
        foreach (MapRenderModel.AltitudePatch patch in _render.AltitudePatches)
            DrawColoredPolygon(patch.Poly, patch.Tint);
        foreach (MapRenderModel.ContourSegment contour in _render.Contours)
            DrawLine(contour.A, contour.B, contour.Height >= 0.58f ? new Color(1f, 1f, 1f, 0.34f) : new Color(0f, 0f, 0f, 0.28f), 0.85f);
        foreach (MapRenderModel.CityTerritory territory in _render.CityTerritories)
            if (territory.City.Devoured)
                foreach (Vector2[] piece in territory.FillPieces)
                    DrawColoredPolygon(piece, new Color(0.05f, 0.03f, 0.08f, 0.68f));
        foreach (MapRenderModel.RiverSurface river in _render.Rivers)
        {
            DrawColoredPolygon(river.Polygon, RiverWater);
            DrawPolyline(river.LeftBank, new Color(0.70f, 0.84f, 1f, 0.78f), 1.4f);
            DrawPolyline(river.RightBank, new Color(0.70f, 0.84f, 1f, 0.78f), 1.4f);
        }
        if (_render.IslandCoast.Length >= 3)
        {
            var closed = new Vector2[_render.IslandCoast.Length + 1];
            _render.IslandCoast.CopyTo(closed, 0);
            closed[^1] = _render.IslandCoast[0];
            DrawPolyline(closed, new Color(0.04f, 0.08f, 0.13f, 0.85f), 2f);
        }
        foreach (MapRenderModel.Road road in _render.Roads)
        {
            if (road.A.Devoured || road.B.Devoured || road.Route == null || road.Route.Length < 2) continue;
            DrawPolyline(road.Route, RoadColor, 3.5f);
        }
        if (_render.Pentagon != null && _render.Pentagon.Length >= 3)
        {
            DrawColoredPolygon(_render.Pentagon, new Color(0.09f, 0.09f, 0.15f));
            DrawCircle(_graph.Center, _graph.LakeRadius, new Color(0.04f, 0.03f, 0.08f));
            DrawArc(_graph.Center, _graph.RiverRadius, 0f, Mathf.Tau, 40, new Color(0.28f, 0.4f, 0.72f, 0.9f), 3f);
        }
        foreach (MapNode node in _graph.Nodes)
        {
            if (node.Devoured) continue;
            float radius = node.IsCombat ? 15f : 9f;
            Vector2 p = node.Pos;
            switch (node.Kind)
            {
                case NodeKind.Combat:
                    DrawCircle(p, radius, node.Color);
                    DrawArc(p, radius, 0f, Mathf.Tau, 22, new Color(0f, 0f, 0f, 0.6f), 2f);
                    break;
                case NodeKind.Boss:
                    float d = radius + 4f;
                    DrawColoredPolygon(new[] { p + new Vector2(0, -d), p + new Vector2(d, 0), p + new Vector2(0, d), p + new Vector2(-d, 0) }, node.Color);
                    break;
                case NodeKind.TransportHub:
                    float t = radius + 1f;
                    DrawColoredPolygon(new[] { p + new Vector2(0, -t), p + new Vector2(t, t * 0.9f), p + new Vector2(-t, t * 0.9f) }, node.Color);
                    break;
                case NodeKind.Shelter:
                    DrawRect(new Rect2(p - new Vector2(radius, radius), new Vector2(radius * 2f, radius * 2f)), node.Color);
                    break;
                case NodeKind.River:
                    DrawCircle(p, radius - 1f, new Color(0.30f, 0.45f, 0.85f));
                    DrawArc(p, radius - 1f, 0f, Mathf.Tau, 18, new Color(0.7f, 0.8f, 1f), 1.5f);
                    break;
                default:
                    DrawCircle(p, radius, node.Color);
                    break;
            }
        }
    }
}

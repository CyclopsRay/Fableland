using System.Collections.Generic;
using Godot;
using Fableland.Map;

/// <summary>
/// Rendered "atlas" view for <see cref="MapController"/> (partial). Draws the precomputed
/// <see cref="MapRenderModel.RenderedMap"/> as a real-worldmap: cities with regime
/// territories, roads for edges, themed AREA barriers (thick terrain belts) where neighbours
/// aren't connected, POINT barriers (landmarks) on blocked would-be roads, golden sea
/// causeways between realms, and a central pentagon VOID.
///
/// Everything is drawn in SCREEN space via <see cref="MapController.Project"/>, so the map can
/// rotate + tilt (the "book on a table" view modes) while node markers and labels stay upright.
/// Live state (visited / devoured / mist / reachable) is layered on the static geometry here.
/// See Docs/MapGDD.md §11.
/// </summary>
public partial class MapController : Node2D
{
    private const float AtlasNodeR = 15f;   // combat/boss city marker
    private const float AtlasFuncR = 9f;    // shelter / question-mark (small territories)

    private static readonly Color Sea = new(0.10f, 0.16f, 0.26f);
    private static readonly Color FogLand = new(0.14f, 0.14f, 0.18f);
    private static readonly Color RoadColor = new(0.82f, 0.68f, 0.44f); // bright tan — clearly a road
    private static readonly Color Causeway = new(0.95f, 0.82f, 0.35f);  // golden sea causeway

    private void DrawAtlas(Dictionary<MapNode, int> vdist)
    {
        var rm = _render;
        if (rm == null) return;

        // Sea backdrop (whole viewport; we draw in screen space now).
        DrawRect(new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size), Sea);

        // --- outer realms: island base -------------------------------------------
        for (int w = 0; w < _graph.Worlds.Count && w < rm.Islands.Count; w++)
        {
            bool shown = !_mist || _revealed.Contains(_graph.Worlds[w].Abbr);
            var poly = ProjectPoly(rm.Islands[w]);
            DrawColoredPolygon(poly, shown ? MapRenderModel.ThemeFor(_graph.Worlds[w].Abbr).Land.Darkened(0.08f) : FogLand);
        }

        // Territory fills + outlines (skip fogged worlds; devoured cities read as consumed VOID).
        foreach (var t in rm.Territories)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[t.WorldIndex].Abbr)) continue;
            // Devoured land reads as dim "dead ruins" (still legible), not pure black VOID, so you
            // never lose sight of where you've been once the VOID eats a ring.
            var fill = t.Node.Devoured ? new Color(0.20f, 0.19f, 0.22f)
                     : _visited.Contains(t.Node) ? t.Fill.Lerp(VisitedGrey, 0.55f)
                     : t.Fill;
            var poly = ProjectPoly(t.Poly);
            DrawColoredPolygon(poly, fill);
            DrawPolyline(Closed(poly), new Color(0, 0, 0, 0.22f), 1.5f);
        }

        // --- AREA barriers: thick themed terrain belts on disconnected frontiers ---
        foreach (var bd in rm.Borders)
        {
            if (bd.Kind != MapRenderModel.BorderKind.Barrier) continue;
            if (_mist && !_revealed.Contains(_graph.Worlds[bd.WorldIndex].Abbr)) continue;
            var col = MapRenderModel.ThemeFor(_graph.Worlds[bd.WorldIndex].Abbr).AreaColor;
            col.A = 0.9f;
            DrawLine(Project(bd.A), Project(bd.B), col, Scaled(15f)); // wide band → reads as an area
        }

        // --- roads (replace edges) -- drawn over the belts so passable routes read clearly ---
        foreach (var rd in rm.Roads)
        {
            if (rd.A.Devoured || rd.B.Devoured) continue;
            if (_mist && !NodeVisible(rd.A) && !NodeVisible(rd.B)) continue;
            var col = rd.CrossWorld ? Causeway : RoadColor;
            DrawRoad(rd.A.Pos, rd.Ctrl, rd.B.Pos, col, Scaled(rd.CrossWorld ? 5f : 3.5f));
        }

        // --- zone 6: pentagon VOID ------------------------------------------------
        bool voidShown = !_mist || _revealed.Contains("XX");
        if (rm.Pentagon != null)
        {
            DrawColoredPolygon(ProjectPoly(rm.Pentagon), voidShown ? new Color(0.09f, 0.09f, 0.15f) : FogLand);
            if (voidShown)
            {
                foreach (var t in rm.Zone6Cells)
                {
                    var fill = t.Node.Devoured ? new Color(0.16f, 0.15f, 0.20f)
                             : _visited.Contains(t.Node) ? t.Fill.Lerp(VisitedGrey, 0.5f) : t.Fill.Lightened(0.05f);
                    var poly = ProjectPoly(t.Poly);
                    DrawColoredPolygon(poly, fill);
                    DrawPolyline(Closed(poly), new Color(0.3f, 0.28f, 0.45f, 0.4f), 1.2f);
                }
                DrawPolyline(Closed(ProjectPoly(rm.Pentagon)), new Color(0.35f, 0.32f, 0.5f, 0.7f), 2f);
                // Lake + river are terrain, so they tilt with the map (projected discs, not upright circles).
                DrawWorldDisc(_graph.Center, _graph.LakeRadius, new Color(0.04f, 0.03f, 0.08f));
                DrawWorldRing(_graph.Center, _graph.RiverRadius, new Color(0.28f, 0.4f, 0.72f, 0.9f), 3f);
            }
        }

        // Isolated XX-S shelters in the sea ring.
        foreach (var s in rm.Islets)
        {
            if (!NodeVisible(s)) continue;
            DrawCircle(Project(s.Pos), Scaled(AtlasFuncR + 4f), new Color(0.16f, 0.20f, 0.30f));
            DrawAtlasNode(s, vdist);
        }

        // --- barrier labels (marking only, per current scope) --------------------
        // Point glyphs on every blocked pass, but the NAME only once per realm to avoid clutter.
        var pointLabeled = new HashSet<int>();
        foreach (var p in rm.Points)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[p.WorldIndex].Abbr)) continue;
            var col = MapRenderModel.ThemeFor(_graph.Worlds[p.WorldIndex].Abbr).PointColor;
            var sp = Project(p.Pos);
            DrawDiamond(sp, Scaled(6f), col);
            if (pointLabeled.Add(p.WorldIndex))
                DrawString(_font, sp + new Vector2(8, 4), p.Label, HorizontalAlignment.Left, -1, FontSize(12), new Color(1, 1, 1, 0.8f));
        }
        foreach (var a in rm.Areas)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[a.WorldIndex].Abbr)) continue;
            DrawString(_font, Project(a.Pos) + new Vector2(-a.Label.Length * 3.5f, 4), a.Label,
                HorizontalAlignment.Left, -1, FontSize(14), new Color(1, 1, 1, 0.6f));
        }

        // --- city markers (icons) + token ----------------------------------------
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured) continue;
            if (n.WorldIndex == -1 && n.Kind == NodeKind.Shelter) continue; // islets drawn above
            DrawAtlasNode(n, vdist);
        }
        DrawToken(Project(_tokenPos));
    }

    private void DrawAtlasNode(MapNode n, Dictionary<MapNode, int> vdist)
    {
        bool vis = NodeVisible(n);
        bool explore = CanExplore(n, vdist);
        if (!vis)
        {
            if (explore) DrawFrontier(n);
            return;
        }
        bool visited = _visited.Contains(n);
        bool reachable = explore || CanRevisit(n, vdist);
        var p = Project(n.Pos);
        float r = Scaled(n.IsCombat ? AtlasNodeR : (n.WorldIndex == -2 ? AtlasFuncR : AtlasNodeR - 3f));

        float alpha = 1f;
        if (DevourDay.TryGetValue(n.LevelTag, out int d) && d == _day)
            alpha = 0.45f + 0.45f * Mathf.Sin(_time * 8f);

        if (reachable)
            DrawArc(p, r + Scaled(5f), 0, Mathf.Tau, 24, new Color(Gold.R, Gold.G, Gold.B, 0.9f), 2f);

        var c = visited ? VisitedGrey : n.Color;
        c.A = alpha;

        switch (n.Kind)
        {
            case NodeKind.Combat:
                DrawCircle(p, r, c);
                DrawArc(p, r, 0, Mathf.Tau, 22, new Color(0, 0, 0, 0.6f * alpha), 2f);
                break;
            case NodeKind.Boss:
                DrawDiamond(p, r + Scaled(4f), c);
                break;
            case NodeKind.Shelter:
                DrawTriangle(p, r + Scaled(1f), c);
                break;
            case NodeKind.QuestionMark:
                DrawCircle(p, r, c);
                DrawString(_font, p + new Vector2(-Scaled(4f), Scaled(5f)), "?", HorizontalAlignment.Left, -1, FontSize(14), new Color(0, 0, 0, alpha));
                break;
            case NodeKind.River:
                var rc = visited ? VisitedGrey : new Color(0.30f, 0.45f, 0.85f);
                rc.A = alpha;
                DrawCircle(p, r - Scaled(1f), rc);
                DrawArc(p, r - Scaled(1f), 0, Mathf.Tau, 18, new Color(0.7f, 0.8f, 1f, alpha), 1.5f);
                break;
        }

        if (n.IsCombat)
            DrawString(_font, p + new Vector2(-r, r + Scaled(13f)), n.Id,
                HorizontalAlignment.Left, -1, FontSize(10), new Color(1, 1, 1, 0.8f * alpha));
    }

    /// <summary>Sampled quadratic-bezier road, projected to screen.</summary>
    private void DrawRoad(Vector2 a, Vector2 ctrl, Vector2 b, Color col, float width)
    {
        const int seg = 12;
        var pts = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float t = (float)i / seg, u = 1f - t;
            pts[i] = Project(u * u * a + 2f * u * t * ctrl + t * t * b);
        }
        DrawPolyline(pts, col, width);
    }

    /// <summary>A world-space circle drawn as a projected polygon, so it tilts with the map.</summary>
    private void DrawWorldDisc(Vector2 center, float radius, Color fill)
    {
        const int seg = 40;
        var pts = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = Mathf.Tau * i / seg;
            pts[i] = Project(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
        DrawColoredPolygon(pts, fill);
    }

    /// <summary>A world-space ring drawn as a projected closed polyline, so it tilts with the map.</summary>
    private void DrawWorldRing(Vector2 center, float radius, Color col, float width)
    {
        const int seg = 40;
        var pts = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.Tau * i / seg;
            pts[i] = Project(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
        DrawPolyline(pts, col, width);
    }

    private Vector2[] ProjectPoly(Vector2[] poly)
    {
        var outP = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++) outP[i] = Project(poly[i]);
        return outP;
    }

    private static Vector2[] Closed(Vector2[] poly)
    {
        var pts = new Vector2[poly.Length + 1];
        System.Array.Copy(poly, pts, poly.Length);
        pts[poly.Length] = poly[0];
        return pts;
    }
}

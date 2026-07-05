using System.Collections.Generic;
using Godot;
using Fableland.Map;

/// <summary>
/// Rendered "atlas" view for <see cref="MapController"/> (partial). Draws the precomputed
/// <see cref="MapRenderModel.RenderedMap"/> as a real-worldmap: cities with regime
/// territories, roads for edges, themed barriers where neighbours aren't connected, golden
/// sea causeways between realms, and a central pentagon VOID. Live state (visited / devoured /
/// mist / reachable) is layered on the static geometry here. See Docs/MapGDD.md §11.
/// </summary>
public partial class MapController : Node2D
{
    private const float AtlasNodeR = 15f;   // combat/boss city marker
    private const float AtlasFuncR = 9f;    // shelter / question-mark (small territories)

    private static readonly Color Sea = new(0.10f, 0.16f, 0.26f);
    private static readonly Color FogLand = new(0.14f, 0.14f, 0.18f);
    private static readonly Color RoadColor = new(0.48f, 0.36f, 0.22f);
    private static readonly Color Causeway = new(0.93f, 0.80f, 0.36f); // golden sea causeway

    private void DrawAtlas(Dictionary<MapNode, int> vdist)
    {
        var rm = _render;
        if (rm == null) return;

        // Sea backdrop.
        float span = MapGenerator.RimRadius + 140f;
        DrawRect(new Rect2(_graph.Center - new Vector2(span, span), new Vector2(span * 2, span * 2)), Sea);

        // --- outer realms: island base + territories -----------------------------
        for (int w = 0; w < _graph.Worlds.Count; w++)
        {
            bool shown = !_mist || _revealed.Contains(_graph.Worlds[w].Abbr);
            if (w < rm.Islands.Count)
            {
                var island = rm.Islands[w];
                if (!shown) { DrawColoredPolygon(island, FogLand); continue; }
                DrawColoredPolygon(island, MapRenderModel.ThemeFor(_graph.Worlds[w].Abbr).Land.Darkened(0.15f));
            }
        }

        // Territory fills + outlines (skip fogged worlds; devoured cities read as consumed VOID).
        foreach (var t in rm.Territories)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[t.WorldIndex].Abbr)) continue;
            var fill = t.Node.Devoured ? new Color(0.06f, 0.06f, 0.10f)
                     : _visited.Contains(t.Node) ? t.Fill.Lerp(VisitedGrey, 0.55f)
                     : t.Fill;
            DrawColoredPolygon(t.Poly, fill);
            DrawPolyline(Closed(t.Poly), new Color(0, 0, 0, 0.25f), 1.5f);
        }

        // --- barriers: themed thick strokes on disconnected frontiers -------------
        foreach (var bd in rm.Borders)
        {
            if (bd.Kind != MapRenderModel.BorderKind.Barrier) continue;
            if (_mist && !_revealed.Contains(_graph.Worlds[bd.WorldIndex].Abbr)) continue;
            DrawLine(bd.A, bd.B, MapRenderModel.ThemeFor(_graph.Worlds[bd.WorldIndex].Abbr).AreaColor, 5f);
        }

        // --- roads (replace edges) ------------------------------------------------
        foreach (var rd in rm.Roads)
        {
            if (rd.A.Devoured || rd.B.Devoured) continue;
            if (_mist && !NodeVisible(rd.A) && !NodeVisible(rd.B)) continue;
            var col = rd.CrossWorld ? Causeway : RoadColor;
            DrawRoad(rd.A.Pos, rd.Ctrl, rd.B.Pos, col, rd.CrossWorld ? 5f : 3.5f);
        }

        // --- zone 6: pentagon VOID ------------------------------------------------
        bool voidShown = !_mist || _revealed.Contains("XX");
        if (rm.Pentagon != null)
        {
            DrawColoredPolygon(rm.Pentagon, voidShown ? new Color(0.09f, 0.09f, 0.15f) : FogLand);
            if (voidShown)
            {
                foreach (var t in rm.Zone6Cells)
                {
                    var fill = t.Node.Devoured ? new Color(0.05f, 0.05f, 0.09f)
                             : _visited.Contains(t.Node) ? t.Fill.Lerp(VisitedGrey, 0.5f) : t.Fill.Lightened(0.05f);
                    DrawColoredPolygon(t.Poly, fill);
                    DrawPolyline(Closed(t.Poly), new Color(0.3f, 0.28f, 0.45f, 0.4f), 1.2f);
                }
                DrawPolyline(Closed(rm.Pentagon), new Color(0.35f, 0.32f, 0.5f, 0.7f), 2f);
                DrawCircle(_graph.Center, _graph.LakeRadius, new Color(0.04f, 0.03f, 0.08f));
                DrawArc(_graph.Center, _graph.RiverRadius, 0, Mathf.Tau, 48, new Color(0.28f, 0.4f, 0.72f, 0.9f), 3f);
            }
        }

        // Isolated XX-S shelters in the sea ring.
        foreach (var s in rm.Islets)
        {
            if (!NodeVisible(s)) continue;
            DrawCircle(s.Pos, AtlasFuncR + 4f, new Color(0.16f, 0.20f, 0.30f));
            DrawAtlasNode(s, vdist);
        }

        // --- barrier labels (marking only, per current scope) --------------------
        foreach (var p in rm.Points)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[p.WorldIndex].Abbr)) continue;
            var col = MapRenderModel.ThemeFor(_graph.Worlds[p.WorldIndex].Abbr).PointColor;
            DrawDiamond(p.Pos, 6f, col);
            DrawString(_font, p.Pos + new Vector2(8, 4), p.Label, HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.75f));
        }
        foreach (var a in rm.Areas)
        {
            if (_mist && !_revealed.Contains(_graph.Worlds[a.WorldIndex].Abbr)) continue;
            DrawString(_font, a.Pos + new Vector2(-a.Label.Length * 3.5f, 4), a.Label,
                HorizontalAlignment.Left, -1, 13, new Color(1, 1, 1, 0.55f));
        }

        // --- city markers (icons) + token ----------------------------------------
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured) continue;
            if (n.WorldIndex == -1 && n.Kind == NodeKind.Shelter) continue; // islets drawn above
            DrawAtlasNode(n, vdist);
        }
        DrawToken(_tokenPos);
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
        float r = n.IsCombat ? AtlasNodeR : (n.WorldIndex == -2 ? AtlasFuncR : AtlasNodeR - 3f);

        float alpha = 1f;
        if (DevourDay.TryGetValue(n.LevelTag, out int d) && d == _day)
            alpha = 0.45f + 0.45f * Mathf.Sin(_time * 8f);

        if (reachable)
            DrawArc(n.Pos, r + 5f, 0, Mathf.Tau, 24, new Color(Gold.R, Gold.G, Gold.B, 0.9f), 2f);

        var c = visited ? VisitedGrey : n.Color;
        c.A = alpha;

        switch (n.Kind)
        {
            case NodeKind.Combat:
                DrawCircle(n.Pos, r, c);
                DrawArc(n.Pos, r, 0, Mathf.Tau, 22, new Color(0, 0, 0, 0.6f * alpha), 2f);
                break;
            case NodeKind.Boss:
                DrawDiamond(n.Pos, r + 4f, c);
                break;
            case NodeKind.Shelter:
                DrawTriangle(n.Pos, r + 1f, c);
                break;
            case NodeKind.QuestionMark:
                DrawCircle(n.Pos, r, c);
                DrawString(_font, n.Pos + new Vector2(-4, 5), "?", HorizontalAlignment.Left, -1, 14, new Color(0, 0, 0, alpha));
                break;
            case NodeKind.River:
                var rc = visited ? VisitedGrey : new Color(0.30f, 0.45f, 0.85f);
                rc.A = alpha;
                DrawCircle(n.Pos, r - 1f, rc);
                DrawArc(n.Pos, r - 1f, 0, Mathf.Tau, 18, new Color(0.7f, 0.8f, 1f, alpha), 1.5f);
                break;
        }

        if (n.IsCombat)
            DrawString(_font, n.Pos + new Vector2(-r, r + 13f), n.Id,
                HorizontalAlignment.Left, -1, 10, new Color(1, 1, 1, 0.8f * alpha));
    }

    /// <summary>Sampled quadratic-bezier road.</summary>
    private void DrawRoad(Vector2 a, Vector2 ctrl, Vector2 b, Color col, float width)
    {
        const int seg = 12;
        var pts = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float t = (float)i / seg, u = 1f - t;
            pts[i] = u * u * a + 2f * u * t * ctrl + t * t * b;
        }
        DrawPolyline(pts, col, width);
    }

    private static Vector2[] Closed(Vector2[] poly)
    {
        var pts = new Vector2[poly.Length + 1];
        System.Array.Copy(poly, pts, poly.Length);
        pts[poly.Length] = poly[0];
        return pts;
    }
}

using System.Collections.Generic;
using Godot;
using Fableland.Map;

/// <summary>
/// Root of the Map scene. Owns the generated <see cref="MapGraph"/>, draws it, and
/// runs the debug loop: seed entry / dice reroll, day + stamina, Rest, Mist toggle, and
/// click-to-move for the player token. The VOID devours the outer rings on a schedule.
///
/// Day / movement rule: you spend stamina walking across nodes you've ALREADY visited;
/// the day ends the moment you step onto a NEW node (it becomes your camp) OR when you
/// run stamina to 0 among visited nodes. Either way the next day refreshes stamina to 5.
/// Visited nodes render grey.
///
/// Mist (fog of war): when on, you only see worlds you've entered — plus the bridge edges
/// out of them and any function node sitting on a bridge. Unentered worlds and the VOID
/// stay dark until you set foot in them.
///
/// Prototype/debug harness — nodes only differ by icon; node CONTENT is not implemented.
/// See Docs/MapGDD.md.
/// </summary>
public partial class MapController : Node2D
{
    // The day each outer sublevel is eaten by the VOID (spec: dawns 10,20,30,35,40,45).
    private static readonly Dictionary<string, int> DevourDay = new()
    {
        ["1-A"] = 10, ["1-B"] = 20, ["2-A"] = 30, ["2-B"] = 35, ["3"] = 40, ["4"] = 45,
    };
    private const int MaxStamina = 5;
    private const float NodeRadius = 11f;
    private const float ClickRadius = 18f;
    private const float WedgeDeg = 62f;    // world background wedge width (< 72° so worlds show a gap)

    private static readonly Color Gold = new(0.91f, 0.76f, 0.42f);
    private static readonly Color VisitedGrey = new(0.42f, 0.42f, 0.47f);

    private MapGraph _graph;
    private MapNode _current;
    private int _day = 1;
    private int _stamina = MaxStamina;

    private readonly HashSet<MapNode> _visited = new();
    private readonly HashSet<string> _revealed = new();  // regions entered: world abbrs and "XX"
    private bool _mist;

    private Vector2 _tokenPos;   // smoothed visual position of the player token
    private float _time;         // for flicker + token lerp
    private Font _font;

    // Rendered atlas view (v0.3.2): the schematic graph turned into a real-worldmap.
    private bool _rendered = true;                 // start in the atlas; toggle back to schematic
    private MapRenderModel.RenderedMap _render;
    private Camera2D _cam;
    private bool _panning;
    private static readonly Vector2 ZoomLimit = new(0.15f, 3f);

    // UI
    private LineEdit _seedEdit;
    private Label _infoLabel;
    private Button _mistButton;
    private Button _renderButton;

    public override void _Ready()
    {
        _seedEdit = GetNode<LineEdit>("UI/SeedEdit");
        _infoLabel = GetNode<Label>("UI/InfoLabel");
        _font = _infoLabel.GetThemeDefaultFont();
        _mistButton = GetNode<Button>("UI/MistButton");
        _renderButton = GetNode<Button>("UI/RenderButton");
        GetNode<Button>("UI/DiceButton").Pressed += OnDice;
        GetNode<Button>("UI/RestButton").Pressed += OnRest;
        _mistButton.Pressed += OnToggleMist;
        _renderButton.Pressed += OnToggleRender;
        _seedEdit.TextSubmitted += OnSeedSubmitted;
        GetNode<Label>("UI/VersionLabel").Text = "v" + GameVersion.Current;

        _cam = GetNode<Camera2D>("Cam");
        _cam.MakeCurrent();
        _renderButton.Text = _rendered ? "View: atlas" : "View: schematic";
        FitCamera();

        Restart(DetRandom.NewSeed());
    }

    /// <summary>Center the camera and zoom so the whole map fits the viewport.</summary>
    private void FitCamera()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float worldSpan = 2f * (MapGenerator.RimRadius + 60f);
        float z = Mathf.Min(vp.X / worldSpan, vp.Y / worldSpan);
        z = Mathf.Clamp(z, ZoomLimit.X, ZoomLimit.Y);
        _cam.Zoom = new Vector2(z, z);
        _cam.Position = MapGenerator.Center;
    }

    private void OnToggleRender()
    {
        _rendered = !_rendered;
        _renderButton.Text = _rendered ? "View: atlas" : "View: schematic";
        QueueRedraw();
    }

    private void Restart(string seed)
    {
        seed = string.IsNullOrWhiteSpace(seed) ? DetRandom.NewSeed() : seed.Trim().ToUpperInvariant();
        _graph = MapGenerator.Generate(seed);
        _render = MapRenderModel.Build(_graph);
        _current = _graph.StartNode;
        _tokenPos = _current.Pos;
        _day = 1;
        _stamina = MaxStamina;
        _visited.Clear();
        _revealed.Clear();
        _visited.Add(_current);
        AddRevealed(_current);
        _seedEdit.Text = seed;
        UpdateInfo();
        QueueRedraw();
    }

    private void OnDice() => Restart(DetRandom.NewSeed());
    private void OnSeedSubmitted(string text) => Restart(text);
    private void OnRest() { EndDay(); QueueRedraw(); }

    private void OnToggleMist()
    {
        _mist = !_mist;
        _mistButton.Text = _mist ? "Mist: on" : "Mist: off";
        QueueRedraw();
    }

    /// <summary>Region a node belongs to for mist purposes: world abbr, "XX", or null for function nodes.</summary>
    private static string Region(MapNode n) => n.WorldIndex == -1 ? "XX" : (n.WorldIndex == -2 ? null : n.Zone);

    private void AddRevealed(MapNode n) { var r = Region(n); if (r != null) _revealed.Add(r); }

    /// <summary>End the current day: apply the VOID devour, advance the day, refresh stamina.</summary>
    private void EndDay()
    {
        foreach (var n in _graph.Nodes)
            if (DevourDay.TryGetValue(n.LevelTag, out int d) && d == _day)
                n.Devoured = true;

        // Function nodes sit on edges between combat nodes; once every neighbour is eaten
        // the function node is orphaned, so the VOID takes it too (no floating shelters).
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured || n.WorldIndex != -2) continue; // -2 == function node
            bool anyAlive = false;
            foreach (var e in _graph.EdgesOf(n))
                if (!e.Other(n).Devoured) { anyAlive = true; break; }
            if (!anyAlive) n.Devoured = true;
        }

        _day++;
        _stamina = MaxStamina;
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        int fights = 0, camps = 0, marks = 0;
        foreach (var n in _visited)
        {
            if (n.Kind == NodeKind.Combat || n.Kind == NodeKind.Boss) fights++;
            else if (n.Kind == NodeKind.Shelter) camps++;
            else if (n.Kind == NodeKind.QuestionMark) marks++;
        }
        _infoLabel.Text = $"Day {_day}   Stamina {_stamina}/{MaxStamina}\n" +
                          $"Traversed {_visited.Count}\n" +
                          $"fights {fights}  camps {camps}  ? {marks}";
    }

    // ---- movement --------------------------------------------------------------

    /// <summary>Steps from the current node reachable by walking ONLY through already-visited nodes.</summary>
    private Dictionary<MapNode, int> VisitedSteps()
    {
        var dist = new Dictionary<MapNode, int> { [_current] = 0 };
        var q = new Queue<MapNode>();
        q.Enqueue(_current);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in _graph.EdgesOf(cur))
            {
                var nxt = e.Other(cur);
                if (nxt.Devoured || !_visited.Contains(nxt)) continue;
                if (cur.WorldIndex == -1 && nxt.WorldIndex != -1) continue; // one-way out of the VOID
                if (dist.ContainsKey(nxt)) continue;
                dist[nxt] = dist[cur] + 1;
                q.Enqueue(nxt);
            }
        }
        return dist;
    }

    /// <summary>Cost to step onto a NEW node u: cheapest visited neighbour's distance + 1, or -1 if unreachable.</summary>
    private int ExploreCost(MapNode u, Dictionary<MapNode, int> vdist)
    {
        if (u.Devoured || _visited.Contains(u)) return -1;
        int best = int.MaxValue;
        foreach (var e in _graph.EdgesOf(u))
        {
            var n = e.Other(u);
            if (n.Devoured || !vdist.TryGetValue(n, out int d)) continue;
            if (n.WorldIndex == -1 && u.WorldIndex != -1) continue; // one-way out of the VOID
            if (d + 1 < best) best = d + 1;
        }
        return best == int.MaxValue ? -1 : best;
    }

    private bool CanRevisit(MapNode n, Dictionary<MapNode, int> vdist)
        => _visited.Contains(n) && n != _current && vdist.TryGetValue(n, out int c) && c > 0 && c <= _stamina;

    private bool CanExplore(MapNode n, Dictionary<MapNode, int> vdist)
    {
        if (_visited.Contains(n)) return false;
        int c = ExploreCost(n, vdist);
        return c > 0 && c <= _stamina;
    }

    private void TryMove(MapNode target, Dictionary<MapNode, int> vdist)
    {
        if (target == null || target == _current) return;

        if (_visited.Contains(target))
        {
            if (!CanRevisit(target, vdist)) return;
            _current = target;
            _stamina -= vdist[target];
            if (_stamina <= 0) EndDay(); else UpdateInfo();
        }
        else
        {
            int c = ExploreCost(target, vdist);
            if (c <= 0 || c > _stamina) return;
            _current = target;
            _stamina -= c;
            _visited.Add(target);
            AddRevealed(target);
            EndDay(); // reaching a NEW node ends the day
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _tokenPos = _tokenPos.Lerp(_current.Pos, Mathf.Min(1f, (float)delta * 12f));
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Camera: right/middle-drag to pan, wheel to zoom about the cursor.
        if (@event is InputEventMouseButton cam)
        {
            if (cam.ButtonIndex is MouseButton.Right or MouseButton.Middle)
            {
                _panning = cam.Pressed;
                return;
            }
            if (cam.Pressed && cam.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                ZoomAtCursor(cam.ButtonIndex == MouseButton.WheelUp ? 1.12f : 1f / 1.12f);
                return;
            }
        }
        if (@event is InputEventMouseMotion motion && _panning)
        {
            _cam.Position -= motion.Relative / _cam.Zoom; // screen delta → world delta
            return;
        }

        if (@event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
            return;

        // Hit-test in WORLD space (a Camera2D now transforms the view — see KNOWLEDGE.md).
        var world = GetGlobalMousePosition();
        float pickR = ClickRadius / Mathf.Max(0.001f, _cam.Zoom.X); // keep click tolerance in screen px
        MapNode target = null;
        float best = pickR * pickR;
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured) continue;
            float d = n.Pos.DistanceSquaredTo(world);
            if (d < best) { best = d; target = n; }
        }
        if (target != null) TryMove(target, VisitedSteps());
    }

    private void ZoomAtCursor(float factor)
    {
        var before = GetGlobalMousePosition();
        float z = Mathf.Clamp(_cam.Zoom.X * factor, ZoomLimit.X, ZoomLimit.Y);
        _cam.Zoom = new Vector2(z, z);
        _cam.Position += before - GetGlobalMousePosition(); // keep the point under the cursor fixed
    }

    // ---- mist visibility -------------------------------------------------------

    private bool NodeVisible(MapNode n)
    {
        if (!_mist || _visited.Contains(n)) return true;
        string r = Region(n);
        if (r == "XX") return _revealed.Contains("XX");
        if (r != null) return _revealed.Contains(r);
        // function node: visible if it touches a combat/boss node in a revealed outer world
        foreach (var e in _graph.EdgesOf(n))
        {
            var m = e.Other(n);
            if (m.WorldIndex >= 0 && _revealed.Contains(m.Zone)) return true;
        }
        return false;
    }

    // ---- drawing ---------------------------------------------------------------

    public override void _Draw()
    {
        if (_graph == null) return;
        var vdist = VisitedSteps();

        if (_rendered) { DrawAtlas(vdist); return; }

        for (int w = 0; w < _graph.Worlds.Count; w++)
            if (!_mist || _revealed.Contains(_graph.Worlds[w].Abbr))
                DrawWedge(w, _graph.Worlds[w].Color);

        // Zone 6 (the VOID): dark disc containing the lv5 ring, the lake, the river ring.
        bool voidSeen = !_mist || _revealed.Contains("XX");
        DrawCircle(_graph.Center, _graph.Zone6Radius, new Color(0.02f, 0.02f, 0.04f));
        if (voidSeen)
        {
            DrawArc(_graph.Center, _graph.Zone6Radius, 0, Mathf.Tau, 64, new Color(0.30f, 0.28f, 0.45f, 0.35f), 1.5f);
            DrawCircle(_graph.Center, _graph.LakeRadius, new Color(0.04f, 0.03f, 0.07f));
            DrawArc(_graph.Center, _graph.RiverRadius, 0, Mathf.Tau, 48, new Color(0.25f, 0.35f, 0.65f, 0.9f), 3f);
        }

        // Edges — drawn if one endpoint is visible (so bridge edges out of a revealed world show).
        foreach (var e in _graph.Edges)
        {
            if (!e.Visible || e.A.Devoured || e.B.Devoured) continue;
            if (_mist && !NodeVisible(e.A) && !NodeVisible(e.B)) continue;
            DrawLine(e.A.Pos, e.B.Pos, new Color(0.55f, 0.55f, 0.55f, 0.75f), 1.5f);
        }

        // Nodes.
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured) continue;
            bool vis = NodeVisible(n);
            bool explore = CanExplore(n, vdist);
            if (!vis)
            {
                if (explore) DrawFrontier(n); // fogged but you can step into it
                continue;
            }
            DrawNode(n, _visited.Contains(n), explore || CanRevisit(n, vdist));
        }

        DrawToken(_tokenPos);
    }

    private void DrawWedge(int w, Color color)
    {
        float startDeg = -90f + w * 72f - WedgeDeg / 2f;
        var pts = new List<Vector2> { _graph.Center };
        int seg = 12;
        for (int i = 0; i <= seg; i++)
        {
            float deg = startDeg + WedgeDeg * i / seg;
            float rad = Mathf.DegToRad(deg);
            pts.Add(_graph.Center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * MapGenerator.RimRadius);
        }
        DrawColoredPolygon(pts.ToArray(), new Color(color.R, color.G, color.B, 0.12f));
    }

    private void DrawFrontier(MapNode n)
    {
        // Unknown node you may explore into: a gold ring around a dim marker.
        DrawArc(n.Pos, NodeRadius + 3f, 0, Mathf.Tau, 20, Gold, 1.5f);
        DrawCircle(n.Pos, 5f, new Color(0.3f, 0.3f, 0.35f, 0.8f));
    }

    private void DrawNode(MapNode n, bool visited, bool reachable)
    {
        float alpha = 1f;
        if (DevourDay.TryGetValue(n.LevelTag, out int d) && d == _day)
            alpha = 0.45f + 0.45f * Mathf.Sin(_time * 8f);

        if (reachable)
            DrawArc(n.Pos, NodeRadius + 4f, 0, Mathf.Tau, 22, new Color(Gold.R, Gold.G, Gold.B, 0.85f), 1.5f);

        var c = visited ? VisitedGrey : n.Color;
        c.A = alpha;

        switch (n.Kind)
        {
            case NodeKind.Combat:
                DrawCircle(n.Pos, NodeRadius, c);
                DrawArc(n.Pos, NodeRadius, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.6f * alpha), 1.5f);
                break;
            case NodeKind.Boss:
                DrawDiamond(n.Pos, NodeRadius + 3f, c);
                break;
            case NodeKind.Shelter:
                DrawTriangle(n.Pos, NodeRadius + 1f, c);
                break;
            case NodeKind.QuestionMark:
                DrawCircle(n.Pos, NodeRadius, c);
                DrawString(_font, n.Pos + new Vector2(-4, 5), "?",
                    HorizontalAlignment.Left, -1, 14, new Color(0, 0, 0, alpha));
                break;
            case NodeKind.River: // selectable marker sitting on the river ring
                var rc = visited ? VisitedGrey : new Color(0.30f, 0.45f, 0.85f);
                rc.A = alpha;
                DrawCircle(n.Pos, NodeRadius - 1f, rc);
                DrawArc(n.Pos, NodeRadius - 1f, 0, Mathf.Tau, 18, new Color(0.7f, 0.8f, 1f, alpha), 1.5f);
                break;
        }

        if (n.IsCombat)
            DrawString(_font, n.Pos + new Vector2(-NodeRadius, NodeRadius + 11f), n.Id,
                HorizontalAlignment.Left, -1, 9, new Color(1, 1, 1, 0.7f * alpha));
    }

    private void DrawToken(Vector2 p)
    {
        DrawCircle(p, 9f, new Color(1f, 0.9f, 0.2f));
        DrawArc(p, 9f, 0, Mathf.Tau, 16, new Color(0, 0, 0, 0.8f), 1.5f);
        DrawCircle(p + new Vector2(-3, -2), 1.4f, Colors.Black);
        DrawCircle(p + new Vector2(3, -2), 1.4f, Colors.Black);
        DrawArc(p, 5f, Mathf.DegToRad(20), Mathf.DegToRad(160), 10, Colors.Black, 1.4f);
    }

    private void DrawDiamond(Vector2 p, float r, Color c)
    {
        var pts = new[] { p + new Vector2(0, -r), p + new Vector2(r, 0), p + new Vector2(0, r), p + new Vector2(-r, 0) };
        DrawColoredPolygon(pts, c);
    }

    private void DrawTriangle(Vector2 p, float r, Color c)
    {
        var pts = new[] { p + new Vector2(0, -r), p + new Vector2(r, r * 0.9f), p + new Vector2(-r, r * 0.9f) };
        DrawColoredPolygon(pts, c);
    }
}

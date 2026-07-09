using System.Collections.Generic;
using Godot;
using Fableland.Map;
using Fableland.Run;
using Fableland.Debug;

/// <summary>
/// Root of the Map scene — the <b>Exploration-mode view + input layer</b> (v0.5.0). All run
/// state (day, stamina, visited/completed nodes, VOID latch, the graph) is OWNED BY
/// <see cref="RunState"/>; this class reads it, draws it, and turns clicks into moves. It keeps
/// thin local caches (_graph/_current/_visited/_revealed), rebuilt from RunState on scene load.
///
/// Movement (NODES §1.3/§7.1): moving between visited nodes costs 1 stamina/edge; entering an
/// unvisited node — or re-entering an unconquered combat node, a shelter, or an unresolved "?" —
/// hands off to <see cref="RunState.BeginAdventure"/> (which swaps to the Adventure scene). The
/// day ends only via the "Finish the Day" button → <see cref="RunState.EndDay"/> (no longer on
/// node arrival). Paths cannot pass through unvisited nodes or unconquered combat nodes.
///
/// Mist (fog of war): when on, you only see worlds you've entered — plus the bridge edges out of
/// them and any function node sitting on a bridge. Unentered worlds and the VOID stay dark.
///
/// Prototype/debug harness — see Docs/MapGDD.md / Docs/NODES.gdd.
/// </summary>
public partial class MapController : Node2D
{
    // The VOID devour schedule now lives on the map layer as VoidSchedule.DevourDay (shared with
    // RunState's day-end pipeline). Kept here as an alias so view code reads it unchanged.
    private static Dictionary<string, int> DevourDay => VoidSchedule.DevourDay;

    private const int MaxStamina = RunState.MaxStamina;
    private const float NodeRadius = 11f;
    private const float ClickRadius = 18f;
    private const float WedgeDeg = 62f;    // world background wedge width (< 72° so worlds show a gap)

    private static readonly Color Gold = new(0.91f, 0.76f, 0.42f);
    private static readonly Color VisitedGrey = new(0.42f, 0.42f, 0.47f);

    private MapGraph _graph;
    private MapNode _current;

    // Day / stamina / void latch are OWNED BY RunState now (one-owner rule). These read-only
    // aliases keep the view code (and the atlas partial) reading them unchanged, and stay
    // null-tolerant so the map is still launchable straight from F5 before the run inits.
    private int _day => RunState.Instance?.Day ?? 1;
    private int _stamina => RunState.Instance?.Stamina ?? MaxStamina;
    private bool _inVoid => RunState.Instance?.InVoid ?? false;

    // Local view caches, rebuilt from RunState on scene load (SyncFromRunState). MapController is
    // the only writer of visited/current DURING exploration, always writing through to RunState.
    private readonly HashSet<MapNode> _visited = new();
    private readonly HashSet<string> _revealed = new();  // regions entered: world abbrs and "XX"
    private bool _mist;

    private Vector2 _tokenPos;   // smoothed visual position of the player token
    private float _time;         // for flicker + token lerp
    private Font _font;

    // Rendered atlas view (v0.3.2) + view modes (v0.3.3). Instead of a Camera2D we project
    // world→screen ourselves (see Project): this lets the map rotate + tilt like a board on a
    // table while node markers/labels stay upright. Three modes:
    //   Flat       — top-down, free pan/zoom (the "current vision", a).
    //   BossUp     — tilted; the map spins so the VOID (final boss) is always UP, player at
    //                the bottom, so you're always heading toward the finale (b).
    //   HeadingUp  — tilted; after each move the map spins so the step you just took points UP (c).
    private bool _rendered = true;                 // start in the atlas; toggle back to schematic
    private MapRenderModel.RenderedMap _render;

    public enum ViewMode { Flat, BossUp, HeadingUp }
    private ViewMode _mode = ViewMode.Flat;
    private float _zoom = 1f;
    private Vector2 _pan;              // Flat-mode pan (screen px)
    private bool _dragging;
    private float _rot, _rotTarget;    // view rotation (radians), smoothed toward the target
    private float _tilt = 1f;          // vertical foreshorten — the "tilted book on a table" look
    private Vector2 _lastMoveDir = new(0, -1);
    private Vector2 _anchor;           // screen point the focus is pinned to (recomputed each _Draw)
    private const float TiltFactor = 0.62f;   // how far the map leans back in the tilted modes
    private static readonly Vector2 ZoomLimit = new(0.2f, 4f);

    // UI
    private LineEdit _seedEdit;
    private Label _infoLabel;
    private Button _mistButton;
    private Button _renderButton;
    private Button _viewModeButton;

    // Day-end summary toast (T30 §5 residual, v0.5.0): shown once on scene load if RunState left a
    // LastDayEndSummary, auto-hides after ToastDuration or on the next click (whichever first).
    private Label _toastLabel;
    private float _toastTimer;
    private const float ToastDuration = 5f;

    public override void _Ready()
    {
        _seedEdit = GetNode<LineEdit>("UI/SeedEdit");
        _infoLabel = GetNode<Label>("UI/InfoLabel");
        _font = _infoLabel.GetThemeDefaultFont();
        _mistButton = GetNode<Button>("UI/MistButton");
        _renderButton = GetNode<Button>("UI/RenderButton");
        GetNode<Button>("UI/DiceButton").Pressed += OnDice;
        GetNode<Button>("UI/FinishDayButton").Pressed += OnFinishDay;
        _mistButton.Pressed += OnToggleMist;
        _renderButton.Pressed += OnToggleRender;
        _seedEdit.TextSubmitted += OnSeedSubmitted;
        GetNode<Label>("UI/VersionLabel").Text = "v" + GameVersion.Current;

        _viewModeButton = GetNode<Button>("UI/ViewModeButton");
        _viewModeButton.Pressed += OnCycleViewMode;
        _renderButton.Text = _rendered ? "View: atlas" : "View: schematic";
        UpdateViewModeButton();
        FitZoom();

        _toastLabel = GetNode<Label>("UI/ToastLabel");
        _toastLabel.Visible = false;

        // Null-tolerant boot: if no run is in progress (e.g. F5 straight into Map.tscn), start one
        // so the map stays directly launchable. Otherwise adopt the run RunState already holds.
        var rs = RunState.Instance;
        if (rs == null || rs.Graph == null) rs?.NewRun(DetRandom.NewSeed());
        SyncFromRunState();
        _seedEdit.Text = rs?.Seed ?? "";
        UpdateInfo();
        ShowDayEndToastIfAny();
        QueueRedraw();
    }

    /// <summary>
    /// Show RunState.LastDayEndSummary as a transient toast, once (cleared immediately so a
    /// re-sync — e.g. Restart — doesn't repeat it). Null-tolerant: no run / no summary = no-op.
    /// </summary>
    private void ShowDayEndToastIfAny()
    {
        var rs = RunState.Instance;
        string msg = rs?.LastDayEndSummary;
        if (string.IsNullOrEmpty(msg)) return;
        _toastLabel.Text = msg;
        _toastLabel.Visible = true;
        _toastTimer = ToastDuration;
        rs.LastDayEndSummary = ""; // shows once
    }

    /// <summary>Rebuild the local view caches (_graph, _current, _visited, _revealed) from RunState.</summary>
    private void SyncFromRunState()
    {
        var rs = RunState.Instance;
        if (rs?.Graph == null) return;
        _graph = rs.Graph;
        _render = MapRenderModel.Build(_graph);
        _current = rs.FindNode(rs.CurrentNodeId) ?? _graph.StartNode;
        _tokenPos = _current.Pos;

        _visited.Clear();
        foreach (var n in _graph.Nodes)
            if (rs.VisitedNodeIds.Contains(n.Id)) _visited.Add(n);

        _revealed.Clear();
        foreach (var n in _visited) AddRevealed(n);
    }

    /// <summary>Zoom so the whole map fits the viewport (Flat mode default).</summary>
    private void FitZoom()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float worldSpan = 2f * (MapGenerator.RimRadius + 60f);
        _zoom = Mathf.Clamp(Mathf.Min(vp.X / worldSpan, vp.Y / worldSpan), ZoomLimit.X, ZoomLimit.Y);
    }

    private void OnCycleViewMode()
    {
        _mode = (ViewMode)(((int)_mode + 1) % 3);
        _pan = Vector2.Zero;
        UpdateViewModeButton();
        QueueRedraw();
    }

    private void UpdateViewModeButton() => _viewModeButton.Text = _mode switch
    {
        ViewMode.Flat => "Cam: flat",
        ViewMode.BossUp => "Cam: boss-up",
        _ => "Cam: heading-up",
    };

    // ---- view projection -------------------------------------------------------
    /// <summary>World point the view is pinned on: map center (Flat) or the player (tilted modes).</summary>
    private Vector2 Focus() => _mode == ViewMode.Flat ? MapGenerator.Center : _tokenPos;

    /// <summary>World → screen: rotate so the heading points up, foreshorten (tilt), zoom, pin the focus.</summary>
    private Vector2 Project(Vector2 world)
    {
        var v = (world - Focus()).Rotated(-_rot) * _zoom;
        v.Y *= _tilt;
        return _anchor + _pan + v;
    }

    private float Scaled(float s) => s * _zoom;

    /// <summary>Font size that scales with zoom (so labels grow/shrink with the map), min 7px.</summary>
    private int FontSize(int s) => Mathf.Max(7, Mathf.RoundToInt(s * _zoom));

    /// <summary>Screen point the focus is pinned to: center (Flat) or near the bottom (tilted modes).</summary>
    private Vector2 ComputeAnchor()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        return _mode == ViewMode.Flat ? vp / 2f : new Vector2(vp.X / 2f, vp.Y * 0.72f);
    }

    /// <summary>Desired view rotation so the heading (toward-boss, or last step) points straight up.</summary>
    private float RotTarget()
    {
        if (_mode == ViewMode.Flat) return 0f;
        var h = _mode == ViewMode.BossUp ? MapGenerator.Center - _tokenPos : _lastMoveDir;
        if (h.LengthSquared() < 1e-4f) h = MapGenerator.Center - _tokenPos;
        return h.LengthSquared() < 1e-4f ? _rotTarget : h.Angle() + Mathf.Pi / 2f;
    }

    private void OnToggleRender()
    {
        _rendered = !_rendered;
        _renderButton.Text = _rendered ? "View: atlas" : "View: schematic";
        QueueRedraw();
    }

    /// <summary>Start a fresh run on this seed (debug dice / seed entry), then re-sync the view.</summary>
    private void Restart(string seed)
    {
        var rs = RunState.Instance;
        rs?.NewRun(seed);
        SyncFromRunState();
        _seedEdit.Text = rs?.Seed ?? "";
        UpdateInfo();
        QueueRedraw();
    }

    private void OnDice() => Restart(DetRandom.NewSeed());
    private void OnSeedSubmitted(string text) => Restart(text);

    /// <summary>
    /// Exploration-mode day ending (replaces the old debug "Rest" button). Confirmation popup,
    /// warning when the current node devours tonight (NODES §7.4), routed through RunState.EndDay
    /// which runs the ordered pipeline and swaps back to the map (or to RunOver on VOID death).
    /// </summary>
    private void OnFinishDay()
    {
        var rs = RunState.Instance;
        if (rs == null) return;
        bool flickers = _current != null
                        && DevourDay.TryGetValue(_current.LevelTag, out int d) && d == rs.Day;
        string text = flickers
            ? "Finish the Day?\n\nWARNING: you are standing on flickering ground — the VOID\n" +
              "devours it tonight. Finishing the day here ENDS THE RUN."
            : "Finish the Day?";
        var dlg = new ConfirmationDialog { DialogText = text, Title = "Finish the Day" };
        AddChild(dlg);
        dlg.Confirmed += () => { dlg.QueueFree(); rs.EndDay(); };
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered();
    }

    private void OnToggleMist()
    {
        _mist = !_mist;
        _mistButton.Text = _mist ? "Mist: on" : "Mist: off";
        QueueRedraw();
    }

    /// <summary>Region a node belongs to for mist purposes: world abbr, "XX", or null for function nodes.</summary>
    private static string Region(MapNode n) => n.WorldIndex == -1 ? "XX" : (n.WorldIndex == -2 ? null : n.Zone);

    private void AddRevealed(MapNode n) { var r = Region(n); if (r != null) _revealed.Add(r); }

    /// <summary>A combat node whose goal was achieved (RunState truth) — safe to pass through.</summary>
    private bool IsCompleted(MapNode n) => RunState.Instance?.CompletedNodeIds.Contains(n.Id) ?? false;

    /// <summary>Whether a "?" node's event has been resolved (revisits are then inert, NODES §1.3).</summary>
    private bool IsResolvedEvent(MapNode n) => RunState.Instance?.ResolvedEventIds.Contains(n.Id) ?? false;

    /// <summary>
    /// Can a shortest path CONTINUE through this node? Unvisited nodes and unconquered combat
    /// nodes are destination-only — a path may reach them but not cross them (NODES §1.3).
    /// </summary>
    private bool IsPassThrough(MapNode n)
    {
        if (!_visited.Contains(n)) return false;
        if (n.IsCombat && !IsCompleted(n)) return false;
        return true;
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
        // Inside the VOID, time is unknowable — the black hole ate the clock.
        string day = _inVoid ? "???" : _day.ToString();
        _infoLabel.Text = $"Day {day}   Stamina {_stamina}/{MaxStamina}\n" +
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
            // Expand only from the start node or a pass-through-able node: an unconquered combat
            // node is reachable as a DESTINATION but a path cannot continue past it (NODES §1.3).
            if (cur != _current && !IsPassThrough(cur)) continue;
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
            // You must be able to stand at n and step out — you can't launch a new-node entry
            // from an unconquered combat node (reaching it ends there).
            if (n != _current && !IsPassThrough(n)) continue;
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

    /// <summary>
    /// Commit a move to <paramref name="target"/> (Exploration input). Deducts stamina, updates
    /// RunState position, then either triggers the destination's content (unvisited node, combat
    /// re-attempt, shelter, or unresolved "?") via RunState.BeginAdventure — which swaps scene —
    /// or, for an inert visited destination, just repositions the token. Day-ending is NOT here
    /// anymore (NODES decision log: the day ends on the Finish-the-Day button, not on arrival).
    /// </summary>
    private void TryMove(MapNode target, Dictionary<MapNode, int> vdist)
    {
        var rs = RunState.Instance;
        if (rs == null || target == null || target == _current) return;

        // ---- Debug mode: jump to any non-devoured node (bypasses all checks) ----
        if (DebugManager.Instance?.Enabled == true && !target.Devoured)
        {
            var fromNode = _current;
            rs.PreviousNodeId = fromNode.Id;
            _current = target;
            rs.CurrentNodeId = target.Id;
            _lastMoveDir = target.Pos - fromNode.Pos;
            if (!rs.InVoid && target.WorldIndex == -1) rs.EnterVoid();
            bool trig = (!_visited.Contains(target) && target.Kind != NodeKind.River)
                     || (target.IsCombat && !IsCompleted(target))
                     || target.Kind == NodeKind.Shelter
                     || (target.Kind == NodeKind.QuestionMark && !IsResolvedEvent(target));
            if (trig) { rs.BeginAdventure(target.Id); return; }
            rs.MarkNodeVisited(target.Id);
            _visited.Add(target);
            AddRevealed(target);
            UpdateInfo();
            QueueRedraw();
            return;
        }

        bool visited = _visited.Contains(target);
        int cost;
        if (visited)
        {
            if (!CanRevisit(target, vdist)) return;
            cost = vdist[target];
        }
        else
        {
            cost = ExploreCost(target, vdist);
            if (cost <= 0 || cost > rs.Stamina) return;
        }

        // Commit movement cost + position (RunState is the owner of truth).
        var fromNode = _current;
        rs.Stamina -= cost;
        rs.PreviousNodeId = fromNode.Id;
        _current = target;
        rs.CurrentNodeId = target.Id;
        _lastMoveDir = target.Pos - fromNode.Pos;   // heading-up mode spins this step to the top

        // Crossing the zone-6 singularity devours the whole outer ring at once (one-way in).
        if (!rs.InVoid && target.WorldIndex == -1) rs.EnterVoid();

        // Does the destination trigger Adventure content? (NODES §1.3)
        bool triggers =
            (!visited && target.Kind != NodeKind.River)                  // any brand-new node w/ content
            || (target.IsCombat && !IsCompleted(target))                 // failed combat re-attempt
            || target.Kind == NodeKind.Shelter                           // shelter always opens
            || (target.Kind == NodeKind.QuestionMark && !IsResolvedEvent(target)); // unresolved "?"

        if (triggers)
        {
            rs.BeginAdventure(target.Id); // snapshots + swaps scene (drains all stamina for combat)
            return;
        }

        // Inert destination (conquered combat / resolved "?" / river): just move the token.
        rs.MarkNodeVisited(target.Id); // no-op if already visited (covers the unvisited River hub)
        _visited.Add(target);          // keep the local view cache consistent
        AddRevealed(target);
        UpdateInfo();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _tokenPos = _tokenPos.Lerp(_current.Pos, Mathf.Min(1f, (float)delta * 12f));
        _rotTarget = RotTarget();
        float k = Mathf.Min(1f, (float)delta * 6f);
        _rot = Mathf.LerpAngle(_rot, _rotTarget, k);
        _tilt = Mathf.Lerp(_tilt, _mode == ViewMode.Flat ? 1f : TiltFactor, k);

        if (_toastLabel != null && _toastLabel.Visible)
        {
            _toastTimer -= (float)delta;
            if (_toastTimer <= 0f) _toastLabel.Visible = false;
        }

        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Wheel zooms; right/middle-drag pans (Flat mode only — tilted modes follow the player).
        if (@event is InputEventMouseButton wheel)
        {
            if (wheel.Pressed && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                _zoom = Mathf.Clamp(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.12f : 1f / 1.12f),
                                    ZoomLimit.X, ZoomLimit.Y);
                QueueRedraw();
                return;
            }
            if (wheel.ButtonIndex is MouseButton.Right or MouseButton.Middle)
            {
                _dragging = wheel.Pressed && _mode == ViewMode.Flat;
                return;
            }
        }
        if (@event is InputEventMouseMotion motion && _dragging)
        {
            _pan += motion.Relative;
            QueueRedraw();
            return;
        }

        if (@event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
            return;

        // Dismiss-on-click: any left click clears the day-end toast (doesn't consume the click —
        // the same click still resolves as a move below).
        if (_toastLabel != null && _toastLabel.Visible) _toastLabel.Visible = false;

        // Hit-test in SCREEN space: compare the cursor to each node's PROJECTED position.
        MapNode target = null;
        float best = ClickRadius * ClickRadius;
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured) continue;
            float d = Project(n.Pos).DistanceSquaredTo(mb.Position);
            if (d < best) { best = d; target = n; }
        }
        if (target != null) TryMove(target, VisitedSteps());
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
        _anchor = ComputeAnchor();
        var vdist = VisitedSteps();

        if (_rendered) { DrawAtlas(vdist); return; }

        for (int w = 0; w < _graph.Worlds.Count; w++)
            if (!_mist || _revealed.Contains(_graph.Worlds[w].Abbr))
                DrawWedge(w, _graph.Worlds[w].Color);

        // Zone 6 (the VOID): dark disc containing the lv5 ring, the lake, the river ring.
        bool voidSeen = !_mist || _revealed.Contains("XX");
        DrawCircle(Project(_graph.Center), Scaled(_graph.Zone6Radius), new Color(0.02f, 0.02f, 0.04f));
        if (voidSeen)
        {
            DrawArc(Project(_graph.Center), Scaled(_graph.Zone6Radius), 0, Mathf.Tau, 64, new Color(0.30f, 0.28f, 0.45f, 0.35f), 1.5f);
            DrawCircle(Project(_graph.Center), Scaled(_graph.LakeRadius), new Color(0.04f, 0.03f, 0.07f));
            DrawArc(Project(_graph.Center), Scaled(_graph.RiverRadius), 0, Mathf.Tau, 48, new Color(0.25f, 0.35f, 0.65f, 0.9f), 3f);
        }

        // Edges — drawn if one endpoint is visible (so bridge edges out of a revealed world show).
        foreach (var e in _graph.Edges)
        {
            if (!e.Visible || e.A.Devoured || e.B.Devoured) continue;
            if (_mist && !NodeVisible(e.A) && !NodeVisible(e.B)) continue;
            DrawLine(Project(e.A.Pos), Project(e.B.Pos), new Color(0.55f, 0.55f, 0.55f, 0.75f), 1.5f);
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

        DrawToken(Project(_tokenPos));
    }

    private void DrawWedge(int w, Color color)
    {
        float startDeg = -90f + w * 72f - WedgeDeg / 2f;
        var pts = new List<Vector2> { Project(_graph.Center) };
        int seg = 12;
        for (int i = 0; i <= seg; i++)
        {
            float deg = startDeg + WedgeDeg * i / seg;
            float rad = Mathf.DegToRad(deg);
            pts.Add(Project(_graph.Center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * MapGenerator.RimRadius));
        }
        DrawColoredPolygon(pts.ToArray(), new Color(color.R, color.G, color.B, 0.12f));
    }

    private void DrawFrontier(MapNode n)
    {
        // Unknown node you may explore into: a gold ring around a dim marker.
        var p = Project(n.Pos);
        DrawArc(p, Scaled(NodeRadius + 3f), 0, Mathf.Tau, 20, Gold, 1.5f);
        DrawCircle(p, Scaled(5f), new Color(0.3f, 0.3f, 0.35f, 0.8f));
    }

    private void DrawNode(MapNode n, bool visited, bool reachable)
    {
        var p = Project(n.Pos);
        float r = Scaled(NodeRadius);
        float alpha = 1f;
        if (DevourDay.TryGetValue(n.LevelTag, out int d) && d == _day)
            alpha = 0.45f + 0.45f * Mathf.Sin(_time * 8f);

        if (reachable)
            DrawArc(p, r + Scaled(4f), 0, Mathf.Tau, 22, new Color(Gold.R, Gold.G, Gold.B, 0.85f), 1.5f);

        var c = visited ? VisitedGrey : n.Color;
        c.A = alpha;

        switch (n.Kind)
        {
            case NodeKind.Combat:
                DrawCircle(p, r, c);
                DrawArc(p, r, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.6f * alpha), 1.5f);
                break;
            case NodeKind.Boss:
                DrawDiamond(p, r + Scaled(3f), c);
                break;
            case NodeKind.Shelter:
                DrawTriangle(p, r + Scaled(1f), c);
                break;
            case NodeKind.QuestionMark:
                DrawCircle(p, r, c);
                DrawString(_font, p + new Vector2(-4, 5), "?",
                    HorizontalAlignment.Left, -1, 14, new Color(0, 0, 0, alpha));
                break;
            case NodeKind.River: // selectable marker sitting on the river ring
                var rc = visited ? VisitedGrey : new Color(0.30f, 0.45f, 0.85f);
                rc.A = alpha;
                DrawCircle(p, r - Scaled(1f), rc);
                DrawArc(p, r - Scaled(1f), 0, Mathf.Tau, 18, new Color(0.7f, 0.8f, 1f, alpha), 1.5f);
                break;
        }

        if (n.IsCombat)
            DrawString(_font, p + new Vector2(-r, r + 11f), n.Id,
                HorizontalAlignment.Left, -1, 9, new Color(1, 1, 1, 0.7f * alpha));
    }

    /// <summary>Player token — drawn upright in screen space at the projected point.</summary>
    private void DrawToken(Vector2 p)
    {
        float r = Scaled(9f);
        DrawCircle(p, r, new Color(1f, 0.9f, 0.2f));
        DrawArc(p, r, 0, Mathf.Tau, 16, new Color(0, 0, 0, 0.8f), 1.5f);
        DrawCircle(p + new Vector2(-r * 0.33f, -r * 0.22f), Scaled(1.4f), Colors.Black);
        DrawCircle(p + new Vector2(r * 0.33f, -r * 0.22f), Scaled(1.4f), Colors.Black);
        DrawArc(p, Scaled(5f), Mathf.DegToRad(20), Mathf.DegToRad(160), 10, Colors.Black, 1.4f);
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

using System.Collections.Generic;
using Godot;
using Fableland.Map;

/// <summary>
/// Root of the Map scene. Owns the generated <see cref="MapGraph"/>, draws it, and
/// runs the debug loop: seed entry / dice reroll, day + stamina, the Rest button, and
/// click-to-move for the player token. The VOID devours the outer rings on a schedule.
///
/// This is a prototype/debug harness — nodes only differ by icon; node CONTENT
/// (fights, shelters, question marks) is not implemented yet. See Docs/MapGDD.md.
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

    private MapGraph _graph;
    private MapNode _current;
    private int _day = 1;
    private int _stamina = MaxStamina;

    private Vector2 _tokenPos;   // smoothed visual position of the player token
    private float _time;         // for flicker + token lerp
    private Font _font;

    // UI
    private LineEdit _seedEdit;
    private Label _infoLabel;

    public override void _Ready()
    {
        _seedEdit = GetNode<LineEdit>("UI/SeedEdit");
        _infoLabel = GetNode<Label>("UI/InfoLabel");
        _font = _infoLabel.GetThemeDefaultFont();
        GetNode<Button>("UI/DiceButton").Pressed += OnDice;
        GetNode<Button>("UI/RestButton").Pressed += OnRest;
        _seedEdit.TextSubmitted += OnSeedSubmitted;
        GetNode<Label>("UI/VersionLabel").Text = "v" + GameVersion.Current;

        Restart(DetRandom.NewSeed());
    }

    private void Restart(string seed)
    {
        seed = string.IsNullOrWhiteSpace(seed) ? DetRandom.NewSeed() : seed.Trim().ToUpperInvariant();
        _graph = MapGenerator.Generate(seed);
        _current = _graph.StartNode;
        _tokenPos = _current.Pos;
        _day = 1;
        _stamina = MaxStamina;
        _seedEdit.Text = seed;
        UpdateInfo();
        QueueRedraw();
    }

    private void OnDice() => Restart(DetRandom.NewSeed());
    private void OnSeedSubmitted(string text) => Restart(text);

    private void OnRest()
    {
        // End of day: any level scheduled to be devoured today is now gone.
        foreach (var n in _graph.Nodes)
            if (DevourDay.TryGetValue(n.LevelTag, out int d) && d == _day)
                n.Devoured = true;
        _day++;
        _stamina = MaxStamina;
        UpdateInfo();
        QueueRedraw();
    }

    private void UpdateInfo() => _infoLabel.Text = $"Day {_day}\nStamina {_stamina}/{MaxStamina}";

    public override void _Process(double delta)
    {
        _time += (float)delta;
        // Smoothly slide the token toward its node.
        _tokenPos = _tokenPos.Lerp(_current.Pos, Mathf.Min(1f, (float)delta * 12f));
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
            return;

        MapNode target = null;
        float best = ClickRadius * ClickRadius;
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured) continue;
            float d = n.Pos.DistanceSquaredTo(mb.Position);
            if (d < best) { best = d; target = n; }
        }
        if (target == null || target == _current) return;

        var steps = _graph.StepsFrom(_current);
        if (steps.TryGetValue(target, out int cost) && cost > 0 && cost <= _stamina)
        {
            _current = target;
            _stamina -= cost;
            UpdateInfo();
        }
    }

    // ---- drawing ---------------------------------------------------------------

    public override void _Draw()
    {
        if (_graph == null) return;

        // World backgrounds: faint 72° wedges tinted by palette.
        for (int w = 0; w < _graph.Worlds.Count; w++)
            DrawWedge(w, _graph.Worlds[w].Color);

        // Zone 6: the lake + the counter-clockwise river ring (edges here are invisible).
        DrawCircle(_graph.Center, _graph.LakeRadius, new Color(0.03f, 0.03f, 0.05f));
        DrawArc(_graph.Center, _graph.RiverRadius, 0, Mathf.Tau, 48, new Color(0.25f, 0.35f, 0.65f, 0.9f), 3f);

        // Edges (zone 1-5 only are visible): grey lines.
        foreach (var e in _graph.Edges)
        {
            if (!e.Visible || e.A.Devoured || e.B.Devoured) continue;
            DrawLine(e.A.Pos, e.B.Pos, new Color(0.55f, 0.55f, 0.55f, 0.75f), 1.5f);
        }

        // Nodes.
        foreach (var n in _graph.Nodes)
        {
            if (n.Devoured || n.Kind == NodeKind.River) continue;
            DrawNode(n);
        }

        // Player token (smiley).
        DrawToken(_tokenPos);
    }

    private void DrawWedge(int w, Color color)
    {
        float startDeg = -90f + w * 72f - 36f;
        var pts = new List<Vector2> { _graph.Center };
        int seg = 12;
        for (int i = 0; i <= seg; i++)
        {
            float deg = startDeg + 72f * i / seg;
            float rad = Mathf.DegToRad(deg);
            pts.Add(_graph.Center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 320f);
        }
        var col = new Color(color.R, color.G, color.B, 0.12f);
        DrawColoredPolygon(pts.ToArray(), col);
    }

    private void DrawNode(MapNode n)
    {
        float alpha = 1f;
        // Flicker on the day this level is about to be devoured.
        if (DevourDay.TryGetValue(n.LevelTag, out int d) && d == _day)
            alpha = 0.45f + 0.45f * Mathf.Sin(_time * 8f);

        var c = n.Color;
        c.A = alpha;

        switch (n.Kind)
        {
            case NodeKind.Combat:
                DrawCircle(n.Pos, NodeRadius, c);
                DrawArc(n.Pos, NodeRadius, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.6f * alpha), 1.5f);
                break;
            case NodeKind.Boss: // diamond
                DrawDiamond(n.Pos, NodeRadius + 3f, c);
                break;
            case NodeKind.Shelter: // camp: up-triangle
                DrawTriangle(n.Pos, NodeRadius + 1f, c);
                break;
            case NodeKind.QuestionMark:
                DrawCircle(n.Pos, NodeRadius, c);
                DrawString(_font, n.Pos + new Vector2(-4, 5), "?",
                    HorizontalAlignment.Left, -1, 14, new Color(0, 0, 0, alpha));
                break;
        }

        // Small id label under combat/boss nodes.
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
        DrawArc(p + new Vector2(0, 0), 5f, Mathf.DegToRad(20), Mathf.DegToRad(160), 10, Colors.Black, 1.4f);
    }

    private void DrawDiamond(Vector2 p, float r, Color c)
    {
        var pts = new[]
        {
            p + new Vector2(0, -r), p + new Vector2(r, 0),
            p + new Vector2(0, r), p + new Vector2(-r, 0),
        };
        DrawColoredPolygon(pts, c);
    }

    private void DrawTriangle(Vector2 p, float r, Color c)
    {
        var pts = new[]
        {
            p + new Vector2(0, -r), p + new Vector2(r, r * 0.9f), p + new Vector2(-r, r * 0.9f),
        };
        DrawColoredPolygon(pts, c);
    }
}

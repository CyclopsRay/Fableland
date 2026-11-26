using System;
using System.Collections.Generic;
using Godot;

namespace Fableland.Map.Pages;

/// <summary>
/// The overworld drawn as <b>torn storybook pages on a desk</b> — a third view of the same
/// generated map beside the schematic and the atlas (`Docs/Art/OverworldStyle.md`,
/// `MapGDD.md` §11). It reads a <see cref="MapGraph"/> and adds no gameplay.
///
/// Back to front: the desk and its cut-paper waves and compass; the shallows; the river
/// paper; each realm's page (ground, deckle, shadow); char where the VOID has eaten; the
/// kit's symbols; roads; mist; the burnt hole; pins; a thin frame.
///
/// <b>Cost rule (T40 §4):</b> geometry is built once per generation (<see cref="Build"/>),
/// state-dependent meshes once per state change (<see cref="RefreshState"/>). Per frame the
/// host only calls <see cref="SetView"/>, which moves two transforms; the two upright layers
/// (symbols, pins) re-submit their quads when the view actually changed. World-space pieces
/// live in children that carry the view transform, so pan, zoom, rotation and tilt cost
/// nothing on the CPU. Symbols and pins are drawn in screen space instead so they stay
/// upright when the map is rotated or tilted, as the atlas's markers do.
/// </summary>
public partial class OverworldPageView : Node2D
{
    // ---- what the host tells the view ---------------------------------------------
    public bool ShowStamps = true;
    public bool ShowLabels = true;
    public bool ShowFrame = true;
    /// <summary>Mist on: realms the player has not entered lie under tracing paper.</summary>
    public bool Mist;
    public int Day = 1;
    public Font LabelFont;
    /// <summary>Where the player's token stands (world), or null for no token.</summary>
    public Vector2? Token;
    public Func<string, bool> RegionRevealed = _ => true;   // a realm's abbreviation, or "XX"
    public Func<MapNode, bool> NodeVisible = _ => true;
    public Func<MapNode, bool> NodeVisited = _ => false;
    public Func<MapNode, bool> NodeReachable = _ => false;
    public Func<MapNode, bool> NodeFrontier = _ => false;   // hidden by mist, yet can be stepped into

    /// <summary>The part of the screen this view fills (its frame is drawn there); the whole
    /// viewport when unset.</summary>
    public Rect2? ScreenRect;

    public PageModel Model { get; private set; }
    public float Zoom => _zoom;
    private Rect2 Screen => ScreenRect ?? GetViewportRect();

    private static readonly Color Gold = new(0.96f, 0.80f, 0.38f);
    private static readonly Color VisitedGrey = new(0.52f, 0.52f, 0.56f);
    private static readonly Color RealityThread = new(0.78f, 0.38f, 1f);

    private Transform2D _view = Transform2D.Identity;
    private float _zoom = 1f;
    private float _time;
    private bool _anyFlicker;

    private Node2D _below, _above;
    private DrawLayer _stampLayer, _pinLayer, _frameLayer;
    private MeshInstance2D _charFill, _charDarken, _charEdge, _charEmber, _roads, _holeInside, _holeEmber;
    private readonly List<MeshInstance2D> _mist = new();
    private readonly List<(RealmPage Page, StampPlacement Stamp)> _stamps = new();
    private int[] _stampOrder = Array.Empty<int>();
    private float[] _stampKeys = Array.Empty<float>();
    private CanvasItemMaterial _additive, _multiply;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    // ---- build (once per generation) --------------------------------------------------

    public void Build(MapGraph graph, bool torn = true)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _mist.Clear();
        _stamps.Clear();
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        Model = PageModel.Build(graph, torn);
        PageFurniture furniture = PageFurniture.Shared;
        _additive ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _multiply ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul };

        _below = new Node2D { Name = "Below" };
        AddChild(_below);
        AddMesh(_below, "Desk", Model.DeskMesh, furniture.Desk, repeat: true);
        AddMesh(_below, "Ripples", Model.RippleMesh, null);
        var waves = new DrawLayer { Name = "Waves", Painter = DrawWaves };
        _below.AddChild(waves);
        if (furniture.Compass != null)
        {
            var compass = new Sprite2D
            {
                Name = "Compass", Texture = furniture.Compass, Position = Model.CompassPos,
                Scale = Vector2.One * (Model.CompassSize / furniture.Compass.GetWidth()),
            };
            _below.AddChild(compass);
        }
        AddMesh(_below, "Under", Model.UnderMesh, furniture.Paper, repeat: true);
        AddMesh(_below, "RiverLines", Model.RiverLineMesh, null);
        foreach (RealmPage page in Model.Pages)
        {
            AddMesh(_below, "Page" + page.Abbr, page.PrintMesh, page.Kit?.Ground);
            if (page.AgeMesh != null) AddMesh(_below, "Age" + page.Abbr, page.AgeMesh, null);
        }
        _charFill = AddMesh(_below, "Char", new ArrayMesh(), furniture.Char, repeat: true);
        // The same geometry twice: first it darkens the page (so the page's medium still
        // ghosts through), then the cracked char goes over it, not quite opaque.
        _charDarken = AddMesh(_below, "CharDarken", _charFill.Mesh, null);
        _charDarken.Material = _multiply;
        _charDarken.Modulate = new Color(0.30f, 0.24f, 0.21f);
        _below.MoveChild(_charDarken, _charFill.GetIndex());
        _charFill.Modulate = new Color(1f, 1f, 1f, 0.80f);
        _charEdge = AddMesh(_below, "CharEdge", new ArrayMesh(), null);
        _charEmber = AddMesh(_below, "CharEmber", new ArrayMesh(), null);
        _charEmber.Material = _additive;

        _stampLayer = new DrawLayer { Name = "Stamps", Painter = DrawStamps };
        AddChild(_stampLayer);
        foreach (RealmPage page in Model.Pages)
            foreach (StampPlacement stamp in page.Stamps)
                _stamps.Add((page, stamp));
        _stampOrder = new int[_stamps.Count];
        _stampKeys = new float[_stamps.Count];

        _above = new Node2D { Name = "Above" };
        AddChild(_above);
        _roads = AddMesh(_above, "Roads", new ArrayMesh(), null);
        foreach (RealmPage page in Model.Pages)
            _mist.Add(AddMesh(_above, "Mist" + page.Abbr, page.MistMesh, furniture.Paper, repeat: true));
        AddMesh(_above, "HoleScorch", Model.HoleScorchMesh, null);
        AddMesh(_above, "Hole", Model.HoleMesh, null);
        _holeInside = AddMesh(_above, "HoleInside", Model.HoleInsideMesh, null);
        _holeEmber = AddMesh(_above, "HoleEmber", Model.HoleEmberMesh, null);
        _holeEmber.Material = _additive;

        _pinLayer = new DrawLayer { Name = "Pins", Painter = DrawPins };
        AddChild(_pinLayer);
        _frameLayer = new DrawLayer { Name = "Frame", Painter = DrawFrame };
        AddChild(_frameLayer);

        ApplyView();
        RefreshState();
    }

    private static MeshInstance2D AddMesh(Node parent, string name, Mesh mesh, Texture2D texture, bool repeat = false)
    {
        var instance = new MeshInstance2D { Name = name, Mesh = mesh, Texture = texture };
        if (repeat) instance.TextureRepeat = TextureRepeatEnum.Enabled;
        parent.AddChild(instance);
        return instance;
    }

    // ---- view (every frame, cheap) -------------------------------------------------------

    /// <summary>World → screen. <paramref name="zoom"/> is the scale symbols and pins are
    /// drawn at (they ignore the view's rotation and tilt).</summary>
    public void SetView(Transform2D view, float zoom)
    {
        if (view.IsEqualApprox(_view) && Mathf.IsEqualApprox(zoom, _zoom)) return;
        _view = view;
        _zoom = zoom;
        ApplyView();
    }

    private void ApplyView()
    {
        if (_below == null) return;
        _below.Transform = _view;
        _above.Transform = _view;
        _stampLayer.QueueRedraw();
        _pinLayer.QueueRedraw();
    }

    /// <summary>Advance what breathes: the embers' glow, and pins on ground the VOID takes
    /// tonight. No geometry is rebuilt.</summary>
    public void Animate(float time)
    {
        _time = time;
        if (_holeEmber == null) return;
        float breath = 0.80f + 0.14f * Mathf.Sin(time * 2.3f) + 0.06f * Mathf.Sin(time * 5.9f + 1.3f);
        _holeEmber.SelfModulate = new Color(1f, 1f, 1f, breath);
        _charEmber.SelfModulate = new Color(1f, 1f, 1f, 0.78f + 0.20f * Mathf.Sin(time * 2.9f + 0.7f));
        if (_anyFlicker || Token != null) _pinLayer.QueueRedraw();
    }

    public void RedrawOverlays()
    {
        _stampLayer?.QueueRedraw();
        _pinLayer?.QueueRedraw();
        _frameLayer?.QueueRedraw();
    }

    // ---- state (once per change: day end, a realm entered, a bridge built) ------------------

    public void RefreshState()
    {
        if (Model == null || _below == null) return;
        MapGraph graph = Model.Graph;

        for (int i = 0; i < Model.Pages.Count; i++)
            _mist[i].Visible = Mist && !RegionRevealed(Model.Pages[i].Abbr);
        _holeInside.Visible = !Mist || RegionRevealed("XX");

        // Roads: ink dashes on the page, casing first so no dash is cut by a neighbour's.
        var roads = new PaperCut.MeshBuffer();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (RealmPage page in Model.Pages)
            {
                Color ink = page.Kit?.Ink ?? new Color(0.23f, 0.16f, 0.13f);
                Color casing = page.Kit?.InkCase ?? new Color(1f, 0.97f, 0.9f);
                casing.A = page.Kit != null && page.Kit.Dark ? 0.62f : 0.55f;
                float width = pass == 0 ? 5.4f : 2.9f;
                Color color = pass == 0 ? casing : ink;
                foreach (RoadInk road in page.Roads)
                {
                    if (!RoadShown(road.Edge)) continue;
                    foreach ((Vector2 a, Vector2 b) in road.Dashes) AddDash(roads, a, b, width, color, pass == 0 ? 1.1f : 0f);
                }
            }
        }
        foreach (MapEdge edge in graph.Edges)
        {
            if (edge.Kind != MapEdgeKind.RealityBridge || !RoadShown(edge) || edge.Route == null || edge.Route.Length < 2) continue;
            // A bridge of Eidolon is not printed on any page: it is a thread pulled taut between two pins.
            roads.Stroke(edge.Route, 6.5f, new Color(0.10f, 0.02f, 0.18f, 0.45f));
            roads.Stroke(edge.Route, 3.4f, RealityThread);
            roads.Stroke(edge.Route, 1.0f, new Color(1f, 0.88f, 1f, 0.9f));
        }
        roads.ToMesh((ArrayMesh)_roads.Mesh);

        BuildChar();

        _anyFlicker = false;
        foreach (MapNode node in graph.Nodes)
            if (VoidSchedule.IsFlickering(graph, node, Day)) { _anyFlicker = true; break; }
        RedrawOverlays();
    }

    private bool RoadShown(MapEdge edge)
    {
        if (edge.A.Devoured || edge.B.Devoured) return false;
        return !Mist || NodeVisible(edge.A) || NodeVisible(edge.B);
    }

    private static void AddDash(PaperCut.MeshBuffer buffer, Vector2 a, Vector2 b, float width, Color color, float extend)
    {
        Vector2 along = b - a;
        if (along.LengthSquared() < 1e-6f) return;
        along = along.Normalized();
        var side = new Vector2(-along.Y, along.X) * (width * 0.5f);
        a -= along * extend;
        b += along * extend;
        buffer.Quad(a + side, b + side, b - side, a - side, color);
    }

    /// <summary>
    /// Devoured land as charred paper. Each devoured city's field (clipped to its torn page)
    /// first darkens the page under it — a multiply, so a ghost of the page's own medium
    /// survives — and is then covered in cracked char. Wherever devoured ground meets ground
    /// still alive the edge is bitten, scorched and lit: the frontier the fire is working
    /// along. Frontier edges from neighbouring fields are joined into one line before they
    /// are drawn, so the burn runs round a corner instead of stopping at every cell.
    /// </summary>
    private void BuildChar()
    {
        var fill = new PaperCut.MeshBuffer();
        var edge = new PaperCut.MeshBuffer();
        var ember = new PaperCut.MeshBuffer();
        var segments = new List<(Vector2 A, Vector2 B)>();
        foreach (RealmPage page in Model.Pages)
        {
            segments.Clear();
            foreach (CityChar city in page.Cities)
            {
                if (!city.City.Devoured) continue;
                for (int k = 0; k < city.Pieces.Count; k++)
                {
                    Vector2[] piece = city.Pieces[k];
                    fill.Fill(piece, city.Triangles[k], Colors.White, Vector2.Zero, PageModel.CharUv);
                    int n = piece.Length;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 a = piece[i], b = piece[(i + 1) % n];
                        Vector2 along = b - a;
                        float length = along.Length();
                        if (length < 0.05f) continue;
                        along /= length;
                        var right = new Vector2(along.Y, -along.X);
                        Vector2 mid = (a + b) * 0.5f;
                        bool aliveRight = Alive(page, mid + right * 3.5f);
                        bool aliveLeft = Alive(page, mid - right * 3.5f);
                        if (aliveRight == aliveLeft) continue;     // inside the burn, or the page's own edge
                        // Store every frontier edge with living ground on its right.
                        segments.Add(aliveRight ? (a, b) : (b, a));
                    }
                }
            }
            foreach (List<Vector2> chain in Chain(segments)) BurnEdge(chain, fill, edge, ember);
        }
        fill.ToMesh((ArrayMesh)_charFill.Mesh);
        edge.ToMesh((ArrayMesh)_charEdge.Mesh);
        ember.ToMesh((ArrayMesh)_charEmber.Mesh);
    }

    /// <summary>Is this spot on the page and in a city's field that the VOID has not taken?</summary>
    private static bool Alive(RealmPage page, Vector2 point)
    {
        MapNode owner = null;
        float best = float.MaxValue;
        foreach (MapNode city in page.CityNodes)
        {
            float d = city.Pos.DistanceSquaredTo(point);
            if (d < best) { best = d; owner = city; }
        }
        return owner != null && !owner.Devoured && PaperCut.Contains(page.Sheet.Outer, point);
    }

    /// <summary>Join directed segments end to start into polylines (a closed one repeats its
    /// first point at the end).</summary>
    private static List<List<Vector2>> Chain(List<(Vector2 A, Vector2 B)> segments)
    {
        static Vector2I Key(Vector2 p) => new(Mathf.RoundToInt(p.X * 2f), Mathf.RoundToInt(p.Y * 2f));
        var byStart = new Dictionary<Vector2I, List<int>>();
        var byEnd = new Dictionary<Vector2I, List<int>>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (!byStart.TryGetValue(Key(segments[i].A), out List<int> starts)) byStart[Key(segments[i].A)] = starts = new List<int>();
            starts.Add(i);
            if (!byEnd.TryGetValue(Key(segments[i].B), out List<int> ends)) byEnd[Key(segments[i].B)] = ends = new List<int>();
            ends.Add(i);
        }
        var used = new bool[segments.Count];
        int Take(Dictionary<Vector2I, List<int>> index, Vector2 at)
        {
            Vector2I key = Key(at);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!index.TryGetValue(key + new Vector2I(dx, dy), out List<int> list)) continue;
                foreach (int candidate in list)
                    if (!used[candidate]) return candidate;
            }
            return -1;
        }

        var chains = new List<List<Vector2>>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var chain = new List<Vector2> { segments[i].A, segments[i].B };
            for (int next = Take(byStart, chain[^1]); next >= 0; next = Take(byStart, chain[^1]))
            {
                used[next] = true;
                chain.Add(segments[next].B);
            }
            for (int previous = Take(byEnd, chain[0]); previous >= 0; previous = Take(byEnd, chain[0]))
            {
                used[previous] = true;
                chain.Insert(0, segments[previous].A);
            }
            chains.Add(chain);
        }
        return chains;
    }

    /// <summary>One run of frontier, living ground on its right: bite into the living side,
    /// scorch beyond the bite, and light the edge.</summary>
    private void BurnEdge(List<Vector2> run, PaperCut.MeshBuffer fill, PaperCut.MeshBuffer edge, PaperCut.MeshBuffer ember)
    {
        bool closed = run.Count > 3 && run[0].DistanceTo(run[^1]) < 1.5f;
        Vector2[] line = PaperCut.ResampleOpen(run.ToArray(), 3.0f);
        int n = line.Length;
        if (n < 3) return;
        // The burn's shape is fixed by where the edge is, so it does not crawl between days.
        var rng = new DetRandom(Model.Graph.Seed + ":pages:burn:" + Mathf.RoundToInt(line[0].X) + "," + Mathf.RoundToInt(line[0].Y) + ":" + n);
        var bite = new PaperCut.LoopNoise(rng, Mathf.Max(3, n / 5));
        var tooth = new PaperCut.LoopNoise(rng, Mathf.Max(4, n / 2));
        var glow = new PaperCut.LoopNoise(rng, Mathf.Max(3, n / 7));
        var heat = new PaperCut.LoopNoise(rng, Mathf.Max(3, n / 3));
        var bitten = new Vector2[n];
        var char2 = new Vector2[n];
        var scorch = new Vector2[n];
        // The fields meet at sharp corners; a band offset from a sharp corner folds over
        // itself. Fire rounds corners anyway: offset from a rounded copy of the line.
        Vector2[] round = PaperCut.Rounded(line, 4, closed);
        for (int i = 0; i < n; i++)
        {
            int before = closed ? (i - 4 + n) % n : Mathf.Max(0, i - 4);
            int after = closed ? (i + 4) % n : Mathf.Min(n - 1, i + 4);
            Vector2 tangent = round[after] - round[before];
            tangent = tangent.LengthSquared() < 1e-8f ? Vector2.Right : tangent.Normalized();
            var living = new Vector2(tangent.Y, -tangent.X);
            float t = (float)i / n;
            // Where a run ends it has reached the page's edge: let the burn close there.
            float ends = closed ? 1f : Mathf.Min(1f, Mathf.Min(i, n - 1 - i) / 3f);
            float eat = (Mathf.Abs(bite.Sample(t)) * 5.5f + tooth.Sample(t) * 1.2f + 1.6f) * ends;
            // Never let the bitten edge fall short of the field's true corner.
            Vector2 from = round[i].Lerp(line[i], 1f - ends);
            eat = Mathf.Max(eat, (line[i] - from).Dot(living) + 1.2f * ends);
            bitten[i] = from + living * eat;
            char2[i] = bitten[i] + living * (5.0f * ends + 0.5f);
            scorch[i] = bitten[i] + living * (16f * ends + 1f);
        }
        fill.Band(line, bitten, Colors.White, Colors.White, closed, Vector2.Zero, PageModel.CharUv);
        var charred = new Color(0.070f, 0.045f, 0.030f, 0.95f);
        var brown = new Color(0.250f, 0.130f, 0.050f, 0.58f);
        edge.Band(bitten, char2, charred, brown, closed);
        edge.Band(char2, scorch, brown, new Color(0.42f, 0.25f, 0.09f, 0f), closed);
        var lit = new List<Vector2>();
        float strength = 0.9f;
        for (int i = 0; i <= n; i++)
        {
            bool on = i < n && glow.Sample((float)i / n) > -0.30f;
            if (on)
            {
                if (lit.Count == 0) strength = 0.62f + 0.38f * Mathf.Abs(heat.Sample((float)i / n));
                lit.Add(bitten[i]);
            }
            if (!on && lit.Count > 0)
            {
                PageModel.AddEmber(ember, lit, strength);
                lit.Clear();
            }
        }
    }

    // ---- upright layers ---------------------------------------------------------------------

    private void DrawWaves(DrawLayer layer)
    {
        PageFurniture furniture = PageFurniture.Shared;
        if (furniture.Waves == null || furniture.WaveCells.Length == 0) return;
        // The sheet holds each wave twice: as cut, and mirrored (with its shadow still falling
        // to the lower right). A wave is never flipped at draw time.
        int shapes = Mathf.Max(1, furniture.WaveCells.Length / 2);
        foreach (WavePlacement wave in Model.Waves)
        {
            int index = wave.Cell % shapes + (wave.Flip && furniture.WaveCells.Length >= shapes * 2 ? shapes : 0);
            Rect2 cell = furniture.WaveCells[index];
            float height = wave.Width * cell.Size.Y / cell.Size.X;
            layer.DrawSetTransform(wave.Pos, wave.Tilt, Vector2.One);
            layer.DrawTextureRectRegion(furniture.Waves, new Rect2(-wave.Width * 0.5f, -height * 0.5f, wave.Width, height), cell, wave.Tint);
        }
        layer.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawStamps(DrawLayer layer)
    {
        if (!ShowStamps || Model == null || _stamps.Count == 0) return;
        Rect2 screen = Screen.Grow(8f);
        for (int i = 0; i < _stamps.Count; i++)
        {
            _stampOrder[i] = i;
            _stampKeys[i] = (_view * _stamps[i].Stamp.Pos).Y;
        }
        Array.Sort(_stampKeys, _stampOrder);
        foreach (int index in _stampOrder)
        {
            StampPlacement stamp = _stamps[index].Stamp;
            Vector2 at = _view * stamp.Pos;
            Vector2 size = stamp.Size * _zoom;
            var rect = new Rect2(at.X - stamp.Stamp.Anchor.X * size.X, at.Y - stamp.Stamp.Anchor.Y * size.Y, size.X, size.Y);
            if (!screen.Intersects(rect)) continue;
            // A symbol on devoured land is still there, burnt: the ruin of what the page showed.
            bool burnt = stamp.City != null && stamp.City.Devoured;
            layer.DrawTextureRect(stamp.Stamp.Texture, rect, false, burnt ? new Color(0.20f, 0.15f, 0.13f, 0.92f) : Colors.White);
        }
    }

    private static PageFurniture.PinShape ShapeOf(MapNode node) => node.Kind switch
    {
        NodeKind.Boss => PageFurniture.PinShape.Diamond,
        NodeKind.TransportHub => PageFurniture.PinShape.Triangle,
        NodeKind.Shelter => PageFurniture.PinShape.Square,
        NodeKind.River => PageFurniture.PinShape.Ring,
        _ => PageFurniture.PinShape.Circle,
    };

    private void DrawPins(DrawLayer layer)
    {
        if (Model == null) return;
        PageFurniture furniture = PageFurniture.Shared;
        MapGraph graph = Model.Graph;
        Rect2 screen = Screen.Grow(40f);
        Font font = LabelFont ?? ThemeDB.FallbackFont;
        bool labels = ShowLabels && _zoom >= 0.80f;

        // Names first, pins over them: a pin is never hidden by a neighbour's slip of paper.
        if (labels)
        {
            foreach (MapNode node in graph.Nodes)
            {
                if (node.Devoured || !node.IsCombat || !NodeVisible(node)) continue;
                Vector2 at = _view * node.Pos;
                if (!screen.HasPoint(at)) continue;
                float radius = Mathf.Max(PageModel.PinRadius(node) * _zoom, 5.2f);
                float alpha = VoidSchedule.IsFlickering(graph, node, Day) ? 0.50f + 0.45f * Mathf.Sin(_time * 8f) : 1f;
                DrawLabel(layer, font, node, at, radius * 1.35f + 4f, alpha);
            }
        }

        foreach (MapNode node in graph.Nodes)
        {
            if (node.Devoured) continue;
            Vector2 at = _view * node.Pos;
            if (!screen.HasPoint(at)) continue;
            // Pins never shrink below a size the eye can sort by shape.
            float radius = Mathf.Max(PageModel.PinRadius(node) * _zoom, node.IsCombat ? 5.2f : 4.0f);
            if (!NodeVisible(node))
            {
                if (NodeFrontier(node))
                {
                    layer.DrawArc(at, radius + 3f, 0f, Mathf.Tau, 24, Gold, 1.6f, true);
                    layer.DrawCircle(at, radius * 0.42f, new Color(0.32f, 0.32f, 0.36f, 0.85f));
                }
                continue;
            }

            float alpha = 1f;
            if (VoidSchedule.IsFlickering(graph, node, Day)) alpha = 0.50f + 0.45f * Mathf.Sin(_time * 8f);
            bool visited = NodeVisited(node);
            if (NodeReachable(node))
                layer.DrawArc(at, radius * 1.28f + 3f, 0f, Mathf.Tau, 28, new Color(Gold.R, Gold.G, Gold.B, 0.95f * alpha), Mathf.Max(1.6f, 2.2f * _zoom), true);

            PageFurniture.PinShape shape = ShapeOf(node);
            if (furniture.Pins != null)
            {
                float cell = radius / furniture.PinRadius * furniture.PinCell;
                var rect = new Rect2(at.X - cell * 0.5f, at.Y - cell * 0.5f, cell, cell);
                Color head = visited ? VisitedGrey : node.Color;
                var sticker = visited ? new Color(0.84f, 0.84f, 0.84f, alpha) : new Color(1f, 1f, 1f, alpha);
                layer.DrawTextureRectRegion(furniture.Pins, rect, furniture.PinRegion(shape, PageFurniture.PinLayer.Shadow), new Color(1f, 1f, 1f, alpha));
                layer.DrawTextureRectRegion(furniture.Pins, rect, furniture.PinRegion(shape, PageFurniture.PinLayer.Sticker), sticker);
                layer.DrawTextureRectRegion(furniture.Pins, rect, furniture.PinRegion(shape, PageFurniture.PinLayer.Head), new Color(head.R, head.G, head.B, alpha));
                layer.DrawTextureRectRegion(furniture.Pins, rect, furniture.PinRegion(shape, PageFurniture.PinLayer.Gloss), new Color(1f, 1f, 1f, alpha));
            }
            else
            {
                layer.DrawCircle(at, radius, new Color(node.Color.R, node.Color.G, node.Color.B, alpha));
            }

            if (node.Kind == NodeKind.Event && radius >= 6f)
            {
                int size = Mathf.Max(8, Mathf.RoundToInt(radius * 1.25f));
                layer.DrawString(font, at + new Vector2(-radius, radius * 0.46f), "?", HorizontalAlignment.Center, radius * 2f, size, new Color(0.08f, 0.06f, 0.05f, alpha));
            }
        }

        if (Token is Vector2 token) DrawToken(layer, _view * token);
    }

    /// <summary>A node's name on a slip of paper — the game's own text, never drawn into the art.</summary>
    private void DrawLabel(DrawLayer layer, Font font, MapNode node, Vector2 at, float reach, float alpha)
    {
        int size = Mathf.Clamp(Mathf.RoundToInt(9.5f * _zoom), 9, 20);
        Vector2 extent = font.GetStringSize(node.Id, HorizontalAlignment.Left, -1, size);
        var paper = new Vector2(extent.X + 8f, extent.Y + 1f);
        // Under the pin if that is clear; otherwise over it, or to a side — wherever no
        // other pin would sit on the name.
        Rect2 slip = default;
        for (int side = 0; side < 4; side++)
        {
            Vector2 corner = side switch
            {
                0 => new Vector2(at.X - paper.X * 0.5f, at.Y + reach),
                1 => new Vector2(at.X - paper.X * 0.5f, at.Y - reach - paper.Y),
                2 => new Vector2(at.X + reach, at.Y - paper.Y * 0.5f),
                _ => new Vector2(at.X - reach - paper.X, at.Y - paper.Y * 0.5f),
            };
            var candidate = new Rect2(corner, paper);
            if (side == 0) slip = candidate;
            if (!PinOn(candidate, node)) { slip = candidate; break; }
        }
        layer.DrawRect(new Rect2(slip.Position + new Vector2(1.2f, 1.6f), slip.Size), new Color(0f, 0f, 0f, 0.28f * alpha));
        layer.DrawRect(slip, new Color(0.975f, 0.955f, 0.905f, 0.94f * alpha));
        layer.DrawString(font, new Vector2(slip.Position.X + 4f, slip.Position.Y + font.GetAscent(size)), node.Id,
            HorizontalAlignment.Left, -1, size, new Color(0.16f, 0.11f, 0.09f, alpha));
    }

    /// <summary>Would another node's pin cover this slip of paper?</summary>
    private bool PinOn(Rect2 slip, MapNode owner)
    {
        foreach (MapNode other in Model.Graph.Nodes)
        {
            if (other == owner || other.Devoured) continue;
            if (Mathf.Abs(other.Pos.X - owner.Pos.X) > 90f || Mathf.Abs(other.Pos.Y - owner.Pos.Y) > 90f) continue;
            float radius = Mathf.Max(PageModel.PinRadius(other) * _zoom, 4f);
            if (slip.Grow(radius * 0.85f).HasPoint(_view * other.Pos)) return true;
        }
        return false;
    }

    private void DrawToken(DrawLayer layer, Vector2 at)
    {
        float r = Mathf.Max(7f, 10.5f * _zoom);
        float bob = Mathf.Sin(_time * 3.2f) * 1.2f;
        Vector2 head = at + new Vector2(0f, -r * 2.1f + bob);
        // A brass map tack with a gold halo: where you stand.
        layer.DrawArc(at, r * 1.75f, 0f, Mathf.Tau, 32, new Color(Gold.R, Gold.G, Gold.B, 0.90f), Mathf.Max(1.8f, r * 0.22f), true);
        layer.DrawLine(at, head, new Color(0.12f, 0.10f, 0.09f, 0.9f), Mathf.Max(1.4f, r * 0.20f), true);
        layer.DrawCircle(head + new Vector2(1.5f, 2f), r * 0.92f, new Color(0f, 0f, 0f, 0.30f));
        layer.DrawCircle(head, r * 0.92f, new Color(0.14f, 0.10f, 0.08f));
        layer.DrawCircle(head, r * 0.76f, new Color(1f, 0.84f, 0.22f));
        layer.DrawCircle(head + new Vector2(-r * 0.24f, -r * 0.26f), r * 0.24f, new Color(1f, 0.98f, 0.82f, 0.9f));
    }

    private void DrawFrame(DrawLayer layer)
    {
        if (!ShowFrame) return;
        Rect2 rect = Screen;
        var ink = new Color(0.93f, 0.88f, 0.74f, 0.80f);
        Rect2 outer = rect.Grow(-9f);
        Rect2 inner = rect.Grow(-14f);
        layer.DrawRect(outer, ink, false, 2.2f);
        layer.DrawRect(inner, new Color(ink.R, ink.G, ink.B, 0.45f), false, 1f);
        // Corner squares, as on an engraved map's neatline.
        const float k = 7f;
        foreach (Vector2 corner in new[] { outer.Position, new Vector2(outer.End.X, outer.Position.Y), outer.End, new Vector2(outer.Position.X, outer.End.Y) })
            layer.DrawRect(new Rect2(corner - new Vector2(k, k) * 0.5f, new Vector2(k, k)), ink);
    }
}

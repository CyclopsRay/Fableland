using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Fableland.Map.Pages;

/// <summary>A kit symbol placed on a page.</summary>
public sealed class StampPlacement
{
    public PageStamp Stamp;
    public Vector2 Pos;          // world position of the stamp's anchor (where it stands)
    public Vector2 Size;         // world size of the whole picture
    public bool Seat;
    public MapNode City;         // the city whose field it stands in (it chars with that city)
    public Rect2 Box => new(Pos.X - Stamp.Anchor.X * Size.X, Pos.Y - Stamp.Anchor.Y * Size.Y, Size.X, Size.Y);
}

/// <summary>One outer city's field, clipped to its torn page — what chars when the city is devoured.</summary>
public sealed class CityChar
{
    public MapNode City;
    public readonly List<Vector2[]> Pieces = new();
    public readonly List<int[]> Triangles = new();
}

/// <summary>A road's ink: its dashes, cut once.</summary>
public sealed class RoadInk
{
    public MapEdge Edge;
    public List<(Vector2 A, Vector2 B)> Dashes = new();
}

/// <summary>One realm as a torn page.</summary>
public sealed class RealmPage
{
    public int WorldIndex;
    public string Abbr;
    public PageKit Kit;                 // null → plain tinted paper
    public WorldLandmass Land;
    public PaperCut.TornSheet Sheet;
    public Rect2 Bounds;
    public ArrayMesh PrintMesh;         // the ground, clipped to the page
    public ArrayMesh AgeMesh;           // foxing toward the page's edge (null when not torn)
    public ArrayMesh MistMesh;          // the tracing paper laid over it while unexplored
    public Color Tint = Colors.White;   // multiplies the print (only used without a kit)
    public readonly List<StampPlacement> Stamps = new();
    public readonly List<CityChar> Cities = new();
    public readonly List<RoadInk> Roads = new();
    public readonly List<MapNode> CityNodes = new();
}

public struct WavePlacement
{
    public Vector2 Pos;      // world centre
    public float Width;      // world width
    public int Cell;         // which wave on the sheet
    public bool Flip;
    public float Tilt;       // radians; small, so the baked shadow still falls lower right
    public Color Tint;
}

/// <summary>
/// The torn-pages overworld, precomputed (`Docs/Art/OverworldStyle.md`). Everything that
/// depends only on the generated map — every outline, tear, clip mesh, stamp position and
/// dash — is built here, once per generation, from <see cref="MapGraph"/>. Nothing in it is
/// gameplay: it reads the map and draws nothing from the generator's own random streams
/// (its streams are <c>seed + ":pages:…"</c>, the `DetRandom.Sub` rule), so a seed's map is
/// byte-for-byte what it was. What changes during a run (what is devoured, what the mist
/// hides) is laid over this by <see cref="OverworldPageView"/>.
/// </summary>
public sealed class PageModel
{
    /// <summary>World pixels per pixel of a kit's original 1024 px symbol sheet.</summary>
    public const float StampScale = 0.238f;
    /// <summary>The seat is the page's one large picture.</summary>
    public const float SeatScale = 1.50f;
    public const float PaperUv = 1f / 205f;     // paper grain: 256 texels across 205 world px
    public const float DeskUv = 1f / 820f;
    public const float CharUv = 1f / 420f;

    public static readonly Color DeckleColor = new(0.975f, 0.955f, 0.905f);
    public static readonly Color ShadowColor = new(0.01f, 0.04f, 0.07f, 0.48f);
    public static readonly Color RiverPaper = new(0.085f, 0.215f, 0.560f);
    public static readonly Color RiverRim = new(0.36f, 0.52f, 0.86f);
    public static readonly Color ShallowsOuter = new(0.330f, 0.640f, 0.640f);
    public static readonly Color ShallowsInner = new(0.560f, 0.800f, 0.745f);

    public MapGraph Graph;
    public bool Torn = true;
    public readonly List<RealmPage> Pages = new();

    public ArrayMesh DeskMesh;        // desk paper (desk texture)
    public ArrayMesh RippleMesh;      // coast ripple lines (no texture)
    public ArrayMesh UnderMesh;       // shallows, river paper, page shadows, deckles (paper texture)
    public ArrayMesh RiverLineMesh;   // flow lines down the rivers (no texture)
    public ArrayMesh HoleScorchMesh;  // the brown the fire left round the hole (no texture)
    public ArrayMesh HoleMesh;        // the hole itself and what shows in it (no texture)
    public ArrayMesh HoleInsideMesh;  // zone 6's lake and river ring, shown once the VOID is known
    public ArrayMesh HoleEmberMesh;   // glowing edge (additive)
    public Vector2[] HoleOutline = Array.Empty<Vector2>();
    public readonly List<WavePlacement> Waves = new();
    public Vector2 CompassPos;
    public float CompassSize = 250f;
    public int VertexCount, TriangleCount;

    private DetRandom Stream(string tag) => new(Graph.Seed + ":pages:" + tag);

    public static PageModel Build(MapGraph graph, bool torn = true)
    {
        var model = new PageModel { Graph = graph, Torn = torn };
        model.BuildDesk();
        model.BuildRipples();
        var under = new PaperCut.MeshBuffer();
        model.BuildShallows(under);
        model.BuildRivers(under);
        model.BuildPages(under);
        model.UnderMesh = under.ToMesh();
        model.Count(under);
        model.BuildHole();
        model.BuildWaves();
        foreach (RealmPage page in model.Pages)
        {
            model.PlaceStamps(page);
            model.CutRoads(page);
            model.ClipCityFields(page);
        }
        return model;
    }

    private void Count(PaperCut.MeshBuffer buffer)
    {
        VertexCount += buffer.Vertices.Count;
        TriangleCount += buffer.Indices.Count / 3;
    }

    // ---- the desk ---------------------------------------------------------------

    private void BuildDesk()
    {
        // One big sheet of sea-coloured paper under everything, a touch lighter under the
        // island so the eye settles there. A coarse grid carries that as vertex colour.
        const float half = 3400f;
        const int cells = 14;
        var buffer = new PaperCut.MeshBuffer();
        var index = new int[cells + 1, cells + 1];
        for (int y = 0; y <= cells; y++)
        for (int x = 0; x <= cells; x++)
        {
            var p = Graph.Center + new Vector2(Mathf.Lerp(-half, half, (float)x / cells), Mathf.Lerp(-half, half, (float)y / cells));
            float falloff = Mathf.SmoothStep(500f, 2300f, p.DistanceTo(Graph.Center));
            float value = Mathf.Lerp(1.16f, 0.80f, falloff);
            index[x, y] = buffer.Add(p, new Color(value, value, value), p * DeskUv);
        }
        for (int y = 0; y < cells; y++)
        for (int x = 0; x < cells; x++)
        {
            buffer.Triangle(index[x, y], index[x + 1, y], index[x + 1, y + 1]);
            buffer.Triangle(index[x, y], index[x + 1, y + 1], index[x, y + 1]);
        }
        DeskMesh = buffer.ToMesh();
        Count(buffer);
    }

    /// <summary>The island's coast pushed out by <paramref name="distance"/> px.</summary>
    private Vector2[] CoastRing(float distance, PaperCut.LoopNoise wander, float wanderAmount)
    {
        Vector2[] coast = Graph.IslandCoast;
        var ring = new Vector2[coast.Length];
        for (int i = 0; i < coast.Length; i++)
        {
            Vector2 radial = (coast[i] - Graph.Center).Normalized();
            float push = distance + (wander?.Sample((float)i / coast.Length) ?? 0f) * wanderAmount;
            ring[i] = coast[i] + radial * push;
        }
        return ring;
    }

    private void BuildRipples()
    {
        // Borrowed from the cartographer: ripple lines off the coast, in pale gouache on the
        // desk. Broken into strokes so they read as drawn by hand.
        var buffer = new PaperCut.MeshBuffer();
        DetRandom rng = Stream("ripples");
        float[] distances = { 86f, 108f, 134f };
        float[] alphas = { 0.50f, 0.34f, 0.20f };
        float[] widths = { 2.0f, 1.7f, 1.4f };
        for (int ring = 0; ring < distances.Length; ring++)
        {
            var wander = new PaperCut.LoopNoise(rng, 14);
            Vector2[] loop = PaperCut.ResampleClosed(CoastRing(distances[ring], wander, 5f + ring * 3f), 7f);
            var color = new Color(0.78f, 0.93f, 0.92f, alphas[ring]);
            int i = rng.Range(0, 12);
            while (i < loop.Length)
            {
                int length = rng.Range(7, 20);
                var stroke = new List<Vector2>();
                for (int k = 0; k <= length && i + k <= loop.Length; k++) stroke.Add(loop[(i + k) % loop.Length]);
                if (stroke.Count >= 2) buffer.Stroke(stroke, widths[ring], color);
                i += length + rng.Range(3, 7);
            }
        }
        RippleMesh = buffer.ToMesh();
        Count(buffer);
    }

    private void AddSheet(PaperCut.MeshBuffer under, PaperCut.TornSheet sheet, Color fill, Color rim, Vector2 shadowOffset, float feather, float shadowAlpha)
    {
        var shadow = new Color(ShadowColor.R, ShadowColor.G, ShadowColor.B, shadowAlpha);
        if (shadowAlpha > 0f) PaperCut.AddShadow(under, sheet.Outer, sheet.Normals, shadowOffset, feather, shadow);
        under.Fill(sheet.Inner, sheet.Triangles, fill, Vector2.Zero, PaperUv);
        if (Torn) AddDeckle(under, sheet, rim);
    }

    /// <summary>The band of bare fibre between a sheet's torn edge and its printed surface,
    /// with a hair of soft fuzz beyond the tear.</summary>
    private static void AddDeckle(PaperCut.MeshBuffer buffer, PaperCut.TornSheet sheet, Color rim)
    {
        buffer.Band(sheet.Inner, sheet.Outer, rim, rim, closed: true, Vector2.Zero, PaperUv);
        int n = sheet.Outer.Length;
        var fuzz = new Vector2[n];
        for (int i = 0; i < n; i++) fuzz[i] = sheet.Outer[i] + sheet.Normals[i] * 0.9f;
        buffer.Band(sheet.Outer, fuzz, rim, new Color(rim.R, rim.G, rim.B, 0f), closed: true, Vector2.Zero, PaperUv);
    }

    private void BuildShallows(PaperCut.MeshBuffer under)
    {
        // The shallows: two sheets of sea-green paper torn a little larger than the island.
        DetRandom rng = Stream("shallows");
        PaperCut.TearStyle style = Torn ? PaperCut.TearStyle.Sheet : PaperCut.TearStyle.Clean;
        style.Inset = 0f;
        Vector2[] outer = CoastRing(56f, new PaperCut.LoopNoise(rng, 7), 19f);
        AddSheet(under, PaperCut.Tear(outer, rng, style), ShallowsOuter, ShallowsOuter.Lerp(Colors.White, 0.52f),
            new Vector2(4.5f, 6.5f), 13f, Torn ? 0.44f : 0f);
        Vector2[] inner = CoastRing(27f, new PaperCut.LoopNoise(rng, 11), 11f);
        AddSheet(under, PaperCut.Tear(inner, rng, style), ShallowsInner, ShallowsInner.Lerp(Colors.White, 0.60f),
            new Vector2(3f, 4.5f), 9f, Torn ? 0.32f : 0f);
    }

    private void BuildRivers(PaperCut.MeshBuffer under)
    {
        // A river is the gap between two pages: a strip of deep blue paper lying under both,
        // from the VOID out past the coast. Wider than the generated river by what the pages
        // were trimmed, so even where a river has no width the tear reads.
        var lines = new PaperCut.MeshBuffer();
        for (int r = 0; r < Graph.RealmRivers.Count; r++)
        {
            MapRiver river = Graph.RealmRivers[r];
            DetRandom rng = Stream("river:" + r);
            int n = river.LeftBank.Length;
            if (n < 3) continue;
            var centre = new Vector2[n];
            var halfWidth = new float[n];
            for (int i = 0; i < n; i++)
            {
                centre[i] = (river.LeftBank[i] + river.RightBank[i]) * 0.5f;
                halfWidth[i] = river.LeftBank[i].DistanceTo(river.RightBank[i]) * 0.5f;
            }

            // Carry the strip under the hole at one end and out to sea at the other.
            var spine = new List<Vector2>();
            var widths = new List<float>();
            Vector2 inward = (centre[0] - centre[1]).Normalized();
            spine.Add(centre[0] + inward * 16f);
            widths.Add(halfWidth[0]);
            for (int i = 0; i < n; i++) { spine.Add(centre[i]); widths.Add(halfWidth[i]); }
            // Past the coast the strip ends as a short rounded tongue lying on the shallows:
            // the river's mouth. Under the pages it is cut generously wide (the tuck), so no
            // notch in a page's edge can show anything but river under it.
            Vector2 outward = (centre[n - 1] - centre[n - 2]).Normalized();
            float reach = 24f + (float)rng.NextDouble() * 8f;
            float[] tongue = { 0.96f, 0.84f, 0.62f, 0.30f };
            for (int k = 1; k <= tongue.Length; k++)
            {
                spine.Add(centre[n - 1] + outward * (reach * k / tongue.Length));
                widths.Add((halfWidth[n - 1] + 5f) * tongue[k - 1]);
            }

            var left = new Vector2[spine.Count];
            var right = new Vector2[spine.Count];
            for (int i = 0; i < spine.Count; i++)
            {
                Vector2 tangent = spine[Mathf.Min(spine.Count - 1, i + 1)] - spine[Mathf.Max(0, i - 1)];
                tangent = tangent.Normalized();
                var side = new Vector2(-tangent.Y, tangent.X);
                // Wide under the pages, narrowing to the coast (where a wide strip would show
                // as shoulders past the pages' corners); the tongue is its own width.
                float tuck = i <= n - 3 ? 27f : i == n - 2 ? 18f : i == n - 1 ? 11f : i == n ? 6f : 0f;
                left[i] = spine[i] + side * (widths[i] + tuck);
                right[i] = spine[i] - side * (widths[i] + tuck);
            }
            var strip = new List<Vector2>(left);
            for (int i = right.Length - 1; i >= 0; i--) strip.Add(right[i]);

            PaperCut.TearStyle style = Torn ? PaperCut.TearStyle.Strip : PaperCut.TearStyle.Clean;
            style.Inset = 0f;
            PaperCut.TornSheet sheet = PaperCut.Tear(strip.ToArray(), rng, style);
            AddSheet(under, sheet, RiverPaper, RiverRim, new Vector2(2f, 3f), 6f, Torn ? 0.28f : 0f);

            // Flow lines: two or three broken strokes down the middle, where there is room.
            var flow = new Color(0.55f, 0.72f, 0.98f, 0.55f);
            for (int lane = -1; lane <= 1; lane++)
            {
                var stroke = new List<Vector2>();
                float phase = (float)rng.NextDouble() * 6f;
                for (int i = 2; i < spine.Count - 1; i++)
                {
                    float room = widths[i] + 3.5f;
                    bool open = room > (lane == 0 ? 4.5f : 11f) && Mathf.Sin(i * 1.9f + phase + lane * 2.1f) > -0.25f;
                    if (open)
                    {
                        Vector2 tangent = (spine[Mathf.Min(spine.Count - 1, i + 1)] - spine[i - 1]).Normalized();
                        var side = new Vector2(-tangent.Y, tangent.X);
                        stroke.Add(spine[i] + side * (lane * room * 0.46f));
                    }
                    if ((!open || i == spine.Count - 2) && stroke.Count > 0)
                    {
                        if (stroke.Count >= 2) lines.Stroke(Smooth(stroke), 1.5f, flow);
                        stroke.Clear();
                    }
                }
            }
        }
        RiverLineMesh = lines.ToMesh();
        Count(lines);
    }

    /// <summary>One pass of corner-cutting, so a stroke through a few points bends instead of kinking.</summary>
    private static List<Vector2> Smooth(List<Vector2> line)
    {
        if (line.Count < 3) return line;
        var result = new List<Vector2> { line[0] };
        for (int i = 0; i + 1 < line.Count; i++)
        {
            result.Add(line[i].Lerp(line[i + 1], 0.25f));
            result.Add(line[i].Lerp(line[i + 1], 0.75f));
        }
        result.Add(line[^1]);
        return result;
    }

    // ---- the pages ----------------------------------------------------------------

    private void BuildPages(PaperCut.MeshBuffer under)
    {
        var sheets = new List<PaperCut.TornSheet>();
        for (int world = 0; world < Graph.Landmasses.Count; world++)
        {
            WorldLandmass land = Graph.Landmasses[world];
            string abbr = Graph.Worlds[world].Abbr;
            DetRandom rng = Stream("tear:" + abbr);
            // A pin near the bank keeps a tab of paper under it: the tear goes round it.
            var pins = new List<(Vector2 At, float Radius)>();
            foreach (MapNode node in Graph.Nodes)
                if (node.WorldIndex == world) pins.Add((node.Pos, PinRadius(node) + 3.5f));
            PaperCut.TornSheet sheet = PaperCut.Tear(land.Coast, rng, Torn ? PaperCut.TearStyle.Page : PaperCut.TearStyle.Clean, pins);
            PageKit kit = PageKit.Load(abbr);
            var page = new RealmPage
            {
                WorldIndex = world, Abbr = abbr, Kit = kit, Land = land, Sheet = sheet,
                Bounds = PaperCut.Bounds(sheet.Outer),
                Tint = kit?.Ground != null ? Colors.White : MapRenderModel.ThemeFor(abbr).Land,
            };
            foreach (MapNode node in Graph.Nodes)
                if (node.WorldIndex == world && node.IsCombat) page.CityNodes.Add(node);

            // The ground is one un-tiled copy of the kit's square, upright, spanning the page.
            float span = Mathf.Max(page.Bounds.Size.X, page.Bounds.Size.Y) * 1.02f;
            Vector2 origin = page.Bounds.GetCenter() - new Vector2(span, span) * 0.5f;
            var print = new PaperCut.MeshBuffer();
            print.Fill(sheet.Inner, sheet.Triangles, page.Tint, origin, 1f / span);
            page.PrintMesh = print.ToMesh();
            Count(print);

            // A page torn from an old book is a little foxed toward its edges. Drawn over the
            // print as its own (untextured) mesh: a warm shade that fades out a thumb's width in.
            if (Torn)
            {
                int count = sheet.Inner.Length;
                var within = new Vector2[count];
                for (int i = 0; i < count; i++) within[i] = sheet.Inner[i] - sheet.Normals[i] * 15f;
                var age = new PaperCut.MeshBuffer();
                age.Band(sheet.Inner, within, new Color(0.30f, 0.20f, 0.10f, 0.26f), new Color(0.30f, 0.20f, 0.10f, 0f), closed: true);
                page.AgeMesh = age.ToMesh();
                Count(age);
            }

            // Tracing paper for the mist: cut (not torn) a hair outside the page.
            Vector2[] veil = PaperCut.ResampleClosed(PaperCut.Offset(land.Coast, -2.5f), 9f);
            int[] veilTriangles = Geometry2D.TriangulatePolygon(veil);
            var mist = new PaperCut.MeshBuffer();
            var veilColor = new Color(0.90f, 0.93f, 0.94f, 0.60f);
            mist.Fill(veil, veilTriangles, veilColor, Vector2.Zero, PaperUv);
            Vector2[] veilEdge = PaperCut.Offset(veil, -1.6f);
            if (veilEdge.Length == veil.Length)
                mist.Band(veilEdge, veil, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.55f), closed: true, Vector2.Zero, PaperUv);
            else
                mist.Stroke(veil, 1.4f, new Color(1f, 1f, 1f, 0.40f), closed: true);
            page.MistMesh = mist.ToMesh();
            Count(mist);

            Pages.Add(page);
            sheets.Add(sheet);
        }

        // Every page's shadow goes down before any page's deckle: pages lie side by side, so
        // a shadow belongs in the gap between them and never on a neighbour.
        if (Torn)
        {
            foreach (PaperCut.TornSheet sheet in sheets)
                PaperCut.AddShadow(under, sheet.Outer, sheet.Normals, new Vector2(3.6f, 5.2f), 9.5f, ShadowColor);
            foreach (PaperCut.TornSheet sheet in sheets) AddDeckle(under, sheet, DeckleColor);
        }
    }

    // ---- the VOID: a hole burnt through every page ------------------------------------

    private void BuildHole()
    {
        if (Graph.VoidPentagon == null || Graph.VoidPentagon.Length < 3) return;
        DetRandom rng = Stream("hole");
        // The fire ate a little past the pentagon, enough to take the pages' inner deckles.
        Vector2[] grown = PaperCut.Offset(Graph.VoidPentagon, 9f, Geometry2D.PolyJoinType.Round);
        Vector2[] edge = PaperCut.ResampleClosed(grown, 2.4f);
        Vector2[] normals = PaperCut.OutwardNormals(edge, 3);
        int n = edge.Length;
        var slow = new PaperCut.LoopNoise(rng, 11);
        var bite = new PaperCut.LoopNoise(rng, 46);
        var tooth = new PaperCut.LoopNoise(rng, 150);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            // Scalloped: fire eats in rounded bites, so fold the middle noise upward.
            float scallop = Mathf.Abs(bite.Sample(t)) * 4.2f;
            edge[i] += normals[i] * (slow.Sample(t) * 2.6f + scallop + tooth.Sample(t) * 0.8f - 1.2f);
        }
        HoleOutline = edge;

        var char1 = new Vector2[n];
        var char2 = new Vector2[n];
        var scorch = new Vector2[n];
        var glowNoise = new PaperCut.LoopNoise(rng, 31);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float reach = 1f + 0.35f * glowNoise.Sample(t);
            char1[i] = edge[i] + normals[i] * (3.6f * reach);
            char2[i] = edge[i] + normals[i] * (11.5f * reach);
            scorch[i] = edge[i] + normals[i] * (34f * reach);
        }
        var black = new Color(0.030f, 0.020f, 0.018f, 1f);
        var charred = new Color(0.085f, 0.050f, 0.030f, 0.96f);
        var brown = new Color(0.260f, 0.135f, 0.050f, 0.62f);
        var toast = new Color(0.420f, 0.250f, 0.090f, 0f);
        var scorchBuffer = new PaperCut.MeshBuffer();
        scorchBuffer.Band(edge, char1, black, charred, closed: true);
        scorchBuffer.Band(char1, char2, charred, brown, closed: true);
        scorchBuffer.Band(char2, scorch, brown, toast, closed: true);
        HoleScorchMesh = scorchBuffer.ToMesh();
        Count(scorchBuffer);

        var hole = new PaperCut.MeshBuffer();
        int[] triangles = Geometry2D.TriangulatePolygon(edge);
        hole.Fill(edge, triangles, new Color(0.012f, 0.010f, 0.020f), Vector2.Zero, 0f);
        // The cut edges of the stack of pages, seen just inside the hole: a pale ash lip.
        var lip = new Vector2[n];
        for (int i = 0; i < n; i++) lip[i] = edge[i] - normals[i] * 2.0f;
        hole.Band(lip, edge, new Color(0.30f, 0.26f, 0.22f, 0f), new Color(0.52f, 0.45f, 0.36f, 0.85f), closed: true);
        HoleMesh = hole.ToMesh();
        Count(hole);

        // Zone 6, drawn only once the VOID is known: its lake and the river ring round the core.
        var inside = new PaperCut.MeshBuffer();
        Vector2[] lake = Circle(Graph.Center, Graph.LakeRadius, 48);
        inside.Fill(lake, Geometry2D.TriangulatePolygon(lake), new Color(0.055f, 0.040f, 0.105f), Vector2.Zero, 0f);
        inside.Stroke(lake, 1.4f, new Color(0.34f, 0.30f, 0.52f, 0.55f), closed: true);
        inside.Stroke(Circle(Graph.Center, Graph.RiverRadius, 44), 3.2f, new Color(0.30f, 0.42f, 0.80f, 0.92f), closed: true);
        inside.Stroke(Circle(Graph.Center, Graph.RiverRadius, 44), 1.0f, new Color(0.72f, 0.82f, 1f, 0.75f), closed: true);
        HoleInsideMesh = inside.ToMesh();
        Count(inside);

        // Embers along the edge, in broken runs. Drawn additive, and breathed by the view.
        var ember = new PaperCut.MeshBuffer();
        var run = new List<Vector2>();
        var emberNoise = new PaperCut.LoopNoise(rng, 38);
        for (int i = 0; i <= n; i++)
        {
            float t = (float)(i % n) / n;
            bool lit = emberNoise.Sample(t) > -0.28f && i < n;
            if (lit) run.Add(edge[i % n] + normals[i % n] * 0.6f);
            if (!lit && run.Count > 0)
            {
                AddEmber(ember, run, 1f);
                run.Clear();
            }
        }
        HoleEmberMesh = ember.ToMesh();
        Count(ember);
    }

    /// <summary>A glowing run: a wide soft orange and a thin hot core.</summary>
    public static void AddEmber(PaperCut.MeshBuffer buffer, List<Vector2> run, float strength)
    {
        if (run.Count < 2) return;
        // An ember is not a neon tube: it swells where the paper is still feeding it and
        // thins to nothing at both ends of the run.
        var weight = new float[run.Count];
        for (int i = 0; i < run.Count; i++)
        {
            float ends = Mathf.Min(1f, Mathf.Min(i, run.Count - 1 - i) / 2.5f);
            float swell = 0.62f + 0.38f * Mathf.Sin(i * 0.83f + run[0].X * 0.21f) * Mathf.Sin(i * 0.31f + run[0].Y * 0.13f);
            weight[i] = ends * swell;
        }
        var wide = new float[run.Count];
        var mid = new float[run.Count];
        var core = new float[run.Count];
        for (int i = 0; i < run.Count; i++)
        {
            wide[i] = 7.5f * weight[i];
            mid[i] = 3.2f * weight[i];
            core[i] = 1.15f * weight[i];
        }
        buffer.StrokeVarying(run, wide, new Color(0.95f, 0.28f, 0.04f, 0.20f * strength));
        buffer.StrokeVarying(run, mid, new Color(1.00f, 0.46f, 0.10f, 0.52f * strength));
        buffer.StrokeVarying(run, core, new Color(1.00f, 0.86f, 0.50f, 0.92f * strength));
    }

    private static Vector2[] Circle(Vector2 centre, float radius, int segments)
    {
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }

    // ---- sea furniture -----------------------------------------------------------------

    private void BuildWaves()
    {
        // Rows of cut-paper waves on the desk, like a paper theatre's sea. The same rows on
        // every island (they are furniture, not geography); only which wave and which of the
        // sea's papers varies with the seed.
        DetRandom rng = Stream("waves");
        Color[] papers =
        {
            new(0.74f, 0.88f, 0.90f), new(0.58f, 0.78f, 0.83f), new(0.46f, 0.70f, 0.78f), new(0.83f, 0.92f, 0.89f),
        };
        CompassPos = Graph.Center + new Vector2(-905f, 395f);
        const float rowStep = 152f, columnStep = 318f;
        int row = 0;
        for (float y = -700f; y <= 700f; y += rowStep, row++)
        {
            float shift = (row % 2 == 0 ? 0f : columnStep * 0.5f) + (float)(rng.NextDouble() - 0.5) * 40f;
            for (float x = -1650f; x <= 1650f; x += columnStep)
            {
                var offset = new Vector2(x + shift + (float)(rng.NextDouble() - 0.5) * 70f, y + (float)(rng.NextDouble() - 0.5) * 44f);
                int cell = rng.Range(0, 3);
                bool flip = rng.Chance(0.5);
                Color tint = papers[rng.Range(0, papers.Length - 1)];
                float width = 88f + (float)rng.NextDouble() * 84f;
                float tilt = ((float)rng.NextDouble() - 0.5f) * 0.42f;
                Vector2 pos = Graph.Center + offset;
                // Keep the sea clear round the island, its ripple lines and the compass.
                if (offset.Length() < MapGenerator.RimRadius + 208f) continue;
                if (pos.DistanceTo(CompassPos) < CompassSize * 0.5f + 96f) continue;
                Waves.Add(new WavePlacement { Pos = pos, Width = width, Cell = cell, Flip = flip, Tint = tint, Tilt = tilt });
            }
        }
    }

    // ---- stamps ------------------------------------------------------------------------

    public static float PinRadius(MapNode node) => node.Kind switch
    {
        NodeKind.Boss => 16f,
        NodeKind.Combat => 12.5f,
        _ => 8.5f,
    };

    private void PlaceStamps(RealmPage page)
    {
        PageKit kit = page.Kit;
        if (kit == null || kit.Stamps.Count == 0) return;
        DetRandom rng = Stream("stamps:" + page.Abbr);
        int world = page.WorldIndex;
        var nodes = Graph.Nodes.Where(node => node.WorldIndex == world).ToList();
        var roadPoints = new List<Vector2>();
        foreach (MapEdge edge in Graph.Edges)
        {
            if (edge.Kind != MapEdgeKind.Road || edge.A.WorldIndex != world || edge.B.WorldIndex != world) continue;
            foreach (Vector2 p in PaperCut.ResampleOpen(edge.Route, 6f)) roadPoints.Add(p);
        }
        float innerRadius = Graph.Zone6Radius + 34f;
        float outerRadius = MapGenerator.RimRadius;
        var placed = new List<Rect2>();

        Rect2 Core(Rect2 box) => new(box.Position + new Vector2(box.Size.X * 0.10f, box.Size.Y * 0.12f),
            new Vector2(box.Size.X * 0.80f, box.Size.Y * 0.86f));

        bool OnPage(Rect2 box)
        {
            Rect2 core = Core(box).Grow(4f);
            Vector2 a = core.Position, b = core.End;
            float midX = (a.X + b.X) * 0.5f, midY = (a.Y + b.Y) * 0.5f;
            Span<Vector2> probes = stackalloc Vector2[]
            {
                new(a.X, b.Y), new(midX, b.Y), new(b.X, b.Y), new(a.X, midY), new(b.X, midY),
                new(a.X + core.Size.X * 0.18f, a.Y), new(midX, a.Y), new(b.X - core.Size.X * 0.18f, a.Y),
            };
            foreach (Vector2 probe in probes)
            {
                if (!PaperCut.Contains(page.Sheet.Inner, probe)) return false;
                if (probe.DistanceTo(Graph.Center) < Graph.Zone6Radius + 30f) return false;
            }
            return true;
        }

        int Conflicts(Rect2 box, MapNode ignore)
        {
            Rect2 core = Core(box);
            int count = 0;
            foreach (MapNode node in nodes)
            {
                if (node == ignore) continue;
                if (core.Grow(PinRadius(node) + 3.5f).HasPoint(node.Pos)) count += 6;
            }
            Rect2 lane = core.Grow(2.5f);
            foreach (Vector2 p in roadPoints)
            {
                if (ignore != null && p.DistanceTo(ignore.Pos) < 16f) continue;
                if (lane.HasPoint(p)) count++;
            }
            foreach (Rect2 other in placed)
                if (core.Grow(3f).Intersects(other)) count += 8;
            return count;
        }

        MapNode Owner(Vector2 p)
        {
            MapNode best = null;
            float bestDistance = float.MaxValue;
            foreach (MapNode city in page.CityNodes)
            {
                float d = city.Pos.DistanceSquaredTo(p);
                if (d < bestDistance) { bestDistance = d; best = city; }
            }
            return best;
        }

        void Place(PageStamp stamp, Vector2 pos, Vector2 size, bool seat)
        {
            var placement = new StampPlacement { Stamp = stamp, Pos = pos, Size = size, Seat = seat, City = Owner(pos) };
            page.Stamps.Add(placement);
            placed.Add(Core(placement.Box));
        }

        Rect2 BoxAt(PageStamp stamp, Vector2 pos, Vector2 size) =>
            new(pos.X - stamp.Anchor.X * size.X, pos.Y - stamp.Anchor.Y * size.Y, size.X, size.Y);

        // The seat stands at the capital: the pin at its gate if that is clear, else beside it.
        MapNode capital = nodes.FirstOrDefault(node => node.Kind == NodeKind.Boss);
        PageStamp seatStamp = kit.Stamps[kit.Seat];
        if (capital != null)
        {
            float pin = PinRadius(capital);
            Vector2 bestPos = capital.Pos;
            Vector2 bestSize = seatStamp.SheetSize * StampScale * SeatScale;
            int bestScore = int.MaxValue;
            float[] scales = { 1f, 0.86f, 0.74f };
            for (int s = 0; s < scales.Length && bestScore > -5; s++)
            {
                Vector2 size = seatStamp.SheetSize * StampScale * SeatScale * scales[s];
                Vector2[] offsets =
                {
                    new(0f, -pin * 0.55f),                                      // pin at the gate
                    new(-(size.X * 0.42f + pin), size.Y * 0.16f),               // seat to the left
                    new(size.X * 0.42f + pin, size.Y * 0.16f),                  // seat to the right
                    new(0f, size.Y * 0.90f + pin + 3f),                         // seat below the pin
                    new(-(size.X * 0.36f + pin), -pin - 4f),
                    new(size.X * 0.36f + pin, -pin - 4f),
                };
                for (int o = 0; o < offsets.Length; o++)
                {
                    Vector2 pos = capital.Pos + offsets[o];
                    Rect2 box = BoxAt(seatStamp, pos, size);
                    // The pin at the gate is worth a road or two crossing the picture.
                    int score = Conflicts(box, o == 0 ? capital : null) + (OnPage(box) ? 0 : 60) + (o == 0 ? -5 : o) + s * 3;
                    if (score < bestScore) { bestScore = score; bestPos = pos; bestSize = size; }
                }
            }
            Place(seatStamp, bestPos, bestSize, seat: true);
        }

        // The rest scatter by best-candidate sampling: each new symbol takes the emptiest
        // legal spot among a handful of tries — blue noise, and it fills the big gaps first.
        var bag = new List<int>();
        int misses = 0;
        int target = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(PaperCut.SignedArea(page.Sheet.Inner)) / 13000f), 7, 15);
        while (page.Stamps.Count < target + 1 && misses < 8)
        {
            if (bag.Count == 0)
            {
                for (int i = 0; i < kit.Stamps.Count; i++) if (i != kit.Seat) bag.Add(i);
                rng.Shuffle(bag);
            }
            PageStamp stamp = kit.Stamps[bag[^1]];
            bag.RemoveAt(bag.Count - 1);
            float jitter = 0.90f + (float)rng.NextDouble() * 0.20f;
            Vector2 size = stamp.SheetSize * StampScale * jitter;

            bool found = false;
            Vector2 bestPos = default;
            float bestRoom = -1f;
            for (int attempt = 0; attempt < 120; attempt++)
            {
                var pos = new Vector2(
                    Mathf.Lerp(page.Bounds.Position.X, page.Bounds.End.X, (float)rng.NextDouble()),
                    Mathf.Lerp(page.Bounds.Position.Y, page.Bounds.End.Y, (float)rng.NextDouble()));
                float thin = (float)rng.NextDouble();
                float progress = (pos.DistanceTo(Graph.Center) - innerRadius) / Mathf.Max(1f, outerRadius - innerRadius);
                // Density thins toward the VOID: the land nearest the hole is the barest.
                if (thin > 0.16f + 0.84f * Mathf.SmoothStep(0.02f, 0.55f, progress)) continue;
                if (stamp.Coast && progress < 0.70f) continue;
                Rect2 box = BoxAt(stamp, pos, size);
                if (!OnPage(box) || Conflicts(box, null) > 0) continue;
                float room = float.MaxValue;
                foreach (StampPlacement other in page.Stamps)
                    room = Mathf.Min(room, other.Pos.DistanceTo(pos) * (other.Stamp == stamp ? 0.45f : 1f));
                if (room > bestRoom) { bestRoom = room; bestPos = pos; found = true; }
            }
            if (!found) { misses++; continue; }
            misses = 0;
            Place(stamp, bestPos, size, seat: false);
        }
    }

    // ---- roads -------------------------------------------------------------------------

    private void CutRoads(RealmPage page)
    {
        foreach (MapEdge edge in Graph.Edges)
        {
            if (edge.Kind != MapEdgeKind.Road) continue;
            if (edge.A.WorldIndex != page.WorldIndex || edge.B.WorldIndex != page.WorldIndex) continue;
            page.Roads.Add(new RoadInk
            {
                Edge = edge,
                Dashes = PaperCut.Dashes(edge.Route, 7.5f, 5.0f, PinRadius(edge.A) + 3f, PinRadius(edge.B) + 3f),
            });
        }
    }

    // ---- city fields (what chars) ------------------------------------------------------

    private void ClipCityFields(RealmPage page)
    {
        foreach (MapNode city in page.CityNodes)
        {
            var entry = new CityChar { City = city };
            if (city.ControlledField != null && city.ControlledField.Length >= 3)
            {
                foreach (Vector2[] cut in Geometry2D.IntersectPolygons(city.ControlledField, page.Sheet.Outer))
                {
                    if (cut.Length < 3 || Mathf.Abs(PaperCut.SignedArea(cut)) < 4f) continue;
                    // Grown by a hair, so neighbouring fields overlap instead of leaving a
                    // speck of unburnt page where three of them meet.
                    Vector2[] piece = PaperCut.Offset(cut, 0.7f, Geometry2D.PolyJoinType.Miter);
                    int[] triangles = Geometry2D.TriangulatePolygon(piece);
                    if (triangles.Length < 3)
                    {
                        piece = cut;
                        triangles = Geometry2D.TriangulatePolygon(piece);
                        if (triangles.Length < 3) continue;
                    }
                    entry.Pieces.Add(piece);
                    entry.Triangles.Add(triangles);
                }
            }
            page.Cities.Add(entry);
        }
    }
}

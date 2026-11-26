using System;
using System.Collections.Generic;
using Godot;

namespace Fableland.Map.Pages;

/// <summary>
/// Geometry for cut and torn paper: the one tool every sheet in the torn-pages overworld is
/// made with (`Docs/Art/OverworldStyle.md` §2). A sheet is an outline; tearing it gives two
/// outlines — the fibre edge and, a little inside it, where the printed surface ends — and
/// from those come three meshes: the print, the white deckle between the two, and a soft
/// shadow. All of it is built once per generation; nothing here runs per frame.
/// </summary>
public static class PaperCut
{
    // ---- outlines ---------------------------------------------------------------

    public static float SignedArea(Vector2[] poly)
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

    public static float Perimeter(Vector2[] poly)
    {
        float length = 0f;
        for (int i = 0; i < poly.Length; i++) length += poly[i].DistanceTo(poly[(i + 1) % poly.Length]);
        return length;
    }

    public static Rect2 Bounds(Vector2[] points)
    {
        if (points == null || points.Length == 0) return new Rect2();
        Vector2 min = points[0], max = points[0];
        foreach (Vector2 p in points)
        {
            min = new Vector2(Mathf.Min(min.X, p.X), Mathf.Min(min.Y, p.Y));
            max = new Vector2(Mathf.Max(max.X, p.X), Mathf.Max(max.Y, p.Y));
        }
        return new Rect2(min, max - min);
    }

    /// <summary>Walk a closed outline and drop a point every <paramref name="spacing"/> px.</summary>
    public static Vector2[] ResampleClosed(Vector2[] poly, float spacing)
    {
        float perimeter = Perimeter(poly);
        int count = Mathf.Max(12, Mathf.RoundToInt(perimeter / Mathf.Max(0.5f, spacing)));
        return ResampleCount(poly, count, closed: true);
    }

    /// <summary>Walk an open polyline and drop a point every <paramref name="spacing"/> px,
    /// keeping both ends.</summary>
    public static Vector2[] ResampleOpen(Vector2[] line, float spacing)
    {
        float length = 0f;
        for (int i = 0; i + 1 < line.Length; i++) length += line[i].DistanceTo(line[i + 1]);
        int count = Mathf.Max(2, Mathf.RoundToInt(length / Mathf.Max(0.5f, spacing)) + 1);
        return ResampleCount(line, count, closed: false);
    }

    private static Vector2[] ResampleCount(Vector2[] pts, int count, bool closed)
    {
        int segments = closed ? pts.Length : pts.Length - 1;
        var cumulative = new float[segments + 1];
        for (int i = 0; i < segments; i++)
            cumulative[i + 1] = cumulative[i] + pts[i].DistanceTo(pts[(i + 1) % pts.Length]);
        float total = cumulative[segments];
        var result = new Vector2[count];
        int seg = 0;
        for (int k = 0; k < count; k++)
        {
            float target = closed ? total * k / count : total * k / Mathf.Max(1, count - 1);
            while (seg < segments - 1 && cumulative[seg + 1] < target) seg++;
            float span = cumulative[seg + 1] - cumulative[seg];
            float t = span > 1e-5f ? (target - cumulative[seg]) / span : 0f;
            result[k] = pts[seg].Lerp(pts[(seg + 1) % pts.Length], Mathf.Clamp(t, 0f, 1f));
        }
        return result;
    }

    /// <summary>Outward unit normals of a closed outline, from a tangent taken over
    /// <paramref name="reach"/> neighbours each side (so a jagged outline keeps calm normals).</summary>
    public static Vector2[] OutwardNormals(Vector2[] poly, int reach = 2)
    {
        bool positive = SignedArea(poly) > 0f;
        int n = poly.Length;
        var normals = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 tangent = poly[(i + reach) % n] - poly[(i - reach + n * 4) % n];
            if (tangent.LengthSquared() < 1e-8f) tangent = poly[(i + 1) % n] - poly[(i - 1 + n) % n];
            tangent = tangent.LengthSquared() < 1e-8f ? Vector2.Right : tangent.Normalized();
            normals[i] = positive ? new Vector2(tangent.Y, -tangent.X) : new Vector2(-tangent.Y, tangent.X);
        }
        return normals;
    }

    /// <summary>Grow (+) or shrink (−) an outline with rounded corners; the largest piece.</summary>
    public static Vector2[] Offset(Vector2[] poly, float delta, Geometry2D.PolyJoinType join = Geometry2D.PolyJoinType.Round)
    {
        if (Mathf.Abs(delta) < 0.01f) return poly;
        Godot.Collections.Array<Vector2[]> pieces = Geometry2D.OffsetPolygon(poly, delta, join);
        Vector2[] best = null;
        float bestArea = 0f;
        foreach (Vector2[] piece in pieces)
        {
            float area = Mathf.Abs(SignedArea(piece));
            if (area > bestArea) { bestArea = area; best = piece; }
        }
        return best ?? poly;
    }

    public static bool Contains(Vector2[] poly, Vector2 point) => Geometry2D.IsPointInPolygon(point, poly);

    public static float DistanceToOutline(Vector2[] poly, Vector2 point)
    {
        float best = float.MaxValue;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i];
            Vector2 ab = poly[(i + 1) % poly.Length] - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-6f ? Mathf.Clamp((point - a).Dot(ab) / len2, 0f, 1f) : 0f;
            float d = point.DistanceSquaredTo(a + ab * t);
            if (d < best) best = d;
        }
        return Mathf.Sqrt(best);
    }

    // ---- noise on a loop ----------------------------------------------------------

    /// <summary>Smooth value noise that closes on itself: <c>Sample(0) == Sample(1)</c>.</summary>
    public sealed class LoopNoise
    {
        private readonly float[] _values;

        public LoopNoise(DetRandom rng, int cells)
        {
            _values = new float[Mathf.Max(2, cells)];
            for (int i = 0; i < _values.Length; i++) _values[i] = (float)rng.NextDouble() * 2f - 1f;
        }

        /// <summary>−1..1 at <paramref name="t"/> in 0..1 round the loop.</summary>
        public float Sample(float t)
        {
            float x = (t - Mathf.Floor(t)) * _values.Length;
            int i0 = (int)x % _values.Length;
            int i1 = (i0 + 1) % _values.Length;
            float f = x - Mathf.Floor(x);
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(_values[i0], _values[i1], f);
        }
    }

    // ---- tearing --------------------------------------------------------------------

    /// <summary>How a sheet's edge is made. Lengths are world pixels.</summary>
    public struct TearStyle
    {
        public float Inset;        // the sheet is first shrunk by this
        public float Spacing;      // distance between outline points
        public float Wobble;       // slow wander of the tear line (± px)
        public float WobbleLength; // …over this distance
        public float Rag;          // the ragged middle scale
        public float RagLength;
        public float Fibre;        // the finest tooth, only read when zoomed in
        public float RimMin;       // width of bare paper between the tear and the print
        public float RimMax;
        public float NickEvery;    // one deeper notch per this many px of edge (0 = none)
        public float NickDepth;

        /// <summary>A storybook page torn out by hand.</summary>
        public static TearStyle Page => new()
        {
            Inset = 6.0f, Spacing = 2.6f, Wobble = 3.8f, WobbleLength = 92f, Rag = 3.0f, RagLength = 15f,
            Fibre = 0.9f, RimMin = 1.9f, RimMax = 8.8f, NickEvery = 125f, NickDepth = 8.0f,
        };

        /// <summary>A sheet of coloured paper torn more loosely (the shallows).</summary>
        public static TearStyle Sheet => new()
        {
            Inset = 0f, Spacing = 3.4f, Wobble = 6.5f, WobbleLength = 150f, Rag = 3.4f, RagLength = 24f,
            Fibre = 0.8f, RimMin = 0.9f, RimMax = 4.6f, NickEvery = 210f, NickDepth = 9f,
        };

        /// <summary>A strip cut with scissors: nearly clean.</summary>
        public static TearStyle Strip => new()
        {
            Inset = 0f, Spacing = 4.0f, Wobble = 0.9f, WobbleLength = 90f, Rag = 0.5f, RagLength = 22f,
            Fibre = 0.2f, RimMin = 0.4f, RimMax = 1.1f, NickEvery = 0f, NickDepth = 0f,
        };

        /// <summary>No tear at all — the "torn edges off" comparison.</summary>
        public static TearStyle Clean => new()
        {
            Inset = 6.0f, Spacing = 6f, Wobble = 0f, WobbleLength = 100f, Rag = 0f, RagLength = 20f,
            Fibre = 0f, RimMin = 0f, RimMax = 0f, NickEvery = 0f, NickDepth = 0f,
        };
    }

    public sealed class TornSheet
    {
        public Vector2[] Outer;     // the fibre edge
        public Vector2[] Inner;     // where the printed surface ends
        public Vector2[] Normals;   // outward, one per point
        public int[] Triangles;     // triangulation of Inner
    }

    /// <summary>
    /// Tear a sheet out along <paramref name="outline"/>. Deterministic from
    /// <paramref name="rng"/>; any simple outline works, so any generated region gets an edge.
    /// If the displaced outline folds over itself the roughness is halved and it is torn
    /// again — a sheet always comes back. <paramref name="keep"/> lists discs the tear must
    /// leave on the sheet (a pin near the edge keeps a tab of paper under it): there the edge
    /// is let back out, as far as the original outline and no further.
    /// </summary>
    public static TornSheet Tear(Vector2[] outline, DetRandom rng, TearStyle style,
        IReadOnlyList<(Vector2 At, float Radius)> keep = null)
    {
        Vector2[] basePoly = Offset(outline, -style.Inset);
        Vector2[] points = ResampleClosed(basePoly, style.Spacing);
        Vector2[] normals = OutwardNormals(points, 3);
        float perimeter = Perimeter(points);
        int n = points.Length;

        var wobble = new LoopNoise(rng, Mathf.Max(3, Mathf.RoundToInt(perimeter / Mathf.Max(8f, style.WobbleLength))));
        var rag = new LoopNoise(rng, Mathf.Max(5, Mathf.RoundToInt(perimeter / Mathf.Max(4f, style.RagLength))));
        var rag2 = new LoopNoise(rng, Mathf.Max(7, Mathf.RoundToInt(perimeter / Mathf.Max(3f, style.RagLength * 0.43f))));
        var fibre = new LoopNoise(rng, Mathf.Max(9, Mathf.RoundToInt(perimeter / Mathf.Max(2f, style.Spacing * 1.7f))));
        var rim = new LoopNoise(rng, Mathf.Max(5, Mathf.RoundToInt(perimeter / 42f)));
        var rim2 = new LoopNoise(rng, Mathf.Max(5, Mathf.RoundToInt(perimeter / 13f)));

        var nick = new float[n];
        if (style.NickEvery > 0f && style.NickDepth > 0f)
        {
            int nicks = Mathf.RoundToInt(perimeter / style.NickEvery);
            for (int k = 0; k < nicks; k++)
            {
                int at = rng.Range(0, n - 1);
                float depth = style.NickDepth * (0.45f + (float)rng.NextDouble() * 0.8f);
                int half = Mathf.Max(2, Mathf.RoundToInt((3f + (float)rng.NextDouble() * 6f) / style.Spacing));
                for (int d = -half; d <= half; d++)
                {
                    int i = ((at + d) % n + n) % n;
                    nick[i] = Mathf.Max(nick[i], depth * (1f - Mathf.Abs(d) / (float)(half + 1)));
                }
            }
        }

        // How far out each point must stay so every kept disc is still on paper.
        var hold = new float[n];
        var held = new bool[n];
        if (keep != null)
        {
            for (int i = 0; i < n; i++)
            {
                float need = float.MinValue;
                foreach ((Vector2 at, float radius) in keep)
                {
                    Vector2 to = points[i] - at;
                    float along = to.Dot(normals[i]);            // how far inside the disc's centre is
                    float lateral = (to - normals[i] * along).Length();
                    if (lateral >= radius || along < -radius) continue;
                    need = Mathf.Max(need, Mathf.Sqrt(radius * radius - lateral * lateral) - along);
                }
                if (need > float.MinValue)
                {
                    held[i] = true;
                    hold[i] = Mathf.Min(need, style.Inset + 0.5f);
                }
            }
        }

        for (float strength = 1f; strength > 0.05f; strength *= 0.5f)
        {
            var outer = new Vector2[n];
            var inner = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float slow = style.Wobble * wobble.Sample(t) - style.Wobble * 0.45f;
                float mid = style.Rag * (rag.Sample(t) * 0.65f + rag2.Sample(t) * 0.35f) * strength;
                float fine = style.Fibre * fibre.Sample(t) * strength;
                float bare = Mathf.Lerp(style.RimMin, style.RimMax,
                    Mathf.Pow(Mathf.Clamp(0.5f + 0.42f * rim.Sample(t) + 0.20f * rim2.Sample(t), 0f, 1f), 1.6f));
                float outerOffset = slow + mid + fine - nick[i] * strength;
                if (held[i]) outerOffset = Mathf.Max(outerOffset, hold[i] + fine);
                float innerOffset = Mathf.Min(slow + mid * 0.35f - nick[i] * strength * 0.8f - bare,
                    outerOffset - Mathf.Min(0.5f, style.RimMin));
                if (held[i]) innerOffset = Mathf.Max(innerOffset, Mathf.Min(hold[i] - 1.6f, outerOffset - Mathf.Min(0.9f, style.RimMin)));
                outer[i] = points[i] + normals[i] * outerOffset;
                inner[i] = points[i] + normals[i] * innerOffset;
            }

            int[] triangles = Geometry2D.TriangulatePolygon(inner);
            if (triangles.Length >= 3 && (style.RimMax <= 0f || Geometry2D.TriangulatePolygon(outer).Length >= 3))
                return new TornSheet { Outer = outer, Inner = inner, Normals = normals, Triangles = triangles };
        }

        int[] plain = Geometry2D.TriangulatePolygon(points);
        return new TornSheet { Outer = points, Inner = points, Normals = normals, Triangles = plain };
    }

    // ---- meshes -----------------------------------------------------------------------

    /// <summary>Accumulates coloured, textured triangles and turns them into one mesh.</summary>
    public sealed class MeshBuffer
    {
        public readonly List<Vector2> Vertices = new();
        public readonly List<Color> Colors = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<int> Indices = new();

        public int Add(Vector2 position, Color color, Vector2 uv)
        {
            Vertices.Add(position);
            Colors.Add(color);
            Uvs.Add(uv);
            return Vertices.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int i0 = Add(a, color, Vector2.Zero);
            int i1 = Add(b, color, Vector2.Zero);
            int i2 = Add(c, color, Vector2.Zero);
            int i3 = Add(d, color, Vector2.Zero);
            Triangle(i0, i1, i2);
            Triangle(i0, i2, i3);
        }

        /// <summary>A filled polygon from its outline and triangulation. <paramref name="uvScale"/>
        /// maps world pixels to texture space about <paramref name="uvOrigin"/>.</summary>
        public void Fill(Vector2[] outline, int[] triangles, Color color, Vector2 uvOrigin, float uvScale)
        {
            int start = Vertices.Count;
            foreach (Vector2 p in outline) Add(p, color, (p - uvOrigin) * uvScale);
            foreach (int index in triangles) Indices.Add(start + index);
        }

        /// <summary>A band between two outlines with the same point count.</summary>
        public void Band(Vector2[] a, Vector2[] b, Color colorA, Color colorB, bool closed,
            Vector2 uvOrigin = default, float uvScale = 0f)
        {
            int n = Mathf.Min(a.Length, b.Length);
            if (n < 2) return;
            int start = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Add(a[i], colorA, (a[i] - uvOrigin) * uvScale);
                Add(b[i], colorB, (b[i] - uvOrigin) * uvScale);
            }
            int last = closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                int j = (i + 1) % n;
                int a0 = start + i * 2, b0 = a0 + 1, a1 = start + j * 2, b1 = a1 + 1;
                Triangle(a0, b0, b1);
                Triangle(a0, b1, a1);
            }
        }

        /// <summary>A line of constant width along a polyline (butt ends, mitre-less joins —
        /// meant for thin strokes).</summary>
        public void Stroke(IReadOnlyList<Vector2> line, float width, Color color, bool closed = false)
        {
            int n = line.Count;
            if (n < 2) return;
            float half = width * 0.5f;
            int start = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = line[closed ? (i - 1 + n) % n : Mathf.Max(0, i - 1)];
                Vector2 next = line[closed ? (i + 1) % n : Mathf.Min(n - 1, i + 1)];
                Vector2 tangent = next - prev;
                tangent = tangent.LengthSquared() < 1e-8f ? Vector2.Right : tangent.Normalized();
                var normal = new Vector2(-tangent.Y, tangent.X);
                Add(line[i] + normal * half, color, Vector2.Zero);
                Add(line[i] - normal * half, color, Vector2.Zero);
            }
            int last = closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                int j = (i + 1) % n;
                int a0 = start + i * 2, b0 = a0 + 1, a1 = start + j * 2, b1 = a1 + 1;
                Triangle(a0, b0, b1);
                Triangle(a0, b1, a1);
            }
        }

        /// <summary>A stroke whose width (and so its weight) changes along its length.</summary>
        public void StrokeVarying(IReadOnlyList<Vector2> line, IReadOnlyList<float> widths, Color color)
        {
            int n = Mathf.Min(line.Count, widths.Count);
            if (n < 2) return;
            int start = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 tangent = line[Mathf.Min(n - 1, i + 1)] - line[Mathf.Max(0, i - 1)];
                tangent = tangent.LengthSquared() < 1e-8f ? Vector2.Right : tangent.Normalized();
                var normal = new Vector2(-tangent.Y, tangent.X) * (widths[i] * 0.5f);
                Add(line[i] + normal, color, Vector2.Zero);
                Add(line[i] - normal, color, Vector2.Zero);
            }
            for (int i = 0; i + 1 < n; i++)
            {
                int a0 = start + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                Triangle(a0, b0, b1);
                Triangle(a0, b1, a1);
            }
        }

        public bool IsEmpty => Indices.Count == 0;

        public ArrayMesh ToMesh(ArrayMesh into = null)
        {
            ArrayMesh mesh = into ?? new ArrayMesh();
            mesh.ClearSurfaces();
            if (IsEmpty) return mesh;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = Colors.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = Uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }

    /// <summary>The soft shadow a sheet throws on what lies under it: a solid sliver out to
    /// <paramref name="offset"/> and a feather beyond. Light is from the upper left.</summary>
    public static void AddShadow(MeshBuffer buffer, Vector2[] outer, Vector2[] normals, Vector2 offset,
        float feather, Color color)
    {
        int n = outer.Length;
        var shifted = new Vector2[n];
        var soft = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            shifted[i] = outer[i] + offset;
            soft[i] = shifted[i] + normals[i] * feather;
        }
        var clear = new Color(color.R, color.G, color.B, 0f);
        buffer.Band(outer, shifted, color, color, closed: true);
        buffer.Band(shifted, soft, color, clear, closed: true);
    }

    /// <summary>A moving average along a polyline: rounds its corners over about
    /// <paramref name="reach"/> points each side, so a band offset from it does not fold.</summary>
    public static Vector2[] Rounded(Vector2[] line, int reach, bool closed)
    {
        int n = line.Length;
        var result = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int span = closed ? reach : Mathf.Min(reach, Mathf.Min(i, n - 1 - i));
            Vector2 sum = Vector2.Zero;
            for (int d = -span; d <= span; d++) sum += line[((i + d) % n + n) % n];
            result[i] = sum / (span * 2 + 1);
        }
        return result;
    }

    /// <summary>Dashes along a polyline: (start, end) pairs, <paramref name="dash"/> long with
    /// <paramref name="gap"/> between, skipping <paramref name="trim"/> px at both ends.</summary>
    public static List<(Vector2 A, Vector2 B)> Dashes(Vector2[] route, float dash, float gap, float trimStart, float trimEnd, float phase = 0f)
    {
        var result = new List<(Vector2, Vector2)>();
        if (route == null || route.Length < 2) return result;
        float total = 0f;
        for (int i = 0; i + 1 < route.Length; i++) total += route[i].DistanceTo(route[i + 1]);
        float from = trimStart, to = total - trimEnd;
        if (to - from < dash * 0.6f) return result;
        // Centre the pattern so both ends finish on ink, not on a gap.
        float usable = to - from;
        int count = Mathf.Max(1, Mathf.FloorToInt((usable + gap) / (dash + gap)));
        float slack = usable - (count * dash + (count - 1) * gap);
        float cursor = from + slack * 0.5f + phase;
        for (int k = 0; k < count; k++)
        {
            float a = cursor, b = Mathf.Min(to, cursor + dash);
            if (b - a > 0.5f) result.Add((PointAt(route, a), PointAt(route, b)));
            cursor += dash + gap;
        }
        return result;
    }

    public static Vector2 PointAt(Vector2[] route, float distance)
    {
        float walked = 0f;
        for (int i = 0; i + 1 < route.Length; i++)
        {
            float length = route[i].DistanceTo(route[i + 1]);
            if (walked + length >= distance || i == route.Length - 2)
                return route[i].Lerp(route[i + 1], length > 1e-5f ? Mathf.Clamp((distance - walked) / length, 0f, 1f) : 0f);
            walked += length;
        }
        return route[^1];
    }
}

namespace Fableland.Vfx;

using System;
using Godot;

/// <summary>
/// The pen every style draws with: one triangle mesh per mark, built in static scratch
/// buffers and handed to the rendering server in a single command — so a mark is one canvas
/// command (it batches), carries per-vertex colour (soft edges without a texture), and
/// drawing it allocates nothing (T40 §2). Main thread only, used between
/// <see cref="Begin"/> and <see cref="End"/> inside a mark's <c>_Draw</c>.
/// </summary>
public static class VfxPen
{
    private const int MaxVerts = 6000;
    private const int MaxIndices = 18000;
    private static readonly Vector2[] P = new Vector2[MaxVerts];
    private static readonly Color[] C = new Color[MaxVerts];
    private static readonly Vector2[] Uv = new Vector2[MaxVerts];
    private static readonly int[] I = new int[MaxIndices];
    private static int _np, _ni;
    private static CanvasItem _item;
    private static Texture2D _texture;
    private static int _strip = -1;

    /// <summary>Triangles submitted since boot — for the check and the gallery's cost line.</summary>
    public static long Triangles { get; private set; }
    /// <summary>Canvas commands submitted since boot.</summary>
    public static long Commands { get; private set; }

    public const float Tau = Mathf.Tau;

    // ── a mark's mesh ────────────────────────────────────────────────────────────
    public static void Begin(CanvasItem item)
    {
        _item = item;
        _texture = null;
        _np = 0;
        _ni = 0;
        _strip = -1;
    }

    /// <summary>Switch texture (null = plain colour). Geometry so far is flushed first.</summary>
    public static void Use(Texture2D texture)
    {
        if (ReferenceEquals(texture, _texture)) return;
        Flush();
        _texture = texture;
    }

    public static void End()
    {
        Flush();
        _item = null;
        _texture = null;
    }

    private static void Flush()
    {
        if (_ni > 0 && _item != null)
        {
            RenderingServer.CanvasItemAddTriangleArray(_item.GetCanvasItem(),
                new ReadOnlySpan<int>(I, 0, _ni), new ReadOnlySpan<Vector2>(P, 0, _np),
                new ReadOnlySpan<Color>(C, 0, _np),
                _texture != null ? new ReadOnlySpan<Vector2>(Uv, 0, _np) : default,
                default, default, _texture != null ? _texture.GetRid() : default, -1);
            Triangles += _ni / 3;
            Commands++;
        }
        _np = 0;
        _ni = 0;
        _strip = -1;
    }

    private static bool Room(int verts, int indices)
    {
        if (_np + verts <= MaxVerts && _ni + indices <= MaxIndices) return true;
        Flush();
        return true;
    }

    /// <summary>Every vertex's alpha is multiplied by this — how a stopped loop leaves.</summary>
    public static float Fade = 1f;

    public static int V(Vector2 p, Color c)
    {
        c.A *= Fade;
        P[_np] = p;
        C[_np] = c;
        Uv[_np] = default;
        return _np++;
    }

    public static int V(Vector2 p, Color c, Vector2 uv)
    {
        c.A *= Fade;
        P[_np] = p;
        C[_np] = c;
        Uv[_np] = uv;
        return _np++;
    }

    public static void Tri(int a, int b, int c)
    {
        I[_ni++] = a;
        I[_ni++] = b;
        I[_ni++] = c;
    }

    // ── shapes ───────────────────────────────────────────────────────────────────
    public static void Triangle(Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        Room(3, 3);
        int i = V(a, col);
        V(b, col);
        V(c, col);
        Tri(i, i + 1, i + 2);
    }

    public static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        Room(4, 6);
        int i = V(a, ca);
        V(b, cb);
        V(c, cc);
        V(d, cd);
        Tri(i, i + 1, i + 2);
        Tri(i, i + 2, i + 3);
    }

    public static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) => Quad(a, b, c, d, col, col, col, col);

    /// <summary>A straight line as a quad; <paramref name="w0"/> and <paramref name="w1"/> are
    /// full widths at each end (a taper).</summary>
    public static void Line(Vector2 a, Vector2 b, float w0, float w1, Color c0, Color c1)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.001f) return;
        Vector2 n = new Vector2(d.Y, -d.X) / len;
        Quad(a + n * (w0 * 0.5f), b + n * (w1 * 0.5f), b - n * (w1 * 0.5f), a - n * (w0 * 0.5f), c0, c1, c1, c0);
    }

    public static void Line(Vector2 a, Vector2 b, float w, Color c) => Line(a, b, w, w, c, c);

    /// <summary>An axis-aligned rectangle (pixel art's only shape).</summary>
    public static void Box(Vector2 topLeft, Vector2 size, Color c) =>
        Quad(topLeft, topLeft + new Vector2(size.X, 0f), topLeft + size, topLeft + new Vector2(0f, size.Y), c);

    /// <summary>A rectangle of half-extents (hx, hy), turned by <paramref name="rot"/>.</summary>
    public static void Rect(Vector2 centre, float hx, float hy, float rot, Color c)
    {
        Vector2 x = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot)) * hx;
        Vector2 y = new Vector2(-Mathf.Sin(rot), Mathf.Cos(rot)) * hy;
        Quad(centre - x - y, centre + x - y, centre + x + y, centre - x + y, c);
    }

    /// <summary>The radius factor of a hand-made outline at an angle: a seeded sum of three
    /// sines, so a blob is never a circle and never the same blob twice.</summary>
    public static float Wobble(uint seed, float angle, float amount)
    {
        if (amount <= 0f) return 1f;
        float a = R(seed, 1) * Tau, b = R(seed, 2) * Tau, c = R(seed, 3) * Tau;
        return 1f + amount * (0.55f * Mathf.Sin(angle * 2f + a) + 0.30f * Mathf.Sin(angle * 3f + b)
                              + 0.15f * Mathf.Sin(angle * 5f + c));
    }

    /// <summary>A filled disc as a fan: <paramref name="inner"/> at the centre,
    /// <paramref name="outer"/> at the rim. <paramref name="sy"/> squashes it.</summary>
    public static void Disc(Vector2 centre, float r, Color inner, Color outer, int segs = 20,
                            float wobble = 0f, uint seed = 0, float sy = 1f)
    {
        if (r <= 0.01f) return;
        Room(segs + 2, segs * 3);
        int c0 = V(centre, inner);
        for (int i = 0; i <= segs; i++)
        {
            float a = i * Tau / segs;
            float rr = r * Wobble(seed, a, wobble);
            V(centre + new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr * sy), outer);
            if (i > 0) Tri(c0, c0 + i, c0 + i + 1);
        }
    }

    /// <summary>An annular band between two radii (a ring, a wet rim, a feather), over an
    /// angle range. Both radii follow the same wobble, so stacked bands share one outline.</summary>
    public static void Band(Vector2 centre, float r0, float r1, Color c0, Color c1, int segs = 24,
                            float wobble = 0f, uint seed = 0, float sy = 1f, float a0 = 0f, float a1 = Tau)
    {
        if (r1 <= 0.01f) return;
        Room((segs + 1) * 2, segs * 6);
        int first = _np;
        for (int i = 0; i <= segs; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)segs);
            float w = Wobble(seed, a, wobble);
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * sy);
            V(centre + d * (r0 * w), c0);
            V(centre + d * (r1 * w), c1);
            if (i == 0) continue;
            int k = first + (i - 1) * 2;
            Tri(k, k + 1, k + 3);
            Tri(k, k + 3, k + 2);
        }
    }

    /// <summary>A regular polygon, flat colour.</summary>
    public static void Ngon(Vector2 centre, float r, int sides, float rot, Color c)
    {
        Room(sides + 2, sides * 3);
        int c0 = V(centre, c);
        for (int i = 0; i <= sides; i++)
        {
            float a = rot + i * Tau / sides;
            V(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, c);
            if (i > 0) Tri(c0, c0 + i, c0 + i + 1);
        }
    }

    /// <summary>A star whose facets alternate between two flat colours — a lit face and a
    /// shadow face split by a hard terminator.</summary>
    public static void Star(Vector2 centre, float rOuter, float rInner, int points, float rot, Color lit, Color shade)
    {
        Room(points * 6, points * 6);
        for (int i = 0; i < points; i++)
        {
            float a = rot + i * Tau / points;
            float half = Mathf.Pi / points;
            Vector2 tip = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rOuter;
            Vector2 l = centre + new Vector2(Mathf.Cos(a - half), Mathf.Sin(a - half)) * rInner;
            Vector2 r = centre + new Vector2(Mathf.Cos(a + half), Mathf.Sin(a + half)) * rInner;
            Triangle(centre, l, tip, lit);
            Triangle(centre, tip, r, shade);
        }
    }

    /// <summary>A teardrop: a round head at <paramref name="p"/> and a tail of
    /// <paramref name="length"/> trailing against <paramref name="dir"/>.</summary>
    public static void Tear(Vector2 p, Vector2 dir, float length, float width, Color head, Color tail)
    {
        if (dir.LengthSquared() < 0.0001f) dir = Vector2.Up;
        Vector2 n = new Vector2(dir.Y, -dir.X) * (width * 0.5f);
        Disc(p, width * 0.5f, head, head, 10);
        Room(3, 3);
        int i = V(p + n, head);
        V(p - n, head);
        V(p - dir * length, tail);
        Tri(i, i + 1, i + 2);
    }

    // ── paths: a curve sampled at any u in 0..1, and strips laid along it ────────
    private const int MaxPath = 128;
    private static readonly Vector2[] PathP = new Vector2[MaxPath];
    private static readonly Vector2[] PathN = new Vector2[MaxPath];
    public static int PathCount { get; private set; }

    public static void PathClear() => PathCount = 0;

    public static void PathAdd(Vector2 p)
    {
        if (PathCount < MaxPath) PathP[PathCount++] = p;
    }

    public static Vector2 PathPoint(int i) => PathP[i];

    /// <summary>An arc of radius r about a centre, from angle a0 to a1 (radians).</summary>
    public static void PathArc(Vector2 centre, float rx, float ry, float a0, float a1, int n)
    {
        PathClear();
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)(n - 1));
            PathAdd(centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
        }
        PathFinish();
    }

    public static void PathLine(Vector2 a, Vector2 b, int n = 8)
    {
        PathClear();
        for (int i = 0; i < n; i++) PathAdd(a.Lerp(b, i / (float)(n - 1)));
        PathFinish();
    }

    /// <summary>Compute the normals. +normal is "up" for a stroke written left to right and
    /// outward for an arc written with rising angle.</summary>
    public static void PathFinish()
    {
        for (int i = 0; i < PathCount; i++)
        {
            Vector2 t = PathP[Math.Min(i + 1, PathCount - 1)] - PathP[Math.Max(i - 1, 0)];
            float len = t.Length();
            PathN[i] = len > 0.0001f ? new Vector2(t.Y, -t.X) / len : Vector2.Up;
        }
    }

    public static void PathAt(float u, out Vector2 p, out Vector2 n)
    {
        if (PathCount < 2)
        {
            p = PathCount == 1 ? PathP[0] : Vector2.Zero;
            n = Vector2.Up;
            return;
        }
        float f = Mathf.Clamp(u, 0f, 1f) * (PathCount - 1);
        int i = Math.Min((int)f, PathCount - 2);
        float k = f - i;
        p = PathP[i].Lerp(PathP[i + 1], k);
        n = PathN[i].Lerp(PathN[i + 1], k);
    }

    public static void StripBegin() => _strip = -1;

    /// <summary>Add one row of a strip — two vertices — joined to the row before it.</summary>
    public static void StripRow(Vector2 lo, Vector2 hi, Color cLo, Color cHi)
    {
        if (_np + 2 > MaxVerts || _ni + 6 > MaxIndices) { Flush(); }
        int a = V(lo, cLo);
        V(hi, cHi);
        if (_strip >= 0)
        {
            Tri(_strip, _strip + 1, a + 1);
            Tri(_strip, a + 1, a);
        }
        _strip = a;
    }

    public static void StripRow(Vector2 lo, Vector2 hi, Color cLo, Color cHi, Vector2 uvLo, Vector2 uvHi)
    {
        if (_np + 2 > MaxVerts || _ni + 6 > MaxIndices) { Flush(); }
        int a = V(lo, cLo, uvLo);
        V(hi, cHi, uvHi);
        if (_strip >= 0)
        {
            Tri(_strip, _strip + 1, a + 1);
            Tri(_strip, a + 1, a);
        }
        _strip = a;
    }

    /// <summary>A row across the path at u, from lateral offset lo to hi (px along the normal).</summary>
    public static void StripAt(float u, float lo, float hi, Color cLo, Color cHi)
    {
        PathAt(u, out Vector2 p, out Vector2 n);
        StripRow(p + n * lo, p + n * hi, cLo, cHi);
    }

    /// <summary>A band along the path between u0 and u1 whose half-width follows a crescent
    /// profile (0 at both ends), shifted sideways by <paramref name="offset"/>.</summary>
    public static void Crescent(float u0, float u1, float halfWidth, float offset, Color cLo, Color cHi, int rows = 14, float power = 0.8f)
    {
        if (u1 - u0 < 0.001f) return;
        StripBegin();
        for (int k = 0; k <= rows; k++)
        {
            float u = Mathf.Lerp(u0, u1, k / (float)rows);
            float hw = halfWidth * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.Pi)), power);
            StripAt(u, offset - hw, offset + hw, cLo, cHi);
        }
    }

    /// <summary>Part of a crescent: the band between two fractions (−1 inner edge … +1 outer
    /// edge) of its local half-width — a two-tone stroke is two of these.</summary>
    public static void CrescentBand(float u0, float u1, float halfWidth, float f0, float f1, Color c0, Color c1, int rows = 14, float power = 0.8f)
    {
        if (u1 - u0 < 0.001f) return;
        StripBegin();
        for (int k = 0; k <= rows; k++)
        {
            float u = Mathf.Lerp(u0, u1, k / (float)rows);
            float hw = halfWidth * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.Pi)), power);
            StripAt(u, f0 * hw, f1 * hw, c0, c1);
        }
    }

    /// <summary>The crescent's local half-width at u — for styles that stamp along a stroke.</summary>
    public static float CrescentWidth(float u, float halfWidth, float power = 0.8f) =>
        halfWidth * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.Pi)), power);

    /// <summary>The length of the path in px.</summary>
    public static float PathLength()
    {
        float length = 0f;
        for (int i = 1; i < PathCount; i++) length += PathP[i].DistanceTo(PathP[i - 1]);
        return length;
    }

    /// <summary>The path's bounding box.</summary>
    public static Rect2 PathBounds()
    {
        if (PathCount == 0) return default;
        Vector2 lo = PathP[0], hi = PathP[0];
        for (int i = 1; i < PathCount; i++)
        {
            lo = new Vector2(Mathf.Min(lo.X, PathP[i].X), Mathf.Min(lo.Y, PathP[i].Y));
            hi = new Vector2(Mathf.Max(hi.X, PathP[i].X), Mathf.Max(hi.Y, PathP[i].Y));
        }
        return new Rect2(lo, hi - lo);
    }

    /// <summary>Is p inside the path, taken as a closed convex polygon?</summary>
    public static bool PathContains(Vector2 p)
    {
        if (PathCount < 3) return false;
        bool positive = false, negative = false;
        for (int i = 0; i < PathCount; i++)
        {
            Vector2 a = PathP[i], b = PathP[(i + 1) % PathCount];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (cross > 0f) positive = true;
            else if (cross < 0f) negative = true;
            if (positive && negative) return false;
        }
        return true;
    }

    /// <summary>A line riding one edge of a crescent (side +1 = the outer, leading edge).</summary>
    public static void CrescentEdge(float u0, float u1, float halfWidth, float offset, float lineWidth, Color c, float side = 1f, int rows = 14, float power = 0.8f)
    {
        if (u1 - u0 < 0.001f) return;
        StripBegin();
        for (int k = 0; k <= rows; k++)
        {
            float u = Mathf.Lerp(u0, u1, k / (float)rows);
            float s = Mathf.Max(0f, Mathf.Sin(u * Mathf.Pi));
            float at = offset + side * halfWidth * Mathf.Pow(s, power);
            float w = lineWidth * 0.5f * (0.35f + 0.65f * s);
            StripAt(u, at - w, at + w, c, c);
        }
    }

    /// <summary>A constant-width line along the path between u0 and u1.</summary>
    public static void Wire(float u0, float u1, float width, float offset, Color c, int rows = 14)
    {
        if (u1 - u0 < 0.001f) return;
        StripBegin();
        for (int k = 0; k <= rows; k++)
            StripAt(Mathf.Lerp(u0, u1, k / (float)rows), offset - width * 0.5f, offset + width * 0.5f, c, c);
    }

    /// <summary>Fill the path as a closed convex polygon (a fan about its centroid).</summary>
    public static void Fill(Color centre, Color edge)
    {
        if (PathCount < 3) return;
        Vector2 mid = Vector2.Zero;
        for (int i = 0; i < PathCount; i++) mid += PathP[i];
        mid /= PathCount;
        Room(PathCount + 2, PathCount * 3);
        int c0 = V(mid, centre);
        for (int i = 0; i <= PathCount; i++)
        {
            V(PathP[i % PathCount], edge);
            if (i > 0) Tri(c0, c0 + i, c0 + i + 1);
        }
    }

    // ── arithmetic the styles share ──────────────────────────────────────────────
    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    /// <summary>A repeatable random number in [0, 1) for a seed and an index.</summary>
    public static float R(uint seed, int i) => (Hash(seed + (uint)i * 0x9E3779B9u) & 0xFFFFFF) / 16777216f;
    /// <summary>The same in [−1, 1).</summary>
    public static float Rs(uint seed, int i) => R(seed, i) * 2f - 1f;

    public static float Sat(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
    public static float EaseOut(float t) { t = 1f - Sat(t); return 1f - t * t * t; }
    public static float EaseIn(float t) { t = Sat(t); return t * t * t; }
    public static float Smooth(float t) { t = Sat(t); return t * t * (3f - 2f * t); }
    /// <summary>Out with a small overshoot — a pop that settles.</summary>
    public static float Back(float t)
    {
        t = Sat(t) - 1f;
        return 1f + t * t * (2.70158f * t + 1.70158f);
    }
    /// <summary>0 → 1 → 0 across a life: up over the first <paramref name="rise"/>, down after.</summary>
    public static float Bell(float t, float rise = 0.2f) => t < rise ? Sat(t / rise) : Sat(1f - (t - rise) / (1f - rise));

    /// <summary>A colour at a share of its alpha.</summary>
    public static Color A(Color c, float alpha) => new(c.R, c.G, c.B, c.A * Sat(alpha));
    public static Color Mix(Color a, Color b, float t) => a.Lerp(b, Sat(t));
    public static Vector2 Polar(float angle, float r) => new(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
    public static Vector2 Snap(Vector2 p, float grid) =>
        grid <= 0f ? p : new Vector2(Mathf.Round(p.X / grid) * grid, Mathf.Round(p.Y / grid) * grid);
    public static float Snap(float v, float grid) => grid <= 0f ? v : Mathf.Round(v / grid) * grid;
}

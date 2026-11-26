namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// KH Dark Wood — scratchboard (RealmStyleBible §7A; R2 §4.5). A black board with light
/// scratched out of it: a mark is a black cut-out and thin, nervous lines on it — radiating
/// hatches, cross-hatching, wandering curves. Nothing is filled with colour. The lines are
/// scratched in over two or three frames, boil at eleven frames a second, and leave one by
/// one. Colour is the realm's code and not decoration: moon blue is neutral, yellow gives
/// light (anything that burns or blasts), red means danger.
/// </summary>
public sealed class ScratchboardStyle : VfxStyle
{
    public override string Id => "scratchboard";
    public override string Medium => "Scratchboard";
    public override float Fps => 11f;
    public override bool Boils => true;
    public override int MaxPieces => 14;
    protected override bool BoxedField => true;
    public override float ReleaseSeconds => 0f;

    private static readonly Color Board = Hex("070a14");

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        (string main, string alt, string accent) = element switch
        {
            VfxElement.Fire or VfxElement.Light or VfxElement.Heal => ("ffd23f", "fff1a8", "fff8d0"),    // gives light
            VfxElement.Danger => ("d8323a", "7a161c", "ffd23f"),
            VfxElement.Dust or VfxElement.Smoke or VfxElement.Water or VfxElement.Leaf => ("3f6fd8", "1b2d6b", "9dbeff"),
            _ => ("9dbeff", "3f6fd8", "fff1a8"),                                                         // moonlight
        };
        // The board is the medium: every mark cuts its own black first, so it reads on any canvas.
        return new VfxPalette(Hex(main), Hex(alt), Hex(accent), Hex(main), Board);
    }

    private const float Hair = 1.4f;

    /// <summary>How many of n lines are on the board at this moment: scratched in over the
    /// first three frames, dropping out one at a time at the end.</summary>
    private static bool Shown(VfxMark m, int i, int n, float t)
    {
        if (i >= (m.Step + 1) * (n / 2 + 1)) return false;                // not scratched yet
        return R(m.S.Seed, i + 700) >= (t - 0.55f) / 0.45f;               // already gone?
    }

    private static Vector2 Boil(VfxMark m, int i) => new(Rs(m.Boil, i * 2 + 11) * 1.2f, Rs(m.Boil, i * 2 + 12) * 1.2f);

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        uint seed = m.S.Seed + (uint)(int)(c.X * 3f + c.Y * 5f);
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            // Fog: a black shape with two or three nervous curls scratched on it.
            Disc(c, r * 0.95f, p.Ground, p.Ground, 12, 0.22f, seed);
            for (int k = 0; k < 3; k++)
            {
                if (!Shown(m, k, 3, t)) continue;
                float start = R(seed, k) * Tau;
                PathArc(c + Boil(m, k), r * (0.3f + 0.2f * k), r * (0.25f + 0.18f * k), start, start + 3.6f, 10);
                Wire(0f, 1f, Hair, 0f, k == 0 ? p.Main : p.Alt, 9);
            }
            return;
        }
        bool big = (m.S.Flags & VfxFlags.Big) != 0;
        float radius = r * 1.25f;
        Disc(c, radius * (t < 0.6f ? 1f : 1f - (t - 0.6f) * 0.8f), p.Ground, p.Ground, 16, 0.18f, seed);    // the cut-out
        int n = big ? 22 : 13;
        for (int i = 0; i < n; i++)
        {
            if (!Shown(m, i, n, t)) continue;
            float a = i * Tau / n + Rs(seed, i) * 0.22f;
            float inner = radius * (0.18f + 0.2f * R(seed, i + 50)), outer = radius * (0.62f + 0.75f * R(seed, i + 90));
            Vector2 jitter = Boil(m, i);
            Line(c + Polar(a, inner) + jitter, c + Polar(a, outer) + jitter, Hair, i % 3 == 0 ? p.Alt : p.Main);
        }
        if (m.Step > 1) return;
        // A cross-hatched X on the contact, for the first two frames.
        for (int k = -1; k <= 1; k++)
        {
            Vector2 off = new Vector2(k * 3.2f, 0f);
            Line(c + off + new Vector2(-radius, -radius) * 0.5f, c + off + new Vector2(radius, radius) * 0.5f, Hair, p.Accent);
            Line(c + off + new Vector2(-radius, radius) * 0.5f, c + off + new Vector2(radius, -radius) * 0.5f, Hair, p.Accent);
        }
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        if (u1 - u0 < 0.01f) return;
        ref VfxPalette p = ref m.S.P;
        float hw = width * 0.5f;
        CrescentBand(u0, u1, hw * 1.1f, -1f, 1f, p.Ground, p.Ground, 12, 0.7f);            // black under the scratches
        // A bundle of thin lines running with the stroke, none of them steady.
        for (int k = 0; k < 6; k++)
        {
            if (!Shown(m, k, 6, t)) continue;
            float f = Mathf.Lerp(-0.74f, 0.84f, k / 5f), start = u0 + (u1 - u0) * 0.12f * R(m.S.Seed, k + 20);
            StripBegin();
            for (int row = 0; row <= 12; row++)
            {
                float u = Mathf.Lerp(start, u1, row / 12f);
                float at = f * CrescentWidth(u, hw, 0.7f) + Rs(m.Boil, k * 16 + row) * 1.0f;
                StripAt(u, at - Hair * 0.5f, at + Hair * 0.5f, k >= 2 ? p.Main : p.Alt, k >= 2 ? p.Main : p.Alt);
            }
        }
        // Hatches thrown off the leading edge.
        for (int i = 0; i < 7; i++)
        {
            float u = 0.14f + i * 0.12f;
            if (u < u0 || u > u1 || !Shown(m, i + 5, 7, t)) continue;
            PathAt(u, out Vector2 at, out Vector2 n);
            float w = CrescentWidth(u, hw, 0.7f);
            Line(at + n * (w * 0.85f), at + n * (w * 1.05f + 5f + 4f * R(m.Boil, i)), Hair, p.Accent);
        }
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float sy = ry / Mathf.Max(rx, 0.01f);
        Band(c, rx - 4f, rx + 4f, p.Ground, p.Ground, 30, 0.04f, m.S.Seed, sy);
        // One wandering line round, and short ticks scratched across it.
        Band(c, rx - Hair * 0.5f, rx + Hair * 0.5f, p.Main, p.Main, 30, 0.035f, m.Boil, sy);
        const int ticks = 20;
        for (int i = 0; i < ticks; i++)
        {
            if (!Shown(m, i, ticks, t)) continue;
            float a = i * Tau / ticks + Rs(m.S.Seed, i) * 0.1f, len = 3f + 5f * R(m.Boil, i);
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * sy);
            Line(c + d * (rx - len * 0.4f), c + d * (rx + len), Hair, i % 2 == 0 ? p.Alt : p.Main);
        }
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        if (R(m.S.Seed, i + 700) < (t - 0.5f) / 0.5f) return;            // drops out; never fades
        Vector2 half = Polar(rot + Rs(m.Boil, i) * 0.25f, size * 0.9f);
        Line(p - half, p + half, Hair + 2.6f, pal.Ground);
        Line(p - half, p + half, Hair, i % 3 == 0 ? pal.Alt : pal.Main);
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        Fill(A(p.Ground, 0.55f), A(p.Ground, 0.55f));
        int n = PathCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = PathPoint(i), b = PathPoint((i + 1) % n);
            Line(a + Boil(m, i), b + Boil(m, i + 1), Hair, p.Main);
            Line(a + Boil(m, i + 7), b + Boil(m, i + 8), Hair, p.Alt);     // the line is drawn twice and never tidy
        }
        // Cross-hatching, denser as the moment nears.
        Rect2 box = PathBounds();
        float gap = Mathf.Lerp(16f, 8f, t), span = box.Size.X + box.Size.Y;
        int drawn = 0;
        for (float d = gap; d < span && drawn < 70; d += gap, drawn++)
        {
            Vector2 a = box.Position + new Vector2(Mathf.Min(d, box.Size.X), Mathf.Max(0f, d - box.Size.X));
            Vector2 b = box.Position + new Vector2(Mathf.Max(0f, d - box.Size.Y), Mathf.Min(d, box.Size.Y));
            Vector2 lo = a.Lerp(b, 0.1f + 0.2f * R(m.Boil, drawn)), hi = a.Lerp(b, 0.9f - 0.2f * R(m.Boil, drawn + 40));
            if (PathContains(lo) && PathContains(hi)) Line(lo, hi, 1.1f, drawn % 2 == 0 ? p.Main : p.Alt);
        }
    }

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            // Fireflies: the one thing in the wood that gives light. Slow yellow blinks.
            Id = "fireflies", Texture = "soft_dot", Additive = true, Amount = 8, Lifetime = 6f, Direction = Vector2.Up, SpreadDeg = 180f,
            SpeedMin = 4f, SpeedMax = 14f, SizeMin = 4f, SizeMax = 7f,
            A = Hex("ffd23f", 0.95f), B = Hex("fff1a8", 0.95f), ALight = Hex("d9a13b", 0.9f), BLight = Hex("ffd23f", 0.9f),
            Ramp = Ramp((0f, 0f), (0.12f, 1f), (0.3f, 0.15f), (0.5f, 1f), (0.68f, 0.1f), (0.85f, 0.9f), (1f, 0f)),
        },
        new()
        {
            Id = "fog_scratches", Texture = "scratch/tick", Amount = 4, Lifetime = 5f, Direction = new Vector2(1f, 0.1f), SpreadDeg = 12f,
            SpeedMin = 10f, SpeedMax = 26f, SizeMin = 5f, SizeMax = 8f, Fps = 11f, AlignToVelocity = true,
            A = Hex("9dbeff", 0.5f), B = Hex("3f6fd8", 0.5f), ALight = Hex("1b2d6b", 0.6f), BLight = Hex("3f6fd8", 0.6f),
            Ramp = Ramp((0f, 0f), (0.2f, 1f), (0.8f, 1f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

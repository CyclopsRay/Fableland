namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// BM Wulin — freehand ink, 写意 (RealmStyleBible §4; R2 §4.5). A mark is one stroke of the
/// brush: pressed down, so it swells; lifted, so it tapers and ends in dry flying-white. It
/// is written on in a few held frames (12 a second), holds, and then bleeds — its edge
/// grows while its ink thins. A hit is a blot with satellite dots and, for two frames, a disc
/// of bare paper. Five dilutions of black, indigo-cyan and ink green: no warm hue, ever —
/// fire here is black flame with a cyan heart. Off its own paper, a mark brings its paper
/// with it.
/// </summary>
public sealed class InkStyle : VfxStyle
{
    public override string Id => "ink";
    public override string Medium => "Freehand ink (写意)";
    public override float Fps => 12f;
    public override float Tempo => 1.6f;
    public override int MaxPieces => 10;
    public override float ReleaseSeconds => 0.30f;

    private static readonly Color Paper = Hex("f7f9f8");
    private static Texture2D DryBrush => VfxTextures.Get("ink/drybrush");

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        bool pale = canvas == VfxCanvas.Light;
        (string main, string alt, string accent) = element switch
        {
            VfxElement.Dust => ("8e979b", "bec6c8", "f7f9f8"),          // dust is mist: thin ink and bare paper
            VfxElement.Fire => ("14171a", "2f6b7c", "a6cad0"),
            VfxElement.Ice => ("5e9ba9", "a6cad0", "f7f9f8"),
            VfxElement.Water => ("5e9ba9", "2f6b7c", "f7f9f8"),
            VfxElement.Leaf => ("33574a", "1c352b", "6e8f80"),
            VfxElement.Poison => ("6e8f80", "33574a", "bec6c8"),
            VfxElement.Danger => ("14171a", "2a2f33", "2f6b7c"),
            VfxElement.Heal => ("33574a", "6e8f80", "f7f9f8"),
            VfxElement.Light => pale ? ("565e63", "8e979b", "f7f9f8") : ("f7f9f8", "bec6c8", "a6cad0"),
            VfxElement.Smoke => ("8e979b", "565e63", "bec6c8"),
            _ => ("14171a", "565e63", "bec6c8"),
        };
        Color ground = canvas switch { VfxCanvas.Dark => A(Paper, 0.86f), VfxCanvas.Mid => A(Paper, 0.50f), _ => default };
        return new VfxPalette(Hex(main), Hex(alt), Hex(accent), Hex("14171a"), ground);
    }

    /// <summary>How hard the brush is pressed along a stroke: down fast, held, lifted to a thread.</summary>
    private static float Press(float u) =>
        u < 0.6f ? 0.30f + 0.70f * Mathf.Pow(Mathf.Sin(Mathf.Min(1f, u * 1.7f) * Mathf.Pi * 0.5f), 0.6f)
                 : Mathf.Lerp(1f, 0.18f, (u - 0.6f) / 0.4f);

    /// <summary>How far a mark has bled: nothing for the first third of its life.</summary>
    private static float Bleed(float t) => Smooth((t - 0.34f) / 0.66f);
    private static float Thin(float t) => 1f - Smooth((t - 0.55f) / 0.45f);

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        uint seed = m.S.Seed + (uint)(int)(c.X * 5f + c.Y * 11f);
        float a = Thin(t), bleed = Bleed(t);
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            // Mist is unpainted: a breath of bare paper with a thread of thin ink under it.
            Disc(c, r * (0.8f + 0.5f * t), A(Paper, 0.80f * a), A(Paper, 0f), 16, 0.12f, seed);
            Line(c + new Vector2(-r * 0.6f, r * 0.25f), c + new Vector2(r * 0.6f, r * 0.25f), 2.4f, 0.6f, A(p.Main, 0.55f * a), A(p.Main, 0f));
            return;
        }
        float radius = r * 0.9f;
        if (t < 0.13f) Disc(c, radius * 1.7f, A(Paper, 0.95f), A(Paper, 0.9f), 18, 0.08f, seed + 3u);   // the paper flashes bare
        else if (p.Ground.A > 0f) Disc(c, radius * 1.45f, A(p.Ground, 0.85f * a), A(p.Ground, 0f), 18, 0.10f, seed + 3u);
        // The bleed: the edge creeps outward in a thinner dilution.
        Band(c, radius * 0.7f, radius * (1.0f + 0.55f * bleed), A(p.Alt, 0.55f * a), A(p.Alt, 0f), 22, 0.26f, seed);
        Disc(c, radius, A(p.Main, 0.95f * a), A(p.Main, 0.92f * a), 22, 0.26f, seed);
        Disc(c + new Vector2(radius * 0.15f, -radius * 0.1f), radius * 0.42f, A(p.Line, a), A(p.Line, 0.9f * a), 10, 0.2f, seed + 9u);
        // Satellites, flung on along the blow.
        int dots = (m.S.Flags & VfxFlags.Big) != 0 ? 7 : 4;
        for (int i = 0; i < dots; i++)
        {
            float angle = m.S.Rot + Rs(seed, i + 20) * ((m.S.Flags & VfxFlags.Big) != 0 ? Mathf.Pi : 0.9f);
            Vector2 at = c + Polar(angle, radius * (1.35f + 0.9f * R(seed, i + 30)));
            Disc(at, radius * (0.09f + 0.14f * R(seed, i + 40)), A(p.Main, 0.95f * a), A(p.Main, 0.9f * a), 8, 0.2f, seed + (uint)i);
        }
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        if (u1 - u0 < 0.01f) return;
        ref VfxPalette p = ref m.S.P;
        float a = Thin(t), bleed = Bleed(t), hw = width * 0.5f;
        const int rows = 18;
        if (p.Ground.A > 0f)
        {
            // Bare paper under the stroke: fullest along it, gone a little way out.
            for (int side = -1; side <= 1; side += 2)
            {
                StripBegin();
                for (int k = 0; k <= rows; k++)
                {
                    float u = Mathf.Lerp(u0, u1, k / (float)rows), w = hw * Press(u) * 1.35f + 4f;
                    StripAt(u, 0f, side * w, A(p.Ground, 0.9f * a), A(p.Ground, 0f));
                }
            }
        }
        if (bleed > 0f)
        {
            StripBegin();
            for (int k = 0; k <= rows; k++)
            {
                float u = Mathf.Lerp(u0, u1, k / (float)rows), w = hw * Press(u) * (1f + 0.5f * bleed);
                StripAt(u, -w, w, A(p.Alt, 0.40f * a), A(p.Alt, 0.40f * a));
            }
        }
        // The stroke itself, through the dry-brush mask: full ink, then flying white.
        Use(DryBrush);
        StripBegin();
        for (int k = 0; k <= rows; k++)
        {
            float u = Mathf.Lerp(u0, u1, k / (float)rows), w = hw * Press(u);
            PathAt(u, out Vector2 at, out Vector2 n);
            Color ink = A(p.Main, 0.96f * a);
            StripRow(at - n * w, at + n * w, ink, ink, new Vector2(u, 0f), new Vector2(u, 1f));
        }
        Use(null);
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        // An enso: one turn of the brush that does not quite close.
        float start = R(m.S.Seed, 2) * Tau;
        PathArc(c, rx, ry, start, start + Tau * 0.92f, 40);
        Brush(m, Mathf.Clamp(rx * 0.22f, 3.5f, 14f), 0f, 1f, t);
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        float a = Thin(t);
        Vector2 dir = Polar(rot, 1f);
        if (pal.Ground.A > 0f) Disc(p, size * 0.9f, A(pal.Ground, a), A(pal.Ground, 0f), 8);
        // A flicked dab: a dot with the lift of the brush behind it.
        Color ink = A(i % 3 == 2 ? pal.Alt : pal.Main, 0.95f * a);
        Tear(p, dir, size * 1.7f, size * 0.85f, ink, A(ink, 0.2f));
        if (i % 2 == 0) Disc(p + dir * (size * 1.1f) + Polar(rot + 1.3f, size * 0.5f), size * 0.16f, ink, ink, 6);
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        if (p.Ground.A > 0f) Fill(A(p.Ground, 0.7f), A(p.Ground, 0.7f));
        Fill(A(p.Accent, 0.10f + 0.20f * t), A(p.Alt, 0.22f + 0.26f * t));      // a wash of the thinnest ink
        // Dry-brush corners mark the true box.
        int n = PathCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 corner = PathPoint(i), next = PathPoint((i + 1) % n), prev = PathPoint((i + n - 1) % n);
            float arm = Mathf.Min(18f, corner.DistanceTo(next) * 0.3f);
            Line(corner, corner + (next - corner).Normalized() * arm, 3.6f, 0.8f, A(p.Line, 0.95f), A(p.Line, 0.5f));
            Line(corner, corner + (prev - corner).Normalized() * Mathf.Min(arm, corner.DistanceTo(prev) * 0.4f), 3.6f, 0.8f, A(p.Line, 0.95f), A(p.Line, 0.5f));
        }
    }

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            Id = "bamboo_leaves", Texture = "ink/leaf", Amount = 8, Lifetime = 8f, Direction = new Vector2(-0.35f, 1f), SpreadDeg = 25f,
            SpeedMin = 18f, SpeedMax = 42f, SizeMin = 14f, SizeMax = 22f, SpinDeg = 70f, Fps = 12f,
            A = Hex("bec6c8", 0.8f), B = Hex("6e8f80", 0.8f), ALight = Hex("33574a", 0.85f), BLight = Hex("565e63", 0.8f),
            Ramp = Ramp((0f, 0f), (0.1f, 1f), (0.85f, 1f), (1f, 0f)),
        },
        new()
        {
            Id = "mist", Texture = "soft_dot", Amount = 4, Lifetime = 9f, Direction = Vector2.Right, SpreadDeg = 20f,
            SpeedMin = 6f, SpeedMax = 14f, SizeMin = 46f, SizeMax = 90f, BottomBand = 0.45f,
            A = Hex("f7f9f8", 0.16f), B = Hex("bec6c8", 0.14f), ALight = Hex("f7f9f8", 0.55f), BLight = Hex("ffffff", 0.45f),
            Ramp = Ramp((0f, 0f), (0.3f, 1f), (0.7f, 1f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

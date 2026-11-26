namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// SL Starfield — flat minimalist poster (RealmStyleBible §3; R2 §4.5). No outline, no
/// gradient, no texture: every form is two flat planes split by a hard terminator — a lit
/// face and a shadow face. Shapes are few, large and geometric (four-point stars, diamonds,
/// flat rings, hairlines); motion is linear; a mark ends by shrinking to nothing or by
/// stepping its alpha in three bands, never by a soft fade. At most five pieces: the poster
/// forbids clutter.
/// </summary>
public sealed class PosterStyle : VfxStyle
{
    public override string Id => "poster";
    public override string Medium => "Flat minimalist poster";
    public override float Fps => 30f;
    public override float Tempo => 0.85f;
    public override int MaxPieces => 5;
    protected override bool BoxedField => true;
    public override float ReleaseSeconds => 0f;
    public override float Ease(float t) => Sat(t);                       // linear: nothing eases on a poster

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        // Lit face, shadow face, the bright cut, the hairline. On a pale canvas the lit face
        // deepens and the shadow face is plum, so the shape still reads.
        bool pale = canvas == VfxCanvas.Light;
        (string lit, string shade, string cut) = element switch
        {
            VfxElement.Dust => pale ? ("cc8a62", "9a5c5e", "f6d79c") : ("f6d79c", "cc8a62", "fff8e0"),
            VfxElement.Fire => ("f0875c", "a8527a", "fbd08a"),
            VfxElement.Ice => ("35b3a8", "3b2a6e", "fff8e0"),
            VfxElement.Water => ("35b3a8", "3b2a6e", "fbe08a"),
            VfxElement.Leaf => ("35b3a8", "5b3a63", "fbe08a"),
            VfxElement.Poison => ("a8527a", "5b3a63", "fbd08a"),
            VfxElement.Danger => ("f0875c", "5b3a63", "fbd08a"),
            VfxElement.Heal => ("35b3a8", "5b3a63", "fff8e0"),
            VfxElement.Light => pale ? ("d9a441", "5b3a63", "fbe08a") : ("fff8e0", "fbe08a", "fff8e0"),
            VfxElement.Smoke => ("5b3a63", "3b2a6e", "a8527a"),
            _ => pale ? ("d9a441", "5b3a63", "35b3a8") : ("fbe08a", "d9a441", "fff8e0"),
        };
        return new VfxPalette(Hex(lit), Hex(shade), Hex(cut), Hex(pale ? "5b3a63" : "fbe08a"));
    }

    public override VfxPalette Look(VfxLook look, VfxCanvas canvas)
    {
        VfxPalette p = base.Look(look, canvas);
        if (canvas == VfxCanvas.Light && look != null && look.HasColours) { p.Main = look.Alt; p.Alt = Hex("5b3a63"); }
        return p;
    }

    /// <summary>Alpha in three hard bands — the poster's only way to grow faint.</summary>
    private static float Bands(float t, float from = 0.55f)
    {
        if (t < from) return 1f;
        float k = (t - from) / (1f - from);
        return k < 0.34f ? 0.66f : k < 0.67f ? 0.33f : 0f;
    }

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            // Dust is a flat half-diamond lying on the ground: lit to the left, shadow to the right.
            float a = Bands(t, 0.5f), w = r * (0.9f + 0.5f * t), h = r * 0.7f * (1f - t * 0.6f);
            Triangle(c + new Vector2(-w, 0f), c + new Vector2(0f, -h), c, A(p.Main, a));
            Triangle(c, c + new Vector2(0f, -h), c + new Vector2(w, 0f), A(p.Alt, a));
            return;
        }
        // A four-point star: it pops over-size for one frame, turns 45° and shrinks to nothing.
        float scale = t < 0.12f ? 1.3f : 1f - (t - 0.12f) / 0.88f;
        float turn = Mathf.Pi * 0.25f * Sat(t / 0.6f) + R(m.S.Seed, 5) * 0.3f;
        if ((m.S.Flags & VfxFlags.Big) != 0)
        {
            // An explosion: a flat disc, a star cut out of it.
            float a = Bands(t, 0.6f), grow = 0.6f + 0.4f * Sat(t / 0.25f);
            Ngon(c, r * grow, 28, 0f, A(p.Main, a));
            Star(c, r * grow * 0.86f, r * grow * 0.30f, 4, turn, A(p.Accent, a), A(p.Alt, a));
            return;
        }
        Star(c, r * 1.25f * scale, r * 0.36f * scale, 4, turn, p.Main, p.Alt);
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = Bands(t, 0.6f), hw = width * 0.5f * (1f - 0.35f * Sat((t - 0.5f) / 0.5f));
        // Two flat planes, split down the stroke: lit outside, shadow inside. No blend between.
        CrescentBand(u0, u1, hw, 0f, 1f, A(p.Main, a), A(p.Main, a), 16, 0.7f);
        CrescentBand(u0, u1, hw, -1f, 0f, A(p.Alt, a), A(p.Alt, a), 16, 0.7f);
        if (t < 0.5f) CrescentEdge(u0, u1, hw * 1.35f, 0f, 1.4f, p.Line, 1f, 16, 0.7f);    // a hairline of inlay
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = Bands(t, 0.5f), sy = ry / Mathf.Max(rx, 0.01f);
        Band(c, rx - 1.4f, rx + 1.4f, A(p.Main, a), A(p.Main, a), 40, 0f, 0u, sy);
        // The shadow half of the ring: the same plane, the other tone.
        Band(c, rx - 1.4f, rx + 1.4f, A(p.Alt, a), A(p.Alt, a), 20, 0f, 0u, sy, Mathf.Pi * 0.15f, Mathf.Pi * 1.15f);
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        float s = size * 1.25f * (1f - t);                              // shrinks to nothing; no fade
        Vector2 x = Polar(rot, s), y = Polar(rot + Mathf.Pi * 0.5f, s * 0.55f);
        Triangle(p - x, p + y, p + x, pal.Main);
        Triangle(p - x, p + x, p - y, pal.Alt);
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = t < 0.34f ? 0.16f : t < 0.67f ? 0.26f : 0.38f;       // it fills in three steps
        Fill(A(p.Main, a), A(p.Main, a));
        int n = PathCount;
        for (int i = 0; i < n; i++) Line(PathPoint(i), PathPoint((i + 1) % n), 1.5f, p.Line);
    }

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            // Sparse twinkles: hard on, hard off, slow.
            Id = "twinkles", Texture = "poster/star", Amount = 8, Lifetime = 4.5f, Direction = Vector2.Up, SpreadDeg = 180f,
            SpeedMin = 0f, SpeedMax = 3f, SizeMin = 5f, SizeMax = 10f,
            A = Hex("fff8e0", 0.9f), B = Hex("fbe08a", 0.9f), ALight = Hex("d9a441", 0.9f), BLight = Hex("5b3a63", 0.8f),
            Ramp = Ramp((0f, 0f), (0.20f, 0f), (0.21f, 1f), (0.48f, 1f), (0.49f, 0.4f), (0.62f, 0.4f), (0.63f, 1f), (0.80f, 1f), (0.81f, 0f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

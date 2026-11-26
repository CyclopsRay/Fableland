namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// The VOID, zone 6 — erasure (R2 §4.5; a proposal: no art bible stands behind it yet). A
/// mark is a hole: a black disc with a hairline white rim that eats what it touches, and
/// white outline shards. Everything runs backwards — pieces converge instead of scattering,
/// rings close instead of opening — and nothing eases: marks cut in, hold, drop a frame now
/// and then, and are swallowed toward a point. Black, white, and one desaturated accent of
/// whoever is acting.
/// </summary>
public sealed class VoidStyle : VfxStyle
{
    public override string Id => "void";
    public override string Medium => "VOID (erasure)";
    public override float Fps => 15f;
    public override float Tempo => 0.95f;
    public override int MaxPieces => 8;
    protected override bool BoxedField => true;
    public override float ReleaseSeconds => 0f;
    public override float Ease(float t) => t > 0f ? 1f : 0f;             // a cut, not a curve

    private static readonly Color Black = Hex("050508");
    private static readonly Color White = Hex("f5f5f0");
    private const float Hair = 1.3f;

    /// <summary>Every sixth frame or so simply is not there.</summary>
    public override bool Skips(VfxMark m) => m.Step > 0 && !m.Looping && Hash(m.S.Seed + (uint)m.Step * 31u) % 6u == 0u;

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        string accent = element switch
        {
            VfxElement.Fire or VfxElement.Danger => "b09a90",
            VfxElement.Ice or VfxElement.Water => "9aa8b0",
            VfxElement.Leaf or VfxElement.Poison or VfxElement.Heal => "9aa898",
            VfxElement.Light => "d8d6c8",
            _ => "a8a4a8",
        };
        return new VfxPalette(Black, White, Hex(accent), White);
    }

    public override VfxPalette Look(VfxLook look, VfxCanvas canvas)
    {
        VfxPalette p = Element(VfxElement.Impact, canvas);
        if (look == null || !look.HasColours) return p;
        float grey = look.Main.Luminance;
        p.Accent = look.Main.Lerp(new Color(grey, grey, grey), 0.7f);     // the actor's colour, drained
        return p;
    }

    public override void Play(VfxArchetype archetype, ref VfxCtx x, VfxDirector d)
    {
        x.Flags |= VfxFlags.Converge;                                     // implosion: nothing here scatters
        base.Play(archetype, ref x, d);
    }

    /// <summary>How much of a mark is left: whole, then swallowed toward its centre.</summary>
    private static float Left(float t, float hold = 0.3f) => t < hold ? 1f : 1f - EaseIn((t - hold) / (1f - hold));

    protected override void DrawRing(VfxMark m)
    {
        // A ring closes on its centre.
        float rx = m.S.Extents.X > 0.5f ? m.S.Extents.X : m.S.Size, ry = m.S.Extents.Y > 0.5f ? m.S.Extents.Y : m.S.Size;
        float k = (m.S.Flags & VfxFlags.Reverse) != 0 ? Sat(m.T * 1.2f) : 1f - EaseIn(m.T);
        Hoop(m, Vector2.Zero, rx * k, ry * k, m.T);
    }

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float radius = r * 1.1f * Left(t, m.S.Kind == VfxMarkKind.Puff ? 0.15f : 0.3f);
        if (radius < 0.6f) return;
        Vector2 at = m.S.Kind == VfxMarkKind.Puff ? c * Left(t, 0.1f) : c;             // lobes are drawn in, too
        Disc(at, radius, p.Main, p.Main, 22);
        Band(at, radius, radius + Hair, p.Line, p.Line, 26);
        if (m.S.Kind == VfxMarkKind.Impact && t < 0.3f) Disc(at, radius * 0.18f, p.Accent, p.Accent, 8);
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float hw = width * 0.5f * Left(t, 0.5f);
        CrescentBand(u0, u1, hw, -1f, 1f, p.Main, p.Main, 16, 0.7f);
        CrescentEdge(u0, u1, hw, 0f, Hair, p.Line, 1f, 16, 0.7f);
        CrescentEdge(u0, u1, hw, 0f, Hair, p.Line, -1f, 16, 0.7f);
        CrescentEdge(u0, u1, hw * 0.4f, 0f, 1f, p.Accent, 1f, 16, 0.7f);
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        if (rx < 1f) return;
        ref VfxPalette p = ref m.S.P;
        float sy = ry / Mathf.Max(rx, 0.01f);
        Band(c, Mathf.Max(0f, rx - 3.5f), rx, p.Main, p.Main, 40, 0f, 0u, sy);
        Band(c, rx, rx + Hair, p.Line, p.Line, 40, 0f, 0u, sy);
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        // A shard: a black sliver in a white hairline.
        ref VfxPalette pal = ref m.S.P;
        float s = size * 1.3f;
        Vector2 a = p + Polar(rot, s), b = p + Polar(rot + 2.3f, s * 0.6f), c = p + Polar(rot - 2.5f, s * 0.5f);
        Triangle(a, b, c, pal.Main);
        Line(a, b, Hair, pal.Line);
        Line(b, c, Hair, pal.Line);
        Line(c, a, Hair, i % 3 == 0 ? pal.Accent : pal.Line);
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        Fill(A(p.Main, 0.35f + 0.3f * t), A(p.Main, 0.35f + 0.3f * t));
        int n = PathCount;
        for (int i = 0; i < n; i++)
        {
            // A broken hairline: the edge is there and not there.
            Vector2 a = PathPoint(i), b = PathPoint((i + 1) % n);
            int dashes = Mathf.Clamp((int)(a.DistanceTo(b) / 14f), 1, 40);
            for (int k = 0; k < dashes; k++)
                Line(a.Lerp(b, k / (float)dashes), a.Lerp(b, (k + 0.55f) / dashes), Hair, p.Line);
        }
    }

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            Id = "specks", Texture = null, Amount = 6, Lifetime = 6f, Direction = Vector2.Up, SpreadDeg = 40f,
            SpeedMin = 6f, SpeedMax = 18f, SizeMin = 2f, SizeMax = 3f, Fps = 15f,
            A = Hex("f5f5f0", 0.8f), B = Hex("a8a4a8", 0.8f), ALight = Hex("050508", 0.85f), BLight = Hex("050508", 0.6f),
            Ramp = Ramp((0f, 0f), (0.1f, 0f), (0.11f, 1f), (0.85f, 1f), (0.86f, 0f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

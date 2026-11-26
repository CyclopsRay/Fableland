namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// Cloud Court — cloisonné enamel, 景泰蓝 (RealmStyleBible §7C; R2 §4.5). Every shape is a
/// cell of one flat, saturated enamel inside a raised gold wire: there is no line that is
/// not gold and no colour that is not enclosed, and never a gradient across a cell. Marks
/// are symmetric and ceremonial — lotus rosettes, scale bands, beaded rings — and crisp:
/// they pop with a small overshoot, turn slowly, catch one glint instead of glowing, and end
/// by shrinking or by breaking into a few chips that fall.
/// </summary>
public sealed class EnamelStyle : VfxStyle
{
    public override string Id => "enamel";
    public override string Medium => "Cloisonné enamel (景泰蓝)";
    public override float Fps => 30f;
    public override float Tempo => 1.1f;
    public override int MaxPieces => 6;
    protected override bool BoxedField => true;
    public override float ReleaseSeconds => 0.12f;
    public override float Ease(float t) => Back(t);                      // a pop that settles

    private const float Wire = 2.2f;
    private static readonly Color Gold = Hex("e3b341");
    private static readonly Color GoldLit = Hex("ffe9a0");
    private static readonly Color Night = Hex("2a1b4a");

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        (string a, string b) = element switch
        {
            VfxElement.Dust => ("f4ead4", "e9a3b8"),
            VfxElement.Fire => ("c5382c", "edbe3c"),
            VfxElement.Ice => ("2cb5a8", "f4ead4"),
            VfxElement.Water => ("1e4fa3", "2cb5a8"),
            VfxElement.Leaf => ("74c095", "2cb5a8"),
            VfxElement.Poison => ("4b2a7a", "74c095"),
            VfxElement.Danger => ("c5382c", "4b2a7a"),
            VfxElement.Heal => ("74c095", "f4ead4"),
            VfxElement.Light => ("f4ead4", "edbe3c"),
            VfxElement.Smoke => ("4b2a7a", "1e4fa3"),
            _ => ("1e4fa3", "2cb5a8"),
        };
        // Gold on a pale canvas is thin: the wire casts the night under itself there.
        Color under = canvas == VfxCanvas.Light ? Night : default;
        return new VfxPalette(Hex(a), Hex(b), GoldLit, Gold, under);
    }

    private static void WireLine(ref VfxPalette p, Vector2 a, Vector2 b, float alpha, float width = Wire)
    {
        if (p.Ground.A > 0f) Line(a + new Vector2(1f, 1.2f), b + new Vector2(1f, 1.2f), width + 1.2f, A(p.Ground, alpha));
        Line(a, b, width, A(p.Line, alpha));
    }

    /// <summary>One cell: a flat enamel kite closed by gold wire, lit on one flank.</summary>
    private static void Cell(ref VfxPalette p, Vector2 c, float angle, float length, float width, Color enamel, float alpha)
    {
        Vector2 tip = c + Polar(angle, length), mid = c + Polar(angle, length * 0.55f);
        Vector2 side = Polar(angle + Mathf.Pi * 0.5f, width), root = c + Polar(angle, length * 0.14f);
        Quad(root, mid + side, tip, mid - side, A(enamel, alpha));
        WireLine(ref p, root, mid + side, alpha);
        WireLine(ref p, mid + side, tip, alpha);
        WireLine(ref p, tip, mid - side, alpha);
        WireLine(ref p, mid - side, root, alpha);
        Line(mid - side, tip, Wire * 0.45f, A(p.Accent, alpha));         // the lit flank of the wire
    }

    private static float Leave(float t, float from = 0.62f) => 1f - Smooth((t - from) / (1f - from));

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float pop = Back(t / 0.26f) * (1f - 0.55f * Smooth((t - 0.62f) / 0.38f)), a = Leave(t, 0.75f);
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            // A ruyi cloud curl: a flat ivory cell in a gold ring, rising.
            Vector2 at = c + new Vector2(0f, -r * 0.5f * t);
            float rr = r * 0.62f * pop;
            Ngon(at, rr, 14, 0f, A(p.Main, a));
            Band(at, rr - Wire * 0.5f, rr + Wire * 0.5f, A(p.Line, a), A(p.Line, a), 16);
            Band(at, rr * 0.45f - 1f, rr * 0.45f + 1f, A(p.Line, a), A(p.Line, a), 12, 0f, 0u, 1f, 0.6f, 4.6f);
            return;
        }
        // A lotus rosette: petals of two enamels turn about a gold boss.
        int petals = (m.S.Flags & VfxFlags.Big) != 0 ? 12 : 8;
        float turn = R(m.S.Seed, 1) * Tau + t * 0.6f, length = r * 1.5f * pop;
        for (int i = 0; i < petals; i++)
            Cell(ref p, c, turn + i * Tau / petals, length, r * 0.30f * pop, (i & 1) == 0 ? p.Main : p.Alt, a);
        Ngon(c, r * 0.30f * pop, 12, 0f, A(p.Line, a));
        Ngon(c, r * 0.15f * pop, 8, 0f, A(p.Accent, a));
        // One glint sweeps across — enamel does not glow.
        float sweep = Sat((t - 0.1f) / 0.5f);
        if (sweep > 0f && sweep < 1f)
        {
            Vector2 g = c + new Vector2(Mathf.Lerp(-length, length, sweep), Mathf.Lerp(length * 0.4f, -length * 0.4f, sweep)) * 0.7f;
            Rect(g, r * 0.30f, r * 0.06f, -0.6f, A(Colors.White, 0.9f));
            Rect(g, r * 0.06f, r * 0.30f, -0.6f, A(Colors.White, 0.9f));
        }
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        if (u1 - u0 < 0.01f) return;
        ref VfxPalette p = ref m.S.P;
        float a = Leave(t), hw = width * 0.5f;
        // A band of scale cells: alternate enamels, a gold wire across between each pair.
        const int cells = 7;
        for (int i = 0; i < cells; i++)
        {
            float lo = Mathf.Max(u0, i / (float)cells), hi = Mathf.Min(u1, (i + 1) / (float)cells);
            if (hi - lo < 0.004f) continue;
            Color enamel = A((i & 1) == 0 ? p.Main : p.Alt, a);
            CrescentBand(lo, hi, hw, -1f, 1f, enamel, enamel, 4, 0.75f);
            PathAt(lo, out Vector2 at, out Vector2 n);
            float w = CrescentWidth(lo, hw, 0.75f);
            if (i > 0 && lo > u0 + 0.001f) WireLine(ref p, at - n * w, at + n * w, a, Wire * 0.8f);
        }
        if (p.Ground.A > 0f) CrescentEdge(u0, u1, hw + 1.4f, 0f, Wire + 1.2f, A(p.Ground, a), 1f, 16, 0.75f);
        CrescentEdge(u0, u1, hw, 0f, Wire, A(p.Line, a), 1f, 16, 0.75f);
        CrescentEdge(u0, u1, hw, 0f, Wire, A(p.Line, a), -1f, 16, 0.75f);
        CrescentEdge(u0, u1, hw - 1f, 0f, Wire * 0.45f, A(p.Accent, a), 1f, 16, 0.75f);
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float sy = ry / Mathf.Max(rx, 0.01f), a = Leave(t, 0.55f);
        if (p.Ground.A > 0f) Band(c + new Vector2(1f, 1.2f), rx - Wire, rx + Wire, A(p.Ground, a), A(p.Ground, a), 40, 0f, 0u, sy);
        Band(c, rx - Wire * 0.5f, rx + Wire * 0.5f, A(p.Line, a), A(p.Line, a), 40, 0f, 0u, sy);
        if (rx > 14f) Band(c, rx - 5.5f, rx - 4.5f, A(p.Line, a), A(p.Line, a), 40, 0f, 0u, sy);
        // Beads of enamel set on the ring.
        int beads = rx > 40f ? 12 : 8;
        for (int i = 0; i < beads; i++)
        {
            float angle = i * Tau / beads + t * 0.8f;
            Vector2 at = c + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
            Ngon(at, 3.6f, 8, 0f, A(p.Line, a));
            Ngon(at, 2.2f, 8, 0f, A((i & 1) == 0 ? p.Main : p.Alt, a));
        }
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        float a = Leave(t, 0.7f), s = size * 1.1f;
        Vector2 x = Polar(rot, s), y = Polar(rot + Mathf.Pi * 0.5f, s * 0.62f);
        Quad(p - x, p + y, p + x, p - y, A((i & 1) == 0 ? pal.Main : pal.Alt, a));
        WireLine(ref pal, p - x, p + y, a, Wire * 0.8f);
        WireLine(ref pal, p + y, p + x, a, Wire * 0.8f);
        WireLine(ref pal, p + x, p - y, a, Wire * 0.8f);
        WireLine(ref pal, p - y, p - x, a, Wire * 0.8f);
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float fill = 0.20f + 0.22f * t;
        Fill(A(p.Main, fill), A(p.Main, fill));                           // one cell, one enamel, no gradient
        int n = PathCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 corner = PathPoint(i), next = PathPoint((i + 1) % n);
            WireLine(ref p, corner, next, 1f);
            // A step of the running meander 回纹 at each corner.
            Vector2 along = (next - corner).Normalized(), inward = new Vector2(-along.Y, along.X);
            float arm = Mathf.Min(9f, corner.DistanceTo(next) * 0.2f);
            WireLine(ref p, corner + along * arm + inward * arm, corner + along * arm * 2f + inward * arm, 1f, Wire * 0.7f);
        }
    }

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            Id = "gold_dust", Texture = "enamel/dust", Amount = 10, Lifetime = 7f, Direction = new Vector2(0.2f, 1f), SpreadDeg = 30f,
            SpeedMin = 8f, SpeedMax = 22f, SizeMin = 3f, SizeMax = 6f, SpinDeg = 120f,
            A = Hex("e3b341", 0.9f), B = Hex("ffe9a0", 0.9f), ALight = Hex("9c7420", 0.9f), BLight = Hex("e3b341", 0.9f),
            Ramp = Ramp((0f, 0f), (0.1f, 1f), (0.45f, 1f), (0.5f, 0.35f), (0.55f, 1f), (0.9f, 1f), (1f, 0f)),
        },
        new()
        {
            Id = "petals", Texture = "watercolour/petal", Amount = 2, Lifetime = 9f, Direction = new Vector2(-0.4f, 1f), SpreadDeg = 20f,
            SpeedMin = 12f, SpeedMax = 26f, SizeMin = 7f, SizeMax = 10f, SpinDeg = 60f,
            A = Hex("e9a3b8", 0.85f), B = Hex("f4ead4", 0.85f), ALight = Hex("c5382c", 0.8f), BLight = Hex("e9a3b8", 0.85f),
            Ramp = Ramp((0f, 0f), (0.1f, 1f), (0.9f, 1f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

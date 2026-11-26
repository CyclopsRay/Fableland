namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// TD Ghost Town — linocut / woodblock print (RealmStyleBible §6; R2 §4.5). A mark is
/// stamped: a black key block cut in jagged wedges, over ONE flat spot ink printed a pixel
/// or two out of register. Tone is hatching, light is bare paper, and there is no gradient
/// and no fade — a mark pops in hard, wobbles a pixel each frame like a hand-pulled print
/// (10 frames a second), and ends by breaking up: wedges and segments drop out. Off its
/// cream paper it prints on a scrap of it.
/// </summary>
public sealed class LinocutStyle : VfxStyle
{
    public override string Id => "linocut";
    public override string Medium => "Linocut / woodblock print";
    public override float Fps => 10f;
    public override bool Boils => true;
    public override float Tempo => 1.15f;
    public override int MaxPieces => 8;
    protected override bool BoxedField => true;
    public override float ReleaseSeconds => 0f;

    private static readonly Color Key = Hex("1f1a17");
    private static readonly Color Bone = Hex("eadfc8");
    private static readonly Color PaperTone = Hex("f1e3c4");
    private static readonly Vector2 Register = new(2.2f, 1.2f);          // how far the spot plate slipped

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        string spot = element switch
        {
            VfxElement.Dust or VfxElement.Light => "d9a13b",              // ochre
            VfxElement.Ice or VfxElement.Water or VfxElement.Heal => "5e8c87",   // faded teal
            VfxElement.Leaf or VfxElement.Poison => "7c8f58",             // sage
            VfxElement.Smoke => "1f1a17",
            _ => "b5482e",                                                // rust
        };
        Color ground = canvas == VfxCanvas.Dark ? PaperTone : default;
        return new VfxPalette(Hex(spot), Bone, Bone, Key, ground);
    }

    public override VfxPalette Look(VfxLook look, VfxCanvas canvas)
    {
        VfxPalette p = Element(VfxElement.Impact, canvas);
        if (look != null && look.HasColours) p.Main = look.Main;          // one spot ink: the owner's
        return p;
    }

    /// <summary>The hand's wobble for this frame: a pixel either way.</summary>
    private static Vector2 Wob(VfxMark m, int i) => new(Rs(m.Boil, i * 2 + 1) * 1.1f, Rs(m.Boil, i * 2 + 2) * 1.1f);

    /// <summary>A carved starburst. Wedges whose number comes up under
    /// <paramref name="broken"/> have dropped out — how a print ends.</summary>
    private static void Carve(Vector2 c, float r, int spikes, uint seed, Color colour, float broken)
    {
        float step = Tau / spikes;
        for (int i = 0; i < spikes; i++)
        {
            if (R(seed, i + 500) < broken) continue;
            float a = i * step + Rs(seed, i) * step * 0.22f;
            float tip = r * (0.72f + 0.42f * R(seed, i + 60));
            Triangle(c, c + Polar(a - step * 0.5f, r * 0.46f), c + Polar(a, tip), colour);
            Triangle(c, c + Polar(a, tip), c + Polar(a + step * 0.5f, r * 0.46f), colour);
        }
    }

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        uint seed = m.S.Seed + (uint)(int)(c.X * 3f + c.Y * 7f);
        float broken = Sat((t - 0.5f) / 0.5f) * 0.95f;
        c += Wob(m, 0);
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            // Dust is hatched: a flat spot of ink, and short cuts of the gouge across it.
            float w = r * (0.8f + 0.5f * t);
            if (p.Ground.A > 0f) Ngon(c, w * 1.25f, 7, 0.4f, p.Ground);
            if (broken < 0.6f) Ngon(c + Register, w * 0.85f, 7, 0.4f, p.Main);
            for (int i = 0; i < 5; i++)
            {
                if (R(seed, i + 500) < broken) continue;
                float y = Mathf.Lerp(-w * 0.6f, w * 0.6f, i / 4f), half = w * Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow(y / w, 2f)));
                Line(c + new Vector2(-half, y + half * 0.35f), c + new Vector2(half, y - half * 0.35f), 2f, p.Line);
            }
            return;
        }
        int spikes = (m.S.Flags & VfxFlags.Big) != 0 ? 14 : 10;
        float radius = r * 1.35f * (t < 0.11f ? 1.14f : 1f);             // stamped over-size, then true
        if (p.Ground.A > 0f) Carve(c, radius * 1.22f, spikes, seed, p.Ground, 0f);
        Carve(c, radius, spikes, seed, p.Line, broken);                  // the key block
        Carve(c, radius * 0.66f, spikes, seed, p.Accent, broken);        // cut back to paper
        Carve(c + Register, radius * 0.58f, spikes, seed, p.Main, broken);   // the spot ink, out of register
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        if (u1 - u0 < 0.01f) return;
        ref VfxPalette p = ref m.S.P;
        float hw = width * 0.5f, slip = Rs(m.Boil, 3) * 1.1f;
        if (p.Ground.A > 0f) CrescentBand(u0, u1, hw * 1.3f + 3f, -1f, 1f, p.Ground, p.Ground, 12, 0.6f);
        CrescentBand(u0, u1, hw, -1f, 1f, p.Line, p.Line, 12, 0.6f);     // a carved wedge of key black
        Crescent(u0, u1, hw * 0.50f, -hw * 0.12f, p.Accent, p.Accent, 12, 0.6f);          // paper cut out of it
        Crescent(u0, u1, hw * 0.44f, -hw * 0.12f + Register.X + slip, p.Main, p.Main, 12, 0.6f);
        // Gouge marks: short cuts across the wedge.
        for (int i = 0; i < 5; i++)
        {
            float u = 0.18f + i * 0.16f;
            if (u < u0 || u > u1) continue;
            PathAt(u, out Vector2 at, out Vector2 n);
            float w = CrescentWidth(u, hw, 0.6f);
            Line(at + n * (w * 0.15f), at + n * (w * 0.85f), 1.6f, p.Accent);
        }
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        // A broken ring: arcs of unequal length, the spot plate slipped under the key.
        ref VfxPalette p = ref m.S.P;
        float sy = ry / Mathf.Max(rx, 0.01f), broken = Sat((t - 0.45f) / 0.55f);
        c += Wob(m, 1);
        const int arcs = 7;
        float a = R(m.S.Seed, 1) * Tau;
        for (int i = 0; i < arcs; i++)
        {
            float span = Tau / arcs * (0.5f + 0.35f * R(m.S.Seed, i + 10));
            if (R(m.S.Seed, i + 500) >= broken)
            {
                Band(c + Register, rx - 2.2f, rx + 2.2f, p.Main, p.Main, 6, 0f, 0u, sy, a, a + span);
                Band(c, rx - 2.6f, rx + 2.6f, p.Line, p.Line, 6, 0f, 0u, sy, a, a + span);
            }
            a += Tau / arcs;
        }
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        if (R(m.S.Seed, i + 500) < (t - 0.6f) / 0.4f) return;            // drops out; never fades
        float turn = Mathf.Round(rot / (Mathf.Pi * 0.25f)) * Mathf.Pi * 0.25f;     // 45° at a time
        Vector2 tip = Polar(turn, size * 1.25f), side = Polar(turn + Mathf.Pi * 0.5f, size * 0.55f);
        p += Wob(m, i + 4);
        Triangle(p + Register - tip * 0.5f + side, p + Register + tip, p + Register - tip * 0.5f - side, pal.Main);
        Triangle(p - tip * 0.5f + side * 0.7f, p + tip * 0.8f, p - tip * 0.5f - side * 0.7f, pal.Line);
    }

    protected override void Tone(VfxMark m, float t)
    {
        // Tone is hatching: the lines crowd closer as the moment nears.
        ref VfxPalette p = ref m.S.P;
        Rect2 box = PathBounds();
        float gap = t < 0.34f ? 14f : t < 0.67f ? 10f : 7f;
        if (p.Ground.A > 0f) Fill(A(p.Ground, 0.85f), A(p.Ground, 0.85f));
        int n = PathCount;
        for (int i = 0; i < n; i++) Line(PathPoint(i) + Register, PathPoint((i + 1) % n) + Register, 3f, p.Main);
        for (int i = 0; i < n; i++) Line(PathPoint(i), PathPoint((i + 1) % n), 2.4f, p.Line);
        // Diagonals across the box, kept to the part of each that lies inside the shape.
        float span = box.Size.X + box.Size.Y;
        int drawn = 0;
        for (float d = gap * 0.5f; d < span && drawn < 90; d += gap)
        {
            Vector2 a = box.Position + new Vector2(Mathf.Min(d, box.Size.X), Mathf.Max(0f, d - box.Size.X));
            Vector2 b = box.Position + new Vector2(Mathf.Max(0f, d - box.Size.Y), Mathf.Min(d, box.Size.Y));
            Vector2 lo = a.Lerp(b, 0.06f), hi = a.Lerp(b, 0.94f);
            if (!PathContains(lo.Lerp(hi, 0.5f))) continue;
            if (!PathContains(lo)) lo = lo.Lerp(hi, 0.3f);
            if (!PathContains(hi)) hi = hi.Lerp(lo, 0.3f);
            Line(lo, hi, 1.5f, p.Line);
            drawn++;
        }
    }

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            Id = "print_specks", Texture = "linocut/speck", Amount = 10, Lifetime = 6f, Direction = new Vector2(1f, 0.25f), SpreadDeg = 25f,
            SpeedMin = 16f, SpeedMax = 44f, SizeMin = 4f, SizeMax = 8f, Fps = 10f, SpinDeg = 30f,
            A = Hex("eadfc8", 0.85f), B = Hex("d9a13b", 0.85f), ALight = Hex("1f1a17", 0.8f), BLight = Hex("b5482e", 0.8f),
            Ramp = Ramp((0f, 0f), (0.05f, 1f), (0.9f, 1f), (0.91f, 0f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

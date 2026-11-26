namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// PL Loong's Nest — gongbi with heavy colour, 工笔重彩 (RealmStyleBible §7; R2 §4.5). The
/// opposite hand to ink: every form is closed, symmetric and finished — an even "iron-wire"
/// line of constant width round a fill graded smoothly from dark to light. Rings, petals,
/// pearls and outlined bubbles; purple, cyan, blue and the line's near-black, no warm hue.
/// Everything moves as if through water: slow, eased in and out, and a piece that is thrown
/// decelerates and then rises. A ring ends by thinning away at constant width; a bubble
/// drifts up and is gone.
/// </summary>
public sealed class GongbiStyle : VfxStyle
{
    public override string Id => "gongbi";
    public override string Medium => "Gongbi, heavy colour (工笔重彩)";
    public override float Fps => 30f;
    public override float Tempo => 1.55f;
    public override float ReleaseSeconds => 0.25f;
    public override float Ease(float t) => Smooth(t);                    // viscous: in and out

    private const float Wire = 1.5f;                                     // the line never swells
    private static readonly Color Pearl = Hex("eafbff");

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        // Dark end of the grade, light end, the pearl. The line is the realm's near-black;
        // on a dark canvas a second, pale line rides inside it so the form still closes.
        (string dark, string light, string pearl) = element switch
        {
            VfxElement.Dust => ("5b3fa8", "a48be6", "eafbff"),
            VfxElement.Fire => ("5b3fa8", "a48be6", "eafbff"),            // a cold flame: there is no warm hue here
            VfxElement.Ice => ("33c7d6", "b6f3f4", "eafbff"),
            VfxElement.Water => ("127a8c", "33c7d6", "b6f3f4"),
            VfxElement.Leaf => ("127a8c", "33c7d6", "b6f3f4"),
            VfxElement.Poison => ("2c1f5c", "5b3fa8", "a48be6"),
            VfxElement.Danger => ("2c1f5c", "5b3fa8", "eafbff"),
            VfxElement.Heal => ("33c7d6", "b6f3f4", "eafbff"),
            VfxElement.Light => ("7fb0f5", "eafbff", "eafbff"),
            VfxElement.Smoke => ("161d4a", "15307a", "7fb0f5"),
            _ => ("2c63cc", "7fb0f5", "eafbff"),
        };
        Color inner = canvas == VfxCanvas.Dark ? A(Pearl, 0.85f) : default;
        return new VfxPalette(Hex(dark), Hex(light), Hex(pearl), Hex("05060f"), inner);
    }

    private static float Thin(float t, float from = 0.5f) => 1f - Smooth((t - from) / (1f - from));

    /// <summary>The iron-wire line between two points — and its pale twin on a dark canvas.</summary>
    private static void WireLine(ref VfxPalette p, Vector2 a, Vector2 b, float alpha)
    {
        Line(a, b, Wire, A(p.Line, alpha));
        if (p.Ground.A <= 0f) return;
        Vector2 n = (b - a).Normalized();
        n = new Vector2(-n.Y, n.X) * 1.3f;
        Line(a + n, b + n, 1f, A(p.Ground, alpha));
    }

    /// <summary>One petal: graded from its base to its tip, closed by the wire.</summary>
    private static void Petal(ref VfxPalette p, Vector2 c, float angle, float length, float width, float alpha)
    {
        Vector2 tip = c + Polar(angle, length), mid = c + Polar(angle, length * 0.52f);
        Vector2 side = Polar(angle + Mathf.Pi * 0.5f, width);
        Vector2 basePoint = c + Polar(angle, length * 0.10f);
        Color dark = A(p.Main, 0.92f * alpha), light = A(p.Alt, 0.92f * alpha), pearl = A(p.Accent, 0.95f * alpha);
        Quad(basePoint, mid + side, tip, mid - side, dark, light, pearl, light);
        WireLine(ref p, basePoint, mid + side, alpha);
        WireLine(ref p, mid + side, tip, alpha);
        WireLine(ref p, tip, mid - side, alpha);
        WireLine(ref p, mid - side, basePoint, alpha);
    }

    /// <summary>An outlined pearl or bubble: graded fill, even ring, one highlight.</summary>
    private static void Bubble(ref VfxPalette p, Vector2 c, float r, float alpha, bool solid)
    {
        Disc(c + new Vector2(-r * 0.15f, -r * 0.15f), r, A(p.Accent, (solid ? 0.95f : 0.55f) * alpha), A(solid ? p.Alt : p.Main, (solid ? 0.95f : 0.35f) * alpha), 16);
        Band(c, r - Wire * 0.5f, r + Wire * 0.5f, A(p.Line, alpha), A(p.Line, alpha), 18);
        if (p.Ground.A > 0f) Band(c, r - Wire * 0.5f - 1.3f, r - Wire * 0.5f - 0.3f, A(p.Ground, alpha), A(p.Ground, alpha), 18);
        Disc(c + new Vector2(-r * 0.35f, -r * 0.38f), r * 0.20f, A(p.Accent, alpha), A(p.Accent, alpha), 8);
    }

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = Thin(t);
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            Bubble(ref p, c + new Vector2(0f, -r * 0.5f * Smooth(t)), r * (0.55f + 0.25f * Smooth(t)), a, false);    // silt lifts as outlined scrolls
            return;
        }
        // A six-petal pearl flash: it opens slowly, turns a little, and closes.
        int petals = (m.S.Flags & VfxFlags.Big) != 0 ? 8 : 6;
        float open = Smooth(t / 0.35f), turn = R(m.S.Seed, 1) * Tau + t * 0.5f;
        float length = r * 1.5f * (0.35f + 0.65f * open) * (1f - 0.25f * Smooth((t - 0.6f) / 0.4f));
        for (int i = 0; i < petals; i++) Petal(ref p, c, turn + i * Tau / petals, length, r * 0.30f * open, a);
        Bubble(ref p, c, r * 0.30f, a, true);
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = Thin(t), hw = width * 0.5f;
        // A ribbon: graded across from the dark edge to the light, closed by two even lines.
        CrescentBand(u0, u1, hw, -1f, 0.2f, A(p.Main, 0.90f * a), A(p.Alt, 0.90f * a), 18);
        CrescentBand(u0, u1, hw, 0.2f, 1f, A(p.Alt, 0.90f * a), A(p.Accent, 0.92f * a), 18);
        CrescentEdge(u0, u1, hw, 0f, Wire * 1.1f, A(p.Line, a), 1f, 18);
        CrescentEdge(u0, u1, hw, 0f, Wire * 1.1f, A(p.Line, a), -1f, 18);
        if (p.Ground.A > 0f) CrescentEdge(u0, u1, hw * 0.86f, 0f, 1f, A(p.Ground, a), 1f, 18);
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        // Two thin ripples, even all the way round. They do not swell: they thin away.
        ref VfxPalette p = ref m.S.P;
        float sy = ry / Mathf.Max(rx, 0.01f), a = Thin(t, 0.45f);
        for (int k = 0; k < 2; k++)
        {
            float r = rx * (1f - 0.26f * k), alpha = a * (1f - 0.4f * k);
            if (r < 2f) continue;
            Band(c, r - Wire * 0.5f, r + Wire * 0.5f, A(p.Line, alpha), A(p.Line, alpha), 44, 0f, 0u, sy);
            Band(c, r - Wire * 0.5f - 1.6f, r - Wire * 0.5f, A(p.Alt, 0.9f * alpha), A(p.Accent, 0.95f * alpha), 44, 0f, 0u, sy);
        }
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        // A cold flame is a petal standing on its base; anything else is a pearl.
        bool flame = m.S.Kind is VfxMarkKind.Aura or VfxMarkKind.Field && m.S.Variant < 2;
        if (flame) Petal(ref m.S.P, p + new Vector2(0f, size * 0.5f), -Mathf.Pi * 0.5f + Mathf.Sin(m.Time * 5f + i) * 0.18f, size * 1.7f, size * 0.42f, Thin(t, 0.55f));
        else Bubble(ref m.S.P, p, size * 0.6f, Thin(t, 0.6f), i % 3 == 0);
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        Fill(A(p.Alt, 0.10f + 0.12f * t), A(p.Main, 0.26f + 0.26f * t));         // graded toward its edge
        int n = PathCount;
        for (int i = 0; i < n; i++) WireLine(ref p, PathPoint(i), PathPoint((i + 1) % n), 1f);
    }

    // ── particles: outlined bubbles, which slow and then rise ────────────────────
    private static readonly Gradient Pop = Ramp((0f, 0f), (0.08f, 1f), (0.88f, 1f), (0.9f, 0f), (1f, 0f));
    private static readonly VfxBurstRecipe Thrown = new()
    {
        Texture = "gongbi/bubble", TwoTone = false, Colour = -1, Lifetime = 1.3f, Gravity = new Vector2(0f, -70f), Damping = 420f,
        SpeedMin = 0.3f, SpeedMax = 0.7f, SizeMin = 0.9f, SizeMax = 1.8f, Ramp = Pop,
    };
    private static readonly VfxBurstRecipe Sinking = new()
    {
        Texture = "gongbi/bubble", TwoTone = false, Colour = -1, Lifetime = 1.5f, Gravity = new Vector2(0f, 50f), Damping = 300f,
        SizeMin = 0.9f, SizeMax = 1.8f, Ramp = Pop,
    };
    private static readonly VfxBurstRecipe Rising = new()
    {
        Texture = "gongbi/bubble", TwoTone = false, Colour = -1, Lifetime = 1.3f, Gravity = new Vector2(0f, -60f), Damping = 160f,
        SpeedMin = 0.3f, SpeedMax = 0.7f, SizeMin = 0.8f, SizeMax = 1.6f, EmitRadius = 8f, Ramp = Pop,
    };

    public override VfxBurstRecipe Particles(VfxBits kind) => kind switch
    {
        VfxBits.Spray => Thrown,
        VfxBits.Fall => Sinking,
        _ => Rising,
    };

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            Id = "bubble_streams", Texture = "gongbi/bubble", Amount = 12, Lifetime = 7f, Direction = Vector2.Up, SpreadDeg = 10f,
            SpeedMin = 16f, SpeedMax = 40f, SizeMin = 5f, SizeMax = 11f,
            A = Hex("ffffff", 0.75f), B = Hex("ffffff", 0.55f), ALight = Hex("ffffff", 0.9f), BLight = Hex("ffffff", 0.8f),
            Ramp = Ramp((0f, 0f), (0.1f, 1f), (0.92f, 1f), (0.94f, 0f), (1f, 0f)),
        },
        new()
        {
            Id = "pearl_motes", Texture = "soft_dot", Amount = 4, Lifetime = 8f, Direction = new Vector2(0.4f, -1f), SpreadDeg = 60f,
            SpeedMin = 3f, SpeedMax = 9f, SizeMin = 3f, SizeMax = 5f,
            A = Hex("eafbff", 0.55f), B = Hex("b6f3f4", 0.5f), ALight = Hex("2c63cc", 0.5f), BLight = Hex("5b3fa8", 0.5f),
            Ramp = Ramp((0f, 0f), (0.3f, 1f), (0.7f, 1f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

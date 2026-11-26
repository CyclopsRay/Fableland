namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// VK Beach — storybook watercolour and ink (RealmStyleBible; R2 §4.5). A mark is a wash:
/// a translucent bloom that is palest at its heart and wettest at its rim, with a cream
/// light in it; dark-brown ink is kept for the key shape only. It blooms fast (a fifth of
/// its life), then bleeds — spreading a tenth while it dries away. Never pure white or
/// black; no additive light. On a dark canvas a wash lays its own cream paper first.
/// </summary>
public sealed class WatercolourStyle : VfxStyle
{
    public override string Id => "watercolour";
    public override string Medium => "Storybook watercolour & ink";
    public override float Fps => 30f;
    public override float Tempo => 1.15f;

    private static readonly Color Ink = Hex("4a2e22");
    private static readonly Color Cream = Hex("fff4dc");

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        (string main, string alt, string accent) = element switch
        {
            VfxElement.Dust => ("e6c88f", "b98f5e", "fff4d8"),
            VfxElement.Fire => ("f59a32", "d8432e", "ffe07a"),
            VfxElement.Ice => ("a9dceb", "5fa9c9", "f2fbfd"),
            VfxElement.Water => ("8fd3e8", "3f97bf", "f0fafc"),
            VfxElement.Leaf => ("8fbf6f", "4f7f44", "eef6cf"),
            VfxElement.Poison => ("efe5c4", "a99b6e", "fffbea"),
            VfxElement.Danger => ("e5553f", "9b2a22", "ffe1c4"),
            VfxElement.Heal => ("93d998", "4e9e62", "f4ffe6"),
            VfxElement.Light => ("ffe08a", "e8a93a", "fff8dc"),
            VfxElement.Smoke => ("a99f95", "6f645d", "e6dfd6"),
            _ => ("de6a72", "9c2f4a", "fff1d6"),
        };
        Color ground = canvas == VfxCanvas.Dark ? A(Cream, 0.26f) : default;
        return new VfxPalette(Hex(main), Hex(alt), Hex(accent), Ink, ground);
    }

    private static float Dry(float t, float from = 0.40f) => t < from ? 1f : 1f - Smooth((t - from) / (1f - from));

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        uint seed = m.S.Seed + (uint)(int)(c.X * 7f + c.Y * 13f);       // each lobe of a puff has its own outline
        bool dust = m.S.Kind == VfxMarkKind.Puff;
        float radius = r * (0.42f + 0.58f * EaseOut(t / 0.22f)) * (1f + 0.10f * t);
        float a = Dry(t);
        if (p.Ground.A > 0f) Disc(c, radius * 1.14f, A(p.Ground, a), A(p.Ground, 0f), 18, 0.16f, seed);
        // A wash is flat: palest where the water pooled, and a thin wet line where it dried.
        Bloom(c, radius, seed, 0.20f, A(p.Main, (dust ? 0.36f : 0.44f) * a), A(p.Alt, (dust ? 0.55f : 0.88f) * a), 0.86f, 0.05f, 26);
        if (!dust) Disc(c + new Vector2(-radius * 0.18f, -radius * 0.16f), radius * 0.62f, A(p.Accent, 0.34f * a), A(p.Accent, 0f), 14, 0.2f, seed + 3u);
        if (m.S.Kind != VfxMarkKind.Impact) return;
        // The splat throws two small blooms of its own.
        for (int i = 0; i < 2; i++)
        {
            Vector2 at = c + Polar(m.S.Rot + Rs(seed, i + 70) * 1.1f, radius * (1.25f + 0.35f * R(seed, i + 80)));
            Bloom(at, radius * (0.20f + 0.12f * R(seed, i + 90)), seed + (uint)i + 11u, 0.2f, A(p.Main, 0.55f * a), A(p.Alt, 0.9f * a), 0.8f, 0.05f, 12);
        }
        if (t >= 0.36f) return;
        // Ink on the key shape only: a few ticks flung from the splat, for its first frames.
        Rays(c, radius * 1.16f, radius * 1.66f, (m.S.Flags & VfxFlags.Big) != 0 ? 9 : 6, seed, 1.9f, A(p.Line, 0.9f));
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        if (u1 - u0 < 0.01f) return;
        ref VfxPalette p = ref m.S.P;
        float a = Dry(t, 0.45f);
        float hw = width * 0.5f * (1f + 0.08f * t);
        if (m.S.Kind == VfxMarkKind.Glint)
        {
            // A sparkle is light, not a drawn thing: a pale wash with a wet edge, no ink.
            Crescent(u0, u1, hw * 1.5f, 0f, A(p.Main, 0.55f * a), A(p.Main, 0.55f * a), 10, 1.6f);
            Crescent(u0, u1, hw * 0.7f, 0f, A(p.Accent, 0.95f * a), A(p.Accent, 0.95f * a), 10, 1.6f);
            return;
        }
        if (p.Ground.A > 0f) Crescent(u0, u1, hw * 1.2f, 0f, A(p.Ground, a), A(p.Ground, a));
        // The wash: pale on the inside, wet and deep toward the leading edge.
        Crescent(u0, u1, hw, 0f, A(p.Main, 0.22f * a), A(p.Alt, 0.70f * a));
        Crescent(u0, u1, hw * 0.34f, -hw * 0.30f, A(p.Accent, 0f), A(p.Accent, 0.60f * a));
        // Dark-brown ink on the leading edge only — the one drawn line of the stroke.
        CrescentEdge(u0, u1, hw, 0f, 2.2f, A(p.Line, 0.88f * Dry(t, 0.30f)));
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = Dry(t, 0.35f), sy = ry / Mathf.Max(rx, 0.01f);
        float w = Mathf.Clamp(rx * 0.20f, 3f, 11f) * (1f - 0.35f * t);
        uint seed = m.S.Seed;
        if (p.Ground.A > 0f) Band(c, rx - w * 1.5f, rx + w * 0.4f, A(p.Ground, 0f), A(p.Ground, a), 28, 0.05f, seed, sy);
        Band(c, rx - w, rx - w * 0.25f, A(p.Main, 0f), A(p.Main, 0.50f * a), 28, 0.05f, seed, sy);
        Band(c, rx - w * 0.25f, rx, A(p.Main, 0.50f * a), A(p.Alt, 0.85f * a), 28, 0.05f, seed, sy);
        Band(c, rx, rx + 1.5f, A(p.Alt, 0.85f * a), A(p.Alt, 0f), 28, 0.05f, seed, sy);
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        float a = Dry(t, 0.5f);
        Vector2 dir = Polar(rot, 1f);
        Color body = A(Mix(pal.Main, pal.Alt, R(m.S.Seed, i + 7) * 0.45f), 0.72f * a);
        if (pal.Ground.A > 0f) Disc(p, size * 0.75f, A(pal.Ground, a), A(pal.Ground, 0f), 8);
        Tear(p, dir, size * 1.5f, size, body, A(pal.Alt, 0.20f * a));
        Band(p, size * 0.34f, size * 0.52f, A(body, 0f), A(pal.Alt, 0.5f * a), 10);
        Disc(p + new Vector2(-size * 0.14f, -size * 0.14f), size * 0.20f, A(pal.Accent, 0.75f * a), A(pal.Accent, 0f), 8);
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        if (p.Ground.A > 0f) Fill(A(p.Ground, 0.30f), A(p.Ground, 0.12f));
        Fill(A(p.Main, 0.08f + 0.12f * t), A(p.Main, 0.22f + 0.20f * t));
        int n = PathCount;
        for (int i = 0; i < n; i++)                                     // the wet edge of the wash
            Line(PathPoint(i), PathPoint((i + 1) % n), 2.6f, A(p.Alt, 0.42f + 0.35f * t));
    }

    // ── particles: droplets fall, sand lobes hang ────────────────────────────────
    private static readonly Gradient Fade = Ramp((0f, 0.95f), (0.55f, 0.9f), (1f, 0f));
    private static readonly VfxBurstRecipe Droplets = new()
    {
        Texture = "watercolour/drop", AlignToVelocity = true, Lifetime = 1.05f, Gravity = new Vector2(0f, 980f),
        Damping = 40f, SizeMin = 0.7f, SizeMax = 1.3f, Ramp = Fade,
    };
    private static readonly VfxBurstRecipe Debris = new()
    {
        Texture = "watercolour/drop", AlignToVelocity = true, Lifetime = 1.0f, Gravity = new Vector2(0f, 760f),
        Damping = 60f, SizeMin = 0.7f, SizeMax = 1.4f, Ramp = Fade,
    };
    private static readonly VfxBurstRecipe Motes = new()
    {
        Texture = "watercolour/blob", Lifetime = 1.0f, Gravity = new Vector2(0f, -60f), Damping = 120f,
        SizeMin = 0.8f, SizeMax = 1.5f, Ramp = Ramp((0f, 0f), (0.15f, 0.85f), (0.6f, 0.7f), (1f, 0f)),
        Scale = Grow(0.7f, 1.25f), EmitRadius = 6f,
    };
    private static readonly VfxBurstRecipe Sand = new()
    {
        Texture = "watercolour/blob", Lifetime = 1.1f, Gravity = new Vector2(0f, 40f), Damping = 260f,
        SizeMin = 1.2f, SizeMax = 2.2f, Ramp = Ramp((0f, 0f), (0.12f, 0.7f), (0.5f, 0.55f), (1f, 0f)),
        Scale = Grow(0.6f, 1.4f), EmitRadius = 4f,
    };

    public override VfxBurstRecipe Particles(VfxBits kind) => kind switch
    {
        VfxBits.Spray => Droplets,
        VfxBits.Fall => Debris,
        VfxBits.Rise => Motes,
        _ => Sand,
    };

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            Id = "petals", Texture = "watercolour/petal", Amount = 8, Lifetime = 9f, Direction = new Vector2(0.45f, 1f),
            SpreadDeg = 30f, SpeedMin = 14f, SpeedMax = 34f, SizeMin = 7f, SizeMax = 11f, SpinDeg = 90f,
            A = Hex("fff6e0", 0.80f), B = Hex("f6bccb", 0.80f), ALight = Hex("f3a9bb", 0.85f), BLight = Hex("e58aa3", 0.85f),
            Ramp = Ramp((0f, 0f), (0.1f, 1f), (0.85f, 1f), (1f, 0f)),
        },
        new()
        {
            Id = "sea_spray", Texture = "soft_dot", Amount = 4, Lifetime = 5f, Direction = Vector2.Up, SpreadDeg = 50f,
            SpeedMin = 8f, SpeedMax = 22f, SizeMin = 3f, SizeMax = 5f, BottomBand = 0.35f,
            A = Hex("ffffff", 0.55f), B = Hex("dff4f7", 0.55f), ALight = Hex("9fc6d6", 0.6f), BLight = Hex("c7e2ea", 0.6f),
            Ramp = Ramp((0f, 0f), (0.2f, 1f), (0.7f, 1f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

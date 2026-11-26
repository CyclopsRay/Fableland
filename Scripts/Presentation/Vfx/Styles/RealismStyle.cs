namespace Fableland.Vfx;

using Godot;
using static VfxPen;

/// <summary>
/// HC Dungeon — lit realism (RealmStyleBible §5; R2 §4.5). The only style that emits light:
/// a mark is glow, velocity-stretched sparks and soft smoke. Light is additive, smoke is
/// alpha; anything hot runs the fire ramp and cools along it before it fades, steel answers
/// with one cold glint, and smoke lingers. Motion is continuous and physical — gravity and
/// drag at full frame rate. On a pale canvas additive light would vanish, so it is drawn
/// solid over a bed of soot instead.
/// </summary>
public sealed class RealismStyle : VfxStyle
{
    public override string Id => "realism";
    public override string Medium => "Lit realism";
    public override float Fps => 0f;                                     // every frame, interpolated
    public override float ReleaseSeconds => 0.22f;
    public override bool Additive(VfxMarkKind kind, VfxCanvas canvas) =>
        canvas != VfxCanvas.Light && kind != VfxMarkKind.Puff && kind != VfxMarkKind.Warm;

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        (string main, string alt, string accent) = element switch
        {
            VfxElement.Dust => ("7e848d", "4f535b", "c9d6e6"),
            VfxElement.Fire => ("f26a0e", "c22a06", "ffb02e"),
            VfxElement.Ice => ("8fb8e8", "4f6f9e", "e6f0fa"),
            VfxElement.Water => ("7fa6c9", "3d5a78", "dce9f5"),
            VfxElement.Leaf => ("6e8f4e", "3c5230", "b9c99a"),
            VfxElement.Poison => ("8fa05a", "3c5230", "d6e0a8"),
            VfxElement.Danger => ("f26a0e", "c22a06", "fff2b0"),         // fire is the realm's one saturated colour
            VfxElement.Heal => ("bfe8b0", "6faf72", "f4fff0"),
            VfxElement.Light => ("fff2b0", "ffb02e", "fffbe8"),
            VfxElement.Smoke => ("3a383c", "25252a", "6a686c"),
            _ => ("ffb02e", "f26a0e", "fff2b0"),
        };
        Color soot = canvas == VfxCanvas.Light ? Hex("25252a", 0.55f) : default;
        return new VfxPalette(Hex(main), Hex(alt), Hex(accent), Hex("c9d6e6"), soot);
    }

    private static float Cool(float t, float from = 0.25f) => 1f - Smooth((t - from) / (1f - from));

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            // Smoke: soft, rising, and slow to leave.
            float fade = Bell(t, 0.18f);
            Vector2 at = c + new Vector2(0f, -r * 0.7f * t);
            Disc(at, r * (0.7f + 0.8f * t), A(p.Accent, 0.55f * fade), A(p.Main, 0f), 18, 0.12f, m.S.Seed + (uint)(int)c.X);
            return;
        }
        bool lit = m.Canvas != VfxCanvas.Light;
        float a = Cool(t), flash = 1f - Smooth(t / 0.38f);
        Color hot = Mix(p.Accent, p.Main, t * 2f), cool = Mix(p.Main, p.Alt, t * 1.5f);     // down the ramp
        if (!lit) Disc(c, r * 1.7f, A(p.Ground, a), A(p.Ground, 0f), 18);
        Disc(c, r * (1.4f + 1.0f * EaseOut(t)), A(cool, (lit ? 0.60f : 0.50f) * a), A(cool, 0f), 24);
        Disc(c, r * (0.80f - 0.30f * t), A(hot, a), A(cool, lit ? 0f : 0.55f * a), 18);
        if (flash <= 0.02f) return;
        // One cold glint, as steel gives: four hairlines from the contact.
        float len = r * (2.0f + 1.6f * flash), tilt = m.S.Rot + 0.35f + R(m.S.Seed, 3) * 0.5f;
        for (int i = 0; i < 4; i++)
            Line(c, c + Polar(tilt + i * Mathf.Pi * 0.5f, i % 2 == 0 ? len : len * 0.55f), 2.4f * flash, 0f, A(p.Line, flash), A(p.Line, 0f));
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = Cool(t, 0.30f), hw = width * 0.5f;
        if (m.Canvas == VfxCanvas.Light) CrescentBand(u0, u1, hw * 1.15f, -1f, 1f, A(p.Ground, 0f), A(p.Ground, a));
        // The trail of light a fast edge leaves: nothing at the back, white-hot at the front.
        CrescentBand(u0, u1, hw, -1f, 0.25f, A(p.Alt, 0f), A(p.Main, 0.55f * a));
        CrescentBand(u0, u1, hw, 0.25f, 1f, A(p.Main, 0.55f * a), A(p.Accent, 0.95f * a));
        CrescentEdge(u0, u1, hw, 0f, 2.2f, A(p.Line, Cool(t, 0.15f)));                       // the steel itself
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        ref VfxPalette p = ref m.S.P;
        float a = Cool(t, 0.20f), sy = ry / Mathf.Max(rx, 0.01f), w = Mathf.Clamp(rx * 0.28f, 5f, 26f);
        Band(c, Mathf.Max(0f, rx - w), rx, A(p.Main, 0f), A(p.Main, 0.50f * a), 40, 0f, 0u, sy);
        Band(c, rx - 1.6f, rx + 1.6f, A(p.Accent, a), A(p.Accent, a), 40, 0f, 0u, sy);
        Band(c, rx + 1.6f, rx + w * 0.4f, A(p.Main, 0.35f * a), A(p.Main, 0f), 40, 0f, 0u, sy);
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        float a = Cool(t, 0.40f);
        Color hot = Mix(pal.Accent, pal.Main, t), cool = Mix(pal.Main, pal.Alt, t);
        if (m.Canvas == VfxCanvas.Light) Disc(p, size * 1.1f, A(pal.Ground, a), A(pal.Ground, 0f), 10);
        Disc(p, size * 1.4f, A(cool, 0.45f * a), A(cool, 0f), 12);
        Tear(p, Polar(rot, 1f), size * 1.9f, size * 0.8f, A(hot, a), A(cool, 0f));
    }

    protected override void Tone(VfxMark m, float t)
    {
        ref VfxPalette p = ref m.S.P;
        Fill(A(p.Alt, 0.05f + 0.08f * t), A(p.Main, 0.16f + 0.24f * t));                    // heat, brightest at the edge
        int n = PathCount;
        for (int i = 0; i < n; i++) Line(PathPoint(i), PathPoint((i + 1) % n), 2f, A(p.Accent, 0.45f + 0.4f * t));
    }

    // ── particles: sparks, chips, embers, smoke ──────────────────────────────────
    private static readonly VfxBurstRecipe Sparks = new()
    {
        Texture = "realism/spark", Additive = true, AlignToVelocity = true, Lifetime = 1.0f, Gravity = new Vector2(0f, 640f),
        Damping = 30f, SpeedMin = 0.5f, SpeedMax = 1.25f, SizeMin = 0.5f, SizeMax = 1.0f,
        Ramp = Ramp((0f, new Color(1f, 1f, 1f)), (0.35f, new Color(1f, 0.85f, 0.6f)), (1f, new Color(0.8f, 0.25f, 0.1f, 0f))),
    };
    private static readonly VfxBurstRecipe Chips = new()
    {
        Texture = "realism/chip", Lifetime = 1.0f, Gravity = new Vector2(0f, 900f), Damping = 20f, SizeMin = 0.7f, SizeMax = 1.5f,
        SpinDeg = 420f, Ramp = Ramp((0f, 1f), (0.7f, 1f), (1f, 0f)),
    };
    private static readonly VfxBurstRecipe Embers = new()
    {
        Texture = "soft_dot", Additive = true, Lifetime = 1.3f, Gravity = new Vector2(0f, -150f), Damping = 40f,
        SizeMin = 0.5f, SizeMax = 1.1f, EmitRadius = 6f,
        Ramp = Ramp((0f, new Color(1f, 1f, 1f, 0f)), (0.12f, new Color(1f, 1f, 1f)), (0.6f, new Color(1f, 0.7f, 0.4f)), (1f, new Color(0.7f, 0.2f, 0.1f, 0f))),
    };
    private static readonly VfxBurstRecipe Smoke = new()
    {
        Texture = "realism/smoke", Lifetime = 1.7f, Gravity = new Vector2(0f, -36f), Damping = 220f, SizeMin = 1.4f, SizeMax = 2.6f,
        Scale = Grow(0.6f, 1.6f), EmitRadius = 5f, Ramp = Ramp((0f, 0f), (0.15f, 0.8f), (0.6f, 0.55f), (1f, 0f)),
    };

    public override VfxBurstRecipe Particles(VfxBits kind) => kind switch
    {
        VfxBits.Spray => Sparks,
        VfxBits.Fall => Chips,
        VfxBits.Rise => Embers,
        _ => Smoke,
    };

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            Id = "embers", Texture = "soft_dot", Additive = true, Amount = 16, Lifetime = 5f, Direction = Vector2.Up, SpreadDeg = 22f,
            SpeedMin = 14f, SpeedMax = 46f, SizeMin = 2.5f, SizeMax = 5f, BottomBand = 0.6f,
            A = Hex("ffb02e", 0.9f), B = Hex("f26a0e", 0.9f), ALight = Hex("c22a06", 0.8f), BLight = Hex("f26a0e", 0.8f),
            Ramp = Ramp((0f, 0f), (0.1f, 1f), (0.6f, 0.8f), (1f, 0f)),
        },
        new()
        {
            Id = "dust_in_light", Texture = "soft_dot", Amount = 8, Lifetime = 8f, Direction = new Vector2(0.3f, 1f), SpreadDeg = 60f,
            SpeedMin = 3f, SpeedMax = 10f, SizeMin = 2f, SizeMax = 4f,
            A = Hex("c9d6e6", 0.30f), B = Hex("7e848d", 0.30f), ALight = Hex("4f535b", 0.4f), BLight = Hex("7e848d", 0.4f),
            Ramp = Ramp((0f, 0f), (0.3f, 1f), (0.7f, 1f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

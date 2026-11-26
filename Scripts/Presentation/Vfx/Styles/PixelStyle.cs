namespace Fableland.Vfx;

using System;
using Godot;
using static VfxPen;

/// <summary>
/// TF Forgotten City — pixel art (RealmStyleBible §7B; R2 §4.5). Everything is a square on
/// the realm's grid: one art pixel is 2 px of the world (32 to a map cell), and nothing is
/// ever turned off that grid, blurred or blended. A mark plays in fixed frames at 12 a
/// second, uses at most four colours, and ends by palette step (white → neon → its second
/// neon → dark → off) and dither dropout — never by alpha. A hit is four frames: a block, a
/// plus, a ring of eight, four scattered pixels.
/// </summary>
public sealed class PixelStyle : VfxStyle
{
    public override string Id => "pixel";
    public override string Medium => "Pixel art";
    public override float Fps => 12f;
    public override float Grid => U;
    public override bool Nearest => true;
    public override bool KeepsGrid => true;
    public override int MaxPieces => 8;
    public override float Tempo => 1.4f;                                // a hit's four frames need a third of a second
    protected override bool BoxedField => true;
    public override float ReleaseSeconds => 0f;
    public override float Ease(float t) => Sat(t);

    /// <summary>One art pixel, in world px.</summary>
    private const float U = 2f;

    public override VfxPalette Element(VfxElement element, VfxCanvas canvas)
    {
        (string main, string alt, string accent) = element switch
        {
            VfxElement.Dust => ("9a90b8", "6f648f", "ffffff"),
            VfxElement.Fire => ("ffe14a", "ff3dbe", "ffffff"),
            VfxElement.Ice => ("3df6ff", "6fe0d0", "ffffff"),
            VfxElement.Water => ("6fe0d0", "3df6ff", "ffffff"),
            VfxElement.Leaf => ("5fbf6e", "2f7a52", "9bff4a"),
            VfxElement.Poison => ("9bff4a", "2f7a52", "ffffff"),
            VfxElement.Danger => ("ff3dbe", "ffe14a", "ffffff"),
            VfxElement.Heal => ("9bff4a", "5fbf6e", "ffffff"),
            VfxElement.Light => ("ffffff", "ffe14a", "3df6ff"),
            VfxElement.Smoke => ("6f648f", "4a4168", "9a90b8"),
            _ => ("3df6ff", "ff3dbe", "ffffff"),
        };
        // Off its own night, neon needs a dark pixel under it to read — and on a pale canvas
        // the first, brightest step of a mark is dark instead of white.
        Color shadow = canvas == VfxCanvas.Dark ? default : Hex("1b1730");
        if (canvas == VfxCanvas.Light && accent == "ffffff") accent = "1b1730";
        return new VfxPalette(Hex(main), Hex(alt), Hex(accent), Hex("1b1730"), shadow);
    }

    /// <summary>A block of w × h art pixels whose top-left sits on the grid at p.</summary>
    private static void Px(ref VfxPalette pal, Vector2 p, int w, int h, Color c)
    {
        var at = new Vector2(Mathf.Floor(p.X / U) * U, Mathf.Floor(p.Y / U) * U);
        var size = new Vector2(w * U, h * U);
        if (pal.Ground.A > 0f) Box(at + new Vector2(U, U), size, pal.Ground);
        Box(at, size, c);
    }

    /// <summary>A square of n art pixels centred on p.</summary>
    private static void Dot(ref VfxPalette pal, Vector2 p, int n, Color c) => Px(ref pal, p - new Vector2(n * U * 0.5f, n * U * 0.5f), n, n, c);

    /// <summary>The colour of a mark at its age: the palette steps down, it never fades.</summary>
    private static Color StepDown(ref VfxPalette pal, float t) =>
        t < 0.25f ? pal.Accent : t < 0.55f ? pal.Main : t < 0.85f ? pal.Alt : pal.Line;

    private static readonly Vector2[] Eight =
    {
        new(1f, 0f), new(0.7071f, 0.7071f), new(0f, 1f), new(-0.7071f, 0.7071f),
        new(-1f, 0f), new(-0.7071f, -0.7071f), new(0f, -1f), new(0.7071f, -0.7071f),
    };

    protected override void Mass(VfxMark m, Vector2 c, float r, float t)
    {
        ref VfxPalette p = ref m.S.P;
        int frame = Math.Min(3, (int)(t * 4f));
        int k = Math.Max(1, Mathf.RoundToInt(r / (U * 2f)));
        if (m.S.Kind == VfxMarkKind.Puff)
        {
            // Dust: one block per lobe, a pixel smaller and a pixel higher each frame.
            int side = Math.Max(1, k + 1 - frame);
            Dot(ref p, c + new Vector2(0f, -frame * U * 2f), side, frame == 0 ? p.Accent : frame < 3 ? p.Main : p.Alt);
            return;
        }
        bool big = (m.S.Flags & VfxFlags.Big) != 0;
        switch (frame)
        {
            case 0:     // a block
                Dot(ref p, c, 2 * k + 1, p.Accent);
                break;
            case 1:     // a plus
            {
                int arm = 2 * k + 2, thick = Math.Max(1, k / 2 + 1);
                Px(ref p, c - new Vector2(arm * U, thick * U * 0.5f), arm * 2, thick, p.Main);
                Px(ref p, c - new Vector2(thick * U * 0.5f, arm * U), thick, arm * 2, p.Main);
                Dot(ref p, c, thick, p.Accent);
                break;
            }
            case 2:     // a ring of eight (sixteen for a blast)
            {
                int n = big ? 16 : 8;
                for (int i = 0; i < n; i++)
                {
                    Vector2 d = big ? Polar(i * Tau / 16f, 1f) : Eight[i];
                    Dot(ref p, c + d * (r * 1.7f), Math.Max(1, k / 2 + 1), (i & 1) == 0 ? p.Main : p.Alt);
                }
                break;
            }
            default:    // four scattered pixels
                for (int i = 1; i < 8; i += 2) Dot(ref p, c + Eight[i] * (r * 2.4f), 1, p.Alt);
                break;
        }
    }

    protected override void Brush(VfxMark m, float width, float u0, float u1, float t)
    {
        if (u1 - u0 < 0.01f) return;
        ref VfxPalette p = ref m.S.P;
        float hw = width * 0.5f;
        int samples = Mathf.Clamp((int)(PathLength() / U), 2, 150);
        bool dither = t > 0.6f;
        Color body = StepDown(ref p, t * 0.9f);
        for (int s = 0; s <= samples; s++)
        {
            float u = s / (float)samples;
            if (u < u0 || u > u1) continue;
            PathAt(u, out Vector2 at, out Vector2 n);
            int thick = Mathf.Clamp(Mathf.RoundToInt(CrescentWidth(u, hw, 0.6f) * 2f / U), 1, 9);
            for (int j = 0; j < thick; j++)
            {
                if (dither && ((s + j) & 1) == 1) continue;                  // dither dropout
                Color c = j == thick - 1 ? p.Accent : j == 0 && thick > 2 ? p.Alt : body;     // a bright leading edge
                Px(ref p, at + n * ((j - (thick - 1) * 0.5f) * U), 1, 1, c);
            }
        }
    }

    protected override void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t)
    {
        ref VfxPalette p = ref m.S.P;
        int n = Mathf.Clamp((int)(Tau * Mathf.Max(rx, ry) / U * 0.75f), 8, 110);
        bool dither = t > 0.55f;
        Color body = StepDown(ref p, t);
        for (int i = 0; i < n; i++)
        {
            if (dither && (i & 1) == 1) continue;
            float a = i * Tau / n;
            Px(ref p, c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), 1, 1, i % 4 == 0 ? p.Alt : body);
        }
    }

    protected override void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i)
    {
        ref VfxPalette pal = ref m.S.P;
        if (t > 0.7f && (i & 1) == 1) return;                                // dither dropout
        Dot(ref pal, p, size >= 7f ? 2 : 1, StepDown(ref pal, t));
    }

    protected override void Tone(VfxMark m, float t)
    {
        // An ordered dither inside the shape, a one-pixel dotted line round it.
        ref VfxPalette p = ref m.S.P;
        Rect2 box = PathBounds();
        const float cell = 8f;
        int drawn = 0;
        for (float y = Mathf.Floor(box.Position.Y / cell) * cell; y < box.End.Y && drawn < 420; y += cell)
        {
            for (float x = Mathf.Floor(box.Position.X / cell) * cell; x < box.End.X && drawn < 420; x += cell)
            {
                var at = new Vector2(x + 2f, y + 2f);
                if (!PathContains(at)) continue;
                Px(ref p, at, 1, 1, p.Alt);
                if (t > 0.5f) Px(ref p, at + new Vector2(4f, 4f), 1, 1, p.Main);       // the dither thickens
                drawn++;
            }
        }
        int n = PathCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = PathPoint(i), b = PathPoint((i + 1) % n);
            int steps = Mathf.Clamp((int)(a.DistanceTo(b) / (U * 2f)), 1, 140);
            for (int s = 0; s <= steps; s++) Px(ref p, a.Lerp(b, s / (float)steps), 1, 1, t > 0.75f ? p.Accent : p.Main);
        }
    }

    private static readonly VfxAmbientRecipe[] Air =
    {
        new()
        {
            // Neon motes climbing the glyph lines: thin, and the only bright thing in the night.
            Id = "neon_motes", Texture = null, Amount = 12, Lifetime = 5f, Direction = Vector2.Up, SpreadDeg = 4f,
            SpeedMin = 18f, SpeedMax = 44f, SizeMin = 2f, SizeMax = 4f, Fps = 12f,
            A = Hex("3df6ff", 0.9f), B = Hex("ff3dbe", 0.9f), ALight = Hex("1b1730", 0.9f), BLight = Hex("d9589f", 0.9f),
            Ramp = Ramp((0f, 0f), (0.05f, 0f), (0.06f, 1f), (0.8f, 1f), (0.81f, 0.5f), (0.93f, 0.5f), (0.94f, 0f), (1f, 0f)),
        },
    };

    public override VfxAmbientRecipe[] Ambience => Air;
}

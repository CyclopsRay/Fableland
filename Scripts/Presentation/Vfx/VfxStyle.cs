namespace Fableland.Vfx;

using System;
using Godot;
using static VfxPen;

/// <summary>How a medium throws small pieces through the air with a pooled
/// <c>CPUParticles2D</c> — for media whose motion is continuous. A medium that is stamped or
/// stepped draws its pieces instead (<see cref="VfxMarkKind.Bits"/>) and returns no recipe.</summary>
public sealed class VfxBurstRecipe
{
    public string Texture { get; init; }                 // a VfxTextures id; null = a square
    public bool Additive { get; init; }
    public bool AlignToVelocity { get; init; }
    public float Fps { get; init; }                      // 0 = smooth
    public float Lifetime { get; init; } = 0.4f;         // × the composition's life
    public float SpeedMin { get; init; } = 0.45f;        // × the composition's speed
    public float SpeedMax { get; init; } = 1f;
    public Vector2 Gravity { get; init; } = new(0f, 900f);
    public float Damping { get; init; } = 60f;
    public float SizeMin { get; init; } = 0.6f;          // × the composition's piece size
    public float SizeMax { get; init; } = 1.2f;
    public float SpinDeg { get; init; }
    public float EmitRadius { get; init; } = 2f;
    /// <summary>Scale across a particle's life (null = constant).</summary>
    public Curve Scale { get; init; }
    /// <summary>Colour across a particle's life, multiplied into its own colour.</summary>
    public Gradient Ramp { get; init; }
    /// <summary>True: each piece is somewhere between the palette's Main and Alt.</summary>
    public bool TwoTone { get; init; } = true;
    /// <summary>Which palette colour the pieces take when not two-tone: 0 Main, 1 Alt, 2 Accent,
    /// 3 Line, −1 none (a texture that carries its own colours).</summary>
    public int Colour { get; init; }

    internal Texture2D TextureResource;
    internal bool Resolved;
}

/// <summary>A realm's own air: one continuous emitter that follows the view.</summary>
public sealed class VfxAmbientRecipe
{
    public string Id { get; init; }
    public string Texture { get; init; }
    public bool Additive { get; init; }
    public float Fps { get; init; }
    public int Amount { get; init; } = 10;
    public float Lifetime { get; init; } = 6f;
    public Vector2 Direction { get; init; } = Vector2.Down;
    public float SpreadDeg { get; init; } = 25f;
    public float SpeedMin { get; init; } = 10f;
    public float SpeedMax { get; init; } = 30f;
    public Vector2 Gravity { get; init; }
    public float SizeMin { get; init; } = 3f;            // px
    public float SizeMax { get; init; } = 6f;
    public float SpinDeg { get; init; }
    public bool AlignToVelocity { get; init; }
    /// <summary>The two ends of the colours a mote may take — on a dark canvas, and on a pale one.</summary>
    public Color A { get; init; } = Colors.White;
    public Color B { get; init; } = Colors.White;
    public Color ALight { get; init; } = Colors.White;
    public Color BLight { get; init; } = Colors.White;
    public Gradient Ramp { get; init; }
    /// <summary>0 = the whole view; else only this share of it, measured up from the bottom.</summary>
    public float BottomBand { get; init; }
}

/// <summary>
/// A medium — how one world makes a mark (Docs/VFX.gdd §2). One style per medium; a style
/// renders every archetype. Adding a world is adding a style.
///
/// A style is five primitives — the medium's <see cref="Mass"/> (a filled contact mark),
/// <see cref="Brush"/> (a stroke along a path), <see cref="Hoop"/> (a ring), <see cref="Chip"/>
/// (one small piece) and <see cref="Tone"/> (an area at low strength) — plus its palette rule
/// (every <see cref="VfxElement"/>, on a dark, a mid-tone and a pale canvas), its timing rule
/// (<see cref="Fps"/>, <see cref="Grid"/>, <see cref="Ease"/>) and how its marks end. The base
/// class composes the archetypes and the mark kinds from those; a style overrides a
/// composition where its medium has a grammar of its own (the VOID implodes, pixel art plays
/// four fixed frames).
/// </summary>
public abstract class VfxStyle
{
    public abstract string Id { get; }
    /// <summary>The medium's name, for the gallery and the docs.</summary>
    public abstract string Medium { get; }

    // ── timing rule ──────────────────────────────────────────────────────────────
    /// <summary>Frames a second the medium is redrawn at. Hand-made media are stepped
    /// (8–15); 0 redraws every frame.</summary>
    public virtual float Fps => 30f;
    /// <summary>True when every step re-seeds the hand's wobble (line boil).</summary>
    public virtual bool Boils => false;
    /// <summary>Positions snap to this many px (pixel art); 0 = none.</summary>
    public virtual float Grid => 0f;
    /// <summary>How long the medium's marks last, as a multiple of the composition's life.</summary>
    public virtual float Tempo => 1f;
    /// <summary>The most pieces one drawn burst may hold (the poster forbids clutter).</summary>
    public virtual int MaxPieces => 12;
    public virtual bool Nearest => false;
    /// <summary>True when a field shows its box as an area (a hatched, dithered or enamelled
    /// plane). Soft media leave it out: their ribbons reach the box's edges and that is the
    /// area.</summary>
    protected virtual bool BoxedField => false;
    /// <summary>True when a mark must never be turned off its grid: the node stays unrotated
    /// and the direction is drawn into the geometry instead (<c>VfxMarkState.Rot</c>).</summary>
    public virtual bool KeepsGrid => false;
    /// <summary>True on a step this medium drops (the VOID's missing frames).</summary>
    public virtual bool Skips(VfxMark m) => false;
    /// <summary>Seconds a stopped loop takes to leave; 0 = it is cut (a medium with no fade).</summary>
    public virtual float ReleaseSeconds => 0.18f;
    /// <summary>Whether this mark is drawn with additive blending (light, on a dark canvas).</summary>
    public virtual bool Additive(VfxMarkKind kind, VfxCanvas canvas) => false;
    /// <summary>How a mark of this medium grows: 0 → 1.</summary>
    public virtual float Ease(float t) => EaseOut(t);

    // ── palette rule ─────────────────────────────────────────────────────────────
    public abstract VfxPalette Element(VfxElement element, VfxCanvas canvas);

    /// <summary>An owner's own colours, laid into this medium's line and ground.</summary>
    public virtual VfxPalette Look(VfxLook look, VfxCanvas canvas)
    {
        VfxPalette p = Element(VfxElement.Impact, canvas);
        if (look == null || !look.HasColours) return p;
        p.Main = look.Main;
        p.Alt = look.Alt;
        p.Accent = look.Accent;
        return p;
    }

    // ── the five primitives ──────────────────────────────────────────────────────
    /// <summary>The medium's filled contact mark, radius r, at life phase t (0..1).</summary>
    protected abstract void Mass(VfxMark m, Vector2 c, float r, float t);
    /// <summary>A stroke along <see cref="VfxPen"/>'s path, visible between u0 and u1.</summary>
    protected abstract void Brush(VfxMark m, float width, float u0, float u1, float t);
    /// <summary>A ring of radii (rx, ry).</summary>
    protected abstract void Hoop(VfxMark m, Vector2 c, float rx, float ry, float t);
    /// <summary>One small piece at p, turned to rot, at its own life phase t.</summary>
    protected abstract void Chip(VfxMark m, Vector2 p, float rot, float size, float t, int i);
    /// <summary>The pen's path as a closed area at strength t (a telegraph filling, a field).</summary>
    protected abstract void Tone(VfxMark m, float t);

    // ── particles and ambience ───────────────────────────────────────────────────
    /// <summary>The emitter recipe for a kind of pieces, or null to draw them stepped.</summary>
    public virtual VfxBurstRecipe Particles(VfxBits kind) => null;
    public virtual VfxAmbientRecipe[] Ambience => Array.Empty<VfxAmbientRecipe>();

    // ── compositions: an archetype as pieces ─────────────────────────────────────
    /// <summary>Compose one effect from marks and bursts. <c>x.Size</c> is already the
    /// row's size rule applied to the cue's real numbers.</summary>
    public virtual void Play(VfxArchetype archetype, ref VfxCtx x, VfxDirector d)
    {
        float s = x.Size;
        switch (archetype)
        {
            case VfxArchetype.HitSpark:
            {
                d.Mark(ref x, VfxMarkKind.Impact, x.Pos, Angle(x.Dir), s, 0.24f);
                if (x.Power < 0.05f) break;                                   // a graze: the mark and nothing else
                d.Bits(ref x, VfxBits.Spray, x.Pos, x.Dir, 2 + (int)(x.Power * 9f), 190f + 250f * x.Power, 70f, s * 0.34f, 0.38f);
                if (x.Power >= 0.74f) d.Mark(ref x, VfxMarkKind.Ring, x.Pos, 0f, s * 2.1f, 0.30f);
                // The rare flourish: one glint above the blow. (A kill is told by the row's
                // modifier — more, bigger — and by the victim's own death burst.)
                if ((x.Flags & VfxFlags.Flourish) != 0)
                    d.Mark(ref x, VfxMarkKind.Glint, x.Pos + new Vector2(0f, -s * 1.6f), 0f, s * 0.9f, 0.28f, 0.05f);
                break;
            }
            case VfxArchetype.HitReact:
                d.Bits(ref x, VfxBits.Fall, x.Pos, x.Dir, 2 + (int)(x.Power * 5f), 130f + 160f * x.Power, 110f, s * 0.36f, 0.5f);
                break;
            case VfxArchetype.SlashArc:
            {
                VfxMark arc = d.Mark(ref x, VfxMarkKind.Arc, x.Pos, Angle(x.Dir), s, 0.26f);
                if (arc != null)
                {
                    arc.S.Arc = x.Arc;
                    // Stage 2 comes back up; a swing to the left is the same swing mirrored.
                    if ((x.Stage == 2) ^ (x.Dir.X < 0f)) arc.S.Flags |= VfxFlags.Reverse;
                    if (x.Stage >= 3) arc.S.Flags |= VfxFlags.Double;
                }
                if (x.Stage >= 3) d.Bits(ref x, VfxBits.Spray, x.Pos + x.Dir * (s * 0.72f), x.Dir, 5, 260f, 55f, s * 0.085f, 0.40f);
                if ((x.Flags & VfxFlags.Burning) != 0)
                {
                    VfxCtx fire = x;
                    fire.P = Element(VfxElement.Fire, x.Canvas);
                    d.Bits(ref fire, VfxBits.Rise, x.Pos + x.Dir * (s * 0.70f), Vector2.Up, 4, 110f, 120f, s * 0.11f, 0.42f);
                }
                break;
            }
            case VfxArchetype.Burst:
            {
                VfxMark core = d.Mark(ref x, VfxMarkKind.Impact, x.Pos, 0f, s * 0.5f, 0.32f);
                if (core != null) core.S.Flags |= VfxFlags.Big;
                d.Mark(ref x, VfxMarkKind.Ring, x.Pos, 0f, s, 0.36f);                 // to exactly its radius
                d.Bits(ref x, VfxBits.Spray, x.Pos, Vector2.Zero, 14, s * 3.4f, 360f, Mathf.Max(5f, s * 0.11f), 0.5f);
                VfxCtx smoke = x;
                smoke.P = Element(VfxElement.Smoke, x.Canvas);
                smoke.Priority = VfxPriority.Cosmetic;
                d.Mark(ref smoke, VfxMarkKind.Puff, x.Pos, 0f, s * 0.55f, 0.6f, 0.06f);
                break;
            }
            case VfxArchetype.Ring:
            {
                VfxMark ring = d.Mark(ref x, VfxMarkKind.Ring, x.Pos, 0f, s, 0.32f);
                if (ring != null) ring.S.Extents = x.Extents;
                break;
            }
            case VfxArchetype.Trail:
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(x.Magnitude * 0.5f), 3, 5);
                float centre = Angle(x.Dir.LengthSquared() > 0.01f ? x.Dir : Vector2.Up);
                for (int i = 0; i < n; i++)
                    d.Mark(ref x, VfxMarkKind.Streak, x.Pos, centre + Mathf.DegToRad(Mathf.Lerp(-38f, 38f, (i + 0.5f) / n)), s, 0.22f);
                d.Mark(ref x, VfxMarkKind.Impact, x.Pos, 0f, s * 0.26f, 0.18f);
                break;
            }
            case VfxArchetype.DustPuff:
            {
                if ((x.Flags & VfxFlags.Submerged) != 0)
                {
                    VfxCtx water = x;
                    water.P = Element(VfxElement.Water, x.Canvas);
                    d.Bits(ref water, VfxBits.Rise, x.Pos, Vector2.Up, 3 + (int)(x.Power * 3f), 70f, 70f, s * 0.26f, 0.6f);
                    break;
                }
                d.Mark(ref x, VfxMarkKind.Puff, x.Pos, 0f, s, 0.34f);
                if (x.Power > 0.55f) d.Bits(ref x, VfxBits.Dust, x.Pos, Vector2.Up, 2 + (int)(x.Power * 4f), 70f + 80f * x.Power, 150f, s * 0.30f, 0.5f);
                break;
            }
            case VfxArchetype.Splash:
            {
                bool leaving = (x.Flags & VfxFlags.Exit) != 0;
                VfxMark ripple = d.Mark(ref x, VfxMarkKind.Ring, x.Pos, 0f, s * 1.9f, 0.44f);
                if (ripple != null) ripple.S.Extents = new Vector2(s * 1.9f, s * 0.5f);      // lying on the surface
                Vector2 up = x.Dir.LengthSquared() > 0.01f ? new Vector2(-x.Dir.X * 0.35f, -1f).Normalized() : Vector2.Up;
                d.Bits(ref x, VfxBits.Spray, x.Pos, up, (leaving ? 3 : 4) + (int)(x.Power * 6f), 210f + 210f * x.Power, 62f, s * 0.24f, 0.5f);
                break;
            }
            case VfxArchetype.Rustle:
                d.Bits(ref x, VfxBits.Fall, x.Pos, x.Dir.LengthSquared() > 0.01f ? x.Dir : Vector2.Up, 3 + (int)(x.Power * 3f), 100f + 70f * x.Power, 200f, s * 0.40f, 0.62f);
                break;
            case VfxArchetype.StatusLoop:
            {
                VfxMark aura = d.Mark(ref x, VfxMarkKind.Aura, x.Pos, 0f, s, 1.2f);
                if (aura != null) aura.S.Variant = x.Stage;
                break;
            }
            case VfxArchetype.Telegraph:
            {
                VfxMark zone = d.Mark(ref x, VfxMarkKind.Zone, x.Pos, Angle(x.Dir), s, 0.5f);
                if (zone != null)
                {
                    zone.S.Extents = new Vector2(s, Mathf.Max(x.BodyRadius * 0.7f, 10f));
                    zone.S.Param = x.Magnitude;                                           // the tell's seconds
                }
                break;
            }
            case VfxArchetype.DeathBurst:
            {
                bool big = (x.Flags & VfxFlags.Big) != 0;
                d.Mark(ref x, VfxMarkKind.Impact, x.Pos, 0f, s, 0.30f);
                d.Bits(ref x, VfxBits.Fall, x.Pos, Vector2.Up, big ? 10 : 6, 210f, 300f, s * 0.34f, 0.62f);
                VfxCtx dust = x;
                dust.P = Element(VfxElement.Dust, x.Canvas);
                dust.Priority = VfxPriority.Cosmetic;
                d.Mark(ref dust, VfxMarkKind.Puff, x.Pos + new Vector2(0f, s * 0.5f), 0f, s * 0.9f, 0.42f, 0.04f);
                if (big) d.Mark(ref x, VfxMarkKind.Ring, x.Pos, 0f, s * 2.8f, 0.42f);
                break;
            }
            case VfxArchetype.SpawnPop:
            {
                bool leaving = (x.Flags & VfxFlags.Exit) != 0;
                VfxMark ring = d.Mark(ref x, VfxMarkKind.Ring, x.Pos, 0f, s * 1.5f, 0.28f);
                if (ring != null && !leaving) ring.S.Flags |= VfxFlags.Reverse;                   // closes on the arrival
                d.Bits(ref x, VfxBits.Rise, x.Pos + new Vector2(0f, s * 0.5f), Vector2.Up, leaving ? 3 : 4, 95f, 110f, s * 0.24f, 0.42f);
                break;
            }
            case VfxArchetype.Heal:
            {
                d.Bits(ref x, VfxBits.Rise, x.Pos, Vector2.Up, 5 + (int)(x.Power * 4f), 80f, 80f, s * 0.36f, 0.72f);
                VfxMark ring = d.Mark(ref x, VfxMarkKind.Ring, x.Pos + new Vector2(0f, s * 0.9f), 0f, s * 1.2f, 0.42f);
                if (ring != null) ring.S.Extents = new Vector2(s * 1.2f, s * 0.34f);
                break;
            }
            case VfxArchetype.Shine:
                d.Mark(ref x, VfxMarkKind.Glint, x.Pos, 0f, s, 0.38f);
                d.Bits(ref x, VfxBits.Rise, x.Pos, Vector2.Up, 3, 70f, 150f, s * 0.20f, 0.40f);
                break;
            case VfxArchetype.ExpirePop:
                d.Mark(ref x, VfxMarkKind.Impact, x.Pos, 0f, s, 0.20f);
                d.Bits(ref x, VfxBits.Spray, x.Pos, -x.Dir, 2, 120f, 110f, s * 0.36f, 0.30f);
                break;
            case VfxArchetype.FieldLoop:
            {
                VfxMark field = d.Mark(ref x, VfxMarkKind.Field, x.Pos, 0f, s, 0.9f);
                if (field != null) field.S.Extents = x.Extents.LengthSquared() > 1f ? x.Extents : new Vector2(s, s * 0.5f);
                break;
            }
            case VfxArchetype.Cast:
                d.Mark(ref x, VfxMarkKind.Ring, x.Pos, 0f, s * 1.35f, 0.38f);
                d.Bits(ref x, VfxBits.Rise, x.Pos, Vector2.Up, 5, 85f, 90f, s * 0.20f, 0.5f);
                break;
            case VfxArchetype.Sting:
                d.Mark(ref x, VfxMarkKind.Impact, x.Pos, 0f, s * 0.6f, 0.18f);
                d.Bits(ref x, VfxBits.Rise, x.Pos, Vector2.Up, 2, 70f, 60f, s * 0.24f, 0.36f);
                break;
        }
    }

    protected static float Angle(Vector2 dir) => dir.LengthSquared() > 0.0001f ? dir.Angle() : 0f;

    // ── drawing: a mark kind from the primitives ─────────────────────────────────
    /// <summary>Draw a mark. Called from the mark's own <c>_Draw</c>, in its local space.</summary>
    public void Draw(VfxMark m)
    {
        if (Skips(m)) return;
        Begin(m);
        switch (m.S.Kind)
        {
            case VfxMarkKind.Impact: DrawImpact(m); break;
            case VfxMarkKind.Arc: DrawArc(m); break;
            case VfxMarkKind.Ring: DrawRing(m); break;
            case VfxMarkKind.Puff: DrawPuff(m); break;
            case VfxMarkKind.Streak: DrawStreak(m); break;
            case VfxMarkKind.Zone: DrawZone(m); break;
            case VfxMarkKind.Field: DrawField(m); break;
            case VfxMarkKind.Aura: DrawAura(m); break;
            case VfxMarkKind.Bits: DrawBits(m); break;
            case VfxMarkKind.Glint: DrawGlint(m); break;
            case VfxMarkKind.Sheet: DrawSheet(m); break;
            case VfxMarkKind.Warm: DrawWarm(m); break;
        }
        End();
    }

    protected virtual void DrawImpact(VfxMark m) => Mass(m, Vector2.Zero, m.S.Size, m.T);

    /// <summary>A swing. The path is the middle of the band the eye reads as the cone: its
    /// outer edge reaches exactly the cue's reach, and it spans exactly the cone's angle.</summary>
    protected virtual void DrawArc(VfxMark m)
    {
        float reach = m.S.Size, half = m.S.Arc > 0.01f ? m.S.Arc : 0.7f, t = m.T;
        float head = Sat(t / 0.28f), tail = Sat((t - 0.42f) / 0.58f);
        bool up = (m.S.Flags & VfxFlags.Reverse) != 0;
        float width = reach * 0.30f;
        CrescentPath(reach, width, m.S.Rot, half, 24);
        Brush(m, width, up ? 1f - head : tail, up ? 1f - tail : head, t);
        if ((m.S.Flags & VfxFlags.Double) == 0) return;
        // The third slash: a second, shorter crescent crossing back under the first.
        CrescentPath(reach * 0.66f, reach * 0.20f, m.S.Rot, half * 0.85f, 20);
        float late = Sat((t - 0.10f) / 0.9f);
        float head2 = Sat(late / 0.28f), tail2 = Sat((late - 0.42f) / 0.58f);
        Brush(m, reach * 0.20f, up ? tail2 : 1f - head2, up ? head2 : 1f - tail2, late);
    }

    /// <summary>The centre line of a crescent whose OUTER edge is the arc of radius
    /// <paramref name="reach"/>: a stroke of this width drawn along it touches the cone's
    /// true boundary all the way round and comes to a point at both ends.</summary>
    protected static void CrescentPath(float reach, float width, float rot, float half, int points)
    {
        PathClear();
        for (int i = 0; i < points; i++)
        {
            float u = i / (float)(points - 1);
            float r = reach - CrescentWidth(u, width * 0.5f);
            PathAdd(Polar(rot + Mathf.Lerp(-half, half, u), r));
        }
        PathFinish();
    }

    protected virtual void DrawRing(VfxMark m)
    {
        float g = Ease(m.T);
        if ((m.S.Flags & VfxFlags.Reverse) != 0) g = 1f - g;
        float rx = m.S.Extents.X > 0.5f ? m.S.Extents.X : m.S.Size;
        float ry = m.S.Extents.Y > 0.5f ? m.S.Extents.Y : m.S.Size;
        float k = 0.22f + 0.78f * g;
        Hoop(m, Vector2.Zero, rx * k, ry * k, m.T);
    }

    protected virtual void DrawPuff(VfxMark m)
    {
        uint seed = m.S.Seed;
        int n = 3 + (int)(seed % 3u);
        float r = m.S.Size, g = Ease(m.T);
        for (int i = 0; i < n; i++)
        {
            float side = Mathf.Lerp(-1f, 1f, (i + 0.5f) / n) + Rs(seed, i) * 0.15f;
            var p = new Vector2(side * r * (0.25f + 0.85f * g), -r * (0.10f + 0.40f * g) * (0.4f + R(seed, i + 9)));
            Mass(m, p, r * (0.34f + 0.22f * R(seed, i + 20)), m.T);
        }
    }

    protected virtual void DrawStreak(VfxMark m)
    {
        float len = m.S.Size;
        PathLine(Polar(m.S.Rot, len * 0.12f), Polar(m.S.Rot, len), 8);
        Brush(m, m.S.Param > 0f ? m.S.Param : len * 0.14f, Sat((m.T - 0.30f) / 0.70f), Sat(m.T / 0.40f), m.T);
    }

    /// <summary>A telegraph on the attack's true reach: a lane Size long, or a ring round
    /// the body when the cue gave no reach. It fills as the tell runs out.</summary>
    protected virtual void DrawZone(VfxMark m)
    {
        float reach = m.S.Size, hw = Mathf.Max(4f, m.S.Extents.Y);
        float progress = m.S.Param > 0.01f ? Sat(m.Time / m.S.Param) : 0.5f + 0.5f * Mathf.Sin(m.Time * 9f);
        if (reach <= 1f)
        {
            float r = hw * 2.2f;
            Hoop(m, Vector2.Zero, r, r, 0.25f);
            return;
        }
        Vector2 along = Polar(m.S.Rot, 1f), across = new Vector2(-along.Y, along.X) * hw;
        PathClear();
        PathAdd(-across);
        PathAdd(along * reach - across);
        PathAdd(along * reach + across);
        PathAdd(across);
        PathFinish();
        Tone(m, progress);
    }

    /// <summary>A field on its real box: the area at low strength, two ribbons wound through
    /// it, and a few pieces rising.</summary>
    protected virtual void DrawField(VfxMark m)
    {
        Vector2 e = m.S.Extents;
        if (BoxedField)
        {
            PathClear();
            PathAdd(new Vector2(-e.X, -e.Y));
            PathAdd(new Vector2(e.X, -e.Y));
            PathAdd(new Vector2(e.X, e.Y));
            PathAdd(new Vector2(-e.X, e.Y));
            PathFinish();
            Tone(m, 0.35f);
        }
        // Two ribbons wound through each other, end to end of the box and as tall as it:
        // where they reach is where the field hits.
        for (int k = 0; k < 2; k++)
        {
            PathClear();
            for (int i = 0; i < 22; i++)
            {
                float u = i / 21f;
                float wave = Mathf.Sin(u * Tau * 1.5f + k * Mathf.Pi + m.Time * 8f);
                PathAdd(new Vector2(Mathf.Lerp(-e.X, e.X, u), wave * e.Y * 0.72f * (0.55f + 0.45f * Mathf.Sin(u * Mathf.Pi))));
            }
            PathFinish();
            Brush(m, e.Y * 0.56f, 0f, 1f, 0.30f);
        }
        for (int i = 0; i < 5; i++)
        {
            float phase = Mathf.PosMod(m.Time * 1.1f + i / 5f, 1f);
            var p = new Vector2(Mathf.Lerp(-e.X, e.X, R(m.S.Seed, i)) * 0.9f, e.Y * 0.8f - phase * e.Y * 1.9f);
            Chip(m, p, Mathf.Pi * 0.5f, e.Y * 0.30f * (1f - phase * 0.5f), phase, i);      // head down, tip up: a tongue
        }
    }

    /// <summary>A status worn by a body of radius Size. Variant: 0 burning, 1 on fire,
    /// 2 frozen, 3 trapped, 4 stunned (<see cref="VfxTable.StatusIndex"/>).</summary>
    protected virtual void DrawAura(VfxMark m)
    {
        float r = Mathf.Max(10f, m.S.Size), time = m.Time;
        uint seed = m.S.Seed;
        switch (m.S.Variant)
        {
            case 2:     // frozen: shards set round the body, and a thin ring
                for (int i = 0; i < 4; i++)
                {
                    float a = -Mathf.Pi * 0.5f + (i - 1.5f) * 0.9f + Rs(seed, i) * 0.15f;
                    Chip(m, Polar(a, r * 1.05f), a, r * 0.42f, 0.2f + 0.1f * Mathf.Sin(time * 4f + i), i);
                }
                Hoop(m, new Vector2(0f, r * 0.9f), r * 1.1f, r * 0.30f, 0.35f);
                break;
            case 3:     // trapped: two rings at the feet, breathing in turn
                for (int k = 0; k < 2; k++)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(time * 5f + k * Mathf.Pi);
                    Hoop(m, new Vector2(0f, r * (0.95f - 0.25f * k)), r * (1.0f + 0.12f * pulse), r * 0.30f, 0.30f);
                }
                break;
            case 4:     // stunned: three pieces circling over the head
                for (int i = 0; i < 3; i++)
                {
                    float a = time * 5f + i * Tau / 3f;
                    Chip(m, new Vector2(Mathf.Cos(a) * r * 0.75f, -r * 1.35f + Mathf.Sin(a) * r * 0.20f), a, r * 0.26f, 0.25f, i);
                }
                break;
            default:    // burning / on fire: tongues rising off the body
            {
                int n = m.S.Variant == 1 ? 8 : 6;
                for (int i = 0; i < n; i++)
                {
                    float phase = Mathf.PosMod(time * 0.95f + i / (float)n + R(seed, i) * 0.3f, 1f);
                    var p = new Vector2(Rs(seed, i + 30) * r * 0.85f + Mathf.Sin(phase * 6f + i) * 3f, r * 0.55f - phase * r * 2.1f);
                    Chip(m, p, Mathf.Pi * 0.5f, r * 0.46f * (1f - phase * 0.55f), phase, i);     // head down, tip up
                }
                break;
            }
        }
    }

    /// <summary>Count pieces on ballistic paths, drawn at the medium's own step: out along
    /// their cone with drag, then gravity. With <see cref="VfxFlags.Converge"/> they run the
    /// film backwards — in toward the point.</summary>
    protected virtual void DrawBits(VfxMark m)
    {
        ref VfxMarkState s = ref m.S;
        int n = Math.Min(s.Count, MaxPieces);
        float seconds = m.Time, t = m.T;
        bool converge = (s.Flags & VfxFlags.Converge) != 0;
        if (converge) { seconds = Mathf.Max(0f, s.Life - seconds); t = 1f - t; }
        float baseAngle = s.Dir.LengthSquared() > 0.01f ? s.Dir.Angle() : -Mathf.Pi * 0.5f;
        float drag = (1f - Mathf.Exp(-3.2f * seconds)) / 3.2f;
        for (int i = 0; i < n; i++)
        {
            float a = baseAngle + Rs(s.Seed, i) * s.Spread * 0.5f;
            float speed = s.Speed * (0.45f + 0.65f * R(s.Seed, i + 40));
            Vector2 p = Polar(a, speed * drag) + new Vector2(0f, 0.5f * s.Gravity * seconds * seconds);
            float size = s.Size * (0.65f + 0.7f * R(s.Seed, i + 80));
            float rot = a + (R(s.Seed, i + 120) - 0.5f) * 6f * seconds;
            Chip(m, Snap(p, Grid), rot, size, converge ? 1f - t : t, i);
        }
    }

    protected virtual void DrawGlint(VfxMark m)
    {
        float r = m.S.Size * (0.35f + 0.65f * Ease(Sat(m.T / 0.35f)));
        PathLine(new Vector2(-r, 0f), new Vector2(r, 0f), 6);
        Brush(m, r * 0.26f, 0f, 1f, m.T);
        PathLine(new Vector2(0f, -r), new Vector2(0f, r), 6);
        Brush(m, r * 0.26f, 0f, 1f, m.T);
    }

    /// <summary>An authored strip of square frames, sized to the effect's real size.</summary>
    protected virtual void DrawSheet(VfxMark m)
    {
        Texture2D sheet = m.S.Sheet;
        if (sheet == null) return;
        int frames = Math.Max(1, m.S.Frames);
        int frame = Math.Min(frames - 1, (int)(m.T * frames));
        float r = m.S.Size, u0 = frame / (float)frames, u1 = (frame + 1) / (float)frames;
        Use(sheet);
        int i = V(new Vector2(-r, -r), Colors.White, new Vector2(u0, 0f));
        V(new Vector2(r, -r), Colors.White, new Vector2(u1, 0f));
        V(new Vector2(r, r), Colors.White, new Vector2(u1, 1f));
        V(new Vector2(-r, r), Colors.White, new Vector2(u0, 1f));
        Tri(i, i + 1, i + 2);
        Tri(i, i + 2, i + 3);
    }

    private static void DrawWarm(VfxMark m)
    {
        Texture2D texture = VfxTextures.Get("soft_dot");
        var clear = new Color(1f, 1f, 1f, 0f);
        Triangle(Vector2.Zero, new Vector2(2f, 0f), new Vector2(0f, 2f), clear);
        Use(texture);
        int i = V(Vector2.Zero, clear, Vector2.Zero);
        V(new Vector2(2f, 0f), clear, Vector2.Right);
        V(new Vector2(0f, 2f), clear, Vector2.Down);
        Tri(i, i + 1, i + 2);
    }

    // ── helpers styles share ─────────────────────────────────────────────────────
    /// <summary>A bloom: a wobbling disc with a body colour, a rim colour from
    /// <paramref name="rimAt"/> of the radius outward, and a feather beyond it.</summary>
    protected static void Bloom(Vector2 c, float r, uint seed, float wobble, Color body, Color rim, float rimAt = 0.78f, float feather = 0.07f, int segs = 22, float sy = 1f)
    {
        Disc(c, r * rimAt, body, body, segs, wobble, seed, sy);
        Band(c, r * rimAt, r, body, rim, segs, wobble, seed, sy);
        if (feather > 0f) Band(c, r, r * (1f + feather), rim, A(rim, 0f), segs, wobble, seed, sy);
    }

    /// <summary>Short rays round a point, between two radii.</summary>
    protected static void Rays(Vector2 c, float r0, float r1, int count, uint seed, float width, Color colour, float jitter = 0.35f)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (i + Rs(seed, i + 300) * jitter) * Tau / count;
            float far = Mathf.Lerp(r0, r1, 0.55f + 0.45f * R(seed, i + 340));
            Line(c + Polar(a, r0), c + Polar(a, far), width, width * 0.35f, colour, colour);
        }
    }

    /// <summary>White across a particle's life at these alphas.</summary>
    protected static Gradient Ramp(params (float At, float Alpha)[] stops)
    {
        var offsets = new float[stops.Length];
        var colours = new Color[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            offsets[i] = stops[i].At;
            colours[i] = new Color(1f, 1f, 1f, stops[i].Alpha);
        }
        return new Gradient { Offsets = offsets, Colors = colours };
    }

    protected static Gradient Ramp(params (float At, Color Colour)[] stops)
    {
        var offsets = new float[stops.Length];
        var colours = new Color[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            offsets[i] = stops[i].At;
            colours[i] = stops[i].Colour;
        }
        return new Gradient { Offsets = offsets, Colors = colours };
    }

    /// <summary>A particle's scale across its life, from one multiple to another.</summary>
    protected static Curve Grow(float from, float to)
    {
        var curve = new Curve { MinValue = 0f, MaxValue = Mathf.Max(1f, Mathf.Max(from, to)) };
        curve.AddPoint(new Vector2(0f, from));
        curve.AddPoint(new Vector2(1f, to));
        return curve;
    }

    public static Color Hex(string hex) => new(hex);
    public static Color Hex(string hex, float alpha) { var c = new Color(hex); c.A = alpha; return c; }
}

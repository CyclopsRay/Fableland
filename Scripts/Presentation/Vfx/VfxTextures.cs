namespace Fableland.Vfx;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The small textures effects are made of — generated in code, once, at most 64 px a side
/// (Docs/VFX.gdd §8). An authored file with the same id replaces the generated one:
/// <c>res://Assets/Vfx/&lt;id&gt;.png</c> (<c>watercolour/drop</c> →
/// <c>Assets/Vfx/watercolour/drop.png</c>) — keep the name and the footprint, as a tile does
/// (RealmStyleBible §1). Whole effects are replaced the same way by an authored sheet
/// (<see cref="Sheet"/>).
/// </summary>
public static class VfxTextures
{
    public const string Root = "res://Assets/Vfx/";
    public const int MaxSize = 64;

    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static readonly Dictionary<(string Style, string Id), Texture2D> Sheets = new();
    private static readonly Dictionary<string, Texture2D> Overrides = new();

    /// <summary>Bytes of generated texture memory (RGBA8) — for the budget line.</summary>
    public static int GeneratedBytes { get; private set; }
    public static int GeneratedCount { get; private set; }
    public static IEnumerable<string> Ids => Cache.Keys;

    /// <summary>Every generated id, for warm-up and the check.</summary>
    public static readonly string[] All =
    {
        "soft_dot", "watercolour/drop", "watercolour/blob", "watercolour/petal", "ink/dot", "ink/leaf",
        "ink/drybrush", "realism/spark", "realism/glow", "realism/smoke", "gongbi/bubble", "linocut/speck",
        "scratch/tick", "enamel/dust", "poster/star", "realism/chip",
    };

    /// <summary>The texture for an id: an override registered in code, else an authored file,
    /// else the generator. Null for an unknown id (a plain square).</summary>
    public static Texture2D Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Cache.TryGetValue(id, out Texture2D texture)) return texture;
        if (Overrides.TryGetValue(id, out texture)) return Cache[id] = texture;
        string file = Root + id + ".png";
        if (ResourceLoader.Exists(file)) texture = ResourceLoader.Load<Texture2D>(file);
        texture ??= Generate(id);
        Cache[id] = texture;
        return texture;
    }

    /// <summary>Replace a texture from code (tools, the check). Clears what was cached.</summary>
    public static void Override(string id, Texture2D texture)
    {
        if (texture == null) Overrides.Remove(id);
        else Overrides[id] = texture;
        Cache.Remove(id);
    }

    /// <summary>
    /// An authored sheet that replaces a whole procedural effect, or null:
    /// <c>res://Assets/Vfx/&lt;style&gt;/&lt;effect id&gt;.png</c>, a horizontal strip of square
    /// frames. Looked up once per (style, id); a miss is remembered too.
    /// </summary>
    public static Texture2D Sheet(string style, string id)
    {
        if (id == null || style == null) return null;
        if (Sheets.TryGetValue((style, id), out Texture2D sheet)) return sheet;
        string file = Root + style + "/" + id + ".png";
        sheet = ResourceLoader.Exists(file) ? ResourceLoader.Load<Texture2D>(file) : null;
        Sheets[(style, id)] = sheet;
        return sheet;
    }

    /// <summary>Register (or, with null, remove) a sheet from code.</summary>
    public static void SetSheet(string style, string id, Texture2D sheet) => Sheets[(style, id)] = sheet;

    public static void ForgetSheets() => Sheets.Clear();

    // ── generators ───────────────────────────────────────────────────────────────
    private static Texture2D Generate(string id)
    {
        switch (id)
        {
            case "soft_dot":
                return Make(32, 32, (u, v) => White(Soft(1f - Radius(u, v), 0f, 1f)));
            case "watercolour/drop":
                // A teardrop, round head down (a particle's +Y is its velocity), wet rim.
                return Make(24, 32, (u, v) =>
                {
                    float y = v * 2f - 1f, x = u * 2f - 1f;
                    float w = y > 0.25f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Sq((y - 0.25f) / 0.75f))) : Mathf.Max(0f, (y + 1f) / 1.25f);
                    float d = Mathf.Abs(x) / Mathf.Max(0.02f, w * 0.82f);
                    float alpha = Soft(1f - d, 0f, 0.22f);
                    float rim = Soft(d, 0.55f, 0.95f);
                    return new Color(1f - rim * 0.28f, 1f - rim * 0.28f, 1f - rim * 0.28f, alpha * (0.72f + 0.28f * rim));
                });
            case "watercolour/blob":
                // A bloom with a darker wet edge and a little granulation.
                return Make(48, 48, (u, v) =>
                {
                    float a = Mathf.Atan2(v - 0.5f, u - 0.5f);
                    float r = Radius(u, v) / (0.86f + 0.10f * Mathf.Sin(a * 3f + 1.3f) + 0.05f * Mathf.Sin(a * 5f));
                    float alpha = Soft(1f - r, 0f, 0.16f);
                    float rim = Soft(r, 0.62f, 0.96f);
                    float grain = 0.92f + 0.08f * Noise(u * 9f, v * 9f, 11);
                    float tone = (1f - rim * 0.30f) * grain;
                    return new Color(tone, tone, tone, alpha * (0.55f + 0.45f * rim));
                });
            case "watercolour/petal":
                return Make(16, 24, (u, v) =>
                {
                    float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;
                    float w = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)) * (0.55f + 0.25f * y);
                    float d = Mathf.Abs(x) / Mathf.Max(0.02f, w);
                    float rim = Soft(d, 0.5f, 1f);
                    return new Color(1f - rim * 0.2f, 1f - rim * 0.2f, 1f - rim * 0.2f, Soft(1f - d, 0f, 0.3f) * 0.85f);
                });
            case "ink/dot":
                return Make(16, 16, (u, v) =>
                {
                    float a = Mathf.Atan2(v - 0.5f, u - 0.5f);
                    float r = Radius(u, v) / (0.8f + 0.16f * Mathf.Sin(a * 3f + 0.7f));
                    return White(Soft(1f - r, 0f, 0.35f));
                });
            case "ink/leaf":
                // One dab of the brush: pressed, then lifted to a point.
                return Make(32, 12, (u, v) =>
                {
                    float w = Mathf.Pow(Mathf.Sin(Mathf.Min(1f, u * 1.25f) * Mathf.Pi * 0.5f), 0.7f) * (1f - Mathf.Pow(u, 3f));
                    float d = Mathf.Abs(v - 0.5f) * 2f / Mathf.Max(0.04f, w);
                    float dry = u > 0.7f ? Step(Noise(u * 3f, v * 14f, 5), (u - 0.7f) * 1.6f) : 1f;
                    return White(Soft(1f - d, 0f, 0.25f) * dry);
                });
            case "ink/drybrush":
                // u runs along the stroke: full ink, then flying white — bristle tracks with gaps.
                return Make(64, 16, (u, v) =>
                {
                    float bristle = Noise(u * 2.5f, v * 16f, 3) * 0.75f + Noise(u * 9f, v * 16f, 9) * 0.25f;
                    float threshold = Mathf.Max(0f, u - 0.45f) * 1.7f;
                    float edge = Soft(1f - Mathf.Abs(v - 0.5f) * 2f, 0f, 0.3f);
                    return White(Step(bristle, threshold) * edge);
                });
            case "realism/spark":
                // A streak, hot end down (leading).
                return Make(8, 32, (u, v) =>
                {
                    float across = Soft(1f - Mathf.Abs(u - 0.5f) * 2f, 0f, 0.9f);
                    float along = Mathf.Pow(v, 1.6f);
                    return White(across * along);
                });
            case "realism/glow":
                return Make(64, 64, (u, v) =>
                {
                    float r = Radius(u, v);
                    return White(Mathf.Pow(Mathf.Max(0f, 1f - r), 2.2f));
                });
            case "realism/smoke":
                return Make(64, 64, (u, v) =>
                {
                    float r = Radius(u, v);
                    float n = 0.55f + 0.45f * Noise(u * 4f, v * 4f, 21) * (0.6f + 0.4f * Noise(u * 9f, v * 9f, 4));
                    return White(Soft(1f - r, 0f, 0.7f) * n);
                });
            case "realism/chip":
                // A shard of something solid, lit from the upper left.
                return Make(12, 12, (u, v) =>
                {
                    bool inside = v > 0.12f + 0.5f * u && v < 0.95f - 0.15f * u && u > 0.08f + 0.2f * (1f - v) && u < 0.92f;
                    float lit = 0.62f + 0.38f * Sat(1.2f - u - v * 0.6f);
                    return inside ? new Color(lit, lit, lit, 1f) : default;
                });
            case "gongbi/bubble":
                // An even dark line round a graded fill, one pearl highlight. Pre-coloured:
                // a tint would dye the line.
                return Make(32, 32, (u, v) =>
                {
                    float r = Radius(u, v);
                    if (r > 1f) return default;
                    float line = Soft(r, 0.80f, 0.86f) * Soft(1f - r, 0f, 0.06f);
                    Color fill = new Color("7fb0f5").Lerp(new Color("eafbff"), Sat(0.9f - v * 0.9f + (0.5f - u) * 0.3f));
                    float glint = Soft(1f - new Vector2(u - 0.34f, v - 0.30f).Length() / 0.14f, 0f, 0.5f);
                    Color c = fill.Lerp(new Color("eafbff"), glint).Lerp(new Color("05060f"), line);
                    return new Color(c.R, c.G, c.B, Mathf.Max(line, 0.42f + 0.4f * glint) * Soft(1f - r, 0f, 0.05f));
                });
            case "linocut/speck":
                return Make(8, 8, (u, v) => White(Noise(u * 3f, v * 3f, 17) > 0.42f && Radius(u, v) < 0.95f ? 1f : 0f));
            case "scratch/tick":
                return Make(4, 16, (u, v) => White(Soft(1f - Mathf.Abs(u - 0.5f) * 2f, 0f, 0.6f) * Soft(1f - Mathf.Abs(v - 0.5f) * 2f, 0f, 0.4f)));
            case "enamel/dust":
                return Make(8, 8, (u, v) => White(Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) < 0.46f ? 1f : 0f));
            case "poster/star":
                return Make(16, 16, (u, v) =>
                {
                    float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
                    return White(Mathf.Pow(x, 0.55f) + Mathf.Pow(y, 0.55f) < 1f ? 1f : 0f);
                });
            default:
                return null;
        }
    }

    private static Texture2D Make(int w, int h, Func<float, float, Color> pixel)
    {
        w = Math.Min(w, MaxSize);
        h = Math.Min(h, MaxSize);
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = pixel((x + 0.5f) / w, (y + 0.5f) / h);
                int i = (y * w + x) * 4;
                data[i] = (byte)Mathf.RoundToInt(Sat(c.R) * 255f);
                data[i + 1] = (byte)Mathf.RoundToInt(Sat(c.G) * 255f);
                data[i + 2] = (byte)Mathf.RoundToInt(Sat(c.B) * 255f);
                data[i + 3] = (byte)Mathf.RoundToInt(Sat(c.A) * 255f);
            }
        }
        GeneratedBytes += data.Length;
        GeneratedCount++;
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, data));
    }

    private static Color White(float alpha) => new(1f, 1f, 1f, Sat(alpha));
    private static float Sat(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
    private static float Sq(float t) => t * t;
    /// <summary>Distance from the texture's centre, 1 at the edge.</summary>
    private static float Radius(float u, float v) => new Vector2(u - 0.5f, v - 0.5f).Length() * 2f;
    private static float Soft(float x, float lo, float hi) => VfxPen.Smooth((x - lo) / Mathf.Max(0.0001f, hi - lo));
    private static float Step(float x, float threshold) => x >= threshold ? 1f : 0f;

    /// <summary>Value noise in 0..1.</summary>
    private static float Noise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Cell(xi, yi, seed), b = Cell(xi + 1, yi, seed), c = Cell(xi, yi + 1, seed), d = Cell(xi + 1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Cell(int x, int y, int seed) =>
        (VfxPen.Hash((uint)(x * 374761393 + y * 668265263 + seed * 1442695041)) & 0xFFFF) / 65535f;
}

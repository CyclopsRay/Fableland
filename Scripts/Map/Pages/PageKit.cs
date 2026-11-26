using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Fableland.Map.Pages;

/// <summary>One map symbol of a page kit: a keyed, trimmed picture and where it stands.</summary>
public sealed class PageStamp
{
    public string Name;
    public Texture2D Texture;
    /// <summary>The symbol's size on the original 1024 px sheet — its size relative to the
    /// kit's other symbols, which were drawn "at a matching scale".</summary>
    public Vector2 SheetSize;
    /// <summary>Where it stands, 0..1 of the picture: the middle of its ground patch.</summary>
    public Vector2 Anchor = new(0.5f, 0.93f);
    /// <summary>Width of its ground patch as a share of the picture's width.</summary>
    public float Foot = 0.8f;
    /// <summary>True for a symbol that stands in water: it is placed near the sea.</summary>
    public bool Coast;
}

/// <summary>
/// A world's <b>page kit</b> — the art that carries a realm's identity on the torn-pages
/// overworld: one ground, nine symbols, one of them the seat (`Docs/Art/OverworldStyle.md`
/// §3). Built by `Tools/overworld_art/build_kits.py`; read from
/// `res://Assets/Sprites/Map/Overworld/&lt;ABBR&gt;/kit.json`. Loaded once and cached: the
/// textures get mipmaps here, because a page is usually seen smaller than it was painted and
/// `.import` settings are not in the repository.
/// </summary>
public sealed class PageKit
{
    public const string Root = "res://Assets/Sprites/Map/Overworld/";

    public string Abbr;
    public string Medium = "";
    public Texture2D Ground;
    public Color GroundMean = new(0.8f, 0.8f, 0.8f);
    public bool Dark;
    /// <summary>The ink roads are drawn in on this page, and the casing under it.</summary>
    public Color Ink = new(0.23f, 0.16f, 0.13f);
    public Color InkCase = new(1f, 0.97f, 0.90f);
    public int Seat;
    public readonly List<PageStamp> Stamps = new();

    private static readonly Dictionary<string, PageKit> Cache = new();

    /// <summary>The kit for a world, or null when it has none (the page is then drawn as
    /// plain tinted paper — a new world is playable before its kit exists).</summary>
    public static PageKit Load(string abbr)
    {
        if (string.IsNullOrEmpty(abbr)) return null;
        if (Cache.TryGetValue(abbr, out PageKit cached)) return cached;
        PageKit kit = Read(abbr);
        Cache[abbr] = kit;
        return kit;
    }

    private static PageKit Read(string abbr)
    {
        string folder = Root + abbr + "/";
        string path = folder + "kit.json";
        if (!FileAccess.FileExists(path)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
            JsonElement root = doc.RootElement;
            var kit = new PageKit
            {
                Abbr = abbr,
                Medium = Text(root, "medium", ""),
                Ground = PageTextures.Mipmapped(folder + Text(root, "ground", "ground.jpg")),
                Dark = Text(root, "tone", "light") == "dark",
                Seat = root.TryGetProperty("seat", out JsonElement seat) ? seat.GetInt32() : 0,
            };
            kit.GroundMean = Hex(Text(root, "groundMean", ""), kit.GroundMean);
            kit.Ink = Hex(Text(root, "ink", ""), kit.Dark ? new Color(0.96f, 0.92f, 0.82f) : kit.Ink);
            kit.InkCase = Hex(Text(root, "inkCase", ""), kit.Dark ? new Color(0.10f, 0.08f, 0.07f) : kit.InkCase);
            if (root.TryGetProperty("stamps", out JsonElement stamps))
            {
                foreach (JsonElement entry in stamps.EnumerateArray())
                {
                    Texture2D texture = PageTextures.Mipmapped(folder + Text(entry, "file", ""));
                    if (texture == null) continue;
                    var stamp = new PageStamp
                    {
                        Name = Text(entry, "name", ""),
                        Texture = texture,
                        SheetSize = new Vector2(Number(entry, "sheetW", texture.GetWidth() * 4f),
                            Number(entry, "sheetH", texture.GetHeight() * 4f)),
                        Foot = Number(entry, "foot", 0.8f),
                        Coast = Text(entry, "band", "any") == "coast",
                    };
                    if (entry.TryGetProperty("anchor", out JsonElement anchor) && anchor.GetArrayLength() == 2)
                        stamp.Anchor = new Vector2((float)anchor[0].GetDouble(), (float)anchor[1].GetDouble());
                    kit.Stamps.Add(stamp);
                }
            }
            kit.Seat = Mathf.Clamp(kit.Seat, 0, Mathf.Max(0, kit.Stamps.Count - 1));
            return kit;
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"PageKit: could not read {path}: {error.Message}");
            return null;
        }
    }

    private static string Text(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : fallback;

    private static float Number(JsonElement element, string name, float fallback) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? (float)value.GetDouble() : fallback;

    private static Color Hex(string text, Color fallback)
    {
        if (string.IsNullOrEmpty(text)) return fallback;
        try { return new Color(text); } catch { return fallback; }
    }
}

/// <summary>
/// The furniture every island shares, whatever its worlds: desk paper, paper grain, char,
/// cut-paper waves, the compass rose, the pins. Drawn by code
/// (`Tools/overworld_art/shared.py`), identical on every run.
/// </summary>
public sealed class PageFurniture
{
    public Texture2D Desk, Paper, Char, Waves, Compass, Pins;
    public Rect2[] WaveCells = System.Array.Empty<Rect2>();
    public float PinCell = 80f;
    /// <summary>Radius of a pin's shape inside its cell, in texture pixels.</summary>
    public float PinRadius = 24f;

    public enum PinShape { Circle, Diamond, Triangle, Square, Ring }
    public enum PinLayer { Shadow, Sticker, Head, Gloss }

    public Rect2 PinRegion(PinShape shape, PinLayer layer) =>
        new((int)shape * PinCell, (int)layer * PinCell, PinCell, PinCell);

    private static PageFurniture _shared;

    public static PageFurniture Shared => _shared ??= Read();

    private static PageFurniture Read()
    {
        string folder = PageKit.Root + "_shared/";
        var furniture = new PageFurniture
        {
            Desk = PageTextures.Mipmapped(folder + "desk.jpg"),
            Paper = PageTextures.Mipmapped(folder + "paper.png"),
            Char = PageTextures.Mipmapped(folder + "char.jpg"),
            Waves = PageTextures.Mipmapped(folder + "waves.png"),
            Compass = PageTextures.Mipmapped(folder + "compass.png"),
            Pins = PageTextures.Mipmapped(folder + "pins.png"),
        };
        string path = folder + "shared.json";
        if (!FileAccess.FileExists(path)) return furniture;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("waves", out JsonElement waves) && waves.TryGetProperty("cells", out JsonElement cells))
            {
                var list = new List<Rect2>();
                foreach (JsonElement cell in cells.EnumerateArray())
                    list.Add(new Rect2((float)cell[0].GetDouble(), (float)cell[1].GetDouble(),
                        (float)cell[2].GetDouble(), (float)cell[3].GetDouble()));
                furniture.WaveCells = list.ToArray();
            }
            if (root.TryGetProperty("pins", out JsonElement pins))
            {
                if (pins.TryGetProperty("cell", out JsonElement cell)) furniture.PinCell = (float)cell.GetDouble();
                if (pins.TryGetProperty("radius", out JsonElement radius)) furniture.PinRadius = (float)radius.GetDouble();
            }
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"PageFurniture: could not read {path}: {error.Message}");
        }
        return furniture;
    }
}

public static class PageTextures
{
    /// <summary>Load a texture and give it mipmaps. Art here is drawn at a third to a tenth
    /// of its painted size at the overview zoom; without mipmaps fine marks (scratchboard
    /// lines, pixel art, hatching) shimmer and alias. Returns null if the file is missing.</summary>
    public static Texture2D Mipmapped(string path)
    {
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) return null;
        var texture = ResourceLoader.Load<Texture2D>(path);
        if (texture == null) return null;
        Image image = texture.GetImage();
        if (image == null || image.IsEmpty()) return texture;
        if (image.IsCompressed() && image.Decompress() != Error.Ok) return texture;
        if (!image.HasMipmaps() && image.GenerateMipmaps() != Error.Ok) return texture;
        return ImageTexture.CreateFromImage(image);
    }
}

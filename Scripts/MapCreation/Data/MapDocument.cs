namespace Fableland.MapCreation.Data;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// GDD §1, §7.8, §8 — the serialized map document root. `Id` is a GUID minted
/// once at creation and is the file's identity forever (GDD §7.8: "File
/// identity is a GUID" — renames only touch `Name`, fixing the v0.5.x
/// name-derived-filename overwrite bug).
///
/// STRUCTURAL RULE (GDD §11.2): every serialized member is a public property
/// with { get; set; } — System.Text.Json ignores public fields (root cause of
/// the v0.5.x "every save was {}" bug).
/// </summary>
public sealed class MapDocument
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Untitled";

    /// <summary>Free string naming the overworld world this map belongs to; empty if none.</summary>
    public string World { get; set; } = "";

    public string CreatedUtc { get; set; } = "";
    public string ModifiedUtc { get; set; } = "";

    public CanvasData Canvas { get; set; } = new();

    /// <summary>Back-to-front draw order: farview sublayers…, battlefield, closeview
    /// sublayers…. List order IS draw order (T00 rule 3: explicit order, no z-index).
    /// Canvas is NOT a layer.</summary>
    public List<MapLayerData> Layers { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; }

    /// <summary>Fresh document: new GUID identity, default canvas, and a ready-made layer
    /// stack (GDD §1 rework) so the user doesn't have to hand-build parallax depth. List
    /// order is back-to-front draw order: farthest farview first … battlefield … closeview
    /// last. The canvas (never-moving backdrop) is <see cref="Canvas"/>, not a layer.
    /// Parallax picked so distant layers barely drift and the near decoration hugs the
    /// battlefield; farthest two loop so a narrow mountain strip tiles across a wide map.</summary>
    public static MapDocument CreateNew(string name)
    {
        string nowIso = DateTime.UtcNow.ToString("o");
        return new MapDocument
        {
            Version = 1,
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrEmpty(name) ? "Untitled" : name,
            World = "",
            CreatedUtc = nowIso,
            ModifiedUtc = nowIso,
            Canvas = new CanvasData(),
            Layers = new List<MapLayerData>
            {
                MapLayerData.CreateFarview("Very Far Mountains", 0.15f, 0.15f, loop: true),
                MapLayerData.CreateFarview("Far Mountains", 0.30f, 0.30f, loop: true),
                MapLayerData.CreateFarview("Backdrop Scene", 0.55f, 0.55f),
                MapLayerData.CreateFarview("Near Decoration", 0.85f, 0.85f),
                MapLayerData.CreateBattlefield(),
                MapLayerData.CreateCloseview("Foreground"),
            },
        };
    }
}

/// <summary>
/// GDD §1.1 — the backdrop; not a layer. `Type = "solidColor"` in v1;
/// `Type = "mode"` + `ModeId` reserved for future animated weather canvases
/// (Docs/IDEAS.md §6).
/// </summary>
public sealed class CanvasData
{
    public string Type { get; set; } = "solidColor";
    public string Color { get; set; } = "#87CEEB";
    public string ModeId { get; set; } = null;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; }
}

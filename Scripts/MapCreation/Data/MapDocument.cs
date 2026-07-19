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
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Untitled";

    /// <summary>
    /// Legacy single-world field from v1. It remains readable so existing user maps do not
    /// disappear; v2 authors use <see cref="Worlds"/> instead. An empty value means all worlds.
    /// </summary>
    public string World { get; set; } = "";

    /// <summary>
    /// Overworld world ids this combat map can serve (world name or abbreviation). Empty means
    /// all worlds. Multiple entries are intentionally ORed: a seashore may later serve several
    /// coastal realms without a duplicate map file.
    /// </summary>
    public List<string> Worlds { get; set; } = new();

    /// <summary>Combat-node hardship levels this map can serve (1..6). Empty means all levels.</summary>
    public List<int> HardshipLevels { get; set; } = new();

    /// <summary>
    /// Combat goals this map can serve. Canonical values are claim, protect, destroy, and
    /// slaughter. New maps default to claim; the runtime maps claim to the Collection mission.
    /// </summary>
    public List<string> Goals { get; set; } = new() { CombatMapGoals.Claim };

    /// <summary>
    /// Terrain label used by combat-map selection. Sea-level is the default; high-ground and
    /// low-ground are assigned by the overworld's seeded altitude field when applicable. A
    /// literal "*" matches every terrain band, for maps authored to a realm/level rather than
    /// a specific altitude.
    /// </summary>
    public string Terrain { get; set; } = CombatMapTerrain.SeaLevel;

    /// <summary>Optional per-level foe mix. A Level of 0 supplies this map's default mix.</summary>
    public List<FoeComposition> FoeCompositions { get; set; } = new();

    /// <summary>
    /// Optional cell-row eligibility rules for authored foe spawners. Null values mean that foe
    /// type may use every spawn marker; values are compared to the marker's authored grid Y.
    /// </summary>
    public FoeSpawnRules FoeSpawnRules { get; set; } = new();

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
            Version = CurrentVersion,
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrEmpty(name) ? "Untitled" : name,
            World = "",
            Worlds = new List<string>(),
            HardshipLevels = new List<int>(),
            Goals = new List<string> { CombatMapGoals.Claim },
            Terrain = CombatMapTerrain.SeaLevel,
            FoeCompositions = new List<FoeComposition>(),
            FoeSpawnRules = new FoeSpawnRules(),
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

/// <summary>String constants deliberately kept in Map Creation data so authored map JSON never
/// depends on the Run/orchestration layer's mission enum.</summary>
public static class CombatMapGoals
{
    public const string Claim = "claim";
    public const string Protect = "protect";
    public const string Destroy = "destroy";
    public const string Slaughter = "slaughter";
}

/// <summary>Canonical combat-map terrain labels. These are map-selection data, not physics.</summary>
public static class CombatMapTerrain
{
    public const string SeaLevel = "sea-level";
    public const string High = "high-ground";
    public const string Lowground = "low-ground";
}

/// <summary>Weighted crab/seagull composition for one hardship level (0 = map default).</summary>
public sealed class FoeComposition
{
    public int Level { get; set; }
    public int CrabWeight { get; set; } = 60;
    public int SeagullWeight { get; set; } = 40;
}

/// <summary>Grid-Y eligibility rules for map-authored foe spawn tiles.</summary>
public sealed class FoeSpawnRules
{
    public int? CrabMaxCellY { get; set; }
    public int? SeagullMinCellY { get; set; }
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

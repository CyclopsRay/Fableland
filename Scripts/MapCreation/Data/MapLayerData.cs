namespace Fableland.MapCreation.Data;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// GDD §1.2 — the unified layer schema. A farview sublayer, the battlefield, and
/// a closeview sublayer are all this same shape; `Role` is what distinguishes
/// them (one paint pipeline, one renderer, one serializer per GDD §1).
///
/// STRUCTURAL RULE (GDD §11.2): every serialized member is a public property
/// with { get; set; } — System.Text.Json ignores public fields (root cause of
/// the v0.5.x "every save was {}" bug).
/// </summary>
public sealed class MapLayerData
{
    public const string RoleFarview = "farview";
    public const string RoleBattlefield = "battlefield";
    public const string RoleCloseview = "closeview";

    public string Role { get; set; } = RoleBattlefield;
    public string Name { get; set; } = "Layer";

    /// <summary>1.0 = moves with the battlefield. Farview default 0.5/0.5, closeview 1.2/1.2,
    /// battlefield locked to 1/1 (GDD §1.2).</summary>
    public float ParallaxX { get; set; } = 1f;
    public float ParallaxY { get; set; } = 1f;

    /// <summary>Horizontally tiles the layer (clouds, hills). Loop layers can never collide
    /// (GDD §3 rule 2).</summary>
    public bool Loop { get; set; }

    /// <summary>Only legal when parallax is exactly (1.0, 1.0) — camera-dependent physics
    /// otherwise breaks determinism (GDD §3). The editor enforces this, not this data class.</summary>
    public bool Collision { get; set; }

    /// <summary>Hex color modulate, or null for none. Closeview defaults dark (#404050).</summary>
    public string Tint { get; set; }
    public float Opacity { get; set; } = 1f;

    /// <summary>Visual-only sway (GDD §4). Forced 0 on closeview ("steady") and on the
    /// battlefield (gameplay readability) — enforced by whoever constructs those layers.</summary>
    public float SwayAmplitudePx { get; set; }
    public float SwayPeriodSec { get; set; } = 4.0f;

    /// <summary>Drifting clouds/fog independent of camera, px/s.</summary>
    public float AutoScrollX { get; set; }
    public float AutoScrollY { get; set; }

    /// <summary>Per-layer grid size; battlefield default 64x36, max 512x256 (GDD §1.2).</summary>
    public int GridW { get; set; } = 64;
    public int GridH { get; set; } = 36;

    /// <summary>Sparse, anchor-only placed tiles (GDD §2.1).</summary>
    public List<PlacedTile> Tiles { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; }

    public static MapLayerData CreateBattlefield(int gridW = 64, int gridH = 36) => new()
    {
        Role = RoleBattlefield,
        Name = "Battlefield",
        ParallaxX = 1f,
        ParallaxY = 1f,
        Loop = false,
        Collision = true,
        SwayAmplitudePx = 0f,
        GridW = gridW,
        GridH = gridH,
    };

    public static MapLayerData CreateFarview(string name = "Farview") => new()
    {
        Role = RoleFarview,
        Name = name,
        ParallaxX = 0.5f,
        ParallaxY = 0.5f,
    };

    public static MapLayerData CreateCloseview(string name = "Closeview") => new()
    {
        Role = RoleCloseview,
        Name = name,
        ParallaxX = 1.2f,
        ParallaxY = 1.2f,
        Tint = "#404050",
        SwayAmplitudePx = 0f,
    };
}

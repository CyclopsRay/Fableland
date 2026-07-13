namespace Fableland.MapCreation.Data;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// GDD §2.1/§2.3 — a single placed tile on a layer, stored sparse and
/// anchor-only: (X, Y) is the tile's top-left footprint cell. Occupancy of the
/// rest of a multi-cell footprint is derived at load/edit time (see
/// LayerOccupancy), never stored per-cell here.
///
/// STRUCTURAL RULE (GDD §11.2): every serialized member is a public property
/// with { get; set; } — System.Text.Json silently ignores public fields, which
/// was the root cause of the v0.5.x "every save was {}" bug.
/// </summary>
public sealed class PlacedTile
{
    public string DefId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public bool FlipX { get; set; }

    /// <summary>Per-instance overrides (e.g. a spawn tile's foe-table id). Null when there are
    /// none so most tiles don't carry an empty dictionary in the saved file.</summary>
    public Dictionary<string, string> Props { get; set; }

    /// <summary>Unknown fields from a newer save format are preserved on rewrite (T20 §5
    /// forward-compat), never silently dropped.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; }
}

namespace Fableland.Data;

/// <summary>
/// Designer-tunable generation values for the v0.9 realm island. Keep this Godot-free so map
/// generation invariants can be tested without a scene.
/// </summary>
public static class MapGenTable
{
    // MapGDD §2 — one organic island, five river-cut realms.
    public const int RealmCount = 5;
    public const int IslandCoastSamples = 180;
    public const int RealmOuterCoastSamples = 28;
    public const int RiverSamples = 14;
    public const float VoidAreaFraction = 1f / 30f;
    public const float CoastIrregularity = 34f;
    public const float RiverMinHalfWidthDeg = 1.9f;
    public const float RiverMaxHalfWidthDeg = 4.2f;

    // MapGDD §4 — LV1 and LV2 totals match while both retain A/B sublevels.
    public const int SublevelMinCities = 3;
    public const int SublevelMaxCities = 4;

    // MapGDD §5–6 — local kingdom roads and function nodes.
    public const int LocalRoadLoopsMin = 1;
    public const int LocalRoadLoopsMax = 3;
    public const int TransportationHubsPerRealmMin = 3;
    public const int TransportationHubsPerRealmMax = 5;
    public const int EventNodesPerRealmMin = 4;
    public const int EventNodesPerRealmMax = 6;
    public const int HubClusterCities = 3;
    public const float RealmCapitalVoidBuffer = 82f;

}

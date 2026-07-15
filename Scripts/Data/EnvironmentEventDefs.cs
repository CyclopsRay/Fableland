using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>
/// Pure, designer-owned data for an arena-local environmental event. Presentation
/// colours remain hex strings here so this domain-data table has no Godot dependency;
/// <c>ArenaEnvironmentController</c> creates Godot colours at its presentation edge.
/// </summary>
public sealed class EnvironmentEventDef
{
    public string Id { get; init; }

    public float WarningDurationSec { get; init; }
    public float RestoreDurationSec { get; init; }
    public float CooldownSec { get; init; }

    public string StormCanvasHex { get; init; }
    public string StormWorldTintHex { get; init; }
    public float ShakeRampDurationSec { get; init; }
    public float SustainedShakeTrauma { get; init; }

    // Ambient gusts are presentation-only in the first vertical slice.
    public float AmbientGustMinIntervalSec { get; init; }
    public float AmbientGustMaxIntervalSec { get; init; }
    public float AmbientGustDurationSec { get; init; }
    public float AmbientGustVisualStrength { get; init; }

    // The active tsunami delivers a horizontal delta-v pulse, rather than a
    // permanently accumulating force. This matches the external-velocity model:
    // each pulse decays through the target's existing ExternalDamping.
    public float StormWindVisualStrength { get; init; }
    public float StormWindPulseMps { get; init; }
    public float StormWindPulseIntervalSec { get; init; }
}

/// <summary>
/// Registry for arena event definitions (Gameplay.gdd §A.6). This is deliberately
/// a read-only pure-C# lookup: adding lava or meteor night means adding a definition
/// and its event behaviour, never extending the tsunami trigger with another switch.
/// </summary>
public static class EnvironmentEventDefs
{
    private static readonly Dictionary<string, EnvironmentEventDef> ById = new(StringComparer.Ordinal)
    {
        ["tsunami"] = new EnvironmentEventDef
        {
            Id = "tsunami",
            WarningDurationSec = 2f,
            RestoreDurationSec = 2f,
            CooldownSec = 4f,
            StormCanvasHex = "#595959",
            StormWorldTintHex = "#68727C",
            ShakeRampDurationSec = 3f,
            SustainedShakeTrauma = 0.75f,
            AmbientGustMinIntervalSec = 3f,
            AmbientGustMaxIntervalSec = 6f,
            AmbientGustDurationSec = 0.7f,
            AmbientGustVisualStrength = 0.18f,
            StormWindVisualStrength = 1f,
            StormWindPulseMps = 6f,
            StormWindPulseIntervalSec = 0.2f,
        },
    };

    public static bool TryGet(string id, out EnvironmentEventDef def) => ById.TryGetValue(id, out def);
}

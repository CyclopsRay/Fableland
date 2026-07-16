using System;
using System.Collections.Generic;

namespace Fableland.Data;

/// <summary>Defines how a protagonist's universal basic-attack magazine behaves.
/// Gameplay.gdd §A.2.1 owns the contract; this table owns its tunable values.</summary>
public enum AmmoRespawnMode { Full, Empty }

public sealed class CharacterAmmoDef
{
    public string CharacterId { get; init; }
    public int Capacity { get; init; }
    public int ReloadSize { get; init; }
    public float ReloadDelaySec { get; init; }
    public bool RepeatReloadWhileMissing { get; init; }
    public float BaseAttackIntervalSec { get; init; }
    public AmmoRespawnMode RespawnMode { get; init; }
}

/// <summary>Read-only protagonist balance registry. Keep this pure C# so its values
/// remain headless-testable and later move cleanly into designer-editable data.</summary>
public static class CharacterTable
{
    private static readonly Dictionary<string, CharacterAmmoDef> AmmoByCharacter = new(StringComparer.Ordinal)
    {
        ["Pomegraknight"] = new CharacterAmmoDef
        {
            CharacterId = "Pomegraknight",
            Capacity = 3,
            ReloadSize = 3,
            ReloadDelaySec = 0.75f,
            RepeatReloadWhileMissing = false,
            BaseAttackIntervalSec = 0.25f,
            RespawnMode = AmmoRespawnMode.Full,
        },
        ["PumpKing"] = new CharacterAmmoDef
        {
            CharacterId = "PumpKing",
            Capacity = 1,
            ReloadSize = 1,
            ReloadDelaySec = 1.5f,
            RepeatReloadWhileMissing = false,
            BaseAttackIntervalSec = 0f,
            RespawnMode = AmmoRespawnMode.Full,
        },
        ["Cleopastar"] = new CharacterAmmoDef
        {
            CharacterId = "Cleopastar",
            Capacity = 5,
            ReloadSize = 1,
            ReloadDelaySec = 1.5f,
            RepeatReloadWhileMissing = true,
            BaseAttackIntervalSec = 0.5f,
            RespawnMode = AmmoRespawnMode.Empty,
        },
    };

    public static bool TryGetAmmo(string characterId, out CharacterAmmoDef def) =>
        AmmoByCharacter.TryGetValue(characterId, out def);
}

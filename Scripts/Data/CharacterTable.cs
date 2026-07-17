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

/// <summary>Pixolotl's complete balance row (Pixolotl.gdd v1.1). Kept free of Godot
/// types so the temporal kit can be tested without a loaded scene.</summary>
public sealed class PixolotlDef
{
    public float BaseHp { get; init; }
    public float MoveSpeedMps { get; init; }
    public int MaxJumps { get; init; }
    public float FrameInterval { get; init; }
    public int FrameCapacity { get; init; }
    public float GhostFadeSeconds { get; init; }
    public float GhostForceFadeSeconds { get; init; }
    public float BubbleDamage { get; init; }
    public float BubbleSpeedMps { get; init; }
    public float BubbleDirectionalSeconds { get; init; }
    public float BubbleBrakeSeconds { get; init; }
    public float BubbleRiseSpeedMps { get; init; }
    public float BubbleLifetime { get; init; }
    public float ShiftCooldown { get; init; }
    public float ShiftFirstIntervals { get; init; }
    public int ShiftSlowJumps { get; init; }
    public float ShiftLaterIntervals { get; init; }
    public float ShiftDamage { get; init; }
    public float ShiftTrapSeconds { get; init; }
    public float ShiftRecovery { get; init; }
    public float ESkillCooldown { get; init; }
    public float ELocalTimeRate { get; init; }
    public int EFrameLimit { get; init; }
    public float EMaxRealSeconds { get; init; }
    public float[] BubbleAnglesDeg { get; init; }
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
        ["Pixolotl"] = new CharacterAmmoDef
        {
            CharacterId = "Pixolotl",
            Capacity = 3,
            ReloadSize = 3,
            ReloadDelaySec = 0.9f,
            RepeatReloadWhileMissing = false,
            BaseAttackIntervalSec = 0.3f,
            RespawnMode = AmmoRespawnMode.Full,
        },
    };

    public static readonly PixolotlDef Pixolotl = new()
    {
        BaseHp = 150f,
        MoveSpeedMps = 10f,
        MaxJumps = 1,
        FrameInterval = 0.3f,
        FrameCapacity = 8,
        GhostFadeSeconds = 3.2f,
        GhostForceFadeSeconds = 0.3f,
        BubbleDamage = 20f,
        BubbleSpeedMps = 6f,
        BubbleDirectionalSeconds = 1.5f,
        BubbleBrakeSeconds = 0.15f,
        BubbleRiseSpeedMps = 2f,
        BubbleLifetime = 10f,
        ShiftCooldown = 15f,
        ShiftFirstIntervals = 0.375f,
        ShiftSlowJumps = 3,
        ShiftLaterIntervals = 0.25f,
        ShiftDamage = 25f,
        ShiftTrapSeconds = 1f,
        ShiftRecovery = 0.25f,
        ESkillCooldown = 10f,
        ELocalTimeRate = 1.5f,
        EFrameLimit = 10,
        EMaxRealSeconds = 2.5f,
        BubbleAnglesDeg = new[] { 0f, 25f, 65f, 115f, 155f, 180f },
    };

    public static bool TryGetAmmo(string characterId, out CharacterAmmoDef def) =>
        AmmoByCharacter.TryGetValue(characterId, out def);
}

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

/// <summary>Sifu Pangda's two-stance balance row (Sifu Pangda.gdd v1.1).
/// All meters/seconds remain pure data; gameplay converts through Units.</summary>
public sealed class SifuPangdaDef
{
    public float BaseHp { get; init; }
    public float MoveSpeedMps { get; init; }
    public int MaxJumps { get; init; }
    public float ChiDamageConversion { get; init; }
    public float ChiShieldCap { get; init; }
    public float CombatInactivityGrace { get; init; }
    public float ChiDecayPerSecond { get; init; }
    public int FuhuPalmStacks { get; init; }
    public float FuhuPalmDamage { get; init; }
    public float FuhuPalmWidthM { get; init; }
    public float FuhuPalmHeightM { get; init; }
    public float FuhuPalmStun { get; init; }
    public float FuhuPalmWindup { get; init; }
    public float FuhuPalmRecovery { get; init; }
    public float FuhuPalmLungeDistanceM { get; init; }
    public float FuhuPalmLungeSeconds { get; init; }
    public float HeliDiveSpeedMps { get; init; }
    public float HeliDiveImpactWidthM { get; init; }
    public float HeliDiveImpactHeightM { get; init; }
    public float HeliDiveDamage { get; init; }
    public float HeliDiveKnockupMps { get; init; }
    public float HeliDiveStun { get; init; }
    public float HeliDiveRecovery { get; init; }
    public float FuhuShiftCooldown { get; init; }
    public float FuhuShiftWindup { get; init; }
    public float FuhuKickWidthM { get; init; }
    public float FuhuKickHeightM { get; init; }
    public float FuhuKickDamage { get; init; }
    public float FuhuKickKnockbackMps { get; init; }
    public float FuhuKickStun { get; init; }
    public float HeliShiftCooldown { get; init; }
    public float HeliDashSpeedMps { get; init; }
    public float HeliDashDistanceM { get; init; }
    public float HeliDashCheckBoxM { get; init; }
    public float HeliDashImpactBoxM { get; init; }
    public float HeliDashDamage { get; init; }
    public float HeliDashKnockbackMps { get; init; }
    public float FuhuECooldown { get; init; }
    public float DrinkDuration { get; init; }
    public float DrinkShieldAt { get; init; }
    public float DrinkMoveMultiplier { get; init; }
    public float HeliECooldown { get; init; }
    public float BottleSpeedMps { get; init; }
    public float BottleLifetime { get; init; }
    public float BottleRadiusM { get; init; }
    public float BottleDamage { get; init; }
    public float BottleTrappedSeconds { get; init; }
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
        ["Sifu Pangda"] = new CharacterAmmoDef
        {
            CharacterId = "Sifu Pangda",
            Capacity = 2,
            ReloadSize = 2,
            ReloadDelaySec = 1f,
            RepeatReloadWhileMissing = false,
            BaseAttackIntervalSec = 0.6f,
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
        EMaxRealSeconds = 3f,
        BubbleAnglesDeg = new[] { 0f, 25f, 65f, 115f, 155f, 180f },
    };

    public static readonly SifuPangdaDef SifuPangda = new()
    {
        BaseHp = 300f, MoveSpeedMps = 6.5f, MaxJumps = 1,
        ChiDamageConversion = 0.30f, ChiShieldCap = 60f,
        CombatInactivityGrace = 3f, ChiDecayPerSecond = 10f, FuhuPalmStacks = 2,
        FuhuPalmDamage = 40f, FuhuPalmWidthM = 4f, FuhuPalmHeightM = 3f,
        FuhuPalmStun = 0.5f, FuhuPalmWindup = 0.2f, FuhuPalmRecovery = 0.4f,
        FuhuPalmLungeDistanceM = 1f, FuhuPalmLungeSeconds = 0.2f,
        HeliDiveSpeedMps = 32f, HeliDiveImpactWidthM = 4f, HeliDiveImpactHeightM = 1f,
        HeliDiveDamage = 50f, HeliDiveKnockupMps = 10f, HeliDiveStun = 0.5f,
        HeliDiveRecovery = 0.5f,
        FuhuShiftCooldown = 8f, FuhuShiftWindup = 1f,
        FuhuKickWidthM = 6f, FuhuKickHeightM = 4f, FuhuKickDamage = 40f,
        FuhuKickKnockbackMps = 18f, FuhuKickStun = 1f,
        HeliShiftCooldown = 8f, HeliDashSpeedMps = 32f, HeliDashDistanceM = 16f,
        HeliDashCheckBoxM = 2f, HeliDashImpactBoxM = 2f, HeliDashDamage = 40f,
        HeliDashKnockbackMps = 16f,
        FuhuECooldown = 14f, DrinkDuration = 1.5f, DrinkShieldAt = 0.8f,
        DrinkMoveMultiplier = 0.4f,
        HeliECooldown = 14f, BottleSpeedMps = 20f, BottleLifetime = 2f, BottleRadiusM = 0.25f,
        BottleDamage = 25f, BottleTrappedSeconds = 1f,
    };

    public static bool TryGetAmmo(string characterId, out CharacterAmmoDef def) =>
        AmmoByCharacter.TryGetValue(characterId, out def);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Fableland.Run;

/// <summary>
/// Versioned, slot-based persistence for a run. This service owns file IO only; RunState
/// remains the single owner of the game data which is copied into and out of these DTOs.
/// Unknown JSON fields live in extension dictionaries so a newer save can survive a
/// downgrade-and-rewrite without silently losing data.
/// </summary>
public static class SaveGameService
{
    public const int SlotCount = 3;
    public const int CurrentVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static SaveSlotInfo GetSlotInfo(int slot)
    {
        if (!IsValidSlot(slot)) return new SaveSlotInfo(slot, false, "Invalid slot");
        if (!File.Exists(SlotPath(slot))) return new SaveSlotInfo(slot, false, "Empty");
        if (!TryRead(slot, out RunSaveData save, out string error))
            return new SaveSlotInfo(slot, true, "Unreadable save", error);

        string seed = string.IsNullOrWhiteSpace(save.Seed) ? "Unknown seed" : save.Seed;
        string day = save.InVoid ? "Day ???" : $"Day {Math.Max(1, save.Day)}";
        return new SaveSlotInfo(slot, true, $"{seed}  •  {day}");
    }

    public static bool TryRead(int slot, out RunSaveData save, out string error)
    {
        save = null;
        error = null;
        if (!IsValidSlot(slot)) { error = "Save slot is out of range."; return false; }

        string path = SlotPath(slot);
        if (!File.Exists(path)) { error = "This save slot is empty."; return false; }

        try
        {
            save = JsonSerializer.Deserialize<RunSaveData>(File.ReadAllText(path), JsonOptions);
            if (save == null) { error = "The save file contained no data."; return false; }
            return MigrateToCurrent(save, out error);
        }
        catch (Exception ex)
        {
            error = $"Could not read save slot {slot + 1}: {ex.Message}";
            GD.PushError($"[SaveGame] {error}");
            return false;
        }
    }

    public static bool TryWrite(int slot, RunSaveData save, out string error)
    {
        error = null;
        if (!IsValidSlot(slot)) { error = "Save slot is out of range."; return false; }
        if (save == null) { error = "There is no run to save."; return false; }

        string destination = SlotPath(slot);
        string temporary = destination + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            save.Version = CurrentVersion;
            File.WriteAllText(temporary, JsonSerializer.Serialize(save, JsonOptions));
            // Same-directory replace is atomic on the target desktop filesystems. The temporary
            // file is intentionally retained only if the OS rejects the final replacement.
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not write save slot {slot + 1}: {ex.Message}";
            GD.PushError($"[SaveGame] {error}");
            return false;
        }
    }

    public static void Delete(int slot)
    {
        if (!IsValidSlot(slot)) return;
        try
        {
            string destination = SlotPath(slot);
            if (File.Exists(destination)) File.Delete(destination);
            string temporary = destination + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        catch (Exception ex)
        {
            GD.PushError($"[SaveGame] Could not delete save slot {slot + 1}: {ex.Message}");
        }
    }

    /// <summary>Deep-copy a DTO at a persistence boundary. Rewind checkpoints deliberately do
    /// not nest another checkpoint, keeping save size bounded to two run states.</summary>
    public static RunSaveData CloneRunSave(RunSaveData source, bool includeLastDayCheckpoint = false)
    {
        if (source == null) return null;
        RunSaveData clone = JsonSerializer.Deserialize<RunSaveData>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions);
        if (clone != null && !includeLastDayCheckpoint) clone.LastDayCheckpoint = null;
        return clone;
    }

    /// <summary>In-memory regression hook for DTO shape/version/forward-field preservation.
    /// It never touches <c>user://</c>, so tests and debug tooling can call it safely.</summary>
    public static bool RoundTripSelfTest(out string error)
    {
        error = null;
        try
        {
            using JsonDocument futureJson = JsonDocument.Parse("{\"futureValue\":7}");
            var source = new RunSaveData
            {
                Seed = "SAVE1234",
                Day = 7,
                Stamina = 3,
                CurrentNodeId = "VK-1-A",
                ActiveBuild = new List<string> { "Pixolotl" },
                Map = new MapSaveData { DevouredNodeIds = new List<string> { "SL-1-A" } },
                Owned = new List<ProtagonistSaveData>
                {
                    new() { Id = "Pixolotl", HpRatio = 0.75f, BonusAtk = 10 },
                },
                ExtensionData = new Dictionary<string, JsonElement>
                {
                    ["futureValue"] = futureJson.RootElement.GetProperty("futureValue").Clone(),
                },
            };

            string json = JsonSerializer.Serialize(source, JsonOptions);
            RunSaveData restored = JsonSerializer.Deserialize<RunSaveData>(json, JsonOptions);
            if (restored?.Version != CurrentVersion || restored.Seed != source.Seed
                || restored.Owned?.Count != 1 || restored.Owned[0].BonusAtk != 10
                || restored.Map?.DevouredNodeIds?.Count != 1
                || restored.ExtensionData == null || !restored.ExtensionData.ContainsKey("futureValue"))
            {
                error = "Save DTO round-trip lost a durable field.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool MigrateToCurrent(RunSaveData save, out string error)
    {
        error = null;
        switch (save.Version)
        {
            case CurrentVersion:
                return true;
            case 2:
                MigrateV2ToV3(save);
                return true;
            case 1:
                MigrateV1ToV2(save);
                MigrateV2ToV3(save);
                return true;
            case 0:
                MigrateV0ToV1(save);
                MigrateV1ToV2(save);
                MigrateV2ToV3(save);
                return true;
            default:
                error = $"Save version {save.Version} is newer than this game supports.";
                GD.PushError($"[SaveGame] {error}");
                return false;
        }
    }

    /// <summary>Migration placeholder for pre-versioned prototype data. Keep later migrations
    /// explicit and chained here instead of scattering compatibility checks through RunState.</summary>
    private static void MigrateV0ToV1(RunSaveData save)
    {
        save.Version = 1;
        save.Map ??= new MapSaveData();
        save.Owned ??= new List<ProtagonistSaveData>();
        save.ActiveBuild ??= new List<string>();
        save.Items ??= new List<ItemSaveData>();
    }

    /// <summary>v0.10.0 adds concrete item identity plus the combat cooldown axis. Missing
    /// values intentionally remain empty/zero here; RunState assigns stable legacy ids while
    /// hydrating because it owns every inventory location.</summary>
    private static void MigrateV1ToV2(RunSaveData save)
    {
        save.Version = 2;
        save.Items ??= new List<ItemSaveData>();
        save.Owned ??= new List<ProtagonistSaveData>();
        foreach (ProtagonistSaveData protagonist in save.Owned)
            if (protagonist != null)
                protagonist.HeldItemSecondCooldownRemaining = Math.Max(0f, protagonist.HeldItemSecondCooldownRemaining);
    }

    /// <summary>v0.10.0 stores the most recently completed day as a bounded rewind checkpoint
    /// for TwistedReality's boss-loss possession.</summary>
    private static void MigrateV2ToV3(RunSaveData save)
    {
        save.Version = 3;
        save.LastDayCheckpoint = null;
    }

    private static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;
    private static string SlotPath(int slot) => ProjectSettings.GlobalizePath($"user://saves/slot_{slot + 1}.json");
}

/// <summary>Small title-menu projection of a save slot. It deliberately exposes no mutable run data.</summary>
public readonly record struct SaveSlotInfo(int Slot, bool Occupied, string Summary, string Error = null);

// The save DTOs are deliberately Godot-free. Add durable map mutations to MapSaveData rather
// than serializing MapGraph itself; the seeded graph remains derived data.
public sealed class RunSaveData
{
    public int Version { get; set; } = SaveGameService.CurrentVersion;
    public string Seed { get; set; } = "";
    public int Day { get; set; } = 1;
    public bool InVoid { get; set; }
    public int Stamina { get; set; }
    public string CurrentNodeId { get; set; } = "";
    public string PreviousNodeId { get; set; } = "";
    /// <summary>Set only by Save & Quit in an unfinished combat. The mission is rebuilt from its
    /// deterministic node contract on Continue; no live foe/projectile state is serialized.</summary>
    public string ResumeBattleNodeId { get; set; } = "";
    public MapSaveData Map { get; set; } = new();
    public List<string> VisitedNodeIds { get; set; } = new();
    public List<string> CompletedNodeIds { get; set; } = new();
    public List<string> MundaneShelterIds { get; set; } = new();
    public List<string> ResolvedEventIds { get; set; } = new();
    public List<ProtagonistSaveData> Owned { get; set; } = new();
    public List<string> ActiveBuild { get; set; } = new();
    public int ActiveProtagonistIndex { get; set; }
    public int WonderCores { get; set; }
    public List<ItemSaveData> Items { get; set; } = new();
    public int NodesTraversed { get; set; }
    public int GoalsSucceeded { get; set; }
    public int ProtagonistsCollected { get; set; }
    public int ItemsCollected { get; set; }
    public List<string> WorldsVisited { get; set; } = new();
    /// <summary>Start-of-current-day state, stored without recursively embedding itself.</summary>
    public RunSaveData LastDayCheckpoint { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}

/// <summary>Durable, non-derived changes to the map. More map mutation fields belong here;
/// extension data preserves future additions while older builds rewrite a run.</summary>
public sealed class MapSaveData
{
    public List<string> DevouredNodeIds { get; set; } = new();
    public List<RealityBridgeSaveData> RealityBridges { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}

public sealed class RealityBridgeSaveData
{
    public string NodeAId { get; set; } = "";
    public string NodeBId { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}

public sealed class ProtagonistSaveData
{
    public string Id { get; set; } = "";
    public float HpRatio { get; set; } = 1f;
    public int BonusAtk { get; set; }
    public int BonusDef { get; set; }
    public int MaxHpPercentPoints { get; set; }
    public string HeldItemDefId { get; set; }
    public string HeldItemInstanceId { get; set; }
    public int HeldItemDayCooldownRemaining { get; set; }
    public float HeldItemSecondCooldownRemaining { get; set; }
    public bool HeldItemFromBackpack { get; set; }
    public float ShiftCdRemaining { get; set; }
    public float ESkillCdRemaining { get; set; }
    public float ShiftAltCdRemaining { get; set; }
    public float ESkillAltCdRemaining { get; set; }
    public bool AmmoInitialized { get; set; }
    public int AmmoCurrent { get; set; }
    public float AmmoAttackCooldownRemaining { get; set; }
    public bool AmmoReloadActive { get; set; }
    public float AmmoReloadRemaining { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}

public sealed class ItemSaveData
{
    public string InstanceId { get; set; } = "";
    public string DefId { get; set; } = "";
    public int DayCooldownRemaining { get; set; }
    public float SecondCooldownRemaining { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}

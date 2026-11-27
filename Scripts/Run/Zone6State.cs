using System;
using System.Collections.Generic;
using Fableland.Data;

namespace Fableland.Run;

/// <summary>Wins and losses at one zone-6 node.</summary>
public sealed class Zone6NodeRecord
{
    public int Wins;
    public int Losses;
}

/// <summary>
/// A run's progress through zone 6 (Docs/Zone6.gdd §5): which variant this run's zone 6 is, the
/// Void Frontier's gravity, Avīci's seals on you and in Māra, and wins and losses per node.
/// Pure C#; one owner — <see cref="RunState"/> — writes it, at mission resolution only. Saved
/// with the run (<see cref="Zone6SaveData"/>); a save from before v0.12.0 has none and starts
/// empty, its variant derived from its seed.
/// </summary>
public sealed class Zone6State
{
    /// <summary>Debug knob: force every run's zone 6 to this variant (an id from
    /// <see cref="Zone6Table.Variants"/>), or null to let the seed decide. Also set at boot from
    /// the <c>FABLELAND_ZONE6</c> environment variable.</summary>
    public static string ForcedVariantId;

    /// <summary>The zone 6 a debug fight with no run is dealt (the debug page's gravity and
    /// seal knobs edit it when no run exists). Never saved.</summary>
    public static readonly Zone6State Debug = new() { VariantId = Zone6Table.VoidFrontierId };

    public string VariantId = "";
    /// <summary>The Void Frontier's gravity factor; 1 on arrival and always in Avīci.</summary>
    public float Gravity = Zone6Table.GravityOnArrival;
    /// <summary>Avīci: seals on you — poison id → stacks (1..cap). Never cleared.</summary>
    public readonly Dictionary<string, int> PlayerSeals = new(StringComparer.Ordinal);
    /// <summary>Avīci: the poisons whose ability is sealed in Māra.</summary>
    public readonly HashSet<string> MaraSealed = new(StringComparer.Ordinal);
    public readonly Dictionary<string, Zone6NodeRecord> Nodes = new(StringComparer.Ordinal);

    public Zone6VariantDef Variant => Zone6Table.Variant(VariantId);
    public bool IsVoidFrontier => string.Equals(VariantId, Zone6Table.VoidFrontierId, StringComparison.Ordinal);
    public bool IsAvici => string.Equals(VariantId, Zone6Table.AviciId, StringComparison.Ordinal);

    /// <summary>The variant a run of this seed gets: the debug override if one is set, else the
    /// seed's own draw (<see cref="Zone6Table.VariantForSeed"/>).</summary>
    public static string VariantFor(string seed) =>
        Zone6Table.Variant(ForcedVariantId) != null ? Zone6Table.Variant(ForcedVariantId).Id : Zone6Table.VariantForSeed(seed);

    public static Zone6State ForSeed(string seed) => new() { VariantId = VariantFor(seed) };

    /// <summary>The <c>BossTable</c> id of the mini-boss at an LV5 node, or "" for any other node.</summary>
    public string MiniBossAt(string nodeId)
    {
        int slot = Zone6Table.MiniBossSlot(nodeId);
        Zone6VariantDef variant = Variant;
        return slot >= 0 && variant != null ? variant.MiniBossIds[slot] : "";
    }

    public int SealStacks(string poisonId) =>
        poisonId != null && PlayerSeals.TryGetValue(poisonId, out int stacks) ? stacks : 0;

    public int SealCount
    {
        get { int n = 0; foreach (int stacks in PlayerSeals.Values) n += stacks; return n; }
    }

    private Zone6NodeRecord Record(string nodeId)
    {
        if (!Nodes.TryGetValue(nodeId ?? "", out Zone6NodeRecord record))
        {
            record = new Zone6NodeRecord();
            Nodes[nodeId ?? ""] = record;
        }
        return record;
    }

    /// <summary>A zone-6 mini-boss fell. Applies the variant's reward and returns a line that
    /// says what changed ("" when nothing did).</summary>
    public string RecordWin(string nodeId)
    {
        Record(nodeId).Wins++;
        if (IsVoidFrontier)
        {
            float before = Gravity;
            Gravity = Zone6Table.StepGravity(Gravity, won: true);
            return $"The living world weighs less: gravity ×{Gravity:0.00} (was ×{before:0.00}).";
        }
        if (IsAvici)
        {
            Zone6PoisonDef poison = Zone6Table.PoisonOfBoss(MiniBossAt(nodeId));
            if (poison == null) return "";
            bool sealedNow = MaraSealed.Add(poison.Id);
            return sealedNow
                ? $"{poison.Glyph} {poison.Name} is sealed in Māra: he loses {poison.MaraAbility}."
                : $"{poison.Glyph} {poison.Name} was already sealed in Māra.";
        }
        return "";
    }

    /// <summary>A zone-6 mini-boss beat you (every fighter down, or its timer ran out). Applies
    /// the variant's penalty and returns a line that says what changed.</summary>
    public string RecordLoss(string nodeId)
    {
        Record(nodeId).Losses++;
        if (IsVoidFrontier)
        {
            float before = Gravity;
            Gravity = Zone6Table.StepGravity(Gravity, won: false);
            return $"You fall back heavier with life: gravity ×{Gravity:0.00} (was ×{before:0.00}).";
        }
        if (IsAvici)
        {
            Zone6PoisonDef poison = Zone6Table.PoisonOfBoss(MiniBossAt(nodeId));
            if (poison == null) return "";
            int before = SealStacks(poison.Id);
            int after = Math.Min(Zone6Table.SealStackCap, before + 1);
            PlayerSeals[poison.Id] = after;
            return after > before
                ? $"{poison.Glyph} {poison.Name} enters you — sealed with {poison.SealName} ×{after}: {poison.SealSummary} per stack."
                : $"{poison.Glyph} {poison.Name} cannot seal you deeper ({poison.SealName} ×{after}).";
        }
        return "";
    }

    /// <summary>One line for the map and the HUD: where you are and what weighs on you.</summary>
    public string StatusLine()
    {
        Zone6VariantDef variant = Variant;
        if (variant == null) return "";
        if (IsVoidFrontier) return $"{variant.Name} ({variant.NameZh}) · gravity ×{Gravity:0.00}";
        var parts = new List<string>();
        foreach (Zone6PoisonDef poison in Zone6Table.Poisons)
        {
            int stacks = SealStacks(poison.Id);
            if (stacks > 0) parts.Add($"{poison.SealName} ×{stacks}");
        }
        string inYou = parts.Count > 0 ? "sealed in you: " + string.Join(", ", parts) : "no seal on you";
        return $"{variant.Name} ({variant.NameZh}) · {inYou} · Māra sealed {MaraSealed.Count}/5";
    }

    // ── Debug knobs ──────────────────────────────────────────────────────────────────
    public void DebugSetGravity(float gravity) =>
        Gravity = Math.Clamp(gravity, Zone6Table.GravityMin, Zone6Table.GravityMax);

    public void DebugAddSeal(string poisonId, int delta)
    {
        if (Zone6Table.Poison(poisonId) == null) return;
        int stacks = Math.Clamp(SealStacks(poisonId) + delta, 0, Zone6Table.SealStackCap);
        if (stacks == 0) PlayerSeals.Remove(poisonId);
        else PlayerSeals[poisonId] = stacks;
    }

    // ── Save ─────────────────────────────────────────────────────────────────────────
    public Zone6SaveData ToSave()
    {
        var save = new Zone6SaveData { VariantId = VariantId ?? "", Gravity = Gravity };
        var poisons = new List<string>(PlayerSeals.Keys);
        poisons.Sort(StringComparer.Ordinal);
        foreach (string id in poisons) save.PlayerSeals[id] = PlayerSeals[id];
        save.MaraSealed = new List<string>(MaraSealed);
        save.MaraSealed.Sort(StringComparer.Ordinal);
        var nodes = new List<string>(Nodes.Keys);
        nodes.Sort(StringComparer.Ordinal);
        foreach (string id in nodes)
            save.Nodes.Add(new Zone6NodeSaveData { NodeId = id, Wins = Nodes[id].Wins, Losses = Nodes[id].Losses });
        return save;
    }

    /// <summary>A run's zone-6 state from its save; a save without one (before v0.12.0) starts
    /// empty, its variant the one its seed draws.</summary>
    public static Zone6State FromSave(Zone6SaveData save, string seed)
    {
        var state = new Zone6State
        {
            VariantId = Zone6Table.Variant(save?.VariantId) != null ? Zone6Table.Variant(save.VariantId).Id : VariantFor(seed),
        };
        if (save == null) return state;
        state.Gravity = Math.Clamp(save.Gravity > 0f ? save.Gravity : Zone6Table.GravityOnArrival, Zone6Table.GravityMin, Zone6Table.GravityMax);
        if (save.PlayerSeals != null)
            foreach (KeyValuePair<string, int> seal in save.PlayerSeals)
                if (Zone6Table.Poison(seal.Key) != null && seal.Value > 0)
                    state.PlayerSeals[seal.Key] = Math.Min(Zone6Table.SealStackCap, seal.Value);
        if (save.MaraSealed != null)
            foreach (string id in save.MaraSealed)
                if (Zone6Table.Poison(id) != null) state.MaraSealed.Add(id);
        if (save.Nodes != null)
            foreach (Zone6NodeSaveData node in save.Nodes)
                if (!string.IsNullOrWhiteSpace(node?.NodeId))
                    state.Nodes[node.NodeId] = new Zone6NodeRecord { Wins = Math.Max(0, node.Wins), Losses = Math.Max(0, node.Losses) };
        return state;
    }

    /// <summary>Pure checks of the ladder and the seals (run by the zone-6 check).</summary>
    public static List<string> SelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }

        var vf = new Zone6State { VariantId = Zone6Table.VoidFrontierId };
        for (int i = 1; i <= 5; i++) vf.RecordWin($"XX-5-{i}");
        Check(Math.Abs(vf.Gravity - 0.32768f) < 1e-4f, $"five clean wins leave gravity ×0.328, not ×{vf.Gravity:0.0000}");
        vf.RecordLoss("XX-5-1");
        Check(Math.Abs(vf.Gravity - 0.360448f) < 1e-4f, $"a loss after five wins makes gravity ×0.360, not ×{vf.Gravity:0.0000}");
        for (int i = 0; i < 20; i++) vf.RecordWin("XX-5-2");
        Check(Math.Abs(vf.Gravity - Zone6Table.GravityMin) < 1e-6f, $"gravity must stop at ×{Zone6Table.GravityMin}, not ×{vf.Gravity}");
        for (int i = 0; i < 40; i++) vf.RecordLoss("XX-5-3");
        Check(Math.Abs(vf.Gravity - Zone6Table.GravityMax) < 1e-6f, $"gravity must stop at ×{Zone6Table.GravityMax}, not ×{vf.Gravity}");
        Check(vf.Nodes["XX-5-1"].Wins == 1 && vf.Nodes["XX-5-1"].Losses == 1 && vf.Nodes["XX-5-3"].Losses == 40,
            "the per-node record miscounts wins and losses");
        Check(vf.PlayerSeals.Count == 0 && vf.MaraSealed.Count == 0, "the Void Frontier must never seal anything");

        var av = new Zone6State { VariantId = Zone6Table.AviciId };
        Check(av.MiniBossAt("XX-5-1") == Zone6Table.Greed.MiniBossId && av.MiniBossAt("XX-5-5") == Zone6Table.Doubt.MiniBossId
              && av.MiniBossAt("XX-6-1") == "" && av.MiniBossAt("SL-1-A") == "", "Avīci's gates are not Greed … Doubt on XX-5-1 … XX-5-5");
        for (int i = 0; i < 5; i++) av.RecordLoss("XX-5-4");
        Check(av.SealStacks("pride") == Zone6Table.SealStackCap, $"Pride must stack to {Zone6Table.SealStackCap}, not {av.SealStacks("pride")}");
        av.RecordWin("XX-5-1");
        av.RecordWin("XX-5-1");
        Check(av.MaraSealed.Count == 1 && av.MaraSealed.Contains("greed"), "beating Greed twice seals one ability in Māra");
        Check(Math.Abs(av.Gravity - 1f) < 1e-6f, "Avīci must never change gravity");

        Zone6State round = FromSave(av.ToSave(), "ANY");
        Check(round.VariantId == av.VariantId && round.SealStacks("pride") == 3 && round.MaraSealed.Contains("greed")
              && round.Nodes["XX-5-4"].Losses == 5, "the zone-6 state does not survive a save");
        Zone6State none = FromSave(null, "ANY");
        Check(none.VariantId == VariantFor("ANY") && none.PlayerSeals.Count == 0 && Math.Abs(none.Gravity - 1f) < 1e-6f,
            "a save without zone-6 state does not start empty on its seed's variant");

        string forcedBefore = ForcedVariantId;
        ForcedVariantId = Zone6Table.AviciId;
        Check(VariantFor("ANYSEED") == Zone6Table.AviciId, "the debug override does not force the variant");
        ForcedVariantId = forcedBefore;
        return failures;
    }
}

/// <summary>The zone-6 part of a save (<c>RunSaveData.Zone6</c>). Null in a save from before
/// v0.12.0, which loads as an empty zone 6 (no save-version bump: the field is optional).</summary>
public sealed class Zone6SaveData
{
    public string VariantId { get; set; } = "";
    public float Gravity { get; set; } = 1f;
    public Dictionary<string, int> PlayerSeals { get; set; } = new();
    public List<string> MaraSealed { get; set; } = new();
    public List<Zone6NodeSaveData> Nodes { get; set; } = new();
}

public sealed class Zone6NodeSaveData
{
    public string NodeId { get; set; } = "";
    public int Wins { get; set; }
    public int Losses { get; set; }
}

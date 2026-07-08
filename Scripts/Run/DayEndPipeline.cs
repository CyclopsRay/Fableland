using System.Collections.Generic;
using Godot;
using Fableland.Map;

namespace Fableland.Run;

/// <summary>One ordered step of day-end resolution (NODES §7.4 / T30 §5).</summary>
public interface IDayStep
{
    string Name { get; }
    void Run(RunState rs);
}

/// <summary>
/// The day-end resolution pipeline (T30 §5). Steps run in a fixed, printable order; NO other
/// code path may perform any of these effects. Future systems (plantation growth, phantom
/// movement, …) register as new <see cref="IDayStep"/>s — the order stays testable headlessly.
/// </summary>
public sealed class DayEndPipeline
{
    private readonly List<IDayStep> _steps = new()
    {
        new AdvanceDayStep(),   // 1. Day++ (hidden day still ticks in the VOID)
        new VoidDevourStep(),   // 2. ring devour + orphaned function nodes + death check
        new ItemCdsStep(),      // 3. day-based item cooldowns (stub — items land v0.6.0)
        new PerishStep(),       // 4. perishable items decrement/expire (stub)
        new NpcWindowsStep(),   // 5. traders/wanderers leave after 5 days (stub)
        new StaminaStep(),      // 6. Stamina = 5
    };

    public void Run(RunState rs)
    {
        foreach (var s in _steps)
        {
            // Once the run has ended mid-pipeline (VOID ate the player's node), stop touching
            // state — the scene is already swapping to RunOver.
            if (rs.RunFinished) break;
            s.Run(rs);
        }
    }

    /// <summary>Debug-printable execution order.</summary>
    public string Order()
    {
        var names = new List<string>();
        foreach (var s in _steps) names.Add(s.Name);
        return string.Join(" → ", names);
    }
}

// ---- steps ------------------------------------------------------------------

/// <summary>Day++. The hidden day advances even inside the VOID (only the DISPLAY is "???").</summary>
public sealed class AdvanceDayStep : IDayStep
{
    public string Name => "AdvanceDay";
    public void Run(RunState rs) => rs.Day++;
}

/// <summary>
/// VOID devour (MapGDD §8). Preserves the exact semantics MapController.EndDay used to have:
/// nodes are eaten on the day that just ENDED (== rs.Day - 1, since AdvanceDayStep already
/// incremented). Orphaned function nodes (all neighbours eaten) go too. If the player's current
/// node is now devoured, the run is over (NODES §2.2).
/// </summary>
public sealed class VoidDevourStep : IDayStep
{
    public string Name => "VoidDevour";
    public void Run(RunState rs)
    {
        if (rs.Graph == null) return;
        int endedDay = rs.Day - 1; // AdvanceDayStep ran first; compare against the day just ended

        int devouredCount = 0;
        string devouredTag = null;
        foreach (var n in rs.Graph.Nodes)
        {
            if (n.Devoured) continue;
            if (VoidSchedule.DevourDay.TryGetValue(n.LevelTag, out int d) && d == endedDay)
            {
                n.Devoured = true;
                devouredCount++;
                devouredTag = n.LevelTag;
            }
        }

        // Function nodes sit on edges between combat nodes; once every neighbour is eaten the
        // function node is orphaned, so the VOID takes it too (no floating shelters).
        foreach (var n in rs.Graph.Nodes)
        {
            if (n.Devoured || n.WorldIndex != -2) continue; // -2 == function node
            bool anyAlive = false;
            foreach (var e in rs.Graph.EdgesOf(n))
                if (!e.Other(n).Devoured) { anyAlive = true; break; }
            if (!anyAlive) { n.Devoured = true; devouredCount++; }
        }

        // Describe what happened for the day-end summary toast (T30 §5 residual) — this step only
        // appends a note; RunState.EndDay owns rendering it.
        if (devouredTag != null)
            rs.DayEndNotes.Add($"The VOID devoured {devouredTag} ({devouredCount} node{(devouredCount == 1 ? "" : "s")}).");

        // Standing on ground the VOID just ate = death (the third death source, NODES §2.2).
        var cur = rs.FindNode(rs.CurrentNodeId);
        if (cur != null && cur.Devoured)
            rs.EndRun(RunEndKind.VoidDevoured);
    }
}

/// <summary>Day-based item cooldowns decrement (held items only). Stub — items land v0.6.0 (T30 §4).</summary>
public sealed class ItemCdsStep : IDayStep
{
    public string Name => "ItemCds";
    public void Run(RunState rs) { /* TODO(v0.6.0): tick held items' DayCdRemaining. */ }
}

/// <summary>Perishable items decrement (held AND backpack); expiry may convert. Stub — v0.6.0.</summary>
public sealed class PerishStep : IDayStep
{
    public string Name => "Perish";
    public void Run(RunState rs) { /* TODO(v0.6.0): tick PerishRemaining, expire/convert at 0. */ }
}

/// <summary>Traders/wanderers whose 5-day window elapsed leave. Stub — NPCs land in a later phase.</summary>
public sealed class NpcWindowsStep : IDayStep
{
    public string Name => "NpcWindows";
    public void Run(RunState rs) { /* TODO: tick shelter NPC windows, depart at 5 days. */ }
}

/// <summary>Stamina refreshes to full (NODES §7.4 step 6).</summary>
public sealed class StaminaStep : IDayStep
{
    public string Name => "Stamina";
    public void Run(RunState rs)
    {
        rs.Stamina = RunState.MaxStamina;
        rs.DayEndNotes.Add("Stamina restored.");
    }
}

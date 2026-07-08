namespace Fableland.Missions;

/// <summary>
/// Pure per-node-level mission scaling data (NODES.gdd §4.2 / §4.4 / §4.5). No Godot types.
/// Levels 1/2/3/5 are the non-boss combat levels; LV4/LV6 are structural BOSS nodes and only
/// expose a time limit here. All authored values come straight from the NODES table — the
/// "multiplier" column is a design guide, not applied at runtime (NODES §4.2).
/// </summary>
public static class MissionTable
{
    /// <summary>Node difficulty display name per map level 1–6 (NODES §4.2).</summary>
    public static string DifficultyName(int nodeLevel) => nodeLevel switch
    {
        1 => "Trip",
        2 => "Adventure",
        3 => "March",
        4 => "Confrontation",
        5 => "Reincarnation",
        6 => "Rout",
        _ => "Trip",
    };

    /// <summary>Collection: wonder cores to collect (NODES §4.2).</summary>
    public static int CollectionCores(int nodeLevel) => nodeLevel switch
    {
        1 => 10, 2 => 15, 3 => 20, 5 => 25, _ => 10,
    };

    /// <summary>Protect: survive duration in seconds (NODES §4.2). Also the UNIVERSAL time
    /// limit shared by every non-boss mission at this level (NODES §4.2).</summary>
    public static float ProtectDuration(int nodeLevel) => nodeLevel switch
    {
        1 => 40f, 2 => 60f, 3 => 80f, 5 => 100f, _ => 40f,
    };

    /// <summary>The universal (non-boss) mission time limit = this level's Protect duration.</summary>
    public static float TimeLimit(int nodeLevel) => ProtectDuration(nodeLevel);

    /// <summary>Destroy: number of objectives (NODES §4.2).</summary>
    public static int DestroyObjectives(int nodeLevel) => nodeLevel switch
    {
        1 => 2, 2 => 3, 3 => 4, 5 => 5, _ => 2,
    };

    /// <summary>Destroy: per-objective HP = 60 × this multiplier (report Q8: 1 / 1.5 / 2 / 2.5).</summary>
    public static float DestroyObjectiveHpMult(int nodeLevel) => nodeLevel switch
    {
        1 => 1f, 2 => 1.5f, 3 => 2f, 5 => 2.5f, _ => 1f,
    };

    /// <summary>Destroy: absolute HP of one objective.</summary>
    public static float DestroyObjectiveHp(int nodeLevel) => 60f * DestroyObjectiveHpMult(nodeLevel);

    /// <summary>Slaughter: number of waves (NODES §4.2 / §4.3).</summary>
    public static int SlaughterWaves(int nodeLevel) => nodeLevel switch
    {
        1 => 2, 2 => 3, 3 => 4, 5 => 5, _ => 2,
    };

    /// <summary>Protect: Condensed Wonder Core HP pool (NODES §4.4).</summary>
    public static float ProtectCoreHp(int nodeLevel) => nodeLevel switch
    {
        1 => 150f, 2 => 200f, 3 => 250f, 5 => 300f, _ => 150f,
    };

    /// <summary>Boss fight time limit in seconds: LV4 = 240 s, LV6 = 360 s (NODES §4.5).</summary>
    public static float BossTimeLimit(int nodeLevel) => nodeLevel >= 6 ? 360f : 240f;
}

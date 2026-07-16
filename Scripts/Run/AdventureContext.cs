using Fableland.Map;

namespace Fableland.Run;

/// <summary>
/// Immutable snapshot of the node the player just entered — the ONLY thing an Adventure
/// scene reads to configure itself (T10 §3 handshake). Null ⇒ the scene was launched
/// directly (F5) and must fall back to debug defaults.
/// </summary>
public sealed class AdventureContext
{
    public string NodeId;
    public int NodeLevel;          // 1..6 (map difficulty axis)
    public MissionType Mission;    // rolled at mapgen (combat nodes); Boss for LV4/LV6
    public NodeKind Kind;          // Combat/Boss/Shelter/QuestionMark
    public int Day;                // day the adventure started (foe difficulty axis)
    public bool IsRevisitCombat;   // re-attempt of a previously-failed combat node (NODES §1.3)
    public string Terrain;         // combat-map selection terrain from the entered node's altitude label
    public string CombatMapPath;   // absolute selected authored map path; empty => legacy Arena fallback
    /// <summary>Frozen party order captured exactly once at combat entry. The Arena
    /// owns its live switch cursor; shelter edits affect the next battle only.</summary>
    public BattleTeamSnapshot BattleTeam;
}
